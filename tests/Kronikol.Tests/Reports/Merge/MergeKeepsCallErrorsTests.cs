using System.Net;
using System.Text.Json;
using Kronikol.ComponentDiagram;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// <c>plans/INGEST_FIDELITY_PLAN.md</c> S2 (F11, T15): <c>kronikol merge</c> read neither a call's <c>error</c> nor how it
/// got its scenario (<c>attributionSource</c>), so a merged report wrote nulls where every shard had them; and it read
/// no shard's <c>background</c> block at all, so the calls that belonged to no scenario, and the <c>expiredFrom</c> that
/// says which scenario an expired call inherited, were gone from every merged report.
/// </summary>
public class MergeKeepsCallErrorsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kronikol-merge-errors-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Start = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);

    public MergeKeepsCallErrorsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void A_merge_keeps_a_failed_sends_error_how_each_call_got_its_scenario_and_the_background_calls()
    {
        WriteShard("runner1.json", "a1", "Orders");
        WriteShard("runner2.json", "b1", "Payments");

        using var merged = Merge();
        var root = merged.RootElement;

        foreach (var (id, service) in new[] { ("a1", "Orders"), ("b1", "Payments") })
        {
            var calls = Scenario(root, id).GetProperty("httpInteractions").EnumerateArray().ToArray();
            Assert.Equal("TestContext", calls[0].GetProperty("attributionSource").GetString());
            Assert.Equal("!HttpRequestException", calls[1].GetProperty("statusText").GetString());
            Assert.Equal($"{service} said no Caused by: connection reset", calls[1].GetProperty("error").GetString());
        }

        var background = root.GetProperty("background");
        var expired = background.GetProperty("interactions").EnumerateArray()
            .Where(i => i.GetProperty("type").GetString() == "Request").ToArray();
        Assert.Equal(["a1", "b1"], expired.Select(i => i.GetProperty("expiredFrom").GetString()).Order());
        Assert.All(expired, i => Assert.Equal("Expired", i.GetProperty("attributionSource").GetString()));
        Assert.Equal(["a1", "b1"], background.GetProperty("afterScenarioEnd").EnumerateArray()
            .Select(g => g.GetProperty("scenarioId").GetString()).Order());
    }

    /// <summary>
    /// One scenario with a call made under the test framework's context that threw, and a call that inherited the
    /// scenario's context after it ended, which the report moves to the background block.
    /// </summary>
    private void WriteShard(string name, string scenarioId, string service)
    {
        var at = new DateTimeOffset(Start).AddSeconds(1);
        var uri = new Uri($"http://{service.ToLowerInvariant()}/x");
        var failure = new HttpRequestException($"{service} said no", new IOException("connection reset"));
        var (trace, pair, lateTrace, latePair) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        RequestResponseLog[] logs =
        [
            new(scenarioId, scenarioId, HttpMethod.Post, "{}", uri, [], service, "Test", RequestResponseType.Request, trace, pair, false)
                { Timestamp = at, AttributionSource = AttributionSource.TestContext },
            new(scenarioId, scenarioId, HttpMethod.Post, null, uri, [], service, "Test", RequestResponseType.Response, trace, pair, false, FailedSend.Status(failure))
                { Timestamp = at.AddMilliseconds(20), AttributionSource = AttributionSource.TestContext, Error = FailedSend.Describe(failure) },
            new(TestIdentityScope.UnknownTestName, TestIdentityScope.UnknownTestId, HttpMethod.Get, null, uri, [], service, "Worker", RequestResponseType.Request, lateTrace, latePair, false)
                { Timestamp = at.AddSeconds(30), AttributionSource = AttributionSource.Expired, ExpiredFromTestId = scenarioId },
            new(TestIdentityScope.UnknownTestName, TestIdentityScope.UnknownTestId, HttpMethod.Get, "{}", uri, [], service, "Worker", RequestResponseType.Response, lateTrace, latePair, false, HttpStatusCode.OK)
                { Timestamp = at.AddSeconds(30.02), AttributionSource = AttributionSource.Expired, ExpiredFromTestId = scenarioId },
        ];

        var json = ReportGenerator.GenerateMergeableReportJson(
            [new Feature { DisplayName = service, Scenarios = [new Scenario { Id = scenarioId, DisplayName = "Call " + service, Result = ExecutionResult.Failed, EndedAt = at.AddSeconds(1) }] }],
            Start, Start.AddMinutes(1),
            new[] { new DiagramAsCode(scenarioId, "", $"@startuml\nTest -> {service} : POST /x\n@enduml") }.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            [new ComponentRelationship("Test", service, "HTTP", new HashSet<string> { "POST /x" }, 1, 1, "http")],
            internalFlowSegmentData: null, wholeTestFlow: null, WholeTestFlowVisualization.None, ciMetadata: null,
            diagnostics: null, trackedLogs: logs);
        File.WriteAllText(Path.Combine(_directory, name), json);

        // The shard itself holds what the merge has to keep.
        using var shard = JsonDocument.Parse(json);
        Assert.Single(shard.RootElement.GetProperty("background").GetProperty("interactions").EnumerateArray(),
            i => i.GetProperty("type").GetString() == "Request");
    }

    private JsonDocument Merge()
    {
        var error = new StringWriter();
        var exit = MergeCommand.Run([_directory, "-o", Path.Combine(_directory, "Combined.html")], new StringWriter(), error);
        Assert.True(exit == 0, error.ToString());
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "Combined.json")));
    }

    private static JsonElement Scenario(JsonElement root, string id) =>
        root.GetProperty("features").EnumerateArray().SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .Single(s => s.GetProperty("id").GetString() == id);
}
