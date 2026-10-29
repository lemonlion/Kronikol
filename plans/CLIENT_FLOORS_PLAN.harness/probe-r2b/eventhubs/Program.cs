// R2b's fourth test for Kronikol.Extensions.EventHubs (plans/CLIENT_FLOORS_PLAN.md 5.1, 5.2): the real
// Azure.Messaging.EventHubs clients wrapped by TrackingEventHubProducerClient and TrackingEventHubConsumerClient against
// the Event Hubs emulator, on the floor the extension asks for (no property) or on the newest 5.x (-p:Newest=true). The
// wrappers are held as the clients they derive from, as DI hands them out, and the members they leave to the base
// (probe-r2b/wrappers.cs) and the properties they hide with `new` are called too. Prints each outcome and the record.
// Usage: dotnet run -- <connection string, default the emulator on localhost:35672>
using System.Reflection;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Kronikol.Extensions.EventHubs;
using Kronikol.Tracking;

var connection = args.Length > 0 ? args[0]
    : "Endpoint=sb://localhost:35672;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
var version = typeof(EventHubProducerClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
Console.WriteLine($"# Azure.Messaging.EventHubs {version.Split('+')[0]} | emulator");
var options = new EventHubsTrackingOptions { CurrentTestInfoFetcher = () => ("probe scenario", "probe-1") };

var innerProducer = new EventHubProducerClient(connection, "eh1");
EventHubProducerClient producer = new TrackingEventHubProducerClient(innerProducer, options);
await Step("the producer's names, held as EventHubProducerClient", () => Task.FromResult($"{producer.FullyQualifiedNamespace} {producer.EventHubName}"));
await Step("the partitions", async () => string.Join(",", await producer.GetPartitionIdsAsync()));
await Step("send two events", async () =>
{
    await producer.SendAsync([new EventData("one"), new EventData("two")]);
    return "sent";
});
await Step("send a batch", async () =>
{
    using var batch = await producer.CreateBatchAsync();
    batch.TryAdd(new EventData("three"));
    await producer.SendAsync(batch);
    return "sent";
});

var innerConsumer = new EventHubConsumerClient("cg1", connection, "eh1");
EventHubConsumerClient consumer = new TrackingEventHubConsumerClient(innerConsumer, options);
await Step("the consumer's names, held as EventHubConsumerClient", () => Task.FromResult($"{consumer.ConsumerGroup} {consumer.EventHubName}"));
await Step("read from the start", async () => await FirstBody(consumer.ReadEventsAsync(true, new ReadEventOptions(), Cancel())));
await Step("read with options", async () => await FirstBody(consumer.ReadEventsAsync(new ReadEventOptions(), Cancel())));
await Step("read a partition", async () => await FirstBody(consumer.ReadEventsFromPartitionAsync("0", EventPosition.Earliest, Cancel())));
await Step("dispose both (left to the base)", async () =>
{
    await producer.DisposeAsync();
    await consumer.DisposeAsync();
    return $"producer closed {producer.IsClosed} (inner {innerProducer.IsClosed}), consumer closed {consumer.IsClosed} (inner {innerConsumer.IsClosed})";
});

Console.WriteLine("## What Kronikol recorded");
foreach (var log in RequestResponseLogger.RequestAndResponseLogs)
    Console.WriteLine($"  {log.Type} {log.Method.Value} {log.Uri} ({log.ServiceName})");

static CancellationToken Cancel() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

static async Task<string> FirstBody(IAsyncEnumerable<PartitionEvent> events)
{
    await foreach (var e in events)
        return e.Data.EventBody.ToString();
    return "nothing";
}

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
