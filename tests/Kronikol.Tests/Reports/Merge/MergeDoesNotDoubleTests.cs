using System.Globalization;
using System.Text.Json;
using Kronikol.Tool;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// A shard the merge has already been given adds nothing the second time.
///
/// <para>Scenarios were deduplicated where the features were unioned, and only there. The second copy's
/// traffic, diagrams and component-diagram counts were not: the same shard supplied twice — a CI artifact
/// downloaded into two folders, yesterday's merged file left in the directory being merged, the merge's
/// own output fed back in — kept one scenario and gave it every interaction twice, two identical
/// diagrams, and a component diagram reading <c>16 calls across 6 tests</c> where the run made 8 across
/// 3. Nothing said so.</para>
///
/// <para>What is <b>not</b> a duplicate: a scenario that genuinely ran twice. Two shards that overlap, or
/// a failed shard re-run beside its first attempt, are two runs with different results or timings, and
/// both are kept — first-wins would merge a failing run green, which is the one outcome a merge may never
/// produce. A copy is the same scenario with the same result, the same duration and the same message.</para>
/// </summary>
public class MergeDoesNotDoubleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-merge-once-" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "kronikol-merge-once-out-" + Guid.NewGuid().ToString("N"));

    public MergeDoesNotDoubleTests()
    {
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(_out);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
        try { Directory.Delete(_out, true); } catch { /* best effort */ }
    }

    [Fact]
    public void Merging_the_same_shard_twice_does_not_double_its_interactions()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed", calls: 2);
        File.Copy(In("runner1.json"), In("runner1-downloaded-again.json"));

        var (exit, _, error) = Merge();

        Assert.True(exit == 0, error);
        var scenario = Assert.Single(Scenarios());
        Assert.Equal(2, scenario.GetProperty("httpInteractions").GetArrayLength());
    }

    /// <summary>F1's measured symptom: the component diagram's arrow label doubled on a re-merge.</summary>
    [Fact]
    public void Merging_the_same_shard_twice_does_not_double_the_component_label()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed", calls: 8);
        File.Copy(In("runner1.json"), In("runner1-downloaded-again.json"));

        var (exit, _, error) = Merge();

        Assert.True(exit == 0, error);
        var relationship = Assert.Single(Relationships());
        Assert.Equal(8, relationship.GetProperty("callCount").GetInt32());
        Assert.Equal(1, relationship.GetProperty("testCount").GetInt32());
    }

    [Fact]
    public void Merging_the_same_shard_twice_does_not_double_its_diagrams()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed");
        File.Copy(In("runner1.json"), In("runner1-downloaded-again.json"));

        var (exit, _, error) = Merge();

        Assert.True(exit == 0, error);
        Assert.Equal(1, Assert.Single(Scenarios()).GetProperty("diagrams").GetArrayLength());
    }

    [Fact]
    public void A_shard_supplied_twice_is_said_to_have_been_counted_once()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed");
        File.Copy(In("runner1.json"), In("runner1-downloaded-again.json"));

        var (exit, _, error) = Merge();

        Assert.True(exit == 0, error);
        var entry = Assert.Single(Diagnostics(), d => d.Contains("supplied twice", StringComparison.Ordinal));
        Assert.Contains("counted once", entry, StringComparison.Ordinal);
    }

    /// <summary>
    /// The realistic shape of the duplicate: a directory that still holds the previous merge's output. A
    /// directory input is swept recursively for <c>*.json</c>, so yesterday's <c>Combined.json</c> is a
    /// shard as far as the sweep can tell — one that happens to contain every other shard.
    /// </summary>
    [Fact]
    public void Yesterdays_merged_file_left_beside_the_shards_adds_nothing()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed", calls: 2);
        WriteShard("runner2.json", "0-1002", "Discount is applied", "Failed", calls: 3, service: "Discounts");
        var yesterday = Merge(Path.Combine(_dir, "yesterday", "Combined.html"));
        Assert.True(yesterday.Exit == 0, yesterday.Error);

        var today = Merge(Path.Combine(_out, "Combined.html"));

        Assert.True(today.Exit == 0, today.Error);
        var scenarios = Scenarios(Path.Combine(_out, "Combined.json"));
        Assert.Equal(2, scenarios.Length);
        Assert.Equal(5, scenarios.Sum(s => s.GetProperty("httpInteractions").GetArrayLength()));

        var relationships = Relationships(Path.Combine(_out, "Combined.json"));
        Assert.Equal(2, relationships.Single(r => r.GetProperty("service").GetString() == "Pricing").GetProperty("callCount").GetInt32());
        Assert.Equal(3, relationships.Single(r => r.GetProperty("service").GetString() == "Discounts").GetProperty("callCount").GetInt32());
        Assert.All(relationships, r => Assert.Equal(1, r.GetProperty("testCount").GetInt32()));
    }

    /// <summary>
    /// Two shards that both ran a scenario are two runs, not one run twice. The results differ here; in
    /// practice the durations always do. Keeping both is the only answer that cannot hide the failure.
    /// </summary>
    [Fact]
    public void A_scenario_that_ran_in_two_shards_with_different_outcomes_keeps_both_runs()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed", duration: 1.0);
        WriteShard("runner2.json", "0-1002", "Cart is priced", "Failed", duration: 1.4);

        var (exit, _, error) = Merge();

        Assert.True(exit == 0, error);
        var scenarios = Scenarios();
        Assert.Equal(2, scenarios.Length);
        Assert.Contains(scenarios, s => s.GetProperty("result").GetString() == "Failed");
        Assert.Contains(Diagnostics(), d => d.Contains("more than one shard", StringComparison.Ordinal));
    }

    /// <summary>
    /// A retry is the same scenario twice inside ONE shard, and the single-run report shows both attempts.
    /// Under NUnit the attempts even share a runtime id. The merge keeps them the way the run did.
    /// </summary>
    [Fact]
    public void A_retried_scenario_keeps_both_attempts()
    {
        File.WriteAllText(In("runner1.json"), """
            {
              "mergeableFormatVersion": 1, "kronikolVersion": "3.5.1",
              "startTime": "2026-01-01T10:00:00Z", "endTime": "2026-01-01T10:01:00Z",
              "features": [ { "name": "Checkout", "scenarios": [
                { "id": "0-1002", "name": "Cart is priced", "result": "Failed", "durationSeconds": 1.0, "attempt": 1, "errorMessage": "flaky" },
                { "id": "0-1002", "name": "Cart is priced", "result": "Passed", "durationSeconds": 1.1, "attempt": 2 }
              ] } ]
            }
            """);

        var (exit, _, error) = Merge();

        Assert.True(exit == 0, error);
        var attempts = Scenarios().Select(s => s.GetProperty("attempt").GetInt32()).OrderBy(a => a).ToArray();
        Assert.Equal([1, 2], attempts);
        Assert.DoesNotContain(Diagnostics(), d => d.Contains("more than one shard", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_shards_that_partition_the_suite_say_nothing_about_duplicates()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed");
        WriteShard("runner2.json", "0-1002", "Discount is applied", "Passed", service: "Discounts");

        var (exit, _, error) = Merge();

        Assert.True(exit == 0, error);
        Assert.Equal(2, Scenarios().Length);
        Assert.DoesNotContain(Diagnostics(), d => d.Contains("supplied twice", StringComparison.Ordinal) || d.Contains("more than one shard", StringComparison.Ordinal));
    }

    private string In(string name) => Path.Combine(_dir, name);

    private JsonElement Root(string? json = null) =>
        JsonDocument.Parse(File.ReadAllText(json ?? In("Combined.json"))).RootElement;

    private JsonElement[] Scenarios(string? json = null) =>
        Root(json).GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .ToArray();

    private JsonElement[] Relationships(string? json = null) =>
        Root(json).GetProperty("componentRelationships").EnumerateArray().ToArray();

    private string[] Diagnostics(string? json = null) =>
        Root(json).TryGetProperty("diagnostics", out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(d => d.GetProperty("message").GetString() ?? "").ToArray()
            : [];

    private (int Exit, string Output, string Error) Merge(string? output = null)
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        var exit = Commands.Dispatch(["merge", _dir, "-o", output ?? In("Combined.html")], outWriter, errWriter);
        return (exit, outWriter.ToString(), errWriter.ToString());
    }

    /// <summary>One scenario with <paramref name="calls"/> requests to one service, one diagram, and the component-diagram row the run would have computed for it.</summary>
    private void WriteShard(string name, string scenarioId, string scenarioName, string result,
        int calls = 1, string service = "Pricing", double duration = 1.0)
    {
        var interactions = string.Join(",\n", Enumerable.Range(0, calls).Select(i => $$"""
                { "type": "Request", "metaType": "Default", "method": "GET", "uri": "https://{{service.ToLowerInvariant()}}.test/quote/{{i}}",
                  "serviceName": "{{service}}", "callerName": "Tests",
                  "requestResponseId": "{{Guid.NewGuid()}}", "timestamp": "2026-01-01T10:00:{{i:D2}}Z" }
                """));

        File.WriteAllText(In(name), $$"""
            {
              "mergeableFormatVersion": 1,
              "kronikolVersion": "3.5.1",
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:01:00Z",
              "componentRelationships": [
                { "caller": "Tests", "service": "{{service}}", "protocol": "HTTP", "methods": ["GET"],
                  "callCount": {{calls}}, "testCount": 1, "dependencyCategory": "http" }
              ],
              "features": [
                { "name": "Checkout", "scenarios": [
                  { "id": {{JsonSerializer.Serialize(scenarioId)}},
                    "name": {{JsonSerializer.Serialize(scenarioName)}},
                    "result": "{{result}}",
                    "durationSeconds": {{duration.ToString(CultureInfo.InvariantCulture)}},
                    "diagrams": ["@startuml\nTests->{{service}}\n@enduml"],
                    "httpInteractions": [ {{interactions}} ] }
                ] }
              ]
            }
            """);
    }
}
