using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Reports.Merge;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// 5.9.1 (plans/REPORT_UI_FIXES_PLAN.md N3): the data file carries <see cref="Feature.StableIdName"/> as
/// <c>stableIdName</c>, beside the feature's name and only where it differs from it, so <c>kronikol merge</c> computes the
/// ids the shard wrote for a feature shown as written (a Gherkin title), whose ids key on the name 5.9.0 showed.
/// </summary>
[Collection("DiagramsFetcher")]
public class FeatureStableIdNameMergeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-merge-stable-id-name-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Start = new(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);

    public FeatureStableIdNameMergeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    // a Gherkin feature as the Reqnroll lane writes it from 5.9.1, and one whose name keys its ids as it is
    private static Feature[] Shard() =>
    [
        new Feature
        {
            DisplayName = "Month's reports via gRPC",
            StableIdName = "Month S Reports Via G RPC",
            Scenarios = [new Scenario { Id = "m1", DisplayName = "A report is sent", Result = ExecutionResult.Passed }],
        },
        new Feature
        {
            DisplayName = "Orders",
            StableIdName = "Orders",
            Scenarios = [new Scenario { Id = "o1", DisplayName = "An order is taken", Result = ExecutionResult.Passed }],
        },
    ];

    private static string Json(Feature[] features) => ReportGenerator.GenerateMergeableReportJson(features, Start, Start.AddSeconds(9),
        diagramLookup: null, componentRelationships: null, internalFlowSegmentData: null, wholeTestFlow: null,
        WholeTestFlowVisualization.None, ciMetadata: null);

    private static Dictionary<string, JsonElement> FeaturesByName(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("features").EnumerateArray()
            .ToDictionary(f => f.GetProperty("name").GetString()!, f => f.Clone());
    }

    private static string StableId(string json, string feature) =>
        FeaturesByName(json)[feature].GetProperty("scenarios")[0].GetProperty("stableId").GetString()!;

    private string Write(string name, string json)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void The_data_file_writes_the_name_the_ids_key_on_only_where_it_is_not_the_name()
    {
        var features = FeaturesByName(Json(Shard()));

        Assert.Equal("Month S Reports Via G RPC", features["Month's reports via gRPC"].GetProperty("stableIdName").GetString());
        Assert.False(features["Orders"].TryGetProperty("stableIdName", out _));
        // beside the name, as keyedByOutline stands beside outlineId
        Assert.Equal(["name", "stableIdName"], features["Month's reports via gRPC"].EnumerateObject().Take(2).Select(p => p.Name));
    }

    [Fact]
    public void A_feature_shown_as_written_keeps_the_ids_its_old_name_gave_its_scenarios()
    {
        var scenario = Shard()[0].Scenarios[0];
        Assert.Equal(ScenarioStableId.For(null, "Month S Reports Via G RPC", scenario), StableId(Json(Shard()), "Month's reports via gRPC"));
    }

    [Fact]
    public void A_merged_report_keeps_a_name_shown_as_written_and_its_scenarios_ids()
    {
        var shard = Json(Shard());

        var merged = MergeableReportRenderer.MergeFiles([Write("shard.json", shard)]);

        var feature = merged.Features.Single(f => f.DisplayName == "Month's reports via gRPC");
        Assert.Equal("Month S Reports Via G RPC", feature.StableIdName);
        Assert.Equal(StableId(shard, "Month's reports via gRPC"), StableId(Json(merged.Features), "Month's reports via gRPC"));
    }
}
