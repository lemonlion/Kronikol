"""Breaks the prototype one line at a time and says which first-cut fact notices: python -I mutations.py <checkout> <out.txt>.

Each mutation is an exact-string edit, built and run with the facts' filter, then restored from the file's own bytes. A mutation no fact
notices is a gap in the first cut, and the plan's section 6 says where the fact that catches it belongs. One marked REAL is in the hooks, which only a real run
reaches, so it runs section 6.3's facts in the Integration project, which build the example projects themselves.
"""
import pathlib
import subprocess
import sys

root = pathlib.Path(sys.argv[1])
out = pathlib.Path(sys.argv[2])
FILTER = "FullyQualifiedName~BackgroundAsWrittenTests|FullyQualifiedName~SharedStepsTests|FullyQualifiedName~StepsAStepRanTests|FullyQualifiedName~StepsUnderAWovenStepTests|FullyQualifiedName~BackgroundSectionFromTheOptionsTests|FullyQualifiedName~WhatTheFixLeavesTests|FullyQualifiedName~QueryOverBackgroundStepsTests"
TESTS = "tests/Kronikol.Tests/Kronikol.Tests.csproj"
REAL = "examples/Example.Api/tests/Example.Api.Tests.Integration"
REAL_FILTER = "FullyQualifiedName~ReqNRollDuplicateStepsTests"
E2E = "tests/Kronikol.Tests.EndToEnd"
E2E_FILTER = "FullyQualifiedName~BackgroundSectionRowSwitchTests|FullyQualifiedName~SearchFunctionPageTests"

MUTATIONS = [
    ("M1 the detector hands every scenario the first's steps again", "src/Kronikol/Reports/BackgroundStepsDetector.cs",
     "scenario.BackgroundSteps = scenario.Steps!.Take(commonPrefixLength).ToArray();",
     "scenario.BackgroundSteps = members[0].Steps!.Take(commonPrefixLength).ToArray();"),
    ("M2 the attachment walk visits a shared step twice", "src/Kronikol/Reports/AttachmentPlacement.cs",
     "if (!visited.Add(step))\n                    continue;\n", ""),
    ("M3 a separate section holding a failed step starts closed", "src/Kronikol/Reports/ReportGenerator.cs",
     "var holdsFailure = backgroundOpensOnFailure && background.Any(HasAnyFailed);", "var holdsFailure = false;"),
    ("M4 the count is not limited to the rows that ran", "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
     "while (count < ran.Count && count < writtenBackground.Count", "while (count < writtenBackground.Count"),
    ("M5 a rule's background replaces its feature's", "src/Kronikol.ReqNRoll.Core/ExamplesBlockResolver.cs",
     "[.. featureSteps, .. ruleSteps]", "ruleSteps"),
    ("M6 a row that disagrees with the document is counted", "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
     "&& string.Equals(ran[count].Text, writtenBackground[count], StringComparison.Ordinal)", ""),
    ("M7 no title route when no pickle is found", "src/Kronikol.ReqNRoll.Core/ExamplesBlockResolver.cs",
     "return scenarioInfo.Title is { Length: > 0 } title && lookup.BackgroundByScenarioName.TryGetValue(title, out var byName)\n                ? byName\n                : [];",
     "return [];"),
    ("M8 the adapter guesses over what the hooks saw too", "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
     "if (featureGroup.All(x => x.WrittenBackground is null))", "if (featureGroup.Any())"),
    ("M9 a step a step ran joins the scenario's own rows", "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
     "x.Steps.Where(step => !step.RanByAStep).ToList()", "x.Steps.ToList()"),
    ("M10 a step a step ran loses its doc string", "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
     "subStep.DocString = step.DocString;\n", ""),
    ("M11 a step a step ran loses its table", "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
     "subStep.Parameters = ParseTableText(step.TableText);\n", ""),
    ("M12 a step a step ran loses its outcome", "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
     "subStep.Status = step.Status.ToStepResult(false);\n", ""),
    ("M13 a step reads only its own status, not the scenario's", "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
     "var status = stepContext.Status != ScenarioExecutionStatus.OK ? stepContext.Status : _scenarioContext.ScenarioExecutionStatus;", "var status = stepContext.Status;", True),
    ("M14 the attachment walk visits a scenario listed twice", "src/Kronikol/Reports/AttachmentPlacement.cs",
     "if (!visited.Add(scenario))\n                continue;\n", ""),
    ("M15 frames a suite's AfterStep left open are not closed", "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
     "            CloseLeftOpen(opened, scenarioId, steps);\n", "", True),
    ("M17 a step under a woven step is not found", "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
     "DescribeNested(subStep.SubSteps, nested);\n", ""),
    ("M18 a section's hold lets the row left's state cross", "src/Kronikol/Reports/report-select-row-function.js",
     "if (!d.classList.contains('step-collapsible') && !selectRowHoldsFailure(d)) visit(d, d.classList[0]);", "if (!d.classList.contains('step-collapsible')) visit(d, d.classList[0]);", "e2e"),
    ("M23 a row's details go by position, whatever their kind", "src/Kronikol/Reports/report-select-row-function.js",
     "visit(d, d.classList[0]);", "visit(d, 'any');", "e2e"),
    ("M24 a nested step is paired by its text", "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
     "var step = subStep.OpenedBy is null ? null : nested.FirstOrDefault(s => ReferenceEquals(s.Nested, subStep.OpenedBy));",
     "var step = nested.FirstOrDefault(s => s.Text == subStep.Text);"),
    ("M27 a report from the options is not told the option was left unset", "src/Kronikol/Reports/ReportGenerator.cs",
     "backgroundOpensOnFailure: ReportToggleDefaultsResolver.BackgroundOpensOnFailure(options, specifications: false)", "backgroundOpensOnFailure: false"),
    ("M28 an outline's row opens for a failure whatever was set", "src/Kronikol/Reports/ReportGenerator.cs",
     "RenderScenarioStepSections(body, s, showStepNumbers, separateBackgroundSteps, collapseRepeatedStepKeywords,\n                    toggles.StepsSectionOpen, toggles.BackgroundStepsOpen, stepDeferral, backgroundOpensOnFailure);",
     "RenderScenarioStepSections(body, s, showStepNumbers, separateBackgroundSteps, collapseRepeatedStepKeywords,\n                    toggles.StepsSectionOpen, toggles.BackgroundStepsOpen, stepDeferral, true);"),
    ("M29 an outline's rows are not told the option was left unset", "src/Kronikol/Reports/ReportGenerator.cs",
     "                        backgroundOpensOnFailure: backgroundOpensOnFailure,\n", ""),
    ("M30 a scenario's section opens for a failure whatever was set", "src/Kronikol/Reports/ReportGenerator.cs",
     "RenderScenarioStepSections(body, scenario, showStepNumbers, separateBackgroundSteps, collapseRepeatedStepKeywords,\n                    toggles.StepsSectionOpen, toggles.BackgroundStepsOpen, stepDeferral, backgroundOpensOnFailure);",
     "RenderScenarioStepSections(body, scenario, showStepNumbers, separateBackgroundSteps, collapseRepeatedStepKeywords,\n                    toggles.StepsSectionOpen, toggles.BackgroundStepsOpen, stepDeferral, true);"),
    ("M31 a failed step two levels down is not seen", "src/Kronikol/Reports/ReportGenerator.cs",
     "if (HasAnyFailed(sub)) return true;", "if (sub.Status == ExecutionResult.Failed) return true;"),
    ("M32 a section reads its steps' own status only", "src/Kronikol/Reports/ReportGenerator.cs",
     "background.Any(HasAnyFailed);", "background.Any(s => s.Status == ExecutionResult.Failed);"),
    ("M33 a composite that failed by itself has its sub-steps held", "src/Kronikol/Reports/ReportGenerator.cs",
     "var held = deferral is { Inside: false } && !HasAnyFailed(step);", "var held = deferral is { Inside: false } && !step.SubSteps!.Any(HasAnyFailed);"),
    ("M34 the Specifications' own value is not read", "src/Kronikol/Reports/ReportToggleDefaultsResolver.cs",
     "\n        && (!specifications || options.SpecificationsToggleDefaults.BackgroundStepsOpen is null);", ";"),
    ("M35 the report's own value is not read", "src/Kronikol/Reports/ReportToggleDefaultsResolver.cs",
     "options.TestRunReportToggleDefaults.BackgroundStepsOpen is null\n        && (!specifications", "(!specifications"),
    ("M36 only the step list's state carries", "src/Kronikol/Reports/report-select-row-function.js",
     "if (key in kept.sections) d.open = kept.sections[key];", "if (key in kept.sections && d.classList.contains('scenario-steps')) d.open = kept.sections[key];", "e2e"),
    ("M37 what the reader left lasts one switch", "src/Kronikol/Reports/report-select-row-function.js",
     "var kept = selectRowKept[prefix] || (selectRowKept[prefix] = { sections: Object.create(null), steps: Object.create(null) });", "var kept = { sections: Object.create(null), steps: Object.create(null) };", "e2e"),
    ("M38 a failed composite holds nothing", "src/Kronikol/Reports/report-select-row-function.js",
     " || (d.classList.contains('step-collapsible') && d.querySelector('.step-status.failed') !== null)", "", "e2e"),
    ("M39 a click on the row already chosen puts back what was left", "src/Kronikol/Reports/report-select-row-function.js",
     "if (carrying) selectRowEachSection(activePanel,", "selectRowEachSection(activePanel,", "e2e"),
    ("M40 a composite's state crosses from a failed one", "src/Kronikol/Reports/report-select-row-function.js",
     "if (!selectRowHoldsFailure(d)) kept.steps[selectRowStepPath(d, outgoing)] = d.open;", "kept.steps[selectRowStepPath(d, outgoing)] = d.open;", "e2e"),
    ("M41 the search does not open its match's sections after choosing the row", "src/Kronikol/Reports/report-search-function.js",
     "    if (r.count === 1 && r.single) revealMatchSections(r.single);\n", "", "e2e"),
    ("M42 the search opens the first row's sections alone", "src/Kronikol/Reports/report-search-function.js",
     """var shown = el.querySelector('.param-detail-panel:not([style*="display"])') || el;""", "var shown = el;", "e2e"),
    ("M43 a nested step a hook stopped is not opened at its place", "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
     "        if (opened is null && InnermostOpenStep is not null)\n            opened = Open(stepContext.StepInfo.StepInstance.StepDefinitionKeyword.ToString());\n", "", True),
    ("M44 the report's step does not hear what opened it", "src/Kronikol/Tracking/StepCollector.cs",
     "                : null,\n            OpenedBy = step.OpenedBy\n", "                : null\n"),
    ("M45 a nested step's own nested steps are not described", "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
     "            DescribeNested(subStep.SubSteps, step.Nested);\n", ""),
    ("M46 a nested step loses its inline values", "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
     "subStep.TextSegments = BuildTextSegments(step);\n", ""),
    ("M47 a feature with one scenario the hooks did not see is guessed at", "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
     "if (featureGroup.All(x => x.WrittenBackground is null))", "if (featureGroup.Any(x => x.WrittenBackground is null))"),
    ("M48 a row differing in case is counted", "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
     "string.Equals(ran[count].Text, writtenBackground[count], StringComparison.Ordinal)", "string.Equals(ran[count].Text, writtenBackground[count], StringComparison.OrdinalIgnoreCase)"),
    ("M49 the hooks do not tell the collector what opened a step", "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
     "stepInfo.MultilineText, opened.Nested);", "stepInfo.MultilineText);", True),
    ("M50 what the reader left is one store for every outline", "src/Kronikol/Reports/report-select-row-function.js",
     "selectRowKept[prefix] || (selectRowKept[prefix] = ", "selectRowKept.all || (selectRowKept.all = ", "e2e"),
    ("M51 a click on the row already chosen puts back a composite's state", "src/Kronikol/Reports/report-select-row-function.js",
     "selectRowSyncSteps(activePanel, carrying ? kept.steps : Object.create(null));", "selectRowSyncSteps(activePanel, kept.steps);", "e2e"),
    ("M52 a composite holds only for its own failure", "src/Kronikol/Reports/report-select-row-function.js",
     "d.querySelector('.step-status.failed') !== null", "d.querySelector(':scope > summary .step-status.failed') !== null", "e2e"),
    ("M53 composites go by kind as sections do", "src/Kronikol/Reports/report-select-row-function.js",
     "if (!d.classList.contains('step-collapsible') && !selectRowHoldsFailure(d)) visit(d, d.classList[0]);", "if (!selectRowHoldsFailure(d)) visit(d, d.classList[0]);", "e2e"),
    ("M54 a frame left open is closed but not described under its parent", "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
     "            left.Parent?.Nested.Add(info);\n", "", True),
    ("M55 the public stopwatch key is not set", "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
     "_scenarioContext[ReqNRollConstants.StepStopwatchKey] = Open(stepType).Stopwatch;", "Open(stepType);", True),
    ("M56 the Specifications page is told the report's value", "src/Kronikol/Reports/ReportGenerator.cs",
     "backgroundOpensOnFailure: ReportToggleDefaultsResolver.BackgroundOpensOnFailure(options, specifications: true)", "backgroundOpensOnFailure: ReportToggleDefaultsResolver.BackgroundOpensOnFailure(options, specifications: false)"),
    ("M57 a row the search chooses or puts back keeps and brings what the reader left", "src/Kronikol/Reports/report-select-row-function.js",
     "var carrying = outgoing !== activePanel && !selectRowBySearch;", "var carrying = outgoing !== activePanel;", "e2e"),
    ("M58 the search opens every row's sections", "src/Kronikol/Reports/report-search-function.js",
     "if (section && !_sectionsRevealed.has(section)) { _sectionsRevealed.add(section); section.setAttribute('open', ''); }\n    });",
     "if (section && !_sectionsRevealed.has(section)) { _sectionsRevealed.add(section); section.setAttribute('open', ''); }\n    });\n    el.querySelectorAll('details.scenario-steps, details.scenario-background').forEach(function(section) { section.setAttribute('open', ''); });", "e2e"),
    ("M59 the search opens a section again on every keystroke", "src/Kronikol/Reports/report-search-function.js",
     "if (section && !_sectionsRevealed.has(section)) {", "if (section) {", "e2e"),
    ("M60 a new search does not open what the reader closed in the last", "src/Kronikol/Reports/report-search-function.js",
     "    _sectionsRevealed = new WeakSet();\n    _openedBySearch.forEach", "    _openedBySearch.forEach", "e2e"),
    ("M62 a row the search chooses brings what the reader left", "src/Kronikol/Reports/report-search-function.js",
     "    chosen.row = row;\n    click_search_row(row);", "    chosen.row = row;\n    row.click();", "e2e"),
    ("M63 the row the search puts back takes what it opened", "src/Kronikol/Reports/report-search-function.js",
     "!c.was.classList.contains('row-active')) click_search_row(c.was);", "!c.was.classList.contains('row-active')) c.was.click();", "e2e"),
    ("M64 the search does not open the diagrams", "src/Kronikol/Reports/report-search-function.js",
     "[el.querySelector('details.example-diagrams'), shown.querySelector('details.scenario-steps')", "[shown.querySelector('details.scenario-steps')", "e2e"),
    ("M66 the last hook does not come back for a step a suite's hook left open", "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
     "        _stopwatch?.Stop();\n        CloseStepsLeftOpen();\n", "        _stopwatch?.Stop();\n", True),
    ("M67 a step left open is listed again when the last hook comes back", "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
     "StepsCollectionKey]);\n        _scenarioContext.Remove(ReqNRollConstants.OpenStepKey);\n", "StepsCollectionKey]);\n", True),
    ("M16 query's hint reads Steps alone", "src/Kronikol/Query/QueryCommand.Narrative.cs",
     "scenario.BackgroundSteps.Count + scenario.Steps.Count > 0", "scenario.Steps.Count > 0"),
    ("M19 scenarios built by hand lose the guess", "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
     "if (featureGroup.All(x => x.WrittenBackground is null))", "if (!featureGroup.Any())"),
    ("M20 a section set to stay closed opens for a failure", "src/Kronikol/Reports/ReportGenerator.cs",
     "var holdsFailure = backgroundOpensOnFailure && background.Any(HasAnyFailed);", "var holdsFailure = background.Any(HasAnyFailed);"),
    ("M22 a failed step whose sub-steps passed starts closed", "src/Kronikol/Reports/ReportGenerator.cs",
     "if (step.Status == ExecutionResult.Failed) return true;\n        if (step.SubSteps is not { Length: > 0 }) return false;\n        foreach (var sub in step.SubSteps)\n        {\n",
     "if (step.SubSteps is not { Length: > 0 }) return false;\n        foreach (var sub in step.SubSteps)\n        {\n            if (sub.Status == ExecutionResult.Failed) return true;\n"),
    ("M25 a step left open is not closed as the AfterScenario hooks begin", "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
     "    [AfterScenario(Order = int.MinValue)]\n    internal void CloseStepsLeftOpen()", "    internal void CloseStepsLeftOpen()", True),
    ("M61 the hook that closes what a suite left open is a public method", "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
     "    internal void CloseStepsLeftOpen()", "    public void CloseStepsLeftOpen()"),
    ("M26 a step a step ran leaves the list a suite can read", "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
     "parent?.Nested.Add(info);\n        steps.Add(info);\n    }\n\n    private static ReqNRollStepInfo Describe",
     "parent?.Nested.Add(info);\n        if (parent is null) steps.Add(info);\n    }\n\n    private static ReqNRollStepInfo Describe", True),
]


def run(*args):
    return subprocess.run(args, cwd=root, capture_output=True, encoding="utf-8", errors="replace")


lines = []
only = set(sys.argv[3:])
for name, path, old, new, *real in MUTATIONS:
    if only and name.split()[0] not in only:
        continue
    target = root / path
    original = target.read_bytes()
    text = original.decode("utf-8")
    assert old in text, f"{name}: not found"
    target.write_bytes(text.replace(old, new, 1).encode("utf-8"))
    try:
        project, filter_by = (E2E, E2E_FILTER) if real == ["e2e"] else (REAL, REAL_FILTER) if real else (TESTS, FILTER)
        build = run("dotnet", "build", project, "-c", "Release", "--no-restore", "-p:GeneratePackageOnBuild=false")
        if "Build succeeded" not in build.stdout:
            lines.append(f"{name}: DID NOT BUILD")
            continue
        test = run("dotnet", "test", project, "-c", "Release", "--no-build", "--filter", filter_by)
        failed = sorted({l.strip().split(" [")[0].replace("Failed ", "").split(".")[-1] for l in test.stdout.splitlines() if l.strip().startswith("Failed ") and "." in l})
        verdict = "CAUGHT by " + ", ".join(failed) if failed else "SURVIVED"
        lines.append(f"{name}: {verdict}")
    finally:
        target.write_bytes(original)
    out.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
run("dotnet", "build", TESTS, "-c", "Release", "--no-restore", "-p:GeneratePackageOnBuild=false")
test = run("dotnet", "test", TESTS, "-c", "Release", "--no-build", "--filter", FILTER)
lines.append("restored, facts: " + test.stdout.strip().splitlines()[-1][:110])
run("dotnet", "build", REAL, "-c", "Release", "--no-restore", "-p:GeneratePackageOnBuild=false")
test = run("dotnet", "test", REAL, "-c", "Release", "--no-build", "--filter", REAL_FILTER)
lines.append("restored, a real run's facts: " + test.stdout.strip().splitlines()[-1][:110])
out.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
print("\n".join(lines))
