using System.Security.Cryptography;
using System.Text;

namespace Kronikol.Reports;

/// <summary>
/// Computes deterministic stable IDs for scenarios. Unlike runtime <see cref="Scenario.Id"/>
/// (which varies by test framework and can be randomised), stable IDs are consistent across runs.
/// </summary>
public static class ScenarioStableId
{
    /// <summary>
    /// Hashes what identifies a scenario across runs. For a scenario outline, the display name is often
    /// shared by every example row, so the ordered example values go into the hash too — without them all
    /// rows of an outline collapse onto one id and cross-run matching cannot tell row 1 from row 3, which
    /// is exactly the case where per-row matching matters.
    /// </summary>
    /// <param name="suite">
    /// What separates two suites that happen to name a feature and a scenario the same way. Measured
    /// before it was added: 145 of 905 ids collided across suites, and <b>0 of 1,043 within any single
    /// report</b> — so this is a correctness fix for <i>combined</i> reports (a <c>merge</c> across
    /// suites, an ingest folding two runners, the cross-run ledger), not a live defect in an ordinary
    /// run. A null or empty suite reproduces the pre-3.1.0 id byte for byte, deliberately: a caller whose
    /// suite cannot be resolved keeps the ids it has always had rather than silently minting new ones.
    /// </param>
    public static string Compute(string? suite, string featureName, string scenarioDisplayName, string? outlineId = null,
        IReadOnlyDictionary<string, string>? exampleValues = null)
    {
        var input = $"{featureName}::{scenarioDisplayName}";
        if (outlineId is not null)
            input = $"{featureName}::{outlineId}::{scenarioDisplayName}";

        if (!string.IsNullOrEmpty(suite))
            input = $"{suite}::{input}";

        if (exampleValues is { Count: > 0 })
        {
            var ordered = string.Join("|", exampleValues
                .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => $"{kvp.Key}={kvp.Value}"));
            input = $"{input}::{ordered}";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
