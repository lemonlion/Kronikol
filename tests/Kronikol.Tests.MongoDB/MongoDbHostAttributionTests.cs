using System.Text.RegularExpressions;
using Kronikol.Constants;
using Kronikol.Extensions.MongoDB;
using Kronikol.Tracking;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Kronikol.Tests.MongoDB;

/// <summary>
/// #136: the commands a host runs while it serves a request land in the scenario the request's headers name. The host
/// wires tracking as the wiki's MongoDB page shows: through <c>WithTestTracking</c> with the host's accessor in the
/// options (Option C), or with the subscriber <c>AddMongoDbTestTracking</c> registers, subscribed by the client (Option D).
/// It registers no propagation middleware, and its fetcher answers a new id outside a test, as Kronikol.xUnit2's did
/// (#133). Measured on 4.9.0 without the accessor, every command went to a test no scenario has; Option D as 4.9.0's
/// page wrote it, without the client's line, recorded none.
/// </summary>
[Collection("TestCorrelationStore")]
public class MongoDbHostAttributionTests(MongoServerFixture mongo) : IClassFixture<MongoServerFixture>
{
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    // The two setups the wiki's MongoDB page shows for a host, each as the page writes it.
    public static TheoryData<string> Wirings => [OptionC, OptionD];
    private const string OptionC = "WithTestTracking with the options' accessor (Option C)";
    private const string OptionD = "AddMongoDbTestTracking, subscribed by the client (Option D)";

    [Theory]
    [MemberData(nameof(Wirings))]
    public async Task A_hosts_commands_land_in_the_scenario_its_request_names(string wiring)
    {
        var connectionString = mongo.Require();
        var serviceName = "MongoDB " + _run;
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddHttpContextAccessor();
                    if (wiring == OptionC)
                    {
                        services.AddSingleton<IMongoClient>(sp => new MongoClient(MongoClientSettings
                            .FromConnectionString(connectionString)
                            .WithTestTracking(new MongoDbTrackingOptions
                            {
                                ServiceName = serviceName,
                                CurrentTestInfoFetcher = NotAScenario,
                                HttpContextAccessor = sp.GetService<IHttpContextAccessor>(),
                            })));
                        return;
                    }
                    services.AddMongoDbTestTracking(options =>
                    {
                        options.ServiceName = serviceName;
                        options.CurrentTestInfoFetcher = NotAScenario;
                    });
                    services.AddSingleton<IMongoClient>(sp =>
                    {
                        var subscriber = sp.GetRequiredService<MongoDbTrackingSubscriber>();
                        var settings = MongoClientSettings.FromConnectionString(connectionString);
                        var existing = settings.ClusterConfigurator;
                        settings.ClusterConfigurator = cb => { existing?.Invoke(cb); subscriber.Subscribe(cb); };
                        return new MongoClient(settings);
                    });
                })
                .Configure(app => app.Run(async context =>
                {
                    var scenario = context.Request.Query["s"].ToString();
                    var orders = context.RequestServices.GetRequiredService<IMongoClient>()
                        .GetDatabase("kronikol136").GetCollection<BsonDocument>($"orders_{scenario}_{_run}");
                    await orders.InsertOneAsync(new BsonDocument("scenario", scenario), cancellationToken: context.RequestAborted);
                    await orders.Find(new BsonDocument("scenario", scenario)).FirstOrDefaultAsync(context.RequestAborted);
                    await context.Response.WriteAsync("ok", context.RequestAborted);
                })))
            .StartAsync(TestContext.Current.CancellationToken);
        var client = host.GetTestClient();

        // Two scenarios in turn, then two at once: an insert and a find each, so eight commands.
        await Send(client, "A");
        await Send(client, "B");
        await Task.WhenAll(Send(client, "C"), Send(client, "D"));

        var commands = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.ServiceName == serviceName && l.Type == RequestResponseType.Request).ToArray();
        var right = commands.Count(l => l.TestId == ScenarioId(ScenarioOf(l.Uri)));
        var sources = string.Join(", ", commands.GroupBy(l => l.AttributionSource).Select(g => $"{g.Key} {g.Count()}"));
        Assert.Equal("right 8, wrong 0, lost 0; RequestHeader 8",
            $"right {right}, wrong {commands.Length - right}, lost {8 - commands.Length}; {sources}");
    }

    private async Task Send(HttpClient client, string scenario)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/?s=" + scenario);
        request.Headers.Add(TestTrackingHttpHeaders.CurrentTestNameHeader, "Scenario " + scenario);
        request.Headers.Add(TestTrackingHttpHeaders.CurrentTestIdHeader, ScenarioId(scenario));
        (await client.SendAsync(request, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
    }

    private string ScenarioId(string scenario) => $"scenario-{scenario}-{_run}";

    // A new id on every call made outside a test, as Kronikol.xUnit2's fetcher answered (#133).
    private static (string Name, string Id) NotAScenario() => ("Not a scenario", Guid.NewGuid().ToString());

    // Each scenario writes to a collection named for it, and a command's address names its collection.
    private static string ScenarioOf(Uri uri) => Regex.Match(uri.ToString(), "orders_([A-D])_").Groups[1].Value;
}
