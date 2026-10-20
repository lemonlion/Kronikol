using System.Text.Json;
using System.Text.Json.Nodes;
using Kronikol.Query;
using Kronikol.Reports;

namespace Kronikol.Tests.Tool;

/// <summary>
/// What <c>kronikol query</c> reads of a scenario's tags (plans/TAGS_PLAN.md §4.13): its effective tags, its feature's
/// first, from a file's <c>tags</c> or, in a file written before 5.0.0, from its <c>labels</c> and <c>categories</c>
/// less the <c>retry N</c> labels its attempt accounts for. <c>--label</c> had no fact before these (F20).
/// </summary>
public class QueryTagsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-query-tags").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static Feature[] Shop() =>
    [
        new()
        {
            DisplayName = "Checkout", Tags = ["checkout", "Smoke"],
            Scenarios =
            [
                new Scenario { Id = "pay", DisplayName = "Pay", Result = ExecutionResult.Passed, Tags = ["smoke", "payments"] },
                new Scenario { Id = "refuse", DisplayName = "Refuse", Result = ExecutionResult.Failed, ErrorMessage = "boom" }
            ]
        },
        new() { DisplayName = "Browse", Scenarios = [new Scenario { Id = "look", DisplayName = "Look around", Result = ExecutionResult.Passed }] }
    ];

    private string Written(Feature[] features)
    {
        var written = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(1),
            $"QueryTags_{Guid.NewGuid():N}.json", DataFormat.Json, null, []);
        var path = Path.Combine(_directory, "TestRunReport.json");
        File.Move(written, path, overwrite: true);
        return path;
    }

    private static Dictionary<string, string?[]> TagsByScenario(string path, params string[] more)
    {
        using var json = JsonDocument.Parse(Query(["scenarios", path, "--json", .. more]));
        return json.RootElement.GetProperty("items").EnumerateArray().ToDictionary(
            i => i.GetProperty("scenario").GetString()!,
            i => i.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray());
    }

    [Fact]
    public void Scenarios_json_carries_each_scenario_s_effective_tags_the_feature_s_first()
    {
        var path = Written(Shop());

        var tags = TagsByScenario(path);

        Assert.Equal(["checkout", "Smoke", "payments"], tags["Pay"]);
        Assert.Equal(["checkout", "Smoke"], tags["Refuse"]);
        Assert.Empty(tags["Look around"]);
        using var json = JsonDocument.Parse(Query("scenarios", path, "--json"));
        var item = json.RootElement.GetProperty("items")[0];
        Assert.False(item.TryGetProperty("labels", out _));
        Assert.False(item.TryGetProperty("categories", out _));
    }

    [Fact]
    public void A_4x_file_s_labels_and_categories_read_as_one_tag_list_less_its_generated_retry_labels()
    {
        // what a 4.x writer wrote: the same report with labels and categories in place of tags, Pay on its third attempt
        var path = Written(Shop());
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        var feature = WithLabels(json["features"]!.AsArray(), "Checkout", "checkout");
        var pay = feature["scenarios"]!.AsArray().Single(s => (string?)s!["id"] == "pay")!.AsObject();
        pay.Remove("tags");
        pay["attempt"] = 3;
        pay["labels"] = new JsonArray("retry 1", "retry 2", "fast");
        pay["categories"] = new JsonArray("payments", "FAST");
        File.WriteAllText(path, json.ToJsonString());

        var tags = TagsByScenario(path);

        Assert.Equal(["checkout", "fast", "payments"], tags["Pay"]);
        Assert.Equal(["checkout"], tags["Refuse"]);
    }

    [Theory]
    [InlineData("pay", new[] { "Pay" })]
    // a feature's tag picks out every one of its scenarios
    [InlineData("CHECK", new[] { "Pay", "Refuse" })]
    [InlineData("nothing", new string[0])]
    public void Label_keeps_the_scenarios_with_an_effective_tag_holding_the_text_in_any_case(string text, string[] expected)
    {
        var path = Written(Shop());

        Assert.Equal(expected, TagsByScenario(path, "--label", text).Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Puts the named feature back as a 4.x writer wrote it: <c>labels</c> where 5.0.0 writes <c>tags</c>, so before its
    /// <c>scenarios</c>, where the one-pass scanner reads a feature's tags from (plans/TAGS_PLAN.md §4.12).
    /// </summary>
    internal static JsonObject WithLabels(JsonArray features, string name, params string[] labels)
    {
        var index = features.IndexOf(features.Single(f => (string?)f!["name"] == name));
        var rewritten = new JsonObject();
        foreach (var (key, value) in features[index]!.AsObject())
            rewritten[key == "tags" ? "labels" : key] = key == "tags"
                ? new JsonArray([.. labels.Select(l => (JsonNode?)l)])
                : value?.DeepClone();
        features[index] = rewritten;
        return rewritten;
    }

    private static string Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString().ReplaceLineEndings("\n");
    }
}
