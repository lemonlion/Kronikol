using System.Net;
using System.Text.Json;
using Kronikol.Tracking;
using Kronikol.ComponentDiagram;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tool;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// <c>kronikol merge</c> writes the merged data file beside the merged HTML, so a sharded run is
/// something an agent can query and something a later run can diff against. The merged file is the
/// mergeable superset - the same format merge consumes - so merging is closed under itself and the
/// combined run can be promoted to a baseline without a second tool.
/// </summary>
public class MergedJsonOutputTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-merge-json").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // ─── The file itself ───────────────────────────────────────

    [Fact]
    public void Merge_writes_the_merged_json_beside_the_html()
    {
        WriteShard("runner1.json", "Orders", "r1s1", "Place order", ExecutionResult.Passed);
        WriteShard("runner2.json", "Inventory", "r2s1", "Adjust stock", ExecutionResult.Failed);

        var (exit, output, error) = Merge(Out("Combined.html"));

        Assert.True(exit == 0, error);
        var json = Out("Combined.json");
        Assert.True(File.Exists(json), "The merged data file should sit beside the HTML. Stderr: " + error);
        Assert.Contains("Combined.json", output);

        // It is the mergeable superset, not the standard shape: that is what makes it re-mergeable.
        var root = JsonDocument.Parse(File.ReadAllText(json)).RootElement;
        Assert.Equal(1, root.GetProperty("mergeableFormatVersion").GetInt32());
        Assert.Equal(2, root.GetProperty("features").GetArrayLength());
    }

    [Fact]
    public void The_json_takes_its_name_from_the_output_html()
    {
        WriteShard("runner1.json", "Orders", "r1s1", "Place order", ExecutionResult.Passed);

        var (exit, _, error) = Merge(Out("Nightly.html"));

        Assert.True(exit == 0, error);
        Assert.True(File.Exists(Out("Nightly.json")), "-o names both files. Stderr: " + error);
    }

    [Fact]
    public void No_json_writes_only_the_html()
    {
        WriteShard("runner1.json", "Orders", "r1s1", "Place order", ExecutionResult.Passed);

        var (exit, output, error) = Merge(Out("Combined.html"), "--no-json");

        Assert.True(exit == 0, error);
        Assert.True(File.Exists(Out("Combined.html")));
        Assert.False(File.Exists(Out("Combined.json")), "--no-json means the HTML alone.");
        Assert.DoesNotContain("Combined.json", output);
    }

    // ─── The merged file is queryable ──────────────────────────

    [Fact]
    public void The_merged_json_answers_kronikol_query()
    {
        WriteShard("runner1.json", "Orders", "r1s1", "Place order", ExecutionResult.Passed);
        WriteShard("runner2.json", "Inventory", "r2s1", "Adjust stock", ExecutionResult.Failed);

        Assert.Equal(0, Merge(Out("Combined.html")).Exit);

        var summary = Query("summary", Out("Combined.json"));

        // Both runners' scenarios in one answer - the whole point of merging.
        Assert.Contains("2 scenarios", summary);
        Assert.Contains("1 failed", summary);
    }

    [Fact]
    public void The_merged_json_merges_again()
    {
        WriteShard("runner1.json", "Orders", "r1s1", "Place order", ExecutionResult.Passed);
        WriteShard("runner2.json", "Inventory", "r2s1", "Adjust stock", ExecutionResult.Failed);
        Assert.Equal(0, Merge(Out("Combined.html")).Exit);

        // Merging is closed under itself: yesterday's combined file is a valid input to tomorrow's merge.
        var second = Path.Combine(_directory, "second");
        Directory.CreateDirectory(second);
        var (exit, _, error) = Run(["--no-json", Out("Combined.json"), "-o", Path.Combine(second, "Again.html")]);

        Assert.True(exit == 0, error);
        Assert.Contains("Adjust stock", File.ReadAllText(Path.Combine(second, "Again.html")));
    }

    // ─── What the merge must not lose or invent ────────────────

    [Fact]
    public void The_merged_json_keeps_the_diagnostics_every_shard_recorded()
    {
        // A worker that died mid-run leaves ResultDefaulted behind, and a scenario defaulted to Passed
        // is indistinguishable from a real pass without it. Merging must not be how that warning is lost.
        WriteRawShard("runner1.json", "3.0.50", "s1", """
            [ { "kind": "ResultDefaulted", "message": "1 scenario never reported an end", "scenarioId": "s1" } ]
            """);
        WriteRawShard("runner2.json", "3.0.50", "s2", "[]");

        Assert.Equal(0, Merge(Out("Combined.html")).Exit);

        var root = JsonDocument.Parse(File.ReadAllText(Out("Combined.json"))).RootElement;
        var diagnostics = root.GetProperty("diagnostics");
        Assert.Equal(1, diagnostics.GetArrayLength());
        Assert.Equal("ResultDefaulted", diagnostics[0].GetProperty("kind").GetString());

        // And it still reaches a reader of the merged file.
        Assert.Contains("defaulted", Query("summary", Out("Combined.json")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_merged_json_reports_the_version_that_ran_the_tests()
    {
        // The merging tool is not what produced the run, and `query summary` prints this to tell a
        // reader which Kronikol's contract the file honours.
        WriteRawShard("runner1.json", "3.0.50", "s1", "[]");

        Assert.Equal(0, Merge(Out("Combined.html")).Exit);

        var root = JsonDocument.Parse(File.ReadAllText(Out("Combined.json"))).RootElement;
        Assert.Equal("3.0.50", root.GetProperty("kronikolVersion").GetString());
    }

    /// <summary>
    /// `kronikol merge ./artifacts -o ./artifacts/runner1.html` would otherwise overwrite the shard it
    /// just read, and the next run of the same command would re-ingest its own output.
    /// </summary>
    /// <remarks>
    /// Until 3.6.0 this asserted <c>exit == 0</c> and that the HTML was written anyway, which is what
    /// the tool did and not what it should have done: the refusal ran after the render, so a "refused"
    /// merge left a rewritten report beside a stale data file, each describing a different run, and a CI
    /// step reading <c>$?</c> saw success. Both assertions are inverted deliberately. The wider defect
    /// the old shape concealed is in
    /// <see cref="MergeRefusesBeforeItWritesTests"/>: only this <c>.html</c> spelling was ever checked,
    /// so <c>-o runner1.json</c> destroyed the shard outright.
    /// </remarks>
    [Fact]
    public void The_json_is_never_written_over_one_of_the_merge_inputs()
    {
        WriteShard("runner1.json", "Orders", "r1s1", "Place order", ExecutionResult.Passed);
        WriteShard("runner2.json", "Inventory", "r2s1", "Adjust stock", ExecutionResult.Failed);
        var shard = Out("runner1.json");
        var before = File.ReadAllText(shard);

        var (exit, _, error) = Merge(Out("runner1.html"));

        Assert.Equal(2, exit);
        Assert.False(File.Exists(Out("runner1.html")), "A refused merge writes nothing at all.");
        Assert.Equal(before, File.ReadAllText(shard));
        Assert.Contains("runner1.json", error);
    }

    [Fact]
    public void A_failed_merge_leaves_no_json_behind()
    {
        var standard = ReportGenerator.GenerateTestRunReportData(
            [new Feature { DisplayName = "F", Scenarios = [new Scenario { Id = "x", DisplayName = "s", Result = ExecutionResult.Passed }] }],
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 1, 0, DateTimeKind.Utc),
            "standard.json", DataFormat.Json);
        File.Copy(standard, Out("standard.json"), overwrite: true);

        var (exit, _, _) = Run([Out("standard.json"), "-o", Out("Combined.html")]);

        Assert.Equal(1, exit);
        Assert.False(File.Exists(Out("Combined.json")), "A half-written data file is worse than none.");
    }

    [Fact]
    public void The_usage_text_names_the_second_file_and_the_flag()
    {
        var usage = new StringWriter();
        MergeCommand.PrintUsage(usage);

        Assert.Contains("--no-json", usage.ToString());
    }

    // ─── The traffic survives the merge ───────────────

    [Fact]
    public void The_mergeable_shard_carries_the_traffic_it_captured()
    {
        // The format called itself a strict superset of the standard report while dropping the single
        // largest thing the standard report carries. Every shard now writes its interactions.
        WriteShardWithTraffic("runner1.json", "Orders", "r1s1", "Place order", "OrdersApi");

        var scenario = JsonDocument.Parse(File.ReadAllText(Out("runner1.json")))
            .RootElement.GetProperty("features")[0].GetProperty("scenarios")[0];

        var interactions = scenario.GetProperty("httpInteractions");
        Assert.Equal(2, interactions.GetArrayLength());
        Assert.Equal("OrdersApi", interactions[0].GetProperty("serviceName").GetString());
        Assert.Equal("POST", interactions[0].GetProperty("method").GetString());
        Assert.Equal("{\"sku\":\"A1\"}", interactions[0].GetProperty("content").GetString());
        Assert.Equal(200, interactions[1].GetProperty("statusCode").GetInt32());
        Assert.Equal("OK", interactions[1].GetProperty("statusText").GetString());
    }

    [Fact]
    public void The_merged_json_answers_the_interaction_verbs()
    {
        // The promise a merged file exists to keep: one table across every runner's traffic.
        WriteShardWithTraffic("runner1.json", "Orders", "r1s1", "Place order", "OrdersApi");
        WriteShardWithTraffic("runner2.json", "Inventory", "r2s1", "Adjust stock", "InventoryApi");

        Assert.Equal(0, Merge(Out("Combined.html")).Exit);
        var merged = Out("Combined.json");

        var summary = Query("summary", merged);
        Assert.Contains("4 interactions", summary);

        var services = Query("services", merged);
        Assert.Contains("OrdersApi", services);
        Assert.Contains("InventoryApi", services);
    }

    [Fact]
    public void A_body_in_a_merged_report_can_still_be_read()
    {
        // `query body` resolves an address to a payload; without the interactions the address does not
        // exist, so the agent's whole read path dead-ends on a merged file.
        WriteShardWithTraffic("runner1.json", "Orders", "r1s1", "Place order", "OrdersApi");
        Assert.Equal(0, Merge(Out("Combined.html")).Exit);

        // The whole read path an agent walks: list the calls, take a content address off the listing,
        // ask for the payload behind it.
        var addresses = System.Text.RegularExpressions.Regex
            .Matches(Query("interactions", Out("Combined.json")), "b:[0-9a-f]+")
            .Select(m => m.Value).Distinct().ToArray();

        Assert.NotEmpty(addresses);
        Assert.Contains(addresses, a => Query("body", Out("Combined.json"), a).Contains("sku", StringComparison.Ordinal));
    }

    [Fact]
    public void The_merged_report_does_not_claim_a_scenario_touched_nothing()
    {
        // `showNoInteractionsMarker` fell back to the ambient log of the *merging* process - always
        // empty - so every merged scenario with a diagram was labelled as having made no calls.
        WriteShardWithTraffic("runner1.json", "Orders", "r1s1", "Place order", "OrdersApi");

        Assert.Equal(0, Merge(Out("Combined.html")).Exit);

        Assert.DoesNotContain("data-no-interactions", File.ReadAllText(Out("Combined.html")));
    }

    [Fact]
    public void A_baseline_folder_is_not_swept_in_as_a_shard()
    {
        // `<reports>/baseline/` is last-green, kept beside a run for `query diff --baseline`. A
        // directory input is swept recursively, so without the skip a merge folds yesterday's results
        // into today's and every count is wrong.
        WriteShard("runner1.json", "Orders", "r1s1", "Place order", ExecutionResult.Passed);
        Directory.CreateDirectory(Path.Combine(_directory, "baseline"));
        WriteShard(Path.Combine("baseline", "TestRunReport.json"), "Orders", "old1", "Place order", ExecutionResult.Failed);

        var (exit, output, error) = Merge(Out("Combined.html"));

        Assert.True(exit == 0, error);
        Assert.DoesNotContain("baseline", output);
        Assert.Equal(1, JsonDocument.Parse(File.ReadAllText(Out("Combined.json")))
            .RootElement.GetProperty("features")[0].GetProperty("scenarios").GetArrayLength());
    }

    // ─── Fixtures ──────────────────────────────────────────────

    private string Out(string name) => Path.Combine(_directory, name);

    private (int Exit, string Output, string Error) Merge(string output, params string[] extra) =>
        Run([_directory, "-o", output, .. extra]);

    private static (int Exit, string Output, string Error) Run(string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = MergeCommand.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }

    private static string Query(string command, string report, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([command, report, .. args], output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString();
    }

    private void WriteShard(string name, string feature, string scenarioId, string scenarioName, ExecutionResult result)
    {
        var json = ReportGenerator.GenerateMergeableReportJson(
            [new Feature { DisplayName = feature, Scenarios = [new Scenario { Id = scenarioId, DisplayName = scenarioName, Result = result }] }],
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 2, 0, DateTimeKind.Utc),
            diagramLookup: null,
            [new ComponentRelationship("Test", feature + "Api", "HTTP", new HashSet<string> { "GET /" }, 1, 1, "http")],
            internalFlowSegmentData: null, wholeTestFlow: null,
            WholeTestFlowVisualization.None, ciMetadata: null);
        File.WriteAllText(Out(name), json);
    }

    /// <summary>A shard whose scenario made one real call, so the merge has traffic to carry.</summary>
    private void WriteShardWithTraffic(string name, string feature, string scenarioId, string scenarioName, string service)
    {
        var pairId = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        OneOf<HttpMethod, string> post = HttpMethod.Post;

        RequestResponseLog Log(RequestResponseType type, string? content, HttpStatusCode? status, int offsetMs)
        {
            OneOf<HttpStatusCode, string>? code = status is null ? null : status.Value;
            return new RequestResponseLog(scenarioName, scenarioId, post, content,
                new Uri($"https://{service.ToLowerInvariant()}.test/orders"), [], service, "Test", type,
                traceId, pairId, TrackingIgnore: false, StatusCode: code)
            { Timestamp = new DateTimeOffset(start).AddMilliseconds(offsetMs) };
        }

        var logs = new[]
        {
            Log(RequestResponseType.Request, """{"sku":"A1"}""", null, 0),
            Log(RequestResponseType.Response, """{"id":7}""", HttpStatusCode.OK, 40),
        };

        var json = ReportGenerator.GenerateMergeableReportJson(
            [new Feature { DisplayName = feature, Scenarios = [new Scenario { Id = scenarioId, DisplayName = scenarioName, Result = ExecutionResult.Passed }] }],
            start, start.AddMinutes(2),
            diagramLookup: null,
            [new ComponentRelationship("Test", service, "HTTP", new HashSet<string> { "POST /orders" }, 1, 1, "http")],
            internalFlowSegmentData: null, wholeTestFlow: null,
            WholeTestFlowVisualization.None, ciMetadata: null, diagnostics: null, trackedLogs: logs);
        File.WriteAllText(Out(name), json);
    }

    /// <summary>A mergeable shard written by hand, so the version and the diagnostics are ours to choose.</summary>
    private void WriteRawShard(string name, string version, string scenarioId, string diagnostics)
    {
        File.WriteAllText(Out(name), $$"""
            {
              "kronikolVersion": "{{version}}",
              "mergeableFormatVersion": 1,
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:02:00Z",
              "features": [
                {
                  "name": "Orders",
                  "labels": [],
                  "scenarios": [
                    { "id": "{{scenarioId}}", "name": "Scenario {{scenarioId}}", "result": "Passed", "durationSeconds": 1.0, "labels": [], "categories": [], "steps": [] }
                  ]
                }
              ],
              "wholeTestVisualization": "None",
              "componentRelationships": [],
              "internalFlowSegments": {},
              "wholeTestFlow": {},
              "diagnostics": {{diagnostics}}
            }
            """);
    }
}
