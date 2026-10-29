namespace Kronikol.History;

/// <summary>
/// One call as the history templater takes it in, before any rule: what every kept rule makes a call line from
/// (<see cref="InteractionShape.Calls(IReadOnlyList{HistoryRawCall}, HistoryShapeRules?, int)"/>). The fragment carries
/// the run's, so <c>history record</c>, which has the ledger, fingerprints the run under the consumer's patterns the
/// ledger's window was fingerprinted under, as a run that reads its ledger does from its logs
/// (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8, Q18). The ledger never holds one.
/// </summary>
/// <param name="Caller">Who made the call.</param>
/// <param name="Service">What was called.</param>
/// <param name="Method">The verb.</param>
/// <param name="Path">The path the operation key templates, untemplated (<see cref="CallOperation.PathOf"/>, with the query).</param>
/// <param name="Operation">A GraphQL request's operation, or <c>batch</c>; null for any other call.</param>
/// <param name="Head">A statement-shaped call's statement, its first line as the key templates it; null for any other call.</param>
/// <param name="Status">The response's status code or text, or null when no response was captured.</param>
internal sealed record HistoryRawCall(string Caller, string Service, string Method, string Path, string? Operation, string? Head, string? Status)
{
    /// <summary>The callee as rule 6 named it, when that differs: a host named by its address was <c>localhost:{port}</c>.</summary>
    public string? Service6 { get; init; }

    /// <summary>The verb as rules 6 and 7 read it, when that differs (<see cref="Tracking.RequestResponseLog.RecordedBefore8"/>).</summary>
    public string? Method7 { get; init; }

    /// <summary>The escaped path and query rules 6 and 7 template, when that differs from <see cref="Path"/>.</summary>
    public string? Path7 { get; init; }

    /// <summary>The statement's first line as rules 6 and 7 read it, when that differs from <see cref="Head"/>.</summary>
    public string? Head7 { get; init; }

    /// <summary>Whether rules 6 and 7 read no call here: its tracker did not record it before 8.</summary>
    public bool Absent7 { get; init; }

    /// <summary>The verb as rule 8 read it, when that differs (<see cref="Tracking.RequestResponseLog.RecordedBefore9"/>).</summary>
    public string? Method8 { get; init; }

    /// <summary>The untemplated path and query rule 8 keys, when that differs from <see cref="Path"/>.</summary>
    public string? Path8 { get; init; }

    /// <summary>Whether rule 8 reads no call here: its tracker did not record it before 9.</summary>
    public bool Absent8 { get; init; }

    /// <summary>Whether rules 6 to 8 read no response here: its tracker recorded none before 9, as for a send that threw.</summary>
    public bool Unanswered8 { get; init; }
}

/// <summary>
/// A run's raw calls (<see cref="HistoryRawCall"/>): each distinct one once, and each roster position's calls, in the
/// order they were made, as indices into them.
/// </summary>
/// <param name="Calls">The distinct calls.</param>
/// <param name="Order">Each position's calls, in request order.</param>
internal sealed record HistoryRawCalls(IReadOnlyList<HistoryRawCall> Calls, IReadOnlyList<IReadOnlyList<int>> Order)
{
    /// <summary>The calls of the scenario at <paramref name="position"/>, in the order it made them.</summary>
    public IReadOnlyList<HistoryRawCall> At(int position) =>
        position >= 0 && position < Order.Count ? Order[position].Select(i => Calls[i]).ToArray() : [];

    /// <summary>Each position's calls (one list per position), each distinct call held once.</summary>
    public static HistoryRawCalls Create(IReadOnlyList<IReadOnlyList<HistoryRawCall>> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var calls = new List<HistoryRawCall>();
        var index = new Dictionary<HistoryRawCall, int>();
        var order = new IReadOnlyList<int>[positions.Count];
        for (var p = 0; p < positions.Count; p++)
        {
            var at = new int[positions[p].Count];
            for (var i = 0; i < at.Length; i++)
            {
                var call = positions[p][i];
                if (!index.TryGetValue(call, out var found))
                {
                    found = calls.Count;
                    index[call] = found;
                    calls.Add(call);
                }
                at[i] = found;
            }
            order[p] = at;
        }
        return new HistoryRawCalls(calls, order);
    }
}
