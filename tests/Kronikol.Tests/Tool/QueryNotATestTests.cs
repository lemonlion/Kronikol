using System.Text.Json;
using Kronikol.Query;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol query</c> reads <c>notATest</c> and counts such a scenario as no test (plans/RUN_WINDOW_PLAN.md R1, F7):
/// on 5.2.3 the summary read the fold scenario as one passed scenario of feature "Ingested", and listed it under
/// Slowest when it was (harness P3: <c>s3  25.01s  Stack start-up</c>).
/// </summary>
public class QueryNotATestTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-query-not-a-test").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Two passed tests in Orders, and in Ingested the fold scenario, 780 s long and not a test. Features by name, so
    /// Ingested is s0 and the tests s1 and s2.
    /// </summary>
    private string Report()
    {
        var path = Path.Combine(_directory, $"TestRunReport-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "kronikolVersion": "5.2.5",
              "startTime": "2026-10-21T10:00:00Z",
              "endTime": "2026-10-21T10:00:09Z",
              "features": [
                { "name": "Ingested", "tags": [], "scenarios": [
                  { "id": "session", "stableId": "0000000000000001", "name": "Traffic outside any test", "result": "Passed", "durationSeconds": 780, "isHappyPath": false, "notATest": true, "tags": [], "steps": [] } ] },
                { "name": "Orders", "tags": [], "scenarios": [
                  { "id": "b1", "stableId": "0000000000000002", "name": "Lists orders", "result": "Passed", "durationSeconds": 2, "isHappyPath": false, "tags": [], "steps": [] },
                  { "id": "b2", "stableId": "0000000000000003", "name": "Shows the menu", "result": "Passed", "durationSeconds": 1, "isHappyPath": false, "tags": [], "steps": [] } ] }
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
        return output.ToString().Replace("\r\n", "\n");
    }

    private static string[] Lines(string output) => output.Split('\n');

    [Fact]
    public void Summary_counts_the_tests_and_says_what_is_not_one()
    {
        var output = Run("summary", Report());

        Assert.Contains("2 scenarios · 1 not a test · 0 failed · ", output);
        Assert.Contains("Ingested  1 not a test", Lines(output));
        Assert.Contains("Orders  2 passed", Lines(output));
    }

    [Fact]
    public void Slowest_leaves_out_a_scenario_that_is_not_a_test()
    {
        var output = Run("summary", Report());

        var slowest = Lines(output).SkipWhile(l => l != "Slowest:").Skip(1).TakeWhile(l => l.StartsWith("  ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["  s1  2s  Lists orders", "  s2  1s  Shows the menu"], slowest);
    }

    [Fact]
    public void Summary_json_counts_the_tests_and_names_what_is_not_one()
    {
        using var envelope = JsonDocument.Parse(Run("summary", Report(), "--json"));

        var run = envelope.RootElement.GetProperty("run");
        Assert.Equal(2, run.GetProperty("scenarios").GetInt32());
        Assert.Equal(1, run.GetProperty("notATest").GetInt32());
        var ingested = envelope.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("feature").GetString() == "Ingested");
        Assert.Equal(0, ingested.GetProperty("total").GetInt32());
        Assert.Equal(0, ingested.GetProperty("passed").GetInt32());
        Assert.Equal(1, ingested.GetProperty("notATest").GetInt32());
        var orders = envelope.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("feature").GetString() == "Orders");
        Assert.False(orders.TryGetProperty("notATest", out _));
        Assert.Equal("2", Run("summary", Report(), "--count").Trim());
    }

    [Fact]
    public void The_scenario_listing_marks_it()
    {
        var output = Run("scenarios", Report());

        var row = Assert.Single(Lines(output), l => l.Contains("Traffic outside any test", StringComparison.Ordinal));
        // No verdict in the flags column, which a test fills with pass, FAIL or skip, and the mark after the name.
        Assert.StartsWith("s0    " + "    " + " ", row);
        Assert.EndsWith(" calls  Traffic outside any test  not a test", row);
        Assert.StartsWith("s1    pass      2s", Assert.Single(Lines(output), l => l.Contains("Lists orders", StringComparison.Ordinal)));

        using var envelope = JsonDocument.Parse(Run("scenarios", Report(), "--json"));
        var items = envelope.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.True(items.Single(i => i.GetProperty("address").GetString() == "s0").GetProperty("notATest").GetBoolean());
        Assert.False(items.Single(i => i.GetProperty("address").GetString() == "s1").TryGetProperty("notATest", out _));
    }

    [Fact]
    public void Failures_counts_the_tests_when_nothing_failed()
    {
        var output = Run("failures", Report());

        Assert.Contains("2 scenarios, all passed · 1 not a test · next: scenarios · services", output);
    }

    [Fact]
    public void Ctrf_leaves_it_out_and_keeps_every_other_test_at_its_address()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.True(CtrfCommand.Run([Report()], output, error) == 0, error.ToString());

        using var document = JsonDocument.Parse(output.ToString());
        var results = document.RootElement.GetProperty("results");
        Assert.Equal(2, results.GetProperty("summary").GetProperty("tests").GetInt32());
        Assert.Equal(["s1", "s2"], results.GetProperty("tests").EnumerateArray()
            .Select(t => t.GetProperty("extra").GetProperty("kronikolAddress").GetString()!).ToArray());
    }

    /// <summary>A copy of <paramref name="report"/> with <paramref name="change"/> made to its JSON.</summary>
    private string Variant(string report, Action<System.Text.Json.Nodes.JsonNode> change)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(report))!;
        change(json);
        var path = Path.Combine(_directory, $"TestRunReport-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json.ToJsonString());
        return path;
    }

    [Fact]
    public void The_run_diff_lists_no_change_of_a_scenario_that_is_not_a_test()
    {
        // It comes and goes with the traffic, and lasts as long as the traffic does: new, gone or slower, it is no change of
        // the run's. 5.4.0 listed the fold under each, and counted it in --json's left and right.
        var with = Report();
        var without = Variant(with, json => json["features"]!.AsArray().RemoveAt(0));
        var longer = Variant(with, json => json["features"]![0]!["scenarios"]![0]!["durationSeconds"] = 2000);

        Assert.DoesNotContain("Traffic outside any test", Run("diff", without, with));
        Assert.DoesNotContain("Traffic outside any test", Run("diff", with, without));
        Assert.DoesNotContain("Traffic outside any test", Run("diff", with, longer));
        Assert.Equal("0", Run("diff", without, with, "--count").Trim());
        using var json = JsonDocument.Parse(Run("diff", with, longer, "--json"));
        Assert.Equal(2, json.RootElement.GetProperty("left").GetProperty("scenarios").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("right").GetProperty("scenarios").GetInt32());
        Assert.Empty(json.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public void Result_and_time_filters_match_no_scenario_that_is_not_a_test_and_the_footers_count_the_tests()
    {
        // 5.4.0 listed the fold under --result Passed and under --slower-than 100 (its 780 s are the traffic's), and the
        // footers counted it as a scenario.
        var report = Report();

        Assert.Equal("2", Run("scenarios", report, "--result", "Passed", "--count").Trim());
        var slow = Run("scenarios", report, "--slower-than", "100");
        Assert.DoesNotContain("Traffic outside any test", slow);
        Assert.Contains("2 scenarios in the report · 1 not a test", slow);
        Assert.Contains("2 scenarios · 1 not a test · next: failures · scenarios", Run("repro", report));
    }

    [Fact]
    public void The_warm_up_note_names_no_scenario_that_is_not_a_test()
    {
        // The start-up can pay the run's first-call warm-up: under --slower-than it is left out for being no test, and the
        // note said its warm-up was the reason.
        var warmed = Variant(Report(), json => json["features"]![0]!["scenarios"]![0]!["warmUpSeconds"] = 779.5);

        var answer = Run("scenarios", warmed, "--slower-than", "100");

        Assert.DoesNotContain("first-call warm-up", answer);
        Assert.DoesNotContain("Traffic outside any test", answer);
    }

    [Fact]
    public void Repro_leaves_out_a_failed_scenario_that_is_not_a_test()
    {
        // NotATest is public: a host can set it on a scenario that failed, and its result is no failure of the run's, as
        // failures counts it.
        var failedFold = Variant(Report(), json => json["features"]![0]!["scenarios"]![0]!["result"] = "Failed");

        Assert.Contains("2 scenarios · 1 not a test · next: failures · scenarios", Run("repro", failedFold));
        Assert.Equal("0", Run("repro", failedFold, "--count").Trim());
    }

    [Fact]
    public void The_run_diffs_header_counts_each_runs_tests()
    {
        // 5.2.5 read "3 scenarios, 0 failed" on each side: the fold counted as a scenario of each run.
        var header = Lines(Run("diff", Report(), Report())).Take(2).ToArray();

        Assert.Equal(2, header.Length);
        Assert.All(header, line => Assert.EndsWith("  2 scenarios, 0 failed", line));
    }
}
