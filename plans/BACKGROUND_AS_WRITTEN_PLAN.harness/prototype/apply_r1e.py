#!/usr/bin/env python
"""R1, fifth part: the Background is counted over the mapped rows, and one query hint reads both lists (apply_r1e.py <worktree>).

Trying R1 with #223's prototype on top showed the count belongs where the rows are final. At the hook it has only the adapter's list, which on that
plan holds nested frames ahead of their parents and no hook rows; in ToFeatures it has the list the report will show, so the two plans meet in one
condition (a row no framework step opened) and not in a second copy of the nesting rule. The document's texts ride on the scenario info instead of a count.

The hint is `query failures`'s "(no step is marked failed ...)": it asked whether Steps held anything, and R1 can leave only Background steps there.
"""
import sys

root = sys.argv[1]


def edit(path, *pairs):
    full = root + "/" + path
    text = open(full, encoding="utf-8", newline="").read()
    for old, new in pairs:
        assert text.count(old) == 1, old[:90]
        text = text.replace(old, new)
    open(full, "w", encoding="utf-8", newline="").write(text)


edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollScenarioInfo.cs",
    (
        """    /// <summary>
    /// How many of <see cref="Steps"/>, from the first, came from a <c>Background:</c> written in the feature or the
    /// scenario's rule (#222): what the Gherkin document says, as far as the steps that ran agree with it.
    /// </summary>
    internal int BackgroundStepCount { get; init; }
""",
        """    /// <summary>
    /// The texts of the steps a <c>Background:</c> written in the feature or the scenario's rule gives this scenario, in the
    /// order they run (#222). Empty when none is written, when the Gherkin document is out of reach, and for a scenario
    /// built by hand.
    /// </summary>
    internal IReadOnlyList<string> WrittenBackground { get; init; } = [];
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
    (
        """        var writtenBackground = ExamplesBlockResolver.ResolveBackgroundSteps(_featureContext.FeatureInfo, _scenarioContext.ScenarioInfo);

""",
        "",
    ),
    (
        """            BackgroundStepCount = CountBackgroundSteps(steps, writtenBackground),
""",
        """            WrittenBackground = ExamplesBlockResolver.ResolveBackgroundSteps(_featureContext.FeatureInfo, _scenarioContext.ScenarioInfo),
""",
    ),
    (
        """
    /// <summary>
    /// How many steps, from the first, are the Background the document writes: a failed step ends a scenario early, and a
    /// step that disagrees with the document is never counted as it.
    /// </summary>
    internal static int CountBackgroundSteps(IReadOnlyList<ReqNRollStepInfo> steps, IReadOnlyList<string> writtenBackground)
    {
        var count = 0;
        while (count < steps.Count && count < writtenBackground.Count
               && string.Equals(steps[count].Text, writtenBackground[count], StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
""",
        "",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
    (
        """                            var backgroundCount = Math.Min(x.BackgroundStepCount, ran.Length);
""",
        """                            var backgroundCount = CountBackgroundSteps(ran, x.WrittenBackground);
""",
    ),
    (
        """    /// <summary>
    /// The steps a step definition ran, as the hooks saw them.""",
        """    /// <summary>
    /// How many rows, from the first, are the Background the document writes: a failed step ends a scenario early, and a
    /// row that disagrees with the document is never counted as it.
    /// </summary>
    internal static int CountBackgroundSteps(IReadOnlyList<ScenarioStep> ran, IReadOnlyList<string> writtenBackground)
    {
        var count = 0;
        while (count < ran.Count && count < writtenBackground.Count
               && string.Equals(ran[count].Text, writtenBackground[count], StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    /// <summary>
    /// The steps a step definition ran, as the hooks saw them.""",
    ),
)

edit(
    "src/Kronikol/Query/QueryCommand.Narrative.cs",
    (
        """            if (failingSteps.Length == 0 && scenario.Steps.Count > 0)
""",
        """            if (failingSteps.Length == 0 && scenario.BackgroundSteps.Count + scenario.Steps.Count > 0)
""",
    ),
)

edit(
    "tests/Kronikol.Tests/ReqNRoll/BackgroundAsWrittenTests.cs",
    (
        """        var steps = ran.Select(text => new ReqNRollStepInfo("Given", text)).ToArray();

        Assert.Equal(expected, ReqNRollTrackingHooks.CountBackgroundSteps(steps, written));
""",
        """        var steps = ran.Select(text => new ScenarioStep { Keyword = "Given", Text = text }).ToArray();

        Assert.Equal(expected, ScenarioInfoEnumerableExtensions.CountBackgroundSteps(steps, written));
""",
    ),
    (
        """        BackgroundStepCount = backgroundCount,
""",
        """        WrittenBackground = steps.Take(backgroundCount).Select(s => s.Text).ToArray(),
""",
    ),
    (
        """        var scenario = new[] { Collected("a", "A", 5, new ReqNRollStepInfo("Given", "only one ran")) }.ToFeatures()[0].Scenarios[0];
""",
        """        var ran = Collected("a", "A", 1, new ReqNRollStepInfo("Given", "only one ran")) with { WrittenBackground = ["only one ran", "the second never ran"] };

        var scenario = new[] { ran }.ToFeatures()[0].Scenarios[0];
""",
    ),
)
edit(
    "tests/Kronikol.Tests/ReqNRoll/BackgroundAsWrittenTests.cs",
    (
        """    [Fact]
    public void A_separate_background_section_holding_a_failed_step_starts_open()
""",
        """    [Fact]
    public void A_scenario_a_list_holds_twice_is_placed_once()
    {
        var folder = Directory.CreateTempSubdirectory("krk-222-").FullName;
        var file = Path.Combine(folder, "shot.png");
        File.WriteAllText(file, "x");
        var scenario = new Scenario { Id = "a", DisplayName = "a", Attachments = [new FileAttachment("shot.png", file)] };

        ReportGenerator.CopyAttachmentsToReportsFolder([new Feature { DisplayName = "Orders", Scenarios = [scenario, scenario] }], Path.Combine(folder, "Reports"));

        Assert.Equal("attachments/shot.png", scenario.Attachments![0].RelativePath);
    }

    [Fact]
    public void A_separate_background_section_holding_a_failed_step_starts_open()
""",
    ),
)
print("ok")
