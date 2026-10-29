// R2b's fourth test for Kronikol.Extensions.CosmosDB (plans/CLIENT_FLOORS_PLAN.md 5.1, 5.2): the real
// Microsoft.Azure.Cosmos client, its options given WithTestTracking, against the vnext-preview emulator over HTTP, on
// the floor the extension asks for (no property) or on the newest 3.x (-p:Newest=true). Prints each call's outcome and
// what Kronikol recorded. With --plain the client has no tracking, to tell the client's own faults from the handler's.
// Usage: dotnet run -- [--plain] [<the emulator's endpoint, e.g. http://127.0.0.1:28081/>]
using System.Reflection;
using Kronikol.Extensions.CosmosDB;
using Kronikol.Tracking;
using Microsoft.Azure.Cosmos;

const string Key = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
var plain = args.Contains("--plain");
var endpoint = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "http://127.0.0.1:28081/";
var version = typeof(CosmosClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
Console.WriteLine($"# Microsoft.Azure.Cosmos {version.Split('+')[0]} | emulator at {endpoint}{(plain ? " | no tracking" : "")}");

var options = new CosmosClientOptions { LimitToEndpoint = true, ConnectionMode = ConnectionMode.Gateway };
if (!plain)
    options.WithTestTracking(new CosmosTrackingMessageHandlerOptions
    {
        CurrentTestInfoFetcher = () => ("probe scenario", "probe-1"),
    });
using var client = new CosmosClient(endpoint, Key, options);
var name = "probe-" + version.Split('+')[0].Replace('.', '-') + (plain ? "-plain" : "");
Database? database = null;
Container? container = null;

await Step("create a database", async () => { database = (await client.CreateDatabaseIfNotExistsAsync(name)).Database; return "created"; });
await Step("create a container", async () => { container = (await database!.CreateContainerIfNotExistsAsync("items", "/pk")).Container; return "created"; });
await Step("create an item", async () => (await container!.CreateItemAsync(new Item("1", "a", "hello"), new PartitionKey("a"))).StatusCode.ToString());
await Step("read it", async () => (await container!.ReadItemAsync<Item>("1", new PartitionKey("a"))).Resource.text);
await Step("query it", async () =>
{
    var texts = new List<string>();
    using var query = container!.GetItemQueryIterator<Item>("SELECT * FROM c WHERE c.pk = 'a'");
    while (query.HasMoreResults)
        texts.AddRange((await query.ReadNextAsync()).Select(i => i.text));
    return string.Join(",", texts);
});
await Step("replace it", async () => (await container!.ReplaceItemAsync(new Item("1", "a", "again"), "1", new PartitionKey("a"))).Resource.text);
await Step("delete it", async () => (await container!.DeleteItemAsync<Item>("1", new PartitionKey("a"))).StatusCode.ToString());
await Step("delete the database", async () => (await database!.DeleteAsync()).StatusCode.ToString());

Console.WriteLine("## What Kronikol recorded");
foreach (var log in RequestResponseLogger.RequestAndResponseLogs)
    Console.WriteLine($"  {log.Type} {log.Method.Value} {log.Uri.AbsolutePath} {log.StatusCode?.Value} ({log.ServiceName})");

static async Task Step<T>(string name, Func<Task<T>> call)
{
    try
    {
        var result = await call();
        Console.WriteLine($"{name}: ok{(result is string s ? " " + s : "")}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{name}: THROWS {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
    }
}

record Item(string id, string pk, string text);
