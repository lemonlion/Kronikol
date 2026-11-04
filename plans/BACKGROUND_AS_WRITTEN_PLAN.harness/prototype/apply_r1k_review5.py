#!/usr/bin/env python
"""The fifth review's faults, in the source (apply_r1k_review5.py <checkout>; the facts are apply_r1k_facts.py).

A nested step is paired by what opened it and not by its text. A step a suite's hook stopped before ours opened it takes its
place among the steps when it closes. Nothing on the public toggle record moves: only the options can tell a section left unset
from one set to false, so they say so apart. The row script keeps what the reader left for as long as the page is open, and a
failed composite holds as a section does. The filter search opens what its match needs after it has chosen the row.
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


# The collector carries what opened a step to the report's step.
edit(
    "src/Kronikol/Tracking/StepCollector.cs",
    (
        """    public static void StartStep(string? testId, string? keyword, string text, string[]? paramNames, object?[]? paramValues,
        string[][]? table, string? docString)
    {
""",
        """    public static void StartStep(string? testId, string? keyword, string text, string[]? paramNames, object?[]? paramValues,
        string[][]? table, string? docString)
        => StartStep(testId, keyword, text, paramNames, paramValues, table, docString, openedBy: null);

    /// <summary>
    /// The same, saying what opened the step, which reaches <see cref="ScenarioStep.OpenedBy"/>: an adapter that hears of a
    /// step twice can then tell its own from one that reads the same.
    /// </summary>
    internal static void StartStep(string? testId, string? keyword, string text, string[]? paramNames, object?[]? paramValues,
        string[][]? table, string? docString, object? openedBy)
    {
""",
    ),
    (
        """            Parameters = BuildParameters(paramNames, paramValues)
        };

        var isTopLevel = false;
""",
        """            Parameters = BuildParameters(paramNames, paramValues),
            OpenedBy = openedBy
        };

        var isTopLevel = false;
""",
    ),
    (
        """            Attachments = step.Attachments is { Count: > 0 }
                ? step.Attachments.ToArray()
                : null
        };
    }

    /// <summary>
    /// Wraps an async step's returned Task so that CompleteStep is called on
""",
        """            Attachments = step.Attachments is { Count: > 0 }
                ? step.Attachments.ToArray()
                : null,
            OpenedBy = step.OpenedBy
        };
    }

    /// <summary>
    /// Wraps an async step's returned Task so that CompleteStep is called on
""",
    ),
    (
        """        public List<FileAttachment>? Attachments { get; set; }
    }
}
""",
        """        public List<FileAttachment>? Attachments { get; set; }
        public object? OpenedBy { get; set; }
    }
}
""",
    ),
)

edit(
    "src/Kronikol/Reports/ScenarioStep.cs",
    (
        """    public string? DocStringMediaType { get; set; }

""",
        """    public string? DocStringMediaType { get; set; }

    /// <summary>What opened this step, where an adapter has to tell it from one that reads the same (#222). Never written out.</summary>
    internal object? OpenedBy { get; set; }

""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollStepInfo.cs",
    (
        """    /// <summary>The steps this step's definition ran, in the order they finished.</summary>
""",
        """    /// <summary>The steps this step's definition ran, in the order they finished. Each step that opened a frame has its own list,
    /// and StepCollector was told that list as what opened the step, which is how the two are paired.</summary>
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
    (
        """        TestPhaseContext.Current = PhaseConfiguration.ResolvePhaseFromStepType(stepType);
        var stepStopwatch = Stopwatch.StartNew();
        _scenarioContext[ReqNRollConstants.StepStopwatchKey] = stepStopwatch;
        var opened = new OpenStep(stepStopwatch, InnermostOpenStep, _scenarioContext.StepContext.StepInfo);
        _scenarioContext[ReqNRollConstants.OpenStepKey] = opened;
        _scenarioContext.StepContext[ReqNRollConstants.OpenStepKey] = opened;

        // Push a step onto StepCollector so assertion tracking sub-steps attach here. The step's
        // Gherkin arguments ride along so the delimiter bar can draw them — the table Reqnroll hands
        // an outline step already has its <placeholders> substituted.
        var scenarioId = (string)_scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey];
        var stepInfo = _scenarioContext.StepContext.StepInfo;
        StepCollector.StartStep(scenarioId, stepType, stepInfo.Text, null, null,
            ReqNRollStepArguments.ToBarRows(stepInfo.Table), stepInfo.MultilineText);
    }
""",
        """        TestPhaseContext.Current = PhaseConfiguration.ResolvePhaseFromStepType(stepType);
        Open(stepType);
    }

    private OpenStep Open(string stepType)
    {
        var stepStopwatch = Stopwatch.StartNew();
        _scenarioContext[ReqNRollConstants.StepStopwatchKey] = stepStopwatch;
        var opened = new OpenStep(stepStopwatch, InnermostOpenStep, _scenarioContext.StepContext.StepInfo);
        _scenarioContext[ReqNRollConstants.OpenStepKey] = opened;
        _scenarioContext.StepContext[ReqNRollConstants.OpenStepKey] = opened;

        // Push a step onto StepCollector so assertion tracking sub-steps attach here. The step's
        // Gherkin arguments ride along so the delimiter bar can draw them — the table Reqnroll hands
        // an outline step already has its <placeholders> substituted.
        var scenarioId = (string)_scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey];
        var stepInfo = _scenarioContext.StepContext.StepInfo;
        StepCollector.StartStep(scenarioId, stepType, stepInfo.Text, null, null,
            ReqNRollStepArguments.ToBarRows(stepInfo.Table), stepInfo.MultilineText, opened.Nested);
        return opened;
    }
""",
    ),
    (
        """        // None when a suite's hook sorted ahead of BeforeStep and threw: Reqnroll still runs this, and the step is still the
        // one that failed, so it is listed, with no frame of the collector's to close and no time.
        var stepContext = _scenarioContext.StepContext;
        stepContext.TryGetValue(ReqNRollConstants.OpenStepKey, out OpenStep? opened);
        opened?.Stopwatch.Stop();
""",
        """        // None when a suite's hook sorted ahead of BeforeStep and threw: Reqnroll still runs this, and the step is still the
        // one that failed, so it is listed, with no frame of the collector's to close and no time. Under a step that ran it, it
        // still has its place among the steps that one ran, so it is opened here to close at that place.
        var stepContext = _scenarioContext.StepContext;
        stepContext.TryGetValue(ReqNRollConstants.OpenStepKey, out OpenStep? opened);
        if (opened is null && InnermostOpenStep is not null)
            opened = Open(stepContext.StepInfo.StepInstance.StepDefinitionKeyword.ToString());
        opened?.Stopwatch.Stop();
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
    (
        """    /// The steps a step definition ran, as the hooks saw them. The collector holds their place among the woven steps, but
    /// not their table, doc string or inline values. Each is found in order, by its text, under any woven step between. One a
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

        void Walk(ScenarioStep[] steps)
        {
            foreach (var subStep in steps)
            {
                if (next >= nested.Count || string.IsNullOrEmpty(subStep.Keyword) || !string.Equals(subStep.Text, nested[next].Text, StringComparison.Ordinal))
                {
                    Walk(subStep.SubSteps ?? []);
                    continue;
                }

                var step = nested[next++];
                subStep.Status = step.Status.ToStepResult(false);
                subStep.DocString = step.DocString;
                subStep.Parameters = ParseTableText(step.TableText) ?? subStep.Parameters;
                subStep.TextSegments = BuildTextSegments(step);
                subStep.SubSteps = DescribeNested(subStep.SubSteps, step.Nested);
            }
        }
    }
""",
        """    /// The steps a step definition ran, as the hooks saw them. The collector holds their place among the woven steps, but
    /// not their table, doc string or inline values. Each is found by what opened it, so a woven step that reads the same stays
    /// what it was, under any woven step between.
    /// </summary>
    private static ScenarioStep[]? DescribeNested(ScenarioStep[]? subSteps, IReadOnlyList<ReqNRollStepInfo>? nested)
    {
        if (subSteps is null || nested is not { Count: > 0 })
            return subSteps;

        foreach (var subStep in subSteps)
        {
            var step = subStep.OpenedBy is null ? null : nested.FirstOrDefault(s => ReferenceEquals(s.Nested, subStep.OpenedBy));
            if (step is null)
            {
                DescribeNested(subStep.SubSteps, nested);
                continue;
            }

            subStep.Status = step.Status.ToStepResult(false);
            subStep.DocString = step.DocString;
            subStep.Parameters = ParseTableText(step.TableText) ?? subStep.Parameters;
            subStep.TextSegments = BuildTextSegments(step);
            DescribeNested(subStep.SubSteps, step.Nested);
        }

        return subSteps;
    }
""",
    ),
)

# The public record goes back to what it is on published; the options say whether the section was left unset.
edit(
    "src/Kronikol/Reports/ReportToggleDefaultsResolver.cs",
    (
        "public static ResolvedToggleDefaults BuiltIn { get; } = new() { BackgroundStepsOpenOnFailure = true };",
        "public static ResolvedToggleDefaults BuiltIn { get; } = new();",
    ),
    (
        """    public bool BackgroundStepsOpen
    {
        get;
        init
        {
            field = value;
            BackgroundStepsOpenOnFailure = false;
        }
    }
    /// <summary>Whether a section holding a failed step opens though <see cref="BackgroundStepsOpen"/> is false: so when
    /// <see cref="ReportToggleDefaults.BackgroundStepsOpen"/> was left unset (#222). A value set there, on a record built by
    /// hand or on a copy of <see cref="BuiltIn"/>, says what it says, which is why setting that property clears this.</summary>
    internal bool BackgroundStepsOpenOnFailure { get; init; }
""",
        """    public bool BackgroundStepsOpen { get; init; }
""",
    ),
    (
        """        BackgroundStepsOpenOnFailure = overrides.BackgroundStepsOpen is null && baseline.BackgroundStepsOpenOnFailure,
""",
        "",
    ),
    (
        """    private static ResolvedToggleDefaults Overlay(ResolvedToggleDefaults baseline, ReportToggleDefaults overrides) => new()
""",
        """    /// <summary>
    /// Whether a report's separate Background section holding a failed step starts open though
    /// <see cref="ResolvedToggleDefaults.BackgroundStepsOpen"/> is false: so when <see cref="ReportToggleDefaults.BackgroundStepsOpen"/>
    /// was left unset (#222). Only the options can tell unset from false, so a value set says what it says, as does a record
    /// handed to <c>GenerateHtmlReport</c>.
    /// </summary>
    internal static bool BackgroundOpensOnFailure(ReportConfigurationOptions options, bool specifications) =>
        options.TestRunReportToggleDefaults.BackgroundStepsOpen is null
        && (!specifications || options.SpecificationsToggleDefaults.BackgroundStepsOpen is null);

    private static ResolvedToggleDefaults Overlay(ResolvedToggleDefaults baseline, ReportToggleDefaults overrides) => new()
""",
    ),
)

edit(
    "src/Kronikol/Reports/ReportGenerator.cs",
    (
        "toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: true)",
        "toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: true), backgroundOpensOnFailure: ReportToggleDefaultsResolver.BackgroundOpensOnFailure(options, specifications: true)",
    ),
    (
        "toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: false)",
        "toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: false), backgroundOpensOnFailure: ReportToggleDefaultsResolver.BackgroundOpensOnFailure(options, specifications: false)",
    ),
    (
        """        IReadOnlyDictionary<Guid, string>? backgroundPaths = null)
    {
        // The Specifications page describes the tests, so a scenario that is not a test is not in it.
""",
        """        IReadOnlyDictionary<Guid, string>? backgroundPaths = null,
        bool backgroundOpensOnFailure = false)
    {
        // The Specifications page describes the tests, so a scenario that is not a test is not in it.
""",
    ),
    (
        """                        toggleDefaults: toggles,
                        // Without this the flat outline rows compute their ids with no suite while the
""",
        """                        toggleDefaults: toggles,
                        backgroundOpensOnFailure: backgroundOpensOnFailure,
                        // Without this the flat outline rows compute their ids with no suite while the
""",
    ),
    (
        """                RenderScenarioStepSections(body, scenario, showStepNumbers, separateBackgroundSteps, collapseRepeatedStepKeywords,
                    toggles.StepsSectionOpen, toggles.BackgroundStepsOpen, stepDeferral, toggles.BackgroundStepsOpenOnFailure);
""",
        """                RenderScenarioStepSections(body, scenario, showStepNumbers, separateBackgroundSteps, collapseRepeatedStepKeywords,
                    toggles.StepsSectionOpen, toggles.BackgroundStepsOpen, stepDeferral, backgroundOpensOnFailure);
""",
    ),
    (
        """        WarmUpResult? warmUp = null,
        StepDeferral? stepDeferral = null)
    {
        var toggles = toggleDefaults ?? ResolvedToggleDefaults.BuiltIn;
""",
        """        WarmUpResult? warmUp = null,
        StepDeferral? stepDeferral = null,
        bool backgroundOpensOnFailure = false)
    {
        var toggles = toggleDefaults ?? ResolvedToggleDefaults.BuiltIn;
""",
    ),
    (
        """                RenderScenarioStepSections(body, s, showStepNumbers, separateBackgroundSteps, collapseRepeatedStepKeywords,
                    toggles.StepsSectionOpen, toggles.BackgroundStepsOpen, stepDeferral, toggles.BackgroundStepsOpenOnFailure);
""",
        """                RenderScenarioStepSections(body, s, showStepNumbers, separateBackgroundSteps, collapseRepeatedStepKeywords,
                    toggles.StepsSectionOpen, toggles.BackgroundStepsOpen, stepDeferral, backgroundOpensOnFailure);
""",
    ),
)

# The row script: what the reader left stays, and a failure holds on a composite as on a section.
edit(
    "src/Kronikol/Reports/report-select-row-function.js",
    (
        """function selectRow(clickedRow, prefix) {
""",
        """// What the reader left open or closed in each outline's rows, for as long as the page is open. It outlives a switch, so a
// choice carries past a row that lacks that <details> or holds a failure there.
var selectRowKept = Object.create(null);
function selectRow(clickedRow, prefix) {
""",
    ),
    (
        """    // Capture outgoing panel's <details> open states before hiding
""",
        """    var kept = selectRowKept[prefix] || (selectRowKept[prefix] = { sections: Object.create(null), steps: Object.create(null) });
    // Capture outgoing panel's <details> open states before hiding
""",
    ),
    (
        """    // A composite step's state goes by its place among the steps: a panel draws a closed one's sub-steps only when it is
    // first opened (5.0.0, plans/REPORT_FORMAT_PLAN.md 4.8), so counted by position an undrawn subtree would shift every
    // state after it. The other <details> go by their kind and their place among that kind, as a row can lack a kind the
    // row left has: one that never reached its steps has a Failure Result where the row left has its Background.
    var detailStates = {};
    var stepStates = {};
    if (outgoing && outgoing !== activePanel) {
        outgoing.querySelectorAll('details').forEach(function(d) {
            if (d.classList.contains('step-collapsible')) stepStates[selectRowStepPath(d, outgoing)] = d.open;
            else (detailStates[d.className] = detailStates[d.className] || []).push(d);
        });
    }
""",
        """    // A composite step's state goes by its place among the steps: a panel draws a closed one's sub-steps only when it is
    // first opened (5.0.0, plans/REPORT_FORMAT_PLAN.md 4.8), so counted by position an undrawn subtree would shift every
    // state after it. The other <details> go by their kind and their place among that kind, as a row can lack a kind the
    // row left has: one that never reached its steps has a Failure Result where the row left has its Background.
    var switching = outgoing !== activePanel;
    if (outgoing && switching) {
        selectRowEachSection(outgoing, function(d, key) { kept.sections[key] = d.open; });
        outgoing.querySelectorAll('details.step-collapsible').forEach(function(d) {
            if (!selectRowHoldsFailure(d)) kept.steps[selectRowStepPath(d, outgoing)] = d.open;
        });
    }
""",
    ),
    (
        """        // Sync <details> open states from outgoing panel
        var seen = {};
        Array.prototype.forEach.call(activePanel.querySelectorAll('details'), function(d) {
            if (d.classList.contains('step-collapsible')) return;
            var place = seen[d.className] = (seen[d.className] || 0) + 1;
            var left = (detailStates[d.className] || [])[place - 1];
            // What the reader did to the row left carries over. A Background section holding a failure is open for that
            // reason and not by the reader's hand, so nothing crosses to or from one, and each row keeps its own.
            if (left && !left.hasAttribute('data-holds-failure') && !d.hasAttribute('data-holds-failure')) d.open = left.open;
        });
        selectRowSyncSteps(activePanel, stepStates);
""",
        """        // Sync <details> open states from what the reader left
        if (switching) selectRowEachSection(activePanel, function(d, key) { if (key in kept.sections) d.open = kept.sections[key]; });
        selectRowSyncSteps(activePanel, switching ? kept.steps : Object.create(null));
""",
    ),
    (
        """// A composite step's place among the steps of `panel`: its index, and that of each composite above it, among its
""",
        """// A <details> holding a failure is open for that reason and not by the reader's hand, so nothing crosses to or from it, and
// each row keeps its own: a Background section the generator marks, as one set to stay closed has no such hold, and a
// composite step with a failed step in it, which always starts open and always has its sub-steps drawn.
function selectRowHoldsFailure(d) {
    return d.hasAttribute('data-holds-failure') || (d.classList.contains('step-collapsible') && d.querySelector('.step-status.failed') !== null);
}

// Each <details> of `panel` but the composite steps and those holding a failure, with its kind and its place among that kind.
function selectRowEachSection(panel, visit) {
    var seen = Object.create(null);
    panel.querySelectorAll('details:not(.step-collapsible)').forEach(function(d) {
        var kind = d.classList[0];
        seen[kind] = (seen[kind] || 0) + 1;
        if (!selectRowHoldsFailure(d)) visit(d, kind + ' ' + seen[kind]);
    });
}

// A composite step's place among the steps of `panel`: its index, and that of each composite above it, among its
""",
    ),
    (
        """            var open = states[selectRowStepPath(d, panel)];
""",
        """            if (selectRowHoldsFailure(d)) return;
            var open = states[selectRowStepPath(d, panel)];
""",
    ),
)

edit(
    "src/Kronikol/Reports/report-search-function.js",
    (
        """    ['details.example-diagrams', 'details.scenario-steps', 'details.scenario-background'].forEach(function(sel) {
        var section = el.querySelector(sel);
        if (section) section.setAttribute('open', '');
    });
}
""",
        """    revealMatchSections(el);
}

// Every row's, as the search may choose any of them: choosing one gives it what the reader left, so this runs again after that.
function revealMatchSections(el) {
    ['details.example-diagrams', 'details.scenario-steps', 'details.scenario-background'].forEach(function(sel) {
        el.querySelectorAll(sel).forEach(function(section) { section.setAttribute('open', ''); });
    });
}
""",
    ),
    (
        """    highlight_search_rows(r.words);
}

// The search's own query (the owner, 2026-10-27: a search gives a page of results, and a result picked goes to it; a
""",
        """    highlight_search_rows(r.words);
    if (r.count === 1 && r.single) revealMatchSections(r.single);
}

// The search's own query (the owner, 2026-10-27: a search gives a page of results, and a result picked goes to it; a
""",
    ),
)

print("source edited")
