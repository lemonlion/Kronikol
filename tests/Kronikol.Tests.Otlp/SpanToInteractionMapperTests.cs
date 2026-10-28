using Kronikol.Constants;
using Kronikol.Extensions.Otlp;
using Kronikol.Ingestion;
using Kronikol.Tests.History;

namespace Kronikol.Tests.Otlp;

public class SpanToInteractionMapperTests
{
    private static OtlpTapOptions Options()
    {
        var options = new OtlpTapOptions { ListenPort = 1 };
        options.ServiceNameMap["localhost:27099"] = "mongo";
        options.ServiceNameMap["localhost:6399"] = "redis";
        options.ServiceNameMap["localhost:9000"] = "intelligence-ai";
        options.ServiceNameMap["bq-emulator"] = "bigquery";
        return options;
    }

    private static OtlpSpan Single(string json) => Assert.Single(OtlpTraceReader.ReadJson(OtlpGoldens.Utf8(json)));

    [Fact]
    public void Every_change_to_a_recorded_call_line_moves_the_version()
    {
        // The history guard (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8): what this tracker records for one fixed call, as the
        // call line history fingerprints, pinned under the rule it was pinned with (HistoryGuard).
        var (request, response) = SpanToInteractionMapper.Map(Single(OtlpGoldens.MongoFindOldSemconv), Options())!.ToLogs();

        HistoryGuard.Pin([request, response], 8,
            "data-insights>mongo Find ← Trial /data-insights-Development/Trial OK");
    }

    [Fact]
    public void Maps_a_mongo_find_span_with_deprecated_semconv_to_the_same_label_the_mongodb_extension_uses()
    {
        var mapped = SpanToInteractionMapper.Map(Single(OtlpGoldens.MongoFindOldSemconv), Options());

        Assert.NotNull(mapped);
        Assert.Equal("Find ← Trial", mapped!.Method);
        Assert.Equal("mongodb:///data-insights-Development/Trial", mapped.Uri);
        Assert.Equal("mongo", mapped.ServiceName);
        Assert.Equal("data-insights", mapped.CallerName);
        Assert.Equal(DependencyCategories.MongoDB, mapped.DependencyCategory);
        Assert.Equal(OtlpGoldens.TestTraceId, mapped.TestId);
        Assert.Equal(OtlpGoldens.TestTraceId, mapped.TraceId);
        Assert.Equal("00f067aa0ba902b7", mapped.SpanId);
        Assert.Null(mapped.RequestContent); // the vendor package captures no command text
        Assert.Equal("OK", mapped.StatusCode);
        Assert.Equal(12, mapped.DurationMs);
    }

    /// <summary>The ioredis golden span with its db attributes replaced by <paramref name="attributes"/>.</summary>
    private static OtlpSpan WithAttributes(string attributes) => Single(OtlpGoldens.RedisGetIoredis
        .Replace("""{ "key": "db.system", "value": { "stringValue": "redis" } },""", attributes)
        .Replace("""{ "key": "db.statement", "value": { "stringValue": "get insights:trials:2026-08-22" } },""", "")
        .Replace("""{ "key": "db.redis.database_index", "value": { "intValue": "0" } },""", ""));

    [Theory]
    [InlineData("""{ "key": "messaging.system", "value": { "stringValue": "aws_sqs" } }, { "key": "messaging.destination.name", "value": { "stringValue": "orders" } },""", "aws-sqs://localhost/orders")]
    [InlineData("""{ "key": "messaging.system", "value": { "stringValue": "gcp_pubsub" } }, { "key": "messaging.destination.name", "value": { "stringValue": "orders" } },""", "gcp-pubsub://localhost/orders")]
    [InlineData("""{ "key": "rpc.system", "value": { "stringValue": "dotnet_wcf" } }, { "key": "rpc.service", "value": { "stringValue": "Orders" } }, { "key": "rpc.method", "value": { "stringValue": "Get" } },""", "dotnet-wcf://localhost:6399/Orders/Get")]
    [InlineData("""{ "key": "db.system", "value": { "stringValue": "other_sql" } }, { "key": "db.name", "value": { "stringValue": "app" } },""", "other-sql://localhost:6399/app")]
    public void A_system_name_with_an_underscore_makes_a_scheme_a_uri_can_hold(string attributes, string uri)
    {
        // Semantic-convention names hold underscores (aws_sqs, gcp_pubsub, dotnet_wcf, other_sql), which no URI scheme may:
        // until #192 R1 every such call was drawn at http://unknown/, its destination lost. The old address is kept for
        // history's rules 6 and 7.
        var options = Options();
        options.CaptureKinds.Add(OtlpCaptureKinds.Messaging);
        options.CaptureKinds.Add(OtlpCaptureKinds.Rpc);
        var mapped = SpanToInteractionMapper.Map(WithAttributes(attributes), options);

        Assert.NotNull(mapped);
        Assert.Equal(uri, mapped!.Uri);
        var (request, _) = mapped.ToLogs();
        Assert.Equal(uri, request.Uri.ToString());
        Assert.Equal("http://unknown/", request.RecordedBefore8!.Uri.ToString());
        Assert.Equal(request.Method.Value, request.RecordedBefore8.Method.Value);
    }

    [Theory]
    [InlineData("""{ "key": "messaging.system", "value": { "stringValue": "aws_sqs" } }, { "key": "messaging.destination.name", "value": { "stringValue": "orders" } },""", "aws-sqs://localhost/orders")]
    [InlineData("""{ "key": "messaging.system", "value": { "stringValue": "gcp_pubsub" } }, { "key": "messaging.destination.name", "value": { "stringValue": "orders" } },""", "gcp-pubsub://localhost/orders")]
    [InlineData("""{ "key": "rpc.system", "value": { "stringValue": "dotnet_wcf" } }, { "key": "rpc.service", "value": { "stringValue": "Orders" } }, { "key": "rpc.method", "value": { "stringValue": "Get" } },""", "dotnet-wcf://localhost:6399/Orders/Get")]
    [InlineData("""{ "key": "db.system", "value": { "stringValue": "other_sql" } }, { "key": "db.name", "value": { "stringValue": "app" } },""", "other-sql://localhost:6399/app")]
    public void A_span_written_as_records_is_ingested_with_the_address_its_rules_6_and_7_read(string attributes, string uri)
    {
        // The records carry the address as 5.16.1 wrote it where that one does not parse, and the ingest mends it as it
        // mends any: the call reads as the in-process log does, and history's rules 6 and 7 read it where 5.16.1's ingest
        // did, as the path of http://unknown/. Written mended, nothing kept the old reading, and the first run after the
        // upgrade read changed against every run before it.
        var options = Options();
        options.CaptureKinds.Add(OtlpCaptureKinds.Messaging);
        options.CaptureKinds.Add(OtlpCaptureKinds.Rpc);
        var mapped = SpanToInteractionMapper.Map(WithAttributes(attributes), options)!;

        var (request, _) = mapped.ToRecords();
        var log = InteractionRecord.FromJson(request.ToJson()).ToLog();

        Assert.Equal(uri, log.Uri.ToString());
        Assert.Equal("http://unknown/" + mapped.UriBefore8, log.RecordedBefore8!.Uri.ToString());
    }

    [Fact]
    public void A_scheme_that_was_valid_keeps_no_old_address()
    {
        var mapped = SpanToInteractionMapper.Map(Single(OtlpGoldens.RedisGetIoredis), Options());

        Assert.Null(mapped!.ToLogs().Request.RecordedBefore8);
    }

    [Fact]
    public void Maps_an_ioredis_span_to_the_command_and_the_key_in_the_uri()
    {
        var mapped = SpanToInteractionMapper.Map(Single(OtlpGoldens.RedisGetIoredis), Options());

        Assert.NotNull(mapped);
        Assert.Equal("GET", mapped!.Method); // no hit/miss: the instrumentation reports no result
        Assert.Equal("redis://db0/insights:trials:2026-08-22", mapped.Uri);
        Assert.Equal("redis", mapped.ServiceName);
        Assert.Equal("superpay-graphql", mapped.CallerName);
        Assert.Equal(DependencyCategories.Redis, mapped.DependencyCategory);
        Assert.Equal("get insights:trials:2026-08-22", mapped.RequestContent);
    }

    [Fact]
    public void Elided_redis_arguments_leave_the_key_out_of_the_uri()
    {
        var json = OtlpGoldens.RedisGetIoredis.Replace("get insights:trials:2026-08-22", "get [1 other arguments]");
        var mapped = SpanToInteractionMapper.Map(Single(json), Options());

        Assert.NotNull(mapped);
        Assert.Equal("redis://db0/", mapped!.Uri);
    }

    [Fact]
    public void Maps_an_http_client_span_and_ignores_the_server_span_for_the_same_hop()
    {
        var spans = OtlpTraceReader.ReadJson(OtlpGoldens.Utf8(OtlpGoldens.HttpClientAndServer));
        var mapped = SpanToInteractionMapper.MapAll(spans, Options());

        var call = Assert.Single(mapped);
        Assert.Equal("GET", call.Method);
        Assert.Equal("http://localhost:9000/api/summary?trial=42", call.Uri);
        Assert.Equal("intelligence-ai", call.ServiceName);
        Assert.Equal("data-insights", call.CallerName);
        Assert.Null(call.DependencyCategory);
        Assert.Equal("200", call.StatusCode);
    }

    [Fact]
    public void Server_spans_are_mapped_only_when_asked_for()
    {
        var options = Options();
        options.IncludeServerSpans = true;
        var spans = OtlpTraceReader.ReadJson(OtlpGoldens.Utf8(OtlpGoldens.HttpClientAndServer));

        var mapped = SpanToInteractionMapper.MapAll(spans, options);

        Assert.Equal(2, mapped.Count);
        var server = mapped[1];
        Assert.Equal("data-insights", server.ServiceName);
        Assert.Equal("127.0.0.1", server.CallerName);
        Assert.Equal("http://unknown/api/summary", server.Uri);
    }

    [Fact]
    public void A_failed_db_span_becomes_a_500_carrying_the_status_message()
    {
        var mapped = SpanToInteractionMapper.Map(Single(OtlpGoldens.BigQueryFailed), Options());

        Assert.NotNull(mapped);
        Assert.Equal(DependencyCategories.BigQuery, mapped!.DependencyCategory);
        Assert.Equal("500", mapped.StatusCode);
        Assert.Equal("Deadline exceeded", mapped.ResponseContent);
        Assert.Equal("SELECT trial_id FROM `insights.trials` LIMIT 10", mapped.RequestContent);
        Assert.Equal("SELECT", mapped.Method);
        Assert.Equal("bigquery://bq-emulator/insights", mapped.Uri);
        Assert.Equal("bigquery", mapped.ServiceName);
    }

    [Fact]
    public void Capture_kinds_gate_each_span_family()
    {
        var options = Options();
        options.CaptureKinds.Clear();
        options.CaptureKinds.Add(OtlpCaptureKinds.Db);

        var http = OtlpTraceReader.ReadJson(OtlpGoldens.Utf8(OtlpGoldens.HttpClientAndServer))[0];
        Assert.Null(SpanToInteractionMapper.Map(http, options));

        var mongo = Single(OtlpGoldens.MongoFindOldSemconv);
        Assert.NotNull(SpanToInteractionMapper.Map(mongo, options));
    }

    [Fact]
    public void Attribution_uses_the_trace_id_and_falls_back_when_it_is_not_a_known_test()
    {
        var mongo = Single(OtlpGoldens.MongoFindOldSemconv);

        var byTrace = SpanToInteractionMapper.Map(mongo, Options());
        Assert.Equal(OtlpGoldens.TestTraceId, byTrace!.TestId);

        var options = Options();
        options.KnownTestIds = id => id == "another-test";
        options.FallbackTestId = "outside-any-test";
        var folded = SpanToInteractionMapper.Map(mongo, options);
        Assert.Equal("outside-any-test", folded!.TestId);
        Assert.Equal(OtlpGoldens.TestTraceId, folded.TraceId); // the span trace id still cross-links to Tempo

        options.AttributeByTraceId = false;
        options.KnownTestIds = null;
        Assert.Equal("outside-any-test", SpanToInteractionMapper.Map(mongo, options)!.TestId);
    }

    [Fact]
    public void A_hit_miss_attribute_becomes_the_same_suffix_the_redis_extension_uses()
    {
        var json = OtlpGoldens.RedisGetIoredis.Replace(
            """{ "key": "db.redis.database_index", "value": { "intValue": "0" } },""",
            """{ "key": "db.redis.database_index", "value": { "intValue": "0" } }, { "key": "cache.hit", "value": { "boolValue": true } },""");

        var mapped = SpanToInteractionMapper.Map(Single(json), Options());
        Assert.Equal("GET (Hit)", mapped!.Method);
    }

    [Fact]
    public void Internal_spans_without_dependency_attributes_are_not_calls()
    {
        var json = OtlpGoldens.MongoFindOldSemconv
            .Replace("\"kind\": 3", "\"kind\": 1")
            .Replace("{ \"key\": \"db.system\", \"value\": { \"stringValue\": \"mongodb\" } },", "");

        Assert.Null(SpanToInteractionMapper.Map(Single(json), Options()));
    }

    [Fact]
    public void The_pair_it_produces_is_a_span_sourced_request_and_response_with_both_timestamps()
    {
        var mapped = SpanToInteractionMapper.Map(Single(OtlpGoldens.MongoFindOldSemconv), Options())!;

        var (request, response) = mapped.ToRecords();

        Assert.Equal("Request", request.Type);
        Assert.Equal("Response", response.Type);
        Assert.Equal(request.RequestResponseId, response.RequestResponseId);
        Assert.Equal(mapped.Start, request.Timestamp);
        Assert.Equal(mapped.End, response.Timestamp);
        Assert.Equal(InteractionMerger.SpanSource, request.CapturedBy);
        Assert.Equal(InteractionMerger.SpanSource, response.CapturedBy);
        Assert.Equal(OtlpGoldens.TestTraceId, request.ActivityTraceId);
        Assert.Equal("00f067aa0ba902b7", request.ActivitySpanId);

        var (requestLog, responseLog) = mapped.ToLogs();
        Assert.Equal("mongodb:///data-insights-Development/Trial", requestLog.Uri.ToString());
        Assert.Equal(mapped.End, responseLog.Timestamp);
        Assert.Equal(InteractionMerger.SpanSource, responseLog.CapturedBy);
    }
    [Fact]
    public void Mongo_handshake_and_auth_spans_are_connection_plumbing_not_calls()
    {
        // The wire tap never records hello/isMaster/sasl*/ping/...; the span view must agree, or every
        // connection the driver opens draws an `IsMaster → mongodb` arrow (seen live 2026-08-22).
        foreach (var plumbing in new[] { "isMaster", "hello", "saslContinue", "saslStart", "ping", "endSessions", "buildInfo" })
        {
            var json = OtlpGoldens.MongoFindOldSemconv
                .Replace("\"Trial.find\"", $"\"mongodb.{plumbing}\"")
                .Replace("{ \"key\": \"db.operation\", \"value\": { \"stringValue\": \"find\" } }",
                    $"{{ \"key\": \"db.operation\", \"value\": {{ \"stringValue\": \"{plumbing}\" }} }}");
            Assert.Null(SpanToInteractionMapper.Map(Single(json), Options()));
        }

        // A real command on the same connection still maps.
        Assert.NotNull(SpanToInteractionMapper.Map(Single(OtlpGoldens.MongoFindOldSemconv), Options()));
    }
}
