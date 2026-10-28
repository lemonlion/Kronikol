using System.Text;
using System.Text.Json;
using Kronikol.Compression;
using Kronikol.Reports;
using Kronikol.Reports.Merge;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// The data file carries a scenario's recorded id key (<see cref="Scenario.StableIdKey"/>) as <c>stableIdName</c>, with
/// <c>stableIdOutlineId</c> and <c>stableIdExampleValues</c> where the key has them, and its lane's outline title as
/// <c>outlineTitle</c>: so <c>kronikol merge</c> computes the ids and the headings the shard wrote
/// (plans/TITLES_AS_WRITTEN_PLAN.md 4.3, 4.4).
/// </summary>
[Collection("DiagramsFetcher")]
public class RecordedKeyMergeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-merge-recorded-key-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Start = new(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc);

    public RecordedKeyMergeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A written xUnit fact name, "Ships to UK [EU]", as R1 shows it (whole, no values) and as 5.16.4 showed it, which read
    /// its brackets as a row's values; and a generated title the rule now reads with its capitals.
    /// </summary>
    private static Feature[] Shard(bool asR1) =>
    [
        new Feature
        {
            DisplayName = "Order Tests",
            Scenarios =
            [
                asR1
                    ? new Scenario
                    {
                        Id = "w", DisplayName = "Ships to UK [EU]", NameAsWritten = true, Result = ExecutionResult.Passed,
                        StableIdKey = new ScenarioIdKey("Ships to uk [eu]", "Ships to uk", new Dictionary<string, string> { ["arg0"] = "eu" }),
                    }
                    : new Scenario
                    {
                        Id = "w", DisplayName = "Ships to uk [eu]", OutlineId = "Ships to uk", Result = ExecutionResult.Passed,
                        ExampleValues = new() { ["arg0"] = "eu" },
                    },
                asR1
                    ? new Scenario
                    {
                        Id = "g", DisplayName = "Get HTTP response", Result = ExecutionResult.Passed,
                        StableIdKey = new ScenarioIdKey("Get http response", null, null),
                    }
                    : new Scenario { Id = "g", DisplayName = "Get http response", Result = ExecutionResult.Passed },
                new Scenario { Id = "p", DisplayName = "A plain scenario", Result = ExecutionResult.Passed },
            ],
        },
    ];

    private static string Json(Feature[] features) => ReportGenerator.GenerateMergeableReportJson(features, Start, Start.AddSeconds(9),
        diagramLookup: null, componentRelationships: null, internalFlowSegmentData: null, wholeTestFlow: null,
        WholeTestFlowVisualization.None, ciMetadata: null);

    private static Dictionary<string, JsonElement> ScenariosById(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .ToDictionary(s => s.GetProperty("id").GetString()!, s => s.Clone());
    }

    private string Write(string name, string json)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void The_data_file_writes_a_key_only_on_a_scenario_that_records_one()
    {
        var scenarios = ScenariosById(Json(Shard(asR1: true)));

        var written = scenarios["w"];
        Assert.Equal("Ships to uk [eu]", written.GetProperty("stableIdName").GetString());
        Assert.Equal("Ships to uk", written.GetProperty("stableIdOutlineId").GetString());
        Assert.Equal("eu", written.GetProperty("stableIdExampleValues").GetProperty("arg0").GetString());
        Assert.Equal(JsonValueKind.Null, written.GetProperty("outlineId").ValueKind);

        var generated = scenarios["g"];
        Assert.Equal("Get http response", generated.GetProperty("stableIdName").GetString());
        Assert.False(generated.TryGetProperty("stableIdOutlineId", out _));
        Assert.False(generated.TryGetProperty("stableIdExampleValues", out _));

        Assert.False(scenarios["p"].TryGetProperty("stableIdName", out _));
        Assert.False(ScenariosById(Json(Shard(asR1: false)))["w"].TryGetProperty("stableIdName", out _));
    }

    [Theory]
    [InlineData("w")]
    [InlineData("g")]
    [InlineData("p")]
    public void A_scenario_s_data_file_id_is_the_id_5_16_4_gave_it(string id) =>
        Assert.Equal(ScenariosById(Json(Shard(asR1: false)))[id].GetProperty("stableId").GetString(),
            ScenariosById(Json(Shard(asR1: true)))[id].GetProperty("stableId").GetString());

    // A data file is compressed by default, and the compressed file drops a null and an empty object: a key with no
    // outline or values reads back as none, where the merge read the scenario's own and computed another id
    [Fact]
    public void A_key_with_no_outline_or_values_reads_back_as_none_from_a_compressed_file()
    {
        Feature[] shard =
        [
            new Feature
            {
                DisplayName = "Order Tests",
                Scenarios =
                [
                    // an xUnit v3 row with the label "v1.2": 5.16.4 cut its name at the label's dot and read no values
                    new Scenario
                    {
                        Id = "l", DisplayName = "Ships [v1.2]", OutlineId = "Ships", ExampleValues = new() { ["arg0"] = "v1.2" },
                        Result = ExecutionResult.Passed, StableIdKey = new ScenarioIdKey("2]", null, null),
                    },
                ],
            },
        ];
        using var pruned = new MemoryStream();
        using (var pruner = new ReportJsonPruner(pruned, RequiredKeys.None))
        {
            var bytes = Encoding.UTF8.GetBytes(Json(shard));
            pruner.Write(bytes, 0, bytes.Length);
            pruner.Complete();
        }

        var merged = MergeableReportRenderer.MergeFiles([Write("pruned.json", Encoding.UTF8.GetString(pruned.ToArray()))]);

        var feature = merged.Features.Single();
        Assert.Equal(new ScenarioIdKey("2]", null, null), feature.Scenarios.Single().StableIdKey);
        Assert.Equal(ScenarioStableId.Compute(null, "Order Tests", "2]"), ScenarioStableId.For(null, feature, feature.Scenarios.Single()));
    }

    [Fact]
    public void The_merge_reads_the_key_back_and_writes_the_shard_s_ids()
    {
        var shard = Json(Shard(asR1: true));

        var merged = MergeableReportRenderer.MergeFiles([Write("r1.json", shard)]);

        var written = merged.Features.SelectMany(f => f.Scenarios).Single(s => s.Id == "w");
        Assert.Equal(new ScenarioIdKey("Ships to uk [eu]", "Ships to uk", written.StableIdKey!.ExampleValues), written.StableIdKey);
        Assert.Equal("eu", written.StableIdKey.ExampleValues!["arg0"]);
        var again = ScenariosById(Json(merged.Features));
        foreach (var id in new[] { "w", "g", "p" })
            Assert.Equal(ScenariosById(shard)[id].GetProperty("stableId").GetString(), again[id].GetProperty("stableId").GetString());
    }

    [Fact]
    public void A_scenario_from_a_shard_written_before_the_rule_and_one_written_after_are_one_scenario()
    {
        var merged = MergeableReportRenderer.MergeFiles(
            [Write("before.json", Json(Shard(asR1: false))), Write("after.json", Json(Shard(asR1: true)))]);

        Assert.Equal(3, merged.Features.SelectMany(f => f.Scenarios).Count());
    }

    [Fact]
    public void The_outline_title_reaches_a_merged_report_where_the_rows_are_named_apart()
    {
        Scenario Row(string id, string name) => new()
        {
            Id = id, DisplayName = name, OutlineId = "PlacesAnOrder", OutlineTitle = "Places an order", NameAsWritten = true,
            ExampleValues = new() { ["region"] = id }, Result = ExecutionResult.Passed,
        };
        var shard = Json([new Feature { DisplayName = "Orders", Scenarios = [Row("uk", "Happy path UK"), Row("us", "Happy path US")] }]);

        Assert.Equal("Places an order", ScenariosById(shard)["uk"].GetProperty("outlineTitle").GetString());
        var merged = MergeableReportRenderer.MergeFiles([Write("rows.json", shard)]);
        var rows = merged.Features.Single().Scenarios;
        Assert.All(rows, r => Assert.Equal("Places an order", r.OutlineTitle));
        Assert.Equal("Places an order", Assert.Single(ParameterGrouper.Analyze(rows).Groups).GroupDisplayName);
    }

    [Fact]
    public void A_merge_orders_its_features_as_a_single_report_does()
    {
        // B6: an ordinal sort put "gRPC calls" after "Orders", where the report's own order (and each shard's) puts it first
        Feature Named(string name) => new() { DisplayName = name, Scenarios = [new Scenario { Id = name, DisplayName = "Runs", Result = ExecutionResult.Passed }] };

        var merged = MergeableReportRenderer.MergeFiles(
            [Write("a.json", Json([Named("Orders"), Named("gRPC calls")])), Write("b.json", Json([Named("Get HTTP response")]))]);

        Assert.Equal(["Get HTTP response", "gRPC calls", "Orders"], merged.Features.Select(f => f.DisplayName));
    }

    [Fact]
    public void No_outline_title_is_written_where_the_row_s_own_name_says_it()
    {
        var row = new Scenario
        {
            Id = "r", DisplayName = "Ships to the UK [country: GB]", OutlineId = "ShipsToTheUK", OutlineTitle = "Ships to the UK",
            ExampleValues = new() { ["country"] = "GB" }, Result = ExecutionResult.Passed,
        };

        Assert.False(ScenariosById(Json([new Feature { DisplayName = "Orders", Scenarios = [row] }]))["r"].TryGetProperty("outlineTitle", out _));
    }
}
