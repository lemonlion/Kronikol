using System.Net;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A loop's box and the Setup box on the page (5.8.0). A loop is a rounded light box with a thin light edge, its tab
/// shaded and its condition in plain type, where it was a square frame in a 1.5 px black line; the Setup box is a rounded
/// frame with a thin light edge. Both take the theme's colours: in the dark theme the Setup box was a near-white block with
/// the dark theme's light ink on it, and a loop's black frame and the ink of its tab were lost on the dark ground. The
/// diagram is the report's own emitter's, with a loop in the Setup box and a loop after it.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class DiagramLoopLookTests : DiagramNotePlaywrightBase
{
    public DiagramLoopLookTests(PlaywrightFixture fixture) : base(fixture) { }

    private static readonly DateTimeOffset Start = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);

    private static IEnumerable<RequestResponseLog> Call(string caller, string service, string method, string url, int at)
    {
        var trace = Guid.NewGuid();
        var id = Guid.NewGuid();
        yield return new RequestResponseLog("Collect milk", "s1", method, "{}", new Uri(url), [], service, caller,
            RequestResponseType.Request, trace, id, false) { Timestamp = Start.AddMilliseconds(at) };
        yield return new RequestResponseLog("Collect milk", "s1", method, "{}", new Uri(url), [], service, caller,
            RequestResponseType.Response, trace, id, false, HttpStatusCode.OK) { Timestamp = Start.AddMilliseconds(at + 5) };
    }

    // the setup's calls, three alike collapsed into a loop, the start of the action, and three alike again
    private static string Source(string? setupColour = null)
    {
        var logs = new List<RequestResponseLog>();
        logs.AddRange(Call("Test", "CowService", "PUT", "http://cows.internal/herd", 0));
        for (var i = 0; i < 3; i++) logs.AddRange(Call("CowService", "MilkDb", "Execute", "sql://milk-db/Cows", 10 + i * 10));
        logs.Add(new RequestResponseLog("Collect milk", "s1", "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { IsActionStart = true, Timestamp = Start.AddMilliseconds(50) });
        logs.AddRange(Call("Test", "CowService", "POST", "http://cows.internal/milk", 60));
        for (var i = 0; i < 3; i++) logs.AddRange(Call("CowService", "MilkDb", "Query", "sql://milk-db/Cows", 70 + i * 10));
        var source = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, separateSetup: true, setupHighlightColor: setupColour,
            collapseConsecutiveIdenticalCalls: true, systemUiFont: true).Single().PlantUmls.Single().PlainText;
        Assert.Matches(@"(?m)^partition\b", source);
        Assert.Equal(2, source.Split('\n').Count(l => l.StartsWith("loop ", StringComparison.Ordinal)));
        return source;
    }

    private async Task OpenAsync(string fileName, string theme, string? setupColour = null)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Dairy",
                Scenarios = [new() { Id = "s1", DisplayName = "Collect milk", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode("s1", "", Source(setupColour))],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Dairy", true,
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
    }

    // A colour as the browser resolves it, a token's value, the drawing's elements by the colour the source gives them
    // (the page's stylesheet paints over that colour, the attribute stays), and the contrast of two resolved colours
    private const string Helpers = """
        const col = v => { const p = document.createElement('div'); p.style.color = v; document.body.appendChild(p); const c = getComputedStyle(p).color; p.remove(); return c; };
        const tok = n => col('var(' + n + ')');
        const drawn = (sel) => Array.from(el.querySelectorAll(sel));
        const rgb = c => (c.match(/[\d.]+/g) || []).slice(0, 3).map(Number);
        const lum = c => { const [r, g, b] = rgb(c).map(v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }); return 0.2126 * r + 0.7152 * g + 0.0722 * b; };
        const contrast = (a, b) => { const x = lum(a), y = lum(b); return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); };
        """;

    private Task<T> InSvgAsync<T>(string body) => Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<T>(
        "el => { " + Helpers + body + " }");

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task A_loop_is_a_rounded_box_in_the_themes_loop_colours_with_its_condition_in_plain_type(string theme)
    {
        await OpenAsync($"LoopLook-{theme}.html", theme);

        var r = await InSvgAsync<string[]>($$"""
            const ground = drawn('rect[fill="{{DiagramLook.LoopFill}}" i]');
            const frame = drawn('rect[stroke="{{DiagramLook.LoopEdge}}" i]');
            const tab = drawn(':is(path, polygon)[fill="{{DiagramLook.LoopTab}}" i]');
            const keyword = Array.from(el.querySelectorAll('text')).filter(t => t.textContent === 'loop');
            const condition = Array.from(el.querySelectorAll('text')).find(t => t.textContent.startsWith('[×3'));
            const ink = keyword.length ? getComputedStyle(keyword[0]).fill : 'none';
            return [
                String(ground.length), String(frame.length), String(tab.length), String(keyword.length),
                ground.length ? getComputedStyle(ground[0]).fill : 'missing', tok('--dg-loop'),
                frame.length ? getComputedStyle(frame[0]).stroke : 'missing', tok('--dg-loop-edge'),
                tab.length ? getComputedStyle(tab[0]).fill : 'missing', tok('--dg-loop-tab'),
                frame.length ? String(parseFloat(frame[0].getAttribute('rx') || '0')) : '0',
                frame.length ? getComputedStyle(frame[0]).strokeWidth : 'missing',
                condition ? getComputedStyle(condition).fontWeight : 'missing',
                keyword.length ? getComputedStyle(keyword[0]).fontWeight : 'missing',
                tab.length ? contrast(ink, getComputedStyle(tab[0]).fill).toFixed(2) : '0',
                getComputedStyle(document.documentElement).getPropertyValue('--dg-loop').trim(),
            ];
            """);

        // the engine draws each of the two loops' grounds, frames and tabs
        Assert.True(int.Parse(r[0]) >= 2 && int.Parse(r[1]) >= 2 && int.Parse(r[2]) >= 2 && int.Parse(r[3]) == 2,
            $"grounds {r[0]}, frames {r[1]}, tabs {r[2]}, keywords {r[3]}");
        Assert.Equal(r[5], r[4]);
        Assert.Equal(r[7], r[6]);
        Assert.Equal(r[9], r[8]);
        Assert.True(double.Parse(r[10], System.Globalization.CultureInfo.InvariantCulture) > 0, $"the frame's corner radius is {r[10]}");
        Assert.Equal("1px", r[11]);
        Assert.Equal("400", r[12]);
        Assert.Equal("700", r[13]);
        Assert.True(double.Parse(r[14], System.Globalization.CultureInfo.InvariantCulture) >= 4.5, $"the tab's keyword reads at {r[14]}:1");
        Assert.Equal(theme == "dark" ? "#1d2632" : "#f4f7fb", r[15]);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_Setup_box_is_a_rounded_frame_in_the_themes_Setup_colours_and_its_labels_read(string theme)
    {
        await OpenAsync($"SetupLook-{theme}.html", theme);

        var r = await InSvgAsync<string[]>($$"""
            const ground = drawn('rect[fill="{{DiagramLook.SetupFill}}" i]');
            const frame = drawn('rect[stroke="{{DiagramLook.SetupEdge}}" i]');
            const title = Array.from(el.querySelectorAll('text')).find(t => t.textContent === 'Setup');
            const label = Array.from(el.querySelectorAll('text')).find(t => t.textContent.includes('PUT'));
            const fill = ground.length ? getComputedStyle(ground[0]).fill : 'missing';
            return [
                String(ground.length), String(frame.length),
                fill, tok('--dg-setup'),
                frame.length ? getComputedStyle(frame[0]).stroke : 'missing', tok('--dg-setup-edge'),
                frame.length ? String(parseFloat(frame[0].getAttribute('rx') || '0')) : '0',
                title ? contrast(getComputedStyle(title).fill, fill).toFixed(2) : '0',
                label ? contrast(getComputedStyle(label).fill, fill).toFixed(2) : '0',
            ];
            """);

        Assert.True(int.Parse(r[0]) >= 1 && int.Parse(r[1]) >= 1, $"grounds {r[0]}, frames {r[1]}");
        Assert.Equal(r[3], r[2]);
        Assert.Equal(r[5], r[4]);
        Assert.True(double.Parse(r[6], System.Globalization.CultureInfo.InvariantCulture) > 0, $"the frame's corner radius is {r[6]}");
        // the dark theme drew its light ink on a near-white box: about 1.1:1
        Assert.True(double.Parse(r[7], System.Globalization.CultureInfo.InvariantCulture) >= 4.5, $"the Setup title reads at {r[7]}:1");
        Assert.True(double.Parse(r[8], System.Globalization.CultureInfo.InvariantCulture) >= 4.5, $"a call's label in the Setup box reads at {r[8]}:1");
    }

    // A colour the options name for the Setup box is the user's choice, in either theme
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task A_Setup_box_in_a_colour_the_options_name_keeps_it(string theme)
    {
        await OpenAsync($"SetupOwnColour-{theme}.html", theme, setupColour: "#FFE4B5");

        var fill = await InSvgAsync<string>("""
            const ground = drawn('rect[fill="#FFE4B5" i]');
            return ground.length ? getComputedStyle(ground[0]).fill : 'missing';
            """);

        Assert.Equal("rgb(255, 228, 181)", fill);
    }

    // A copy is pasted into documents, nearly always on white: it carries the light theme's loop and Setup box whichever
    // theme the page shows
    [Fact]
    public async Task A_copy_of_the_diagram_carries_the_light_looks_loop_and_Setup_box_from_the_dark_theme()
    {
        await OpenAsync("LoopLookCopy.html", "dark");
        await Page.EvaluateAsync("() => navigator.clipboard.writeText('nothing copied yet')");
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
        await parent.Locator(".submenu").GetByText("Copy as SVG", new() { Exact = true }).First.ClickAsync();

        var r = await Page.EvaluateAsync<string[]>($$"""
            async () => {
                let text = '';
                for (let k = 0; k < 50 && !text.startsWith('<svg'); k++) {
                    text = await navigator.clipboard.readText();
                    if (!text.startsWith('<svg')) await new Promise(res => setTimeout(res, 200));
                }
                const col = v => { const p = document.createElement('div'); p.style.color = v; document.body.appendChild(p); const c = getComputedStyle(p).color; p.remove(); return c; };
                const paint = (e, prop) => {
                    if (!e) return 'missing';
                    const m = new RegExp('(?:^|;)\\s*' + prop + '\\s*:\\s*([^;]+)').exec(e.getAttribute('style') || '');
                    const v = m ? m[1].trim() : e.getAttribute(prop);
                    return v ? col(v) : 'unpainted';
                };
                const doc = new DOMParser().parseFromString(text, 'image/svg+xml');
                return [
                    paint(doc.querySelector('rect[fill="{{DiagramLook.LoopFill}}" i]'), 'fill'), col('{{DiagramLook.LoopFill}}'),
                    paint(doc.querySelector('rect[stroke="{{DiagramLook.LoopEdge}}" i]'), 'stroke'), col('{{DiagramLook.LoopEdge}}'),
                    paint(doc.querySelector('rect[fill="{{DiagramLook.SetupFill}}" i]'), 'fill'), col('{{DiagramLook.SetupFill}}'),
                    document.documentElement.getAttribute('data-theme'),
                ];
            }
            """);

        Assert.Equal(r[1], r[0]);
        Assert.Equal(r[3], r[2]);
        Assert.Equal(r[5], r[4]);
        Assert.Equal("dark", r[6]);
    }
}
