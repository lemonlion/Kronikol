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
    public static string Compute(string featureName, string scenarioDisplayName, string? outlineId = null,
        IReadOnlyDictionary<string, string>? exampleValues = null)
    {
        var input = $"{featureName}::{scenarioDisplayName}";
        if (outlineId is not null)
            input = $"{featureName}::{outlineId}::{scenarioDisplayName}";

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
