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
    /// is that call's, and is not counted as left out, unless that call ran inside this one: made while this one was
    /// open, under its span by the tree, or, where the tree says nothing, as <c>kronikol query flow</c> nests calls
    /// (#170). A span that descends from none of them stays in every call whose time holds it.
    /// <para>A call's own span is the one its request names, unless its response names a span the store holds under
    /// that one: <see cref="Tracking.TestTrackingMessageHandler"/> logs a call under the span current when it sees the
    /// call, a host's request span inside a host, and names on the response the span its transport then started for
    /// the call. A span that began in the 50 ms before a call's request is the call's, but for one that ran across
    /// another call of its test made beside it, and one that began in the time of another call of its test, the call it
    /// was made in or one before it; the call's own span, when no other call of its test claims it or was made under it
    /// beside this one, and the spans under it are the call's whatever their times, unless the span's parent is another
    /// call's own span: that is the first span of the other call's handling, which a tracker records for a call made while
    /// serving it. A client library's span that no call claims, begun a moment before a call with no trace id is logged
    /// and still running then, is that call's, with the spans under it: each goes to the first such call logged after it
    /// began, in the order the spans began. Before, every call a host made under one request, at once or one after
    /// another, and every call with no trace id made inside a request, showed that request's whole flow.</para>
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

    /// <summary>A call's time, as its segment is cut: from its request to its response, or to the next record.</summary>
    private readonly record struct CallWindow(RequestResponseLog Log, DateTimeOffset Start, DateTimeOffset End);

    /// <summary>How long before a call's request a span may begin and still be the call's (LeadOf says which are).</summary>
    private const int LeadToleranceMs = 50;

    /// <summary>
    /// Whether <paramref name="span"/> ran across the whole of another call of the test made beside <paramref name="call"/>,
    /// one that did not run inside it (<paramref name="inside"/>): the span of what made both, not of either.
    /// <paramref name="windows"/> are in time order.
    /// </summary>
    private static bool HoldsACallBeside(FlowSpan span, CallWindow call, List<CallWindow> windows, List<RequestResponseLog>? inside)
    {
        var spanEnd = span.StartTimeUtc + span.Duration;
        for (var i = FirstStartingAtOrAfter(windows, span.StartTimeUtc); i < windows.Count && windows[i].Start.UtcDateTime < spanEnd; i++)
        {
            var other = windows[i];
            if (other.Log.RequestResponseId == call.Log.RequestResponseId
                || (inside is not null && inside.Contains(other.Log, ReferenceEqualityComparer.Instance)))
                continue;
            if (other.End.UtcDateTime <= spanEnd)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether <paramref name="span"/>, which began before <paramref name="call"/>, began in the time of another call of the
    /// test: the call this one was made in, or one before it. It is that call's work. <paramref name="windows"/> are in
    /// time order.
    /// </summary>
    private static bool BeganInAnotherCall(FlowSpan span, CallWindow call, List<CallWindow> windows)
    {
        foreach (var other in windows)
        {
            if (other.Start.UtcDateTime > span.StartTimeUtc)
                break;
            if (other.Log.RequestResponseId != call.Log.RequestResponseId && span.StartTimeUtc < other.End.UtcDateTime)
                return true;
        }
        return false;
    }

    /// <summary>The index of the first of <paramref name="windows"/>, in time order, that starts at or after <paramref name="at"/>.</summary>
    private static int FirstStartingAtOrAfter(List<CallWindow> windows, DateTime at)
    {
        int low = 0, high = windows.Count;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (windows[mid].Start.UtcDateTime < at)
                low = mid + 1;
            else
                high = mid;
        }
        return low;
    }

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

        // Per trace, its spans by span id, built when first needed.
        var spanById = new Dictionary<string, Dictionary<string, FlowSpan>>(StringComparer.Ordinal);
        Dictionary<string, FlowSpan> SpansById(string trace)
        {
            if (!spanById.TryGetValue(trace, out var byId))
            {
                byId = new Dictionary<string, FlowSpan>(StringComparer.Ordinal);
                if (attribution.SpansByTrace.TryGetValue(trace, out var ofTrace))
                    foreach (var held in ofTrace)
                        byId.TryAdd(held.SpanId, held);
                spanById[trace] = byId;
            }
            return byId;
        }

        // Each call's own span. TestTrackingMessageHandler logs a call under the span current when it sees the call, which
        // inside a host is the host's request span, and the span the transport starts for the call begins later, so the
        // call's response names that one. Every call a host made under one request claimed that request's span, and the
        // tree could not tell them apart: each popup showed the host's whole request. The span a response names is the
        // call's own when the store holds it under the span its request names; else the request's span is.
        var ownSpans = OwnSpans(timed, SpansById);
        string? OwnSpan(RequestResponseLog log) => ownSpans.TryGetValue(log, out var own) ? own : log.ActivitySpanId;

        // The span ids each test's calls record in each trace: what tells one test's calls apart in a trace they share.
        var claimedByTest = new Dictionary<(string Trace, string Test), HashSet<string>>();
        // The calls that claim each of a test's own spans in a trace.
        var callsByOwnSpan = new Dictionary<(string Trace, string Test, string Span), HashSet<Guid>>();
        foreach (var log in timed)
        {
            if (!IsCall(log) || log.ActivityTraceId is not { } trace)
                continue;
            if (!attribution.TestsByTrace.TryGetValue(trace, out var tests))
                attribution.TestsByTrace[trace] = tests = new HashSet<string>(StringComparer.Ordinal);
            tests.Add(log.TestId);
            if (log.Type != RequestResponseType.Request)
                continue;
            if (OwnSpan(log) is { } claimedSpan)
            {
                if (!claimedByTest.TryGetValue((trace, log.TestId), out var claimed))
                    claimedByTest[(trace, log.TestId)] = claimed = new HashSet<string>(StringComparer.Ordinal);
                claimed.Add(claimedSpan);
                if (!callsByOwnSpan.TryGetValue((trace, log.TestId, claimedSpan), out var calls))
                    callsByOwnSpan[(trace, log.TestId, claimedSpan)] = calls = [];
                calls.Add(log.RequestResponseId);
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

        // The span ids above a call's span: its parent's, and up through every parent the store holds, the first it does
        // not hold included. None when the store does not hold the call's own span.
        HashSet<string> IdsAbove(string trace, string spanId)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var byId = SpansById(trace);
            var id = byId.TryGetValue(spanId, out var own) ? own.ParentSpanId : null;
            while (id is not null && ids.Add(id) && byId.TryGetValue(id, out var held))
                id = held.ParentSpanId;
            ids.Remove(spanId);
            return ids;
        }

        // Which of a test's calls ran inside which, made while the other was still waiting for its answer. The span tree
        // says first: an open call whose span id is above this call's span ran it. Where the tree says nothing, the rule
        // kronikol query flow nests calls by (CallNesting, plans/FLOW_NESTING_PLAN.md R4): by the service handling it, or
        // by another party on its Kronikol trace, never two calls from one party. That rule alone put the queries a
        // service made for two of an app's parallel calls inside whichever call opened last (#170's own shape).
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

                // A record handed in twice is one call: it never runs inside itself.
                var above = entry.ActivityTraceId is { } entryTrace && OwnSpan(entry) is { } entrySpan
                    ? IdsAbove(entryTrace, entrySpan)
                    : null;
                RequestResponseLog? outer = null;
                for (var k = open.Count - 1; k >= 0 && outer is null && above is { Count: > 0 }; k--)
                {
                    var candidate = open[k];
                    if (!ReferenceEquals(candidate, entry) && OwnSpan(candidate) is { } candidateSpan
                        && string.Equals(candidate.ActivityTraceId, entry.ActivityTraceId, StringComparison.Ordinal)
                        && above.Contains(candidateSpan))
                        outer = candidate;
                }
                for (var k = open.Count - 1; k >= 0 && outer is null; k--)
                {
                    var candidate = open[k];
                    if (ReferenceEquals(candidate, entry))
                        continue;
                    if (string.Equals(candidate.ServiceName, entry.CallerName, StringComparison.Ordinal)
                        || (!string.Equals(candidate.CallerName, entry.CallerName, StringComparison.Ordinal)
                            && entry.TraceId != Guid.Empty && candidate.TraceId == entry.TraceId))
                        outer = candidate;
                }
                if (outer is not null)
                    outerOf[entry] = outer;
                if (answeredAt.TryGetValue(entry.RequestResponseId, out var answer) && answer > i)
                    open.Add(entry);
            }
        }

        // The calls that ran inside each call, at any depth, from one walk up each call's outer calls: asking it again for
        // every span of a trace took 22 s for a test of 300 calls under one span (4.18.3).
        var innerOf = new Dictionary<RequestResponseLog, List<RequestResponseLog>>(ReferenceEqualityComparer.Instance);
        foreach (var (inner, outer) in outerOf)
        {
            // Each call's outer call opened before it, so the walk ends; the set keeps it ending whatever it is handed.
            var visited = new HashSet<RequestResponseLog>(ReferenceEqualityComparer.Instance) { inner };
            for (RequestResponseLog? at = outer; at is not null && visited.Add(at); at = outerOf.GetValueOrDefault(at))
            {
                if (!innerOf.TryGetValue(at, out var inside))
                    innerOf[at] = inside = [];
                inside.Add(inner);
            }
        }

        // Calls of one test that share a trace under span ids of their own (one trace per test, as a browser test
        // sends, or as a tap forwards the test's trace) are told apart by the span tree too: a span that descends from
        // the span of another of those calls is that call's, unless the call recording it ran inside this one, whose
        // work it then is. A span that descends from none stays wherever its time puts it, and a trace of several
        // tests keeps the rule above.
        var claimedAbove = new Dictionary<FlowSpan, HashSet<string>>(ReferenceEqualityComparer.Instance);

        // The claimed span ids on the span's chain: its own, its parent's, and up through every parent the store
        // holds. A parent the store does not hold (the span a tap forwarded, recorded in another process) is
        // checked too; the walk stops there.
        HashSet<string> ClaimedAbove(string trace, FlowSpan span, HashSet<string> claimed)
        {
            if (claimedAbove.TryGetValue(span, out var known))
                return known;
            var byId = SpansById(trace);

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

        IEnumerable<FlowSpan> OwnCallsOnly(RequestResponseLog call, string trace, IEnumerable<FlowSpan> candidates)
        {
            if (OwnSpan(call) is not { } callSpan
                || attribution.TestsByTrace[trace].Count > 1
                || !claimedByTest.TryGetValue((trace, call.TestId), out var claimed)
                || claimed.Count < 2)
                return candidates;

            // The span ids this call and the calls that ran inside it record in this trace: a span under one of them is
            // this call's work.
            var own = new HashSet<string>(StringComparer.Ordinal) { callSpan };
            foreach (var inner in innerOf.GetValueOrDefault(call) ?? [])
                if (OwnSpan(inner) is { } innerSpan && string.Equals(inner.ActivityTraceId, trace, StringComparison.Ordinal))
                    own.Add(innerSpan);

            return candidates.Where(s => ClaimedAbove(trace, s, claimed) is var above && (above.Count == 0 || own.Overlaps(above)));
        }

        // A client library's span begun a moment before the call it makes is logged, by a tracker inside the library that
        // logs the call once the library has started its span: Cosmos DB's SDK starts its operation, then sends its request
        // through Kronikol's handler. A call whose tracker records no trace id has no span of its own to vouch for that span,
        // which began in the time of the request the call was made in, so only the request's popup showed it (4.26.5 and
        // 4.26.6). Each such span, a client's or a producer's that no call claims, goes to the first call with no trace id
        // logged after it began and while it ran, in the order the spans began, unless it began in the time of a call that
        // one was not made in; the spans under it go with it. One a server or consumer we see answered is not a library's
        // but a call's to that service, whose work the server's is: a gRPC call whose tracker records the span it was made
        // under, and the calls the service made while handling it, which ran inside it.
        Dictionary<FlowSpan, RequestResponseLog> ClientSpanOwners(List<CallWindow> testWindows, IReadOnlyCollection<FlowSpan> candidates,
            string testId)
        {
            var owners = new Dictionary<FlowSpan, RequestResponseLog>(ReferenceEqualityComparer.Instance);
            var untraced = testWindows.Where(w => w.Log.ActivityTraceId is null).ToList();
            if (untraced.Count == 0)
                return owners;

            // The spans a server or consumer span runs under.
            var answered = new HashSet<(string Trace, string Span)>();
            foreach (var s in candidates)
            {
                if (s.Activity is not { Kind: ActivityKind.Server or ActivityKind.Consumer })
                    continue;
                var byId = SpansById(s.TraceId);
                var visited = new HashSet<string>(StringComparer.Ordinal);
                for (var id = s.ParentSpanId; id is not null && visited.Add(id) && answered.Add((s.TraceId, id));
                     id = byId.TryGetValue(id, out var up) ? up.ParentSpanId : null)
                {
                }
            }

            var clientSpans = candidates
                .Where(s => s.Activity is { Kind: ActivityKind.Client or ActivityKind.Producer }
                            && !(claimedByTest.TryGetValue((s.TraceId, testId), out var claimed) && claimed.Contains(s.SpanId))
                            && !answered.Contains((s.TraceId, s.SpanId)))
                .ToList();
            if (clientSpans.Count == 0)
                return owners;

            // A client span under another, as a library's request under its operation, goes with the one above it.
            var clientIds = clientSpans.Select(s => (s.TraceId, s.SpanId)).ToHashSet();
            bool UnderAnotherClientSpan(FlowSpan s)
            {
                var byId = SpansById(s.TraceId);
                var visited = new HashSet<string>(StringComparer.Ordinal);
                for (var id = s.ParentSpanId; id is not null && visited.Add(id); id = byId.TryGetValue(id, out var up) ? up.ParentSpanId : null)
                    if (clientIds.Contains((s.TraceId, id)))
                        return true;
                return false;
            }

            bool MadeIn(RequestResponseLog inner, RequestResponseLog outer)
            {
                var visited = new HashSet<RequestResponseLog>(ReferenceEqualityComparer.Instance);
                for (var at = outerOf.GetValueOrDefault(inner); at is not null && visited.Add(at); at = outerOf.GetValueOrDefault(at))
                    if (at.RequestResponseId == outer.RequestResponseId)
                        return true;
                return false;
            }

            bool BeganInACallBeside(FlowSpan s, CallWindow call) =>
                testWindows.Any(other => other.Log.RequestResponseId != call.Log.RequestResponseId
                                         && other.Start.UtcDateTime <= s.StartTimeUtc && s.StartTimeUtc < other.End.UtcDateTime
                                         && !MadeIn(call.Log, other.Log));

            var taken = new HashSet<Guid>();
            foreach (var span in clientSpans.Where(s => !UnderAnotherClientSpan(s)).OrderBy(s => s.StartTimeUtc))
            {
                var spanEnd = span.StartTimeUtc + span.Duration;
                foreach (var window in untraced)
                {
                    var at = window.Start.UtcDateTime;
                    if (at <= span.StartTimeUtc || taken.Contains(window.Log.RequestResponseId))
                        continue;
                    if (at > span.StartTimeUtc.AddMilliseconds(LeadToleranceMs))
                        break;
                    if (spanEnd <= at || BeganInACallBeside(span, window))
                        continue;
                    owners[span] = window.Log;
                    taken.Add(window.Log.RequestResponseId);
                    break;
                }
            }
            return owners;
        }

        // Of the spans that began in the 50 ms before a call's request, the ones that are the call's: all but a span that ran
        // across another call of its test made beside it, which is the span of what made both, and a span that began in the
        // time of another call of its test, the call it was made in or one before it, which is that call's work. The tree
        // vouches for the call's own span, when no other call of its test claims it or was made under it beside this one,
        // and for the spans under it, but never for the first span of another call's handling, whose parent is that
        // call's own span: a tracker logs a call under the span current when it is made, which may be the request span of
        // the call it was made in, or the method that made it and other calls. Before, a call took every one of them, so
        // each call a host made at once, or one after another, showed the host's request span and the calls' before it,
        // and a call with no trace id the request it was made in: every popup the request's whole flow. A client span
        // ClientSpanOwners gave to a call, and the spans under it, are that call's alone.
        List<FlowSpan> LeadOf(CallWindow call, List<CallWindow> testWindows, List<FlowSpan> lead,
            Dictionary<(string Trace, string Span), List<RequestResponseLog>> callsUnder, Dictionary<FlowSpan, RequestResponseLog> clientOwner)
        {
            if (lead.Count == 0)
                return lead;

            var log = call.Log;
            var callSpan = OwnSpan(log);
            var (own, ownTrace) = log.ActivityTraceId is { } trace && callSpan is { } span
                && callsByOwnSpan.GetValueOrDefault((trace, log.TestId, span)) is not { Count: > 1 }
                ? (span, trace)
                : (null, null);
            var inside = innerOf.GetValueOrDefault(log);
            // A span another call beside this one was made under is the span of what made both, such as the method that
            // published an event and then called a service (OrderService.CreateOrder): not this call's own.
            if (own is not null && callsUnder.TryGetValue((ownTrace!, own), out var under)
                && under.Any(o => o.RequestResponseId != log.RequestResponseId
                                  && (inside is null || !inside.Contains(o, ReferenceEqualityComparer.Instance))))
                (own, ownTrace) = (null, null);

            bool HandlesAnotherCall(FlowSpan s) =>
                s.ParentSpanId is { } parent && !string.Equals(parent, callSpan, StringComparison.Ordinal)
                && claimedByTest.TryGetValue((s.TraceId, log.TestId), out var claimed) && claimed.Contains(parent);

            RequestResponseLog? ClientOwnerOf(FlowSpan s)
            {
                if (clientOwner.Count == 0)
                    return null;
                var byId = SpansById(s.TraceId);
                var visited = new HashSet<string>(StringComparer.Ordinal);
                for (FlowSpan? at = s; at is not null && visited.Add(at.SpanId);
                     at = at.ParentSpanId is { } parent && byId.TryGetValue(parent, out var up) ? up : null)
                    if (clientOwner.TryGetValue(at, out var owner))
                        return owner;
                return null;
            }

            return lead.Where(s => ClientOwnerOf(s) is { } owner
                    ? owner.RequestResponseId == log.RequestResponseId
                    : !HandlesAnotherCall(s)
                      && ((own is not null && (string.Equals(s.SpanId, own, StringComparison.Ordinal) || DescendsFrom(SpansById(ownTrace!), s.SpanId, own)))
                          || !(HoldsACallBeside(s, call, testWindows, inside) || BeganInAnotherCall(s, call, testWindows))))
                .ToList();
        }

        var selections = new List<Selection>();
        foreach (var testGroup in timed.GroupBy(l => l.TestId))
        {
            var orderedLogs = testGroup.OrderBy(l => l.Timestamp!.Value).ToArray();
            var testCalls = orderedLogs
                .Where(l => IsCall(l) && l.ActivityTraceId is not null)
                .Select(l => (Trace: l.ActivityTraceId!, Span: OwnSpan(l)))
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

            var windows = new List<CallWindow>();
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

                windows.Add(new CallWindow(log, segmentStart, segmentEnd));
            }

            // Per span, the calls of the test whose own span is under it.
            var callsUnder = new Dictionary<(string Trace, string Span), List<RequestResponseLog>>();
            foreach (var window in windows)
            {
                if (window.Log.ActivityTraceId is not { } windowTrace || OwnSpan(window.Log) is not { } windowSpan)
                    continue;
                var byId = SpansById(windowTrace);
                var visited = new HashSet<string>(StringComparer.Ordinal);
                for (var id = byId.TryGetValue(windowSpan, out var heldSpan) ? heldSpan.ParentSpanId : null;
                     id is not null && visited.Add(id);
                     id = byId.TryGetValue(id, out var aboveSpan) ? aboveSpan.ParentSpanId : null)
                {
                    if (!callsUnder.TryGetValue((windowTrace, id), out var madeUnder))
                        callsUnder[(windowTrace, id)] = madeUnder = [];
                    madeUnder.Add(window.Log);
                }
            }

            var clientOwner = ClientSpanOwners(windows, testCalls.Length > 0 ? testSpans : unclaimed, testGroup.Key);

            foreach (var window in windows)
            {
                var (log, segmentStart, segmentEnd) = window;

                // The call's own trace and span tree; with no trace id, what its test's traced calls may take; with
                // none of those either, the spans no call claims. Never every span of the run: that pooled the spans
                // of every concurrent test (#87).
                IEnumerable<FlowSpan> candidateSpans = log.ActivityTraceId is { } callTrace
                    ? ByTraceAndTree(callTrace, OwnSpan(log))
                    : testCalls.Length > 0 ? testSpans : unclaimed;

                // A span may begin a moment before its call is logged: a client library starts its operation's span and
                // then the tracker inside it logs the call (Cosmos DB's SDK, a database driver's command), and an ingested
                // capture logs a call to the millisecond inside a request hook its request's root span began before. So the
                // window opens LeadToleranceMs early, and LeadOf says which of the spans begun then are the call's. It was
                // added in 2.0.173-beta for a root span TestTrackingMessageHandler started before logging a call, which it
                // stopped creating in 2.21.2.
                var start = segmentStart.UtcDateTime;
                var toleranceStart = start.AddMilliseconds(-LeadToleranceMs);
                var lead = new List<FlowSpan>();
                var inWindow = new List<FlowSpan>();
                foreach (var span in candidateSpans)
                {
                    if (span.StartTimeUtc >= segmentEnd.UtcDateTime || span.StartTimeUtc < toleranceStart)
                        continue;
                    (span.StartTimeUtc >= start ? inWindow : lead).Add(span);
                }

                IEnumerable<FlowSpan> inTime = inWindow.Concat(LeadOf(window, windows, lead, callsUnder, clientOwner));
                // Of the spans in its time, the tree's, when calls of its test share its trace (#170). After the time
                // filter, so a call pays for the spans of its own time, not for every span of a trace a whole test shares.
                if (log.ActivityTraceId is { } ownTrace)
                    inTime = OwnCallsOnly(log, ownTrace, inTime);
                var segmentSpans = inTime
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
    /// The calls whose response names a span of their own: one the store holds under the span the call's request names,
    /// in the same trace. The HTTP handler names the span its transport started for the call there, which the call's
    /// request, logged before the transport ran, could not.
    /// </summary>
    private static Dictionary<RequestResponseLog, string> OwnSpans(
        RequestResponseLog[] logs,
        Func<string, Dictionary<string, FlowSpan>> spansById)
    {
        var responses = new Dictionary<(string Test, Guid Id), RequestResponseLog>();
        foreach (var log in logs)
            if (log.Type == RequestResponseType.Response && IsCall(log))
                responses.TryAdd((log.TestId, log.RequestResponseId), log);

        var own = new Dictionary<RequestResponseLog, string>(ReferenceEqualityComparer.Instance);
        foreach (var log in logs)
        {
            if (log.Type != RequestResponseType.Request || !IsCall(log)
                || log.ActivityTraceId is not { } trace || log.ActivitySpanId is not { } madeUnder
                || !responses.TryGetValue((log.TestId, log.RequestResponseId), out var response)
                || response.ActivitySpanId is not { } carried
                || string.Equals(carried, madeUnder, StringComparison.Ordinal)
                || !string.Equals(response.ActivityTraceId, trace, StringComparison.Ordinal))
                continue;
            if (DescendsFrom(spansById(trace), carried, madeUnder))
                own[log] = carried;
        }
        return own;
    }

    /// <summary>
    /// Whether the store holds <paramref name="spanId"/> and <paramref name="ancestor"/> is on its chain of parents: its
    /// parent's id, or a parent's of a span the store holds above it.
    /// </summary>
    private static bool DescendsFrom(Dictionary<string, FlowSpan> byId, string spanId, string ancestor)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var id = byId.TryGetValue(spanId, out var span) ? span.ParentSpanId : null;
             id is not null && visited.Add(id);
             id = byId.TryGetValue(id, out var held) ? held.ParentSpanId : null)
        {
            if (string.Equals(id, ancestor, StringComparison.Ordinal))
                return true;
        }
        return false;
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
