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
                groups.Add(BuildGroup(HeadingOf(og.Key, members), members, maxColumns, diagramComparer));
            }
            foreach (var m in members) consumed.Add(m.Id);
        }

        // 2. Group remaining scenarios by display name prefix (only when enabled)
        var remaining = scenarios.Where(s => !consumed.Contains(s.Id)).ToArray();
        if (!enabled)
            return (groups.ToArray(), remaining);

        // a name a person wrote is the scenario's name, never a method's with its arguments in brackets (5.9.1, N5, E8:
        // "Returns 404 (not found)" was a group headed "Returns 404" with an Arg 0 column)
        ungrouped.AddRange(remaining.Where(s => s.NameAsWritten));
        remaining = remaining.Where(s => !s.NameAsWritten).ToArray();

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

    /// <summary>
    /// The heading of an outline's group (plans/TITLES_AS_WRITTEN_PLAN.md 4.3), never its key read by a humaniser a second
    /// time: a Reqnroll outline whose rows are named from their values keeps its title as written (5.9.1, N4); else what
    /// every row's name shares less its arguments; else the title the lane gave the outline
    /// (<see cref="Scenario.OutlineTitle"/>), or a Gherkin outline's title as written; else, for a model none of Kronikol's
    /// lanes made in 5.16.5 or later, the heading 5.16.4 gave.
    /// </summary>
    internal static string HeadingOf(string outlineId, IReadOnlyCollection<Scenario> members)
    {
        if (members.Any(m => m.KeyedByOutline))
            return outlineId;

        var bases = members.Select(BaseOf)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        if (bases.Length == 1)
            return bases[0];

        if (members.Select(m => m.OutlineTitle).FirstOrDefault(t => t is not null) is { } title)
            return title;
        return members.Any(m => m.NameAsWritten) ? outlineId : ScenarioTitleResolver.FormatScenarioDisplayNameAsOf5164(outlineId);
    }

    // A row's name without its arguments. A name the test wrote keeps its own brackets, a Gherkin title's "(<case>)"
    // among them (#102); only the arguments a lane put after it, in square brackets, go.
    private static string BaseOf(Scenario member) =>
        !member.NameAsWritten || (member.OutlineTitle is not null && member.DisplayName.EndsWith(']'))
            ? ParameterParser.ExtractBaseName(member.DisplayName) ?? member.DisplayName
            : member.DisplayName;

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
        members.Any(m => m.ExampleValues is { Count: > 0 } || (!m.NameAsWritten && ParameterParser.Parse(m.DisplayName) is { Count: > 0 }));

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
