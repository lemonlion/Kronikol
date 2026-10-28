using System.Net;
using System.Text.Json;
using Kronikol.ComponentDiagram;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.4.5: <c>kronikol merge</c> writes <c>service.json</c> beside the merged report, the card
/// <c>kronikol card</c> writes from the merged data file, so a sharded run has one card for all its shards.
/// </summary>
public class MergeServiceCardTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-merge-card").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private void WriteShard(string name, string scenarioId, string scenario, string service)
    {
        var start = new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);
        var at = new DateTimeOffset(start).AddSeconds(1);
        var (traceId, pairId) = (Guid.NewGuid(), Guid.NewGuid());
        RequestResponseLog[] logs =
        [
            new(scenarioId, scenarioId, HttpMethod.Post, "{}", new Uri("http://localhost/orders"), [], service, "Test",
                RequestResponseType.Request, traceId, pairId, false) { Timestamp = at, AttributionSource = AttributionSource.TestContext },
            new(scenarioId, scenarioId, HttpMethod.Post, "{}", new Uri("http://localhost/orders"), [], service, "Test",
                RequestResponseType.Response, traceId, pairId, false, HttpStatusCode.Created) { Timestamp = at.AddMilliseconds(20), AttributionSource = AttributionSource.TestContext }
        ];

        var json = ReportGenerator.GenerateMergeableReportJson(
            [new Feature { DisplayName = service, Scenarios = [new Scenario { Id = scenarioId, DisplayName = scenario, Result = ExecutionResult.Passed }] }],
            start, start.AddMinutes(1),
            new[] { new DiagramAsCode(scenarioId, "", $"@startuml\nTest -> {service} : POST /orders\n@enduml") }.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            [new ComponentRelationship("Test", service, "HTTP", new HashSet<string> { "POST /orders" }, 1, 1, "http")],
            internalFlowSegmentData: null, wholeTestFlow: null, WholeTestFlowVisualization.None, ciMetadata: null,
            diagnostics: null, trackedLogs: logs, compressPayloads: false);
        File.WriteAllText(Path.Combine(_directory, name), json);
    }

    [Fact]
    public void A_merge_writes_the_card_of_its_merged_data_file()
    {
        WriteShard("runner1.json", "a1", "Order pancakes", "Breakfast Provider");
        WriteShard("runner2.json", "b1", "Order waffles", "Breakfast Provider");

        var error = new StringWriter();
        Assert.True(MergeCommand.Run([_directory, "-o", Path.Combine(_directory, "Combined.html")], new StringWriter(), error) == 0, error.ToString());

        var card = Path.Combine(_directory, "service.json");
        Assert.True(File.Exists(card), "no service.json beside the merged report");
        var output = new StringWriter();
        Assert.True(CardCommand.Run([Path.Combine(_directory, "Combined.json")], output, error) == 0, error.ToString());
        Assert.Equal(output.ToString(), File.ReadAllText(card));

        using var json = JsonDocument.Parse(File.ReadAllText(card));
        Assert.Equal(2, json.RootElement.GetProperty("behaviour").GetProperty("scenarios").GetArrayLength());
        Assert.Equal("Combined.json", json.RootElement.GetProperty("run").GetProperty("report").GetProperty("file").GetString());
    }
}
