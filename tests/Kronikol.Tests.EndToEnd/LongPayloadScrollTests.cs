using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A payload whose body would draw more than <c>PayloadScrollLines</c> lines (40 by default) is drawn that many lines tall,
/// with its <c>...</c> line and a clear row as wide as its widest line, and a scroll box over its body rows holds the whole
/// body (<c>plans/LONG_NOTE_SCROLL_PLAN.md</c>, 5.4.0). Until 5.4.0 a long payload was drawn in full: one of 1,500 lines
/// was cut into three diagrams. The report is written by the generator from captured calls
/// (<see cref="LongPayloadReports.ScrollMix"/>), in the JSON view with headers shown, so a body's lines are the lines its
/// note draws, and starts Expanded unless a fact says otherwise.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class LongPayloadScrollTests : PlaywrightTestBase
{
    public LongPayloadScrollTests(PlaywrightFixture fixture) : base(fixture) { }

    private static ResolvedToggleDefaults Expanded(int scrollLines = 40) =>
        ReportTestHelper.ClassicStart with { Details = ReportDetailsState.Expanded, PayloadScrollLines = scrollLines };

    /// <summary>The page's helpers for these facts: the diagram, a note's original index by the key its body starts with,
    /// its drawn group, its box, and the box's lines.</summary>
    private const string Helpers = """
        () => {
            var h = window.__scroll = {};
            h.container = () => document.querySelector('[data-diagram-type="plantuml"]');
            h.svgs = (c) => Array.from((c || h.container()).querySelectorAll('svg')).filter(s => !s.closest('.note-scroll'));
            h.noteIndex = (key, c) => {
                c = c || h.container();
                return window._parseNoteBlocks(c._noteOriginalSource || c.getAttribute('data-plantuml'))
                    .findIndex(b => b.contentLines.some(l => l.indexOf('"' + key + '_0001"') >= 0));
            };
            h.group = (idx, c) => {
                var found = null;
                h.svgs(c).forEach(svg => window._findNoteGroups(svg).forEach(g => {
                    var ix = window._noteIndicesOf(g);
                    if (ix && ix.original === idx) found = g;
                }));
                return found;
            };
            h.frame = (idx, c) => (c || h.container()).querySelector('.note-scroll-frame[data-note="' + idx + '"]');
            h.box = (idx, c) => { var f = h.frame(idx, c); return f ? f.querySelector('.note-scroll') : null; };
            h.lines = (idx, c) => { var b = h.box(idx, c); return b ? b.textContent.split('\n') : null; };
            h.rows = (g) => { var ys = {}; g.texts.forEach(t => { if (getComputedStyle(t).visibility !== 'hidden') ys[t.getAttribute('y')] = 1; }); return Object.keys(ys).length; };
            h.settled = () => {
                var c = h.container();
                return c && !c._noteRendering && !window._plantumlRendering && !document.querySelector('[data-pending]')
                    && h.svgs(c).length > 0 && h.svgs(c).every(s => s.querySelector('.note-hover-rect'));
            };
            return true;
        }
        """;

    private async Task OpenAsync(string fileName, ResolvedToggleDefaults toggles, bool manyCalls = false)
    {
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await Page.GotoAsync(LongPayloadReports.ScrollMix(TempDir, OutputDir, fileName, toggles, manyCalls));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg(60000);
        await Page.EvaluateAsync(Helpers);
        await SettledAsync();
    }

    private async Task SettledAsync()
    {
        await Page.WaitForFunctionAsync("() => window.__scroll && window.__scroll.settled()", null,
            new() { Timeout = 90000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
    }

    private Task<int> NoteIndexAsync(string key) => Page.EvaluateAsync<int>($"() => window.__scroll.noteIndex('{key}')");

    /// <summary>Brings the note to the middle of the window and waits for its box to be made and filled.</summary>
    private async Task<int> BoxAsync(string key)
    {
        var idx = await NoteIndexAsync(key);
        Assert.True(idx >= 0, $"premise: a note holds {key}");
        await Page.EvaluateAsync($"() => window.__scroll.group({idx}).paths[0].scrollIntoView({{ block: 'center' }})");
        await Page.WaitForFunctionAsync($"() => !!window.__scroll.box({idx})", null, new() { Timeout = 15000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        return idx;
    }

    private Task<string[]> LinesAsync(int idx) => Page.EvaluateAsync<string[]>($"() => window.__scroll.lines({idx})");

    /// <summary>Runs a control's command and waits for its queue to end.</summary>
    private async Task CommandAsync(string action)
    {
        await Page.EvaluateAsync(action);
        await SettledAsync();
    }

    private sealed record Rect(double X, double Y, double W, double H);

    [Fact]
    public async Task T1_An_expanded_1500_line_response_is_one_diagram_and_its_box_holds_every_line_on_its_own_row()
    {
        await OpenAsync("scroll-t1.html", Expanded());
        var parts = await Page.EvaluateAsync<int>("() => window.__scroll.container().querySelectorAll(':scope > .puml-fragment').length");
        Assert.Equal(0, parts);
        Assert.DoesNotContain("Continued", await Page.EvaluateAsync<string>("() => window.__scroll.container().textContent"));

        var idx = await BoxAsync("r1500");
        var lines = await LinesAsync(idx);
        Assert.Equal(1500, lines.Length);
        Assert.Equal("{", lines[0]);
        Assert.Equal("}", lines[^1]);
        Assert.Contains("\"r1500_0700\": \"the widest value of this payload", lines[700]);
        // the clear row keeps the note as wide as its widest line, deep in the body: no line of the box wraps
        var wraps = await Page.EvaluateAsync<bool>($$"""
            () => {
                var b = window.__scroll.box({{idx}});
                var lh = parseFloat(b.style.lineHeight);
                return b.scrollHeight > b.textContent.split('\n').length * lh + 2 || b.scrollWidth > b.clientWidth + 1;
            }
            """);
        Assert.False(wraps, "a line of the box wraps: the note is narrower than its widest line");
    }

    [Fact]
    public async Task T2_A_40_line_response_has_no_box_and_a_41_line_one_draws_40_lines_under_its_box()
    {
        await OpenAsync("scroll-t2.html", Expanded());
        var forty = await NoteIndexAsync("r40");
        await Page.EvaluateAsync($"() => window.__scroll.group({forty}).paths[0].scrollIntoView({{ block: 'center' }})");
        await TwoAnimationFramesAsync(Page);
        Assert.False(await Page.EvaluateAsync<bool>($"() => !!window.__scroll.frame({forty})"));
        // its two meta rows (the header and the blank line under it) and its 40 body lines
        Assert.Equal(42, await Page.EvaluateAsync<int>($"() => window.__scroll.rows(window.__scroll.group({forty}))"));

        var fortyOne = await BoxAsync("r41");
        Assert.Equal(41, (await LinesAsync(fortyOne)).Length);
        // under the box: 40 body rows and the `...` row
        var under = await Page.EvaluateAsync<int>($$"""
            () => {
                var ys = {};
                window.__scroll.group({{fortyOne}}).texts.forEach(t => { if (t.classList.contains('note-scroll-under')) ys[t.getAttribute('y')] = 1; });
                return Object.keys(ys).length;
            }
            """);
        Assert.Equal(41, under);
    }

    /// <summary>The box holds the body as "Copy box text" reads it: the width budget's breaks joined and the escapes
    /// undone. The copy also holds the note's header lines, drawn above the box.</summary>
    [Fact]
    public async Task T3_The_box_holds_what_Copy_box_text_copies_below_the_headers()
    {
        await OpenAsync("scroll-t3.html", Expanded());
        var idx = await BoxAsync("r60x");
        var box = string.Join("\n", await LinesAsync(idx));
        // the run the generator broke at 120 is one line again, and the markup it escaped reads as written
        Assert.Contains("\"r60x_0003\": \"" + new string('x', 300) + "\",", box);
        Assert.Contains("creole: **not bold** //not italic// --not struck-- __not underlined__ <b>not a tag</b> ~tilde [[not a link]]", box);
        Assert.Equal(60, box.Split('\n').Length);

        await Page.EvaluateAsync($$"""
            () => {
                var f = window.__scroll.frame({{idx}}), r = f.getBoundingClientRect();
                f.querySelector('.note-scroll').dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true,
                    clientX: r.left + r.width / 2, clientY: r.top + r.height / 2 }));
            }
            """);
        var item = Page.Locator(".diagram-ctx-menu").GetByText("Copy box text", new() { Exact = true });
        await item.WaitForAsync(new() { Timeout = 5000 });
        await item.ClickAsync();
        // the copy keeps a source's CRLF line ends
        var copied = (await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()")).Replace("\r\n", "\n");
        Assert.EndsWith("\n" + box, copied);
        Assert.StartsWith("[Content-Type=application/json]", copied);
    }

    [Fact]
    public async Task T4_Headers_stay_above_the_box_and_with_them_hidden_the_box_starts_below_the_buttons_row()
    {
        await OpenAsync("scroll-t4.html", Expanded());
        var idx = await BoxAsync("r150");
        var shown = await Page.EvaluateAsync<double[]>($$"""
            () => {
                var g = window.__scroll.group({{idx}}), f = window.__scroll.frame({{idx}});
                var header = g.texts.find(t => (t.textContent || '').indexOf('Content-Type') >= 0);
                return [header ? header.getBBox().y + header.getBBox().height : -1, parseFloat(f.getAttribute('y')),
                    getComputedStyle(header).visibility === 'hidden' ? 1 : 0];
            }
            """);
        Assert.True(shown[0] > 0 && shown[0] <= shown[1] + 0.5, $"the header row ends at {shown[0]:F1}, the box starts at {shown[1]:F1}");
        Assert.Equal(0, shown[2]);

        await CommandAsync("() => window._toggleHeaders(document.querySelector('.toolbar-right .toggle-btn[data-toggle=\"headers\"]'))");
        idx = await BoxAsync("r150");
        var hidden = await Page.EvaluateAsync<double[]>($$"""
            () => {
                var g = window.__scroll.group({{idx}}), f = window.__scroll.frame({{idx}});
                var top = Math.min.apply(null, g.paths.map(p => p.getBBox().y));
                return [top, parseFloat(f.getAttribute('y'))];
            }
            """);
        // the top-right buttons sit 3 px into the note and are 14 px tall
        Assert.True(hidden[1] >= hidden[0] + 3 + 14 - 1, $"the note starts at {hidden[0]:F1} and its box at {hidden[1]:F1}");
    }

    [Fact]
    public async Task T5_The_wheel_over_the_box_scrolls_the_box_and_not_the_page()
    {
        await OpenAsync("scroll-t5.html", Expanded());
        var idx = await BoxAsync("r1500");
        var at = await Page.EvaluateAsync<double[]>($$"""
            () => { var r = window.__scroll.frame({{idx}}).getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2, window.scrollY]; }
            """);
        await Page.Mouse.MoveAsync((float)at[0], (float)at[1]);
        await Page.Mouse.WheelAsync(0, 300);
        await Page.WaitForFunctionAsync($"() => window.__scroll.box({idx}).scrollTop > 0", null, new() { Timeout = 5000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        Assert.Equal(at[2], await Page.EvaluateAsync<double>("() => window.scrollY"));
    }

    /// <summary>Opens the report in a window narrow enough for the diagram to zoom.</summary>
    private async Task OpenNarrowAsync(string fileName)
    {
        await Page.SetViewportSizeAsync(900, 1000);
        await OpenAsync(fileName, Expanded());
        await Page.WaitForFunctionAsync("() => !!window.__scroll.container().querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider')",
            null, new() { Timeout = 15000, PollingInterval = 200 });
    }

    private async Task ZoomToAsync(int percent)
    {
        await Page.EvaluateAsync($$"""
            () => {
                var s = window.__scroll.container().querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                s.value = String(Math.max(parseInt(s.min), {{percent}}));
                s.dispatchEvent(new Event('input', { bubbles: true }));
            }
            """);
        await TwoAnimationFramesAsync(Page);
    }

    [Fact]
    public async Task T6_Zooming_with_the_slider_keeps_the_box_in_its_place_in_the_note()
    {
        await OpenNarrowAsync("scroll-t6.html");
        var idx = await BoxAsync("r150");
        const string place = """
            (idx) => {
                var g = window.__scroll.group(idx), f = window.__scroll.frame(idx).getBoundingClientRect();
                var n = g.paths.map(p => p.getBoundingClientRect()).reduce((a, b) => a.width * a.height > b.width * b.height ? a : b);
                return [(f.left - n.left) / n.width, (f.top - n.top) / n.height, f.width / n.width, f.height / n.height];
            }
            """;
        var fit = await Page.EvaluateAsync<double[]>(place, idx);
        await ZoomToAsync(100);
        await BoxAsync("r150");
        var full = await Page.EvaluateAsync<double[]>(place, idx);
        for (var i = 0; i < 4; i++) Assert.True(Math.Abs(fit[i] - full[i]) < 0.001, $"fraction {i}: {fit[i]:F4} at the fit, {full[i]:F4} at full size");
    }

    [Fact]
    public async Task T7_Hovering_the_box_shows_the_note_s_buttons()
    {
        await OpenAsync("scroll-t7.html", Expanded());
        var idx = await BoxAsync("r150");
        await Page.EvaluateAsync($"() => window.__scroll.frame({idx}).dispatchEvent(new MouseEvent('mouseenter'))");
        var shown = await Page.EvaluateAsync<string[]>($$"""
            () => {
                var g = window.__scroll.group({{idx}}), b = g.paths[0].getBBox();
                return Array.from(g.paths[0].ownerSVGElement.querySelectorAll('.note-toggle-icon')).filter(e => {
                    var r = e.getBBox();
                    return e.style.opacity === '1' && r.x >= b.x - 1 && r.x + r.width <= b.x + b.width + 1;
                }).map(e => e.getAttribute('data-note-btn'));
            }
            """);
        Assert.Contains("minus", shown);
        Assert.Contains("truncate-bottom", shown);
    }

    [Fact]
    public async Task T8_A_drag_inside_the_box_of_a_zoomed_diagram_selects_its_text_and_pans_nothing()
    {
        await OpenNarrowAsync("scroll-t8.html");
        await ZoomToAsync(100);
        var idx = await BoxAsync("r150");
        var at = await Page.EvaluateAsync<double[]>($$"""
            () => {
                var c = window.__scroll.container(), r = window.__scroll.frame({{idx}}).getBoundingClientRect();
                return [r.left + 10, r.top + 8, r.left + Math.min(r.width - 20, 200), r.top + 60, c.scrollLeft, window.scrollY];
            }
            """);
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__scroll.container().classList.contains('diagram-natural-size')"),
            "premise: the diagram is zoomed");
        await Page.Mouse.MoveAsync((float)at[0], (float)at[1]);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)at[2], (float)at[3], new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await TwoAnimationFramesAsync(Page);
        var after = await Page.EvaluateAsync<JsonElement>("""
            () => ({ selected: String(window.getSelection()), left: window.__scroll.container().scrollLeft, y: window.scrollY })
            """);
        Assert.Contains("r150_", after.GetProperty("selected").GetString());
        Assert.Equal(at[4], after.GetProperty("left").GetDouble());
        Assert.Equal(at[5], after.GetProperty("y").GetDouble());
    }

    [Fact]
    public async Task T9_A_click_inside_the_box_leaves_the_diagram_s_selection_as_it_was()
    {
        await OpenAsync("scroll-t9.html", Expanded());
        var idx = await BoxAsync("r150");
        await Page.EvaluateAsync($"() => window.__scroll.box({idx}).click()");
        Assert.False(await Page.EvaluateAsync<bool>("() => window.__scroll.container().classList.contains('diagram-selected')"));
        await Page.EvaluateAsync($"() => window.__scroll.group({idx}).paths[0].dispatchEvent(new MouseEvent('click', {{ bubbles: true }}))");
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__scroll.container().classList.contains('diagram-selected')"),
            "premise: a click on the note selects the diagram");
        await Page.EvaluateAsync($"() => window.__scroll.box({idx}).click()");
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__scroll.container().classList.contains('diagram-selected')"));
    }

    [Fact]
    public async Task T10_The_box_is_a_Tab_stop_whose_keys_scroll_it_and_move_nothing_else()
    {
        await OpenAsync("scroll-t10.html", Expanded());
        var idx = await BoxAsync("r1500");
        // a group, so its name is read out: ARIA does not allow a name on an element with no role
        var named = await Page.EvaluateAsync<string[]>($$"""
            () => { var b = window.__scroll.box({{idx}}); return [b.getAttribute('tabindex'), b.getAttribute('aria-label'), b.getAttribute('role')]; }
            """);
        Assert.Equal(["0", "Payload, 1,500 lines", "group"], named);
        await Page.EvaluateAsync($"() => window.__scroll.box({idx}).focus()");
        const string state = """
            (idx) => ({
                top: window.__scroll.box(idx).scrollTop,
                focused: document.querySelectorAll('.scenario-focused').length,
                tab: (document.querySelector('.report-tab[aria-selected="true"]') || {}).id || '',
                view: (document.querySelector('.view-btn[aria-selected="true"]') || {}).id || '',
                active: document.activeElement === window.__scroll.box(idx)
            })
            """;
        var before = await Page.EvaluateAsync<JsonElement>(state, idx);
        var top = before.GetProperty("top").GetDouble();
        foreach (var (key, down) in new[] { ("ArrowDown", true), ("PageDown", true), ("End", true), ("ArrowUp", false), ("PageUp", false), ("Home", false) })
        {
            await Page.Keyboard.PressAsync(key);
            await Page.WaitForFunctionAsync($"(t) => {{ var s = window.__scroll.box({idx}).scrollTop; return {(down ? "s > t" : "s < t")}; }}", top,
                new() { Timeout = 5000, PollingInterval = 200 });
            var now = await Page.EvaluateAsync<JsonElement>(state, idx);
            top = now.GetProperty("top").GetDouble();
            Assert.Equal(before.GetProperty("focused").GetInt32(), now.GetProperty("focused").GetInt32());
            Assert.Equal(before.GetProperty("tab").GetString(), now.GetProperty("tab").GetString());
            Assert.Equal(before.GetProperty("view").GetString(), now.GetProperty("view").GetString());
            Assert.True(now.GetProperty("active").GetBoolean(), $"the box lost the focus on {key}");
        }
    }

    /// <summary>Selects lines <paramref name="from"/> to <paramref name="to"/> of a box's text and opens the menu on it.</summary>
    private async Task SelectBoxLinesAndOpenMenuAsync(int idx, int from, int to)
    {
        await Page.EvaluateAsync($$"""
            () => {
                var b = window.__scroll.box({{idx}}), text = b.textContent, lines = text.split('\n');
                var start = lines.slice(0, {{from}}).join('\n').length + ({{from}} > 0 ? 1 : 0);
                var end = lines.slice(0, {{to}} + 1).join('\n').length;
                var walker = document.createTreeWalker(b, NodeFilter.SHOW_TEXT), node, seen = 0, range = document.createRange();
                while ((node = walker.nextNode())) {
                    var len = node.textContent.length;
                    if (start >= seen && start <= seen + len) range.setStart(node, start - seen);
                    if (end >= seen && end <= seen + len) { range.setEnd(node, end - seen); break; }
                    seen += len;
                }
                var sel = window.getSelection(); sel.removeAllRanges(); sel.addRange(range);
                var r = b.getBoundingClientRect();
                b.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: r.left + 20, clientY: r.top + 20 }));
            }
            """);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });
    }

    [Fact]
    public async Task T11_Copy_Highlighted_Text_over_three_lines_of_the_box_keeps_their_line_breaks()
    {
        await OpenAsync("scroll-t11.html", Expanded());
        var idx = await BoxAsync("r150");
        var lines = await LinesAsync(idx);
        await SelectBoxLinesAndOpenMenuAsync(idx, 3, 5);
        await Page.Locator(".diagram-ctx-menu").GetByText("Copy Highlighted Text").ClickAsync();
        // mapped onto the note's text, which keeps a source's CRLF line ends
        var copied = (await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()")).Replace("\r\n", "\n");
        Assert.Equal(string.Join("\n", lines[3..6]).Trim(), copied);
    }

    private async Task ClickMenuItemAsync(string parent, string item)
    {
        var p = Page.Locator(".diagram-ctx-menu .submenu-parent", new() { HasTextString = parent });
        await p.HoverAsync();
        var i = p.Locator(".submenu").GetByText(item, new() { Exact = true });
        await i.WaitForAsync(new() { Timeout = 5000 });
        await i.ClickAsync();
    }

    private async Task OpenMenuOnNoteAsync(int idx)
    {
        await Page.EvaluateAsync($$"""
            () => {
                var g = window.__scroll.group({{idx}}), r = g.paths[0].getBoundingClientRect();
                g.paths[0].dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true,
                    clientX: r.left + r.width / 2, clientY: r.top + 6 }));
            }
            """);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });
    }

    [Fact]
    public async Task T12_A_copied_picture_is_plain_SVG_drawing_the_note_s_first_lines_and_its_cut()
    {
        await OpenAsync("scroll-t12.html", Expanded());
        var idx = await BoxAsync("r150");
        await OpenMenuOnNoteAsync(idx);
        await ClickMenuItemAsync("Copy image", "Copy as SVG");
        var svg = await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        Assert.DoesNotContain("foreignObject", svg);
        Assert.DoesNotContain("note-scroll", svg);
        Assert.Contains("r150_0039", svg);
        Assert.DoesNotContain("r150_0040", svg);
        var hidden = await Page.EvaluateAsync<int>("""
            (markup) => {
                var doc = new DOMParser().parseFromString(markup, 'image/svg+xml');
                return Array.from(doc.querySelectorAll('text')).filter(t => /r150_00/.test(t.textContent)
                    && /visibility:\s*hidden/.test(t.getAttribute('style') || '')).length;
            }
            """, svg);
        Assert.Equal(0, hidden);

        await OpenMenuOnNoteAsync(idx);
        await ClickMenuItemAsync("Copy image", "Copy as PNG");
        // the PNG is written once its picture has drawn on a canvas: read the clipboard until it holds one
        var decoded = await Page.EvaluateAsync<string>("""
            async () => {
                var last = 'nothing read';
                for (var i = 0; i < 75; i++) {
                    try {
                        var items = await navigator.clipboard.read();
                        var png = items.find(it => it.types.indexOf('image/png') >= 0);
                        if (png) {
                            var bmp = await createImageBitmap(await png.getType('image/png'));
                            return bmp.width + 'x' + bmp.height;
                        }
                        last = 'types: ' + items.map(it => it.types.join('+')).join(', ');
                    } catch (e) { last = String(e); }
                    await new Promise(r => setTimeout(r, 200));
                }
                return last;
            }
            """);
        Assert.Matches(@"^[1-9][0-9]*x[1-9][0-9]*$", decoded);
    }

    /// <summary>A control: passes before 5.4.0, where nothing is capped, and fails if the item read the drawn source.</summary>
    [Fact]
    public async Task T13_Copy_current_source_holds_the_whole_body_and_no_clear_row()
    {
        await OpenAsync("scroll-t13.html", Expanded());
        // the note itself, boxed or not: the item copies the diagram's source either way
        var idx = await NoteIndexAsync("r1500");
        await Page.EvaluateAsync($"() => window.__scroll.group({idx}).paths[0].scrollIntoView({{ block: 'center' }})");
        await TwoAnimationFramesAsync(Page);
        await OpenMenuOnNoteAsync(idx);
        var single = Page.Locator(".diagram-ctx-menu > div", new() { HasTextString = "Copy PlantUML source" });
        if (await Page.Locator(".diagram-ctx-menu .submenu-parent", new() { HasTextString = "Copy PlantUML source" }).CountAsync() > 0)
            await ClickMenuItemAsync("Copy PlantUML source", "Copy current PlantUML source");
        else
            await single.First.ClickAsync();
        var copied = await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        Assert.Contains("r1500_1498", copied);
        Assert.DoesNotContain("#FFFFFF00", copied);
    }

    [Fact]
    public async Task T14_The_YAML_view_and_a_Payload_length_over_the_cap_are_boxed_too()
    {
        await OpenAsync("scroll-t14.html", Expanded());
        var idx = await BoxAsync("r150");
        await CommandAsync("""
            () => { var s = document.querySelector('.toolbar-right .note-format-select'); s.value = 'yaml'; window._setNoteFormat(s); }
            """);
        idx = await BoxAsync("r150");
        var yaml = await LinesAsync(idx);
        Assert.Equal("r150_0001: value 1", yaml[0]);
        Assert.Equal(148, yaml.Length);

        await CommandAsync("""
            () => {
                var s = document.querySelector('.toolbar-right .truncate-lines-select'); s.value = '100'; window._setTruncateLines(s);
                window._setReportDetails('truncated');
            }
            """);
        idx = await BoxAsync("r150");
        var cut = await LinesAsync(idx);
        Assert.Equal("...", cut[^1]);
        // the line limit counts the header line and the blank line under it
        Assert.Equal(100 - 2 + 1, cut.Length);
    }

    [Fact]
    public async Task T15_A_report_that_starts_Expanded_caps_on_its_first_drawing()
    {
        await OpenAsync("scroll-t15.html", Expanded());
        var drawn = await Page.EvaluateAsync<string>("() => window.__scroll.container().getAttribute('data-plantuml')");
        Assert.Contains("<color:#FFFFFF00>", drawn);
        Assert.DoesNotContain("r1500_0100", drawn);
        var idx = await BoxAsync("r1500");
        Assert.Equal(1500, (await LinesAsync(idx)).Length);
    }

    /// <summary>A control: with the option at 0 the long payload is drawn in full and cut across diagrams, as before 5.4.0.</summary>
    [Fact]
    public async Task T16_PayloadScrollLines_0_draws_every_payload_in_full()
    {
        await OpenAsync("scroll-t16.html", Expanded(scrollLines: 0));
        var parts = await Page.EvaluateAsync<int>("() => window.__scroll.container().querySelectorAll(':scope > .puml-fragment').length");
        Assert.True(parts >= 3, $"the 1,500-line response is cut across diagrams ({parts} parts)");
        Assert.Contains("Continued", await Page.EvaluateAsync<string>("() => window.__scroll.container().textContent"));
        Assert.Equal(0, await Page.EvaluateAsync<int>("() => document.querySelectorAll('.note-scroll-frame').length"));
    }

    [Fact]
    public async Task T17_A_diagram_drawn_in_parts_by_its_calls_boxes_every_long_note_in_every_part_as_it_comes_near()
    {
        await OpenAsync("scroll-t17.html", Expanded(), manyCalls: true);
        await Page.EvaluateAsync("""
            () => {
                var sc = document.querySelectorAll('details.scenario')[1];
                sc.open = true;
                sc.scrollIntoView();
            }
            """);
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelectorAll('details.scenario')[1].querySelector('[data-diagram-type="plantuml"]');
                var parts = c ? c.querySelectorAll(':scope > .puml-fragment') : [];
                return parts.length > 1 && Array.from(parts).every(p => p.querySelector('svg .note-hover-rect'));
            }
            """, null, new() { Timeout = 120000, PollingInterval = 200 });
        var parts = await Page.EvaluateAsync<int>("() => document.querySelectorAll('details.scenario')[1].querySelectorAll('[data-diagram-type=\"plantuml\"] > .puml-fragment').length");
        Assert.True(parts > 1, $"premise: 300 calls are drawn in parts ({parts})");
        // every 60-line response note of each part: brought near, it gets its box
        var missing = await Page.EvaluateAsync<int>("""
            async () => {
                var c = document.querySelectorAll('details.scenario')[1].querySelector('[data-diagram-type="plantuml"]');
                var missing = 0;
                for (var p of Array.from(c.querySelectorAll(':scope > .puml-fragment'))) {
                    var groups = window._findNoteGroups(p.querySelector('svg')).filter(g => g.texts.some(t => /many_0001/.test(t.textContent)));
                    for (var g of [groups[0], groups[groups.length - 1]]) {
                        g.paths[0].scrollIntoView({ block: 'center' });
                        var ix = window._noteIndicesOf(g).original, t0 = Date.now();
                        while (!c.querySelector('.note-scroll-frame[data-note="' + ix + '"]') && Date.now() - t0 < 5000)
                            await new Promise(r => setTimeout(r, 100));
                        if (!c.querySelector('.note-scroll-frame[data-note="' + ix + '"]')) missing++;
                    }
                }
                return missing;
            }
            """);
        Assert.Equal(0, missing);
    }

    [Fact]
    public async Task T18_A_box_keeps_its_place_when_the_Headers_control_draws_it_again()
    {
        await OpenAsync("scroll-t18.html", Expanded());
        var idx = await BoxAsync("r1500");
        await Page.EvaluateAsync($"() => {{ var b = window.__scroll.box({idx}); b.scrollTop = 500; b.dispatchEvent(new Event('scroll')); }}");
        await TwoAnimationFramesAsync(Page);
        var before = await Page.EvaluateAsync<double>($"() => window.__scroll.box({idx}).scrollTop");
        await CommandAsync("() => window._toggleHeaders(document.querySelector('.toolbar-right .toggle-btn[data-toggle=\"headers\"]'))");
        idx = await BoxAsync("r1500");
        Assert.Equal(before, await Page.EvaluateAsync<double>($"() => window.__scroll.box({idx}).scrollTop"));
    }

    [Fact]
    public async Task T19_The_box_s_text_takes_the_drawn_ink_and_follows_a_theme_switch()
    {
        await OpenAsync("scroll-t19.html", Expanded());
        var idx = await BoxAsync("r150");
        const string inks = """
            (idx) => {
                var g = window.__scroll.group(idx);
                var body = g.texts.find(t => t.classList.contains('note-scroll-under') && /value/.test(t.textContent));
                return [getComputedStyle(window.__scroll.box(idx)).color, getComputedStyle(body).fill];
            }
            """;
        var light = await Page.EvaluateAsync<string[]>(inks, idx);
        Assert.Equal(light[1], light[0]);
        await Page.EvaluateAsync("() => document.documentElement.setAttribute('data-theme', 'dark')");
        await TwoAnimationFramesAsync(Page);
        var dark = await Page.EvaluateAsync<string[]>(inks, idx);
        Assert.Equal(dark[1], dark[0]);
        Assert.NotEqual(light[0], dark[0]);
    }

    [Fact]
    public async Task T20_A_filter_that_drops_notes_before_a_boxed_one_leaves_it_its_own_body()
    {
        await OpenAsync("scroll-t20.html", Expanded());
        await CommandAsync("() => window._toggleDatabases(document.querySelector('.toolbar-right .toggle-btn[data-toggle=\"databases\"]'))");
        Assert.Equal(0, await Page.EvaluateAsync<int>("() => window.__scroll.svgs().reduce((n, s) => n + (s.textContent.indexOf('count()') >= 0 ? 1 : 0), 0)"));
        var idx = await BoxAsync("r1500");
        var lines = await LinesAsync(idx);
        Assert.Contains("r1500_0001", lines[1]);
        Assert.Equal(1500, lines.Length);
    }

    private sealed record Drawing(double Ms, int Parts, int Records, int Filled);

    /// <summary>Draws the 40 calls with every payload expanded and reads when the diagram was drawn and decorated (time since
    /// the page's start), its parts, the notes the cap recorded and the boxes made.</summary>
    private async Task<Drawing> DrawFortyCallsAsync(string fileName, int scrollLines)
    {
        await Page.GotoAsync(LongPayloadReports.FortyCalls(TempDir, OutputDir, fileName, Expanded(scrollLines)));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg(60000);
        await Page.EvaluateAsync(Helpers);
        await Page.WaitForFunctionAsync("() => window.__scroll.settled() && (window.__settledAt = window.__settledAt || performance.now())",
            null, new() { Timeout = 120000, PollingInterval = 200 });
        var json = await Page.EvaluateAsync<string>("""
            () => {
                var c = window.__scroll.container(), d = c._noteScrollDrawn;
                return JSON.stringify({ Ms: window.__settledAt, Parts: c.querySelectorAll(':scope > .puml-fragment').length,
                    Records: d && d.records ? Object.keys(d.records).length : 0,
                    Filled: c.querySelectorAll('.note-scroll-frame').length });
            }
            """);
        return JsonSerializer.Deserialize<Drawing>(json)!;
    }

    /// <summary>The plan's M1b: 17 parts and 2.5 s drawn in full, 3 parts and 1.2 s capped. The two drawings are compared
    /// with each other on the same machine, after a first one that loads the engine, so its load does not decide the fact.</summary>
    [Fact]
    public async Task T21_Forty_calls_every_payload_expanded_draw_in_three_parts_faster_than_in_full_filling_only_boxes_near_the_window()
    {
        // the first drawing in a page loads the engine: one before the two that are compared
        await DrawFortyCallsAsync("scroll-t21-warm.html", 40);
        var full = await DrawFortyCallsAsync("scroll-t21-full.html", 0);
        var capped = await DrawFortyCallsAsync("scroll-t21-capped.html", 40);
        Assert.True(capped.Parts <= 3, $"capped: {capped.Parts} parts");
        Assert.True(full.Parts > 3, $"premise: drawn in full it is cut into more parts ({full.Parts})");
        Assert.Equal(16, capped.Records);
        Assert.True(capped.Filled < capped.Records, $"{capped.Filled} of {capped.Records} boxes made before the reader scrolled");
        Assert.True(capped.Ms < full.Ms, $"drawn in {capped.Ms:F0} ms capped and {full.Ms:F0} ms in full");
    }

    [Fact]
    public async Task T22_A_state_drawn_the_same_as_the_last_is_not_drawn_again_and_its_box_holds_the_new_state()
    {
        await Page.GotoAsync(LongPayloadReports.Write(TempDir, OutputDir, "scroll-t22.html",
            [("scroll-one", "One long response", LongPayloadReports.Call("scroll-one", 0, LongPayloadReports.JsonOfLines(150, "r150", wideAt: 5)))],
            ReportTestHelper.ClassicStart with { TruncateLines = TruncateLineCount.Lines100, PayloadScrollLines = 40 }));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg(60000);
        await Page.EvaluateAsync(Helpers);
        await SettledAsync();
        var idx = await BoxAsync("r150");
        Assert.Equal("...", (await LinesAsync(idx))[^1]);
        await Page.EvaluateAsync("() => { window.__drawn = window.__scroll.svgs()[0]; window.__engineCalls = 0; var r = window.plantuml.render; window.plantuml.render = function() { window.__engineCalls++; return r.apply(this, arguments); }; }");

        await CommandAsync("() => window._setReportDetails('expanded')");
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__scroll.svgs()[0] === window.__drawn"), "the picture was drawn again");
        Assert.Equal(0, await Page.EvaluateAsync<int>("() => window.__engineCalls"));
        idx = await BoxAsync("r150");
        var lines = await LinesAsync(idx);
        Assert.Equal(150, lines.Length);
        Assert.Equal("}", lines[^1]);
    }

    /// <summary>
    /// A note's redraw the engine throws on keeps the picture on the page, with its box and the box's text, the box where
    /// it was, and the diagram's source the picture's: the failed source stayed on the diagram, so the picture was
    /// decorated again against it, its box placed from that source's header lines, and the menu's current source
    /// items read the failed source with its clear rows.
    /// </summary>
    [Fact]
    public async Task T23_A_note_redraw_the_engine_throws_on_keeps_the_old_picture_and_its_box()
    {
        await OpenAsync("scroll-t23.html", Expanded());
        var idx = await BoxAsync("r1500");
        const string place = """
            (idx) => {
                var f = window.__scroll.frame(idx);
                return [f.getAttribute('y'), f.getAttribute('height'), window.__scroll.container().getAttribute('data-plantuml')];
            }
            """;
        var before = await Page.EvaluateAsync<string[]>(place, idx);
        await Page.EvaluateAsync("""
            () => {
                window.__drawn = window.__scroll.svgs()[0];
                var real = window.plantuml.render;
                window.plantuml.render = function() { window.plantuml.render = real; throw new Error('a stubbed engine failure'); };
            }
            """);
        await Page.EvaluateAsync($$"""
            () => {
                var g = window.__scroll.group({{idx}}), b = g.paths[0].getBBox();
                var minus = Array.from(g.paths[0].ownerSVGElement.querySelectorAll('.note-toggle-icon[data-note-btn="minus"]')).find(e => {
                    var r = e.getBBox(); return r.x >= b.x - 1 && r.x + r.width <= b.x + b.width + 1 && r.y >= b.y - 1 && r.y <= b.y + 40;
                });
                minus.querySelector('rect').dispatchEvent(new MouseEvent('click', { bubbles: true }));
            }
            """);
        await SettledAsync();
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__scroll.svgs()[0] === window.__drawn"), "premise: the old picture stays");
        idx = await BoxAsync("r1500");
        Assert.Equal(1500, (await LinesAsync(idx)).Length);
        var after = await Page.EvaluateAsync<string[]>(place, idx);
        Assert.Equal(before[0], after[0]);
        Assert.Equal(before[1], after[1]);
        Assert.True(before[2] == after[2], "the diagram kept the source of the redraw that failed");
        // the same picture decorated again: its old box goes before the new one is made
        Assert.Equal(1, await Page.EvaluateAsync<int>($"() => window.__scroll.container().querySelectorAll('.note-scroll-frame[data-note=\"{idx}\"]').length"));
    }

    [Fact]
    public async Task T24_A_boxed_note_s_buttons_stay_in_its_top_row_and_a_hidden_one_takes_no_click()
    {
        await OpenAsync("scroll-t24.html", Expanded());
        var idx = await BoxAsync("r1500");
        // the note's top above the window, its box on the screen
        await Page.EvaluateAsync($"() => {{ var r = window.__scroll.frame({idx}).getBoundingClientRect(); window.scrollBy(0, r.top + 120); }}");
        await TwoAnimationFramesAsync(Page);
        await Page.EvaluateAsync($"() => window.__scroll.frame({idx}).dispatchEvent(new MouseEvent('mouseenter'))");
        var moved = await Page.EvaluateAsync<string[]>($$"""
            () => {
                var g = window.__scroll.group({{idx}}), b = g.paths[0].getBBox();
                return Array.from(g.paths[0].ownerSVGElement.querySelectorAll('.note-toggle-icon')).filter(e => {
                    var r = e.getBBox(); return r.x >= b.x - 1 && r.x + r.width <= b.x + b.width + 1 && r.y < b.y + 30;
                }).map(e => e.getAttribute('transform') || '');
            }
            """);
        Assert.NotEmpty(moved);
        Assert.All(moved, t => Assert.Matches(@"^(|translate\([-0-9.]+,0\))$", t));

        await Page.EvaluateAsync($"() => window.__scroll.frame({idx}).dispatchEvent(new MouseEvent('mouseleave'))");
        await Page.WaitForTimeoutAsync(400);
        var taken = await Page.EvaluateAsync<string>($$"""
            () => {
                var g = window.__scroll.group({{idx}}), b = g.paths[0].getBoundingClientRect();
                var bottom = Array.from(g.paths[0].ownerSVGElement.querySelectorAll('.note-toggle-icon')).map(e => e.getBoundingClientRect())
                    .filter(r => r.top > b.top + 30 && r.bottom <= b.bottom + 1 && r.left >= b.left && r.right <= b.right);
                if (!bottom.length) return 'no bottom button';
                var r = bottom[0], hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                return hit && hit.closest('.note-toggle-icon') ? 'button' : 'other';
            }
            """);
        Assert.Equal("other", taken);
    }

    [Fact]
    public async Task T25_FocusFields_emphasis_shows_as_bold_colour_in_the_box_never_as_markup()
    {
        await OpenAsync("scroll-t25.html", Expanded());
        var idx = await BoxAsync("focus");
        // FocusFields' default look: the focused field bold, every other line light grey (FocusEmphasis.Bold,
        // FocusDeEmphasis.LightGray)
        var look = await Page.EvaluateAsync<JsonElement>($$"""
            () => {
                var b = window.__scroll.box({{idx}});
                var spans = Array.from(b.querySelectorAll('span'));
                var focused = spans.find(s => s.textContent.indexOf('focus_0003') >= 0);
                var other = spans.find(s => s.textContent.indexOf('focus_0004') >= 0);
                return { text: b.textContent, weight: focused ? getComputedStyle(focused).fontWeight : '0',
                    color: other ? other.style.color : '' };
            }
            """);
        Assert.DoesNotContain("<b>", look.GetProperty("text").GetString());
        Assert.DoesNotContain("<color", look.GetProperty("text").GetString());
        Assert.DoesNotContain("</", look.GetProperty("text").GetString());
        Assert.True(int.Parse(look.GetProperty("weight").GetString()!) >= 600, $"font-weight {look.GetProperty("weight").GetString()}");
        Assert.Equal("lightgray", look.GetProperty("color").GetString());
        // the note is as wide as its widest line painted (40 wide letters), not its longest in characters (70 narrow ones)
        var wraps = await Page.EvaluateAsync<bool>($$"""
            () => {
                var b = window.__scroll.box({{idx}});
                var lh = parseFloat(b.style.lineHeight);
                return b.scrollHeight > b.textContent.split('\n').length * lh + 2 || b.scrollWidth > b.clientWidth + 1;
            }
            """);
        Assert.False(wraps, "a line of the box wraps: the note is narrower than its widest line");
    }

    /// <summary>A control: passes before 5.4.0, and fails if the item read the drawn rows under the box.</summary>
    [Fact]
    public async Task T26_Truncated_at_100_lines_Copy_current_box_text_copies_the_100_lines_and_the_cut()
    {
        await OpenAsync("scroll-t26.html", Expanded());
        await CommandAsync("""
            () => {
                var s = document.querySelector('.toolbar-right .truncate-lines-select'); s.value = '100'; window._setTruncateLines(s);
                window._setReportDetails('truncated');
            }
            """);
        var idx = await NoteIndexAsync("r150");
        await Page.EvaluateAsync($"() => window.__scroll.group({idx}).paths[0].scrollIntoView({{ block: 'center' }})");
        await TwoAnimationFramesAsync(Page);
        await OpenMenuOnNoteAsync(idx);
        await ClickMenuItemAsync("Copy box text", "Copy current box text");
        var copied = (await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()")).Replace("\r\n", "\n").Split('\n');
        Assert.Equal("[Content-Type=application/json]", copied[0]);
        Assert.Equal("...", copied[^1]);
        Assert.Equal(100 + 1, copied.Length);
        Assert.DoesNotContain(copied, l => l.Contains("FFFFFF00"));
    }

    [Fact]
    public async Task T27_After_Wide_and_its_correction_pass_each_boxed_note_has_one_box()
    {
        await OpenAsync("scroll-t27.html", Expanded());
        await CommandAsync("""
            () => { var s = document.querySelector('.toolbar-right .note-width-select'); s.value = 'full'; window._setNoteWidth(s); }
            """);
        await Page.WaitForTimeoutAsync(1500);
        await SettledAsync();
        foreach (var key in new[] { "r41", "r150", "r1500", "m60", "focus" })
        {
            var idx = await BoxAsync(key);
            Assert.Equal(1, await Page.EvaluateAsync<int>($"() => window.__scroll.container().querySelectorAll('.note-scroll-frame[data-note=\"{idx}\"]').length"));
        }
    }

    /// <summary>What "Copy box text" copies from a boxed note: its headers, then the box's text.</summary>
    private async Task<string> CopyBoxTextAsync(int idx)
    {
        await Page.EvaluateAsync($$"""
            () => {
                var f = window.__scroll.frame({{idx}}), r = f.getBoundingClientRect();
                f.querySelector('.note-scroll').dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true,
                    clientX: r.left + r.width / 2, clientY: r.top + r.height / 2 }));
            }
            """);
        var item = Page.Locator(".diagram-ctx-menu").GetByText("Copy box text", new() { Exact = true });
        await item.WaitForAsync(new() { Timeout = 5000 });
        await item.ClickAsync();
        return (await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()")).Replace("\r\n", "\n");
    }

    /// <summary>
    /// Selects from the start of the note's first row to <paramref name="end"/>: "rows", the end of the last row the
    /// engine drew (the clear row), or "box:N", the end of the box's line N; then copies it with Copy Highlighted Text.
    /// </summary>
    private async Task<string> CopyHighlightedAsync(int idx, string end)
    {
        await Page.EvaluateAsync($$"""
            () => {
                var g = window.__scroll.group({{idx}}), end = '{{end}}';
                var texts = g.texts.slice().sort((a, b) => a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1);
                var range = document.createRange();
                range.setStart(texts[0].firstChild, 0);
                if (end === 'rows') {
                    var last = texts[texts.length - 1].lastChild;
                    range.setEnd(last, last.textContent.length);
                } else {
                    var b = window.__scroll.box({{idx}}), lines = b.textContent.split('\n'), n = parseInt(end.slice(4), 10);
                    var at = lines.slice(0, n + 1).join('\n').length, walker = document.createTreeWalker(b, NodeFilter.SHOW_TEXT), node, seen = 0;
                    while ((node = walker.nextNode())) {
                        if (at <= seen + node.textContent.length) { range.setEnd(node, at - seen); break; }
                        seen += node.textContent.length;
                    }
                }
                var sel = window.getSelection(); sel.removeAllRanges(); sel.addRange(range);
                var r = g.paths[0].getBoundingClientRect();
                g.paths[0].dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true,
                    clientX: r.left + r.width / 2, clientY: r.top + 6 }));
            }
            """);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });
        await Page.Locator(".diagram-ctx-menu").GetByText("Copy Highlighted Text").ClickAsync();
        return (await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()")).Replace("\r\n", "\n");
    }

    /// <summary>
    /// A selection over every row the engine drew for a boxed note copies the note: the rows under the box are the
    /// first 40 lines of what the box holds, and the clear row under them is only there to keep the note as wide as
    /// its widest line. Copied as painted, the selection gave 40 lines, the cut and the widest line again, ten no-break
    /// spaces on its end.
    /// </summary>
    [Fact]
    public async Task T28_Copy_Highlighted_Text_over_a_boxed_note_s_drawn_rows_copies_the_note_and_not_the_rows_under_its_box()
    {
        await OpenAsync("scroll-t28.html", Expanded());
        var idx = await BoxAsync("r150");
        var note = await CopyBoxTextAsync(idx);
        Assert.Contains("\"r150_0148\": \"value 148\"", note);

        var copied = await CopyHighlightedAsync(idx, "rows");

        Assert.DoesNotContain(' ', copied);
        Assert.Equal(note.Trim(), copied);
    }

    /// <summary>A drag from a boxed note's headers into its box copies the headers and the box's lines up to where it
    /// ends, each on its own line.</summary>
    [Fact]
    public async Task T29_A_selection_from_a_boxed_note_s_headers_into_its_box_copies_the_headers_and_the_box_s_lines_it_covers()
    {
        // a header of several words, which the engine draws a word to a <text>: the browser's own reading of such a
        // selection (Chromium's) puts each word on a line of its own, so the page reads the headers and the box itself
        var call = LongPayloadReports.Call("scroll-headers", 0, LongPayloadReports.JsonOfLines(150, "hd"),
            responseHeaders: [("Content-Type", "application/json"), ("X-Request-Note", "three words here")]);
        await OpenOneCallAsync("scroll-t29.html", "scroll-headers", call);
        var idx = await BoxAsync("hd");
        var lines = await LinesAsync(idx);
        var note = await CopyBoxTextAsync(idx);
        var headers = note[..^(string.Join("\n", lines).Length + 1)];
        Assert.Contains("three words here", headers);

        var copied = await CopyHighlightedAsync(idx, "box:5");

        Assert.Equal(headers + "\n" + string.Join("\n", lines[..6]), copied);
    }

    /// <summary>
    /// A Payload length at the cap draws as it did: with headers hidden, Truncated at 40 draws a long payload's first 40
    /// lines and its cut, which the cap counted as 41 lines of payload, and boxed. The cut is not a line of the payload.
    /// </summary>
    [Fact]
    public async Task T30_Truncated_at_the_cap_with_headers_hidden_draws_its_40_lines_and_its_cut_with_no_box()
    {
        await OpenAsync("scroll-t30.html", ReportTestHelper.ClassicStart with
        {
            HeadersShown = false, Details = ReportDetailsState.Truncated, TruncateLines = TruncateLineCount.Lines40, PayloadScrollLines = 40
        });
        var idx = await NoteIndexAsync("r150");
        Assert.True(idx >= 0, "premise: a note holds r150");
        await Page.EvaluateAsync($"() => window.__scroll.group({idx}).paths[0].scrollIntoView({{ block: 'center' }})");
        await TwoAnimationFramesAsync(Page);

        Assert.Null(await Page.EvaluateAsync<string[]?>($"() => window._noteScrollLines(window.__scroll.container(), {idx})"));
        Assert.Equal(0, await Page.EvaluateAsync<int>($"() => window.__scroll.container().querySelectorAll('.note-scroll-frame[data-note=\"{idx}\"]').length"));
        var painted = await Page.EvaluateAsync<string[]>($$"""
            () => {
                var rows = {};
                window.__scroll.group({{idx}}).texts.forEach(t => { var y = t.getAttribute('y'); rows[y] = (rows[y] || '') + t.textContent; });
                return Object.keys(rows).sort((a, b) => a - b).map(y => rows[y].trim());
            }
            """);
        Assert.Equal(41, painted.Length);
        Assert.Equal("...", painted[^1]);
    }

    /// <summary>
    /// A capped payload stays one note when its lines are long: 40 lines of about 700 characters are 28,000, past the
    /// 15,000 at which the page cuts a note across diagrams, so the cap draws fewer of them, and the note keeps one box
    /// that holds every line. Cut, each part had a box of its own over the whole payload.
    /// </summary>
    [Fact]
    public async Task T31_A_payload_of_long_lines_is_drawn_short_enough_to_stay_one_note_with_one_box()
    {
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        var words = string.Join(" ", Enumerable.Repeat("word", 140));
        var body = System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(1, 58).ToDictionary(i => $"w60_{i:D4}", _ => words));
        await Page.GotoAsync(LongPayloadReports.Write(TempDir, OutputDir, "scroll-t31.html",
            [("scroll-long-lines", "A response of long lines", LongPayloadReports.Call("scroll-long-lines", 0, body))], Expanded()));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg(60000);
        await Page.EvaluateAsync(Helpers);
        await SettledAsync();

        var idx = await BoxAsync("w60");
        await Page.WaitForTimeoutAsync(500);
        Assert.False(await Page.EvaluateAsync<bool>("() => /Continued (On Next|From Previous) Diagram/.test(window.__scroll.container().textContent)"),
            "the capped note was cut across diagrams");
        Assert.Equal(1, await Page.EvaluateAsync<int>($"() => window.__scroll.container().querySelectorAll('.note-scroll-frame[data-note=\"{idx}\"]').length"));
        Assert.Equal(60, (await LinesAsync(idx)).Length);
        // the composed source, before the page cuts anything across diagrams
        var drawn = await Page.EvaluateAsync<int>("""
            () => window._parseNoteBlocks(window.__scroll.container().getAttribute('data-plantuml'))
                .find(b => b.contentLines.some(l => l.indexOf('"w60_0001"') >= 0)).contentLines.join('\n').length
            """);
        Assert.InRange(drawn, 1, 15000);
    }

    private async Task OpenOneCallAsync(string fileName, string id, RequestResponseLog[] call, Func<string, string>? edit = null,
        ResolvedToggleDefaults? toggles = null)
    {
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await Page.GotoAsync(LongPayloadReports.WriteEdited(TempDir, OutputDir, fileName, [(id, "One long response", call)],
            edit ?? (source => source), toggles ?? Expanded()));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg(60000);
        await Page.EvaluateAsync(Helpers);
        await SettledAsync();
    }

    /// <summary>
    /// The clear row stays one row: it keeps a capped note as wide as its widest line, and a widest line longer than the
    /// note's wrap width (a 600-character value with spaces) wrapped in the engine as the line itself does, painting a
    /// band of clear rows under the box with the note's lower buttons at its foot.
    /// </summary>
    [Fact]
    public async Task T32_A_widest_line_longer_than_the_wrap_width_leaves_one_clear_row_and_no_band_under_the_box()
    {
        var words = string.Join(" ", Enumerable.Repeat("word", 120));
        var body = System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(1, 58).ToDictionary(i => $"wr_{i:D4}", i => i == 30 ? words : $"value {i}"));
        await OpenOneCallAsync("scroll-t32.html", "scroll-wrap", LongPayloadReports.Call("scroll-wrap", 0, body));
        var idx = await BoxAsync("wr");

        // the engine paints no text in a clear ink: a clear row it wraps shows only as the height it adds under the cut
        var band = await Page.EvaluateAsync<double>($$"""
            () => {
                var g = window.__scroll.group({{idx}});
                var cut = g.texts.filter(t => t.textContent.trim() === '...').pop().getBoundingClientRect();
                var note = g.paths.reduce((a, p) => { var b = p.getBoundingClientRect(); return b.height * b.width > a.height * a.width ? b : a; }, { width: 0, height: 0 });
                return note.bottom - cut.bottom;
            }
            """);
        // under the cut: one clear row and the note's padding
        Assert.InRange(band, 0, 40);
        Assert.Contains("  \"wr_0030\": \"" + words + "\",", await LinesAsync(idx));
    }

    /// <summary>
    /// A FocusFields payload whose long token the generator broke into pieces, each ending in the join marker, reads as
    /// one line in the box and copies without Kronikol's markup, in either form the generator writes it: where line ends
    /// are LF each piece carries its own tags, so its marker sits inside <c>&lt;/color&gt;</c> and the pieces never
    /// joined; where they are CRLF one pair spans the pieces, and the copies kept both tags.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task T33_A_FocusFields_token_broken_into_pieces_is_one_line_in_the_box_and_copies_without_markup(bool crlf)
    {
        var token = string.Concat(Enumerable.Range(0, 400).Select(i => (char)('A' + i % 26)));
        var body = System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(1, 58).ToDictionary(i => $"tk_{i:D4}", i => i == 3 ? token : $"value {i}"));
        var call = LongPayloadReports.Call("scroll-token", 0, body);
        call[1].FocusFields = ["tk_0001"];
        await OpenOneCallAsync($"scroll-t33-{(crlf ? "crlf" : "lf")}.html", "scroll-token", call, source => TokenPieces(source, crlf));
        var idx = await BoxAsync("tk");

        Assert.Contains("  \"tk_0003\": \"" + token + "\",", await LinesAsync(idx));
        var note = await CopyBoxTextAsync(idx);
        Assert.DoesNotContain("<color", note);
        Assert.DoesNotContain("</color>", note);
        Assert.Contains("  \"tk_0003\": \"" + token + "\",", note.Split('\n'));
    }

    /// <summary>
    /// A box the page could not make when its note first came near (nothing measured yet, as for a scenario closed in
    /// the same moment) is made when the note comes near again: the first attempt ended the note's watch for good.
    /// </summary>
    [Fact]
    public async Task T34_A_box_that_could_not_be_made_when_its_note_first_came_near_is_made_when_it_comes_near_again()
    {
        await OpenAsync("scroll-t34.html", Expanded());
        var idx = await NoteIndexAsync("r60x");
        Assert.Equal(0, await Page.EvaluateAsync<int>($"() => window.__scroll.container().querySelectorAll('.note-scroll-frame[data-note=\"{idx}\"]').length"));
        await Page.EvaluateAsync("""
            () => {
                window.__realBBox = SVGGraphicsElement.prototype.getBBox;
                SVGGraphicsElement.prototype.getBBox = function() { return { x: 0, y: 0, width: 0, height: 0 }; };
            }
            """);
        await Page.EvaluateAsync($"() => window.__scroll.group({idx}).paths[0].scrollIntoView({{ block: 'center' }})");
        await Page.WaitForTimeoutAsync(600);
        await Page.EvaluateAsync("() => { SVGGraphicsElement.prototype.getBBox = window.__realBBox; }");
        Assert.Equal(0, await Page.EvaluateAsync<int>($"() => window.__scroll.container().querySelectorAll('.note-scroll-frame[data-note=\"{idx}\"]').length"));

        await Page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await Page.WaitForTimeoutAsync(400);
        Assert.True(await Page.EvaluateAsync<bool>($"() => window.__scroll.group({idx}).paths[0].getBoundingClientRect().top > 2 * window.innerHeight"),
            "premise: at the top of the page the note is more than a window height away");
        await Page.EvaluateAsync($"() => window.__scroll.group({idx}).paths[0].scrollIntoView({{ block: 'center' }})");
        await Page.WaitForFunctionAsync($"() => !!window.__scroll.box({idx})", null, new() { Timeout = 15000, PollingInterval = 200 });
    }

    /// <summary>
    /// A first drawing installs the records composed for it: it read the slot when the drawing ended, so a compose in
    /// between (the queue's reset after 15 s composes the same diagram again) gave the picture another compose's records.
    /// </summary>
    [Fact]
    public async Task T35_A_first_drawing_installs_the_records_composed_for_it_and_not_a_later_compose_s()
    {
        await Page.AddInitScriptAsync("""
            document.addEventListener('DOMContentLoaded', () => {
                var install = window._installNoteScroll;
                window._installNoteScroll = function(owner) {
                    owner._noteScrollPending = { source: 'another compose', uncapped: 'another compose', records: {} };
                    return install.apply(this, arguments);
                };
            });
            """);
        await OpenAsync("scroll-t35.html", Expanded());
        var idx = await BoxAsync("r150");
        Assert.Equal(150, (await LinesAsync(idx)).Length);
    }

    /// <summary>
    /// A boxed note whose widest line is wider than the note wraps can be widened before its box is made: Wide was offered
    /// only when the box's own text wrapped, so a hover before the box was made hid the button for as long as the note
    /// was drawn.
    /// </summary>
    [Fact]
    public async Task T37_A_boxed_note_whose_lines_wrap_offers_Wide_before_its_box_is_made()
    {
        var words = string.Join(" ", Enumerable.Repeat("word", 120));
        var calls = Enumerable.Range(0, 8)
            .SelectMany(i => LongPayloadReports.Call("scroll-far", i, LongPayloadReports.JsonOfLines(60, $"near{i}")))
            .Concat(LongPayloadReports.Call("scroll-far", 8, System.Text.Json.JsonSerializer.Serialize(
                Enumerable.Range(1, 58).ToDictionary(i => $"far_{i:D4}", i => i == 30 ? words : $"value {i}"))))
            .ToArray();
        await OpenOneCallAsync("scroll-t37.html", "scroll-far", calls);
        var idx = await NoteIndexAsync("far");
        Assert.True(idx >= 0, "premise: a note holds far");
        Assert.Equal(0, await Page.EvaluateAsync<int>($"() => window.__scroll.container().querySelectorAll('.note-scroll-frame[data-note=\"{idx}\"]').length"));

        var shown = await Page.EvaluateAsync<string[]>($$"""
            () => {
                var g = window.__scroll.group({{idx}}), svg = g.paths[0].ownerSVGElement;
                var rect = Array.from(svg.querySelectorAll('.note-hover-rect')).find(r => r.nextElementSibling === g.paths[0]);
                rect.dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }));
                return Array.from(svg.querySelectorAll('[data-note-btn="width"]'))
                    .filter(b => b.style.opacity === '1').map(b => b.style.display || 'shown');
            }
            """);
        Assert.Equal(["shown"], shown);
    }

    /// <summary>
    /// A message's note is drawn at 11 px (the generator's <c>.eventNote</c> style) and its lines are measured at that size.
    /// Measured at the 13 px of a call's note, a line under the wrap width was taken to wrap: the clear row was cut short,
    /// the note was drawn narrower than that line, the line wrapped in the box, and Wide was offered for it.
    /// </summary>
    [Fact]
    public async Task T38_A_message_note_is_measured_at_its_own_size_and_a_line_that_fits_stays_one_row_in_its_box()
    {
        // the 50th line about 120 characters: under the wrap width at 11 px, over it at 13 px; the 40 drawn lines short
        var wide = string.Join(" ", Enumerable.Repeat("word", 21));
        var body = JsonSerializer.Serialize(Enumerable.Range(1, 58).ToDictionary(i => $"ev_{i:D4}", i => i == 50 ? wide : $"value {i}"));
        await OpenOneCallAsync("scroll-t38.html", "scroll-event", LongPayloadReports.Message("scroll-event", 0, body));
        var idx = await BoxAsync("ev");

        var box = await Page.EvaluateAsync<JsonElement>($$"""
            () => {
                var b = window.__scroll.box({{idx}}), lineHeight = parseFloat(b.style.lineHeight);
                return { size: b.style.fontSize, wraps: b.scrollHeight > b.textContent.split('\n').length * lineHeight + 2 };
            }
            """);
        Assert.Equal("11px", box.GetProperty("size").GetString());
        Assert.False(box.GetProperty("wraps").GetBoolean(), "a line that fits the note wrapped in its box");
        var shown = await Page.EvaluateAsync<string[]>($$"""
            () => {
                var g = window.__scroll.group({{idx}}), svg = g.paths[0].ownerSVGElement;
                var rect = Array.from(svg.querySelectorAll('.note-hover-rect')).find(r => r.nextElementSibling === g.paths[0]);
                rect.dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }));
                return Array.from(svg.querySelectorAll('[data-note-btn="width"]'))
                    .filter(b => b.style.opacity === '1').map(b => b.style.display || 'shown');
            }
            """);
        Assert.DoesNotContain("shown", shown);
    }

    /// <summary>
    /// Kronikol's own markup in a payload's lines reads in the box as the note draws it: a form body's grey <c>&amp;</c>
    /// after each field (<c>&lt;font color="lightgray"&gt;&amp;</c>) and the grey "[Full path]" line a long URL's request
    /// note ends with, which the box showed as markup; "Copy box text" copied the divider's markup too, before 5.4.0. The
    /// divider at the end of the widest line also painted a grey <c>&amp;</c> in the clear row, under the cut, and the box
    /// stretched over it.
    /// </summary>
    [Fact]
    public async Task T39_A_form_body_and_a_long_URL_read_in_the_box_and_copy_without_Kronikol_s_markup()
    {
        var fields = string.Join("&", Enumerable.Range(1, 60).Select(i => $"fm_{i:D4}=" + (i == 50 ? new string('w', 40) : $"value{i}")));
        // past the 2,000 characters of an arrow's label
        var uri = new Uri("http://orders.internal/api/forms" + string.Concat(Enumerable.Range(1, 400).Select(i => $"/s{i:D3}x")) + "?page=1");
        await OpenOneCallAsync("scroll-t39.html", "scroll-form", LongPayloadReports.FormPost("scroll-form", fields, uri));
        var idx = await Page.EvaluateAsync<int>("""
            () => {
                var c = window.__scroll.container();
                return window._parseNoteBlocks(c._noteOriginalSource || c.getAttribute('data-plantuml'))
                    .findIndex(b => b.contentLines.some(l => l.indexOf('fm_0001=') >= 0));
            }
            """);
        Assert.True(idx >= 0, "premise: a note holds the form");
        await Page.EvaluateAsync($"() => window.__scroll.group({idx}).paths[0].scrollIntoView({{ block: 'center' }})");
        await Page.WaitForFunctionAsync($"() => !!window.__scroll.box({idx})", null, new() { Timeout = 15000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);

        var lines = await LinesAsync(idx);
        Assert.Equal("fm_0001=value1&", lines[0]);
        Assert.Equal("fm_0060=value60", lines[59]);
        Assert.DoesNotContain(lines, l => l.Contains("<font") || l.Contains("<color"));
        var full = Array.IndexOf(lines, "[Full path]");
        Assert.True(full > 59, "the box holds the [Full path] line as the note draws it");
        Assert.Equal(uri.PathAndQuery, lines[full + 1]);

        var copied = await CopyBoxTextAsync(idx);
        Assert.DoesNotContain("<font", copied);
        Assert.EndsWith("\n" + string.Join("\n", lines), copied);

        // the clear row paints nothing, so the rows under the box end at the cut
        var ys = await Page.EvaluateAsync<double[]>($$"""
            () => {
                var g = window.__scroll.group({{idx}});
                var cut = g.texts.filter(t => t.textContent.trim() === '...').pop();
                var under = g.texts.filter(t => t.classList.contains('note-scroll-under')).map(t => parseFloat(t.getAttribute('y')));
                return [parseFloat(cut.getAttribute('y')), Math.max.apply(null, under)];
            }
            """);
        Assert.Equal(ys[0], ys[1]);
    }

    /// <summary>
    /// A first drawing records the source before the cap it was composed with, beside the records it installs: it read
    /// the slot when the drawing ended, so a compose in between (the queue's reset after 15 s composes the same diagram
    /// again) gave the picture another compose's source, and a later pass with that compose's state was passed over with
    /// the first drawing's box. Read in the page, as the race takes 15 s to come about.
    /// </summary>
    [Fact]
    public async Task T40_A_first_drawing_records_the_source_before_the_cap_it_was_composed_with()
    {
        await Page.AddInitScriptAsync("""
            document.addEventListener('DOMContentLoaded', () => {
                var record = window._recordDrawnSource;
                window._recordDrawnSource = function(el, source) {
                    el._noteScrollPending = { source: source, uncapped: 'another compose', records: {} };
                    return record.apply(this, arguments);
                };
            });
            """);
        await OpenAsync("scroll-t40.html", Expanded());
        await BoxAsync("r150");
        var recorded = await Page.EvaluateAsync<string[]>("""
            () => {
                var c = window.__scroll.container();
                return [c._drawnUncapped, window._noteUncappedSource(c, c.getAttribute('data-plantuml'))];
            }
            """);
        Assert.NotNull(recorded[1]);
        Assert.True(recorded[0] == recorded[1], "the first drawing recorded another compose's source");
    }

    /// <summary>
    /// A widest line whose first word is wider than the note wraps at leaves one clear row: the clear row is cut at a
    /// space to what fits, and with no space before the wrap width it was left whole, wrapped as the line does, and
    /// painted a band of clear rows under the box. The engine never breaks a word, so the first word alone is one row.
    /// </summary>
    [Fact]
    public async Task T41_A_widest_line_whose_first_word_is_wider_than_the_wrap_width_leaves_one_clear_row()
    {
        var tail = string.Join(" ", Enumerable.Repeat("word", 120));
        var items = Enumerable.Range(1, 60).Select(i => i == 30 ? new string('M', 110) + " " + tail : $"aw_{i:D4}").ToArray();
        await OpenOneCallAsync("scroll-t41.html", "scroll-word", LongPayloadReports.Call("scroll-word", 0, JsonSerializer.Serialize(items)));
        var idx = await Page.EvaluateAsync<int>("""
            () => {
                var c = window.__scroll.container();
                return window._parseNoteBlocks(c._noteOriginalSource || c.getAttribute('data-plantuml'))
                    .findIndex(b => b.contentLines.some(l => l.indexOf('"aw_0001"') >= 0));
            }
            """);
        Assert.True(idx >= 0, "premise: a note holds aw_0001");
        await Page.EvaluateAsync($"() => window.__scroll.group({idx}).paths[0].scrollIntoView({{ block: 'center' }})");
        await Page.WaitForFunctionAsync($"() => !!window.__scroll.box({idx})", null, new() { Timeout = 15000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);

        var band = await Page.EvaluateAsync<double>($$"""
            () => {
                var g = window.__scroll.group({{idx}});
                var cut = g.texts.filter(t => t.textContent.trim() === '...').pop().getBoundingClientRect();
                var note = g.paths.reduce((a, p) => { var b = p.getBoundingClientRect(); return b.height * b.width > a.height * a.width ? b : a; }, { width: 0, height: 0 });
                return note.bottom - cut.bottom;
            }
            """);
        // under the cut: one clear row and the note's padding
        Assert.InRange(band, 0, 40);
    }

    /// <summary>
    /// A first body line that alone would carry the note past the 15,000 characters at which the page cuts a note across
    /// diagrams is drawn cut at a space, and the note stays one note with one box holding every line. The cap drew at
    /// least one line whatever its length, so a payload opening with a long paragraph was cut across diagrams, its box
    /// over the first part only.
    /// </summary>
    [Fact]
    public async Task T42_A_first_line_alone_past_the_character_limit_is_drawn_cut_and_the_note_stays_one_note_with_one_box()
    {
        var paragraph = string.Join(" ", Enumerable.Repeat("word", 3040));
        var body = paragraph + "\n" + string.Join("\n", Enumerable.Range(1, 59).Select(i => $"\"tx_{i:D4}\" is line {i + 1}"));
        await OpenOneCallAsync("scroll-t42.html", "scroll-paragraph", LongPayloadReports.Call("scroll-paragraph", 0, body));
        var idx = await BoxAsync("tx");
        await Page.WaitForTimeoutAsync(500);
        Assert.False(await Page.EvaluateAsync<bool>("() => /Continued (On Next|From Previous) Diagram/.test(window.__scroll.container().textContent)"),
            "the capped note was cut across diagrams");
        Assert.Equal(1, await Page.EvaluateAsync<int>($"() => window.__scroll.container().querySelectorAll('.note-scroll-frame[data-note=\"{idx}\"]').length"));
        var lines = await LinesAsync(idx);
        Assert.Equal(60, lines.Length);
        Assert.Equal(paragraph, lines[0]);
        // the composed source, before the page cuts anything across diagrams: the paragraph, cut, is all it draws
        var drawn = await Page.EvaluateAsync<int>("""
            () => window._parseNoteBlocks(window.__scroll.container().getAttribute('data-plantuml'))
                .find(b => b.contentLines.some(l => l.indexOf('word word word') >= 0)).contentLines.join('\n').length
            """);
        Assert.InRange(drawn, 1, 15000);
    }

    /// <summary>
    /// A note the browser's IntersectionObserver cannot see still gets its box as it comes near. WebKit's observer reads
    /// an SVG element inside the report's scaled <c>&lt;svg&gt;</c> as a box with no height at the svg's corner, so it
    /// never saw a note come near, and in WebKit no note had a box. The observer is stubbed to answer as WebKit's does
    /// for an element inside an svg (the suite runs in Chromium); the outer svg and HTML elements it reads as they are.
    /// </summary>
    [Fact]
    public async Task T43_A_note_the_browser_s_observer_cannot_see_gets_its_box_as_it_comes_near()
    {
        await Page.AddInitScriptAsync("""
            (() => {
                var Real = window.IntersectionObserver;
                window.IntersectionObserver = function(callback, options) {
                    return new Real(function(entries, observer) {
                        callback(entries.map(function(e) {
                            if (!(e.target instanceof SVGElement) || !e.target.ownerSVGElement) return e;
                            var corner = { x: 0, y: 0, top: 0, bottom: 0, left: 0, right: 0, width: 0, height: 0 };
                            return { target: e.target, time: e.time, rootBounds: e.rootBounds, isIntersecting: false,
                                intersectionRatio: 0, boundingClientRect: corner, intersectionRect: corner };
                        }), observer);
                    }, options);
                };
            })();
            """);
        await OpenAsync("scroll-t43.html", Expanded());
        // a note far down the page has no box until it comes near
        var idx = await NoteIndexAsync("r60x");
        await Page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await Page.WaitForTimeoutAsync(400);
        Assert.True(await Page.EvaluateAsync<bool>($"() => window.__scroll.group({idx}).paths[0].getBoundingClientRect().top > 2 * window.innerHeight"),
            "premise: at the top of the page the note is more than a window height away");
        Assert.Equal(0, await Page.EvaluateAsync<int>($"() => window.__scroll.container().querySelectorAll('.note-scroll-frame[data-note=\"{idx}\"]').length"));
        idx = await BoxAsync("r60x");
        Assert.Equal(60, (await LinesAsync(idx)).Length);
    }

    /// <summary>
    /// A selection that runs from a box past its note, into the next arrow's label, copies the box's lines and the label
    /// as it reads. Inside one note the menu maps any selection back onto the note's own text; past it, the page reads
    /// the selection itself, and the browser's own reading (Chromium's) ends every word the engine drew with a line break.
    /// </summary>
    [Fact]
    public async Task T44_A_selection_from_a_box_into_the_next_arrow_copies_the_box_lines_and_the_label_as_it_reads()
    {
        await OpenAsync("scroll-t44.html", Expanded());
        var idx = await BoxAsync("r150");
        var lines = await LinesAsync(idx);
        await Page.EvaluateAsync($$"""
            () => {
                var g = window.__scroll.group({{idx}}), box = window.__scroll.box({{idx}}), svg = g.paths[0].ownerSVGElement;
                // the next request's label, drawn after the note
                var label = Array.from(svg.querySelectorAll('text')).filter(t => t.textContent.indexOf('/api/items?page=4') >= 0
                    && (g.paths[0].compareDocumentPosition(t) & Node.DOCUMENT_POSITION_FOLLOWING))[0];
                var walker = document.createTreeWalker(box, NodeFilter.SHOW_TEXT), node, line = 0, start = null;
                while ((node = walker.nextNode()) && !start) { if (line === 3) start = node; if (node.textContent === '\n') line++; }
                var range = document.createRange();
                range.setStart(start, 0);
                range.setEnd(label.firstChild, label.firstChild.textContent.length);
                var sel = window.getSelection(); sel.removeAllRanges(); sel.addRange(range);
                var r = g.paths[0].getBoundingClientRect();
                g.paths[0].dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true,
                    clientX: r.left + r.width / 2, clientY: r.top + 6 }));
            }
            """);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });
        await Page.Locator(".diagram-ctx-menu").GetByText("Copy Highlighted Text").ClickAsync();
        var copied = (await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()")).Replace("\r\n", "\n");

        // the copy is trimmed, so its first line starts after that line's indent
        Assert.StartsWith((string.Join("\n", lines.Skip(3)) + "\n").TrimStart(), copied);
        // the label's row, after the arrow's number: one line
        Assert.Contains(copied.Split('\n'), l => l.EndsWith("GET: /api/items?page=4", StringComparison.Ordinal));
    }

    /// <summary>
    /// The token's pieces in the form <paramref name="crlf"/> picks, whichever this platform's generator wrote: LF line
    /// ends with each piece in its own tags, or CRLF line ends with the pieces, joined by bare LFs, in one pair.
    /// </summary>
    private static string TokenPieces(string source, bool crlf)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n').ToList();
        var first = lines.FindIndex(l => l.Contains("\"tk_0003\"", StringComparison.Ordinal));
        var last = first;
        while (System.Text.RegularExpressions.Regex.IsMatch(lines[last], @"<U\+200B>(</color>)?$")) last++;
        Assert.True(last > first, "premise: the generator broke the token into pieces");
        const string open = "<color:lightgray>", close = "</color>";
        var pieces = lines.GetRange(first, last - first + 1).Select(l => l.Replace(open, "").Replace(close, "")).ToList();
        var indent = pieces[0][..(pieces[0].Length - pieces[0].TrimStart(' ').Length)];
        pieces[0] = pieces[0].TrimStart(' ');
        var group = crlf
            ? indent + open + string.Join("\n", pieces) + close
            : indent + string.Join("\n", pieces.Select(p => open + p + close));
        lines.RemoveRange(first, last - first + 1);
        lines.Insert(first, group);
        return string.Join(crlf ? "\r\n" : "\n", lines);
    }
}
