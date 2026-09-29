using System.Diagnostics;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// #87: an internal-flow popup holds the spans of the call it is attached to, never another request's
/// (<c>plans/SPAN_ATTRIBUTION_PLAN.md</c>). Two tests call one endpoint at overlapping times, each doing a known and
/// different number of queries. The trace, then the span tree, then the call's time decide which spans are a call's,
/// and a span that calls of two tests would both take goes to neither: its popup says how many it left out. Before,
/// a call with no trace id took every span of the run in its time, and calls sharing an ambient trace were told apart
/// by time alone, so one request's popup showed ~60 requests' queries.
/// </summary>
public class SpanAttributionTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly ActivitySource _source = new("Kronikol.Tests.SpanAttribution");
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public SpanAttributionTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Kronikol.Tests.SpanAttribution",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        foreach (var a in _activities) a.Dispose();
        _listener.Dispose();
        _source.Dispose();
    }

    // ─── (a) calls that record no trace id ─────────────────────

    [Fact]
    public void Two_tests_without_trace_ids_keep_their_own_spans_and_leave_out_the_ones_both_could_own()
    {
        // A calls from 0 to 100 ms, B from 90 to 160 ms. A call's time starts 50 ms before its request (a span can
        // start before the request is logged), so 40 to 100 ms is both tests' time.
        var a = Request("test-a", 0, 100);
        var b = Request("test-b", 90, 160);
        var aOwn = Spans("a-query", 10, 20, 30);
        var bOwn = Spans("b-query", 110, 120, 130, 140, 150);
        var both = Spans("either", 95, 98);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b], [.. aOwn, .. bOwn, .. both]);

        var aSegment = segments[Key(a)];
        var bSegment = segments[Key(b)];
        Assert.Equal(["a-query", "a-query", "a-query"], aSegment.Spans.Select(s => s.OperationName));
        Assert.Equal(Enumerable.Repeat("b-query", 5), bSegment.Spans.Select(s => s.OperationName));
        Assert.Equal(2, aSegment.SpansLeftOut);
        Assert.Equal(2, bSegment.SpansLeftOut);
    }

    [Fact]
    public void A_call_without_a_trace_id_does_not_take_a_span_another_tests_trace_claims()
    {
        var a = Request("test-a", 0, 100);
        var bTrace = ActivityTraceId.CreateRandom().ToString();
        var b = Request("test-b", 10, 90, bTrace);
        var bSpans = Spans("b-query", 20, 30, 40, 50, 60, traceId: bTrace);
        var aSpan = Spans("a-query", 70, traceId: null);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b], [.. bSpans, .. aSpan]);

        Assert.Equal(["a-query"], segments[Key(a)].Spans.Select(s => s.OperationName));
        Assert.Equal(Enumerable.Repeat("b-query", 5), segments[Key(b)].Spans.Select(s => s.OperationName));
        Assert.Equal(0, segments[Key(a)].SpansLeftOut);
    }

    [Fact]
    public void The_whole_test_flow_of_a_test_without_trace_ids_holds_only_its_own_calls_spans()
    {
        var a = Request("test-a", 0, 100);
        var bTrace = ActivityTraceId.CreateRandom().ToString();
        var b = Request("test-b", 10, 90, bTrace);
        var bSpans = Spans("b-query", 20, 30, traceId: bTrace);
        var aSpan = Spans("a-query", 70, traceId: null);
        var elsewhere = Spans("another-run-span", 500, traceId: null);

        var whole = InternalFlowSegmentBuilder.BuildWholeTestSegments([.. a, .. b], [.. bSpans, .. aSpan, .. elsewhere]);

        Assert.Equal(["a-query"], whole["iflow-test-test-a"].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-query", "b-query"], whole["iflow-test-test-b"].Spans.Select(s => s.OperationName));
    }

    // ─── (b) calls sharing a trace through an ambient Activity ──

    [Fact]
    public void Tests_sharing_one_ambient_span_keep_what_only_they_could_own()
    {
        // One Activity around both tests: every call records its trace and its span.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var ambient = ActivitySpanId.CreateRandom().ToString();
        var a = Request("test-a", 0, 100, trace, ambient);
        var b = Request("test-b", 90, 160, trace, ambient);
        var aServer = Span("a-server", 5, trace, ambient);
        var aQueries = Children("a-query", aServer, 10, 20, 30);
        var bServer = Span("b-server", 105, trace, ambient);
        var bQueries = Children("b-query", bServer, 110, 120, 130, 140, 150);
        var both = Children("either", aServer, 95, 98);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b],
            [aServer, .. aQueries, bServer, .. bQueries, .. both]);

        Assert.Equal(["a-server", "a-query", "a-query", "a-query"], segments[Key(a)].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-server", "b-query", "b-query", "b-query", "b-query", "b-query"], segments[Key(b)].Spans.Select(s => s.OperationName));
        Assert.Equal(2, segments[Key(a)].SpansLeftOut);
    }

    [Fact]
    public void The_whole_test_flow_of_tests_sharing_one_ambient_span_holds_only_what_their_calls_kept()
    {
        // A trace other tests' calls claim too is not the test's to take whole: its flame chart holds what its calls
        // kept, as their popups do.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var ambient = ActivitySpanId.CreateRandom().ToString();
        var a = Request("test-a", 0, 100, trace, ambient);
        var b = Request("test-b", 90, 160, trace, ambient);
        var aServer = Span("a-server", 5, trace, ambient);
        var aQueries = Children("a-query", aServer, 10, 20, 30);
        var bServer = Span("b-server", 105, trace, ambient);
        var bQueries = Children("b-query", bServer, 110, 120);
        var both = Children("either", aServer, 95, 98);

        var whole = InternalFlowSegmentBuilder.BuildWholeTestSegments([.. a, .. b],
            [aServer, .. aQueries, bServer, .. bQueries, .. both]);

        Assert.Equal(["a-server", "a-query", "a-query", "a-query"], whole["iflow-test-test-a"].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-server", "b-query", "b-query"], whole["iflow-test-test-b"].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public void Each_tests_own_ambient_span_under_one_trace_tells_its_calls_apart_by_the_span_tree()
    {
        // A parent Activity shared by the run, and one per test under it: the trace is shared, the spans are not.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var aTest = ActivitySpanId.CreateRandom().ToString();
        var bTest = ActivitySpanId.CreateRandom().ToString();
        var a = Request("test-a", 0, 100, trace, aTest);
        var b = Request("test-b", 0, 100, trace, bTest);
        var aServer = Span("a-server", 5, trace, aTest);
        var aQueries = Children("a-query", aServer, 10, 40, 70);
        var bServer = Span("b-server", 6, trace, bTest);
        var bQueries = Children("b-query", bServer, 15, 30, 45, 60, 75);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b], [aServer, .. aQueries, bServer, .. bQueries]);

        Assert.Equal(["a-server", "a-query", "a-query", "a-query"], segments[Key(a)].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-server", "b-query", "b-query", "b-query", "b-query", "b-query"], segments[Key(b)].Spans.Select(s => s.OperationName));
        Assert.Equal(0, segments[Key(a)].SpansLeftOut + segments[Key(b)].SpansLeftOut);
    }

    [Fact]
    public void A_call_without_a_trace_id_takes_what_its_tests_span_tree_holds_not_the_whole_shared_trace()
    {
        // Each test's HTTP call records the shared trace and the test's own span; each test's database call, like
        // every database tracker's, records no trace at all. The database call may take only what the span tree says
        // is its test's: the whole shared trace in its time would contest the other test's spans, and lose them from
        // the popup the tree had given them to.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var aTest = ActivitySpanId.CreateRandom().ToString();
        var bTest = ActivitySpanId.CreateRandom().ToString();
        var aHttp = Request("test-a", 0, 100, trace, aTest);
        var bHttp = Request("test-b", 0, 100, trace, bTest);
        var aDatabase = Request("test-a", 20, 60);
        var bDatabase = Request("test-b", 30, 70);
        var aServer = Span("a-server", 5, trace, aTest);
        var aQueries = Children("a-query", aServer, 25, 40);
        var bServer = Span("b-server", 6, trace, bTest);
        var bQueries = Children("b-query", bServer, 35, 50, 65);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. aHttp, .. bHttp, .. aDatabase, .. bDatabase],
            [aServer, .. aQueries, bServer, .. bQueries]);

        Assert.Equal(["a-server", "a-query", "a-query"], segments[Key(aHttp)].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-server", "b-query", "b-query", "b-query"], segments[Key(bHttp)].Spans.Select(s => s.OperationName));
        Assert.Equal(["a-server", "a-query", "a-query"], segments[Key(aDatabase)].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-server", "b-query", "b-query", "b-query"], segments[Key(bDatabase)].Spans.Select(s => s.OperationName));
        Assert.Equal(0, new[] { aHttp, bHttp, aDatabase, bDatabase }.Sum(call => segments[Key(call)].SpansLeftOut));
    }

    [Fact]
    public void A_trace_only_one_tests_calls_record_keeps_every_span_in_the_calls_time_whatever_the_tree_says()
    {
        // Nothing to tell apart, so nothing changes: a span of the trace that does not descend from the call's span
        // (work the test started beside the call) stays in the popup, as it did before #87's fix.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var aTest = ActivitySpanId.CreateRandom().ToString();
        var a = Request("test-a", 0, 100, trace, aTest);
        var server = Span("a-server", 10, trace, aTest);
        var beside = Span("a-beside", 20, trace, ActivitySpanId.CreateRandom().ToString());

        var segments = InternalFlowSegmentBuilder.BuildSegments(a, [server, beside]);

        Assert.Equal(["a-server", "a-beside"], segments[Key(a)].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public void Calls_with_traces_of_their_own_are_exact_whatever_overlaps()
    {
        var aTrace = ActivityTraceId.CreateRandom().ToString();
        var bTrace = ActivityTraceId.CreateRandom().ToString();
        var a = Request("test-a", 0, 100, aTrace);
        var b = Request("test-b", 0, 100, bTrace);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b],
            [.. Spans("a-query", 10, 20, 30, traceId: aTrace), .. Spans("b-query", 10, 20, 30, 40, 50, traceId: bTrace)]);

        Assert.Equal(3, segments[Key(a)].Spans.Length);
        Assert.Equal(5, segments[Key(b)].Spans.Length);
        Assert.Equal(0, segments[Key(a)].SpansLeftOut + segments[Key(b)].SpansLeftOut);
    }

    [Fact]
    public void Calls_of_one_test_still_share_the_spans_of_the_calls_nested_in_them()
    {
        // The test's call to the service (0 to 200 ms) and the service's call to a database inside it (50 to 100 ms)
        // record no trace: both popups show the query.
        var outer = Request("test-a", 0, 200);
        var inner = Request("test-a", 50, 100);
        var query = Spans("query", 60);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. outer, .. inner], [.. query]);

        Assert.Single(segments[Key(outer)].Spans);
        Assert.Single(segments[Key(inner)].Spans);
    }

    // ─── What the reader sees ──────────────────────────────────

    [Theory]
    [InlineData(false, InternalFlowFlameChartPosition.BehindWithToggle)]
    [InlineData(true, InternalFlowFlameChartPosition.BehindWithToggle)]
    [InlineData(true, InternalFlowFlameChartPosition.Underneath)]
    public void A_popup_that_left_spans_out_says_how_many_and_why(bool flameChart, InternalFlowFlameChartPosition position)
    {
        // The flame chart is on by default, and each of its layouts builds the popup's content anew.
        var a = Request("test-a", 0, 100);
        var b = Request("test-b", 90, 160);
        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b],
            [.. Spans("a-query", 10, 20, 30), .. Spans("either", 95, 98)]);

        var data = InternalFlowHtmlGenerator.BuildSegmentData(segments, InternalFlowDiagramStyle.ActivityDiagram,
            showFlameChart: flameChart, flameChartPosition: position);

        var aPopup = System.Text.Json.JsonSerializer.Serialize(data[Key(a)]);
        Assert.Contains("Internal Flow (3 spans, 2 left out)", aPopup, StringComparison.Ordinal);
        Assert.Contains("2 spans that started during this call are left out", aPopup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_that_lost_every_span_gets_a_popup_that_says_why_even_where_empty_popups_are_hidden()
    {
        var a = Request("test-a", 0, 100);
        var b = Request("test-b", 0, 100);
        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b], [.. Spans("either", 10, 20)]);

        var data = InternalFlowHtmlGenerator.BuildSegmentData(segments, InternalFlowDiagramStyle.ActivityDiagram,
            noDataBehavior: InternalFlowNoDataBehavior.HideLink);

        Assert.Contains("2 spans that started during this call are left out", System.Text.Json.JsonSerializer.Serialize(data[Key(a)]), StringComparison.Ordinal);
    }

    // ─── Fixture ───────────────────────────────────────────────

    private static string Key(RequestResponseLog[] call) => $"iflow-{call[0].RequestResponseId}";

    /// <summary>A request and its response, <paramref name="fromMs"/> to <paramref name="toMs"/> after T0.</summary>
    private static RequestResponseLog[] Request(string testId, int fromMs, int toMs, string? traceId = null, string? spanId = null)
    {
        var id = Guid.NewGuid();
        RequestResponseLog Log(RequestResponseType type, int ms) =>
            new("Test", testId, HttpMethod.Get, null, new Uri("http://sut/api/orders"), [], "Orders", "Caller", type,
                Guid.NewGuid(), id, false)
            { Timestamp = T0.AddMilliseconds(ms), ActivityTraceId = traceId, ActivitySpanId = spanId };
        return [Log(RequestResponseType.Request, fromMs), Log(RequestResponseType.Response, toMs)];
    }

    private Activity[] Spans(string name, params int[] startsMs) => Spans(name, startsMs, traceId: null);

    private Activity[] Spans(string name, int startMs, string? traceId) => Spans(name, [startMs], traceId);

    private Activity[] Spans(string name, int[] startsMs, string? traceId) =>
        startsMs.Select(ms => Span(name, ms, traceId ?? ActivityTraceId.CreateRandom().ToString(), null)).ToArray();

    private Activity[] Spans(string name, int a, int b, string? traceId) => Spans(name, [a, b], traceId);

    private Activity[] Spans(string name, int a, int b, int c, string? traceId) => Spans(name, [a, b, c], traceId);

    private Activity[] Spans(string name, int a, int b, int c, int d, int e, string? traceId) => Spans(name, [a, b, c, d, e], traceId);

    private Activity Span(string name, int startMs, string traceId, string? parentSpanId)
    {
        Activity.Current = null;
        var parent = new ActivityContext(ActivityTraceId.CreateFromString(traceId),
            parentSpanId is null ? default : ActivitySpanId.CreateFromString(parentSpanId), ActivityTraceFlags.Recorded);
        var span = _source.StartActivity(name, ActivityKind.Internal, parent)!;
        span.SetStartTime(T0.UtcDateTime.AddMilliseconds(startMs));
        span.SetEndTime(T0.UtcDateTime.AddMilliseconds(startMs + 5));
        _activities.Add(span);
        return span;
    }

    private Activity[] Children(string name, Activity parent, params int[] startsMs) =>
        startsMs.Select(ms => Span(name, ms, parent.TraceId.ToString(), parent.SpanId.ToString())).ToArray();
}
