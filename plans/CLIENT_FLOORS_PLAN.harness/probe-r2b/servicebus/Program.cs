// R2b's fourth test for Kronikol.Extensions.ServiceBus (plans/CLIENT_FLOORS_PLAN.md 5.1, 5.2): the real
// Azure.Messaging.ServiceBus client wrapped by TrackingServiceBusClient against the Service Bus emulator, on the floor
// the extension asks for (no property) or on the newest 7.x (-p:Newest=true). The wrapper is held as ServiceBusClient,
// as DI hands it out, and every member it leaves to its base (probe-r2b/wrappers.cs) is called as well as the ones it
// overrides: the base was built through the constructor for mocking, so a member that does not come back to an
// override runs against an empty client. Prints each call's outcome and what Kronikol recorded.
// Usage: dotnet run -- <connection string, default the emulator on localhost:25672>
using System.Reflection;
using Azure.Messaging.ServiceBus;
using Kronikol.Extensions.ServiceBus;
using Kronikol.Tracking;

var connection = args.Length > 0 ? args[0]
    : "Endpoint=sb://localhost:25672;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
var version = typeof(ServiceBusClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
Console.WriteLine($"# Azure.Messaging.ServiceBus {version.Split('+')[0]} | emulator");

var inner = new ServiceBusClient(connection);
ServiceBusClient client = new TrackingServiceBusClient(inner, new ServiceBusTrackingOptions
{
    CurrentTestInfoFetcher = () => ("probe scenario", "probe-1"),
});
const string Queue = "queue.1";

await Step("the client's namespace and identifier", () => Task.FromResult($"{client.FullyQualifiedNamespace} {client.Identifier}"));
var sender = client.CreateSender(Queue);
await Step("send through the sender", async () => { await sender.SendMessageAsync(new ServiceBusMessage("one")); return sender.EntityPath; });
await Step("send a batch", async () =>
{
    using var batch = await sender.CreateMessageBatchAsync();
    batch.TryAddMessage(new ServiceBusMessage("two"));
    batch.TryAddMessage(new ServiceBusMessage("three"));
    await sender.SendMessagesAsync(batch);
    return "sent 2";
});
var receiver = client.CreateReceiver(Queue);
await Step("receive and complete", async () =>
{
    var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(15));
    await receiver.CompleteMessageAsync(message);
    return message.Body.ToString();
});
await Step("receive as a stream (left to the base)", async () =>
{
    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await foreach (var message in receiver.ReceiveMessagesAsync(cancel.Token))
    {
        await receiver.CompleteMessageAsync(message);
        return message.Body.ToString();
    }
    return "nothing";
});
await Step("dead-letter with a reason (the overload left to the base on the newest)", async () =>
{
    await sender.SendMessageAsync(new ServiceBusMessage("four"));
    var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(15));
#if NEWEST
    await receiver.DeadLetterMessageAsync(message, new Dictionary<string, object> { ["why"] = "probe" }, "probe", "a reason");
#else
    await receiver.DeadLetterMessageAsync(message, "probe", "a reason");
#endif
    return "dead-lettered";
});
await Step("a processor from CreateProcessor(String) (left to the base)", async () =>
{
    await sender.SendMessageAsync(new ServiceBusMessage("five"));
    await using var processor = client.CreateProcessor(Queue);
    var handled = new TaskCompletionSource<string>();
    processor.ProcessMessageAsync += args => { handled.TrySetResult(args.Message.Body.ToString()); return Task.CompletedTask; };
    processor.ProcessErrorAsync += args => { handled.TrySetException(args.Exception); return Task.CompletedTask; };
    await processor.StartProcessingAsync();
    var body = await handled.Task.WaitAsync(TimeSpan.FromSeconds(30));
    await processor.StopProcessingAsync();
    return body;
});
await Step("a rule manager (left to the base)", async () =>
{
    await using var rules = client.CreateRuleManager("topic.1", "subscription.1");
    return rules.SubscriptionPath;
});
#if NEWEST
await Step("the queue's message sessions (left to the base on the newest)", async () =>
{
    var sessions = 0;
    await foreach (var _ in client.GetMessageSessionsAsync(Queue))
        sessions++;
    return sessions.ToString();
});
#endif
await Step("dispose the sender and the receiver (left to the base)", async () =>
{
    await sender.DisposeAsync();
    await receiver.DisposeAsync();
    return $"sender closed {sender.IsClosed}, receiver closed {receiver.IsClosed}";
});
await Step("dispose the client", async () => { await client.DisposeAsync(); return $"wrapper closed {client.IsClosed}, inner closed {inner.IsClosed}"; });

Console.WriteLine("## What Kronikol recorded");
foreach (var log in RequestResponseLogger.RequestAndResponseLogs)
    Console.WriteLine($"  {log.Type} {log.Method.Value} {log.Uri} ({log.ServiceName})");

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
