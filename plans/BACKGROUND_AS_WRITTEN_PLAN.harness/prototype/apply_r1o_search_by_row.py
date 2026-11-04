# After the eighth review. The search's lists of what it opened drew faults in two reviews running, and they only fixed a leak
# 5.28.2 has, so they are gone: the search opens the sections of the row shown, each once until it is emptied, and closes nothing,
# as 5.28.2 does; its own row switches keep and bring nothing. Also a step a suite's AfterScenario hook ran and left open.
# Usage: apply_r1o_search_by_row.py <root> [facts|source]
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
what = sys.argv[2] if len(sys.argv) > 2 else "both"


def edit(path, *pairs):
    target = root / path
    text = target.read_text(encoding="utf-8")
    for old, new in pairs:
        assert text.count(old) == 1, (path, text.count(old), old[:90])
        text = text.replace(old, new)
    target.write_text(text, encoding="utf-8", newline="\n")


if what in ("both", "facts"):
    edit(
        "tests/Kronikol.Tests.EndToEnd/BackgroundSectionRowSwitchTests.cs",
        (
            "public async Task A_search_that_moves_to_another_row_leaves_no_row_open_when_emptied()",
            "public async Task A_search_that_moves_to_another_row_shows_its_match_on_each_and_leaves_the_rest()",
        ),
        (
            '''        Assert.False(await IsOpenOnRow(0, "details.scenario-background"));
        await FillSearchBar("");
        await WaitForRow("0");

        Assert.False(await IsOpenOnRow(0, "details.scenario-background"));
        Assert.False(await IsOpenOnRow(1, "details.scenario-background"));
''',
            '''        await FillSearchBar("");
        await WaitForRow("0");

        Assert.False(await IsOpenOnRow(2, "details.scenario-background"));
''',
        ),
        (
            '''    private async Task<bool> IsOpenIn(int scenario, string selector) =>''',
            '''    // The reader starts on a, and b and c each hold one word.
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

    private async Task<bool> IsOpenIn(int scenario, string selector) =>''',
        ),
    )
    edit(
        "tests/Kronikol.Tests.EndToEnd/SearchFunctionPageTests.cs",
        (
            '''        Assert.Equal("feature rule scenario steps background diagrams", await OpenedAsync(ChocolateCake));
        Assert.Equal("none", (await DisplaysAsync("details.scenario"))[Vanilla]);
    }
''',
            '''        Assert.Equal("feature rule scenario steps background diagrams", await OpenedAsync(ChocolateCake));
        Assert.Equal("none", (await DisplaysAsync("details.scenario"))[Vanilla]);
    }

    [Fact]
    public async Task A_diagrams_section_the_reader_closed_stays_closed_on_the_next_key()
    {
        await OpenAsync(GenerateReveal("SearchPage_RevealReaderClosed.html"));
        await SearchAsync("chocolate", ChocolateCake);
        await Page.EvaluateAsync("id => { document.getElementById(id).querySelector('details.example-diagrams').open = false; }", ChocolateCake);

        await SearchAsync("chocolate cake", ChocolateCake);

        Assert.Equal("feature rule scenario steps background", await OpenedAsync(ChocolateCake));
    }
''',
        ),
    )
    edit(
        "tests/Kronikol.Tests/Reports/ToggleDefaultsMarkupTests.cs",
        (
            '''Assert.Contains("[el.closest('details.feature'), el.closest('details.rule'), el, el.querySelector('details.example-diagrams')].forEach(", content);''',
            '''Assert.Contains("[el.closest('details.feature'), el.closest('details.rule'), el].forEach(", content);''',
        ),
        (
            '''Assert.Contains("['details.scenario-steps', 'details.scenario-background'].forEach(", content);''',
            '''Assert.Contains("[el.querySelector('details.example-diagrams'), shown.querySelector('details.scenario-steps'), shown.querySelector('details.scenario-background')].forEach(", content);''',
        ),
    )
    for project in ("xUnit2", "xUnit3", "TUnit"):
        base = f"examples/Example.Api/tests/Example.Api.Tests.Component.ReqNRoll.{project}"
        feature = root / base / "Features/StepsThatFail.feature"
        feature.write_text(
            feature.read_text(encoding="utf-8").rstrip("\n")
            + """

@after-scenario-runs-a-step
Scenario: An AfterScenario hook runs a step an AfterStep hook fails
    Given a step that passes
""",
            encoding="utf-8",
            newline="\n",
        )
        edit(
            f"{base}/StepDefinitions/StepsAsWrittenStepDefinitions.cs",
            (
                '''    [AfterStep("after-step-fails")]
    public void AfterStepFails()
''',
                '''    [AfterStep("after-step-fails")]
    [AfterStep("after-scenario-runs-a-step")]
    public void AfterStepFails()
''',
            ),
            (
                '''    [AfterScenario("reads-the-public-list")]
''',
                '''    // A suite's hook can run a step of its own, and an AfterStep hook can stop that one like any other.
    [AfterScenario("after-scenario-runs-a-step")]
    public async Task RunsAStepAnAfterStepHookFails()
    {
        try
        {
            await runner.GivenAsync("a step an AfterStep hook fails when asked");
        }
        catch (Exception) when (AskedToFail)
        {
        }
    }

    [AfterScenario("reads-the-public-list")]
''',
            ),
        )
    edit(
        "examples/Example.Api/tests/Example.Api.Tests.Integration/Tests/ReqNRollDuplicateStepsTests.cs",
        (
            '''        Assert.Equal(7, failing.Count);
''',
            '''        Assert.Equal(8, failing.Count);
''',
        ),
        (
            '''        // The hook stopped ours from closing that inner step, and it is still described.
''',
            '''        Assert.Equal(["Given a step an AfterStep hook fails when asked"], failing["An AfterScenario hook runs a step an AfterStep hook fails"]);

        // The hook stopped ours from closing that inner step, and it is still described.
''',
        ),
    )

if what in ("both", "source"):
    edit(
        "src/Kronikol/Reports/report-search-function.js",
        (
            "[el.closest('details.feature'), el.closest('details.rule'), el, el.querySelector('details.example-diagrams')].forEach(function(d) {",
            "[el.closest('details.feature'), el.closest('details.rule'), el].forEach(function(d) {",
        ),
        (
            """// The sections of the row shown, so this runs once the search has chosen its row, as choosing one gives it what the reader
// left. No other row's, as the reader's choices there are not the search's to change, and once for that row, so one the reader
// closes stays closed.
var _sectionsOpenedBySearch = [], _revealedIn = null;
function revealMatchSections(el) {
    var shown = el.querySelector('.param-detail-panel:not([style*="display"])') || el;
    if (shown === _revealedIn) return;
    close_sections_opened_by_search();
    _revealedIn = shown;
    ['details.scenario-steps', 'details.scenario-background'].forEach(function(sel) {
        var section = shown.querySelector(sel);
        if (section && !section.open) { _sectionsOpenedBySearch.push(section); section.setAttribute('open', ''); }
    });
}
function close_sections_opened_by_search() {
    _sectionsOpenedBySearch.forEach(function(d) { d.removeAttribute('open'); });
    _sectionsOpenedBySearch = [];
    _revealedIn = null;
}

function close_opened_by_search() {
    close_sections_opened_by_search();
""",
            """// The sections a match needs, on the row shown, so this runs once the search has chosen its row. No other row's, as the
// reader's choices there are not the search's to change, and each once until the search is emptied, so one the reader closes
// stays closed.
var _sectionsRevealed = new WeakSet();
function revealMatchSections(el) {
    var shown = el.querySelector('.param-detail-panel:not([style*="display"])') || el;
    [el.querySelector('details.example-diagrams'), shown.querySelector('details.scenario-steps'), shown.querySelector('details.scenario-background')].forEach(function(section) {
        if (section && !_sectionsRevealed.has(section)) { _sectionsRevealed.add(section); section.setAttribute('open', ''); }
    });
}

function close_opened_by_search() {
    _sectionsRevealed = new WeakSet();
""",
        ),
        (
            """    chosen.row = row;
    // What it opened on the row it leaves was not the reader's choice, so it goes before that row's click keeps it as one.
    close_sections_opened_by_search();
    row.click();
}
""",
            """    chosen.row = row;
    click_search_row(row);
}
// A row the search chooses or puts back shows as it stands, as the search's choice is not the reader's to keep.
function click_search_row(row) {
    selectRowBySearch = true;
    row.click();
    selectRowBySearch = false;
}
""",
        ),
        (
            "if (c.was && c.row.classList.contains('row-active') && !c.was.classList.contains('row-active')) c.was.click();",
            "if (c.was && c.row.classList.contains('row-active') && !c.was.classList.contains('row-active')) click_search_row(c.was);",
        ),
    )
    edit(
        "src/Kronikol/Reports/report-select-row-function.js",
        (
            """var selectRowKept = Object.create(null);
""",
            """var selectRowKept = Object.create(null);
// Set round a click the search makes: that row shows as it stands, and neither keeps nor brings what the reader left.
var selectRowBySearch = false;
""",
        ),
        (
            """    var switching = outgoing !== activePanel;
    if (outgoing && switching) {
""",
            """    var carrying = outgoing !== activePanel && !selectRowBySearch;
    if (outgoing && carrying) {
""",
        ),
        (
            """        if (switching) selectRowEachSection(activePanel, function(d, key) { if (key in kept.sections) d.open = kept.sections[key]; });
        selectRowSyncSteps(activePanel, switching ? kept.steps : Object.create(null));
""",
            """        if (carrying) selectRowEachSection(activePanel, function(d, key) { if (key in kept.sections) d.open = kept.sections[key]; });
        selectRowSyncSteps(activePanel, carrying ? kept.steps : Object.create(null));
""",
        ),
    )
    edit(
        "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
        (
            """    // Ahead of a suite's own, so a step left open takes neither their time nor their woven steps, and they find it in the list.
    [AfterScenario(Order = int.MinValue)]
    internal void CloseStepsLeftOpen()
    {
        if (IsOwner && InnermostOpenStep is not null)
            CloseLeftOpen(null, (string)_scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey], (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey]);
    }
""",
            """    // Ahead of a suite's own at any later order, so a step left open takes neither their time nor their woven steps, and they
    // find it in the list. AfterScenario comes back to it for a step one of their hooks ran and left open.
    [AfterScenario(Order = int.MinValue)]
    internal void CloseStepsLeftOpen()
    {
        if (!IsOwner || InnermostOpenStep is null)
            return;

        CloseLeftOpen(null, (string)_scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey], (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey]);
        _scenarioContext.Remove(ReqNRollConstants.OpenStepKey);
    }
""",
        ),
        (
            """        _stopwatch?.Stop();
        var scenarioId = (string)_scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey];
        var steps = (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey];

        var arguments = _scenarioContext.ScenarioInfo.Arguments;
""",
            """        _stopwatch?.Stop();
        CloseStepsLeftOpen();
        var scenarioId = (string)_scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey];
        var steps = (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey];

        var arguments = _scenarioContext.ScenarioInfo.Arguments;
""",
        ),
    )
