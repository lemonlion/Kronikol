using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    public void Slowest_is_listed_when_every_scenario_left_no_time_of_its_own()
    {
        // Ranked by the time left, a skipped scenario of 0 s tied with one that was all warm-up and, coming first, stood at
        // the top, whose 0 s of wall time dropped the whole Slowest block (until 4.16.3).
        var logs = new List<RequestResponseLog>();
        Call(logs, "place", 0, 600);
        Call(logs, "place", 1000, 5);
        Call(logs, "place", 1100, 5);
        var report = Write("all-warm-up", [Orders(("skip", "Skip", 0, ExecutionResult.Skipped), ("place", "Place", 0.5, ExecutionResult.Passed))], logs);

        Assert.Contains("""

            Slowest, first-call warm-up left out:
              s1  0s  Place
              s0  0s  Skip
            First-call warm-up: 0.5s in 1 scenario; the most in s1, 0.5s of its 0.5s (flow s1)
            """.ReplaceLineEndings("\n"), Query("summary", report));
    }

    [Fact]
    public void The_warm_up_line_counts_the_calls_of_the_scenarios_it_counts()
    {
        // A scenario with no duration holds its marks and no warm-up seconds; the member counted its calls with the
        // others' seconds (until 4.16.3), so 0.59 s was over two calls of which one was not in it.
        var logs = new List<RequestResponseLog>();
        Call(logs, "untimed", 0, 600);
        Call(logs, "waiter", 10, 590);
        Call(logs, "warm", 1000, 5);
        Call(logs, "warm", 1100, 5);
        var report = Write("untimed", [Orders(("untimed", "Place", null, ExecutionResult.Passed), ("waiter", "Place again", 2.2, ExecutionResult.Passed),
            ("warm", "Place twice", 2.0, ExecutionResult.Passed))], logs);

        using var json = JsonDocument.Parse(Query("summary", report, "--json"));
        var warmUp = json.RootElement.GetProperty("warmUp");

        Assert.Equal(1, warmUp.GetProperty("scenarios").GetInt32());
        Assert.Equal(1, warmUp.GetProperty("calls").GetInt32());
    }

    [Fact]
    public void A_warm_up_under_ten_milliseconds_reads_in_milliseconds()
    {
        // #122: BDDfy can record a duration shorter than the calls in it, and the warm-up is capped there; at 3 ms the line
        // read "0s of its 0s" (until 4.16.3).
        var logs = new List<RequestResponseLog>();
        Call(logs, "first", 0, 600);
        Call(logs, "warm", 1000, 5);
        Call(logs, "warm", 1100, 5);
        var report = Write("capped", [Orders(("first", "Place", 0.003, ExecutionResult.Passed), ("warm", "Place twice", 2.0, ExecutionResult.Passed))], logs);

        Assert.Contains("\nFirst-call warm-up: 3ms in 1 scenario; the most in s0, 3ms of its 3ms (flow s0)\n", Query("summary", report));
    }

    [Fact]
    public void A_mark_this_build_cannot_read_is_no_mark()
    {
        // As the merge reader reads it: a kind other than first or waited is not a mark, and a field of the wrong type is
        // not a crash. Until 4.16.3 a numeric kind threw out of the query and an unknown one read as a first call.
        var report = Report("unreadable");
        var json = ReportHtml.Read(report);
        Assert.Contains("\"kind\": \"first\"", json);
        Assert.Contains("\"kind\": \"waited\"", json);
        File.WriteAllText(report, json.Replace("\"kind\": \"first\"", "\"kind\": 7").Replace("\"kind\": \"waited\"", "\"kind\": \"layered\""));

        Assert.DoesNotContain("warm-up", Query("interactions", report));
        Assert.DoesNotContain("of the run", Query("flow", report, "s0"));
        Assert.DoesNotContain("waited for", Query("flow", report, "s1"));
    }

    private static Feature Orders(params (string Id, string Name, double? Seconds, ExecutionResult Result)[] scenarios) => new()
    {
        DisplayName = "Orders",
        Scenarios = scenarios.Select(s => new Scenario
        {
            Id = s.Id, DisplayName = s.Name, Result = s.Result, Duration = s.Seconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null
        }).ToArray()
    };

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
        var first = marked.Single(i => i.GetProperty("address").GetString() == "s0/i0").GetProperty("warmUp");
        Assert.Equal("first", first.GetProperty("kind").GetString());
        // A call that waited names the call it waited for by the address every verb takes, as flow does; until 4.16.3
        // it was the request's id, which no verb accepts, and the first call carried "first": null the report leaves out.
        Assert.False(first.TryGetProperty("first", out _));
        var waited = marked.Single(i => i.GetProperty("address").GetString() == "s1/i0").GetProperty("warmUp");
        Assert.Equal("waited", waited.GetProperty("kind").GetString());
        Assert.Equal("s0/i0", waited.GetProperty("first").GetString());
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
        // s0 (2.5 s) and s1 (2.2 s) took longer by wall time: the answer says why they are not in it (until 4.16.3 it
        // did not, and listed s2 at 2 s without them).
        Assert.Equal(["2 scenarios that took 1.95s or more are left out: the first-call warm-up they paid is not their own time"],
            json.RootElement.GetProperty("notes").EnumerateArray().Select(n => n.GetString()));
        Assert.Contains("\n2 scenarios that took 1.95s or more are left out: the first-call warm-up they paid is not their own time",
            Query("scenarios", report, "--slower-than", "1.95"));
    }

    [Fact]
    public void Scenarios_say_how_much_of_each_was_the_warm_up()
    {
        // The rows read wall time, which --slower-than does not, so a row says what it held of the warm-up.
        var report = Report("marked");

        var rows = Query("scenarios", report).Split('\n');

        Assert.Contains(rows, r => r.StartsWith("s0 ", StringComparison.Ordinal) && r.EndsWith("Place  0.6s warm-up", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.StartsWith("s1 ", StringComparison.Ordinal) && r.EndsWith("Place again  0.59s warm-up", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.StartsWith("s2 ", StringComparison.Ordinal) && r.EndsWith("Place twice", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(Query("scenarios", report, "--json"));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("address").GetString()!);
        Assert.Equal(0.6, items["s0"].GetProperty("warmUpSeconds").GetDouble(), 6);
        Assert.False(items["s2"].TryGetProperty("warmUpSeconds", out _));
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

    [Fact]
    public void Diff_reads_a_run_that_paid_no_warm_up_as_paying_none()
    {
        // A run written since the marks writes no key on a scenario that paid none, which until 4.16.3 read as a run
        // written before them: by wall time s0 went from 1.5 s to 2.5 s ("slower"), though 0.6 s of the 2.5 was the
        // warm-up that moved into it, and its own time went from 1.5 s to 1.9 s (not).
        var before = Report("before", firstMs: 5, firstScenarioSeconds: 1.5);
        var after = Report("after");

        Assert.DoesNotContain("Slower", Query("diff", before, after));
    }

    [Fact]
    public void Diff_finds_a_scenario_slower_by_its_own_time_when_the_new_run_paid_no_warm_up()
    {
        // The reverse: s0 took 2.5 s with 0.6 s of warm-up, then 3.0 s with none. By wall time 1.2 times (not slower); its
        // own time went from 1.9 s to 3.0 s.
        var before = Report("before");
        var after = Report("after", firstMs: 5, firstScenarioSeconds: 3.0);

        Assert.Contains("1.9s → 3s  Place", Query("diff", before, after));
    }

    [Fact]
    public void Diff_reads_a_run_written_before_the_marks_by_wall_time_on_both_sides()
    {
        // A report from before 4.8.0 says nothing about the warm-up: its 1.5 s may hold one, so both runs are read by wall
        // time, as they were.
        var before = Report("before", firstMs: 5, firstScenarioSeconds: 1.5);
        File.WriteAllText(before, Regex.Replace(ReportHtml.Read(before), "\"kronikolVersion\": \"[^\"]+\"", "\"kronikolVersion\": \"4.7.3\""));
        var after = Report("after");

        Assert.Contains("1.5s → 2.5s  Place", Query("diff", before, after));
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
        return Write(name, features, logs);
    }

    private string Write(string name, Feature[] features, List<RequestResponseLog> logs)
    {
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
