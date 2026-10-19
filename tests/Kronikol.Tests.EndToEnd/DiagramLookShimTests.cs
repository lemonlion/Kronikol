using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The page's half of a diagram source's look (5.0.0, plans/OPTION_C_PLAN.md S10, P21 and P22): the report's render
/// shim draws every step bar as a rectangle with its label in bold, in place of the hexagon the source asks for, so the
/// role pass can give it the band's colours, and drops the blank line an older source still writes under a step bar's
/// table. What the page keeps (and its filters, menus and notes read) is the report's own source.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class DiagramLookShimTests : DiagramNotePlaywrightBase
{
    public DiagramLookShimTests(PlaywrightFixture fixture) : base(fixture) { }

    // a one-line step bar, then a step bar carrying a table as a report before 5.0.0 wrote it, with a blank line under it
    private const string Source = """
        @startuml
        !pragma teoz true
        <style>
         .stepBody {
             BackgroundColor black
             FontColor white
             LineColor white
         }
        </style>
        actor "Caller" as caller
        participant "OrderService" as svc
        hnote across <<stepDelimiter>> #black:<color:white>Given the shop is open
        caller -> svc : POST /api/orders
        hnote across <<stepDelimiter>><<stepBody>>: Given the following muffins exist\n\n|= name |= price |\n| Blueberry | 3.50 |\n
        svc --> caller : 201 Created
        @enduml
        """;

    private async Task OpenAsync(string fileName)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios = [new() { Id = "s1", DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode("s1", "", Source)],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
    }

    [Fact]
    public async Task A_step_bar_is_drawn_as_a_rectangle_with_its_label_in_bold()
    {
        await OpenAsync("LookShimBars.html");

        var drawn = await Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<double[]>("""
            el => {
                const black = f => /^(#000000|#000|black)$/i.test((f || '').trim());
                const hexagons = Array.from(el.querySelectorAll('polygon')).filter(p => black(p.getAttribute('fill'))).length;
                const boxes = Array.from(el.querySelectorAll('rect, path')).filter(p => black(p.getAttribute('fill'))).length;
                const label = Array.from(el.querySelectorAll('text')).find(t => t.textContent.includes('shop is open'));
                const bold = label && /^(bold|700)$/.test(label.getAttribute('font-weight') || '') ? 1 : 0;
                return [hexagons, boxes, bold];
            }
            """);

        Assert.Equal(0, drawn[0]);
        Assert.True(drawn[1] >= 2, $"expected both step bars drawn as rectangles, found {drawn[1]}");
        Assert.Equal(1, drawn[2]);
    }

    // Judged on what the engine paints: the trailing blank line of an older source paints as a whitespace-only text
    // line just below the table's last row.
    [Fact]
    public async Task An_older_step_bars_blank_line_under_its_table_is_not_drawn()
    {
        await OpenAsync("LookShimTrailing.html");

        var spacer = await Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<bool>("""
            el => {
                const ts = Array.from(el.querySelectorAll('text')).map(t => ({ y: parseFloat(t.getAttribute('y') || '0'), text: t.textContent }));
                const last = ts.find(t => t.text.trim() === '3.50');
                return !!last && ts.some(t => t.text.trim() === '' && t.y > last.y && t.y - last.y < 30);
            }
            """);

        Assert.False(spacer, "expected no blank line painted below the table's last row");
    }

    [Fact]
    public async Task The_page_keeps_the_reports_own_source()
    {
        await OpenAsync("LookShimSource.html");

        var kept = await Page.EvaluateAsync<string>("() => document.querySelector('[data-plantuml]').getAttribute('data-plantuml')");

        Assert.Contains("hnote across <<stepDelimiter>> #black:<color:white>Given the shop is open", kept);
        Assert.DoesNotContain("rnote across", kept);
    }
}
