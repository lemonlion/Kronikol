using Kronikol.Reports;
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

    private static Scenario Row(string id, string row, ExecutionResult result, ScenarioStep[]? background, ScenarioStep[]? steps, string outline = "Pay") => new()
    {
        Id = id,
        DisplayName = outline,
        OutlineId = outline,
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

    [Theory]
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

    // a holds a failure in its Background, and only b's Background holds the word.
    private static Scenario[] Searched() =>
    [
        Row("a222a", "a fails", ExecutionResult.Failed, SignedIn(ExecutionResult.Failed), null),
        Row("a222b", "b passes", ExecutionResult.Passed, [new ScenarioStep { Keyword = "Given", Text = "a zebraword customer is signed in", Status = ExecutionResult.Passed }], Pays()),
        Row("a222c", "c passes", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays()),
        Row("a222d", "d passes", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays()),
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
        // and a row neither the reader nor the search has been to is as it started
        Assert.True(await Page.EvaluateAsync<bool>(
            """() => document.querySelector('[id$="-detail-3"] details.scenario-steps').open"""));
        Assert.False(await Page.EvaluateAsync<bool>(
            """() => document.querySelector('[id$="-detail-3"] details.scenario-background').open"""));
    }

    private static ScenarioStep[] Reading(string text) => [new ScenarioStep { Keyword = "Given", Text = text, Status = ExecutionResult.Passed }];

    private async Task<bool> IsOpenOnRow(int row, string selector) => await Page.EvaluateAsync<bool>(
        """([row, selector]) => document.querySelector('[id$="-detail-' + row + '"] ' + selector).open""",
        new object[] { row, selector });

    [Fact]
    public async Task A_search_that_moves_to_another_row_shows_its_match_on_each_and_leaves_the_rest()
    {
        await OpenReport("RowSwitchSearchMoves",
        [
            Row("a222a", "a passes", ExecutionResult.Passed, Reading("a zebra customer is signed in"), Pays()),
            Row("a222b", "b passes", ExecutionResult.Passed, Reading("a zebra stripe customer is signed in"), Pays()),
            Row("a222c", "c passes", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays()),
        ]);

        await FillSearchBar("zebra");
        await WaitForRow("0");
        Assert.True(await IsOpen("details.scenario-background"));
        await FillSearchBar("zebra stripe");
        await WaitForRow("1");
        Assert.True(await IsOpen("details.scenario-background"));
        await FillSearchBar("");
        await WaitForRow("0");

        Assert.False(await IsOpenOnRow(2, "details.scenario-background"));
    }

    [Fact]
    public async Task A_section_the_reader_closed_during_a_search_stays_closed_as_it_narrows()
    {
        await OpenReport("RowSwitchSearchNarrows",
        [
            Row("a222a", "a passes", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays()),
            Row("a222b", "b passes", ExecutionResult.Passed, Reading("a zebra stripe customer is signed in"), Pays()),
        ]);

        await FillSearchBar("zebra");
        await WaitForRow("1");
        Assert.True(await IsOpen("details.scenario-background"));
        await SetOpen("details.scenario-background", false);
        await FillSearchBar("zebra stripe");
        await WaitForRow("1");

        Assert.False(await IsOpen("details.scenario-background"));
    }

    // The reader starts on a, and b and c each hold one word.
    private static Scenario[] TwoWords() =>
    [
        Row("a222a", "a passes", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays()),
        Row("a222b", "b passes", ExecutionResult.Passed, Reading("a zebra customer is signed in"), Pays()),
        Row("a222c", "c passes", ExecutionResult.Passed, Reading("a stripe customer is signed in"), Pays()),
    ];

    [Fact]
    public async Task A_section_the_reader_closed_stays_closed_when_the_match_moves_away_and_back()
    {
        await OpenReport("RowSwitchSearchAwayAndBack", TwoWords());

        await FillSearchBar("zebra");
        await WaitForRow("1");
        await SetOpen("details.scenario-background", false);
        await FillSearchBar("stripe");
        await WaitForRow("2");
        Assert.True(await IsOpen("details.scenario-background"));
        await FillSearchBar("zebra");
        await WaitForRow("1");

        Assert.False(await IsOpen("details.scenario-background"));
    }

    [Fact]
    public async Task A_new_search_opens_again_what_the_reader_closed_in_the_last()
    {
        await OpenReport("RowSwitchSearchAgain", TwoWords());

        await FillSearchBar("zebra");
        await WaitForRow("1");
        await SetOpen("details.scenario-background", false);
        await FillSearchBar("");
        await WaitForRow("0");
        await FillSearchBar("zebra");
        await WaitForRow("1");

        Assert.True(await IsOpen("details.scenario-background"));
    }

    [Fact]
    public async Task A_search_after_Clear_All_opens_again_what_the_reader_closed()
    {
        await OpenReport("RowSwitchSearchClearAll", TwoWords());

        await FillSearchBar("zebra");
        await WaitForRow("1");
        await SetOpen("details.scenario-background", false);
        await Page.EvaluateAsync("clear_all_filters()");
        await FillSearchBar("zebra");
        await WaitForRow("1");

        Assert.True(await IsOpen("details.scenario-background"));
    }

    [Fact]
    public async Task A_search_after_going_Back_opens_again_what_the_reader_closed()
    {
        await OpenReport("RowSwitchSearchBack", TwoWords());

        await FillSearchBar("zebra");
        await WaitForRow("1");
        await SetOpen("details.scenario-background", false);
        await Page.GoBackAsync();
        await Page.WaitForFunctionAsync("() => document.getElementById('searchbar').value === ''", null, new PageWaitForFunctionOptions { PollingInterval = 200 });
        await FillSearchBar("zebra");
        await WaitForRow("1");

        Assert.True(await IsOpen("details.scenario-background"));
    }

    // Each row opens with a composite, and the second and fourth hold the word: so one outline, whose match the search shows on the second.
    private static Scenario[] OkapiRows()
    {
        ScenarioStep[] Plain() => Composite(ExecutionResult.Passed);
        ScenarioStep[] Holding() => [.. Plain(), .. Reading("an okapi pays")];
        return
        [
            Row("a222a", "a", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Plain()),
            Row("a222b", "b", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Holding()),
            Row("a222c", "c", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Plain()),
            Row("a222d", "d", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Holding()),
        ];
    }

    [Fact]
    public async Task A_row_the_search_chose_does_not_undo_what_the_reader_closed()
    {
        await OpenReport("RowSwitchChosenClosed", OkapiRows());

        await SetOpen("details.scenario-steps", false);
        await FillSearchBar("okapi");
        await WaitForRow("1");
        Assert.True(await IsOpen("details.scenario-steps"));

        await SelectRow(2);
        await WaitForRow("2");
        Assert.False(await IsOpen("details.scenario-steps"));
        await SelectRow(0);
        await WaitForRow("0");
        Assert.False(await IsOpen("details.scenario-steps"));
    }

    [Theory]
    [InlineData("details.scenario-background")]
    [InlineData("details.step-collapsible")]
    public async Task A_row_the_search_chose_does_not_undo_what_the_reader_opened(string selector)
    {
        await OpenReport("RowSwitchChosenOpened" + selector.Length, OkapiRows());

        await SetOpen(selector, true);
        await FillSearchBar("okapi");
        await WaitForRow("1");

        await SelectRow(2);
        await WaitForRow("2");
        Assert.True(await IsOpen(selector));
        await SelectRow(0);
        await WaitForRow("0");
        Assert.True(await IsOpen(selector));
    }

    [Fact]
    public async Task What_the_reader_changes_on_a_row_the_search_chose_carries()
    {
        await OpenReport("RowSwitchChosenChanged", OkapiRows());

        await FillSearchBar("okapi");
        await WaitForRow("1");
        await SetOpen("details.scenario-steps", false);

        await SelectRow(2);
        await WaitForRow("2");
        Assert.False(await IsOpen("details.scenario-steps"));
    }

    [Fact]
    public async Task A_row_the_search_chose_is_the_readers_once_they_come_back_to_it()
    {
        await OpenReport("RowSwitchChosenComeBack", OkapiRows());

        await SetOpen("details.scenario-steps", false);
        await FillSearchBar("okapi");
        await WaitForRow("1");
        await SelectRow(2);
        await WaitForRow("2");
        await SelectRow(1);
        await WaitForRow("1");
        Assert.False(await IsOpen("details.scenario-steps"));

        // open again as the search found it, so only a row that is the reader's by now passes that on
        await SetOpen("details.scenario-steps", true);
        await SelectRow(2);
        await WaitForRow("2");
        Assert.True(await IsOpen("details.scenario-steps"));
    }

    [Fact]
    public async Task A_search_that_lands_on_the_row_shown_passes_on_none_of_what_it_opens_there()
    {
        await OpenReport("RowSwitchSearchOnRowShown", OkapiRows());

        await SelectRow(1);
        await WaitForRow("1");
        await SetOpen("details.scenario-steps", false);
        await FillSearchBar("okapi");
        await Page.WaitForFunctionAsync(
            "() => document.querySelector('[id$=\"-detail-1\"] details.scenario-background').open",
            null, new PageWaitForFunctionOptions { PollingInterval = 200 });
        Assert.True(await IsOpen("details.scenario-steps"));

        await SelectRow(2);
        await WaitForRow("2");
        Assert.False(await IsOpen("details.scenario-steps"));
        Assert.False(await IsOpen("details.scenario-background"));
    }

    [Fact]
    public async Task What_the_reader_opened_before_the_search_opened_the_rest_carries()
    {
        // two outlines hold the word, so the search chooses rows and reveals nothing until it narrows to one
        await OpenReport("RowSwitchSearchNarrows",
        [
            Row("a222a", "a", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays()),
            Row("a222b", "b", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Reading("an okapi pays with cash")),
            Row("a222c", "c", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays()),
            Row("a222d", "d", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Reading("an okapi pays"), outline: "Refund"),
            Row("a222e", "e", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays(), outline: "Refund"),
        ]);

        await FillSearchBar("okapi");
        await WaitForRow("1");
        Assert.False(await IsOpen("details.scenario-background"));
        await SetOpen("details.scenario-background", true);
        await FillSearchBar("okapi pays with");
        await Page.WaitForFunctionAsync(
            "() => Array.from(document.querySelectorAll('details.scenario')).filter(s => getComputedStyle(s).display !== 'none').length === 1",
            null, new PageWaitForFunctionOptions { PollingInterval = 200 });

        await SelectRow(2);
        await WaitForRow("2");
        Assert.True(await IsOpen("details.scenario-background"));
    }

    [Fact]
    public async Task Going_back_to_the_same_search_keeps_closed_what_the_reader_closed()
    {
        await OpenReport("RowSwitchSearchBackSame", TwoWords());

        await FillSearchBar("zebra");
        await WaitForRow("1");
        await SetOpen("details.scenario-background", false);
        await Page.Locator(".status-toggle[data-status='Passed']").EvaluateAsync("b => b.click()");
        await Page.WaitForFunctionAsync("() => location.hash.includes('status')", null, new PageWaitForFunctionOptions { PollingInterval = 200 });
        await Page.GoBackAsync();
        await Page.WaitForFunctionAsync("() => !location.hash.includes('status')", null, new PageWaitForFunctionOptions { PollingInterval = 200 });

        Assert.False(await IsOpen("details.scenario-background"));
    }

    // A composite whose sub-steps are long enough to be held until it first opens, with a nested composite among them.
    private static ScenarioStep[] Held()
    {
        var chip = string.Concat(Enumerable.Repeat("chip says yes and the terminal answers ", 70));
        return
        [
            new ScenarioStep
            {
                Keyword = "When", Text = "the customer pays", Status = ExecutionResult.Passed,
                SubSteps =
                [
                    new ScenarioStep
                    {
                        Keyword = "Given", Text = "the card is read", Status = ExecutionResult.Passed,
                        SubSteps = [new ScenarioStep { Keyword = "Given", Text = chip, Status = ExecutionResult.Passed }],
                    },
                ],
            },
        ];
    }

    private async Task OpenStepAt(string[] path)
    {
        await Page.EvaluateAsync(
            """
            async path => {
                const panel = Array.from(document.querySelectorAll('[id*="-detail-"]')).find(p => p.style.display !== 'none');
                let scope = panel.querySelector('details.scenario-steps');
                for (const summary of path) {
                    let step = null;
                    for (let tries = 0; !step && tries < 40; tries++) {
                        step = Array.from(scope.querySelectorAll('details.step-collapsible')).find(d => d.querySelector(':scope > summary').textContent.includes(summary));
                        if (!step) await new Promise(resolve => setTimeout(resolve, 50));
                    }
                    step.open = true;
                    await new Promise(resolve => setTimeout(resolve, 50));
                    scope = step;
                }
            }
            """,
            path);
    }

    // A held composite draws its sub-steps after the row shows, so this waits for them, and fails if the step stays closed.
    private async Task WaitForStepOpenAt(string[] path) => await Page.WaitForFunctionAsync(
        """
        path => {
            const panel = Array.from(document.querySelectorAll('[id*="-detail-"]')).find(p => p.style.display !== 'none');
            let scope = panel.querySelector('details.scenario-steps');
            let step = null;
            for (const summary of path) {
                step = Array.from(scope.querySelectorAll('details.step-collapsible')).find(d => d.querySelector(':scope > summary').textContent.includes(summary));
                if (!step) return false;
                scope = step;
            }
            return step.open;
        }
        """,
        path,
        new PageWaitForFunctionOptions { PollingInterval = 200, Timeout = 5000 });

    [Fact]
    public async Task Sub_steps_drawn_since_the_search_chose_the_row_are_no_change_of_the_readers()
    {
        await OpenReport("RowSwitchChosenDrawnSince",
        [
            Row("a222a", "a", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Held()),
            Row("a222b", "b", ExecutionResult.Passed, Reading("a zebra customer is signed in"), Held()),
            Row("a222c", "c", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Held()),
            Row("a222d", "d", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Held()),
        ]);

        await OpenStepAt(["the customer pays", "the card is read"]);
        await SelectRow(2);
        await WaitForRow("2");
        await FillSearchBar("zebra");
        await WaitForRow("1");
        await OpenStepAt(["the customer pays"]);

        await SelectRow(3);
        await WaitForRow("3");
        await WaitForStepOpenAt(["the customer pays", "the card is read"]);
    }

    [Fact]
    public async Task What_the_search_opened_on_a_row_it_chose_does_not_carry()
    {
        await OpenReport("RowSwitchChosenOpenedBySearch", OkapiRows());

        await FillSearchBar("okapi");
        await WaitForRow("1");
        Assert.True(await IsOpen("details.scenario-background"));

        await SelectRow(2);
        await WaitForRow("2");
        Assert.False(await IsOpen("details.scenario-background"));
    }

    [Fact]
    public async Task A_row_the_search_chooses_takes_nothing_from_the_row_it_left()
    {
        await OpenReport("RowSwitchSearchTakesNothing",
        [
            Row("a222a", "a never starts", ExecutionResult.Failed, null, null),
            Row("a222b", "b zebra never starts", ExecutionResult.Failed, null, null),
        ]);

        await SetOpen("details.failure-result", false);
        await FillSearchBar("zebra");
        await WaitForRow("1");

        Assert.True(await IsOpen("details.failure-result"));
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
            // c has no Background, so nothing of this outline's overwrites what the other outline's row left
            Row("a222c", "c", ExecutionResult.Failed, null, null, outline: "Refund"),
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
