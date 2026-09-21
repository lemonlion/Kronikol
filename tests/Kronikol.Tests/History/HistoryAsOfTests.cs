using Kronikol.History;

namespace Kronikol.Tests.History;

/// <summary>
/// A run reads against the ledger as it stood before the run's own line. While the run is being
/// generated that is the whole ledger, because its line is not appended yet. On any later read of the
/// same report (<c>kronikol query history</c>, <c>history gate</c>, <c>merge --history</c>) there may be
/// runs after it, and those are not its history. Two ways it used to go wrong, both pinned here: the
/// analyzer excluded the current run by id and never cut the ledger at its line, and the reader kept
/// the last <c>window</c> lines of a suite whatever their stream and wherever the run sat, so an older
/// report saw nothing but later runs and a quiet target branch was crowded out by pull request runs.
/// </summary>
public class HistoryAsOfTests
{
    private static readonly string[] Ids = ["cccc000000000001", "cccc000000000002"];

    private static HistoryRoster Roster(string suite = "Suite") =>
        HistoryRoster.Create(suite, Ids.Select(id => new HistoryRosterEntry(id, "Scenario " + id[^1], "Feature", null)).ToArray());

    private static HistoryRun Run(HistoryRoster roster, int n, string results, string? branch = "main") => new()
    {
        Id = $"gh:{n}:1",
        Suite = roster.Suite,
        Partial = false,
        At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(n),
        Branch = branch,
        Commit = $"c{n:D6}",
        Provider = "GitHubActions",
        Url = null,
        Shards = 1,
        RosterHash = roster.Hash,
        Results = results,
        Attempts = new string('-', results.Length),
        Errors = results.Select(r => r == 'F' ? "e1" : null).ToArray(),
        ErrorText = results.Contains('F') ? new Dictionary<string, string> { ["e1"] = "Expected 200 but got 500" } : new Dictionary<string, string>(),
        Deps = ["Test>orders"]
    };

    private static string Text(HistoryRoster roster, IEnumerable<HistoryRun> runs) =>
        HistoryJson.HeaderLine("test") + "\n" + HistoryJson.RosterLine(roster) + "\n" + string.Join("\n", runs.Select(HistoryJson.RunLine)) + "\n";

    private static HistoryLedger Ledger(HistoryRoster roster, IEnumerable<HistoryRun> runs, int window = 50) =>
        HistoryLedgerReader.Parse(Text(roster, runs), window).Ledger!;

    private static HistoryVerdicts Analyse(HistoryLedger ledger, HistoryRoster roster, HistoryRun current, HistoryAnalysisOptions? options = null) =>
        HistoryAnalyzer.Analyse(ledger, roster, current, options ?? new HistoryAnalysisOptions());

    // ─── The analyzer: the run's own line ends its history ─────

    [Fact]
    public void A_run_reads_against_the_runs_appended_before_it_and_not_the_ones_after()
    {
        // Runs 1 to 3 pass, 4 and 5 fail. Read again once 4 and 5 are in the ledger, run 3 used to read
        // "fixed - failed the previous 2 runs", and the two runs it was fixed from are later ones.
        var roster = Roster();
        var runs = new[] { Run(roster, 1, "PP"), Run(roster, 2, "PP"), Run(roster, 3, "PP"), Run(roster, 4, "FP"), Run(roster, 5, "FP") };
        var verdicts = Analyse(Ledger(roster, runs), roster, runs[2]);

        Assert.Equal(2, verdicts.RunsRecorded);
        Assert.Equal("PPP", verdicts.Scenarios[0].Series);
        Assert.False(verdicts.Scenarios[0].Has(HistoryVerdictKind.Fixed));
    }

    [Fact]
    public void A_run_reads_the_same_as_the_last_line_of_the_ledger_and_in_the_middle_of_it()
    {
        var roster = Roster();
        var runs = Enumerable.Range(1, 12).Select(n => Run(roster, n, n is 4 or 9 or 10 ? "FP" : "PP")).ToArray();
        var current = runs[7];

        var then = Analyse(Ledger(roster, runs.Take(8)), roster, current);
        var later = Analyse(Ledger(roster, runs), roster, current);

        Assert.Equal(then.RunsRecorded, later.RunsRecorded);
        for (var i = 0; i < Ids.Length; i++)
        {
            Assert.Equal(then.Scenarios[i].Series, later.Scenarios[i].Series);
            Assert.Equal(then.Scenarios[i].Primary, later.Scenarios[i].Primary);
            Assert.Equal(then.Scenarios[i].Verdicts.Order(), later.Scenarios[i].Verdicts.Order());
            Assert.Equal(then.Scenarios[i].Evidence, later.Scenarios[i].Evidence);
        }
    }

    [Fact]
    public void A_run_that_is_not_in_the_ledger_reads_against_all_of_it()
    {
        // The run being generated: its line is appended after the analysis, so everything there is earlier.
        var roster = Roster();
        var runs = Enumerable.Range(1, 5).Select(n => Run(roster, n, "PP")).ToArray();

        var verdicts = Analyse(Ledger(roster, runs), roster, Run(roster, 6, "FP"));

        Assert.Equal(5, verdicts.RunsRecorded);
        Assert.Equal(HistoryVerdictKind.Broke, verdicts.Scenarios[0].Primary);
    }

    [Fact]
    public void A_pull_request_reads_its_target_as_the_target_stood_when_the_run_was_recorded()
    {
        var roster = Roster();
        var current = Run(roster, 4, "PP", branch: "feature/x");
        var runs = new[] { Run(roster, 1, "PP"), Run(roster, 2, "PP"), Run(roster, 3, "PP"), current, Run(roster, 5, "FP"), Run(roster, 6, "FP") };

        var verdicts = Analyse(Ledger(roster, runs), roster, current, new HistoryAnalysisOptions { Branch = "main" });

        Assert.Equal(3, verdicts.RunsRecorded);
        Assert.Equal("PPPP", verdicts.Scenarios[0].Series);
    }

    [Fact]
    public void The_compare_stream_is_cut_at_the_same_line()
    {
        var roster = Roster();
        var current = Run(roster, 4, "PP", branch: "feature/x");
        var runs = new[] { Run(roster, 1, "PP"), Run(roster, 2, "PP", branch: "feature/x"), Run(roster, 3, "PP"), current, Run(roster, 5, "FP"), Run(roster, 6, "FP", branch: "feature/x") };

        var verdicts = Analyse(Ledger(roster, runs), roster, current, new HistoryAnalysisOptions { CompareBranch = "main" });

        Assert.Equal(1, verdicts.RunsRecorded);
        Assert.NotNull(verdicts.Compare);
        Assert.Equal(2, verdicts.Compare!.RunsRecorded);
        Assert.Equal("PPP", verdicts.Compare.Scenarios[0].Series);
    }

    // ─── The reader: the window ends at the run, and belongs to the stream ─────

    [Fact]
    public void A_run_older_than_the_window_still_reads_its_own_past()
    {
        // Sixty runs, a window of fifty, and the report in hand is run 5: its line is outside the last
        // fifty, so it used to read against runs 11 to 60, every one of them later than itself.
        var roster = Roster();
        var runs = Enumerable.Range(1, 60).Select(n => Run(roster, n, n <= 5 ? "PP" : "FP")).ToArray();

        var verdicts = Analyse(Ledger(roster, runs, window: 50), roster, runs[4]);

        Assert.Equal(4, verdicts.RunsRecorded);
        Assert.Equal("PPPPP", verdicts.Scenarios[0].Series);
        Assert.False(verdicts.Scenarios[0].Has(HistoryVerdictKind.Fixed));
    }

    [Fact]
    public void Pull_request_runs_do_not_crowd_the_target_branch_out_of_its_own_window()
    {
        // Ten runs of main, then sixty pull request runs of the same suite. The last fifty lines of the
        // suite hold no run of main at all, so a new run of main used to read as the first there ever was.
        var roster = Roster();
        var runs = Enumerable.Range(1, 10).Select(n => Run(roster, n, "PP"))
            .Concat(Enumerable.Range(11, 60).Select(n => Run(roster, n, "PP", branch: $"pr/{n % 7}")))
            .ToArray();

        var verdicts = Analyse(Ledger(roster, runs, window: 50), roster, Run(roster, 99, "FP"));

        Assert.Equal(10, verdicts.RunsRecorded);
        Assert.Equal(HistoryVerdictKind.Broke, verdicts.Scenarios[0].Primary);
    }

    [Fact]
    public void The_analysis_window_still_bounds_how_far_back_a_stream_is_read()
    {
        var roster = Roster();
        var runs = Enumerable.Range(1, 30).Select(n => Run(roster, n, "PP")).ToArray();

        var verdicts = Analyse(Ledger(roster, runs, window: 50), roster, Run(roster, 99, "PP"), new HistoryAnalysisOptions { Window = 8 });

        Assert.Equal(8, verdicts.RunsRecorded);
    }

    [Fact]
    public void A_run_recorded_twice_ends_its_history_at_its_first_line()
    {
        // merge=union can leave the same run line on both sides of a merge; the first is when it was recorded.
        var roster = Roster();
        var current = Run(roster, 3, "PP");
        var runs = new[] { Run(roster, 1, "PP"), Run(roster, 2, "PP"), current, Run(roster, 4, "FP"), current, Run(roster, 5, "FP") };

        var verdicts = Analyse(Ledger(roster, runs), roster, current);

        Assert.Equal(2, verdicts.RunsRecorded);
    }

    // ─── What it costs, and what it leaves alone ───────────────

    [Fact]
    public void The_newest_run_of_the_only_stream_parses_nothing_the_read_had_not_parsed()
    {
        // The test run's own path: one stream, the run not in the ledger yet. The lines the analysis wants
        // are the lines the read already parsed, so reading a stream back costs no second parse.
        var roster = Roster();
        var ledger = Ledger(roster, Enumerable.Range(1, 80).Select(n => Run(roster, n, "PP")), window: 50);

        Analyse(ledger, roster, Run(roster, 99, "PP"));

        Assert.Equal(0, ledger.LinesParsedOnDemand);
    }

    [Fact]
    public void Reading_a_stream_back_parses_no_more_than_the_analysis_window()
    {
        var roster = Roster();
        var runs = Enumerable.Range(1, 200).Select(n => Run(roster, n, "PP", branch: n % 4 == 0 ? "main" : $"pr/{n % 5}")).ToArray();
        var ledger = Ledger(roster, runs, window: 50);

        var verdicts = Analyse(ledger, roster, Run(roster, 999, "PP"), new HistoryAnalysisOptions { Window = 20 });

        Assert.Equal(20, verdicts.RunsRecorded);
        Assert.InRange(ledger.LinesParsedOnDemand, 0, 20);
    }

    [Fact]
    public void The_runs_a_ledger_lists_for_a_suite_are_still_the_last_of_the_window()
    {
        // `Runs` is what `history show` and the writer read: the last `window` lines of the suite, as before.
        var roster = Roster();
        var runs = Enumerable.Range(1, 60).Select(n => Run(roster, n, "PP")).ToArray();
        var ledger = Ledger(roster, runs, window: 50);

        Analyse(ledger, roster, runs[4]);

        Assert.Equal(50, ledger.Runs(roster.Suite).Count);
        Assert.Equal("gh:11:1", ledger.Runs(roster.Suite)[0].Id);
        Assert.Equal("gh:60:1", ledger.LatestRun(roster.Suite)!.Id);
    }

    // ─── The ledger on disk: the lines the window let go are read again ─────

    private static string Write(string text)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("kronikol-as-of").FullName, "history.jsonl");
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void A_ledger_on_disk_is_read_again_for_the_lines_the_window_let_go()
    {
        var roster = Roster();
        var runs = Enumerable.Range(1, 60).Select(n => Run(roster, n, n <= 5 ? "PP" : "FP")).ToArray();
        var path = Write(Text(roster, runs));
        try
        {
            var ledger = HistoryLedgerReader.Read(path, 50).Ledger!;

            var verdicts = Analyse(ledger, roster, runs[4]);

            Assert.Equal(4, verdicts.RunsRecorded);
            Assert.Equal(4, ledger.LinesParsedOnDemand);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [Fact]
    public void A_ledger_rewritten_since_the_read_is_not_trusted_by_position()
    {
        // `kronikol history prune` between the read and the analysis: a place among the suite's runs now
        // names a different line. The id says so, and the run reads no history rather than somebody else's.
        var roster = Roster();
        var runs = Enumerable.Range(1, 60).Select(n => Run(roster, n, n <= 5 ? "PP" : "FP")).ToArray();
        var path = Write(Text(roster, runs));
        try
        {
            var ledger = HistoryLedgerReader.Read(path, 50).Ledger!;
            File.WriteAllText(path, Text(roster, runs.Skip(20)));

            var verdicts = Analyse(ledger, roster, runs[4]);

            Assert.Equal(0, verdicts.RunsRecorded);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [Fact]
    public void A_ledger_gone_since_the_read_costs_the_lines_it_let_go_and_nothing_else()
    {
        var roster = Roster();
        var runs = Enumerable.Range(1, 60).Select(n => Run(roster, n, "PP")).ToArray();
        var path = Write(Text(roster, runs));
        var ledger = HistoryLedgerReader.Read(path, 50).Ledger!;
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);

        // Run 5's past was outside the window and cannot be read again; a new run's is what the read kept.
        Assert.Equal(0, Analyse(ledger, roster, runs[4]).RunsRecorded);
        Assert.Equal(50, Analyse(ledger, roster, Run(roster, 99, "PP")).RunsRecorded);
    }

    // ─── The index: id and stream from a peek ─────

    [Fact]
    public void A_run_with_no_branch_is_indexed_under_the_local_stream()
    {
        var roster = Roster();
        var runs = new[] { Run(roster, 1, "PP", branch: null), Run(roster, 2, "PP"), Run(roster, 3, "PP", branch: null) };

        var verdicts = Analyse(Ledger(roster, runs), roster, Run(roster, 4, "FP", branch: null));

        Assert.Equal(2, verdicts.RunsRecorded);
        Assert.Equal("local", verdicts.Stream);
    }

    [Fact]
    public void The_peek_reads_a_run_lines_branch_whichever_side_of_the_suite_it_sits()
    {
        // Kronikol writes the suite first. A writer that does not is still a ledger line.
        Assert.Equal((HistoryLineKind.Run, "gh:1:1", "Suite", false, "main"),
            HistoryJson.PeekRun("""{"t":"run","id":"gh:1:1","suite":"Suite","partial":false,"at":"2026-09-01T00:00:00Z","branch":"main","results":"PP"}"""));
        Assert.Equal((HistoryLineKind.Run, "gh:1:1", "Suite", false, "main"),
            HistoryJson.PeekRun("""{"t":"run","id":"gh:1:1","branch":"main","suite":"Suite","results":"PP"}"""));
        Assert.Equal((HistoryLineKind.Run, "gh:1:1", "Suite", false, null),
            HistoryJson.PeekRun("""{"t":"run","id":"gh:1:1","suite":"Suite","branch":null,"results":"PP"}"""));
        Assert.Equal((HistoryLineKind.Run, "gh:1:1", "Suite", false, null),
            HistoryJson.PeekRun("""{"t":"run","id":"gh:1:1","suite":"Suite","results":"PP"}"""));
    }

    [Fact]
    public void The_public_peek_still_stops_at_the_suite()
    {
        Assert.Equal((HistoryLineKind.Run, "gh:1:1", "Suite", false),
            HistoryJson.Peek("""{"t":"run","id":"gh:1:1","suite":"Suite","branch":"main","results":"PP"}"""));
        // A roster line carries a suite and no branch: both peeks are done with it at the suite.
        Assert.Equal((HistoryLineKind.Roster, "abcd", "Suite", false, null),
            HistoryJson.PeekRun("""{"t":"roster","hash":"abcd","suite":"Suite","ids":[]}"""));
    }

    [Fact]
    public void A_damaged_line_outside_the_window_is_skipped_when_a_stream_is_read_back()
    {
        var roster = Roster();
        var runs = Enumerable.Range(1, 60).Select(n => Run(roster, n, n <= 5 ? "PP" : "FP")).ToArray();
        var lines = Text(roster, runs).Split('\n').ToList();
        // Run 2's line keeps enough to be bucketed (kind, id, suite, branch) and cannot be parsed in full.
        var damaged = lines.FindIndex(l => l.Contains("\"id\":\"gh:2:1\"", StringComparison.Ordinal));
        lines[damaged] = lines[damaged][..lines[damaged].IndexOf("\"commit\"", StringComparison.Ordinal)] + "\"results\":12}";
        var ledger = HistoryLedgerReader.Parse(string.Join("\n", lines), 50).Ledger!;

        var verdicts = Analyse(ledger, roster, runs[4]);

        Assert.Equal(3, verdicts.RunsRecorded);
    }
}
