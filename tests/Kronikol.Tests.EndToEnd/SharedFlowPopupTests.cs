namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// #86 on the page (<c>plans/V4_PLAN.md</c> R5), on a report the whole pipeline wrote with its defaults: a call and the
/// call nested in it show the same two queries, so the page's segment map stores that flow once, in the first
/// segment, and the other names it. Each arrow still opens its popup, with its own title and the flow. From 4.0.1 the
/// segment holding a flow names its diagram and flame chart by their places in the table the map ends in: a third call
/// running the same queries at other moments names the first call's diagram and a flame chart of its own, a call
/// nested in it names its flow, and each popup draws the diagram and the flame chart its segment names.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class SharedFlowPopupTests : PlaywrightTestBase
{
    public SharedFlowPopupTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 1280;
    protected override int ViewportHeight => 900;

    [Fact]
    public async Task Each_arrow_of_a_flow_stored_once_opens_that_flow()
    {
        var (uri, _, outer, inner, _, _) = ReportTestHelper.GenerateRunReportWithSharedFlow(TempDir, OutputDir, "SharedFlow.html");

        // Stored once: the outer call's segment holds the flow and the inner call's names it. Read by the calls' own
        // keys: the map also holds the segments of every other fixture this process has logged.
        var map = SegmentMap(File.ReadAllText(new Uri(uri).LocalPath));
        Assert.Contains("SELECT orders", Diagram(map, $"iflow-{outer}"), StringComparison.Ordinal);
        Assert.Equal($"iflow-{outer}", map.GetProperty($"iflow-{inner}").GetProperty("sameAs").GetString());
        Assert.False(map.GetProperty($"iflow-{inner}").TryGetProperty("content", out _), "the inner call's segment holds no copy");

        await Page.GotoAsync(uri);
        await Page.EvaluateAsync("() => document.querySelectorAll('details').forEach(d => d.open = true)");
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync(BrowserRenderWorkerTests.AllRenderedJs, null, new() { Timeout = 60000, PollingInterval = 200 });

        foreach (var path in new[] { "/shared-outer", "/shared-inner" })
        {
            // SVG rule: a dispatched click, on the arrow's own text.
            await Page.Locator(".plantuml-browser:not(.iflow-diagram) svg text", new() { HasTextString = path }).First
                .EvaluateAsync("t => t.dispatchEvent(new MouseEvent('click', { bubbles: true }))");
            var popup = Page.Locator(".iflow-popup");
            await Expect(popup.Locator("h3")).ToHaveTextAsync("Internal Flow (2 spans)");
            await popup.Locator(".iflow-diagram svg").First.WaitForAsync(new() { Timeout = PopupFirstDrawTimeout });
            var diagram = await popup.Locator(".iflow-diagram svg").First.TextContentAsync();
            Assert.Contains("SELECT orders", diagram);
            Assert.Contains("SELECT lines", diagram);
            await popup.Locator(".iflow-popup-close").ClickAsync();
        }
    }

    [Fact]
    public async Task Each_arrow_of_a_diagram_stored_once_opens_it_with_its_own_flame_chart()
    {
        var (uri, _, outer, _, later, laterInner) = ReportTestHelper.GenerateRunReportWithSharedFlow(TempDir, OutputDir, "SharedDiagram.html");

        var map = SegmentMap(File.ReadAllText(new Uri(uri).LocalPath));
        var outerSegment = map.GetProperty($"iflow-{outer}");
        var laterSegment = map.GetProperty($"iflow-{later}");
        Assert.Equal(outerSegment.GetProperty("contentAt").GetInt32(), laterSegment.GetProperty("contentAt").GetInt32());
        Assert.NotEqual(outerSegment.GetProperty("flameAt").GetInt32(), laterSegment.GetProperty("flameAt").GetInt32());
        Assert.Equal($"iflow-{later}", map.GetProperty($"iflow-{laterInner}").GetProperty("sameAs").GetString());

        await Page.GotoAsync(uri);
        await Page.EvaluateAsync("() => document.querySelectorAll('details').forEach(d => d.open = true)");
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync(BrowserRenderWorkerTests.AllRenderedJs, null, new() { Timeout = 60000, PollingInterval = 200 });

        // Where the second query starts, as each popup's flame chart places it: 20 ms into a 30 ms flow for the outer
        // call, 40 ms into 50 ms for the later one and the call nested in it.
        var secondQueryAt = new Dictionary<string, string?>();
        foreach (var path in new[] { "/shared-outer", "/shared-later", "/shared-later-inner" })
        {
            await Page.Locator(".plantuml-browser:not(.iflow-diagram) svg text", new() { HasTextString = path }).First
                .EvaluateAsync("t => t.dispatchEvent(new MouseEvent('click', { bubbles: true }))");
            var popup = Page.Locator(".iflow-popup");
            await Expect(popup.Locator("h3")).ToHaveTextAsync("Internal Flow (2 spans)");
            await popup.Locator(".iflow-diagram svg").First.WaitForAsync(new() { Timeout = PopupFirstDrawTimeout });
            var diagram = await popup.Locator(".iflow-diagram svg").First.TextContentAsync();
            Assert.Contains("SELECT orders", diagram);
            Assert.Contains("SELECT lines", diagram);

            await popup.Locator(".iflow-toggle-btn[data-view='flame']").ClickAsync();
            var bar = popup.Locator(".iflow-flame-bar", new() { HasTextString = "SELECT lines" }).First;
            await bar.WaitForAsync(new() { Timeout = 10000 });
            secondQueryAt[path] = await bar.GetAttributeAsync("data-left");
            await popup.Locator(".iflow-popup-close").ClickAsync();
        }

        Assert.Equal("66.6700", secondQueryAt["/shared-outer"]);
        Assert.Equal("80.0000", secondQueryAt["/shared-later"]);
        Assert.Equal("80.0000", secondQueryAt["/shared-later-inner"]);
    }

    /// <summary>A segment's diagram as the popup script finds it: through <c>sameAs</c> to its flow, then the flow's place
    /// in the map's table, or inline in a map written before 4.0.1.</summary>
    private static string? Diagram(System.Text.Json.JsonElement map, string key)
    {
        var segment = map.GetProperty(key);
        var flow = segment.TryGetProperty("sameAs", out var sameAs) ? map.GetProperty(sameAs.GetString()!) : segment;
        return flow.TryGetProperty("table", out var table)
            ? map.GetProperty(table.GetString()!).GetProperty("contents")[flow.GetProperty("contentAt").GetInt32()].GetString()
            : flow.GetProperty("content").GetString();
    }

    /// <summary>The page's segment map, decoded from its element's <c>z</c>.</summary>
    private static System.Text.Json.JsonElement SegmentMap(string html)
    {
        const string head = "<script id=\"iflow-segments\" type=\"application/json\">";
        var start = html.IndexOf(head, StringComparison.Ordinal);
        Assert.True(start >= 0, "the page carries a segment element");
        start += head.Length;
        using var element = System.Text.Json.JsonDocument.Parse(html[start..html.IndexOf("</script>", start, StringComparison.Ordinal)]);
        using var gzip = new System.IO.Compression.GZipStream(
            new MemoryStream(Convert.FromBase64String(element.RootElement.GetProperty("z").GetString()!)), System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return System.Text.Json.JsonDocument.Parse(reader.ReadToEnd()).RootElement.Clone();
    }
}
