using Kronikol.InternalFlow;
using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// "Export Filtered HTML" has to produce a report whose diagrams still draw.
/// <para>
/// Under <c>BrowserJs</c> rendering a diagram's PlantUML source is not in its element — the element is an
/// empty <c>&lt;div class="plantuml-browser" id="puml-N"&gt;</c> and every source lives, gzipped, in a
/// single <c>&lt;script id="puml-data"&gt;</c> keyed by that id. That script sits at the very end of the
/// <b>body</b>. The export copied the <c>&lt;head&gt;</c> and the visible <c>details.feature</c> elements
/// and nothing else, so the exported file shipped the whole render machinery and none of the data:
/// <c>getPumlZ</c> returned null, <c>enqueueElement</c> silently did nothing, and every diagram that had
/// not already been drawn before the export stayed permanently blank — no error, no message, just an
/// empty box. Measured on a real 488-diagram report: 12 of 460 diagrams drew, 448 did not.
/// </para>
/// <para>
/// The pre-existing coverage could not see this. It asserted that a seeded script string appears in the
/// exported text, which it does — the head is copied verbatim. Presence of the machinery says nothing
/// about whether a diagram renders, so these facts open the export in a browser and wait for an
/// <c>&lt;svg&gt;</c>.
/// </para>
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class ExportFilteredHtmlRenderingTests : PlaywrightTestBase
{
    public ExportFilteredHtmlRenderingTests(PlaywrightFixture fixture) : base(fixture) { }

    /// <summary>Clicks Export Filtered HTML and returns the path the download was saved to.</summary>
    private async Task<string> ExportFilteredHtml()
    {
        var download = await Page.RunAndWaitForDownloadAsync(async () =>
        {
            await Page.Locator("button.export-btn", new() { HasTextString = "Export Filtered HTML" }).ClickAsync();
        });
        var path = Path.Combine(TempDir, $"export_{Guid.NewGuid():N}.html");
        await download.SaveAsAsync(path);
        return path;
    }

    /// <summary>
    /// Opens <paramref name="path"/> in a clean page — a fresh context matters, because a diagram that
    /// rendered in the source report is serialised into the export as inline SVG and would mask a
    /// missing payload.
    /// </summary>
    private async Task OpenExport(string path)
    {
        await Page.GotoAsync(new Uri(path).AbsoluteUri);
    }

    [Fact]
    public async Task Exported_filtered_report_still_renders_its_diagrams()
    {
        await Page.GotoAsync(GenerateReport("ExportRender_Diagrams.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var exported = await ExportFilteredHtml();
        await OpenExport(exported);

        // Open everything and let the export's own pipeline draw.
        await Page.EvaluateAsync("() => document.querySelectorAll('details').forEach(d => d.setAttribute('open',''))");
        await Page.WaitForFunctionAsync(
            "() => !!window._renderDiagramsInContainer",
            null, new() { Timeout = 30000, PollingInterval = 200 });
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");

        await Page.WaitForFunctionAsync("""
            () => {
                var els = document.querySelectorAll('.plantuml-browser');
                if (els.length === 0) return false;
                for (var i = 0; i < els.length; i++) if (els[i].querySelector('svg')) return true;
                return false;
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Exported_filtered_report_carries_the_diagram_payload()
    {
        // The direct statement of the defect: the export referenced sources it did not ship.
        await Page.GotoAsync(GenerateReport("ExportRender_Payload.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var exported = await ExportFilteredHtml();
        await OpenExport(exported);

        // Asserting the tag appears in the text would pass on the export script that writes it. Read the
        // payload out of the loaded document instead, and require it to hold sources.
        var sources = await Page.EvaluateAsync<int>("""
            () => {
                var s = document.getElementById('puml-data');
                if (!s) return -1;
                try { return Object.keys(JSON.parse(s.textContent)).length; } catch (e) { return -2; }
            }
        """);

        Assert.True(sources > 0, $"exported report carries no diagram sources (got {sources})");
    }

    [Fact]
    public async Task Every_diagram_in_the_export_can_find_its_source()
    {
        // Rendering one diagram is not enough: a diagram already drawn before the export is serialised
        // as inline SVG and would pass on its own. This asserts every container can resolve a source.
        await Page.GotoAsync(GenerateReport("ExportRender_AllSources.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var exported = await ExportFilteredHtml();
        await OpenExport(exported);

        await Page.WaitForFunctionAsync(
            "() => !!window._getPumlZ",
            null, new() { Timeout = 30000, PollingInterval = 200 });

        var orphaned = await Page.EvaluateAsync<int>("""
            () => {
                var els = [].slice.call(document.querySelectorAll('.plantuml-browser'));
                return els.filter(function (el) {
                    if (el.querySelector('svg')) return false;          // already drawn, source not needed
                    return !window._getPumlZ(el);                        // nothing to draw from
                }).length;
            }
        """);

        Assert.Equal(0, orphaned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_arrow_opens_its_popup_in_the_export(bool drawnBeforeTheExport)
    {
        // INTERNAL_FLOW_BLOB_PLAN S2: the export carries the segment map, and its arrows open it. F14 (§11.1): a diagram
        // drawn before the export was serialised as the SVG it drew, its link texts still styled as bound, but no
        // listener survives serialisation, so a click opened nothing.
        var (uri, _) = ReportTestHelper.GenerateRunReportWithFlowArrows(TempDir, OutputDir, $"ExportRender_Arrows_{drawnBeforeTheExport}.html", withFlow: 1, withoutFlow: 1);
        await Page.GotoAsync(uri);
        await ExpandFirstScenarioWithDiagram();
        if (drawnBeforeTheExport)
            await DrawAll();

        var exported = await ExportFilteredHtml();
        await OpenExport(exported);
        await DrawAll();

        await Page.EvaluateAsync("""
            () => Array.from(document.querySelectorAll('.plantuml-browser:not(.iflow-diagram) svg text'))
                .find(t => t.textContent.includes('/with-flow-0'))
                .dispatchEvent(new MouseEvent('click', { bubbles: true }))
            """);
        await Expect(Page.Locator(".iflow-popup h3")).ToHaveTextAsync("Internal Flow (2 spans)");
        await Page.Locator(".iflow-popup .iflow-diagram svg").First.WaitForAsync(new() { Timeout = 30000 });
    }

    [Fact]
    public async Task The_export_carries_the_segment_map_whole()
    {
        // §5, Q1: the export copies the head, and the segment element with it, whole: the element is small once
        // compressed, and pruning it by key would need its ids read back out of the map.
        var (uri, reportsDir) = ReportTestHelper.GenerateRunReportWithFlowArrows(TempDir, OutputDir, "ExportRender_Map.html", withFlow: 2, withoutFlow: 1);
        await Page.GotoAsync(uri);
        await Page.Locator("details.feature").First.WaitForAsync();

        var exported = await ExportFilteredHtml();

        static string Element(string html)
        {
            const string head = "<script id=\"iflow-segments\" type=\"application/json\">";
            var start = html.IndexOf(head, StringComparison.Ordinal);
            Assert.True(start >= 0, "the page carries the segment element");
            return html[start..(html.IndexOf("</script>", start, StringComparison.Ordinal) + "</script>".Length)];
        }
        Assert.Equal(Element(File.ReadAllText(Path.Combine(reportsDir, "TestRunReport.html"))), Element(File.ReadAllText(exported)));
    }

    [Fact]
    public async Task A_note_drawn_before_the_export_still_folds_in_the_export()
    {
        // F14 (INTERNAL_FLOW_BLOB_PLAN §11.1) beyond the links: every listener the render script bound to a drawn
        // diagram was lost in the export, a note's fold on a double-click among them.
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithLongNotes(TempDir, OutputDir, "ExportRender_Notes.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await DrawAll();
        await Page.Locator(".note-hover-rect").First.WaitForAsync();

        var exported = await ExportFilteredHtml();
        await OpenExport(exported);
        await DrawAll();
        await Page.Locator(".note-hover-rect").First.WaitForAsync();

        var before = await Page.Locator(".plantuml-browser svg").First.EvaluateAsync<string>("svg => svg.outerHTML");
        await Page.Locator(".note-hover-rect").First.EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true }))");
        await Page.WaitForFunctionAsync(
            "before => { const svg = document.querySelector('.plantuml-browser svg'); return !window._plantumlRendering && !!svg && svg.outerHTML !== before; }",
            before, new() { Timeout = 15000, PollingInterval = 200 });
    }

    [Fact]
    public async Task A_flame_chart_drawn_before_the_export_still_zooms_in_the_export()
    {
        // A flame chart went into the export drawn and marked as drawn, so the export never bound its click-to-zoom.
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithMarkersInTheWholeTestFlow(TempDir, OutputDir, "ExportRender_Flame.html"));
        await ExpandFirstScenarioWithDiagram();
        await Page.Locator(".diagram-toggle-btn[data-dtype='flame']").First.ClickAsync();
        var bar = Page.Locator(".diagram-view-flame .iflow-flame .iflow-flame-bar").First;
        await Expect(bar).ToBeVisibleAsync();

        var exported = await ExportFilteredHtml();
        await OpenExport(exported);

        await Expect(bar).ToBeVisibleAsync();
        await bar.EvaluateAsync("el => el.dispatchEvent(new MouseEvent('click', { bubbles: true }))");
        await Expect(Page.Locator(".diagram-view-flame .iflow-flame-zoom-hint").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task An_inline_diagram_goes_into_the_export_not_marked_as_bound()
    {
        // A diagram drawn when the report was written (NodeJs, Server, Local) arrives inline, and the page marks it
        // data-iflow-bound once it has looked at its links. The mark means "bound in this page", and no listener survives
        // the copy, so the export's copy goes without it and the export looks at its links again.
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="220" height="90" viewBox="0 0 220 90"><g><line x1="30" y1="50" x2="190" y2="50" stroke="#181818"/><a href="#iflow-seg-1" xlink:href="#iflow-seg-1"><text x="40" y="45" fill="#0000FF" font-size="13" text-decoration="underline">GET: /api/orders</text></a></g></svg>
            """;
        const string scenarioId = "export-inline-1";
        var dataScript = DiagramContextMenu.GetInternalFlowConfigScript(InternalFlowHasDataBehavior.ShowLinkOnHover)
            + InternalFlowHtmlGenerator.WrapSegmentData(new Dictionary<string, object>
            {
                ["iflow-seg-1"] = new { title = "Internal Flow (1 span)", content = "<p>flow</p>" }
            });
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode(scenarioId, svg, "@startuml\nCaller -> Orders: [[#iflow-seg-1 GET: /api/orders]]\n@enduml")],
            [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = scenarioId, DisplayName = "Look an order up", Result = ExecutionResult.Passed }] }],
            DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, "ExportRender_Inline.html"), "Inline Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.Local,
            inlineSvgRendering: true,
            internalFlowTracking: true,
            internalFlowDataScript: dataScript);
        File.Copy(path, Path.Combine(OutputDir, "ExportRender_Inline.html"), true);
        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        await ExpandFirstScenarioWithDiagram();
        await Page.Locator(".plantuml-inline-svg").First.ScrollIntoViewIfNeededAsync();
        await Page.WaitForFunctionAsync("() => document.querySelector('.plantuml-inline-svg').dataset.iflowBound === '1'", null,
            new() { Timeout = 10_000, PollingInterval = 200 });

        var exported = await ExportFilteredHtml();

        var copies = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(exported), "<div class=\"plantuml-inline-svg\"[^>]*>");
        Assert.Single(copies);
        Assert.DoesNotContain("data-iflow-bound", copies[0].Value, StringComparison.Ordinal);
    }

    /// <summary>Asks the page to draw every diagram and waits until each has drawn.</summary>
    private async Task DrawAll()
    {
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync(BrowserRenderWorkerTests.AllRenderedJs, null, new() { Timeout = 60000, PollingInterval = 200 });
    }
}
