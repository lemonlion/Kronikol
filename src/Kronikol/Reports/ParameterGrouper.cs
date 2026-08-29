using System.Diagnostics;

namespace Kronikol.Reports;

/// <summary>
/// Groups parameterized test scenarios and determines the optimal display rule
/// (scalar columns, flattened object, or fallback) for the parameter table.
/// </summary>
public static class ParameterGrouper
{
    /// <summary>
    /// Analyzes scenarios and groups parameterized test runs sharing the same test method.
    /// Returns (parameterizedGroups, ungroupedScenarios).
    /// </summary>
    public static (ParameterizedGroup[] Groups, Scenario[] Ungrouped) Analyze(
        Scenario[] scenarios,
        bool enabled = true,
        int maxColumns = 10,
        Func<Scenario[], bool>? diagramComparer = null)
    {
        if (scenarios.Length == 0)
            return ([], scenarios);

        // Deep-clone scenarios before analysis, because R2 flattening mutates ExampleValues/ExampleRawValues.
        // This prevents race conditions when multiple report generators call Analyze in parallel
        // on the same Scenario objects (e.g. Specifications + TestRunReport via Parallel.Invoke).
        scenarios = scenarios.Select(s => s with
        {
            ExampleValues = s.ExampleValues is not null ? new Dictionary<string, string>(s.ExampleValues) : null,
            ExampleRawValues = s.ExampleRawValues is not null ? new Dictionary<string, object?>(s.ExampleRawValues) : null,
            ExampleFlatValues = s.ExampleFlatValues is not null ? new Dictionary<string, string>(s.ExampleFlatValues) : null
        }).ToArray();

        var groups = new List<ParameterizedGroup>();
        var ungrouped = new List<Scenario>();
        var consumed = new HashSet<string>(); // scenario IDs already assigned to a group

        // 1. Group by OutlineId (framework-provided, e.g. ReqNRoll) — always active
        var outlineGroups = scenarios
            .Where(s => s.OutlineId is not null)
            .GroupBy(s => s.OutlineId!)
            .ToArray();

        foreach (var og in outlineGroups)
        {
            var members = og.ToArray();
            if (members.Length < 2 && !HasParameters(members))
            {
                ungrouped.AddRange(members);
            }
            else
            {
                groups.Add(BuildGroup(ScenarioTitleResolver.FormatScenarioDisplayName(og.Key), members, maxColumns, diagramComparer));
            }
            foreach (var m in members) consumed.Add(m.Id);
        }

        // 2. Group remaining scenarios by display name prefix (only when enabled)
        var remaining = scenarios.Where(s => !consumed.Contains(s.Id)).ToArray();
        if (!enabled)
            return (groups.ToArray(), remaining);

        var prefixGroups = remaining
            .Select(s => (Scenario: s, BaseName: ParameterParser.ExtractBaseName(s.DisplayName) ?? s.DisplayName))
            .GroupBy(x => x.BaseName)
            .ToArray();

        foreach (var pg in prefixGroups)
        {
            var members = pg.Select(x => x.Scenario).ToArray();
            if (members.Length < 2 && !HasParameters(members))
            {
                ungrouped.AddRange(members);
            }
            else
            {
                groups.Add(BuildGroup(pg.Key, members, maxColumns, diagramComparer));
            }
        }

        return (groups.ToArray(), ungrouped.ToArray());
    }

    private static ParameterizedGroup BuildGroup(
        string groupName,
        Scenario[] members,
        int maxColumns,
        Func<Scenario[], bool>? diagramComparer)
    {
        // Rows from the same Examples: block must sit together. OrderBy is a stable sort, so
        // source order within a block survives, and blockless members (index null) stay in
        // their existing order after the indexed ones — output unchanged when no block info exists.
        if (members.Any(m => m.ExamplesBlockIndex is not null))
            members = members.OrderBy(m => m.ExamplesBlockIndex ?? int.MaxValue).ToArray();

        // Determine parameter names and rule
        var (paramNames, rule) = DetermineParamsAndRule(members, maxColumns);
        var identical = diagramComparer?.Invoke(members) ?? false;

        // Compute flat parameter names when all members have ExampleFlatValues
        string[]? flatParamNames = null;
        if (members.All(m => m.ExampleFlatValues is { Count: > 0 }))
        {
            flatParamNames = members
                .SelectMany(m => m.ExampleFlatValues!.Keys)
                .Distinct()
                .ToArray();
        }

        return new ParameterizedGroup(groupName, paramNames, rule, members, identical, flatParamNames);
    }

    private static bool HasParameters(Scenario[] members) =>
        members.Any(m => m.ExampleValues is { Count: > 0 } || ParameterParser.Parse(m.DisplayName) is { Count: > 0 });

    private static (string[] ParamNames, ParameterDisplayRule Rule) DetermineParamsAndRule(
        Scenario[] members, int maxColumns)
    {
        // If any member has an ExampleDisplayName (custom display name), use Fallback
        if (members.Any(m => m.ExampleDisplayName is not null))
            return ([], ParameterDisplayRule.Fallback);

        // Try ExampleValues first (structured params from framework)
        var withExampleValues = members.Where(m => m.ExampleValues is { Count: > 0 }).ToArray();
        if (withExampleValues.Length == members.Length)
        {
            var keys = withExampleValues
                .SelectMany(m => m.ExampleValues!.Keys)
                .Distinct()
                .ToArray();

            // R2 detection: single complex param with all scalar properties → flatten
            if (keys.Length == 1)
            {
                var r2Result = TryDetectFlattenedObject(members, keys[0], maxColumns);
                if (r2Result is not null)
                    return r2Result.Value;
            }

            if (keys.Length > maxColumns)
                return ([], ParameterDisplayRule.Fallback);

            return (keys, ParameterDisplayRule.ScalarColumns);
        }

        // Fall back to parsing display names
        var allParsed = new List<Dictionary<string, string>>();
        foreach (var m in members)
        {
            var parsed = ParameterParser.Parse(m.DisplayName);
            if (parsed is null || parsed.Count == 0)
                return ([], ParameterDisplayRule.Fallback);
            allParsed.Add(parsed);

            // Populate ExampleValues on the scenario so the renderer can use them
            m.ExampleValues ??= parsed;
        }

        var allKeys = allParsed.SelectMany(d => d.Keys).Distinct().ToArray();
        if (allKeys.Length > maxColumns)
            return ([], ParameterDisplayRule.Fallback);

        return (allKeys, ParameterDisplayRule.ScalarColumns);
    }

    /// <summary>
    /// R2: If a single param is a complex object with all scalar properties ≤ maxColumns,
    /// flatten its properties into columns and update ExampleValues/ExampleRawValues on each scenario.
    /// </summary>
    private static (string[] ParamNames, ParameterDisplayRule Rule)? TryDetectFlattenedObject(
        Scenario[] members, string paramKey, int maxColumns)
    {
        // Try reflection-based flattening first (when raw objects are available)
        var withRaw = members.Where(m => m.ExampleRawValues is not null && m.ExampleRawValues.ContainsKey(paramKey)).ToArray();
        if (withRaw.Length == members.Length)
        {
            var firstRawValue = withRaw.Select(m => m.ExampleRawValues![paramKey]).FirstOrDefault(v => v is not null);
            if (firstRawValue is not null)
            {
                var propertyNames = ParameterValueRenderer.TryGetFlattenableProperties(firstRawValue, maxColumns);
                if (propertyNames is not null)
                {
                    foreach (var m in members)
                    {
                        var rawValue = m.ExampleRawValues![paramKey];
                        if (rawValue is not null)
                        {
                            m.ExampleValues = ParameterValueRenderer.FlattenToStringValues(rawValue, propertyNames);
                            m.ExampleRawValues = ParameterValueRenderer.FlattenToRawValues(rawValue, propertyNames);
                        }
                    }
                    return (propertyNames, ParameterDisplayRule.FlattenedObject);
                }
            }
        }

        // Fallback: try string-based record ToString() parsing (e.g. "TypeName { Prop = Val, ... }")
        return TryStringBasedFlatten(members, paramKey, maxColumns);
    }

    /// <summary>
    /// String-based R2: Parse record ToString() representations (e.g. "TypeName { Prop = Val, ... }")
    /// into individual columns when all members match and property count ≤ maxColumns.
    /// </summary>
    private static (string[] ParamNames, ParameterDisplayRule Rule)? TryStringBasedFlatten(
        Scenario[] members, string paramKey, int maxColumns)
    {
        try
        {
            // Parse the first member's value to detect the pattern
            var firstValue = members[0].ExampleValues?.GetValueOrDefault(paramKey);
            var firstParsed = ParameterParser.TryParseRecordToString(firstValue);
            if (firstParsed is null)
                return null;

            var propertyNames = firstParsed.Keys.ToArray();
            if (propertyNames.Length == 0 || propertyNames.Length > maxColumns)
                return null;

            // Verify all members parse with the same property names and flatten
            foreach (var m in members)
            {
                var val = m.ExampleValues?.GetValueOrDefault(paramKey);
                var parsed = ParameterParser.TryParseRecordToString(val);
                if (parsed is null)
                    return null;

                // All members must have the same set of property names
                if (!propertyNames.All(parsed.ContainsKey))
                    return null;

                m.ExampleValues = parsed;
            }

            return (propertyNames, ParameterDisplayRule.FlattenedObject);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Kronikol] Warning: String-based record flattening failed for param '{paramKey}': {ex.Message}");
            return null;
        }
    }
}
