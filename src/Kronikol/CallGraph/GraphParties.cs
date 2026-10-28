using Kronikol.Parties;

namespace Kronikol.CallGraph;

/// <summary>
/// The parties a graph reads (plans/SERVICE_CARD_PLAN.md 4.2, Q15): #192's party table of the report,
/// <see cref="PartyTable.OfReport"/>, the function <c>kronikol query</c> reads a report's parties with, so the two agree on
/// who each party is. It is the report's block as written when the run declared something, else the inference over the
/// calls, never what a run observed and did not write.
/// </summary>
internal static class GraphParties
{
    /// <summary>
    /// The table, and the card's rows of it: one a name the calls carry, the table's row or, for a name the block does not
    /// hold, an unknown one, and the subject's row though no call reaches it. The subject is the table's. The inference's
    /// version when the rows were inferred, null when they came from the block.
    /// </summary>
    internal static (PartyTable Table, IReadOnlyList<Party> Rows, Party? Subject, int? Inference) Resolve(PartiesInput input, IReadOnlyCollection<PartyCall> calls)
    {
        var (table, subject) = PartyTable.OfReport(input.Block, input.ServiceId, calls);
        var held = new Dictionary<string, Party>(StringComparer.Ordinal);
        foreach (var row in table.Parties)
            held.TryAdd(row.Name, row);

        var names = calls.SelectMany(c => new[] { c.Caller, c.Service }).Append(subject).OfType<string>()
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        // A name the block does not hold is a party the run did not resolve, which is unknown, not inferred here.
        var rows = names.Select(n => held.TryGetValue(n, out var row) ? row : new Party(n, null, PartyKind.Unknown, null, null)).ToList();
        return (table, rows, subject is null ? null : held[subject], input.Block is null ? PartyResolver.InferenceVersion : null);
    }
}
