using Kronikol.Tracking;

namespace Kronikol.Reports;

/// <summary>What <see cref="OrphanedCalls.Find"/> found.</summary>
/// <param name="Calls">Distinct calls; a request and its response count once.</param>
/// <param name="TestIds">Each id with its calls, the most calls first, then by id.</param>
internal sealed record OrphanedCallSummary(int Calls, IReadOnlyList<(string TestId, int Calls)> TestIds);

/// <summary>
/// The calls a run logged under a test id that names none of its scenarios. A call is drawn under the scenario
/// whose id it carries, and a call that carries none (<see cref="TestIdentityScope.UnknownTestId"/>) is listed
/// as a background call; a call whose id names no scenario is in neither. A test that is not tracked makes such
/// calls through a tracked client, and so does a test-info fetcher that answers with an id the adapter does not
/// report. The run records them as a <see cref="DiagnosticKind.UnattributedInteractions"/> entry; a console line, which
/// <c>dotnet test</c> does not show at its default verbosity, was once the only trace.
/// </summary>
internal static class OrphanedCalls
{
    public static OrphanedCallSummary Find(IReadOnlyList<Feature> features, IEnumerable<RequestResponseLog?> logs)
    {
        var scenarioIds = features.SelectMany(f => f.Scenarios ?? []).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var byId = logs
            .Where(l => l is not null && !l.IsDiagramMarker && l.TestId != TestIdentityScope.UnknownTestId && !scenarioIds.Contains(l.TestId))
            .GroupBy(l => l!.TestId, StringComparer.Ordinal)
            .Select(g => (TestId: g.Key, Calls: g.Select(l => l!.RequestResponseId).Distinct().Count()))
            .OrderByDescending(x => x.Calls)
            .ThenBy(x => x.TestId, StringComparer.Ordinal)
            .ToArray();
        return new OrphanedCallSummary(byId.Sum(x => x.Calls), byId);
    }

    /// <summary>The diagnostic's message, or null when there is nothing to say.</summary>
    public static string? Describe(OrphanedCallSummary found)
    {
        if (found.Calls == 0)
            return null;

        var (topId, topCalls) = found.TestIds[0];
        return $"{Count(found.Calls, "call")} carried a test id that no scenario of this run has, so no scenario and no " +
            $"background section shows them: {Count(found.TestIds.Count, "test id")}, the most calls ({topCalls}) under '{topId}'. " +
            "A test that is not tracked, or a test-info fetcher that answers with an id the adapter does not report, logs " +
            "calls like these.";
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
}
