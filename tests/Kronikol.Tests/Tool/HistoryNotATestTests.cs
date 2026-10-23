using Kronikol.History;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The history read from a report file keeps a scenario that is not a test as <c>N</c> (plans/RUN_WINDOW_PLAN.md R1).
/// Without the report's <c>History.run.json</c> beside it, <c>kronikol history import</c>, <c>history gate</c> and
/// <c>query history</c> rebuild the run from the data file, which did not carry the flag: an ingest's fold scenario was
/// imported into the ledger as a pass, and the gate's pass rate counted it as one.
/// </summary>
public class HistoryNotATestTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-history-not-a-test").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private const string PayId = "aaaabbbbccccdddd";
    private const string RefundId = "eeeeffff00001111";
    private const string FoldId = "1234123412341234";

    private string Ledger => Path.Combine(_dir, ".kronikol", "history.jsonl");

    private static (string Out, string Err, int Exit) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = HistoryCommand.Run(args, output, error, _ => null);
        return (output.ToString(), error.ToString(), exit);
    }

    /// <summary>
    /// A report of two tests, the first failed unless said otherwise, and an ingest's fold scenario, with no fragment
    /// beside it. Features by name: Checkout (s0, s1), then Ingested (s2).
    /// </summary>
    private string WriteReport(string name, string pay = "Failed", string fold = "Passed")
    {
        var directory = Path.Combine(_dir, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "TestRunReport.json"), $$"""
            {
              "kronikolVersion": "5.2.5", "formatVersion": 1, "suite": "Suite",
              "startTime": "2026-10-21T10:00:00Z", "endTime": "2026-10-21T10:05:00Z",
              "ciMetadata": { "provider": "GitHubActions", "buildNumber": "42", "branch": "main", "commitSha": "abc1234", "pipelineUrl": null, "repository": "o/r", "runId": "500", "runAttempt": "1" },
              "features": [
                { "name": "Checkout", "tags": [], "scenarios": [
                  { "id": "t0", "stableId": "{{PayId}}", "name": "Pay by card", "result": "{{pay}}", "durationSeconds": 0.1, "errorMessage": "boom", "isHappyPath": false, "tags": [], "steps": [], "httpInteractions": [] },
                  { "id": "t1", "stableId": "{{RefundId}}", "name": "Refund an order", "result": "Passed", "durationSeconds": 0.05, "isHappyPath": false, "tags": [], "steps": [], "httpInteractions": [] } ] },
                { "name": "Ingested", "tags": [], "scenarios": [
                  { "id": "session", "stableId": "{{FoldId}}", "name": "Traffic outside any test", "result": "{{fold}}", "durationSeconds": 780, "isHappyPath": false, "notATest": true, "tags": [], "steps": [], "httpInteractions": [] } ] }
              ]
            }
            """);
        return directory;
    }

    [Fact]
    public void Import_records_a_scenario_that_is_not_a_test_as_N()
    {
        var (output, error, exit) = Run("import", WriteReport("import"), "--history", Ledger);

        Assert.True(exit == 0, error);
        Assert.Contains("imported   gh:500:1", output);
        var ledger = HistoryLedgerReader.Read(Ledger, 50).Ledger!;
        var run = Assert.Single(ledger.Runs("Suite"));
        Assert.Equal("FPN", run.Results);
    }

    [Fact]
    public void Import_and_show_count_the_tests_and_name_what_is_not_one()
    {
        // 5.3.0 read "3 scenarios" and "3 scenarios: 1 passed, 1 failed, 1 other" for two tests and the fold.
        var (imported, importError, importExit) = Run("import", WriteReport("labels"), "--history", Ledger);
        var (shown, showError, showExit) = Run("show", "--history", Ledger);

        Assert.True(importExit == 0, importError);
        Assert.Contains("imported   gh:500:1  Suite  2 scenarios, 1 not a test  ", imported);
        Assert.True(showExit == 0, showError);
        Assert.Contains(" · 2 scenarios: 1 passed, 1 failed, 1 not a test", shown);
    }

    [Fact]
    public void A_run_without_the_fold_the_runs_before_it_held_is_imported_as_full()
    {
        // The fold comes and goes with the traffic. Counted as a missing scenario, it made this run of the same two tests
        // partial (1 of 3 over 0.10): nothing absent was reported, and its times were no baseline.
        Seed(2);
        var directory = WriteReport("nofold", pay: "Passed");
        var path = Path.Combine(directory, "TestRunReport.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(ReportHtml.Read(path))!;
        json["features"]!.AsArray().RemoveAt(1);
        File.WriteAllText(path, json.ToJsonString());

        var (_, error, exit) = Run("import", directory, "--history", Ledger);

        Assert.True(exit == 0, error);
        var run = HistoryLedgerReader.Read(Ledger, 50).Ledger!.Runs("Suite").Last();
        Assert.Equal("PP", run.Results);
        Assert.False(run.Partial);
    }

    /// <summary>Earlier runs of the same three scenarios, every test passing and the fold not a test.</summary>
    private void Seed(int runs)
    {
        var roster = HistoryRoster.Create("Suite",
        [
            new HistoryRosterEntry(PayId, "Pay by card", "Checkout", null),
            new HistoryRosterEntry(RefundId, "Refund an order", "Checkout", null),
            new HistoryRosterEntry(FoldId, "Traffic outside any test", "Ingested", null),
        ]);
        for (var i = 0; i < runs; i++)
            HistoryLedgerWriter.Append(Ledger, roster, new HistoryRun
            {
                Id = $"gh:{i + 1}:1", Suite = "Suite", Partial = false, At = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i),
                Branch = "main", Commit = $"c{i:D6}", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
                Results = "PPN", Attempts = "---", Durations = [100, 50, 780000], Errors = [null, null, null],
                ErrorText = new Dictionary<string, string>()
            }, "5.2.5");
    }

    [Fact]
    public void The_gates_pass_rate_is_the_tests()
    {
        Seed(3);
        // One of the two tests failed: 0.50. Counted with the fold as a pass, it read 0.67 and passed a 0.6 minimum.
        var (output, _, exit) = Run("gate", Path.Combine(WriteReport("gate"), "TestRunReport.json"), "--max-new-failures", "1", "--min-pass-rate", "0.6", "--history", Ledger);

        Assert.Contains("pass rate 0.50 (min 0.60)", output);
        Assert.Equal(1, exit);
    }

    [Fact]
    public void A_fold_that_failed_is_no_failure_of_the_gate()
    {
        Seed(3);
        // Both tests passed: 1.00. Counted with the fold as a failure, it read 0.67 and failed a 0.9 minimum.
        var (output, error, exit) = Run("gate", Path.Combine(WriteReport("gate-fold-failed", pay: "Passed", fold: "Failed"), "TestRunReport.json"), "--max-new-failures", "0", "--min-pass-rate", "0.9", "--history", Ledger);

        Assert.Contains("pass rate 1.00 (min 0.90)", output);
        Assert.True(exit == 0, output + error);
    }
}
