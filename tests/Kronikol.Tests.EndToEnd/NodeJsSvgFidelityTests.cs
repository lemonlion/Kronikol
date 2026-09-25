namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// What a <c>PlantUmlRendering.NodeJs</c> report shows (DIAGRAM_COLOURS_PLAN S5). The Node renderer wrote its
/// SVG with text and attributes unescaped: inlined (the default, since internal-flow tracking inlines
/// NodeJs SVG), an XML body's tags became SVG elements that paint nothing; as a <c>data:</c> image
/// (tracking off), a bare <c>&amp;</c> made the image fail to parse. Its processing instruction came out as
/// an HTML <c>&lt;div&gt;</c>, which the page reads as the end of the inline SVG.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class NodeJsSvgFidelityTests : PlaywrightTestBase
{
    public NodeJsSvgFidelityTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task Open(bool inlineSvg, [System.Runtime.CompilerServices.CallerMemberName] string? testName = null)
    {
        Assert.SkipWhen(!NodeAvailable.Value, "Node.js not available on PATH");
        await Page.GotoAsync(ReportTestHelper.GenerateNodeJsReport(TempDir, OutputDir, $"NodeJsSvg_{testName}.html", inlineSvg));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
    }

    private static readonly Lazy<bool> NodeAvailable = new(() =>
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("node", "--version")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    });

    [Fact]
    public async Task An_xml_body_paints_its_tags_as_text()
    {
        await Open(inlineSvg: true);

        var lengths = await Page.EvaluateAsync<double[]>("""
            () => Array.from(document.querySelectorAll('svg text'))
                .filter(t => t.textContent.indexOf('<order>') === 0 || t.textContent.indexOf('<result>') === 0)
                .map(t => { t.scrollIntoView(); return t.getComputedTextLength(); })
            """);

        Assert.Equal(2, lengths.Length);
        Assert.All(lengths, l => Assert.True(l > 0, $"a body line painted {l} px wide"));
    }

    [Fact]
    public async Task An_inline_svg_ends_where_the_renderer_ended_it()
    {
        await Open(inlineSvg: true);

        // The page parses what the renderer wrote: an element it cannot hold inside an <svg> ends the <svg>
        // there, and the element lands after it in the diagram's container.
        var strays = await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('svg')).filter(s => s.parentElement && s.parentElement.closest('.scenario'))
                .map(s => s.nextElementSibling)
                .filter(n => n && n.tagName === 'DIV' && n.attributes.length === 0 && n.childNodes.length === 0)
                .map(n => n.outerHTML)
            """);

        Assert.Empty(strays);
    }

    [Fact]
    public async Task A_data_uri_image_whose_diagram_carries_an_ampersand_loads()
    {
        await Open(inlineSvg: false);

        var images = Page.Locator("img[src^='data:image/svg+xml']");
        Assert.Equal(2, await images.CountAsync());
        for (var i = 0; i < 2; i++)
        {
            var img = images.Nth(i);
            await img.ScrollIntoViewIfNeededAsync();
            await Page.WaitForFunctionAsync("el => el.complete", await img.ElementHandleAsync(),
                new() { Timeout = 15_000, PollingInterval = 200 });
            Assert.True(await img.EvaluateAsync<int>("el => el.naturalWidth") > 0, $"image {i} did not load");
        }
    }
}
