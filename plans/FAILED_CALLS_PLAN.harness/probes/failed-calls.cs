// What each tracker records when the call it tracks fails (throws), and what its caller sees then: how many records, each one's
// kind, status, note, Error and phase variants, what the fingerprint reads and what the diagram draws; and, of the exception the
// caller catches, its type, whether it is the very object the inner client threw (or wraps it), whether its stack trace still
// names the inner frame that threw it, and whether it came at the call or from the call's task. Driven through the shipped
// handlers, wrappers, proxies and interceptor with an inner client that throws, except where the line says so, on the published
// 5.31.1 packages. It started as plans/CALLS_AND_EVENTS_PLAN.harness/probes/failed-sends.cs, whose cases are kept, as is its note
// on MassTransit. Elasticsearch is the one real transport: its client is pointed at 127.0.0.1:1, which refuses the connection, and
// that is the only connection the probe makes. It opens no port.
//
// Each inner client throws through Thrown.Here, which keeps the exception and the frame that throws it as a stack trace prints
// it (Type.Method), so a case can say whether the caller's exception is that object and whether its stack still names that frame.
//
// The SDKs are pinned at the versions the src csproj files name, where the graph allows it, so Azure.Storage.Queues is at its floor
// 12.11.0 where failed-sends.cs had 12.28.1. Grpc.Core.Api is left to the graph: Google.Cloud.PubSub.V1 3.35.0 needs 2.71.0 or later
// (through Google.Api.Gax.Grpc 4.13.1), and a pin below that is a downgrade error. The first lines print what NuGet resolved each
// package to, from the app's own deps.json.
//
// Usage, from the harness directory, on the published 5.31.1: dotnet run --file probes/failed-calls.cs > results/failed-calls-5.31.1.txt 2>&1
#:package Kronikol@5.31.1
#:package Kronikol.Extensions.PubSub@5.31.1
#:package Kronikol.Extensions.ServiceBus@5.31.1
#:package Kronikol.Extensions.EventHubs@5.31.1
#:package Kronikol.Extensions.Kafka@5.31.1
#:package Kronikol.Extensions.MassTransit@5.31.1
#:package Kronikol.Extensions.SQS@5.31.1
#:package Kronikol.Extensions.SNS@5.31.1
#:package Kronikol.Extensions.EventBridge@5.31.1
#:package Kronikol.Extensions.StorageQueues@5.31.1
#:package Kronikol.Extensions.S3@5.31.1
#:package Kronikol.Extensions.DynamoDB@5.31.1
#:package Kronikol.Extensions.AtlasDataApi@5.31.1
#:package Kronikol.Extensions.CosmosDB@5.31.1
#:package Kronikol.Extensions.BigQuery@5.31.1
#:package Kronikol.Extensions.DispatchProxy@5.31.1
#:package Kronikol.Extensions.Grpc@5.31.1
#:package Kronikol.Extensions.Redis@5.31.1
#:package Kronikol.Extensions.Spanner@5.31.1
#:package Kronikol.Extensions.Elasticsearch@5.31.1
#:package AWSSDK.SQS@4.0.2.5
#:package AWSSDK.SimpleNotificationService@4.0.2.7
#:package AWSSDK.EventBridge@4.0.5.7
#:package AWSSDK.S3@4.0.13.1
#:package AWSSDK.DynamoDBv2@4.0.10.1
#:package Azure.Storage.Queues@12.11.0
#:package StackExchange.Redis@2.6.80
#:package Elastic.Clients.Elasticsearch@9.0.0
#:package Google.Cloud.Spanner.Data@5.0.0
#:property PublishAot=false

using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using Amazon.EventBridge;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Queues;
using Confluent.Kafka;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Grpc.Core.Interceptors;
using Kronikol.Extensions.AtlasDataApi;
using Kronikol.Extensions.BigQuery;
using Kronikol.Extensions.CosmosDB;
using Kronikol.Extensions.DispatchProxy;
using Kronikol.Extensions.DynamoDB;
using Kronikol.Extensions.Elasticsearch;
using Kronikol.Extensions.EventBridge;
using Kronikol.Extensions.EventHubs;
using Kronikol.Extensions.Grpc;
using Kronikol.Extensions.Kafka;
using Kronikol.Extensions.MassTransit;
using Kronikol.Extensions.PubSub;
using Kronikol.Extensions.Redis;
using Kronikol.Extensions.S3;
using Kronikol.Extensions.ServiceBus;
using Kronikol.Extensions.SNS;
using Kronikol.Extensions.Spanner;
using Kronikol.Extensions.SQS;
using Kronikol.Extensions.StorageQueues;
using Kronikol.History;
using Kronikol.PlantUml;
using Kronikol.Tracking;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using DynamoModel = Amazon.DynamoDBv2.Model;
using EbModel = Amazon.EventBridge.Model;
using Es = Elastic.Clients.Elasticsearch;
using GrpcCore = Grpc.Core;
using Redis = StackExchange.Redis;
using S3Model = Amazon.S3.Model;
using SnsModel = Amazon.SimpleNotificationService.Model;
using SqsModel = Amazon.SQS.Model;

var core = typeof(RequestResponseLog).Assembly;
Console.WriteLine($"Kronikol {core.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
// What NuGet resolved each package to, from the app's own deps.json: an assembly's own attributes do not say (AWSSDK's read 1.0.0).
var depsFile = (AppContext.GetData("APP_CONTEXT_DEPS_FILES") as string)?.Split(';')[0] is { Length: > 0 } listed
    ? listed
    : Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetEntryAssembly()!.GetName().Name}.deps.json");
var packages = System.Text.Json.JsonDocument.Parse(File.ReadAllText(depsFile)).RootElement.GetProperty("libraries").EnumerateObject()
    .Where(library => library.Value.GetProperty("type").GetString() == "package")
    .Select(library => library.Name.Split('/'))
    .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
var kronikolPackages = packages.Where(p => p.Key.StartsWith("Kronikol", StringComparison.OrdinalIgnoreCase)).ToArray();
Console.WriteLine($"Kronikol packages: {string.Join(", ", kronikolPackages.Select(p => p.Value).Distinct())}, for all {kronikolPackages.Length} ({string.Join(", ", kronikolPackages.Select(p => p.Key).Order())})");
string[] sdks =
[
    "AWSSDK.Core", "AWSSDK.SQS", "AWSSDK.SimpleNotificationService", "AWSSDK.EventBridge", "AWSSDK.S3", "AWSSDK.DynamoDBv2",
    "Azure.Core", "Azure.Storage.Queues", "Azure.Messaging.EventHubs", "Azure.Messaging.ServiceBus", "Microsoft.Azure.Cosmos",
    "Google.Cloud.BigQuery.V2", "Google.Cloud.PubSub.V1", "Google.Cloud.Spanner.Data", "Grpc.Core.Api", "Grpc.Net.Client", "Google.Protobuf",
    "Confluent.Kafka", "MassTransit", "StackExchange.Redis", "Elastic.Clients.Elasticsearch", "Elastic.Transport",
];
Console.WriteLine($"SDKs: {string.Join(", ", sdks.Select(id => $"{id} {packages.GetValueOrDefault(id, "not in the graph")}"))}");
var read = core.GetType("Kronikol.CallGraph.MessageTable", throwOnError: true)!.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!;
var schemeOf = core.GetType("Kronikol.CallGraph.MessageTable", throwOnError: true)!.GetMethod("Scheme", BindingFlags.Static | BindingFlags.NonPublic)!;

var currentTest = "";
(string Name, string Id) Current() => ("failed calls", currentTest);

string Verdict(RequestResponseLog log) =>
    read.Invoke(null, [(string?)schemeOf.Invoke(null, [log.Uri.ToString()]), log.Method.Value?.ToString()])!.ToString()!;

// One case: the call is driven, and what its caller caught is set beside the exception the inner threw (Thrown), then each record
// it left, the fingerprint line and the diagram's arrows and notes. `returned` says what a call that throws nothing handed back.
void Failed(string name, Func<Task> drive, Func<string>? returned = null)
{
    currentTest = Guid.NewGuid().ToString("N");
    TestPhaseContext.Reset();
    Thrown.Reset();
    Exception? caught = null;
    var atTheCall = false;
    try
    {
        var task = drive();
        try
        {
            task.GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            caught = e;
        }
    }
    catch (Exception e)
    {
        caught = e;
        atTheCall = true;
    }

    var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == currentTest).ToArray();
    Console.WriteLine();
    Console.WriteLine(name);
    Console.WriteLine($"    the caller saw: {Show.Saw(caught, atTheCall)}");
    if (caught is not null && Thrown.Last is not null && !ReferenceEquals(caught, Thrown.Last))
        Console.WriteLine($"    its message: {Show.Excerpt(caught.Message, 110)}");
    if (caught is not null && Thrown.Where is not null && !Show.Names(caught, Thrown.Where))
        Console.WriteLine($"    its stack starts: {Show.FirstFrame(caught)}");
    if (returned is not null)
        Console.WriteLine($"    it returned: {returned()}");
    Console.WriteLine($"    {logs.Length} records");
    foreach (var log in logs)
    {
        var status = log.StatusCode?.Value?.ToString() ?? "-";
        Console.WriteLine($"    {log.Type,-8} {log.Method.Value,-34} kind {log.MetaType,-7} status {status,-24} content {Show.Marked(log.Content),-20} error {Show.Marked(log.Error),-20} variants {Show.Variants(log),-12} table {Verdict(log)}");
        if (log.Type == RequestResponseType.Response && (log.Content is not null || log.Error is not null))
            Console.WriteLine($"             its content {Show.Excerpt(log.Content)}; its error {Show.Excerpt(log.Error)}");
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

// ============================================================================ failed-sends.cs, kept

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

// ============================================================================ the calls FAILED_CALLS_PLAN adds

// ---------------------------------------------------------------------------- 1. the HTTP handlers, over the same stub that throws

var s3 = new Amazon.S3.AmazonS3Client(credentials, new Amazon.S3.AmazonS3Config
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new S3TrackingMessageHandler(new S3TrackingMessageHandlerOptions { ServiceName = "S3", CurrentTestInfoFetcher = Current }, refused)),
});
Failed("S3: a PutObject that throws", () => s3.PutObjectAsync(new S3Model.PutObjectRequest { BucketName = "orders", Key = "order-1.json", ContentBody = "{\"id\":\"order-1\"}" }));

var dynamo = new Amazon.DynamoDBv2.AmazonDynamoDBClient(credentials, new Amazon.DynamoDBv2.AmazonDynamoDBConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new DynamoDbTrackingMessageHandler(new DynamoDbTrackingMessageHandlerOptions { ServiceName = "DynamoDB", CurrentTestInfoFetcher = Current }, refused)),
});
Failed("DynamoDB: a PutItem that throws", () => dynamo.PutItemAsync(new DynamoModel.PutItemRequest
{
    TableName = "Orders",
    Item = new Dictionary<string, DynamoModel.AttributeValue> { ["id"] = new() { S = "order-1" } },
}));

var atlas = new HttpClient(new AtlasDataApiTrackingMessageHandler(new AtlasDataApiTrackingMessageHandlerOptions { CurrentTestInfoFetcher = Current }, refused));
Failed("Atlas Data API: an insertOne that throws", () => atlas.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://data.mongodb-api.com/app/myapp/endpoint/data/v1/action/insertOne")
{
    Content = new StringContent("""{"dataSource":"Cluster0","database":"orders_db","collection":"orders","document":{"name":"x"}}""", System.Text.Encoding.UTF8, "application/json"),
}));

HttpRequestMessage CosmosCreate() => new(HttpMethod.Post, "https://account.documents.azure.com/dbs/mydb/colls/orders/docs")
{
    Content = new StringContent("""{"id":"order-1","total":42.50}"""),
};
var cosmos = new HttpMessageInvoker(new CosmosTrackingMessageHandler(new CosmosTrackingMessageHandlerOptions { ServiceName = "CosmosDB", CurrentTestInfoFetcher = Current }, refused));
var cosmosPhased = new HttpMessageInvoker(new CosmosTrackingMessageHandler(new CosmosTrackingMessageHandlerOptions
{
    ServiceName = "CosmosDB",
    CurrentTestInfoFetcher = Current,
    SetupVerbosity = CosmosTrackingVerbosity.Summarised,
    ActionVerbosity = CosmosTrackingVerbosity.Detailed,
}, refused));
Failed("Cosmos: a create that throws", () => cosmos.SendAsync(CosmosCreate(), CancellationToken.None));
Failed("Cosmos: a create that throws, under SetupVerbosity Summarised and ActionVerbosity Detailed, so a call of no known phase carries phase variants",
    () => cosmosPhased.SendAsync(CosmosCreate(), CancellationToken.None));

HttpRequestMessage BigQueryInsertJob() => new(HttpMethod.Put, "http://0.0.0.0:9050/upload/bigquery/v2/projects/shop/jobs?uploadType=resumable&upload_id=u1");
var bigQuery = new HttpMessageInvoker(new BigQueryTrackingMessageHandler(new BigQueryTrackingMessageHandlerOptions { ServiceName = "BigQuery", CurrentTestInfoFetcher = Current }, refused));
var bigQueryPhased = new HttpMessageInvoker(new BigQueryTrackingMessageHandler(new BigQueryTrackingMessageHandlerOptions
{
    ServiceName = "BigQuery",
    CurrentTestInfoFetcher = Current,
    SetupVerbosity = BigQueryTrackingVerbosity.Summarised,
    ActionVerbosity = BigQueryTrackingVerbosity.Detailed,
}, refused));
Failed("BigQuery control: an insert job that throws", () => bigQuery.SendAsync(BigQueryInsertJob(), CancellationToken.None));
Failed("BigQuery control: the same, under SetupVerbosity Summarised and ActionVerbosity Detailed", () => bigQueryPhased.SendAsync(BigQueryInsertJob(), CancellationToken.None));

// Kronikol's own HTTP handler, built as tests/Kronikol.Tests builds it standalone: options, then the inner handler.
var kronikolHandler = new HttpMessageInvoker(new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
{
    FixedNameForReceivingService = "Orders API",
    CurrentTestInfoFetcher = Current,
})
{
    InnerHandler = refused,
});
Failed("Kronikol's HTTP handler control (TestTrackingMessageHandler): a GET that throws",
    () => kronikolHandler.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://orders-api/orders/1"), CancellationToken.None));

// ---------------------------------------------------------------------------- 2. Pub/Sub: a subscriber whose handler throws

var subscriber = new TrackingSubscriberClient(new OneMessageSubscriber(), pubSubOptions);
Failed("Pub/Sub: a subscriber whose handler throws (StartAsync(SubscriptionHandler), the inner handing it one message)",
    () => subscriber.StartAsync(new ThrowingSubscriptionHandler()));

// ---------------------------------------------------------------------------- 3. Event Hubs: reads that fail

var failingRead = new TrackingEventHubConsumerClient(new MoveNextFails(), eventHubsOptions);
Failed("Event Hubs: a read whose MoveNextAsync throws", async () =>
{
    await foreach (var _ in failingRead.ReadEventsAsync())
    {
    }
});
var refusedRead = new TrackingEventHubConsumerClient(new ReadThrowsAtOnce(), eventHubsOptions);
Failed("Event Hubs: a read whose inner ReadEventsAsync throws as it is called, before it has an enumerator to hand back", async () =>
{
    await foreach (var _ in refusedRead.ReadEventsAsync())
    {
    }
});

// ---------------------------------------------------------------------------- 4. Service Bus: a receive that fails

var receiver = new TrackingServiceBusReceiver(new ThrowingReceiver(), new ServiceBusTracker(serviceBusOptions), serviceBusOptions);
Failed("Service Bus: a ReceiveMessageAsync that throws ServiceBusException", () => receiver.ReceiveMessageAsync());

// ---------------------------------------------------------------------------- 5. Kafka: a handler that throws inside the consume's scope

Failed("Kafka: a handler that throws inside TrackConsume's scope", () =>
{
    using (kafka.TrackConsume(KafkaInner.Consumed()))
        KafkaInner.Handle();
    return Task.CompletedTask;
});
var kafkaProcessingOptions = new KafkaTrackingOptions { ServiceName = "Kafka", CurrentTestInfoFetcher = Current, TraceMessageProcessing = true };
Failed("Kafka: TrackingKafkaConsumer under TraceMessageProcessing, the loop's handler throwing after a Consume, the consumer disposed on the way out", () =>
{
    using var consumer = new TrackingKafkaConsumer<string, string>(OneMessageConsumer.Create(KafkaInner.Consumed()), new KafkaTracker(kafkaProcessingOptions), kafkaProcessingOptions);
    consumer.Consume(TimeSpan.FromSeconds(1));
    KafkaInner.Handle();
    return Task.CompletedTask;
});

// ---------------------------------------------------------------------------- 6. TrackingProxy, and DispatchProxy's ReplaceWithTracked over it

var orders = TrackingProxy<IOrders>.Create(new FailingOrders(), new TrackingProxyOptions { ServiceName = "Orders", CurrentTestInfoFetcher = Current });
Failed("TrackingProxy: a sync method that throws InvalidOperationException", () =>
{
    orders.Place("order-1");
    return Task.CompletedTask;
});
Failed("TrackingProxy: an async Task<T> method that throws InvalidOperationException", () => orders.PlaceAsync("order-1"));
Failed("TrackingProxy: a Task<T> method that throws InvalidOperationException as it is called, before it has a task", () => orders.PlaceNowAsync("order-1"));
Failed("TrackingProxy: an async ValueTask<T> method that throws InvalidOperationException", () => orders.PlaceValueAsync("order-1").AsTask());

var services = new ServiceCollection();
var implementation = new FailingOrders();
services.AddSingleton<IOrders>(implementation);
services.ReplaceWithTracked<IOrders>(implementation, "Orders (DI)", testInfoFetcher: Current);
var resolved = services.BuildServiceProvider().GetRequiredService<IOrders>();
Failed("DispatchProxy ReplaceWithTracked: a sync method that throws InvalidOperationException", () =>
{
    resolved.Place("order-1");
    return Task.CompletedTask;
});
Failed("DispatchProxy ReplaceWithTracked: an async Task<T> method that throws InvalidOperationException", () => resolved.PlaceAsync("order-1"));

// ---------------------------------------------------------------------------- 7. gRPC: a unary call that fails

var grpc = new GrpcTrackingInterceptor(new GrpcTrackingOptions { ServiceName = "Orders gRPC", CurrentTestInfoFetcher = Current });
GrpcCore.RpcException Unavailable() => new(new GrpcCore.Status(GrpcCore.StatusCode.Unavailable, Boom));
Failed("gRPC: AsyncUnaryCall, the continuation's response failing with RpcException(Unavailable)",
    () => grpc.AsyncUnaryCall("order-1", GrpcInner.Context(), (_, _) => GrpcInner.Answered(Unavailable())).ResponseAsync);
Failed("gRPC: AsyncUnaryCall, the continuation's response failing with InvalidOperationException",
    () => grpc.AsyncUnaryCall("order-1", GrpcInner.Context(), (_, _) => GrpcInner.Answered(new InvalidOperationException(Boom))).ResponseAsync);
Failed("gRPC: AsyncUnaryCall, the continuation itself throwing RpcException(Unavailable) as the call starts",
    () => grpc.AsyncUnaryCall("order-1", GrpcInner.Context(), (_, _) => GrpcInner.Refuse(Unavailable())).ResponseAsync);
Failed("gRPC: AsyncUnaryCall, the continuation itself throwing InvalidOperationException as the call starts",
    () => grpc.AsyncUnaryCall("order-1", GrpcInner.Context(), (_, _) => GrpcInner.Refuse(new InvalidOperationException(Boom))).ResponseAsync);

// ---------------------------------------------------------------------------- 8. Redis: WithRedisTestTracking over a database that fails

var redis = RedisStub.Create().WithRedisTestTracking(new RedisTrackingDatabaseOptions { ServiceName = "Redis", CurrentTestInfoFetcher = Current });
Failed("Redis: a StringGet (sync) that throws RedisConnectionException", () =>
{
    redis.StringGet("order:1");
    return Task.CompletedTask;
});
Failed("Redis: a StringGetAsync whose task faults with RedisConnectionException", () => redis.StringGetAsync("order:1"));
Failed("Redis: a StringSetAsync that throws RedisConnectionException as it is called, before it has a task", () => redis.StringSetAsync("order:1", "placed"));

// ---------------------------------------------------------------------------- 9. Spanner: a statement that fails

var spannerOptions = new SpannerTrackingOptions { ServiceName = "Spanner", CurrentTestInfoFetcher = Current };
var spannerConnection = new TrackingSpannerConnection(new FakeSpannerConnection(), spannerOptions);
var statement = new TrackingSpannerCommand(new FailingSpannerCommand(), spannerConnection, spannerOptions) { CommandText = "INSERT INTO Orders (Id) VALUES ('order-1')" };
Failed("Spanner: an ExecuteNonQuery that throws", () =>
{
    statement.ExecuteNonQuery();
    return Task.CompletedTask;
});
Failed("Spanner: an ExecuteNonQueryAsync whose task faults (DbCommand's own, over the ExecuteNonQuery that throws)", () => statement.ExecuteNonQueryAsync());

// ---------------------------------------------------------------------------- 10. Elasticsearch: the real transport, against a port that refuses

var elastic = new Es.ElasticsearchClient(new Es.ElasticsearchClientSettings(new Uri("http://127.0.0.1:1"))
    .WithTestTracking(new ElasticsearchTrackingOptions { ServiceName = "Elasticsearch", CurrentTestInfoFetcher = Current }));
Es.IndexResponse? indexed = null;
Failed("Elasticsearch: an IndexAsync to http://127.0.0.1:1, which refuses (nothing stubbed)",
    async () => indexed = await elastic.IndexAsync(new OrderDocument("order-1"), (Es.IndexName)"orders", (Es.Id)"order-1"),
    () => Show.Answer(indexed));
Es.GetResponse<OrderDocument>? fetched = null;
Failed("Elasticsearch: a GetAsync to http://127.0.0.1:1, which refuses (nothing stubbed)",
    async () => fetched = await elastic.GetAsync<OrderDocument>((Es.IndexName)"orders", (Es.Id)"order-1"),
    () => Show.Answer(fetched));

return 0;

// One word, so a note and a status can be searched for it.
public partial class Program
{
    public const string Boom = "call-refused";
}

// The exception an inner client throws, kept with the frame that throws it as a stack trace prints that frame (Type.Method), so a
// case can ask whether what its caller caught is that object and whether its stack still names that frame. Reset before each case.
public static class Thrown
{
    public static Exception? Last { get; private set; }
    public static string? Where { get; private set; }

    public static void Reset()
    {
        Last = null;
        Where = null;
    }

    public static T Here<T>(T exception, Type thrower, [System.Runtime.CompilerServices.CallerMemberName] string member = "") where T : Exception
    {
        Last = exception;
        Where = $"{thrower.Name}.{member}";
        return exception;
    }
}

static class Show
{
    // What the caller caught, set beside what the inner threw.
    public static string Saw(Exception? caught, bool atTheCall)
    {
        if (caught is null)
            return "nothing";
        var how = atTheCall ? "at the call" : "from its task";
        if (Thrown.Last is not { } thrown)
            return $"{caught.GetType().Name} {how}, where nothing the probe made threw: {Excerpt(caught.Message, 90)}";

        var depth = 0;
        Exception? link = caught;
        while (link is not null && !ReferenceEquals(link, thrown))
        {
            link = link.InnerException;
            depth++;
        }
        var relation = link is null
            ? $"no, and the {thrown.GetType().Name} thrown is not in its chain"
            : depth == 0
                ? "yes"
                : $"no, it wraps it (the {thrown.GetType().Name} thrown is {depth} deep, and that one's own stack names {Thrown.Where}: {YesNo(Names(thrown, Thrown.Where!))})";
        return $"{caught.GetType().Name} {how}; the very object the inner threw: {relation}; its stack names {Thrown.Where}: {YesNo(Names(caught, Thrown.Where!))}";
    }

    // Whether an exception's stack trace has the frame, as "Type.Method(" (an async iterator's reads "Type.Method(...)+MoveNext()").
    public static bool Names(Exception exception, string frame) => exception.StackTrace?.Contains(frame + "(", StringComparison.Ordinal) == true;

    public static string FirstFrame(Exception exception) =>
        exception.StackTrace?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) is { } frame ? Excerpt(frame, 120) : "no stack";

    private static string YesNo(bool value) => value ? "yes" : "no";

    public static string Marked(string? text) =>
        text is null ? "none" : text.Contains(Program.Boom, StringComparison.Ordinal) ? "the exception's text" : "other";

    public static string Variants(RequestResponseLog log) => (log.SetupVariant, log.ActionVariant) switch
    {
        (null, null) => "none",
        (not null, not null) => "setup+action",
        (not null, null) => "setup only",
        _ => "action only",
    };

    public static string Excerpt(string? text, int length = 70)
    {
        if (text is null)
            return "none";
        var flat = Regex.Replace(text, @"\s+", " ");
        return flat.Length > length ? $"\"{flat[..length]}...\"" : $"\"{flat}\"";
    }

    public static string Chain(Exception? exception)
    {
        if (exception is null)
            return "none";
        var types = new List<string>();
        for (var link = exception; link is not null; link = link.InnerException)
            types.Add(link.GetType().Name);
        return $"{string.Join(" > ", types)} ({Excerpt(exception.Message, 80)})";
    }

    public static string Answer(Elastic.Transport.Products.Elasticsearch.ElasticsearchResponse? response) => response is null
        ? "no response"
        : $"IsValidResponse {response.IsValidResponse}; HttpStatusCode {response.ApiCallDetails?.HttpStatusCode?.ToString() ?? "none"}; "
          + $"OriginalException {Chain(response.ApiCallDetails?.OriginalException)}";
}

public record OrderPlaced(string OrderId);

public record OrderDocument(string Id);

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
        throw Thrown.Here(new HttpRequestException(Program.Boom), typeof(Throws));
}

sealed class AwsFactory(HttpMessageHandler handler) : Amazon.Runtime.HttpClientFactory
{
    public override HttpClient CreateHttpClient(Amazon.Runtime.IClientConfig clientConfig) => new(handler, disposeHandler: false);
}

sealed class ThrowingPublisher : PublisherClient
{
    public override TopicName TopicName { get; } = new("shop", "orders");

    public override Task<string> PublishAsync(PubsubMessage message) => throw Thrown.Here(new InvalidOperationException(Program.Boom), typeof(ThrowingPublisher));
}

sealed class ThrowingProducer() : EventHubProducerClient("Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==;EntityPath=orders")
{
    public override Task SendAsync(IEnumerable<EventData> eventBatch, CancellationToken cancellationToken = default) =>
        throw Thrown.Here(new InvalidOperationException(Program.Boom), typeof(ThrowingProducer));
}

sealed class ThrowingSender : ServiceBusSender
{
    public override string EntityPath => "orders";
    public override string FullyQualifiedNamespace => "test.servicebus.windows.net";

    public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default) =>
        throw Thrown.Here(new InvalidOperationException(Program.Boom), typeof(ThrowingSender));
}

// A subscriber that hands its handler one message as it starts (as OneMessageSubscriberClient in tests/Kronikol.Tests.PubSub).
sealed class OneMessageSubscriber : SubscriberClient
{
    public override SubscriptionName SubscriptionName { get; } = new("shop", "orders-subscription");

    public override Task StartAsync(Func<PubsubMessage, CancellationToken, Task<Reply>> handlerAsync) =>
        handlerAsync(new PubsubMessage { Data = ByteString.CopyFromUtf8("hello") }, CancellationToken.None);
}

sealed class ThrowingSubscriptionHandler : SubscriptionHandler
{
    public override Task<SubscriberClient.Reply> HandleMessage(PubsubMessage message, CancellationToken cancellationToken) =>
        throw Thrown.Here(new InvalidOperationException(Program.Boom), typeof(ThrowingSubscriptionHandler));
}

// A consumer whose read fails on its first MoveNextAsync.
sealed class MoveNextFails() : EventHubConsumerClient("orders-group", "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==;EntityPath=orders")
{
    public override IAsyncEnumerable<PartitionEvent> ReadEventsAsync(CancellationToken cancellationToken = default) => Fail(cancellationToken);

    private async IAsyncEnumerable<PartitionEvent> Fail([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (!cancellationToken.IsCancellationRequested)
            throw Thrown.Here(new InvalidOperationException(Program.Boom), typeof(MoveNextFails));
        yield break;
    }
}

// A consumer whose read throws as it is called, so there is no enumerator.
sealed class ReadThrowsAtOnce() : EventHubConsumerClient("orders-group", "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==;EntityPath=orders")
{
    public override IAsyncEnumerable<PartitionEvent> ReadEventsAsync(CancellationToken cancellationToken = default) =>
        throw Thrown.Here(new InvalidOperationException(Program.Boom), typeof(ReadThrowsAtOnce));
}

sealed class ThrowingReceiver : ServiceBusReceiver
{
    public override string EntityPath => "orders";
    public override string FullyQualifiedNamespace => "test.servicebus.windows.net";

    public override Task<ServiceBusReceivedMessage> ReceiveMessageAsync(TimeSpan? maxWaitTime = null, CancellationToken cancellationToken = default) =>
        throw Thrown.Here(new ServiceBusException(Program.Boom, ServiceBusFailureReason.ServiceCommunicationProblem), typeof(ThrowingReceiver));
}

public static class KafkaInner
{
    // A message with no Kronikol headers, so the consume is the running test's.
    public static ConsumeResult<string, string> Consumed() => new()
    {
        TopicPartitionOffset = new Confluent.Kafka.TopicPartitionOffset("orders", new Confluent.Kafka.Partition(0), new Confluent.Kafka.Offset(3)),
        Message = new Message<string, string> { Key = "order-1", Value = "{\"Status\":\"Placed\"}", Headers = new Confluent.Kafka.Headers() },
    };

    // The loop's own code for the message, which throws.
    public static void Handle() => throw Thrown.Here(new InvalidOperationException(Program.Boom), typeof(KafkaInner));
}

// A consumer whose first Consume returns the message it was made with, and every later one nothing.
public class OneMessageConsumer : DispatchProxy
{
    private ConsumeResult<string, string>? _next;

    public static IConsumer<string, string> Create(ConsumeResult<string, string> result)
    {
        var proxy = Create<IConsumer<string, string>, OneMessageConsumer>();
        ((OneMessageConsumer)(object)proxy)._next = result;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        "Consume" => Interlocked.Exchange(ref _next, null),
        "get_Name" => "orders-consumer",
        _ => targetMethod?.ReturnType is { IsValueType: true } type && type != typeof(void) ? Activator.CreateInstance(type) : null,
    };
}

public interface IOrders
{
    int Place(string id);
    Task<int> PlaceAsync(string id);
    Task<int> PlaceNowAsync(string id);
    ValueTask<int> PlaceValueAsync(string id);
}

public sealed class FailingOrders : IOrders
{
    public int Place(string id) => throw Thrown.Here(new InvalidOperationException(Program.Boom), typeof(FailingOrders));

    public async Task<int> PlaceAsync(string id)
    {
        await Task.Yield();
        throw Thrown.Here(new InvalidOperationException(Program.Boom), typeof(FailingOrders));
    }

    // Throws as it is called, before it has a task to hand back, as an argument check does.
    public Task<int> PlaceNowAsync(string id) => throw Thrown.Here(new InvalidOperationException(Program.Boom), typeof(FailingOrders));

    public async ValueTask<int> PlaceValueAsync(string id)
    {
        await Task.Yield();
        throw Thrown.Here(new InvalidOperationException(Program.Boom), typeof(FailingOrders));
    }
}

public static class GrpcInner
{
    private static readonly GrpcCore.Marshaller<string> Text = GrpcCore.Marshallers.Create(
        text => System.Text.Encoding.UTF8.GetBytes(text), bytes => System.Text.Encoding.UTF8.GetString(bytes));

    public static ClientInterceptorContext<string, string> Context() =>
        new(new GrpcCore.Method<string, string>(GrpcCore.MethodType.Unary, "shop.Orders", "Place", Text, Text), null, new GrpcCore.CallOptions());

    // The call a channel hands back when the server cannot be reached: it starts, and its response fails.
    public static GrpcCore.AsyncUnaryCall<string> Answered(Exception failure) =>
        new(Fail(failure), Task.FromResult(new GrpcCore.Metadata()), () => new GrpcCore.Status(GrpcCore.StatusCode.Unavailable, Program.Boom),
            () => new GrpcCore.Metadata(), () => { });

    private static async Task<string> Fail(Exception failure)
    {
        await Task.Yield();
        throw Thrown.Here(failure, typeof(GrpcInner));
    }

    // A continuation that throws as the call starts, as a disposed channel's does.
    public static GrpcCore.AsyncUnaryCall<string> Refuse(Exception failure) => throw Thrown.Here(failure, typeof(GrpcInner));
}

// An IDatabase whose StringGet throws, whose StringGetAsync hands back a faulted task, and whose StringSetAsync throws as it is
// called (as StubDatabase in tests/Kronikol.Tests.Redis, reduced to these).
public class RedisStub : DispatchProxy
{
    public static Redis.IDatabase Create() => Create<Redis.IDatabase, RedisStub>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        "get_Database" => 0,
        "StringGet" => throw Thrown.Here(Refused(), typeof(RedisStub)),
        "StringGetAsync" => Faulted(),
        "StringSetAsync" => throw Thrown.Here(Refused(), typeof(RedisStub)),
        _ => targetMethod?.ReturnType is { IsValueType: true } type && type != typeof(void) ? Activator.CreateInstance(type) : null,
    };

    private static Redis.RedisConnectionException Refused() => new(Redis.ConnectionFailureType.UnableToConnect, Program.Boom);

    // An async method that throws before its first await hands back a task that has already faulted.
#pragma warning disable CS1998
    private static async Task<Redis.RedisValue> Faulted()
    {
        throw Thrown.Here(Refused(), typeof(RedisStub));
    }
#pragma warning restore CS1998
}

// The connection and command TrackingSpannerCommandFailureTests builds, the command's every statement failing as a driver's does
// for one the server rejects.
sealed class FakeSpannerConnection : DbConnection
{
    [AllowNull]
    public override string ConnectionString { get; set; } = "";
    public override string Database => "orders";
    public override string DataSource => "spanner-host";
    public override string ServerVersion => "1";
    public override System.Data.ConnectionState State => System.Data.ConnectionState.Open;
    public override void Open() { }
    public override void Close() { }
    public override void ChangeDatabase(string databaseName) { }
    protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel) => throw new NotSupportedException();
    protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
}

sealed class FailingSpannerCommand : DbCommand
{
    [AllowNull]
    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; } = 30;
    public override System.Data.CommandType CommandType { get; set; } = System.Data.CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override System.Data.UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbTransaction? DbTransaction { get; set; }
    protected override DbParameterCollection DbParameterCollection { get; } = new NoParameters();

    protected override DbDataReader ExecuteDbDataReader(System.Data.CommandBehavior behavior) => throw Thrown.Here(Rejected(), typeof(FailingSpannerCommand));
    public override int ExecuteNonQuery() => throw Thrown.Here(Rejected(), typeof(FailingSpannerCommand));
    public override object? ExecuteScalar() => throw Thrown.Here(Rejected(), typeof(FailingSpannerCommand));
    public override void Prepare() { }
    public override void Cancel() { }
    protected override DbParameter CreateDbParameter() => throw new NotSupportedException();

    private static InvalidOperationException Rejected() => new(Program.Boom);
}

sealed class NoParameters : DbParameterCollection
{
    public override int Count => 0;
    public override object SyncRoot { get; } = new();
    public override int Add(object value) => throw new NotSupportedException();
    public override void AddRange(Array values) => throw new NotSupportedException();
    public override void Clear() { }
    public override bool Contains(object value) => false;
    public override bool Contains(string value) => false;
    public override void CopyTo(Array array, int index) { }
    public override System.Collections.IEnumerator GetEnumerator() => Array.Empty<DbParameter>().GetEnumerator();
    protected override DbParameter GetParameter(int index) => throw new IndexOutOfRangeException();
    protected override DbParameter GetParameter(string parameterName) => throw new IndexOutOfRangeException();
    public override int IndexOf(object value) => -1;
    public override int IndexOf(string parameterName) => -1;
    public override void Insert(int index, object value) => throw new NotSupportedException();
    public override void Remove(object value) { }
    public override void RemoveAt(int index) => throw new IndexOutOfRangeException();
    public override void RemoveAt(string parameterName) => throw new IndexOutOfRangeException();
    protected override void SetParameter(int index, DbParameter value) => throw new IndexOutOfRangeException();
    protected override void SetParameter(string parameterName, DbParameter value) => throw new IndexOutOfRangeException();
}
