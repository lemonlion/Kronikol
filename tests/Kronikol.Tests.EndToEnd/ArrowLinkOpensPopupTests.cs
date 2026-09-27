namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The report's main internal-flow interaction on a report the whole pipeline wrote: an arrow of a drawn sequence
/// diagram, clicked, opens its segment (INTERNAL_FLOW_BLOB_PLAN S0, F6: until now every popup test pressed a button
/// that called <c>_iflowShowPopup</c>), and the render script binds exactly the arrows that have a segment, in the same
/// tick as it draws, from the segment element's id list (§3.3), whichever list the page carries.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class ArrowLinkOpensPopupTests : PlaywrightTestBase
{
    public ArrowLinkOpensPopupTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 1280;
    protected override int ViewportHeight => 900;

    private async Task OpenAndDraw(string uri)
    {
        await Page.GotoAsync(uri);
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync(BrowserRenderWorkerTests.AllRenderedJs, null, new() { Timeout = 60000, PollingInterval = 200 });
    }

    /// <summary>
    /// For each text of the drawn sequence diagram naming <paramref name="pathPart"/>: whether it takes pointer events,
    /// and its fill. The whole-test flow's activity diagram names the same paths in its spans, so it is left out.
    /// </summary>
    private Task<string[]> Binding(string pathPart) => Page.EvaluateAsync<string[]>("""
        part => Array.from(document.querySelectorAll('.plantuml-browser:not(.iflow-diagram) svg text'))
            .filter(t => t.textContent.includes(part))
            .map(t => (t.style.pointerEvents || 'none') + '|' + (t.getAttribute('fill') || '').toUpperCase())
        """, pathPart);

    private Task<string> ListInPage() => Page.EvaluateAsync<string>(
        "() => Object.keys(JSON.parse(document.getElementById('iflow-segments').textContent))[0]");

    [Fact]
    public async Task Clicking_an_arrow_that_has_a_segment_opens_its_popup()
    {
        var (uri, _) = ReportTestHelper.GenerateRunReportWithFlowArrows(TempDir, OutputDir, "ArrowLink_Click.html", withFlow: 2, withoutFlow: 1);
        await OpenAndDraw(uri);

        // SVG rule: a dispatched click, on the arrow's own text (the browser engine draws the link as text, no <a>).
        await Page.EvaluateAsync("""
            () => Array.from(document.querySelectorAll('.plantuml-browser:not(.iflow-diagram) svg text'))
                .find(t => t.textContent.includes('with-flow-0') && !t.textContent.includes('without'))
                .dispatchEvent(new MouseEvent('click', { bubbles: true }))
            """);

        var popup = Page.Locator(".iflow-popup");
        await Expect(popup).ToBeVisibleAsync();
        await Expect(popup.Locator("h3")).ToHaveTextAsync("Internal Flow (2 spans)");
        await popup.Locator(".iflow-diagram svg").First.WaitForAsync(new() { Timeout = 30000 });
        Assert.Contains("GET /with-flow-0", await popup.Locator(".iflow-diagram svg").First.TextContentAsync());
    }

    [Fact]
    public async Task A_merged_report_binds_and_opens_the_arrows_of_every_shard()
    {
        // §4: the merge renderer builds the element from the merged map, its id list from the merged diagrams.
        var (_, shardA) = ReportTestHelper.GenerateRunReportWithFlowArrows(TempDir, OutputDir, "ArrowLink_ShardA.html", 1, 1, name: "Shard A", mergeableData: true);
        var (_, shardB) = ReportTestHelper.GenerateRunReportWithFlowArrows(TempDir, OutputDir, "ArrowLink_ShardB.html", 2, 0, name: "Shard B", mergeableData: true);
        var merged = Kronikol.Reports.Merge.MergeableReportMerger.Merge([
            Kronikol.Reports.Merge.MergeableReportReader.ReadFile(Path.Combine(shardA, "TestRunReport.json")),
            Kronikol.Reports.Merge.MergeableReportReader.ReadFile(Path.Combine(shardB, "TestRunReport.json"))]);
        var written = Kronikol.Reports.Merge.MergeableReportRenderer.Render(merged, Path.Combine(TempDir, "merged-" + Guid.NewGuid().ToString("N"), "TestRunReport.html"));
        File.Copy(written, Path.Combine(OutputDir, "ArrowLink_Merged.html"), true);

        await Page.GotoAsync(new Uri(written).AbsoluteUri);
        await Page.EvaluateAsync("() => document.querySelectorAll('details').forEach(d => d.open = true)");
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync(BrowserRenderWorkerTests.AllRenderedJs, null, new() { Timeout = 60000, PollingInterval = 200 });

        // Each shard's diagram: its arrow with spans bound, its arrow without none, and a click opens the segment.
        foreach (var (shard, path) in new[] { ("Shard A", "/with-flow-0"), ("Shard B", "/with-flow-1") })
        {
            var scenario = Page.Locator("details.scenario", new() { Has = Page.Locator("summary", new() { HasTextString = shard }) });
            var texts = scenario.Locator(".plantuml-browser:not(.iflow-diagram) svg text", new() { HasTextString = path });
            await Expect(texts.First).ToBeVisibleAsync();
            Assert.Equal("all", await texts.First.EvaluateAsync<string>("t => t.style.pointerEvents"));
            await texts.First.EvaluateAsync("t => t.dispatchEvent(new MouseEvent('click', { bubbles: true }))");
            await Expect(Page.Locator(".iflow-popup h3")).ToHaveTextAsync("Internal Flow (2 spans)");
            await Page.Locator(".iflow-popup .iflow-diagram svg").First.WaitForAsync(new() { Timeout = 30000 });
            Assert.Contains("GET " + path, await Page.Locator(".iflow-popup .iflow-diagram svg").First.TextContentAsync());
            await Page.Locator(".iflow-popup-close").ClickAsync();
        }
        var without = Page.Locator("details.scenario", new() { Has = Page.Locator("summary", new() { HasTextString = "Shard A" }) })
            .Locator(".plantuml-browser:not(.iflow-diagram) svg text", new() { HasTextString = "/without-flow-0" });
        Assert.Equal("", await without.First.EvaluateAsync<string>("t => t.style.pointerEvents"));
    }

    [Theory]
    [InlineData(2, 1, "hidden")]
    [InlineData(1, 2, "has")]
    public async Task An_arrow_is_bound_when_drawn_exactly_when_it_has_a_segment(int withFlow, int withoutFlow, string list)
    {
        var (uri, _) = ReportTestHelper.GenerateRunReportWithFlowArrows(TempDir, OutputDir, $"ArrowLink_Bind_{list}.html", withFlow, withoutFlow);
        await OpenAndDraw(uri);

        Assert.Equal(list, await ListInPage());
        // Read at once after the render, with no popup opened and no wait on the map: binding needs only the list.
        for (var i = 0; i < withFlow; i++)
            Assert.All(await Binding($"with-flow-{i}"), b => Assert.StartsWith("all|", b));
        for (var i = 0; i < withoutFlow; i++)
        {
            var texts = await Binding($"without-flow-{i}");
            Assert.NotEmpty(texts);
            // At rest in the ink around it, and deaf to the pointer: nothing to open.
            Assert.All(texts, b => Assert.Equal("none|#000000", b));
        }
    }
}
