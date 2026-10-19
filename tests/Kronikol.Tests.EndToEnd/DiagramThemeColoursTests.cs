using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A drawn diagram takes the theme (5.0.0, plans/OPTION_C_PLAN.md S10, P36): the colours the engine and the emitter
/// paint are mapped to the theme's tokens, so the diagram's ground, ink and lines, a database's calls, the assertion
/// strips and a bar that is not a step follow the light and the dark theme alike.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class DiagramThemeColoursTests : DiagramNotePlaywrightBase
{
    public DiagramThemeColoursTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string Source = """
        @startuml
        !pragma teoz true
        actor "Caller" as caller
        participant "OrderService" as svc
        participant "Stock Api" as stock #438DD5
        database "Orders Db" as db #0E7C86
        queue "Orders" as orders #9B59B6
        hnote across #black:<color:white>Test: Place an order
        caller -> svc : POST /api/orders
        svc -[#438DD5]> stock : GET /api/stock
        svc -[#0E7C86]> db : INSERT order
        svc -[#9B59B6]> orders : Publish OrderPlaced
        hnote across <<assertionNote>> #d4edda
        Assert.Equal(201, status)
        end note
        hnote across <<assertionNote>> #f8d7da
        Assert.Equal("Placed", state)
        end note
        svc --> caller : 201 Created
        @enduml
        """;

    // A token's value as the browser resolves a colour, and the colour an element is drawn with: the engine writes it
    // as an attribute (stroke="#181818"), a source's own style can write it in a style
    private const string Paint = """
        window.__tok = window.__tok || function (name) {
            const p = document.createElement('div'); p.style.color = 'var(' + name + ')'; document.body.appendChild(p);
            const c = getComputedStyle(p).color; p.remove(); return c;
        };
        window.__stroke = window.__stroke || function (e) {
            const m = /stroke:\s*(#[0-9a-f]{6})/i.exec(e.getAttribute('style') || '');
            return (e.getAttribute('stroke') || (m ? m[1] : '')).toLowerCase();
        };
        """;

    private async Task OpenAsync(string fileName, string theme)
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
        await Page.EvaluateAsync("t => document.documentElement.setAttribute('data-theme', t)", theme);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
    }

    private Task<string[]> InSvgAsync(string body) => Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<string[]>(
        "el => { " + Paint + body + " }");

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_diagrams_ground_ink_and_lines_take_the_theme(string theme)
    {
        await OpenAsync($"ThemeGround-{theme}.html", theme);

        var r = await InSvgAsync("""
            const label = Array.from(el.querySelectorAll('text')).find(t => t.textContent.includes('POST /api/orders'));
            // a message's arrow, level: a lifeline is drawn in the same colour, and the role pass gives it its own
            const arrow = Array.from(el.querySelectorAll('line')).find(l => l.getAttribute('y1') === l.getAttribute('y2') && __stroke(l) === '#181818');
            const head = Array.from(el.querySelectorAll('polygon')).find(p => /^#181818$/i.test(p.getAttribute('fill') || ''));
            return [getComputedStyle(el).backgroundColor, __tok('--dg-bg'),
                    getComputedStyle(label).fill, __tok('--dg-ink'),
                    getComputedStyle(arrow).stroke, __tok('--dg-line'),
                    head ? getComputedStyle(head).fill : 'none', __tok('--dg-line')];
            """);

        Assert.Equal(r[1], r[0]);
        Assert.Equal(r[3], r[2]);
        Assert.Equal(r[5], r[4]);
        Assert.Equal(r[7], r[6]);
    }

    // A call to a dependency is drawn in its category's colour (DependencyPalette), which the theme gives a value of
    // its own: on the dark ground the light theme's colours read too dark.
    [Theory]
    [InlineData("light", "#438DD5", "--dg-http")]
    [InlineData("dark", "#438DD5", "--dg-http")]
    [InlineData("light", "#0E7C86", "--dg-db")]
    [InlineData("dark", "#0E7C86", "--dg-db")]
    [InlineData("light", "#9B59B6", "--dg-msg")]
    [InlineData("dark", "#9B59B6", "--dg-msg")]
    public async Task A_call_to_a_dependency_takes_the_themes_colour_for_it(string theme, string colour, string token)
    {
        await OpenAsync($"ThemeDependency-{theme}-{colour.TrimStart('#')}.html", theme);

        var r = await Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<string[]>(
            "(el, c) => { " + Paint + """
                const arrow = Array.from(el.querySelectorAll('line')).find(l => __stroke(l) === c.colour.toLowerCase());
                // its arrowhead: the only polygon in the colour (the participant's box is a rect, a queue's a path)
                const head = Array.from(el.querySelectorAll('polygon')).find(p => (p.getAttribute('fill') || '').toLowerCase() === c.colour.toLowerCase());
                return [arrow ? getComputedStyle(arrow).stroke : 'none', __tok(c.token), head ? getComputedStyle(head).fill : 'none'];
            }
            """, new { colour, token });

        Assert.Equal(r[1], r[0]);
        Assert.Equal(r[1], r[2]);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_assertion_strips_and_a_bar_that_is_not_a_step_take_the_theme(string theme)
    {
        await OpenAsync($"ThemeStrips-{theme}.html", theme);

        var r = await InSvgAsync("""
            const by = f => Array.from(el.querySelectorAll('polygon')).find(p => (p.getAttribute('fill') || '').toLowerCase() === f);
            const fill = p => p ? getComputedStyle(p).fill : 'none';
            return [fill(by('#d4edda')), __tok('--dg-pass'), fill(by('#f8d7da')), __tok('--dg-fail'), fill(by('#000000')), __tok('--dg-bar')];
            """);

        Assert.Equal(r[1], r[0]);
        Assert.Equal(r[3], r[2]);
        Assert.Equal(r[5], r[4]);
    }
}
