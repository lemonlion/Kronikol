using System.Text.Json;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// What the zoom facts read of a diagram (<c>plans/ZOOM_EVERY_DIAGRAM_PLAN.md</c>): its own slider, the level each of its
/// pictures is drawn at and where, and its marks. A diagram is the n-th <c>[data-diagram-type='plantuml']</c> of the
/// page; drawn in parts, its pictures are its parts', in order.
/// </summary>
internal static class ZoomFacts
{
    /// <summary>
    /// A diagram as the reader sees it. <see cref="Levels"/>: each picture's drawn width over its natural width, in
    /// percent. <see cref="Centres"/>: each picture's middle less the middle of the box's content, in pixels.
    /// <see cref="Lefts"/>: each picture's left edge on the screen.
    /// </summary>
    internal sealed record Diagram(int Slider, int Min, int Max, int PartSliders, double[] Levels, double[] Widths,
        double[] Centres, double[] Lefts, bool In, bool Out, bool Selected, int PartsSelected, string Cursor,
        int ScrollLeft, int ScrollWidth, int ClientWidth);

    /// <summary>The script that reads diagram <c>i</c>, as a page function of one argument.</summary>
    private const string ReadDiagram = """
        (i) => {
            const c = document.querySelectorAll("[data-diagram-type='plantuml']")[i];
            const s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
            const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
            const pics = (parts.length ? parts : [c]).map(b => b.querySelector(':scope > svg')).filter(Boolean);
            const cr = c.getBoundingClientRect(), cs = getComputedStyle(c);
            const left = cr.left + c.clientLeft + parseFloat(cs.paddingLeft);
            const right = cr.left + c.clientLeft + c.clientWidth - parseFloat(cs.paddingRight);
            const rects = pics.map(p => p.getBoundingClientRect());
            return JSON.stringify({
                Slider: s ? parseInt(s.value) : -1, Min: s ? parseInt(s.min) : -1, Max: s ? parseInt(s.max) : -1,
                PartSliders: c.querySelectorAll('.puml-fragment .diagram-zoom-controls').length,
                Levels: pics.map((p, k) => rects[k].width / p.width.baseVal.value * 100),
                Widths: rects.map(r => r.width),
                Centres: rects.map(r => (r.left + r.right) / 2 - (left + right) / 2),
                Lefts: rects.map(r => r.left),
                In: c.classList.contains('diagram-natural-size'), Out: c.classList.contains('diagram-zoomed-out'),
                Selected: c.classList.contains('diagram-selected'),
                PartsSelected: c.querySelectorAll('.puml-fragment.diagram-selected').length,
                Cursor: getComputedStyle(c).cursor, ScrollLeft: Math.round(c.scrollLeft),
                ScrollWidth: c.scrollWidth, ClientWidth: c.clientWidth
            });
        }
        """;

    internal static async Task<Diagram> ReadAsync(IPage page, int index) =>
        JsonSerializer.Deserialize<Diagram>(await page.EvaluateAsync<string>(ReadDiagram, index))!;

    /// <summary>Waits until diagram <paramref name="index"/> is drawn (every part, when in parts) and has its slider.</summary>
    internal static async Task WaitForSliderAsync(IPage page, int index, int timeoutMs = 60000)
    {
        await page.WaitForFunctionAsync("""
            (i) => {
                const c = document.querySelectorAll("[data-diagram-type='plantuml']")[i];
                if (!c || c._noteRendering) return false;
                const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                return (parts.length ? parts : [c]).every(b => b.querySelector(':scope > svg'))
                    && !!c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
            }
            """, index, new() { Timeout = timeoutMs, PollingInterval = 200 });
        await PlaywrightTestBase.TwoAnimationFramesAsync(page);
    }

    /// <summary>Waits until diagram <paramref name="index"/> is drawn (every part, when in parts), slider or not.</summary>
    internal static async Task WaitForDrawnAsync(IPage page, int index, int timeoutMs = 60000)
    {
        await page.WaitForFunctionAsync("""
            (i) => {
                const c = document.querySelectorAll("[data-diagram-type='plantuml']")[i];
                if (!c || c._noteRendering) return false;
                const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                return (parts.length ? parts : [c]).every(b => b.querySelector(':scope > svg'));
            }
            """, index, new() { Timeout = timeoutMs, PollingInterval = 200 });
        await PlaywrightTestBase.TwoAnimationFramesAsync(page);
    }

    /// <summary>
    /// Scrolls picture <paramref name="part"/> of diagram <paramref name="index"/> (its only picture when it is drawn
    /// whole) to the middle of the window and returns a point in its middle, below the report's sticky bar.
    /// </summary>
    internal static async Task<(float X, float Y)> PointInAsync(IPage page, int index, int part = 0)
    {
        var p = await page.EvaluateAsync<float[]>("""
            ([i, k]) => {
                const c = document.querySelectorAll("[data-diagram-type='plantuml']")[i];
                const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                const pic = (parts.length ? parts[k] : c).querySelector(':scope > svg');
                pic.scrollIntoView({ block: 'center' });
                const r = pic.getBoundingClientRect(), top = Math.max(r.top, 160), bottom = Math.min(r.bottom, innerHeight - 20);
                const p = [r.left + Math.min(r.width, innerWidth - r.left) / 2, (top + bottom) / 2];
                if (!isFinite(p[0]) || !isFinite(p[1]) || bottom <= top)
                    throw new Error('no point in picture ' + k + ' of ' + parts.length + ': ' + [r.left, r.top, r.width, r.height].join(','));
                return p;
            }
            """, new object[] { index, part });
        await PlaywrightTestBase.TwoAnimationFramesAsync(page);
        return (p[0], p[1]);
    }

    /// <summary>Sets diagram <paramref name="index"/>'s slider to <paramref name="level"/>, as a reader dragging it.</summary>
    internal static async Task SetLevelAsync(IPage page, int index, int level)
    {
        await page.EvaluateAsync("""
            ([i, v]) => {
                const c = document.querySelectorAll("[data-diagram-type='plantuml']")[i];
                const s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                s.value = String(v);
                s.dispatchEvent(new Event('input', { bubbles: true }));
            }
            """, new object[] { index, level });
        await PlaywrightTestBase.TwoAnimationFramesAsync(page);
    }

    /// <summary>
    /// Records, for every wheel event from now on, whether the page prevented it: a listener on the window runs after the
    /// page's own on the document. A Ctrl+wheel the page leaves alone zooms the whole page.
    /// </summary>
    internal static Task WatchWheelAsync(IPage page) => page.EvaluateAsync("""
        () => {
            window.__wheels = [];
            if (!window.__wheelWatched) {
                window.__wheelWatched = true;
                window.addEventListener('wheel', e => { if (e.ctrlKey) window.__wheels.push(e.defaultPrevented); }, { passive: true });
            }
        }
        """);

    internal static Task<bool[]> WheelsPreventedAsync(IPage page) => page.EvaluateAsync<bool[]>("() => window.__wheels || []");

    /// <summary>Ctrl with the wheel at a point, <paramref name="notches"/> times: up (in) when positive, down (out) when negative.</summary>
    internal static async Task CtrlWheelAsync(IPage page, float x, float y, int notches)
    {
        await page.Mouse.MoveAsync(x, y);
        await page.Keyboard.DownAsync("Control");
        for (var i = 0; i < Math.Abs(notches); i++) await page.Mouse.WheelAsync(0, notches > 0 ? -100 : 100);
        await page.Keyboard.UpAsync("Control");
        await PlaywrightTestBase.TwoAnimationFramesAsync(page);
    }

    /// <summary>
    /// Where the point of a picture under (<paramref name="x"/>, <paramref name="y"/>) is now, read as a fraction of the
    /// picture before and as screen pixels after, kept in the page for <see cref="PointMovedAsync"/>.
    /// </summary>
    internal static Task MarkPointAsync(IPage page, float x, float y) => page.EvaluateAsync("""
        ([x, y]) => {
            const pics = Array.from(document.querySelectorAll("[data-diagram-type='plantuml'] svg"));
            const pic = pics.find(p => { const r = p.getBoundingClientRect(); return x >= r.left && x <= r.right && y >= r.top && y <= r.bottom; });
            if (!pic) throw new Error('no picture under ' + x + ', ' + y + ' (scrollY ' + scrollY + '): ' + pics.map(p => {
                const r = p.getBoundingClientRect(); return [r.left, r.top, r.right, r.bottom].map(Math.round).join(',');
            }).join(' | '));
            const r = pic.getBoundingClientRect();
            window.__point = { pic, fx: (x - r.left) / r.width, fy: (y - r.top) / r.height, x, y };
        }
        """, new object[] { (double)x, (double)y });

    /// <summary>
    /// Opens the menu of the first diagram on its part <paramref name="part"/> (scrolled into view first: the page closes
    /// its menu on a scroll) and clicks <paramref name="item"/> under <paramref name="submenu"/>.
    /// </summary>
    internal static async Task MenuAsync(IPage page, int part, string submenu, string item)
    {
        await ScrollToPartAsync(page, part);
        await page.EvaluateAsync("""
            (k) => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                const svg = c.querySelectorAll(':scope > .puml-fragment')[k].querySelector(':scope > svg');
                const r = svg.getBoundingClientRect();
                svg.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: r.left + 8, clientY: r.top + 40 }));
            }
            """, part);
        var menu = page.Locator(".diagram-ctx-menu");
        await menu.WaitForAsync(new() { Timeout = 5000 });
        var parent = menu.Locator(".submenu-parent", new() { HasTextRegex = new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(submenu)) }).First;
        await parent.HoverAsync();
        await parent.Locator(".submenu").GetByText(item, new() { Exact = true }).First.ClickAsync();
    }

    /// <summary>Scrolls part <paramref name="part"/> of the first diagram into view and lets the scroll's event pass.</summary>
    internal static async Task ScrollToPartAsync(IPage page, int part)
    {
        await page.EvaluateAsync(
            "(k) => document.querySelector(\"[data-diagram-type='plantuml']\").querySelectorAll(':scope > .puml-fragment')[k].scrollIntoView({ block: 'center' })",
            part);
        await PlaywrightTestBase.TwoAnimationFramesAsync(page);
        await PlaywrightTestBase.TwoAnimationFramesAsync(page);
    }

    /// <summary>The clipboard's text once it starts with <paramref name="startsWith"/> (ten seconds at most).</summary>
    internal static Task<string> ClipboardTextAsync(IPage page, string startsWith) => page.EvaluateAsync<string>("""
        async (start) => {
            let text = '';
            for (let k = 0; k < 50 && !text.startsWith(start); k++) {
                text = await navigator.clipboard.readText();
                if (!text.startsWith(start)) await new Promise(res => setTimeout(res, 200));
            }
            return text;
        }
        """, startsWith);

    /// <summary>
    /// Every <c>&lt;?plantuml-src ...?&gt;</c> of an SVG's text, decoded as PlantUML encodes a source (raw deflate, then its
    /// own 64 characters); a source that does not decode reads as "ERROR" and the reason.
    /// </summary>
    internal static Task<string[]> PlantUmlSourcesAsync(IPage page, string svgText) => page.EvaluateAsync<string[]>("""
        async (text) => {
            const alphabet = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_';
            const out = [];
            for (const m of text.matchAll(/<\?plantuml-src ([^?]*)\?>/g)) {
                const enc = m[1].trim(), bits = Array.from(enc).map(ch => alphabet.indexOf(ch)), bytes = [];
                for (let k = 0; k < bits.length; k += 4) {
                    const [a, b, c, d] = [bits[k], bits[k + 1] ?? 0, bits[k + 2] ?? 0, bits[k + 3] ?? 0];
                    bytes.push((a << 2) | (b >> 4), ((b & 15) << 4) | (c >> 2), ((c & 3) << 6) | d);
                }
                // the last group of four characters can carry one or two bytes of padding after the deflate stream
                const n = Math.floor(enc.length * 6 / 8);
                let decoded = '';
                for (let cut = 0; cut <= 2 && (!decoded || decoded.startsWith('ERROR')); cut++) {
                    const stream = new Blob([new Uint8Array(bytes.slice(0, n - cut))]).stream().pipeThrough(new DecompressionStream('deflate-raw'));
                    try { decoded = await new Response(stream).text(); } catch (e) { decoded = 'ERROR ' + e; }
                }
                out.push(decoded);
            }
            return out;
        }
        """, svgText);

    /// <summary>How far, across and down, the point marked by <see cref="MarkPointAsync"/> now is from where it was.</summary>
    internal static async Task<(double Across, double Down)> PointMovedAsync(IPage page)
    {
        var d = await page.EvaluateAsync<double[]>("""
            () => {
                const p = window.__point, r = p.pic.getBoundingClientRect();
                return [r.left + p.fx * r.width - p.x, r.top + p.fy * r.height - p.y];
            }
            """);
        return (d[0], d[1]);
    }
}
