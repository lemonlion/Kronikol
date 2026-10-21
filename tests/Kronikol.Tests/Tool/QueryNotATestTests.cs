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
}
