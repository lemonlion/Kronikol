"""R2's first cut, on top of the final R1 (r1-first-cut.patch): every step the pickle plans is listed, those that never ran as skipped.

python -I apply_r2.py <checkout>

MapSteps already turns a Skipped step after a failure into SkippedAfterFailure; since Reqnroll runs no step hook for a step it skips, that
branch has never been reached on this lane.
"""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])


def edit(path, *pairs):
    p = root / path
    t = p.read_text(encoding="utf-8")
    for old, new in pairs:
        assert old in t, f"{path}: not found: {old[:70]!r}"
        t = t.replace(old, new, 1)
    p.write_text(t, encoding="utf-8", newline="\n")


edit(
    "src/Kronikol.ReqNRoll.Core/ExamplesBlockResolver.cs",
    (
        """/// <summary>
/// Maps a running Reqnroll scenario back to the <c>Examples:</c> block its row came from.""",
        """/// <summary>A step a scenario's pickle plans: its keyword as the hooks name one, its text, and its arguments with an outline's values filled.</summary>
internal readonly record struct PlannedStep(string Keyword, string Text, string? TableText, string? DocString);

/// <summary>
/// Maps a running Reqnroll scenario back to the <c>Examples:</c> block its row came from.""",
    ),
    (
        """    /// <summary>
    /// The texts of the steps the feature's <c>Background:</c> and the scenario's rule's give the running scenario, in the""",
        """    /// <summary>
    /// Every step the running scenario's pickle plans, in the order they run; empty when the pickle is not found or is not
    /// this scenario's. Same reflection seam, same silent-degradation contract as <see cref="Resolve"/>.
    /// </summary>
    public static IReadOnlyList<PlannedStep> ResolvePlannedSteps(FeatureInfo? featureInfo, ScenarioInfo? scenarioInfo)
    {
        if (featureInfo is null || scenarioInfo is null)
            return [];
        try
        {
            var lookup = LookupCache.GetValue(featureInfo, BuildLookup);
            if (FindPickle(lookup, scenarioInfo) is not { Steps: { } steps })
                return [];
            return steps.Select(step => new PlannedStep(
                KeywordOf(step, lookup),
                step.Text ?? "",
                TableTextOf(step.Argument?.DataTable),
                step.Argument?.DocString?.Content)).ToArray();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// The keyword as Reqnroll's <c>StepDefinitionKeyword</c> names it, whatever the feature file's language: the step's type,
    /// with a conjunction read as And unless it is written But in English.
    /// </summary>
    private static string KeywordOf(PickleStep step, FeatureBlockLookup lookup)
    {
        var written = step.AstNodeIds is { Count: > 0 } ids && lookup.StepsById.TryGetValue(ids[0], out var ast) ? ast : null;
        return written?.KeywordType switch
        {
            StepKeywordType.CONTEXT => "Given",
            StepKeywordType.ACTION => "When",
            StepKeywordType.OUTCOME => "Then",
            _ => written?.Keyword?.Trim() == "But" ? "But" : "And",
        };
    }

    /// <summary>A table as <c>Table.ToString()</c> writes it, which is what the step mapper parses.</summary>
    private static string? TableTextOf(PickleTable? table)
    {
        if (table?.Rows is not { Count: > 0 } rows)
            return null;
        return string.Concat(rows.Select(row => "|" + string.Concat(row.Cells.Select(c => " " + c.Value + " |")) + "\\n"));
    }

    /// <summary>
    /// The texts of the steps the feature's <c>Background:</c> and the scenario's rule's give the running scenario, in the""",
    ),
    (
        """        /// <summary>A scenario's ast id to the texts of the Background steps it runs first: its feature's, then its rule's.</summary>""",
        """        /// <summary>Every written step by its ast id, which a pickle step names: its keyword is the document's alone.</summary>
        public Dictionary<string, Step> StepsById { get; } = new(StringComparer.Ordinal);

        /// <summary>A scenario's ast id to the texts of the Background steps it runs first: its feature's, then its rule's.</summary>""",
    ),
    (
        """    private static void IndexBackground(FeatureBlockLookup lookup, Scenario scenario, IReadOnlyList<Step> steps)
    {
        var texts = steps.Select(s => s.Text ?? "").ToArray();""",
        """    private static void IndexBackground(FeatureBlockLookup lookup, Scenario scenario, IReadOnlyList<Step> steps)
    {
        foreach (var step in steps.Concat(scenario.Steps ?? []))
        {
            if (step.Id is { Length: > 0 } stepId)
                lookup.StepsById[stepId] = step;
        }
        var texts = steps.Select(s => s.Text ?? "").ToArray();""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
    (
        """        var source = ExamplesBlockResolver.ResolveSource(_featureContext.FeatureInfo, _scenarioContext.ScenarioInfo);
""",
        """        var source = ExamplesBlockResolver.ResolveSource(_featureContext.FeatureInfo, _scenarioContext.ScenarioInfo);
        AddStepsThatNeverRan(steps, ExamplesBlockResolver.ResolvePlannedSteps(_featureContext.FeatureInfo, _scenarioContext.ScenarioInfo));
""",
    ),
    (
        """    /// <summary>A step under way. Its definition can run more of them, which finish inside it.</summary>""",
        """    /// <summary>
    /// Reqnroll runs no step hook for a step it skips, so a failed step leaves the rest of the scenario out of the list. The
    /// pickle has them. Only when the steps that ran are the first of those it plans: anything else is left as it ran.
    /// </summary>
    internal static void AddStepsThatNeverRan(List<ReqNRollStepInfo> steps, IReadOnlyList<PlannedStep> planned)
    {
        var rows = steps.Where(step => !step.RanByAStep).ToList();
        if (rows.Count >= planned.Count)
            return;
        for (var i = 0; i < rows.Count; i++)
        {
            if (!string.Equals(rows[i].Text, planned[i].Text, StringComparison.Ordinal))
                return;
        }
        for (var i = rows.Count; i < planned.Count; i++)
            steps.Add(new ReqNRollStepInfo(planned[i].Keyword, planned[i].Text, ScenarioExecutionStatus.Skipped, null, planned[i].TableText, planned[i].DocString));
    }

    /// <summary>A step under way. Its definition can run more of them, which finish inside it.</summary>""",
    ),
)
print("applied")
