# What the fourth review and #223's fifth change in the prototype: the matcher goes back to published's, and the row switch, the toggle, the nested pairing and a fact are fixed.
import pathlib
import re

P = pathlib.Path(r"C:\Code\Kronikol-222p")
H = pathlib.Path(r"C:\Code\Kronikol-222\plans\BACKGROUND_AS_WRITTEN_PLAN.harness")


def edit(path, *pairs):
    text = path.read_bytes().decode("utf-8")
    for old, new in pairs:
        assert text.count(old) == 1, old[:90]
        text = text.replace(old, new)
    path.write_bytes(text.encode("utf-8"))


# 1. StepMarkerMatches: published's, word for word.
gen = P / "src/Kronikol/Reports/ReportGenerator.cs"
text = gen.read_bytes().decode("utf-8")
start = text.index("    internal static bool StepMarkerMatches(string plantUml, string stepText)")
end_marker = 'text.Replace("\\\\n", "", StringComparison.Ordinal).Replace("\\n", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);\n'
end = text.index(end_marker, start) + len(end_marker)
text = (
    text[:start]
    + """    private static bool StepMarkerMatches(string plantUml, string stepText)
    {
        if (string.IsNullOrWhiteSpace(stepText))
            return true;

        var marker = plantUml.Replace('\\n', ' ').Trim();
        return marker.Contains(stepText, StringComparison.OrdinalIgnoreCase)
               || marker.Contains(StepText.CapitaliseIfEnabled(stepText) ?? stepText, StringComparison.OrdinalIgnoreCase);
    }
"""
    + text[end:]
)
# 2. The section's flag: what the resolver says, so a record built by hand reads as it did.
for old, new in [
    ("toggles.StepsSectionOpen, toggles.BackgroundStepsOpen, stepDeferral, toggles.BackgroundStepsKeptClosed);", None),
    ("bool backgroundStepsKeptClosed = false)", "bool backgroundOpensOnFailure = false)"),
    ("var holdsFailure = !backgroundStepsKeptClosed && background.Any(HasAnyFailed);", "var holdsFailure = backgroundOpensOnFailure && background.Any(HasAnyFailed);"),
]:
    if new is None:
        assert text.count(old) == 2
        text = text.replace(old, "toggles.StepsSectionOpen, toggles.BackgroundStepsOpen, stepDeferral, toggles.BackgroundStepsOpenOnFailure);")
    else:
        assert text.count(old) == 1, old
        text = text.replace(old, new)
gen.write_bytes(text.encode("utf-8"))

edit(
    P / "src/Kronikol/Reports/ReportToggleDefaultsResolver.cs",
    ("    public static ResolvedToggleDefaults BuiltIn { get; } = new();", "    public static ResolvedToggleDefaults BuiltIn { get; } = new() { BackgroundStepsOpenOnFailure = true };"),
    (
        """    /// <summary>Whether <see cref="ReportToggleDefaults.BackgroundStepsOpen"/> was set to false, which also keeps a section
    /// holding a failed step closed. Left unset, such a section opens (#222).</summary>
    internal bool BackgroundStepsKeptClosed { get; init; }
""",
        """    /// <summary>Whether a section holding a failed step opens though <see cref="BackgroundStepsOpen"/> is false: so when
    /// <see cref="ReportToggleDefaults.BackgroundStepsOpen"/> was left unset (#222). A value set there, or a record built by
    /// hand, says what it says.</summary>
    internal bool BackgroundStepsOpenOnFailure { get; init; }
""",
    ),
    (
        "BackgroundStepsKeptClosed = overrides.BackgroundStepsOpen is { } backgroundStepsOpen ? !backgroundStepsOpen : baseline.BackgroundStepsKeptClosed,",
        "BackgroundStepsOpenOnFailure = overrides.BackgroundStepsOpen is null && baseline.BackgroundStepsOpenOnFailure,",
    ),
)

# 3. The row switch.
edit(
    P / "src/Kronikol/Reports/report-select-row-function.js",
    (
        """    // first opened (5.0.0, plans/REPORT_FORMAT_PLAN.md 4.8), so counted by position an undrawn subtree would shift every
    // state after it. The other <details> keep their order, which nothing changes.
    var detailStates = [];
""",
        """    // first opened (5.0.0, plans/REPORT_FORMAT_PLAN.md 4.8), so counted by position an undrawn subtree would shift every
    // state after it. The other <details> go by their kind and their place among that kind, as a row can lack a kind the
    // row left has: one that never reached its steps has a Failure Result where the row left has its Background.
    var detailStates = {};
""",
    ),
    (
        "            else detailStates.push(d.open);\n",
        "            else (detailStates[d.className] = detailStates[d.className] || []).push(d);\n",
    ),
    (
        """        if (detailStates.length > 0) {
            var incoming = Array.prototype.filter.call(activePanel.querySelectorAll('details'), function(d) {
                return !d.classList.contains('step-collapsible');
            });
            for (var i = 0; i < incoming.length && i < detailStates.length; i++) {
                // What the reader did to the row left carries over, but a row's Background section holding a failure stays open.
                incoming[i].open = detailStates[i] || incoming[i].hasAttribute('data-holds-failure');
            }
        }
""",
        """        var seen = {};
        Array.prototype.forEach.call(activePanel.querySelectorAll('details'), function(d) {
            if (d.classList.contains('step-collapsible')) return;
            var place = seen[d.className] = (seen[d.className] || 0) + 1;
            var left = (detailStates[d.className] || [])[place - 1];
            // What the reader did to the row left carries over. A Background section holding a failure is open for that
            // reason and not by the reader's hand, so nothing crosses to or from one, and each row keeps its own.
            if (left && !left.hasAttribute('data-holds-failure') && !d.hasAttribute('data-holds-failure')) d.open = left.open;
        });
""",
    ),
)

# 4. A nested step is paired only with a step that has a keyword, as a woven [Step] has none.
edit(
    P / "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
    (
        "if (next >= nested.Count || !string.Equals(subStep.Text, nested[next].Text, StringComparison.Ordinal))",
        "if (next >= nested.Count || string.IsNullOrEmpty(subStep.Keyword) || !string.Equals(subStep.Text, nested[next].Text, StringComparison.Ordinal))",
    ),
)

# 5. The facts.
facts = P / "tests/Kronikol.Tests/ReqNRoll/BackgroundAsWrittenTests.cs"
text = facts.read_bytes().decode("utf-8")
start = text.index('    [Theory]\n    [InlineData("the file is at C:\\\\temp\\\\new")]')
end_marker = 'Assert.False(ReportGenerator.StepMarkerMatches(StepBarPlantUml.Build("Given a customer is signed in"), "the customer pays"));\n'
end = text.index(end_marker, start) + len(end_marker)
text = text[:start].rstrip() + "\n" + text[end:]
anchor = "    [Fact]\n    public void A_step_found_under_a_woven_step_keeps_its_arguments_as_does_the_one_after_it()\n"
assert text.count(anchor) == 1
text = text.replace(
    anchor,
    """    [Fact]
    public void A_nested_step_is_not_taken_for_a_woven_step_that_reads_the_same()
    {
        var id = nameof(A_nested_step_is_not_taken_for_a_woven_step_that_reads_the_same);
        var inner = new ReqNRollStepInfo("Given", "the namesake step", ScenarioExecutionStatus.OK, null, "|sku|\\n|APPLE|");
        var outer = new ReqNRollStepInfo("Given", "a composite step", ScenarioExecutionStatus.OK) { Nested = [inner] };

        Kronikol.Tracking.StepCollector.StartStep(id, "Given", outer.Text, null, null);
        Kronikol.Tracking.StepCollector.StartStep(id, null, inner.Text, null, null);
        Kronikol.Tracking.StepCollector.CompleteStep(id, true);
        Kronikol.Tracking.StepCollector.StartStep(id, "Given", inner.Text, null, null);
        Kronikol.Tracking.StepCollector.CompleteStep(id, true);
        Kronikol.Tracking.StepCollector.CompleteStep(id, true);

        var info = new ReqNRollScenarioInfo
        {
            ScenarioId = id,
            ScenarioTitle = "A",
            FeatureTitle = "Orders",
            ScenarioTags = [],
            CombinedTags = [],
            Steps = [outer],
            WrittenBackground = [],
        };

        var subSteps = new[] { info }.ToFeatures()[0].Scenarios[0].Steps![0].SubSteps!;

        Assert.Null(subSteps[0].Parameters);
        Assert.NotNull(subSteps[1].Parameters);
    }

"""
    + anchor,
)
anchor = "    [Fact]\n    public void A_separate_background_section_holding_a_failed_step_starts_open()\n"
assert text.count(anchor) == 1
text = text.replace(
    anchor,
    """    [Fact]
    public void A_toggle_record_built_by_hand_says_what_it_says()
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
            [new DefaultDiagramsFetcher.DiagramAsCode("a", "", "@startuml\\n@enduml")], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, $"Bg222_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            separateBackgroundSteps: true, toggleDefaults: new ResolvedToggleDefaults { BackgroundStepsOpen = false });

        Assert.Contains("<details class=\\"scenario-background\\">", ReportHtml.Read(path));
    }

"""
    + anchor,
)
facts.write_bytes(text.encode("utf-8"))

# 6. The nested failing run stays out of the CI job's summary.
old = 'new Dictionary<string, string> { ["KRONIKOL_EXAMPLE_STEP_FAILURES"] = "true" }'
new = 'new Dictionary<string, string> { ["KRONIKOL_EXAMPLE_STEP_FAILURES"] = "true", ["GITHUB_STEP_SUMMARY"] = "" }'
edit(P / "examples/Example.Api/tests/Example.Api.Tests.Integration/Tests/ReqNRollDuplicateStepsTests.cs", (old, new))
edit(H / "prototype/apply_examples.py", (old, new))

# 7. A Playwright fact for the row switch.
(P / "tests/Kronikol.Tests.EndToEnd/BackgroundSectionRowSwitchTests.cs").write_text(
    '''using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// #222: with SeparateBackgroundSteps, what a row switch does to a Background section holding a failed step, and to a row
/// that never reached its steps.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class BackgroundSectionRowSwitchTests : PlaywrightTestBase
{
    public BackgroundSectionRowSwitchTests(PlaywrightFixture fixture) : base(fixture) { }

    private static Scenario Row(string id, string row, ExecutionResult background, ExecutionResult result, bool reachedItsSteps = true) => new()
    {
        Id = id,
        DisplayName = "Pay",
        OutlineId = "Pay",
        Result = result,
        ExampleValues = new Dictionary<string, string> { ["row"] = row },
        ExampleRawValues = new Dictionary<string, object?> { ["row"] = row },
        BackgroundSteps = reachedItsSteps ? [new ScenarioStep { Keyword = "Given", Text = "a customer is signed in", Status = background }] : null,
        Steps = reachedItsSteps ? [new ScenarioStep { Keyword = "When", Text = "the customer pays", Status = ExecutionResult.Passed }] : null,
        ErrorMessage = result == ExecutionResult.Failed ? "it failed" : null,
    };

    private async Task OpenReport(string fileName)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    Row("a", "passes", ExecutionResult.Passed, ExecutionResult.Passed),
                    Row("b", "fails", ExecutionResult.Failed, ExecutionResult.Failed),
                    Row("c", "never starts", ExecutionResult.Passed, ExecutionResult.Failed, reachedItsSteps: false),
                ],
            },
        };
        var path = ReportGenerator.GenerateHtmlReport(
            [
                new DefaultDiagramsFetcher.DiagramAsCode("a", "", "@startuml\\n@enduml"),
                new DefaultDiagramsFetcher.DiagramAsCode("b", "", "@startuml\\n@enduml"),
                new DefaultDiagramsFetcher.DiagramAsCode("c", "", "@startuml\\n@enduml"),
            ],
            features, DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            separateBackgroundSteps: true);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
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

    [Fact]
    public async Task A_failing_rows_section_is_open_and_the_row_left_keeps_its_own_state()
    {
        await OpenReport("RowSwitchFailing.html");

        Assert.False(await IsOpen("details.scenario-background"));
        await SelectRow(1);
        Assert.True(await IsOpen("details.scenario-background"));
        await SelectRow(0);
        Assert.False(await IsOpen("details.scenario-background"));
    }

    [Fact]
    public async Task A_failing_rows_section_the_reader_closed_stays_closed_when_they_come_back()
    {
        await OpenReport("RowSwitchReaderClosed.html");

        await SelectRow(1);
        await Page.EvaluateAsync(
            """
            () => {
                const panel = Array.from(document.querySelectorAll('[id*="-detail-"]')).find(p => p.style.display !== 'none');
                panel.querySelector('details.scenario-background').open = false;
            }
            """);
        await SelectRow(0);
        await SelectRow(1);

        Assert.False(await IsOpen("details.scenario-background"));
    }

    [Fact]
    public async Task A_row_that_never_reached_its_steps_keeps_its_failure_result_open()
    {
        await OpenReport("RowSwitchFailureResult.html");

        await SelectRow(2);

        Assert.True(await IsOpen("details.failure-result"));
    }
}
''',
    encoding="utf-8",
    newline="\n",
)

# 8. The mutations.
m = H / "prototype/mutations.py"
text = m.read_text(encoding="utf-8")
for name in ("M23", "M24", "M27", "M28"):
    text, n = re.subn(r'    \("' + name + r' .*\n.*\n', "", text)
    assert n == 1, name
text = text.replace(
    '"var holdsFailure = !backgroundStepsKeptClosed && background.Any(HasAnyFailed);", "var holdsFailure = false;"',
    '"var holdsFailure = backgroundOpensOnFailure && background.Any(HasAnyFailed);", "var holdsFailure = false;"',
)
text = text.replace(
    '"var holdsFailure = !backgroundStepsKeptClosed && background.Any(HasAnyFailed);", "var holdsFailure = background.Any(HasAnyFailed);"',
    '"var holdsFailure = backgroundOpensOnFailure && background.Any(HasAnyFailed);", "var holdsFailure = background.Any(HasAnyFailed);"',
)
old = """    ("M18 a row's Background section takes the row left's state, failure or not", "src/Kronikol/Reports/report-select-row-function.js",
     "detailStates[i] || incoming[i].hasAttribute('data-holds-failure');", "detailStates[i];"),
"""
assert text.count(old) == 1
text = text.replace(
    old,
    """    ("M18 a row's Background section takes the row left's state, failure or not", "src/Kronikol/Reports/report-select-row-function.js",
     "if (left && !left.hasAttribute('data-holds-failure') && !d.hasAttribute('data-holds-failure')) d.open = left.open;", "if (left) d.open = left.open;", "e2e"),
    ("M23 a row's details go by position, whatever their kind", "src/Kronikol/Reports/report-select-row-function.js",
     "var left = (detailStates[d.className] || [])[place - 1];", "var left = Object.values(detailStates).flat()[Array.from(activePanel.querySelectorAll('details')).filter(function(x) { return !x.classList.contains('step-collapsible'); }).indexOf(d)];", "e2e"),
    ("M24 a nested step is taken for a woven step that reads the same", "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
     "next >= nested.Count || string.IsNullOrEmpty(subStep.Keyword) || !string.Equals", "next >= nested.Count || !string.Equals"),
    ("M27 a toggle record built by hand opens for a failure", "src/Kronikol/Reports/ReportToggleDefaultsResolver.cs",
     "internal bool BackgroundStepsOpenOnFailure { get; init; }", "internal bool BackgroundStepsOpenOnFailure { get; init; } = true;"),
""",
)
old = '''REAL_FILTER = "FullyQualifiedName~ReqNRollDuplicateStepsTests"\n'''
assert text.count(old) == 1
text = text.replace(old, old + '''E2E = "tests/Kronikol.Tests.EndToEnd"\nE2E_FILTER = "FullyQualifiedName~BackgroundSectionRowSwitchTests"\n''')
old = "project, filter_by = (REAL, REAL_FILTER) if real else (TESTS, FILTER)"
assert text.count(old) == 1
text = text.replace(old, 'project, filter_by = (E2E, E2E_FILTER) if real == ["e2e"] else (REAL, REAL_FILTER) if real else (TESTS, FILTER)')
m.write_text(text, encoding="utf-8", newline="\n")

# 9. The combination takes every ReportGenerator hunk again.
a = H / "prototype/apply_on_223.py"
text = a.read_text(encoding="utf-8")
start = text.index("# That prototype's R11 already holds section 5.4")
end = text.index("def edit(path, *pairs):")
text = text[:start] + text[end:]
old = '        "--include=src/Kronikol/Reports/ReportToggleDefaults.cs",\n'
assert text.count(old) == 1
text = text.replace(old, '        "--include=src/Kronikol/Reports/ReportGenerator.cs",\n' + old)
text = text.replace("import os\nimport subprocess\n", "import subprocess\n")
text = text.replace("and sections 5.4, 5.5 and 5.11's comparison, as that prototype's R11 holds its own.", "and sections 5.4 and 5.5, as that prototype's R11 holds them.")
a.write_text(text, encoding="utf-8", newline="\n")
print("edited")
