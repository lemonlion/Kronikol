using System.Net;
using System.Text.Json;
using AngleSharp;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// A merge carries each shard's warm-up marks and never runs the rule over the merged calls (plans/WARM_UP_PLAN.md 4.3,
/// T22): every shard is a process of its own, which paid its own warm-up, and judging one shard's first call against
/// another's calls would mark what neither process saw.
/// </summary>
// The ingest fact runs the report pipeline, whose diagram fetcher and log store are process-wide.
[Collection("DiagramsFetcher")]
public class MergeCarriesWarmUpTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-merge-warm-up").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Each_shards_first_call_keeps_its_mark()
    {
        // Shard B ran later: over the merged calls its 700 ms first call would be one more call after shard A's first.
        var a = Shard("a", startMs: 0, firstMs: 600, warmUp: null);
        var b = Shard("b", startMs: 5000, firstMs: 700, warmUp: null);

        using var merged = Merge(a, b);
        var marks = Records(merged).Where(r => r.TryGetProperty("warmUp", out _)).ToList();

        Assert.Equal(new[] { a.First.ToString(), b.First.ToString() }.Order(),
            marks.Select(r => r.GetProperty("requestResponseId").GetString()).Order());
        Assert.All(marks, r => Assert.Equal("first", r.GetProperty("warmUp").GetProperty("kind").GetString()));
        var warmUps = Scenarios(merged).Where(s => s.TryGetProperty("warmUpSeconds", out _))
            .ToDictionary(s => s.GetProperty("id").GetString()!, s => s.GetProperty("warmUpSeconds").GetDouble());
        Assert.Equal(0.6, warmUps["a1"], 6);
        Assert.Equal(0.7, warmUps["b1"], 6);
        // The merged report draws the marks it carries (R2).
        Assert.Equal(["600", "700"], MarkedInReport().Order());
    }

    [Fact]
    public void Shards_that_carry_no_marks_get_none_though_their_merged_calls_would()
    {
        // Written as a shard from before the marks: the merged calls hold a 600 ms first call and later 5 ms ones.
        var a = Shard("a", startMs: 0, firstMs: 600, warmUp: WarmUpResult.None);
        var b = Shard("b", startMs: 5000, firstMs: 5, warmUp: WarmUpResult.None);

        using var merged = Merge(a, b);

        Assert.DoesNotContain(Records(merged), r => r.TryGetProperty("warmUp", out _));
        Assert.DoesNotContain(Scenarios(merged), s => s.TryGetProperty("warmUpSeconds", out _));
        Assert.Empty(MarkedInReport());
    }

    [Fact]
    public void An_ingested_feed_is_marked_by_the_same_rule()
    {
        // T23: kronikol ingest reaches the run's own report pipeline, so it gets the pass with no code of its own.
        const string first = "0a1b2c3d4e5f60718293a4b5c6d7e8f0", later = "0a1b2c3d4e5f60718293a4b5c6d7e8f1";
        var captures = Path.Combine(_directory, "captures");
        Directory.CreateDirectory(captures);
        var lines = new List<string>();
        foreach (var (testId, startMs, durationMs) in new[] { (first, 0.0, 600.0), (later, 1000.0, 5.0), (later, 1100.0, 5.0) })
        {
            var (req, resp) = InteractionRecord.Pair(testId, null, "POST", "http://localhost:8081/orders", "orders", "web",
                requestContent: "{}", responseContent: "{}", statusCode: "200",
                requestTimestamp: T0.AddMilliseconds(startMs), responseTimestamp: T0.AddMilliseconds(startMs + durationMs));
            lines.Add(req.ToJson());
            lines.Add(resp.ToJson());
        }
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), lines);
        File.WriteAllLines(Path.Combine(captures, "tests.ndjson"),
        [
            new TestRunRecord { Event = "start", TestId = first, TestName = "orders › first", Feature = "orders.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = first, Status = "passed", DurationMs = 900, Timestamp = T0.AddMilliseconds(900) }.ToJson(),
            new TestRunRecord { Event = "start", TestId = later, TestName = "orders › later", Feature = "orders.spec.ts", Timestamp = T0.AddMilliseconds(950) }.ToJson(),
            new TestRunRecord { Event = "end", TestId = later, Status = "passed", DurationMs = 300, Timestamp = T0.AddMilliseconds(1250) }.ToJson(),
        ]);
        var output = Path.Combine(_directory, "out");
        var err = new StringWriter();

        var exit = IngestCommand.Run([captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", output], new StringWriter(), err);

        Assert.True(exit == 0, err.ToString());
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "TestRunReport.json")));
        var mark = Assert.Single(Records(json), r => r.TryGetProperty("warmUp", out _));
        Assert.Equal("first", mark.GetProperty("warmUp").GetProperty("kind").GetString());
        Assert.Equal(0.6, Assert.Single(Scenarios(json), s => s.TryGetProperty("warmUpSeconds", out _)).GetProperty("warmUpSeconds").GetDouble(), 6);
        Assert.Equal(["600"], MarkedInReport(Path.Combine(output, "TestRunReport.html")));
    }

    private (string Path, Guid First) Shard(string name, double startMs, double firstMs, WarmUpResult? warmUp)
    {
        var logs = new List<RequestResponseLog>();
        var firstCall = Call(logs, name + "1", startMs, firstMs);
        Call(logs, name + "2", startMs + 1000, 5);
        Call(logs, name + "2", startMs + 1100, 5);
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders " + name,
                Scenarios =
                [
                    new Scenario { Id = name + "1", DisplayName = "First " + name, Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) },
                    new Scenario { Id = name + "2", DisplayName = "Later " + name, Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) }
                ]
            }
        ];
        var path = System.IO.Path.Combine(_directory, "shards", name + ".json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ReportGenerator.GenerateMergeableReportJson(features, T0.UtcDateTime, T0.UtcDateTime.AddMinutes(1),
            Array.Empty<DiagramAsCode>().ToLookup(d => d.TestRuntimeId, d => d.CodeBehind), [], internalFlowSegmentData: null, wholeTestFlow: null,
            WholeTestFlowVisualization.None, ciMetadata: null, diagnostics: null, trackedLogs: logs.ToArray(), warmUp: warmUp));
        return (path, firstCall);
    }

    private JsonDocument Merge(params (string Path, Guid First)[] shards)
    {
        var error = new StringWriter();
        var exit = MergeCommand.Run([System.IO.Path.Combine(_directory, "shards"), "-o", System.IO.Path.Combine(_directory, "Combined.html")], new StringWriter(), error);
        Assert.True(exit == 0, error.ToString());
        Assert.Equal(2, shards.Length);
        return JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(_directory, "Combined.json")));
    }

    private static Guid Call(List<RequestResponseLog> logs, string scenario, double startMs, double durationMs)
    {
        var (trace, id) = (Guid.NewGuid(), Guid.NewGuid());
        var uri = new Uri("http://orders/orders");
        logs.Add(new RequestResponseLog(scenario, scenario, HttpMethod.Post, "{}", uri, [], "orders", "Test", RequestResponseType.Request, trace, id, false)
            { Timestamp = T0.AddTicks((long)Math.Round(startMs * TimeSpan.TicksPerMillisecond)) });
        logs.Add(new RequestResponseLog(scenario, scenario, HttpMethod.Post, "{}", uri, [], "orders", "Test", RequestResponseType.Response, trace, id, false, HttpStatusCode.OK)
            { Timestamp = T0.AddTicks((long)Math.Round((startMs + durationMs) * TimeSpan.TicksPerMillisecond)) });
        return id;
    }

    /// <summary>The <c>data-warmup-ms</c> of every scenario the merged (or ingested) report draws a mark on.</summary>
    private string[] MarkedInReport(string? path = null)
    {
        var html = File.ReadAllText(path ?? System.IO.Path.Combine(_directory, "Combined.html"));
        var document = BrowsingContext.New(AngleSharp.Configuration.Default).OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();
        return document.QuerySelectorAll("details.scenario[data-warmup-ms]").Select(e => e.GetAttribute("data-warmup-ms")!).ToArray();
    }

    private static IEnumerable<JsonElement> Scenarios(JsonDocument json) =>
        json.RootElement.GetProperty("features").EnumerateArray().SelectMany(f => f.GetProperty("scenarios").EnumerateArray());

    private static IEnumerable<JsonElement> Records(JsonDocument json) =>
        Scenarios(json).SelectMany(s => s.TryGetProperty("httpInteractions", out var i) ? i.EnumerateArray() : Enumerable.Empty<JsonElement>());
}
