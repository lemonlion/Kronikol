using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// #192 R2 (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.5, 3.9): <c>kronikol merge</c> keeps the shards' HistoryShapeTemplates
/// when every input records the same ones and none otherwise, naming the inputs that differ, as <c>history record</c>'s
/// fold blanks a run whose shards differ; and <c>merge --history</c> builds the merged run with them, where it read with
/// no rules against runs recorded under them.
/// </summary>
[Collection("DiagramsFetcher")]
public class MergeShapeTemplatesTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);
    private static readonly HistoryShapeTemplate[] Rules = [new(@"(?<=/cache/)[a-z]+-\d+", "{key}")];
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-merge-templates").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static Feature[] Features(string scenario) =>
        [new Feature { DisplayName = "Orders " + scenario, Scenarios = [new Scenario { Id = scenario, DisplayName = "Place order " + scenario, Result = ExecutionResult.Passed }] }];

    // The shards of one CI run, so the merged run has the id the run recorded itself under.
    private static readonly CiMetadata Ci = new(CiEnvironment.GitHubActions, "60", "main", "abc1234", null, "o/r", "60", "1");

    private static string ShardJson(string scenario, IReadOnlyList<HistoryShapeTemplate>? rules, RequestResponseLog[]? logs = null) =>
        ReportGenerator.GenerateMergeableReportJson(Features(scenario), Start, Start.AddMinutes(1), null, null, null, null,
            WholeTestFlowVisualization.None, Ci, trackedLogs: logs, shapeTemplates: rules);

    private static MergeableReport Shard(string scenario, IReadOnlyList<HistoryShapeTemplate>? rules, RequestResponseLog[]? logs = null) =>
        MergeableReportReader.Parse(ShardJson(scenario, rules, logs));

    private static RequestResponseLog[] Get(string testId, string uri)
    {
        var trace = Guid.NewGuid();
        var pair = Guid.NewGuid();
        return
        [
            new RequestResponseLog(testId, testId, HttpMethod.Get, null, new Uri(uri), [], "Cache", "orders-api", RequestResponseType.Request, trace, pair, false)
                { Timestamp = new DateTimeOffset(Start).AddSeconds(1) },
            new RequestResponseLog(testId, testId, HttpMethod.Get, "{}", new Uri(uri), [], "Cache", "orders-api", RequestResponseType.Response, trace, pair, false, HttpStatusCode.OK)
                { Timestamp = new DateTimeOffset(Start).AddSeconds(1.02) }
        ];
    }

    private static bool SaysTemplates(MergeableReport merged) =>
        merged.Diagnostics.Any(d => d.Message.Contains("HistoryShapeTemplates", StringComparison.Ordinal));

    [Fact]
    public void A_shard_reads_back_its_rules()
    {
        Assert.Equal(Rules, Shard("a", Rules).ShapeTemplates);
        Assert.Null(Shard("a", null).ShapeTemplates);
    }

    [Fact]
    public void Merge_keeps_the_templates_when_every_input_agrees()
    {
        var merged = MergeableReportMerger.Merge([Shard("a", Rules), Shard("b", Rules)]);

        Assert.Equal(Rules, merged.ShapeTemplates);
        Assert.False(SaysTemplates(merged));
        // And the merged file says so, for a query of it to read.
        var block = JsonDocument.Parse(MergeableReportRenderer.Serialize(merged)).RootElement.GetProperty("shapeTemplates");
        Assert.Equal(Rules.Select(r => r.Pattern), block.EnumerateArray().Select(t => t.GetProperty("pattern").GetString()!));
    }

    [Fact]
    public void Merge_drops_the_templates_when_the_inputs_differ_and_names_them()
    {
        var merged = MergeableReportMerger.Merge([Shard("a", Rules), Shard("b", null), Shard("c", Rules)]);

        Assert.Null(merged.ShapeTemplates);
        var said = Assert.Single(merged.Diagnostics, d => d.Message.Contains("HistoryShapeTemplates", StringComparison.Ordinal));
        Assert.Contains("reports 1 and 3", said.Message, StringComparison.Ordinal);
        Assert.Contains("report 2 records none", said.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("shapeTemplates", MergeableReportRenderer.Serialize(merged), StringComparison.Ordinal);
    }

    [Fact]
    public void A_merge_of_inputs_without_rules_records_none_and_says_nothing()
    {
        var merged = MergeableReportMerger.Merge([Shard("a", null), Shard("b", null)]);

        Assert.Null(merged.ShapeTemplates);
        Assert.False(SaysTemplates(merged));
    }

    [Fact]
    public void Merge_history_reads_with_the_shards_rules()
    {
        var merged = MergeableReportMerger.Merge([Shard("a", Rules), Shard("b", Rules)]);

        var (_, run) = MergeCommand.HistoryRunOf(merged);

        Assert.Equal(HistoryShapeRules.Create(Rules)!.Hash, run.ShapeRules);
        Assert.Equal(Rules, run.ShapeTemplates);
    }

    [Fact]
    public void Merge_history_bridges_the_rules_its_window_was_recorded_under()
    {
        // As query history bridges a run it rebuilds from a report (R1): an edit of the rules between the window's runs and
        // this one is compared through a bridge, where the merged run read as one made under other rules, with no verdict.
        var merged = MergeableReportMerger.Merge([Shard("a", Rules, Get("a", "http://cache/cache/abc-1"))]);
        var earlier = HistoryShapeRules.Create([new(@"abc-\d+", "{old}")])!;
        var (roster, run) = MergeCommand.HistoryRunOf(merged);
        var path = Path.Combine(_dir, ".kronikol", "history.jsonl");
        HistoryLedgerWriter.Append(path, roster, run with
        {
            Id = "gh:59:1", At = new DateTimeOffset(Start).AddHours(-1), ShapeRules = earlier.Hash, ShapeTemplates = earlier.Templates
        }, "5.17.0");
        var ledger = HistoryLedgerReader.Read(path, 51).Ledger!;

        var (_, bridged) = MergeCommand.HistoryRunOf(merged, ledger, [], 50);

        Assert.NotNull(run.ShapeSet);
        Assert.True(bridged.Bridge?.ContainsKey(HistoryBridge.RuleKey(InteractionShape.Version, earlier.Hash)) == true,
            "bridges: " + string.Join(", ", bridged.Bridge?.Keys ?? []));
    }

    [Fact]
    public void Merge_history_after_record_reads_the_ledger_once()
    {
        // The merged run is the ledger's newest line, as after history record folded its shards: the read keeps the window
        // before it as well, so the analysis finds every run it compares without reading the ledger again.
        var shards = Path.Combine(_dir, "shards");
        Directory.CreateDirectory(shards);
        File.WriteAllText(Path.Combine(shards, "runner1.json"), ShardJson("a", null));
        File.WriteAllText(Path.Combine(shards, "runner2.json"), ShardJson("b", null));
        var merged = MergeableReportRenderer.MergeFiles(Directory.GetFiles(shards));
        var (roster, run) = MergeCommand.HistoryRunOf(merged);
        Assert.Equal("gh:60:1", run.Id);
        var ledger = Path.Combine(_dir, ".kronikol", "history.jsonl");
        for (var i = 1; i <= 59; i++)
            HistoryLedgerWriter.Append(ledger, roster, run with
            {
                Id = $"gh:{i}:1", At = new DateTimeOffset(Start).AddHours(i - 60), Results = new string('P', roster.Count),
                Attempts = new string('-', roster.Count), Errors = new string?[roster.Count], ErrorText = new Dictionary<string, string>()
            }, "5.17.0");
        HistoryLedgerWriter.Append(ledger, roster, run, "5.17.0");
        var reloads = new StrongBox<int>();

        HistoryLedger.ReloadCounter.Value = reloads;
        int exit;
        var error = new StringWriter();
        try
        {
            exit = MergeCommand.Run([shards, "-o", Path.Combine(_dir, "Combined.html"), "--history", ledger], new StringWriter(), error, _ => null);
        }
        finally
        {
            HistoryLedger.ReloadCounter.Value = null;
        }

        Assert.True(exit == 0, error.ToString());
        Assert.Equal(0, reloads.Value);
    }
}
