using System.Text.Json;
using Kronikol.Tool;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// Merging two shards keeps every scenario both of them ran.
///
/// <para>Scenarios were deduplicated by their <b>runtime</b> id, on the stated invariant that "a scenario
/// id belongs to exactly one shard, so a collision means the same runner's output was supplied twice".
/// That invariant is false for the frameworks most likely to be sharded. NUnit's id is a per-process
/// sequential counter — a real report in this repository contains <c>"id": "0-1002"</c> — so every shard
/// process mints the same ids for entirely different tests.</para>
///
/// <para>The consequence is the worst one a merge can have: two shards each running a different scenario
/// of the same feature produced <b>one</b> scenario in the merged file. Whichever sorted second was
/// deleted, its traffic re-attributed to the survivor, and no diagnostic said so. A shard whose test
/// failed merged green.</para>
///
/// <para>What tells the two cases apart is not the runtime id but the scenario's content: the same
/// runner's output supplied twice has the same feature, name and example row, and two different tests do
/// not. That is exactly what <c>ScenarioStableId</c> already computes, and what every cross-run
/// comparison in the tool already keys on.</para>
/// </summary>
public class MergeKeepsEveryScenarioTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-merge-keep-" + Guid.NewGuid().ToString("N"));

    public MergeKeepsEveryScenarioTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    /// <summary>
    /// The measured case, in the shape sharded NUnit produces it: one feature, two shards, two different
    /// tests that both happen to be their process's second.
    /// </summary>
    [Fact]
    public void Two_different_scenarios_that_share_a_runtime_id_both_survive()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed");
        WriteShard("runner2.json", "0-1002", "Discount is applied", "Failed");

        Assert.Equal(0, Merge().Exit);

        var scenarios = Scenarios();
        Assert.Equal(2, scenarios.Length);
        Assert.Contains(scenarios, s => s.GetProperty("name").GetString() == "Discount is applied");
    }

    /// <summary>The half that matters to anyone reading the result: a failing shard must not merge green.</summary>
    [Fact]
    public void A_failing_shard_does_not_merge_green()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed");
        WriteShard("runner2.json", "0-1002", "Discount is applied", "Failed");

        Assert.Equal(0, Merge().Exit);

        Assert.Contains(Scenarios(), s => s.GetProperty("result").GetString() == "Failed");
    }

    /// <summary>
    /// The traffic has to travel with the scenario that made it. Everything on the side — interactions,
    /// step paths, annotations, diagrams — is keyed by the runtime id, so two survivors sharing one id
    /// would give each of them all of both scenarios' calls.
    /// </summary>
    [Fact]
    public void Each_surviving_scenario_keeps_only_its_own_calls()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed", "https://pricing.test/quote");
        WriteShard("runner2.json", "0-1002", "Discount is applied", "Failed", "https://discounts.test/apply");

        Assert.Equal(0, Merge().Exit);

        foreach (var scenario in Scenarios())
        {
            var uris = scenario.GetProperty("httpInteractions").EnumerateArray()
                .Select(i => i.GetProperty("uri").GetString()).ToArray();

            Assert.Single(uris);
            Assert.Equal(scenario.GetProperty("name").GetString() == "Cart is priced"
                ? "https://pricing.test/quote"
                : "https://discounts.test/apply", uris[0]);
        }
    }

    /// <summary>
    /// The case the old invariant described really does exist — the same shard handed over twice — and it
    /// still deduplicates, because the two entries have the same content and not merely the same id.
    /// </summary>
    [Fact]
    public void The_same_shard_supplied_twice_still_deduplicates()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed");
        File.Copy(Path.Combine(_dir, "runner1.json"), Path.Combine(_dir, "runner1-copy.json"));

        Assert.Equal(0, Merge().Exit);

        Assert.Single(Scenarios());
    }

    private JsonElement[] Scenarios()
    {
        var root = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "Combined.json"))).RootElement;
        return root.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .ToArray();
    }

    private (int Exit, string Output, string Error) Merge()
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        var exit = Commands.Dispatch(["merge", _dir, "-o", Path.Combine(_dir, "Combined.html")], outWriter, errWriter);
        return (exit, outWriter.ToString(), errWriter.ToString());
    }

    private void WriteShard(string name, string scenarioId, string scenarioName, string result, string? uri = null)
    {
        var interactions = uri is null
            ? "[]"
            : $$"""
                [ { "type": "Request", "metaType": "Default", "method": "GET", "uri": {{JsonSerializer.Serialize(uri)}},
                    "serviceName": "Pricing", "callerName": "Tests",
                    "requestResponseId": "{{Guid.NewGuid()}}", "timestamp": "2026-01-01T10:00:01Z" } ]
                """;

        File.WriteAllText(Path.Combine(_dir, name), $$"""
            {
              "mergeableFormatVersion": 1,
              "kronikolVersion": "3.5.1",
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:01:00Z",
              "features": [
                { "name": "Checkout", "scenarios": [
                  { "id": {{JsonSerializer.Serialize(scenarioId)}},
                    "name": {{JsonSerializer.Serialize(scenarioName)}},
                    "result": {{JsonSerializer.Serialize(result)}}, "durationSeconds": 1.0,
                    "httpInteractions": {{interactions}} }
                ] }
              ]
            }
            """);
    }
}
