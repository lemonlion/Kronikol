using System.Net;
using Kronikol.History;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>history record</c> bridges an edit of the consumer's patterns from the fragment's raw calls (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md
/// 4.8, Q18). A run that only writes a fragment, the History action's flow, cannot know the patterns its ledger's window
/// was fingerprinted under, so until #192 R1 an edit cost every scenario one quiet run there, as it did everywhere before.
/// </summary>
public class HistoryRecordRawCallsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-record-raw").FullName;
    private string Ledger => Path.Combine(_dir, "ledger", "history.jsonl");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static readonly HistoryShapeRules Narrow = HistoryShapeRules.Create([new HistoryShapeTemplate(@"tenant\d+x", "{tenant}")])!;
    private static readonly HistoryShapeRules Wide = HistoryShapeRules.Create([new HistoryShapeTemplate(@"key-tenant\d+x", "key-{tenant}")])!;

    /// <summary>A run's fragment, as a test run that only writes one leaves it: its window unknown.</summary>
    private string Fragment(int run, HistoryShapeRules rules, string path = "menu")
    {
        var features = new[]
        {
            new Feature { DisplayName = "Cache", Scenarios = [new Scenario { Id = "t1", DisplayName = "Read the menu", Result = ExecutionResult.Passed }] }
        };
        var pair = Guid.NewGuid();
        var uri = new Uri($"http://cache/{path}/key-tenant{run}x");
        RequestResponseLog?[] logs =
        [
            new RequestResponseLog("t", "t1", HttpMethod.Get, null, uri, [], "Cache", "Caller", RequestResponseType.Request, Guid.NewGuid(), pair, TrackingIgnore: false),
            new RequestResponseLog("t", "t1", HttpMethod.Get, null, uri, [], "Cache", "Caller", RequestResponseType.Response, Guid.NewGuid(), pair, TrackingIgnore: false,
                StatusCode: HttpStatusCode.OK)
        ];
        var ci = new CiMetadata(CiEnvironment.GitHubActions, null, "main", "abc1234", null, null, run.ToString(System.Globalization.CultureInfo.InvariantCulture), "1");
        var built = HistoryRunBuilder.Build(features, logs, "Suite", ci, new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero).AddHours(run),
            new HistoryBuildOptions { Durations = true, Shapes = true, ErrorKeys = true, Bridges = true, ShapeRules = rules });
        var directory = Path.Combine(_dir, "run" + run);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, HistoryFormat.FragmentFileName),
            HistoryFragment.Write(built.Roster, built.Run with { Generator = "5.17.0" }, "5.17.0", built.Shapes, built.RawCalls));
        return directory;
    }

    /// <summary>A run's fragment beside the report it describes, which import, gate and query history read.</summary>
    private string Report(int run, HistoryShapeRules rules, string path = "menu")
    {
        var directory = Fragment(run, rules, path);
        var id = HistoryFragment.Parse(File.ReadAllText(Path.Combine(directory, HistoryFormat.FragmentFileName))).Roster.Ids[0];
        var report = Path.Combine(directory, "TestRunReport.json");
        File.WriteAllText(report, $$"""
            {
              "kronikolVersion": "5.17.0", "formatVersion": 1, "suite": "Suite",
              "startTime": "2026-10-28T09:00:00Z", "endTime": "2026-10-28T13:00:00Z",
              "ciMetadata": { "provider": "GitHubActions", "buildNumber": "{{run}}", "branch": "main", "commitSha": "abc1234", "pipelineUrl": null, "repository": "o/r", "runId": "{{run}}", "runAttempt": "1" },
              "features": [ { "name": "Cache", "labels": [], "scenarios": [
                { "id": "t1", "stableId": "{{id}}", "name": "Read the menu", "result": "Passed", "durationSeconds": 0.1, "labels": [], "categories": [], "steps": [], "httpInteractions": [] }
              ] } ]
            }
            """);
        return report;
    }

    private void Record(string directory)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = HistoryCommand.Run(["record", directory, "--history", Ledger], output, error, _ => null);
        Assert.True(exit == 0, error.ToString() + output);
    }

    private ScenarioHistory Latest()
    {
        var ledger = HistoryLedgerReader.Read(Ledger, 50).Ledger!;
        var current = ledger.LatestRun("Suite")!;
        var verdicts = HistoryAnalyzer.Analyse(ledger, ledger.Roster(current.RosterHash)!, current, new HistoryAnalysisOptions { Window = 50, MinRuns = 2 });
        return Assert.Single(verdicts.Scenarios);
    }

    [Fact]
    public void Record_bridges_a_consumer_rule_edit_from_the_fragments_raw_calls()
    {
        for (var n = 1; n <= 3; n++)
            Record(Fragment(n, Narrow));

        Record(Fragment(4, Wide));

        var line = HistoryLedgerReader.Read(Ledger, 50).Ledger!.LatestRun("Suite")!;
        Assert.Contains(HistoryBridge.RuleKey(InteractionShape.Version, Narrow.Hash), line.Bridge!.Keys);
        var edited = Latest();
        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, edited.Verdicts);
        Assert.DoesNotContain("earlier rule", edited.Evidence, StringComparison.Ordinal);
        Assert.NotNull(edited.PreviousShapeSet);
    }

    [Fact]
    public void Import_bridges_a_consumer_rule_edit_from_the_fragments_raw_calls()
    {
        for (var n = 1; n <= 3; n++)
            Record(Fragment(n, Narrow));

        var report = Report(4, Wide);
        var (output, error) = (new StringWriter(), new StringWriter());
        Assert.True(HistoryCommand.Run(["import", Path.GetDirectoryName(report)!, "--history", Ledger], output, error, _ => null) == 0, error.ToString() + output);

        Assert.Contains(HistoryBridge.RuleKey(InteractionShape.Version, Narrow.Hash), HistoryLedgerReader.Read(Ledger, 50).Ledger!.LatestRun("Suite")!.Bridge!.Keys);
        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, Latest().Verdicts);
    }

    [Theory]
    [InlineData("orders", 1)]
    [InlineData("menu", 0)]
    public void Gate_reads_a_run_that_only_wrote_its_fragment_across_an_edit(string path, int exit)
    {
        // Until #192 R1 the gate read an edit as no verdict at all, so a real change across it passed.
        for (var n = 1; n <= 3; n++)
            Record(Fragment(n, Narrow));

        var report = Report(4, Wide, path);
        var (output, error) = (new StringWriter(), new StringWriter());

        Assert.Equal(exit, HistoryCommand.Run(["gate", report, "--history", Ledger, "--fail-on", "behaviour-change", "--min-runs", "2"], output, error, _ => null));
    }

    [Theory]
    [InlineData("orders", true)]
    [InlineData("menu", false)]
    public void Query_history_reads_a_run_that_only_wrote_its_fragment_across_an_edit(string path, bool changed)
    {
        for (var n = 1; n <= 3; n++)
            Record(Fragment(n, Narrow));

        var report = Report(4, Wide, path);
        var (output, error) = (new StringWriter(), new StringWriter());

        Assert.Equal(0, QueryCommand.Run(["history", report, "--history", Ledger, "--json"], output, error, _ => null, workingDirectory: _dir));
        Assert.Equal(changed, output.ToString().Contains("\"behaviour-changed\"", StringComparison.Ordinal));
    }

    [Fact]
    public void A_real_change_across_an_edit_still_reads_as_one_through_record()
    {
        for (var n = 1; n <= 3; n++)
            Record(Fragment(n, Narrow));

        Record(Fragment(4, Wide, path: "orders"));

        Assert.Contains(HistoryVerdictKind.BehaviourChanged, Latest().Verdicts);
    }
}
