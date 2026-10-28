using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Reports.Merge;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// <c>kronikol merge</c> pairs two features whose lanes made their names by the name their ids key on, which the data file
/// marks (<c>nameGenerated</c>), so two shards that spell one differently, one with <c>TitleWords</c> and one without,
/// are one feature; and every other feature by its name, as before (plans/TITLES_AS_WRITTEN_PLAN.md 4.5).
/// </summary>
[Collection("DiagramsFetcher")]
public class TitleWordsMergeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-merge-title-words-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Start = new(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc);

    public TitleWordsMergeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private static string Json(params Feature[] features) => ReportGenerator.GenerateMergeableReportJson(features, Start, Start.AddSeconds(9),
        diagramLookup: null, componentRelationships: null, internalFlowSegmentData: null, wholeTestFlow: null,
        WholeTestFlowVisualization.None, ciMetadata: null);

    private string Write(string name, string json)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    private static Feature Feature(string name, string scenarioId, bool generated, string? stableIdName = null) => new()
    {
        DisplayName = name,
        StableIdName = stableIdName,
        NameGenerated = generated,
        Scenarios = [new Scenario { Id = scenarioId, DisplayName = "Runs " + scenarioId, NameGenerated = generated, Result = ExecutionResult.Passed }],
    };

    private static Dictionary<string, string> StableIds(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .ToDictionary(s => s.GetProperty("id").GetString()!, s => s.GetProperty("stableId").GetString()!);
    }

    [Fact]
    public void A_feature_two_shards_spell_differently_is_one_feature_when_its_lane_made_its_name()
    {
        var withList = Json(Feature("AcmePay Webhook Tests", "a", generated: true, stableIdName: "Acme Pay Webhook Tests"));
        var without = Json(Feature("Acme Pay Webhook Tests", "b", generated: true));

        var merged = MergeableReportRenderer.MergeFiles([Write("with.json", withList), Write("without.json", without)]);

        var feature = Assert.Single(merged.Features);
        Assert.Equal("AcmePay Webhook Tests", feature.DisplayName);
        Assert.Equal(["a", "b"], feature.Scenarios.Select(s => s.Id));
        var ids = StableIds(Json(merged.Features));
        Assert.Equal(StableIds(withList)["a"], ids["a"]);
        Assert.Equal(StableIds(without)["b"], ids["b"]);
    }

    [Fact]
    public void The_shard_without_the_list_may_come_first()
    {
        var merged = MergeableReportRenderer.MergeFiles(
        [
            Write("without.json", Json(Feature("Acme Pay Webhook Tests", "b", generated: true))),
            Write("with.json", Json(Feature("AcmePay Webhook Tests", "a", generated: true, stableIdName: "Acme Pay Webhook Tests"))),
        ]);

        Assert.Equal(["b", "a"], Assert.Single(merged.Features).Scenarios.Select(s => s.Id));
    }

    [Fact]
    public void Two_Gherkin_features_that_share_a_key_stay_two_features()
    {
        // a Reqnroll lane keys both on 5.9.0's titleized name, "Order Flow"
        var merged = MergeableReportRenderer.MergeFiles(
        [
            Write("lower.json", Json(Feature("order flow", "a", generated: false, stableIdName: "Order Flow"))),
            Write("upper.json", Json(Feature("Order Flow", "b", generated: false, stableIdName: "Order Flow"))),
        ]);

        Assert.Equal(2, merged.Features.Length);
        Assert.All(merged.Features, f => Assert.Single(f.Scenarios));
    }

    [Fact]
    public void A_shard_written_before_the_mark_pairs_by_name_as_before()
    {
        var merged = MergeableReportRenderer.MergeFiles(
        [
            Write("marked.json", Json(Feature("Order Flow", "a", generated: true), Feature("AcmePay Tests", "c", generated: true, stableIdName: "Acme Pay Tests"))),
            Write("unmarked.json", Json(Feature("Order Flow", "b", generated: false), Feature("Acme Pay Tests", "d", generated: false))),
        ]);

        Assert.Equal(["a", "b"], merged.Features.Single(f => f.DisplayName == "Order Flow").Scenarios.Select(s => s.Id));
        Assert.Equal(3, merged.Features.Length);
        Assert.False(merged.Features.Single(f => f.DisplayName == "Order Flow").NameGenerated);
        Assert.True(merged.Features.Single(f => f.DisplayName == "AcmePay Tests").NameGenerated);
    }
}
