using Microsoft.Playwright;
using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// How what a scenario holds fits inside it (roadmap 1.10, plans/TOOLBAR_AT_EVERY_WIDTH_PLAN.md Q7). A
/// table wider than its scenario scrolls in a container of its own and keeps its words whole; a long token
/// in text breaks where it has to; a doc string is the grey scrolling block its rule describes; an
/// attachment image keeps its 320 px cap and shrinks to a phone's step. <see cref="ViewportSweepTests"/>
/// proves that nothing runs past a scenario at any width; these facts pin which way each kind of content
/// fits.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class ScenarioContentWidthTests : PlaywrightTestBase
{
    public ScenarioContentWidthTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task Open(int width)
    {
        await Page.SetViewportSizeAsync(width, 900);
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithWideContent(TempDir, OutputDir, $"WideContent_{width}.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.EvaluateAsync("() => document.querySelectorAll('details').forEach(d => d.open = true)");
    }

    /// <summary>The right edge of an element's text, the content edge of the scenario or feature holding
    /// it, and the number of lines the text takes.</summary>
    private const string TextGeometry = """
        el => {
            const holder = el.closest('.scenario, .feature');
            const edge = holder.getBoundingClientRect().left + holder.clientLeft + holder.clientWidth;
            const range = document.createRange();
            range.selectNodeContents(el);
            const rects = [...range.getClientRects()].filter(r => r.width > 0);
            return [Math.max(...rects.map(r => r.right)), edge, new Set(rects.map(r => Math.round(r.top))).size];
        }
        """;

    [Theory]
    [InlineData(800, "Different muffin recipes", "table.param-test-table", "SiliconeMuffinPan")]
    [InlineData(400, "Every step content kind", ".step-param-table table", "SiliconeMuffinPan")]
    [InlineData(1000, "Batches round trip", ".step-param-combined-table table", "LifecycleTestCustomer_1632818846264990595")]
    public async Task A_table_wider_than_its_scenario_scrolls_inside_it_and_keeps_its_words_whole(int width, string scenario, string table, string cellText)
    {
        await Open(width);
        var t = Page.Locator("details.scenario", new() { HasTextString = scenario }).Locator(table).First;
        await t.ScrollIntoViewIfNeededAsync();

        var scroller = await t.EvaluateAsync<double[]>("""
            t => {
                let s = t;
                while (s && !/^(auto|scroll)$/.test(getComputedStyle(s).overflowX)) s = s.parentElement;
                return s && t.closest('.scenario').contains(s) ? [s.scrollWidth, s.clientWidth] : [];
            }
            """);
        Assert.True(scroller.Length == 2, "the table has no scroll container inside its scenario");
        Assert.True(scroller[0] > scroller[1], $"the table should be wider than its scroll container at {width} px (scroll width {scroller[0]}, width {scroller[1]})");

        var geometry = await t.Locator("td", new() { HasTextString = cellText }).First.EvaluateAsync<double[]>(TextGeometry);
        Assert.Equal(1, geometry[2]);
    }

    [Theory]
    [InlineData("span.step-text", "System.Collections.Generic.List")]
    [InlineData(".tree-children .tree-node", "System.Collections.Generic.List")]
    [InlineData("summary.h3", "Every step content kind for")]
    [InlineData(".step-comment", "retried for")]
    [InlineData("a.step-attachment", "request_body_for_")]
    [InlineData(".endpoint", "/api/v1/orders/")]
    public async Task A_long_token_in_text_breaks_inside_its_holder_on_a_phone(string selector, string text)
    {
        await Open(320);
        var el = Page.Locator(selector, new() { HasTextString = text }).First;
        await el.ScrollIntoViewIfNeededAsync();

        var geometry = await el.EvaluateAsync<double[]>(TextGeometry);
        Assert.True(geometry[0] <= geometry[1] + 1, $"the text ends {geometry[0] - geometry[1]:F0} px past its holder's edge");
        Assert.True(geometry[2] > 1, "the text should break onto more lines than one");
    }

    [Fact]
    public async Task A_doc_string_is_a_grey_block_that_scrolls_its_long_line()
    {
        await Open(1000);
        var docString = Page.Locator("pre.step-docstring").First;
        await docString.ScrollIntoViewIfNeededAsync();

        await Expect(docString).ToHaveCSSAsync("background-color", "rgb(245, 245, 245)");
        await Expect(docString).ToHaveCSSAsync("border-top-color", "rgb(221, 221, 221)");
        await Expect(docString).ToHaveCSSAsync("overflow-x", "auto");
        Assert.True(await docString.EvaluateAsync<bool>("p => p.scrollWidth > p.clientWidth"), "the long line should scroll inside the block");
    }

    /// <summary>A diagram rendered by a PlantUML server or a local jar is an image in the scenario, with its
    /// source under it: an image wider than the scenario scrolled only at phone widths, and a long source
    /// line never did. Both scroll at every width, as the phone layout already chose over shrinking a
    /// diagram until it is illegible.</summary>
    [Theory]
    [InlineData(1000)]
    [InlineData(400)]
    public async Task A_server_rendered_diagram_and_its_source_scroll_inside_their_scenario(int width)
    {
        File.WriteAllText(Path.Combine(TempDir, "wide-diagram.svg"),
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1600\" height=\"200\"><rect width=\"1600\" height=\"200\" fill=\"#cfe8fc\"/></svg>");
        var source = "@startuml\nCaller -> OrderService : POST /api/v1/orders/3fa85f6457174562b3fc2c963f66afa6/items?include=toppings,ingredients,audit,customer,history\n@enduml";
        var path = ReportGenerator.GenerateHtmlReport(
            [new DiagramAsCode("sr1", "wide-diagram.svg", source)],
            [new Feature { DisplayName = "Server rendering", Scenarios = [new Scenario { Id = "sr1", DisplayName = "A wide diagram", Result = ExecutionResult.Passed }] }],
            DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, $"ServerDiagram_{width}.html"), "Test Run Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.Server);
        File.Copy(path, Path.Combine(OutputDir, $"ServerDiagram_{width}.html"), true);
        File.Copy(Path.Combine(TempDir, "wide-diagram.svg"), Path.Combine(OutputDir, "wide-diagram.svg"), true);
        await Page.SetViewportSizeAsync(width, 900);
        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        await Page.EvaluateAsync("() => document.querySelectorAll('details').forEach(d => d.open = true)");

        var image = Page.Locator("summary.example-image img").First;
        await image.ScrollIntoViewIfNeededAsync();
        await Expect(image).ToHaveJSPropertyAsync("naturalWidth", 1600);
        foreach (var scrolled in new[] { "summary.example-image", ".raw-plantuml pre" })
        {
            var geometry = await Page.Locator(scrolled).First.EvaluateAsync<double[]>("""
                el => {
                    const scenario = el.closest('.scenario');
                    const edge = scenario.getBoundingClientRect().left + scenario.clientLeft + scenario.clientWidth;
                    const scrolls = /^(auto|scroll)$/.test(getComputedStyle(el).overflowX) ? 1 : 0;
                    return [el.getBoundingClientRect().right, edge, el.scrollWidth, el.clientWidth, scrolls];
                }
                """);
            Assert.True(geometry[0] <= geometry[1] + 1, $"{scrolled} ends {geometry[0] - geometry[1]:F0} px past its scenario");
            // Wider content than box alone would not do: content overflowing a box that does not scroll
            // shows the same, and is what the scenario clips.
            Assert.True(geometry[4] == 1 && geometry[2] > geometry[3],
                $"{scrolled} should be a scroll container with more to scroll (scrolls: {geometry[4]}, scroll width {geometry[2]}, width {geometry[3]})");
        }
    }

    [Theory]
    [InlineData(320)]
    [InlineData(1400)]
    public async Task An_attachment_image_keeps_its_cap_and_fits_its_step(int width)
    {
        await Open(width);
        var image = Page.Locator("img.attachment-image").First;
        await image.ScrollIntoViewIfNeededAsync();
        await Expect(image).ToHaveJSPropertyAsync("complete", true);

        // The image's content box (its 1 px border lies outside the cap), its right edge, its step's
        // content edge, and the widths of the image and the link around it.
        var geometry = await image.EvaluateAsync<double[]>("""
            img => {
                const step = img.closest('.step'), box = img.getBoundingClientRect();
                return [img.clientWidth, box.right, step.getBoundingClientRect().left + step.clientLeft + step.clientWidth,
                    box.width, img.parentElement.getBoundingClientRect().width];
            }
            """);
        Assert.True(geometry[0] <= 320, $"the image is {geometry[0]} px wide, past its 320 px cap");
        Assert.True(geometry[1] <= geometry[2] + 1, $"the image ends {geometry[1] - geometry[2]:F0} px past its step");
        Assert.True(Math.Abs(geometry[3] - geometry[4]) <= 0.5, $"the link is {geometry[4]:F0} px wide around a {geometry[3]:F0} px image: the rest of it opens the lightbox too");
        if (width == 1400) Assert.Equal(320, geometry[0]);
    }
}
