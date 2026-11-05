#!/usr/bin/env python
# R14, on R13 (f9dda54a). usage: apply_r14.py <worktree> tests|code
# A narrow review of R13 measured that its rule (a stamp naming no row reads as no stamp) moved calls in live adapters: with bars
# off a call in a hidden or later step took the row before it, and with bars on a later example row's bar took a row by its text.
# So that rule goes, a stamp naming no row reads no step again, and the case it was for goes the other way: a scenario with a
# top-level row that ran and carries no id (a consumer's, rebuilt among collected rows) is read by the walk whole, so it reads as
# published by construction. That needs BDDfy's and LightBDD's rows that did not run to say so, as Reqnroll's do.
import pathlib, sys

root = pathlib.Path(sys.argv[1])
stage = sys.argv[2]


def edit(path, change):
    p = root / path
    raw = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in raw
    t = change(raw.replace("\r\n", "\n"))
    p.write_bytes((t.replace("\n", "\r\n") if crlf else t).encode("utf-8"))


def replace(t, old, new):
    assert t.count(old) == 1, (t.count(old), old[:80])
    return t.replace(old, new)


g = "src/Kronikol/Reports/ReportGenerator.cs"

if stage == "tests":
    edit("tests/Kronikol.Tests/Reports/ReportAttributionTests.cs", lambda t: replace(t, "    // ─── Helpers ─", """    [Fact]
    public void A_stamp_that_names_no_row_reads_no_step()
    {
        // A hidden step, a later run of a step or a step still open at report time: the row before it did not make that call.
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios = [new Scenario { Id = "t4", DisplayName = "S", Steps = [new ScenarioStep { CollectedId = 1, Keyword = "Given", Text = "a basket" }] }],
            }
        };
        var logs = new[]
        {
            Real("t4", "svc-a") with { OpenStep = 1 },
            Real("t4", "svc-b") with { OpenStep = 99 },
        };

        Assert.Equal(["0", null], ReportGenerator.AttributeInteractionsToStepPaths(logs, features)["t4"]);
    }

    [Fact]
    public void A_row_that_did_not_run_leaves_the_others_read_by_their_stamps()
    {
        // A bar drawn for another step, so the walk would refuse it: only the stamp can place this call.
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t5",
                        DisplayName = "S",
                        Steps =
                        [
                            new ScenarioStep { CollectedId = 1, Keyword = "Given", Text = "a basket" },
                            new ScenarioStep { DidNotRun = true, Keyword = "Then", Text = "it is empty" },
                        ],
                    },
                ],
            }
        };
        var logs = new[]
        {
            Marker("t5", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>a hook's step") with { OpenStep = 1 },
            Real("t5", "svc-a") with { OpenStep = 1 },
        };

        Assert.Equal(["0"], ReportGenerator.AttributeInteractionsToStepPaths(logs, features)["t5"]);
    }

    // ─── Helpers ─"""))

elif stage == "code":
    edit(g, lambda t: replace(t, """                    // The step open when the call was logged, where that is a row of this scenario. With none open, no ids to
                    // go by or a row rebuilt by hand, the step before it.
                    if (collected is not null && log.OpenStep is { } open && collected.TryGetValue(open, out var stamped))
                        current = stamped;
""", """                    // The step open when the call was logged, where this scenario's steps carry their ids. With none open,
                    // or no ids to go by, the step before it.
                    if (collected is not null && log.OpenStep is { } open)
                        current = collected.GetValueOrDefault(open);
"""))
    edit(g, lambda t: replace(t, """                    // A bar a collected step drew names its step, with no counting and no reading of its text. Anything else
                    // is read at the place the last bar left.
                    case DiagramMarkerKind.Step when collected is not null && log.OpenStep is { } drawn && collected.TryGetValue(drawn, out var named):
                        current = named;
                        stepMarkerCount = ordered.FindIndex(step => step.Path == named) + 1;
                        break;
""", """                    // A bar a collected step drew names its step, with no counting and no reading of its text.
                    case DiagramMarkerKind.Step when collected is not null && log.OpenStep is { } drawn:
                        current = collected.GetValueOrDefault(drawn);
                        break;
"""))
    edit(g, lambda t: replace(t, """    /// The address a call under each collected step gets: the top-level step it is in. Null for a scenario whose top-level
    /// steps carry no ids (built by hand through the public model, or paired by position, whether or not the collector's
    /// sub-steps are under them), whose calls are read against the bars: a stamp could name a sub-step there and nothing else.
    /// </summary>
    private static Dictionary<int, string?>? CollectedStepPaths(Scenario scenario)
    {
        var background = scenario.BackgroundSteps ?? [];
        var own = scenario.Steps ?? [];
        if (!background.Concat(own).Any(step => step.CollectedId is not null))
            return null;
""", """    /// The address a call under each collected step gets: the top-level step it is in. Null for a scenario with a top-level
    /// step that ran and carries no id (built by hand through the public model, paired by position, or rebuilt among collected
    /// steps), whose calls are all read against the bars, as a stamp could not name that step.
    /// </summary>
    private static Dictionary<int, string?>? CollectedStepPaths(Scenario scenario)
    {
        var background = scenario.BackgroundSteps ?? [];
        var own = scenario.Steps ?? [];
        var rows = background.Concat(own).ToArray();
        if (rows.Length == 0 || rows.Any(step => step.CollectedId is null && !step.DidNotRun))
            return null;
"""))
    # The diagnostics copy back where it was
    edit(g, lambda t: replace(replace(t, """            () => AttributeInteractionsToSteps(dataLogs, features));

        var reportDiagnostics = ReportDiagnosticsScope.Current?.Entries ?? [];
""", """            () => AttributeInteractionsToSteps(dataLogs, features));
"""), """        // Which call happened under which step, and the annotations worth exporting: read by the data file
""", """        var reportDiagnostics = ReportDiagnosticsScope.Current?.Entries ?? [];

        // Which call happened under which step, and the annotations worth exporting: read by the data file
"""))
    # BDDfy's and LightBDD's rows that did not run say so, as Reqnroll's do
    edit("src/Kronikol.BDDfy.xUnit3/ScenarioInfoExtensions.cs", lambda t: replace(t, """            var mapped = new ScenarioStep
            {
                CollectedId = collected?.CollectedId,
                Keyword = step.Keyword,""", """            var mapped = new ScenarioStep
            {
                CollectedId = collected?.CollectedId,
                DidNotRun = collected is null,
                Keyword = step.Keyword,"""))
    edit("src/Kronikol.LightBDD.Core/FeatureResultExtensions.cs", lambda t: replace(t, """            var mapped = MapStep(step, priorFailure, scenarioSkipped, collected);
""", """            var mapped = MapStep(step, priorFailure, scenarioSkipped, collected) with { DidNotRun = collected is null };
"""))
else:
    raise SystemExit("tests or code")
print("applied", stage)
