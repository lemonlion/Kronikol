// What each broker package logs when a send fails: how many records, each one's kind, status and note, what the diagram draws and what
// the fingerprint reads. Driven through the shipped wrappers and handlers with an inner client that throws, except where the line says so.
// MassTransit's three real observers are called with a proxy for the context, as its package's tests do, for the reason given below.
//
// Usage, from the harness directory, on the published 5.27.13: dotnet run --file probes/failed-sends.cs
// On a worktree holding prototype/r1-first-cut.patch and r2-first-cut.patch: probes/on-prototype.sh <worktree> probes/failed-sends.cs <scratch dir>
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.PubSub@5.27.13
#:package Kronikol.Extensions.ServiceBus@5.27.13
#:package Kronikol.Extensions.EventHubs@5.27.13
#:package Kronikol.Extensions.Kafka@5.27.13
#:package Kronikol.Extensions.MassTransit@5.27.13
#:package Kronikol.Extensions.SQS@5.27.13
#:package Kronikol.Extensions.SNS@5.27.13
#:package Kronikol.Extensions.EventBridge@5.27.13
#:package Kronikol.Extensions.StorageQueues@5.27.13
#:package Azure.Storage.Queues@12.28.1
#:property PublishAot=false

using System.Reflection;
using System.Text.RegularExpressions;
using Amazon.EventBridge;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Queues;
using Confluent.Kafka;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Kronikol.Extensions.EventBridge;
using Kronikol.Extensions.EventHubs;
using Kronikol.Extensions.Kafka;
using Kronikol.Extensions.MassTransit;
using Kronikol.Extensions.PubSub;
using Kronikol.Extensions.ServiceBus;
using Kronikol.Extensions.SNS;
using Kronikol.Extensions.SQS;
using Kronikol.Extensions.StorageQueues;
using Kronikol.History;
using Kronikol.PlantUml;
using Kronikol.Tracking;
using MassTransit;
using EbModel = Amazon.EventBridge.Model;
using SnsModel = Amazon.SimpleNotificationService.Model;
using SqsModel = Amazon.SQS.Model;

var core = typeof(RequestResponseLog).Assembly;
Console.WriteLine($"Kronikol {core.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
Console.WriteLine($"MassTransit {typeof(IBus).Assembly.GetName().Version}");
var read = core.GetType("Kronikol.CallGraph.MessageTable", throwOnError: true)!.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!;
var schemeOf = core.GetType("Kronikol.CallGraph.MessageTable", throwOnError: true)!.GetMethod("Scheme", BindingFlags.Static | BindingFlags.NonPublic)!;

var currentTest = "";
(string Name, string Id) Current() => ("failed sends", currentTest);

string Verdict(RequestResponseLog log) =>
    read.Invoke(null, [(string?)schemeOf.Invoke(null, [log.Uri.ToString()]), log.Method.Value?.ToString()])!.ToString()!;

void Failed(string name, Func<Task> drive)
{
    currentTest = Guid.NewGuid().ToString("N");
    string thrown;
    try
    {
        drive().GetAwaiter().GetResult();
        thrown = "nothing";
    }
    catch (Exception e)
    {
        thrown = e.GetType().Name;
    }

    var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == currentTest).ToArray();
    Console.WriteLine();
    Console.WriteLine($"{name}");
    Console.WriteLine($"    the caller saw: {thrown}. {logs.Length} records.");
    foreach (var log in logs)
    {
        var status = log.StatusCode?.Value?.ToString() ?? "-";
        var body = log.Content is null ? "none" : log.Content.Contains(Boom, StringComparison.Ordinal) ? "the exception's text" : "other";
        var error = log.Error is null ? "none" : log.Error.Contains(Boom, StringComparison.Ordinal) ? "the exception's text" : "other";
        Console.WriteLine($"    {log.Type,-8} {log.Method.Value,-34} kind {log.MetaType,-7} status {status,-24} content {body,-20} error {error,-20} table {Verdict(log)}");
    }

    var answered = logs.Count(l => l.Type == RequestResponseType.Response);
    var asked = logs.Count(l => l.Type == RequestResponseType.Request);
    Console.WriteLine($"    {asked} requests, {answered} responses{(asked > answered ? ", so an orphan request" : "")}");
    foreach (var line in InteractionShape.Calls(logs))
        Console.WriteLine($"    fingerprint line: {line}");

    if (logs.Length == 0)
        return;
    var source = string.Join("\n", PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.Select(p => p.PlainText));
    foreach (var arrow in source.Split('\n').Select(l => l.Trim()).Where(l => Regex.IsMatch(l, @"^\S+ -\S*> \S+\s*:") || Regex.IsMatch(l, @"^\S+ <-\S* \S+\s*:")))
        Console.WriteLine($"    arrow: {arrow}");
    foreach (Match note in Regex.Matches(source, @"^note(?<card><<eventNote>>)? (?<side>\w+)\r?\n(?<body>.*?)\r?\nend note", RegexOptions.Multiline | RegexOptions.Singleline))
    {
        var body = Regex.Replace(note.Groups["body"].Value, @"\s+", " ");
        Console.WriteLine($"    {(note.Groups["card"].Success ? "event card" : "plain note"),-10} {(body.Length > 80 ? body[..80] + "..." : body)}");
    }
}

// ---------------------------------------------------------------------------- the handlers, over a stub that throws

var credentials = new Amazon.Runtime.BasicAWSCredentials("AKIDEXAMPLE", "secret");
const string QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/orders";
const string TopicArn = "arn:aws:sns:us-east-1:123456789012:orders";
var refused = new Throws();

var sqs = new AmazonSQSClient(credentials, new AmazonSQSConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new SqsTrackingMessageHandler(new SqsTrackingMessageHandlerOptions { ServiceName = "SQS", CurrentTestInfoFetcher = Current }, refused)),
});
var sns = new AmazonSimpleNotificationServiceClient(credentials, new AmazonSimpleNotificationServiceConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new SnsTrackingMessageHandler(new SnsTrackingMessageHandlerOptions { ServiceName = "SNS", CurrentTestInfoFetcher = Current }, refused)),
});
var eventBridge = new AmazonEventBridgeClient(credentials, new AmazonEventBridgeConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new EventBridgeTrackingMessageHandler(new EventBridgeTrackingMessageHandlerOptions { ServiceName = "EventBridge", CurrentTestInfoFetcher = Current }, refused)),
});
var queuesOptions = new QueueClientOptions().WithTestTracking(new StorageQueueTrackingMessageHandlerOptions { ServiceName = "Storage Queues", CurrentTestInfoFetcher = Current }, refused);
queuesOptions.Retry.MaxRetries = 0;
var queue = new QueueClient("DefaultEndpointsProtocol=https;AccountName=acct;AccountKey=a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5;EndpointSuffix=core.windows.net", "orders", queuesOptions);

Failed("SQS: a send that throws", () => sqs.SendMessageAsync(new SqsModel.SendMessageRequest { QueueUrl = QueueUrl, MessageBody = "hello" }));
Failed("SNS: a publish that throws", () => sns.PublishAsync(new SnsModel.PublishRequest { TopicArn = TopicArn, Message = "hello" }));
Failed("EventBridge: a put that throws", () => eventBridge.PutEventsAsync(new EbModel.PutEventsRequest { Entries = [new EbModel.PutEventsRequestEntry { EventBusName = "orders-bus", Source = "shop", DetailType = "OrderPlaced", Detail = "{}" }] }));
Failed("Storage Queues: a send that throws", () => queue.SendMessageAsync("hello"));

// ---------------------------------------------------------------------------- the wrappers, over an inner client that throws

var pubSubOptions = new PubSubTrackingOptions { ServiceName = "Pub/Sub", CurrentTestInfoFetcher = Current };
var publisher = new TrackingPublisherClient(new ThrowingPublisher(), pubSubOptions);
Failed("Pub/Sub: a publish that throws", () => publisher.PublishAsync(new PubsubMessage { Data = ByteString.CopyFromUtf8("hello") }));

var eventHubsOptions = new EventHubsTrackingOptions { ServiceName = "Event Hubs", CurrentTestInfoFetcher = Current };
var producer = new TrackingEventHubProducerClient(new ThrowingProducer(), eventHubsOptions);
Failed("Event Hubs: a send that throws", () => producer.SendAsync([new EventData("hello")]));

var serviceBusOptions = new ServiceBusTrackingOptions { ServiceName = "Service Bus", CurrentTestInfoFetcher = Current };
var sender = new TrackingServiceBusSender(new ThrowingSender(), new ServiceBusTracker(serviceBusOptions), serviceBusOptions);
Failed("Service Bus: a send that throws", () => sender.SendMessageAsync(new ServiceBusMessage("hello")));

var kafka = new KafkaTracker(new KafkaTrackingOptions { ServiceName = "Kafka", CurrentTestInfoFetcher = Current });
Failed("Kafka: a produce whose delivery fails (the tracking TrackingKafkaProducer holds, Failed being what it calls)", () =>
{
    var tracking = kafka.TrackProduce("orders", new Message<string, string> { Key = "k", Value = "v" }, KafkaOperation.ProduceAsync);
    tracking.Failed(new KafkaException(new Error(ErrorCode.Local_MsgTimedOut, Boom)));
    return Task.CompletedTask;
});

// ---------------------------------------------------------------------------- MassTransit, its three real observers

// The real bus is not run: MassTransit 9 refuses to build one with no licence ("License must be specified with SetLicense"), and 9.0.0 is
// the package's floor. So the observers are called as a transport calls them. Its own documentation says which: PostSend "after the message
// is sent to the transport", SendFault "when the message fails to send to the transport", and PostConsume "in the case of an exception this
// method is not called" (MassTransit.Abstractions 9.0.0, ISendObserver, IPublishObserver, IConsumeObserver).
var massTransitOptions = new MassTransitTrackingOptions { ServiceName = "RabbitMQ", CallerName = "Test", CurrentTestInfoFetcher = Current };
var massTransit = new MassTransitTracker(massTransitOptions);
var sendObserver = new TrackingSendObserver(massTransit, massTransitOptions);
var publishObserver = new TrackingPublishObserver(massTransit, massTransitOptions);
var consumeObserver = new TrackingConsumeObserver(massTransit, massTransitOptions);
var fault = new InvalidOperationException(Boom);
var placed = new OrderPlaced("1");

Failed("MassTransit control: a send that works (PostSend)", () => sendObserver.PostSend(ContextProxy.Create<SendContext<OrderPlaced>>(placed)));
Failed("MassTransit control: a publish that works (PostPublish)", () => publishObserver.PostPublish(ContextProxy.Create<PublishContext<OrderPlaced>>(placed)));
Failed("MassTransit control: a consume that works (PostConsume)", () => consumeObserver.PostConsume(ContextProxy.Create<ConsumeContext<OrderPlaced>>(placed)));
Failed("MassTransit: a send that fails (SendFault)", () => sendObserver.SendFault(ContextProxy.Create<SendContext<OrderPlaced>>(placed), fault));
Failed("MassTransit: a publish that fails (PublishFault)", () => publishObserver.PublishFault(ContextProxy.Create<PublishContext<OrderPlaced>>(placed), fault));
Failed("MassTransit: a consumer that throws (ConsumeFault)", () => consumeObserver.ConsumeFault(ContextProxy.Create<ConsumeContext<OrderPlaced>>(placed), fault));

// ---------------------------------------------------------------------------- what answering a failed send moves in the fingerprint (F22)

Console.WriteLine();
Console.WriteLine("F22: one SQS send that throws, as the handler logs it, and with the response Storage Queues' catch writes in its place");
currentTest = Guid.NewGuid().ToString("N");
try
{
    sqs.SendMessageAsync(new SqsModel.SendMessageRequest { QueueUrl = QueueUrl, MessageBody = "hello" }).GetAwaiter().GetResult();
}
catch (Exception)
{
}
var orphan = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == currentTest).ToArray();
var request = orphan.Single(l => l.Type == RequestResponseType.Request);
var failure = new HttpRequestException(Boom);
var answer = request with
{
    Type = RequestResponseType.Response,
    Content = null,
    StatusCode = FailedSend.Status(failure),
    Error = FailedSend.Describe(failure),
};
Console.WriteLine($"    as logged:  {InteractionShape.Calls(orphan).Single()}");
Console.WriteLine($"    answered:   {InteractionShape.Calls([request, answer]).Single()}");
var same = InteractionShape.Calls(orphan).Single().ToString() == InteractionShape.Calls([request, answer]).Single().ToString();
Console.WriteLine($"    the same line: {same}");

return 0;

// One word, so a note and a status can be searched for it.
public partial class Program
{
    public const string Boom = "broker-refused";
}

public record OrderPlaced(string OrderId);

// What a transport hands an observer, reduced to what the observers read (as ContextProxy in tests/Kronikol.Tests.MassTransit).
public class ContextProxy : DispatchProxy
{
    private object? _message;

    public static T Create<T>(object message) where T : class
    {
        var proxy = Create<T, ContextProxy>();
        ((ContextProxy)(object)proxy)._message = message;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        "get_Message" => _message,
        "get_DestinationAddress" => new Uri("rabbitmq://localhost/orders"),
        "get_SourceAddress" => new Uri("rabbitmq://localhost/test"),
        "get_MessageId" => (Guid?)Guid.NewGuid(),
        _ => targetMethod?.ReturnType is { IsValueType: true } type && type != typeof(void) ? Activator.CreateInstance(type) : null,
    };
}

sealed class Throws : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException(Program.Boom);
}

sealed class AwsFactory(HttpMessageHandler handler) : Amazon.Runtime.HttpClientFactory
{
    public override HttpClient CreateHttpClient(Amazon.Runtime.IClientConfig clientConfig) => new(handler, disposeHandler: false);
}

sealed class ThrowingPublisher : PublisherClient
{
    public override TopicName TopicName { get; } = new("shop", "orders");

    public override Task<string> PublishAsync(PubsubMessage message) => throw new InvalidOperationException(Program.Boom);
}

sealed class ThrowingProducer() : EventHubProducerClient("Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==;EntityPath=orders")
{
    public override Task SendAsync(IEnumerable<EventData> eventBatch, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Program.Boom);
}

sealed class ThrowingSender : ServiceBusSender
{
    public override string EntityPath => "orders";
    public override string FullyQualifiedNamespace => "test.servicebus.windows.net";

    public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Program.Boom);
}
