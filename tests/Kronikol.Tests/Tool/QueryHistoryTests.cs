using System.Text.Json;
using Kronikol.History;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol query history</c> (plans/CROSS_RUN_HISTORY_PLAN.md §6.4): a report read against the ledger
/// as it stands now — so a downloaded artifact answers "has this been flaky?" without the run that wrote
/// it — plus the <c>history:</c> line <c>failures</c> gains when a ledger is there to be read.
/// </summary>
public class QueryHistoryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-query-history").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private const string PayId = "aaaabbbbccccdddd";
    private const string RefundId = "eeeeffff00001111";

    private string Ledger => Path.Combine(_dir, ".kronikol", "history.jsonl");

    /// <summary>Runs from the temp root, never from the test's own directory - which sits inside this repository, whose ledger the walk would otherwise find.</summary>
    private (string Output, string Error, int Exit) Query(Func<string, string?>? env, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error, env ?? (_ => null), workingDirectory: _dir);
        return (output.ToString(), error.ToString(), exit);
    }

    private static HistoryRoster Roster() =>
        HistoryRoster.Create("Suite", [new HistoryRosterEntry(PayId, "Pay by card", "Checkout", null), new HistoryRosterEntry(RefundId, "Refund an order", "Checkout", null)]);

    private void Seed(params string[] results)
    {
        var roster = Roster();
        for (var i = 0; i < results.Length; i++)
        {
            var run = new HistoryRun
            {
                Id = $"gh:{i + 1}:1", Suite = "Suite", Partial = false, At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i),
                Branch = "main", Commit = $"c{i:D6}", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
                Results = results[i], Attempts = "--", Durations = [100, 50], Calls = null, ShapeSet = null, ShapeOrdered = null,
                Errors = results[i].Select(r => r == 'F' ? "e1" : null).ToArray(),
                ErrorText = results[i].Contains('F') ? new Dictionary<string, string> { ["e1"] = "Expected 200 but got 500" } : new Dictionary<string, string>(),
                Deps = ["Test>orders"]
            };
            HistoryLedgerWriter.Append(Ledger, roster, run, "3.9.0");
        }
    }

    /// <summary>A current report in <c>reports/</c> under the temp root, so the ledger above it is found by the walk.</summary>
    private string WriteReport(string pay, string refund = "Passed", string runId = "99", string branch = "main")
    {
        var directory = Path.Combine(_dir, "reports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "TestRunReport.json");
        File.WriteAllText(path, $$"""
            {
              "kronikolVersion": "3.9.0",
              "formatVersion": 1,
              "suite": "Suite",
              "startTime": "2026-09-12T10:00:00Z",
              "endTime": "2026-09-12T10:05:00Z",
              "ciMetadata": { "provider": "GitHubActions", "buildNumber": "42", "branch": "{{branch}}", "commitSha": "abc1234", "pipelineUrl": null, "repository": "o/r", "runId": "{{runId}}", "runAttempt": "1" },
              "features": [
                {
                  "name": "Checkout",
                  "labels": [],
                  "scenarios": [
                    { "id": "t0", "stableId": "{{PayId}}", "name": "Pay by card", "result": "{{pay}}", "durationSeconds": 0.1, "errorMessage": {{(pay == "Failed" ? "\"Expected 200 but got 500\"" : "null")}}, "labels": [], "categories": [], "steps": [], "httpInteractions": [] },
                    { "id": "t1", "stableId": "{{RefundId}}", "name": "Refund an order", "result": "{{refund}}", "durationSeconds": 0.05, "labels": [], "categories": [], "steps": [], "httpInteractions": [] }
                  ]
                }
              ]
            }
            """);
        return path;
    }

    [Fact]
    public void The_run_view_leads_with_the_summary_and_lists_the_scenarios_with_a_verdict()
    {
        Seed("PP", "PP", "PP", "PP", "PP");
        var report = WriteReport(pay: "Failed");

        var (output, error, exit) = Query(null, "history", report);

        Assert.True(exit == 0, error);
        Assert.Contains("history: 1 broke", output);
        Assert.Contains("against 5 earlier runs on main", output);
        Assert.Contains("s0  Checkout › Pay by card", output);
        Assert.Contains("broke", output);
        Assert.Contains("PPPPPF", output);
        Assert.Contains("passed in gh:5:1", output);
        Assert.DoesNotContain("s1  Checkout › Refund", output);
        Assert.Contains("1 scenario(s) with a verdict", output);
    }

    [Fact]
    public void The_ledger_above_the_report_is_found_without_being_named()
    {
        Seed("PP");
        var report = WriteReport(pay: "Passed");

        var (output, error, exit) = Query(null, "history", report);

        Assert.True(exit == 0, error);
        Assert.Contains(Ledger, output);
    }

    [Fact]
    public void Filters_select_verdicts_and_the_count_form_counts_them()
    {
        Seed("PP", "FP", "PP", "FP", "PP", "FP", "PP");
        var report = WriteReport(pay: "Failed");

        var flaky = Query(null, "history", report, "--flaky");
        var regressed = Query(null, "history", report, "--regressed");
        var newOnes = Query(null, "history", report, "--new");
        var count = Query(null, "history", report, "--flaky", "--count");

        Assert.True(flaky.Exit == 0, flaky.Error);
        Assert.Contains("s0", flaky.Output);
        Assert.Contains("flaky", flaky.Output);
        // Flaky leads; broke is still there as the secondary verdict, so --regressed finds it too.
        Assert.Contains("s0", regressed.Output);
        Assert.Contains("no scenario with a verdict (new)", newOnes.Output);
        Assert.Equal("1", count.Output.Trim());
    }

    [Fact]
    public void One_scenario_is_answered_in_full_with_its_runs()
    {
        Seed("PP", "FP", "FP");
        var report = WriteReport(pay: "Failed");

        var (output, error, exit) = Query(null, "history", report, "s0");

        Assert.True(exit == 0, error);
        Assert.Contains("verdict: failing", output);
        Assert.Contains("failing since: gh:2:1", output);
        Assert.Contains("runs seen: 3", output);
        Assert.Contains("flip rate", output);
        Assert.Contains("gh:1:1", output);
        Assert.Contains("gh:3:1", output);
        Assert.Contains("Expected 200 but got 500", output);

        var byId = Query(null, "history", report, "sid:" + PayId);
        Assert.True(byId.Exit == 0, byId.Error);
        Assert.Contains("verdict: failing", byId.Output);
    }

    [Fact]
    public void Json_carries_the_rows_and_the_run_level_member()
    {
        Seed("PP", "PP");
        var report = WriteReport(pay: "Failed");

        var (output, error, exit) = Query(null, "history", report, "--json");

        Assert.True(exit == 0, error);
        var envelope = JsonDocument.Parse(output).RootElement;
        var item = Assert.Single(envelope.GetProperty("items").EnumerateArray());
        Assert.Equal("broke", item.GetProperty("primary").GetString());
        Assert.Equal("s0", item.GetProperty("address").GetString());
        Assert.Equal("PPF", item.GetProperty("series").GetString());
        var history = envelope.GetProperty("history");
        Assert.Equal("main", history.GetProperty("stream").GetString());
        Assert.Equal(2, history.GetProperty("runsRecorded").GetInt32());
        Assert.Equal(1, history.GetProperty("counts").GetProperty("broke").GetInt32());
        Assert.False(history.GetProperty("behaviourVerdicts").GetBoolean());
    }

    [Fact]
    public void A_compare_branch_adds_a_second_reading()
    {
        Seed("PP", "PP");
        var report = WriteReport(pay: "Failed");
        var roster = Roster();
        HistoryLedgerWriter.Append(Ledger, roster, new HistoryRun
        {
            Id = "gh:50:1", Suite = "Suite", Partial = false, At = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero), Branch = "feature/x", Commit = "fff",
            Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash, Results = "FP", Attempts = "--", Errors = ["e1", null],
            ErrorText = new Dictionary<string, string> { ["e1"] = "boom" }
        }, "3.9.0");

        var (output, error, exit) = Query(null, "history", report, "--branch", "feature/x", "--compare-branch", "main");

        Assert.True(exit == 0, error);
        Assert.Contains("stream feature/x", output);
        Assert.Contains("on main: 1 broke", output);
    }

    [Fact]
    public void Without_a_fragment_beside_the_report_behaviour_verdicts_are_declared_off()
    {
        Seed("PP");
        var report = WriteReport(pay: "Passed");

        var (output, _, _) = Query(null, "history", report);

        Assert.Contains("! no History.run.json beside the report", output);
    }

    [Fact]
    public void The_fragment_beside_the_report_is_the_run_line_when_it_matches()
    {
        Seed("PP");
        var report = WriteReport(pay: "Passed");
        var roster = Roster();
        var fragmentRun = new HistoryRun
        {
            Id = "gh:99:1", Suite = "Suite", Partial = null, At = new DateTimeOffset(2026, 9, 12, 10, 5, 0, TimeSpan.Zero), Branch = "main", Commit = "abc1234",
            Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash, Results = "PP", Attempts = "--",
            Durations = [100, 50], Calls = [3, 1], ShapeSet = ["11111111", "22222222"], ShapeOrdered = ["11111111", "22222222"], Errors = [null, null]
        };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(report)!, HistoryFormat.FragmentFileName), HistoryFragment.Write(roster, fragmentRun, "3.9.0"));

        var (output, error, exit) = Query(null, "history", report, "--json");

        Assert.True(exit == 0, error);
        Assert.DoesNotContain("no History.run.json", output);
        Assert.True(JsonDocument.Parse(output).RootElement.GetProperty("history").GetProperty("behaviourVerdicts").GetBoolean());
    }

    [Fact]
    public void No_ledger_anywhere_is_a_usage_error_that_names_every_way_to_get_one()
    {
        var report = WriteReport(pay: "Failed");

        var (_, error, exit) = Query(null, "history", report);

        Assert.Equal(2, exit);
        Assert.Contains("--history", error);
        Assert.Contains("KRONIKOL_HISTORY", error);
        Assert.Contains("kronikol history init", error);
    }

    [Fact]
    public void History_off_in_the_environment_is_refused_unless_a_ledger_is_named()
    {
        Seed("PP");
        var report = WriteReport(pay: "Failed");

        var refused = Query(name => name == "KRONIKOL_HISTORY" ? "off" : null, "history", report);
        var named = Query(name => name == "KRONIKOL_HISTORY" ? "off" : null, "history", report, "--history", Ledger);

        Assert.Equal(2, refused.Exit);
        Assert.Contains("--history", refused.Error);
        Assert.Equal(0, named.Exit);
    }

    [Fact]
    public void A_report_without_stable_ids_cannot_be_read_against_history()
    {
        Seed("PP");
        var report = WriteReport(pay: "Failed").Replace("TestRunReport.json", "Old.json", StringComparison.Ordinal);
        File.WriteAllText(report, File.ReadAllText(Path.Combine(_dir, "reports", "TestRunReport.json")).Replace("\"stableId\": \"" + PayId + "\", ", "", StringComparison.Ordinal));
        File.Delete(Path.Combine(_dir, "reports", "TestRunReport.json"));

        var (_, error, exit) = Query(null, "history", report);

        Assert.Equal(2, exit);
        Assert.Contains("stableIds", error);
    }

    [Fact]
    public void Failures_gains_a_history_line_when_a_ledger_is_there_and_stays_silent_when_not()
    {
        var report = WriteReport(pay: "Failed");
        var silent = Query(null, "failures", report);
        Assert.True(silent.Exit == 0, silent.Error);
        Assert.DoesNotContain("history:", silent.Output);

        Seed("PP", "PP");
        var (output, error, exit) = Query(null, "failures", report);

        Assert.True(exit == 0, error);
        Assert.Contains("history: broke — passed in gh:2:1", output);
        Assert.Contains("PPF", output);
    }

    [Fact]
    public void The_verb_is_described_and_its_flags_are_refused_elsewhere()
    {
        var describe = Query(null, "--describe");
        Assert.Contains("\"history\"", describe.Output);

        Seed("PP");
        var report = WriteReport(pay: "Failed");
        var refused = Query(null, "failures", report, "--flaky");
        Assert.Equal(2, refused.Exit);
        Assert.Contains("--flaky", refused.Error);
    }

    [Fact]
    public void A_pull_request_build_reads_against_the_branch_it_targets()
    {
        // On a pull request build the run read against GITHUB_BASE_REF; queried in the same job, the report
        // reads the same way without --branch, which still names any other stream.
        Seed("PP", "PP", "PP");
        var report = WriteReport(pay: "Failed", branch: "42/merge");
        string? PullRequest(string key) => key switch { "GITHUB_ACTIONS" => "true", "GITHUB_BASE_REF" => "main", _ => null };

        var pullRequest = Query(PullRequest, "history", report);

        Assert.True(pullRequest.Exit == 0, pullRequest.Error);
        Assert.Contains("stream main", pullRequest.Output);
        Assert.Contains("1 broke", pullRequest.Output);
        Assert.Contains("stream 42/merge", Query(null, "history", report).Output);
        Assert.Contains("stream feature/x", Query(PullRequest, "history", report, "--branch", "feature/x").Output);
    }
}
