#!/usr/bin/env python
"""R1, seventh part: what a fresh verifier found against the patch claim and the repository's rule on bugs (apply_r1g.py <worktree>).

Three changes the fix did not need come out, so nothing public behaves differently but for what the fix is about:
  - A step a step ran stays in ReqNRollScenarioCollector.GetAll() and ScenarioContext[StepsCollectionKey], as on 5.28.2 and in #223's prototype. It is marked, and only
    the scenario's own steps become rows. ExampleValueGrouper reads that list again.
  - An explicit ToggleDefaults.BackgroundStepsOpen = false is honoured, as StepsSectionOpen = false is. The page reads a mark on the section, not the failure.
  - A ReqNRollScenarioInfo built by hand, which has no document to read, keeps the guess, with its own copies.
And four faults the plan had left, each small:
  - An inner step that never opened a frame vanished (E39): it is listed under its parent.
  - A step whose suite AfterStep hook threw was not listed, nor a call under it (E35, E41): the frame still open when the scenario ends lists it.
  - A failed step whose sub-steps passed started collapsed: HasAnyFailed reads the step too.
  - A step text the bar escapes or wraps lost its calls' step (E46, E48): StepMarkerMatches compares what the bar draws.
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
    "src/Kronikol.ReqNRoll.Core/ReqNRollStepInfo.cs",
    (
        """    internal IReadOnlyList<ReqNRollStepInfo>? Nested { get; init; }
""",
        """    internal IReadOnlyList<ReqNRollStepInfo>? Nested { get; init; }

    /// <summary>A step definition ran this step, which makes it that step's sub-step and not one of the scenario's own rows.</summary>
    internal bool RanByAStep { get; init; }
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollScenarioInfo.cs",
    (
        """    /// order they run (#222). Empty when none is written, when the Gherkin document is out of reach, and for a scenario
    /// built by hand.
    /// </summary>
    internal IReadOnlyList<string> WrittenBackground { get; init; } = [];
""",
        """    /// order they run (#222). Empty when none is written and when the Gherkin document is out of reach; null for a scenario
    /// built by hand, which never had a document to read.
    /// </summary>
    internal IReadOnlyList<string>? WrittenBackground { get; init; }
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
    (
        """        var opened = new OpenStep(stepStopwatch, InnermostOpenStep);
""",
        """        var opened = new OpenStep(stepStopwatch, InnermostOpenStep, _scenarioContext.StepContext.StepInfo);
""",
    ),
    (
        """        var steps = (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey];
        var tableText = stepContext.StepInfo.Table?.ToString();
        var docString = stepContext.StepInfo.MultilineText;

        // Capture inline parameter positions from binding match
        InlineParamCapture[]? inlineParams = null;
        var bindingMatch = stepContext.StepInfo.BindingMatch;
        if (bindingMatch is { Success: true, Arguments.Length: > 0 })
        {
            var stepText = stepContext.StepInfo.Text;
            var captures = new List<InlineParamCapture>();
            var methodParams = bindingMatch.StepBinding?.Method?.Parameters?.ToArray();
            for (var i = 0; i < bindingMatch.Arguments.Length; i++)
            {
                var arg = bindingMatch.Arguments[i];
                if (arg.StartOffset is not null && arg.Value is string strValue)
                {
                    var offset = arg.StartOffset.Value;
                    var length = strValue.Length;
                    if (offset >= 0 && offset + length <= stepText.Length)
                    {
                        string? name = null;
                        if (methodParams != null && i < methodParams.Length)
                            name = methodParams[i].ParameterName;
                        captures.Add(new InlineParamCapture(offset, length, strValue, name));
                    }
                }
            }
            if (captures.Count > 0)
                inlineParams = captures.ToArray();
        }

        // Pop the StepCollector step so assertion sub-steps are captured
""",
        """        var steps = (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey];

        // Pop the StepCollector step so assertion sub-steps are captured
""",
    ),
    (
        """        var info = new ReqNRollStepInfo(
            stepContext.StepInfo.StepInstance.StepDefinitionKeyword.ToString(),
            stepContext.StepInfo.Text,
            status,
            opened?.Stopwatch.Elapsed,
            tableText,
            docString,
            inlineParams) { Nested = opened?.Nested };

        // A step a step definition ran is that step's sub-step, which StepCollector has made it; it is not one of the
        // scenario's own, whose list and whose Background count run by the feature file.
        var parent = opened is not null ? opened.Parent : InnermostOpenStep;
        if (opened is not null && parent is null)
            _scenarioContext.Remove(ReqNRollConstants.OpenStepKey);
        else if (opened is not null)
            _scenarioContext[ReqNRollConstants.OpenStepKey] = parent!;

        if (parent is not null)
        {
            parent.Nested.Add(info);
            return;
        }

        steps.Add(info);
    }

    private static List<ReqNRollStepInfo> AndWhatTheyRan(IEnumerable<ReqNRollStepInfo> steps) =>
        steps.SelectMany(step => new[] { step }.Concat(AndWhatTheyRan(step.Nested ?? []))).ToList();

    private OpenStep? InnermostOpenStep =>
""",
        """        // A step a step definition ran is that step's sub-step, which StepCollector has made it, so it is no row of the
        // scenario's own, whose list and whose Background count run by the feature file.
        var parent = opened is not null ? opened.Parent : InnermostOpenStep;
        var info = Describe(stepContext.StepInfo, status, opened, parent);
        if (opened is not null && parent is null)
            _scenarioContext.Remove(ReqNRollConstants.OpenStepKey);
        else if (opened is not null)
            _scenarioContext[ReqNRollConstants.OpenStepKey] = parent!;

        parent?.Nested.Add(info);
        steps.Add(info);
    }

    private static ReqNRollStepInfo Describe(StepInfo stepInfo, ScenarioExecutionStatus status, OpenStep? opened, OpenStep? parent)
    {
        // Capture inline parameter positions from binding match
        InlineParamCapture[]? inlineParams = null;
        var bindingMatch = stepInfo.BindingMatch;
        if (bindingMatch is { Success: true, Arguments.Length: > 0 })
        {
            var stepText = stepInfo.Text;
            var captures = new List<InlineParamCapture>();
            var methodParams = bindingMatch.StepBinding?.Method?.Parameters?.ToArray();
            for (var i = 0; i < bindingMatch.Arguments.Length; i++)
            {
                var arg = bindingMatch.Arguments[i];
                if (arg.StartOffset is not null && arg.Value is string strValue)
                {
                    var offset = arg.StartOffset.Value;
                    var length = strValue.Length;
                    if (offset >= 0 && offset + length <= stepText.Length)
                    {
                        string? name = null;
                        if (methodParams != null && i < methodParams.Length)
                            name = methodParams[i].ParameterName;
                        captures.Add(new InlineParamCapture(offset, length, strValue, name));
                    }
                }
            }
            if (captures.Count > 0)
                inlineParams = captures.ToArray();
        }

        return new ReqNRollStepInfo(
            stepInfo.StepInstance.StepDefinitionKeyword.ToString(),
            stepInfo.Text,
            status,
            opened?.Stopwatch.Elapsed,
            stepInfo.Table?.ToString(),
            stepInfo.MultilineText,
            inlineParams) { Nested = opened?.Nested, RanByAStep = parent is not null };
    }

    private OpenStep? InnermostOpenStep =>
""",
    ),
    (
        """    private sealed class OpenStep(Stopwatch stopwatch, OpenStep? parent)
    {
        public Stopwatch Stopwatch { get; } = stopwatch;
        public OpenStep? Parent { get; } = parent;
""",
        """    private sealed class OpenStep(Stopwatch stopwatch, OpenStep? parent, StepInfo stepInfo)
    {
        public Stopwatch Stopwatch { get; } = stopwatch;
        public OpenStep? Parent { get; } = parent;
        public StepInfo StepInfo { get; } = stepInfo;
""",
    ),
    (
        """        var steps = (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey];

        var arguments = _scenarioContext.ScenarioInfo.Arguments;
""",
        """        var steps = (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey];

        // A suite's AfterStep hook that threw kept ours from running, as Reqnroll stops a type's hooks at the first throw. That
        // step is the one that failed, and nothing is left to list it.
        for (var left = InnermostOpenStep; left is not null; left = left.Parent)
        {
            left.Stopwatch.Stop();
            StepCollector.CompleteStep(scenarioId, false);
            var info = Describe(left.StepInfo, _scenarioContext.ScenarioExecutionStatus, left, left.Parent);
            left.Parent?.Nested.Add(info);
            steps.Add(info);
        }

        var arguments = _scenarioContext.ScenarioInfo.Arguments;
""",
    ),
    (
        """ExampleValueGrouper.BuildStructured(flatValues, AndWhatTheyRan(steps));""",
        """ExampleValueGrouper.BuildStructured(flatValues, steps);""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
    (
        """                            var ran = x.Steps.Count > 0 ? MapSteps(x.Steps, StepCollector.GetSteps(x.ScenarioId)) : [];
                            var backgroundCount = CountBackgroundSteps(ran, x.WrittenBackground);
""",
        """                            var rows = x.Steps.Where(step => !step.RanByAStep).ToList();
                            var ran = rows.Count > 0 ? MapSteps(rows, StepCollector.GetSteps(x.ScenarioId)) : [];
                            var backgroundCount = CountBackgroundSteps(ran, x.WrittenBackground ?? []);
""",
    ),
    (
        """                        }).ToArray();

                return new Feature
""",
        """                        }).ToArray();

                // Scenarios built by hand have no document to read, so what they share is still only guessed at.
                if (featureGroup.All(x => x.WrittenBackground is null))
                    BackgroundStepsDetector.DetectAndExtract(scenarios);

                return new Feature
""",
    ),
    (
        """    /// not their table, doc string or inline values. Each is found in order, by its text, under any woven step between.
    /// </summary>
    private static ScenarioStep[]? DescribeNested(ScenarioStep[]? subSteps, IReadOnlyList<ReqNRollStepInfo>? nested)
    {
        if (subSteps is null || nested is not { Count: > 0 })
            return subSteps;

        var next = 0;
        Walk(subSteps);
        return subSteps;

""",
        """    /// not their table, doc string or inline values. Each is found in order, by its text, under any woven step between. One a
    /// suite's BeforeStep hook stopped before ours opened it was never collected, and is added after the rest: nothing runs after it.
    /// </summary>
    private static ScenarioStep[]? DescribeNested(ScenarioStep[]? subSteps, IReadOnlyList<ReqNRollStepInfo>? nested)
    {
        if (nested is not { Count: > 0 })
            return subSteps;

        var next = 0;
        Walk(subSteps ?? []);
        return next == nested.Count
            ? subSteps
            :
            [
                .. subSteps ?? [],
                .. nested.Skip(next).Select(step => new ScenarioStep
                {
                    Keyword = step.Keyword,
                    Text = step.Text,
                    Status = step.Status.ToStepResult(false),
                    DocString = step.DocString,
                    Parameters = ParseTableText(step.TableText),
                    TextSegments = BuildTextSegments(step),
                }),
            ];

""",
    ),
    (
        """                DescribeNested(subStep.SubSteps, step.Nested);
""",
        """                subStep.SubSteps = DescribeNested(subStep.SubSteps, step.Nested);
""",
    ),
)

edit(
    "src/Kronikol/Reports/ReportToggleDefaultsResolver.cs",
    (
        """    public bool BackgroundStepsOpen { get; init; }
""",
        """    public bool BackgroundStepsOpen { get; init; }
    /// <summary>Whether <see cref="ReportToggleDefaults.BackgroundStepsOpen"/> was set to false, which also keeps a section
    /// holding a failed step closed. Left unset, such a section opens (#222).</summary>
    internal bool BackgroundStepsKeptClosed { get; init; }
""",
    ),
    (
        """        BackgroundStepsOpen = overrides.BackgroundStepsOpen ?? baseline.BackgroundStepsOpen,
""",
        """        BackgroundStepsOpen = overrides.BackgroundStepsOpen ?? baseline.BackgroundStepsOpen,
        BackgroundStepsKeptClosed = overrides.BackgroundStepsOpen is { } backgroundStepsOpen ? !backgroundStepsOpen : baseline.BackgroundStepsKeptClosed,
""",
    ),
)

edit(
    "src/Kronikol/Reports/ReportToggleDefaults.cs",
    (
        """    /// <see cref="ReportConfigurationOptions.SeparateBackgroundSteps"/>). Default: <c>false</c>.</summary>
""",
        """    /// <see cref="ReportConfigurationOptions.SeparateBackgroundSteps"/>). Default: closed, but open where it holds a failed
    /// step; <c>false</c> keeps that one closed too.</summary>
""",
    ),
)

generator = root + "/src/Kronikol/Reports/ReportGenerator.cs"
text = open(generator, encoding="utf-8", newline="").read()
assert text.count("toggles.StepsSectionOpen, toggles.BackgroundStepsOpen, stepDeferral);") == 2
text = text.replace(
    "toggles.StepsSectionOpen, toggles.BackgroundStepsOpen, stepDeferral);",
    "toggles.StepsSectionOpen, toggles.BackgroundStepsOpen, stepDeferral, toggles.BackgroundStepsKeptClosed);",
)
open(generator, "w", encoding="utf-8", newline="").write(text)

edit(
    "src/Kronikol/Reports/ReportGenerator.cs",
    (
        """        bool backgroundStepsOpen = false,
        StepDeferral? deferral = null)
    {
        var background = scenario.BackgroundSteps ?? [];
""",
        """        bool backgroundStepsOpen = false,
        StepDeferral? deferral = null,
        bool backgroundStepsKeptClosed = false)
    {
        var background = scenario.BackgroundSteps ?? [];
""",
    ),
    (
        """                body.Append($\"\"\"<details class="scenario-background"{(backgroundStepsOpen || background.Any(b => b.Status == ExecutionResult.Failed || HasAnyFailed(b)) ? " open" : "")}>\"\"\");
""",
        """                // A section holding a failed step starts open, and stays open as its outline row is selected, unless it was set to stay closed.
                var holdsFailure = !backgroundStepsKeptClosed && background.Any(HasAnyFailed);
                body.Append($\"\"\"<details class="scenario-background"{(holdsFailure ? " data-holds-failure" : "")}{(backgroundStepsOpen || holdsFailure ? " open" : "")}>\"\"\");
""",
    ),
    (
        """    private static bool HasAnyFailed(ScenarioStep step)
    {
        if (step.SubSteps is not { Length: > 0 }) return false;
        foreach (var sub in step.SubSteps)
        {
            if (sub.Status == ExecutionResult.Failed) return true;
            if (HasAnyFailed(sub)) return true;
        }
""",
        """    private static bool HasAnyFailed(ScenarioStep step)
    {
        if (step.Status == ExecutionResult.Failed) return true;
        if (step.SubSteps is not { Length: > 0 }) return false;
        foreach (var sub in step.SubSteps)
        {
            if (HasAnyFailed(sub)) return true;
        }
""",
    ),
    (
        """    private static bool StepMarkerMatches(string plantUml, string stepText)
    {
        if (string.IsNullOrWhiteSpace(stepText))
            return true;

        var marker = plantUml.Replace('\\n', ' ').Trim();
        return marker.Contains(stepText, StringComparison.OrdinalIgnoreCase)
               || marker.Contains(StepText.CapitaliseIfEnabled(stepText) ?? stepText, StringComparison.OrdinalIgnoreCase);
    }
""",
        """    internal static bool StepMarkerMatches(string plantUml, string stepText)
    {
        if (string.IsNullOrWhiteSpace(stepText))
            return true;

        // The bar escapes its label and wraps a long one (StepBarPlantUml.Build), so what it drew is what is compared, breaks aside.
        var marker = WithoutBreaks(plantUml.Replace("\\\\n", ""));
        return marker.Contains(Drawn(stepText), StringComparison.OrdinalIgnoreCase)
               || marker.Contains(Drawn(StepText.CapitaliseIfEnabled(stepText) ?? stepText), StringComparison.OrdinalIgnoreCase);

        static string Drawn(string text) => WithoutBreaks(PlantUml.PlantUmlCreator.EscapeOneLineMarkup(text));
        static string WithoutBreaks(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
    }
""",
    ),
)

edit(
    "src/Kronikol/Reports/report-select-row-function.js",
    (
        """                incoming[i].open = detailStates[i] || (incoming[i].classList.contains('scenario-background')
                    && !!incoming[i].querySelector('.step-status.failed'));
""",
        """                incoming[i].open = detailStates[i] || incoming[i].hasAttribute('data-holds-failure');
""",
    ),
)

tests = root + "/tests/Kronikol.Tests/ReqNRoll/BackgroundAsWrittenTests.cs"
text = open(tests, encoding="utf-8", newline="").read()
old = """        Assert.Contains("<details class=\\"scenario-background\\" open>", ReportHtml.Read(path));
    }
}
"""
assert text.count(old) == 1
new = """        Assert.Contains("<details class=\\"scenario-background\\" data-holds-failure open>", ReportHtml.Read(path));
    }

    [Fact]
    public void A_separate_background_section_set_to_stay_closed_does_so_with_a_failed_step_in_it()
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
        var options = new ReportConfigurationOptions { TestRunReportToggleDefaults = { BackgroundStepsOpen = false } };
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("a", "", "@startuml\\n@enduml")], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, $"Bg222_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            separateBackgroundSteps: true,
            toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: false));

        Assert.Contains("<details class=\\"scenario-background\\">", ReportHtml.Read(path));
    }

    [Fact]
    public void A_failed_step_whose_sub_steps_passed_starts_open()
    {
        var step = new ScenarioStep
        {
            Keyword = "When", Text = "the customer pays", Status = ExecutionResult.Failed,
            SubSteps = [new ScenarioStep { Keyword = "Given", Text = "the card is read", Status = ExecutionResult.Passed }],
        };
        var features = new[]
        {
            new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = "a", DisplayName = "a", Result = ExecutionResult.Failed, Steps = [step] }] },
        };
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("a", "", "@startuml\\n@enduml")], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, $"Bg222_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);

        Assert.Contains("<details class=\\"step step-collapsible\\" open>", ReportHtml.Read(path));
    }

    [Theory]
    [InlineData("the file is at C:\\\\temp\\\\new")]
    [InlineData("the page shows <$sprite> and <img:a.png>")]
    [InlineData("a step quoting \\"something\\" in it")]
    public void A_step_is_recognised_in_the_bar_that_escapes_its_text(string text) =>
        Assert.True(ReportGenerator.StepMarkerMatches(StepBarPlantUml.Build("Given " + text), text));

    [Fact]
    public void A_step_is_recognised_in_the_bar_that_wraps_its_text()
    {
        var text = string.Join(" ", Enumerable.Repeat("a long step text", 30));

        Assert.True(ReportGenerator.StepMarkerMatches(StepBarPlantUml.Build("Given " + text), text));
    }

    [Fact]
    public void A_step_is_not_recognised_in_another_steps_bar() =>
        Assert.False(ReportGenerator.StepMarkerMatches(StepBarPlantUml.Build("Given a customer is signed in"), "the customer pays"));
}

/// <summary>#222: what the fix leaves as it was, for whoever reads the public lists or builds a scenario by hand.</summary>
public class WhatTheFixLeavesTests
{
    private static ReqNRollScenarioInfo ByHand(string id, string title, params ReqNRollStepInfo[] steps) => new()
    {
        ScenarioId = id,
        ScenarioTitle = title,
        FeatureTitle = "Orders",
        ScenarioTags = [],
        CombinedTags = [],
        Steps = [.. steps],
    };

    [Fact]
    public void Scenarios_built_by_hand_keep_the_guess_each_with_its_own_steps()
    {
        var first = ByHand("a", "A first", new ReqNRollStepInfo("Given", "a customer is signed in"), new ReqNRollStepInfo("Then", "the order is confirmed"));
        var second = ByHand("b", "B second", new ReqNRollStepInfo("Given", "a customer is signed in", ScenarioExecutionStatus.TestError), new ReqNRollStepInfo("Then", "a receipt is sent"));

        var scenarios = new[] { first, second }.ToFeatures()[0].Scenarios;

        Assert.Equal("a customer is signed in", Assert.Single(scenarios[0].BackgroundSteps!).Text);
        Assert.Equal(ExecutionResult.Failed, Assert.Single(scenarios[1].BackgroundSteps!).Status);
        Assert.NotSame(scenarios[0].BackgroundSteps![0], scenarios[1].BackgroundSteps![0]);
    }

    [Fact]
    public void A_feature_the_hooks_saw_is_not_guessed_at_when_the_document_writes_nothing()
    {
        var first = ByHand("a", "A first", new ReqNRollStepInfo("Given", "a customer is signed in"), new ReqNRollStepInfo("Then", "the order is confirmed")) with { WrittenBackground = [] };
        var second = ByHand("b", "B second", new ReqNRollStepInfo("Given", "a customer is signed in"), new ReqNRollStepInfo("Then", "a receipt is sent")) with { WrittenBackground = [] };

        var scenarios = new[] { first, second }.ToFeatures()[0].Scenarios;

        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
    }

    [Fact]
    public void A_step_a_step_ran_stays_in_the_list_and_is_no_row()
    {
        var inner = new ReqNRollStepInfo("Given", "the card is read") { RanByAStep = true };
        var outer = new ReqNRollStepInfo("When", "the customer pays") { Nested = [inner] };
        var then = new ReqNRollStepInfo("Then", "the order is confirmed");
        var collected = ByHand("a222left", "A first", inner, outer, then) with { WrittenBackground = [] };

        var scenario = new[] { collected }.ToFeatures()[0].Scenarios[0];

        Assert.Equal(3, collected.Steps.Count);
        Assert.Equal(["the customer pays", "the order is confirmed"], scenario.Steps!.Select(s => s.Text));
    }

    [Fact]
    public void A_step_the_collector_never_saw_is_listed_under_the_step_that_ran_it()
    {
        const string id = "a222unseen";
        StepCollector.StartStep(id, "When", "the customer pays", null, null);
        StepCollector.CompleteStep(id, false);
        var inner = new ReqNRollStepInfo("Given", "the hook stops this one", ScenarioExecutionStatus.TestError) { RanByAStep = true };
        var outer = new ReqNRollStepInfo("When", "the customer pays", ScenarioExecutionStatus.TestError) { Nested = [inner] };
        var collected = ByHand(id, "A first", inner, outer) with { WrittenBackground = [], ExecutionStatus = ScenarioExecutionStatus.TestError };

        var row = Assert.Single(new[] { collected }.ToFeatures()[0].Scenarios[0].Steps!);

        var sub = Assert.Single(row.SubSteps!);
        Assert.Equal("the hook stops this one", sub.Text);
        Assert.Equal(ExecutionResult.Failed, sub.Status);
    }
}
"""
text = text.replace(old, new)
if "using Kronikol.PlantUml;" not in text:
    text = text.replace("using Kronikol.Reports;\n", "using Kronikol.PlantUml;\nusing Kronikol.Reports;\n", 1)
if "using Kronikol.Tracking;" not in text:
    text = text.replace("using Kronikol.ReqNRoll;\n", "using Kronikol.ReqNRoll;\nusing Kronikol.Tracking;\n", 1)
open(tests, "w", encoding="utf-8", newline="").write(text)
print("ok")
