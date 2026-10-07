using System.Text.Json;
using Kronikol.Query;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The counts the overview verbs print: a scenario that did not fail did not necessarily pass (#105,
/// CUCUMBER_BYPASS_PLAN.md F3). <c>summary</c> counted every scenario that did not fail as passed, so a run of
/// skipped and bypassed scenarios read "N passed" in the verb an agent runs first.
/// </summary>
public class QuerySummaryCountsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-query-counts").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>One feature holding one scenario of each result named.</summary>
    private string Report(params string[] results)
    {
        var scenarios = results.Select((result, i) =>
            $$"""{ "id": "t{{i}}", "stableId": "{{i:x16}}", "name": "Scenario {{i}} {{result}}", "result": "{{result}}", "durationSeconds": 0.5, "labels": [], "categories": [], "steps": [] }""");
        var path = Path.Combine(_directory, $"TestRunReport-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, $$"""
            {
              "kronikolVersion": "4.6.0",
              "startTime": "2026-10-07T10:00:00Z",
              "endTime": "2026-10-07T10:05:00Z",
              "features": [
                { "name": "Catalogue", "labels": [], "scenarios": [ {{string.Join(",\n", scenarios)}} ] }
              ]
            }
            """);
        return path;
    }

    private static string Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString();
    }

    private static string FeatureLine(string output) =>
        output.Split('\n').Select(l => l.TrimEnd('\r')).Single(l => l.StartsWith("Catalogue", StringComparison.Ordinal));

    [Fact]
    public void Summary_counts_skipped_and_bypassed_scenarios_apart_from_passed_ones()
    {
        var output = Run("summary", Report("Passed", "Skipped", "SkippedAfterFailure", "Bypassed", "Failed"));

        Assert.Equal("Catalogue  1 passed, 2 skipped, 1 bypassed, 1 FAILED", FeatureLine(output));
    }

    [Fact]
    public void Summary_names_only_the_counts_a_feature_has()
    {
        Assert.Equal("Catalogue  2 passed", FeatureLine(Run("summary", Report("Passed", "Passed"))));
        Assert.Equal("Catalogue  0 passed, 1 bypassed", FeatureLine(Run("summary", Report("Bypassed"))));
        Assert.Equal("Catalogue  1 passed, 1 skipped", FeatureLine(Run("summary", Report("Passed", "Skipped"))));
    }

    [Fact]
    public void Summary_json_says_passed_for_the_scenarios_that_passed()
    {
        var envelope = JsonDocument.Parse(Run("summary", Report("Passed", "Skipped", "SkippedAfterFailure", "Bypassed", "Failed"), "--json")).RootElement;

        var item = envelope.GetProperty("items").EnumerateArray().Single(i => i.TryGetProperty("feature", out _));
        Assert.Equal("Catalogue", item.GetProperty("feature").GetString());
        Assert.Equal(5, item.GetProperty("total").GetInt32());
        Assert.Equal(1, item.GetProperty("passed").GetInt32());
        Assert.Equal(1, item.GetProperty("failed").GetInt32());
        Assert.Equal(2, item.GetProperty("skipped").GetInt32());
        Assert.Equal(1, item.GetProperty("bypassed").GetInt32());
    }

    [Fact]
    public void Failures_with_nothing_failed_does_not_say_a_bypassed_scenario_did_not_run()
    {
        // A bypassed scenario ran: one step of it was skipped over, and the steps after it ran.
        var output = Run("failures", Report("Passed", "Skipped", "Bypassed"));

        Assert.Contains("3 scenarios: 1 passed, 1 did not run, 1 bypassed", output);
    }

    [Fact]
    public void Failures_with_nothing_failed_keeps_its_line_for_a_run_with_no_bypass()
    {
        Assert.Contains("2 scenarios: 1 passed, 1 did not run", Run("failures", Report("Passed", "Skipped")));
        Assert.Contains("2 scenarios, all passed", Run("failures", Report("Passed", "Passed")));
    }
}
