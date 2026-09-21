using System.Globalization;
using System.Text.Json;
using Kronikol.History;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol query history</c> when the answer is empty (plans/EVIDENCE_SURVIVES_A_RERUN_PLAN.md §5, #84):
/// the note says what it is empty OF - this run - and points at what the window still holds, with the
/// reason a near-miss missed taken from the analyzer rather than guessed.
/// </summary>
public class QueryHistoryHintsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-query-hints").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Ledger => Path.Combine(_dir, ".kronikol", "history.jsonl");

    private (string Output, string Error, int Exit) Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error, _ => null, workingDirectory: _dir);
        return (output.ToString(), error.ToString(), exit);
    }

    private static string Id(int scenario) => scenario.ToString("x16", CultureInfo.InvariantCulture);

    private static HistoryRoster RosterOf(IEnumerable<int> scenarios) =>
        HistoryRoster.Create("Suite", scenarios.Select(s => new HistoryRosterEntry(Id(s), $"Scenario {s}", "Checkout", null)).ToArray());

    /// <summary>One recorded run on main: <paramref name="results"/> is one character per scenario of <paramref name="scenarios"/>.</summary>
    private void Record(int number, IReadOnlyList<int> scenarios, string results, bool partial = false)
    {
        var roster = RosterOf(scenarios);
        HistoryLedgerWriter.Append(Ledger, roster, new HistoryRun
        {
            Id = $"gh:{number}:1", Suite = "Suite", Partial = partial, At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(number),
            Branch = "main", Commit = $"c{number:D6}", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
            Results = results, Attempts = new string('-', results.Length), Durations = results.Select(_ => (int?)100).ToArray(),
            Calls = null, ShapeSet = null, ShapeOrdered = null,
            Errors = results.Select(r => r == 'F' ? "e1" : null).ToArray(),
            ErrorText = results.Contains('F') ? new Dictionary<string, string> { ["e1"] = "Expected 200 but got 500" } : new Dictionary<string, string>()
        }, "3.9.0");
    }

    private static readonly int[] Full = Enumerable.Range(0, 12).ToArray();

    private static string Green(int count) => new('P', count);

    private static string With(string results, int position, char result) =>
        results[..position] + result + results[(position + 1)..];

    /// <summary>The current run's report: the scenarios named, every one with the result given.</summary>
    private string WriteReport(IReadOnlyList<int> scenarios, string results, string runId = "99")
    {
        var directory = Path.Combine(_dir, "reports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "TestRunReport.json");
        var rows = scenarios.Select((s, i) =>
            $$"""{ "id": "t{{s}}", "stableId": "{{Id(s)}}", "name": "Scenario {{s}}", "result": "{{(results[i] == 'F' ? "Failed" : "Passed")}}", "durationSeconds": 0.1, "errorMessage": {{(results[i] == 'F' ? "\"Expected 200 but got 500\"" : "null")}}, "labels": [], "categories": [], "steps": [], "httpInteractions": [] }""");
        File.WriteAllText(path, $$"""
            {
              "kronikolVersion": "3.9.0", "formatVersion": 1, "suite": "Suite",
              "startTime": "2026-09-12T10:00:00Z", "endTime": "2026-09-12T10:05:00Z",
              "ciMetadata": { "provider": "GitHubActions", "buildNumber": "42", "branch": "main", "commitSha": "abc1234", "pipelineUrl": null, "repository": "o/r", "runId": "{{runId}}", "runAttempt": "1" },
              "features": [ { "name": "Checkout", "labels": [], "scenarios": [ {{string.Join(",\n", rows)}} ] } ]
            }
            """);
        return path;
    }

    /// <summary>#84's session: two green runs, a full run where scenario 3 (and 7) fail, then filtered green re-runs of 3 and 4.</summary>
    private string TheIssue()
    {
        Record(1, Full, Green(12));
        Record(2, Full, Green(12));
        Record(3, Full, With(With(Green(12), 3, 'F'), 7, 'F'));
        Record(4, [3, 4], "PP", partial: true);
        return WriteReport([3, 4], "PP");
    }

    [Fact]
    public void An_empty_failing_filter_names_what_failed_earlier_in_the_window()
    {
        var report = TheIssue();

        var (output, error, exit) = Query("history", report, "--failing");

        Assert.True(exit == 0, error);
        Assert.True(output.Contains("no scenario is failing in this run"), output);
        Assert.DoesNotContain("no scenario with a verdict", output);
        Assert.Contains("1 scenario here failed earlier in the window: s0 (2 runs ago, gh:3:1)", output);
        Assert.Contains("next: history s0", output);
    }

    [Fact]
    public void A_flaky_near_miss_is_given_the_analyzer_s_reason_and_never_the_bar_it_met()
    {
        // P P F P P: five verdicts, which MEETS --min-runs 5. It is not flaky because it is one failing episode.
        var report = TheIssue();

        var (output, error, exit) = Query("history", report, "--flaky");

        Assert.True(exit == 0, error);
        Assert.True(output.Contains("no scenario is flaky as of this run"), output);
        Assert.Contains("1 scenario has flips but is not flaky: s0 — 2 flips, one failing episode", output);
        Assert.Contains("flaky needs two", output);
        Assert.DoesNotContain("--min-runs", output);
    }

    [Fact]
    public void When_the_bar_is_the_only_obstacle_the_hint_names_it_with_both_numbers()
    {
        // F P F P: two failing episodes and every transition a flip - flaky but for the four verdicts.
        Record(1, [3], "F");
        Record(2, [3], "P");
        Record(3, [3], "F");
        var report = WriteReport([3], "P");

        var (output, error, exit) = Query("history", report, "--flaky");

        Assert.True(exit == 0, error);
        Assert.True(output.Contains("4 of the 5 verdicts flaky needs (--min-runs)"), output);
        Assert.DoesNotContain("one failing episode", output);
        // And lowering it is all it takes.
        Assert.DoesNotContain("no scenario is flaky", Query("history", report, "--flaky", "--min-runs", "4").Output);
    }

    [Fact]
    public void One_episode_under_the_bar_leads_with_the_episode_because_lowering_the_bar_would_not_make_it_flaky()
    {
        // P F P P with --min-runs 5: under the bar AND one episode. The plan's first draft printed the bar.
        Record(1, [3], "P");
        Record(2, [3], "F");
        Record(3, [3], "P");
        var report = WriteReport([3], "P");

        var (output, error, exit) = Query("history", report, "--flaky");
        var lowered = Query("history", report, "--flaky", "--min-runs", "3");

        Assert.True(exit == 0, error);
        Assert.True(output.Contains("one failing episode"), output);
        Assert.Contains("and 4 of the 5 verdicts", output);
        // The proof that the bar was never the obstacle: at the lower bar it is still not flaky.
        Assert.Contains("no scenario is flaky as of this run", lowered.Output);
        Assert.DoesNotContain("verdicts flaky needs", lowered.Output);
    }

    [Fact]
    public void A_full_green_run_over_a_clean_window_prints_the_note_and_no_hint()
    {
        Record(1, Full, Green(12));
        Record(2, Full, Green(12));
        var report = WriteReport(Full, Green(12));

        var (output, error, exit) = Query("history", report, "--failing");

        Assert.True(exit == 0, error);
        Assert.True(output.Contains("no scenario is failing in this run · 12 scenarios read against 2 earlier run(s)"), output);
        Assert.DoesNotContain("earlier in the window", output);
        Assert.DoesNotContain("this run is partial", output);
        Assert.DoesNotContain("also:", output);
    }

    [Fact]
    public void A_partial_run_says_so_on_its_own_line_with_both_counts()
    {
        var report = TheIssue();

        var (output, error, exit) = Query("history", report);

        Assert.True(exit == 0, error);
        Assert.True(output.Contains("this run is partial: 2 scenarios, the last full run had 12 — a filter answers for these 2 only"), output);
        Assert.DoesNotContain("· partial", output);
    }

    [Fact]
    public void A_failure_outside_a_partial_run_is_counted_and_the_run_that_has_it_is_one_command_away()
    {
        // Scenario 7 failed in the full run and has not run since: it is in no filtered re-run's roster.
        var report = TheIssue();

        var (output, error, exit) = Query("history", report, "--failing");

        Assert.True(exit == 0, error);
        Assert.True(output.Contains("also: 1 scenario outside this partial run was failing when it last ran (gh:3:1)"), output);
        Assert.Contains("next: history --run gh:3:1", output);

        // And the pointer is not a second dead end. The plan's first draft printed `--run last-failed
        // --failing`: scenario 7 BROKE in gh:3:1, so `--failing` - failing now and before - is empty there too.
        var followed = Query("history", report, "--run", "gh:3:1");
        Assert.True(followed.Exit == 0, followed.Error);
        Assert.Contains($"sid:{Id(7)}  Checkout › Scenario 7", followed.Output);
        Assert.Contains("broke", followed.Output);
        var asFirstDrafted = Query("history", report, "--run", "last-failed", "--failing");
        Assert.DoesNotContain($"sid:{Id(7)}  Checkout", asFirstDrafted.Output);
    }

    [Fact]
    public void A_run_in_which_something_broke_is_never_told_that_nothing_is_failing()
    {
        // `failing` is a verdict - failing now and in the previous run - so a scenario that broke in this run
        // is not in the --failing answer. "no scenario is failing in this run" would then be false.
        Record(1, Full, Green(12));
        Record(2, Full, Green(12));
        var report = WriteReport(Full, With(Green(12), 3, 'F'));

        var (output, error, exit) = Query("history", report, "--failing");

        Assert.True(exit == 0, error);
        Assert.True(!output.Contains("no scenario is failing in this run"), output);
        Assert.Contains("no scenario has been failing since an earlier run", output);
        Assert.Contains("1 scenario failed in this run under another verdict: s3 (broke)", output);
    }

    [Fact]
    public void Count_stays_one_bare_number_and_the_hint_goes_to_stderr()
    {
        var report = TheIssue();

        var (output, error, exit) = Query("history", report, "--failing", "--count");

        Assert.True(exit == 0, error);
        Assert.Equal("0", output.Trim());
        Assert.Contains("failed earlier in the window", error);
    }

    [Fact]
    public void Only_recent_failures_are_named_and_at_most_five_of_them()
    {
        // Run 1: scenario 0 fails - six runs back from the current one. Run 2: scenarios 1-7 fail - five back.
        Record(1, Full, With(Green(12), 0, 'F'));
        Record(2, Full, "PFFFFFFFPPPP");
        for (var run = 3; run <= 6; run++)
            Record(run, Full, Green(12));
        var report = WriteReport(Full, Green(12));

        var (output, error, exit) = Query("history", report, "--failing");

        Assert.True(exit == 0, error);
        Assert.True(output.Contains("7 scenarios here failed earlier in the window: s1 (5 runs ago, gh:2:1), s2, s3, s4, s5 … and 2 more"), output);
        Assert.Contains("and 1 older, the newest 6 runs ago in gh:1:1", output);
        Assert.DoesNotContain("s0 (", output);
    }

    [Fact]
    public void A_failure_too_old_to_list_still_comes_with_an_address()
    {
        // Stable now, so the run view lists it nowhere: a hint that only counted it would be a dead end.
        Record(1, Full, With(Green(12), 3, 'F'));
        for (var run = 2; run <= 7; run++)
            Record(run, Full, Green(12));
        var report = WriteReport(Full, Green(12));

        var failing = Query("history", report, "--failing");
        var flaky = Query("history", report, "--flaky");
        var unfiltered = Query("history", report);

        Assert.True(failing.Exit == 0, failing.Error);
        Assert.True(failing.Output.Contains("1 scenario here failed more than 5 runs ago, the newest s3 (7 runs ago, gh:1:1) · next: history s3"), failing.Output);
        Assert.True(flaky.Output.Contains("the last failure more than 5 runs back: s3 — 1 flip, one failing episode (7 runs ago) · next: history s3"), flaky.Output);
        // What the plan's first draft pointed at: the unfiltered view, which does not have it.
        Assert.DoesNotContain("Scenario 3", unfiltered.Output);
    }

    [Fact]
    public void Json_carries_the_near_misses_and_the_size_of_the_last_full_run()
    {
        var report = TheIssue();

        var (output, error, exit) = Query("history", report, "--flaky", "--json");

        Assert.True(exit == 0, error);
        var history = JsonDocument.Parse(output).RootElement.GetProperty("history");
        Assert.Equal(12, history.GetProperty("previousFullCount").GetInt32());
        var miss = Assert.Single(history.GetProperty("nearMisses").EnumerateArray());
        Assert.Equal("s0", miss.GetProperty("address").GetString());
        Assert.Equal(Id(3), miss.GetProperty("stableId").GetString());
        Assert.Equal("flips-not-flaky", miss.GetProperty("kind").GetString());
        Assert.Equal("one-episode", miss.GetProperty("reason").GetString());
        var outside = Assert.Single(history.GetProperty("failingOutside").EnumerateArray());
        Assert.Equal(Id(7), outside.GetProperty("stableId").GetString());
    }

    [Fact]
    public void The_scenario_view_counts_failing_episodes_beside_the_flips()
    {
        var report = TheIssue();

        var (output, error, exit) = Query("history", report, "s0");

        Assert.True(exit == 0, error);
        Assert.True(output.Contains("flips 2 · failing episodes 1 ·"), output);
    }
}
