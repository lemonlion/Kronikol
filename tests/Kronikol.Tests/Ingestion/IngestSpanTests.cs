using System.Diagnostics;
using System.Text.Json;
using Kronikol.Extensions.Otlp;
using Kronikol.Ingestion;
using Kronikol.InternalFlow;
using Kronikol.Reports;
using Kronikol.Tests.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// <c>plans/INGEST_FIDELITY_PLAN.md</c> S5 (T18, T19): an ingest handed spans (<see cref="IngestRequest.Spans"/>, what
/// <c>kronikol ingest --spans</c> reads) draws internal flow from them as an in-process run draws it from its activities:
/// a call joins its spans by the <c>activityTraceId</c> and <c>activitySpanId</c> its capturer stamped. The spans are the
/// OpenTelemetry-under-Jest spike's (S0, variant V2): one GraphQL request to a Fastify and Mercurius service, whose
/// resolver called a payment provider and a risk service, and one span of the service's start-up, in a trace of its own.
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestSpanTests : IDisposable
{
    private const string Trace = "eb7b756166d44b0d1306973d5a79985f";
    private const string OnRequestSpan = "c6103b1e3b3c954e";
    private const string ResolverSpan = "8b7f9746a6a508c9";

    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "TestData", "Ingest", "otel-jest-v2.spans.jsonl");

    /// <summary>The second the spike's spans were recorded in; the request span started 363 ms into it.</summary>
    private static readonly DateTimeOffset Second = DateTimeOffset.FromUnixTimeMilliseconds(1790755298000);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-ingest-spans-" + Guid.NewGuid().ToString("N"));
    private readonly string _testId = "t-v2-" + Guid.NewGuid().ToString("N")[..8];
    private readonly Guid _graphQl = Guid.NewGuid();
    private readonly Guid _psp = Guid.NewGuid();
    private readonly Guid _risk = Guid.NewGuid();

    public IngestSpanTests()
    {
        Directory.CreateDirectory(_dir);
        RequestResponseLogger.Redaction = null;
    }

    public void Dispose()
    {
        RequestResponseLogger.Redaction = null;
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    // ─── T18 ───────────────────────────────────────────────────

    [Fact]
    public void The_graphql_calls_popup_holds_the_resolver_tree_and_the_test_its_whole_flow()
    {
        var options = Options();
        Assert.Equal(InternalFlowSpanGranularity.AutoInstrumentation, options.InternalFlowSpanGranularity);
        Assert.False(options.InternalFlowTracking); // an ingest's default: spans turn it on

        var result = Ingest(Spike(), options);

        var html = File.ReadAllText(result.TestRunReportHtml);
        var map = InternalFlowSegmentMapReportTests.PageMap(html);
        var graphQl = SegmentMapText.Resolve(map, Key(_graphQl));
        Assert.Equal("Internal Flow (15 spans)", graphQl.Title);
        Assert.Contains("graphql.resolve charge", graphQl.Content, StringComparison.Ordinal);
        Assert.Contains("|@opentelemetry/instrumentation-graphql|", graphQl.Content, StringComparison.Ordinal);
        Assert.Contains("graphql.resolve charge", graphQl.FlameData, StringComparison.Ordinal);
        // The calls the resolver made show the spans that started in their time: the risk call its own GET.
        Assert.Contains(":GET (15ms);", SegmentMapText.Resolve(map, Key(_risk)).Content, StringComparison.Ordinal);
        // The service's start-up is a trace no call carries: no popup and no whole-test flow holds it.
        Assert.DoesNotContain("graphql.parseSchema", map.GetRawText(), StringComparison.Ordinal);
        // The scenario's Activity Diagrams tab: its whole flow, one activity per span of the test's trace.
        var whole = WholeTestActivity(html);
        Assert.Equal(15, whole.Split('\n').Count(line => line.StartsWith(':')));
        Assert.Contains("graphql.resolve charge", whole, StringComparison.Ordinal);
        Assert.DoesNotContain("graphql.parseSchema", whole, StringComparison.Ordinal);

        Assert.Contains(result.Diagnostics, d => d.Message.StartsWith("1 supplied span(s) are in no call's internal flow: 1 of a trace no call carries (graphql.parseSchema)", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("no supplied span has", StringComparison.Ordinal));
    }

    [Fact]
    public void A_call_whose_trace_no_supplied_span_has_is_counted()
    {
        var lines = Spike().Concat(Call(Guid.NewGuid(), "GET", "http://ledger.local/entries", "Ledger", "Payments GraphQL", 450, 460,
            trace: "ffffffffffffffffffffffffffffffff", span: "ffffffffffffffff")).ToArray();

        var result = Ingest(lines, Options());

        Assert.Contains(result.Diagnostics, d => d.Message.StartsWith("1 call(s) carry an activityTraceId no supplied span has", StringComparison.Ordinal));
    }

    [Fact]
    public void An_uppercase_trace_id_joins_its_spans_and_an_empty_span_id_is_absent()
    {
        var lines = Spike(graphQlTrace: Trace.ToUpperInvariant(), graphQlSpan: "");

        var result = Ingest(lines, Options());

        var graphQl = SegmentMapText.Resolve(InternalFlowSegmentMapReportTests.PageMap(File.ReadAllText(result.TestRunReportHtml)), Key(_graphQl));
        Assert.Equal("Internal Flow (15 spans)", graphQl.Title);
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ReportsDirectory, "TestRunReport.json")));
        var call = data.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("httpInteractions")[0];
        Assert.Equal(Trace, call.GetProperty("activityTraceId").GetString());
        Assert.False(call.TryGetProperty("activitySpanId", out var spanId) && spanId.ValueKind == JsonValueKind.String);
    }

    [Fact]
    public void A_span_supplied_twice_is_drawn_once_and_counted()
    {
        var spans = SpikeSpans();

        var result = Ingest(Spike(), Options(), [.. spans, .. SpikeSpans()]);

        var graphQl = SegmentMapText.Resolve(InternalFlowSegmentMapReportTests.PageMap(File.ReadAllText(result.TestRunReportHtml)), Key(_graphQl));
        Assert.Equal("Internal Flow (15 spans)", graphQl.Title);
        Assert.Contains(result.Diagnostics, d => d.Message.StartsWith($"{spans.Count} span(s) were supplied more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void An_ingest_given_no_spans_draws_no_internal_flow()
    {
        var result = Ingest(Spike(), Options(), spans: null);

        var html = File.ReadAllText(result.TestRunReportHtml);
        Assert.DoesNotContain("id=\"iflow-segments\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"iflow-puml-whole-{_testId}", html, StringComparison.Ordinal);
        Assert.All(InternalFlowSegmentMapReportTests.DiagramSourcesInPage(html), source => Assert.DoesNotContain("[[#iflow-", source, StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("supplied span", StringComparison.Ordinal));
    }

    [Fact]
    public void The_report_diagnostics_count_the_supplied_spans_where_a_run_counts_its_store()
    {
        Feature[] features = [new Feature { DisplayName = "F", Scenarios = [new Scenario { Id = "s", DisplayName = "S" }] }];

        Assert.Contains("Spans supplied to the ingest: 16 span(s).", ReportDiagnostics.Analyse([], features, false, true, suppliedSpans: 16));
        Assert.Contains(ReportDiagnostics.Analyse([], features, false, true, suppliedSpans: 0), line => line.StartsWith("Warning: no spans were supplied to the ingest", StringComparison.Ordinal));
        Assert.DoesNotContain(ReportDiagnostics.Analyse([], features, false, true, suppliedSpans: 16), line => line.Contains("InternalFlowSpanStore", StringComparison.Ordinal));
        Assert.Contains(ReportDiagnostics.Analyse([], features, false, true), line => line.Contains("InternalFlowSpanStore", StringComparison.Ordinal));
    }

    // ─── T19 ───────────────────────────────────────────────────

    [Fact]
    public void Manual_sources_narrow_the_supplied_spans()
    {
        var options = Options();
        options.InternalFlowSpanGranularity = InternalFlowSpanGranularity.Manual;
        options.InternalFlowActivitySources = ["@fastify/otel"];

        var result = Ingest(Spike(), options);

        var graphQl = SegmentMapText.Resolve(InternalFlowSegmentMapReportTests.PageMap(File.ReadAllText(result.TestRunReportHtml)), Key(_graphQl));
        Assert.Equal("Internal Flow (4 spans)", graphQl.Title);
        Assert.Contains("|@fastify/otel|", graphQl.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("graphql.resolve charge", graphQl.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void The_span_store_is_neither_read_nor_written_by_an_ingest_given_spans()
    {
        // A span of this process on the spike's trace, under its resolver, in the GraphQL call's time: read from the
        // store, it would be one of that call's spans.
        var sourceName = "Kronikol.Tests.IngestSpans." + Guid.NewGuid().ToString("N");
        using var source = new ActivitySource(sourceName);
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        Activity.Current = null;
        var name = "store-only-" + Guid.NewGuid().ToString("N");
        using var inStore = source.StartActivity(name, ActivityKind.Internal,
            new ActivityContext(ActivityTraceId.CreateFromString(Trace), ActivitySpanId.CreateFromString(ResolverSpan), ActivityTraceFlags.Recorded))!;
        inStore.SetStartTime(Second.AddMilliseconds(400).UtcDateTime);
        inStore.SetEndTime(Second.AddMilliseconds(401).UtcDateTime);
        InternalFlowSpanStore.Add(inStore);

        var options = Options();
        options.InternalFlowSpanGranularity = InternalFlowSpanGranularity.Full;
        var result = Ingest(Spike(), options);

        var html = File.ReadAllText(result.TestRunReportHtml);
        Assert.DoesNotContain(name, InternalFlowSegmentMapReportTests.PageMap(html).GetRawText(), StringComparison.Ordinal);
        Assert.Equal("Internal Flow (15 spans)", SegmentMapText.Resolve(InternalFlowSegmentMapReportTests.PageMap(html), Key(_graphQl)).Title);
        var onTrace = InternalFlowSpanStore.GetSpans().Where(a => a.TraceId.ToString() == Trace).ToArray();
        Assert.Same(inStore, Assert.Single(onTrace));
    }

    // ─── Fixture ───────────────────────────────────────────────

    private static string Key(Guid call) => $"iflow-{call}";

    /// <summary>The scenario's whole-flow activity diagram, from the page's <c>puml-data</c> block.</summary>
    private string WholeTestActivity(string html)
    {
        const string tag = "<script id=\"puml-data\" type=\"application/json\">";
        for (var at = html.IndexOf(tag, StringComparison.Ordinal); at >= 0; at = html.IndexOf(tag, at + tag.Length, StringComparison.Ordinal))
        {
            var start = at + tag.Length;
            if (start >= html.Length || html[start] != '{')
                continue;
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(html[start..html.IndexOf("</script>", start, StringComparison.Ordinal)])!;
            Assert.True(map.TryGetValue($"iflow-puml-whole-{_testId}", out var compressed), "the page holds the scenario's whole flow");
            return InternalFlowHtmlGenerator.DecompressFromBase64(compressed!);
        }

        Assert.Fail("the page carries no puml-data block");
        return "";
    }

    private ReportConfigurationOptions Options()
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "Reports");
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;
        return options;
    }

    private static List<FlowSpan> SpikeSpans() =>
        OtlpTraceReader.ReadJsonLines(Fixture, malformed: null).Select(s => s.ToFlowSpan()).ToList();

    private IngestResult Ingest(string[] lines, ReportConfigurationOptions options) => Ingest(lines, options, SpikeSpans());

    private IngestResult Ingest(string[] lines, ReportConfigurationOptions options, IReadOnlyList<FlowSpan>? spans)
    {
        var capture = Path.Combine(_dir, "interactions.ndjson");
        File.WriteAllLines(capture, lines);
        return IngestPipeline.Run(new IngestRequest
        {
            InteractionFiles = [capture],
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = _testId, TestName = "charges a card", Timestamp = Second.AddMilliseconds(300) },
                new TestRunRecord { Event = "end", TestId = _testId, Status = "passed", Timestamp = Second.AddMilliseconds(500) },
            ],
            Options = options,
            Spans = spans,
        });
    }

    /// <summary>
    /// What the spike's harness saw, stamped as it stamped it: the GraphQL request in Fastify's onRequest hook, under the
    /// hook's span; the two calls the resolver made in MSW's listener, under the resolver's span.
    /// </summary>
    private string[] Spike(string graphQlTrace = Trace, string graphQlSpan = OnRequestSpan) =>
    [
        .. Call(_graphQl, "POST", "http://payments.local/graphql", "Payments GraphQL", "Jest", 365, 449, graphQlTrace, graphQlSpan),
        .. Call(_psp, "POST", "https://psp.example.test/charges/c2", "PSP", "Payments GraphQL", 382, 416, Trace, ResolverSpan),
        .. Call(_risk, "GET", "http://127.0.0.1:45053/score/c2", "Risk", "Payments GraphQL", 418, 433, Trace, ResolverSpan),
    ];

    private string[] Call(Guid id, string method, string uri, string service, string caller, int fromMs, int toMs, string trace, string span)
    {
        string Line(string type, int ms, string extra) =>
            $$"""{"type":"{{type}}","method":"{{method}}","uri":"{{uri}}","serviceName":"{{service}}","callerName":"{{caller}}","requestResponseId":"{{id}}","timestamp":"{{Second.AddMilliseconds(ms):O}}","testId":"{{_testId}}","activityTraceId":"{{trace}}","activitySpanId":"{{span}}"{{extra}}}""";
        return [Line("Request", fromMs, ""), Line("Response", toMs, ",\"statusCode\":\"200\"")];
    }
}
