#!/usr/bin/env python
"""The seventh review's faults (apply_r1m_review7.py <checkout>).

A step a suite's AfterStep left open closes as the scenario's AfterScenario hooks begin, so it takes neither their time nor their
woven steps. The search keeps what it opened on the row it shows apart: that goes back before it chooses another row, so a click
does not keep it as the reader's choice, and it opens a row's sections once, so one the reader closes stays closed. It records
the diagrams section it opens too.
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
    "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
    (
        """    /// are the ones that failed, and nothing else is left to list them. They are closed when the step that ran them closes, or
    /// when the scenario ends.
""",
        """    /// are the ones that failed, and nothing else is left to list them. They are closed when the step that ran them closes, or
    /// as the scenario's AfterScenario hooks begin.
""",
    ),
    (
        """    [AfterScenario(Order = int.MaxValue)]
    public void AfterScenario()
""",
        """    // Ahead of a suite's own, so a step left open takes neither their time nor their woven steps, and they find it in the list.
    [AfterScenario(Order = int.MinValue)]
    public void CloseStepsLeftOpen()
    {
        if (IsOwner && InnermostOpenStep is not null)
            CloseLeftOpen(null, (string)_scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey], (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey]);
    }

    [AfterScenario(Order = int.MaxValue)]
    public void AfterScenario()
""",
    ),
    (
        """        var steps = (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey];

        CloseLeftOpen(null, scenarioId, steps);

        var arguments = _scenarioContext.ScenarioInfo.Arguments;
""",
        """        var steps = (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey];

        var arguments = _scenarioContext.ScenarioInfo.Arguments;
""",
    ),
)

edit(
    "src/Kronikol/Reports/report-search-function.js",
    (
        """    [el.closest('details.feature'), el.closest('details.rule'), el].forEach(function(d) {
        if (d && !d.open) { _openedBySearch.push(d); d.setAttribute('open', ''); }
    });
    var diagrams = el.querySelector('details.example-diagrams');
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

function close_opened_by_search() {
    _openedBySearch.forEach(function(d) { d.removeAttribute('open'); });
""",
        """    [el.closest('details.feature'), el.closest('details.rule'), el, el.querySelector('details.example-diagrams')].forEach(function(d) {
        if (d && !d.open) { _openedBySearch.push(d); d.setAttribute('open', ''); }
    });
}

// The sections of the row shown, so this runs once the search has chosen its row, as choosing one gives it what the reader
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
    _openedBySearch.forEach(function(d) { d.removeAttribute('open'); });
""",
    ),
    (
        """    chosen.row = row;
    row.click();
""",
        """    chosen.row = row;
    // What it opened on the row it leaves was not the reader's choice, so it goes before that row's click keeps it as one.
    close_sections_opened_by_search();
    row.click();
""",
    ),
)

for project in ("xUnit2", "xUnit3", "TUnit"):
    edit(
        f"examples/Example.Api/tests/Example.Api.Tests.Component.ReqNRoll.{project}/StepDefinitions/StepsAsWrittenStepDefinitions.cs",
        (
            """    [AfterScenario("reads-the-public-list")]
""",
            """    // A step an AfterStep hook kept the adapter from closing is closed ahead of this hook, so this hook's time and work are not that step's.
    [AfterScenario("after-step-fails")]
    public void FindsTheStepAHookLeftOpen()
    {
        var steps = (List<ReqNRollStepInfo>)scenario[ReqNRollConstants.StepsCollectionKey];
        if (AskedToFail && !steps.Any(step => step.Text is "a step an AfterStep hook fails when asked" or "a step with a doc string an AfterStep hook fails when asked"))
            throw new InvalidOperationException("The step a hook left open is not in the list.");
    }

    [AfterScenario("reads-the-public-list")]
""",
        ),
    )

e2e = root + "/tests/Kronikol.Tests.EndToEnd/BackgroundSectionRowSwitchTests.cs"
t = open(e2e, encoding="utf-8", newline="").read()
old = """    private async Task<bool> IsOpenIn(int scenario, string selector) =>"""
assert t.count(old) == 1
new = '''    private static ScenarioStep[] Reading(string text) => [new ScenarioStep { Keyword = "Given", Text = text, Status = ExecutionResult.Passed }];

    private async Task<bool> IsOpenOnRow(int row, string selector) => await Page.EvaluateAsync<bool>(
        """([row, selector]) => document.querySelector('[id$="-detail-' + row + '"] ' + selector).open""",
        new object[] { row, selector });

    [Fact]
    public async Task A_search_that_moves_to_another_row_leaves_no_row_open_when_emptied()
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
        Assert.False(await IsOpenOnRow(0, "details.scenario-background"));
        await FillSearchBar("");
        await WaitForRow("0");

        Assert.False(await IsOpenOnRow(0, "details.scenario-background"));
        Assert.False(await IsOpenOnRow(1, "details.scenario-background"));
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

''' + old
t = t.replace(old, new)
open(e2e, "w", encoding="utf-8", newline="").write(t)
edit(
    "tests/Kronikol.Tests/Reports/VerdictFencesTests.cs",
    (
        '{ "report-search-function.js", "83de72075ebea0d0063e8e6eaf50d05466221c7b6e25d75781e629b65f8c8c60", "f16c5fbf948b3d99c93783038fbf1a8d82a4a86af5661d95992c55b7f695e5d7" }',
        '{ "report-search-function.js", "835029f50a5b5be0877891107da53948dd1534a96422de42631f99f185a70086", "611e91f4655b7caa3ddfbe6b3928cd593a30c16a542d1e2b3dd5abf732712acf" }',
    ),
)
edit(
    'tests/Kronikol.Tests/Reports/ToggleDefaultsMarkupTests.cs',
    (
        r'''        Assert.Contains("[el.closest('details.feature'), el.closest('details.rule'), el].forEach(", content);
        Assert.Contains("'details.example-diagrams', 'details.scenario-steps', 'details.scenario-background'", content);
''',
        r'''        Assert.Contains("[el.closest('details.feature'), el.closest('details.rule'), el, el.querySelector('details.example-diagrams')].forEach(", content);
        Assert.Contains("['details.scenario-steps', 'details.scenario-background'].forEach(", content);
''',
    ),
)
edit(
    'tests/Kronikol.Tests.EndToEnd/BackgroundSectionRowSwitchTests.cs',
    (
        r'''            Row("a222c", "c", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays(), outline: "Refund"),
            Row("a222d", "d", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays(), outline: "Refund"),
''',
        r'''            // c has no Background, so nothing of this outline's overwrites what the other outline's row left
            Row("a222c", "c", ExecutionResult.Failed, null, null, outline: "Refund"),
            Row("a222d", "d", ExecutionResult.Passed, SignedIn(ExecutionResult.Passed), Pays(), outline: "Refund"),
''',
    ),
)
print("source and facts edited")
