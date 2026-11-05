namespace Kronikol.Reports;

/// <summary>
/// Guesses a Gherkin Background from steps that read alike: the longest prefix several scenarios share goes into
/// <see cref="Scenario.BackgroundSteps"/>, each scenario keeping its own steps.
/// Groups scenarios by <see cref="Scenario.Rule"/> so each Rule can have its own background.
/// <para>
/// It is a guess, and the steps' text cannot settle it: it misses a Background over one scenario or followed by a
/// <c>Given</c> or <c>When</c>, and calls a prefix one where none was written. Only a producer with no source to read should
/// call it. The Reqnroll adapter reads the Gherkin document, and the Cucumber lane its messages.
/// </para>
/// </summary>
public static class BackgroundStepsDetector
{
    /// <summary>
    /// Detects and extracts common step prefixes within each Rule group.
    /// Mutates the scenarios in-place: sets <see cref="Scenario.BackgroundSteps"/>
    /// and trims the prefix from <see cref="Scenario.Steps"/>.
    /// </summary>
    public static void DetectAndExtract(Scenario[] scenarios)
    {
        if (scenarios.Length < 2)
            return;

        var groups = scenarios.GroupBy(s => s.Rule);

        foreach (var group in groups)
        {
            var members = group.Where(s => s.Steps is { Length: > 0 }).ToArray();
            if (members.Length < 2)
                continue;

            // If any scenario starts with "And" or "When", the shared prefix is not
            // a Gherkin Background – skip extraction for this group.
            if (members.Any(s => s.Steps![0].Keyword is "And" or "When"))
                continue;

            var commonPrefixLength = FindCommonPrefixLength(members);
            if (commonPrefixLength == 0)
                continue;

            // Don't extract if any scenario's first remaining step starts with "Given" or "When";
            // background extraction only makes sense when remaining steps start with continuation
            // keywords (Then, And, But) or when all steps are common (no remaining steps).
            if (members.Any(s => s.Steps!.Length > commonPrefixLength &&
                                 s.Steps![commonPrefixLength].Keyword is "Given" or "When"))
                continue;

            // Steps that read alike are still each scenario's own: its result, its arguments, its sub-steps (#222).
            foreach (var scenario in members)
            {
                scenario.BackgroundSteps = scenario.Steps!.Take(commonPrefixLength).ToArray();
                scenario.Steps = scenario.Steps!.Skip(commonPrefixLength).ToArray();
            }
        }
    }

    private static int FindCommonPrefixLength(Scenario[] scenarios)
    {
        var minStepCount = scenarios.Min(s => s.Steps!.Length);
        var prefixLength = 0;

        for (var i = 0; i < minStepCount; i++)
        {
            var reference = scenarios[0].Steps![i];
            // A row no framework step ran says nothing about what a feature shares, and a scenario rebuilt by hand keeps its rows' marks.
            var allMatch = scenarios.All(s =>
                !s.Steps![i].OutsideFrameworkStep &&
                !s.Steps![i].DidNotRun &&
                s.Steps![i].Keyword == reference.Keyword &&
                s.Steps[i].Text == reference.Text);

            if (!allMatch)
                break;

            prefixLength++;
        }

        return prefixLength;
    }
}
