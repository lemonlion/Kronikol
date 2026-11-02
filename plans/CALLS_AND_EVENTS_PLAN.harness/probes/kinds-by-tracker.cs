// What each message and GCP tracker logs as MetaType, per operation, beside what the report's other readers make of the
// same record: MessageTable's verdict on the label, and the OTLP mapper's span kind and db.system.name. Every value in a
// row comes from a record the shipped code wrote, or from the shipped code reading that record. The published 5.27.13
// packages, so this measures what a consumer has. Detailed verbosity throughout (every package has that level).
//
// How each route is driven:
// - The trackers are hand-called through their public entry points, once per member of the operation enum. The operation
//   info comes from the package's public classifier where it has one, as the package's wrappers get it, so the label is
//   the one a wrapper's call records.
// - The handlers sit in front of a stub and are driven by the real SDK client, so each request is the one the SDK sends.
//   Their operation column reads Enum[SdkCall]: the enum member is what the package's classifier makes of that request,
//   and the SDK call is left out when it has the member's name. A call that sends several requests writes a row for each.
// - Options that stop a call being logged by default are opened up: KafkaTrackingOptions' Track* flags, and
//   EventBridgeTrackingMessageHandlerOptions.ExcludedOperations.
//
// A driven operation that logs nothing, or an enum member nothing here reaches, writes a row saying so (requestKind
// NO-RECORD or NOT-DRIVEN), prints to stderr and makes the exit code 1. It is never dropped.
//
// A request and its response are paired by RequestResponseId, and otlpSpanKind and otlpDbSystem are what OtlpSpanMapper.Map
// makes of that pair. Stdout says what the file cannot: the versions measured, the SDK calls that threw after the handler
// logged them, and any pair MapAll, which also compares TraceId, does not read as one call.
//
// Usage, from the harness directory: dotnet run --file probes/kinds-by-tracker.cs -- results/kinds-by-tracker.tsv
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.Otlp@5.27.13
#:package Kronikol.Extensions.Spanner@5.27.13
#:package Kronikol.Extensions.BigQuery@5.27.13
#:package Kronikol.Extensions.Bigtable@5.27.13
#:package Kronikol.Extensions.PubSub@5.27.13
#:package Kronikol.Extensions.ServiceBus@5.27.13
#:package Kronikol.Extensions.EventHubs@5.27.13
#:package Kronikol.Extensions.Kafka@5.27.13
#:package Kronikol.Extensions.MassTransit@5.27.13
#:package Kronikol.Extensions.SQS@5.27.13
#:package Kronikol.Extensions.SNS@5.27.13
#:package Kronikol.Extensions.EventBridge@5.27.13
#:package Kronikol.Extensions.StorageQueues@5.27.13
#:package Google.Cloud.BigQuery.V2@3.12.0
#:package Azure.Storage.Queues@12.28.1
#:property PublishAot=false

using System.Net;
using System.Reflection;
using System.Text;
using Amazon.EventBridge;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Azure.Storage.Queues;
using Confluent.Kafka;
using Google.Cloud.BigQuery.V2;
using Kronikol.Constants;
using Kronikol.Extensions.BigQuery;
using Kronikol.Extensions.Bigtable;
using Kronikol.Extensions.EventBridge;
using Kronikol.Extensions.EventHubs;
using Kronikol.Extensions.Kafka;
using Kronikol.Extensions.MassTransit;
using Kronikol.Extensions.Otlp;
using Kronikol.Extensions.PubSub;
using Kronikol.Extensions.ServiceBus;
using Kronikol.Extensions.SNS;
using Kronikol.Extensions.Spanner;
using Kronikol.Extensions.SQS;
using Kronikol.Extensions.StorageQueues;
using Kronikol.Tracking;
using BqData = Google.Apis.Bigquery.v2.Data;
using EbModel = Amazon.EventBridge.Model;
using SnsModel = Amazon.SimpleNotificationService.Model;
using SqsModel = Amazon.SQS.Model;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: dotnet run --file probes/kinds-by-tracker.cs -- <out.tsv>");
    return 2;
}

const string Published = "5.27.13";
const string NoRecord = "NO-RECORD";
const string NotDriven = "NOT-DRIVEN";

var rows = new List<string[]>();
var failures = new List<string>();
var sdkNotes = new List<string>();
var pairingNotes = new List<string>();

// Each driven operation runs under a test id of its own, which is how its records are told from every other's.
var currentTest = "";
(string Name, string Id) Current() => ("kinds-by-tracker", currentTest);

// MessageTable is internal to the Kronikol assembly.
var messageTable = typeof(RequestResponseLog).Assembly.GetType("Kronikol.CallGraph.MessageTable", throwOnError: true)!;
var hasScheme = messageTable.GetMethod("HasScheme", BindingFlags.Static | BindingFlags.NonPublic)!;
var read = messageTable.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!;

var otlp = new OtlpExportOptions { Endpoint = new Uri("http://localhost:4318/v1/traces") };

// ---------------------------------------------------------------------------- what this run measured

foreach (var type in new[]
{
    typeof(RequestResponseLog), typeof(OtlpSpanMapper), typeof(SpannerTracker), typeof(BigQueryTracker), typeof(BigtableTracker),
    typeof(PubSubTracker), typeof(ServiceBusTracker), typeof(EventHubsTracker), typeof(KafkaTracker), typeof(MassTransitTracker),
    typeof(SqsTrackingMessageHandler), typeof(SnsTrackingMessageHandler), typeof(EventBridgeTrackingMessageHandler),
    typeof(StorageQueueTrackingMessageHandler),
})
{
    var version = VersionOf(type);
    Console.WriteLine($"{type.Assembly.GetName().Name} {version}");
    if (!version.StartsWith(Published, StringComparison.Ordinal))
        failures.Add($"{type.Assembly.GetName().Name} is {version}, not {Published}");
}

foreach (var type in new[] { typeof(AmazonSQSClient), typeof(AmazonSimpleNotificationServiceClient), typeof(AmazonEventBridgeClient), typeof(QueueClient), typeof(BigQueryClient) })
    Console.WriteLine($"SDK {type.Assembly.GetName().Name} {VersionOf(type)}");

// A phase of Setup or Action would gate each tracker on its options; Unknown always tracks.
if (TestPhaseContext.Current != TestPhase.Unknown)
    failures.Add($"the phase is {TestPhaseContext.Current}, not Unknown");

// ---------------------------------------------------------------------------- Spanner: the tracker

const string SpannerPackage = "Kronikol.Extensions.Spanner";
var spanner = new SpannerTracker(new SpannerTrackingOptions { Verbosity = SpannerTrackingVerbosity.Detailed, CurrentTestInfoFetcher = Current });
EachMember<SpannerOperation, SpannerOperationInfo>(SpannerPackage, nameof(SpannerTracker),
    member => member switch
    {
        SpannerOperation.Query => SpannerOperationClassifier.ClassifySql("SELECT Id FROM Orders WHERE Id = 1"),
        SpannerOperation.Insert => SpannerOperationClassifier.ClassifySql("INSERT INTO Orders (Id) VALUES (1)"),
        SpannerOperation.Update => SpannerOperationClassifier.ClassifySql("UPDATE Orders SET Total = 2 WHERE Id = 1"),
        SpannerOperation.Delete => SpannerOperationClassifier.ClassifySql("DELETE FROM Orders WHERE Id = 1"),
        SpannerOperation.Ddl => SpannerOperationClassifier.ClassifySql("CREATE TABLE Orders (Id INT64) PRIMARY KEY (Id)"),
        SpannerOperation.Other => SpannerOperationClassifier.ClassifySql("GRANT SELECT ON TABLE Orders TO ROLE reader"),
        SpannerOperation.Read => SpannerOperationClassifier.ClassifyGrpc("Read", "Orders", "shop"),
        SpannerOperation.StreamingRead => SpannerOperationClassifier.ClassifyGrpc("StreamingRead", "Orders", "shop"),
        SpannerOperation.Commit => SpannerOperationClassifier.ClassifyGrpc("Commit", null, "shop"),
        SpannerOperation.Rollback => SpannerOperationClassifier.ClassifyGrpc("Rollback", null, "shop"),
        SpannerOperation.BeginTransaction => SpannerOperationClassifier.ClassifyGrpc("BeginTransaction", null, "shop"),
        SpannerOperation.BatchDml => SpannerOperationClassifier.ClassifyGrpc("ExecuteBatchDml", null, "shop"),
        SpannerOperation.PartitionQuery => SpannerOperationClassifier.ClassifyGrpc("PartitionQuery", null, "shop"),
        SpannerOperation.PartitionRead => SpannerOperationClassifier.ClassifyGrpc("PartitionRead", "Orders", "shop"),
        SpannerOperation.CreateSession => SpannerOperationClassifier.ClassifyGrpc("CreateSession", null, "shop"),
        SpannerOperation.DeleteSession => SpannerOperationClassifier.ClassifyGrpc("DeleteSession", null, "shop"),
        // No classifier gives these two: the interceptor and the observers name them from a commit's first mutation.
        SpannerOperation.InsertOrUpdate or SpannerOperation.Replace => new SpannerOperationInfo(member, "Orders", "shop"),
        _ => null,
    },
    info => info.Operation,
    info => spanner.LogRequest(info, info.SqlText ?? "content"),
    (info, id, trace) => spanner.LogResponse(info, id, trace, "1 row"));

// ---------------------------------------------------------------------------- BigQuery: the tracker, by hand

const string BigQueryPackage = "Kronikol.Extensions.BigQuery";
var bigQuery = new BigQueryTracker(new BigQueryTrackingMessageHandlerOptions { Verbosity = BigQueryTrackingVerbosity.Detailed, CurrentTestInfoFetcher = Current });
EachMember<BigQueryOperation, BigQueryOperationInfo>(BigQueryPackage, nameof(BigQueryTracker),
    member => member == BigQueryOperation.Query
        ? new BigQueryOperationInfo(member, "query", null, "shop", null)
        : new BigQueryOperationInfo(member, "table", "orders", "shop", "sales"),
    info => info.Operation,
    info => bigQuery.LogRequest(info, "content"),
    (info, id, trace) => bigQuery.LogResponse(info, id, trace, "answer"));

// ---------------------------------------------------------------------------- BigQuery: the handler, the automatic route

var bigQueryOptions = new BigQueryTrackingMessageHandlerOptions { Verbosity = BigQueryTrackingVerbosity.Detailed, CurrentTestInfoFetcher = Current };
var bigQueryStub = new Stub(readBody: false, (request, _) => BigQueryOperationClassifier.Classify(request).Operation.ToString(), BigQueryAnswer);
var bigQueryClient = new BigQueryClientBuilder
{
    ProjectId = "shop",
    ApiKey = "any",
    HttpClientFactory = new BigQueryFactory(bigQueryOptions, bigQueryStub),
}.Build();
var bigQuerySchema = new TableSchemaBuilder { { "id", BigQueryDbType.String } }.Build();

foreach (var (call, run) in new (string, Action)[]
{
    ("CreateDataset", () => bigQueryClient.CreateDataset("sales")),
    ("GetDataset", () => bigQueryClient.GetDataset("sales")),
    ("ListDatasets", () => bigQueryClient.ListDatasets().Take(1).ToList()),
    ("CreateTable", () => bigQueryClient.CreateTable("sales", "orders", bigQuerySchema)),
    ("GetTable", () => bigQueryClient.GetTable("sales", "orders")),
    ("ListTables", () => bigQueryClient.ListTables("sales").Take(1).ToList()),
    ("PatchTable", () => bigQueryClient.PatchTable("sales", "orders", new BqData.Table { Description = "d" })),
    ("InsertRow", () => bigQueryClient.InsertRow("sales", "orders", new BigQueryInsertRow { { "id", "1" } })),
    ("ListRows", () => bigQueryClient.ListRows("sales", "orders", bigQuerySchema).Take(1).ToList()),
    ("ExecuteQuery", () => bigQueryClient.ExecuteQuery("SELECT 1", parameters: null).ToList()),
    ("CreateQueryJob", () => bigQueryClient.CreateQueryJob("SELECT 1", parameters: null)),
    ("Jobs.Query", () => bigQueryClient.Service.Jobs.Query(new BqData.QueryRequest { Query = "SELECT 1", UseLegacySql = false }, "shop").Execute()),
    ("GetJob", () => bigQueryClient.GetJob("job_1")),
    ("ListJobs", () => bigQueryClient.ListJobs().Take(1).ToList()),
    ("CancelJob", () => bigQueryClient.CancelJob("job_1")),
    ("DeleteJob", () => bigQueryClient.DeleteJob("job_1")),
    ("UploadCsv", () => bigQueryClient.UploadCsv("sales", "orders", bigQuerySchema, new MemoryStream(Encoding.UTF8.GetBytes("1\n")))),
    ("ListProjects", () => bigQueryClient.ListProjects().Take(1).ToList()),
    ("ServiceAccountEmail", () => bigQueryClient.GetBigQueryServiceAccountEmail()),
    ("ListModels", () => bigQueryClient.ListModels("sales").Take(1).ToList()),
    ("ListRoutines", () => bigQueryClient.ListRoutines("sales").Take(1).ToList()),
    ("Tables.GetIamPolicy", () => bigQueryClient.Service.Tables.GetIamPolicy(new BqData.GetIamPolicyRequest(), "projects/shop/datasets/sales/tables/orders").Execute()),
    ("Datasets.Undelete", () => bigQueryClient.Service.Datasets.Undelete(new BqData.UndeleteDatasetRequest(), "shop", "sales").Execute()),
    ("RowAccessPolicies.List", () => bigQueryClient.Service.RowAccessPolicies.List("shop", "sales", "orders").Execute()),
    ("DeleteTable", () => bigQueryClient.DeleteTable("sales", "orders")),
    ("DeleteDataset", () => bigQueryClient.DeleteDataset("sales")),
})
    Sdk(BigQueryPackage, nameof(BigQueryTrackingMessageHandler), bigQueryStub, call, run);

// No method of the client reaches Other: it is a call the classifier cannot name, as a method a later API version adds.
var bigQueryInvoker = new HttpMessageInvoker(new BigQueryTrackingMessageHandler(bigQueryOptions, bigQueryStub));
Sdk(BigQueryPackage, nameof(BigQueryTrackingMessageHandler), bigQueryStub, "hand-shaped-unknown-collection", () =>
    bigQueryInvoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://bigquery.googleapis.com/bigquery/v2/projects/shop/futureCollection"), default).GetAwaiter().GetResult());
Covered<BigQueryOperation>(BigQueryPackage, nameof(BigQueryTrackingMessageHandler));

// ---------------------------------------------------------------------------- Bigtable: the tracker

const string BigtableTable = "projects/shop/instances/main/tables/orders";
var bigtable = new BigtableTracker(new BigtableTrackingOptions { Verbosity = BigtableTrackingVerbosity.Detailed, CurrentTestInfoFetcher = Current });
EachMember<BigtableOperation, BigtableOperationInfo>("Kronikol.Extensions.Bigtable", nameof(BigtableTracker),
    member => member switch
    {
        BigtableOperation.MutateRows => BigtableOperationClassifier.Classify("MutateRows", BigtableTable, null, 3),
        BigtableOperation.Other => BigtableOperationClassifier.Classify("GenerateInitialChangeStreamPartitions", BigtableTable),
        _ => BigtableOperationClassifier.Classify(member.ToString(), BigtableTable, "row-1"),
    },
    info => info.Operation,
    info => bigtable.LogRequest(info, "content"),
    (info, id, trace) => bigtable.LogResponse(info, id, trace, "answer"));

// ---------------------------------------------------------------------------- Pub/Sub: the tracker

const string PubSubTopic = "projects/shop/topics/orders";
const string PubSubSubscription = "projects/shop/subscriptions/orders-sub";
var pubSub = new PubSubTracker(new PubSubTrackingOptions { Verbosity = PubSubTrackingVerbosity.Detailed, CurrentTestInfoFetcher = Current });
EachMember<PubSubOperation, PubSubOperationInfo>("Kronikol.Extensions.PubSub", nameof(PubSubTracker),
    member => member switch
    {
        PubSubOperation.Publish => PubSubOperationClassifier.Classify("PublishAsync", PubSubTopic, null, 1),
        PubSubOperation.PublishBatch => PubSubOperationClassifier.Classify("PublishAsync", PubSubTopic, null, 3),
        PubSubOperation.Pull => PubSubOperationClassifier.Classify("PullAsync", null, PubSubSubscription, 1),
        PubSubOperation.Acknowledge => PubSubOperationClassifier.Classify("AcknowledgeAsync", null, PubSubSubscription, 1),
        PubSubOperation.ModifyAckDeadline => PubSubOperationClassifier.Classify("ModifyAckDeadlineAsync", null, PubSubSubscription, 1),
        PubSubOperation.Receive => PubSubOperationClassifier.Classify("Receive", null, PubSubSubscription, 1),
        PubSubOperation.StartSubscriber => PubSubOperationClassifier.Classify("StartAsync", null, PubSubSubscription, null),
        PubSubOperation.StopSubscriber => PubSubOperationClassifier.Classify("StopAsync", null, PubSubSubscription, null),
        PubSubOperation.Other => PubSubOperationClassifier.Classify("SeekAsync", null, PubSubSubscription, null),
        _ => null,
    },
    info => info.Operation,
    info => pubSub.LogRequest(info, "content"),
    (info, id, trace) => pubSub.LogResponse(info, id, trace, "answer"));

// ---------------------------------------------------------------------------- Service Bus: the tracker

const string ServiceBusQueue = "orders";
var serviceBus = new ServiceBusTracker(new ServiceBusTrackingOptions { Verbosity = ServiceBusTrackingVerbosity.Detailed, CurrentTestInfoFetcher = Current });
EachMember<ServiceBusOperation, ServiceBusOperationInfo>("Kronikol.Extensions.ServiceBus", nameof(ServiceBusTracker),
    member =>
    {
        var method = member switch
        {
            ServiceBusOperation.Send => "SendMessageAsync",
            ServiceBusOperation.SendBatch => "SendMessagesAsync",
            ServiceBusOperation.Schedule => "ScheduleMessageAsync",
            ServiceBusOperation.CancelSchedule => "CancelScheduledMessageAsync",
            ServiceBusOperation.Receive => "ReceiveMessageAsync",
            ServiceBusOperation.ReceiveBatch => "ReceiveMessagesAsync",
            ServiceBusOperation.Peek => "PeekMessageAsync",
            ServiceBusOperation.Complete => "CompleteMessageAsync",
            ServiceBusOperation.Abandon => "AbandonMessageAsync",
            ServiceBusOperation.DeadLetter => "DeadLetterMessageAsync",
            ServiceBusOperation.Defer => "DeferMessageAsync",
            ServiceBusOperation.RenewMessageLock => "RenewMessageLockAsync",
            ServiceBusOperation.RenewSessionLock => "RenewSessionLockAsync",
            ServiceBusOperation.GetSessionState => "GetSessionStateAsync",
            ServiceBusOperation.SetSessionState => "SetSessionStateAsync",
            ServiceBusOperation.StartProcessing => "StartProcessingAsync",
            ServiceBusOperation.StopProcessing => "StopProcessingAsync",
            ServiceBusOperation.Other => "ReceiveDeferredMessageAsync",
            _ => null,
        };
        if (method is null)
            return null;
        var info = ServiceBusOperationClassifier.Classify(method, ServiceBusQueue, null);
        // As TrackingServiceBusSender does for a batch.
        return member == ServiceBusOperation.SendBatch ? info with { MessageCount = 3 } : info;
    },
    info => info.Operation,
    info => serviceBus.LogRequest(info, "content"),
    (info, id, trace) => serviceBus.LogResponse(info, id, trace, "answer"));

// ---------------------------------------------------------------------------- Event Hubs: the tracker

const string EventHub = "orders";
var eventHubs = new EventHubsTracker(new EventHubsTrackingOptions { Verbosity = EventHubsTrackingVerbosity.Detailed, CurrentTestInfoFetcher = Current });
EachMember<EventHubsOperation, EventHubsOperationInfo>("Kronikol.Extensions.EventHubs", nameof(EventHubsTracker),
    member => member switch
    {
        EventHubsOperation.Send => EventHubsOperationClassifier.Classify("SendAsync", EventHub, null, 1),
        EventHubsOperation.SendBatch => EventHubsOperationClassifier.Classify("SendAsync", EventHub, null, 3),
        EventHubsOperation.CreateBatch => EventHubsOperationClassifier.Classify("CreateBatchAsync", EventHub),
        EventHubsOperation.ReadEvents => EventHubsOperationClassifier.Classify("ReadEventsAsync", EventHub),
        EventHubsOperation.ReadEventsFromPartition => EventHubsOperationClassifier.Classify("ReadEventsFromPartitionAsync", EventHub, "0"),
        EventHubsOperation.GetPartitionIds => EventHubsOperationClassifier.Classify("GetPartitionIdsAsync", EventHub),
        EventHubsOperation.GetEventHubProperties => EventHubsOperationClassifier.Classify("GetEventHubPropertiesAsync", EventHub),
        EventHubsOperation.GetPartitionProperties => EventHubsOperationClassifier.Classify("GetPartitionPropertiesAsync", EventHub, "0"),
        EventHubsOperation.StartProcessing => EventHubsOperationClassifier.Classify("StartProcessingAsync", EventHub),
        EventHubsOperation.StopProcessing => EventHubsOperationClassifier.Classify("StopProcessingAsync", EventHub),
        EventHubsOperation.ProcessEvent => EventHubsOperationClassifier.Classify("ProcessEvent", EventHub, "0", 1),
        EventHubsOperation.Other => EventHubsOperationClassifier.Classify("CloseAsync", EventHub),
        _ => null,
    },
    info => info.Operation,
    info => eventHubs.LogRequest(info, "content"),
    (info, id, trace) => eventHubs.LogResponse(info, id, trace, "answer"));

// ---------------------------------------------------------------------------- Kafka: the tracker's public entries

const string KafkaPackage = "Kronikol.Extensions.Kafka";
var kafka = new KafkaTracker(new KafkaTrackingOptions
{
    Verbosity = KafkaTrackingVerbosity.Detailed,
    CurrentTestInfoFetcher = Current,
    // Off by default, and each logs nothing while off.
    TrackSubscribe = true,
    TrackUnsubscribe = true,
    TrackCommit = true,
    TrackFlush = true,
    TrackTransactions = true,
});

KafkaOperationInfo KafkaOn(KafkaOperation member) => new(member, "orders", 0, 7);
KafkaOperationInfo KafkaBare(KafkaOperation member) => new(member);
Message<string, string> KafkaMessage() => new() { Key = "k", Value = "v" };

Tracked(KafkaPackage, "KafkaTracker.LogProduce", nameof(KafkaOperation.Produce), () => kafka.LogProduce(KafkaOn(KafkaOperation.Produce), "v"));
Tracked(KafkaPackage, "KafkaTracker.LogProduce", nameof(KafkaOperation.ProduceAsync), () => kafka.LogProduce(KafkaOn(KafkaOperation.ProduceAsync), "v"));
Tracked(KafkaPackage, "KafkaTracker.LogConsume", nameof(KafkaOperation.Consume), () => kafka.LogConsume(KafkaOn(KafkaOperation.Consume), "v"));
Tracked(KafkaPackage, "KafkaTracker.LogSubscribe", nameof(KafkaOperation.Subscribe), () => kafka.LogSubscribe(new KafkaOperationInfo(KafkaOperation.Subscribe, "orders")));
Tracked(KafkaPackage, "KafkaTracker.LogUnsubscribe", nameof(KafkaOperation.Unsubscribe), () => kafka.LogUnsubscribe(KafkaBare(KafkaOperation.Unsubscribe)));
Tracked(KafkaPackage, "KafkaTracker.LogCommit", nameof(KafkaOperation.Commit), () => kafka.LogCommit(KafkaBare(KafkaOperation.Commit)));
Tracked(KafkaPackage, "KafkaTracker.LogFlush", nameof(KafkaOperation.Flush), () => kafka.LogFlush(KafkaBare(KafkaOperation.Flush)));
foreach (var member in new[] { KafkaOperation.InitTransactions, KafkaOperation.BeginTransaction, KafkaOperation.CommitTransaction, KafkaOperation.AbortTransaction, KafkaOperation.SendOffsetsToTransaction })
    Tracked(KafkaPackage, "KafkaTracker.LogTransaction", member.ToString(), () => kafka.LogTransaction(KafkaBare(member)));
// Other has no entry of its own: LogProduce takes any operation.
Tracked(KafkaPackage, "KafkaTracker.LogProduce", nameof(KafkaOperation.Other), () => kafka.LogProduce(new KafkaOperationInfo(KafkaOperation.Other, "orders"), "v"));

// The entries for a client this package does not wrap (#160), with no broker: the message objects are plain.
Tracked(KafkaPackage, "KafkaTracker.TrackProduce.Sent", nameof(KafkaOperation.Produce), () => kafka.TrackProduce("orders", KafkaMessage(), KafkaOperation.Produce).Sent());
Tracked(KafkaPackage, "KafkaTracker.TrackProduce.Delivered", nameof(KafkaOperation.ProduceAsync), () =>
    kafka.TrackProduce("orders", KafkaMessage(), KafkaOperation.ProduceAsync)
        .Delivered(new DeliveryResult<string, string> { Topic = "orders", Partition = new Partition(0), Offset = new Offset(7) }));
Tracked(KafkaPackage, "KafkaTracker.TrackProduce.Failed", nameof(KafkaOperation.Produce), () =>
    kafka.TrackProduce("orders", KafkaMessage(), KafkaOperation.Produce).Failed(new InvalidOperationException("broker down")));
Tracked(KafkaPackage, "KafkaTracker.TrackConsume", nameof(KafkaOperation.Consume), () =>
{
    using var scope = kafka.TrackConsume(new ConsumeResult<string, string>
    {
        TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(0), new Offset(7)),
        Message = KafkaMessage(),
    });
});
Covered<KafkaOperation>(KafkaPackage, "KafkaTracker");

// ---------------------------------------------------------------------------- MassTransit: the tracker's public entries

const string MassTransitPackage = "Kronikol.Extensions.MassTransit";
var massTransit = new MassTransitTracker(new MassTransitTrackingOptions { Verbosity = MassTransitTrackingVerbosity.Detailed, CurrentTestInfoFetcher = Current });
var fault = new InvalidOperationException("broker down");

MassTransitOperationInfo MassTransitOn(MassTransitOperation member) =>
    new(member, "OrderPlaced", new Uri("rabbitmq://localhost/orders"), new Uri("rabbitmq://localhost/source"), Guid.NewGuid(), Guid.NewGuid());

Tracked(MassTransitPackage, "MassTransitTracker.LogSend", nameof(MassTransitOperation.Send), () => massTransit.LogSend(MassTransitOn(MassTransitOperation.Send), new { Id = 1 }));
Tracked(MassTransitPackage, "MassTransitTracker.LogPublish", nameof(MassTransitOperation.Publish), () => massTransit.LogPublish(MassTransitOn(MassTransitOperation.Publish), new { Id = 1 }));
Tracked(MassTransitPackage, "MassTransitTracker.LogConsume", nameof(MassTransitOperation.Consume), () => massTransit.LogConsume(MassTransitOn(MassTransitOperation.Consume), new { Id = 1 }));
Tracked(MassTransitPackage, "MassTransitTracker.LogSendFault", nameof(MassTransitOperation.SendFault), () => massTransit.LogSendFault(MassTransitOn(MassTransitOperation.SendFault), fault));
Tracked(MassTransitPackage, "MassTransitTracker.LogPublishFault", nameof(MassTransitOperation.PublishFault), () => massTransit.LogPublishFault(MassTransitOn(MassTransitOperation.PublishFault), fault));
Tracked(MassTransitPackage, "MassTransitTracker.LogConsumeFault", nameof(MassTransitOperation.ConsumeFault), () => massTransit.LogConsumeFault(MassTransitOn(MassTransitOperation.ConsumeFault), fault));
// Other has no entry of its own: LogSend takes any operation.
Tracked(MassTransitPackage, "MassTransitTracker.LogSend", nameof(MassTransitOperation.Other), () => massTransit.LogSend(MassTransitOn(MassTransitOperation.Other), new { Id = 1 }));
Covered<MassTransitOperation>(MassTransitPackage, "MassTransitTracker");

// ---------------------------------------------------------------------------- the AWS handlers, behind the real clients

var awsCredentials = new Amazon.Runtime.BasicAWSCredentials("AKIDEXAMPLE", "secret");
const string AwsAccount = "123456789012";

// SQS
const string SqsPackage = "Kronikol.Extensions.SQS";
var sqsStub = new Stub(readBody: true, (request, body) => SqsOperationClassifier.Classify(request, body).Operation.ToString(), _ => Answer("{}", "application/x-amz-json-1.0"));
var sqs = new AmazonSQSClient(awsCredentials, new AmazonSQSConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new SqsTrackingMessageHandler(
        new SqsTrackingMessageHandlerOptions { Verbosity = SqsTrackingVerbosity.Detailed, CurrentTestInfoFetcher = Current }, sqsStub)),
});
var queueUrl = $"https://sqs.us-east-1.amazonaws.com/{AwsAccount}/orders";

foreach (var (call, run) in new (string, Func<Task>)[]
{
    ("SendMessage", () => sqs.SendMessageAsync(new SqsModel.SendMessageRequest { QueueUrl = queueUrl, MessageBody = "hello" })),
    ("SendMessageBatch", () => sqs.SendMessageBatchAsync(new SqsModel.SendMessageBatchRequest { QueueUrl = queueUrl, Entries = [new SqsModel.SendMessageBatchRequestEntry { Id = "1", MessageBody = "hello" }] })),
    ("ReceiveMessage", () => sqs.ReceiveMessageAsync(new SqsModel.ReceiveMessageRequest { QueueUrl = queueUrl })),
    ("DeleteMessage", () => sqs.DeleteMessageAsync(new SqsModel.DeleteMessageRequest { QueueUrl = queueUrl, ReceiptHandle = "rh" })),
    ("DeleteMessageBatch", () => sqs.DeleteMessageBatchAsync(new SqsModel.DeleteMessageBatchRequest { QueueUrl = queueUrl, Entries = [new SqsModel.DeleteMessageBatchRequestEntry { Id = "1", ReceiptHandle = "rh" }] })),
    ("ChangeMessageVisibility", () => sqs.ChangeMessageVisibilityAsync(new SqsModel.ChangeMessageVisibilityRequest { QueueUrl = queueUrl, ReceiptHandle = "rh", VisibilityTimeout = 5 })),
    ("ChangeMessageVisibilityBatch", () => sqs.ChangeMessageVisibilityBatchAsync(new SqsModel.ChangeMessageVisibilityBatchRequest { QueueUrl = queueUrl, Entries = [new SqsModel.ChangeMessageVisibilityBatchRequestEntry { Id = "1", ReceiptHandle = "rh", VisibilityTimeout = 5 }] })),
    ("CreateQueue", () => sqs.CreateQueueAsync(new SqsModel.CreateQueueRequest { QueueName = "orders" })),
    ("DeleteQueue", () => sqs.DeleteQueueAsync(new SqsModel.DeleteQueueRequest { QueueUrl = queueUrl })),
    ("GetQueueUrl", () => sqs.GetQueueUrlAsync(new SqsModel.GetQueueUrlRequest { QueueName = "orders" })),
    ("GetQueueAttributes", () => sqs.GetQueueAttributesAsync(new SqsModel.GetQueueAttributesRequest { QueueUrl = queueUrl, AttributeNames = ["All"] })),
    ("SetQueueAttributes", () => sqs.SetQueueAttributesAsync(new SqsModel.SetQueueAttributesRequest { QueueUrl = queueUrl, Attributes = new() { ["VisibilityTimeout"] = "5" } })),
    ("PurgeQueue", () => sqs.PurgeQueueAsync(new SqsModel.PurgeQueueRequest { QueueUrl = queueUrl })),
    ("ListQueues", () => sqs.ListQueuesAsync(new SqsModel.ListQueuesRequest())),
    ("ListQueueTags", () => sqs.ListQueueTagsAsync(new SqsModel.ListQueueTagsRequest { QueueUrl = queueUrl })),
})
    Sdk(SqsPackage, nameof(SqsTrackingMessageHandler), sqsStub, call, () => run().GetAwaiter().GetResult());
Covered<SqsOperation>(SqsPackage, nameof(SqsTrackingMessageHandler));

// SNS
const string SnsPackage = "Kronikol.Extensions.SNS";
var snsStub = new Stub(readBody: true, (request, body) => SnsOperationClassifier.Classify(request, body).Operation.ToString(), _ => Answer("<Ok/>", "text/xml"));
var sns = new AmazonSimpleNotificationServiceClient(awsCredentials, new AmazonSimpleNotificationServiceConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new SnsTrackingMessageHandler(
        new SnsTrackingMessageHandlerOptions { Verbosity = SnsTrackingVerbosity.Detailed, CurrentTestInfoFetcher = Current }, snsStub)),
});
var topicArn = $"arn:aws:sns:us-east-1:{AwsAccount}:orders";
var subscriptionArn = $"{topicArn}:6b0e71bd-7e97-4d97-80ce-4a0994e55286";

foreach (var (call, run) in new (string, Func<Task>)[]
{
    ("Publish", () => sns.PublishAsync(new SnsModel.PublishRequest { TopicArn = topicArn, Message = "hello" })),
    ("PublishBatch", () => sns.PublishBatchAsync(new SnsModel.PublishBatchRequest { TopicArn = topicArn, PublishBatchRequestEntries = [new SnsModel.PublishBatchRequestEntry { Id = "1", Message = "hello" }] })),
    ("Subscribe", () => sns.SubscribeAsync(new SnsModel.SubscribeRequest { TopicArn = topicArn, Protocol = "sqs", Endpoint = $"arn:aws:sqs:us-east-1:{AwsAccount}:orders" })),
    ("Unsubscribe", () => sns.UnsubscribeAsync(new SnsModel.UnsubscribeRequest { SubscriptionArn = subscriptionArn })),
    ("CreateTopic", () => sns.CreateTopicAsync(new SnsModel.CreateTopicRequest { Name = "orders" })),
    ("DeleteTopic", () => sns.DeleteTopicAsync(new SnsModel.DeleteTopicRequest { TopicArn = topicArn })),
    ("ListTopics", () => sns.ListTopicsAsync(new SnsModel.ListTopicsRequest())),
    ("ListSubscriptions", () => sns.ListSubscriptionsAsync(new SnsModel.ListSubscriptionsRequest())),
    ("ListSubscriptionsByTopic", () => sns.ListSubscriptionsByTopicAsync(new SnsModel.ListSubscriptionsByTopicRequest { TopicArn = topicArn })),
    ("GetTopicAttributes", () => sns.GetTopicAttributesAsync(new SnsModel.GetTopicAttributesRequest { TopicArn = topicArn })),
    ("SetTopicAttributes", () => sns.SetTopicAttributesAsync(new SnsModel.SetTopicAttributesRequest { TopicArn = topicArn, AttributeName = "DisplayName", AttributeValue = "orders" })),
    ("ConfirmSubscription", () => sns.ConfirmSubscriptionAsync(new SnsModel.ConfirmSubscriptionRequest { TopicArn = topicArn, Token = "t" })),
    ("ListTagsForResource", () => sns.ListTagsForResourceAsync(new SnsModel.ListTagsForResourceRequest { ResourceArn = topicArn })),
})
    Sdk(SnsPackage, nameof(SnsTrackingMessageHandler), snsStub, call, () => run().GetAwaiter().GetResult());
Covered<SnsOperation>(SnsPackage, nameof(SnsTrackingMessageHandler));

// EventBridge: four operations are excluded by default, and each logs nothing while excluded.
const string EventBridgePackage = "Kronikol.Extensions.EventBridge";
var eventBridgeStub = new Stub(readBody: true, (request, body) => EventBridgeOperationClassifier.Classify(request, body).Operation.ToString(), _ => Answer("{}", "application/x-amz-json-1.1"));
var eventBridge = new AmazonEventBridgeClient(awsCredentials, new AmazonEventBridgeConfig
{
    RegionEndpoint = Amazon.RegionEndpoint.USEast1,
    MaxErrorRetry = 0,
    HttpClientFactory = new AwsFactory(new EventBridgeTrackingMessageHandler(
        new EventBridgeTrackingMessageHandlerOptions { Verbosity = EventBridgeTrackingVerbosity.Detailed, CurrentTestInfoFetcher = Current, ExcludedOperations = [] }, eventBridgeStub)),
});
var busArn = $"arn:aws:events:us-east-1:{AwsAccount}:event-bus/orders-bus";
var archiveArn = $"arn:aws:events:us-east-1:{AwsAccount}:archive/orders-archive";
var connectionArn = $"arn:aws:events:us-east-1:{AwsAccount}:connection/orders-connection";

foreach (var (call, run) in new (string, Func<Task>)[]
{
    ("PutEvents", () => eventBridge.PutEventsAsync(new EbModel.PutEventsRequest { Entries = [new EbModel.PutEventsRequestEntry { EventBusName = "orders-bus", Source = "shop", DetailType = "OrderPlaced", Detail = "{}" }] })),
    ("PutPartnerEvents", () => eventBridge.PutPartnerEventsAsync(new EbModel.PutPartnerEventsRequest { Entries = [new EbModel.PutPartnerEventsRequestEntry { Source = "aws.partner/shop.example/orders", DetailType = "OrderPlaced", Detail = "{}" }] })),
    ("TestEventPattern", () => eventBridge.TestEventPatternAsync(new EbModel.TestEventPatternRequest { Event = "{}", EventPattern = "{}" })),
    ("PutRule", () => eventBridge.PutRuleAsync(new EbModel.PutRuleRequest { Name = "orders-rule", EventBusName = "orders-bus", EventPattern = "{}" })),
    ("DeleteRule", () => eventBridge.DeleteRuleAsync(new EbModel.DeleteRuleRequest { Name = "orders-rule", EventBusName = "orders-bus" })),
    ("DescribeRule", () => eventBridge.DescribeRuleAsync(new EbModel.DescribeRuleRequest { Name = "orders-rule", EventBusName = "orders-bus" })),
    ("EnableRule", () => eventBridge.EnableRuleAsync(new EbModel.EnableRuleRequest { Name = "orders-rule", EventBusName = "orders-bus" })),
    ("DisableRule", () => eventBridge.DisableRuleAsync(new EbModel.DisableRuleRequest { Name = "orders-rule", EventBusName = "orders-bus" })),
    ("ListRules", () => eventBridge.ListRulesAsync(new EbModel.ListRulesRequest { EventBusName = "orders-bus" })),
    ("PutTargets", () => eventBridge.PutTargetsAsync(new EbModel.PutTargetsRequest { Rule = "orders-rule", EventBusName = "orders-bus", Targets = [new EbModel.Target { Id = "1", Arn = $"arn:aws:sqs:us-east-1:{AwsAccount}:orders" }] })),
    ("RemoveTargets", () => eventBridge.RemoveTargetsAsync(new EbModel.RemoveTargetsRequest { Rule = "orders-rule", EventBusName = "orders-bus", Ids = ["1"] })),
    ("ListTargetsByRule", () => eventBridge.ListTargetsByRuleAsync(new EbModel.ListTargetsByRuleRequest { Rule = "orders-rule", EventBusName = "orders-bus" })),
    ("CreateEventBus", () => eventBridge.CreateEventBusAsync(new EbModel.CreateEventBusRequest { Name = "orders-bus" })),
    ("DeleteEventBus", () => eventBridge.DeleteEventBusAsync(new EbModel.DeleteEventBusRequest { Name = "orders-bus" })),
    ("DescribeEventBus", () => eventBridge.DescribeEventBusAsync(new EbModel.DescribeEventBusRequest { Name = "orders-bus" })),
    ("ListEventBuses", () => eventBridge.ListEventBusesAsync(new EbModel.ListEventBusesRequest())),
    ("CreateArchive", () => eventBridge.CreateArchiveAsync(new EbModel.CreateArchiveRequest { ArchiveName = "orders-archive", EventSourceArn = busArn })),
    ("DeleteArchive", () => eventBridge.DeleteArchiveAsync(new EbModel.DeleteArchiveRequest { ArchiveName = "orders-archive" })),
    ("DescribeArchive", () => eventBridge.DescribeArchiveAsync(new EbModel.DescribeArchiveRequest { ArchiveName = "orders-archive" })),
    ("ListArchives", () => eventBridge.ListArchivesAsync(new EbModel.ListArchivesRequest())),
    ("StartReplay", () => eventBridge.StartReplayAsync(new EbModel.StartReplayRequest { ReplayName = "orders-replay", EventSourceArn = archiveArn, EventStartTime = DateTime.UtcNow.AddHours(-1), EventEndTime = DateTime.UtcNow, Destination = new EbModel.ReplayDestination { Arn = busArn } })),
    ("DescribeReplay", () => eventBridge.DescribeReplayAsync(new EbModel.DescribeReplayRequest { ReplayName = "orders-replay" })),
    ("ListReplays", () => eventBridge.ListReplaysAsync(new EbModel.ListReplaysRequest())),
    ("CreateApiDestination", () => eventBridge.CreateApiDestinationAsync(new EbModel.CreateApiDestinationRequest { Name = "orders-destination", ConnectionArn = connectionArn, HttpMethod = ApiDestinationHttpMethod.POST, InvocationEndpoint = "https://shop.example/orders" })),
    ("CreateConnection", () => eventBridge.CreateConnectionAsync(new EbModel.CreateConnectionRequest { Name = "orders-connection", AuthorizationType = ConnectionAuthorizationType.API_KEY, AuthParameters = new EbModel.CreateConnectionAuthRequestParameters { ApiKeyAuthParameters = new EbModel.CreateConnectionApiKeyAuthRequestParameters { ApiKeyName = "k", ApiKeyValue = "v" } } })),
    ("TagResource", () => eventBridge.TagResourceAsync(new EbModel.TagResourceRequest { ResourceARN = busArn, Tags = [new EbModel.Tag { Key = "k", Value = "v" }] })),
    ("UntagResource", () => eventBridge.UntagResourceAsync(new EbModel.UntagResourceRequest { ResourceARN = busArn, TagKeys = ["k"] })),
    ("ListTagsForResource", () => eventBridge.ListTagsForResourceAsync(new EbModel.ListTagsForResourceRequest { ResourceARN = busArn })),
    ("ListConnections", () => eventBridge.ListConnectionsAsync(new EbModel.ListConnectionsRequest())),
})
    Sdk(EventBridgePackage, nameof(EventBridgeTrackingMessageHandler), eventBridgeStub, call, () => run().GetAwaiter().GetResult());
Covered<EventBridgeOperation>(EventBridgePackage, nameof(EventBridgeTrackingMessageHandler));

// ---------------------------------------------------------------------------- Storage Queues: the handler, behind the real client

const string StorageQueuesPackage = "Kronikol.Extensions.StorageQueues";
const string AzureAccount = "DefaultEndpointsProtocol=https;AccountName=acct;AccountKey=a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5;EndpointSuffix=core.windows.net";
var queuesStub = new Stub(readBody: false, (request, _) => StorageQueueOperationClassifier.Classify(request).Operation.ToString(), AzureQueueAnswer);
var queuesOptions = new QueueClientOptions().WithTestTracking(
    new StorageQueueTrackingMessageHandlerOptions { Verbosity = StorageQueueTrackingVerbosity.Detailed, CurrentTestInfoFetcher = Current }, queuesStub);
queuesOptions.Retry.MaxRetries = 0;
var queue = new QueueClient(AzureAccount, "orders", queuesOptions);
var queueService = new QueueServiceClient(AzureAccount, queuesOptions);

foreach (var (call, run) in new (string, Action)[]
{
    ("CreateQueue", () => queue.Create()),
    ("SendMessage", () => queue.SendMessage("hello")),
    ("ReceiveMessages", () => queue.ReceiveMessages()),
    ("PeekMessages", () => queue.PeekMessages()),
    ("UpdateMessage", () => queue.UpdateMessage("m1", "pop1", "changed")),
    ("DeleteMessage", () => queue.DeleteMessage("m1", "pop1")),
    ("ClearMessages", () => queue.ClearMessages()),
    ("GetProperties", () => queue.GetProperties()),
    ("SetMetadata", () => queue.SetMetadata(new Dictionary<string, string> { ["k"] = "v" })),
    ("ListQueues", () => queueService.GetQueues().Take(1).ToList()),
    ("GetAccessPolicy", () => queue.GetAccessPolicy()),
    ("DeleteQueue", () => queue.Delete()),
})
    Sdk(StorageQueuesPackage, nameof(StorageQueueTrackingMessageHandler), queuesStub, call, run);
Covered<StorageQueueOperation>(StorageQueuesPackage, nameof(StorageQueueTrackingMessageHandler));

// ---------------------------------------------------------------------------- the core MessageTracker

// Its label and URI are the caller's. A scheme outside the table, as Example.Api's event:// calls; then one of the table,
// with the labels the method docs give as examples.
const string CorePackage = "Kronikol";
var messages = new MessageTracker(new MessageTrackerOptions { Verbosity = MessageTrackerVerbosity.Detailed, CurrentTestInfoFetcher = Current });
foreach (var (shape, sendLabel, consumeLabel, destination, uri) in new[]
{
    ("event://", "Publish", "Consume", "Event broker", new Uri("event://cake_events")),
    ("kafka://", "Kafka", "Consume (Kafka)", "OrderService", new Uri("kafka:///orders")),
})
{
    Tracked(CorePackage, "MessageTracker.TrackSendEvent", $"TrackSendEvent[{shape}]", () => messages.TrackSendEvent(sendLabel, destination, uri, new { Id = 1 }));
    Tracked(CorePackage, "MessageTracker.TrackSendMessage", $"TrackSendMessage[{shape}]", () => messages.TrackSendMessage(sendLabel, destination, uri, new { Id = 1 }));
    Tracked(CorePackage, "MessageTracker.TrackConsumeEvent", $"TrackConsumeEvent[{shape}]", () => messages.TrackConsumeEvent(consumeLabel, destination, uri, new { Id = 1 }));
    Tracked(CorePackage, "MessageTracker.TrackMessageRequest+TrackMessageResponse", $"TrackMessageRequest+TrackMessageResponse[{shape}]", () =>
    {
        var id = messages.TrackMessageRequest(sendLabel, destination, uri, new { Id = 1 });
        if (id == Guid.Empty)
            throw new InvalidOperationException("TrackMessageRequest returned no id: a phase gate or no test identity");
        messages.TrackMessageResponse(sendLabel, destination, uri, id);
    });
}

// ---------------------------------------------------------------------------- the file

string[] header = ["package", "route", "operation", "label", "category", "statementShaped", "uriScheme", "requestKind", "responseKind", "responseLogged", "tableVerdict", "otlpSpanKind", "otlpDbSystem"];
var lines = new List<string> { string.Join('\t', header) };
lines.AddRange(rows.Select(row => string.Join('\t', row)));
var outPath = Path.GetFullPath(args[0]);
Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
File.WriteAllText(outPath, string.Join('\n', lines) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

Console.WriteLine($"{rows.Count} rows, {outPath}");
foreach (var group in rows.GroupBy(row => row[0]))
    Console.WriteLine($"  {group.Key}: {group.Count()}");
Console.WriteLine($"{sdkNotes.Count} SDK calls threw after the handler logged them (the stub answers only what the handler needs):");
foreach (var note in sdkNotes)
    Console.WriteLine($"  {note}");
Console.WriteLine($"{pairingNotes.Count} pairs the OTLP mapper does not read as one call:");
foreach (var note in pairingNotes)
    Console.WriteLine($"  {note}");

if (failures.Count == 0)
    return 0;
Console.Error.WriteLine($"{failures.Count} FAILED:");
foreach (var failure in failures)
    Console.Error.WriteLine($"  {failure}");
return 1;

// ---------------------------------------------------------------------------- the rows

static string VersionOf(Type type) =>
    type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? type.Assembly.GetName().Version?.ToString() ?? "unknown";

static string OneLine(string text) => text.ReplaceLineEndings(" ").Replace('\t', ' ');

string TableVerdict(string scheme, string label) =>
    (bool)hasScheme.Invoke(null, [scheme])! ? read.Invoke(null, [scheme, label])!.ToString()! : "-";

void Measured(string package, string route, string operation, RequestResponseLog request, RequestResponseLog? response)
{
    var label = request.Method.Value?.ToString() ?? "";
    var span = OtlpSpanMapper.Map(request, response, otlp, DateTimeOffset.UtcNow);
    // Map takes the pair as given; MapAll, which the exporter uses, pairs by TraceId and RequestResponseId.
    if (response is not null && OtlpSpanMapper.MapAll([request, response], otlp, DateTimeOffset.UtcNow).Spans.Count is var spans and not 1)
        pairingNotes.Add($"{route} {operation}: TraceId {(request.TraceId == response.TraceId ? "same" : "differs")} between request and response; MapAll makes {spans} spans of them");
    rows.Add(
    [
        package, route, operation, OneLine(label),
        request.DependencyCategory ?? "-",
        DependencyCategories.IsStatementShaped(request.DependencyCategory) ? "true" : "false",
        request.Uri.Scheme,
        request.MetaType.ToString(),
        response?.MetaType.ToString() ?? "-",
        response is null ? "false" : "true",
        TableVerdict(request.Uri.Scheme, label),
        span.Kind.ToString(),
        span.Attribute("db.system.name") ?? "-",
    ]);
}

void Unmeasured(string mark, string package, string route, string operation, string why)
{
    rows.Add([package, route, operation, OneLine($"{mark}: {why}"), "-", "-", "-", mark, "-", "-", "-", "-", "-"]);
    failures.Add($"{mark} {package} {route} {operation}: {why}");
}

// The call, under a test id of its own, and the records it wrote, each request with its response.
((RequestResponseLog Request, RequestResponseLog? Response)[] Pairs, Exception? Thrown, int OrphanResponses) Capture(Action call)
{
    currentTest = Guid.NewGuid().ToString("N");
    Exception? thrown = null;
    try
    {
        call();
    }
    catch (Exception e)
    {
        thrown = e;
    }

    var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == currentTest).ToArray();
    var responses = logs.Where(l => l.Type == RequestResponseType.Response).ToList();
    var pairs = logs.Where(l => l.Type == RequestResponseType.Request)
        .Select(request =>
        {
            // By RequestResponseId alone: Measured says when the two halves' TraceIds differ.
            var response = responses.FirstOrDefault(r => r.RequestResponseId == request.RequestResponseId);
            if (response is not null)
                responses.Remove(response);
            return (request, response);
        })
        .ToArray();
    return (pairs, thrown, responses.Count);
}

// One driven operation of a hand-called tracker: it has to write its one call, and throw nothing.
void Tracked(string package, string route, string operation, Action call)
{
    var (pairs, thrown, orphans) = Capture(call);
    if (thrown is not null)
        Unmeasured(NoRecord, package, route, operation, $"threw {thrown.GetType().Name}: {thrown.Message}");
    else if (pairs.Length != 1 || orphans != 0)
        Unmeasured(NoRecord, package, route, operation, $"wrote {pairs.Length} requests and {orphans} responses with no request, not one call");
    else
        Measured(package, route, operation, pairs[0].Request, pairs[0].Response);
}

// Every member of a hand-called tracker's operation enum, through its LogRequest and LogResponse.
void EachMember<TEnum, TInfo>(string package, string route, Func<TEnum, TInfo?> infoOf, Func<TInfo, TEnum> operationOf,
    Func<TInfo, (Guid Id, Guid Trace)> logRequest, Action<TInfo, Guid, Guid> logResponse)
    where TEnum : struct, Enum
    where TInfo : class
{
    foreach (var member in Enum.GetValues<TEnum>())
    {
        var info = infoOf(member);
        if (info is null)
        {
            Unmeasured(NotDriven, package, route, member.ToString(), "this probe has no operation info for this member");
            continue;
        }

        if (!EqualityComparer<TEnum>.Default.Equals(operationOf(info), member))
        {
            Unmeasured(NoRecord, package, route, member.ToString(), $"the classifier gave {operationOf(info)}");
            continue;
        }

        Tracked(package, route, member.ToString(), () =>
        {
            var (id, trace) = logRequest(info);
            if (id == Guid.Empty)
                throw new InvalidOperationException("LogRequest returned no id: a phase gate, an exclusion or no test identity");
            logResponse(info, id, trace);
        });
    }
}

// One call of a real SDK client through a handler and its stub. What the SDK does with the stub's answer is not measured,
// since the handler logged the call by then; that it threw is noted.
void Sdk(string package, string route, Stub stub, string call, Action run)
{
    stub.Classified.Clear();
    var (pairs, thrown, orphans) = Capture(run);
    if (pairs.Length == 0 || pairs.Length != stub.Classified.Count || orphans != 0)
    {
        var why = $"{stub.Classified.Count} requests reached the stub, {pairs.Length} request records, {orphans} responses with no request";
        Unmeasured(NoRecord, package, route, call, thrown is null ? why : $"{why}; the SDK threw {thrown.GetType().Name}: {thrown.Message}");
        return;
    }

    for (var i = 0; i < pairs.Length; i++)
    {
        var member = stub.Classified[i];
        var named = pairs.Length > 1 ? $"{call}#{i + 1}" : call;
        Measured(package, route, member == named ? member : $"{member}[{named}]", pairs[i].Request, pairs[i].Response);
    }

    if (thrown is not null)
        sdkNotes.Add($"{route} {call}: {thrown.GetType().Name}");
}

// An enum member no row reached says so.
void Covered<TEnum>(string package, string routePrefix) where TEnum : struct, Enum
{
    var reached = rows.Where(row => row[0] == package && row[1].StartsWith(routePrefix, StringComparison.Ordinal))
        .Select(row => row[2].Split('[')[0])
        .ToHashSet();
    foreach (var member in Enum.GetNames<TEnum>())
        if (!reached.Contains(member))
            Unmeasured(NotDriven, package, routePrefix, member, "no call this probe makes classifies as this member");
}

static HttpResponseMessage Answer(string body, string mediaType) =>
    new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

// One body every response type the BigQuery client parses can read, as unknown fields are ignored, and the session a
// resumable upload's first request opens.
static HttpResponseMessage BigQueryAnswer(HttpRequestMessage request)
{
    const string body = """
        {"kind":"probe","id":"shop:sales.orders","etag":"e",
         "jobReference":{"projectId":"shop","jobId":"job_1","location":"US"},"jobComplete":true,"totalRows":"0",
         "schema":{"fields":[{"name":"id","type":"STRING"}]},"rows":[],"status":{"state":"DONE"},
         "configuration":{"query":{"query":"SELECT 1"}},
         "datasetReference":{"projectId":"shop","datasetId":"sales"},
         "tableReference":{"projectId":"shop","datasetId":"sales","tableId":"orders"},
         "email":"sa@shop.iam.gserviceaccount.com"}
        """;
    var response = Answer(body, "application/json");
    var uri = request.RequestUri!;
    if (request.Method == HttpMethod.Post && uri.Query.Contains("uploadType=resumable"))
        response.Headers.Location = new Uri(uri, uri.AbsolutePath + "?uploadType=resumable&upload_id=u1");
    return response;
}

// The status the Azure client expects of each request. The bodies stay empty: a call that parses one throws after it is logged.
static HttpResponseMessage AzureQueueAnswer(HttpRequestMessage request)
{
    var uri = request.RequestUri!;
    var status = request.Method.Method switch
    {
        "POST" => HttpStatusCode.Created,
        "PUT" when uri.AbsolutePath.Contains("/messages/") || uri.Query.Contains("comp=") => HttpStatusCode.NoContent,
        "PUT" => HttpStatusCode.Created,
        "DELETE" => HttpStatusCode.NoContent,
        _ => HttpStatusCode.OK,
    };
    return new HttpResponseMessage(status) { Content = new StringContent("") };
}

// ---------------------------------------------------------------------------- the stub and the client factories

// Answers every request, and keeps what the package's own classifier makes of each, in the order they came.
sealed class Stub(bool readBody, Func<HttpRequestMessage, string?, string> classify, Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    public List<string> Classified { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = readBody && request.Content is not null ? await request.Content.ReadAsStringAsync(cancellationToken) : null;
        Classified.Add(classify(request, body));
        return answer(request);
    }
}

// The shipped WithTestTracking for AWS takes no inner handler; this is its factory with the stub inside.
sealed class AwsFactory(HttpMessageHandler handler) : Amazon.Runtime.HttpClientFactory
{
    public override HttpClient CreateHttpClient(Amazon.Runtime.IClientConfig clientConfig) => new(handler, disposeHandler: false);
}

// The shipped TrackingBigQueryHttpClientFactory is internal and wraps a real HttpClientHandler; this is the same pipeline
// with the stub as the inner handler.
sealed class BigQueryFactory(BigQueryTrackingMessageHandlerOptions options, HttpMessageHandler inner) : Google.Apis.Http.HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(Google.Apis.Http.CreateHttpClientArgs args) => new BigQueryTrackingMessageHandler(options, inner);
}
