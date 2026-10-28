namespace Kronikol.CallGraph;

/// <summary>One record as <see cref="NestingRule"/> reads it: which half of a call it is, the call's id and trace, and its two parties.</summary>
internal readonly record struct NestingRecord(bool IsRequest, bool IsResponse, string? RequestResponseId, string Caller, string Service, string? TraceId);

/// <summary>
/// Which call each call ran inside: made while that call was still waiting for its answer, by the service handling it or
/// on its trace (plans/FLOW_NESTING_PLAN.md §4.1, rule R4). The rule lives here alone: <c>kronikol query flow</c> asks it
/// through <see cref="Kronikol.Query.CallNesting"/> to indent, and the call graph asks it for each call's parent
/// (plans/SERVICE_CARD_PLAN.md 4.2), so the two cannot read one report two ways.
///
/// <para>A request is open from its record until the first response carrying its <c>requestResponseId</c>, the pairing
/// <c>FindResponse</c> makes. A request with no id, or whose answer is not recorded after it, is never open: nobody knows
/// when it ended, and one left open would hold every later call its service made and the test's next call with them. The
/// parent of a request is the innermost open request that is either handled by the party that made it (its service is
/// the request's caller), or was made by another party and shares the request's Kronikol <c>traceId</c>, which is how a
/// delivered message sits inside the call that published it: a delivery is recorded broker → consumer, so no open call
/// has the broker as its service.</para>
///
/// <para>Each clause answers a shape measured on real reports (FLOW_NESTING_PLAN.md §3). "The innermost open request,
/// whatever it is" turned a service's parallel calls into a staircase, each inside its sibling, hung the test's next call
/// under a request never answered, and put the test's call under a query the service had started before the step.
/// Matching the caller alone left deliveries at the top level, where the lines after them read as their children. Two
/// calls from one party are never parent and child, even on one trace. <c>activityTraceId</c> is not read: it can be
/// ambient to a whole test, and adding it moved no parent on the corpus. Capture order drives it all, because two requests
/// in five carry no timestamp.</para>
/// </summary>
internal static class NestingRule
{
    /// <summary>
    /// For each record, the position in <paramref name="records"/> of the request it ran inside, or null at the top level;
    /// null for every response. For a scenario's own list a position is the ordinal.
    /// </summary>
    public static int?[] Parents(IReadOnlyList<NestingRecord> records)
    {
        var parents = new int?[records.Count];

        // A request is open only while its answer is still ahead of it.
        var answeredAt = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < records.Count; i++)
            if (records[i].IsResponse && records[i].RequestResponseId is { } id)
                answeredAt.TryAdd(id, i);

        var open = new List<int>();
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            if (record.IsRequest)
            {
                parents[i] = InnermostParent(records, open, record);
                if (record.RequestResponseId is { } id && answeredAt.TryGetValue(id, out var answer) && answer > i)
                    open.Add(i);
            }
            else if (record.IsResponse && record.RequestResponseId is { } id)
            {
                // Wherever it sits: a party's calls made at once are answered in any order.
                open.RemoveAll(o => records[o].RequestResponseId == id);
            }
        }

        return parents;
    }

    private static int? InnermostParent(IReadOnlyList<NestingRecord> records, List<int> open, NestingRecord request)
    {
        for (var k = open.Count - 1; k >= 0; k--)
        {
            var candidate = records[open[k]];
            if (string.Equals(candidate.Service, request.Caller, StringComparison.Ordinal))
                return open[k];
            if (!string.Equals(candidate.Caller, request.Caller, StringComparison.Ordinal)
                && request.TraceId is not null
                && string.Equals(candidate.TraceId, request.TraceId, StringComparison.Ordinal))
                return open[k];
        }

        return null;
    }
}
