// What an Event Hubs client built for a tracking client's base answers (plans/CLIENT_FLOORS_PLAN.md R2b): the
// identifier, names and IsClosed of a client built through the constructor for mocking, and of one built from a
// namespace, a hub and a credential that throws if asked for a token, which shows whether building or closing it asks.
//   dotnet run -p:EventHubsVersion=<release>
using Azure.Core;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;

const string connection = "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==;EntityPath=my-hub";
var version = typeof(EventHubProducerClient).Assembly.GetName().Version;
Console.WriteLine($"Azure.Messaging.EventHubs {version}");

static string Try(Func<object?> read)
{
    try { return read()?.ToString() ?? "null"; }
    catch (Exception ex) { return $"throws {ex.GetType().Name}: {ex.Message.Split(Environment.NewLine)[0]}"; }
}

static string Identifier(object client) =>
    client.GetType().GetProperty("Identifier") is { } p ? Try(() => p.GetValue(client)) : "(no Identifier on this release)";

var inner = new EventHubProducerClient(connection);
Console.WriteLine($"inner producer: identifier {Identifier(inner)}");

var mocking = new MockingProducer();
Console.WriteLine($"mocking producer: identifier {Identifier(mocking)}, names {Try(() => mocking.FullyQualifiedNamespace)} {Try(() => mocking.EventHubName)}, closed {mocking.IsClosed}");
Console.WriteLine($"mocking producer closes: {Try(() => { mocking.CloseAsync().GetAwaiter().GetResult(); return "ok"; })}");

var built = new EventHubProducerClient(inner.FullyQualifiedNamespace, inner.EventHubName, new ThrowingCredential());
Console.WriteLine($"built producer: identifier {Identifier(built)}, names {built.FullyQualifiedNamespace} {built.EventHubName}, closed {built.IsClosed}");
Console.WriteLine($"built producer closes: {Try(() => { built.CloseAsync().GetAwaiter().GetResult(); return "ok"; })}, closed {built.IsClosed}");
Console.WriteLine($"built producer, another close: {Try(() => { built.CloseAsync().GetAwaiter().GetResult(); return "ok"; })}");

foreach (var host in new[] { "unknown.invalid", "unknown" })
{
    Console.WriteLine($"producer named {host}: {Try(() => new EventHubProducerClient(host, "unknown", new ThrowingCredential()).FullyQualifiedNamespace)}");
    Console.WriteLine($"consumer named {host}: {Try(() => new EventHubConsumerClient("$Default", host, "unknown", new ThrowingCredential()).FullyQualifiedNamespace)}");
}
var started = System.Diagnostics.Stopwatch.StartNew();
var nowhere = new EventHubProducerClient("unknown.invalid", "unknown", new ThrowingCredential());
Console.WriteLine($"producer named unknown.invalid asked for partitions: {Try(() => nowhere.GetPartitionIdsAsync().GetAwaiter().GetResult())} after {started.ElapsedMilliseconds} ms");
await nowhere.CloseAsync();

var unused = new EventHubProducerClient(inner.FullyQualifiedNamespace, inner.EventHubName, new ThrowingCredential());
started.Restart();
Console.WriteLine($"built producer asked for partitions: {Try(() => unused.GetPartitionIdsAsync().GetAwaiter().GetResult())} after {started.ElapsedMilliseconds} ms");
await unused.CloseAsync();

var innerConsumer = new EventHubConsumerClient("my-group", connection);
var mockingConsumer = new MockingConsumer();
Console.WriteLine($"inner consumer: identifier {Identifier(innerConsumer)}");
Console.WriteLine($"mocking consumer: identifier {Identifier(mockingConsumer)}, group {Try(() => mockingConsumer.ConsumerGroup)}, names {Try(() => mockingConsumer.FullyQualifiedNamespace)} {Try(() => mockingConsumer.EventHubName)}, closed {mockingConsumer.IsClosed}");
var builtConsumer = new EventHubConsumerClient(innerConsumer.ConsumerGroup, innerConsumer.FullyQualifiedNamespace, innerConsumer.EventHubName, new ThrowingCredential());
Console.WriteLine($"built consumer: identifier {Identifier(builtConsumer)}, {builtConsumer.ConsumerGroup} {builtConsumer.FullyQualifiedNamespace} {builtConsumer.EventHubName}");
Console.WriteLine($"built consumer closes: {Try(() => { builtConsumer.CloseAsync().GetAwaiter().GetResult(); return "ok"; })}, closed {builtConsumer.IsClosed}");

if (typeof(EventHubProducerClientOptions).GetProperty("Identifier") is { } option)
{
    var options = new EventHubProducerClientOptions();
    option.SetValue(options, Identifier(inner));
    var named = new EventHubProducerClient(inner.FullyQualifiedNamespace, inner.EventHubName, new ThrowingCredential(), options);
    Console.WriteLine($"built producer given the inner's identifier: {Identifier(named)}");
    await named.CloseAsync();
}
await inner.CloseAsync();
await innerConsumer.CloseAsync();

sealed class MockingProducer : EventHubProducerClient { }
sealed class MockingConsumer : EventHubConsumerClient { }

sealed class ThrowingCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("asked for a token");
    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("asked for a token");
}
