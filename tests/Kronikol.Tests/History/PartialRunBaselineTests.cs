using Kronikol.History;

namespace Kronikol.Tests.History;

/// <summary>
/// #75 sections 3 to 5 (plans/HISTORY_VERDICT_NOISE_PLAN.md S3). A partial run's pass or fail is a fact
/// about the scenario; its duration relative to the run and its set of captured calls are facts about
/// the run's conditions. So status keeps reading partial runs, and duration and behaviour stop: a
/// filtered run is alone on the machine, or pays the cold start with fewer scenarios to spread it over,
/// and with one worker running a window attribution is suddenly exclusive for everything.
/// </summary>
public class PartialRunBaselineTests
{
    private static readonly string[] Full = Enumerable.Range(1, 20).Select(i => $"bbbb{i:D12}").ToArray();
    private static readonly string[] Subset = Full.Take(5).ToArray();

    private static HistoryRoster Roster(string[] ids) =>
        HistoryRoster.Create("Suite", ids.Select(id => new HistoryRosterEntry(id, "Scenario " + id, "Feature", null)).ToArray());

    private static HistoryRun Run(HistoryRoster roster, int n, int?[]? durations, bool partial = false, string[]? shapes = null, int[]? calls = null,
        string? results = null, IReadOnlyList<int>[]? callSets = null, string? shapesHash = null) => new()
    {
        Id = $"local:{n}", Suite = roster.Suite, Partial = partial,
        At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(n),
        Branch = null, Commit = null, Provider = null, Url = null, Shards = 1,
        RosterHash = roster.Hash,
        Results = results ?? new string('P', roster.Count), Attempts = new string('1', roster.Count),
        Durations = durations, Errors = new string?[roster.Count],
        ShapeSet = shapes, ShapeOrdered = shapes, Calls = calls,
        ShapeVersion = shapes is null ? null : InteractionShape.Version,
        ShapesHash = shapesHash, CallSets = callSets,
        ErrorText = new Dictionary<string, string>(), Deps = []
    };

    private static HistoryLedger Ledger(IEnumerable<(HistoryRoster Roster, HistoryRun Run)> runs, params HistoryShapes[] shapes)
    {
        var text = HistoryJson.HeaderLine("test") + "\n";
        foreach (var list in shapes) text += HistoryJson.ShapesLine(list) + "\n";
        var seen = new HashSet<string>();
        foreach (var (roster, run) in runs)
        {
            if (seen.Add(roster.Hash)) text += HistoryJson.RosterLine(roster) + "\n";
            text += HistoryJson.RunLine(run) + "\n";
        }
        return HistoryLedgerReader.Parse(text, window: 50).Ledger!;
    }

    private static int?[] FullDurations(int first) => [first, .. Enumerable.Repeat<int?>(5700, 19)];
    private static readonly int?[] SubsetDurations = [9079, 11110, 2482, 97, 739];

    // ─── #75 section 3: durations ───────────────────────────

    [Fact]
    public void A_partial_run_does_not_inflate_the_p95_of_earlier_runs()
    {
        // The partial run's "speed" is the median of five arbitrary scenarios, 2,482 ms where a full run's is
        // 5,700: read against it the first scenario's 9,079 ms became a relative 8.2 and a bar of 32,133 ms.
        var full = Roster(Full);
        var subset = Roster(Subset);
        var ledger = Ledger([
            (full, Run(full, 1, FullDurations(5913))),
            (full, Run(full, 2, FullDurations(5759))),
            (full, Run(full, 3, FullDurations(5824))),
            (subset, Run(subset, 4, SubsetDurations, partial: true)),
            (full, Run(full, 5, FullDurations(5812))),
        ]);
        var scenario = HistoryAnalyzer.Analyse(ledger, full, Run(full, 6, FullDurations(5760)), new HistoryAnalysisOptions()).Scenarios[0];

        // The highest of the three full-run readings before the previous one, at a speed of 5,700 in both runs.
        Assert.Equal(5913, scenario.DurationP95);
    }

    [Fact]
    public void A_partial_run_in_the_window_does_not_hide_a_real_slowdown()
    {
        var full = Roster(Full);
        var subset = Roster(Subset);
        var runs = new List<(HistoryRoster, HistoryRun)>();
        for (var i = 1; i <= 6; i++) runs.Add((full, Run(full, i, FullDurations(5800))));
        runs.Add((subset, Run(subset, 7, SubsetDurations, partial: true)));
        runs.Add((full, Run(full, 8, FullDurations(20_000))));
        var scenario = HistoryAnalyzer.Analyse(Ledger(runs), full, Run(full, 9, FullDurations(20_000)), new HistoryAnalysisOptions()).Scenarios[0];

        Assert.Contains(HistoryVerdictKind.Slower, scenario.Verdicts);
    }

    [Fact]
    public void Control_the_same_slowdown_without_the_partial_run_is_slower()
    {
        var full = Roster(Full);
        var runs = new List<(HistoryRoster, HistoryRun)>();
        for (var i = 1; i <= 6; i++) runs.Add((full, Run(full, i, FullDurations(5800))));
        runs.Add((full, Run(full, 8, FullDurations(20_000))));
        var scenario = HistoryAnalyzer.Analyse(Ledger(runs), full, Run(full, 9, FullDurations(20_000)), new HistoryAnalysisOptions()).Scenarios[0];

        Assert.Contains(HistoryVerdictKind.Slower, scenario.Verdicts);
    }

    [Fact]
    public void A_partial_current_run_reads_no_slower_and_its_bar_is_the_raw_p95_of_full_runs()
    {
        // Its speed is the median of an arbitrary subset, so nothing is scaled to it: the bar is the plain
        // p95 of what the scenario took in full runs, and no duration verdict is read.
        var full = Roster(Full);
        var subset = Roster(Subset);
        var runs = new List<(HistoryRoster, HistoryRun)>();
        for (var i = 1; i <= 7; i++) runs.Add((full, Run(full, i, FullDurations(5800 + i))));
        runs.Add((subset, Run(subset, 8, [60_000, 100, 100, 100, 100], partial: true)));
        var verdicts = HistoryAnalyzer.Analyse(Ledger(runs), subset, Run(subset, 9, [60_000, 100, 100, 100, 100], partial: true), new HistoryAnalysisOptions());
        var scenario = verdicts.Scenarios[0];

        Assert.True(verdicts.Partial);
        Assert.DoesNotContain(HistoryVerdictKind.Slower, scenario.Verdicts);
        Assert.Equal(5807, scenario.DurationP95);
        Assert.True(scenario.DurationP95IsRaw);
    }

    [Fact]
    public void A_full_runs_bar_is_not_raw()
    {
        var full = Roster(Full);
        var runs = Enumerable.Range(1, 6).Select(i => (full, Run(full, i, FullDurations(5800)))).ToList();
        var scenario = HistoryAnalyzer.Analyse(Ledger(runs), full, Run(full, 9, FullDurations(5800)), new HistoryAnalysisOptions()).Scenarios[0];

        Assert.False(scenario.DurationP95IsRaw);
        Assert.False(scenario.Points[^1].Partial);
    }

    // ─── #75 section 4, and the plan's F2 ───────────────────

    private static string[] Shapes(HistoryRoster roster, string first) => [first, .. Enumerable.Repeat("cccccccc", roster.Count - 1)];
    private static int[] Counts(HistoryRoster roster, int first) => [first, .. Enumerable.Repeat(3, roster.Count - 1)];

    /// <summary>
    /// 22 calls three times, 74 in a partial run, then full runs on 22 again. Taking partial runs out of the
    /// alternating memory alone, which is what the issue asks for, makes the first full run after the partial
    /// one read behaviour-changed: the previous shaped point is still the partial one.
    /// </summary>
    [Theory]
    [InlineData(1)]   // the first full run after the partial one
    [InlineData(2)]   // the second
    public void A_partial_run_with_a_different_set_of_calls_is_not_a_state_of_the_full_suite(int after)
    {
        var full = Roster(Full);
        var subset = Roster(Subset);
        var runs = new List<(HistoryRoster, HistoryRun)>();
        for (var i = 1; i <= 3; i++) runs.Add((full, Run(full, i, null, shapes: Shapes(full, "aaaaaaaa"), calls: Counts(full, 22))));
        runs.Add((subset, Run(subset, 4, null, partial: true, shapes: Shapes(subset, "bbbbbbbb"), calls: Counts(subset, 74))));
        for (var i = 1; i < after; i++) runs.Add((full, Run(full, 4 + i, null, shapes: Shapes(full, "aaaaaaaa"), calls: Counts(full, 22))));

        var scenario = HistoryAnalyzer.Analyse(Ledger(runs), full, Run(full, 9, null, shapes: Shapes(full, "aaaaaaaa"), calls: Counts(full, 22)), new HistoryAnalysisOptions()).Scenarios[0];

        Assert.Equal("stable", scenario.VerdictNames);
        Assert.Equal(22, scenario.PreviousCalls);
    }

    [Fact]
    public void A_partial_current_run_still_compares_its_calls_with_the_full_run_before_it()
    {
        // A developer who filtered to one scenario after an edit wants to hear that its calls changed.
        var full = Roster(Full);
        var subset = Roster(Subset);
        var runs = Enumerable.Range(1, 3).Select(i => (full, Run(full, i, null, shapes: Shapes(full, "aaaaaaaa"), calls: Counts(full, 22)))).ToList();

        var scenario = HistoryAnalyzer.Analyse(Ledger(runs), subset, Run(subset, 9, null, partial: true, shapes: Shapes(subset, "bbbbbbbb"), calls: Counts(subset, 74)), new HistoryAnalysisOptions()).Scenarios[0];

        Assert.Contains(HistoryVerdictKind.BehaviourChanged, scenario.Verdicts);
    }

    [Fact]
    public void A_partial_current_run_compares_with_a_partial_run_before_it()
    {
        // Two filtered runs in a row are each other's conditions; the second is read against the first.
        var full = Roster(Full);
        var subset = Roster(Subset);
        var runs = Enumerable.Range(1, 3).Select(i => (full, Run(full, i, null, shapes: Shapes(full, "aaaaaaaa"), calls: Counts(full, 22)))).ToList();
        runs.Add((subset, Run(subset, 4, null, partial: true, shapes: Shapes(subset, "bbbbbbbb"), calls: Counts(subset, 74))));

        var scenario = HistoryAnalyzer.Analyse(Ledger(runs), subset, Run(subset, 9, null, partial: true, shapes: Shapes(subset, "bbbbbbbb"), calls: Counts(subset, 74)), new HistoryAnalysisOptions()).Scenarios[0];

        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, scenario.Verdicts);
    }

    // ─── Status still reads partial runs ────────────────────

    [Fact]
    public void A_fix_seen_in_a_partial_run_is_not_fixed_again_by_the_next_full_run()
    {
        // The principle, not just the case that happened to pass: the filtered re-run that showed the fix is
        // a verdict on the scenario, so the full run after it has nothing to announce.
        var full = Roster(Full);
        var subset = Roster(Subset);
        var failing = "F" + new string('P', 19);
        var ledger = Ledger([
            (full, Run(full, 1, null)),
            (full, Run(full, 2, null, results: failing)),
            (subset, Run(subset, 3, null, partial: true)),
        ]);

        var scenario = HistoryAnalyzer.Analyse(ledger, full, Run(full, 9, null), new HistoryAnalysisOptions()).Scenarios[0];

        Assert.DoesNotContain(HistoryVerdictKind.Fixed, scenario.Verdicts);
        Assert.Equal("PFPP", scenario.Series);
        Assert.True(scenario.Points[2].Partial);
    }

    // ─── #75 section 4b: the other state, named ─────────────

    [Fact]
    public void Alternating_on_the_set_the_previous_run_held_names_the_other_state()
    {
        // Warm, cold, warm, warm: the fourth run is alternating and its set equals the third's, so the diff
        // against the previous run is empty and the evidence named no call at all.
        var roster = Roster(Full);
        var warm = HistoryShapes.Create(["Api>cache GET /k 200"]);
        var cold = HistoryShapes.Create(["Api>cache GET /k 404", "Api>db QUERY /orders 200"]);
        HistoryRun Shaped(int n, HistoryShapes list, string hash, int count) =>
            Run(roster, n, null, shapes: Shapes(roster, hash), calls: Counts(roster, count), shapesHash: list.Hash,
                callSets: [Enumerable.Range(0, list.Calls.Count).ToArray(), .. Enumerable.Repeat<IReadOnlyList<int>>([], roster.Count - 1)]);

        var ledger = Ledger([(roster, Shaped(1, warm, "aaaaaaaa", 1)), (roster, Shaped(2, cold, "bbbbbbbb", 2)), (roster, Shaped(3, warm, "aaaaaaaa", 1))], warm, cold);
        var scenario = HistoryAnalyzer.Analyse(ledger, roster, Shaped(4, warm, "aaaaaaaa", 1), new HistoryAnalysisOptions(), shapes: warm).Scenarios[0];

        Assert.Contains(HistoryVerdictKind.Alternating, scenario.Verdicts);
        Assert.Contains("other set last held in local:2", scenario.Evidence);
        Assert.Contains("new there: Api>cache GET /k 404, Api>db QUERY /orders 200", scenario.Evidence);
        Assert.Contains("gone there: Api>cache GET /k 200", scenario.Evidence);
        // Nothing changed since the previous run, so the run-to-run lists stay empty.
        Assert.Empty(scenario.NewCalls);
        Assert.Empty(scenario.GoneCalls);
    }

    // ─── #75 section 5: the fold scenario ───────────────────

    [Fact]
    public void A_position_that_is_not_a_test_has_no_verdict_and_is_never_new()
    {
        var plain = Roster(Full);
        var withFold = Roster([.. Full, "ffff000000000001"]);
        var runs = Enumerable.Range(1, 3).Select(i => (plain, Run(plain, i, null))).ToList();

        var verdicts = HistoryAnalyzer.Analyse(Ledger(runs), withFold,
            Run(withFold, 9, null, results: new string('P', 20) + "N", shapes: Shapes(withFold, "aaaaaaaa"), calls: Counts(withFold, 1)), new HistoryAnalysisOptions());
        var fold = verdicts.Scenarios[^1];

        Assert.Equal(HistoryFormat.NotATest, fold.Current);
        Assert.Equal("stable", fold.VerdictNames);
        Assert.Equal("not a test: it collects the traffic no test could be given", fold.Evidence);
        Assert.Equal(0, verdicts.FirstSeen);
    }

    [Fact]
    public void A_position_that_was_not_a_test_is_never_absent()
    {
        // It exists only when unattributed traffic survived, so it came and went as "absent" run after run.
        var plain = Roster(Full);
        var withFold = Roster([.. Full, "ffff000000000001"]);
        var ledger = Ledger([(withFold, Run(withFold, 1, null, results: new string('P', 20) + "N"))]);

        var verdicts = HistoryAnalyzer.Analyse(ledger, plain, Run(plain, 9, null), new HistoryAnalysisOptions());

        Assert.Empty(verdicts.Absent);
    }

    [Fact]
    public void A_real_scenario_that_went_missing_is_still_absent()
    {
        var plain = Roster(Full);
        var fewer = Roster(Full.Take(19).ToArray());
        var ledger = Ledger([(plain, Run(plain, 1, null))]);

        var verdicts = HistoryAnalyzer.Analyse(ledger, fewer, Run(fewer, 9, null), new HistoryAnalysisOptions());

        Assert.Single(verdicts.Absent);
    }

    [Fact]
    public void Not_a_test_is_not_a_verdict_the_rates_count()
    {
        Assert.False(HistoryFormat.IsRealVerdict(HistoryFormat.NotATest));
        Assert.Equal('N', HistoryFormat.NotATest);
    }
}
