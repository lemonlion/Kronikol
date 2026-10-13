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
    /// time alone, so one request's popup could show the queries of sixty concurrent requests as its own. Calls of
    /// one test that share a trace under span ids of their own (one trace per test, as a browser test sends or a
    /// proxy tap forwards) are told apart by the tree too: a span that descends from another of those calls' span ids
    /// is that call's, and is not counted as left out, unless that call ran inside this one, as <c>kronikol query
    /// flow</c> nests calls (#170). A span that descends from none of them stays in every call whose time holds it.
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
        // The span ids each test's calls record in each trace, and which calls record each: what tells one test's calls
        // apart in a trace they share.
        var claimedByTest = new Dictionary<(string Trace, string Test), HashSet<string>>();
        var claimers = new Dictionary<(string Trace, string Test, string Span), List<RequestResponseLog>>();
        foreach (var log in timed)
        {
            if (!IsCall(log) || log.ActivityTraceId is not { } trace)
                continue;
            if (!attribution.TestsByTrace.TryGetValue(trace, out var tests))
                attribution.TestsByTrace[trace] = tests = new HashSet<string>(StringComparer.Ordinal);
            tests.Add(log.TestId);
            if (log.Type == RequestResponseType.Request && log.ActivitySpanId is { } claimedSpan)
            {
                if (!claimedByTest.TryGetValue((trace, log.TestId), out var claimed))
                    claimedByTest[(trace, log.TestId)] = claimed = new HashSet<string>(StringComparer.Ordinal);
                claimed.Add(claimedSpan);
                if (!claimers.TryGetValue((trace, log.TestId, claimedSpan), out var by))
                    claimers[(trace, log.TestId, claimedSpan)] = by = [];
                by.Add(log);
            }
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

        // Which of a test's calls ran inside which, by the rule kronikol query flow nests calls by (CallNesting,
        // plans/FLOW_NESTING_PLAN.md R4): made while the other was still waiting for its answer, by the service
        // handling it, or by another party on its Kronikol trace. Two calls from one party are never nested.
        var outerOf = new Dictionary<RequestResponseLog, RequestResponseLog>(ReferenceEqualityComparer.Instance);
        foreach (var testLogs in timed.GroupBy(l => l.TestId))
        {
            // In time order, as the segments are cut; records that tie keep the order they were captured in.
            var entries = testLogs.OrderBy(l => l.Timestamp!.Value).ToArray();
            var answeredAt = new Dictionary<Guid, int>();
            for (var i = 0; i < entries.Length; i++)
                if (entries[i].Type == RequestResponseType.Response)
                    answeredAt.TryAdd(entries[i].RequestResponseId, i);

            var open = new List<RequestResponseLog>();
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry.Type == RequestResponseType.Response)
                {
                    open.RemoveAll(o => o.RequestResponseId == entry.RequestResponseId);
                    continue;
                }
                if (!IsCall(entry))
                    continue;

                for (var k = open.Count - 1; k >= 0; k--)
                {
                    // A record handed in twice is one call: it never runs inside itself.
                    var candidate = open[k];
                    if (ReferenceEquals(candidate, entry))
                        continue;
                    if (string.Equals(candidate.ServiceName, entry.CallerName, StringComparison.Ordinal)
                        || (!string.Equals(candidate.CallerName, entry.CallerName, StringComparison.Ordinal)
                            && entry.TraceId != Guid.Empty && candidate.TraceId == entry.TraceId))
                    {
                        outerOf[entry] = candidate;
                        break;
                    }
                }
                if (answeredAt.TryGetValue(entry.RequestResponseId, out var answer) && answer > i)
                    open.Add(entry);
            }
        }

        bool IsOrRanInside(RequestResponseLog call, RequestResponseLog outer)
        {
            // Each call's outer call opened before it, so the walk ends; the set keeps it ending whatever it is handed.
            var visited = new HashSet<RequestResponseLog>(ReferenceEqualityComparer.Instance);
            for (RequestResponseLog? at = call; at is not null && visited.Add(at); at = outerOf.GetValueOrDefault(at))
                if (ReferenceEquals(at, outer))
                    return true;
            return false;
        }

        // Calls of one test that share a trace under span ids of their own (one trace per test, as a browser test
        // sends, or as a tap forwards the test's trace) are told apart by the span tree too: a span that descends from
        // the span of another of those calls is that call's, unless the call recording it ran inside this one, whose
        // work it then is. A span that descends from none stays wherever its time puts it, and a trace of several
        // tests keeps the rule above.
        var spanById = new Dictionary<string, Dictionary<string, FlowSpan>>(StringComparer.Ordinal);
        var claimedAbove = new Dictionary<FlowSpan, HashSet<string>>(ReferenceEqualityComparer.Instance);

        // The claimed span ids on the span's chain: its own, its parent's, and up through every parent the store
        // holds. A parent the store does not hold (the span a tap forwarded, recorded in another process) is
        // checked too; the walk stops there.
        HashSet<string> ClaimedAbove(string trace, FlowSpan span, HashSet<string> claimed)
        {
            if (claimedAbove.TryGetValue(span, out var known))
                return known;
            if (!spanById.TryGetValue(trace, out var byId))
            {
                byId = new Dictionary<string, FlowSpan>(StringComparer.Ordinal);
                foreach (var held in attribution.SpansByTrace[trace])
                    byId.TryAdd(held.SpanId, held);
                spanById[trace] = byId;
            }

            var found = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string? id = span.SpanId;
            while (id is not null && visited.Add(id))
            {
                if (claimed.Contains(id))
                    found.Add(id);
                id = byId.TryGetValue(id, out var held) ? held.ParentSpanId : null;
            }

            claimedAbove[span] = found;
            return found;
        }

        IReadOnlyCollection<FlowSpan> OwnCallsOnly(RequestResponseLog call, string trace, IReadOnlyCollection<FlowSpan> candidates)
        {
            if (call.ActivitySpanId is null
                || attribution.TestsByTrace[trace].Count > 1
                || !claimedByTest.TryGetValue((trace, call.TestId), out var claimed)
                || claimed.Count < 2)
                return candidates;
            return candidates
                .Where(s => ClaimedAbove(trace, s, claimed) is var above
                    && (above.Count == 0
                        || above.Any(id => claimers[(trace, call.TestId, id)].Any(by => IsOrRanInside(by, call)))))
                .ToArray();
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
                    ? OwnCallsOnly(log, callTrace, ByTraceAndTree(callTrace, log.ActivitySpanId))
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
