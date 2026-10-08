// #136 probe: which scenario a host's MongoDB commands land in, for each way a host can wire tracking today.
// One in-process host per configuration (TestServer on a legacy WebHostBuilder, as WebApplicationFactory runs the
// hosts #136 was found on), a real MongoDB server and the published Kronikol.Extensions.MongoDB.V2 package. Each
// scenario sends the two test-tracking headers TestTrackingMessageHandler sends; the host inserts and finds in a
// collection named for the scenario, so each tracked command says which scenario it should have landed in.
//   dotnet run -c Release                          the shipped package (4.9.0 by default)
//   dotnet run -c Release -p:AccessorOption=true -p:KronikolVersion=<local build>   adds the plan's wiring
using Kronikol.Constants;
using Kronikol.Extensions.MongoDB;
using Kronikol.Tracking;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

var connectionString = Environment.GetEnvironmentVariable("PROBE136_MONGO") ?? "mongodb://127.0.0.1:27017";

var wirings = new List<(string Name, Action<IServiceCollection, MongoDbTrackingOptions> Register)>
{
    ("A WithTestTracking (wiki Options A and C)", (services, options) =>
        services.AddSingleton<IMongoClient>(_ => new MongoClient(
            MongoClientSettings.FromConnectionString(connectionString).WithTestTracking(options)))),
    ("D AddMongoDbTestTracking (wiki Option D)", (services, options) =>
    {
        services.AddMongoDbTestTracking(o => o.CurrentTestInfoFetcher = options.CurrentTestInfoFetcher);
        services.AddSingleton<IMongoClient>(_ => new MongoClient(MongoClientSettings.FromConnectionString(connectionString)));
    }),
    ("W subscriber + accessor (the #136 workaround)", (services, options) =>
        services.AddSingleton<IMongoClient>(sp =>
        {
            var settings = MongoClientSettings.FromConnectionString(connectionString);
            var subscriber = new MongoDbTrackingSubscriber(options, sp.GetService<IHttpContextAccessor>());
            var configureCluster = settings.ClusterConfigurator;
            settings.ClusterConfigurator = builder => { configureCluster?.Invoke(builder); subscriber.Subscribe(builder); };
            return new MongoClient(settings);
        })),
    // The accessor reads a static AsyncLocal that ASP.NET Core fills only in a host whose container holds an
    // IHttpContextAccessor: this host registers none (see StartHost), so even an accessor sees no request.
    ("X subscriber + new HttpContextAccessor(), host has none", (services, options) =>
        services.AddSingleton<IMongoClient>(_ =>
        {
            var settings = MongoClientSettings.FromConnectionString(connectionString);
            var subscriber = new MongoDbTrackingSubscriber(options, new HttpContextAccessor());
            settings.ClusterConfigurator = builder => subscriber.Subscribe(builder);
            return new MongoClient(settings);
        })),
#if ACCESSOR_OPTION
    ("N WithTestTracking, options.HttpContextAccessor", (services, options) =>
        services.AddSingleton<IMongoClient>(sp => new MongoClient(
            MongoClientSettings.FromConnectionString(connectionString)
                .WithTestTracking(options with { HttpContextAccessor = sp.GetService<IHttpContextAccessor>() })))),
#endif
};

var fetchers = new (string Name, Func<(string Name, string Id)>? Fetch)[]
{
    ("no fetcher", null),
    ("fetcher: 'unknown' outside a test", () => (TestIdentityScope.UnknownTestName, TestIdentityScope.UnknownTestId)),
    ("fetcher: a new id outside a test (#133)", () => ("some test", Guid.NewGuid().ToString())),
};

Console.WriteLine($"{typeof(MongoDbTrackingSubscriber).Assembly.GetName().Name} {Version(typeof(MongoDbTrackingSubscriber))}, " +
                  $"MongoDB.Driver {Version(typeof(MongoClient))}, {Environment.Version}, {connectionString}");
Console.WriteLine("Each row: 8 commands, an insert and a find for each of scenarios A and B in turn, then C and D at once.");
Console.WriteLine("right: the scenario whose request ran it. wrong: any other id. lost: not recorded.");
Console.WriteLine();

foreach (var wiring in wirings)
foreach (var propagation in new[] { false, true })
foreach (var fetcher in fetchers)
{
    RequestResponseLogger.Clear();
    TrackingComponentRegistry.Clear();
    var options = new MongoDbTrackingOptions { CurrentTestInfoFetcher = fetcher.Fetch };
    using var server = StartHost(services => wiring.Register(services, options), propagation,
        registerAccessor: !wiring.Name.StartsWith("X "));
    var client = server.CreateClient();
    await Send(client, "A");
    await Send(client, "B");
    await Task.WhenAll(Send(client, "C"), Send(client, "D"));

    var requests = RequestResponseLogger.RequestAndResponseLogs
        .Where(l => l.DependencyCategory == DependencyCategories.MongoDB && l.Type == RequestResponseType.Request)
        .ToArray();
    var right = requests.Count(l => l.TestId == ScenarioId(ScenarioOf(l.Uri)));
    var sources = string.Join(" ", requests.GroupBy(l => l.AttributionSource).OrderBy(g => g.Key)
        .Select(g => $"{g.Key}:{g.Count()}"));
    var accessor = TrackingComponentRegistry.GetRegisteredComponents().OfType<MongoDbTrackingSubscriber>()
        .Select(s => s.HasHttpContextAccessor ? "yes" : "no").FirstOrDefault() ?? "none built";
    Console.WriteLine($"{wiring.Name,-48} | {(propagation ? "propagation" : "no propagation"),-14} | {fetcher.Name,-40} | " +
                      $"right {right} wrong {requests.Length - right} lost {8 - requests.Length} | {(sources == "" ? "-" : sources)} | accessor: {accessor}");
}

static TestServer StartHost(Action<IServiceCollection> configureServices, bool propagation, bool registerAccessor) =>
    new(new WebHostBuilder()
        .ConfigureServices(services =>
        {
            if (registerAccessor) services.AddHttpContextAccessor(); // TrackDependenciesForDiagrams registers one too
            if (propagation) services.AddTestTrackingContextPropagation();
            configureServices(services);
        })
        .Configure(app => app.Run(async context =>
        {
            var scenario = context.Request.Query["s"].ToString();
            var orders = context.RequestServices.GetRequiredService<IMongoClient>()
                .GetDatabase("probe136").GetCollection<BsonDocument>("orders_" + scenario);
            await orders.InsertOneAsync(new BsonDocument("scenario", scenario));
            await orders.Find(new BsonDocument("scenario", scenario)).FirstOrDefaultAsync();
            await context.Response.WriteAsync("ok");
        })));

static async Task Send(HttpClient client, string scenario)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, "/?s=" + scenario);
    request.Headers.Add(TestTrackingHttpHeaders.CurrentTestNameHeader, "Scenario " + scenario);
    request.Headers.Add(TestTrackingHttpHeaders.CurrentTestIdHeader, ScenarioId(scenario));
    (await client.SendAsync(request)).EnsureSuccessStatusCode();
}

static string ScenarioId(string scenario) => "scenario-" + scenario;

static string ScenarioOf(Uri? uri)
{
    var text = uri?.ToString() ?? "";
    var at = text.IndexOf("orders_", StringComparison.Ordinal);
    return at < 0 ? "?" : text.Substring(at + "orders_".Length, 1);
}

static string Version(Type type) =>
    type.Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion
    ?? type.Assembly.GetName().Version!.ToString();
