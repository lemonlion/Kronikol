using Kronikol.History;

namespace Kronikol.Tests.History;

/// <summary>
/// The verdicts (plans/CROSS_RUN_HISTORY_PLAN.md §2, §7): pure arithmetic over the last N runs of one
/// suite in one branch stream. Each verdict is pinned with the shape that produces it and the shape
/// that must not, because the whole feature is the vocabulary — a wrong "broke" on a flaky test is
/// worse than no history at all.
/// </summary>
public class HistoryAnalyzerTests
{
    private static readonly string[] Ids = ["aaaa000000000001", "aaaa000000000002", "aaaa000000000003"];

    private static HistoryRoster Roster(params string[] ids) =>
        HistoryRoster.Create("Suite", ids.Select((id, i) => new HistoryRosterEntry(id, "Scenario " + id[^1], "Feature", null)).ToArray());

    private static HistoryRun Run(HistoryRoster roster, int n, string results, string? branch = "main", int?[]? durations = null,
        string[]? shapes = null, string[]? ordered = null, int[]? calls = null, string? attempts = null, string[]? deps = null, bool? partial = false,
        int? shapeVersion = null) => new()
    {
        Id = $"gh:{n}:1",
        Suite = roster.Suite,
        Partial = partial,
        At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(n),
        Branch = branch,
        Commit = $"c{n:D6}",
        Provider = "GitHubActions",
        Url = null,
        Shards = 1,
        RosterHash = roster.Hash,
        Results = results,
        Attempts = attempts ?? new string('-', results.Length),
        Durations = durations,
        Calls = calls,
        ShapeSet = shapes,
        ShapeOrdered = ordered ?? shapes,
        ShapeVersion = shapes is null ? null : shapeVersion ?? InteractionShape.Version,
        Errors = results.Select(r => r == 'F' ? "e1" : null).ToArray(),
        ErrorText = results.Contains('F') ? new Dictionary<string, string> { ["e1"] = "Expected 200 but got 500" } : new Dictionary<string, string>(),
        Deps = deps ?? ["Test>orders"]
    };

    /// <summary>A ledger holding the given prior runs, in order.</summary>
    private static HistoryLedger Ledger(IEnumerable<(HistoryRoster Roster, HistoryRun Run)> runs)
    {
        var text = HistoryJson.HeaderLine("test") + "\n";
        var rosters = new HashSet<string>();
        foreach (var (roster, run) in runs)
        {
            if (rosters.Add(roster.Hash)) text += HistoryJson.RosterLine(roster) + "\n";
            text += HistoryJson.RunLine(run) + "\n";
        }
        return HistoryLedgerReader.Parse(text, window: 50).Ledger!;
    }

    /// <summary>Prior runs of one roster from a column-per-scenario string list: "PPF" per run, oldest first.</summary>
    private static HistoryLedger Series(HistoryRoster roster, params string[] results) =>
        Ledger(results.Select((r, i) => (roster, Run(roster, i + 1, r))));

    private static HistoryVerdicts Analyse(HistoryLedger ledger, HistoryRoster roster, HistoryRun current, HistoryAnalysisOptions? options = null) =>
        HistoryAnalyzer.Analyse(ledger, roster, current, options ?? new HistoryAnalysisOptions());

    private static ScenarioHistory First(HistoryVerdicts verdicts) => verdicts.Scenarios[0];

    // ─── Status verdicts ───────────────────────────────────────

    [Fact]
    public void A_failure_after_a_pass_is_broke()
    {
        var roster = Roster(Ids);
        var ledger = Series(roster, "PPP", "PPP", "PPP");

        var verdicts = Analyse(ledger, roster, Run(roster, 9, "FPP"));

        Assert.Equal(HistoryVerdictKind.Broke, First(verdicts).Primary);
        Assert.Equal(HistoryVerdictKind.Stable, verdicts.Scenarios[1].Primary);
        Assert.Contains("passed", First(verdicts).Evidence);
    }

    [Fact]
    public void A_failure_after_failures_is_failing_since_the_first_of_them()
    {
        var roster = Roster(Ids);
        var ledger = Series(roster, "PPP", "PPP", "FPP", "FPP");

        var scenario = First(Analyse(ledger, roster, Run(roster, 9, "FPP")));

        Assert.Equal(HistoryVerdictKind.Failing, scenario.Primary);
        Assert.NotNull(scenario.FailingSince);
        Assert.Equal("gh:3:1", scenario.FailingSince!.RunId);
        Assert.Equal(3, scenario.FailingSince.Runs);
        Assert.Contains("gh:3:1", scenario.Evidence);
    }

    [Fact]
    public void A_scenario_that_never_passed_is_always_failing_not_broke()
    {
        var roster = Roster(Ids);
        var ledger = Series(roster, "FPP", "FPP", "FPP");

        var scenario = First(Analyse(ledger, roster, Run(roster, 9, "FPP")));

        Assert.Equal(HistoryVerdictKind.AlwaysFailing, scenario.Primary);
        Assert.Contains("every", scenario.Evidence);
    }

    [Fact]
    public void A_pass_after_a_failure_is_fixed()
    {
        var roster = Roster(Ids);
        var ledger = Series(roster, "PPP", "FPP", "FPP");

        var scenario = First(Analyse(ledger, roster, Run(roster, 9, "PPP")));

        Assert.Equal(HistoryVerdictKind.Fixed, scenario.Primary);
    }

    [Fact]
    public void A_scenario_not_in_any_earlier_run_of_the_stream_is_new()
    {
        var roster = Roster(Ids);
        var ledger = Series(Roster(Ids[0], Ids[1]), "PP", "PP");

        var verdicts = Analyse(ledger, roster, Run(roster, 9, "PPF"));

        Assert.Equal(HistoryVerdictKind.New, verdicts.Scenarios[2].Primary);
        Assert.Equal(HistoryVerdictKind.Stable, verdicts.Scenarios[0].Primary);
        Assert.Equal(1, verdicts.FirstSeen);
    }

    [Fact]
    public void A_first_run_with_no_history_marks_nothing_new_and_says_it_is_cold()
    {
        // Every scenario is new to an empty ledger; saying so three hundred times is noise, and the
        // cold-start line says what the reader needs: how many runs until verdicts mean something.
        var roster = Roster(Ids);

        var verdicts = Analyse(HistoryLedger.Empty, roster, Run(roster, 1, "PPF"), new HistoryAnalysisOptions { MinRuns = 5 });

        Assert.True(verdicts.ColdStart);
        Assert.Contains("0 run", verdicts.ColdStartMessage);
        Assert.Contains("5", verdicts.ColdStartMessage);
        Assert.All(verdicts.Scenarios, s => Assert.DoesNotContain(HistoryVerdictKind.New, s.Verdicts));
        Assert.Equal(HistoryVerdictKind.Stable, verdicts.Scenarios[0].Primary);
        Assert.Equal(HistoryVerdictKind.Unknown, verdicts.Scenarios[2].Primary);
    }

    // ─── Flakiness ─────────────────────────────────────────────

    [Fact]
    public void Flakiness_is_flip_rate_not_fail_rate()
    {
        // §7.2: a scenario that failed 5 of 10 in one contiguous block flipped once - it broke and was
        // fixed. One that failed 5 of 10 alternating flipped nine times - it is flaky. Same fail rate.
        var roster = Roster(Ids);
        var block = Series(roster, "PPP", "PPP", "PPP", "PPP", "PPP", "FPP", "FPP", "FPP", "FPP", "FPP");
        var alternating = Series(roster, "PPP", "FPP", "PPP", "FPP", "PPP", "FPP", "PPP", "FPP", "PPP", "FPP");

        var blockVerdict = First(Analyse(block, roster, Run(roster, 20, "PPP")));
        var alternatingVerdict = First(Analyse(alternating, roster, Run(roster, 20, "PPP")));

        Assert.DoesNotContain(HistoryVerdictKind.Flaky, blockVerdict.Verdicts);
        Assert.Equal(HistoryVerdictKind.Fixed, blockVerdict.Primary);
        Assert.Contains(HistoryVerdictKind.Flaky, alternatingVerdict.Verdicts);
        Assert.Equal(HistoryVerdictKind.Flaky, alternatingVerdict.Primary);
        Assert.Equal(10, alternatingVerdict.Flips);
        Assert.Contains("failed 5 of the last 11", alternatingVerdict.Evidence);
    }

    [Fact]
    public void A_flaky_scenario_that_fails_now_is_flaky_first_so_nobody_chases_a_regression()
    {
        var roster = Roster(Ids);
        var ledger = Series(roster, "PPP", "FPP", "PPP", "FPP", "PPP", "PPP", "FPP", "PPP");

        var scenario = First(Analyse(ledger, roster, Run(roster, 20, "FPP")));

        Assert.Equal(HistoryVerdictKind.Flaky, scenario.Primary);
        Assert.Contains(HistoryVerdictKind.Broke, scenario.Verdicts);
    }

    [Fact]
    public void Flakiness_needs_the_minimum_runs_and_two_flips()
    {
        var roster = Roster(Ids);
        var tooFew = Series(roster, "PPP", "FPP", "PPP");
        var oneFlip = Series(roster, "PPP", "PPP", "PPP", "PPP", "PPP", "PPP");

        Assert.DoesNotContain(HistoryVerdictKind.Flaky, First(Analyse(tooFew, roster, Run(roster, 9, "FPP"), new HistoryAnalysisOptions { MinRuns = 5 })).Verdicts);
        Assert.DoesNotContain(HistoryVerdictKind.Flaky, First(Analyse(oneFlip, roster, Run(roster, 9, "FPP"))).Verdicts);
    }

    [Fact]
    public void Skips_and_unknowns_are_not_verdicts_and_do_not_flip()
    {
        // §7.2: skipping a test for a week neither creates nor hides flakiness, and a defaulted result
        // is not a verdict at all.
        var roster = Roster(Ids);
        var ledger = Series(roster, "PPP", "SPP", "?PP", "SPP", "PPP", "SPP");

        var scenario = First(Analyse(ledger, roster, Run(roster, 9, "PPP")));

        Assert.Equal(0, scenario.Flips);
        Assert.Equal(3, scenario.RealVerdicts);
        Assert.Equal(HistoryVerdictKind.Stable, scenario.Primary);
    }

    [Fact]
    public void A_pass_on_a_retry_is_flaky_evidence_on_its_own()
    {
        // The runner already said so: the scenario failed and then passed inside one run.
        var roster = Roster(Ids);
        var ledger = Series(roster, "PPP", "PPP");

        var scenario = First(Analyse(ledger, roster, Run(roster, 9, "PPP", attempts: "2--")));

        Assert.Contains(HistoryVerdictKind.Flaky, scenario.Verdicts);
        Assert.Contains("retry", scenario.Evidence);
    }

    [Fact]
    public void The_flaky_rate_is_configurable()
    {
        var roster = Roster(Ids);
        var ledger = Series(roster, "PPP", "PPP", "FPP", "PPP", "PPP", "PPP", "PPP", "PPP", "PPP", "FPP", "PPP", "PPP");

        var loose = First(Analyse(ledger, roster, Run(roster, 20, "PPP"), new HistoryAnalysisOptions { FlakyRate = 0.1 }));
        var strict = First(Analyse(ledger, roster, Run(roster, 20, "PPP"), new HistoryAnalysisOptions { FlakyRate = 0.5 }));

        Assert.Contains(HistoryVerdictKind.Flaky, loose.Verdicts);
        Assert.DoesNotContain(HistoryVerdictKind.Flaky, strict.Verdicts);
    }

    // ─── Duration ──────────────────────────────────────────────

    [Fact]
    public void Slower_needs_two_consecutive_runs_above_the_p95_by_the_factor()
    {
        var roster = Roster(Ids);
        int?[] Durations(int a) => [a, 50, 50];
        var runs = Enumerable.Range(1, 8).Select(i => (roster, Run(roster, i, "PPP", durations: Durations(100 + i)))).ToList();
        runs.Add((roster, Run(roster, 9, "PPP", durations: Durations(400))));

        var oneSpike = First(Analyse(Ledger(runs.Take(8)), roster, Run(roster, 20, "PPP", durations: Durations(400))));
        var sustained = First(Analyse(Ledger(runs), roster, Run(roster, 20, "PPP", durations: Durations(400))));

        Assert.DoesNotContain(HistoryVerdictKind.Slower, oneSpike.Verdicts);
        Assert.Contains(HistoryVerdictKind.Slower, sustained.Verdicts);
        Assert.Equal(HistoryVerdictKind.Slower, sustained.Primary);
        Assert.Contains("400 ms", sustained.Evidence);
        Assert.NotNull(sustained.DurationP95);
    }

    [Fact]
    public void Slower_is_gated_by_the_minimum_runs()
    {
        var roster = Roster(Ids);
        var runs = Enumerable.Range(1, 3).Select(i => (roster, Run(roster, i, "PPP", durations: [100, 50, 50]))).ToList();
        runs.Add((roster, Run(roster, 4, "PPP", durations: [400, 50, 50])));

        var scenario = First(Analyse(Ledger(runs), roster, Run(roster, 20, "PPP", durations: [400, 50, 50]), new HistoryAnalysisOptions { MinRuns = 5 }));

        Assert.DoesNotContain(HistoryVerdictKind.Slower, scenario.Verdicts);
    }

    [Fact]
    public void Slower_is_not_read_from_a_run_that_is_slow_all_over()
    {
        // Measured on a consumer's CI: a lane read "18 slower" on a healthy run because the runner was
        // slow that day and every scenario went with it. A scenario is slower when it got slower than
        // its run did: durations are read against the run's median, so a slow runner lifts the bar
        // with the readings.
        var roster = Roster("a1", "a2", "a3", "a4", "a5");
        int?[] Usual() => [1000, 1500, 2000, 2500, 3000];
        int?[] AllDoubled() => [2000, 3000, 4000, 5000, 6000];
        int?[] OneTripled() => [3000, 1500, 2000, 2500, 3000];
        var baseline = Enumerable.Range(1, 6).Select(i => (roster, Run(roster, i, "PPPPP", durations: Usual()))).ToList();

        var slowRunner = Analyse(Ledger(baseline.Append((roster, Run(roster, 7, "PPPPP", durations: AllDoubled())))), roster,
            Run(roster, 8, "PPPPP", durations: AllDoubled()), new HistoryAnalysisOptions { MinRuns = 3 });
        var oneScenario = Analyse(Ledger(baseline.Append((roster, Run(roster, 7, "PPPPP", durations: OneTripled())))), roster,
            Run(roster, 8, "PPPPP", durations: OneTripled()), new HistoryAnalysisOptions { MinRuns = 3 });

        Assert.All(slowRunner.Scenarios, s => Assert.DoesNotContain(HistoryVerdictKind.Slower, s.Verdicts));
        Assert.Contains(HistoryVerdictKind.Slower, oneScenario.Scenarios[0].Verdicts);
        Assert.All(oneScenario.Scenarios.Skip(1), s => Assert.DoesNotContain(HistoryVerdictKind.Slower, s.Verdicts));
    }

    [Fact]
    public void Slower_needs_the_excess_over_the_bar_to_clear_a_floor()
    {
        // 8 ms that became 13 ms is above the bar by the factor and below anyone's notice: without a
        // floor in milliseconds, a scenario measured in single digits reads slower on runner jitter.
        var roster = Roster("x1", "x2", "x3");
        var runs = Enumerable.Range(1, 6).Select(i => (roster, Run(roster, i, "PPP", durations: [8, 50, 50]))).ToList();
        runs.Add((roster, Run(roster, 7, "PPP", durations: [13, 50, 50])));
        var current = Run(roster, 8, "PPP", durations: [13, 50, 50]);

        var floored = First(Analyse(Ledger(runs), roster, current, new HistoryAnalysisOptions { MinRuns = 3 }));
        var unfloored = First(Analyse(Ledger(runs), roster, current, new HistoryAnalysisOptions { MinRuns = 3, SlowerMinMs = 0 }));

        Assert.DoesNotContain(HistoryVerdictKind.Slower, floored.Verdicts);
        Assert.Contains(HistoryVerdictKind.Slower, unfloored.Verdicts);
        Assert.Equal(100, new HistoryAnalysisOptions().SlowerMinMs);
    }

    // ─── Behaviour ─────────────────────────────────────────────

    [Fact]
    public void Shapes_made_by_an_earlier_rule_are_not_compared()
    {
        // A fingerprint is comparable only with one the same templating rule made. The first run after an
        // upgrade that changed the rule reads no behaviour verdict against the runs before it, and says so,
        // rather than calling every scenario changed once; the run after that compares again.
        var roster = Roster(Ids);
        var ledger = Ledger(Enumerable.Range(1, 3).Select(i => (roster, Run(roster, i, "PPP", shapes: ["s1", "s2", "s3"], calls: [3, 1, 1], shapeVersion: 1))));

        var scenario = First(Analyse(ledger, roster, Run(roster, 9, "PPP", shapes: ["s9", "s2", "s3"], calls: [3, 1, 1], shapeVersion: 2)));

        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, scenario.Verdicts);
        Assert.Contains("earlier rule", scenario.Evidence);
        Assert.Null(scenario.PreviousShapeSet);

        // A run made by the current rule further back is still the one compared against.
        var mixed = Ledger([(roster, Run(roster, 1, "PPP", shapes: ["s1", "s2", "s3"], calls: [3, 1, 1], shapeVersion: 2)), (roster, Run(roster, 2, "PPP", shapes: ["s7", "s2", "s3"], calls: [3, 1, 1], shapeVersion: 1))]);
        var compared = First(Analyse(mixed, roster, Run(roster, 9, "PPP", shapes: ["s9", "s2", "s3"], calls: [3, 1, 1], shapeVersion: 2)));
        Assert.Contains(HistoryVerdictKind.BehaviourChanged, compared.Verdicts);
        Assert.Equal("s1", compared.PreviousShapeSet);
    }

    /// <summary>Prior passing runs whose first scenario made the same calls the given number of times, oldest first.</summary>
    private static HistoryLedger Counts(HistoryRoster roster, params int[] counts) =>
        Ledger(counts.Select((c, i) => (roster, Counted(roster, i + 1, c))));

    private static HistoryRun Counted(HistoryRoster roster, int n, int count, string set = "s1") =>
        Run(roster, n, "PPP", shapes: [set, "s2", "s3"], calls: [count, 1, 1]);

    [Fact]
    public void The_same_calls_made_more_often_is_read_out_first_and_behaviour_changed_when_the_next_run_holds_it()
    {
        // 3, 3, 3 then 5: an N+1 regression, which the set of calls cannot see because the calls are the
        // same. A count moves on its own too often to trip on at first sight (ten of the thirteen count
        // verdicts in a consumer's ledger reverted the next run), so the run that shows the new count reads
        // it out, and the next run that still holds it is the verdict, naming the run the change appeared in.
        var roster = Roster(Ids);
        var options = new HistoryAnalysisOptions { MinRuns = 3 };

        var first = First(Analyse(Counts(roster, 3, 3, 3), roster, Counted(roster, 4, 5), options));

        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, first.Verdicts);
        Assert.Contains("calls 3 in gh:3:1 to 5 now", first.Evidence);
        Assert.Contains("held for 2 runs", first.Evidence);

        var second = First(Analyse(Counts(roster, 3, 3, 3, 5), roster, Counted(roster, 5, 5), options));

        Assert.Equal(HistoryVerdictKind.BehaviourChanged, second.Primary);
        Assert.Contains("calls 3 to 5 in gh:4:1 and now", second.Evidence);
        Assert.Contains("constant over the 3 runs before", second.Evidence);

        // Once reported, a count that stays is the count: nothing to say the run after.
        var third = First(Analyse(Counts(roster, 3, 3, 3, 5, 5), roster, Counted(roster, 6, 5), options));

        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, third.Verdicts);
        Assert.DoesNotContain("to 5", third.Evidence);
    }

    [Fact]
    public void A_count_that_reverts_the_next_run_is_never_a_verdict()
    {
        // 3, 3, 3, 5, then 3 again: the processor's timing, not the code. Neither the 5 nor the return trips,
        // and the return says what it is rather than calling the count unstable.
        var roster = Roster(Ids);

        var scenario = First(Analyse(Counts(roster, 3, 3, 3, 5), roster, Counted(roster, 5, 3), new HistoryAnalysisOptions { MinRuns = 3 }));

        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, scenario.Verdicts);
        Assert.Contains("calls 5 in gh:4:1 to 3 now", scenario.Evidence);
        Assert.Contains("back to the count", scenario.Evidence);
    }

    [Fact]
    public void The_runs_a_new_count_must_hold_can_be_set()
    {
        // CountRuns = 1 is the 3.15.0 rule, the run that shows the new count trips; 3 needs it held twice.
        var roster = Roster(Ids);

        Assert.Equal(HistoryVerdictKind.BehaviourChanged,
            First(Analyse(Counts(roster, 3, 3, 3), roster, Counted(roster, 4, 5), new HistoryAnalysisOptions { MinRuns = 3, CountRuns = 1 })).Primary);

        var three = new HistoryAnalysisOptions { MinRuns = 3, CountRuns = 3 };
        var once = First(Analyse(Counts(roster, 3, 3, 3, 5), roster, Counted(roster, 5, 5), three));
        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, once.Verdicts);
        Assert.Contains("calls 3 in gh:3:1 to 5 in gh:4:1 and now", once.Evidence);
        Assert.Contains("held for 3 runs", once.Evidence);

        var twice = First(Analyse(Counts(roster, 3, 3, 3, 5, 5), roster, Counted(roster, 6, 5), three));
        Assert.Equal(HistoryVerdictKind.BehaviourChanged, twice.Primary);
        Assert.Contains("calls 3 to 5 in gh:4:1, gh:5:1 and now", twice.Evidence);
    }

    [Fact]
    public void A_count_that_changed_with_the_set_is_not_reported_again_when_it_holds()
    {
        // Run 4 changed the set and the count, and was reported then as a different set of calls. Run 5
        // holds both; the count rule must not read it as a second change.
        var roster = Roster(Ids);
        var ledger = Ledger(Enumerable.Range(1, 3).Select(i => (roster, Counted(roster, i, 3))).Append((roster, Counted(roster, 4, 5, set: "s9"))));

        var scenario = First(Analyse(ledger, roster, Counted(roster, 5, 5, set: "s9"), new HistoryAnalysisOptions { MinRuns = 3 }));

        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, scenario.Verdicts);
        Assert.DoesNotContain("different number", scenario.Evidence);
    }

    [Fact]
    public void A_count_that_wobbles_is_evidence_and_never_a_verdict()
    {
        // 7, 8, 7 then 9: a retry against a throttled emulator, or a consumer's work landing in whichever
        // scenario is running. The scenario's own record says its count is not to be read as behaviour.
        var roster = Roster(Ids);
        int[] counts = [7, 8, 7];
        var ledger = Ledger(Enumerable.Range(1, 3).Select(i => (roster, Run(roster, i, "PPP", shapes: ["s1", "s2", "s3"], calls: [counts[i - 1], 1, 1]))));

        var scenario = First(Analyse(ledger, roster, Run(roster, 9, "PPP", shapes: ["s1", "s2", "s3"], calls: [9, 1, 1]), new HistoryAnalysisOptions { MinRuns = 3 }));

        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, scenario.Verdicts);
        Assert.DoesNotContain(HistoryVerdictKind.Reordered, scenario.Verdicts);
        Assert.Contains("varies run to run", scenario.Evidence);
    }

    [Fact]
    public void A_count_change_below_the_minimum_runs_is_advisory()
    {
        var roster = Roster(Ids);
        var ledger = Ledger([(roster, Run(roster, 1, "PPP", shapes: ["s1", "s2", "s3"], calls: [3, 1, 1]))]);

        var scenario = First(Analyse(ledger, roster, Run(roster, 9, "PPP", shapes: ["s1", "s2", "s3"], calls: [5, 1, 1]), new HistoryAnalysisOptions { MinRuns = 3 }));

        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, scenario.Verdicts);
        Assert.Contains("needs 3 runs", scenario.Evidence);
    }

    [Fact]
    public void A_changed_call_set_with_the_same_status_is_behaviour_changed()
    {
        var roster = Roster(Ids);
        var ledger = Ledger(Enumerable.Range(1, 3).Select(i => (roster, Run(roster, i, "PPP", shapes: ["s1", "s2", "s3"], calls: [3, 1, 1]))));

        var scenario = First(Analyse(ledger, roster, Run(roster, 9, "PPP", shapes: ["s9", "s2", "s3"], calls: [5, 1, 1])));

        Assert.Equal(HistoryVerdictKind.BehaviourChanged, scenario.Primary);
        Assert.Contains("3", scenario.Evidence);
        Assert.Contains("5", scenario.Evidence);
        Assert.Equal("s1", scenario.PreviousShapeSet);
    }

    [Fact]
    public void A_changed_call_set_with_a_changed_status_is_the_status_verdict_not_drift()
    {
        // The failure explains the different calls; reporting both would be reporting the failure twice.
        var roster = Roster(Ids);
        var ledger = Ledger(Enumerable.Range(1, 3).Select(i => (roster, Run(roster, i, "PPP", shapes: ["s1", "s2", "s3"]))));

        var scenario = First(Analyse(ledger, roster, Run(roster, 9, "FPP", shapes: ["s9", "s2", "s3"])));

        Assert.Equal(HistoryVerdictKind.Broke, scenario.Primary);
        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, scenario.Verdicts);
    }

    [Fact]
    public void A_shape_that_changes_most_runs_is_unstable_and_suppresses_drift()
    {
        // §2.4: a scenario whose calls differ run to run (random data, time-dependent branching) would
        // report behaviour-changed forever. Unstable-shape says that once, and drift stays quiet.
        var roster = Roster(Ids);
        var ledger = Ledger(Enumerable.Range(1, 8).Select(i => (roster, Run(roster, i, "PPP", shapes: ["s" + i, "s2", "s3"]))));

        var scenario = First(Analyse(ledger, roster, Run(roster, 9, "PPP", shapes: ["s99", "s2", "s3"])));

        Assert.Contains(HistoryVerdictKind.UnstableShape, scenario.Verdicts);
        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, scenario.Verdicts);
        Assert.Equal(HistoryVerdictKind.UnstableShape, scenario.Primary);
    }

    // ── Alternating (plans/ALTERNATING_AND_FAILED_SENDS_PLAN.md §2) ────────

    /// <summary>Prior passing runs whose first scenario held the given sets of calls, oldest first.</summary>
    private static HistoryLedger Shapes(HistoryRoster roster, params string[] sets) =>
        Ledger(sets.Select((s, i) => (roster, Run(roster, i + 1, "PPP", shapes: [s, "s2", "s3"]))));

    private static ScenarioHistory Alternation(HistoryRoster roster, HistoryLedger ledger, string current, HistoryAnalysisOptions? options = null) =>
        First(Analyse(ledger, roster, Run(roster, 50, "PPP", shapes: [current, "s2", "s3"]), options));

    [Fact]
    public void A_return_to_a_set_of_calls_the_scenario_held_recently_is_alternating_not_behaviour_changed()
    {
        // The menu endpoint answers from a warm cache or builds the menu: two sets of calls, both the
        // scenario's own, and which one a run sees depends on ordering. The first sighting of the second
        // set is behaviour-changed (the reader learns of the state once); a return to a set held within
        // the window is the same fact again, read as a standing state.
        var roster = Roster(Ids);
        var scenario = Alternation(roster, Shapes(roster, "A", "A", "B"), "A");

        Assert.Contains(HistoryVerdictKind.Alternating, scenario.Verdicts);
        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, scenario.Verdicts);
        Assert.Equal(HistoryVerdictKind.Alternating, scenario.Primary);
        Assert.Contains("alternating between 2 sets of calls over the last 3 runs: this set in 2 of them", scenario.Evidence);
    }

    [Fact]
    public void Alternating_stands_while_both_sets_are_within_the_window()
    {
        var roster = Roster(Ids);
        var scenario = Alternation(roster, Shapes(roster, "A", "B", "A"), "A");

        Assert.Equal(HistoryVerdictKind.Alternating, scenario.Primary);
        Assert.Contains("this set in 2 of them", scenario.Evidence);
    }

    [Fact]
    public void A_set_the_scenario_never_held_is_still_behaviour_changed()
    {
        var roster = Roster(Ids);
        var scenario = Alternation(roster, Shapes(roster, "A", "A", "A"), "B");

        Assert.Contains(HistoryVerdictKind.BehaviourChanged, scenario.Verdicts);
        Assert.DoesNotContain(HistoryVerdictKind.Alternating, scenario.Verdicts);
    }

    [Fact]
    public void A_change_that_stayed_is_not_alternating()
    {
        // A A B, then B again: the earlier B is contiguous with now, nothing different came between.
        var roster = Roster(Ids);
        var scenario = Alternation(roster, Shapes(roster, "A", "A", "B"), "B");

        Assert.DoesNotContain(HistoryVerdictKind.Alternating, scenario.Verdicts);
        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, scenario.Verdicts);
        Assert.Equal(HistoryVerdictKind.Stable, scenario.Primary);
    }

    [Fact]
    public void A_set_held_only_beyond_the_alternating_window_is_a_change_again()
    {
        // The memory is short on purpose: a regression back to how the scenario behaved long ago must
        // read as a change, not as a known state.
        var roster = Roster(Ids);
        var ledger = Shapes(roster, "B", "A", "A", "A");

        var within = Alternation(roster, ledger, "B", new HistoryAnalysisOptions { AlternatingRuns = 4 });
        var beyond = Alternation(roster, ledger, "B", new HistoryAnalysisOptions { AlternatingRuns = 3 });

        Assert.Equal(HistoryVerdictKind.Alternating, within.Primary);
        Assert.Equal(HistoryVerdictKind.BehaviourChanged, beyond.Primary);
    }

    [Fact]
    public void A_scenario_flipping_between_two_sets_every_run_is_alternating_not_unstable()
    {
        // Unstable-shape is for the set that is new on most runs; a scenario with two states that keep
        // returning is alternating, and the evidence says between how many.
        var roster = Roster(Ids);
        var scenario = Alternation(roster, Shapes(roster, "A", "B", "A", "B", "A", "B", "A", "B"), "A");

        Assert.Equal(HistoryVerdictKind.Alternating, scenario.Primary);
        Assert.DoesNotContain(HistoryVerdictKind.UnstableShape, scenario.Verdicts);
        Assert.Contains("alternating between 2 sets of calls over the last 8 runs: this set in 4 of them", scenario.Evidence);
    }

    [Fact]
    public void A_failing_run_has_no_alternating_verdict_and_failed_runs_are_not_its_memory()
    {
        var roster = Roster(Ids);
        var ledger = Ledger([(roster, Run(roster, 1, "PPP", shapes: ["A", "s2", "s3"])), (roster, Run(roster, 2, "FPP", shapes: ["B", "s2", "s3"])),
            (roster, Run(roster, 3, "PPP", shapes: ["A", "s2", "s3"]))]);

        // B was only ever seen on a failing run: a failure's set is what the failure left, not a state.
        var passing = Alternation(roster, ledger, "A");
        Assert.DoesNotContain(HistoryVerdictKind.Alternating, passing.Verdicts);

        var failing = First(Analyse(ledger, roster, Run(roster, 50, "FPP", shapes: ["A", "s2", "s3"])));
        Assert.DoesNotContain(HistoryVerdictKind.Alternating, failing.Verdicts);
    }

    [Fact]
    public void Alternating_is_named_and_is_not_a_summary_count()
    {
        var roster = Roster(Ids);
        var verdicts = Analyse(Shapes(roster, "A", "A", "B"), roster, Run(roster, 50, "PPP", shapes: ["A", "s2", "s3"]));

        Assert.Equal("alternating", HistoryVerdictNames.Name(HistoryVerdictKind.Alternating));
        Assert.Equal(HistoryVerdictKind.Alternating, HistoryVerdictNames.Parse("alternating"));
        Assert.Equal(1, verdicts.Count(HistoryVerdictKind.Alternating));
        Assert.StartsWith("nothing changed", HistorySummary.Line(verdicts));
    }

    [Fact]
    public void Reordered_calls_are_reported_only_when_asked_for()
    {
        var roster = Roster(Ids);
        var ledger = Ledger(Enumerable.Range(1, 3).Select(i => (roster, Run(roster, i, "PPP", shapes: ["s1", "s2", "s3"], ordered: ["o1", "o2", "o3"]))));
        var current = Run(roster, 9, "PPP", shapes: ["s1", "s2", "s3"], ordered: ["o9", "o2", "o3"]);

        var quiet = First(Analyse(ledger, roster, current));
        var asked = First(Analyse(ledger, roster, current, new HistoryAnalysisOptions { ReportReordered = true }));

        Assert.Equal(HistoryVerdictKind.Stable, quiet.Primary);
        Assert.Equal(HistoryVerdictKind.Reordered, asked.Primary);
    }

    [Fact]
    public void A_new_dependency_pair_is_reported_at_run_level()
    {
        var roster = Roster(Ids);
        var ledger = Ledger(Enumerable.Range(1, 3).Select(i => (roster, Run(roster, i, "PPP", deps: ["Test>orders"]))));

        var verdicts = Analyse(ledger, roster, Run(roster, 9, "PPP", deps: ["Test>orders", "orders>audit"]));

        Assert.Equal(["orders>audit"], verdicts.NewDependencies);
    }

    // ─── Absence and partial runs ──────────────────────────────

    [Fact]
    public void A_scenario_in_the_previous_run_but_not_this_one_is_absent()
    {
        var full = Roster(Ids);
        var ledger = Series(full, "PPP", "PPP");
        var smaller = Roster(Ids[0], Ids[1]);

        var verdicts = Analyse(ledger, smaller, Run(smaller, 9, "PP"));

        var absent = Assert.Single(verdicts.Absent);
        Assert.Equal(Ids[2], absent.StableId);
        Assert.Equal("gh:2:1", absent.LastRunId);
        Assert.False(verdicts.Partial);
    }

    [Fact]
    public void A_run_missing_more_than_the_threshold_is_partial_and_reports_nothing_absent()
    {
        // §5.12: a filtered run - one test in the debugger - must not declare the other 299 absent, and
        // must not be the run 'fixed' is measured against.
        var full = Roster(Ids);
        var ledger = Series(full, "PPP", "PPP");
        var one = Roster(Ids[0]);

        var verdicts = Analyse(ledger, one, Run(one, 9, "P", partial: null), new HistoryAnalysisOptions { PartialThreshold = 0.10 });

        Assert.True(verdicts.Partial);
        Assert.Empty(verdicts.Absent);
    }

    [Fact]
    public void An_explicit_partial_flag_wins_over_the_heuristic()
    {
        var full = Roster(Ids);
        var ledger = Series(full, "PPP", "PPP");
        var smaller = Roster(Ids[0], Ids[1]);

        var declared = Analyse(ledger, smaller, Run(smaller, 9, "PP", partial: true));
        var denied = Analyse(ledger, Roster(Ids[0]), Run(Roster(Ids[0]), 9, "P", partial: false));

        Assert.True(declared.Partial);
        Assert.Empty(declared.Absent);
        Assert.False(denied.Partial);
        Assert.Equal(2, denied.Absent.Count);
    }

    [Fact]
    public void A_partial_prior_run_is_not_the_run_verdicts_compare_against()
    {
        var roster = Roster(Ids);
        var one = Roster(Ids[1]);
        var ledger = Ledger([(roster, Run(roster, 1, "FPP")), (one, Run(one, 2, "P", partial: true))]);

        var scenario = First(Analyse(ledger, roster, Run(roster, 9, "PPP")));

        // The last full run had it failing, so it is fixed - the partial run in between did not run it.
        Assert.Equal(HistoryVerdictKind.Fixed, scenario.Primary);
    }

    // ─── Streams ───────────────────────────────────────────────

    [Fact]
    public void Verdicts_are_computed_within_the_branch_stream()
    {
        // §7.5: a feature branch's failure is not main's history, and main's ten green runs are not the
        // feature branch's. The current run's branch picks the stream.
        var roster = Roster(Ids);
        var ledger = Ledger([
            (roster, Run(roster, 1, "PPP", branch: "main")),
            (roster, Run(roster, 2, "PPP", branch: "main")),
            (roster, Run(roster, 3, "FPP", branch: "feature/x"))
        ]);

        var onFeature = First(Analyse(ledger, roster, Run(roster, 9, "FPP", branch: "feature/x")));
        var onMain = First(Analyse(ledger, roster, Run(roster, 9, "FPP", branch: "main")));

        Assert.Equal(HistoryVerdictKind.AlwaysFailing, onFeature.Primary);
        Assert.Equal(HistoryVerdictKind.Broke, onMain.Primary);
        Assert.Equal("feature/x", Analyse(ledger, roster, Run(roster, 9, "FPP", branch: "feature/x")).Stream);
    }

    [Fact]
    public void Runs_without_a_branch_form_the_local_stream()
    {
        var roster = Roster(Ids);
        var ledger = Ledger([(roster, Run(roster, 1, "PPP", branch: null)), (roster, Run(roster, 2, "PPP", branch: "main"))]);

        var verdicts = Analyse(ledger, roster, Run(roster, 9, "FPP", branch: null));

        Assert.Equal("local", verdicts.Stream);
        Assert.Equal(1, verdicts.RunsRecorded);
    }

    [Fact]
    public void A_compare_branch_gives_a_second_reading_against_the_other_stream()
    {
        var roster = Roster(Ids);
        var ledger = Ledger([
            (roster, Run(roster, 1, "PPP", branch: "main")),
            (roster, Run(roster, 2, "PPP", branch: "main")),
            (roster, Run(roster, 3, "FPP", branch: "feature/x"))
        ]);

        var verdicts = Analyse(ledger, roster, Run(roster, 9, "FPP", branch: "feature/x"), new HistoryAnalysisOptions { CompareBranch = "main" });

        Assert.NotNull(verdicts.Compare);
        Assert.Equal("main", verdicts.Compare!.Stream);
        Assert.Equal(HistoryVerdictKind.Broke, verdicts.Compare.Scenarios[0].Primary);
        Assert.Equal(HistoryVerdictKind.AlwaysFailing, verdicts.Scenarios[0].Primary);
    }

    // ─── Run-level ─────────────────────────────────────────────

    [Fact]
    public void The_run_level_view_counts_verdicts_and_carries_the_series()
    {
        var roster = Roster(Ids);
        var ledger = Series(roster, "PPP", "PFP", "PFP");

        var verdicts = Analyse(ledger, roster, Run(roster, 9, "FPP", durations: [10, 20, 30]));

        Assert.Equal(1, verdicts.Counts[HistoryVerdictKind.Broke]);
        Assert.Equal(1, verdicts.Counts[HistoryVerdictKind.Fixed]);
        Assert.Equal(1, verdicts.Counts[HistoryVerdictKind.Stable]);
        Assert.Equal(4, verdicts.Runs.Count);
        Assert.Equal("gh:9:1", verdicts.Runs[^1].RunId);
        Assert.Equal(2, verdicts.Runs[^1].Passed);
        Assert.Equal(1, verdicts.Runs[^1].Failed);
        Assert.Equal(60, verdicts.Runs[^1].DurationMs);
        Assert.Equal("PPPF", First(verdicts).Series);
        Assert.Equal(3, verdicts.RunsRecorded);
    }

    [Fact]
    public void The_current_run_already_in_the_ledger_is_not_counted_as_its_own_history()
    {
        // `kronikol query history` runs after the line was appended: the current run must be excluded
        // from its own prior runs or every scenario compares equal to itself.
        var roster = Roster(Ids);
        var current = Run(roster, 9, "FPP");
        var ledger = Ledger([(roster, Run(roster, 1, "PPP")), (roster, current)]);

        var scenario = First(Analyse(ledger, roster, current));

        Assert.Equal(HistoryVerdictKind.Broke, scenario.Primary);
        Assert.Equal(1, Analyse(ledger, roster, current).RunsRecorded);
    }

    [Fact]
    public void Verdict_names_are_the_kebab_case_vocabulary()
    {
        Assert.Equal("always-failing", HistoryVerdictNames.Name(HistoryVerdictKind.AlwaysFailing));
        Assert.Equal("behaviour-changed", HistoryVerdictNames.Name(HistoryVerdictKind.BehaviourChanged));
        Assert.Equal("unstable-shape", HistoryVerdictNames.Name(HistoryVerdictKind.UnstableShape));
        Assert.Equal(HistoryVerdictKind.Flaky, HistoryVerdictNames.Parse("flaky"));
        Assert.Null(HistoryVerdictNames.Parse("nonsense"));
        Assert.Equal(Enum.GetValues<HistoryVerdictKind>().Length, HistoryVerdictNames.All.Count);
    }

    // ─── Quarantine and aliases ────────────────────────────────

    [Fact]
    public void A_quarantined_scenario_carries_the_verdict_additively()
    {
        var roster = Roster(Ids);
        var ledger = Series(roster, "PPP", "PPP");
        var quarantine = new HistoryQuarantineList();
        quarantine.Add(Ids[0], "known flake, ticket 123", "me", new DateOnly(2026, 9, 1), until: null);

        var scenario = First(HistoryAnalyzer.Analyse(ledger, roster, Run(roster, 9, "FPP"), new HistoryAnalysisOptions(), quarantine));

        Assert.Contains(HistoryVerdictKind.Quarantined, scenario.Verdicts);
        Assert.Equal(HistoryVerdictKind.Broke, scenario.Primary);
        Assert.Equal("known flake, ticket 123", scenario.Quarantine!.Reason);
    }

    [Fact]
    public void An_expired_quarantine_no_longer_applies()
    {
        var quarantine = new HistoryQuarantineList();
        quarantine.Add(Ids[0], "for a week", "me", new DateOnly(2026, 9, 1), until: new DateOnly(2026, 9, 8));

        Assert.NotNull(quarantine.Find(Ids[0], new DateOnly(2026, 9, 7)));
        Assert.Null(quarantine.Find(Ids[0], new DateOnly(2026, 9, 9)));
    }

    [Fact]
    public void A_renamed_scenario_keeps_its_history_through_an_alias()
    {
        var oldRoster = Roster("oldoldoldoldold1", Ids[1]);
        var ledger = Series(oldRoster, "FP", "FP");
        var newRoster = Roster(Ids[0], Ids[1]);
        var aliases = new HistoryAliases();
        aliases.Add("oldoldoldoldold1", Ids[0]);

        var scenario = First(HistoryAnalyzer.Analyse(ledger, newRoster, Run(newRoster, 9, "FP"), new HistoryAnalysisOptions(), aliases: aliases));

        Assert.Equal(HistoryVerdictKind.AlwaysFailing, scenario.Primary);
        Assert.Equal(2, scenario.RunsSeen);
    }

    // ─── Cost (#91) ────────────────────────────────────────────

    /// <summary>A list that counts how often it is read, by index or by enumeration.</summary>
    private sealed class CountingList(IReadOnlyList<string> inner) : IReadOnlyList<string>
    {
        public long Reads { get; private set; }
        public string this[int index] { get { Reads++; return inner[index]; } }
        public int Count => inner.Count;
        public IEnumerator<string> GetEnumerator() { foreach (var item in inner) { Reads++; yield return item; } }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A ledger made in memory rather than parsed, so that the roster it holds can be an instrumented one.</summary>
    private static HistoryLedger InMemory(HistoryRoster recorded, IReadOnlyList<HistoryRun> prior)
    {
        var lines = new HistorySuiteLines();
        for (var i = 0; i < prior.Count; i++)
        {
            lines.Entries.Add((prior[i].Id, prior[i].Stream));
            lines.Parsed[i] = prior[i];
        }
        return new HistoryLedger(HistoryFormat.Version, "test",
            new Dictionary<string, HistoryRoster>(StringComparer.Ordinal) { [recorded.Hash] = recorded },
            new Dictionary<string, List<HistoryRun>>(StringComparer.Ordinal) { ["Suite"] = [.. prior] },
            new HistoryStats(0, 0, prior.Count, 1, 0, TimeSpan.Zero),
            lines: new Dictionary<string, HistorySuiteLines>(StringComparer.Ordinal) { ["Suite"] = lines });
    }

    [Fact]
    public void A_prior_roster_is_read_a_few_times_per_analysis_not_once_per_scenario()
    {
        // #91: every scenario was looked up in every prior roster by scanning it, so the analysis was
        // quadratic in the scenario count: 2 s at 5,000 x 50 on every run, 7 s through the tool. Counted,
        // not timed: two timings of the same analysis in a parallel suite were measured further apart
        // than the quadratic analyzer and the linear one are. The runs carry durations so that the pass
        // that paces them is under the count as well.
        const int scenarios = 400, runs = 20;
        var roster = Roster(Enumerable.Range(0, scenarios).Select(i => i.ToString("x16")).ToArray());
        var ids = new CountingList(roster.Ids);
        var recorded = roster with { Ids = ids };
        var durations = Enumerable.Range(0, scenarios).Select(i => (int?)(20 + i % 50)).ToArray();
        var prior = Enumerable.Range(1, runs).Select(n => Run(roster, n, new string('P', scenarios), durations: durations)).ToList();
        var ledger = InMemory(recorded, prior);

        var verdicts = HistoryAnalyzer.Analyse(ledger, roster, Run(roster, runs + 1, new string('P', scenarios), durations: durations), new HistoryAnalysisOptions());

        // An analysis that read nothing because it found nothing is not what is being counted.
        Assert.Equal(runs, verdicts.RunsRecorded);
        Assert.All(verdicts.Scenarios, s => Assert.Equal(runs, s.RunsSeen));
        Assert.NotNull(verdicts.Runs[^1].Pace);
        // One read per position to index the roster, one to pace its runs, one for the absent pass, and
        // room for one more pass of that kind. It does not grow with the window: a second prior run on
        // the same roster costs nothing.
        Assert.True(ids.Reads <= scenarios * 4, $"the prior roster's {scenarios} ids were read {ids.Reads} times over {runs} runs");
    }

    [Fact]
    public void The_first_holder_of_a_repeated_id_and_slot_is_the_one_a_scenario_is_read_from()
    {
        // A well-formed roster holds each (id, slot) once, but a roster line comes from a file, and union
        // merges are the reason the format exists. The scan took the first position holding the pair; so
        // does the index that replaced it.
        var twice = Roster(Ids[0], Ids[0]) with { Slots = [0, 0] };
        var ledger = Ledger(Enumerable.Range(1, 3).Select(n => (twice, Run(twice, n, "FP"))));
        var roster = Roster(Ids[0]);

        var scenario = First(Analyse(ledger, roster, Run(roster, 9, "F")));

        Assert.Equal(3, scenario.RunsSeen);
        Assert.Equal("FFFF", scenario.Series);
        Assert.Equal(HistoryVerdictKind.AlwaysFailing, scenario.Primary);
    }

    /// <summary>A ledger of one roster over 51 runs whose lines carry what a real run writes: durations, calls, shapes, call sets, errors.</summary>
    private static (HistoryLedger Ledger, HistoryRoster Roster, HistoryRun Current, HistoryShapes Shapes) RealisticLedger(int scenarios)
    {
        var random = new Random(3);
        var roster = Roster(Enumerable.Range(0, scenarios).Select(i => i.ToString("x16")).ToArray());
        var shapes = HistoryShapes.Create(Enumerable.Range(0, 40).Select(i => $"Caller>Svc GET /thing{i:D2} 200"));
        var fingerprints = Enumerable.Range(0, scenarios).Select(_ => random.Next().ToString("x8")).ToArray();
        var callSets = Enumerable.Range(0, scenarios)
            .Select(_ => (IReadOnlyList<int>)Enumerable.Range(0, 5).Select(_ => random.Next(40)).Distinct().OrderBy(i => i).ToArray()).ToArray();
        var text = new System.Text.StringBuilder();
        text.Append(HistoryJson.HeaderLine("test")).Append('\n').Append(HistoryJson.RosterLine(roster)).Append('\n').Append(HistoryJson.ShapesLine(shapes)).Append('\n');
        HistoryRun current = null!;
        for (var n = 1; n <= 51; n++)
        {
            var results = new string(Enumerable.Range(0, scenarios).Select(_ => random.NextDouble() < 0.004 ? 'F' : 'P').ToArray());
            current = Run(roster, n, results, durations: Enumerable.Range(0, scenarios).Select(_ => (int?)random.Next(5, 900)).ToArray(),
                shapes: fingerprints, calls: Enumerable.Repeat(5, scenarios).ToArray()) with { ShapesHash = shapes.Hash, CallSets = callSets };
            text.Append(HistoryJson.RunLine(current)).Append('\n');
        }
        return (HistoryLedgerReader.Parse(text.ToString(), window: 51).Ledger!, roster, current, shapes);
    }

    [Fact]
    public void An_analysis_does_not_spell_out_the_calls_of_every_point()
    {
        // #91: every point of every scenario had its call set resolved into a new array, and only the
        // points a changed set is named from were ever read. Allocation on this thread is exact, where a
        // timing in a parallel suite is not: 36,910 bytes per scenario before, 21,354 after, the same to
        // the byte on every run. It is a bound on a 50-run window and on what a HistoryPoint holds: a
        // change that adds a field to the point re-pins it, knowingly.
        const int scenarios = 300;
        var (ledger, roster, current, shapes) = RealisticLedger(scenarios);
        HistoryAnalyzer.Analyse(ledger, roster, current, new HistoryAnalysisOptions(), shapes: shapes); // warm

        var before = GC.GetAllocatedBytesForCurrentThread();
        var verdicts = HistoryAnalyzer.Analyse(ledger, roster, current, new HistoryAnalysisOptions(), shapes: shapes);
        var perScenario = (GC.GetAllocatedBytesForCurrentThread() - before) / scenarios;

        Assert.Equal(50, verdicts.RunsRecorded);
        Assert.True(perScenario < 28 * 1024, $"{perScenario} bytes allocated per scenario over a 50-run window");
    }
}
