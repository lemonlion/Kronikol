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
    /// The merged report lists the calls of no scenario as a run's report does (plans/WARM_UP_PLAN.md section 8). Until
    /// 4.7.3 the merged data file carried them and its HTML had no Background calls section at all.
    /// </summary>
    [Fact]
    public void A_merged_report_draws_the_background_calls_section()
    {
        WriteShard("runner1.json", "a1", "Orders");
        WriteShard("runner2.json", "b1", "Payments");

        Merge().Dispose();
        var html = ReportHtml.Read(Path.Combine(_directory, "Combined.html"));

        Assert.Contains("<details class=\"background-calls\">", html, StringComparison.Ordinal);
        Assert.Contains("<td>/x</td>", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two shards whose scenarios share a runtime id: the merge renames the second shard's, and an expired call keeps
    /// naming its own shard's scenario (plans/WARM_UP_PLAN.md section 8). Until 4.7.3 the rename reached the calls'
    /// <c>TestId</c> and not their <c>expiredFrom</c>, so the second shard's late call was counted against the first
    /// shard's scenario.
    /// </summary>
    [Fact]
    public void An_expired_call_follows_its_scenario_when_the_merge_renames_it()
    {
        WriteShard("runner1.json", "a1", "Orders", duration: TimeSpan.FromSeconds(1));
        WriteShard("runner2.json", "a1", "Orders", duration: TimeSpan.FromSeconds(2));

        using var merged = Merge();
        var root = merged.RootElement;

        var ids = root.GetProperty("features").EnumerateArray().SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .Select(s => s.GetProperty("id").GetString()).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["a1", "a1#1"], ids);
        var groups = root.GetProperty("background").GetProperty("afterScenarioEnd").EnumerateArray()
            .Select(g => (g.GetProperty("scenarioId").GetString(), g.GetProperty("calls").GetInt32()))
            .OrderBy(g => g.Item1, StringComparer.Ordinal).ToArray();
        Assert.Equal([("a1", 1), ("a1#1", 1)], groups);
        var expiredFrom = root.GetProperty("background").GetProperty("interactions").EnumerateArray()
            .Where(i => i.GetProperty("type").GetString() == "Request")
            .Select(i => i.GetProperty("expiredFrom").GetString()).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["a1", "a1#1"], expiredFrom);
    }

    /// <summary>
    /// A shard given to the merge a second time beside a scenario of its own, as yesterday's merged file is beside a
    /// shard re-run since: the merge dropped the copy's calls, and kept its expired calls, which sit under no scenario,
    /// so the background block listed each of them twice. Until 4.16.3.
    /// </summary>
    [Fact]
    public void A_copied_scenarios_expired_calls_are_kept_once()
    {
        WriteShard("runner1.json", null, ("a1", "Orders"));
        WriteShard("runner2-with-runner1-again.json", null, ("a1", "Orders"), ("b1", "Payments"));

        using var merged = Merge();
        var root = merged.RootElement;

        Assert.Contains(root.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("message").GetString()!.Contains("supplied twice", StringComparison.Ordinal));
        var background = root.GetProperty("background");
        var expiredFrom = background.GetProperty("interactions").EnumerateArray()
            .Where(i => i.GetProperty("type").GetString() == "Request")
            .Select(i => i.GetProperty("expiredFrom").GetString()).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["a1", "b1"], expiredFrom);
        var groups = background.GetProperty("afterScenarioEnd").EnumerateArray()
            .Select(g => (g.GetProperty("scenarioId").GetString(), g.GetProperty("calls").GetInt32()))
            .OrderBy(g => g.Item1, StringComparer.Ordinal).ToArray();
        Assert.Equal([("a1", 1), ("b1", 1)], groups);
    }

    private void WriteShard(string name, string scenarioId, string service, TimeSpan? duration = null) =>
        WriteShard(name, duration, (scenarioId, service));

    /// <summary>
    /// A scenario a service, each with a call made under the test framework's context that threw, and a call that
    /// inherited the scenario's context after it ended, which the report moves to the background block.
    /// </summary>
    private void WriteShard(string name, TimeSpan? duration, params (string Id, string Service)[] scenarios)
    {
        var at = new DateTimeOffset(Start).AddSeconds(1);
        var logs = new List<RequestResponseLog>();
        foreach (var (scenarioId, service) in scenarios)
        {
            var uri = new Uri($"http://{service.ToLowerInvariant()}/x");
            var failure = new HttpRequestException($"{service} said no", new IOException("connection reset"));
            var (trace, pair, lateTrace, latePair) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            logs.AddRange(
            [
                new(scenarioId, scenarioId, HttpMethod.Post, "{}", uri, [], service, "Test", RequestResponseType.Request, trace, pair, false)
                    { Timestamp = at, AttributionSource = AttributionSource.TestContext },
                new(scenarioId, scenarioId, HttpMethod.Post, null, uri, [], service, "Test", RequestResponseType.Response, trace, pair, false, FailedSend.Status(failure))
                    { Timestamp = at.AddMilliseconds(20), AttributionSource = AttributionSource.TestContext, Error = FailedSend.Describe(failure) },
                new(TestIdentityScope.UnknownTestName, TestIdentityScope.UnknownTestId, HttpMethod.Get, null, uri, [], service, "Worker", RequestResponseType.Request, lateTrace, latePair, false)
                    { Timestamp = at.AddSeconds(30), AttributionSource = AttributionSource.Expired, ExpiredFromTestId = scenarioId },
                new(TestIdentityScope.UnknownTestName, TestIdentityScope.UnknownTestId, HttpMethod.Get, "{}", uri, [], service, "Worker", RequestResponseType.Response, lateTrace, latePair, false, HttpStatusCode.OK)
                    { Timestamp = at.AddSeconds(30.02), AttributionSource = AttributionSource.Expired, ExpiredFromTestId = scenarioId },
            ]);
        }

        var json = ReportGenerator.GenerateMergeableReportJson(
            scenarios.Select(s => new Feature
            {
                DisplayName = s.Service,
                Scenarios = [new Scenario { Id = s.Id, DisplayName = "Call " + s.Service, Result = ExecutionResult.Failed, EndedAt = at.AddSeconds(1), Duration = duration }]
            }).ToArray(),
            Start, Start.AddMinutes(1),
            scenarios.Select(s => new DiagramAsCode(s.Id, "", $"@startuml\nTest -> {s.Service} : POST /x\n@enduml")).ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            scenarios.Select(s => new ComponentRelationship("Test", s.Service, "HTTP", new HashSet<string> { "POST /x" }, 1, 1, "http")).ToArray(),
            internalFlowSegmentData: null, wholeTestFlow: null, WholeTestFlowVisualization.None, ciMetadata: null,
            diagnostics: null, trackedLogs: logs.ToArray());
        File.WriteAllText(Path.Combine(_directory, name), json);

        // The shard itself holds what the merge has to keep.
        using var shard = JsonDocument.Parse(json);
        Assert.Equal(scenarios.Length, shard.RootElement.GetProperty("background").GetProperty("interactions").EnumerateArray()
            .Count(i => i.GetProperty("type").GetString() == "Request"));
    }

    private JsonDocument Merge()
    {
        var error = new StringWriter();
        var exit = MergeCommand.Run([_directory, "-o", Path.Combine(_directory, "Combined.html")], new StringWriter(), error);
        Assert.True(exit == 0, error.ToString());
        return JsonDocument.Parse(ReportHtml.Read(Path.Combine(_directory, "Combined.json")));
    }

    private static JsonElement Scenario(JsonElement root, string id) =>
        root.GetProperty("features").EnumerateArray().SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .Single(s => s.GetProperty("id").GetString() == id);
}
