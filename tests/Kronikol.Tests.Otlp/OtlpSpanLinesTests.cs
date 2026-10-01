using Kronikol.Extensions.Otlp;
using Kronikol.Ingestion;

namespace Kronikol.Tests.Otlp;

/// <summary>
/// <c>plans/INGEST_FIDELITY_PLAN.md</c> S5 (T17, T20): the span stream <c>kronikol ingest --spans</c> reads is OTLP/JSON
/// lines, one <c>TracesData</c> object per line, as OpenTelemetry JS's <c>JsonTraceSerializer</c> and the Collector's file
/// exporter write them; each span becomes a <see cref="Kronikol.InternalFlow.FlowSpan"/>, the shape internal flow draws.
/// A line that is not one is skipped and counted, as a torn interaction line is, unless the read is strict.
/// </summary>
public class OtlpSpanLinesTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "TestData", "otel-jest-v2.spans.jsonl");

    private const string Trace = "eb7b756166d44b0d1306973d5a79985f";

    [Fact]
    public void The_jest_spikes_spans_file_reads_whole()
    {
        var malformed = new List<MalformedLine>();

        var spans = OtlpTraceReader.ReadJsonLines(Fixture, malformed);

        Assert.Empty(malformed);
        Assert.Equal(16, spans.Count);
        Assert.Equal(15, spans.Count(s => s.TraceId == Trace));
        var resolver = Assert.Single(spans, s => s.Name == "graphql.resolve charge");
        Assert.Equal("61125bf9491f6609", resolver.ParentSpanId);
        Assert.Equal("@opentelemetry/instrumentation-graphql", resolver.ScopeName);
    }

    [Fact]
    public void A_span_becomes_a_flow_span_with_its_scope_as_its_source()
    {
        var spans = OtlpTraceReader.ReadJsonLines(Fixture, malformed: null);
        var resolver = spans.Single(s => s.Name == "graphql.resolve charge");
        var root = spans.Single(s => s.Name == "request");

        var flow = resolver.ToFlowSpan();

        Assert.Equal(Trace, flow.TraceId);
        Assert.Equal("8b7f9746a6a508c9", flow.SpanId);
        Assert.Equal("61125bf9491f6609", flow.ParentSpanId);
        Assert.Equal("graphql.resolve charge", flow.Name);
        Assert.Equal("@opentelemetry/instrumentation-graphql", flow.Source);
        Assert.Equal("unknown_service:node", flow.Service);
        Assert.Equal(DateTimeKind.Utc, flow.StartTimeUtc.Kind);
        Assert.Equal(resolver.StartTime.UtcDateTime, flow.StartTimeUtc);
        Assert.Equal(resolver.EndTime - resolver.StartTime, flow.Duration);
        Assert.InRange(flow.Duration.TotalMilliseconds, 63.28, 63.30);
        Assert.Null(root.ToFlowSpan().ParentSpanId);
        Assert.Equal("@fastify/otel", root.ToFlowSpan().Source);
    }

    [Fact]
    public void A_span_with_no_scope_takes_its_service_as_its_source_and_one_with_neither_none()
    {
        var withService = new OtlpSpan
        {
            TraceId = Trace, SpanId = "00f067aa0ba902b7", Name = "charge",
            ResourceAttributes = new Dictionary<string, string> { ["service.name"] = "payments" },
        };
        var bare = withService with { ResourceAttributes = OtlpSpan.NoAttributes };

        Assert.Equal("payments", withService.ToFlowSpan().Source);
        Assert.Equal("", bare.ToFlowSpan().Source);
        Assert.Null(bare.ToFlowSpan().Service);
    }

    [Fact]
    public void A_torn_line_and_a_line_that_is_not_traces_data_are_skipped_and_counted()
    {
        var malformed = new List<MalformedLine>();

        var spans = OtlpTraceReader.ReadJsonLines(new StringReader(string.Join('\n',
            Line("1111111111111111"),
            "",
            """{"resourceSpans":[{"scopeSpans":[{"spans":[{"traceId":""",
            """{"type":"Request","testId":"t1"}""",
            Line("2222222222222222"))), "spans.jsonl", malformed);

        Assert.Equal(["1111111111111111", "2222222222222222"], spans.Select(s => s.SpanId));
        Assert.Equal([3, 4], malformed.Select(m => m.LineNumber));
        Assert.All(malformed, m => Assert.Equal("spans.jsonl", m.Source));
        Assert.Contains("resourceSpans", malformed[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_strict_read_fails_on_the_first_bad_line_and_names_it()
    {
        var reader = new StringReader(string.Join('\n', Line("1111111111111111"), "{\"resourceSpans\":[", Line("2222222222222222")));

        var ex = Assert.Throws<FormatException>(() => OtlpTraceReader.ReadJsonLines(reader, "spans.jsonl", malformed: null));

        Assert.StartsWith("spans.jsonl:2: ", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_span_with_no_trace_id_or_span_id_is_skipped_and_its_line_counted()
    {
        var malformed = new List<MalformedLine>();
        var line = $$"""{"resourceSpans":[{"scopeSpans":[{"scope":{"name":"s"},"spans":[{{Span("1111111111111111")}},{"traceId":"{{Trace}}","spanId":"","name":"no id","startTimeUnixNano":"1","endTimeUnixNano":"2"}]}]}]}""";

        var spans = OtlpTraceReader.ReadJsonLines(new StringReader(line), "spans.jsonl", malformed);

        Assert.Equal("1111111111111111", Assert.Single(spans).SpanId);
        var entry = Assert.Single(malformed);
        Assert.Equal(1, entry.LineNumber);
        Assert.Contains("1 span(s) with no trace id or span id", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pretty_printed_document_is_read_as_one()
    {
        var document = $$"""
            {
              "resourceSpans": [
                {
                  "scopeSpans": [
                    { "scope": { "name": "s" }, "spans": [ {{Span("1111111111111111")}} ] }
                  ]
                }
              ]
            }
            """;
        var malformed = new List<MalformedLine>();

        var spans = OtlpTraceReader.ReadJsonLines(new StringReader(document), "spans.json", malformed);

        Assert.Empty(malformed);
        Assert.Equal("1111111111111111", Assert.Single(spans).SpanId);
    }

    private static string Span(string spanId) =>
        $$"""{"traceId":"{{Trace}}","spanId":"{{spanId}}","name":"op {{spanId}}","kind":1,"startTimeUnixNano":"1790755298363000000","endTimeUnixNano":"1790755298364000000"}""";

    private static string Line(string spanId) =>
        $$$"""{"resourceSpans":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"payments"}}]},"scopeSpans":[{"scope":{"name":"s"},"spans":[{{{Span(spanId)}}}]}]}]}""";
}
