using System.Diagnostics;
using Kronikol.ComponentDiagram;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Tests.InternalFlow;

public class InternalFlowHtmlGeneratorTests : IDisposable
{
    private readonly ActivitySource _source = new("Kronikol.Tests.HtmlGenerator");
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public InternalFlowHtmlGeneratorTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        foreach (var a in _activities) a.Dispose();
        _listener.Dispose();
        _source.Dispose();
    }

    private Activity CreateSpan(string name, TimeSpan? duration = null)
    {
        var span = _source.StartActivity(name)!;
        span.SetEndTime(span.StartTimeUtc + (duration ?? TimeSpan.FromMilliseconds(10)));
        _activities.Add(span);
        return span;
    }

    private static InternalFlowSegment MakeSegment(string testId, params Activity[] spans) =>
        new(Guid.Empty, RequestResponseType.Request, testId,
            spans.Length > 0 ? spans.Min(s => s.StartTimeUtc) : null,
            spans.Length > 0 ? spans.Max(s => s.StartTimeUtc + s.Duration) : null,
            spans);

    // ── GetWholeTestFlowContent ──

    [Fact]
    public void GetWholeTestFlowContent_returns_null_for_empty_segments()
    {
        var segments = new Dictionary<string, InternalFlowSegment>();
        var result = InternalFlowHtmlGenerator.GetWholeTestFlowContent(
            segments, "test-1", [], WholeTestFlowVisualization.Both);

        Assert.Null(result);
    }

    [Fact]
    public void GetWholeTestFlowContent_returns_null_for_None()
    {
        var span = CreateSpan("op");
        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-test-test-1"] = MakeSegment("test-1", span)
        };

        var result = InternalFlowHtmlGenerator.GetWholeTestFlowContent(
            segments, "test-1", [], WholeTestFlowVisualization.None);

        Assert.Null(result);
    }

    [Fact]
    public void GetWholeTestFlowContent_Both_returns_activity_and_flame()
    {
        var span = CreateSpan("op", TimeSpan.FromMilliseconds(100));
        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-test-test-1"] = MakeSegment("test-1", span)
        };

        var result = InternalFlowHtmlGenerator.GetWholeTestFlowContent(
            segments, "test-1", [], WholeTestFlowVisualization.Both);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Value.ActivityHtml);
        Assert.NotEmpty(result.Value.FlameHtml);
        Assert.DoesNotContain("<details", result.Value.ActivityHtml);
        Assert.DoesNotContain("<details", result.Value.FlameHtml);
    }

    [Fact]
    public void GetWholeTestFlowContent_Both_does_not_include_toggle_buttons()
    {
        var span = CreateSpan("op", TimeSpan.FromMilliseconds(100));
        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-test-test-1"] = MakeSegment("test-1", span)
        };

        var result = InternalFlowHtmlGenerator.GetWholeTestFlowContent(
            segments, "test-1", [], WholeTestFlowVisualization.Both);

        Assert.NotNull(result);
        Assert.DoesNotContain("iflow-toggle", result.Value.ActivityHtml);
        Assert.DoesNotContain("iflow-toggle", result.Value.FlameHtml);
    }

    [Fact]
    public void GetWholeTestFlowContent_FlameChart_only_has_flame()
    {
        var span = CreateSpan("op", TimeSpan.FromMilliseconds(100));
        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-test-test-1"] = MakeSegment("test-1", span)
        };

        var result = InternalFlowHtmlGenerator.GetWholeTestFlowContent(
            segments, "test-1", [], WholeTestFlowVisualization.FlameChart);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Value.FlameHtml);
        Assert.Empty(result.Value.ActivityHtml);
    }

    [Fact]
    public void GetWholeTestFlowContent_ActivityDiagram_only_has_activity()
    {
        var span = CreateSpan("op", TimeSpan.FromMilliseconds(100));
        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-test-test-1"] = MakeSegment("test-1", span)
        };

        var result = InternalFlowHtmlGenerator.GetWholeTestFlowContent(
            segments, "test-1", [], WholeTestFlowVisualization.ActivityDiagram);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Value.ActivityHtml);
        Assert.Empty(result.Value.FlameHtml);
    }

    [Fact]
    public void GetWholeTestFlowContent_no_matching_test_returns_null()
    {
        var span = CreateSpan("op");
        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-test-test-1"] = MakeSegment("test-1", span)
        };

        var result = InternalFlowHtmlGenerator.GetWholeTestFlowContent(
            segments, "test-999", [], WholeTestFlowVisualization.Both);

        Assert.Null(result);
    }

    [Fact]
    public void GetWholeTestFlowContent_FlameChart_uses_compressed_attribute()
    {
        var span = CreateSpan("op", TimeSpan.FromMilliseconds(100));
        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-test-test-1"] = MakeSegment("test-1", span)
        };

        var result = InternalFlowHtmlGenerator.GetWholeTestFlowContent(
            segments, "test-1", [], WholeTestFlowVisualization.FlameChart);

        Assert.NotNull(result);
        Assert.Contains("data-flame-z=", result.Value.FlameHtml);
        Assert.DoesNotContain("data-flame=\"", result.Value.FlameHtml);
    }

    [Fact]
    public void GetWholeTestFlowContent_ActivityDiagram_uses_compressed_attribute()
    {
        var span = CreateSpan("op", TimeSpan.FromMilliseconds(100));
        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-test-test-1"] = MakeSegment("test-1", span)
        };

        var result = InternalFlowHtmlGenerator.GetWholeTestFlowContent(
            segments, "test-1", [], WholeTestFlowVisualization.ActivityDiagram);

        Assert.NotNull(result);
        Assert.Contains("data-plantuml-z=", result.Value.ActivityHtml);
        Assert.DoesNotContain("data-plantuml=\"", result.Value.ActivityHtml);
    }

    [Fact]
    public void GetWholeTestFlowContent_many_spans_produces_multiple_activity_divs()
    {
        // Create enough independent root spans to trigger batching (default 500)
        var spans = new List<Activity>();
        for (int i = 0; i < 600; i++)
        {
            Activity.Current = null;
            var s = _source.StartActivity($"op{i}", ActivityKind.Internal,
                new ActivityContext(ActivityTraceId.CreateRandom(), default, ActivityTraceFlags.Recorded))!;
            s.SetEndTime(s.StartTimeUtc + TimeSpan.FromMilliseconds(10));
            _activities.Add(s);
            spans.Add(s);
        }

        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-test-test-1"] = MakeSegment("test-1", spans.ToArray())
        };

        var result = InternalFlowHtmlGenerator.GetWholeTestFlowContent(
            segments, "test-1", [], WholeTestFlowVisualization.ActivityDiagram);

        Assert.NotNull(result);
        // Should have multiple plantuml-browser divs
        var divCount = System.Text.RegularExpressions.Regex.Matches(result.Value.ActivityHtml, "plantuml-browser").Count;
        Assert.True(divCount >= 2, $"Expected multiple batched divs but got {divCount}");
    }

    [Fact]
    public void CompressToBase64_round_trips_correctly()
    {
        var original = "{\"s\":[\"test\"],\"f\":[[0,\"operation\",1.23,45.67,0,100]]}";
        var compressed = InternalFlowHtmlGenerator.CompressToBase64(original);

        // Decompress to verify
        var bytes = Convert.FromBase64String(compressed);
        using var input = new System.IO.MemoryStream(bytes);
        using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new System.IO.StreamReader(gzip);
        var decompressed = reader.ReadToEnd();

        Assert.Equal(original, decompressed);
    }

    // ── GenerateRelationshipPopupContent ──

    [Fact]
    public void GenerateRelationshipPopupContent_ActivityDiagram_ContainsPlantUmlDiv()
    {
        var span = CreateSpan("op1", TimeSpan.FromMilliseconds(50));
        var segment = MakeSegment("aggregated", span);
        var flowData = new RelationshipFlowData(segment, [
            new RelationshipTestSummary("t1", "Test One", 1, 50)
        ]);

        var result = InternalFlowHtmlGenerator.GenerateRelationshipPopupContent(
            flowData, InternalFlowDiagramStyle.ActivityDiagram);

        Assert.Contains("plantuml-browser", result);
        Assert.Contains("iflow-rel-summary-table", result);
    }

    [Fact]
    public void GenerateRelationshipPopupContent_CallTree_ContainsCallTreeHtml()
    {
        var span = CreateSpan("op1", TimeSpan.FromMilliseconds(50));
        var segment = MakeSegment("aggregated", span);
        var flowData = new RelationshipFlowData(segment, [
            new RelationshipTestSummary("t1", "Test One", 1, 50)
        ]);

        var result = InternalFlowHtmlGenerator.GenerateRelationshipPopupContent(
            flowData, InternalFlowDiagramStyle.CallTree);

        Assert.Contains("iflow-call-tree", result);
        Assert.DoesNotContain("plantuml-browser", result);
    }

    [Fact]
    public void GenerateRelationshipPopupContent_IncludesSummaryTableWithTestInfo()
    {
        var span = CreateSpan("op1", TimeSpan.FromMilliseconds(50));
        var segment = MakeSegment("aggregated", span);
        var flowData = new RelationshipFlowData(segment, [
            new RelationshipTestSummary("t1", "My Test Name", 3, 120),
            new RelationshipTestSummary("t2", "Another Test", 1, 30)
        ]);

        var result = InternalFlowHtmlGenerator.GenerateRelationshipPopupContent(
            flowData, InternalFlowDiagramStyle.ActivityDiagram);

        Assert.Contains("iflow-rel-summary-table", result);
        Assert.Contains("My Test Name", result);
        Assert.Contains("Another Test", result);
        Assert.Contains("3", result); // SpanCount
        Assert.Contains("120ms", result); // duration
    }

    [Fact]
    public void GenerateRelationshipPopupContent_ManyTests_LimitsToTopTwenty()
    {
        var span = CreateSpan("op1", TimeSpan.FromMilliseconds(50));
        var segment = MakeSegment("aggregated", span);
        var summaries = Enumerable.Range(1, 25)
            .Select(i => new RelationshipTestSummary($"t{i}", $"Test {i}", 1, i * 10.0))
            .OrderByDescending(s => s.DurationMs)
            .ToArray();
        var flowData = new RelationshipFlowData(segment, summaries);

        var result = InternalFlowHtmlGenerator.GenerateRelationshipPopupContent(
            flowData, InternalFlowDiagramStyle.ActivityDiagram);

        // Should show top 20 and a "more" indicator
        Assert.Contains("and 5 more", result);
        // Should contain the highest duration test (Test 25)
        Assert.Contains("Test 25", result);
    }

    [Fact]
    public void A_segment_map_activity_diagram_carries_its_plantuml_raw_in_the_attribute()
    {
        // The map is gzipped whole (3.31.9), so its diagrams are no gzip islands (INTERNAL_FLOW_BLOB_PLAN §3.7, Q7).
        var span = CreateSpan("SELECT \"id\" FROM t WHERE a < 1 && b\tc", TimeSpan.FromMilliseconds(5));
        var segment = new InternalFlowSegment(Guid.NewGuid(), RequestResponseType.Request, "t1",
            span.StartTimeUtc, span.StartTimeUtc + span.Duration, [span]);

        var data = InternalFlowHtmlGenerator.BuildSegmentData(
            new Dictionary<string, InternalFlowSegment> { ["iflow-a"] = segment }, InternalFlowDiagramStyle.ActivityDiagram);
        var content = System.Text.Json.JsonSerializer.SerializeToElement(data["iflow-a"]).GetProperty("content").GetString()!;

        Assert.DoesNotContain("data-plantuml-z", content, StringComparison.Ordinal);
        var attribute = System.Text.RegularExpressions.Regex.Match(content, "data-plantuml=\"([^\"]*)\"");
        Assert.True(attribute.Success);
        Assert.Equal(InternalFlowRenderer.RenderActivityDiagram(segment), System.Net.WebUtility.HtmlDecode(attribute.Groups[1].Value));
        Assert.Contains("&quot;id&quot;", attribute.Groups[1].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityDiagram_div_does_not_contain_inner_loading_text()
    {
        var span = CreateSpan("op", TimeSpan.FromMilliseconds(100));
        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-test-test-1"] = MakeSegment("test-1", span)
        };

        var result = InternalFlowHtmlGenerator.GetWholeTestFlowContent(
            segments, "test-1", [], WholeTestFlowVisualization.ActivityDiagram);

        Assert.NotNull(result);
        // The div should not contain "Loading..." text since CSS ::before handles loading messages
        Assert.DoesNotContain(">Loading...</div>", result.Value.ActivityHtml);
    }

    [Fact]
    public void The_empty_segment_message_is_markup_with_the_configured_sources_encoded()
    {
        // The popup renders a segment's message as markup (the diagnostic is a <details> list), so a value the user
        // configured goes in encoded.
        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-empty"] = new(Guid.NewGuid(), RequestResponseType.Request, "t", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [])
        };

        var data = InternalFlowHtmlGenerator.BuildSegmentData(segments, InternalFlowDiagramStyle.ActivityDiagram,
            noDataBehavior: InternalFlowNoDataBehavior.ShowMessage, granularity: InternalFlowSpanGranularity.Manual,
            configuredActivitySources: ["Orders<b>"]);

        var message = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data["iflow-empty"]))
            .RootElement.GetProperty("message").GetString()!;
        Assert.Contains("<details", message, StringComparison.Ordinal);
        Assert.Contains("Orders&lt;b&gt;", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Orders<b>", message, StringComparison.Ordinal);
    }
}
