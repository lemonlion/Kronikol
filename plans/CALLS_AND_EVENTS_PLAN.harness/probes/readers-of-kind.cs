// Three readers of a broker call's kind, on records the real trackers and handlers write, to compare 5.27.13 with the prototype:
//
// 1. Protocol. Which options let a consumer blank a tracker's category, then ComponentDiagramGenerator.ExtractRelationships on each
//    call as written, and on the same call with its category blanked by the one route that can: RequestResponseLogger.Redaction,
//    whose CaptureRedaction.Custom hook may rewrite anything on a record. CallGraphBuilder.cs:64 calls the same Protocol, so the card's
//    edge reads the same.
// 2. The collapse key. SequenceCollapser.Apply over the same sequence of real SDK calls at Detailed and at Raw.
// 3. The note's card. PlantUmlCreator over real SDK calls at Detailed: which card each note is on, what the note holds, and the arrows.
//
// Usage, from the harness directory, on the published 5.27.13: dotnet run --file probes/readers-of-kind.cs
// On a worktree holding prototype/r1-first-cut.patch and r2-first-cut.patch: probes/on-prototype.sh <worktree> probes/readers-of-kind.cs <scratch dir>
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

using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Amazon.EventBridge;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Azure.Storage.Queues;
using Kronikol.ComponentDiagram;
using Kronikol.Extensions.EventBridge;
using Kronikol.Extensions.EventHubs;
using Kronikol.Extensions.Kafka;
using Kronikol.Extensions.MassTransit;
using Kronikol.Extensions.PubSub;
using Kronikol.Extensions.ServiceBus;
using Kronikol.Extensions.SNS;
using Kronikol.Extensions.SQS;
using Kronikol.Extensions.StorageQueues;
using Kronikol.PlantUml;
using Kronikol.Tracking;
using EbModel = Amazon.EventBridge.Model;
using SnsModel = Amazon.SimpleNotificationService.Model;
using SqsModel = Amazon.SQS.Model;

Console.WriteLine($"Kronikol {typeof(RequestResponseLog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");

var currentTest = "";
(string Name, string Id) Current() => ("readers of kind", currentTest);

RequestResponseLog[] Captured(Action call)
{
    currentTest = Guid.NewGuid().ToString("N");
    call();
    return RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == currentTest).ToArray();
}

// ---------------------------------------------------------------------------- the clients

var credentials = new Amazon.Runtime.BasicAWSCredentials("AKIDEXAMPLE", "secret");
const string QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/orders";
const string TopicArn = "arn:aws:sns:us-east-1:123456789012:orders";

AmazonSQSClient Sqs(SqsTrackingVerbosity verbosity) => new(credentials, new AmazonSQSConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new SqsTrackingMessageHandler(new SqsTrackingMessageHandlerOptions { ServiceName = "SQS", Verbosity = verbosity, CurrentTestInfoFetcher = Current },
        new Stub(request => Answer(request.Headers.TryGetValues("X-Amz-Target", out var target) && target.First().EndsWith("ReceiveMessage", StringComparison.Ordinal)
            ? """{"Messages":[{"MessageId":"m1","ReceiptHandle":"rh","Body":"{\"orderId\":\"order-1\"}"}]}"""
            : """{"MessageId":"m1","MD5OfMessageBody":"0"}""", "application/x-amz-json-1.0")))),
});
AmazonSimpleNotificationServiceClient Sns(SnsTrackingVerbosity verbosity) => new(credentials, new AmazonSimpleNotificationServiceConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new SnsTrackingMessageHandler(new SnsTrackingMessageHandlerOptions { ServiceName = "SNS", Verbosity = verbosity, CurrentTestInfoFetcher = Current },
        new Stub(_ => Answer("<Ok/>", "text/xml")))),
});
AmazonEventBridgeClient EventBridge(EventBridgeTrackingVerbosity verbosity) => new(credentials, new AmazonEventBridgeConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new EventBridgeTrackingMessageHandler(new EventBridgeTrackingMessageHandlerOptions { ServiceName = "EventBridge", Verbosity = verbosity, CurrentTestInfoFetcher = Current },
        new Stub(_ => Answer("{}", "application/x-amz-json-1.1")))),
});
QueueClient Queue(StorageQueueTrackingVerbosity verbosity)
{
    var options = new QueueClientOptions().WithTestTracking(new StorageQueueTrackingMessageHandlerOptions { ServiceName = "Storage Queues", Verbosity = verbosity, CurrentTestInfoFetcher = Current },
        new Stub(request => new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : request.Method == HttpMethod.Delete ? HttpStatusCode.NoContent : HttpStatusCode.OK) { Content = new StringContent("") }));
    options.Retry.MaxRetries = 0;
    return new QueueClient("DefaultEndpointsProtocol=https;AccountName=acct;AccountKey=a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5;EndpointSuffix=core.windows.net", "orders", options);
}

static void Run(params Func<Task>[] calls)
{
    foreach (var call in calls)
    {
        try
        {
            call().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // The handler logged the call before the SDK read the stub's answer, which an SDK may refuse (its MD5, its XML). A call
            // that logged nothing fails below, where its record is looked for.
        }
    }
}

// ---------------------------------------------------------------------------- 1. the protocol

Console.WriteLine();
Console.WriteLine("== options with a property naming a category");
foreach (var type in new[]
{
    typeof(SqsTrackingMessageHandlerOptions), typeof(SnsTrackingMessageHandlerOptions), typeof(EventBridgeTrackingMessageHandlerOptions), typeof(StorageQueueTrackingMessageHandlerOptions),
    typeof(PubSubTrackingOptions), typeof(EventHubsTrackingOptions), typeof(KafkaTrackingOptions), typeof(MassTransitTrackingOptions), typeof(ServiceBusTrackingOptions), typeof(MessageTrackerOptions),
})
{
    var named = type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.Name.Contains("Category", StringComparison.OrdinalIgnoreCase)).Select(p => $"{p.Name} ({p.PropertyType.Name})").ToArray();
    Console.WriteLine($"{type.Name,-44} {(named.Length == 0 ? "none" : string.Join(", ", named))}");
}

var pubSub = new PubSubTracker(new PubSubTrackingOptions { ServiceName = "Pub/Sub", CurrentTestInfoFetcher = Current });
var eventHubs = new EventHubsTracker(new EventHubsTrackingOptions { ServiceName = "Event Hubs", CurrentTestInfoFetcher = Current });
var kafka = new KafkaTracker(new KafkaTrackingOptions { ServiceName = "Kafka", CurrentTestInfoFetcher = Current, TrackCommit = true });
var massTransit = new MassTransitTracker(new MassTransitTrackingOptions { ServiceName = "RabbitMQ", CurrentTestInfoFetcher = Current });
var blankTracker = new MessageTracker(new MessageTrackerOptions { DependencyCategory = "", CurrentTestInfoFetcher = Current });
var sqsDetailed = Sqs(SqsTrackingVerbosity.Detailed);
var snsDetailed = Sns(SnsTrackingVerbosity.Detailed);
var ebDetailed = EventBridge(EventBridgeTrackingVerbosity.Detailed);
var queueDetailed = Queue(StorageQueueTrackingVerbosity.Detailed);

void Pair<TInfo>(Func<TInfo, string?, (Guid, Guid)> request, Action<TInfo, Guid, Guid, string?> response, TInfo info)
{
    var (id, trace) = request(info, "{}");
    response(info, id, trace, null);
}

Console.WriteLine();
Console.WriteLine("== one call of each: the label, category and kind as written; the edge's protocol as written; and with CaptureRedaction.Custom blanking the category");
foreach (var (name, call) in new (string, Action)[]
{
    ("SQS send", () => Run(() => sqsDetailed.SendMessageAsync(new SqsModel.SendMessageRequest { QueueUrl = QueueUrl, MessageBody = "hello" }))),
    ("SQS delete", () => Run(() => sqsDetailed.DeleteMessageAsync(new SqsModel.DeleteMessageRequest { QueueUrl = QueueUrl, ReceiptHandle = "rh" }))),
    ("SNS publish", () => Run(() => snsDetailed.PublishAsync(new SnsModel.PublishRequest { TopicArn = TopicArn, Message = "hello" }))),
    ("EventBridge put", () => Run(() => ebDetailed.PutEventsAsync(new EbModel.PutEventsRequest { Entries = [new EbModel.PutEventsRequestEntry { EventBusName = "orders-bus", Source = "shop", DetailType = "OrderPlaced", Detail = "{}" }] }))),
    ("Storage Queues send", () => Run(() => queueDetailed.SendMessageAsync("hello"))),
    ("Pub/Sub publish", () => Pair(pubSub.LogRequest, pubSub.LogResponse, new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/orders", null, 1))),
    ("Pub/Sub ack", () => Pair(pubSub.LogRequest, pubSub.LogResponse, new PubSubOperationInfo(PubSubOperation.Acknowledge, null, "projects/p/subscriptions/orders", 1))),
    ("Event Hubs send", () => Pair(eventHubs.LogRequest, eventHubs.LogResponse, new EventHubsOperationInfo(EventHubsOperation.Send, "orders", null, 1))),
    ("Event Hubs partitions", () => Pair(eventHubs.LogRequest, eventHubs.LogResponse, new EventHubsOperationInfo(EventHubsOperation.GetPartitionIds, "orders"))),
    ("Kafka commit", () => kafka.LogCommit(new KafkaOperationInfo(KafkaOperation.Commit))),
    ("MassTransit fault", () => massTransit.LogSendFault(new MassTransitOperationInfo(MassTransitOperation.SendFault, "OrderPlaced", new Uri("rabbitmq://localhost/orders")), new InvalidOperationException("closed"))),
    ("MessageTracker, category \"\"", () => blankTracker.TrackConsumeEvent("Consume (Kafka)", "OrderService", new Uri("kafka:///orders"), new { Id = 1 })),
})
{
    var logs = Captured(call);
    var request = logs.First(l => l.Type == RequestResponseType.Request);
    var written = ComponentDiagramGenerator.ExtractRelationships(logs).Single().Protocol;
    RequestResponseLogger.Redaction = new CaptureRedaction { Custom = log => log with { DependencyCategory = "" } };
    var redacted = Captured(call);
    RequestResponseLogger.Redaction = null;
    var blanked = ComponentDiagramGenerator.ExtractRelationships(redacted).Single().Protocol;
    Console.WriteLine($"{name,-30} {request.Method.Value,-30} category {(request.DependencyCategory is { Length: > 0 } c ? c : "(blank)"),-13} kind {request.MetaType,-7} edge {written,-16} blanked {blanked}");
}

// ---------------------------------------------------------------------------- 2. the collapse key

string Collapsed(RequestResponseLog[] logs)
{
    var result = SequenceCollapser.Apply(logs.ToList(), collapse: true, threshold: 2, maxPairs: null);
    var arrows = result.Traces.Where(t => t.Type == RequestResponseType.Request).Select(t => t.CollapsedCount > 1 ? $"{t.Method.Value} x{t.CollapsedCount}" : $"{t.Method.Value}").ToArray();
    return $"{arrows.Length} arrows: {string.Join(", ", arrows)}";
}

Console.WriteLine();
Console.WriteLine("== SequenceCollapser over a sequence of calls");
foreach (var verbosityName in new[] { "Detailed", "Raw" })
{
    var sqs = Sqs(Enum.Parse<SqsTrackingVerbosity>(verbosityName));
    var sns = Sns(Enum.Parse<SnsTrackingVerbosity>(verbosityName));
    var eb = EventBridge(Enum.Parse<EventBridgeTrackingVerbosity>(verbosityName));
    var queue = Queue(Enum.Parse<StorageQueueTrackingVerbosity>(verbosityName));

    Task<SqsModel.SendMessageResponse> SqsSend() => sqs.SendMessageAsync(new SqsModel.SendMessageRequest { QueueUrl = QueueUrl, MessageBody = "hello" });
    Task<SqsModel.ReceiveMessageResponse> SqsReceive() => sqs.ReceiveMessageAsync(new SqsModel.ReceiveMessageRequest { QueueUrl = QueueUrl });
    Task<SqsModel.DeleteMessageResponse> SqsDelete() => sqs.DeleteMessageAsync(new SqsModel.DeleteMessageRequest { QueueUrl = QueueUrl, ReceiptHandle = "rh" });
    Task SnsPublish() => sns.PublishAsync(new SnsModel.PublishRequest { TopicArn = TopicArn, Message = "hello" });
    Task SnsCreate() => sns.CreateTopicAsync(new SnsModel.CreateTopicRequest { Name = "orders" });
    Task EbPut() => eb.PutEventsAsync(new EbModel.PutEventsRequest { Entries = [new EbModel.PutEventsRequestEntry { EventBusName = "orders-bus", Source = "shop", DetailType = "OrderPlaced", Detail = "{}" }] });
    Task EbRule() => eb.DescribeRuleAsync(new EbModel.DescribeRuleRequest { Name = "orders-rule", EventBusName = "orders-bus" });

    Console.WriteLine($"-- {verbosityName}");
    Console.WriteLine($"SQS send, send, delete, delete       {Collapsed(Captured(() => Run(SqsSend, SqsSend, SqsDelete, SqsDelete)))}");
    Console.WriteLine($"SQS send, receive, delete            {Collapsed(Captured(() => Run(SqsSend, SqsReceive, SqsDelete)))}");
    Console.WriteLine($"SNS publish, publish, create, create {Collapsed(Captured(() => Run(SnsPublish, SnsPublish, SnsCreate, SnsCreate)))}");
    Console.WriteLine($"EventBridge put, put, rule, rule     {Collapsed(Captured(() => Run(EbPut, EbPut, EbRule, EbRule)))}");
    Console.WriteLine($"Queues send, send, peek, peek        {Collapsed(Captured(() => Run(() => queue.SendMessageAsync("hello"), () => queue.SendMessageAsync("hello"), () => queue.PeekMessagesAsync(), () => queue.PeekMessagesAsync())))}");
}

// ---------------------------------------------------------------------------- 3. the note's card

Console.WriteLine();
Console.WriteLine("== SQS at Detailed through PlantUmlCreator: each note's card, in order, and the start of what it holds");
var noteCalls = Captured(() => Run(
    () => sqsDetailed.SendMessageAsync(new SqsModel.SendMessageRequest { QueueUrl = QueueUrl, MessageBody = "hello" }),
    () => sqsDetailed.ReceiveMessageAsync(new SqsModel.ReceiveMessageRequest { QueueUrl = QueueUrl, WaitTimeSeconds = 5 }),
    () => sqsDetailed.DeleteMessageAsync(new SqsModel.DeleteMessageRequest { QueueUrl = QueueUrl, ReceiptHandle = "rh" })));
var source = string.Join("\n", PlantUmlCreator.GetPlantUmlImageTagsPerTestId(noteCalls).Single().PlantUmls.Select(p => p.PlainText));
Console.WriteLine($".eventNote style block: {source.Contains(".eventNote", StringComparison.Ordinal)}");
foreach (Match note in Regex.Matches(source, @"^note(?<card><<eventNote>>)? (?<side>\w+)\r?\n(?<body>.*?)\r?\nend note", RegexOptions.Multiline | RegexOptions.Singleline))
{
    var body = Regex.Replace(note.Groups["body"].Value, @"\s+", " ");
    Console.WriteLine($"{(note.Groups["card"].Success ? "event card" : "plain note"),-11} {note.Groups["side"].Value,-6} {(body.Length > 90 ? body[..90] + "..." : body)}");
}

Console.WriteLine("arrows:");
foreach (var line in source.Split('\n').Select(l => l.Trim()).Where(l => Regex.IsMatch(l, @"^\S+ -\S*> \S+\s*:")))
    Console.WriteLine($"  {line}");

return 0;

static HttpResponseMessage Answer(string body, string mediaType) =>
    new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(answer(request));
}

// The shipped WithTestTracking for AWS takes no inner handler; this is its factory with the stub inside.
sealed class AwsFactory(HttpMessageHandler handler) : Amazon.Runtime.HttpClientFactory
{
    public override HttpClient CreateHttpClient(Amazon.Runtime.IClientConfig clientConfig) => new(handler, disposeHandler: false);
}
