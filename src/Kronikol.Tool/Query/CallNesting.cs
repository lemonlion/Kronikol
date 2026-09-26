namespace Kronikol.Tool.Query;

/// <summary>
/// Which call each call ran inside: made while that call was still waiting for its answer, by the service
/// handling it or on its trace (plans/FLOW_NESTING_PLAN.md §4.1, rule R4). The rule lives here alone, so a
/// verb that nests calls asks it rather than copying it.
///
/// <para>A request is open from its record until the first response carrying its <c>requestResponseId</c>,
/// the pairing <c>FindResponse</c> makes. A request with no id, or whose answer is not recorded after it, is
/// never open: nobody knows when it ended, and one left open would hold every later call its service made
/// and the test's next call with them. The parent of a request is the innermost open request that is
/// either handled by the party that made it (its service is the request's caller), or was made by another
/// party and shares the request's Kronikol <c>traceId</c>, which is how a delivered message sits inside the
/// call that published it: a delivery is recorded broker → consumer, so no open call has the broker as its
/// service.</para>
///
/// <para>Each clause answers a shape measured on real reports (plan §3). "The innermost open request,
/// whatever it is" turned a service's parallel calls into a staircase, each inside its sibling, hung the
/// test's next call under a request never answered, and put the test's call under a query the service had
/// started before the step. Matching the caller alone left deliveries at the top level, where the lines
/// after them read as their children. Two calls from one party are never parent and child, even on one
/// trace. <c>activityTraceId</c> is not read: it can be ambient to a whole test, and adding it moved no
/// parent on the corpus. Capture order drives it all, because two requests in five carry no
/// timestamp.</para>
/// </summary>
internal static class CallNesting
{
    /// <summary>
    /// For each entry, the position in <paramref name="interactions"/> of the request it ran inside, or null
    /// at the top level; null for every response. For a scenario's own list a position is the ordinal.
    /// </summary>
    public static int?[] Parents(IReadOnlyList<InteractionEntry> interactions)
    {
        var parents = new int?[interactions.Count];

        // A request is open only while its answer is still ahead of it.
        var answeredAt = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < interactions.Count; i++)
            if (IsResponse(interactions[i]) && interactions[i].RequestResponseId is { } id)
                answeredAt.TryAdd(id, i);

        var open = new List<int>();
        for (var i = 0; i < interactions.Count; i++)
        {
            var entry = interactions[i];
            if (IsRequest(entry))
            {
                parents[i] = InnermostParent(interactions, open, entry);
                if (entry.RequestResponseId is { } id && answeredAt.TryGetValue(id, out var answer) && answer > i)
                    open.Add(i);
            }
            else if (IsResponse(entry) && entry.RequestResponseId is { } id)
            {
                // Wherever it sits: a party's calls made at once are answered in any order.
                open.RemoveAll(o => interactions[o].RequestResponseId == id);
            }
        }

        return parents;
    }

    private static int? InnermostParent(IReadOnlyList<InteractionEntry> interactions, List<int> open, InteractionEntry request)
    {
        for (var k = open.Count - 1; k >= 0; k--)
        {
            var candidate = interactions[open[k]];
            if (string.Equals(candidate.ServiceName, request.CallerName, StringComparison.Ordinal))
                return open[k];
            if (!string.Equals(candidate.CallerName, request.CallerName, StringComparison.Ordinal)
                && request.TraceId is not null
                && string.Equals(candidate.TraceId, request.TraceId, StringComparison.Ordinal))
                return open[k];
        }

        return null;
    }

    private static bool IsRequest(InteractionEntry entry) => entry.Type.Equals("Request", StringComparison.OrdinalIgnoreCase);

    private static bool IsResponse(InteractionEntry entry) => entry.Type.Equals("Response", StringComparison.OrdinalIgnoreCase);
}
