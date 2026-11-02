// What the warm-up rule marks on the shapes a real suite has, driven through the real trackers and handlers: a consumer in a
// hosted service settling messages, a test publishing to a broker itself, each with a slow first call and fast later ones.
// WarmUpCalls.Find and MessageTable are internal, so they are reached by reflection, as warm-up-replay does.
//
// Each shape prints three things:
// - calls: the requests of its operation that the shape made, and how many of them have a duration (a record pair logged at
//   one instant has none, and WarmUpCalls.Judge says nothing of a shape holding such a call);
// - marked: the marks WarmUpCalls.Find gives on the records as the build wrote them;
// - publishes judged: the same over those records with every Event the table reads as Publishes read as Default, which is what a
//   warm-up test of "an event, unless the table says it publishes" would see (plans/CALLS_AND_EVENTS_PLAN.md Q5).
//
// Timing is real: a first call sleeps 500 ms, later ones do not.
//
// Usage, from the harness directory, on the published 5.27.13: dotnet run --file probes/warm-up-shapes.cs
// On a worktree holding prototype/r1-first-cut.patch and r2-first-cut.patch: probes/on-prototype.sh <worktree> probes/warm-up-shapes.cs <scratch dir>
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

using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using Amazon.EventBridge;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
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
using Kronikol.Reports;
using Kronikol.Tracking;
using EbModel = Amazon.EventBridge.Model;
using SnsModel = Amazon.SimpleNotificationService.Model;
using SqsModel = Amazon.SQS.Model;

const int FirstMs = 500;
const int Calls = 5;
const string Worker = "OrderWorker";

var core = typeof(RequestResponseLog).Assembly;
Console.WriteLine($"Kronikol {core.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
var find = core.GetType("Kronikol.Reports.WarmUpCalls", throwOnError: true)!
    .GetMethod("Find", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
var read = core.GetType("Kronikol.CallGraph.MessageTable", throwOnError: true)!
    .GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!;
var schemeOf = core.GetType("Kronikol.CallGraph.MessageTable", throwOnError: true)!
    .GetMethod("Scheme", BindingFlags.Static | BindingFlags.NonPublic)!;

var currentTest = "";
(string Name, string Id) Current() => ("warm-up shapes", currentTest);

string Marks(Feature[] features, RequestResponseLog[] logs)
{
    var result = find.Invoke(null, [features, logs, null])!;
    var calls = (IDictionary)result.GetType().GetProperty("Calls", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(result)!;
    var shapes = new List<string>();
    foreach (DictionaryEntry pair in calls)
    {
        var mark = pair.Value!;
        var kind = (string)mark.GetType().GetProperty("Kind")!.GetValue(mark)!;
        var shape = (string)mark.GetType().GetProperty("Shape")!.GetValue(mark)!;
        shapes.Add($"{kind} {shape}");
    }
    shapes.Sort(StringComparer.Ordinal);
    return shapes.Count == 0 ? "none" : string.Join("; ", shapes);
}

bool IsPublish(RequestResponseLog log) =>
    (int)read.Invoke(null, [(string?)schemeOf.Invoke(null, [log.Uri.ToString()]), log.Method.Value?.ToString()])! == 0; // MessageLabelKind.Publishes

void Shape(string name, Action drive)
{
    currentTest = Guid.NewGuid().ToString("N");
    drive();
    var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == currentTest).ToArray();
    var features = new[]
    {
        new Feature { DisplayName = "Probe", Scenarios = [new Scenario { Id = currentTest, DisplayName = name, Duration = TimeSpan.FromSeconds(30) }] }
    };
    var requests = logs.Where(l => l.Type == RequestResponseType.Request).ToArray();
    var measured = requests.Count(r =>
        logs.FirstOrDefault(l => l.Type == RequestResponseType.Response && l.RequestResponseId == r.RequestResponseId) is { Timestamp: { } end }
        && r.Timestamp is { } start && end > start);
    var kinds = string.Join(" ", requests.GroupBy(r => r.MetaType).OrderBy(g => g.Key.ToString()).Select(g => $"{g.Key}={g.Count()}"));
    var asWritten = Marks(features, logs);
    var publishesJudged = Marks(features, logs
        .Select(l => l.MetaType == RequestResponseMetaType.Event && IsPublish(l) ? l with { MetaType = RequestResponseMetaType.Default } : l)
        .ToArray());
    Console.WriteLine($"{name}\n    calls {requests.Length}, measured {measured}, kinds {kinds}\n    marked: {asWritten}\n    publishes judged: {publishesJudged}");
}

// A hand-called tracker: request, the call's time, response.
void SlowFirst(Func<(Guid Id, Guid Trace)> request, Action<Guid, Guid> response)
{
    for (var i = 0; i < Calls; i++)
    {
        var (id, trace) = request();
        Thread.Sleep(i == 0 ? FirstMs : 0);
        response(id, trace);
        Thread.Sleep(1);
    }
}

// The HTTP call into the worker a test makes, which makes the worker a service of the scenario (WarmUpCalls.cs:155).
void CallIntoWorker()
{
    var (trace, id) = (Guid.NewGuid(), Guid.NewGuid());
    RequestResponseLogger.Log(new RequestResponseLog(Current().Name, currentTest, HttpMethod.Post, null, new Uri("http://worker/orders"), [],
        Worker, "Caller", RequestResponseType.Request, trace, id, false));
    RequestResponseLogger.Log(new RequestResponseLog(Current().Name, currentTest, HttpMethod.Post, null, new Uri("http://worker/orders"), [],
        Worker, "Caller", RequestResponseType.Response, trace, id, false, HttpStatusCode.OK));
}

// ---------------------------------------------------------------------------- a consumer in a hosted service

var kafkaWorker = new KafkaTracker(new KafkaTrackingOptions { ServiceName = "Kafka", CallerName = Worker, CurrentTestInfoFetcher = Current, TrackCommit = true });
Shape("Kafka: hosted consumer, delivery recorded, commit tracked, slow first commit", () =>
{
    kafkaWorker.LogConsume(new KafkaOperationInfo(KafkaOperation.Consume, "orders", 0, 1), "body");
    for (var i = 0; i < Calls; i++)
    {
        Thread.Sleep(i == 0 ? FirstMs : 0);
        // The wrapper logs after Confluent returns (TrackingKafkaConsumer.Commit), so the time is spent before this line.
        kafkaWorker.LogCommit(new KafkaOperationInfo(KafkaOperation.Commit));
    }
});

var kafkaCommitOnly = new KafkaTracker(new KafkaTrackingOptions { ServiceName = "Kafka", CallerName = Worker, CurrentTestInfoFetcher = Current, TrackCommit = true, TrackConsume = false });
Shape("Kafka: hosted consumer, delivery not recorded, commit tracked, slow first commit", () =>
{
    for (var i = 0; i < Calls; i++)
    {
        Thread.Sleep(i == 0 ? FirstMs : 0);
        kafkaCommitOnly.LogCommit(new KafkaOperationInfo(KafkaOperation.Commit));
    }
});

var pubSubWorker = new PubSubTracker(new PubSubTrackingOptions { ServiceName = "Pub/Sub", CallerName = Worker, CurrentTestInfoFetcher = Current });
var received = new PubSubOperationInfo(PubSubOperation.Receive, null, "projects/p/subscriptions/orders", 1);
var acked = new PubSubOperationInfo(PubSubOperation.Acknowledge, null, "projects/p/subscriptions/orders", 1);
Shape("Pub/Sub: hosted subscriber through the wrapper's route, slow first handler (Receive, the reply is on its response)", () =>
    SlowFirst(() => pubSubWorker.LogRequest(received, "body"), (id, trace) => pubSubWorker.LogResponse(received, id, trace, null)));
Shape("Pub/Sub: Ack logged by hand (no wrapper logs one), slow first, no call into the worker", () =>
    SlowFirst(() => pubSubWorker.LogRequest(acked, null), (id, trace) => pubSubWorker.LogResponse(acked, id, trace, null)));
Shape("Pub/Sub: Ack logged by hand, slow first, the test calls the worker", () =>
{
    CallIntoWorker();
    SlowFirst(() => pubSubWorker.LogRequest(acked, null), (id, trace) => pubSubWorker.LogResponse(acked, id, trace, null));
});

var eventHubsWorker = new EventHubsTracker(new EventHubsTrackingOptions { ServiceName = "Event Hubs", CallerName = Worker, CurrentTestInfoFetcher = Current });
var partitions = new EventHubsOperationInfo(EventHubsOperation.GetPartitionIds, "orders");
Shape("Event Hubs: GetPartitionIds logged by hand (the wrappers pass it through unlogged), slow first", () =>
    SlowFirst(() => eventHubsWorker.LogRequest(partitions, null), (id, trace) => eventHubsWorker.LogResponse(partitions, id, trace, null)));

var massTransit = new MassTransitTracker(new MassTransitTrackingOptions { ServiceName = "RabbitMQ", CallerName = Worker, CurrentTestInfoFetcher = Current });
Shape("MassTransit: send faults from the observer, a slow first attempt before the fault is reported", () =>
{
    for (var i = 0; i < Calls; i++)
    {
        Thread.Sleep(i == 0 ? FirstMs : 0);
        massTransit.LogSendFault(new MassTransitOperationInfo(MassTransitOperation.SendFault, "OrderPlaced", new Uri("rabbitmq://localhost/orders")), new InvalidOperationException("closed"));
    }
});

// ---------------------------------------------------------------------------- a test publishing to a broker itself

var credentials = new Amazon.Runtime.BasicAWSCredentials("AKIDEXAMPLE", "secret");
const string QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/orders";
const string TopicArn = "arn:aws:sns:us-east-1:123456789012:orders";

var sqsStub = new SlowFirstStub(_ => Answer("{}", "application/x-amz-json-1.0"));
var sqs = new AmazonSQSClient(credentials, new AmazonSQSConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new SqsTrackingMessageHandler(new SqsTrackingMessageHandlerOptions { ServiceName = "SQS", CurrentTestInfoFetcher = Current }, sqsStub)),
});
var snsStub = new SlowFirstStub(_ => Answer("<Ok/>", "text/xml"));
var sns = new AmazonSimpleNotificationServiceClient(credentials, new AmazonSimpleNotificationServiceConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new SnsTrackingMessageHandler(new SnsTrackingMessageHandlerOptions { ServiceName = "SNS", CurrentTestInfoFetcher = Current }, snsStub)),
});
var snsWorker = new AmazonSimpleNotificationServiceClient(credentials, new AmazonSimpleNotificationServiceConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new SnsTrackingMessageHandler(new SnsTrackingMessageHandlerOptions { ServiceName = "SNS", CallerName = Worker, CurrentTestInfoFetcher = Current }, snsStub)),
});
var ebStub = new SlowFirstStub(_ => Answer("{}", "application/x-amz-json-1.1"));
var eventBridge = new AmazonEventBridgeClient(credentials, new AmazonEventBridgeConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new EventBridgeTrackingMessageHandler(new EventBridgeTrackingMessageHandlerOptions { ServiceName = "EventBridge", CurrentTestInfoFetcher = Current }, ebStub)),
});
var queuesStub = new SlowFirstStub(request => new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK) { Content = new StringContent("") });
var queuesOptions = new QueueClientOptions().WithTestTracking(new StorageQueueTrackingMessageHandlerOptions { ServiceName = "Storage Queues", CurrentTestInfoFetcher = Current }, queuesStub);
queuesOptions.Retry.MaxRetries = 0;
var queue = new QueueClient("DefaultEndpointsProtocol=https;AccountName=acct;AccountKey=a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5;EndpointSuffix=core.windows.net", "orders", queuesOptions);

// The call's result is not read: an SDK that cannot parse the stub's answer throws after the handler logged the call.
void Sdk(SlowFirstStub stub, Func<Task> call) => Repeated(() => stub.Arm(FirstMs), call);

void Repeated(Action arm, Func<Task> call)
{
    arm();
    for (var i = 0; i < Calls; i++)
    {
        try
        {
            call().GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is not TargetInvocationException)
        {
        }
        Thread.Sleep(1);
    }
}

Shape("SNS: the test publishes itself, slow first (TLS, a first connection)", () => Sdk(snsStub, () => sns.PublishAsync(new SnsModel.PublishRequest { TopicArn = TopicArn, Message = "hello" })));
Shape("SQS: the test sends itself, slow first", () => Sdk(sqsStub, () => sqs.SendMessageAsync(new SqsModel.SendMessageRequest { QueueUrl = QueueUrl, MessageBody = "hello" })));
Shape("SQS: the test long-polls for a message, the first waits 500 ms for it, later ones find one", () => Sdk(sqsStub, () => sqs.ReceiveMessageAsync(new SqsModel.ReceiveMessageRequest { QueueUrl = QueueUrl, WaitTimeSeconds = 1 })));
Shape("SQS: the test deletes messages, slow first (settlement, Default before and after)", () => Sdk(sqsStub, () => sqs.DeleteMessageAsync(new SqsModel.DeleteMessageRequest { QueueUrl = QueueUrl, ReceiptHandle = "rh" })));
Shape("EventBridge: the test puts events, slow first", () => Sdk(ebStub, () => eventBridge.PutEventsAsync(new EbModel.PutEventsRequest { Entries = [new EbModel.PutEventsRequestEntry { EventBusName = "orders-bus", Source = "shop", DetailType = "OrderPlaced", Detail = "{}" }] })));
Shape("Storage Queues: the test sends, slow first", () => Sdk(queuesStub, () => queue.SendMessageAsync("hello")));
Shape("Storage Queues: the test peeks, slow first", () => Sdk(queuesStub, () => queue.PeekMessagesAsync()));
Shape("SNS: the service publishes while the test calls it (the second condition of WarmUpCalls.cs:155)", () =>
{
    CallIntoWorker();
    Sdk(snsStub, () => snsWorker.PublishAsync(new SnsModel.PublishRequest { TopicArn = TopicArn, Message = "hello" }));
});
Shape("SNS: a worker publishes in a scenario that never calls it, slow first", () => Sdk(snsStub, () => snsWorker.PublishAsync(new SnsModel.PublishRequest { TopicArn = TopicArn, Message = "hello" })));

var kafkaTest = new KafkaTracker(new KafkaTrackingOptions { ServiceName = "Kafka", CurrentTestInfoFetcher = Current });
Shape("Kafka: the test produces itself with a delivery report, slow first (metadata, a first connection)", () =>
{
    for (var i = 0; i < Calls; i++)
    {
        var tracking = kafkaTest.TrackProduce("orders", new Message<string, string> { Key = "k", Value = "v" }, KafkaOperation.ProduceAsync);
        Thread.Sleep(i == 0 ? FirstMs : 0);
        tracking.Delivered(new DeliveryResult<string, string> { Topic = "orders", Partition = 0, Offset = i, Message = tracking.Message });
        Thread.Sleep(1);
    }
});

var serviceBus = new ServiceBusTracker(new ServiceBusTrackingOptions { ServiceName = "Service Bus", CurrentTestInfoFetcher = Current });
var send = new ServiceBusOperationInfo(ServiceBusOperation.Send, "orders");
Shape("Service Bus: the test sends itself, slow first", () =>
    SlowFirst(() => serviceBus.LogRequest(send, "body"), (id, trace) => serviceBus.LogResponse(send, id, trace, null)));

// The shape R2 would make of a hand-logged Ack, as it stands today: TrackingServiceBusReceiver logs a Complete with a real duration at Default.
var serviceBusWorker = new ServiceBusTracker(new ServiceBusTrackingOptions { ServiceName = "Service Bus", CallerName = Worker, CurrentTestInfoFetcher = Current });
var complete = new ServiceBusOperationInfo(ServiceBusOperation.Complete, "orders");
Shape("Service Bus: hosted receiver completes (the wrapper logs this, Default today), slow first, no call into the worker", () =>
    SlowFirst(() => serviceBusWorker.LogRequest(complete, null), (id, trace) => serviceBusWorker.LogResponse(complete, id, trace, null)));

// ---------------------------------------------------------------------------- the other publishes Q5 (b) judges

var slowPublisher = new SlowFirstPublisher();
var publisher = new TrackingPublisherClient(slowPublisher, new PubSubTrackingOptions { ServiceName = "Pub/Sub", CurrentTestInfoFetcher = Current });
Shape("Pub/Sub: the test publishes itself through the wrapper, slow first", () =>
    Repeated(() => slowPublisher.Arm(FirstMs), () => publisher.PublishAsync(new PubsubMessage { Data = ByteString.CopyFromUtf8("hello") })));

var slowProducer = new SlowFirstProducer();
var producer = new TrackingEventHubProducerClient(slowProducer, new EventHubsTrackingOptions { ServiceName = "Event Hubs", CurrentTestInfoFetcher = Current });
Shape("Event Hubs: the test sends itself through the wrapper, slow first", () =>
    Repeated(() => slowProducer.Arm(FirstMs), () => producer.SendAsync([new EventData("hello")])));

// MassTransit's observers log both records of a call together, after the transport returned (TrackingSendObserver.PostSend,
// TrackingPublishObserver.PostPublish), so the time is spent before the line that logs.
void MassTransitOutgoing(Action log)
{
    for (var i = 0; i < Calls; i++)
    {
        Thread.Sleep(i == 0 ? FirstMs : 0);
        log();
        Thread.Sleep(1);
    }
}

var massTransitTest = new MassTransitTracker(new MassTransitTrackingOptions { ServiceName = "RabbitMQ", CurrentTestInfoFetcher = Current });
var address = new Uri("rabbitmq://localhost/orders");
var published = new MassTransitOperationInfo(MassTransitOperation.Publish, "OrderPlaced", address);
var sent = new MassTransitOperationInfo(MassTransitOperation.Send, "OrderPlaced", address);
Shape("MassTransit: the test publishes itself, slow first", () => MassTransitOutgoing(() => massTransitTest.LogPublish(published, new { Id = 1 })));
Shape("MassTransit: the test sends itself, slow first", () => MassTransitOutgoing(() => massTransitTest.LogSend(sent, new { Id = 1 })));

// ---------------------------------------------------------------------------- where the table cannot read the label

var queuesRawOptions = new QueueClientOptions().WithTestTracking(new StorageQueueTrackingMessageHandlerOptions { ServiceName = "Storage Queues", Verbosity = StorageQueueTrackingVerbosity.Raw, CurrentTestInfoFetcher = Current }, queuesStub);
queuesRawOptions.Retry.MaxRetries = 0;
var queueRaw = new QueueClient("DefaultEndpointsProtocol=https;AccountName=acct;AccountKey=a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5;EndpointSuffix=core.windows.net", "orders", queuesRawOptions);
Shape("Storage Queues at Raw: the test sends, slow first (POST on https)", () => Sdk(queuesStub, () => queueRaw.SendMessageAsync("hello")));
Shape("Storage Queues at Raw: the test receives, slow first (GET on https)", () => Sdk(queuesStub, () => queueRaw.ReceiveMessagesAsync()));

var sqsRaw = new AmazonSQSClient(credentials, new AmazonSQSConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new SqsTrackingMessageHandler(new SqsTrackingMessageHandlerOptions { ServiceName = "SQS", Verbosity = SqsTrackingVerbosity.Raw, CurrentTestInfoFetcher = Current }, sqsStub)),
});
var snsRaw = new AmazonSimpleNotificationServiceClient(credentials, new AmazonSimpleNotificationServiceConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new SnsTrackingMessageHandler(new SnsTrackingMessageHandlerOptions { ServiceName = "SNS", Verbosity = SnsTrackingVerbosity.Raw, CurrentTestInfoFetcher = Current }, snsStub)),
});
var eventBridgeRaw = new AmazonEventBridgeClient(credentials, new AmazonEventBridgeConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new EventBridgeTrackingMessageHandler(new EventBridgeTrackingMessageHandlerOptions { ServiceName = "EventBridge", Verbosity = EventBridgeTrackingVerbosity.Raw, CurrentTestInfoFetcher = Current }, ebStub)),
});
Shape("SQS at Raw: the test sends, slow first (POST on https)", () => Sdk(sqsStub, () => sqsRaw.SendMessageAsync(new SqsModel.SendMessageRequest { QueueUrl = QueueUrl, MessageBody = "hello" })));
Shape("SNS at Raw: the test publishes, slow first (POST on https)", () => Sdk(snsStub, () => snsRaw.PublishAsync(new SnsModel.PublishRequest { TopicArn = TopicArn, Message = "hello" })));
Shape("EventBridge at Raw: the test puts events, slow first (POST on https)", () => Sdk(ebStub, () => eventBridgeRaw.PutEventsAsync(new EbModel.PutEventsRequest { Entries = [new EbModel.PutEventsRequestEntry { EventBusName = "orders-bus", Source = "shop", DetailType = "OrderPlaced", Detail = "{}" }] })));

var massTransitRaw = new MassTransitTracker(new MassTransitTrackingOptions { ServiceName = "RabbitMQ", Verbosity = MassTransitTrackingVerbosity.Raw, CurrentTestInfoFetcher = Current });
Shape("MassTransit at Raw: the test publishes on rabbitmq://, slow first", () => MassTransitOutgoing(() => massTransitRaw.LogPublish(published, new { Id = 1 })));

// ---------------------------------------------------------------------------- the generic tracker

var generic = new MessageTracker(new MessageTrackerOptions { ServiceName = "Bus", CurrentTestInfoFetcher = Current });
void GenericPublish(Uri uri)
{
    for (var i = 0; i < Calls; i++)
    {
        var request = generic.TrackMessageRequest("Publish", "Bus", uri, new { Id = 1 });
        Thread.Sleep(i == 0 ? FirstMs : 0);
        generic.TrackMessageResponse("Publish", "Bus", uri, request);
        Thread.Sleep(1);
    }
}

Shape("MessageTracker: the test publishes on its own scheme, event://, slow first", () => GenericPublish(new Uri("event://orders")));
Shape("MessageTracker: the test publishes on a relative address, slow first", () => GenericPublish(new Uri("orders", UriKind.Relative)));

return 0;

static HttpResponseMessage Answer(string body, string mediaType) =>
    new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType) };

// Answers every request, and holds the first after it is armed for that long.
sealed class SlowFirstStub(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    private int _firstMs;

    public void Arm(int ms) => _firstMs = ms;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var wait = Interlocked.Exchange(ref _firstMs, 0);
        if (wait > 0)
            await Task.Delay(wait, cancellationToken);
        return answer(request);
    }
}

// The wrappers' inner clients, each holding the first call after it is armed.
sealed class SlowFirstPublisher : PublisherClient
{
    private int _firstMs;

    public override TopicName TopicName { get; } = new("shop", "orders");

    public void Arm(int ms) => _firstMs = ms;

    public override async Task<string> PublishAsync(PubsubMessage message)
    {
        var wait = Interlocked.Exchange(ref _firstMs, 0);
        if (wait > 0)
            await Task.Delay(wait);
        return "message-1";
    }
}

sealed class SlowFirstProducer() : EventHubProducerClient("Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==;EntityPath=orders")
{
    private int _firstMs;

    public void Arm(int ms) => _firstMs = ms;

    public override async Task SendAsync(IEnumerable<EventData> eventBatch, CancellationToken cancellationToken = default)
    {
        var wait = Interlocked.Exchange(ref _firstMs, 0);
        if (wait > 0)
            await Task.Delay(wait, cancellationToken);
    }
}

// The shipped WithTestTracking for AWS takes no inner handler; this is its factory with the stub inside.
sealed class AwsFactory(HttpMessageHandler handler) : Amazon.Runtime.HttpClientFactory
{
    public override HttpClient CreateHttpClient(Amazon.Runtime.IClientConfig clientConfig) => new(handler, disposeHandler: false);
}
