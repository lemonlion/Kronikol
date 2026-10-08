namespace Kronikol.Tests.History;

/// <summary>
/// <see cref="HistoryLedgerTests"/> runs here, after every parallel collection, because one of its facts holds a ledger
/// read to a wall-clock budget.
/// </summary>
/// <remarks>
/// The fact takes the fastest of three reads, which keeps one stalled sample from failing it, but in the parallel suite the
/// stall can outlast all three: on 2026-10-08 the fastest read took 2,812 ms and then 2,704 ms in two full runs of this
/// suite, against a 1,500 ms budget, and the same fact passed when run alone; the reader had not changed since 3.27.0.
/// <c>DisableParallelization</c> runs the class with nothing else of this suite beside it, so the budget meters the reader
/// and still catches a regression to parsing the whole ledger.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HistoryReadBudgetCollection
{
    public const string Name = "HistoryReadBudget";
}
