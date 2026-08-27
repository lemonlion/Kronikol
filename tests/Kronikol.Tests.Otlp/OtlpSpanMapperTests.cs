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
    public void String_failure_statuses_mark_the_span_error()
    {
        var (request, response) = Pair(status: "Failed");
        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);
        Assert.Equal(OtlpStatusCode.Error, span.Status);
        Assert.Equal("Failed", span.StatusMessage);

        var (req2, resp2) = Pair(status: "OK");
        Assert.Equal(OtlpStatusCode.Unset, OtlpSpanMapper.Map(req2, resp2, Options(), ExportTime).Status);
    }

    [Fact]
    public void Event_meta_type_maps_to_producer_kind()
    {
        var (request, response) = Pair(metaType: RequestResponseMetaType.Event);
        var span = OtlpSpanMapper.Map(request, response, Options(), ExportTime);
        Assert.Equal(OtlpSpanKind.Producer, span.Kind);
    }

    [Theory]
    [InlineData(DependencyCategories.Redis, "redis")]
    [InlineData(DependencyCategories.MongoDB, "mongodb")]
    [InlineData(DependencyCategories.PostgreSQL, "postgresql")]
    [InlineData(DependencyCategories.MySQL, "mysql")]
    [InlineData(DependencyCategories.SqlServer, "mssql")]
    [InlineData(DependencyCategories.BigQuery, "bigquery")]
    [InlineData(DependencyCategories.SQLite, "sqlite")]
    [InlineData(DependencyCategories.Oracle, "oracle")]
    [InlineData(DependencyCategories.Elasticsearch, "elasticsearch")]
    [InlineData(DependencyCategories.CosmosDB, "cosmosdb")]
    [InlineData(DependencyCategories.DynamoDB, "dynamodb")]
    [InlineData(DependencyCategories.Spanner, "spanner")]
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
