using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A diagram the page draws in parts acts as one diagram (<c>plans/ZOOM_EVERY_DIAGRAM_PLAN.md</c> Z12 and Z13, facts 19
/// to 24 of section 6.2): a click on any part selects all of it, its pictures are copied and saved as one, it ends once,
/// one placeholder stands for the parts still being drawn, and the lines the page adds to draw it never reach a source
/// the reader copies or opens. Until 5.7.0 a click selected the part under it, and Copy, Save and Open as PNG or SVG gave
/// that part alone.
/// </summary>
[Collection(PlaywrightCollections.Zoom)]
public class DiagramInPartsAsOneTests : PlaywrightTestBase
{
    public DiagramInPartsAsOneTests(PlaywrightFixture fixture) : base(fixture) { }

    /// <summary>The fixture's diagram: thirty calls cut every ten, so three parts holding calls 1-10, 11-20 and 21-30.</summary>
    private async Task OpenAsync(string fileName, string? source = null, int partHeight = 900)
    {
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithDiagramInParts(TempDir, OutputDir, fileName, source, partHeight));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await ZoomFacts.WaitForSliderAsync(Page, 0);
    }

    private Task<int> PartCountAsync() =>
        Page.EvaluateAsync<int>("() => document.querySelector(\"[data-diagram-type='plantuml']\").querySelectorAll(':scope > .puml-fragment').length");

    // 19
    [Fact]
    public async Task A_click_on_any_part_selects_the_whole_diagram_and_a_second_click_takes_it_off()
    {
        await OpenAsync("OneSelect.html");
        Assert.Equal(3, await PartCountAsync());

        var (x, y) = await ZoomFacts.PointInAsync(Page, 0, part: 1);
        await Page.Mouse.ClickAsync(x, y);

        var ring = await Page.EvaluateAsync<bool[]>("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const cr = c.getBoundingClientRect();
                const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                return [c.classList.contains('diagram-selected'),
                    parts.every(p => { const r = p.getBoundingClientRect(); return r.top >= cr.top - 0.5 && r.bottom <= cr.bottom + 0.5; }),
                    !parts.some(p => p.classList.contains('diagram-selected')),
                    getComputedStyle(c).boxShadow !== 'none'];
            }
            """);
        Assert.True(ring[0], "the diagram's container is selected");
        Assert.True(ring[1], "its ring goes round every part");
        Assert.True(ring[2], "no part is selected on its own");
        Assert.True(ring[3], "the ring is drawn");

        (x, y) = await ZoomFacts.PointInAsync(Page, 0, part: 2);
        await Page.Mouse.ClickAsync(x, y);
        Assert.False((await ZoomFacts.ReadAsync(Page, 0)).Selected, "a second click, on another part, takes it off");
    }

    private Task MenuAsync(int part, string submenu, string item) => ZoomFacts.MenuAsync(Page, part, submenu, item);

    private Task ScrollToPartAsync(int part) => ZoomFacts.ScrollToPartAsync(Page, part);

    private Task<string> ClipboardTextAsync(string startsWith) => ZoomFacts.ClipboardTextAsync(Page, startsWith);

    // 20
    [Fact]
    public async Task Copy_as_SVG_over_a_part_gives_the_whole_diagram_as_one_picture_with_one_source()
    {
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await OpenAsync("OneCopySvg.html");
        await Page.EvaluateAsync("() => navigator.clipboard.writeText('nothing copied yet')");

        await MenuAsync(1, "Copy image", "Copy as SVG");
        var svg = await ClipboardTextAsync("<svg");
        Assert.StartsWith("<svg", svg);

        var r = await Page.EvaluateAsync<string[]>("""
            (text) => {
                const doc = new DOMParser().parseFromString(text, 'image/svg+xml');
                const parsed = !doc.querySelector('parsererror');
                const root = doc.documentElement;
                const nested = Array.from(root.children).filter(e => e.localName === 'svg');
                // each part's own calls in its own picture, in order
                const where = n => nested.findIndex(s => Array.from(s.querySelectorAll('text')).some(t => t.textContent.trim() === 'call' + n));
                const order = [5, 15, 25].map(where).join(',');
                // each part's ids its own: prefixed by its part, and none used by another part (the engine can give
                // the pieces of one message the same id within a picture, as a diagram drawn whole has them)
                const idsOf = s => [s].concat(Array.from(s.querySelectorAll('[id]'))).map(e => e.id).filter(Boolean);
                const foreign = nested.flatMap((s, k) => idsOf(s).filter(id => !id.startsWith('p' + (k + 1) + '-')));
                const shared = nested.flatMap((s, k) => idsOf(s).filter(id => nested.some((o, j) => j !== k && idsOf(o).includes(id))));
                const unique = !foreign.length && !shared.length && nested.every(s => idsOf(s).length > 0);
                const twice = foreign.concat(shared).slice(0, 5).join(' ');
                return [String(parsed), String(nested.length), order, String(unique),
                    document.querySelector("[data-diagram-type='plantuml']").getAttribute('data-plantuml'),
                    parsed ? '' : doc.querySelector('parsererror').textContent.slice(0, 300), twice];
            }
            """, svg);

        Assert.True(r[0] == "true", $"the copy parses as XML: {r[5]}");
        Assert.Equal("3", r[1]);
        Assert.Equal("0,1,2", r[2]);
        Assert.True(r[3] == "true", $"no id is used twice: {r[6]}");
        // the engine writes its source into every part, which the page holds as a comment: none of them is copied, as a
        // comment or as an instruction
        var withSource = await Page.EvaluateAsync<int>("""
            () => Array.from(document.querySelectorAll("[data-diagram-type='plantuml'] > .puml-fragment > svg")).filter(s => {
                const w = document.createTreeWalker(s, NodeFilter.SHOW_COMMENT);
                for (let c; (c = w.nextNode());) if (/^\?plantuml-src\b/.test(c.data)) return true;
                return false;
            }).length
            """);
        Assert.True(withSource == 3, $"premise: every part holds the engine's source ({withSource} of 3 do)");
        Assert.Equal(1, svg.Split("plantuml-src").Length - 1);
        var sources = await ZoomFacts.PlantUmlSourcesAsync(Page, svg);
        Assert.Single(sources);
        Assert.Equal(r[4].Replace("\r", ""), sources[0].Replace("\r", ""));
    }

    private async Task<(int Width, int Height, string Name)> SavePngAsync(int part, string item)
    {
        var download = await Page.RunAndWaitForDownloadAsync(() => MenuAsync(part, "Save image", item));
        var file = Path.Combine(TempDir, Guid.NewGuid().ToString("N") + ".png");
        await download.SaveAsAsync(file);
        var b = await File.ReadAllBytesAsync(file);
        int Be(int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
        return (Be(16), Be(20), download.SuggestedFilename);
    }

    private Task<double[]> NaturalSizeAsync() => Page.EvaluateAsync<double[]>("""
        () => {
            const c = document.querySelector("[data-diagram-type='plantuml']");
            const pics = Array.from(c.querySelectorAll(':scope > .puml-fragment > svg'));
            const shift = p => p.parentElement._kShift || 0;
            return [Math.max(...pics.map(p => p.width.baseVal.value + shift(p))), pics.reduce((h, p) => h + p.height.baseVal.value, 0)];
        }
        """);

    // 21, a diagram short enough for a canvas
    [Fact]
    public async Task Save_as_PNG_over_a_part_gives_the_whole_diagram_at_twice_its_natural_size()
    {
        await OpenAsync("OneSavePng.html");
        var natural = await NaturalSizeAsync();

        var png = await SavePngAsync(1, "Save as PNG");

        Assert.True(Math.Abs(png.Width - 2 * natural[0]) <= 4, $"twice the diagram's width: {png.Width} for {natural[0]:F1}");
        Assert.True(png.Height >= 2 * natural[1] - 4, $"at least twice the parts' heights together: {png.Height} for {natural[1]:F1}");
    }

    // 21, a diagram too long for a canvas even at its own size: the PNG items name the part under the click and give it
    [Fact]
    public async Task A_diagram_too_long_for_a_canvas_saves_the_part_under_the_click_and_its_menu_says_so()
    {
        await OpenAsync("OneSavePngTooLong.html", ReportTestHelper.PartsSource(700), partHeight: 12000);
        var parts = await PartCountAsync();
        Assert.True(parts >= 3, $"the diagram is drawn in parts: {parts}");
        var height = (await NaturalSizeAsync())[1];
        Assert.True(height > 33000, $"the diagram is taller than a canvas: {height:F0} px");

        var png = await SavePngAsync(1, $"Save part 2 of {parts} as PNG");

        var part = await Page.EvaluateAsync<double[]>("""
            () => {
                const p = document.querySelector("[data-diagram-type='plantuml']").querySelectorAll(':scope > .puml-fragment > svg')[1];
                return [p.width.baseVal.value, p.height.baseVal.value];
            }
            """);
        Assert.True(Math.Abs(png.Height - 2 * part[1]) <= 4, $"the second part at twice its size: {png.Height} for {part[1]:F1}");
    }

    // 22
    [Fact]
    public async Task Every_part_but_the_last_is_drawn_without_a_footbox_and_no_source_holds_the_line_that_does_it()
    {
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await OpenAsync("OneFootbox.html");

        // a participant drawn with its footbox has its name twice: at the top, and again under the last message
        var names = await Page.EvaluateAsync<int[]>("""
            () => Array.from(document.querySelector("[data-diagram-type='plantuml']").querySelectorAll(':scope > .puml-fragment > svg'))
                .map(svg => Array.from(svg.querySelectorAll('text')).filter(t => t.textContent.trim() === 'Gateway').length)
            """);
        Assert.Equal(new[] { 1, 1, 2 }, names);

        var stored = await Page.EvaluateAsync<bool>("""
            () => Array.from(document.querySelectorAll("[data-diagram-type='plantuml'], [data-diagram-type='plantuml'] .puml-fragment"))
                .some(e => /hide footbox/.test(e.getAttribute('data-plantuml') || '') || /hide footbox/.test(e._fullSource || '') || /hide footbox/.test(e._noteOriginalSource || ''))
            """);
        Assert.False(stored, "no stored source holds hide footbox");

        await Page.EvaluateAsync("() => navigator.clipboard.writeText('nothing copied yet')");
        await ScrollToPartAsync(1);
        await Page.EvaluateAsync("""
            () => {
                const svg = document.querySelector("[data-diagram-type='plantuml']").querySelectorAll(':scope > .puml-fragment > svg')[1];
                const r = svg.getBoundingClientRect();
                svg.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: r.left + 8, clientY: r.top + 40 }));
            }
            """);
        await Page.Locator(".diagram-ctx-menu").GetByText("Copy PlantUML source", new() { Exact = true }).First.ClickAsync();
        var copied = await ClipboardTextAsync("@startuml");
        Assert.StartsWith("@startuml", copied);
        Assert.DoesNotContain("hide footbox", copied);

        await Page.EvaluateAsync("""
            () => {
                const svg = document.querySelector("[data-diagram-type='plantuml']").querySelectorAll(':scope > .puml-fragment > svg')[1];
                const r = svg.getBoundingClientRect();
                svg.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: r.left + 8, clientY: r.top + 40 }));
            }
            """);
        var tab = await Context.RunAndWaitForPageAsync(() =>
            Page.Locator(".diagram-ctx-menu").GetByText("Open PlantUML source in new tab", new() { Exact = true }).First.ClickAsync());
        await tab.WaitForFunctionAsync("() => (document.body && document.body.innerText || '').includes('@startuml')", null, new() { Timeout = 10000, PollingInterval = 200 });
        var opened = await tab.EvaluateAsync<string>("() => document.body.innerText");
        Assert.DoesNotContain("hide footbox", opened);
        await tab.CloseAsync();
    }

    // 23: the stylesheet's rule, read where the report applies it
    [Fact]
    public async Task Only_the_first_part_not_yet_drawn_shows_the_placeholder_and_the_rest_take_no_room()
    {
        await OpenAsync("OnePlaceholder.html");

        var shown = await Page.EvaluateAsync<string[]>("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const box = document.createElement('div');
                box.className = 'plantuml-browser';
                box.setAttribute('data-rendered', '1');
                for (let k = 0; k < 3; k++) { const p = document.createElement('div'); p.className = 'puml-fragment'; box.appendChild(p); }
                c.parentElement.appendChild(box);
                const read = () => Array.from(box.children).map(p => getComputedStyle(p).display + ' ' + Math.round(p.getBoundingClientRect().height)
                    + ' ' + (getComputedStyle(p, '::before').content || '')).join(' | ');
                const before = read();
                box.children[0].setAttribute('data-rendered', '1');
                box.children[0].appendChild(document.createElementNS('http://www.w3.org/2000/svg', 'svg'));
                const after = read();
                box.remove();
                return [before, after];
            }
            """);

        var before = shown[0].Split(" | ");
        Assert.Contains("Rendering diagram", before[0]);
        Assert.StartsWith("none", before[1]);
        Assert.StartsWith("none", before[2]);
        var after = shown[1].Split(" | ");
        Assert.Contains("Rendering diagram", after[1]);
        Assert.StartsWith("none", after[2]);
    }

    // 24: a pin of what already acted as one (5.7.0 keeps it)
    [Fact]
    public async Task Copy_highlighted_text_across_two_parts_gives_the_first_parts_rows_then_the_seconds()
    {
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await OpenAsync("OneHighlighted.html");
        await Page.EvaluateAsync("() => navigator.clipboard.writeText('nothing copied yet')");

        await Page.EvaluateAsync("""
            () => {
                const parts = document.querySelector("[data-diagram-type='plantuml']").querySelectorAll(':scope > .puml-fragment > svg');
                const find = (svg, label) => Array.from(svg.querySelectorAll('text')).find(t => t.textContent.trim() === label);
                const from = find(parts[0], 'ok10'), to = find(parts[1], 'call11');
                const range = document.createRange();
                range.setStart(from.firstChild, 0);
                range.setEnd(to.firstChild, to.firstChild.data.length);
                const sel = window.getSelection(); sel.removeAllRanges(); sel.addRange(range);
                const r = to.getBoundingClientRect();
                to.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: r.left + 2, clientY: r.top + 2 }));
            }
            """);
        await Page.Locator(".diagram-ctx-menu").GetByText("Copy Highlighted Text", new() { Exact = true }).First.ClickAsync();
        var text = await ClipboardTextAsync("ok10");

        Assert.StartsWith("ok10", text);
        Assert.EndsWith("call11", text.TrimEnd());
        Assert.True(text.IndexOf("ok10", StringComparison.Ordinal) < text.IndexOf("call11", StringComparison.Ordinal));
    }
}
