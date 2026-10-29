// R2b's fourth test for Kronikol.Extensions.StorageQueues (plans/CLIENT_FLOORS_PLAN.md 5.1, 5.2): the real
// Azure.Storage.Queues client, its options given WithTestTracking, against Azurite, on the floor the extension asks for
// (no property) or on the newest 12.x (-p:Newest=true). Prints each call's outcome and what Kronikol recorded.
// Usage: dotnet run -- <Azurite's queue endpoint, e.g. http://127.0.0.1:20001/devstoreaccount1>
using System.Reflection;
using Azure.Storage;
using Azure.Storage.Queues;
using Kronikol.Extensions.StorageQueues;
using Kronikol.Tracking;

const string AccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
var endpoint = args.Length > 0 ? args[0] : "http://127.0.0.1:20001/devstoreaccount1";
var version = typeof(QueueClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
Console.WriteLine($"# Azure.Storage.Queues {version.Split('+')[0]} | Azurite at {endpoint}");

var options = new QueueClientOptions().WithTestTracking(new StorageQueueTrackingMessageHandlerOptions
{
    CurrentTestInfoFetcher = () => ("probe scenario", "probe-1"),
});
var name = "probe-" + version.Split('+')[0].Replace('.', '-');
var queue = new QueueClient(new Uri($"{endpoint.TrimEnd('/')}/{name}"), new StorageSharedKeyCredential("devstoreaccount1", AccountKey), options);

await Step("create a queue", async () => { await queue.CreateIfNotExistsAsync(); return "created"; });
await Step("send a message", async () => { await queue.SendMessageAsync("hello"); return "sent"; });
await Step("peek it", async () => (await queue.PeekMessageAsync()).Value.MessageText);
await Step("receive and delete it", async () =>
{
    var message = (await queue.ReceiveMessageAsync()).Value;
    await queue.DeleteMessageAsync(message.MessageId, message.PopReceipt);
    return message.MessageText;
});
await Step("delete the queue", async () => { await queue.DeleteAsync(); return "deleted"; });

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
