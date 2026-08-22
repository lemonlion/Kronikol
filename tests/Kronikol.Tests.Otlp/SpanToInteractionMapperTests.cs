using Kronikol.Constants;
using Kronikol.Extensions.Otlp;
using Kronikol.Ingestion;

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
