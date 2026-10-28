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
    /// <param name="featureName">The feature's display name — the test class, or the title after Feature:.</param>
    /// <param name="scenarioDisplayName">
    /// The scenario's display name; for an outline row, its expanded name. The exception is a Reqnroll outline row: its
    /// name is filled from its values since #101, and the report hashes its outline's title here instead, the name it
    /// bore before, so that its id did not change.
    /// </param>
    /// <param name="outlineId">The outline the scenario belongs to, when it is a row of one.</param>
    /// <param name="exampleValues">
    /// The row's example values, in declaration order. What tells row 1 from row 3 when both carry the
    /// same display name.
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

    /// <summary>
    /// The name a scenario's id, order and in-page anchor key on: the name its lane recorded where it shows another
    /// (<see cref="Scenario.StableIdKey"/>), else its display name, except for an outline row whose name Kronikol filled
    /// from its values (<see cref="Scenario.KeyedByOutline"/>, a Reqnroll row since #101), which keys on its outline's
    /// title, the name it bore before. Read when it is used, so a title
    /// <see cref="ReportConfigurationOptions.CapitaliseTitles"/> re-cased is the key as it is.
    /// </summary>
    internal static string KeyName(Scenario scenario) =>
        scenario.StableIdKey?.Name
        ?? (scenario is { KeyedByOutline: true, OutlineId: { } outlineId } ? outlineId : scenario.DisplayName);

    /// <summary>
    /// The stable id of a scenario the report holds: <see cref="Compute"/> over <see cref="KeyName"/>, its outline and
    /// its example values, or over its recorded <see cref="Scenario.StableIdKey"/> where its lane recorded one.
    /// </summary>
    internal static string For(string? suite, string featureName, Scenario scenario) =>
        scenario.StableIdKey is { } key
            ? Compute(suite, featureName, key.Name, key.OutlineId, key.ExampleValues)
            : Compute(suite, featureName, KeyName(scenario), scenario.OutlineId, scenario.ExampleValues);

    /// <summary>
    /// The key a lane records for a scenario it names <paramref name="name"/>, with <paramref name="outlineId"/> and
    /// <paramref name="exampleValues"/>, where 5.16.4 named it <paramref name="frozen"/>'s way: null when the two agree on
    /// everything the id hashes, so only a scenario whose title the rule changed carries one.
    /// </summary>
    internal static ScenarioIdKey? KeyWhereChanged(ScenarioIdKey frozen, string name, string? outlineId,
        IReadOnlyDictionary<string, string>? exampleValues) =>
        frozen.Name == name && frozen.OutlineId == outlineId && SameValues(frozen.ExampleValues, exampleValues) ? null : frozen;

    /// <summary>Whether two rows' values hash alike: the same pairs, in any order, none and empty being one.</summary>
    internal static bool SameValues(IReadOnlyDictionary<string, string>? a, IReadOnlyDictionary<string, string>? b)
    {
        if (a is not { Count: > 0 } || b is not { Count: > 0 })
            return (a?.Count ?? 0) == 0 && (b?.Count ?? 0) == 0;
        return a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var value) && value == kv.Value);
    }

    /// <summary>
    /// The name a feature's scenarios' ids key on: <see cref="Feature.StableIdName"/> where its lane recorded the name
    /// 5.9.0 hashed (plans/REPORT_UI_FIXES_PLAN.md N3), else its display name.
    /// </summary>
    internal static string FeatureKey(Feature feature) => feature.StableIdName ?? feature.DisplayName;

    /// <summary>The stable id of a scenario of <paramref name="feature"/>, keyed by <see cref="FeatureKey"/>.</summary>
    internal static string For(string? suite, Feature feature, Scenario scenario) =>
        For(suite, FeatureKey(feature), scenario);
}

/// <summary>
/// What a scenario's stable id is computed from where its lane shows the scenario otherwise than 5.16.4 did: the name
/// 5.16.4 showed, and the outline and example values the lane read from that name then
/// (plans/TITLES_AS_WRITTEN_PLAN.md 4.4). So a title the rule now reads better, a name the test wrote shown as written,
/// keeps its id and its history.
/// </summary>
internal sealed record ScenarioIdKey(string Name, string? OutlineId, IReadOnlyDictionary<string, string>? ExampleValues);
