using Kronikol.CallGraph;

namespace Kronikol.Query;

/// <summary>
/// Which call each of a scenario's calls ran inside, for <c>kronikol query flow</c> to indent: the rule is
/// <see cref="NestingRule"/>'s (plans/FLOW_NESTING_PLAN.md §4.1, rule R4), which the call graph asks too, so this only
/// maps the index's entries into it.
/// </summary>
internal static class CallNesting
{
    /// <summary>
    /// For each entry, the position in <paramref name="interactions"/> of the request it ran inside, or null
    /// at the top level; null for every response. For a scenario's own list a position is the ordinal.
    /// </summary>
    public static int?[] Parents(IReadOnlyList<InteractionEntry> interactions) =>
        NestingRule.Parents([.. interactions.Select(entry => new NestingRecord(IsRequest(entry), IsResponse(entry), entry.RequestResponseId, entry.CallerName, entry.ServiceName, entry.TraceId))]);

    private static bool IsRequest(InteractionEntry entry) => entry.Type.Equals("Request", StringComparison.OrdinalIgnoreCase);

    private static bool IsResponse(InteractionEntry entry) => entry.Type.Equals("Response", StringComparison.OrdinalIgnoreCase);
}
