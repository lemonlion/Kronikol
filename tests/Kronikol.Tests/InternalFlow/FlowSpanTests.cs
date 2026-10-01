using System.Diagnostics;
using System.Text.Json;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// T17 of <c>plans/INGEST_FIDELITY_PLAN.md</c> (S5): internal flow reads a <see cref="FlowSpan"/>, the seven fields it
/// has always read off an <see cref="Activity"/>, so a span another process recorded can be drawn. An in-process span
/// becomes one with nothing changed, and a segment of spans with no <see cref="Activity"/> behind them draws as one
/// of activities does.
/// </summary>
public class FlowSpanTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private const string SourceName = "Kronikol.Tests.FlowSpan";

    private readonly ActivitySource _source = new(SourceName);
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public FlowSpanTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == SourceName,
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

    [Fact]
    public void An_activity_becomes_a_flow_span_with_the_seven_fields_internal_flow_reads()
    {
        var root = Span("handler", 0, 40);
        var child = Span("SELECT orders", 5, 10, root);
        child.DisplayName = "SELECT orders WHERE id = @id";

        var span = FlowSpan.From(child);

        Assert.Equal(child.TraceId.ToString(), span.TraceId);
        Assert.Equal(child.SpanId.ToString(), span.SpanId);
        Assert.Equal(root.SpanId.ToString(), span.ParentSpanId);
        Assert.Equal("SELECT orders WHERE id = @id", span.Name);
        Assert.Equal(SourceName, span.Source);
        Assert.Equal(T0.UtcDateTime.AddMilliseconds(5), span.StartTimeUtc);
        Assert.Equal(TimeSpan.FromMilliseconds(10), span.Duration);
        Assert.Null(span.Service);
    }

    [Fact]
    public void A_root_activitys_parent_reads_as_all_zeros_as_internal_flow_has_always_read_it()
    {
        Assert.Equal("0000000000000000", FlowSpan.From(Span("handler", 0, 40)).ParentSpanId);
    }

    [Fact]
    public void A_segment_of_activities_reads_them_as_flow_spans_in_their_order()
    {
        var first = Span("first", 0, 5);
        var second = Span("second", 10, 5);

        var segment = new InternalFlowSegment(Guid.NewGuid(), RequestResponseType.Request, "t", null, null, [second, first]);

        Assert.Equal([second.SpanId.ToString(), first.SpanId.ToString()], segment.FlowSpans.Select(s => s.SpanId));
    }

    [Fact]
    public void A_built_segments_activities_come_back_as_its_spans()
    {
        var call = Call("test-a", 0, 100);
        var spans = new[] { Span("handler", 10, 40), Span("publish", 20, 5) };

        var segment = InternalFlowSegmentBuilder.BuildSegments(call, spans)[Key(call)];

        Assert.Equal(spans, segment.Spans);
        Assert.Equal(spans.Select(s => s.SpanId.ToString()), segment.FlowSpans.Select(s => s.SpanId));
    }

    [Fact]
    public void Replacing_a_segments_activities_replaces_what_it_draws()
    {
        var call = Call("test-a", 0, 100);
        var kept = Span("handler", 10, 40);
        var segment = InternalFlowSegmentBuilder.BuildSegments(call, [kept, Span("publish", 20, 5)])[Key(call)];
        Assert.Equal(2, segment.FlowSpans.Length);

        var filtered = segment with { Spans = [kept] };

        Assert.Equal([kept.SpanId.ToString()], filtered.FlowSpans.Select(s => s.SpanId));
        Assert.Contains("Internal Flow (1 span)", Popup(filtered), StringComparison.Ordinal);
    }

    [Fact]
    public void A_segment_of_spans_another_process_recorded_draws_them_and_holds_no_activity()
    {
        var segment = Foreign(
            Remote("50d3989e0b177afe", null, "request", "@fastify/otel", 0, 85.354),
            Remote("8b7f9746a6a508c9", "50d3989e0b177afe", "graphql.resolve charge", "@opentelemetry/instrumentation-graphql", 18, 63.29));

        Assert.Empty(segment.Spans);

        var activity = InternalFlowRenderer.RenderActivityDiagram(segment);
        Assert.Contains("|@fastify/otel|", activity, StringComparison.Ordinal);
        Assert.Contains(":request (85ms);", activity, StringComparison.Ordinal);
        Assert.Contains("|@opentelemetry/instrumentation-graphql|", activity, StringComparison.Ordinal);
        Assert.Contains(":graphql.resolve charge (63ms);", activity, StringComparison.Ordinal);
        Assert.True(activity.IndexOf(":request", StringComparison.Ordinal) < activity.IndexOf(":graphql.resolve charge", StringComparison.Ordinal));

        var tree = InternalFlowRenderer.RenderCallTree(segment);
        Assert.Contains("[@fastify/otel]</span> request", tree, StringComparison.Ordinal);
        Assert.Contains("graphql.resolve charge", tree, StringComparison.Ordinal);

        var flame = InternalFlowRenderer.GetFlameChartData(segment);
        Assert.Equal(["@fastify/otel", "@opentelemetry/instrumentation-graphql"], flame.Sources);
        Assert.Equal(2, flame.Spans.Length);
        Assert.Equal(1, flame.Spans[1][4]); // the resolver one level down

        Assert.Contains("Internal Flow (2 spans)", Popup(segment), StringComparison.Ordinal);
    }

    [Fact]
    public void A_whole_test_flow_of_spans_another_process_recorded_renders()
    {
        var segment = Foreign(Remote("50d3989e0b177afe", null, "request", "@fastify/otel", 0, 85.354));
        var segments = new Dictionary<string, InternalFlowSegment> { ["iflow-test-t-v2"] = segment with { TestId = "t-v2" } };

        var content = InternalFlowHtmlGenerator.GetWholeTestFlowContent(segments, "t-v2", [("POST: /graphql", T0)], WholeTestFlowVisualization.Both);

        Assert.NotNull(content);
        Assert.Equal(1, content.Value.SpanCount);
        Assert.Contains("iflow-puml-whole-t-v2", content.Value.ActivityHtml, StringComparison.Ordinal);
        Assert.Contains("data-flame-z=", content.Value.FlameHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_span_naming_itself_as_its_parent_is_drawn_as_a_root_rather_than_lost()
    {
        var segment = Foreign(Remote("50d3989e0b177afe", "50d3989e0b177afe", "request", "@fastify/otel", 0, 10));

        Assert.Single(InternalFlowRenderer.BuildSpanTree(segment.FlowSpans));
        Assert.Contains(":request (10ms);", InternalFlowRenderer.RenderActivityDiagram(segment), StringComparison.Ordinal);
    }

    [Fact]
    public void Spans_another_process_recorded_are_a_calls_by_its_trace_and_span_tree_as_activities_are()
    {
        // Two tests share one trace (an ambient span around both), each call made under a span of its own: the span
        // tree tells them apart, as it does for activities (SpanAttributionTests).
        const string Shared = "eb7b756166d44b0d1306973d5a79985f";
        var a = Call("test-a", 0, 100, Shared, "aaaaaaaaaaaaaaaa");
        var b = Call("test-b", 0, 100, Shared, "bbbbbbbbbbbbbbbb");
        FlowSpan[] spans =
        [
            Remote("aaaaaaaaaaaaaaaa", null, "a-server", "svc", 1, 50, Shared),
            Remote("aaaaaaaaaaaaaaa1", "aaaaaaaaaaaaaaaa", "a-query", "db", 5, 5, Shared),
            Remote("bbbbbbbbbbbbbbbb", null, "b-server", "svc", 2, 50, Shared),
            Remote("bbbbbbbbbbbbbbb1", "bbbbbbbbbbbbbbbb", "b-query", "db", 6, 5, Shared),
            Remote("bbbbbbbbbbbbbbb2", "bbbbbbbbbbbbbbbb", "b-query", "db", 7, 5, Shared),
        ];

        var segments = InternalFlowSegmentBuilder.SegmentsOf([.. a, .. b], spans);
        var whole = InternalFlowSegmentBuilder.WholeTestSegmentsOf([.. a, .. b], spans);

        Assert.Equal(["a-server", "a-query"], segments[Key(a)].FlowSpans.Select(s => s.Name));
        Assert.Equal(["b-server", "b-query", "b-query"], segments[Key(b)].FlowSpans.Select(s => s.Name));
        Assert.Equal(["a-server", "a-query"], whole["iflow-test-test-a"].FlowSpans.Select(s => s.Name));
        Assert.Equal(["b-server", "b-query", "b-query"], whole["iflow-test-test-b"].FlowSpans.Select(s => s.Name));
        Assert.All(segments.Values, s => Assert.Empty(s.Spans));
    }

    // ─── Fixture ───────────────────────────────────────────────

    private static string Key(RequestResponseLog[] call) => $"iflow-{call[0].RequestResponseId}";

    private static string Popup(InternalFlowSegment segment) =>
        JsonSerializer.Serialize(InternalFlowHtmlGenerator.BuildSegmentData(
            new Dictionary<string, InternalFlowSegment> { ["iflow-x"] = segment }, InternalFlowDiagramStyle.ActivityDiagram));

    private static RequestResponseLog[] Call(string testId, int fromMs, int toMs, string? traceId = null, string? spanId = null)
    {
        var id = Guid.NewGuid();
        RequestResponseLog Log(RequestResponseType type, int ms) =>
            new("Test", testId, HttpMethod.Post, null, new Uri("http://sut/graphql"), [], "Payments", "Caller", type,
                Guid.NewGuid(), id, false)
            { Timestamp = T0.AddMilliseconds(ms), ActivityTraceId = traceId, ActivitySpanId = spanId };
        return [Log(RequestResponseType.Request, fromMs), Log(RequestResponseType.Response, toMs)];
    }

    private static FlowSpan Remote(string spanId, string? parentSpanId, string name, string source, double startMs, double durationMs,
        string traceId = "eb7b756166d44b0d1306973d5a79985f") =>
        new(traceId, spanId, parentSpanId, name, source, T0.UtcDateTime.AddMilliseconds(startMs), TimeSpan.FromMilliseconds(durationMs), "payments");

    private static InternalFlowSegment Foreign(params FlowSpan[] spans) =>
        InternalFlowSegment.Of(Guid.NewGuid(), RequestResponseType.Request, "t", T0, T0.AddMilliseconds(100), spans);

    private Activity Span(string name, int startMs, int durationMs, Activity? parent = null)
    {
        Activity.Current = null;
        var context = parent is null
            ? new ActivityContext(ActivityTraceId.CreateRandom(), default, ActivityTraceFlags.Recorded)
            : new ActivityContext(parent.TraceId, parent.SpanId, ActivityTraceFlags.Recorded);
        var span = _source.StartActivity(name, ActivityKind.Internal, context)!;
        span.SetStartTime(T0.UtcDateTime.AddMilliseconds(startMs));
        span.SetEndTime(T0.UtcDateTime.AddMilliseconds(startMs + durationMs));
        _activities.Add(span);
        return span;
    }
}
