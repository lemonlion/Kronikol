using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The colours carried from S10 take the theme (5.0.0, plans/OPTION_C_PLAN.md S11): an activity diagram's (a scenario's
/// whole-test flow and the internal-flow popup), the component diagram's person box, database edge and edge labels, and
/// a sequence note's header line, in the light and the dark theme alike.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class DiagramActivityAndComponentColoursTests : DiagramNotePlaywrightBase
{
    public DiagramActivityAndComponentColoursTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 1280;
    protected override int ViewportHeight => 900;

    // A token's value as the browser resolves a colour, and a colour an element was painted with (an attribute)
    private const string Paint = """
        window.__tok = window.__tok || function (name) {
            const p = document.createElement('div'); p.style.color = 'var(' + name + ')'; document.body.appendChild(p);
            const c = getComputedStyle(p).color; p.remove(); return c;
        };
        window.__painted = window.__painted || function (root, tag, attr, value) {
            return Array.from(root.querySelectorAll(tag)).find(e => (e.getAttribute(attr) || '').toLowerCase() === value);
        };
        """;

    private async Task OpenFlowReportAsync(string fileName, string theme)
    {
        // two sources: the flow draws two swimlanes and the line between them, which is the lane colour's only stroke
        var (uri, _) = ReportTestHelper.GenerateRunReportWithFlowArrows(TempDir, OutputDir, fileName, withFlow: 1, withoutFlow: 0,
            twoSources: true);
        await Page.GotoAsync(uri);
        await Page.EvaluateAsync("t => document.documentElement.setAttribute('data-theme', t)", theme);
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task A_whole_test_flow_takes_the_theme(string theme)
    {
        await OpenFlowReportAsync($"ActivityFlow-{theme}.html", theme);
        await Page.WaitForFunctionAsync("() => !!document.querySelector('.plantuml-browser.iflow-diagram svg rect')", null,
            new() { Timeout = 300000, PollingInterval = 200 });

        var r = await Page.Locator(".plantuml-browser.iflow-diagram svg").First.EvaluateAsync<string[]>("el => { " + Paint + """
                const box = __painted(el, 'rect', 'fill', '#f0f4ff'), text = __painted(el, 'text', 'fill', '#000000');
                const line = __painted(el, 'line', 'stroke', '#181818'), head = __painted(el, 'polygon', 'fill', '#181818');
                const lane = __painted(el, 'line', 'stroke', '#cccccc');
                return [box ? getComputedStyle(box).fill : 'none', __tok('--dg-activity'),
                        box ? getComputedStyle(box).stroke : 'none', __tok('--dg-activity-edge'),
                        text ? getComputedStyle(text).fill : 'none', __tok('--dg-ink'),
                        getComputedStyle(el).backgroundColor, __tok('--dg-bg'),
                        line ? getComputedStyle(line).stroke : 'none', __tok('--dg-line'),
                        head ? getComputedStyle(head).fill : 'none', __tok('--dg-line'),
                        lane ? getComputedStyle(lane).stroke : 'none', __tok('--dg-lane')];
            }
            """);

        Assert.Equal(r[1], r[0]);
        Assert.Equal(r[3], r[2]);
        Assert.Equal(r[5], r[4]);
        Assert.Equal(r[7], r[6]);
        Assert.Equal(r[9], r[8]);
        Assert.Equal(r[11], r[10]);
        Assert.Equal(r[13], r[12]);
    }

    // The popup draws the same activity diagram in an .iflow-diagram box of its own
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task A_popups_activity_diagram_takes_the_theme(string theme)
    {
        await OpenFlowReportAsync($"ActivityPopup-{theme}.html", theme);
        await Page.WaitForFunctionAsync(BrowserRenderWorkerTests.AllRenderedJs, null, new() { Timeout = 300000, PollingInterval = 200 });

        // SVG rule: a dispatched click, on the arrow's own text
        await Page.EvaluateAsync("""
            () => Array.from(document.querySelectorAll('.plantuml-browser:not(.iflow-diagram) svg text'))
                .find(t => t.textContent.includes('with-flow-0'))
                .dispatchEvent(new MouseEvent('click', { bubbles: true }))
            """);
        await Page.WaitForFunctionAsync("() => !!document.querySelector('.iflow-popup .iflow-diagram svg rect')", null,
            new() { Timeout = 60000, PollingInterval = 200 });

        var r = await Page.Locator(".iflow-popup .iflow-diagram svg").First.EvaluateAsync<string[]>("el => { " + Paint + """
                const box = __painted(el, 'rect', 'fill', '#f0f4ff'), text = __painted(el, 'text', 'fill', '#000000');
                return [box ? getComputedStyle(box).fill : 'none', __tok('--dg-activity'),
                        text ? getComputedStyle(text).fill : 'none', __tok('--dg-ink')];
            }
            """);

        Assert.Equal(r[1], r[0]);
        Assert.Equal(r[3], r[2]);
    }

    // The component diagram's skin (ComponentDiagramGenerator): the person box, the database box's edge, the edge labels
    private const string Components = """
        @startuml
        left to right direction
        skinparam rectangle<<person>> {
          BackgroundColor #08427B
          FontColor #FFFFFF
          BorderColor #073B6F
        }
        skinparam database {
          BackgroundColor #0B6C75
          FontColor #FFFFFF
          BorderColor #09616A
        }
        skinparam arrow {
          FontColor #666666
        }
        rectangle "Caller" <<person>> as caller
        rectangle "Order Service" as svc
        database "Orders Db" as db
        caller --> svc : "HTTP: POST - 2 calls across 1 test"
        svc -[#0E7C86]-> db : "SQL: INSERT - 1 call across 1 test"
        @enduml
        """;

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_component_diagrams_person_database_edge_and_labels_take_the_theme(string theme)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios = [new() { Id = "s1", DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([], features, DateTime.UtcNow, DateTime.UtcNow, null,
            Path.Combine(TempDir, $"Components-{theme}.html"), "Bakery", true, diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs, componentDiagramPlantUml: Components);
        File.Copy(path, Path.Combine(OutputDir, $"Components-{theme}.html"), true);
        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        await Page.EvaluateAsync("t => document.documentElement.setAttribute('data-theme', t)", theme);
        await OpenReportTabAsync("components");
        await Page.WaitForFunctionAsync("() => !!document.querySelector('#component-diagram .plantuml-browser svg')", null,
            new() { Timeout = 30000, PollingInterval = 200 });

        var r = await Page.Locator("#component-diagram .plantuml-browser svg").First.EvaluateAsync<string[]>("el => { " + Paint + """
                const person = __painted(el, '*', 'fill', '#08427b'), edge = __painted(el, '*', 'stroke', '#09616a');
                const label = __painted(el, 'text', 'fill', '#666666');
                return [person ? getComputedStyle(person).fill : 'none', __tok('--dg-person'),
                        edge ? getComputedStyle(edge).stroke : 'none', __tok('--dg-db-edge'),
                        label ? getComputedStyle(label).fill : 'none', __tok('--dg-ink-soft')];
            }
            """);

        Assert.Equal(r[1], r[0]);
        Assert.Equal(r[3], r[2]);
        Assert.Equal(r[5], r[4]);
    }

    // A note's header line (its method and path, NotePalette.HeaderInk) is drawn in the soft ink of the theme
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task A_notes_header_line_takes_the_themes_soft_ink(string theme)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithHeaderInkNotes(TempDir, OutputDir, $"HeaderInk-{theme}.html"));
        await Page.EvaluateAsync("t => document.documentElement.setAttribute('data-theme', t)", theme);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        var r = await Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<string[]>("el => { " + Paint + """
                const header = __painted(el, 'text', 'fill', '#686868');
                return [header ? getComputedStyle(header).fill : 'none', __tok('--dg-ink-soft')];
            }
            """);

        Assert.Equal(r[1], r[0]);
    }
}
