using System.Globalization;
using System.Text.Json;
using Kronikol.History;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol query history</c> with no report (plans/EVIDENCE_SURVIVES_A_RERUN_PLAN.md §3, #81): the
/// ledger holds the run a re-run overwrote, so the verb reads it from there - <c>--run</c> names the run,
/// <c>--sid</c> one scenario of it, <c>--window</c> how far back it is read against.
/// </summary>
public class QueryHistoryLedgerOnlyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-query-ledger-only").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Ledger => Path.Combine(_dir, ".kronikol", "history.jsonl");

    private (string Output, string Error, int Exit) Query(Func<string, string?>? env, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error, env ?? (_ => null), workingDirectory: _dir);
        return (output.ToString(), error.ToString(), exit);
    }

    private (string Output, string Error, int Exit) Query(params string[] args) => Query(null, args);

    private const string PayId = "aaaabbbbccccdddd";
    private const string RefundId = "eeeeffff00001111";

    private static HistoryRoster Roster(string suite = "Suite") =>
        HistoryRoster.Create(suite, [new HistoryRosterEntry(PayId, "Pay by card", "Checkout", null), new HistoryRosterEntry(RefundId, "Refund an order", "Checkout", null)]);

    private void Record(string id, string results, int hour, string suite = "Suite", string? branch = "main")
    {
        var roster = Roster(suite);
        HistoryLedgerWriter.Append(Ledger, roster, new HistoryRun
        {
            Id = id, Suite = suite, Partial = false, At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(hour),
            Branch = branch, Commit = $"c{hour:D6}", Provider = branch is null ? null : "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
            Results = results, Attempts = "--", Durations = [100, 50], Calls = null, ShapeSet = null, ShapeOrdered = null,
            Errors = results.Select(r => r == 'F' ? "e1" : null).ToArray(),
            ErrorText = results.Contains('F') ? new Dictionary<string, string> { ["e1"] = "Expected 200 but got 500" } : new Dictionary<string, string>()
        }, "3.9.0");
    }

    private void Seed(params string[] results)
    {
        for (var i = 0; i < results.Length; i++)
            Record($"gh:{(i + 1).ToString(CultureInfo.InvariantCulture)}:1", results[i], i);
    }

    [Fact]
    public void A_pointer_printed_for_a_named_run_names_that_run_so_following_it_stays_in_it()
    {
        // Found by driving the CLI over a real ledger: `next: history --sid <id>` under `--run gh:3:1`
        // read the NEWEST run when followed - a different run, and one the scenario may not be in.
        Seed("PP", "FP", "PP", "PP");

        var (output, error, exit) = Query("history", "--run", "gh:3:1", "--failing");

        Assert.True(exit == 0, error);
        Assert.True(output.Contains($"next: history --run gh:3:1 --sid {PayId}"), output);
        var followed = Query("history", "--run", "gh:3:1", "--sid", PayId);
        Assert.True(followed.Exit == 0, followed.Error);
        Assert.Contains("fixed", followed.Output);
    }

    [Fact]
    public void With_no_report_the_newest_run_of_the_ledger_is_read_and_rows_are_addressed_by_stable_id()
    {
        Seed("PP", "PP", "FP");

        var (output, error, exit) = Query("history");

        Assert.True(exit == 0, error);
        Assert.True(output.Contains("history: 1 broke"), output);
        Assert.Contains("run gh:3:1", output);
        Assert.Contains("ledger only — no report read", output);
        Assert.Contains($"sid:{PayId}  Checkout › Pay by card", output);
        Assert.Contains("PPF", output);
    }

    [Fact]
    public void A_run_named_from_the_middle_of_the_ledger_is_read_as_it_stood_then()
    {
        // The F4 test, through the verb: gh:3:1 failed AFTER gh:2:1, so gh:2:1 knows nothing of it.
        Seed("PP", "PP", "FP", "PP");

        var (output, error, exit) = Query("history", "--run", "gh:2:1", "--sid", PayId);

        Assert.True(exit == 0, error);
        Assert.True(output.Contains("verdict: stable"), output);
        Assert.Contains("1 earlier run(s) in the window", output);
        Assert.DoesNotContain("gh:3:1", output);
        Assert.DoesNotContain("gh:4:1", output);
    }

    [Fact]
    public void Last_failed_is_the_newest_run_holding_a_failure_and_a_green_ledger_has_none()
    {
        Seed("PP", "FP", "PP", "PF", "PP");

        var (output, error, exit) = Query("history", "--run", "last-failed", "--failing");
        Assert.True(exit == 0, error);
        Assert.True(output.Contains("run gh:4:1"), output);

        File.Delete(Ledger);
        Seed("PP", "PP");
        var green = Query("history", "--run", "last-failed");
        Assert.Equal(2, green.Exit);
        Assert.Contains("no run of Suite in the ledger has a failure", green.Error);
    }

    [Fact]
    public void A_resume_pointer_names_the_run_an_alias_resolved_to()
    {
        // `last-failed` is a different run the moment another failure is recorded: page two must not be of it.
        Seed("PP", "FF", "PP");

        var text = Query("history", "--run", "last-failed", "--limit", "1");
        var json = Query("history", "--run", "last-failed", "--limit", "1", "--json");

        Assert.True(text.Exit == 0, text.Error);
        Assert.True(text.Output.Contains("next: --run gh:2:1 --offset 1"), text.Output);
        var next = JsonDocument.Parse(json.Output).RootElement.GetProperty("next").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(["query", "history", "--run", "gh:2:1", "--json", "--limit", "1", "--offset", "1"], next);
    }

    [Fact]
    public void Previous_is_the_run_before_the_newest()
    {
        Seed("PP", "FP", "PP");

        var (output, error, exit) = Query("history", "--run", "previous");

        Assert.True(exit == 0, error);
        Assert.True(output.Contains("run gh:2:1"), output);
    }

    [Fact]
    public void A_unique_substring_names_a_run_and_an_ambiguous_one_is_refused_with_the_candidates()
    {
        Record("local:20260918T101611Z:ab12cd34", "FP", 1, branch: null);
        Record("local:20260918T102009Z:ab12cd34", "PP", 2, branch: null);

        var unique = Query("history", "--run", "T101611Z");
        Assert.True(unique.Exit == 0, unique.Error);
        Assert.Contains("run local:20260918T101611Z:ab12cd34", unique.Output);

        var ambiguous = Query("history", "--run", "20260918");
        Assert.Equal(2, ambiguous.Exit);
        Assert.Contains("local:20260918T101611Z:ab12cd34", ambiguous.Error);
        Assert.Contains("local:20260918T102009Z:ab12cd34", ambiguous.Error);
        Assert.Contains("1 failed", ambiguous.Error);

        var unknown = Query("history", "--run", "nothing-like-this");
        Assert.Equal(2, unknown.Exit);
        Assert.Contains("No run matching nothing-like-this", unknown.Error);
        Assert.Contains("local:20260918T102009Z:ab12cd34", unknown.Error);
    }

    [Fact]
    public void A_run_further_back_than_the_reader_s_window_is_still_found_by_name()
    {
        // F5: the reader keeps the last 50 runs of a suite. gh:3:1 is 57 lines back.
        Seed(Enumerable.Repeat("PP", 60).ToArray());

        var (output, error, exit) = Query("history", "--run", "gh:3:1");

        Assert.True(exit == 0, error);
        Assert.True(output.Contains("run gh:3:1"), output);
        Assert.Contains("2 runs recorded", output);
    }

    [Fact]
    public void Sid_prints_the_block_a_report_anchored_reading_prints()
    {
        Seed("PP", "FP", "FP");

        var (output, error, exit) = Query("history", "--sid", PayId);

        Assert.True(exit == 0, error);
        Assert.True(output.Contains($"sid:{PayId}  Checkout › Pay by card"), output);
        Assert.Contains("verdict: failing", output);
        Assert.Contains("failing since: gh:2:1", output);
        Assert.Contains("Expected 200 but got 500", output);

        var missing = Query("history", "--sid", "0000000000000000");
        Assert.Equal(2, missing.Exit);
        Assert.Contains("is not in run gh:3:1", missing.Error);
    }

    // §6.3's decision, as built: a pass on retry keeps what the earlier attempt died of - and the row says
    // whose error it is, because `P … <an error>` on one line reads as a pass that failed.
    [Fact]
    public void A_pass_on_retry_shows_the_earlier_attempt_s_error_as_the_earlier_attempt_s()
    {
        Seed("PP", "PP");
        var roster = Roster();
        HistoryLedgerWriter.Append(Ledger, roster, new HistoryRun
        {
            Id = "gh:3:1", Suite = "Suite", Partial = false, At = new DateTimeOffset(2026, 9, 1, 5, 0, 0, TimeSpan.Zero), Branch = "main", Commit = "c000005",
            Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash, Results = "PP", Attempts = "2-", Durations = [100, 50],
            Errors = ["e1", null], ErrorText = new Dictionary<string, string> { ["e1"] = "Expected 200 but got 500" }
        }, "3.9.0");

        var (output, error, exit) = Query("history", "--sid", PayId);

        Assert.True(exit == 0, error);
        // The row ends with the attempt; the error is under it (#82), and says whose it is.
        var lines = output.ReplaceLineEndings("\n").Split('\n');
        var row = Array.FindIndex(lines, line => line.EndsWith("attempt 2", StringComparison.Ordinal));
        Assert.True(row >= 0, output);
        Assert.Equal("       an earlier attempt failed: Expected 200 but got 500", lines[row + 1]);
        Assert.Contains("passed on retry 2 in this run", output);
    }

    [Fact]
    public void Several_suites_need_naming_and_history_off_is_refused_as_before()
    {
        Record("gh:1:1", "PP", 1, suite: "Api");
        Record("gh:1:1", "FP", 2, suite: "Web");

        var both = Query("history");
        Assert.Equal(2, both.Exit);
        Assert.Contains("Api", both.Error);
        Assert.Contains("Web", both.Error);
        Assert.Contains("--suite", both.Error);

        var named = Query("history", "--suite", "Web");
        Assert.True(named.Exit == 0, named.Error);
        Assert.Contains("run gh:1:1", named.Output);

        var off = Query(name => name == "KRONIKOL_HISTORY" ? "off" : null, "history");
        Assert.Equal(2, off.Exit);
        Assert.Contains("--history", off.Error);
    }

    [Fact]
    public void Json_says_there_was_no_report_and_that_the_ledger_was_the_source()
    {
        Seed("PP", "FP");

        var (output, error, exit) = Query("history", "--json");

        Assert.True(exit == 0, error);
        var envelope = JsonDocument.Parse(output).RootElement;
        Assert.Equal(JsonValueKind.Null, envelope.GetProperty("report").ValueKind);
        Assert.Equal("ledger", envelope.GetProperty("history").GetProperty("source").GetString());
        var item = Assert.Single(envelope.GetProperty("items").EnumerateArray());
        Assert.Equal("sid:" + PayId, item.GetProperty("address").GetString());
        Assert.Equal("Failed", item.GetProperty("result").GetString());
    }

    [Fact]
    public void Every_other_verb_still_needs_a_report_and_no_ledger_at_all_is_a_usage_error()
    {
        Seed("PP");

        var failures = Query("failures");
        Assert.Equal(2, failures.Exit);
        Assert.Contains("No report given", failures.Error);

        File.Delete(Ledger);
        Directory.Delete(Path.GetDirectoryName(Ledger)!);
        var nothing = Query("history");
        Assert.Equal(2, nothing.Exit);
        Assert.Contains("No report given, and no history ledger", nothing.Error);
    }

    [Fact]
    public void Window_is_how_far_back_the_run_is_read_against()
    {
        Seed("FP", "PP", "PP", "PP", "PP");

        var whole = Query("history", "--sid", PayId);
        var narrow = Query("history", "--sid", PayId, "--window", "2");

        Assert.True(whole.Exit == 0, whole.Error);
        Assert.Contains("4 earlier run(s) in the window", whole.Output);
        Assert.True(narrow.Exit == 0, narrow.Error);
        Assert.True(narrow.Output.Contains("2 earlier run(s) in the window"), narrow.Output);
        Assert.DoesNotContain("gh:1:1", narrow.Output);
        Assert.Equal(2, Query("history", "--window", "0").Exit);
    }

    [Fact]
    public void With_a_report_a_run_flag_reads_another_run_of_its_ledger()
    {
        // The report on disk is the green re-run; the run being asked about is the one it overwrote.
        Seed("PP", "FP");
        var directory = Path.Combine(_dir, "reports");
        Directory.CreateDirectory(directory);
        var report = Path.Combine(directory, "TestRunReport.json");
        File.WriteAllText(report, $$"""
            {
              "kronikolVersion": "3.9.0", "formatVersion": 1, "suite": "Suite",
              "startTime": "2026-09-12T10:00:00Z", "endTime": "2026-09-12T10:05:00Z",
              "ciMetadata": { "provider": "GitHubActions", "buildNumber": "42", "branch": "main", "commitSha": "abc1234", "pipelineUrl": null, "repository": "o/r", "runId": "99", "runAttempt": "1" },
              "features": [ { "name": "Checkout", "labels": [], "scenarios": [
                { "id": "t0", "stableId": "{{PayId}}", "name": "Pay by card", "result": "Passed", "durationSeconds": 0.1, "labels": [], "categories": [], "steps": [], "httpInteractions": [] },
                { "id": "t1", "stableId": "{{RefundId}}", "name": "Refund an order", "result": "Passed", "durationSeconds": 0.05, "labels": [], "categories": [], "steps": [], "httpInteractions": [] } ] } ]
            }
            """);

        var (output, error, exit) = Query("history", report, "--run", "last-failed");

        Assert.True(exit == 0, error);
        Assert.True(output.Contains("run gh:2:1"), output);
        Assert.Contains("1 broke", output);
        Assert.Contains("describes gh:99:1", output);

        // Its own run, named, is the ordinary reading.
        var own = Query("history", report, "--run", "gh:99:1");
        Assert.True(own.Exit == 0, own.Error);
        Assert.Contains("s0", own.Output);
        Assert.DoesNotContain("ledger only", own.Output);
    }

    // §3.4, F1: nothing pinned the exit code #81 reported as 0.
    [Fact]
    public void A_missing_report_is_a_usage_error_with_exit_2()
    {
        var (output, error, exit) = Query("summary");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("No report given", error);
    }
}
