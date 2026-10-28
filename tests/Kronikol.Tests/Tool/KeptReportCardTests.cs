using System.Text.Json;
using System.Text.Json.Nodes;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.3.1: reports an older writer wrote, kept as test data with a golden card each, since
/// writers changed what they record between 4.x and 5.x and no run is written by both. Both were written by Kronikol
/// 4.32.0 (the generator is the plan harness's <c>probes/v4-fixtures/gen.cs</c>): <c>v4.32-ingest.json</c>, the capture
/// <c>card-capture.ndjson</c> through that release's ingest, whose 9,000-character request body it stored compressed
/// (<c>formatVersion</c> 2); and <c>v4.32-in-process.json</c>, an in-process-shaped run with <c>attributionSource</c> on
/// every call, a SQL statement and a GraphQL body, and a root background block whose 9,142-character statement it stored
/// compressed there.
/// </summary>
[Collection("DiagramsFetcher")]
public class KeptReportCardTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-kept-card").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "Cards", name);

    private static string Card(string report)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = CardCommand.Run([report], output, error);
        Assert.True(exit == 0, error.ToString());
        return output.ToString();
    }

    /// <summary>The <c>behaviour</c> member's bytes, the range <c>behaviourHash</c> covers (4.3).</summary>
    private static string Behaviour(string card)
    {
        var start = card.LastIndexOf("\"behaviour\":", StringComparison.Ordinal) + "\"behaviour\":".Length;
        return card[start..card.TrimEnd('\n').LastIndexOf('}')];
    }

    [Theory]
    [InlineData("v4.32-ingest")]
    [InlineData("v4.32-in-process")]
    public void A_kept_reports_card_is_its_golden_card(string name)
    {
        var card = Card(Data(name + ".json"));

        Assert.Equal(File.ReadAllText(Data(name + ".behaviour.json")).TrimEnd('\n'), Behaviour(card));
        using var document = JsonDocument.Parse(card);
        var report = document.RootElement.GetProperty("run").GetProperty("report");
        Assert.Equal(2, report.GetProperty("formatVersion").GetInt32());
        Assert.StartsWith("4.32.0+", report.GetProperty("kronikolVersion").GetString(), StringComparison.Ordinal);
    }

    // What the fixtures are kept for: a compressed body in each place a 4.x writer put one.
    [Fact]
    public void The_kept_reports_hold_the_compressed_bodies_they_are_kept_for()
    {
        using var ingest = JsonDocument.Parse(File.ReadAllText(Data("v4.32-ingest.json")));
        var order = ingest.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("httpInteractions")[0];
        Assert.True(order.GetProperty("content").TryGetProperty("$z", out _));

        using var inProcess = JsonDocument.Parse(File.ReadAllText(Data("v4.32-in-process.json")));
        var expire = inProcess.RootElement.GetProperty("background").GetProperty("interactions")[0];
        Assert.True(expire.GetProperty("content").TryGetProperty("$z", out _));

        // the card read the background statement through its wrapper: its target is the statement's head, not the path alone
        var behaviour = JsonDocument.Parse(File.ReadAllText(Data("v4.32-in-process.behaviour.json"))).RootElement;
        var update = behaviour.GetProperty("edges").EnumerateArray().SelectMany(e => e.GetProperty("operations").EnumerateArray())
            .Single(o => o.GetProperty("method").GetString() == "UPDATE");
        Assert.StartsWith("/orders UPDATE orders SET status", update.GetProperty("target").GetString(), StringComparison.Ordinal);
        Assert.Equal(["background"], update.GetProperty("causes").EnumerateArray().Select(c => c.GetString()));
    }

    // One capture, two writers: 4.32.0's ingest and this build's. Listed before either card was written, the differences
    // expected are the stable ids (4.32.0 computed a scenario's id by its own rule) and the run's facts about its report.
    [Fact]
    public void One_capture_ingested_by_4_32_and_by_this_build_differs_only_in_its_stable_ids_and_what_it_says_of_its_rules()
    {
        var reports = Path.Combine(_directory, "Reports");
        using (RequestResponseLogger.IsolateForTests())
            Assert.Equal(0, IngestCommand.Run([Data("card-capture.ndjson"), "-o", reports], new StringWriter(), new StringWriter()));

        var current = JsonNode.Parse(Behaviour(Card(reports)))!.AsObject();
        var kept = JsonNode.Parse(Behaviour(Card(Data("v4.32-ingest.json"))))!.AsObject();

        static Dictionary<string, string> Sids(JsonObject behaviour) =>
            behaviour["scenarios"]!.AsArray().ToDictionary(s => (string)s!["scenario"]!, s => (string)s!["sid"]!);
        var (currentSids, keptSids) = (Sids(current), Sids(kept));
        Assert.Equal(["Order pancakes", "Read the menu"], currentSids.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(currentSids.Values.Intersect(keptSids.Values));

        // each kept row under this build's id, in this build's order
        var rows = kept["scenarios"]!.AsArray().Select(s => s!.DeepClone()).ToList();
        foreach (var row in rows)
            row["sid"] = currentSids[(string)row["scenario"]!];
        kept["scenarios"] = new JsonArray([.. rows.OrderBy(r => (string)r["sid"]!, StringComparer.Ordinal)]);
        // A file this build writes records its run's rules whenever it has any (5.19.0), so its card says "none" where
        // 4.32.0's file cannot say.
        Assert.Equal("none", (string?)current["rules"]!["templateRules"]);
        Assert.Null(kept["rules"]!["templateRules"]);
        kept["rules"]!["templateRules"] = "none";
        Assert.True(JsonNode.DeepEquals(current, kept), $"this build:\n{current}\n4.32.0, its ids replaced:\n{kept}");
    }
}
