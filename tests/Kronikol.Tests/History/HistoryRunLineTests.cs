using System.Text.Json;
using Kronikol.History;
using Kronikol.Parties;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tests.Parties;
using Kronikol.Tool;

namespace Kronikol.Tests.History;

/// <summary>
/// #190 R1: the run line carries the service the run was about as <c>serviceId</c>, when the run declared one
/// (plans/PARTY_IDENTITY_PLAN.md §5.8). It is an optional member of the line every released reader skips by name, so
/// the ledger's version does not move, and each place that builds a run line keeps it: the run's own line, a parsed
/// and rewritten line, both folds and <c>query history</c>'s line from a report.
/// </summary>
[Collection("DiagramsFetcher")]
public class HistoryRunLineTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-run-line").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static Feature[] Features(params string[] scenarios) =>
        [new Feature { DisplayName = "Breakfast", Scenarios = [.. scenarios.Select(s => new Scenario { Id = s, DisplayName = "Order " + s, Result = ExecutionResult.Passed })] }];

    private static HistoryBuildResult Built(string? serviceId, string runId = "gh:1:1", params string[] scenarios) =>
        HistoryRunBuilder.Build(Features(scenarios.Length == 0 ? ["t1"] : scenarios), [], "Suite", null, At, new HistoryBuildOptions { ServiceId = serviceId }, runId);

    private static HistoryFragment Fragment(HistoryBuildResult built) => new(HistoryFormat.Version, built.Roster, built.Run, built.Shapes);

    private static string? ServiceIdOn(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.TryGetProperty("serviceId", out var id) ? id.GetString() : null;
    }

    [Fact]
    public void A_run_line_carries_the_service_id_when_one_is_set_and_no_member_otherwise()
    {
        var line = HistoryJson.RunLine(Built(" breakfast-provider ").Run);

        Assert.Equal("breakfast-provider", ServiceIdOn(line));
        using (var document = JsonDocument.Parse(line))
            Assert.Equal("serviceId", document.RootElement.EnumerateObject().Last().Name);
        Assert.DoesNotContain("serviceId", HistoryJson.RunLine(Built(null).Run));
        Assert.DoesNotContain("serviceId", HistoryJson.RunLine(Built("  ").Run));
    }

    [Fact]
    public void A_line_reads_back_with_its_service_id_and_a_line_written_before_it_reads_with_none()
    {
        var built = Built("breakfast-provider");
        var line = HistoryJson.RunLine(built.Run);

        var read = Assert.IsType<HistoryRun>(HistoryJson.Parse(line).Run);
        Assert.Equal("breakfast-provider", read.ServiceId);
        Assert.Equal(line, HistoryJson.RunLine(read));
        Assert.Null(Assert.IsType<HistoryRun>(HistoryJson.Parse(HistoryJson.RunLine(Built(null).Run)).Run).ServiceId);
    }

    [Theory]
    [InlineData("breakfast-provider", "breakfast-provider", "breakfast-provider")]
    [InlineData("breakfast-provider", "waffle-house", null)]
    [InlineData("breakfast-provider", null, null)]
    [InlineData(null, null, null)]
    public void A_fold_of_shards_keeps_the_service_id_only_when_every_shard_has_the_same_one(string? first, string? second, string? folded)
    {
        var run = HistoryFold.Fold([Fragment(Built(first, "gh:7:1", "t1")), Fragment(Built(second, "gh:7:1", "t2"))]).Single().Run;

        Assert.Equal(2, run.Shards);
        Assert.Equal(folded, run.ServiceId);
    }

    [Theory]
    [InlineData("breakfast-provider", "breakfast-provider", "breakfast-provider")]
    [InlineData("breakfast-provider", "waffle-house", null)]
    [InlineData(null, "breakfast-provider", null)]
    public void Attempts_of_one_run_keep_the_service_id_only_when_every_attempt_has_the_same_one(string? first, string? retry, string? kept)
    {
        var combined = HistoryFold.Attempts([Fragment(Built(first, "gh:7:1", "t1", "t2")), Fragment(Built(retry, "gh:7:1", "t2"))]);

        Assert.Equal(kept, combined.Run.ServiceId);
    }

    [Fact]
    public void A_ledger_rewritten_by_this_build_keeps_each_lines_service_id()
    {
        var path = Path.Combine(_dir, "history.jsonl");
        foreach (var (serviceId, n) in new[] { ("breakfast-provider", 1), ((string?)null, 2), ("breakfast-provider", 3) })
        {
            var built = Built(serviceId, $"gh:{n}:1");
            HistoryLedgerWriter.Append(path, built.Roster, built.Run with { At = At.AddHours(n) }, "test", shapes: built.Shapes);
        }

        HistoryLedgerWriter.Compact(path, 50, "test");

        var runs = HistoryLedgerReader.Read(path, 50).Ledger!.Runs("Suite").OrderBy(r => r.At).ToList();
        Assert.Equal(["breakfast-provider", null, "breakfast-provider"], runs.Select(r => r.ServiceId));
    }

    [Fact]
    public void A_ledger_mixing_lines_with_and_without_it_loads_whole_at_the_same_version_and_the_doctor_says_nothing_of_it()
    {
        var path = Path.Combine(_dir, ".kronikol", "history.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(Path.Combine(_dir, ".gitattributes"), HistoryFormat.GitAttributesLine + "\n");
        foreach (var (serviceId, n) in new[] { ((string?)null, 1), ("breakfast-provider", 2) })
        {
            var built = Built(serviceId, $"gh:{n}:1");
            HistoryLedgerWriter.Append(path, built.Roster, built.Run with { At = At.AddHours(n) }, "test", shapes: built.Shapes);
        }

        var read = HistoryLedgerReader.Read(path, 50);
        Assert.Equal(0, read.Ledger!.Stats.DamagedLines);
        Assert.Equal(2, read.Ledger.Runs("Suite").Count);
        Assert.Empty(HistoryLedgerReader.Verify(path));
        using (var header = JsonDocument.Parse(File.ReadLines(path).First()))
            Assert.Equal(1, header.RootElement.GetProperty("historyFormatVersion").GetInt32());

        var output = new StringWriter();
        var exit = HistoryCommand.Run(["doctor", "--history", path], output, new StringWriter(), _ => null);
        Assert.Equal(0, exit);
        Assert.Contains("none damaged", output.ToString());
    }

    [Fact]
    public void Query_history_builds_its_line_from_the_reports_block()
    {
        string Report(PartyTable? parties) => ReportGenerator.GenerateTestRunReportData(PartiesBlockTests.Features(), PartiesBlockTests.Start,
            PartiesBlockTests.Start.AddMinutes(1), Path.Combine(_dir, $"Parties_{Guid.NewGuid():N}.json"), DataFormat.Json, null, PartiesBlockTests.Logs(), null,
            fullStepDetail: true, ciMetadata: null, suite: "Suite", environment: null, attribution: null, parties: parties);

        Assert.True(ReportHistory.TryBuild(ReportScanner.Scan(Report(PartiesBlockTests.Declared())), null, TextWriter.Null, out _, out var declared, out _));
        Assert.Equal("breakfast-provider", declared.ServiceId);
        Assert.True(ReportHistory.TryBuild(ReportScanner.Scan(Report(null)), null, TextWriter.Null, out _, out var plain, out _));
        Assert.Null(plain.ServiceId);
    }

    [Fact]
    public void An_import_of_a_report_that_declares_its_service_keeps_it()
    {
        var directory = Path.Combine(_dir, "old-run");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "TestRunReport.json"), """
            {
              "kronikolVersion": "5.14.0", "formatVersion": 1, "suite": "Suite",
              "startTime": "2026-10-27T10:00:00Z", "endTime": "2026-10-27T10:05:00Z",
              "ciMetadata": { "provider": "GitHubActions", "branch": "main", "commitSha": "abc", "runId": "500", "runAttempt": "1" },
              "serviceId": "breakfast-provider",
              "parties": [ { "name": "Breakfast Provider", "id": "breakfast-provider", "kind": "underTest", "kindSource": "inferred" } ],
              "features": [ { "name": "Breakfast", "labels": [], "scenarios": [
                { "id": "t0", "stableId": "aaaabbbbccccdddd", "name": "Order", "result": "Passed", "durationSeconds": 1.5, "labels": [], "categories": [], "steps": [], "httpInteractions": [] }
              ] } ]
            }
            """);
        var ledger = Path.Combine(_dir, ".kronikol", "history.jsonl");

        var error = new StringWriter();
        var exit = HistoryCommand.Run(["import", directory, "--history", ledger], new StringWriter(), error, _ => null);

        Assert.True(exit == 0, error.ToString());
        Assert.Equal("breakfast-provider", Assert.Single(HistoryLedgerReader.Read(ledger, 50).Ledger!.Runs("Suite")).ServiceId);
    }
}
