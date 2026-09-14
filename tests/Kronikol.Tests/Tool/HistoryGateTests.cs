using Kronikol.History;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol history gate</c> (plans/CROSS_RUN_HISTORY_PLAN.md §7.3): the history-aware CI gate. A
/// plain "any failure fails the build" gate cannot tell a regression from the flaky test that has been
/// flipping for a month, and a team that cannot tell them apart learns to ignore red. The gate trips on
/// what the ledger says is new, and reads the rest out loud without tripping.
/// </summary>
public class HistoryGateTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-gate").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private const string PayId = "aaaabbbbccccdddd";
    private const string RefundId = "eeeeffff00001111";

    private string Ledger => Path.Combine(_dir, ".kronikol", "history.jsonl");

    private static (string Out, string Err, int Exit) Run(params string[] args) => Run(_ => null, args);

    private static (string Out, string Err, int Exit) Run(Func<string, string?> env, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = HistoryCommand.Run(args, output, error, env);
        return (output.ToString(), error.ToString(), exit);
    }

    private static HistoryRoster Roster() =>
        HistoryRoster.Create("Suite", [new HistoryRosterEntry(PayId, "Pay by card", "Checkout", null), new HistoryRosterEntry(RefundId, "Refund an order", "Checkout", null)]);

    private void Seed(params string[] results)
    {
        var roster = Roster();
        for (var i = 0; i < results.Length; i++)
            HistoryLedgerWriter.Append(Ledger, roster, new HistoryRun
            {
                Id = $"gh:{i + 1}:1", Suite = "Suite", Partial = false, At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i),
                Branch = "main", Commit = $"c{i:D6}", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
                Results = results[i], Attempts = "--", Durations = [100, 50],
                Errors = results[i].Select(r => r == 'F' ? "e1" : null).ToArray(),
                ErrorText = results[i].Contains('F') ? new Dictionary<string, string> { ["e1"] = "boom" } : new Dictionary<string, string>()
            }, "3.10.0");
    }

    private string WriteReport(string pay, string refund = "Passed", double payDuration = 0.1, string branch = "main")
    {
        var directory = Path.Combine(_dir, "reports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "TestRunReport.json");
        File.WriteAllText(path, $$"""
            {
              "kronikolVersion": "3.10.0", "formatVersion": 1, "suite": "Suite",
              "startTime": "2026-09-12T10:00:00Z", "endTime": "2026-09-12T10:05:00Z",
              "ciMetadata": { "provider": "GitHubActions", "buildNumber": "42", "branch": "{{branch}}", "commitSha": "abc1234", "pipelineUrl": null, "repository": "o/r", "runId": "99", "runAttempt": "1" },
              "features": [ { "name": "Checkout", "labels": [], "scenarios": [
                { "id": "t0", "stableId": "{{PayId}}", "name": "Pay by card", "result": "{{pay}}", "durationSeconds": {{payDuration.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "errorMessage": {{(pay == "Failed" ? "\"boom\"" : "null")}}, "labels": [], "categories": [], "steps": [], "httpInteractions": [] },
                { "id": "t1", "stableId": "{{RefundId}}", "name": "Refund an order", "result": "{{refund}}", "durationSeconds": 0.05, "labels": [], "categories": [], "steps": [], "httpInteractions": [] }
              ] } ]
            }
            """);
        return path;
    }

    [Fact]
    public void A_regression_trips_the_gate_and_is_named()
    {
        Seed("PP", "PP", "PP", "PP", "PP");
        var report = WriteReport(pay: "Failed");

        var (output, error, exit) = Run("gate", report);

        Assert.Equal(1, exit);
        Assert.Contains("new-failures: 1", output);
        Assert.Contains("Pay by card", output);
        Assert.Contains("broke", output);
        Assert.Contains("gate: FAILED", output);
        Assert.Equal("", error);
    }

    [Fact]
    public void A_flaky_failure_does_not_trip_the_default_gate_but_is_read_out()
    {
        // The whole point: the test that has been flipping for a month is not this pull request's fault.
        Seed("PP", "FP", "PP", "FP", "PP", "FP", "PP");
        var report = WriteReport(pay: "Failed");

        var (output, _, exit) = Run("gate", report);

        Assert.Equal(0, exit);
        Assert.Contains("flaky: 1", output);
        Assert.Contains("new-failures: 0", output);
        Assert.Contains("gate: passed", output);
    }

    [Fact]
    public void Fail_on_flaky_trips_on_the_flaky_one()
    {
        Seed("PP", "FP", "PP", "FP", "PP", "FP", "PP");
        var report = WriteReport(pay: "Failed");

        var (output, _, exit) = Run("gate", report, "--fail-on", "new-failures,flaky");

        Assert.Equal(1, exit);
        Assert.Contains("gate: FAILED", output);
        Assert.Contains("flaky", output);
    }

    [Fact]
    public void A_long_standing_failure_is_not_a_new_failure()
    {
        Seed("FP", "FP", "FP", "FP", "FP");
        var report = WriteReport(pay: "Failed");

        var (output, _, exit) = Run("gate", report);

        Assert.Equal(0, exit);
        Assert.Contains("new-failures: 0", output);
        Assert.Contains("always-failing", output);
    }

    [Fact]
    public void A_quarantined_failure_trips_nothing()
    {
        Seed("PP", "PP", "PP", "PP", "PP");
        var report = WriteReport(pay: "Failed");
        Run("quarantine", PayId, "--reason", "ticket 123", "--history", Ledger);

        var (output, _, exit) = Run("gate", report, "--fail-on", "new-failures,flaky,duration-regression,behaviour-change");

        Assert.Equal(0, exit);
        Assert.Contains("quarantined: 1", output);
        Assert.Contains("gate: passed", output);
    }

    [Fact]
    public void Max_new_failures_and_min_pass_rate_are_thresholds()
    {
        Seed("PP", "PP", "PP", "PP", "PP");
        var report = WriteReport(pay: "Failed");

        Assert.Equal(0, Run("gate", report, "--max-new-failures", "1").Exit);
        Assert.Equal(1, Run("gate", report, "--max-new-failures", "1", "--min-pass-rate", "0.9").Exit);
        Assert.Contains("pass rate 0.50", Run("gate", report, "--max-new-failures", "1", "--min-pass-rate", "0.9").Out);
    }

    [Fact]
    public void Below_the_minimum_runs_the_statistical_verdicts_are_advisory()
    {
        // Two runs recorded: the ledger cannot call anything flaky or slower yet, and the gate says so
        // rather than tripping on a verdict it does not have. A regression still trips.
        Seed("PP", "FP");
        var report = WriteReport(pay: "Failed");

        var (output, _, exit) = Run("gate", report, "--fail-on", "new-failures,flaky,duration-regression");

        Assert.Equal(0, exit);
        Assert.Contains("advisory", output);
        Assert.Contains("gate: passed", output);
    }

    [Fact]
    public void Min_runs_is_the_bar_the_report_used()
    {
        // A suite that reports with HistoryMinRuns = 3 has its flaky verdicts after three runs. The gate
        // given the same bar reads the same run the same way - flaky, not a cold start with a regression
        // in it - and refuses a bar that is not a positive number.
        Seed("PP", "FP", "PP");
        var report = WriteReport(pay: "Failed");

        var five = Run("gate", report, "--fail-on", "new-failures,flaky");
        var three = Run("gate", report, "--fail-on", "new-failures,flaky", "--min-runs", "3");

        Assert.Contains("advisory", five.Out);
        Assert.Contains("new-failures: 1", five.Out);
        Assert.DoesNotContain("advisory", three.Out);
        Assert.Contains("flaky: 1", three.Out);
        Assert.Contains("new-failures: 0", three.Out);
        Assert.Contains("gate: FAILED (flaky 1)", three.Out);
        Assert.Equal(1, three.Exit);
        Assert.Equal(2, Run("gate", report, "--min-runs", "0").Exit);
        Assert.Equal(2, Run("gate", report, "--min-runs", "three").Exit);
        Assert.Contains("--min-runs", Run("gate", report, "--min-runs", "three").Err);
    }

    [Fact]
    public void A_duration_regression_trips_when_asked_for()
    {
        var roster = Roster();
        for (var i = 1; i <= 8; i++)
            HistoryLedgerWriter.Append(Ledger, roster, new HistoryRun
            {
                Id = $"gh:{i}:1", Suite = "Suite", Partial = false, At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i),
                Branch = "main", Commit = "c", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
                Results = "PP", Attempts = "--", Durations = [i == 8 ? 900 : 100, 50]
            }, "3.10.0");
        var report = WriteReport(pay: "Passed", payDuration: 0.9);

        var quiet = Run("gate", report);
        var asked = Run("gate", report, "--fail-on", "duration-regression", "--slower-by", "2");

        Assert.Equal(0, quiet.Exit);
        Assert.Equal(1, asked.Exit);
        Assert.Contains("slower", asked.Out);
    }

    [Fact]
    public void No_ledger_and_bad_flags_are_usage_errors()
    {
        var report = WriteReport(pay: "Failed");
        var nowhere = Path.Combine(_dir, "nowhere", "history.jsonl");

        Assert.Equal(2, Run("gate", report, "--history", nowhere).Exit);
        Assert.Equal(2, Run("gate").Exit);
        Seed("PP");
        Assert.Equal(2, Run("gate", report, "--fail-on", "everything").Exit);
        Assert.Contains("everything", Run("gate", report, "--fail-on", "everything").Err);
        Assert.Equal(2, Run("gate", report, "--min-pass-rate", "two").Exit);
    }

    [Fact]
    public void A_pull_request_build_reads_against_the_branch_it_targets()
    {
        // The run read against GITHUB_BASE_REF without being told; the gate, in the same job, reads the
        // same way, and a push reads its own stream - where the failure is new rather than broke.
        Seed("PP", "PP", "PP");
        var report = WriteReport(pay: "Failed", branch: "42/merge");
        string? PullRequest(string key) => key switch { "GITHUB_ACTIONS" => "true", "GITHUB_BASE_REF" => "main", _ => null };

        var pullRequest = Run(PullRequest, "gate", report);
        var push = Run(key => key == "GITHUB_ACTIONS" ? "true" : null, "gate", report);

        Assert.Contains("stream main", pullRequest.Out);
        Assert.Contains("1 broke (3 runs recorded in the main stream", pullRequest.Out);
        Assert.Contains("new-failures: 1", pullRequest.Out);
        Assert.Equal(1, pullRequest.Exit);
        Assert.Contains("stream 42/merge", push.Out);
        Assert.Contains("0 runs recorded in the 42/merge stream", push.Out);
        Assert.Contains("stream release", Run(PullRequest, "gate", report, "--branch", "release").Out);
    }
}
