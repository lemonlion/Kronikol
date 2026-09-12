namespace Kronikol.Reports;

/// <summary>
/// Groups failed scenarios by normalised error message to surface common failure patterns.
/// </summary>
public static class FailureClusterer
{
    /// <summary>
    /// A group of scenarios sharing the same normalised error message.
    /// </summary>
    public record FailureCluster(string ClusterKey, Scenario[] Scenarios);

    public static FailureCluster[] Cluster(Scenario[] scenarios)
    {
        // Keyed, then filtered on the key rather than on the message. `ErrorMessage is not null` let a
        // present-but-empty message through, so two failures that said nothing formed a cluster headed by
        // the empty string here while the failures digest - which filters on the key - formed none.
        var failed = scenarios
            .Where(s => s.Result == ExecutionResult.Failed)
            .Select(s => (Scenario: s, Key: FailureText.FirstLine(s.ErrorMessage)))
            .Where(x => x.Key.Length > 0)
            .ToArray();

        if (failed.Length == 0)
            return [];

        return failed
            .GroupBy(x => x.Key, x => x.Scenario, StringComparer.Ordinal)
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Count())
            .Select(g => new FailureCluster(g.Key, g.ToArray()))
            .ToArray();
    }

}
