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
    /// <remarks>
    /// A span is a call's when its trace, its place in the span tree and its start time say so, and a span that calls
    /// of two tests would both take goes to neither; the segment counts it in
    /// <see cref="InternalFlowSegment.SpansLeftOut"/> (#87, <c>plans/SPAN_ATTRIBUTION_PLAN.md</c>). Before, a call
    /// with no trace id took every span of the run in its time, and calls sharing an ambient trace were told apart by
    /// time alone, so one request's popup could show the queries of sixty concurrent requests as its own.
    /// </remarks>
    public static Dictionary<string, InternalFlowSegment> BuildSegments(
        RequestResponseLog[] logs,
        Activity[] spans) =>
        SegmentsOf(logs, spans.Select(FlowSpan.From).ToArray());

    /// <summary>
    /// <see cref="BuildSegments"/> over <see cref="FlowSpan"/>s, whatever recorded them: the report's own path, which
    /// reads an ingest's spans (<c>IngestRequest.Spans</c>) as it reads this process's. Each segment hands back the
    /// spans that are activities as <see cref="InternalFlowSegment.Spans"/>.
    /// </summary>
    internal static Dictionary<string, InternalFlowSegment> SegmentsOf(
        RequestResponseLog[] logs,
        FlowSpan[] spans) =>
        Attribute(logs, spans).Segments;

    /// <summary>
    /// Builds a single whole-test segment per TestId containing all spans for that test.
    /// Keyed as "iflow-test-{TestId}". Tests with no matching spans are excluded.
    /// </summary>
    /// <remarks>
    /// A test's spans are every span of each trace only its calls claim, and, of a trace other tests' calls claim
    /// too or for a test whose calls claim none, the spans its calls kept (<see cref="BuildSegments"/>). Before, a
    /// test whose calls recorded no trace id took every span of the run.
    /// </remarks>
    public static Dictionary<string, InternalFlowSegment> BuildWholeTestSegments(
        RequestResponseLog[] logs,
        Activity[] spans) =>
        WholeTestSegmentsOf(logs, spans.Select(FlowSpan.From).ToArray());

    /// <summary><see cref="BuildWholeTestSegments"/> over <see cref="FlowSpan"/>s, whatever recorded them.</summary>
    internal static Dictionary<string, InternalFlowSegment> WholeTestSegmentsOf(
        RequestResponseLog[] logs,
        FlowSpan[] spans)
    {
        var segments = new Dictionary<string, InternalFlowSegment>();

        if (spans.Length == 0 || logs.Length == 0)
            return segments;

        var attribution = Attribute(logs, spans);

        // Only a test's calls say which spans are its own. A test that made none (its markers and user actions carry
        // no trace id) gets no whole-test flow.
        var logsByTest = logs
            .Where(l => l.Timestamp.HasValue && IsCall(l))
            .GroupBy(l => l.TestId);

        foreach (var testGroup in logsByTest)
        {
            var testSpans = new HashSet<FlowSpan>(ReferenceEqualityComparer.Instance);
            foreach (var trace in testGroup.Where(l => l.ActivityTraceId is not null).Select(l => l.ActivityTraceId!).Distinct(StringComparer.Ordinal))
            {
                if (attribution.TestsByTrace[trace].Count == 1 && attribution.SpansByTrace.TryGetValue(trace, out var ofTrace))
                    testSpans.UnionWith(ofTrace);
            }
            if (attribution.KeptByTest.TryGetValue(testGroup.Key, out var kept))
                testSpans.UnionWith(kept);

            if (testSpans.Count == 0)
                continue;

            var orderedSpans = testSpans.OrderBy(s => s.StartTimeUtc).ToArray();
            var startTime = orderedSpans.Min(s => s.StartTimeUtc);
            var endTime = orderedSpans.Max(s => s.StartTimeUtc + s.Duration);

            var segmentKey = $"iflow-test-{testGroup.Key}";
            segments[segmentKey] = InternalFlowSegment.Of(
                Guid.Empty,
                RequestResponseType.Request,
                testGroup.Key,
                new DateTimeOffset(startTime, TimeSpan.Zero),
                new DateTimeOffset(endTime, TimeSpan.Zero),
                orderedSpans);
        }

        return segments;
    }

    /// <summary>Which spans each call keeps, worked out once for both builders.</summary>
    private sealed class Attribution
    {
        public Dictionary<string, InternalFlowSegment> Segments { get; } = new();

        /// <summary>Per trace id, the tests whose calls record it.</summary>
        public Dictionary<string, HashSet<string>> TestsByTrace { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, FlowSpan[]> SpansByTrace { get; init; } = new(StringComparer.Ordinal);

        /// <summary>Per test, every span one of its calls kept.</summary>
        public Dictionary<string, HashSet<FlowSpan>> KeptByTest { get; } = new(StringComparer.Ordinal);
    }

    private readonly record struct Selection(string Key, RequestResponseLog Log, DateTimeOffset Start, DateTimeOffset End, List<FlowSpan> Spans);

    /// <remarks>
    /// Spans are told apart by reference, as activities always were: a span handed in twice is one span only when it
    /// is one object, so a caller de-duplicates first (an ingest does, by trace and span id).
    /// </remarks>
    private static Attribution Attribute(RequestResponseLog[] logs, FlowSpan[] spans)
    {
        var attribution = new Attribution
        {
            SpansByTrace = spans.GroupBy(s => s.TraceId).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal)
        };

        if (spans.Length == 0)
            return attribution;

        var timed = logs.Where(l => l.Timestamp.HasValue).ToArray();
        foreach (var log in timed)
        {
            if (!IsCall(log) || log.ActivityTraceId is not { } trace)
                continue;
            if (!attribution.TestsByTrace.TryGetValue(trace, out var tests))
                attribution.TestsByTrace[trace] = tests = new HashSet<string>(StringComparer.Ordinal);
            tests.Add(log.TestId);
        }

        // Spans of a trace no call claims: all a call with no trace id can take, when its test's calls claim none.
        var unclaimed = spans.Where(s => !attribution.TestsByTrace.ContainsKey(s.TraceId)).ToArray();
        var children = spans
            .GroupBy(s => (Trace: s.TraceId, Parent: s.ParentSpanId))
            .ToDictionary(g => g.Key, g => g.ToArray());
        var subtrees = new Dictionary<(string Trace, string Anchor), List<FlowSpan>>();

        // What a call recording this trace and span may take, at any time: the trace's spans, or, of a trace other
        // tests' calls record too (an ambient Activity around several tests), the call's span and what descends from
        // it, which tells the tests apart when each has a span of its own.
        IReadOnlyCollection<FlowSpan> ByTraceAndTree(string trace, string? spanId)
        {
            if (attribution.TestsByTrace[trace].Count > 1 && spanId is not null)
            {
                if (!subtrees.TryGetValue((trace, spanId), out var subtree))
                    subtrees[(trace, spanId)] = subtree = Subtree(trace, spanId, attribution.SpansByTrace, children);
                if (subtree.Count > 0)
                    return subtree;
            }
            return attribution.SpansByTrace.TryGetValue(trace, out var ofTrace) ? ofTrace : [];
        }

        var selections = new List<Selection>();
        foreach (var testGroup in timed.GroupBy(l => l.TestId))
        {
            var orderedLogs = testGroup.OrderBy(l => l.Timestamp!.Value).ToArray();
            var testCalls = orderedLogs
                .Where(l => IsCall(l) && l.ActivityTraceId is not null)
                .Select(l => (Trace: l.ActivityTraceId!, Span: l.ActivitySpanId))
                .Distinct()
                .ToArray();
            // What the test's traced calls may take: all a call of the test with no trace id may take (a database
            // tracker records none), so that it never takes what the span tree gives to another test.
            var testSpans = testCalls
                .SelectMany(call => ByTraceAndTree(call.Trace, call.Span))
                .Distinct<FlowSpan>(ReferenceEqualityComparer.Instance)
                .ToArray();

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

                // The call's own trace and span tree; with no trace id, what its test's traced calls may take; with
                // none of those either, the spans no call claims. Never every span of the run: that pooled the spans
                // of every concurrent test (#87).
                IEnumerable<FlowSpan> candidateSpans = log.ActivityTraceId is { } callTrace
                    ? ByTraceAndTree(callTrace, log.ActivitySpanId)
                    : testCalls.Length > 0 ? testSpans : unclaimed;

                // Allow a small tolerance before segmentStart to capture root
                // spans (e.g. Kronikol.Request) whose Activity
                // starts before the log timestamp is recorded.
                var toleranceStart = segmentStart.UtcDateTime.AddMilliseconds(-50);

                var segmentSpans = candidateSpans
                    .Where(s => s.StartTimeUtc >= toleranceStart &&
                                s.StartTimeUtc < segmentEnd.UtcDateTime)
                    .OrderBy(s => s.StartTimeUtc)
                    .ToList();

                selections.Add(new Selection($"iflow-{log.RequestResponseId}", log, segmentStart, segmentEnd, segmentSpans));
            }
        }

        // A span the calls of two tests both took belongs to one of them or to neither, and nothing says which.
        // Calls of one test may share spans: a call nested in another shows inside both.
        var testsBySpan = new Dictionary<FlowSpan, HashSet<string>>(ReferenceEqualityComparer.Instance);
        foreach (var selection in selections)
        {
            foreach (var span in selection.Spans)
            {
                if (!testsBySpan.TryGetValue(span, out var tests))
                    testsBySpan[span] = tests = new HashSet<string>(StringComparer.Ordinal);
                tests.Add(selection.Log.TestId);
            }
        }

        foreach (var selection in selections)
        {
            var kept = selection.Spans.Where(s => testsBySpan[s].Count == 1).ToArray();
            attribution.Segments[selection.Key] = InternalFlowSegment.Of(
                selection.Log.RequestResponseId,
                selection.Log.Type,
                selection.Log.TestId,
                selection.Start,
                selection.End,
                kept,
                spansLeftOut: selection.Spans.Count - kept.Length);

            if (!attribution.KeptByTest.TryGetValue(selection.Log.TestId, out var testKept))
                attribution.KeptByTest[selection.Log.TestId] = testKept = new HashSet<FlowSpan>(ReferenceEqualityComparer.Instance);
            testKept.UnionWith(kept);
        }

        return attribution;
    }

    /// <summary>
    /// The span <paramref name="anchor"/> of <paramref name="trace"/>, when the store holds it, and every span that
    /// descends from it by <see cref="FlowSpan.ParentSpanId"/>.
    /// </summary>
    private static List<FlowSpan> Subtree(
        string trace,
        string anchor,
        Dictionary<string, FlowSpan[]> spansByTrace,
        Dictionary<(string Trace, string? Parent), FlowSpan[]> children)
    {
        var found = new List<FlowSpan>();
        if (spansByTrace.TryGetValue(trace, out var ofTrace))
            found.AddRange(ofTrace.Where(s => s.SpanId == anchor));

        var seen = new HashSet<string>(StringComparer.Ordinal) { anchor };
        var pending = new Queue<string>();
        pending.Enqueue(anchor);
        while (pending.Count > 0)
        {
            if (!children.TryGetValue((trace, pending.Dequeue()), out var kids))
                continue;
            foreach (var kid in kids)
            {
                var id = kid.SpanId;
                if (seen.Add(id))
                {
                    found.Add(kid);
                    pending.Enqueue(id);
                }
            }
        }

        return found;
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
}
