#!/usr/bin/env python
# R13, on R12b (2720adf0). usage: apply_r13.py <worktree> tests|code
# The fifth review measured four faults in what R11 and R12 added for #129 (the forced read, the pass-over, the matcher), all
# regressions against published on an ingest or a hand-built bar. They leave this plan: #129 stays open, the walk and its matcher
# are published's again, and the matcher stays with #225's R2 and #222's R1. What stays is the fifth review's other finding: a
# stamp naming no row of its scenario (a row rebuilt by a consumer among collected rows) says nothing, and the walk reads it.
import pathlib, sys

root = pathlib.Path(sys.argv[1])
stage = sys.argv[2]


def edit(path, change):
    p = root / path
    raw = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in raw
    t = change(raw.replace("\r\n", "\n"))
    p.write_bytes((t.replace("\n", "\r\n") if crlf else t).encode("utf-8"))


def cut(t, start, end):
    a = t.index(start)
    b = t.index(end, a) + len(end)
    return t[:a] + t[b:]


def cut_to(t, start, stop):
    a = t.index(start)
    return t[:a] + t[t.index(stop, a):]


def replace(t, old, new):
    assert t.count(old) == 1, old[:80]
    return t.replace(old, new)


g = "src/Kronikol/Reports/ReportGenerator.cs"
facts = "tests/Kronikol.Tests/Reports/ReportAttributionTests.cs"

if stage == "tests":
    # #129's and the walk's facts leave, and the new one goes in
    edit(facts, lambda t: cut_to(cut_to(
        t,
        "    [Fact]\n    public void A_mismatched_step_marker_reaches_the_report_it_happened_in()\n",
        "    [Fact]\n    public void Background_steps_are_numbered_before_the_scenario_steps()\n"),
        "    [Fact]\n    public void A_background_step_that_drew_no_bar_is_passed_over()\n",
        "    // ─── Helpers ─"))
    edit(facts, lambda t: replace(t, "    // ─── Helpers ─", """    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_row_rebuilt_by_hand_among_collected_rows_keeps_its_calls(bool intoTheBackground)
    {
        // An adapter of a consumer's own: the collector's rows, with one rebuilt. It has no id, so its stamps name no row, and they
        // say nothing: the bar it drew and its text place its calls, as they did before anything was stamped.
        var testId = "attr-mixed-" + Guid.NewGuid().ToString("N");
        void Step(string keyword, string text, string service)
        {
            StepCollector.StartStep(testId, keyword, text, null, null);
            RequestResponseLogger.Log(Real(testId, service) with { Timestamp = Start.AddSeconds(1) });
            StepCollector.CompleteStep(testId, true);
        }

        Step("Given", "a shop", "shop");
        Step("When", "a basket", "basket");
        Step("Then", "it is empty", "check");
        var collected = StepCollector.GetSteps(testId);
        var scenario = new Scenario { Id = testId, DisplayName = "S" };
        if (intoTheBackground)
        {
            scenario.BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "a shop" }];
            scenario.Steps = [collected[1], collected[2]];
        }
        else
        {
            scenario.Steps = [collected[0], new ScenarioStep { Keyword = "When", Text = "a basket" }, collected[2]];
        }

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        var paths = ReportGenerator.AttributeInteractionsToStepPaths(logs, [new Feature { DisplayName = "F", Scenarios = [scenario] }])[testId];

        Assert.Equal(intoTheBackground ? ["b0", "0", "1"] : ["0", "1", "2"], paths);
    }

    // ─── Helpers ─"""))

elif stage == "code":
    # Back to published: the forced read, the pass-over and its mark, the matcher
    edit(g, lambda t: cut(
        t,
        "        // Before the one copy of the diagnostics every output reads, or a mismatch reaches none of them (#129).\n",
        "            _ = attribution.Value;\n\n"))
    edit(g, lambda t: cut(
        t,
        "                        // A tests file's step that draws no bar: a bar that is not its own passes over it. A capture can have\n",
        "                            stepMarkerCount++;\n\n"))
    edit(g, lambda t: replace(replace(replace(replace(
        t,
        "    private static List<(string Path, string Text, bool DrawsNoBar)> OrderedStepPaths(Scenario scenario)",
        "    private static List<(string Path, string Text)> OrderedStepPaths(Scenario scenario)"),
        "        var ordered = new List<(string, string, bool)>();", "        var ordered = new List<(string, string)>();"),
        'ordered.Add(($"b{i}", scenario.BackgroundSteps![i].Text, scenario.BackgroundSteps[i].DrawsNoBar));', 'ordered.Add(($"b{i}", scenario.BackgroundSteps![i].Text));'),
        'ordered.Add(($"{i}", scenario.Steps![i].Text, scenario.Steps[i].DrawsNoBar));', 'ordered.Add(($"{i}", scenario.Steps![i].Text));'))
    fold_end = '.Replace(" ", "", StringComparison.Ordinal);\n'
    edit(g, lambda t: (
        t[:t.index('        var firstLine = stepText.Replace("\\r", "").Trim()')]
        + "        var marker = plantUml.Replace('\\n', ' ').Trim();\n"
        + "        return marker.Contains(stepText, StringComparison.OrdinalIgnoreCase)\n"
        + "               || marker.Contains(StepText.CapitaliseIfEnabled(stepText) ?? stepText, StringComparison.OrdinalIgnoreCase);\n"
        + "    }\n"
        + t[t.index(fold_end, t.index("private static string FoldStepBar")) + len(fold_end):]))
    edit("src/Kronikol/Reports/ScenarioStep.cs", lambda t: cut(
        t,
        "\n    /// <summary>A tests file's step the replay draws no bar for",
        "    internal bool DrawsNoBar { get; init; }\n"))
    edit("src/Kronikol/Ingestion/FeatureSynthesizer.cs", lambda t: replace(t, "            DrawsNoBar = !record.IsDiagramMarker,\n", ""))

    # A stamp says something only where it names a row of the scenario
    edit(g, lambda t: replace(t, """                    // The step open when the call was logged, where this scenario's steps carry their ids. With none open,
                    // or no ids to go by, the step before it.
                    if (collected is not null && log.OpenStep is { } open)
                        current = collected.GetValueOrDefault(open);
""", """                    // The step open when the call was logged, where that is a row of this scenario. With none open, no ids to
                    // go by or a row rebuilt by hand, the step before it.
                    if (collected is not null && log.OpenStep is { } open && collected.TryGetValue(open, out var stamped))
                        current = stamped;
"""))
    edit(g, lambda t: replace(t, """                    // A bar a collected step drew names its step, with no counting and no reading of its text.
                    case DiagramMarkerKind.Step when collected is not null && log.OpenStep is { } drawn:
                        current = collected.GetValueOrDefault(drawn);
                        break;
""", """                    // A bar a collected step drew names its step, with no counting and no reading of its text. Anything else
                    // is read at the place the last bar left.
                    case DiagramMarkerKind.Step when collected is not null && log.OpenStep is { } drawn && collected.TryGetValue(drawn, out var named):
                        current = named;
                        stepMarkerCount = ordered.FindIndex(step => step.Path == named) + 1;
                        break;
"""))
else:
    raise SystemExit("tests or code")
print("applied", stage)
