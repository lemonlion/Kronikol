using Kronikol.Reports;
using Microsoft.Playwright;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// What the report page does with captured text it did not write itself (DIAGRAM_COLOURS_PLAN §12.6): a diagram source from
/// an earlier report, merged or ingested, can still hold a character XML cannot, and an assertion's message is the
/// test's own words, which can say anything.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class CapturedTextPageTests : PlaywrightTestBase
{
    public CapturedTextPageTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Save_as_png_saves_a_diagram_whose_text_holds_an_escape_character()
    {
        // The engine copies the character into the SVG and XMLSerializer writes it as it is, so the SVG the menu turned
        // into an image was not XML: the image never loaded, and Save as PNG (and every PNG and SVG copy) did nothing.
        await Page.GotoReportAsync(ServePage(TestPageGenerator.GenerateBrowserJsPage("",
            ("old-escape", "@startuml\nCaller -> Orders : exit \u001B[31mred\u001B[0m\nOrders --> Caller : 200\n@enduml"))));
        var svg = Page.Locator("#old-escape svg");
        await svg.WaitForAsync(new() { Timeout = 60_000 });
        Assert.Contains("\u001B", await svg.TextContentAsync());

        await DispatchContextMenu(svg);
        var saveImage = Page.Locator(".diagram-ctx-menu .submenu-parent", new() { HasTextString = "Save image" });
        await saveImage.HoverAsync();
        var item = saveImage.Locator(".submenu").GetByText("Save as PNG", new() { Exact = true });
        var download = await Page.RunAndWaitForDownloadAsync(() => item.ClickAsync(), new() { Timeout = 10_000 });

        var path = Path.Combine(TempDir, "saved.png");
        await download.SaveAsAsync(path);
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.True(bytes.Length > 100, $"the PNG is {bytes.Length} bytes");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
    }

    [Fact]
    public async Task A_diagram_whose_source_comment_holds_a_double_hyphen_saves_as_PNG_and_as_SVG_that_parses()
    {
        // The engine writes the diagram's source into the SVG as an instruction, <?plantuml-src ...?>. Drawn into the
        // page it becomes a comment, and an encoded source can hold "--", which no XML comment may: the SVG the menu
        // serialized was not XML, so its PNG never loaded (Save and Copy as PNG did nothing) and its SVG file opened in
        // no viewer. Which sources encode with "--" is the engine's business: forty are drawn, and the first that does
        // is the one saved.
        var diagrams = Enumerable.Range(0, 40).Select(i => ($"dash-{i}", "@startuml\n" + string.Join("\n",
            Enumerable.Range(0, 8).Select(m => $"Caller -> Orders : request {i} step {m}\nOrders --> Caller : 200 for {i * 31 + m}"))
            + "\n@enduml")).ToArray();
        await Page.GotoReportAsync(ServePage(TestPageGenerator.GenerateBrowserJsPage("", diagrams)));
        // the page draws a diagram as it comes near the window: each one still waiting is brought there in turn
        await Page.WaitForFunctionAsync("""
            n => {
                const all = Array.from(document.querySelectorAll('[id^="dash-"]'));
                const waiting = all.find(el => !el.querySelector('svg'));
                if (waiting) waiting.scrollIntoView({ block: 'center' });
                return all.length >= n && !waiting;
            }
            """, diagrams.Length, new() { Timeout = 120_000, PollingInterval = 200 });
        var id = await Page.EvaluateAsync<string?>("""
            () => {
                for (const el of document.querySelectorAll('[id^="dash-"]')) {
                    const w = document.createTreeWalker(el.querySelector('svg'), NodeFilter.SHOW_COMMENT);
                    for (let n = w.nextNode(); n; n = w.nextNode()) if (n.data.indexOf('--') >= 0) return el.id;
                }
                return null;
            }
            """);
        Assert.True(id is not null, "premise: the source comment of one of the forty diagrams holds a double hyphen");
        var svg = Page.Locator($"#{id} svg");

        var png = await SaveAsync(svg, "Save as PNG", "dash.png");
        Assert.True(png.Length > 100, $"the PNG is {png.Length} bytes");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);

        // the SVG file parses as XML, and still carries the engine's source as the instruction it wrote
        var file = System.Text.Encoding.UTF8.GetString(await SaveAsync(svg, "Save as SVG", "dash.svg"));
        var document = System.Xml.Linq.XDocument.Parse(file);
        var source = document.DescendantNodes().OfType<System.Xml.Linq.XProcessingInstruction>().Single(p => p.Target == "plantuml-src");
        Assert.Contains("--", source.Data);

        // a comment that was never an instruction and that XML cannot hold, with "--" in it or a "-" at its end, is left
        // out; one XML can hold stays
        await svg.EvaluateAsync("""
            s => s.append(document.createComment('a -- b'), document.createComment('ends-'), document.createComment('kept'))
            """);
        var withComments = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(await SaveAsync(svg, "Save as SVG", "comments.svg")));
        Assert.Equal(["kept"], withComments.DescendantNodes().OfType<System.Xml.Linq.XComment>().Select(c => c.Value)
            .Where(v => !v.StartsWith('?')));
    }

    private async Task<byte[]> SaveAsync(ILocator svg, string item, string name)
    {
        await DispatchContextMenu(svg);
        var saveImage = Page.Locator(".diagram-ctx-menu .submenu-parent", new() { HasTextString = "Save image" });
        await saveImage.HoverAsync();
        var entry = saveImage.Locator(".submenu").GetByText(item, new() { Exact = true });
        var download = await Page.RunAndWaitForDownloadAsync(() => entry.ClickAsync(), new() { Timeout = 10_000 });
        var path = Path.Combine(TempDir, name);
        await download.SaveAsAsync(path);
        return await File.ReadAllBytesAsync(path);
    }

    [Fact]
    public async Task Hiding_assertions_keeps_a_diagram_whose_assertion_says_end_note()
    {
        // Assertions hidden (written so, as they start shown since 5.0.0): the page takes each assertion note out of
        // the source before drawing it, and the
        // pattern ended the note at the first "end note" anywhere, mid-line included. The rest of the note stayed in the
        // source, with its own `end note`, and the engine drew its syntax-error picture in place of the diagram.
        const string source = """
            @startuml
            participant "Caller" as Caller
            participant "Orders" as Orders
            Caller -> Orders : GET: /orders
            Orders --> Caller : 200 OK
            hnote across <<assertionNote>> #F8D7DA
            ✗ expected the end note to be printed
            but got nothing
            end note
            Caller -> Orders : GET: /orders/2
            Orders --> Caller : 404 Not Found
            @enduml
            """;
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios = [new Scenario { Id = "assert-1", DisplayName = "Assert on a note", Result = ExecutionResult.Passed }]
            }
        ];
        var fileName = "CapturedText_end_note.html";
        var path = ReportGenerator.GenerateHtmlReport([new DiagramAsCode("assert-1", "", source)], features,
            DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Assertions", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: ResolvedToggleDefaults.BuiltIn with { AssertionsShown = false });
        File.Copy(path, Path.Combine(OutputDir, fileName), true);

        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = await WaitForDiagramSvg(60_000);

        var painted = await svg.TextContentAsync() ?? "";
        Assert.DoesNotContain("Syntax Error", painted);
        Assert.Contains("/orders/2", painted);
        Assert.DoesNotContain("expected the end note", painted);
    }
}
