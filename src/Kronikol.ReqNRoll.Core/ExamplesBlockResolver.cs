using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Io.Cucumber.Messages.Types;
using Reqnroll;
using Reqnroll.Formatters.RuntimeSupport;

namespace Kronikol.ReqNRoll;

/// <summary>The <c>Examples:</c> block identity of one scenario-outline row; all-null when unresolvable.</summary>
internal readonly record struct ExamplesBlock(string? Name, string? Description, int? Index)
{
    public static readonly ExamplesBlock None = new(null, null, null);
}

/// <summary>
/// Maps a running Reqnroll scenario back to the <c>Examples:</c> block its row came from.
/// Reqnroll's generated code embeds the feature's Cucumber messages (Gherkin document + pickles)
/// in every <see cref="FeatureInfo"/>; only the two properties that expose the plumbing are
/// internal, so they are read by reflection and every failure degrades to
/// <see cref="ExamplesBlock.None"/> — the report then renders exactly as it did before this feature.
/// </summary>
internal static class ExamplesBlockResolver
{
    private const BindingFlags InstanceAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Verified against Reqnroll 3.3.4. A rename in a future Reqnroll simply yields nulls here
    // (and trips the reflection-contract unit test so the drift is loud in CI).
    private static readonly PropertyInfo? FeatureMessagesProperty = GetProperty(typeof(FeatureInfo), "FeatureCucumberMessages");
    private static readonly PropertyInfo? PickleIdProperty = GetProperty(typeof(ScenarioInfo), "PickleId");
    private static readonly PropertyInfo? PickleIdIndexProperty = GetProperty(typeof(ScenarioInfo), "PickleIdIndex");

    // Hooks run once per scenario; the row-id lookup is per feature, so cache it per FeatureInfo instance.
    private static readonly ConditionalWeakTable<FeatureInfo, FeatureBlockLookup> LookupCache = new();

    public static ExamplesBlock Resolve(FeatureInfo? featureInfo, ScenarioInfo? scenarioInfo)
    {
        if (featureInfo is null || scenarioInfo is null)
            return ExamplesBlock.None;
        try
        {
            return ResolveCore(featureInfo, scenarioInfo);
        }
        catch
        {
            return ExamplesBlock.None;
        }
    }

    private static ExamplesBlock ResolveCore(FeatureInfo featureInfo, ScenarioInfo scenarioInfo)
    {
        var lookup = LookupCache.GetValue(featureInfo, BuildLookup);
        if (lookup.RowsById.Count == 0)
            return ExamplesBlock.None;

        var argumentValues = ArgumentValues(scenarioInfo);

        // 1. Pickle route: PickleId (populated at runtime) or PickleIdIndex → the pickle → its
        //    example-row ast id. Cross-checked against the argument values so a wrong index
        //    assumption falls through to the value match instead of mislabelling the row.
        if (FindPickle(lookup, scenarioInfo) is { AstNodeIds.Count: > 1 } pickle
            && lookup.RowsById.TryGetValue(pickle.AstNodeIds[1], out var entry)
            && (argumentValues is null || entry.CellValues.SequenceEqual(argumentValues, StringComparer.Ordinal)))
        {
            return entry.Block;
        }

        // 2. Value match: the single block owning a row whose cells equal the argument values.
        if (argumentValues is null)
            return ExamplesBlock.None;
        var candidates = lookup.RowsById.Values
            .Where(e => string.Equals(e.ScenarioName, scenarioInfo.Title, StringComparison.Ordinal)
                        && e.CellValues.SequenceEqual(argumentValues, StringComparer.Ordinal))
            .Select(e => e.Block)
            .Distinct()
            .ToArray();

        // 3. Ambiguous (identical rows in two blocks) or nothing found → give up.
        return candidates.Length == 1 ? candidates[0] : ExamplesBlock.None;
    }

    private static Pickle? FindPickle(FeatureBlockLookup lookup, ScenarioInfo scenarioInfo)
    {
        if (PickleIdProperty?.GetValue(scenarioInfo) is string pickleId && pickleId.Length > 0
            && lookup.Pickles.FirstOrDefault(p => string.Equals(p.Id, pickleId, StringComparison.Ordinal)) is { } byId)
        {
            return byId;
        }

        // PickleIdIndex is a 0-based index into the feature's pickle list, in feature-file order
        // (Reqnroll 3.3.4 generated code passes "0" for the file's first scenario and continues
        // counting one per outline row).
        if (PickleIdIndexProperty?.GetValue(scenarioInfo) is string indexText
            && int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            && index >= 0 && index < lookup.Pickles.Count)
        {
            return lookup.Pickles[index];
        }

        return null;
    }

    private static string[]? ArgumentValues(ScenarioInfo scenarioInfo)
    {
        var arguments = scenarioInfo.Arguments;
        if (arguments is null || arguments.Count == 0)
            return null;
        var values = new string[arguments.Count];
        var i = 0;
        foreach (var value in arguments.Values)
            values[i++] = value?.ToString() ?? "";
        return values;
    }

    private sealed record RowEntry(string ScenarioName, ExamplesBlock Block, string[] CellValues);

    private sealed class FeatureBlockLookup
    {
        public Dictionary<string, RowEntry> RowsById { get; } = new(StringComparer.Ordinal);
        public List<Pickle> Pickles { get; } = [];
    }

    private static FeatureBlockLookup BuildLookup(FeatureInfo featureInfo)
    {
        var lookup = new FeatureBlockLookup();
        try
        {
            if (FeatureMessagesProperty?.GetValue(featureInfo) is not IFeatureLevelCucumberMessages messages
                || !messages.HasMessages)
            {
                return lookup;
            }

            lookup.Pickles.AddRange(messages.Pickles ?? []);

            foreach (var scenario in ScenarioNodes(messages.GherkinDocument?.Feature))
                IndexScenario(lookup, scenario);
        }
        catch
        {
            // A malformed or unexpected message shape must never break the scenario hook.
        }

        return lookup;
    }

    private static IEnumerable<Scenario> ScenarioNodes(Feature? feature)
    {
        foreach (var child in feature?.Children ?? [])
        {
            if (child.Scenario is { } scenario)
                yield return scenario;
            foreach (var ruleChild in child.Rule?.Children ?? [])
            {
                if (ruleChild.Scenario is { } ruleScenario)
                    yield return ruleScenario;
            }
        }
    }

    private static void IndexScenario(FeatureBlockLookup lookup, Scenario scenario)
    {
        var blocks = scenario.Examples ?? [];
        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            var examples = blocks[blockIndex];
            var block = new ExamplesBlock(
                NullIfBlank(examples.Name),
                NullIfBlank(Dedent(examples.Description)),
                blockIndex);

            foreach (var row in examples.TableBody ?? [])
            {
                if (row.Id is not { Length: > 0 } rowId)
                    continue;
                var cells = (row.Cells ?? []).Select(c => c.Value ?? "").ToArray();
                lookup.RowsById[rowId] = new RowEntry(scenario.Name ?? "", block, cells);
            }
        }
    }

    private static PropertyInfo? GetProperty(Type type, string name)
    {
        try
        {
            return type.GetProperty(name, InstanceAny);
        }
        catch
        {
            return null;
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Removes the common leading indentation Gherkin keeps on description blocks (mirrors the Cucumber ingest path).</summary>
    private static string? Dedent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var indent = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();
        return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart())).Trim('\n');
    }
}
