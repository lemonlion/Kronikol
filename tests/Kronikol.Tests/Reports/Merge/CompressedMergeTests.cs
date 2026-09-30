using System.Net;
using System.Text.Json;
using Kronikol.ComponentDiagram;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tool;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// <c>kronikol merge</c> over shards written with <c>CompressTestRunReportPayloads</c> (#85,
/// <c>plans/PAYLOAD_COMPRESSION_PLAN.md</c> §2): it reads a compressed shard beside a plain one, and writes the merge
/// compressed when any shard was, so a merge of 3.x shards stays readable by the tools that read them.
/// </summary>
public class CompressedMergeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kronikol-compressed-merge-" + Guid.NewGuid().ToString("N"));

    public CompressedMergeTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void A_compressed_shard_merges_with_a_plain_one_and_the_merge_is_compressed()
    {
        // A 4.0.0 shard, written with the default option, beside a 3.x shard, which was written plain.
        WriteShard("runner1.json", "a1", "Orders", Body("orders"), compress: new ReportConfigurationOptions().CompressTestRunReportPayloads);
        WriteShard("runner2.json", "b1", "Payments", Body("payments"), compress: false);

        var combined = Merge();

        Assert.Equal(2, combined.RootElement.GetProperty("formatVersion").GetInt32());
        foreach (var (id, service) in new[] { ("a1", "orders"), ("b1", "payments") })
        {
            var content = Scenario(combined.RootElement, id).GetProperty("httpInteractions")[0].GetProperty("content");
            Assert.Equal(JsonValueKind.Object, content.ValueKind);
            Assert.Equal(Body(service), ReportPayloads.Inflate(content.GetProperty("$z").GetString()!));
        }

        var body = Query("http", Path.Combine(_directory, "Combined.json"), "s0/i0", "--path", "$.service");
        Assert.Contains("orders", body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_merge_of_plain_shards_is_plain()
    {
        WriteShard("runner1.json", "a1", "Orders", Body("orders"), compress: false);
        WriteShard("runner2.json", "b1", "Payments", Body("payments"), compress: false);

        var combined = Merge();

        Assert.Equal(1, combined.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(Body("orders"), Scenario(combined.RootElement, "a1").GetProperty("httpInteractions")[0].GetProperty("content").GetString());
    }

    [Fact]
    public void A_diagram_a_shard_held_compressed_is_merged_as_its_text()
    {
        WriteShard("runner1.json", "a1", "Orders", Body("orders"), compress: true);

        var report = MergeableReportReader.ReadFile(Path.Combine(_directory, "runner1.json"));

        Assert.Equal(Diagram("Orders"), Assert.Single(report.Diagrams).CodeBehind);
        Assert.Equal(Body("orders"), report.Interactions[0].Content);
    }

    [Fact]
    public void A_shard_of_a_format_this_build_does_not_know_is_refused()
    {
        WriteShard("runner1.json", "a1", "Orders", Body("orders"), compress: true);
        var path = Path.Combine(_directory, "runner1.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"formatVersion\": 2", "\"formatVersion\": 3", StringComparison.Ordinal));

        var error = Assert.Throws<FormatException>(() => MergeableReportReader.ReadFile(path));

        Assert.Contains("formatVersion 3", error.Message, StringComparison.Ordinal);
    }

    // ─── Fixture ───────────────────────────────────────────────

    private static string Body(string service) =>
        JsonSerializer.Serialize(new { service, lines = Enumerable.Range(1, 30).Select(i => new { sku = $"{service}-{i}", qty = i }).ToArray() });

    private static string Diagram(string service) =>
        "@startuml\n" + string.Concat(Enumerable.Range(1, 40).Select(i => $"Test -> {service} : call {i}\n")) + "@enduml";

    private void WriteShard(string name, string scenarioId, string service, string body, bool compress)
    {
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var at = new DateTimeOffset(start).AddSeconds(1);
        var traceId = Guid.NewGuid();
        var pairId = Guid.NewGuid();
        RequestResponseLog[] logs =
        [
            new(scenarioId, scenarioId, HttpMethod.Post, body, new Uri($"http://{service.ToLowerInvariant()}/x"), [], service, "Test",
                RequestResponseType.Request, traceId, pairId, false) { Timestamp = at },
            new(scenarioId, scenarioId, HttpMethod.Post, "{}", new Uri($"http://{service.ToLowerInvariant()}/x"), [], service, "Test",
                RequestResponseType.Response, traceId, pairId, false, HttpStatusCode.OK) { Timestamp = at.AddMilliseconds(20) }
        ];

        var json = ReportGenerator.GenerateMergeableReportJson(
            [new Feature { DisplayName = service, Scenarios = [new Scenario { Id = scenarioId, DisplayName = "Call " + service, Result = ExecutionResult.Passed }] }],
            start, start.AddMinutes(1),
            new[] { new DiagramAsCode(scenarioId, "", Diagram(service)) }.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            [new ComponentRelationship("Test", service, "HTTP", new HashSet<string> { "POST /x" }, 1, 1, "http")],
            internalFlowSegmentData: null, wholeTestFlow: null, WholeTestFlowVisualization.None, ciMetadata: null,
            diagnostics: null, trackedLogs: logs, compressPayloads: compress);
        File.WriteAllText(Path.Combine(_directory, name), json);
    }

    private JsonDocument Merge()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = MergeCommand.Run([_directory, "-o", Path.Combine(_directory, "Combined.html")], output, error);
        Assert.True(exit == 0, error.ToString());
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "Combined.json")));
    }

    private static JsonElement Scenario(JsonElement root, string id) =>
        root.GetProperty("features").EnumerateArray().SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .Single(s => s.GetProperty("id").GetString() == id);

    private static string Query(string command, string report, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([command, report, .. args], output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString();
    }
}
