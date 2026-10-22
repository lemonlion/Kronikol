using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Reports.Merge;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// #101, fact K4: the data file carries <see cref="Scenario.KeyedByOutline"/> as <c>keyedByOutline</c>, so
/// <c>kronikol merge</c> computes the ids the shard wrote, and a row from a shard written before its name was filled
/// and the same row from one written after are one scenario (plans/OUTLINE_ROW_NAMES_PLAN.md 3.3 and 3.4).
/// </summary>
[Collection("DiagramsFetcher")]
public class OutlineRowMergeTests : IDisposable
{
    private const string Title = "A basket is weighed on the UK scale (<case>)";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-merge-outline-row-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Start = new(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

    public OutlineRowMergeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>The basket row as the Reqnroll lane writes it: filled and marked, or (5.4.2 and earlier) by its title.</summary>
    private static Scenario BasketRow(bool filled) => new()
    {
        Id = "s2",
        DisplayName = filled ? "A basket is weighed on the UK scale (two apples)" : Title,
        KeyedByOutline = filled,
        OutlineId = Title,
        ExampleValues = new() { ["case"] = "two apples", ["count"] = "2", ["grams"] = "301" },
        Result = ExecutionResult.Passed,
        Duration = TimeSpan.FromMilliseconds(16),
    };

    private static Feature[] Shard(bool filled) =>
    [
        new Feature
        {
            DisplayName = "Baskets",
            Scenarios = [BasketRow(filled), new Scenario { Id = "p", DisplayName = "A plain scenario", Result = ExecutionResult.Passed }],
        },
    ];

    private static string Json(Feature[] features) => ReportGenerator.GenerateMergeableReportJson(features, Start, Start.AddSeconds(9),
        diagramLookup: null, componentRelationships: null, internalFlowSegmentData: null, wholeTestFlow: null,
        WholeTestFlowVisualization.None, ciMetadata: null);

    private static Dictionary<string, JsonElement> ScenariosByName(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .ToDictionary(s => s.GetProperty("name").GetString()!, s => s.Clone());
    }

    private string Write(string name, string json)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void The_data_file_marks_a_filled_row_and_no_other()
    {
        var scenarios = ScenariosByName(Json(Shard(filled: true)));

        Assert.True(scenarios["A basket is weighed on the UK scale (two apples)"].GetProperty("keyedByOutline").GetBoolean());
        Assert.False(scenarios["A plain scenario"].TryGetProperty("keyedByOutline", out _));
        Assert.False(ScenariosByName(Json(Shard(filled: false)))[Title].TryGetProperty("keyedByOutline", out _));
    }

    [Fact]
    public void A_filled_row_s_data_file_id_is_the_id_its_title_gave_it()
    {
        Assert.Equal(
            ScenariosByName(Json(Shard(filled: false)))[Title].GetProperty("stableId").GetString(),
            ScenariosByName(Json(Shard(filled: true)))["A basket is weighed on the UK scale (two apples)"].GetProperty("stableId").GetString());
    }

    [Fact]
    public void The_merge_reads_the_mark_back_and_writes_the_shard_s_ids()
    {
        var shard = Json(Shard(filled: true));

        var merged = MergeableReportRenderer.MergeFiles([Write("browser.json", shard)]);

        var row = merged.Features.SelectMany(f => f.Scenarios).Single(s => s.Id == "s2");
        Assert.True(row.KeyedByOutline);
        Assert.Equal(ScenariosByName(shard)["A basket is weighed on the UK scale (two apples)"].GetProperty("stableId").GetString(),
            ScenariosByName(Json(merged.Features))["A basket is weighed on the UK scale (two apples)"].GetProperty("stableId").GetString());
    }

    [Fact]
    public void A_row_from_a_shard_written_before_its_name_was_filled_and_one_written_after_are_one_scenario()
    {
        // the same run in both shards: what a scenario is decides that the second copy adds nothing
        var merged = MergeableReportRenderer.MergeFiles(
            [Write("before.json", Json(Shard(filled: false))), Write("after.json", Json(Shard(filled: true)))]);

        Assert.Single(merged.Features.SelectMany(f => f.Scenarios), s => s.OutlineId == Title);
    }
}
