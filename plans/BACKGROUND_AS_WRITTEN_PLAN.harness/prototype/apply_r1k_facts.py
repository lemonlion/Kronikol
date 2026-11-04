#!/usr/bin/env python
"""The fifth review's facts (apply_r1k_facts.py <checkout>): what apply_r1k_review5.py changed, and each break that review found no fact catching."""
import sys

root = sys.argv[1]
path = root + "/tests/Kronikol.Tests/ReqNRoll/BackgroundAsWrittenTests.cs"
t = open(path, encoding="utf-8", newline="").read()


def sub(old, new):
    global t
    assert t.count(old) == 1, (t.count(old), old[:90])
    t = t.replace(old, new)


def between(start, end, new):
    global t
    assert t.count(start) == 1, start[:90]
    i = t.index(start)
    j = t.index(end, i + len(start))
    t = t[:i] + new + t[j:]


# Rows a count must not take.
sub(
    r"""    [Fact]
    public void A_count_past_the_steps_that_ran_takes_them_all()
""",
    r"""    [Fact]
    public void A_step_that_differs_from_the_document_only_in_case_is_not_counted()
    {
        var ran = Collected("a", "A", 1, new ReqNRollStepInfo("Given", "A customer is signed in")) with { WrittenBackground = ["a customer is signed in"] };

        var scenario = new[] { ran }.ToFeatures()[0].Scenarios[0];

        Assert.Null(scenario.BackgroundSteps);
        Assert.Single(scenario.Steps!);
    }

    [Fact]
    public void A_count_past_the_steps_that_ran_takes_them_all()
""",
)

# A nested step is paired by what opened it.
sub(
    r"""        var inner = new ReqNRollStepInfo("Given", "the basket is listed", ScenarioExecutionStatus.TestError, null, "|sku|\n|APPLE|", "a doc string");
""",
    r"""        var inner = new ReqNRollStepInfo("Given", "the basket is listed", ScenarioExecutionStatus.TestError, null, "|sku|\n|APPLE|", "a doc string") { Nested = new List<ReqNRollStepInfo>() };
""",
)
sub(
    r"""                Kronikol.Tracking.StepCollector.StartStep(id, "Given", inner.Text, null, null);
                Kronikol.Tracking.StepCollector.CompleteStep(id, false);
            }),
""",
    r"""                Kronikol.Tracking.StepCollector.StartStep(id, "Given", inner.Text, null, null, null, null, inner.Nested);
                Kronikol.Tracking.StepCollector.CompleteStep(id, false);
            }),
""",
)
between(
    "public class StepsUnderAWovenStepTests\n{",
    "public class SharedStepsTests\n",
    r"""public class StepsUnderAWovenStepTests
{
    // As the hooks do: a list of its own for each step, which is also what StepCollector is told opened it.
    private static ReqNRollStepInfo Opened(string text, ScenarioExecutionStatus status = ScenarioExecutionStatus.OK, string? table = null, string? docString = null,
        InlineParamCapture[]? inlineParams = null, ReqNRollStepInfo[]? nested = null) =>
        new("Given", text, status, null, table, docString, inlineParams) { Nested = new List<ReqNRollStepInfo>(nested ?? []) };

    private static void Start(string id, ReqNRollStepInfo step) =>
        StepCollector.StartStep(id, step.Keyword, step.Text, null, null, null, null, step.Nested);

    private static ScenarioStep[] SubStepsOf(string id, ReqNRollStepInfo outer)
    {
        var info = new ReqNRollScenarioInfo
        {
            ScenarioId = id,
            ScenarioTitle = "A",
            FeatureTitle = "Orders",
            ScenarioTags = [],
            CombinedTags = [],
            Steps = [outer],
            WrittenBackground = [],
            ExecutionStatus = outer.Status,
        };

        return new[] { info }.ToFeatures()[0].Scenarios[0].Steps![0].SubSteps!;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Given")]
    public void A_nested_step_is_not_taken_for_a_woven_step_that_reads_the_same(string? wovenKeyword)
    {
        var id = nameof(A_nested_step_is_not_taken_for_a_woven_step_that_reads_the_same) + wovenKeyword;
        var inner = Opened("the namesake step", ScenarioExecutionStatus.TestError, "|sku|\n|APPLE|", "a doc string");
        var outer = Opened("a composite step", ScenarioExecutionStatus.TestError, nested: [inner]);

        Start(id, outer);
        StepCollector.StartStep(id, wovenKeyword, inner.Text, null, null);
        StepCollector.CompleteStep(id, true);
        Start(id, inner);
        StepCollector.CompleteStep(id, false);
        StepCollector.CompleteStep(id, false);

        var subSteps = SubStepsOf(id, outer);

        Assert.Equal(ExecutionResult.Passed, subSteps[0].Status);
        Assert.Null(subSteps[0].Parameters);
        Assert.Null(subSteps[0].DocString);
        Assert.Equal(ExecutionResult.Failed, subSteps[1].Status);
        Assert.Equal("table", Assert.Single(subSteps[1].Parameters!).Name);
        Assert.Equal("a doc string", subSteps[1].DocString);
    }

    [Fact]
    public void A_woven_step_that_failed_stays_failed_beside_a_nested_step_that_reads_the_same()
    {
        var id = nameof(A_woven_step_that_failed_stays_failed_beside_a_nested_step_that_reads_the_same);
        var inner = Opened("the namesake step");
        var outer = Opened("a composite step", nested: [inner]);

        Start(id, outer);
        StepCollector.StartStep(id, "Given", inner.Text, null, null);
        StepCollector.CompleteStep(id, false);
        Start(id, inner);
        StepCollector.CompleteStep(id, true);
        StepCollector.CompleteStep(id, true);

        var subSteps = SubStepsOf(id, outer);

        Assert.Equal(ExecutionResult.Failed, subSteps[0].Status);
        Assert.Equal(ExecutionResult.Passed, subSteps[1].Status);
    }

    [Fact]
    public void A_step_found_under_a_woven_step_keeps_its_arguments_as_does_the_one_after_it()
    {
        var id = nameof(A_step_found_under_a_woven_step_keeps_its_arguments_as_does_the_one_after_it);
        var inHelper = Opened("the basket is listed", table: "|sku|\n|APPLE|");
        var afterHelper = Opened("a note says", docString: "a doc string");
        var outer = Opened("a composite step", nested: [inHelper, afterHelper]);

        Start(id, outer);
        StepCollector.StartStep(id, "Given", "a woven helper", null, null);
        Start(id, inHelper);
        StepCollector.CompleteStep(id, true);
        StepCollector.CompleteStep(id, true);
        Start(id, afterHelper);
        StepCollector.CompleteStep(id, true);
        StepCollector.CompleteStep(id, true);

        var subSteps = SubStepsOf(id, outer);

        Assert.Equal("table", Assert.Single(subSteps[0].SubSteps![0].Parameters!).Name);
        Assert.Equal("a doc string", subSteps[1].DocString);
    }

    [Fact]
    public void A_nested_steps_own_nested_steps_keep_their_arguments()
    {
        var id = nameof(A_nested_steps_own_nested_steps_keep_their_arguments);
        var leaf = Opened("the card is read", table: "|sku|\n|APPLE|");
        var middle = Opened("the customer pays", nested: [leaf]);
        var outer = Opened("a composite step", nested: [middle]);

        Start(id, outer);
        Start(id, middle);
        Start(id, leaf);
        StepCollector.CompleteStep(id, true);
        StepCollector.CompleteStep(id, true);
        StepCollector.CompleteStep(id, true);

        Assert.Equal("table", Assert.Single(Assert.Single(SubStepsOf(id, outer)[0].SubSteps!).Parameters!).Name);
    }

    [Fact]
    public void A_nested_step_keeps_its_inline_values()
    {
        var id = nameof(A_nested_step_keeps_its_inline_values);
        var inner = Opened("the basket holds APPLE", inlineParams: [new InlineParamCapture(17, 5, "APPLE", "sku")]);
        var outer = Opened("a composite step", nested: [inner]);

        Start(id, outer);
        Start(id, inner);
        StepCollector.CompleteStep(id, true);
        StepCollector.CompleteStep(id, true);

        Assert.NotNull(Assert.Single(SubStepsOf(id, outer)).TextSegments);
    }

    [Fact]
    public void A_step_a_step_ran_reads_what_the_hooks_saw_not_what_the_collector_was_told()
    {
        // The collector only hears passed or not, so a pending step is failed there and skipped here.
        var id = nameof(A_step_a_step_ran_reads_what_the_hooks_saw_not_what_the_collector_was_told);
        var inner = Opened("a pending step", ScenarioExecutionStatus.StepDefinitionPending);
        var outer = Opened("a composite step", ScenarioExecutionStatus.StepDefinitionPending, nested: [inner]);

        Start(id, outer);
        Start(id, inner);
        StepCollector.CompleteStep(id, false);
        StepCollector.CompleteStep(id, false);

        Assert.Equal(ExecutionResult.Skipped, Assert.Single(SubStepsOf(id, outer)).Status);
    }
}

""",
)

# A record says what it says; the options say what they say.
between(
    "    [Fact]\n    public void A_toggle_record_built_by_hand_says_what_it_says() =>",
    "    [Fact]\n    public void A_failed_step_whose_sub_steps_passed_starts_open()",
    r"""    [Fact]
    public void A_record_handed_to_the_generator_says_what_it_says()
    {
        foreach (var toggles in new ResolvedToggleDefaults?[] { null, new(), ResolvedToggleDefaults.BuiltIn, ResolvedToggleDefaults.BuiltIn with { HeadersShown = true }, new() { BackgroundStepsOpen = false } })
            Assert.Contains("<details class=\"scenario-background\">", WithAFailedBackgroundStep(toggles));
        Assert.Contains("<details class=\"scenario-background\" open>", WithAFailedBackgroundStep(new ResolvedToggleDefaults { BackgroundStepsOpen = true }));
    }

    [Fact]
    public void The_built_in_toggles_are_a_record_nobody_configured() =>
        Assert.Equal(new ResolvedToggleDefaults(), ResolvedToggleDefaults.BuiltIn);

    private static string WithAFailedBackgroundStep(ResolvedToggleDefaults? toggles)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "a", DisplayName = "a", Result = ExecutionResult.Failed,
                        BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "a customer is signed in", Status = ExecutionResult.Failed }],
                    },
                ],
            },
        };
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("a", "", "@startuml\n@enduml")], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, $"Bg222_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            separateBackgroundSteps: true, toggleDefaults: toggles);

        return ReportHtml.Read(path);
    }

    private static string Report(ScenarioStep step)
    {
        var features = new[]
        {
            new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = "a", DisplayName = "a", Result = ExecutionResult.Failed, Steps = [step] }] },
        };
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("a", "", "@startuml\n@enduml")], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, $"Bg222_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);

        return ReportHtml.Read(path);
    }

    [Fact]
    public void A_failed_step_whose_many_sub_steps_passed_has_them_drawn_and_not_held()
    {
        var html = Report(new ScenarioStep
        {
            Keyword = "When", Text = "the customer pays", Status = ExecutionResult.Failed,
            SubSteps = Enumerable.Range(1, 30).Select(i => new ScenarioStep { Keyword = "Given", Text = $"the card is read {i} " + new string('x', 100), Status = ExecutionResult.Passed }).ToArray(),
        });

        Assert.DoesNotContain("data-steps=", html, StringComparison.Ordinal);
        Assert.Contains("the card is read 30 ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_step_with_a_failed_step_two_levels_down_starts_open()
    {
        var html = Report(new ScenarioStep
        {
            Keyword = "When", Text = "the customer pays", Status = ExecutionResult.Passed,
            SubSteps =
            [
                new ScenarioStep
                {
                    Keyword = "Given", Text = "the card is read", Status = ExecutionResult.Passed,
                    SubSteps = [new ScenarioStep { Keyword = "Given", Text = "the chip answers", Status = ExecutionResult.Failed }],
                },
            ],
        });

        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "<details class=\"step step-collapsible\" open>").Count);
    }

""",
)
sub(
    r"""/// <summary>#222: what the fix leaves as it was, for whoever reads the public lists or builds a scenario by hand.</summary>
""",
    r"""/// <summary>#222: what a report made from the options does with a Background section holding a failed step.</summary>
[Collection("DiagramsFetcher")]
public class BackgroundSectionFromTheOptionsTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-background-section").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Report(ReportConfigurationOptions options, params Scenario[] scenarios)
    {
        options.ReportsFolderPath = _dir;
        options.SeparateBackgroundSteps = true;
        options.GenerateSpecificationsReport = false;
        options.GenerateSpecificationsData = false;
        DefaultDiagramsFetcher.Reset();
        ReportGenerator.CreateStandardReportsWithDiagrams([new Feature { DisplayName = "Orders", Scenarios = scenarios }], Start, Start.AddSeconds(30), options);
        DefaultDiagramsFetcher.Reset();
        return ReportHtml.Read(Path.Combine(_dir, "TestRunReport.html"));
    }

    private static Scenario Holding(ScenarioStep background) => new()
    {
        Id = "a222section", DisplayName = "a", Result = ExecutionResult.Failed, BackgroundSteps = [background],
    };

    private static Scenario Row(string id, string row, ExecutionResult background) => new()
    {
        Id = id,
        DisplayName = "Pay",
        OutlineId = "Pay",
        Result = background,
        ExampleValues = new Dictionary<string, string> { ["row"] = row },
        ExampleRawValues = new Dictionary<string, object?> { ["row"] = row },
        BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "a customer is signed in", Status = background }],
    };

    private static readonly ScenarioStep FailedStep = new() { Keyword = "Given", Text = "a customer is signed in", Status = ExecutionResult.Failed };

    [Fact]
    public void Left_unset_a_section_holding_a_failed_step_starts_open() =>
        Assert.Contains("<details class=\"scenario-background\" data-holds-failure open>", Report(new ReportConfigurationOptions(), Holding(FailedStep)));

    [Fact]
    public void Left_unset_a_section_whose_step_has_a_failed_sub_step_starts_open() =>
        Assert.Contains("<details class=\"scenario-background\" data-holds-failure open>", Report(new ReportConfigurationOptions(), Holding(new ScenarioStep
        {
            Keyword = "Given", Text = "a composite step", Status = ExecutionResult.Passed,
            SubSteps = [new ScenarioStep { Keyword = "Given", Text = "the inner step", Status = ExecutionResult.Failed }],
        })));

    [Fact]
    public void Set_to_false_a_section_holding_a_failed_step_stays_closed()
    {
        var html = Report(new ReportConfigurationOptions { TestRunReportToggleDefaults = { BackgroundStepsOpen = false } }, Holding(FailedStep));

        Assert.Contains("<details class=\"scenario-background\">", html);
        Assert.DoesNotContain("<details class=\"scenario-background\" data-holds-failure", html);
    }

    [Fact]
    public void Set_to_true_a_section_holding_a_failed_step_is_open_with_no_hold() =>
        Assert.Contains("<details class=\"scenario-background\" open>", Report(new ReportConfigurationOptions { TestRunReportToggleDefaults = { BackgroundStepsOpen = true } }, Holding(FailedStep)));

    [Fact]
    public void Left_unset_a_failing_rows_section_starts_open_and_a_passing_rows_does_not()
    {
        var html = Report(new ReportConfigurationOptions(), Row("a222rowa", "passes", ExecutionResult.Passed), Row("a222rowb", "fails", ExecutionResult.Failed));

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<details class=\"scenario-background\" data-holds-failure open>"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<details class=\"scenario-background\">"));
    }

    [Fact]
    public void Set_to_false_a_failing_rows_section_stays_closed() =>
        Assert.DoesNotContain("<details class=\"scenario-background\" data-holds-failure", Report(new ReportConfigurationOptions { TestRunReportToggleDefaults = { BackgroundStepsOpen = false } },
            Row("a222rowa", "passes", ExecutionResult.Passed), Row("a222rowb", "fails", ExecutionResult.Failed)));

    [Theory]
    [InlineData(null, null, false, true)]
    [InlineData(null, null, true, true)]
    [InlineData(false, null, false, false)]
    [InlineData(false, null, true, false)]
    [InlineData(null, false, false, true)]
    [InlineData(null, false, true, false)]
    [InlineData(true, null, false, false)]
    [InlineData(true, false, true, false)]
    public void Only_a_section_left_unset_opens_for_a_failure(bool? testRun, bool? specifications, bool forSpecifications, bool expected)
    {
        var options = new ReportConfigurationOptions
        {
            TestRunReportToggleDefaults = { BackgroundStepsOpen = testRun },
            SpecificationsToggleDefaults = { BackgroundStepsOpen = specifications },
        };

        Assert.Equal(expected, ReportToggleDefaultsResolver.BackgroundOpensOnFailure(options, forSpecifications));
    }
}

/// <summary>#222: what the fix leaves as it was, for whoever reads the public lists or builds a scenario by hand.</summary>
""",
)

# The rest of what the hooks leave: the collector always sees a nested step now, and a mixed feature is not guessed at.
between(
    "    [Fact]\n    public void A_step_the_collector_never_saw_is_listed_under_the_step_that_ran_it()",
    '[Collection("DiagramsFetcher")]\npublic class QueryOverBackgroundStepsTests',
    r"""    [Fact]
    public void A_feature_any_scenario_of_which_the_hooks_saw_is_not_guessed_at()
    {
        var first = ByHand("a", "A first", new ReqNRollStepInfo("Given", "a customer is signed in"), new ReqNRollStepInfo("Then", "the order is confirmed")) with { WrittenBackground = [] };
        var second = ByHand("b", "B second", new ReqNRollStepInfo("Given", "a customer is signed in"), new ReqNRollStepInfo("Then", "a receipt is sent"));

        var scenarios = new[] { first, second }.ToFeatures()[0].Scenarios;

        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
    }
}

""",
)
open(path, "w", encoding="utf-8", newline="").write(t)

e2e = root + "/tests/Kronikol.Tests.EndToEnd/BackgroundSectionRowSwitchTests.cs"
open(e2e, "w", encoding="utf-8", newline="").write(
    r'''using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// #222: what a row switch does with SeparateBackgroundSteps, in a report made from the options: what the reader left carries
/// over, past a row that lacks that section or holds a failure there, and a section or composite holding a failure keeps its own.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class BackgroundSectionRowSwitchTests : PlaywrightTestBase
{
    public BackgroundSectionRowSwitchTests(PlaywrightFixture fixture) : base(fixture) { }

    private static Scenario Row(string id, string row, ExecutionResult result, ScenarioStep[]? background, ScenarioStep[]? steps) => new()
    {
        Id = id,
        DisplayName = "Pay",
        OutlineId = "Pay",
        Result = result,
        ExampleValues = new Dictionary<string, string> { ["row"] = row },
        ExampleRawValues = new Dictionary<string, object?> { ["row"] = row },
        BackgroundSteps = background,
        Steps = steps,
        ErrorMessage = result == ExecutionResult.Failed ? "it failed" : null,
    };

    private static ScenarioStep[] SignedIn(ExecutionResult status) => [new ScenarioStep { Keyword = "Given", Text = "a customer is signed in", Status = status }];

    private static ScenarioStep[] Pays() => [new ScenarioStep { Keyword = "When", Text = "the customer pays", Status = ExecutionResult.Passed }];

    private static ScenarioStep[] Composite(ExecutionResult status, string inner = "the card is read") =>
    [
        new ScenarioStep
        {
            Keyword = "When", Text = "the customer pays", Status = status,
            SubSteps = [new ScenarioStep { Keyword = "Given", Text = inner, Status = ExecutionResult.Passed }],
        },
    ];

    // a passes, b fails in its Background, c never reaches its steps, d passes
    private static Scenario[] Pay() =>
    [
        Row("a222a", "a passes", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays()),
        Row("a222b", "b fails", ExecutionResult.Failed, SignedIn(ExecutionResult.Failed), null),
        Row("a222c", "c never starts", ExecutionResult.Failed, null, null),
        Row("a222d", "d passes", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays()),
    ];

    private async Task OpenReport(string name, Scenario[] rows, ReportConfigurationOptions? options = null)
    {
        options ??= new ReportConfigurationOptions();
        options.ReportsFolderPath = Path.Combine(TempDir, name);
        options.SeparateBackgroundSteps = true;
        options.GenerateSpecificationsReport = false;
        options.GenerateSpecificationsData = false;
        lock (ReportTestHelper.WholePipeline)
        {
            DefaultDiagramsFetcher.Reset();
            ReportGenerator.CreateStandardReportsWithDiagrams([new Feature { DisplayName = "Orders", Scenarios = rows }], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
            DefaultDiagramsFetcher.Reset();
        }
        await Page.GotoReportAsync(new Uri(Path.Combine(options.ReportsFolderPath, "TestRunReport.html")).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();
    }

    private async Task SelectRow(int row) => await Page.Locator("details.scenario tr[data-row-idx]").Nth(row).ClickAsync();

    private async Task<bool> IsOpen(string selector) => await Page.EvaluateAsync<bool>(
        """
        selector => {
            const panel = Array.from(document.querySelectorAll('[id*="-detail-"]')).find(p => p.style.display !== 'none');
            return panel.querySelector(selector).open;
        }
        """,
        selector);

    private async Task SetOpen(string selector, bool open) => await Page.EvaluateAsync(
        """
        ([selector, open]) => {
            const panel = Array.from(document.querySelectorAll('[id*="-detail-"]')).find(p => p.style.display !== 'none');
            panel.querySelector(selector).open = open;
        }
        """,
        new object[] { selector, open });

    [Fact]
    public async Task A_failing_rows_section_is_open_and_the_row_left_keeps_its_own_state()
    {
        await OpenReport("RowSwitchFailing", Pay());

        Assert.False(await IsOpen("details.scenario-background"));
        await SelectRow(1);
        Assert.True(await IsOpen("details.scenario-background"));
        await SelectRow(0);
        Assert.False(await IsOpen("details.scenario-background"));
    }

    [Fact]
    public async Task A_failing_rows_section_the_reader_closed_stays_closed_when_they_come_back()
    {
        await OpenReport("RowSwitchReaderClosed", Pay());

        await SelectRow(1);
        Assert.True(await IsOpen("details.scenario-background"));
        await SetOpen("details.scenario-background", false);
        await SelectRow(0);
        await SelectRow(1);

        Assert.False(await IsOpen("details.scenario-background"));
    }

    [Fact]
    public async Task A_row_that_never_reached_its_steps_keeps_its_failure_result_open()
    {
        await OpenReport("RowSwitchFailureResult", Pay());

        await SelectRow(2);

        Assert.True(await IsOpen("details.failure-result"));
    }

    [Fact]
    public async Task A_section_the_reader_opened_stays_open_past_a_failing_row_and_back()
    {
        await OpenReport("RowSwitchReaderOpened", Pay());

        await SetOpen("details.scenario-background", true);
        await SelectRow(1);
        await SelectRow(3);
        Assert.True(await IsOpen("details.scenario-background"));
        await SelectRow(0);
        Assert.True(await IsOpen("details.scenario-background"));
    }

    [Fact]
    public async Task A_section_the_reader_closed_stays_closed_past_a_row_without_it()
    {
        await OpenReport("RowSwitchPastARowWithoutIt", Pay());

        await SetOpen("details.scenario-steps", false);
        await SelectRow(2);
        await SelectRow(3);

        Assert.False(await IsOpen("details.scenario-steps"));
    }

    [Fact]
    public async Task A_failure_result_the_reader_closed_stays_closed_on_the_next_row_that_has_one()
    {
        await OpenReport("RowSwitchFailureResultClosed",
        [
            Row("a222a", "a passes", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays()),
            Row("a222b", "b never starts", ExecutionResult.Failed, null, null),
            Row("a222c", "c never starts", ExecutionResult.Failed, null, null),
        ]);

        await SelectRow(1);
        await SetOpen("details.failure-result", false);
        await SelectRow(2);

        Assert.False(await IsOpen("details.failure-result"));
    }

    [Fact]
    public async Task A_section_set_to_stay_closed_holds_nothing_on_a_failing_row()
    {
        await OpenReport("RowSwitchSetClosed", Pay(), new ReportConfigurationOptions { TestRunReportToggleDefaults = { BackgroundStepsOpen = false } });

        await SelectRow(1);
        Assert.False(await IsOpen("details.scenario-background"));
        await SetOpen("details.scenario-background", true);
        await SelectRow(0);
        Assert.True(await IsOpen("details.scenario-background"));
    }

    [Fact]
    public async Task A_failed_composite_keeps_its_state_on_its_own_row()
    {
        await OpenReport("RowSwitchComposite",
        [
            Row("a222a", "a fails", ExecutionResult.Failed, null, Composite(ExecutionResult.Failed)),
            Row("a222b", "b passes", ExecutionResult.Passed, null, Composite(ExecutionResult.Passed)),
            Row("a222c", "c passes", ExecutionResult.Passed, null, Composite(ExecutionResult.Passed)),
        ]);

        Assert.True(await IsOpen("details.step-collapsible"));
        await SelectRow(1);
        Assert.False(await IsOpen("details.step-collapsible"));
        await SelectRow(2);
        Assert.False(await IsOpen("details.step-collapsible"));
        await SelectRow(0);
        Assert.True(await IsOpen("details.step-collapsible"));
    }

    [Fact]
    public async Task A_composite_the_reader_opened_stays_open_past_a_failed_one()
    {
        await OpenReport("RowSwitchCompositeOpened",
        [
            Row("a222a", "a passes", ExecutionResult.Passed, null, Composite(ExecutionResult.Passed)),
            Row("a222b", "b fails", ExecutionResult.Failed, null, Composite(ExecutionResult.Failed)),
            Row("a222c", "c passes", ExecutionResult.Passed, null, Composite(ExecutionResult.Passed)),
        ]);

        await SetOpen("details.step-collapsible", true);
        await SelectRow(1);
        Assert.True(await IsOpen("details.step-collapsible"));
        await SetOpen("details.step-collapsible", false);
        await SelectRow(2);

        Assert.True(await IsOpen("details.step-collapsible"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_single_search_match_shows_in_its_row_when_the_first_row_holds_a_failure(bool readerMovedFirst)
    {
        await OpenReport("RowSwitchSearch" + readerMovedFirst,
        [
            Row("a222a", "a fails", ExecutionResult.Failed, SignedIn(ExecutionResult.Failed), null),
            Row("a222b", "b passes", ExecutionResult.Passed, Composite(ExecutionResult.Passed, "zebraword"), Pays()),
            Row("a222c", "c passes", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays()),
        ]);

        if (readerMovedFirst)
        {
            await SelectRow(2);
            await SelectRow(0);
        }
        await FillSearchBar("zebraword");
        await Page.WaitForFunctionAsync(
            "() => { const r = document.querySelector('details.scenario tr.row-active'); return r && r.getAttribute('data-row-idx') === '1'; }",
            null, new PageWaitForFunctionOptions { PollingInterval = 200 });

        Assert.True(await IsOpen("details.scenario-background"));
    }

    [Fact]
    public async Task A_click_on_the_row_already_chosen_changes_nothing()
    {
        await OpenReport("RowSwitchSameRow", Pay());

        await SelectRow(3);
        await SelectRow(0);
        await SetOpen("details.scenario-background", true);
        await SelectRow(0);

        Assert.True(await IsOpen("details.scenario-background"));
    }

    [Fact]
    public async Task A_details_of_any_class_a_suites_script_adds_does_not_stop_a_switch()
    {
        await OpenReport("RowSwitchAnyClass", Pay());

        await Page.EvaluateAsync(
            """
            () => document.querySelectorAll('[id*="-detail-"]').forEach(panel => {
                const added = document.createElement('details');
                added.className = 'constructor';
                panel.appendChild(added);
            })
            """);
        await SelectRow(1);

        Assert.EndsWith("-detail-1", await Page.EvaluateAsync<string>(
            """() => Array.from(document.querySelectorAll('[id*="-detail-"]')).find(p => p.style.display !== 'none').id"""));
    }
}
''')
real = root + "/examples/Example.Api/tests/Example.Api.Tests.Integration/Tests/ReqNRollDuplicateStepsTests.cs"
r = open(real, encoding="utf-8", newline="").read()
anchor = '''    [Theory]
    [MemberData(nameof(ReqNRollProjects))]
    public async Task A_failure_is_read_on_the_step_that_failed(string projectName)
'''
assert r.count(anchor) == 1
r = r.replace(anchor, r'''    [Theory]
    [MemberData(nameof(ReqNRollProjects))]
    public async Task A_step_a_step_ran_keeps_its_doc_string(string projectName)
    {
        var result = await TestProjectRunner.RunAsync(projectName);
        Assert.True(result.Success, $"{projectName} failed:\n{result.StandardError}\n{result.StandardOutput}");

        var html = Kronikol.Reports.HtmlReportFile.ToPlain(await File.ReadAllTextAsync(ReportParser.GetReportFiles(result.ReportsFolderPath).TestRunReportHtml!));

        Assert.Contains("<pre class=\"step-docstring\"><code>a doc string</code></pre>", html);
    }

''' + anchor)
open(real, "w", encoding="utf-8", newline="").write(r)
print("facts edited")
