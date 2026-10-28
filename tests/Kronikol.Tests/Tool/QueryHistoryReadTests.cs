using System.Runtime.CompilerServices;
using Kronikol.History;
using Kronikol.Query;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol query history</c> after <c>history record</c> (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 3.9): the report's
/// own run is the ledger's newest line, the read kept the newest <c>--window</c> runs with it among them, and the analysis
/// wants the window before it, so the oldest run it needed was always one past the read and the whole ledger was read
/// again for it (1,127 ms against 310 on BreakfastProvider's ledger). The read now keeps one run more.
/// </summary>
public class QueryHistoryReadTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-query-history-read").FullName;
    private const string PayId = "aaaabbbbccccdddd";

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Ledger => Path.Combine(_dir, ".kronikol", "history.jsonl");

    private void Seed(int runs)
    {
        var roster = HistoryRoster.Create("Suite", [new HistoryRosterEntry(PayId, "Pay by card", "Checkout", null)]);
        for (var i = 0; i < runs; i++)
            HistoryLedgerWriter.Append(Ledger, roster, new HistoryRun
            {
                Id = $"gh:{i + 1}:1", Suite = "Suite", Partial = false, At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i),
                Branch = "main", Commit = $"c{i:D6}", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
                Results = "P", Attempts = "-", Durations = [100], Calls = null, ShapeSet = null, ShapeOrdered = null, Errors = null,
                ErrorText = new Dictionary<string, string>(), Deps = ["Test>orders"]
            }, "5.16.1");
    }

    private string WriteReport(string runId)
    {
        var directory = Path.Combine(_dir, "reports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "TestRunReport.json");
        File.WriteAllText(path, $$"""
            {
              "kronikolVersion": "5.16.1",
              "formatVersion": 1,
              "suite": "Suite",
              "startTime": "2026-09-12T10:00:00Z",
              "endTime": "2026-09-12T10:05:00Z",
              "ciMetadata": { "provider": "GitHubActions", "buildNumber": "42", "branch": "main", "commitSha": "abc1234", "pipelineUrl": null, "repository": "o/r", "runId": "{{runId}}", "runAttempt": "1" },
              "features": [ { "name": "Checkout", "labels": [], "scenarios": [
                { "id": "t0", "stableId": "{{PayId}}", "name": "Pay by card", "result": "Passed", "durationSeconds": 0.1, "labels": [], "categories": [], "steps": [], "httpInteractions": [] }
              ] } ]
            }
            """);
        return path;
    }

    [Fact]
    public void Query_history_after_record_reads_the_ledger_once()
    {
        // Sixty runs; the report is the sixtieth, already recorded.
        Seed(60);
        var report = WriteReport(runId: "60");
        var reloads = new StrongBox<int>();
        var (output, error) = (new StringWriter(), new StringWriter());

        HistoryLedger.ReloadCounter.Value = reloads;
        int exit;
        try
        {
            exit = QueryCommand.Run(["history", report], output, error, _ => null, workingDirectory: _dir);
        }
        finally
        {
            HistoryLedger.ReloadCounter.Value = null;
        }

        Assert.True(exit == 0, error.ToString());
        Assert.Contains("against 50 earlier runs on main", output.ToString());
        Assert.Equal(0, reloads.Value);
    }
}
