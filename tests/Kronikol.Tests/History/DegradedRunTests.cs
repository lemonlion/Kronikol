using Kronikol.History;

namespace Kronikol.Tests.History;

/// <summary>
/// #83 (plans/HISTORY_VERDICT_NOISE_PLAN.md S4): a run's pace, and what a degraded run may and may not say.
/// A failure inside a run where everything was slow is weak evidence against the test, so it is labelled;
/// it is still a failure, so it is never discounted. A duration read in such a run is a measurement of the
/// machine, so it is: no slower verdict in a degraded run, and none against one.
/// </summary>
public class DegradedRunTests
{
    private static readonly string[] Ids = Enumerable.Range(1, 20).Select(i => $"dddd{i:D12}").ToArray();

    private static HistoryRoster Roster(string[] ids) =>
        HistoryRoster.Create("Suite", ids.Select(id => new HistoryRosterEntry(id, "Scenario " + id, "Feature", null)).ToArray());

    private static HistoryRun Run(HistoryRoster roster, int n, int?[] durations, string? results = null, bool partial = false) => new()
    {
        Id = $"local:{n}", Suite = roster.Suite, Partial = partial,
        At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(n),
        Branch = null, Commit = null, Provider = null, Url = null, Shards = 1,
        RosterHash = roster.Hash,
        Results = results ?? new string('P', roster.Count), Attempts = new string('1', roster.Count),
        Durations = durations, Errors = new string?[roster.Count],
        ShapeSet = null, ShapeOrdered = null, Calls = null, ShapeVersion = null,
        ErrorText = new Dictionary<string, string>(), Deps = []
    };

    private static HistoryLedger Ledger(IEnumerable<(HistoryRoster Roster, HistoryRun Run)> runs)
    {
        var text = HistoryJson.HeaderLine("test") + "\n";
        var seen = new HashSet<string>();
        foreach (var (roster, run) in runs)
        {
            if (seen.Add(roster.Hash)) text += HistoryJson.RosterLine(roster) + "\n";
            text += HistoryJson.RunLine(run) + "\n";
        }
        return HistoryLedgerReader.Parse(text, window: 50).Ledger!;
    }

    /// <summary>Every scenario at <paramref name="others"/> milliseconds, the first at <paramref name="first"/>.</summary>
    private static int?[] Durations(int first, int others = 1000) => [first, .. Enumerable.Repeat<int?>(others, 19)];

    /// <summary>A machine under load: nine scenarios twice their usual, ten six times, and the first as given. Not uniform — measured, it never is.</summary>
    private static int?[] Contended(int first) => [first, .. Enumerable.Repeat<int?>(2000, 9), .. Enumerable.Repeat<int?>(6000, 10)];

    private static List<(HistoryRoster, HistoryRun)> Healthy(HistoryRoster roster, int runs) =>
        Enumerable.Range(1, runs).Select(i => (roster, Run(roster, i, Durations(1000)))).ToList();

    [Fact]
    public void A_run_whose_passing_scenarios_took_three_times_their_usual_is_degraded()
    {
        var roster = Roster(Ids);
        var verdicts = HistoryAnalyzer.Analyse(Ledger(Healthy(roster, 6)), roster, Run(roster, 7, Durations(3000, 3000)), new HistoryAnalysisOptions());

        Assert.Equal(3.0, verdicts.Runs[^1].Pace!.Value, 2);
        Assert.True(verdicts.Runs[^1].Degraded);
        Assert.All(verdicts.Runs.Take(6), run => Assert.False(run.Degraded));
    }

    [Fact]
    public void One_scenario_thirty_times_slower_does_not_make_the_run_degraded()
    {
        var roster = Roster(Ids);
        var verdicts = HistoryAnalyzer.Analyse(Ledger(Healthy(roster, 6)), roster, Run(roster, 7, Durations(30_000)), new HistoryAnalysisOptions());

        Assert.Equal(1.0, verdicts.Runs[^1].Pace!.Value, 2);
        Assert.False(verdicts.Runs[^1].Degraded);
        Assert.Equal(30.0, verdicts.Scenarios[0].Points[^1].TimesUsual!.Value, 2);
    }

    [Fact]
    public void A_failure_in_a_degraded_run_says_so_and_the_same_failure_in_a_normal_run_does_not()
    {
        var roster = Roster(Ids);
        var failed = "F" + new string('P', 19);
        var degraded = HistoryAnalyzer.Analyse(Ledger(Healthy(roster, 6)), roster, Run(roster, 7, Contended(60_000), failed), new HistoryAnalysisOptions()).Scenarios[0];
        var normal = HistoryAnalyzer.Analyse(Ledger(Healthy(roster, 6)), roster, Run(roster, 7, Durations(60_000), failed), new HistoryAnalysisOptions()).Scenarios[0];

        Assert.Contains(HistoryVerdictKind.Broke, degraded.Verdicts);
        Assert.Contains("this run was degraded: its passing scenarios took 6.0× their usual", degraded.Evidence);
        Assert.Equal(1, degraded.FailuresInDegradedRuns);
        Assert.Contains(HistoryVerdictKind.Broke, normal.Verdicts);
        Assert.DoesNotContain("degraded", normal.Evidence);
        Assert.Equal(0, normal.FailuresInDegradedRuns);
    }

    [Fact]
    public void Failing_scenarios_do_not_move_the_pace()
    {
        var roster = Roster(Ids);
        int?[] durations = [.. Enumerable.Repeat<int?>(60_000, 8), .. Enumerable.Repeat<int?>(1000, 12)];
        var verdicts = HistoryAnalyzer.Analyse(Ledger(Healthy(roster, 6)), roster, Run(roster, 7, durations, new string('F', 8) + new string('P', 12)), new HistoryAnalysisOptions());

        Assert.Equal(1.0, verdicts.Runs[^1].Pace!.Value, 2);
        Assert.False(verdicts.Runs[^1].Degraded);
    }

    [Fact]
    public void A_partial_run_has_no_pace_and_is_in_no_scenarios_usual()
    {
        var roster = Roster(Ids);
        var subset = Roster(Ids.Take(6).ToArray());
        var runs = Healthy(roster, 3);
        for (var i = 4; i <= 8; i++)
            runs.Add((subset, Run(subset, i, Enumerable.Repeat<int?>(10_000, 6).ToArray(), partial: true)));
        var verdicts = HistoryAnalyzer.Analyse(Ledger(runs), roster, Run(roster, 9, Durations(1000)), new HistoryAnalysisOptions { MinRuns = 3 });

        Assert.All(verdicts.Runs.Where(run => run.Partial), run => Assert.Null(run.Pace));
        Assert.Equal(1.0, verdicts.Runs[^1].Pace!.Value, 2);
        Assert.Null(verdicts.Scenarios[0].Points[3].TimesUsual);
    }

    [Fact]
    public void Issue_83_the_failing_reading_was_six_point_four_times_the_scenarios_usual()
    {
        var roster = Roster(Ids);
        var verdicts = HistoryAnalyzer.Analyse(
            Ledger([(roster, Run(roster, 1, Durations(12_502))), (roster, Run(roster, 2, Durations(13_034)))]),
            roster, Run(roster, 3, Durations(82_215), "F" + new string('P', 19)), new HistoryAnalysisOptions());

        // 82,215 over the median of 12,502 and 13,034: not the "5.6x its p95" of the issue, which divided a
        // raw reading by a bar scaled to a different, partial run's speed.
        Assert.Equal(6.44, verdicts.Scenarios[0].Points[^1].TimesUsual!.Value, 2);
        // Two other runs are enough to read a row against, and too few to say anything about the run.
        Assert.Null(verdicts.Runs[^1].Pace);
        Assert.False(verdicts.Runs[^1].Degraded);
    }

    // Measured: under contention a scenario that usually takes a few milliseconds barely registers the
    // load, and the reverse — 3 ms read as 9 — is a timer tick. Neither is evidence about the machine.
    [Fact]
    public void Scenarios_that_usually_take_under_ten_milliseconds_do_not_vote()
    {
        var roster = Roster(Ids);
        int?[] Mixed(int fast, int slow) => [.. Enumerable.Repeat<int?>(fast, 12), .. Enumerable.Repeat<int?>(slow, 8)];
        var runs = Enumerable.Range(1, 6).Select(i => (roster, Run(roster, i, Mixed(3, 1000)))).ToList();

        var onlyTheFastSlowed = HistoryAnalyzer.Analyse(Ledger(runs), roster, Run(roster, 7, Mixed(9, 1000)), new HistoryAnalysisOptions());
        var onlyTheSlowSlowed = HistoryAnalyzer.Analyse(Ledger(runs), roster, Run(roster, 7, Mixed(3, 3000)), new HistoryAnalysisOptions());

        Assert.Equal(1.0, onlyTheFastSlowed.Runs[^1].Pace!.Value, 2);
        Assert.False(onlyTheFastSlowed.Runs[^1].Degraded);
        Assert.Equal(3.0, onlyTheSlowSlowed.Runs[^1].Pace!.Value, 2);
        Assert.True(onlyTheSlowSlowed.Runs[^1].Degraded);
    }

    [Fact]
    public void A_suite_of_nothing_but_millisecond_scenarios_has_no_pace()
    {
        var roster = Roster(Ids);
        var runs = Enumerable.Range(1, 6).Select(i => (roster, Run(roster, i, Durations(3, 3)))).ToList();
        var verdicts = HistoryAnalyzer.Analyse(Ledger(runs), roster, Run(roster, 7, Durations(9, 9)), new HistoryAnalysisOptions());

        Assert.Null(verdicts.Runs[^1].Pace);
        Assert.False(verdicts.Runs[^1].Degraded);
    }

    // The limit the plan states rather than fixes: pace cannot tell a slow machine from a suite that
    // really did get three times slower everywhere. It reads as degraded until the usual catches up,
    // which is when the shifted runs are half the window, and then it stops by itself.
    [Theory]
    [InlineData(1, true)]
    [InlineData(25, true)]
    [InlineData(26, false)]
    [InlineData(40, false)]
    public void A_sustained_shift_reads_as_degraded_until_it_is_half_the_window(int shiftedRun, bool degraded)
    {
        var roster = Roster(Ids);
        var runs = Enumerable.Range(1, 50).Select(i => (roster, Run(roster, i, Durations(1000)))).ToList();
        for (var i = 1; i < shiftedRun; i++)
            runs.Add((roster, Run(roster, 50 + i, Durations(3000, 3000))));
        var verdicts = HistoryAnalyzer.Analyse(Ledger(runs), roster, Run(roster, 50 + shiftedRun, Durations(3000, 3000)), new HistoryAnalysisOptions());

        Assert.Equal(degraded, verdicts.Runs[^1].Degraded);
    }


    // F8, found on the first real degraded run (BreakfastProvider, 2026-09-19): under contention the
    // slowdown is nowhere near uniform (p25 2x, p90 54x in one run), the run's median cannot absorb it, and
    // every scenario whose previous reading happened to sit over its bar is handed a slower it did not
    // earn: 5, 20 and 7 of 203 scenarios in three such runs, all false.
    [Fact]
    public void A_degraded_run_does_not_hand_out_slower_verdicts()
    {
        var roster = Roster(Ids);
        var runs = Healthy(roster, 7);
        runs.Add((roster, Run(roster, 8, Durations(4000))));   // the previous reading was a spike
        var verdicts = HistoryAnalyzer.Analyse(Ledger(runs), roster, Run(roster, 9, Contended(30_000)), new HistoryAnalysisOptions());

        Assert.True(verdicts.Runs[^1].Degraded);
        Assert.DoesNotContain(HistoryVerdictKind.Slower, verdicts.Scenarios[0].Verdicts);
        Assert.Equal(0, verdicts.Counts.GetValueOrDefault(HistoryVerdictKind.Slower));
    }

    [Fact]
    public void The_same_readings_in_a_run_that_is_not_degraded_are_slower()
    {
        // The control: nothing but the one scenario slowed down, so the run is healthy and the verdict stands.
        var roster = Roster(Ids);
        var runs = Healthy(roster, 7);
        runs.Add((roster, Run(roster, 8, Durations(4000))));
        var verdicts = HistoryAnalyzer.Analyse(Ledger(runs), roster, Run(roster, 9, Durations(30_000)), new HistoryAnalysisOptions());

        Assert.False(verdicts.Runs[^1].Degraded);
        Assert.Contains(HistoryVerdictKind.Slower, verdicts.Scenarios[0].Verdicts);
    }

    // And it costs twice: a nearest-rank p95 of fewer than twenty readings is their maximum, so a degraded
    // run left in the baseline lifts the bar for as long as it stays in the window. Measured in the healthy
    // run after two contended ones: 47 of 198 bars more than 1.5x too high, 7 more than 5x.
    [Fact]
    public void A_degraded_run_in_the_window_does_not_hide_a_real_slowdown()
    {
        var roster = Roster(Ids);
        var runs = Healthy(roster, 7);
        runs.Add((roster, Run(roster, 8, Contended(30_000))));
        runs.Add((roster, Run(roster, 9, Durations(2500))));
        var verdicts = HistoryAnalyzer.Analyse(Ledger(runs), roster, Run(roster, 10, Durations(2500)), new HistoryAnalysisOptions());
        var scenario = verdicts.Scenarios[0];

        Assert.True(verdicts.Runs[7].Degraded);
        Assert.Contains(HistoryVerdictKind.Slower, scenario.Verdicts);
        Assert.InRange(scenario.DurationP95!.Value, 900, 1100);
    }

    [Fact]
    public void Too_few_scenarios_with_a_usual_and_the_run_has_no_pace()
    {
        // Five qualifying scenarios are the least a median is taken over; four say nothing about a run.
        var four = Roster(Ids.Take(4).ToArray());
        var runs = Enumerable.Range(1, 6).Select(i => (four, Run(four, i, [1000, 1000, 1000, 1000]))).ToList();
        var verdicts = HistoryAnalyzer.Analyse(Ledger(runs), four, Run(four, 7, [3000, 3000, 3000, 3000]), new HistoryAnalysisOptions());

        Assert.Null(verdicts.Runs[^1].Pace);
        Assert.False(verdicts.Runs[^1].Degraded);
        // The rows are still read: a reading against its usual is a fact about the row, not a verdict on the run.
        Assert.Equal(3.0, verdicts.Scenarios[0].Points[^1].TimesUsual!.Value, 2);
    }

    /// <summary>
    /// #83 as the ledger really was, at the default minimum runs: a scenario that had been in two full runs,
    /// the run it failed in, then two filtered re-runs. With the row made to wait for the suite's minimum
    /// runs, the very row the issue is about came out bare.
    /// </summary>
    [Fact]
    public void Issue_83_as_the_ledger_was()
    {
        var without = Roster(Ids.Skip(1).ToArray());
        var with = Roster(Ids);
        var subset = Roster(Ids.Take(3).ToArray());
        var runs = Enumerable.Range(1, 6).Select(i => (without, Run(without, i, Enumerable.Repeat<int?>(1000, 19).ToArray()))).ToList();
        runs.Add((with, Run(with, 7, Durations(12_502))));
        runs.Add((with, Run(with, 8, Durations(13_034))));
        var failing = Run(with, 9, Durations(82_215, 5600), "F" + new string('P', 19));

        var then = HistoryAnalyzer.Analyse(Ledger(runs), with, failing, new HistoryAnalysisOptions());
        Assert.Equal(6.44, then.Scenarios[0].Points[^1].TimesUsual!.Value, 2);
        Assert.Equal(5.6, then.Runs[^1].Pace!.Value, 2);
        Assert.True(then.Runs[^1].Degraded);
        Assert.Contains("this run was degraded: its passing scenarios took 5.6× their usual", then.Scenarios[0].Evidence);
        Assert.Equal(1, then.Scenarios[0].FailuresInDegradedRuns);

        // Two filtered re-runs later the row still reads 6.44: a filtered run is in no usual.
        runs.Add((with, failing));
        runs.Add((subset, Run(subset, 10, [1181, 1000, 1000], partial: true)));
        var later = HistoryAnalyzer.Analyse(Ledger(runs), subset, Run(subset, 11, [1270, 1000, 1000], partial: true), new HistoryAnalysisOptions());
        var row = later.Scenarios[0].Points.Single(p => p.RunId == "local:9");
        Assert.Equal(6.44, row.TimesUsual!.Value, 2);
        Assert.True(row.RunDegraded);
        Assert.Null(later.Scenarios[0].Points[^1].TimesUsual);
        Assert.Equal(1, later.Scenarios[0].FailuresInDegradedRuns);
    }

    [Fact]
    public void A_row_with_one_other_reading_is_not_read_against_a_usual()
    {
        var roster = Roster(Ids);
        var verdicts = HistoryAnalyzer.Analyse(Ledger([(roster, Run(roster, 1, Durations(1000)))]), roster, Run(roster, 2, Durations(9000)), new HistoryAnalysisOptions());

        Assert.All(verdicts.Scenarios[0].Points, point => Assert.Null(point.TimesUsual));
    }

    /// <summary>
    /// A young ledger: measured over 394 healthy CI runs, a run paced against fewer than five others is
    /// called degraded one to three times in a thousand, and against five never. So nothing is said about a
    /// run until the runs exist, and the degraded one is labelled after the fact.
    /// </summary>
    [Fact]
    public void A_young_ledger_reads_its_rows_at_once_and_labels_the_run_when_it_can()
    {
        var roster = Roster(Ids);
        var failed = "F" + new string('P', 19);
        var runs = new List<(HistoryRoster, HistoryRun)>
        {
            (roster, Run(roster, 1, Durations(1000))),
            (roster, Run(roster, 2, Durations(1000))),
            (roster, Run(roster, 3, Contended(60_000), failed)),
            (roster, Run(roster, 4, Durations(1000))),
        };

        var five = HistoryAnalyzer.Analyse(Ledger(runs), roster, Run(roster, 5, Durations(1000)), new HistoryAnalysisOptions());
        Assert.All(five.Runs, run => Assert.Null(run.Pace));
        Assert.Equal(60.0, five.Scenarios[0].Points[2].TimesUsual!.Value, 2);
        Assert.Equal(0, five.Scenarios[0].FailuresInDegradedRuns);

        runs.Add((roster, Run(roster, 5, Durations(1000))));
        var six = HistoryAnalyzer.Analyse(Ledger(runs), roster, Run(roster, 6, Durations(1000)), new HistoryAnalysisOptions());
        Assert.True(six.Runs[2].Degraded);
        Assert.Equal(6.0, six.Runs[2].Pace!.Value, 2);
        Assert.Equal(1, six.Scenarios[0].FailuresInDegradedRuns);
        Assert.True(six.Scenarios[0].Points[2].RunDegraded);
    }

    [Fact]
    public void A_flaky_scenario_whose_failures_were_all_in_degraded_runs_says_so_first()
    {
        var roster = Roster(Ids);
        var failed = "F" + new string('P', 19);
        var runs = Healthy(roster, 6);
        runs.Add((roster, Run(roster, 7, Contended(60_000), failed)));
        runs.Add((roster, Run(roster, 8, Durations(1000))));
        runs.Add((roster, Run(roster, 9, Contended(60_000), failed)));
        var scenario = HistoryAnalyzer.Analyse(Ledger(runs), roster, Run(roster, 10, Durations(1000)), new HistoryAnalysisOptions()).Scenarios[0];

        Assert.Equal(HistoryVerdictKind.Flaky, scenario.Primary);
        Assert.StartsWith("every failure was in a degraded run; failed 2 of the last 10 runs", scenario.Evidence);
        Assert.Equal(2, scenario.FailuresInDegradedRuns);
    }

    [Fact]
    public void The_threshold_is_an_option()
    {
        var roster = Roster(Ids);
        var current = Run(roster, 7, Durations(1600, 1600));

        Assert.False(HistoryAnalyzer.Analyse(Ledger(Healthy(roster, 6)), roster, current, new HistoryAnalysisOptions()).Runs[^1].Degraded);
        Assert.True(HistoryAnalyzer.Analyse(Ledger(Healthy(roster, 6)), roster, current, new HistoryAnalysisOptions { DegradedBy = 1.5 }).Runs[^1].Degraded);
    }
}
