using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// A shard written with <c>GenerateMergeableData</c> carries its traffic whatever the internal-flow
/// setting. The mergeable branch of the generator handed the writer the internal-flow log set, which is
/// null when <c>InternalFlowTracking</c> is off, so a shard written that way had no <c>httpInteractions</c>
/// at all: the gap 3.8.0 closed for the standard data file, kept by the other branch.
/// </summary>
[Collection("DiagramsFetcher")]
public class MergeableShardTrafficTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);

    [Fact]
    public void A_shard_written_without_internal_flow_tracking_still_carries_its_interactions_and_their_step_paths()
    {
        var testId = "shard-traffic-" + Guid.NewGuid().ToString("N");
        var pair = Guid.NewGuid();
        RequestResponseLogger.Log(new RequestResponseLog(testId, testId, "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        { IsOverrideStart = true, MarkerKind = DiagramMarkerKind.Step, PlantUml = "\nhnote across <<stepDelimiter>> #black:<color:white>Given a cart\n\n", Timestamp = Start });
        RequestResponseLogger.Log(new RequestResponseLog("S", testId, HttpMethod.Get, null, new Uri("http://pricing/quote"), [], "Pricing", "Tests",
            RequestResponseType.Request, pair, pair, false) { Timestamp = Start.AddSeconds(1) });
        RequestResponseLogger.Log(new RequestResponseLog("S", testId, HttpMethod.Get, "{}", new Uri("http://pricing/quote"), [], "Pricing", "Tests",
            RequestResponseType.Response, pair, pair, false, System.Net.HttpStatusCode.OK) { Timestamp = Start.AddSeconds(2) });
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Checkout",
                Scenarios = [new Scenario { Id = testId, DisplayName = "S", Steps = [new ScenarioStep { Keyword = "Given", Text = "a cart" }] }],
            },
        ];
        var directory = Path.Combine(Path.GetTempPath(), "kronikol-shard-traffic-" + Guid.NewGuid().ToString("N"));
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = directory,
            PlantUmlRendering = PlantUmlRendering.BrowserJs,
            InternalFlowTracking = false,
            GenerateMergeableData = true,
            GenerateComponentDiagram = false,
            WriteRunSummaryToConsole = false,
        };

        try
        {
            DefaultDiagramsFetcher.Reset();
            ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(features, Start, End, options, RunEnvironment.Unrecorded, Environment.GetEnvironmentVariable);
            DefaultDiagramsFetcher.Reset();

            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "TestRunReport.json")));
            Assert.True(json.RootElement.TryGetProperty("mergeableFormatVersion", out _), "not a mergeable file");
            var scenario = json.RootElement.GetProperty("features").EnumerateArray()
                .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
                .Single(s => s.GetProperty("id").GetString() == testId);
            var interactions = scenario.GetProperty("httpInteractions").EnumerateArray().ToArray();
            Assert.Equal(2, interactions.Length);
            Assert.All(interactions, i => Assert.Equal("0", i.GetProperty("stepPath").GetString()));
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }
}
