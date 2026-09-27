using System.Diagnostics;
using Kronikol.Tracking;

namespace Kronikol.InternalFlow;

/// <summary>
/// Builds segments of internal flow by correlating OTel spans with
/// <see cref="RequestResponseLog"/> boundary timestamps.
/// </summary>
public static class InternalFlowSegmentBuilder
{
    /// <summary>
    /// Builds a dictionary mapping segment keys ("iflow-{RequestResponseId}", one per request record that is a call)
    /// to the spans that occurred during each segment: from the request to its response, or to the next record when
    /// there is no response. Diagram markers and user actions get no segment, since no arrow links them.
    /// </summary>
    public static Dictionary<string, InternalFlowSegment> BuildSegments(
        RequestResponseLog[] logs,
        Activity[] spans)
    {
        var segments = new Dictionary<string, InternalFlowSegment>();

        if (spans.Length == 0)
            return segments;

        var logsByTest = logs
            .Where(l => l.Timestamp.HasValue)
            .GroupBy(l => l.TestId);

        foreach (var testGroup in logsByTest)
        {
            var orderedLogs = testGroup.OrderBy(l => l.Timestamp!.Value).ToArray();
            var testSpans = FilterSpansByTestTraceIds(spans, orderedLogs);

            // Build a lookup from RequestResponseId → response timestamp
            // so each request segment can span to its matching response.
            var responseTimestamps = new Dictionary<Guid, DateTimeOffset>();
            foreach (var log in orderedLogs)
            {
                if (log.Type == RequestResponseType.Response && log.Timestamp.HasValue)
                    responseTimestamps.TryAdd(log.RequestResponseId, log.Timestamp.Value);
            }

            for (var i = 0; i < orderedLogs.Length; i++)
            {
                var log = orderedLogs[i];

                // Only create segments for request entries — response entries
                // represent the instant the response arrives and contain no
                // internal processing spans.
                if (log.Type != RequestResponseType.Request)
                    continue;

                // A marker (a step bar, an assertion note, the Setup/Action boundary) and a user action are not
                // calls, and no arrow links either, so a segment of theirs could never be opened. Both still carry
                // a time since 3.15.1 and stay in the ordering: a call with no response still runs to the next
                // record, whatever it is, so no call's segment changes.
                if (!IsCall(log))
                    continue;

                var segmentStart = log.Timestamp!.Value;

                // Use the matching response's timestamp as the segment end,
                // so the segment covers the full processing window for this
                // request (including all sub-calls and processing in between).
                // Falls back to next log or +5s if no matching response exists.
                DateTimeOffset segmentEnd;
                if (responseTimestamps.TryGetValue(log.RequestResponseId, out var responseTs))
                    segmentEnd = responseTs;
                else if (i + 1 < orderedLogs.Length)
                    segmentEnd = orderedLogs[i + 1].Timestamp!.Value;
                else
                    segmentEnd = segmentStart.AddSeconds(5);

                // When the request log has an ActivityTraceId, filter to only
                // spans belonging to this specific call's trace. This prevents
                // spans from different HTTP calls bleeding into each other's
                // segments when their timestamp windows happen to overlap.
                var candidateSpans = log.ActivityTraceId is not null
                    ? testSpans.Where(s => s.TraceId.ToString() == log.ActivityTraceId)
                    : testSpans.AsEnumerable();

                // Allow a small tolerance before segmentStart to capture root
                // spans (e.g. Kronikol.Request) whose Activity
                // starts before the log timestamp is recorded.
                var toleranceStart = segmentStart.UtcDateTime.AddMilliseconds(-50);

                var segmentSpans = candidateSpans
                    .Where(s => s.StartTimeUtc >= toleranceStart &&
                                s.StartTimeUtc < segmentEnd.UtcDateTime)
                    .OrderBy(s => s.StartTimeUtc)
                    .ToArray();

                var segmentKey = $"iflow-{log.RequestResponseId}";

                segments[segmentKey] = new InternalFlowSegment(
                    log.RequestResponseId,
                    log.Type,
                    log.TestId,
                    segmentStart,
                    segmentEnd,
                    segmentSpans);
            }
        }

        return segments;
    }

    /// <summary>
    /// Builds a single whole-test segment per TestId containing all spans for that test.
    /// Keyed as "iflow-test-{TestId}". Tests with no matching spans are excluded.
    /// </summary>
    public static Dictionary<string, InternalFlowSegment> BuildWholeTestSegments(
        RequestResponseLog[] logs,
        Activity[] spans)
    {
        var segments = new Dictionary<string, InternalFlowSegment>();

        if (spans.Length == 0 || logs.Length == 0)
            return segments;

        // Only a test's calls say which spans are its own. A test that made none (its markers and user actions carry
        // no trace id) would otherwise fall back to every span of the run.
        var logsByTest = logs
            .Where(l => l.Timestamp.HasValue && IsCall(l))
            .GroupBy(l => l.TestId);

        foreach (var testGroup in logsByTest)
        {
            var testLogs = testGroup.ToArray();
            var testSpans = FilterSpansByTestTraceIds(spans, testLogs);

            if (testSpans.Length == 0)
                continue;

            var orderedSpans = testSpans.OrderBy(s => s.StartTimeUtc).ToArray();
            var startTime = orderedSpans.Min(s => s.StartTimeUtc);
            var endTime = orderedSpans.Max(s => s.StartTimeUtc + s.Duration);

            var segmentKey = $"iflow-test-{testGroup.Key}";
            segments[segmentKey] = new InternalFlowSegment(
                Guid.Empty,
                RequestResponseType.Request,
                testGroup.Key,
                new DateTimeOffset(startTime, TimeSpan.Zero),
                new DateTimeOffset(endTime, TimeSpan.Zero),
                orderedSpans);
        }

        return segments;
    }

    /// <summary>
    /// The request boundaries a test's whole-test flame chart draws as dashed lines: each request's label and time,
    /// in time order, a user action's included. A marker draws none: it is not a request, it only carries a time.
    /// The live report and the mergeable data file both take them from here.
    /// </summary>
    internal static (string Label, DateTimeOffset Timestamp)[] BuildWholeTestBoundaries(
        IEnumerable<RequestResponseLog>? logs,
        string testId) =>
        logs?
            .Where(l => l.TestId == testId && l.Type == RequestResponseType.Request && l.Timestamp.HasValue && !l.IsDiagramMarker)
            .OrderBy(l => l.Timestamp!.Value)
            .Select(l => ($"{l.Method.Value}: {l.Uri.PathAndQuery}", l.Timestamp!.Value))
            .ToArray() ?? [];

    /// <summary>
    /// False for the records in the log stream that are not calls: a diagram marker (<see cref="RequestResponseLog.IsDiagramMarker"/>)
    /// and a user action (<see cref="RequestResponseLog.IsUserAction"/>). Neither carries a trace id or a link.
    /// </summary>
    private static bool IsCall(RequestResponseLog log) => !log.IsDiagramMarker && !log.IsUserAction;

    private static Activity[] FilterSpansByTestTraceIds(Activity[] allSpans, RequestResponseLog[] testLogs)
    {
        var testActivityTraceIds = testLogs
            .Where(l => l.ActivityTraceId is not null)
            .Select(l => l.ActivityTraceId!)
            .ToHashSet();

        if (testActivityTraceIds.Count == 0)
            return allSpans;

        return allSpans
            .Where(s => testActivityTraceIds.Contains(s.TraceId.ToString()))
            .ToArray();
    }
}
