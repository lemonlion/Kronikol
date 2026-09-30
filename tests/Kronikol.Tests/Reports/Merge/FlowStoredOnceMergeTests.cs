using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Kronikol.ComponentDiagram;
using Kronikol.InternalFlow;
using Kronikol.Reports;
using Kronikol.Tests.InternalFlow;
using Kronikol.Tool;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// <c>kronikol merge</c> over shards whose segment maps store each shared flow once (#86, <c>plans/V4_PLAN.md</c>
/// R5): a segment names the copy by another segment's key, a call's id, which no other shard holds, so the merge,
/// which keeps the first value per key, keeps every copy a segment names. A shard written before, with every flow
/// inline, merges beside them. From 4.0.1 each shard's map ends in a table of its diagrams and flame charts, under its
/// first segment's key with a '~' before it, which no other shard holds, so the merge keeps every table as well.
/// </summary>
public class FlowStoredOnceMergeTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kronikol-flow-merge-" + Guid.NewGuid().ToString("N"));
    private readonly ActivitySource _source = new("Kronikol.Tests.FlowStoredOnceMerge");
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public FlowStoredOnceMergeTests()
    {
        Directory.CreateDirectory(_directory);
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Kronikol.Tests.FlowStoredOnceMerge",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        foreach (var a in _activities) a.Dispose();
        _listener.Dispose();
        _source.Dispose();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Shards_that_store_their_flows_once_merge_with_every_named_copy_in_place()
    {
        var spans = new[] { Span("SELECT orders", 60), Span("SELECT lines", 70) };
        var a = WriteShard("runner1.json", "a1", spans, storeOnce: true);
        var b = WriteShard("runner2.json", "b1", spans, storeOnce: true);

        var merged = Merge();

        Assert.Equal(a[0], merged.GetProperty(a[1]).GetProperty("sameAs").GetString());
        Assert.Equal(b[0], merged.GetProperty(b[1]).GetProperty("sameAs").GetString());
        AssertEveryPointerResolves(merged);
        AssertEveryPointerResolves(PageMap(File.ReadAllText(Path.Combine(_directory, "Combined.html"))));
    }

    [Fact]
    public void A_shard_with_every_flow_inline_merges_beside_one_that_stores_them_once()
    {
        var spans = new[] { Span("SELECT orders", 60), Span("SELECT lines", 70) };
        var inline = WriteShard("runner1.json", "a1", spans, storeOnce: false);
        var stored = WriteShard("runner2.json", "b1", spans, storeOnce: true);

        var merged = Merge();

        foreach (var key in inline)
            Assert.Contains("SELECT orders", merged.GetProperty(key).GetProperty("content").GetString());
        Assert.Equal(stored[0], merged.GetProperty(stored[1]).GetProperty("sameAs").GetString());
        AssertEveryPointerResolves(merged);
    }

    [Fact]
    public void Shards_that_store_their_diagrams_in_a_table_merge_with_every_table_in_place()
    {
        // The second call of each shard starts a span later: the first call's diagram, a flame chart of its own. Each
        // shard's map ends in a table under its first segment's key, which the other shard does not hold (4.0.1).
        var first = new[] { Span("SELECT orders", 60), Span("SELECT lines", 70) };
        var later = new[] { Span("SELECT orders", 60), Span("SELECT lines", 90) };
        var a = WriteShard("runner1.json", "a1", first, storeOnce: true, secondSpans: later);
        var b = WriteShard("runner2.json", "b1", first, storeOnce: true, secondSpans: later);

        var merged = Merge();

        foreach (var shard in new[] { a, b })
        {
            Assert.Equal("~" + shard[0], merged.GetProperty(shard[1]).GetProperty("table").GetString());
            Assert.Equal(merged.GetProperty(shard[0]).GetProperty("contentAt").GetInt32(), merged.GetProperty(shard[1]).GetProperty("contentAt").GetInt32());
            Assert.NotEqual(SegmentMapText.Resolve(merged, shard[0]).FlameData, SegmentMapText.Resolve(merged, shard[1]).FlameData);
        }
        AssertEveryPointerResolves(merged);
        AssertEveryPointerResolves(PageMap(File.ReadAllText(Path.Combine(_directory, "Combined.html"))));
    }

    // ─── Fixture ───────────────────────────────────────────────

    /// <summary>Every segment that names another or a table resolves, as the popup script resolves it, to a diagram.</summary>
    private static void AssertEveryPointerResolves(JsonElement map)
    {
        var pointers = map.EnumerateObject()
            .Where(p => p.Value.TryGetProperty("sameAs", out _) || p.Value.TryGetProperty("table", out _)).ToArray();
        Assert.NotEmpty(pointers);
        Assert.All(pointers, p => Assert.True(SegmentMapText.Resolve(map, p.Name).Content is { Length: > 0 },
            $"{p.Name} resolves to a diagram the map holds"));
    }

    /// <summary>A shard of one scenario whose two calls, one nested in the other, show <paramref name="spans"/>, or the
    /// second <paramref name="secondSpans"/> when given.</summary>
    private string[] WriteShard(string name, string scenarioId, Activity[] spans, bool storeOnce, Activity[]? secondSpans = null)
    {
        var at = new DateTimeOffset(Start).AddSeconds(1);
        Guid[] calls = [Guid.NewGuid(), Guid.NewGuid()];
        var segments = calls.ToDictionary(id => $"iflow-{id}",
            id => new InternalFlowSegment(id, RequestResponseType.Request, scenarioId, at, at.AddMilliseconds(200),
                id == calls[1] && secondSpans is not null ? secondSpans : spans));
        var data = InternalFlowHtmlGenerator.BuildSegmentData(segments, InternalFlowDiagramStyle.ActivityDiagram, showFlameChart: true);
        var logs = calls.SelectMany(id => new RequestResponseLog[]
        {
            new(scenarioId, scenarioId, HttpMethod.Get, null, new Uri("http://orders/x"), [], "Orders", "Test",
                RequestResponseType.Request, Guid.NewGuid(), id, false) { Timestamp = at },
            new(scenarioId, scenarioId, HttpMethod.Get, "{}", new Uri("http://orders/x"), [], "Orders", "Test",
                RequestResponseType.Response, Guid.NewGuid(), id, false, HttpStatusCode.OK) { Timestamp = at.AddMilliseconds(200) },
        }).ToArray();
        var diagram = "@startuml\n" + string.Concat(calls.Select(id => $"Test -> Orders : [[#iflow-{id}]] GET /x\n")) + "@enduml";

        var json = ReportGenerator.GenerateMergeableReportJson(
            [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = scenarioId, DisplayName = "Order " + scenarioId, Result = ExecutionResult.Passed }] }],
            Start, Start.AddMinutes(1),
            new[] { new DiagramAsCode(scenarioId, "", diagram) }.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            [new ComponentRelationship("Test", "Orders", "HTTP", new HashSet<string> { "GET /x" }, 2, 2, "http")],
            internalFlowSegmentData: storeOnce ? InternalFlowHtmlGenerator.StoreFlowsOnce(data) : data,
            wholeTestFlow: null, WholeTestFlowVisualization.None, ciMetadata: null, diagnostics: null, trackedLogs: logs);
        File.WriteAllText(Path.Combine(_directory, name), json);
        return segments.Keys.ToArray();
    }

    private JsonElement Merge()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = MergeCommand.Run([_directory, "-o", Path.Combine(_directory, "Combined.html")], output, error);
        Assert.True(exit == 0, error.ToString());
        using var combined = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "Combined.json")));
        return combined.RootElement.GetProperty("internalFlowSegments").Clone();
    }

    private static JsonElement PageMap(string html)
    {
        const string head = "<script id=\"iflow-segments\" type=\"application/json\">";
        var start = html.IndexOf(head, StringComparison.Ordinal);
        Assert.True(start >= 0, "the merged page carries a segment element");
        start += head.Length;
        using var element = JsonDocument.Parse(html[start..html.IndexOf("</script>", start, StringComparison.Ordinal)]);
        using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(element.RootElement.GetProperty("z").GetString()!)), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        using var map = JsonDocument.Parse(reader.ReadToEnd());
        return map.RootElement.Clone();
    }

    private Activity Span(string name, int startMs)
    {
        Activity.Current = null;
        var span = _source.StartActivity(name, ActivityKind.Internal,
            new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded))!;
        span.SetStartTime(Start.AddSeconds(1).AddMilliseconds(startMs));
        span.SetEndTime(Start.AddSeconds(1).AddMilliseconds(startMs + 5));
        _activities.Add(span);
        return span;
    }
}
