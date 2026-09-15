using Kronikol.Tracking;

namespace Kronikol.Reports;

/// <summary>
/// One scenario's share of the run's background calls: how many arrived after it had ended, and when
/// the last one did.
/// </summary>
/// <param name="ScenarioId">The scenario the calls were captured under.</param>
/// <param name="ScenarioName">Its display name, or the id when the report has no such scenario.</param>
/// <param name="Calls">Distinct calls; a request and its response count once.</param>
/// <param name="LastAt">When the latest of them was captured (UTC), when any carried a timestamp.</param>
public sealed record BackgroundCallGroup(string ScenarioId, string ScenarioName, int Calls, DateTimeOffset? LastAt);

/// <summary>
/// The interactions of a run that belong to no scenario: those captured under a scenario's identity
/// after that scenario had ended, which <see cref="BackgroundAttribution.Expire"/> re-attributes, and
/// any captured with no identity at all when <see cref="RequestResponseLogger.CaptureBackground"/> is on.
/// </summary>
/// <param name="Calls">Distinct background calls; a request and its response count once.</param>
/// <param name="AfterScenarioEnd">The expired calls, by the scenario they were taken from.</param>
/// <param name="Interactions">The background interactions themselves, in capture order.</param>
public sealed record BackgroundCalls(int Calls, IReadOnlyList<BackgroundCallGroup> AfterScenarioEnd, IReadOnlyList<RequestResponseLog> Interactions)
{
    /// <summary>A run with no background calls.</summary>
    public static readonly BackgroundCalls None = new(0, [], []);
}

/// <summary>
/// Report-time re-attribution of the calls a scenario did not make (plans/BACKGROUND_ATTRIBUTION_PLAN.md,
/// option D).
/// </summary>
/// <remarks>
/// <para>A web host built inside a test inherits the test's execution context, and every hosted service,
/// timer and consumer that host starts keeps resolving the test's identity for as long as the host lives,
/// which is usually until the end of the run. Their calls were therefore recorded against a scenario that
/// had long since finished, and the scenario's call set changed from run to run with the timing of
/// background loops. The scenario's recorded end (<see cref="Scenario.EndedAt"/>) is the boundary: a call
/// whose identity was <em>inherited</em> (<see cref="AttributionSource.TestContext"/>,
/// <see cref="AttributionSource.GlobalFallback"/>, or <see cref="AttributionSource.DocumentOwner"/>, a
/// document the scenario wrote and the host touched later) and that started after the scenario ended is the host's
/// background work, and is re-attributed to no scenario, with <see cref="RequestResponseLog.ExpiredFromTestId"/>
/// saying which scenario it was taken from.</para>
/// <para>An identity that was <em>stated</em> (a request header, an explicit scope) is trusted whatever
/// the clock says: the caller went out of its way to name the scenario. A capture with no recorded
/// provenance is trusted too, because there is no evidence it was inherited. A pair follows its request:
/// a response that lands after the scenario ended stays with the request the scenario made.</para>
/// </remarks>
public static class BackgroundAttribution
{
    /// <summary>
    /// Re-attributes every interaction that inherited a scenario's identity after that scenario had ended.
    /// Idempotent, and a no-op for a run whose adapter recorded no scenario ends.
    /// </summary>
    /// <param name="features">The run's scenarios, with <see cref="Scenario.EndedAt"/> where the adapter recorded it.</param>
    /// <param name="logs">Every interaction of the run, in capture order. Nulls are passed through.</param>
    /// <returns>A new array of the same length and order; untouched entries are the same instances.</returns>
    public static RequestResponseLog[] Expire(IReadOnlyList<Feature> features, RequestResponseLog[] logs)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(logs);

        var ends = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var scenario in features.SelectMany(f => f.Scenarios ?? []))
        {
            if (scenario.EndedAt is not { } ended)
                continue;
            // An id that appears twice (a retried outline row) keeps the later end: nothing before it is background.
            if (!ends.TryGetValue(scenario.Id, out var existing) || ended > existing)
                ends[scenario.Id] = ended;
        }

        var result = (RequestResponseLog[])logs.Clone();
        if (ends.Count == 0)
            return result;

        // The pair's earliest stamp decides for both halves, so a response captured after the end stays
        // with the request the scenario made, and a request captured after the end takes its response with it.
        var startedAt = new Dictionary<Guid, DateTimeOffset>();
        foreach (var log in logs)
        {
            if (log?.Timestamp is not { } at)
                continue;
            if (!startedAt.TryGetValue(log.RequestResponseId, out var earliest) || at < earliest)
                startedAt[log.RequestResponseId] = at;
        }

        for (var i = 0; i < result.Length; i++)
        {
            var log = result[i];
            if (log is null || !Inherited(log) || log.IsDiagramMarker)
                continue;
            if (!ends.TryGetValue(log.TestId, out var ended))
                continue;
            if (!startedAt.TryGetValue(log.RequestResponseId, out var started) || started <= ended)
                continue;

            result[i] = log with
            {
                TestId = TestIdentityScope.UnknownTestId,
                TestName = TestIdentityScope.UnknownTestName,
                AttributionSource = Tracking.AttributionSource.Expired,
                ExpiredFromTestId = log.TestId
            };
        }

        return result;
    }

    /// <summary>
    /// Gathers the background calls of a run from its (already expired) interactions: everything under the
    /// unknown identity, with the expired ones grouped by the scenario they were taken from.
    /// </summary>
    /// <param name="logs">The run's interactions after <see cref="Expire"/>. Null or empty gives <see cref="BackgroundCalls.None"/>.</param>
    /// <param name="features">The run's scenarios, for the names.</param>
    public static BackgroundCalls Summarise(IEnumerable<RequestResponseLog?>? logs, IReadOnlyList<Feature> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        if (logs is null)
            return BackgroundCalls.None;

        var background = logs
            .Where(l => l is not null && !l.IsDiagramMarker && l.TestId == TestIdentityScope.UnknownTestId)
            .Select(l => l!)
            .ToArray();
        if (background.Length == 0)
            return BackgroundCalls.None;

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var scenario in features.SelectMany(f => f.Scenarios ?? []))
            names.TryAdd(scenario.Id, scenario.DisplayName);

        var groups = background
            .Where(l => l.AttributionSource == Tracking.AttributionSource.Expired && l.ExpiredFromTestId is { Length: > 0 })
            .GroupBy(l => l.ExpiredFromTestId!, StringComparer.Ordinal)
            .Select(g => new BackgroundCallGroup(
                g.Key,
                names.TryGetValue(g.Key, out var name) ? name : g.Key,
                g.Select(l => l.RequestResponseId).Distinct().Count(),
                g.Max(l => l.Timestamp)))
            .OrderBy(g => g.ScenarioName, StringComparer.Ordinal)
            .ThenBy(g => g.ScenarioId, StringComparer.Ordinal)
            .ToArray();

        return new BackgroundCalls(
            background.Select(l => l.RequestResponseId).Distinct().Count(),
            groups,
            background);
    }

    /// <summary>The two sources that are inherited from an execution context rather than stated by the caller.</summary>
    private static bool Inherited(RequestResponseLog log) =>
        log.AttributionSource is Tracking.AttributionSource.TestContext or Tracking.AttributionSource.GlobalFallback or Tracking.AttributionSource.DocumentOwner;
}
