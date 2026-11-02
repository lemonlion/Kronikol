using System.Net;
using Kronikol.Constants;
using Kronikol.Extensions.Otlp;
using Kronikol.Ingestion;
using Kronikol.Tracking;

namespace Kronikol.Tests.Otlp;

public class OtlpSpanMapperTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExportTime = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TraceGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid PairGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static OtlpExportOptions Options() => new() { Endpoint = new Uri("http://localhost:4318/v1/traces") };

    private static RequestResponseLog Log(
        RequestResponseType type,
        string testId = "cafe651916cd43dd8448eb211c80319c",
        string? content = null,
        OneOf<HttpStatusCode, string>? status = null,
        DateTimeOffset? timestamp = null,
        string? activityTraceId = null,
        string? activitySpanId = null,
        string? capturedBy = null,
        Guid? traceId = null,
        Guid? requestResponseId = null,
        RequestResponseMetaType metaType = RequestResponseMetaType.Default,
        string? dependencyCategory = null,
        OneOf<HttpMethod, string>? method = null) =>
        new("My test", testId, method ?? HttpMethod.Get, content, new Uri("http://api.example/things?q=1"), [],
            "backend", "web", type, traceId ?? TraceGuid, requestResponseId ?? PairGuid, false,
            status, metaType, dependencyCategory)
        {
            Timestamp = timestamp,
            ActivityTraceId = activityTraceId,
            ActivitySpanId = activitySpanId,
            CapturedBy = capturedBy,
        };

    private static (RequestResponseLog Request, RequestResponseLog Response) Pair(
        OneOf<HttpStatusCode, string>? status = null, string? requestContent = null, string? responseContent = null,
        string? dependencyCategory = null, RequestResponseMetaType metaType = RequestResponseMetaType.Default)
    {
        var request = Log(RequestResponseType.Request, content: requestContent, timestamp: T0,
            metaType: metaType, dependencyCategory: dependencyCategory);
        var response = Log(RequestResponseType.Response, content: responseContent, timestamp: T0.AddMilliseconds(30),
            status: status ?? HttpStatusCode.OK, metaType: metaType, dependencyCategory: dependencyCategory);
        return (request, response);
    }

    // ------------------------------------------------------------------ identity

    [Fact]
    public void Activity_ids_win_over_derived_ids()
    {
        var (request, response) = Pair();
        request = request with { ActivityTraceId = "4bf92f3577b34da6a3ce929d0e0e4736", ActivitySpanId = "00f067aa0ba902b7" };
        response = response with { ActivityTraceId = "4bf92f3577b34da6a3ce929d0e0e4736", ActivitySpanId = "00f067aa0ba902b7" };

        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);

        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", span.TraceId);
        Assert.Equal("00f067aa0ba902b7", span.SpanId);
    }

    [Fact]
    public void PerTest_strategy_derives_the_trace_id_from_the_test_id_with_the_ToGuid_recipe()
    {
        var (request, response) = Pair();

        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);

        // One test = one trace: the recipe is InteractionRecord.ToGuid, so a 32-hex test id
        // (a browser-minted W3C trace id) maps to itself.
        Assert.Equal("cafe651916cd43dd8448eb211c80319c", span.TraceId);
        Assert.Equal(InteractionRecord.ToGuid(request.TestId).ToString("N"), span.TraceId);
    }

    [Fact]
    public void PerTest_strategy_hashes_a_non_guid_test_id_deterministically()
    {
        var request = Log(RequestResponseType.Request, testId: "My test › does things", timestamp: T0);
        var response = Log(RequestResponseType.Response, testId: "My test › does things", timestamp: T0.AddMilliseconds(1), status: HttpStatusCode.OK);

        var first = OtlpSpanMapper.Map(request, response, Options(), ExportTime);
        var second = OtlpSpanMapper.Map(request, response, Options(), ExportTime);

        Assert.Equal(InteractionRecord.ToGuid("My test › does things").ToString("N"), first.TraceId);
        Assert.Equal(first.TraceId, second.TraceId);
        Assert.Equal(32, first.TraceId.Length);
        Assert.True(first.TraceId.All(Uri.IsHexDigit));
    }

    [Fact]
    public void PerPair_strategy_keeps_the_raw_log_trace_id()
    {
        var options = Options();
        options.TraceIdStrategy = TraceIdStrategy.PerPair;
        var (request, response) = Pair();

        var span = OtlpSpanMapper.Map(request, response, options, ExportTime);

        Assert.Equal(TraceGuid.ToString("N"), span.TraceId);
    }

    [Fact]
    public void Span_id_is_the_first_16_hex_of_the_request_response_id()
    {
        var (request, response) = Pair();

        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);

        Assert.Equal(PairGuid.ToString("N")[..16], span.SpanId);
        Assert.Equal(16, span.SpanId.Length);
    }

    // ------------------------------------------------------------------ mapping table

    [Fact]
    public void Maps_the_core_span_fields()
    {
        var (request, response) = Pair(status: HttpStatusCode.OK);

        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);

        Assert.Equal("GET", span.Name);
        Assert.Equal(OtlpSpanKind.Client, span.Kind);
        Assert.Equal("web", span.ResourceServiceName);
        Assert.Equal((ulong)(T0 - DateTimeOffset.UnixEpoch).Ticks * 100, span.StartTimeUnixNano);
        Assert.Equal((ulong)(T0.AddMilliseconds(30) - DateTimeOffset.UnixEpoch).Ticks * 100, span.EndTimeUnixNano);
        Assert.Equal("http://api.example/things?q=1", span.Attribute("url.full"));
        Assert.Equal("GET", span.Attribute("http.request.method"));
        Assert.Equal("200", span.Attribute("http.response.status_code"));
        Assert.Equal("backend", span.Attribute("peer.service"));
        Assert.Equal("cafe651916cd43dd8448eb211c80319c", span.Attribute("kronikol.test.id"));
        Assert.Equal("My test", span.Attribute("kronikol.test.name"));
        Assert.Equal(OtlpStatusCode.Unset, span.Status);
    }

    [Fact]
    public void Custom_method_labels_are_the_span_name_without_an_http_method_attribute()
    {
        var request = Log(RequestResponseType.Request, timestamp: T0, method: "Find ← Trial");
        var response = Log(RequestResponseType.Response, timestamp: T0.AddMilliseconds(2), status: "OK", method: "Find ← Trial");

        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);

        Assert.Equal("Find ← Trial", span.Name);
        Assert.Null(span.Attribute("http.request.method"));
        Assert.Null(span.Attribute("http.response.status_code"));
    }

    [Fact]
    public void Status_400_and_above_marks_the_span_error()
    {
        var (request, response) = Pair(status: HttpStatusCode.InternalServerError);
        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);
        Assert.Equal(OtlpStatusCode.Error, span.Status);
        Assert.Equal("500", span.Attribute("http.response.status_code"));

        var (req2, resp2) = Pair(status: "404");
        var notFound = OtlpSpanMapper.Map(req2, resp2, Options(), ExportTime);
        Assert.Equal(OtlpStatusCode.Error, notFound.Status);
        Assert.Equal("404", notFound.Attribute("http.response.status_code"));

        var (req3, resp3) = Pair(status: HttpStatusCode.Created);
        Assert.Equal(OtlpStatusCode.Unset, OtlpSpanMapper.Map(req3, resp3, Options(), ExportTime).Status);
    }

    [Fact]
    public void A_failure_status_carries_the_services_own_message()
    {
        // A capturer that read the service's failure (a MongoDB errmsg beside its 500) stored it as the call's Error;
        // the span says it, as a call that threw does.
        var (request, response) = Pair(status: HttpStatusCode.InternalServerError, dependencyCategory: "MongoDB");
        var span = OtlpSpanMapper.Map(request, response with { Error = "E11000 duplicate key error collection: app.loans" }, Options(), ExportTime);

        Assert.Equal(OtlpStatusCode.Error, span.Status);
        Assert.Equal("E11000 duplicate key error collection: app.loans", span.StatusMessage);
        Assert.Equal("500", span.Attribute("http.response.status_code"));

        var withoutError = OtlpSpanMapper.Map(request, response, Options(), ExportTime);
        Assert.Equal(OtlpStatusCode.Error, withoutError.Status);
        Assert.Null(withoutError.StatusMessage);

        var numeric = OtlpSpanMapper.Map(request, response with { StatusCode = "503", Error = "unavailable" }, Options(), ExportTime);
        Assert.Equal("unavailable", numeric.StatusMessage);
    }

    [Fact]
    public void String_failure_statuses_mark_the_span_error()
    {
        var (request, response) = Pair(status: "Failed");
        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);
        Assert.Equal(OtlpStatusCode.Error, span.Status);
        Assert.Equal("Failed", span.StatusMessage);

        var (req2, resp2) = Pair(status: "OK");
        Assert.Equal(OtlpStatusCode.Unset, OtlpSpanMapper.Map(req2, resp2, Options(), ExportTime).Status);
    }

    /// <summary>
    /// <c>plans/INGEST_FIDELITY_PLAN.md</c> T16 (F11): a call that threw is logged with its exception's type behind a bang
    /// as the status (3.18.0), which was not one of the failure words, so <c>kronikol export</c> and the live sink sent it
    /// as an Unset span with no message. It is an error, with the message chain as its message.
    /// </summary>
    [Fact]
    public void A_failed_send_marks_the_span_error_with_its_message_chain()
    {
        var failure = new HttpRequestException("boom", new IOException("connection reset"));
        var (request, response) = Pair(status: FailedSend.Status(failure));
        var span = OtlpSpanMapper.Map(request, response with { Error = FailedSend.Describe(failure) }, Options(), ExportTime);

        Assert.Equal(OtlpStatusCode.Error, span.Status);
        Assert.Equal("boom Caused by: connection reset", span.StatusMessage);
        Assert.Null(span.Attribute("http.response.status_code"));

        // A capturer that wrote the type and no message: the type is the message.
        var withoutMessage = OtlpSpanMapper.Map(request, response, Options(), ExportTime);
        Assert.Equal(OtlpStatusCode.Error, withoutMessage.Status);
        Assert.Equal("!HttpRequestException", withoutMessage.StatusMessage);
    }

    // ------------------------------------------------------------------ span kind

    private static RequestResponseLog Message(
        string uri, OneOf<HttpMethod, string> label, RequestResponseType type = RequestResponseType.Request,
        RequestResponseMetaType metaType = RequestResponseMetaType.Event, string service = "Broker", string caller = "App",
        string? testId = null) =>
        new("My test", testId ?? "cafe651916cd43dd8448eb211c80319c", label, null, new Uri(uri), [], service, caller, type, TraceGuid, PairGuid, false,
            null, metaType, DependencyCategories.MessageQueue)
        {
            Timestamp = T0,
        };

    private static OtlpExportSpan MapMessage(string uri, OneOf<HttpMethod, string> label, RequestResponseMetaType metaType = RequestResponseMetaType.Event,
        string service = "Broker", string caller = "App") =>
        OtlpSpanMapper.Map(
            Message(uri, label, metaType: metaType, service: service, caller: caller),
            Message(uri, label, RequestResponseType.Response, metaType, service, caller),
            Options(), ExportTime);

    // OTel's table, docs/messaging/messaging-spans.md at v1.44.0: send PRODUCER only if its context is the message's creation context, which only Kafka's
    // tracker makes it, otherwise CLIENT; receive CLIENT, process CONSUMER, settle CLIENT. Each
    // label and scheme is what that broker's tracker writes (results/r3-span-kinds.tsv holds the real trackers at every verbosity).
    [Theory]
    // Sends: Kafka's produce injects its context, and no other tracker does
    [InlineData("pubsub:///orders", "Publish → orders", OtlpSpanKind.Client)]
    [InlineData("pubsub:///projects/p/topics/orders", "PublishBatch topic=projects/p/topics/orders sub=", OtlpSpanKind.Client)]
    [InlineData("servicebus://orders/", "Schedule → orders", OtlpSpanKind.Client)]
    [InlineData("eventhubs:///orders", "Send (×3) → orders", OtlpSpanKind.Client)]
    [InlineData("kafka:///orders", "Produce → orders", OtlpSpanKind.Producer)]
    [InlineData("kafka:///", "Produce", OtlpSpanKind.Producer)]
    [InlineData("masstransit:///OrderPlaced", "→ OrderPlaced", OtlpSpanKind.Client)]
    [InlineData("sqs:///orders", "SendMessage", OtlpSpanKind.Client)]
    [InlineData("sns:///orders", "Publish", OtlpSpanKind.Client)]
    [InlineData("eventbridge://orders-bus/", "PutEvents [OrderPlaced]", OtlpSpanKind.Client)]
    [InlineData("storagequeue:///orders", "Send → orders", OtlpSpanKind.Client)]
    // Pulls: the application asked
    [InlineData("pubsub:///orders-sub", "Pull ← orders-sub", OtlpSpanKind.Client)]
    [InlineData("servicebus://orders/", "Receive (×3) ← orders", OtlpSpanKind.Client)]
    [InlineData("servicebus://orders/", "Peek ← orders", OtlpSpanKind.Client)]
    [InlineData("eventhubs:///orders", "Read ← orders[0]", OtlpSpanKind.Client)]
    [InlineData("sqs:///orders", "ReceiveMessage", OtlpSpanKind.Client)]
    [InlineData("storagequeue:///orders", "Receive ← orders", OtlpSpanKind.Client)]
    [InlineData("storagequeue:///orders", "PeekMessages", OtlpSpanKind.Client)]
    // Deliveries: the application was handed it
    [InlineData("pubsub:///orders-sub", "Receive ← orders-sub", OtlpSpanKind.Consumer)]
    [InlineData("pubsub:///projects/p/subscriptions/s", "Receive topic= sub=projects/p/subscriptions/s", OtlpSpanKind.Consumer)]
    [InlineData("eventhubs:///orders", "Process ← orders", OtlpSpanKind.Consumer)]
    [InlineData("eventhubs:///orders", "ProcessEvent hub=orders partition=0 count=1", OtlpSpanKind.Consumer)]
    [InlineData("kafka:///orders", "Consume ← orders", OtlpSpanKind.Consumer)]
    [InlineData("kafka:///orders/0@7", "Consume orders[0]@7", OtlpSpanKind.Consumer)]
    [InlineData("masstransit:///OrderPlaced", "Consume OrderPlaced", OtlpSpanKind.Consumer)]
    [InlineData("masstransit:///OrderPlaced", "← OrderPlaced", OtlpSpanKind.Consumer)]
    // A settlement or a management call an old file logged as an event
    [InlineData("pubsub:///orders-sub", "Ack", OtlpSpanKind.Client)]
    [InlineData("kafka:///", "Commit", OtlpSpanKind.Client)]
    [InlineData("eventhubs:///orders", "GetPartitionIds", OtlpSpanKind.Client)]
    [InlineData("masstransit:///OrderPlaced", "Consume Fault OrderPlaced", OtlpSpanKind.Client)]
    // MassTransit at Raw: the transport's scheme, and a label that names that address
    [InlineData("rabbitmq://localhost/orders", "Send OrderPlaced → rabbitmq://localhost/orders", OtlpSpanKind.Client)]
    [InlineData("rabbitmq://localhost/orders", "Consume OrderPlaced → rabbitmq://localhost/orders", OtlpSpanKind.Consumer)]
    [InlineData("rabbitmq://localhost/orders", "ConsumeFault OrderPlaced → rabbitmq://localhost/orders", OtlpSpanKind.Client)]
    // A log built by hand, on a scheme of nobody's table: its label is its author's own words, so it is a send
    [InlineData("event://cake_events", "Publish", OtlpSpanKind.Client)]
    [InlineData("event://cake_events", "Whatever the author wrote", OtlpSpanKind.Client)]
    [InlineData("event://cake_events", "Consume", OtlpSpanKind.Client)]
    [InlineData("event://cake_events", "Process cake", OtlpSpanKind.Client)]
    [InlineData("event://cake_events", "Receive cake", OtlpSpanKind.Client)]
    public void An_event_takes_the_kind_OpenTelemetrys_table_gives_its_operation(string uri, string label, OtlpSpanKind expected) =>
        Assert.Equal(expected, MapMessage(uri, label).Kind);

    [Theory]
    [InlineData("https://sqs.us-east-1.amazonaws.com/", "POST")]
    [InlineData("https://acct.queue.core.windows.net/orders/messages", "GET")]
    public void An_event_recorded_as_the_http_request_itself_is_a_client_span(string uri, string verb)
    {
        // SQS, SNS, Storage Queues and EventBridge at Raw: the table cannot read the label, and a receive is among them.
        var span = MapMessage(uri, new HttpMethod(verb));

        Assert.Equal(OtlpSpanKind.Client, span.Kind);
        Assert.Equal(verb, span.Attribute("http.request.method"));
        Assert.Null(span.Attribute("messaging.system"));
    }

    [Fact]
    public void A_message_on_a_web_address_with_a_label_of_its_own_is_still_a_send()
    {
        var span = MapMessage("https://events.example/api/events", "Publish");

        Assert.Equal(OtlpSpanKind.Client, span.Kind);
        Assert.Equal("send", span.Attribute("messaging.operation.type"));
        Assert.Equal("other", span.Attribute("messaging.system"));
    }

    [Fact]
    public void A_call_that_moves_no_message_is_a_client_span_whatever_its_label_reads()
    {
        Assert.Equal(OtlpSpanKind.Client, MapMessage("pubsub:///orders", "Publish → orders", RequestResponseMetaType.Default).Kind);
        Assert.Equal(OtlpSpanKind.Client, MapMessage("cosmosdb:///shop/orders", "Consume", RequestResponseMetaType.Default).Kind);
        // The one send that reads PRODUCER: what the first step is there for, as CLIENT is also where a send falls through to.
        Assert.Equal(OtlpSpanKind.Client, MapMessage("kafka:///orders", "Produce \u2192 orders", RequestResponseMetaType.Default).Kind);
    }

    [Fact]
    public void A_statement_read_back_from_a_file_is_a_client_span_whatever_its_label_reads()
    {
        var log = ReadBack(Message("sql://Orders/Orders", "Process order") with { DependencyCategory = DependencyCategories.SQL });

        var span = OtlpSpanMapper.Map(log, null, Options(), ExportTime);

        Assert.Equal(OtlpSpanKind.Client, span.Kind);
        Assert.Equal("other_sql", span.Attribute("db.system.name"));
    }

    [Fact]
    public void An_http_call_marked_as_a_delivery_is_a_client_span()
    {
        var log = Message("https://api.example/orders", new HttpMethod("POST")) with { NoteOnRight = true };

        Assert.Equal(OtlpSpanKind.Client, OtlpSpanMapper.Map(log, null, Options(), ExportTime).Kind);
    }

    [Theory]
    [InlineData("grpc://orders/Get", "Get")]
    [InlineData("mediatr://orders/Place", "Place")]
    public void A_call_on_no_brokers_address_carries_no_messaging_attribute(string uri, string label)
    {
        var span = MapMessage(uri, label, RequestResponseMetaType.Default);

        Assert.DoesNotContain(span.Attributes, a => a.Key.StartsWith("messaging.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_call_on_a_brokers_address_that_moves_no_message_says_no_type()
    {
        var span = MapMessage("kafka:///orders", "Whatever orders", RequestResponseMetaType.Default);

        Assert.Equal("kafka", span.Attribute("messaging.system"));
        Assert.Null(span.Attribute("messaging.operation.type"));
    }

    [Fact]
    public void A_poll_read_back_from_a_file_says_receive()
    {
        var span = OtlpSpanMapper.Map(ReadBack(Message("kafka://broker/orders", "Poll orders")), null, Options(), ExportTime);

        Assert.Equal(OtlpSpanKind.Client, span.Kind);
        Assert.Equal("receive", span.Attribute("messaging.operation.type"));
    }

    // A record the tap made is {system}://{broker}/{destination}: the system a scheme, and the destination where no tracker of that scheme puts it.
    [Theory]
    [InlineData("gcp-pubsub://PubSub/orders", "Receive orders", "gcp_pubsub")]
    [InlineData("aws-sqs://SQS/orders", "Send orders", "aws_sqs")]
    [InlineData("aws-sns://SNS/orders", "Publish orders", "aws.sns")]
    [InlineData("servicebus://ServiceBus/orders", "Send orders", "servicebus")]
    [InlineData("eventbridge://EventBridge/orders", "Send orders", "eventbridge")]
    public void A_message_a_span_made_leaves_with_the_system_and_the_destination_it_came_with(string uri, string label, string system)
    {
        var log = Message(uri, label) with { CapturedBy = InteractionMerger.SpanSource };

        var span = OtlpSpanMapper.Map(log, null, new OtlpExportOptions { Endpoint = Options().Endpoint, IncludeSpanSourced = true }, ExportTime);

        Assert.Equal(system, span.Attribute("messaging.system"));
        Assert.Equal("orders", span.Attribute("messaging.destination.name"));
    }

    [Theory]
    [InlineData("masstransit:///orders", "Consume Fault OrderPlaced", "ConsumeFault")]
    [InlineData("masstransit:///orders", "Send Fault OrderPlaced", "SendFault")]
    [InlineData("kafka:///orders", "Send Offsets", "SendOffsets")]
    [InlineData("masstransit:///orders", "Send OrderPlaced", "Send")]
    public void An_operation_of_two_words_is_named_as_its_enum_names_it(string uri, string label, string name) =>
        Assert.Equal(name, MapMessage(uri, label).Attribute("messaging.operation.name"));

    [Fact]
    public void A_database_call_is_a_client_span_carrying_its_system()
    {
        var request = Message("spanner://shop/Users", "SELECT FROM Users", metaType: RequestResponseMetaType.Default) with { DependencyCategory = DependencyCategories.Spanner };
        var span = OtlpSpanMapper.Map(request, request with { Type = RequestResponseType.Response }, Options(), ExportTime);

        Assert.Equal(OtlpSpanKind.Client, span.Kind);
        Assert.Equal("gcp.spanner", span.Attribute("db.system.name"));
        Assert.Null(span.Attribute("messaging.system"));
    }

    // A delivery logged as the broker's arrow into the consumer: exported as it stands, the broker reads as consuming from the app.
    [Theory]
    [InlineData("kafka:///orders", "Consume ← orders")]
    [InlineData("masstransit:///OrderPlaced", "Consume OrderPlaced")]
    [InlineData("masstransit:///OrderPlaced", "← OrderPlaced")]
    [InlineData("rabbitmq://localhost/orders", "Consume OrderPlaced → rabbitmq://localhost/orders")]
    public void A_delivery_logged_from_the_broker_is_the_consumers_span_and_the_broker_its_peer(string uri, string label)
    {
        var span = MapMessage(uri, label, service: "OrderService", caller: "Broker");

        Assert.Equal(OtlpSpanKind.Consumer, span.Kind);
        Assert.Equal("OrderService", span.ResourceServiceName);
        Assert.Equal("Broker", span.Attribute("peer.service"));
    }

    // Pub/Sub's subscriber and Event Hubs' Process log the consumer as the caller, so nothing moves.
    [Theory]
    [InlineData("pubsub:///orders-sub", "Receive ← orders-sub")]
    [InlineData("eventhubs:///orders", "Process ← orders")]
    public void A_delivery_logged_from_the_consumer_keeps_its_parties(string uri, string label)
    {
        var span = MapMessage(uri, label, service: "Broker", caller: "OrderService");

        Assert.Equal(OtlpSpanKind.Consumer, span.Kind);
        Assert.Equal("OrderService", span.ResourceServiceName);
        Assert.Equal("Broker", span.Attribute("peer.service"));
    }

    private static OtlpExportSpan Tracked(string testId) =>
        Assert.Single(OtlpSpanMapper.MapAll(RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId), Options(), ExportTime).Spans);

    private static MessageTracker Tracker(string testId, string caller) =>
        new(new MessageTrackerOptions { CallerName = caller, ServiceName = "Broker", CurrentTestInfoFetcher = () => ("My test", testId) });

    // TrackConsumeEvent logs the broker's arrow into the consumer on every scheme, and says so with NoteOnRight: the swap follows how the
    // record was logged. Keyed on the scheme, a consume hand-logged on pubsub:// read as the broker consuming from the app, and Tempo drew it
    // backwards. BreakfastProvider logs this very shape, Fakes/PubSub/InMemoryCustomerFeedbackConsumerService.cs.
    [Theory]
    [InlineData("Consume (Pub/Sub)", "pubsub:///orders-sub")]
    [InlineData("Consume (Event Hub)", "eventhubs:///orders")]
    [InlineData("Consume (Event Hub)", "eventhub:///orders")]
    [InlineData("Consume (Kafka)", "kafka:///orders")]
    [InlineData("Consume (Kafka)", "masstransit:///orders")]
    [InlineData("Receive", "pubsub:///orders-sub")]
    [InlineData("Process ← orders", "eventhubs:///orders")]
    [InlineData("Kafka", "kafka:///orders")]
    [InlineData("Whatever the author wrote", "event://cake_events")]
    [InlineData("Consume", "https://events.example/api/events")]
    public void TrackConsumeEvent_is_the_consumers_span_on_every_scheme(string label, string uri)
    {
        var testId = Guid.NewGuid().ToString("N");

        Tracker(testId, "Broker").TrackConsumeEvent(label, "OrderService", new Uri(uri), new { Id = 1 });

        var span = Tracked(testId);
        Assert.Equal(OtlpSpanKind.Consumer, span.Kind);
        Assert.Equal("OrderService", span.ResourceServiceName);
        Assert.Equal("Broker", span.Attribute("peer.service"));
        Assert.Equal("process", span.Attribute("messaging.operation.type"));
    }

    // A label is its author's own words, on any scheme: only the call they made says what the record is. Read by its first word, each of
    // these exported as the destination consuming from the app.
    [Theory]
    [InlineData("Process order", "Orders", "event://orders")]
    [InlineData("Consume", "Orders", "event://orders")]
    [InlineData("ProcessOrder", "Orders", "event://orders")]
    [InlineData("Process", "Payments", "https://payments.example/process")]
    [InlineData("Receive receipt", "Billing", "event://receipts")]
    [InlineData("PollResult", "Billing", "event://receipts")]
    [InlineData("Process order", "Kafka", "kafka:///orders")]
    [InlineData("Consume (Kafka)", "OrderService", "kafka:///orders")]
    [InlineData("Consume OrderPlaced", "Bus", "masstransit:///orders")]
    [InlineData("Receive order", "PubSub", "pubsub:///orders")]
    [InlineData("Ack order", "PubSub", "pubsub:///orders")]
    [InlineData("Process order", "Hub", "eventhubs:///orders")]
    [InlineData("Peek order", "Queue", "servicebus://orders/")]
    [InlineData("ReceiveMessage", "Queue", "sqs:///orders")]
    public void A_send_stays_a_send_whatever_its_label_reads(string label, string destination, string uri)
    {
        var address = new Uri(uri);
        foreach (var route in new Action<MessageTracker>[]
                 {
                     t => t.TrackSendEvent(label, destination, address, new { Id = 1 }),
                     t => t.TrackSendMessage(label, destination, address, new { Id = 1 }),
                     t => t.TrackMessageResponse(label, destination, address, t.TrackMessageRequest(label, destination, address, new { Id = 1 })),
                 })
        {
            var testId = Guid.NewGuid().ToString("N");

            route(Tracker(testId, "App"));

            var span = Tracked(testId);
            Assert.Equal(OtlpSpanKind.Client, span.Kind);
            Assert.Equal("App", span.ResourceServiceName);
            Assert.Equal(destination, span.Attribute("peer.service"));
            Assert.Equal("send", span.Attribute("messaging.operation.type"));
            Assert.Equal("send", span.Attribute("messaging.operation.name"));
        }
    }

    // A log a consumer builds by hand (BreakfastProvider's EventPublishingTracker): no first word of a free label decides.
    [Theory]
    [InlineData("Process order", "event://orders")]
    [InlineData("Consume", "https://payments.example/process")]
    [InlineData("Receive receipt", "eventgrid://topics/receipts")]
    public void An_event_logged_by_hand_is_a_send_whatever_its_label_reads(string label, string uri)
    {
        var span = MapMessage(uri, label);

        Assert.Equal(OtlpSpanKind.Client, span.Kind);
        Assert.Equal("App", span.ResourceServiceName);
        Assert.Equal("send", span.Attribute("messaging.operation.type"));
    }

    private static RequestResponseLog ReadBack(RequestResponseLog log) =>
        InteractionRecord.FromJson(InteractionRecord.FromLog(log).ToJson()).ToLog();

    // What a record read back from NDJSON carries: no NoteOnRight and no sign of who wrote the label, so the label is all that is left. By the
    // ingest contract serviceName is the receiver, so a delivery a label names is the service's own.
    [Theory]
    [InlineData("Consume (Pub/Sub)", "pubsub:///orders-sub", OtlpSpanKind.Consumer, "Service")]
    [InlineData("Consume (Event Hub)", "eventhub:///orders", OtlpSpanKind.Consumer, "Service")]
    [InlineData("Consume (Kafka)", "kafka:///orders", OtlpSpanKind.Consumer, "Service")]
    [InlineData("Consume OrderPlaced → rabbitmq://localhost/orders", "rabbitmq://localhost/orders", OtlpSpanKind.Consumer, "Service")]
    [InlineData("Process order", "event://orders", OtlpSpanKind.Consumer, "Service")]
    [InlineData("Process", "https://payments.example/process", OtlpSpanKind.Consumer, "Service")]
    [InlineData("Consume", "event://orders", OtlpSpanKind.Consumer, "Service")]
    [InlineData("Receive ← orders-sub", "pubsub:///orders-sub", OtlpSpanKind.Consumer, "Caller")]
    [InlineData("Process ← orders", "eventhubs:///orders", OtlpSpanKind.Consumer, "Caller")]
    [InlineData("Receive receipt", "event://receipts", OtlpSpanKind.Client, "Caller")]
    [InlineData("Poll", "event://receipts", OtlpSpanKind.Client, "Caller")]
    [InlineData("ReceiveMessage orders", "aws-sqs://broker/orders", OtlpSpanKind.Client, "Caller")]
    [InlineData("Kafka", "kafka:///orders", OtlpSpanKind.Client, "Caller")]
    [InlineData("Publish", "event://orders", OtlpSpanKind.Client, "Caller")]
    [InlineData("ProcessOrder", "event://orders", OtlpSpanKind.Client, "Caller")]
    [InlineData("Consumed", "event://orders", OtlpSpanKind.Client, "Caller")]
    [InlineData("PollResult", "event://receipts", OtlpSpanKind.Client, "Caller")]
    public void Read_back_from_a_record_its_label_is_all_that_says_what_it_is(string label, string uri, OtlpSpanKind kind, string self)
    {
        var log = ReadBack(Message(uri, label, service: "Service", caller: "Caller"));

        var span = OtlpSpanMapper.Map(log, null, Options(), ExportTime);

        Assert.Equal(kind, span.Kind);
        Assert.Equal(self, span.ResourceServiceName);
        Assert.Equal(self == "Service" ? "Caller" : "Service", span.Attribute("peer.service"));
    }

    // A record that came from a span is drawn from the service that sent the span, and one a wire capturer saw, with or without a span, takes the
    // wire's parties, the client and the server of the connection: neither is the broker's arrow, whatever the span's kind.
    [Theory]
    [InlineData(InteractionMerger.WireSource, "Consume orders", OtlpSpanKind.Consumer)]
    [InlineData(InteractionMerger.SpanSource, "Process orders", OtlpSpanKind.Consumer)]
    [InlineData(InteractionMerger.SpanSource, "Consume orders", OtlpSpanKind.Consumer)]
    [InlineData(InteractionMerger.SpanSource, "Poll orders", OtlpSpanKind.Client)]
    [InlineData(InteractionMerger.SpanSource, "Send orders", OtlpSpanKind.Client)]
    [InlineData(InteractionMerger.MergedSource, "Process orders", OtlpSpanKind.Consumer)]
    [InlineData(InteractionMerger.MergedSource, "Consume orders", OtlpSpanKind.Consumer)]
    [InlineData(InteractionMerger.MergedSource, "Poll orders", OtlpSpanKind.Client)]
    public void A_message_a_span_saw_keeps_its_parties(string capturedBy, string label, OtlpSpanKind kind)
    {
        var options = new OtlpExportOptions { Endpoint = Options().Endpoint, IncludeSpanSourced = true };
        foreach (var log in new[]
                 {
                     Message("kafka://broker/orders", label, service: "kafka", caller: "app") with { CapturedBy = capturedBy },
                     ReadBack(Message("kafka://broker/orders", label, service: "kafka", caller: "app") with { CapturedBy = capturedBy }),
                 })
        {
            var span = OtlpSpanMapper.Map(log, null, options, ExportTime);

            Assert.Equal(kind, span.Kind);
            Assert.Equal("app", span.ResourceServiceName);
            Assert.Equal("kafka", span.Attribute("peer.service"));
        }
    }

    // OTel's database page: "Span kind SHOULD be CLIENT". The record keeps the Event it was written with (a consumer's own, or a capture from
    // before R1): the kind and the attributes are the exporter's reading of it, and no message reads a statement.
    [Theory]
    [InlineData(DependencyCategories.Spanner, "spanner:///shop", "SELECT FROM Orders", "gcp.spanner")]
    [InlineData(DependencyCategories.BigQuery, "bigquery:///shop", "Query", "bigquery")]
    [InlineData(DependencyCategories.Bigtable, "bigtable:///orders", "MutateRow → orders", "bigtable")]
    [InlineData(DependencyCategories.CosmosDB, "cosmosdb:///shop/orders", "Consume", "azure.cosmosdb")]
    [InlineData(DependencyCategories.Redis, "redis://db0/k", "Get", "redis")]
    [InlineData(DependencyCategories.SQL, "sql://Orders/Orders", "Process order", "other_sql")]
    public void A_statement_written_as_an_event_is_a_client_span_and_no_message(string category, string uri, string label, string system)
    {
        var request = Message(uri, label) with { DependencyCategory = category };

        var span = OtlpSpanMapper.Map(request, request with { Type = RequestResponseType.Response }, Options(), ExportTime);

        Assert.Equal(RequestResponseMetaType.Event, request.MetaType);
        Assert.Equal(OtlpSpanKind.Client, span.Kind);
        Assert.Equal(system, span.Attribute("db.system.name"));
        Assert.Equal("App", span.ResourceServiceName);
        Assert.DoesNotContain(span.Attributes, a => a.Key.StartsWith("messaging.", StringComparison.Ordinal));
    }

    // F19. A label that reads as a verb is an HttpMethod after NDJSON, on any scheme: only a call on the web is an HTTP call.
    [Theory]
    [InlineData("http://api.example/things", "GET", true)]
    [InlineData("https://api.example/things", "POST", true)]
    [InlineData("HTTPS://api.example/things", "PATCH", true)]
    [InlineData("cosmosdb:///shop/orders", "DELETE", false)]
    [InlineData("spanner:///shop", "DELETE", false)]
    [InlineData("redis://db0/k", "GET", false)]
    [InlineData("storagequeue:///orders", "DELETE", false)]
    [InlineData("event://cake_events", "DELETE", false)]
    public void Only_a_call_on_the_web_carries_an_http_method(string uri, string verb, bool carries)
    {
        foreach (var metaType in new[] { RequestResponseMetaType.Default, RequestResponseMetaType.Event })
        {
            var span = MapMessage(uri, new HttpMethod(verb), metaType);

            Assert.Equal(carries ? verb : null, span.Attribute("http.request.method"));
            Assert.Equal(verb, span.Name);
        }
    }

    [Theory]
    [InlineData("pubsub:///projects/p/topics/orders", "Publish → orders", "gcp_pubsub", "Publish", "send", "orders")]
    [InlineData("sqs:///orders", "ReceiveMessage", "aws_sqs", "ReceiveMessage", "receive", "orders")]
    [InlineData("sns:///orders", "Publish", "aws.sns", "Publish", "send", "orders")]
    [InlineData("kafka:///orders", "Consume ← orders", "kafka", "Consume", "process", "orders")]
    [InlineData("masstransit:///OrderPlaced", "← OrderPlaced", "masstransit", "process", "process", "OrderPlaced")]
    [InlineData("event://cake_events", "Publish", "event", "send", "send", "cake_events")]
    [InlineData("kafka:///orders", "Kafka", "kafka", "send", "send", "orders")]
    public void A_message_span_carries_what_the_conventions_require(string uri, string label, string system, string name, string type, string destination)
    {
        var span = MapMessage(uri, label);

        Assert.Equal(system, span.Attribute("messaging.system"));
        Assert.Equal(name, span.Attribute("messaging.operation.name"));
        Assert.Equal(type, span.Attribute("messaging.operation.type"));
        Assert.Equal(destination, span.Attribute("messaging.destination.name"));
    }

    [Fact]
    public void Only_a_Kafka_producers_context_makes_a_send_a_producer_span()
    {
        Assert.Equal(OtlpSpanKind.Producer, MapMessage("kafka:///orders", "Produce \u2192 orders").Kind);
        // Its label and scheme, but MessageTracker's call: no context of Kronikol's went on the message.
        var testId = Guid.NewGuid().ToString("N");
        Tracker(testId, "App").TrackSendEvent("Produce \u2192 orders", "Kafka", new Uri("kafka:///orders"), new { Id = 1 });
        Assert.Equal(OtlpSpanKind.Client, Tracked(testId).Kind);
        Assert.Equal(OtlpSpanKind.Client, MapMessage("pubsub:///orders", "Publish \u2192 orders").Kind);
    }

    [Fact]
    public void A_settlement_names_its_system_and_operation_and_no_type_the_table_cannot_tell_it()
    {
        var span = MapMessage("servicebus://orders/", "Complete", RequestResponseMetaType.Default);

        Assert.Equal(OtlpSpanKind.Client, span.Kind);
        Assert.Equal("servicebus", span.Attribute("messaging.system"));
        Assert.Equal("Complete", span.Attribute("messaging.operation.name"));
        Assert.Null(span.Attribute("messaging.operation.type"));
    }

    [Fact]
    public void A_call_that_is_no_message_carries_no_messaging_attribute()
    {
        var (request, response) = Pair();
        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);

        Assert.DoesNotContain(span.Attributes, a => a.Key.StartsWith("messaging.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_record_with_a_relative_address_does_not_throw()
    {
        var log = Message("http://placeholder/", "Publish") with { Uri = new Uri("orders", UriKind.Relative) };

        Assert.Equal(OtlpSpanKind.Client, OtlpSpanMapper.Map(log, null, Options(), ExportTime).Kind);
    }

    // F15: a pair is one span whichever method logged it. Paired on both ids, TrackSendEvent exported two orphans that shared a
    // trace id and a span id.
    [Theory]
    [InlineData("TrackSendEvent")]
    [InlineData("TrackConsumeEvent")]
    [InlineData("TrackMessageRequest")]
    [InlineData("TrackSendMessage")]
    public void A_message_tracked_with_no_http_context_exports_one_span(string route)
    {
        var testId = Guid.NewGuid().ToString("N");
        var tracker = new MessageTracker(new MessageTrackerOptions { CurrentTestInfoFetcher = () => ("My test", testId) });
        var uri = new Uri("event://cake_events");

        switch (route)
        {
            case "TrackSendEvent":
                tracker.TrackSendEvent("Publish", "Broker", uri, new { Id = 1 });
                break;
            case "TrackConsumeEvent":
                tracker.TrackConsumeEvent("Consume", "App", uri, new { Id = 1 });
                break;
            case "TrackMessageRequest":
                tracker.TrackMessageResponse("Publish", "Broker", uri, tracker.TrackMessageRequest("Publish", "Broker", uri, new { Id = 1 }));
                break;
            default:
                tracker.TrackSendMessage("Publish", "Broker", uri, new { Id = 1 });
                break;
        }

        var batch = OtlpSpanMapper.MapAll(RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId), Options(), ExportTime);

        Assert.Single(batch.Spans);
        Assert.Equal(0, batch.OrphanSpans);
    }

    [Theory]
    [InlineData(DependencyCategories.Redis, "redis")]
    [InlineData(DependencyCategories.MongoDB, "mongodb")]
    [InlineData(DependencyCategories.PostgreSQL, "postgresql")]
    [InlineData(DependencyCategories.MySQL, "mysql")]
    [InlineData(DependencyCategories.SqlServer, "microsoft.sql_server")]
    [InlineData(DependencyCategories.BigQuery, "bigquery")]
    [InlineData(DependencyCategories.SQLite, "sqlite")]
    [InlineData(DependencyCategories.Oracle, "oracle.db")]
    [InlineData(DependencyCategories.Elasticsearch, "elasticsearch")]
    [InlineData(DependencyCategories.CosmosDB, "azure.cosmosdb")]
    [InlineData(DependencyCategories.DynamoDB, "aws.dynamodb")]
    [InlineData(DependencyCategories.Spanner, "gcp.spanner")]
    [InlineData(DependencyCategories.Bigtable, "bigtable")]
    [InlineData(DependencyCategories.ClickHouse, "clickhouse")]
    [InlineData(DependencyCategories.Database, "other_sql")]
    [InlineData(DependencyCategories.SQL, "other_sql")]
    public void Db_categories_reverse_map_to_db_system_name(string category, string expected)
    {
        var (request, response) = Pair(dependencyCategory: category);
        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);
        Assert.Equal(expected, span.Attribute("db.system.name"));
        Assert.Equal(category, span.Attribute("kronikol.dependency.category"));
    }

    [Fact]
    public void Non_db_categories_omit_db_system_name_but_keep_the_kronikol_attribute()
    {
        var (request, response) = Pair(dependencyCategory: DependencyCategories.MessageQueue);
        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);
        Assert.Null(span.Attribute("db.system.name"));
        Assert.Equal(DependencyCategories.MessageQueue, span.Attribute("kronikol.dependency.category"));
    }

    [Fact]
    public void Phase_and_captured_by_are_exported_when_set()
    {
        var (request, response) = Pair();
        request = request with { CapturedBy = InteractionMerger.WireSource };
        request.Phase = TestPhase.Action;

        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);

        Assert.Equal("Action", span.Attribute("kronikol.phase"));
        Assert.Equal("wire", span.Attribute("kronikol.captured.by"));

        var (req2, resp2) = Pair();
        var plain = OtlpSpanMapper.Map(req2, resp2, Options(), ExportTime);
        Assert.Null(plain.Attribute("kronikol.phase"));
        Assert.Null(plain.Attribute("kronikol.captured.by"));
    }

    // #190 R2: what answered the call in its service's place, beside kronikol.captured.by; not kronikol.test.*, which is
    // the test's identity. Read from either half, as a capture may mark only the response.
    [Fact]
    public void A_call_a_test_double_answered_is_exported_with_what_answered_it()
    {
        var (request, response) = Pair();
        response.TestDouble = "WireMock";

        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);

        Assert.Equal("WireMock", span.Attribute("kronikol.double"));
        var (req2, resp2) = Pair();
        Assert.Null(OtlpSpanMapper.Map(req2, resp2, Options(), ExportTime).Attribute("kronikol.double"));
    }

    // ------------------------------------------------------------------ verdict

    /// <summary>
    /// The verdict cannot come from the log. Every field on <c>RequestResponseLog</c> is capture-time,
    /// and the result exists only once a framework adapter has assembled the run - so it arrives through
    /// the options, keyed on the test id, which <em>is</em> the scenario id.
    /// </summary>
    [Fact]
    public void The_test_result_is_exported_when_the_run_can_answer_for_the_test()
    {
        var (request, response) = Pair();
        var options = Options();
        options.TestResult = id => id == request.TestId ? "Failed" : null;

        var span = OtlpSpanMapper.Map(request, response, options, ExportTime);

        Assert.Equal("Failed", span.Attribute("kronikol.test.result"));
        // Beside the identity it qualifies: the kronikol.test.* run stays contiguous, and the encoder
        // emits in list order, which is the order the wiki's attribute table documents.
        Assert.Equal(["kronikol.test.id", "kronikol.test.name", "kronikol.test.result"],
            span.Attributes.TakeLast(3).Select(a => a.Key));
    }

    [Fact]
    public void There_is_no_test_result_by_default()
    {
        var (request, response) = Pair();
        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);
        Assert.Null(span.Attribute("kronikol.test.result"));
    }

    [Fact]
    public void A_test_the_lookup_does_not_know_gets_no_result_rather_than_a_guess()
    {
        var (request, response) = Pair();
        var options = Options();
        options.TestResult = _ => null;

        var span = OtlpSpanMapper.Map(request, response, options, ExportTime);

        Assert.Null(span.Attribute("kronikol.test.result"));
    }

    [Fact]
    public void Both_halves_of_a_pair_carry_the_same_verdict()
    {
        // `primary` is request ?? response, so an unpaired response must answer the same as its request.
        var (request, response) = Pair();
        var options = Options();
        options.TestResult = _ => "Passed";

        Assert.Equal("Passed", OtlpSpanMapper.Map(request, null, options, ExportTime).Attribute("kronikol.test.result"));
        Assert.Equal("Passed", OtlpSpanMapper.Map(null, response, options, ExportTime).Attribute("kronikol.test.result"));
    }

    // ------------------------------------------------------------------ bodies

    [Fact]
    public void Bodies_are_not_exported_by_default()
    {
        var (request, response) = Pair(requestContent: "{\"a\":1}", responseContent: "{\"b\":2}");
        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);
        Assert.Null(span.Attribute("kronikol.request.body"));
        Assert.Null(span.Attribute("kronikol.response.body"));
    }

    [Fact]
    public void Bodies_are_exported_and_capped_when_opted_in()
    {
        var options = Options();
        options.IncludeBodies = true;
        options.BodyAttributeCapBytes = 10;
        var (request, response) = Pair(requestContent: "short", responseContent: new string('x', 50));

        var span = OtlpSpanMapper.Map(request, response, options, ExportTime);

        Assert.Equal("short", span.Attribute("kronikol.request.body"));
        var body = span.Attribute("kronikol.response.body");
        Assert.NotNull(body);
        Assert.StartsWith(new string('x', 10), body);
        Assert.Contains("…truncated (50 chars total)", body);
    }

    // ------------------------------------------------------------------ timestamps

    [Fact]
    public void Null_request_time_borrows_the_response_time()
    {
        var request = Log(RequestResponseType.Request, timestamp: null);
        var response = Log(RequestResponseType.Response, timestamp: T0.AddMilliseconds(5), status: HttpStatusCode.OK);

        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);

        Assert.Equal(span.EndTimeUnixNano, span.StartTimeUnixNano);
        Assert.Null(span.Attribute("kronikol.times.synthetic"));
    }

    [Fact]
    public void Both_null_timestamps_stamp_export_time_and_mark_synthetic()
    {
        var request = Log(RequestResponseType.Request, timestamp: null);
        var response = Log(RequestResponseType.Response, timestamp: null, status: HttpStatusCode.OK);

        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);

        Assert.Equal((ulong)(ExportTime - DateTimeOffset.UnixEpoch).Ticks * 100, span.StartTimeUnixNano);
        Assert.Equal(span.StartTimeUnixNano, span.EndTimeUnixNano);
        Assert.Equal("true", span.Attribute("kronikol.times.synthetic"));
    }

    // ------------------------------------------------------------------ batch pairing

    [Fact]
    public void MapAll_pairs_in_order_and_out_of_order()
    {
        var options = Options();
        var pairA = Pair();
        var bRequest = Log(RequestResponseType.Request, timestamp: T0.AddSeconds(1), requestResponseId: Guid.NewGuid());
        var bResponse = bRequest with { Type = RequestResponseType.Response, Timestamp = T0.AddSeconds(2), StatusCode = (OneOf<HttpStatusCode, string>)HttpStatusCode.OK };

        // Out of order: response B before request B.
        var batch = OtlpSpanMapper.MapAll([pairA.Request, bResponse, bRequest, pairA.Response], options, ExportTime);

        Assert.Equal(2, batch.Spans.Count);
        Assert.Equal(0, batch.OrphanSpans);
        Assert.Equal(0, batch.SkippedRecords);
    }

    [Fact]
    public void MapAll_exports_orphans_as_zero_duration_spans()
    {
        var options = Options();
        var lonelyRequest = Log(RequestResponseType.Request, timestamp: T0, requestResponseId: Guid.NewGuid());
        var lonelyResponse = Log(RequestResponseType.Response, timestamp: T0.AddSeconds(1), status: HttpStatusCode.OK, requestResponseId: Guid.NewGuid());

        var batch = OtlpSpanMapper.MapAll([lonelyRequest, lonelyResponse], options, ExportTime);

        Assert.Equal(2, batch.Spans.Count);
        Assert.Equal(2, batch.OrphanSpans);
        Assert.All(batch.Spans, s =>
        {
            Assert.Equal("true", s.Attribute("kronikol.orphan"));
            Assert.Equal(s.StartTimeUnixNano, s.EndTimeUnixNano);
        });
    }

    [Fact]
    public void A_single_record_with_a_measured_duration_is_a_complete_span_not_an_orphan()
    {
        var options = Options();
        var oneRecordCall = Log(RequestResponseType.Request, timestamp: T0, requestResponseId: Guid.NewGuid());
        oneRecordCall.DurationMs = 250;

        var batch = OtlpSpanMapper.MapAll([oneRecordCall], options, ExportTime);

        var span = Assert.Single(batch.Spans);
        Assert.Equal(0, batch.OrphanSpans);
        Assert.Null(span.Attribute("kronikol.orphan"));
        Assert.Equal((ulong)(250 * 1_000_000), span.EndTimeUnixNano - span.StartTimeUnixNano);
    }

    // ------------------------------------------------------------------ skips

    [Fact]
    public void Span_sourced_records_are_suppressed_by_default()
    {
        var (request, response) = Pair();
        request = request with { CapturedBy = InteractionMerger.SpanSource };
        response = response with { CapturedBy = InteractionMerger.SpanSource };

        var batch = OtlpSpanMapper.MapAll([request, response], Options(), ExportTime);
        Assert.Empty(batch.Spans);
        Assert.Equal(2, batch.SkippedRecords);

        var options = Options();
        options.IncludeSpanSourced = true;
        var included = OtlpSpanMapper.MapAll([request, response], options, ExportTime);
        Assert.Single(included.Spans);
    }

    [Fact]
    public void Merged_records_are_suppressed_like_span_sourced_ones()
    {
        var (request, response) = Pair();
        request = request with { CapturedBy = InteractionMerger.MergedSource };
        response = response with { CapturedBy = InteractionMerger.MergedSource };

        var batch = OtlpSpanMapper.MapAll([request, response], Options(), ExportTime);

        Assert.Empty(batch.Spans);
        Assert.Equal(2, batch.SkippedRecords);
    }

    [Fact]
    public void Markers_and_tracking_ignored_records_are_always_skipped()
    {
        var marker = Log(RequestResponseType.Request, timestamp: T0);
        marker.IsOverrideStart = true;
        var ignored = Log(RequestResponseType.Request, timestamp: T0, requestResponseId: Guid.NewGuid()) with { TrackingIgnore = true };
        var options = Options();
        options.IncludeSpanSourced = true;

        var batch = OtlpSpanMapper.MapAll([marker, ignored], options, ExportTime);

        Assert.Empty(batch.Spans);
        Assert.Equal(2, batch.SkippedRecords);
    }

    // ------------------------------------------------------------------ options validation

    [Fact]
    public void Options_validate_rejects_bad_values()
    {
        Assert.Throws<ArgumentException>(() => new OtlpExportOptions().Validate());
        Assert.Throws<ArgumentException>(() => new OtlpExportOptions { Endpoint = new Uri("/relative", UriKind.Relative) }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new OtlpExportOptions { Endpoint = new Uri("http://x/v1/traces"), BodyAttributeCapBytes = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new OtlpExportOptions { Endpoint = new Uri("http://x/v1/traces"), QueueCapacity = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new OtlpExportOptions { Endpoint = new Uri("http://x/v1/traces"), BatchMaxSpans = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new OtlpExportOptions { Endpoint = new Uri("http://x/v1/traces"), FlushInterval = TimeSpan.Zero }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new OtlpExportOptions { Endpoint = new Uri("http://x/v1/traces"), PendingRequestTtl = TimeSpan.Zero }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new OtlpExportOptions { Endpoint = new Uri("http://x/v1/traces"), ShutdownTimeout = TimeSpan.Zero }.Validate());

        var valid = new OtlpExportOptions { Endpoint = new Uri("http://localhost:4318/v1/traces") };
        valid.Validate();
    }
}
