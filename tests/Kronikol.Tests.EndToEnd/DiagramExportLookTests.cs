using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A diagram copied as SVG or PNG carries the look the page gives it (5.0.0, plans/OPTION_C_PLAN.md S10, Q6 and F9). A
/// copy carries no page CSS, so the colours the stylesheet sets are written into it: the ink and the cards keep the
/// theme's colours, the fold the page hides stays hidden, and a step bar's table keeps its ground, which with no fill of
/// its own was drawn black. A copy takes the light theme's look whichever theme the page shows, as every copy did
/// before: it is pasted into documents, nearly always on white.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class DiagramExportLookTests : DiagramNotePlaywrightBase
{
    public DiagramExportLookTests(PlaywrightFixture fixture) : base(fixture) { }

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
        hnote across <<stepDelimiter>><<stepBody>>: Given the following muffins exist\n\n|= name |= price |\n| Blueberry | 3.50 |\n| Double Chocolate | 4.00 |
        caller -> svc : POST /api/orders
        note right
        {"item":"Blueberry","qty":2}
        end note
        svc --> caller : 201 Created
        @enduml
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
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.EvaluateAsync("t => document.documentElement.setAttribute('data-theme', t)", theme);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await Page.WaitForFunctionAsync(
            "() => { const s = document.querySelector(\"[data-diagram-type='plantuml'] svg\"); return !!s && !!s.getAttribute('data-k-roles'); }",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        await Page.EvaluateAsync("() => navigator.clipboard.writeText('nothing copied yet')");
    }

    // The diagram's menu, opened on the picture, then "Copy image" and the item named
    private async Task CopyAsync(string item)
    {
        await Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync("""
            el => {
                const r = el.getBoundingClientRect();
                el.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: r.left + 8, clientY: r.top + 8 }));
            }
            """);
        var menu = Page.Locator(".diagram-ctx-menu");
        await menu.WaitForAsync(new() { Timeout = 5000 });
        var parent = menu.Locator(".submenu-parent", new() { HasTextRegex = new System.Text.RegularExpressions.Regex("^Copy image") }).First;
        await parent.HoverAsync();
        await parent.Locator(".submenu").GetByText(item, new() { Exact = true }).First.ClickAsync();
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task A_diagram_copied_as_SVG_carries_its_look(string theme)
    {
        await OpenAsync($"ExportSvg-{theme}.html", theme);

        await CopyAsync("Copy as SVG");
        var r = await Page.EvaluateAsync<string[]>("""
            async () => {
                // the copy is written after the click: read until it is there (at most 10 s)
                let text = '';
                for (let k = 0; k < 50 && !text.startsWith('<svg'); k++) {
                    text = await navigator.clipboard.readText();
                    if (!text.startsWith('<svg')) await new Promise(res => setTimeout(res, 200));
                }
                // a colour as the browser resolves it, a token's value, and a property an element of the copy is painted with
                const col = v => { const p = document.createElement('div'); p.style.color = v; document.body.appendChild(p); const c = getComputedStyle(p).color; p.remove(); return c; };
                const tok = n => col('var(' + n + ')');
                const paint = (e, prop) => {
                    if (!e) return 'missing';
                    const m = new RegExp('(?:^|;)\\s*' + prop + '\\s*:\\s*([^;]+)').exec(e.getAttribute('style') || '');
                    const v = m ? m[1].trim() : e.getAttribute(prop);
                    return v ? col(v) : 'unpainted';
                };
                const doc = new DOMParser().parseFromString(text, 'image/svg+xml');
                const label = Array.from(doc.querySelectorAll('text')).find(t => t.textContent.includes('POST /api/orders'));
                const fold = doc.querySelector('.k-fold');
                const hidden = !!fold && (fold.getAttribute('visibility') === 'hidden' || /visibility\s*:\s*hidden/.test(fold.getAttribute('style') || ''));
                // the light theme's values, whichever theme the page shows; the page's theme as the copy left it
                const root = document.documentElement, shown = root.getAttribute('data-theme');
                root.setAttribute('data-theme', 'light');
                const want = [tok('--dg-step-table'), tok('--dg-ink'), tok('--dg-note')];
                root.setAttribute('data-theme', shown);
                return [paint(doc.querySelector('rect.k-step-table'), 'fill'), want[0],
                        paint(label, 'fill'), want[1],
                        paint(doc.querySelector('.k-card'), 'fill'), want[2],
                        String(hidden), shown];
            }
            """);

        Assert.Equal(r[1], r[0]);
        Assert.Equal(r[3], r[2]);
        Assert.Equal(r[5], r[4]);
        Assert.Equal("True", r[6], ignoreCase: true);
        Assert.Equal(theme, r[7]);
    }

    // The PNG is drawn from the same copy; read back from the clipboard, the step table's ground is its colour, not black
    [Theory]
    [InlineData("Copy as PNG", "light")]
    [InlineData("Copy as PNG (no transparency)", "light")]
    [InlineData("Copy as PNG", "dark")]
    [InlineData("Copy as PNG (no transparency)", "dark")]
    public async Task A_diagram_copied_as_PNG_draws_a_step_tables_ground_in_its_colour(string item, string theme)
    {
        await OpenAsync($"ExportPng-{(item.Contains("no transparency") ? "opaque" : "clear")}-{theme}.html", theme);
        // a point on the table's ground, clear of its rules and its text: just inside its left edge, half way down
        var at = await Page.EvaluateAsync<double[]>("""
            () => {
                const t = document.querySelector("[data-diagram-type='plantuml'] svg rect.k-step-table");
                return [parseFloat(t.getAttribute('x')) + 2, parseFloat(t.getAttribute('y')) + parseFloat(t.getAttribute('height')) / 2 - 1.5];
            }
            """);

        await CopyAsync(item);
        var r = await Page.EvaluateAsync<int[]>("""
            async at => {
                // the image is drawn and written after the click: read until it is there (at most 10 s)
                let item = null;
                for (let k = 0; k < 50 && !item; k++) {
                    item = (await navigator.clipboard.read()).find(i => i.types.includes('image/png')) || null;
                    if (!item) await new Promise(res => setTimeout(res, 200));
                }
                if (!item) return [-1, -1, -1, 0, 0, 0];
                const bmp = await createImageBitmap(await item.getType('image/png'));
                const c = document.createElement('canvas'); c.width = bmp.width; c.height = bmp.height;
                const ctx = c.getContext('2d'); ctx.drawImage(bmp, 0, 0);
                const d = ctx.getImageData(Math.round(at[0] * 2), Math.round(at[1] * 2), 1, 1).data;
                // the light theme's ground, whichever theme the page shows
                const root = document.documentElement, shown = root.getAttribute('data-theme');
                root.setAttribute('data-theme', 'light');
                const p = document.createElement('div'); p.style.color = 'var(--dg-step-table)'; document.body.appendChild(p);
                const want = getComputedStyle(p).color.match(/\d+/g).map(Number); p.remove();
                root.setAttribute('data-theme', shown);
                return [d[0], d[1], d[2], want[0], want[1], want[2]];
            }
            """, at);

        for (var i = 0; i < 3; i++)
            Assert.True(Math.Abs(r[i] - r[i + 3]) <= 3, $"the table's ground was painted rgb({r[0]}, {r[1]}, {r[2]}), not rgb({r[3]}, {r[4]}, {r[5]})");
    }
}
