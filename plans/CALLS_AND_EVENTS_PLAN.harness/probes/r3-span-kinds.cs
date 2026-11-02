// What OTLP makes of every message-moving operation of every broker package, at every verbosity, through the shipped trackers:
// R3's first cut (prototype/r3-first-cut.patch, on R1) beside the plan's section 5.5 read literally and the table the operation
// truly belongs to (results/r3-span-kinds.tsv). Also what each span's resource and peer say, and whether the call comes through
// NDJSON (kronikol export) and through the OTLP tap as the same call.
//
// R2 is not in that worktree, so its kinds are applied to each record here, by plan section 5.4: a publish, a receive and a delivery
// are Event, settlement, management and Other are Default, and a response carries its request's kind. The column r1Kind is what the
// tracker wrote.
//
// "expected" is by hand, one line per operation, from the tracker's code and OTel's table (docs/messaging/messaging-spans.md at
// v1.44.0): the true table of the plan review. Two choices sit in it, each named in the report: an operation recorded as the HTTP
// request itself (Raw) is CLIENT, and a Kafka consume through the wrapper's default path is CONSUMER although its API is a poll.
//
// Published-package lines, so that it builds as the rest of this harness does, but it says nothing of 5.27.13: run it on a checkout of
// 27cc0e46 with prototype/r1-first-cut.patch and prototype/r3-first-cut.patch applied, with on-prototype.sh.
//
// Usage, from the harness directory: probes/on-prototype.sh <worktree> probes/r3-span-kinds.cs <scratch> results/r3-span-kinds.tsv > results/r3-span-kinds.txt
// On R1, R3 and R2 together the column r1Kind equals r2Kind on all 194 rows, and only r1Kind, today and r3Alone, which read what the tracker wrote, move: results/r3-on-the-stack.txt.
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.Otlp@5.27.13
#:package Kronikol.Extensions.PubSub@5.27.13
#:package Kronikol.Extensions.ServiceBus@5.27.13
#:package Kronikol.Extensions.EventHubs@5.27.13
#:package Kronikol.Extensions.Kafka@5.27.13
#:package Kronikol.Extensions.MassTransit@5.27.13
#:package Kronikol.Extensions.SQS@5.27.13
#:package Kronikol.Extensions.SNS@5.27.13
#:package Kronikol.Extensions.EventBridge@5.27.13
#:package Kronikol.Extensions.StorageQueues@5.27.13
#:property PublishAot=false

using System.Reflection;
using System.Text;
using Amazon.EventBridge;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Azure.Storage.Queues;
using Confluent.Kafka;
using Kronikol.Extensions.EventBridge;
using Kronikol.Extensions.EventHubs;
using Kronikol.Extensions.Kafka;
using Kronikol.Extensions.MassTransit;
using Kronikol.Extensions.Otlp;
using Kronikol.Extensions.PubSub;
using Kronikol.Extensions.ServiceBus;
using Kronikol.Extensions.SNS;
using Kronikol.Extensions.SQS;
using Kronikol.Extensions.StorageQueues;
using Kronikol.Ingestion;
using Kronikol.Tracking;
using EbModel = Amazon.EventBridge.Model;
using SnsModel = Amazon.SimpleNotificationService.Model;
using SqsModel = Amazon.SQS.Model;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: dotnet run --file probes/r3-span-kinds.cs -- <out.tsv>");
    return 2;
}

const string Producer = nameof(OtlpSpanKind.Producer);
const string Client = nameof(OtlpSpanKind.Client);
const string Consumer = nameof(OtlpSpanKind.Consumer);
const string Event = nameof(RequestResponseMetaType.Event);
const string Default = nameof(RequestResponseMetaType.Default);

var rows = new List<string[]>();
var failures = new List<string>();

var currentTest = "";
(string Name, string Id) Current() => ("r3-span-kinds", currentTest);

var messageTable = typeof(RequestResponseLog).Assembly.GetType("Kronikol.CallGraph.MessageTable", throwOnError: true)!;
var read = messageTable.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!;

var otlp = new OtlpExportOptions { Endpoint = new Uri("http://localhost:4318/v1/traces") };
var tap = new OtlpTapOptions { ListenPort = 1 };
tap.CaptureKinds.Add(OtlpCaptureKinds.Messaging);

// ---------------------------------------------------------------------------- what the plan's text says

// Section 5.5's five rows, in its order, against the table as it stood at R1, where Pub/Sub's Receive and Event Hubs' Process were
// Receives and Kafka's and MassTransit's consumes were unknown. The first cut's Delivers is mapped back to that.
string Literal(RequestResponseLog request)
{
    if (request.MetaType != RequestResponseMetaType.Event)
        return Client;
    var label = request.Method.Value?.ToString();
    var scheme = request.Uri.Scheme;
    var verdict = read.Invoke(null, [scheme, label])!.ToString();
    if (verdict == "Delivers")
        verdict = scheme is "pubsub" or "eventhubs" ? "Receives" : "Unknown";
    return verdict switch
    {
        "Publishes" => Producer,
        "Receives" => Client,
        _ => label is not null && (label.StartsWith("Consume", StringComparison.Ordinal) || label.StartsWith("Process", StringComparison.Ordinal)) ? Consumer : Producer,
    };
}

// ---------------------------------------------------------------------------- one call

(RequestResponseLog Request, RequestResponseLog? Response)[] Capture(Action call)
{
    currentTest = Guid.NewGuid().ToString("N");
    try
    {
        call();
    }
    catch (Exception)
    {
        // An SDK call that cannot parse the stub's answer has been logged by then.
    }

    var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == currentTest).ToArray();
    return logs.Where(l => l.Type == RequestResponseType.Request)
        .Select(request => (request, (RequestResponseLog?)logs.FirstOrDefault(r => r.Type == RequestResponseType.Response && r.RequestResponseId == request.RequestResponseId)))
        .ToArray();
}

void Measure(string broker, string route, string operation, string verbosity, string r2Kind, string expected, Action call)
{
    var pairs = Capture(call);
    if (pairs.Length == 0)
    {
        failures.Add($"{broker} {route} {operation} {verbosity}: no record");
        rows.Add([broker, route, operation, verbosity, "NO-RECORD", "-", "-", "-", r2Kind, "-", "-", expected, "-", "-", "-", "-", "-", "-", "-", "-", "-", "-", "-"]);
        return;
    }

    foreach (var (raw, rawResponse) in pairs)
    {
        // R2's kind on both halves.
        var kind = Enum.Parse<RequestResponseMetaType>(r2Kind);
        var request = raw with { MetaType = kind };
        var response = rawResponse is null ? null : rawResponse with { MetaType = kind };

        var span = OtlpSpanMapper.Map(request, response, otlp, DateTimeOffset.UtcNow);
        var label = request.Method.Value?.ToString() ?? "";
        var literal = Literal(request);
        var cut = span.Kind.ToString();

        // kronikol export's path: each record through its NDJSON line, then the same mapper.
        var viaNdjson = OtlpSpanMapper.MapAll(
            new[] { request, response }.Where(l => l is not null).Select(l => InteractionRecord.FromJson(InteractionRecord.FromLog(l!).ToJson()).ToLog()),
            otlp, DateTimeOffset.UtcNow).Spans;
        var ndjson = viaNdjson.Count == 1 ? viaNdjson[0].Kind.ToString() : $"{viaNdjson.Count} spans";

        // The way back: the span encoded and read as a collector would be sent it, through the tap's mapper.
        var decoded = Assert1(OtlpTraceReader.ReadJson(Encoding.UTF8.GetBytes(OtlpJsonEncoder.Encode([span]))));
        var mapped = SpanToInteractionMapper.Map(decoded, tap);
        var back = mapped is null ? "DROPPED" : $"{mapped.ToLogs().Request.MetaType}: {mapped.Method} @ {mapped.Uri}";

        rows.Add(
        [
            broker, route, operation, verbosity, OneLine(label), request.Uri.Scheme,
            read.Invoke(null, [request.Uri.Scheme, label])!.ToString()!,
            raw.MetaType.ToString(), r2Kind, literal, cut, expected,
            literal == expected ? "ok" : "WRONG",
            cut == expected ? "ok" : "WRONG",
            span.ResourceServiceName, span.Attribute("peer.service") ?? "-",
            span.Attribute("messaging.system") ?? "-",
            span.Attribute("messaging.operation.name") ?? "-",
            span.Attribute("messaging.operation.type") ?? "-",
            span.Attribute("messaging.destination.name") ?? "-",
            span.Attribute("http.request.method") ?? "-",
            ndjson == cut ? "same" : ndjson,
            OneLine(back),
            viaNdjson.Count == 1 ? viaNdjson[0].ResourceServiceName : "-",
            // OtlpSpanMapper.cs:254 at 27cc0e46, on the record as the tracker wrote it, and the same record on R3 with R2 not applied.
            raw.MetaType == RequestResponseMetaType.Event ? Producer : Client,
            OtlpSpanMapper.Map(raw, rawResponse, otlp, DateTimeOffset.UtcNow).Kind.ToString(),
        ]);
    }
}

static OtlpSpan Assert1(IReadOnlyList<OtlpSpan> spans) => spans.Count == 1 ? spans[0] : throw new InvalidOperationException($"{spans.Count} spans");
static string OneLine(string text) => text.ReplaceLineEndings(" ").Replace('\t', ' ');

string Sends(string verbosity) => verbosity == "Raw" ? Client : Producer;

// ---------------------------------------------------------------------------- Pub/Sub

const string PubSubTopic = "projects/shop/topics/orders";
const string PubSubSubscription = "projects/shop/subscriptions/orders-sub";
foreach (var verbosity in Enum.GetValues<PubSubTrackingVerbosity>())
{
    var tracker = new PubSubTracker(new PubSubTrackingOptions { ServiceName = "PubSub", CallerName = "App", Verbosity = verbosity, CurrentTestInfoFetcher = Current });
    foreach (var (operation, info, r2, expected) in new (string, PubSubOperationInfo, string, string)[]
    {
        ("Publish", PubSubOperationClassifier.Classify("PublishAsync", PubSubTopic, null, 1), Event, Producer),
        ("PublishBatch", PubSubOperationClassifier.Classify("PublishAsync", PubSubTopic, null, 3), Event, Producer),
        ("Pull", PubSubOperationClassifier.Classify("PullAsync", null, PubSubSubscription, 1), Event, Client),
        ("Receive", PubSubOperationClassifier.Classify("Receive", null, PubSubSubscription, 1), Event, Consumer),
        ("Acknowledge", PubSubOperationClassifier.Classify("AcknowledgeAsync", null, PubSubSubscription, 1), Default, Client),
        ("ModifyAckDeadline", PubSubOperationClassifier.Classify("ModifyAckDeadlineAsync", null, PubSubSubscription, 1), Default, Client),
    })
        Measure("pubsub", "PubSubTracker", operation, verbosity.ToString(), r2, expected, () =>
        {
            var (id, trace) = tracker.LogRequest(info, "content");
            tracker.LogResponse(info, id, trace, "answer");
        });
}

// ---------------------------------------------------------------------------- Service Bus

foreach (var verbosity in Enum.GetValues<ServiceBusTrackingVerbosity>())
{
    var tracker = new ServiceBusTracker(new ServiceBusTrackingOptions { ServiceName = "ServiceBus", CallerName = "App", Verbosity = verbosity, CurrentTestInfoFetcher = Current });
    foreach (var (method, r2, expected) in new[]
    {
        ("SendMessageAsync", Event, Producer), ("SendMessagesAsync", Event, Producer), ("ScheduleMessageAsync", Event, Producer),
        ("ReceiveMessageAsync", Event, Client), ("ReceiveMessagesAsync", Event, Client), ("PeekMessageAsync", Event, Client),
        ("CompleteMessageAsync", Default, Client), ("DeadLetterMessageAsync", Default, Client),
    })
    {
        var info = ServiceBusOperationClassifier.Classify(method, "orders", null);
        Measure("servicebus", "ServiceBusTracker", method.Replace("Async", ""), verbosity.ToString(), r2, expected, () =>
        {
            var (id, trace) = tracker.LogRequest(info, "content");
            tracker.LogResponse(info, id, trace, "answer");
        });
    }
}

// ---------------------------------------------------------------------------- Event Hubs

foreach (var verbosity in Enum.GetValues<EventHubsTrackingVerbosity>())
{
    var tracker = new EventHubsTracker(new EventHubsTrackingOptions { ServiceName = "EventHubs", CallerName = "App", Verbosity = verbosity, CurrentTestInfoFetcher = Current });
    foreach (var (operation, info, r2, expected) in new (string, EventHubsOperationInfo, string, string)[]
    {
        ("Send", EventHubsOperationClassifier.Classify("SendAsync", "orders", null, 1), Event, Producer),
        ("SendBatch", EventHubsOperationClassifier.Classify("SendAsync", "orders", null, 3), Event, Producer),
        ("ReadEvents", EventHubsOperationClassifier.Classify("ReadEventsAsync", "orders"), Event, Client),
        ("ReadEventsFromPartition", EventHubsOperationClassifier.Classify("ReadEventsFromPartitionAsync", "orders", "0"), Event, Client),
        ("ProcessEvent", EventHubsOperationClassifier.Classify("ProcessEvent", "orders", "0", 1), Event, Consumer),
        ("GetPartitionIds", EventHubsOperationClassifier.Classify("GetPartitionIdsAsync", "orders"), Default, Client),
    })
        Measure("eventhubs", "EventHubsTracker", operation, verbosity.ToString(), r2, expected, () =>
        {
            var (id, trace) = tracker.LogRequest(info, "content");
            tracker.LogResponse(info, id, trace, "answer");
        });
}

// ---------------------------------------------------------------------------- Kafka: each route that records a consume

foreach (var verbosity in Enum.GetValues<KafkaTrackingVerbosity>())
{
    KafkaTrackingOptions Options(bool processing = false) => new()
    {
        ServiceName = "Kafka", CallerName = "App", Verbosity = verbosity, CurrentTestInfoFetcher = Current,
        TrackCommit = true, TraceMessageProcessing = processing,
    };

    var tracker = new KafkaTracker(Options());
    var name = verbosity.ToString();
    Measure("kafka", "KafkaTracker.TrackProduce.Sent", "Produce", name, Event, Producer, () =>
        tracker.TrackProduce("orders", new Message<string, string> { Key = "k", Value = "v" }, KafkaOperation.Produce).Sent());
    Measure("kafka", "KafkaTracker.LogConsume (hand)", "Consume", name, Event, Consumer, () =>
        tracker.LogConsume(new KafkaOperationInfo(KafkaOperation.Consume, "orders", 0, 7), "v"));
    Measure("kafka", "KafkaTracker.TrackConsume (scope)", "Consume", name, Event, Consumer, () =>
    {
        using var scope = tracker.TrackConsume(Consumed(new Message<string, string> { Key = "k", Value = "v" }));
    });
    Measure("kafka", "TrackingKafkaConsumer.Consume (poll, default)", "Consume", name, Event, Consumer, () =>
    {
        var options = Options();
        new TrackingKafkaConsumer<string, string>(ScriptedConsumer.Returning(Consumed(new Message<string, string> { Key = "k", Value = "v" })), new KafkaTracker(options), options)
            .Consume(TimeSpan.Zero);
    });
    Measure("kafka", "TrackingKafkaConsumer.Consume (TraceMessageProcessing)", "Consume", name, Event, Consumer, () =>
    {
        var options = Options(processing: true);
        using var consumer = new TrackingKafkaConsumer<string, string>(ScriptedConsumer.Returning(Consumed(new Message<string, string> { Key = "k", Value = "v" })), new KafkaTracker(options), options);
        consumer.Consume(TimeSpan.Zero);
    });
    Measure("kafka", "KafkaTracker.LogCommit", "Commit", name, Default, Client, () => tracker.LogCommit(new KafkaOperationInfo(KafkaOperation.Commit)));
}

static ConsumeResult<string, string> Consumed(Message<string, string> message) => new()
{
    TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(0), new Offset(7)),
    Message = message,
};

// ---------------------------------------------------------------------------- MassTransit

foreach (var verbosity in Enum.GetValues<MassTransitTrackingVerbosity>())
{
    var tracker = new MassTransitTracker(new MassTransitTrackingOptions { ServiceName = "MassTransit", CallerName = "App", Verbosity = verbosity, CurrentTestInfoFetcher = Current });
    MassTransitOperationInfo On(MassTransitOperation member) =>
        new(member, "OrderPlaced", new Uri("rabbitmq://localhost/orders"), new Uri("rabbitmq://localhost/source"), Guid.NewGuid(), Guid.NewGuid());
    var name = verbosity.ToString();
    Measure("masstransit", "LogSend", "Send", name, Event, Producer, () => tracker.LogSend(On(MassTransitOperation.Send), new { Id = 1 }));
    Measure("masstransit", "LogPublish", "Publish", name, Event, Producer, () => tracker.LogPublish(On(MassTransitOperation.Publish), new { Id = 1 }));
    Measure("masstransit", "LogConsume", "Consume", name, Event, Consumer, () => tracker.LogConsume(On(MassTransitOperation.Consume), new { Id = 1 }));
    Measure("masstransit", "LogConsumeFault", "ConsumeFault", name, Default, Client, () => tracker.LogConsumeFault(On(MassTransitOperation.ConsumeFault), new InvalidOperationException("boom")));
}

// ---------------------------------------------------------------------------- the AWS handlers and Storage Queues, behind the real clients

var awsCredentials = new Amazon.Runtime.BasicAWSCredentials("AKIDEXAMPLE", "secret");
const string AwsAccount = "123456789012";

foreach (var verbosity in Enum.GetValues<SqsTrackingVerbosity>())
{
    var stub = new Stub(_ => Answer("{}", "application/x-amz-json-1.0"));
    var sqs = new AmazonSQSClient(awsCredentials, new AmazonSQSConfig
    {
        RegionEndpoint = Amazon.RegionEndpoint.USEast1,
        MaxErrorRetry = 0,
        HttpClientFactory = new AwsFactory(new SqsTrackingMessageHandler(new SqsTrackingMessageHandlerOptions { ServiceName = "SQS", CallerName = "App", Verbosity = verbosity, CurrentTestInfoFetcher = Current }, stub)),
    });
    var url = $"https://sqs.us-east-1.amazonaws.com/{AwsAccount}/orders";
    var name = verbosity.ToString();
    Measure("sqs", "SqsTrackingMessageHandler", "SendMessage", name, Event, Sends(name), () => sqs.SendMessageAsync(new SqsModel.SendMessageRequest { QueueUrl = url, MessageBody = "hello" }).GetAwaiter().GetResult());
    Measure("sqs", "SqsTrackingMessageHandler", "SendMessageBatch", name, Event, Sends(name), () => sqs.SendMessageBatchAsync(new SqsModel.SendMessageBatchRequest { QueueUrl = url, Entries = [new SqsModel.SendMessageBatchRequestEntry { Id = "1", MessageBody = "hello" }] }).GetAwaiter().GetResult());
    Measure("sqs", "SqsTrackingMessageHandler", "ReceiveMessage", name, Event, Client, () => sqs.ReceiveMessageAsync(new SqsModel.ReceiveMessageRequest { QueueUrl = url }).GetAwaiter().GetResult());
    Measure("sqs", "SqsTrackingMessageHandler", "DeleteMessage", name, Default, Client, () => sqs.DeleteMessageAsync(new SqsModel.DeleteMessageRequest { QueueUrl = url, ReceiptHandle = "rh" }).GetAwaiter().GetResult());
}

foreach (var verbosity in Enum.GetValues<SnsTrackingVerbosity>())
{
    var stub = new Stub(_ => Answer("<Ok/>", "text/xml"));
    var sns = new AmazonSimpleNotificationServiceClient(awsCredentials, new AmazonSimpleNotificationServiceConfig
    {
        RegionEndpoint = Amazon.RegionEndpoint.USEast1,
        MaxErrorRetry = 0,
        HttpClientFactory = new AwsFactory(new SnsTrackingMessageHandler(new SnsTrackingMessageHandlerOptions { ServiceName = "SNS", CallerName = "App", Verbosity = verbosity, CurrentTestInfoFetcher = Current }, stub)),
    });
    var arn = $"arn:aws:sns:us-east-1:{AwsAccount}:orders";
    var name = verbosity.ToString();
    Measure("sns", "SnsTrackingMessageHandler", "Publish", name, Event, Sends(name), () => sns.PublishAsync(new SnsModel.PublishRequest { TopicArn = arn, Message = "hello" }).GetAwaiter().GetResult());
    Measure("sns", "SnsTrackingMessageHandler", "PublishBatch", name, Event, Sends(name), () => sns.PublishBatchAsync(new SnsModel.PublishBatchRequest { TopicArn = arn, PublishBatchRequestEntries = [new SnsModel.PublishBatchRequestEntry { Id = "1", Message = "hello" }] }).GetAwaiter().GetResult());
    Measure("sns", "SnsTrackingMessageHandler", "Subscribe", name, Default, Client, () => sns.SubscribeAsync(new SnsModel.SubscribeRequest { TopicArn = arn, Protocol = "sqs", Endpoint = $"arn:aws:sqs:us-east-1:{AwsAccount}:orders" }).GetAwaiter().GetResult());
}

foreach (var verbosity in Enum.GetValues<EventBridgeTrackingVerbosity>())
{
    var stub = new Stub(_ => Answer("{}", "application/x-amz-json-1.1"));
    var eventBridge = new AmazonEventBridgeClient(awsCredentials, new AmazonEventBridgeConfig
    {
        RegionEndpoint = Amazon.RegionEndpoint.USEast1,
        MaxErrorRetry = 0,
        HttpClientFactory = new AwsFactory(new EventBridgeTrackingMessageHandler(new EventBridgeTrackingMessageHandlerOptions { ServiceName = "EventBridge", CallerName = "App", Verbosity = verbosity, CurrentTestInfoFetcher = Current, ExcludedOperations = [] }, stub)),
    });
    var name = verbosity.ToString();
    Measure("eventbridge", "EventBridgeTrackingMessageHandler", "PutEvents", name, Event, Sends(name), () => eventBridge.PutEventsAsync(new EbModel.PutEventsRequest { Entries = [new EbModel.PutEventsRequestEntry { EventBusName = "orders-bus", Source = "shop", DetailType = "OrderPlaced", Detail = "{}" }] }).GetAwaiter().GetResult());
    Measure("eventbridge", "EventBridgeTrackingMessageHandler", "PutPartnerEvents", name, Event, Sends(name), () => eventBridge.PutPartnerEventsAsync(new EbModel.PutPartnerEventsRequest { Entries = [new EbModel.PutPartnerEventsRequestEntry { Source = "shop", DetailType = "OrderPlaced", Detail = "{}" }] }).GetAwaiter().GetResult());
    Measure("eventbridge", "EventBridgeTrackingMessageHandler", "PutRule", name, Default, Client, () => eventBridge.PutRuleAsync(new EbModel.PutRuleRequest { Name = "orders-rule", EventBusName = "orders-bus", EventPattern = "{}" }).GetAwaiter().GetResult());
}

const string AzureAccount = "DefaultEndpointsProtocol=https;AccountName=acct;AccountKey=a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5;EndpointSuffix=core.windows.net";
foreach (var verbosity in Enum.GetValues<StorageQueueTrackingVerbosity>())
{
    var stub = new Stub(AzureQueueAnswer);
    var options = new QueueClientOptions().WithTestTracking(new StorageQueueTrackingMessageHandlerOptions { ServiceName = "StorageQueues", CallerName = "App", Verbosity = verbosity, CurrentTestInfoFetcher = Current }, stub);
    options.Retry.MaxRetries = 0;
    var queue = new QueueClient(AzureAccount, "orders", options);
    var name = verbosity.ToString();
    Measure("storagequeue", "StorageQueueTrackingMessageHandler", "SendMessage", name, Event, Sends(name), () => queue.SendMessage("hello"));
    Measure("storagequeue", "StorageQueueTrackingMessageHandler", "ReceiveMessages", name, Event, Client, () => queue.ReceiveMessages());
    Measure("storagequeue", "StorageQueueTrackingMessageHandler", "PeekMessages", name, Event, Client, () => queue.PeekMessages());
    Measure("storagequeue", "StorageQueueTrackingMessageHandler", "DeleteMessage", name, Default, Client, () => queue.DeleteMessage("m1", "pop1"));
    Measure("storagequeue", "StorageQueueTrackingMessageHandler", "UpdateMessage", name, Default, Client, () => queue.UpdateMessage("m1", "pop1", "changed"));
}

// ---------------------------------------------------------------------------- the core MessageTracker

var messages = new MessageTracker(new MessageTrackerOptions { ServiceName = "Broker", CallerName = "Caller", Verbosity = MessageTrackerVerbosity.Detailed, CurrentTestInfoFetcher = Current });
foreach (var (shape, send, consume, consumer, uri) in new[]
{
    ("event://", "Publish", "Consume", "OrderService", new Uri("event://cake_events")),
    ("kafka://", "Kafka", "Consume (Kafka)", "OrderService", new Uri("kafka:///orders")),
    ("kafka:// with a free-text consume label", "Kafka", "Kafka", "OrderService", new Uri("kafka:///orders")),
    ("https:// with a free-text label", "Publish", "Consume", "OrderService", new Uri("https://events.example/api/events")),
})
{
    Measure("core", "MessageTracker.TrackSendEvent", shape, "Detailed", Event, Producer, () => messages.TrackSendEvent(send, "Broker", uri, new { Id = 1 }));
    Measure("core", "MessageTracker.TrackSendMessage", shape, "Detailed", Event, Producer, () => messages.TrackSendMessage(send, "Broker", uri, new { Id = 1 }));
    Measure("core", "MessageTracker.TrackConsumeEvent", shape, "Detailed", Event, Consumer, () => messages.TrackConsumeEvent(consume, consumer, uri, new { Id = 1 }));
}

// A consume hand-logged on a real scheme: the broker's arrow into the consumer, whatever the scheme's own tracker does. The first two are
// BreakfastProvider's (Fakes/PubSub/InMemoryCustomerFeedbackConsumerService.cs:71-75, Fakes/EventHub/InMemoryEventHubEquipmentAlertConsumerService.cs:77).
foreach (var (shape, consume, uri) in new[]
{
    ("pubsub://", "Consume (Pub/Sub)", new Uri("pubsub:///orders-sub")),
    ("eventhub://", "Consume (Event Hub)", new Uri("eventhub:///orders")),
    ("eventhubs://", "Consume (Event Hub)", new Uri("eventhubs:///orders")),
    ("pubsub:// with its tracker's own label", "Receive", new Uri("pubsub:///orders-sub")),
    ("eventhubs:// with its tracker's own label", "Process \u2190 orders", new Uri("eventhubs:///orders")),
    ("masstransit://", "Consume (MassTransit)", new Uri("masstransit:///orders")),
    ("servicebus://", "Consume (Service Bus)", new Uri("servicebus://orders/")),
    ("sqs://", "Consume (SQS)", new Uri("sqs:///orders")),
    ("storagequeue://", "Consume (Queue)", new Uri("storagequeue:///orders")),
})
    Measure("core", "MessageTracker.TrackConsumeEvent", shape, "Detailed", Event, Consumer, () => messages.TrackConsumeEvent(consume, "OrderService", uri, new { Id = 1 }));

// A send whose label is its author's own words, on any scheme: a send. Publish (...) is BreakfastProvider's own (Fakes/PubSub/TrackedPubSubEventPublisher.cs,
// EventHub/TrackedEventHubEventPublisher.cs, EventGrid/TrackedEventGridPublisher.cs), and Consume (Kafka) its Fakes/Kafka/TrackedKafkaMessageStore.cs:39, a class nothing creates.
foreach (var (shape, send, destination, uri) in new[]
{
    ("event:// with a Process label", "Process order", "Orders", new Uri("event://orders")),
    ("event:// with a Consume label", "Consume", "Orders", new Uri("event://orders")),
    ("event:// with a ProcessOrder label", "ProcessOrder", "Orders", new Uri("event://orders")),
    ("https:// with a Process label", "Process", "Payments", new Uri("https://payments.example/process")),
    ("event:// with a Receive label", "Receive receipt", "Billing", new Uri("event://receipts")),
    ("event:// with a PollResult label", "PollResult", "Billing", new Uri("event://receipts")),
    ("kafka:// with a Process label", "Process order", "Kafka", new Uri("kafka:///orders")),
    ("kafka:// with its tracker's consume label", "Consume (Kafka)", "OrderService", new Uri("kafka:///orders")),
    ("masstransit:// with its tracker's consume label", "Consume OrderPlaced", "Bus", new Uri("masstransit:///orders")),
    ("pubsub:// with its tracker's receive label", "Receive order", "PubSub", new Uri("pubsub:///orders")),
    ("pubsub:// with its tracker's ack label", "Ack order", "PubSub", new Uri("pubsub:///orders")),
    ("eventhubs:// with its tracker's process label", "Process order", "Hub", new Uri("eventhubs:///orders")),
    ("servicebus:// with its tracker's peek label", "Peek order", "Queue", new Uri("servicebus://orders/")),
    ("sqs:// with its tracker's receive label", "ReceiveMessage", "Queue", new Uri("sqs:///orders")),
    ("pubsub:// (BreakfastProvider)", "Publish (Pub/Sub)", "PubSub", new Uri("pubsub:///OrderPlaced")),
    ("eventhub:// (BreakfastProvider)", "Publish (Event Hub)", "EventHub", new Uri("eventhub:///OrderPlaced")),
    ("eventgrid:// (BreakfastProvider)", "Publish (Event Grid)", "EventGrid", new Uri("eventgrid://topics/OrderPlaced")),
})
{
    Measure("core", "MessageTracker.TrackSendEvent", shape, "Detailed", Event, Producer, () => messages.TrackSendEvent(send, destination, uri, new { Id = 1 }));
    Measure("core", "MessageTracker.TrackMessageRequest+Response", shape, "Detailed", Event, Producer, () =>
        messages.TrackMessageResponse(send, destination, uri, messages.TrackMessageRequest(send, destination, uri, new { Id = 1 })));
}

// A log built by hand and given Event (BreakfastProvider's Fakes/Tracking/EventPublishingTracker.cs, Tracking-Custom-Dependencies.md:331).
foreach (var (shape, label, uri, expected) in new[]
{
    ("eventgrid:// (BreakfastProvider)", "Publish (Event Grid)", new Uri("eventgrid://topics/OrderPlaced"), Producer),
    ("event:// with a Process label", "Process order", new Uri("event://orders"), Producer),
    ("https:// with a Consume label", "Consume", new Uri("https://payments.example/process"), Producer),
    // Its author wrote exactly what the scheme's tracker writes, so nothing tells it from that tracker's record.
    ("kafka:// with its tracker's consume label", "Consume \u2190 orders", new Uri("kafka:///orders"), Consumer),
})
    Measure("core", "new RequestResponseLog (by hand)", shape, "Detailed", Event, expected, () =>
    {
        var id = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;
        RequestResponseLogger.Log(new RequestResponseLog("Test", currentTest, label, null, uri, [], "Broker", "App", RequestResponseType.Request, id, id, false,
            MetaType: RequestResponseMetaType.Event) { Timestamp = at });
        RequestResponseLogger.Log(new RequestResponseLog("Test", currentTest, label, null, uri, [], "Broker", "App", RequestResponseType.Response, id, id, false,
            MetaType: RequestResponseMetaType.Event) { Timestamp = at.AddMilliseconds(1) });
    });

// ---------------------------------------------------------------------------- the file

string[] header =
[
    "broker", "route", "operation", "verbosity", "label", "uriScheme", "tableVerdict", "r1Kind", "r2Kind", "literal5.5", "firstCut", "expected", "literalOk", "firstCutOk",
    "service.name", "peer.service", "messaging.system", "messaging.operation.name", "messaging.operation.type", "messaging.destination.name", "http.request.method", "viaNdjson", "throughTheTap",
    "viaNdjsonServiceName", "today", "r3Alone",
];
var outPath = Path.GetFullPath(args[0]);
Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
File.WriteAllText(outPath, string.Join('\n', new[] { string.Join('\t', header) }.Concat(rows.Select(row => string.Join('\t', row)))) + "\n", new UTF8Encoding(false));

Console.WriteLine($"{rows.Count} rows, {outPath}");
var wrongLiteral = rows.Where(r => r[12] == "WRONG").ToArray();
var wrongCut = rows.Where(r => r[13] == "WRONG").ToArray();
Console.WriteLine($"Section 5.5 read literally: {rows.Count - wrongLiteral.Length} right, {wrongLiteral.Length} wrong");
foreach (var row in wrongLiteral)
    Console.WriteLine($"  {row[0],-13} {row[1],-52} {row[2],-24} {row[3],-11} label '{row[4]}': says {row[9]}, is {row[11]}");
Console.WriteLine($"First cut: {rows.Count - wrongCut.Length} right, {wrongCut.Length} wrong");
foreach (var row in wrongCut)
    Console.WriteLine($"  {row[0],-13} {row[1],-52} {row[2],-24} {row[3],-11} label '{row[4]}': says {row[10]}, is {row[11]}");
Console.WriteLine("Rows where NDJSON gives another kind than in process, the service.name each way:");
foreach (var row in rows.Where(r => r[21] != "same"))
    Console.WriteLine($"  {row[0],-13} {row[1],-52} {row[2],-48} {row[3],-11} in process {row[10]} {row[14]}, through NDJSON {row[21]} {row[23]}");
Console.WriteLine("Rows where NDJSON gives the same kind and another service.name:");
foreach (var row in rows.Where(r => r[21] == "same" && r[23] != r[14]))
    Console.WriteLine($"  {row[0],-13} {row[1],-52} {row[2],-48} {row[3],-11} {row[10]}: in process {row[14]}, through NDJSON {row[23]}");
Console.WriteLine("R3 alone: each record as its tracker wrote it, with R2 not applied, against today's kind and the expected column:");
foreach (var group in rows.Where(r => r[24] != r[25]).GroupBy(r => $"{r[24]} -> {r[25]}, expected {r[11]}").OrderBy(g => g.Key))
    Console.WriteLine($"  {group.Key}: {group.Count()} ({string.Join(", ", group.Select(r => r[0]).Distinct())})");
var regressed = rows.Where(r => r[24] == r[11] && r[25] != r[11]).ToArray();
Console.WriteLine($"  right today and wrong on R3 alone: {regressed.Length}");
foreach (var row in regressed)
    Console.WriteLine($"    {row[0],-13} {row[1],-52} {row[2],-40} {row[3],-11} label '{row[4]}': today {row[24]}, R3 alone {row[25]}");
Console.WriteLine("The tap, with Messaging captured, on each exported message span:");
foreach (var group in rows.GroupBy(r => r[22].StartsWith("DROPPED") ? "dropped" : r[22][..r[22].IndexOf(':')]))
    Console.WriteLine($"  {group.Key}: {group.Count()}");

foreach (var failure in failures)
    Console.Error.WriteLine(failure);
return failures.Count == 0 ? 0 : 1;

static HttpResponseMessage Answer(string body, string mediaType) =>
    new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

static HttpResponseMessage AzureQueueAnswer(HttpRequestMessage request)
{
    var uri = request.RequestUri!;
    var status = request.Method.Method switch
    {
        "POST" => System.Net.HttpStatusCode.Created,
        "PUT" when uri.AbsolutePath.Contains("/messages/") || uri.Query.Contains("comp=") => System.Net.HttpStatusCode.NoContent,
        "PUT" => System.Net.HttpStatusCode.Created,
        "DELETE" => System.Net.HttpStatusCode.NoContent,
        _ => System.Net.HttpStatusCode.OK,
    };
    return new HttpResponseMessage(status) { Content = new StringContent("") };
}

sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(answer(request));
}

sealed class AwsFactory(HttpMessageHandler handler) : Amazon.Runtime.HttpClientFactory
{
    public override HttpClient CreateHttpClient(Amazon.Runtime.IClientConfig clientConfig) => new(handler, disposeHandler: false);
}

// The one member the wrapper reads of its inner consumer here is Consume.
class ScriptedConsumer : DispatchProxy
{
    private object? _next;

    public static IConsumer<string, string> Returning(ConsumeResult<string, string> result)
    {
        var proxy = DispatchProxy.Create<IConsumer<string, string>, ScriptedConsumer>();
        ((ScriptedConsumer)(object)proxy)._next = result;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name == "Consume" ? _next : targetMethod?.ReturnType is { IsValueType: true } type && type != typeof(void) ? Activator.CreateInstance(type) : null;
}
