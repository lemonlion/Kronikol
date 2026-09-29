using System.Diagnostics;
using System.Net;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Tests.InternalFlow;

public class InternalFlowSegmentBuilderTests : IDisposable
{
    private readonly ActivitySource _source = new("Kronikol.Tests.SegmentBuilder");
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public InternalFlowSegmentBuilderTests()
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

    private Activity CreateSpan(string name, DateTime startUtc, TimeSpan duration, string? traceId = null)
    {
        Activity.Current = null;
        var ctx = traceId != null
            ? new ActivityContext(ActivityTraceId.CreateFromString(traceId), default, ActivityTraceFlags.Recorded)
            : new ActivityContext(ActivityTraceId.CreateRandom(), default, ActivityTraceFlags.Recorded);

        var span = _source.StartActivity(name, ActivityKind.Internal, ctx)!;
        span.SetStartTime(startUtc);
        span.SetEndTime(startUtc + duration);
        _activities.Add(span);
        return span;
    }

    private static RequestResponseLog MakeRequest(
        string testId = "test-1",
        DateTimeOffset? timestamp = null,
        Guid? requestResponseId = null,
        string? activityTraceId = null)
    {
        return new RequestResponseLog(
            TestName: "Test",
            TestId: testId,
            Method: HttpMethod.Get,
            Content: null,
            Uri: new Uri("http://sut/api/orders"),
            Headers: [],
            ServiceName: "OrderService",
            CallerName: "Caller",
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: requestResponseId ?? Guid.NewGuid(),
            TrackingIgnore: false)
        {
            Timestamp = timestamp,
            ActivityTraceId = activityTraceId
        };
    }

    private static RequestResponseLog MakeResponse(
        string testId = "test-1",
        DateTimeOffset? timestamp = null,
        Guid? requestResponseId = null,
        string? activityTraceId = null)
    {
        return new RequestResponseLog(
            TestName: "Test",
            TestId: testId,
            Method: HttpMethod.Get,
            Content: null,
            Uri: new Uri("http://sut/api/orders"),
            Headers: [],
            ServiceName: "OrderService",
            CallerName: "Caller",
            Type: RequestResponseType.Response,
            TraceId: Guid.NewGuid(),
            RequestResponseId: requestResponseId ?? Guid.NewGuid(),
            TrackingIgnore: false,
            StatusCode: HttpStatusCode.OK)
        {
            Timestamp = timestamp,
            ActivityTraceId = activityTraceId
        };
    }

    // A marker as DefaultTrackingDiagramOverride writes it (a step bar, an assertion note, the Setup/Action boundary):
    // a request record with no response and no trace id, which the logger stamps with a time since 3.15.1.
    private static RequestResponseLog MakeMarker(
        string testId,
        DateTimeOffset timestamp,
        DiagramMarkerKind kind,
        bool start = true,
        bool actionStart = false)
    {
        return new RequestResponseLog(testId, testId, "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsOverrideStart = start && !actionStart,
            IsOverrideEnd = !start && !actionStart,
            IsActionStart = actionStart,
            MarkerKind = kind,
            PlantUml = start && !actionStart ? "note over Caller: marker" : null,
            Timestamp = timestamp
        };
    }

    private static RequestResponseLog MakeUserAction(string testId, DateTimeOffset timestamp) =>
        new(testId, testId, "Click \"Place order\"", null, new Uri("http://web/orders"), [], "Web", "User",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsUserAction = true,
            Timestamp = timestamp
        };

    // ── Records that are not calls (#100) ──

    [Fact]
    public void BuildSegments_gives_no_segment_to_a_diagram_marker_or_a_user_action()
    {
        // Since 3.15.1 every record carries a time, marker records included, and the builder made a segment of each
        // from the test's spans between it and the next record. No arrow links a marker, and a user action's arrow
        // carries no link either, so each such segment was unreachable: 1,602 of 2,903 on an 8 MB report.
        var t0 = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
        var traceId1 = "0af7651916cd43dd8448eb211c80319c";
        var traceId2 = "aaaabbbbccccddddeeee111122223333";
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var root1 = CreateSpan("first.root", t0.UtcDateTime.AddMilliseconds(1), TimeSpan.FromMilliseconds(98), traceId1);
        // Work the service went on with after answering. Each marker's window below holds one of these, so each
        // marker segment has spans, as on the consumer's report, and would survive HideLink.
        var tailA = CreateSpan("first.tail-a", t0.UtcDateTime.AddMilliseconds(105), TimeSpan.FromMilliseconds(2), traceId1);
        var tailB = CreateSpan("first.tail-b", t0.UtcDateTime.AddMilliseconds(125), TimeSpan.FromMilliseconds(2), traceId1);
        var root2 = CreateSpan("second.root", t0.UtcDateTime.AddMilliseconds(201), TimeSpan.FromMilliseconds(98), traceId2);

        var logs = new[]
        {
            MakeRequest(timestamp: t0, requestResponseId: first, activityTraceId: traceId1),
            MakeResponse(timestamp: t0.AddMilliseconds(100), requestResponseId: first, activityTraceId: traceId1),
            MakeMarker("test-1", t0.AddMilliseconds(110), DiagramMarkerKind.Assertion),
            MakeMarker("test-1", t0.AddMilliseconds(111), DiagramMarkerKind.Assertion, start: false),
            MakeMarker("test-1", t0.AddMilliseconds(150), DiagramMarkerKind.Step),
            MakeMarker("test-1", t0.AddMilliseconds(151), DiagramMarkerKind.Step, start: false),
            MakeMarker("test-1", t0.AddMilliseconds(160), DiagramMarkerKind.Phase, actionStart: true),
            MakeUserAction("test-1", t0.AddMilliseconds(170)),
            MakeRequest(timestamp: t0.AddMilliseconds(200), requestResponseId: second, activityTraceId: traceId2),
            MakeResponse(timestamp: t0.AddMilliseconds(300), requestResponseId: second, activityTraceId: traceId2),
        };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [root1, tailA, tailB, root2]);

        Assert.Equal(
            new[] { $"iflow-{first}", $"iflow-{second}" }.OrderBy(k => k, StringComparer.Ordinal),
            result.Keys.OrderBy(k => k, StringComparer.Ordinal));
        // The calls' own segments are what they were.
        Assert.Equal(["first.root"], result[$"iflow-{first}"].Spans.Select(s => s.OperationName));
        Assert.Equal(["second.root"], result[$"iflow-{second}"].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public void BuildSegments_a_marker_still_ends_the_window_of_a_call_with_no_response()
    {
        // A marker gets no segment of its own, but it stays a point in time: a call with no response (an event
        // published) still runs to the next record, a marker included, so no arrow's popup changes with the fix.
        var t0 = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
        var published = Guid.NewGuid();
        var during = CreateSpan("during", t0.UtcDateTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(5));
        var after = CreateSpan("after", t0.UtcDateTime.AddMilliseconds(60), TimeSpan.FromMilliseconds(5));

        var logs = new[]
        {
            MakeRequest(timestamp: t0, requestResponseId: published),
            MakeMarker("test-1", t0.AddMilliseconds(50), DiagramMarkerKind.Step),
        };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [during, after]);

        Assert.Equal(["during"], result[$"iflow-{published}"].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public void BuildWholeTestSegments_a_test_whose_only_records_are_markers_or_user_actions_gets_none()
    {
        // A test with no trace id of its own falls back to every span, which is right for calls that carry no trace
        // id and wrong for a test that made no call at all: since 3.15.1 its step and assertion markers carry a time,
        // and its whole-test flow showed every span of the run.
        var t0 = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
        var traceId = "0af7651916cd43dd8448eb211c80319c";
        var span = CreateSpan("calls.root", t0.UtcDateTime.AddMilliseconds(1), TimeSpan.FromMilliseconds(50), traceId);

        var logs = new[]
        {
            MakeRequest("calls", t0, activityTraceId: traceId),
            MakeMarker("markers-only", t0.AddMilliseconds(5), DiagramMarkerKind.Step),
            MakeMarker("markers-only", t0.AddMilliseconds(6), DiagramMarkerKind.Step, start: false),
            MakeMarker("markers-only", t0.AddMilliseconds(7), DiagramMarkerKind.Assertion),
            MakeMarker("markers-only", t0.AddMilliseconds(8), DiagramMarkerKind.Phase, actionStart: true),
            MakeUserAction("clicks-only", t0.AddMilliseconds(9)),
        };

        var result = InternalFlowSegmentBuilder.BuildWholeTestSegments(logs, [span]);

        Assert.Equal(["iflow-test-calls"], result.Keys);
    }

    [Fact]
    public void BuildWholeTestBoundaries_draws_a_line_for_each_call_and_none_for_a_marker()
    {
        // The whole-test flame chart draws a dashed line at each request's time. Since 3.15.1 a marker is a request
        // with a time, so every step bar and assertion note drew a line labelled ": /" (no method, override.com's path).
        var t0 = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
        var logs = new[]
        {
            MakeMarker("test-1", t0, DiagramMarkerKind.Step),
            MakeMarker("test-1", t0.AddMilliseconds(1), DiagramMarkerKind.Step, start: false),
            MakeRequest(timestamp: t0.AddMilliseconds(10)),
            MakeResponse(timestamp: t0.AddMilliseconds(20)),
            MakeMarker("test-1", t0.AddMilliseconds(30), DiagramMarkerKind.Assertion),
            MakeMarker("test-1", t0.AddMilliseconds(31), DiagramMarkerKind.Assertion, start: false),
            MakeMarker("test-1", t0.AddMilliseconds(32), DiagramMarkerKind.Phase, actionStart: true),
            MakeUserAction("test-1", t0.AddMilliseconds(40)),
            MakeRequest("another-test", t0.AddMilliseconds(50)),
        };

        var boundaries = InternalFlowSegmentBuilder.BuildWholeTestBoundaries(logs, "test-1");

        // A user action is a real moment of the test and keeps its line; only the markers go.
        Assert.Equal(
            new[] { ("GET: /api/orders", t0.AddMilliseconds(10)), ("Click \"Place order\": /orders", t0.AddMilliseconds(40)) },
            boundaries);
    }

    [Fact]
    public void BuildWholeTestBoundaries_of_no_logs_is_empty() =>
        Assert.Empty(InternalFlowSegmentBuilder.BuildWholeTestBoundaries(null, "test-1"));

    // ── Empty inputs ──

    [Fact]
    public void BuildSegments_no_spans_returns_empty()
    {
        var logs = new[] { MakeRequest(timestamp: DateTimeOffset.UtcNow) };
        var result = InternalFlowSegmentBuilder.BuildSegments(logs, []);
        Assert.Empty(result);
    }

    [Fact]
    public void BuildSegments_no_logs_returns_empty()
    {
        var span = CreateSpan("op", DateTime.UtcNow, TimeSpan.FromMilliseconds(10));
        var result = InternalFlowSegmentBuilder.BuildSegments([], [span]);
        Assert.Empty(result);
    }

    [Fact]
    public void BuildSegments_logs_without_timestamps_returns_empty()
    {
        var span = CreateSpan("op", DateTime.UtcNow, TimeSpan.FromMilliseconds(10));
        var logs = new[] { MakeRequest(timestamp: null) };
        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [span]);
        Assert.Empty(result);
    }

    // ── Basic segment creation ──

    [Fact]
    public void BuildSegments_creates_segment_for_request_log()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var reqId = Guid.NewGuid();

        var span = CreateSpan("HTTP GET", baseTime.UtcDateTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(50));
        var logs = new[] { MakeRequest(timestamp: baseTime, requestResponseId: reqId) };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [span]);

        Assert.Single(result);
        Assert.True(result.ContainsKey($"iflow-{reqId}"));
        Assert.Single(result[$"iflow-{reqId}"].Spans);
    }

    [Fact]
    public void BuildSegments_skips_response_entries()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var reqId = Guid.NewGuid();
        var resId = Guid.NewGuid();

        var span = CreateSpan("op", baseTime.UtcDateTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(5));
        var logs = new[]
        {
            MakeRequest(timestamp: baseTime, requestResponseId: reqId),
            MakeResponse(timestamp: baseTime.AddMilliseconds(100), requestResponseId: resId)
        };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [span]);

        Assert.Single(result);
        Assert.True(result.ContainsKey($"iflow-{reqId}"));
        Assert.False(result.ContainsKey($"iflow-{resId}"));
    }

    // ── Time window logic ──

    [Fact]
    public void BuildSegments_assigns_spans_to_correct_time_windows()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var reqId1 = Guid.NewGuid();
        var reqId2 = Guid.NewGuid();

        var span1 = CreateSpan("early-op", baseTime.UtcDateTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(5));
        var span2 = CreateSpan("late-op", baseTime.UtcDateTime.AddMilliseconds(510), TimeSpan.FromMilliseconds(5));

        var logs = new[]
        {
            MakeRequest(timestamp: baseTime, requestResponseId: reqId1),
            MakeRequest(timestamp: baseTime.AddMilliseconds(500), requestResponseId: reqId2)
        };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [span1, span2]);

        Assert.Equal(2, result.Count);
        Assert.Single(result[$"iflow-{reqId1}"].Spans);
        Assert.Equal("early-op", result[$"iflow-{reqId1}"].Spans[0].OperationName);
        Assert.Single(result[$"iflow-{reqId2}"].Spans);
        Assert.Equal("late-op", result[$"iflow-{reqId2}"].Spans[0].OperationName);
    }

    [Fact]
    public void BuildSegments_last_segment_uses_5_second_fallback_window()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var reqId = Guid.NewGuid();

        // Span within 5-second window
        var spanInWindow = CreateSpan("in-window", baseTime.UtcDateTime.AddSeconds(2), TimeSpan.FromMilliseconds(5));
        // Span outside 5-second window
        var spanOutWindow = CreateSpan("out-window", baseTime.UtcDateTime.AddSeconds(6), TimeSpan.FromMilliseconds(5));

        var logs = new[] { MakeRequest(timestamp: baseTime, requestResponseId: reqId) };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [spanInWindow, spanOutWindow]);

        Assert.Single(result);
        Assert.Single(result[$"iflow-{reqId}"].Spans);
        Assert.Equal("in-window", result[$"iflow-{reqId}"].Spans[0].OperationName);
    }

    [Fact]
    public void BuildSegments_span_exactly_at_segment_start_is_included()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var reqId = Guid.NewGuid();

        var span = CreateSpan("at-boundary", baseTime.UtcDateTime, TimeSpan.FromMilliseconds(5));
        var logs = new[] { MakeRequest(timestamp: baseTime, requestResponseId: reqId) };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [span]);

        Assert.Single(result[$"iflow-{reqId}"].Spans);
    }

    [Fact]
    public void BuildSegments_span_exactly_at_segment_end_is_excluded()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var reqId1 = Guid.NewGuid();
        var reqId2 = Guid.NewGuid();
        var boundaryTime = baseTime.AddMilliseconds(500);

        var span = CreateSpan("at-boundary", boundaryTime.UtcDateTime, TimeSpan.FromMilliseconds(5));
        var logs = new[]
        {
            MakeRequest(timestamp: baseTime, requestResponseId: reqId1),
            MakeRequest(timestamp: boundaryTime, requestResponseId: reqId2)
        };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [span]);

        // Span at boundary should be in second segment (>= start), not first (< end)
        Assert.Empty(result[$"iflow-{reqId1}"].Spans);
        Assert.Single(result[$"iflow-{reqId2}"].Spans);
    }

    // ── Trace ID filtering ──

    [Fact]
    public void BuildSegments_filters_spans_by_trace_id()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var reqId = Guid.NewGuid();
        var goodTraceId = "0af7651916cd43dd8448eb211c80319c";
        var badTraceId = "aaaabbbbccccddddeeee111122223333";

        var matchingSpan = CreateSpan("matching", baseTime.UtcDateTime.AddMilliseconds(10),
            TimeSpan.FromMilliseconds(5), traceId: goodTraceId);
        var nonMatchingSpan = CreateSpan("non-matching", baseTime.UtcDateTime.AddMilliseconds(20),
            TimeSpan.FromMilliseconds(5), traceId: badTraceId);

        var logs = new[]
        {
            MakeRequest(timestamp: baseTime, requestResponseId: reqId, activityTraceId: goodTraceId)
        };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [matchingSpan, nonMatchingSpan]);

        Assert.Single(result[$"iflow-{reqId}"].Spans);
        Assert.Equal("matching", result[$"iflow-{reqId}"].Spans[0].OperationName);
    }

    [Fact]
    public void BuildSegments_null_trace_ids_returns_all_spans()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var reqId = Guid.NewGuid();

        var span1 = CreateSpan("op1", baseTime.UtcDateTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(5));
        var span2 = CreateSpan("op2", baseTime.UtcDateTime.AddMilliseconds(20), TimeSpan.FromMilliseconds(5));

        // No ActivityTraceId set — should fall back to returning all spans
        var logs = new[] { MakeRequest(timestamp: baseTime, requestResponseId: reqId, activityTraceId: null) };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [span1, span2]);

        Assert.Equal(2, result[$"iflow-{reqId}"].Spans.Length);
    }

    // ── Multi-test grouping ──

    [Fact]
    public void BuildSegments_groups_by_test_id()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var reqId1 = Guid.NewGuid();
        var reqId2 = Guid.NewGuid();

        var span1 = CreateSpan("test1-op", baseTime.UtcDateTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(5));
        var span2 = CreateSpan("test2-op", baseTime.UtcDateTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(5));

        var logs = new[]
        {
            MakeRequest(testId: "test-1", timestamp: baseTime, requestResponseId: reqId1, activityTraceId: null),
            MakeRequest(testId: "test-2", timestamp: baseTime, requestResponseId: reqId2, activityTraceId: null)
        };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [span1, span2]);

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey($"iflow-{reqId1}"));
        Assert.True(result.ContainsKey($"iflow-{reqId2}"));
    }

    // ── Segment ordering ──

    [Fact]
    public void BuildSegments_orders_spans_by_start_time_within_segment()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var reqId = Guid.NewGuid();

        var lateSpan = CreateSpan("late", baseTime.UtcDateTime.AddMilliseconds(50), TimeSpan.FromMilliseconds(5));
        var earlySpan = CreateSpan("early", baseTime.UtcDateTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(5));

        var logs = new[] { MakeRequest(timestamp: baseTime, requestResponseId: reqId) };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [lateSpan, earlySpan]);

        Assert.Equal(2, result[$"iflow-{reqId}"].Spans.Length);
        Assert.Equal("early", result[$"iflow-{reqId}"].Spans[0].OperationName);
        Assert.Equal("late", result[$"iflow-{reqId}"].Spans[1].OperationName);
    }

    // ── Edge case: span before first log ──

    [Fact]
    public void BuildSegments_span_before_first_log_is_excluded()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var reqId = Guid.NewGuid();

        var earlySpan = CreateSpan("too-early", baseTime.UtcDateTime.AddMilliseconds(-100), TimeSpan.FromMilliseconds(5));
        var logs = new[] { MakeRequest(timestamp: baseTime, requestResponseId: reqId) };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [earlySpan]);

        Assert.Empty(result[$"iflow-{reqId}"].Spans);
    }

    // ── BuildWholeTestSegments ──

    [Fact]
    public void BuildWholeTestSegments_no_spans_returns_empty()
    {
        var logs = new[] { MakeRequest(timestamp: DateTimeOffset.UtcNow) };
        var result = InternalFlowSegmentBuilder.BuildWholeTestSegments(logs, []);
        Assert.Empty(result);
    }

    [Fact]
    public void BuildWholeTestSegments_no_logs_returns_empty()
    {
        var span = CreateSpan("op", DateTime.UtcNow, TimeSpan.FromMilliseconds(10));
        var result = InternalFlowSegmentBuilder.BuildWholeTestSegments([], [span]);
        Assert.Empty(result);
    }

    [Fact]
    public void BuildWholeTestSegments_creates_one_segment_per_test()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var span1 = CreateSpan("op1", baseTime.UtcDateTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(50));
        var span2 = CreateSpan("op2", baseTime.UtcDateTime.AddMilliseconds(100), TimeSpan.FromMilliseconds(50));

        var logs = new[]
        {
            MakeRequest(testId: "test-1", timestamp: baseTime, activityTraceId: null),
            MakeRequest(testId: "test-1", timestamp: baseTime.AddMilliseconds(200), activityTraceId: null)
        };

        var result = InternalFlowSegmentBuilder.BuildWholeTestSegments(logs, [span1, span2]);

        Assert.Single(result);
        Assert.True(result.ContainsKey("iflow-test-test-1"));
        Assert.Equal(2, result["iflow-test-test-1"].Spans.Length);
    }

    [Fact]
    public void BuildWholeTestSegments_key_uses_iflow_test_prefix()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var span = CreateSpan("op", baseTime.UtcDateTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(5));
        var logs = new[] { MakeRequest(testId: "my-test", timestamp: baseTime, activityTraceId: null) };

        var result = InternalFlowSegmentBuilder.BuildWholeTestSegments(logs, [span]);

        Assert.True(result.ContainsKey("iflow-test-my-test"));
    }

    [Fact]
    public void BuildWholeTestSegments_includes_all_spans_for_test()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var traceId = "0af7651916cd43dd8448eb211c80319c";

        var span1 = CreateSpan("early", baseTime.UtcDateTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(5), traceId: traceId);
        var span2 = CreateSpan("middle", baseTime.UtcDateTime.AddMilliseconds(500), TimeSpan.FromMilliseconds(5), traceId: traceId);
        var span3 = CreateSpan("late", baseTime.UtcDateTime.AddSeconds(3), TimeSpan.FromMilliseconds(5), traceId: traceId);

        var logs = new[]
        {
            MakeRequest(testId: "test-1", timestamp: baseTime, activityTraceId: traceId),
            MakeRequest(testId: "test-1", timestamp: baseTime.AddMilliseconds(400), activityTraceId: traceId)
        };

        var result = InternalFlowSegmentBuilder.BuildWholeTestSegments(logs, [span1, span2, span3]);

        Assert.Equal(3, result["iflow-test-test-1"].Spans.Length);
    }

    [Fact]
    public void BuildWholeTestSegments_filters_by_trace_id()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var goodTraceId = "0af7651916cd43dd8448eb211c80319c";
        var badTraceId = "aaaabbbbccccddddeeee111122223333";

        var matching = CreateSpan("match", baseTime.UtcDateTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(5), traceId: goodTraceId);
        var nonMatching = CreateSpan("no-match", baseTime.UtcDateTime.AddMilliseconds(20), TimeSpan.FromMilliseconds(5), traceId: badTraceId);

        var logs = new[] { MakeRequest(testId: "test-1", timestamp: baseTime, activityTraceId: goodTraceId) };

        var result = InternalFlowSegmentBuilder.BuildWholeTestSegments(logs, [matching, nonMatching]);

        Assert.Single(result["iflow-test-test-1"].Spans);
        Assert.Equal("match", result["iflow-test-test-1"].Spans[0].OperationName);
    }

    [Fact]
    public void BuildWholeTestSegments_multiple_tests_get_separate_segments()
    {
        // Each test's calls record their own trace. Until 3.35.1 this fact gave both calls no trace id, and passed
        // because each test's flow then held every span of the run, the other test's included (#87): two tests at
        // one time with nothing to tell their spans apart now keep none (SpanAttributionTests).
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        const string trace1 = "0af7651916cd43dd8448eb211c80319c";
        const string trace2 = "1bf7651916cd43dd8448eb211c80319d";

        var span1 = CreateSpan("test1-op", baseTime.UtcDateTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(5), traceId: trace1);
        var span2 = CreateSpan("test2-op", baseTime.UtcDateTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(5), traceId: trace2);

        var logs = new[]
        {
            MakeRequest(testId: "test-1", timestamp: baseTime, activityTraceId: trace1),
            MakeRequest(testId: "test-2", timestamp: baseTime, activityTraceId: trace2)
        };

        var result = InternalFlowSegmentBuilder.BuildWholeTestSegments(logs, [span1, span2]);

        Assert.Equal(2, result.Count);
        Assert.Equal(["test1-op"], result["iflow-test-test-1"].Spans.Select(s => s.OperationName));
        Assert.Equal(["test2-op"], result["iflow-test-test-2"].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public void BuildWholeTestSegments_orders_spans_by_start_time()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var late = CreateSpan("late", baseTime.UtcDateTime.AddMilliseconds(100), TimeSpan.FromMilliseconds(5));
        var early = CreateSpan("early", baseTime.UtcDateTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(5));

        var logs = new[] { MakeRequest(testId: "test-1", timestamp: baseTime, activityTraceId: null) };

        var result = InternalFlowSegmentBuilder.BuildWholeTestSegments(logs, [late, early]);

        Assert.Equal("early", result["iflow-test-test-1"].Spans[0].OperationName);
        Assert.Equal("late", result["iflow-test-test-1"].Spans[1].OperationName);
    }

    [Fact]
    public void BuildWholeTestSegments_empty_test_with_no_matching_spans_excluded()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var traceId1 = "0af7651916cd43dd8448eb211c80319c";
        var traceId2 = "aaaabbbbccccddddeeee111122223333";

        var span = CreateSpan("op", baseTime.UtcDateTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(5), traceId: traceId1);

        var logs = new[]
        {
            MakeRequest(testId: "test-1", timestamp: baseTime, activityTraceId: traceId1),
            MakeRequest(testId: "test-2", timestamp: baseTime, activityTraceId: traceId2)
        };

        var result = InternalFlowSegmentBuilder.BuildWholeTestSegments(logs, [span]);

        Assert.Single(result);
        Assert.True(result.ContainsKey("iflow-test-test-1"));
    }

    // ── Response-based time windows ──

    [Fact]
    public void BuildSegments_uses_matching_response_timestamp_as_segment_end()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var reqId = Guid.NewGuid();

        // Span at T+200ms — only within window if response timestamp (T+500ms) is used as end,
        // NOT if next-log timestamp were used
        var span = CreateSpan("during-processing", baseTime.UtcDateTime.AddMilliseconds(200),
            TimeSpan.FromMilliseconds(5));

        var logs = new[]
        {
            MakeRequest(timestamp: baseTime, requestResponseId: reqId),
            MakeResponse(timestamp: baseTime.AddMilliseconds(500), requestResponseId: reqId)
        };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [span]);

        Assert.Single(result);
        Assert.Single(result[$"iflow-{reqId}"].Spans);
        Assert.Equal("during-processing", result[$"iflow-{reqId}"].Spans[0].OperationName);
    }

    [Fact]
    public void BuildSegments_outer_request_captures_spans_across_nested_sub_calls()
    {
        // Simulates: Caller→SUT at T0, SUT→ServiceA at T1, ServiceA→SUT at T2, SUT→Caller at T3
        // The Caller→SUT segment should capture spans throughout the entire [T0, T3) window
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var outerReqId = Guid.NewGuid();
        var innerReqId = Guid.NewGuid();

        var earlySpan = CreateSpan("validation", baseTime.UtcDateTime.AddMilliseconds(5),
            TimeSpan.FromMilliseconds(10));
        var midSpan = CreateSpan("transform-response", baseTime.UtcDateTime.AddMilliseconds(250),
            TimeSpan.FromMilliseconds(10));
        var lateSpan = CreateSpan("finalize", baseTime.UtcDateTime.AddMilliseconds(450),
            TimeSpan.FromMilliseconds(10));

        var logs = new[]
        {
            MakeRequest(timestamp: baseTime, requestResponseId: outerReqId),
            MakeRequest(timestamp: baseTime.AddMilliseconds(100), requestResponseId: innerReqId),
            MakeResponse(timestamp: baseTime.AddMilliseconds(200), requestResponseId: innerReqId),
            MakeResponse(timestamp: baseTime.AddMilliseconds(500), requestResponseId: outerReqId)
        };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [earlySpan, midSpan, lateSpan]);

        // Outer segment should capture ALL three spans (full processing window)
        var outerSegment = result[$"iflow-{outerReqId}"];
        Assert.Equal(3, outerSegment.Spans.Length);
        Assert.Equal("validation", outerSegment.Spans[0].OperationName);
        Assert.Equal("transform-response", outerSegment.Spans[1].OperationName);
        Assert.Equal("finalize", outerSegment.Spans[2].OperationName);

        // Inner segment only captures spans during the sub-call window [T+100, T+200)
        var innerSegment = result[$"iflow-{innerReqId}"];
        Assert.Empty(innerSegment.Spans); // No spans start during the sub-call wait
    }

    [Fact]
    public void BuildSegments_no_response_falls_back_to_next_log_timestamp()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var reqId1 = Guid.NewGuid();
        var reqId2 = Guid.NewGuid();

        var earlySpan = CreateSpan("early", baseTime.UtcDateTime.AddMilliseconds(10),
            TimeSpan.FromMilliseconds(5));
        var lateSpan = CreateSpan("late", baseTime.UtcDateTime.AddMilliseconds(510),
            TimeSpan.FromMilliseconds(5));

        // No response logs — falls back to next request's timestamp
        var logs = new[]
        {
            MakeRequest(timestamp: baseTime, requestResponseId: reqId1),
            MakeRequest(timestamp: baseTime.AddMilliseconds(500), requestResponseId: reqId2)
        };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [earlySpan, lateSpan]);

        Assert.Single(result[$"iflow-{reqId1}"].Spans);
        Assert.Equal("early", result[$"iflow-{reqId1}"].Spans[0].OperationName);
        Assert.Single(result[$"iflow-{reqId2}"].Spans);
        Assert.Equal("late", result[$"iflow-{reqId2}"].Spans[0].OperationName);
    }

    // ── Per-call TraceId isolation (Bug: spans bleed between calls) ──

    [Fact]
    public void BuildSegments_two_calls_with_different_trace_ids_isolates_spans_by_trace()
    {
        // Simulates: test makes 2 HTTP calls sequentially, each with a different W3C trace.
        // Call 1 has many spans (identity validation, token generation, etc.)
        // Call 2 has few spans (just client secret validation).
        // BUG: Without per-call TraceId filtering, both calls' popups show the same spans
        // because the timestamp windows overlap or misalign.
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var traceId1 = "0af7651916cd43dd8448eb211c80319c";
        var traceId2 = "aaaabbbbccccddddeeee111122223333";
        var reqId1 = Guid.NewGuid();
        var reqId2 = Guid.NewGuid();

        // Call 1 spans (T+5ms to T+15ms) — many internal spans
        var span1A = CreateSpan("TokenRequestValidator.Validate", baseTime.UtcDateTime.AddMilliseconds(5),
            TimeSpan.FromMilliseconds(2), traceId: traceId1);
        var span1B = CreateSpan("CachingResourceStore.Find", baseTime.UtcDateTime.AddMilliseconds(8),
            TimeSpan.FromMilliseconds(1), traceId: traceId1);
        var span1C = CreateSpan("TokenResponseGenerator.Process", baseTime.UtcDateTime.AddMilliseconds(12),
            TimeSpan.FromMilliseconds(3), traceId: traceId1);

        // Call 2 spans (T+25ms to T+35ms) — fewer spans
        var span2A = CreateSpan("ClientSecretValidator.Validate", baseTime.UtcDateTime.AddMilliseconds(25),
            TimeSpan.FromMilliseconds(5), traceId: traceId2);
        var span2B = CreateSpan("ClientStore.FindClientById", baseTime.UtcDateTime.AddMilliseconds(28),
            TimeSpan.FromMilliseconds(3), traceId: traceId2);

        var logs = new[]
        {
            MakeRequest(timestamp: baseTime, requestResponseId: reqId1, activityTraceId: traceId1),
            MakeResponse(timestamp: baseTime.AddMilliseconds(20), requestResponseId: reqId1, activityTraceId: traceId1),
            MakeRequest(timestamp: baseTime.AddMilliseconds(22), requestResponseId: reqId2, activityTraceId: traceId2),
            MakeResponse(timestamp: baseTime.AddMilliseconds(40), requestResponseId: reqId2, activityTraceId: traceId2)
        };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs,
            [span1A, span1B, span1C, span2A, span2B]);

        // Call 1 should only have its own 3 spans
        var seg1 = result[$"iflow-{reqId1}"];
        Assert.Equal(3, seg1.Spans.Length);
        Assert.Equal("TokenRequestValidator.Validate", seg1.Spans[0].OperationName);
        Assert.Equal("CachingResourceStore.Find", seg1.Spans[1].OperationName);
        Assert.Equal("TokenResponseGenerator.Process", seg1.Spans[2].OperationName);

        // Call 2 should only have its own 2 spans
        var seg2 = result[$"iflow-{reqId2}"];
        Assert.Equal(2, seg2.Spans.Length);
        Assert.Equal("ClientSecretValidator.Validate", seg2.Spans[0].OperationName);
        Assert.Equal("ClientStore.FindClientById", seg2.Spans[1].OperationName);
    }

    [Fact]
    public void BuildSegments_overlapping_timestamps_different_traces_correctly_separates()
    {
        // Edge case: two calls with overlapping time windows but different trace IDs.
        // e.g. parallel calls or clock coarseness causing identical timestamps.
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var traceId1 = "0af7651916cd43dd8448eb211c80319c";
        var traceId2 = "aaaabbbbccccddddeeee111122223333";
        var reqId1 = Guid.NewGuid();
        var reqId2 = Guid.NewGuid();

        // Both spans start at exactly the same time
        var span1 = CreateSpan("call1-op", baseTime.UtcDateTime.AddMilliseconds(10),
            TimeSpan.FromMilliseconds(5), traceId: traceId1);
        var span2 = CreateSpan("call2-op", baseTime.UtcDateTime.AddMilliseconds(10),
            TimeSpan.FromMilliseconds(5), traceId: traceId2);

        var logs = new[]
        {
            MakeRequest(timestamp: baseTime, requestResponseId: reqId1, activityTraceId: traceId1),
            MakeResponse(timestamp: baseTime.AddMilliseconds(50), requestResponseId: reqId1, activityTraceId: traceId1),
            MakeRequest(timestamp: baseTime, requestResponseId: reqId2, activityTraceId: traceId2),
            MakeResponse(timestamp: baseTime.AddMilliseconds(50), requestResponseId: reqId2, activityTraceId: traceId2)
        };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [span1, span2]);

        Assert.Single(result[$"iflow-{reqId1}"].Spans);
        Assert.Equal("call1-op", result[$"iflow-{reqId1}"].Spans[0].OperationName);
        Assert.Single(result[$"iflow-{reqId2}"].Spans);
        Assert.Equal("call2-op", result[$"iflow-{reqId2}"].Spans[0].OperationName);
    }

    [Fact]
    public void BuildSegments_span_starting_slightly_before_segment_start_is_included_with_tolerance()
    {
        // Root span (Kronikol.Request) starts BEFORE the log timestamp
        // because Activity.Start() happens before Timestamp = DateTimeOffset.UtcNow
        var baseTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var traceId = "0af7651916cd43dd8448eb211c80319c";
        var reqId = Guid.NewGuid();

        // Root span starts 5ms before the request log timestamp
        var rootSpan = CreateSpan("Kronikol.Request",
            baseTime.UtcDateTime.AddMilliseconds(-5), TimeSpan.FromMilliseconds(20), traceId: traceId);
        var childSpan = CreateSpan("HttpRequestIn",
            baseTime.UtcDateTime.AddMilliseconds(1), TimeSpan.FromMilliseconds(15), traceId: traceId);

        var logs = new[]
        {
            MakeRequest(timestamp: baseTime, requestResponseId: reqId, activityTraceId: traceId),
            MakeResponse(timestamp: baseTime.AddMilliseconds(25), requestResponseId: reqId, activityTraceId: traceId)
        };

        var result = InternalFlowSegmentBuilder.BuildSegments(logs, [rootSpan, childSpan]);

        // Both spans should be included — root span is the parent of this call's trace
        Assert.Equal(2, result[$"iflow-{reqId}"].Spans.Length);
    }
}
