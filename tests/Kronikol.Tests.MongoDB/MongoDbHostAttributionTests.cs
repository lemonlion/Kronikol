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
/// wires tracking as the wiki's WebApplicationFactory setup does, through <c>WithTestTracking</c>, and hands it the
/// host's accessor. It registers no propagation middleware, and its fetcher answers a new id outside a test, as
/// Kronikol.xUnit2's did (#133). Measured on 4.9.0 without the accessor, every command went to a test no scenario has.
/// </summary>
[Collection("TestCorrelationStore")]
public class MongoDbHostAttributionTests(MongoServerFixture mongo) : IClassFixture<MongoServerFixture>
{
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task A_hosts_commands_land_in_the_scenario_its_request_names()
    {
        var connectionString = mongo.Require();
        var serviceName = "MongoDB " + _run;
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddHttpContextAccessor();
                    services.AddSingleton<IMongoClient>(sp => new MongoClient(MongoClientSettings
                        .FromConnectionString(connectionString)
                        .WithTestTracking(new MongoDbTrackingOptions
                        {
                            ServiceName = serviceName,
                            CurrentTestInfoFetcher = () => ("Not a scenario", Guid.NewGuid().ToString()),
                            HttpContextAccessor = sp.GetService<IHttpContextAccessor>(),
                        })));
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

    // Each scenario writes to a collection named for it, and a command's address names its collection.
    private static string ScenarioOf(Uri uri) => Regex.Match(uri.ToString(), "orders_([A-D])_").Groups[1].Value;
}
