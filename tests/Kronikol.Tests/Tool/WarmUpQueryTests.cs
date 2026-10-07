using System.Net;
using System.Text.Json;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// What <c>kronikol query</c> says of the first-call warm-up (plans/WARM_UP_PLAN.md 4.5, T30 to T33): <c>summary</c>
/// ranks Slowest by the time left and says where the warm-up went, <c>flow</c> and <c>interactions</c> mark the calls,
/// and <c>--slower-than</c> and <c>diff</c>'s Slower read the time left. A report with no marks reads as it did.
/// </summary>
public class WarmUpQueryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-warm-up-query").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Summary_ranks_slowest_by_the_time_left_and_says_where_the_warm_up_went()
    {
        var report = Report("marked");

        var text = Query("summary", report);

        Assert.Contains("""
            Slowest, first-call warm-up left out:
              s2  2s  Place twice
              s0  1.9s  Place
              s1  1.61s  Place again
            First-call warm-up: 1.19s in 2 scenarios; the most in s0, 0.6s of its 2.5s (flow s0)
            """.ReplaceLineEndings("\n"), text);
    }

    [Fact]
    public void Summary_json_carries_each_slowest_scenarios_warm_up_and_the_runs()
    {
        var report = Report("marked");

        var text = Query("summary", report, "--json");
        using var json = JsonDocument.Parse(text);
        var data = json.RootElement;

        var slowest = data.GetProperty("slowest").EnumerateArray().ToList();
        Assert.Equal(["s2", "s0", "s1"], slowest.Select(s => s.GetProperty("address").GetString()));
        Assert.False(slowest[0].TryGetProperty("warmUpSeconds", out _));
        Assert.Equal(0.6, slowest[1].GetProperty("warmUpSeconds").GetDouble(), 6);
        var warmUp = data.GetProperty("warmUp");
        Assert.Equal(1.19, warmUp.GetProperty("seconds").GetDouble(), 6);
        Assert.Equal(2, warmUp.GetProperty("scenarios").GetInt32());
        Assert.Equal(2, warmUp.GetProperty("calls").GetInt32());
        Assert.Equal("s0", warmUp.GetProperty("largest").GetProperty("address").GetString());
        Assert.True(text.Length < 2000, $"{text.Length} bytes");
    }

    [Fact]
    public void Flow_names_the_warm_up_after_a_marked_calls_duration()
    {
        var report = Report("marked");

        var first = Query("flow", report, "s0").Split('\n').Single(l => l.Contains("s0/i0"));
        var waited = Query("flow", report, "s1").Split('\n').Single(l => l.Contains("s1/i0"));

        Assert.Contains("600 ms  first POST /orders of the run: later calls 5 ms median (2)", first);
        Assert.Contains("590 ms  waited for s0/i0, the run's first POST /orders: later calls 5 ms median (2)", waited);
        Assert.DoesNotContain(Query("flow", report, "s2").Split('\n'), l => l.Contains("waited") || l.Contains("first POST"));
        Assert.All(new[] { first, waited }, l => Assert.Equal(l.TrimEnd(), l));
    }

    [Fact]
    public void Interactions_mark_a_warm_up_call_after_its_duration()
    {
        var report = Report("marked");

        var rows = Query("interactions", report).Split('\n');

        Assert.Contains(rows, r => r.StartsWith("s0/i0", StringComparison.Ordinal) && r.Contains("600 ms warm-up"));
        Assert.Contains(rows, r => r.StartsWith("s1/i0", StringComparison.Ordinal) && r.Contains("590 ms warm-up"));
        Assert.DoesNotContain(rows, r => r.StartsWith("s2/", StringComparison.Ordinal) && r.Contains("warm-up"));

        using var json = JsonDocument.Parse(Query("interactions", report, "--json"));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        var marked = items.Where(i => i.TryGetProperty("warmUp", out _)).ToList();
        Assert.Equal(["s0/i0", "s1/i0"], marked.Select(i => i.GetProperty("address").GetString()).Order());
        Assert.Equal("first", marked.Single(i => i.GetProperty("address").GetString() == "s0/i0").GetProperty("warmUp").GetProperty("kind").GetString());
    }

    [Fact]
    public void A_report_without_marks_reads_as_it_did()
    {
        var report = Report("unmarked", firstMs: 5);

        var summary = Query("summary", report);

        Assert.Contains("\nSlowest:\n", summary);
        Assert.DoesNotContain("warm-up", summary);
        Assert.DoesNotContain("warm-up", Query("interactions", report));
        Assert.DoesNotContain("warmUp", Query("summary", report, "--json"));
    }

    [Fact]
    public void Slower_than_reads_the_time_left()
    {
        var report = Report("marked");

        using var json = JsonDocument.Parse(Query("scenarios", report, "--slower-than", "1.95", "--json"));

        Assert.Equal(["s2"], json.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("address").GetString()));
    }

    [Fact]
    public void Diff_reads_the_time_left_when_both_runs_carry_marks()
    {
        // The new run's s0 paid the warm-up the old run's s2 paid: by wall time s0 went from 1.5 s to 2.5 s (1.67 times, so
        // "slower"), by the time left from 1.5 s to 1.9 s (1.27 times, not).
        var before = Report("before", firstMs: 600, firstScenarioSeconds: 1.5, warmScenarioSeconds: 2.0, firstIn: "warm");
        var after = Report("after");

        var text = Query("diff", before, after);

        Assert.DoesNotContain("Slower", text);
    }

    private static string Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Three scenarios of one feature: "Place" (s0, 2.5 s) makes the run's first POST /orders, "Place again" (s1, 2.2 s)
    /// makes a call that waited for it, and "Place twice" (s2, 2.0 s) makes two warm calls after both.
    /// </summary>
    private string Report(string name, double firstMs = 600, double firstScenarioSeconds = 2.5, double warmScenarioSeconds = 2.0, string firstIn = "first")
    {
        var logs = new List<RequestResponseLog>();
        Call(logs, firstIn, 0, firstMs);
        Call(logs, "waiter", 10, firstMs > 20 ? firstMs - 10 : firstMs);
        Call(logs, firstIn == "warm" ? "first" : "warm", 1000, 5);
        Call(logs, "warm", 1100, 5);
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "first", DisplayName = "Place", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(firstScenarioSeconds) },
                    new Scenario { Id = "waiter", DisplayName = "Place again", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2.2) },
                    new Scenario { Id = "warm", DisplayName = "Place twice", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(warmScenarioSeconds) }
                ]
            }
        ];
        var written = ReportGenerator.GenerateTestRunReportData(features, T0.UtcDateTime, T0.UtcDateTime.AddMinutes(1),
            $"WarmUpQuery_{Guid.NewGuid():N}.json", DataFormat.Json, null, logs.ToArray());
        var folder = Path.Combine(_directory, name);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "TestRunReport.json");
        File.Move(written, path, overwrite: true);
        return path;
    }

    private static void Call(List<RequestResponseLog> logs, string scenario, double startMs, double durationMs)
    {
        var (trace, id) = (Guid.NewGuid(), Guid.NewGuid());
        var uri = new Uri("http://orders/orders");
        logs.Add(new RequestResponseLog(scenario, scenario, HttpMethod.Post, "{}", uri, [], "orders", "Test", RequestResponseType.Request, trace, id, false)
            { Timestamp = T0.AddTicks((long)Math.Round(startMs * TimeSpan.TicksPerMillisecond)) });
        logs.Add(new RequestResponseLog(scenario, scenario, HttpMethod.Post, "{}", uri, [], "orders", "Test", RequestResponseType.Response, trace, id, false, HttpStatusCode.OK)
            { Timestamp = T0.AddTicks((long)Math.Round((startMs + durationMs) * TimeSpan.TicksPerMillisecond)) });
    }
}
