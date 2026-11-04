#!/usr/bin/env python
"""The sixth review's faults, in the source and the facts (apply_r1l_review6.py <checkout>).

The filter search opens the sections of the row it chose and of no other, and records them, so emptying it closes them. A frame a
suite's AfterStep left open is described as the scenario's end describes one. The public stopwatch key is set where a step opens
in earnest. A kind of <details> appears once in a panel, so its first class is all the row script keeps it by.
"""
import sys

root = sys.argv[1]


def edit(path, *pairs):
    full = root + "/" + path
    text = open(full, encoding="utf-8", newline="").read()
    for old, new in pairs:
        assert text.count(old) == 1, (path, text.count(old), old[:90])
        text = text.replace(old, new)
    open(full, "w", encoding="utf-8", newline="").write(text)


edit(
    "src/Kronikol/Reports/report-search-function.js",
    (
        """    revealMatchSections(el);
}

// Every row's, as the search may choose any of them: choosing one gives it what the reader left, so this runs again after that.
function revealMatchSections(el) {
    ['details.example-diagrams', 'details.scenario-steps', 'details.scenario-background'].forEach(function(sel) {
        el.querySelectorAll(sel).forEach(function(section) { section.setAttribute('open', ''); });
    });
}
""",
        """    var diagrams = el.querySelector('details.example-diagrams');
    if (diagrams) diagrams.setAttribute('open', '');
}

// The sections of the row shown, so this runs once the search has chosen its row, as choosing one gives it what the reader
// left. No other row's: the reader's choices there are not the search's to change. Emptying the search closes what this opened.
function revealMatchSections(el) {
    var shown = el.querySelector('.param-detail-panel:not([style*="display"])') || el;
    ['details.scenario-steps', 'details.scenario-background'].forEach(function(sel) {
        var section = shown.querySelector(sel);
        if (section && !section.open) { _openedBySearch.push(section); section.setAttribute('open', ''); }
    });
}
""",
    ),
)

edit(
    "src/Kronikol/Reports/report-select-row-function.js",
    (
        """// Each <details> of `panel` but the composite steps and those holding a failure, with its kind and its place among that kind.
function selectRowEachSection(panel, visit) {
    var seen = Object.create(null);
    panel.querySelectorAll('details:not(.step-collapsible)').forEach(function(d) {
        var kind = d.classList[0];
        seen[kind] = (seen[kind] || 0) + 1;
        if (!selectRowHoldsFailure(d)) visit(d, kind + ' ' + seen[kind]);
    });
}
""",
        """// Each <details> of `panel` but the composite steps and those holding a failure, with its kind, of which a panel has one.
function selectRowEachSection(panel, visit) {
    panel.querySelectorAll('details').forEach(function(d) {
        if (!d.classList.contains('step-collapsible') && !selectRowHoldsFailure(d)) visit(d, d.classList[0]);
    });
}
""",
    ),
    (
        """    // state after it. The other <details> go by their kind and their place among that kind, as a row can lack a kind the
    // row left has: one that never reached its steps has a Failure Result where the row left has its Background.
""",
        """    // state after it. The other <details> go by their kind, as a row can lack a kind the row left has: one that never
    // reached its steps has a Failure Result where the row left has its Background.
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
    (
        """        Open(stepType);
    }

    private OpenStep Open(string stepType)
    {
        var stepStopwatch = Stopwatch.StartNew();
        _scenarioContext[ReqNRollConstants.StepStopwatchKey] = stepStopwatch;
        var opened = new OpenStep(stepStopwatch, InnermostOpenStep, _scenarioContext.StepContext.StepInfo);
""",
        """        _scenarioContext[ReqNRollConstants.StepStopwatchKey] = Open(stepType).Stopwatch;
    }

    private OpenStep Open(string stepType)
    {
        var opened = new OpenStep(Stopwatch.StartNew(), InnermostOpenStep, _scenarioContext.StepContext.StepInfo);
""",
    ),
    (
        """            // A suite's AfterStep hook that threw on a step this one ran kept ours from running there.
            for (var left = InnermostOpenStep; left is not null && !ReferenceEquals(left, opened); left = left.Parent)
                StepCollector.CompleteStep(scenarioId, false);
            StepCollector.CompleteStep(scenarioId, passed);
""",
        """            CloseLeftOpen(opened, scenarioId, steps);
            StepCollector.CompleteStep(scenarioId, passed);
""",
    ),
    (
        """    private OpenStep? InnermostOpenStep =>
""",
        """    /// <summary>
    /// A suite's AfterStep hook that threw kept ours from running, as Reqnroll stops a type's hooks at the first throw. Those steps
    /// are the ones that failed, and nothing else is left to list them. They are closed when the step that ran them closes, or
    /// when the scenario ends.
    /// </summary>
    private void CloseLeftOpen(OpenStep? upTo, string scenarioId, List<ReqNRollStepInfo> steps)
    {
        for (var left = InnermostOpenStep; left is not null && !ReferenceEquals(left, upTo); left = left.Parent)
        {
            left.Stopwatch.Stop();
            StepCollector.CompleteStep(scenarioId, false);
            var info = Describe(left.StepInfo, _scenarioContext.ScenarioExecutionStatus, left, left.Parent);
            left.Parent?.Nested.Add(info);
            steps.Add(info);
        }
    }

    private OpenStep? InnermostOpenStep =>
""",
    ),
    (
        """        // A suite's AfterStep hook that threw kept ours from running, as Reqnroll stops a type's hooks at the first throw. That
        // step is the one that failed, and nothing is left to list it.
        for (var left = InnermostOpenStep; left is not null; left = left.Parent)
        {
            left.Stopwatch.Stop();
            StepCollector.CompleteStep(scenarioId, false);
            var info = Describe(left.StepInfo, _scenarioContext.ScenarioExecutionStatus, left, left.Parent);
            left.Parent?.Nested.Add(info);
            steps.Add(info);
        }
""",
        """        CloseLeftOpen(null, scenarioId, steps);
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
    (
        "            subStep.Parameters = ParseTableText(step.TableText) ?? subStep.Parameters;\n",
        "            subStep.Parameters = ParseTableText(step.TableText);\n",
    ),
)

# The real run: the public stopwatch stays pinned, and a nested step an AfterStep hook failed keeps its doc string.
for project in ("xUnit2", "xUnit3", "TUnit"):
    edit(
        f"examples/Example.Api/tests/Example.Api.Tests.Component.ReqNRoll.{project}/StepDefinitions/StepsAsWrittenStepDefinitions.cs",
        (
            """    public Task AStepRunsAStepAnAfterStepHookFails() => runner.GivenAsync("a step an AfterStep hook fails when asked");
""",
            """    public Task AStepRunsAStepAnAfterStepHookFails() => runner.GivenAsync("a step with a doc string an AfterStep hook fails when asked", "its doc string", null);

    [StepDefinition("a step with a doc string an AfterStep hook fails when asked")]
    public void AStepWithADocStringAnAfterStepHookFails(string docString)
    {
    }
""",
        ),
        (
            """        if (StepText == "a step an AfterStep hook fails when asked")
""",
            """        if (StepText is "a step an AfterStep hook fails when asked" or "a step with a doc string an AfterStep hook fails when asked")
""",
        ),
        (
            """            throw new InvalidOperationException("The steps list lost a step a step ran.");
""",
            """            throw new InvalidOperationException("The steps list lost a step a step ran.");
        if (scenario[ReqNRollConstants.StepStopwatchKey] is not System.Diagnostics.Stopwatch)
            throw new InvalidOperationException("The public step stopwatch is gone.");
""",
        ),
    )

edit(
    "examples/Example.Api/tests/Example.Api.Tests.Integration/Tests/ReqNRollDuplicateStepsTests.cs",
    (
        """["Given a step runs a step an AfterStep hook fails when asked", "Given a step an AfterStep hook fails when asked"]""",
        """["Given a step runs a step an AfterStep hook fails when asked", "Given a step with a doc string an AfterStep hook fails when asked"]""",
    ),
    (
        """        Assert.Equal(["When a step an AfterStep hook fails when asked"], failing["An AfterStep hook fails on a scenario's own step"]);
""",
        """        Assert.Equal(["When a step an AfterStep hook fails when asked"], failing["An AfterStep hook fails on a scenario's own step"]);

        // The hook stopped ours from closing that inner step, and it is still described.
        var html = Kronikol.Reports.HtmlReportFile.ToPlain(await File.ReadAllTextAsync(ReportParser.GetReportFiles(result.ReportsFolderPath).TestRunReportHtml!));
        Assert.Contains("<pre class=\\"step-docstring\\"><code>its doc string</code></pre>", html);
""",
    ),
)

# The facts.
facts = root + "/tests/Kronikol.Tests/ReqNRoll/BackgroundAsWrittenTests.cs"
t = open(facts, encoding="utf-8", newline="").read()
old = """        options.GenerateSpecificationsReport = false;
        options.GenerateSpecificationsData = false;
        DefaultDiagramsFetcher.Reset();
        ReportGenerator.CreateStandardReportsWithDiagrams([new Feature { DisplayName = "Orders", Scenarios = scenarios }], Start, Start.AddSeconds(30), options);
        DefaultDiagramsFetcher.Reset();
        return ReportHtml.Read(Path.Combine(_dir, "TestRunReport.html"));
    }
"""
new = """        options.GenerateSpecificationsData = false;
        DefaultDiagramsFetcher.Reset();
        ReportGenerator.CreateStandardReportsWithDiagrams([new Feature { DisplayName = "Orders", Scenarios = scenarios }], Start, Start.AddSeconds(30), options);
        DefaultDiagramsFetcher.Reset();
        return ReportHtml.Read(Path.Combine(_dir, "TestRunReport.html"));
    }

    [Fact]
    public void The_specifications_page_reads_its_own_value()
    {
        // A scenario that passed on a retry keeps the failed attempt's step, and the page is not blank for it.
        var retried = Holding(FailedStep) with { Result = ExecutionResult.Passed };
        var options = new ReportConfigurationOptions { SpecificationsToggleDefaults = { BackgroundStepsOpen = false } };

        var testRun = Report(options, retried);

        Assert.Contains("<details class=\\"scenario-background\\" data-holds-failure open>", testRun);
        Assert.Contains("<details class=\\"scenario-background\\">", ReportHtml.Read(Path.Combine(_dir, "Specifications.html")));
    }
"""
assert t.count(old) == 1
t = t.replace(old, new)
open(facts, "w", encoding="utf-8", newline="").write(t)

e2e = root + "/tests/Kronikol.Tests.EndToEnd/BackgroundSectionRowSwitchTests.cs"
t = open(e2e, encoding="utf-8", newline="").read()


def sub(old, new):
    global t
    assert t.count(old) == 1, (t.count(old), old[:90])
    t = t.replace(old, new)


sub(
    """    private static Scenario Row(string id, string row, ExecutionResult result, ScenarioStep[]? background, ScenarioStep[]? steps) => new()
    {
        Id = id,
        DisplayName = "Pay",
        OutlineId = "Pay",
""",
    """    private static Scenario Row(string id, string row, ExecutionResult result, ScenarioStep[]? background, ScenarioStep[]? steps, string outline = "Pay") => new()
    {
        Id = id,
        DisplayName = outline,
        OutlineId = outline,
""",
)
sub(
    """    [Fact]
    public async Task A_failed_composite_keeps_its_state_on_its_own_row()
    {
        await OpenReport("RowSwitchComposite",
        [
            Row("a222a", "a fails", ExecutionResult.Failed, null, Composite(ExecutionResult.Failed)),
""",
    """    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_failed_composite_keeps_its_state_on_its_own_row(bool failedByItself)
    {
        ScenarioStep[] failed = failedByItself
            ? Composite(ExecutionResult.Failed)
            :
            [
                new ScenarioStep
                {
                    Keyword = "When", Text = "the customer pays", Status = ExecutionResult.Passed,
                    SubSteps = [new ScenarioStep { Keyword = "Given", Text = "the card is read", Status = ExecutionResult.Failed }],
                },
            ];
        await OpenReport("RowSwitchComposite" + failedByItself,
        [
            Row("a222a", "a fails", ExecutionResult.Failed, null, failed),
""",
)
sub(
    """    [Theory]
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
""",
    '''    // a holds a failure in its Background, and only b's Background holds the word.
    private static Scenario[] Searched() =>
    [
        Row("a222a", "a fails", ExecutionResult.Failed, SignedIn(ExecutionResult.Failed), null),
        Row("a222b", "b passes", ExecutionResult.Passed, [new ScenarioStep { Keyword = "Given", Text = "a zebraword customer is signed in", Status = ExecutionResult.Passed }], Pays()),
        Row("a222c", "c passes", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays()),
    ];

    private async Task WaitForRow(string row) => await Page.WaitForFunctionAsync(
        "row => { const r = document.querySelector('details.scenario tr.row-active'); return r && r.getAttribute('data-row-idx') === row; }",
        row, new PageWaitForFunctionOptions { PollingInterval = 200 });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_single_search_match_shows_in_its_row_when_the_first_row_holds_a_failure(bool readerMovedFirst)
    {
        await OpenReport("RowSwitchSearch" + readerMovedFirst, Searched());

        if (readerMovedFirst)
        {
            await SelectRow(2);
            await SelectRow(0);
        }
        await FillSearchBar("zebraword");
        await WaitForRow("1");

        Assert.True(await IsOpen("details.scenario-background"));
        Assert.True(await Page.GetByText("a zebraword customer is signed in").First.IsVisibleAsync());
    }

    [Fact]
    public async Task Emptying_the_search_puts_back_what_the_reader_left()
    {
        await OpenReport("RowSwitchSearchEmptied", Searched());

        await SelectRow(2);
        await SetOpen("details.scenario-steps", false);
        await FillSearchBar("zebraword");
        await WaitForRow("1");
        Assert.True(await IsOpen("details.scenario-steps"));
        await FillSearchBar("");
        await WaitForRow("2");

        Assert.False(await IsOpen("details.scenario-steps"));
        Assert.False(await IsOpen("details.scenario-background"));
    }

    [Fact]
    public async Task The_search_changes_no_row_but_the_one_it_chose()
    {
        await OpenReport("RowSwitchSearchOtherRows", Searched());

        await SelectRow(2);
        await SetOpen("details.scenario-steps", false);
        await FillSearchBar("zebraword");
        await WaitForRow("1");

        Assert.False(await Page.EvaluateAsync<bool>(
            """() => document.querySelector('[id$="-detail-2"] details.scenario-steps').open"""));
        Assert.False(await Page.EvaluateAsync<bool>(
            """() => document.querySelector('[id$="-detail-2"] details.scenario-background').open"""));
    }

    private async Task<bool> IsOpenIn(int scenario, string selector) => await Page.Locator("details.scenario").Nth(scenario).EvaluateAsync<bool>(
        """
        (element, selector) => Array.from(element.querySelectorAll('[id*="-detail-"]')).find(p => p.style.display !== 'none').querySelector(selector).open
        """,
        selector);

    [Fact]
    public async Task What_the_reader_left_in_one_outline_stays_out_of_another()
    {
        await OpenReport("RowSwitchTwoOutlines",
        [
            Row("a222a", "a", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays()),
            Row("a222b", "b", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays()),
            Row("a222c", "c", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays(), outline: "Refund"),
            Row("a222d", "d", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays(), outline: "Refund"),
        ]);

        await Page.Locator("details.scenario").First.EvaluateAsync(
            """
            element => {
                const panel = Array.from(element.querySelectorAll('[id*="-detail-"]')).find(p => p.style.display !== 'none');
                panel.querySelector('details.scenario-background').open = true;
            }
            """);
        await SelectRow(1);
        Assert.True(await IsOpenIn(0, "details.scenario-background"));
        await SelectRow(3);

        Assert.False(await IsOpenIn(1, "details.scenario-background"));
    }

    [Fact]
    public async Task A_composite_at_another_place_among_the_steps_takes_no_state()
    {
        var plain = new ScenarioStep { Keyword = "Given", Text = "a customer is signed in", Status = ExecutionResult.Passed };
        await OpenReport("RowSwitchCompositePlace",
        [
            Row("a222a", "a", ExecutionResult.Passed, null, [plain, .. Composite(ExecutionResult.Passed)]),
            Row("a222b", "b", ExecutionResult.Passed, null, [.. Composite(ExecutionResult.Passed), plain]),
        ]);

        await SetOpen("details.step-collapsible", true);
        await SelectRow(1);

        Assert.False(await IsOpen("details.step-collapsible"));
    }

    [Fact]
    public async Task A_click_on_the_row_already_chosen_leaves_a_composite_as_it_is()
    {
        await OpenReport("RowSwitchSameRowComposite",
        [
            Row("a222a", "a", ExecutionResult.Passed, null, Composite(ExecutionResult.Passed)),
            Row("a222b", "b", ExecutionResult.Passed, null, Composite(ExecutionResult.Passed)),
        ]);

        await SelectRow(1);
        await SelectRow(0);
        await SetOpen("details.step-collapsible", true);
        await SelectRow(0);

        Assert.True(await IsOpen("details.step-collapsible"));
    }
''',
)
open(e2e, "w", encoding="utf-8", newline="").write(t)
edit(
    "tests/Kronikol.Tests/Reports/VerdictFencesTests.cs",
    (
        '{ "report-search-function.js", "0696f19fc5f00917a5fde122641a1a6f62116962d51d23a66a1508367149c0d9", "e6b805eb054d3ce9f80aa4706e2b3fe10581e50f23fc209a4d8420ec298ac9d8" }',
        '{ "report-search-function.js", "83de72075ebea0d0063e8e6eaf50d05466221c7b6e25d75781e629b65f8c8c60", "f16c5fbf948b3d99c93783038fbf1a8d82a4a86af5661d95992c55b7f695e5d7" }',
    ),
)
print("source and facts edited")
