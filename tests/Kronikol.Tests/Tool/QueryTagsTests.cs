using System.Text.Json;
using System.Text.Json.Nodes;
using Kronikol.Query;
using Kronikol.Reports;

namespace Kronikol.Tests.Tool;

/// <summary>
/// What <c>kronikol query</c> reads of a scenario's tags (plans/TAGS_PLAN.md §4.13): its effective tags, its feature's
/// first, from a file's <c>tags</c> or, in a file written before 5.0.0, from its <c>labels</c> and <c>categories</c>
/// less the <c>retry N</c> labels its attempt accounts for. <c>--tag</c> keeps the scenarios one of those holds a text
/// in, and <c>--label</c>, its name before 5.0.0, is the same flag (Q2); <c>--label</c> had no fact before these (F20).
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
        var json = JsonNode.Parse(ReportHtml.Read(path))!;
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
    [InlineData("--tag", "pay", new[] { "Pay" })]
    // a feature's tag picks out every one of its scenarios
    [InlineData("--tag", "CHECK", new[] { "Pay", "Refuse" })]
    [InlineData("--tag", "nothing", new string[0])]
    // --label is the same flag, by its name before 5.0.0, which skills and scripts in the wild type
    [InlineData("--label", "pay", new[] { "Pay" })]
    [InlineData("--label", "CHECK", new[] { "Pay", "Refuse" })]
    public void Tag_and_its_older_name_label_keep_the_scenarios_with_a_tag_holding_the_text_in_any_case(string flag, string text, string[] expected)
    {
        var path = Written(Shop());

        Assert.Equal(expected, TagsByScenario(path, flag, text).Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("--tag", "Happy Path")]
    [InlineData("--tag", "happy")]
    [InlineData("--label", "Happy Path")]
    public void Tag_finds_a_happy_path_by_the_name_the_report_s_Tags_row_gives_it(string flag, string text)
    {
        // 4.x's --label "Happy Path" listed LightBDD's happy paths, whose [HappyPath] it wrote as that label. Since 5.0.0 a
        // happy path is a flag on every lane, which the report's Tags row and its @ search read as the tag Happy Path, and
        // until 5.2.7 --tag did not
        var features = Shop();
        features[0].Scenarios[0] = features[0].Scenarios[0] with { IsHappyPath = true };
        var path = Written(features);

        Assert.Equal(["Pay"], TagsByScenario(path, flag, text).Keys);
        // the flag is not one of the scenario's tags
        Assert.Equal(["checkout", "Smoke", "payments"], TagsByScenario(path)["Pay"]);
    }

    [Fact]
    public void A_4x_file_s_Happy_Path_label_reads_as_the_happy_path_and_not_as_a_tag()
    {
        // 4.x's LightBDD wrote [HappyPath] as the label Happy Path beside isHappyPath; 5.0.0 writes the flag alone, so a
        // file of either kind reads the same (5.2.7)
        var path = Written(Shop());
        var json = JsonNode.Parse(ReportHtml.Read(path))!;
        var feature = WithLabels(json["features"]!.AsArray(), "Checkout", "checkout");
        var pay = feature["scenarios"]!.AsArray().Single(s => (string?)s!["id"] == "pay")!.AsObject();
        pay.Remove("tags");
        pay["isHappyPath"] = true;
        pay["labels"] = new JsonArray("Happy Path", "fast");
        File.WriteAllText(path, json.ToJsonString());

        Assert.Equal(["checkout", "fast"], TagsByScenario(path)["Pay"]);
        Assert.Equal(["Pay"], TagsByScenario(path, "--tag", "Happy Path").Keys);
    }

    [Fact]
    public void A_listing_cut_short_by_tag_or_label_says_how_to_go_on_with_tag()
    {
        // the next: line repeats the filter, by its name since 5.0.0 whichever name was typed
        var path = Written(Shop());

        foreach (var flag in new[] { "--tag", "--label" })
        {
            var text = Query("scenarios", path, flag, "check", "--limit", "1");
            Assert.Contains("--tag check", text);
            Assert.DoesNotContain("--label", text);
        }
    }

    [Fact]
    public void Only_the_scenarios_verb_reads_tag()
    {
        var path = Written(Shop());
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = QueryCommand.Run(["failures", path, "--tag", "pay"], output, error);

        // refused as a flag this verb does not read, naming the verb that does, not as one the tool lacks
        Assert.NotEqual(0, exit);
        Assert.Contains("--tag", error.ToString());
        Assert.Contains("scenarios", error.ToString());
        Assert.DoesNotContain("Unknown option", error.ToString());
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
