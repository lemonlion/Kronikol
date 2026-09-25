using System.Diagnostics;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Tests.InternalFlow;

public class InternalFlowRendererTests : IDisposable
{
    private readonly ActivitySource _source = new("Kronikol.Tests.InternalFlow");
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public InternalFlowRendererTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
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

    private Activity CreateSpan(string name, Activity? parent = null, TimeSpan? duration = null)
    {
        Activity? span;
        if (parent != null)
        {
            var ctx = new ActivityContext(parent.TraceId, parent.SpanId, ActivityTraceFlags.Recorded);
            span = _source.StartActivity(name, ActivityKind.Internal, ctx)!;
        }
        else
        {
            span = _source.StartActivity(name)!;
        }

        if (duration.HasValue)
        {
            // Set duration by stopping after the specified time
            span.SetEndTime(span.StartTimeUtc + duration.Value);
        }
        else
        {
            span.SetEndTime(span.StartTimeUtc + TimeSpan.FromMilliseconds(10));
        }

        _activities.Add(span);
        return span;
    }

    private static InternalFlowSegment MakeSegment(params Activity[] spans) =>
        new(Guid.NewGuid(), RequestResponseType.Request, "test-1",
            spans.Length > 0 ? spans.Min(s => s.StartTimeUtc) : null,
            spans.Length > 0 ? spans.Max(s => s.StartTimeUtc + s.Duration) : null,
            spans);

    // ── BuildSpanTree ──

    [Fact]
    public void BuildSpanTree_with_duplicate_SpanIds_does_not_throw()
    {
        var span1 = CreateSpan("op1");
        // Use the same span twice to simulate duplicate SpanId
        var spans = new[] { span1, span1 };

        var roots = InternalFlowRenderer.BuildSpanTree(spans);

        Assert.Single(roots);
    }

    [Fact]
    public void BuildSpanTree_builds_parent_child_hierarchy()
    {
        var parent = CreateSpan("parent");
        var child = CreateSpan("child", parent);

        var roots = InternalFlowRenderer.BuildSpanTree([parent, child]);

        Assert.Single(roots);
        Assert.Equal("parent", roots[0].Span.OperationName);
        Assert.Single(roots[0].Children);
        Assert.Equal("child", roots[0].Children[0].Span.OperationName);
    }

    [Fact]
    public void BuildSpanTree_sorts_roots_by_start_time()
    {
        // Create two independent root spans with explicit start times — no sleeping needed
        var earlyTime = DateTime.UtcNow;
        var lateTime = earlyTime.AddSeconds(1);

        Activity.Current = null;
        var early = _source.StartActivity("early", ActivityKind.Internal,
            new ActivityContext(ActivityTraceId.CreateRandom(), default, ActivityTraceFlags.Recorded))!;
        early.SetStartTime(earlyTime);
        early.SetEndTime(earlyTime.AddMilliseconds(10));
        _activities.Add(early);

        Activity.Current = null;
        var late = _source.StartActivity("late", ActivityKind.Internal,
            new ActivityContext(ActivityTraceId.CreateRandom(), default, ActivityTraceFlags.Recorded))!;
        late.SetStartTime(lateTime);
        late.SetEndTime(lateTime.AddMilliseconds(10));
        _activities.Add(late);

        var roots = InternalFlowRenderer.BuildSpanTree([late, early]);

        Assert.Equal(2, roots.Count);
        Assert.Equal("early", roots[0].Span.OperationName);
        Assert.Equal("late", roots[1].Span.OperationName);
    }

    [Fact]
    public void BuildSpanTree_orphan_children_become_roots()
    {
        // Create a child whose parent is not in the span array
        var parent = CreateSpan("parent");
        var child = CreateSpan("child", parent);

        // Only include the child
        var roots = InternalFlowRenderer.BuildSpanTree([child]);

        Assert.Single(roots);
        Assert.Equal("child", roots[0].Span.OperationName);
    }

    [Fact]
    public void BuildSpanTree_empty_array_returns_empty()
    {
        var roots = InternalFlowRenderer.BuildSpanTree([]);
        Assert.Empty(roots);
    }

    // ── RenderActivityDiagram ──

    [Fact]
    public void RenderActivityDiagram_empty_spans_returns_empty()
    {
        var result = InternalFlowRenderer.RenderActivityDiagram(MakeSegment());
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void RenderActivityDiagram_produces_plantuml_with_swimlanes()
    {
        var parent = CreateSpan("HTTP GET /api");
        var child = CreateSpan("SELECT * FROM users", parent);

        var result = InternalFlowRenderer.RenderActivityDiagram(MakeSegment(parent, child));

        Assert.Contains("@startuml", result);
        Assert.Contains("@enduml", result);
        Assert.Contains("HTTP GET /api", result);
        Assert.Contains("SELECT * FROM users", result);
    }

    [Fact]
    public void RenderActivityDiagram_escapes_loader_markup_in_a_span_name()
    {
        // The popup renders this through the same engine as the report's diagrams: an unescaped `<&` asks
        // for openiconic.js, which the render worker cannot load.
        var span = CreateSpan("Deserialize Vec<&str> for <:rocket:>");

        var result = InternalFlowRenderer.RenderActivityDiagram(MakeSegment(span));

        Assert.Contains("Deserialize Vec~<&str> for ~<:rocket:>", result);
        Assert.DoesNotMatch(@"(?<!~)<[&:$]", result);
    }

    [Fact]
    public void RenderActivityDiagram_with_duplicate_SpanIds_does_not_throw()
    {
        var span = CreateSpan("op");
        var result = InternalFlowRenderer.RenderActivityDiagram(MakeSegment(span, span));

        Assert.Contains("@startuml", result);
        Assert.Contains("op", result);
    }

    // ── RenderActivityDiagramBatched ──

    [Fact]
    public void RenderActivityDiagramBatched_small_span_count_returns_single_diagram()
    {
        var span1 = CreateSpan("op1");
        var span2 = CreateSpan("op2");

        var results = InternalFlowRenderer.RenderActivityDiagramBatched(MakeSegment(span1, span2), maxSpansPerBatch: 500);

        Assert.Single(results);
        Assert.Contains("op1", results[0]);
        Assert.Contains("op2", results[0]);
    }

    [Fact]
    public void RenderActivityDiagramBatched_splits_when_exceeding_max()
    {
        // Create 6 independent root spans, batch at 2
        var spans = new List<Activity>();
        for (int i = 0; i < 6; i++)
        {
            Activity.Current = null;
            var s = _source.StartActivity($"op{i}", ActivityKind.Internal,
                new ActivityContext(ActivityTraceId.CreateRandom(), default, ActivityTraceFlags.Recorded))!;
            s.SetEndTime(s.StartTimeUtc + TimeSpan.FromMilliseconds(10));
            _activities.Add(s);
            spans.Add(s);
        }

        var results = InternalFlowRenderer.RenderActivityDiagramBatched(MakeSegment(spans.ToArray()), maxSpansPerBatch: 2);

        Assert.Equal(3, results.Length);
        foreach (var diagram in results)
        {
            Assert.Contains("@startuml", diagram);
            Assert.Contains("@enduml", diagram);
        }
    }

    [Fact]
    public void RenderActivityDiagramBatched_keeps_child_spans_with_parent()
    {
        // Parent with 3 children = 4 spans total, batch at 3
        // Should NOT split parent from children
        var parent = CreateSpan("parent");
        var child1 = CreateSpan("child1", parent);
        var child2 = CreateSpan("child2", parent);
        var child3 = CreateSpan("child3", parent);

        var results = InternalFlowRenderer.RenderActivityDiagramBatched(
            MakeSegment(parent, child1, child2, child3), maxSpansPerBatch: 3);

        // All 4 spans under one root — should be in a single batch (can't split a tree)
        Assert.Single(results);
        Assert.Contains("parent", results[0]);
        Assert.Contains("child1", results[0]);
        Assert.Contains("child3", results[0]);
    }

    [Fact]
    public void RenderActivityDiagramBatched_empty_spans_returns_empty()
    {
        var results = InternalFlowRenderer.RenderActivityDiagramBatched(MakeSegment(), maxSpansPerBatch: 500);
        Assert.Empty(results);
    }

    [Fact]
    public void RenderActivityDiagramBatched_each_batch_has_header_label()
    {
        var spans = new List<Activity>();
        for (int i = 0; i < 4; i++)
        {
            Activity.Current = null;
            var s = _source.StartActivity($"op{i}", ActivityKind.Internal,
                new ActivityContext(ActivityTraceId.CreateRandom(), default, ActivityTraceFlags.Recorded))!;
            s.SetEndTime(s.StartTimeUtc + TimeSpan.FromMilliseconds(10));
            _activities.Add(s);
            spans.Add(s);
        }

        var results = InternalFlowRenderer.RenderActivityDiagramBatched(MakeSegment(spans.ToArray()), maxSpansPerBatch: 2);

        Assert.Equal(2, results.Length);
        Assert.Contains("Part 1", results[0]);
        Assert.Contains("Part 2", results[1]);
    }

    [Fact]
    public void RenderActivityDiagramBatched_caps_at_4_batches()
    {
        // Create 30 independent root spans, batch at 1 → 30 batches, capped at 3
        var spans = new List<Activity>();
        for (int i = 0; i < 30; i++)
        {
            Activity.Current = null;
            var s = _source.StartActivity($"op{i}", ActivityKind.Internal,
                new ActivityContext(ActivityTraceId.CreateRandom(), default, ActivityTraceFlags.Recorded))!;
            s.SetEndTime(s.StartTimeUtc + TimeSpan.FromMilliseconds(10));
            _activities.Add(s);
            spans.Add(s);
        }

        var results = InternalFlowRenderer.RenderActivityDiagramBatched(MakeSegment(spans.ToArray()), maxSpansPerBatch: 1);

        Assert.Equal(3, results.Length);
        Assert.Contains("Part 1 of 30 (showing first 3)", results[0]);
        Assert.Contains("Part 3 of 30 (showing first 3)", results[2]);
    }

    // ── RenderCallTree ──

    [Fact]
    public void RenderCallTree_empty_spans_returns_empty()
    {
        var result = InternalFlowRenderer.RenderCallTree(MakeSegment());
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void RenderCallTree_produces_nested_html_list()
    {
        var parent = CreateSpan("HTTP GET /api");
        var child = CreateSpan("DB Query", parent);

        var result = InternalFlowRenderer.RenderCallTree(MakeSegment(parent, child));

        Assert.Contains("<ul", result);
        Assert.Contains("HTTP GET /api", result);
        Assert.Contains("DB Query", result);
    }

    [Fact]
    public void RenderCallTree_with_duplicate_SpanIds_does_not_throw()
    {
        var span = CreateSpan("op");
        var result = InternalFlowRenderer.RenderCallTree(MakeSegment(span, span));

        Assert.Contains("<ul", result);
    }

    // ── GetFlameChartData ──

    [Fact]
    public void GetFlameChartData_caps_at_maxSpans()
    {
        // Create 20 independent root spans, cap at 5
        var spans = new List<Activity>();
        for (int i = 0; i < 20; i++)
        {
            Activity.Current = null;
            var s = _source.StartActivity($"op{i}", ActivityKind.Internal,
                new ActivityContext(ActivityTraceId.CreateRandom(), default, ActivityTraceFlags.Recorded))!;
            s.SetEndTime(s.StartTimeUtc + TimeSpan.FromMilliseconds(10));
            _activities.Add(s);
            spans.Add(s);
        }

        var data = InternalFlowRenderer.GetFlameChartData(MakeSegment(spans.ToArray()), maxSpans: 5);

        Assert.True(data.Spans.Length <= 5, $"Expected at most 5 spans but got {data.Spans.Length}");
        Assert.True(data.Spans.Length >= 1);
    }

    // ── RenderFlameChart ──

    [Fact]
    public void RenderFlameChart_empty_spans_returns_empty()
    {
        var result = InternalFlowRenderer.RenderFlameChart(MakeSegment());
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void RenderFlameChart_produces_flame_bars()
    {
        var parent = CreateSpan("HTTP GET /api", duration: TimeSpan.FromMilliseconds(100));
        var child = CreateSpan("DB Query", parent, duration: TimeSpan.FromMilliseconds(50));

        var result = InternalFlowRenderer.RenderFlameChart(MakeSegment(parent, child));

        Assert.Contains("iflow-flame", result);
        Assert.Contains("iflow-flame-bar", result);
        Assert.Contains("HTTP GET /api", result);
        Assert.Contains("DB Query", result);
    }

    [Fact]
    public void RenderFlameChart_with_duplicate_SpanIds_does_not_throw()
    {
        var span = CreateSpan("op", duration: TimeSpan.FromMilliseconds(10));
        var result = InternalFlowRenderer.RenderFlameChart(MakeSegment(span, span));

        Assert.Contains("iflow-flame", result);
    }

    // ── RenderFlameChartWithBoundaryMarkers ──

    [Fact]
    public void RenderFlameChartWithBoundaryMarkers_empty_spans_returns_empty()
    {
        var result = InternalFlowRenderer.RenderFlameChartWithBoundaryMarkers(MakeSegment(), []);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void RenderFlameChartWithBoundaryMarkers_includes_flame_bars()
    {
        var parent = CreateSpan("HTTP GET /api", duration: TimeSpan.FromMilliseconds(100));
        var child = CreateSpan("DB Query", parent, duration: TimeSpan.FromMilliseconds(50));

        var result = InternalFlowRenderer.RenderFlameChartWithBoundaryMarkers(MakeSegment(parent, child), []);

        Assert.Contains("iflow-flame", result);
        Assert.Contains("iflow-flame-bar", result);
        Assert.Contains("HTTP GET /api", result);
        Assert.Contains("DB Query", result);
    }

    [Fact]
    public void RenderFlameChartWithBoundaryMarkers_renders_boundary_markers()
    {
        var baseTime = DateTime.UtcNow;
        var span = CreateSpan("op", duration: TimeSpan.FromMilliseconds(200));
        span.SetStartTime(baseTime);
        span.SetEndTime(baseTime + TimeSpan.FromMilliseconds(200));

        var segment = MakeSegment(span);

        var boundaryLogs = new[]
        {
            MakeBoundaryLog("GET /api/orders", new DateTimeOffset(baseTime.AddMilliseconds(50), TimeSpan.Zero)),
            MakeBoundaryLog("POST /api/items", new DateTimeOffset(baseTime.AddMilliseconds(150), TimeSpan.Zero))
        };

        var result = InternalFlowRenderer.RenderFlameChartWithBoundaryMarkers(segment, boundaryLogs);

        Assert.Contains("iflow-boundary-marker", result);
        Assert.Contains("GET /api/orders", result);
        Assert.Contains("POST /api/items", result);
    }

    [Fact]
    public void RenderFlameChartWithBoundaryMarkers_marker_uses_dashed_style()
    {
        var baseTime = DateTime.UtcNow;
        var span = CreateSpan("op", duration: TimeSpan.FromMilliseconds(100));
        span.SetStartTime(baseTime);
        span.SetEndTime(baseTime + TimeSpan.FromMilliseconds(100));

        var boundaryLogs = new[]
        {
            MakeBoundaryLog("GET /orders", new DateTimeOffset(baseTime.AddMilliseconds(50), TimeSpan.Zero))
        };

        var result = InternalFlowRenderer.RenderFlameChartWithBoundaryMarkers(MakeSegment(span), boundaryLogs);

        Assert.Contains("left:", result);
        Assert.Contains("iflow-boundary-marker", result);
    }

    private static (string Label, DateTimeOffset Timestamp) MakeBoundaryLog(string label, DateTimeOffset timestamp)
        => (label, timestamp);

    // ── RenderGantt (obsolete but tested for backward compat) ──

#pragma warning disable CS0618
    [Fact]
    public void RenderGantt_empty_spans_returns_empty()
    {
        var result = InternalFlowRenderer.RenderGantt(MakeSegment());
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void RenderGantt_produces_plantuml_gantt()
    {
        var parent = CreateSpan("HTTP GET /api", duration: TimeSpan.FromMilliseconds(100));
        var child = CreateSpan("DB Query", parent, duration: TimeSpan.FromMilliseconds(50));

        var result = InternalFlowRenderer.RenderGantt(MakeSegment(parent, child));

        Assert.Contains("@startgantt", result);
        Assert.Contains("@endgantt", result);
        Assert.Contains("HTTP GET /api", result);
        Assert.Contains("DB Query", result);
    }

    [Fact]
    public void RenderGantt_shows_duration_in_tasks()
    {
        var span = CreateSpan("operation", duration: TimeSpan.FromMilliseconds(100));
        var result = InternalFlowRenderer.RenderGantt(MakeSegment(span));

        Assert.Contains("lasts", result);
    }
#pragma warning restore CS0618

    // ── RenderSequentialTestFlameChart ──

    [Fact]
    public void RenderSequentialTestFlameChart_empty_input_returns_empty()
    {
        var result = InternalFlowRenderer.RenderSequentialTestFlameChart([]);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void RenderSequentialTestFlameChart_renders_test_bands()
    {
        var span1 = CreateSpan("op1", duration: TimeSpan.FromMilliseconds(100));
        var span2 = CreateSpan("op2", duration: TimeSpan.FromMilliseconds(50));

        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-test-test1"] = new(Guid.Empty, RequestResponseType.Request, "test1", null, null, [span1]),
            ["iflow-test-test2"] = new(Guid.Empty, RequestResponseType.Request, "test2", null, null, [span2])
        };

        var result = InternalFlowRenderer.RenderSequentialTestFlameChart(segments);

        Assert.Contains("iflow-flame", result);
        Assert.Contains("iflow-test-band", result);
        Assert.Contains("op1", result);
        Assert.Contains("op2", result);
    }

    // ── data-diagram-type attributes ──

    [Fact]
    public void RenderFlameChart_has_data_diagram_type_flamechart()
    {
        var span = CreateSpan("op", duration: TimeSpan.FromMilliseconds(10));
        var result = InternalFlowRenderer.RenderFlameChart(MakeSegment(span));
        Assert.Contains("data-diagram-type=\"flamechart\"", result);
    }

    [Fact]
    public void RenderFlameChartWithBoundaryMarkers_has_data_diagram_type_flamechart()
    {
        var span = CreateSpan("op", duration: TimeSpan.FromMilliseconds(10));
        var result = InternalFlowRenderer.RenderFlameChartWithBoundaryMarkers(MakeSegment(span), []);
        Assert.Contains("data-diagram-type=\"flamechart\"", result);
    }

    [Fact]
    public void RenderSequentialTestFlameChart_has_data_diagram_type_flamechart()
    {
        var span = CreateSpan("op", duration: TimeSpan.FromMilliseconds(10));
        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-test-t"] = new(Guid.Empty, RequestResponseType.Request, "t", null, null, [span])
        };
        var result = InternalFlowRenderer.RenderSequentialTestFlameChart(segments);
        Assert.Contains("data-diagram-type=\"flamechart\"", result);
    }

    [Fact]
    public void RenderCallTree_has_data_diagram_type_calltree()
    {
        var span = CreateSpan("op");
        var result = InternalFlowRenderer.RenderCallTree(MakeSegment(span));
        Assert.Contains("data-diagram-type=\"calltree\"", result);
    }
}
