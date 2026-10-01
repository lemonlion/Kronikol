using System.Diagnostics;
using Kronikol.Tracking;

namespace Kronikol.InternalFlow;

/// <summary>
/// Represents a segment of internal flow between two consecutive HTTP boundaries
/// (e.g. between a request arriving and the SUT calling a dependency).
/// </summary>
public record InternalFlowSegment(
    Guid RequestResponseId,
    RequestResponseType BoundaryType,
    string TestId,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    Activity[] Spans)
{
    /// <summary>
    /// The segment's in-process spans. Empty for spans an ingest was handed (<c>IngestRequest.Spans</c>), which are
    /// <see cref="FlowSpans"/> only: an <see cref="Activity"/> cannot stand for a span another process recorded.
    /// Setting them (a <c>with</c> expression) sets <see cref="FlowSpans"/> to them too, so a segment whose activities a
    /// caller replaced draws the ones it was given.
    /// </summary>
    public Activity[] Spans
    {
        get => _spans;
        init
        {
            _spans = value;
            _flowSpans = null;
        }
    }

    private Activity[] _spans = Spans;

    /// <summary>
    /// Every span of the segment, as internal flow reads it, whatever recorded it: what the popup, the flame chart and the
    /// whole-test flow are drawn from. A segment built from <see cref="Spans"/> alone has their spans here, in order.
    /// </summary>
    public FlowSpan[] FlowSpans
    {
        get => _flowSpans ??= Spans.Select(FlowSpan.From).ToArray();
        init => _flowSpans = value;
    }

    private FlowSpan[]? _flowSpans;

    /// <summary>
    /// Spans that started during this call and that a call of another test would take too, so neither holds them:
    /// nothing in them says whose they are (#87, <c>plans/SPAN_ATTRIBUTION_PLAN.md</c>). The popup says how many.
    /// </summary>
    internal int SpansLeftOut { get; init; }

    /// <summary>A segment of <paramref name="spans"/>, handing back the in-process ones as <see cref="Spans"/>.</summary>
    internal static InternalFlowSegment Of(Guid requestResponseId, RequestResponseType boundaryType, string testId,
        DateTimeOffset? startTime, DateTimeOffset? endTime, FlowSpan[] spans, int spansLeftOut = 0) =>
        new(requestResponseId, boundaryType, testId, startTime, endTime, ActivitiesOf(spans))
        {
            FlowSpans = spans,
            SpansLeftOut = spansLeftOut,
        };

    private static Activity[] ActivitiesOf(FlowSpan[] spans)
    {
        var activities = new List<Activity>(spans.Length);
        foreach (var span in spans)
        {
            if (span.Activity is { } activity)
                activities.Add(activity);
        }

        return activities.ToArray();
    }
}
