namespace Kronikol.PlantUml;

/// <summary>
/// Who each party is, as the run resolved it (#228, plans/PARTICIPANT_ORDER_PLAN.md 5.1): the names resolved
/// <see cref="Parties.PartyKind.Test"/> and <see cref="Parties.PartyKind.UnderTest"/>, matched ordinal on the raw
/// caller and service names, as the resolver matches them. A sequence diagram declares the test's parties first, then
/// the parties under test, then everyone else in the order of the first call. Only a run that declares something
/// carries roles (<see cref="Parties.PartyTable.DiagramRoles"/>), so a run that declares nothing keeps today's order.
/// </summary>
internal sealed record DiagramRoles(IReadOnlySet<string> Test, IReadOnlySet<string> UnderTest)
{
    /// <summary>
    /// Where a participant goes: 1 the test, 2 under test, 3 everyone else. A participant is one alias, which several
    /// raw names can share (<c>App</c> and <c>app</c>), so it takes the first group any of its names is in.
    /// </summary>
    internal int GroupOf(IEnumerable<string> rawNames)
    {
        var group = 3;
        foreach (var name in rawNames)
        {
            if (Test.Contains(name))
                return 1;
            if (UnderTest.Contains(name))
                group = 2;
        }
        return group;
    }
}
