using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class BackgroundStepsDetectorTests
{
    private static Scenario MakeScenario(string id, params (string Keyword, string Text)[] steps) =>
        MakeScenario(id, null, steps);

    private static Scenario MakeScenario(string id, string? rule, params (string Keyword, string Text)[] steps) =>
        new()
        {
            Id = id,
            DisplayName = $"Scenario {id}",
            Result = ExecutionResult.Passed,
            Rule = rule,
            Steps = steps.Select(s => new ScenarioStep
            {
                Keyword = s.Keyword,
                Text = s.Text,
                Status = ExecutionResult.Passed
            }).ToArray()
        };

    // ── Basic prefix detection ──

    [Fact]
    public void Two_scenarios_sharing_common_prefix_skips_extraction_when_remaining_starts_with_When()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("Given", "the system is running"), ("Given", "a user exists"), ("When", "I do X"), ("Then", "Y happens")),
            MakeScenario("s2", ("Given", "the system is running"), ("Given", "a user exists"), ("When", "I do Z"), ("Then", "W happens"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        // Remaining steps start with "When" → no extraction
        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
        Assert.Equal(4, scenarios[0].Steps!.Length);
        Assert.Equal(4, scenarios[1].Steps!.Length);
    }

    [Fact]
    public void No_common_prefix_does_not_extract_background()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("Given", "A"), ("When", "B")),
            MakeScenario("s2", ("Given", "X"), ("When", "Y"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
        Assert.Equal(2, scenarios[0].Steps!.Length);
    }

    [Fact]
    public void Single_scenario_does_not_extract_background()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("Given", "the system is running"), ("When", "I do X"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Equal(2, scenarios[0].Steps!.Length);
    }

    [Fact]
    public void Partial_prefix_skips_extraction_when_remaining_starts_with_Given()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("Given", "A"), ("Given", "B"), ("When", "C")),
            MakeScenario("s2", ("Given", "A"), ("Given", "X"), ("When", "Y"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        // Remaining steps start with "Given" → no extraction
        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
        Assert.Equal(3, scenarios[0].Steps!.Length);
        Assert.Equal(3, scenarios[1].Steps!.Length);
    }

    [Fact]
    public void Same_text_different_keyword_is_not_matched()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("Given", "the system is running"), ("When", "B")),
            MakeScenario("s2", ("When", "the system is running"), ("Then", "C"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        Assert.Null(scenarios[0].BackgroundSteps);
    }

    [Fact]
    public void Unequal_step_counts_skips_extraction_when_remaining_starts_with_When()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("Given", "A"), ("When", "B"), ("Then", "C")),
            MakeScenario("s2", ("Given", "A"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        // s1's remaining starts with "When" → no extraction
        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
        Assert.Equal(3, scenarios[0].Steps!.Length);
        Assert.Single(scenarios[1].Steps!);
    }

    [Fact]
    public void Steps_with_sub_steps_are_preserved_on_background()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("Given", "setup")),
            MakeScenario("s2", ("Given", "setup"))
        };

        // Add sub-steps to the first scenario's first step
        scenarios[0].Steps![0].SubSteps =
        [
            new ScenarioStep { Keyword = "And", Text = "sub1", Status = ExecutionResult.Passed },
            new ScenarioStep { Keyword = "And", Text = "sub2", Status = ExecutionResult.Passed }
        ];

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        Assert.NotNull(scenarios[0].BackgroundSteps);
        Assert.NotNull(scenarios[0].BackgroundSteps![0].SubSteps);
        Assert.Equal(2, scenarios[0].BackgroundSteps[0].SubSteps!.Length);
    }

    // ── Rule-scoped detection ──

    [Fact]
    public void Two_rules_with_different_backgrounds_skip_extraction_when_remaining_starts_with_When()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Rule A", ("Given", "A setup"), ("When", "A does X")),
            MakeScenario("s2", "Rule A", ("Given", "A setup"), ("When", "A does Y")),
            MakeScenario("s3", "Rule B", ("Given", "B setup"), ("When", "B does X")),
            MakeScenario("s4", "Rule B", ("Given", "B setup"), ("When", "B does Y"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        // All rules' remaining steps start with "When" → no extraction
        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[2].BackgroundSteps);
        Assert.Equal(2, scenarios[0].Steps!.Length);
        Assert.Equal(2, scenarios[2].Steps!.Length);
    }

    [Fact]
    public void Rule_with_single_scenario_does_not_extract_background()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Rule A", ("Given", "setup"), ("When", "X")),
            MakeScenario("s2", "Rule A", ("Given", "setup"), ("When", "Y")),
            MakeScenario("s3", "Rule B", ("Given", "setup"), ("When", "Z"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        // Rule A: remaining starts with "When" → no extraction
        Assert.Null(scenarios[0].BackgroundSteps);

        // Rule B has 1 scenario → should NOT extract
        Assert.Null(scenarios[2].BackgroundSteps);
        Assert.Equal(2, scenarios[2].Steps!.Length);
    }

    [Fact]
    public void Mixed_scenarios_outside_and_inside_rules_skip_extraction_when_remaining_starts_with_When()
    {
        var scenarios = new[]
        {
            // Outside any rule
            MakeScenario("s1", null, ("Given", "global setup"), ("When", "X")),
            MakeScenario("s2", null, ("Given", "global setup"), ("When", "Y")),
            // Inside a rule
            MakeScenario("s3", "Rule A", ("Given", "rule setup"), ("When", "Z")),
            MakeScenario("s4", "Rule A", ("Given", "rule setup"), ("When", "W"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        // All groups' remaining starts with "When" → no extraction
        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[2].BackgroundSteps);
        Assert.Equal(2, scenarios[0].Steps!.Length);
        Assert.Equal(2, scenarios[2].Steps!.Length);
    }

    [Fact]
    public void Empty_scenarios_array_does_not_throw()
    {
        BackgroundStepsDetector.DetectAndExtract([]);
    }

    [Fact]
    public void Scenarios_with_null_steps_are_skipped()
    {
        var scenarios = new[]
        {
            new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed, Steps = null },
            new Scenario { Id = "s2", DisplayName = "S2", Result = ExecutionResult.Passed, Steps = null }
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        Assert.Null(scenarios[0].BackgroundSteps);
    }

    // ── Scenarios starting with And/When skip background detection ──

    [Fact]
    public void Scenarios_starting_with_And_do_not_extract_background()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("And", "something shared"), ("And", "do X"), ("Then", "Y happens")),
            MakeScenario("s2", ("And", "something shared"), ("And", "do Z"), ("Then", "W happens"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
        Assert.Equal(3, scenarios[0].Steps!.Length);
    }

    [Fact]
    public void Scenarios_starting_with_When_do_not_extract_background()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("When", "I do shared action"), ("Then", "X happens")),
            MakeScenario("s2", ("When", "I do shared action"), ("Then", "Y happens"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
        Assert.Equal(2, scenarios[0].Steps!.Length);
    }

    [Fact]
    public void One_scenario_starting_with_When_among_Given_starters_skips_background()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("Given", "shared setup"), ("When", "do X"), ("Then", "Y")),
            MakeScenario("s2", ("Given", "shared setup"), ("When", "do Z"), ("Then", "W")),
            MakeScenario("s3", ("When", "shared setup"), ("Then", "Q"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
        Assert.Null(scenarios[2].BackgroundSteps);
    }

    [Fact]
    public void Scenario_with_common_Given_skips_extraction_when_remaining_starts_with_When()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("Given", "shared"), ("When", "X"), ("Then", "Y")),
            MakeScenario("s2", ("Given", "shared"), ("When", "Z"), ("Then", "W"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        // Remaining starts with "When" → no extraction
        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
        Assert.Equal(3, scenarios[0].Steps!.Length);
    }

    // ── Remaining-step keyword guards ──

    [Fact]
    public void Extracts_background_when_remaining_starts_with_Then()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("Given", "A"), ("Given", "B"), ("Then", "X")),
            MakeScenario("s2", ("Given", "A"), ("Given", "B"), ("Then", "Y"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        Assert.NotNull(scenarios[0].BackgroundSteps);
        Assert.Equal(2, scenarios[0].BackgroundSteps!.Length);
        Assert.Equal("A", scenarios[0].BackgroundSteps[0].Text);
        Assert.Equal("B", scenarios[0].BackgroundSteps[1].Text);
        Assert.Single(scenarios[0].Steps!);
        Assert.Equal("X", scenarios[0].Steps![0].Text);
    }

    [Fact]
    public void Extracts_background_when_remaining_starts_with_But()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("Given", "A"), ("But", "X")),
            MakeScenario("s2", ("Given", "A"), ("But", "Y"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        Assert.NotNull(scenarios[0].BackgroundSteps);
        Assert.Single(scenarios[0].BackgroundSteps!);
        Assert.Equal("A", scenarios[0].BackgroundSteps![0].Text);
        Assert.Single(scenarios[0].Steps!);
    }

    [Fact]
    public void Extracts_background_when_all_steps_are_common()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("Given", "A"), ("Given", "B")),
            MakeScenario("s2", ("Given", "A"), ("Given", "B"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        // All steps are common, no remaining → extraction happens
        Assert.NotNull(scenarios[0].BackgroundSteps);
        Assert.Equal(2, scenarios[0].BackgroundSteps!.Length);
        Assert.Empty(scenarios[0].Steps!);
    }

    [Fact]
    public void Does_not_extract_when_remaining_starts_with_Given()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("Given", "shared"), ("Given", "specific A"), ("Then", "X")),
            MakeScenario("s2", ("Given", "shared"), ("Given", "specific B"), ("Then", "Y"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
        Assert.Equal(3, scenarios[0].Steps!.Length);
    }

    [Fact]
    public void Does_not_extract_when_any_scenario_remaining_starts_with_When()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("Given", "shared"), ("Then", "X")),
            MakeScenario("s2", ("Given", "shared"), ("When", "Y"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        // s2's remaining starts with "When" → no extraction for the group
        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
    }

    [Fact]
    public void Extracts_background_when_one_scenario_has_no_remaining_steps()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", ("Given", "A"), ("Then", "X")),
            MakeScenario("s2", ("Given", "A"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        // s1 remaining starts with "Then", s2 has no remaining → extraction happens
        Assert.NotNull(scenarios[0].BackgroundSteps);
        Assert.Single(scenarios[0].BackgroundSteps!);
        Assert.Single(scenarios[0].Steps!);
        Assert.Empty(scenarios[1].Steps!);
    }

    [Fact]
    public void Rules_with_Then_remaining_extract_while_When_remaining_skip()
    {
        var scenarios = new[]
        {
            // Rule A: remaining starts with "Then" → extraction
            MakeScenario("s1", "Rule A", ("Given", "setup"), ("Then", "X")),
            MakeScenario("s2", "Rule A", ("Given", "setup"), ("Then", "Y")),
            // Rule B: remaining starts with "When" → no extraction
            MakeScenario("s3", "Rule B", ("Given", "setup"), ("When", "Z")),
            MakeScenario("s4", "Rule B", ("Given", "setup"), ("When", "W"))
        };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        // Rule A extracted
        Assert.NotNull(scenarios[0].BackgroundSteps);
        Assert.Equal("setup", scenarios[0].BackgroundSteps![0].Text);
        Assert.Single(scenarios[0].Steps!);

        // Rule B skipped
        Assert.Null(scenarios[2].BackgroundSteps);
        Assert.Equal(2, scenarios[2].Steps!.Length);
    }

    // A consumer that rebuilds a scenario's info keeps its rows' marks and loses the document, so the guess runs over them (Q51, Q52).
    [Fact]
    public void A_row_no_framework_step_opened_is_no_part_of_what_a_feature_shares()
    {
        var a = MakeScenario("a", ("", "Reference data is seeded"), ("Then", "one"));
        var b = MakeScenario("b", ("", "Reference data is seeded"), ("Then", "two"));
        foreach (var scenario in new[] { a, b })
            scenario.Steps![0] = scenario.Steps[0] with { OutsideFrameworkStep = true };

        BackgroundStepsDetector.DetectAndExtract([a, b]);

        Assert.Null(a.BackgroundSteps);
        Assert.Null(b.BackgroundSteps);
    }

    [Fact]
    public void A_row_that_did_not_run_is_no_part_of_what_a_feature_shares()
    {
        var a = MakeScenario("a", ("Given", "a basket"), ("And", "an item"), ("Then", "one"));
        var b = MakeScenario("b", ("Given", "a basket"), ("And", "an item"), ("Then", "two"));
        b.Steps![1] = b.Steps[1] with { DidNotRun = true };

        BackgroundStepsDetector.DetectAndExtract([a, b]);

        Assert.Equal(["a basket"], a.BackgroundSteps!.Select(s => s.Text));
        Assert.Equal(["an item", "one"], a.Steps!.Select(s => s.Text));
        Assert.Equal(["an item", "two"], b.Steps!.Select(s => s.Text));
    }
}
