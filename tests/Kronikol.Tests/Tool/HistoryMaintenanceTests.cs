using System.Text.Json;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The 3.10.0 maintenance verbs (plans/CROSS_RUN_HISTORY_PLAN.md §7.3, §7.4, §7.6): quarantine, rename
/// aliases, the doctor, and imports from reports the run did not write - other Kronikol reports, CTRF
/// documents, Allure results - so a team's existing history is not thrown away on day one.
/// </summary>
public class HistoryMaintenanceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-maint").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Ledger => Path.Combine(_dir, ".kronikol", "history.jsonl");

    private static (string Out, string Err, int Exit) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = HistoryCommand.Run(args, output, error, _ => null);
        return (output.ToString(), error.ToString(), exit);
    }

    private static HistoryRoster Roster(string suite, params (string Id, string Name)[] entries) =>
        HistoryRoster.Create(suite, entries.Select(e => new HistoryRosterEntry(e.Id, e.Name, "Checkout", null)).ToArray());

    private void Seed(HistoryRoster roster, params string[] results)
    {
        for (var i = 0; i < results.Length; i++)
            HistoryLedgerWriter.Append(Ledger, roster, new HistoryRun
            {
                Id = $"gh:{i + 1}:1", Suite = roster.Suite, Partial = false, At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i),
                Branch = "main", Commit = "c", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
                Results = results[i], Attempts = new string('-', results[i].Length),
                Errors = results[i].Select(r => r == 'F' ? "e1" : null).ToArray(),
                ErrorText = results[i].Contains('F') ? new Dictionary<string, string> { ["e1"] = "boom" } : new Dictionary<string, string>()
            }, "3.10.0");
    }

    // ─── quarantine ────────────────────────────────────────────

    [Fact]
    public void Quarantine_adds_lists_and_releases_entries_beside_the_ledger()
    {
        Seed(Roster("Suite", ("aaaabbbbccccdddd", "Pay")), "P");

        var added = Run("quarantine", "sid:aaaabbbbccccdddd", "--reason", "ticket 123", "--by", "me", "--until", "2026-12-31", "--history", Ledger);
        Assert.True(added.Exit == 0, added.Err);
        Assert.Contains("quarantined aaaabbbbccccdddd", added.Out);
        var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, ".kronikol", "quarantine.json"))).RootElement;
        var entry = Assert.Single(file.GetProperty("entries").EnumerateArray());
        Assert.Equal("ticket 123", entry.GetProperty("reason").GetString());
        Assert.Equal("2026-12-31", entry.GetProperty("until").GetString());

        // Released entries leave the file: the list is what is quarantined now, not a log of what was.
        var listed = Run("quarantine", "--list", "--history", Ledger);
        var released = Run("quarantine", "aaaabbbbccccdddd", "--release", "--history", Ledger);
        var empty = Run("quarantine", "--list", "--history", Ledger);

        Assert.Contains("ticket 123", listed.Out);
        Assert.True(released.Exit == 0, released.Err);
        Assert.Contains("released", released.Out);
        Assert.Contains("nothing is quarantined", empty.Out);
    }

    [Fact]
    public void Quarantine_needs_a_reason_and_a_real_id()
    {
        Seed(Roster("Suite", ("aaaabbbbccccdddd", "Pay")), "P");

        Assert.Equal(2, Run("quarantine", "aaaabbbbccccdddd", "--history", Ledger).Exit);
        Assert.Contains("--reason", Run("quarantine", "aaaabbbbccccdddd", "--history", Ledger).Err);
        Assert.Equal(2, Run("quarantine", "not-an-id", "--reason", "x", "--history", Ledger).Exit);
        Assert.Equal(2, Run("quarantine", "aaaabbbbccccdddd", "--reason", "x", "--until", "someday", "--history", Ledger).Exit);
    }

    // ─── rename ────────────────────────────────────────────────

    [Fact]
    public void Rename_records_an_alias_that_the_analysis_follows()
    {
        var old = Roster("Suite", ("0000000000000001", "Pay by card"));
        Seed(old, "F", "F", "F");

        var (output, error, exit) = Run("rename", "0000000000000001", "1111111111111111", "--history", Ledger);

        Assert.True(exit == 0, error);
        Assert.Contains("0000000000000001 → 1111111111111111", output);
        var aliases = HistoryAliases.Load(Path.Combine(_dir, ".kronikol", "aliases.json"));
        Assert.Equal("1111111111111111", aliases.Current("0000000000000001"));

        // The renamed scenario keeps its history: three failures before, failing now = always failing.
        var renamed = Roster("Suite", ("1111111111111111", "Pay by card"));
        var ledger = HistoryLedgerReader.Read(Ledger, 50).Ledger!;
        var verdicts = HistoryAnalyzer.Analyse(ledger, renamed, new HistoryRun
        {
            Id = "gh:9:1", Suite = "Suite", Partial = false, At = DateTimeOffset.UtcNow, Branch = "main", Commit = null, Provider = null, Url = null,
            Shards = 1, RosterHash = renamed.Hash, Results = "F", Attempts = "-"
        }, new HistoryAnalysisOptions(), aliases: aliases);
        Assert.Equal(HistoryVerdictKind.AlwaysFailing, verdicts.Scenarios[0].Primary);
    }

    [Fact]
    public void Record_suggests_renames_and_accepts_them_on_request()
    {
        // The previous full run had "Pay by card" under one id; the new fragment has the same feature
        // and name under another id and no longer has the old one. That is a rename, not a deletion
        // plus an addition, and saying so is what keeps the history attached.
        var old = Roster("Suite", ("0000000000000001", "Pay by card"), ("0000000000000002", "Refund"));
        Seed(old, "PP", "PP");
        var renamed = Roster("Suite", ("1111111111111111", "Pay by card"), ("0000000000000002", "Refund"));
        var directory = Path.Combine(_dir, "run");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, HistoryFormat.FragmentFileName), HistoryFragment.Write(renamed, new HistoryRun
        {
            Id = "gh:9:1", Suite = "Suite", Partial = null, At = DateTimeOffset.UtcNow, Branch = "main", Commit = null, Provider = null, Url = null,
            Shards = 1, RosterHash = renamed.Hash, Results = "PP", Attempts = "--"
        }, "3.10.0"));

        var suggested = Run("record", directory, "--history", Ledger);
        Assert.True(suggested.Exit == 0, suggested.Err);
        Assert.Contains("1 scenario(s) look renamed", suggested.Out);
        Assert.Contains("--accept-renames", suggested.Out);
        Assert.False(File.Exists(Path.Combine(_dir, ".kronikol", "aliases.json")));

        File.Delete(Ledger);
        Seed(old, "PP", "PP");
        var accepted = Run("record", directory, "--history", Ledger, "--accept-renames");
        Assert.True(accepted.Exit == 0, accepted.Err);
        Assert.Contains("aliased 0000000000000001 → 1111111111111111", accepted.Out);
        Assert.Equal("1111111111111111", HistoryAliases.Load(Path.Combine(_dir, ".kronikol", "aliases.json")).Current("0000000000000001"));
    }

    // ─── doctor ────────────────────────────────────────────────

    [Fact]
    public void Doctor_reports_a_healthy_ledger_and_what_it_holds()
    {
        Seed(Roster("Suite", ("aaaabbbbccccdddd", "Pay")), "P", "F", "P");
        File.WriteAllText(Path.Combine(_dir, ".gitattributes"), HistoryFormat.GitAttributesLine + "\n");

        var (output, error, exit) = Run("doctor", "--history", Ledger);

        Assert.True(exit == 0, error);
        Assert.Contains("ledger", output);
        Assert.Contains("1 suite", output);
        Assert.Contains("3 runs", output);
        Assert.Contains("merge=union", output);
        Assert.Contains("ok", output);
    }

    [Fact]
    public void Doctor_names_the_missing_attribute_the_damaged_line_and_the_expired_quarantine()
    {
        Seed(Roster("Suite", ("aaaabbbbccccdddd", "Pay")), "P");
        File.AppendAllText(Ledger, "{\"t\":\"run\",\"id\":\"torn");
        var quarantine = new HistoryQuarantineList();
        quarantine.Add("aaaabbbbccccdddd", "old", "me", new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));
        quarantine.Save(Path.Combine(_dir, ".kronikol", "quarantine.json"));

        var (output, _, exit) = Run("doctor", "--history", Ledger);

        Assert.Equal(1, exit);
        Assert.Contains("merge=union", output);
        Assert.Contains("1 damaged line", output);
        Assert.Contains("expired", output);
    }

    // ─── import ────────────────────────────────────────────────

    [Fact]
    public void Import_reads_a_kronikol_report_that_has_no_fragment()
    {
        var directory = Path.Combine(_dir, "old-run");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "TestRunReport.json"), """
            {
              "kronikolVersion": "3.2.0", "formatVersion": 1, "suite": "Suite",
              "startTime": "2026-08-01T10:00:00Z", "endTime": "2026-08-01T10:05:00Z",
              "ciMetadata": { "provider": "GitHubActions", "branch": "main", "commitSha": "abc", "runId": "500", "runAttempt": "1" },
              "features": [ { "name": "Checkout", "labels": [], "scenarios": [
                { "id": "t0", "stableId": "aaaabbbbccccdddd", "name": "Pay", "result": "Failed", "durationSeconds": 1.5, "errorMessage": "boom", "labels": [], "categories": [], "steps": [], "httpInteractions": [] }
              ] } ]
            }
            """);

        var (output, error, exit) = Run("import", directory, "--history", Ledger);

        Assert.True(exit == 0, error);
        Assert.Contains("imported   gh:500:1", output);
        var run = Assert.Single(HistoryLedgerReader.Read(Ledger, 50).Ledger!.Runs("Suite"));
        Assert.Equal("F", run.Results);
        Assert.Equal([1500], run.Durations);
        Assert.Equal("main", run.Branch);
    }

    [Fact]
    public void Import_reads_ctrf_documents_and_keys_them_the_same_way_the_run_would()
    {
        var directory = Path.Combine(_dir, "ctrf");
        Directory.CreateDirectory(directory);
        var expected = ScenarioStableId.Compute("Suite", "Checkout", "Pay");
        File.WriteAllText(Path.Combine(directory, "ctrf-report.json"), """
            {
              "reportFormat": "CTRF", "specVersion": "0.0.0",
              "results": {
                "tool": { "name": "Other" },
                "summary": { "tests": 2, "passed": 1, "failed": 1, "pending": 0, "skipped": 0, "other": 0, "start": 1756720800000, "stop": 1756721100000 },
                "tests": [
                  { "name": "Pay", "suite": "Checkout", "status": "failed", "duration": 1500, "retries": 0, "flaky": false, "message": "boom" },
                  { "name": "Refund", "suite": "Checkout", "status": "passed", "duration": 40, "retries": 1, "flaky": true }
                ],
                "environment": { "branchName": "main", "commit": "abc1234", "buildNumber": "77" }
              }
            }
            """);

        var (output, error, exit) = Run("import", directory, "--from-ctrf", "--suite", "Suite", "--history", Ledger);

        Assert.True(exit == 0, error);
        Assert.Contains("imported   ctrf:", output);
        var ledger = HistoryLedgerReader.Read(Ledger, 50).Ledger!;
        var run = Assert.Single(ledger.Runs("Suite"));
        var roster = ledger.Roster(run.RosterHash)!;
        Assert.Equal(expected, roster.Ids[0]);
        Assert.Equal("FP", run.Results);
        Assert.Equal("-2", run.Attempts);
        Assert.Equal("main", run.Branch);
        Assert.Equal("boom", run.ErrorAt(0));
    }

    [Fact]
    public void Import_reads_allure_results_keeping_the_last_attempt_of_each_test()
    {
        var directory = Path.Combine(_dir, "allure-results");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "a-result.json"), """
            { "uuid": "a", "historyId": "h1", "fullName": "Checkout.Pay", "name": "Pay", "status": "failed", "start": 1756720800000, "stop": 1756720801000,
              "labels": [ { "name": "suite", "value": "Checkout" } ], "statusDetails": { "message": "boom" } }
            """);
        File.WriteAllText(Path.Combine(directory, "b-result.json"), """
            { "uuid": "b", "historyId": "h1", "fullName": "Checkout.Pay", "name": "Pay", "status": "passed", "start": 1756720802000, "stop": 1756720803000,
              "labels": [ { "name": "suite", "value": "Checkout" } ] }
            """);
        File.WriteAllText(Path.Combine(directory, "c-result.json"), """
            { "uuid": "c", "historyId": "h2", "fullName": "Checkout.Refund", "name": "Refund", "status": "broken", "start": 1756720800000, "stop": 1756720805000,
              "labels": [ { "name": "feature", "value": "Refunds" } ] }
            """);

        var (output, error, exit) = Run("import", directory, "--from-allure", "--suite", "Suite", "--branch", "main", "--run-id", "allure:1", "--history", Ledger);

        Assert.True(exit == 0, error);
        Assert.Contains("imported   allure:1", output);
        var ledger = HistoryLedgerReader.Read(Ledger, 50).Ledger!;
        var run = Assert.Single(ledger.Runs("Suite"));
        var roster = ledger.Roster(run.RosterHash)!;
        Assert.Equal(2, roster.Count);
        var pay = roster.IndexOf(ScenarioStableId.Compute("Suite", "Checkout", "Pay"));
        var refund = roster.IndexOf(ScenarioStableId.Compute("Suite", "Refunds", "Refund"));
        Assert.True(pay >= 0 && refund >= 0);
        Assert.Equal('P', run.ResultAt(pay));
        Assert.Equal(2, run.AttemptAt(pay));
        Assert.Equal('F', run.ResultAt(refund));
        Assert.Equal("main", run.Branch);
    }

    [Fact]
    public void Import_refuses_a_directory_with_nothing_it_understands()
    {
        var directory = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(directory);

        Assert.Equal(2, Run("import", directory, "--history", Ledger).Exit);
        Assert.Equal(2, Run("import", directory, "--from-ctrf", "--from-allure", "--history", Ledger).Exit);
    }
}
