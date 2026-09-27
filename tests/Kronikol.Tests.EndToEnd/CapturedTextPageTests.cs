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
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateBrowserJsPage("",
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
    public async Task Hiding_assertions_keeps_a_diagram_whose_assertion_says_end_note()
    {
        // Assertions start hidden: the page takes each assertion note out of the source before drawing it, and the
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
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);

        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = await WaitForDiagramSvg(60_000);

        var painted = await svg.TextContentAsync() ?? "";
        Assert.DoesNotContain("Syntax Error", painted);
        Assert.Contains("/orders/2", painted);
        Assert.DoesNotContain("expected the end note", painted);
    }
}
