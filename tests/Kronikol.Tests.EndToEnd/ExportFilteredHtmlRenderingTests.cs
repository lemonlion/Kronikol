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
}
