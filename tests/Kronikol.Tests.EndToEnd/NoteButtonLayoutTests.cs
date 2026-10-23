using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A note's hover buttons (5.0.0, plans/OPTION_C_PLAN.md S9, P15, the owner's items R17.9, R17.13 to R17.15 and 24):
/// each button has a name, the triangles are drawn and clear of their button's edges, the top-right buttons sit side by
/// side with no gap where a hidden one would be and follow the visible part of a tall note, a truncated note's bottom
/// holds a collapse and an expand button, a collapsed note that already shows all of itself has nothing to open, and
/// making a note shorter from its lower part brings its new bottom back to the click, in a diagram drawn in fragments
/// too.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteButtonLayoutTests : DiagramNotePlaywrightBase
{
    public NoteButtonLayoutTests(PlaywrightFixture fixture) : base(fixture) { }

    private static readonly string LongBody = string.Join("\n", Enumerable.Range(1, 45).Select(i => $"Line {i}"));
    private static readonly string After = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"caller -> orders : GET /orders/{i}\norders --> caller : 200"));
    private static readonly string LongNote = "@startuml\nparticipant \"Caller\" as caller\nparticipant \"Orders\" as orders\n" +
        "caller -> orders : POST /orders\nnote right\n" + LongBody + "\nend note\norders --> caller : 201\n" + After + "\n@enduml";
    // a one-line note, then a long one
    private static readonly string OneRowAndLong = "@startuml\nparticipant \"Caller\" as caller\nparticipant \"Orders\" as orders\n" +
        "caller -> orders : SELECT\nnote right\n1 row\nend note\ncaller -> orders : POST /orders\nnote right\n" + LongBody + "\nend note\n@enduml";
    // a long note in a diagram tall enough to be drawn in fragments
    private static readonly string FragmentedLongNote = "@startuml\nparticipant \"Caller\" as caller\nparticipant \"Orders\" as orders\n" +
        "caller -> orders : POST /orders\nnote right\n" + LongBody + "\nend note\norders --> caller : 201\n" +
        string.Join("\n", Enumerable.Range(1, 160).Select(i => $"caller -> orders : GET /orders/{i}\norders --> caller : 200")) + "\n@enduml";

    // a short note and a long one, each at the top of a fragment of its own, so each sits where the other does in its own
    // picture, the first shorter than the second is made: the 125 calls after the first fill the first fragment, each
    // labelled as the rest, so both fragments space their participants alike, and 40 after the second keep the page going
    // below it (a page that ends at the note scrolls back by what the note loses, and keeps the place whatever the page does)
    private static readonly string Calls = "caller -> orders : GET /orders\norders --> caller : 200";
    private static readonly string LongNoteAtTheTopOfEachFragment = "@startuml\nparticipant \"Caller\" as caller\nparticipant \"Orders\" as orders\n" +
        "caller -> orders : POST /orders\nnote right\nLine 1\nLine 2\nLine 3\nLine 4\nend note\norders --> caller : 201\n" +
        string.Join("\n", Enumerable.Repeat(Calls, 125)) +
        "\ncaller -> orders : POST /orders\nnote right\n" + LongBody + "\nend note\norders --> caller : 201\n" +
        string.Join("\n", Enumerable.Repeat(Calls, 40)) + "\n@enduml";

    private async Task OpenAsync(string fileName, string source, ResolvedToggleDefaults? toggles = null)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios = [new() { Id = "s1", DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode("s1", "", source)],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs, toggleDefaults: toggles);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    // the buttons showing after a hover, by name
    private Task<string[]> ShownButtonsAsync() => Page.EvaluateAsync<string[]>(
        "() => Array.from(document.querySelectorAll('.note-toggle-icon')).filter(i => i.style.opacity === '1' && i.style.display !== 'none').map(i => i.getAttribute('data-note-btn'))");

    private Task WaitForButtonsAsync() => Page.WaitForFunctionAsync(
        "() => Array.from(document.querySelectorAll('.note-toggle-icon')).some(i => i.style.opacity === '1')", null,
        new() { Timeout = 5000, PollingInterval = 200 });

    [Fact]
    public async Task An_expanded_long_notes_triangles_are_drawn_and_clear_of_their_buttons_edges()
    {
        await OpenAsync("NoteButtonsExpanded.html", LongNote);
        await SetScenarioState("expanded");
        await HoverNoteRect(0);
        await WaitForButtonsAsync();

        var shown = await ShownButtonsAsync();
        Assert.Contains("minus", shown);
        Assert.Contains("truncate", shown);
        Assert.Contains("truncate-bottom", shown);
        Assert.Equal(0, await Page.EvaluateAsync<int>(
            "() => Array.from(document.querySelectorAll('.note-toggle-icon text')).filter(t => /[\\u25B2\\u25BC]/.test(t.textContent)).length"));
        // the top ▲ 3 px clear of its button's top and bottom (item 24), the bottom one 2 px (item 13)
        var clear = await Page.EvaluateAsync<double[]>("""
            () => ['truncate', 'truncate-bottom'].map(n => {
                const g = document.querySelector(".note-toggle-icon[data-note-btn='" + n + "']");
                const r = g.querySelector('rect').getBBox(), p = g.querySelector('polygon').getBBox();
                return Math.min(p.y - r.y, r.y + r.height - (p.y + p.height));
            })
            """);
        Assert.Equal(3, clear[0], 1);
        Assert.Equal(2, clear[1], 1);
    }

    // the title of each button showing after a hover, by name
    private async Task<Dictionary<string, string>> ShownTitlesAsync()
    {
        await HoverNoteRect(0);
        await WaitForButtonsAsync();
        var pairs = await Page.EvaluateAsync<string[][]>(
            "() => Array.from(document.querySelectorAll('.note-toggle-icon')).filter(i => i.style.opacity === '1' && i.style.display !== 'none')" +
            ".map(i => [i.getAttribute('data-note-btn'), (i.querySelector('title') || { textContent: '' }).textContent])");
        return pairs.ToDictionary(p => p[0], p => p[1]);
    }

    private async Task ClickButtonAndWaitForAsync(string name, string shows)
    {
        await Page.EvaluateAsync("n => document.querySelector(`.note-toggle-icon[data-note-btn='${n}'] rect`).dispatchEvent(new MouseEvent('click', { bubbles: true }))", name);
        await Page.WaitForFunctionAsync("n => !!document.querySelector(`.note-toggle-icon[data-note-btn='${n}']`)", shows,
            new() { Timeout = 10000, PollingInterval = 200 });
    }

    // Every button says what a click does (S9, D42): the drawn triangles have no glyph to read, so the tooltips the
    // preview gave them by their glyph text (round 9) are set by name when a button is made.
    [Fact]
    public async Task Every_button_of_a_long_note_says_what_a_click_does()
    {
        await OpenAsync("NoteButtonsTitlesLong.html", LongNote);
        var truncated = await ShownTitlesAsync();
        Assert.Equal("Fold this box to one line", truncated["minus"]);
        Assert.Equal("Fold this box to one line", truncated["collapse-bottom"]);
        Assert.Equal("Show more of this box", truncated["expand-bottom"]);

        await ClickButtonAndWaitForAsync("expand-bottom", "truncate-bottom");
        var expanded = await ShownTitlesAsync();
        Assert.Equal("Cut this box back to the line limit", expanded["truncate"]);
        Assert.Equal("Cut this box back to the line limit", expanded["truncate-bottom"]);
        Assert.Equal("Fold this box to one line", expanded["minus"]);

        // a long note opens from one line to the line limit, not in full
        await ClickButtonAndWaitForAsync("minus", "plus");
        var collapsed = await ShownTitlesAsync();
        Assert.Equal("Open this box to the line limit", collapsed["plus"]);
        Assert.Equal("Show more of this box", collapsed["expand-bottom"]);
    }

    [Fact]
    public async Task A_short_notes_plus_opens_it_in_full()
    {
        const string shortNote = "@startuml\nparticipant \"Caller\" as caller\nparticipant \"Orders\" as orders\n" +
            "caller -> orders : POST /orders\nnote right\nLine 1\nLine 2\nLine 3\nend note\norders --> caller : 201\n@enduml";
        await OpenAsync("NoteButtonsTitlesShort.html", shortNote);
        await ShownTitlesAsync();
        await ClickButtonAndWaitForAsync("minus", "plus");
        Assert.Equal("Open this box in full", (await ShownTitlesAsync())["plus"]);
    }

    [Fact]
    public async Task A_truncated_notes_bottom_holds_a_collapse_and_an_expand_button_side_by_side()
    {
        // payloads start truncated at 10 lines, and this note has 45
        await OpenAsync("NoteButtonsTruncated.html", LongNote);
        await HoverNoteRect(0);
        await WaitForButtonsAsync();

        var shown = await ShownButtonsAsync();
        Assert.Contains("collapse-bottom", shown);
        Assert.Contains("expand-bottom", shown);
        var pair = await Page.EvaluateAsync<double[]>("""
            () => {
                const c = document.querySelector(".note-toggle-icon[data-note-btn='collapse-bottom'] rect").getBBox();
                const e = document.querySelector(".note-toggle-icon[data-note-btn='expand-bottom'] rect").getBBox();
                const n = document.querySelector('.note-hover-rect').getBBox();
                return [c.width, e.width, e.x - (c.x + c.width), (c.x + e.x + e.width) / 2 - (n.x + n.width / 2)];
            }
            """);
        Assert.Equal(24, pair[0], 1);
        Assert.Equal(24, pair[1], 1);
        Assert.Equal(3, pair[2], 1);
        Assert.Equal(0, pair[3], 1);
    }

    [Fact]
    public async Task A_hidden_button_leaves_no_gap_among_the_top_right_buttons()
    {
        // a wide SQL query, drawn by the emitter: it wraps at the set width, so it can widen, and it is not JSON, so its
        // format button stays hidden
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithWideSqlNote(TempDir, OutputDir, "NoteButtonsPacked.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
        await SetScenarioState("expanded");
        await HoverNoteRect(0);
        await WaitForButtonsAsync();

        // from the right: − at the edge, then the width button straight after it, where the hidden format button was
        var xs = await Page.EvaluateAsync<double[]>("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .filter(i => i.style.opacity === '1' && i.style.display !== 'none' && !/bottom/.test(i.getAttribute('data-note-btn') || ''))
                .map(i => { const b = i.querySelector('rect').getBBox(); const t = i.transform.baseVal.consolidate(); return b.x + (t ? t.matrix.e : 0); })
                .sort((a, b) => b - a)
            """);
        Assert.Equal(2, xs.Length); // minus and width
        var right = await Page.EvaluateAsync<double>("() => { const n = document.querySelector('.note-hover-rect').getBBox(); return n.x + n.width; }");
        Assert.Equal(right - 17, xs[0], 1);
        Assert.Equal(right - 34, xs[1], 1);
    }

    [Fact]
    public async Task A_collapsed_note_that_shows_all_of_itself_has_nothing_to_open()
    {
        await OpenAsync("NoteButtonsWhole.html", OneRowAndLong);
        await SetScenarioState("collapsed");

        await HoverNoteRect(1);
        await WaitForButtonsAsync();
        var longNote = await ShownButtonsAsync();
        Assert.Contains("plus", longNote);
        Assert.Contains("expand-bottom", longNote);

        // a note's buttons are made when it is drawn; the one-line note's are those inside its box
        var oneRow = await Page.EvaluateAsync<string[]>("""
            () => {
                const n = document.querySelectorAll('.note-hover-rect')[0].getBBox();
                return Array.from(document.querySelectorAll('.note-toggle-icon')).filter(i => {
                    const b = i.querySelector('rect').getBBox();
                    return b.y >= n.y - 1 && b.y <= n.y + n.height + 1;
                }).map(i => i.getAttribute('data-note-btn'));
            }
            """);
        Assert.DoesNotContain("plus", oneRow);
        Assert.DoesNotContain("expand-bottom", oneRow);
    }

    [Fact]
    public async Task The_top_right_buttons_follow_the_visible_part_of_a_tall_note_the_triangle_too()
    {
        // a tall note: drawn in full, as 5.4.0 draws an expanded payload 40 lines tall with its body in a scroll box,
        // whose buttons stay in its top row (LongPayloadScrollTests.T24)
        await OpenAsync("NoteButtonsTall.html", LongNote, ReportTestHelper.InFull(ResolvedToggleDefaults.BuiltIn));
        await SetScenarioState("expanded");
        // the note's top 300 px above the window
        await Page.EvaluateAsync("() => { const r = document.querySelector('.note-hover-rect').getBoundingClientRect(); window.scrollBy(0, r.top + 300); }");
        await HoverNoteRect(0);
        await WaitForButtonsAsync();

        var dy = await Page.EvaluateAsync<double[]>("""
            () => ['minus', 'truncate'].map(n => {
                const t = document.querySelector(".note-toggle-icon[data-note-btn='" + n + "']").transform.baseVal.consolidate();
                return t ? t.matrix.f : 0;
            })
            """);
        Assert.True(dy[0] > 100, $"the minus button stayed at the note's top (moved {dy[0]})");
        Assert.Equal(dy[0], dy[1], 1);
    }

    [Theory]
    [InlineData("a diagram drawn whole")]
    [InlineData("a diagram drawn in fragments")]
    public async Task Making_a_note_shorter_from_its_bottom_brings_its_new_bottom_back_to_the_click(string where)
    {
        var fragments = where.Contains("fragments");
        await OpenAsync(fragments ? "NoteButtonsKeepPlaceFragments.html" : "NoteButtonsKeepPlace.html", fragments ? FragmentedLongNote : LongNote);
        if (fragments)
            Assert.True(await Page.Locator(".puml-fragment").CountAsync() > 1, "the diagram was not drawn in fragments");
        await SetScenarioState("expanded");

        // the note's bottom button in the middle of the window, then a click on it
        await Page.EvaluateAsync("() => { const r = document.querySelector('.note-hover-rect').getBoundingClientRect(); window.scrollBy(0, r.bottom - innerHeight / 2); }");
        await HoverNoteRect(0);
        await WaitForButtonsAsync();
        var y = await Page.EvaluateAsync<double>(
            "() => { const r = document.querySelector(\".note-toggle-icon[data-note-btn='truncate-bottom'] rect\").getBoundingClientRect(); return r.top + r.height / 2; }");
        await Page.EvaluateAsync("""
            y => {
                const r = document.querySelector(".note-toggle-icon[data-note-btn='truncate-bottom'] rect");
                const b = r.getBoundingClientRect();
                r.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: b.left + b.width / 2, clientY: y }));
            }
            """, y);

        // drawn again shorter, and scrolled so the new bottom sits just under the click
        await Page.WaitForFunctionAsync(
            "y => { const r = document.querySelector('.note-hover-rect'); if (!r) return false; const b = r.getBoundingClientRect(); return b.height < 400 && Math.abs(b.bottom - Math.min(y + 8, innerHeight - 24)) <= 2; }",
            y, new() { Timeout = 20000, PollingInterval = 200 });
    }

    // The page found the note clicked by its place in its picture, among the notes of every fragment: a click on a note
    // in the second fragment found the first fragment's note in that place, and kept nobody's place (5.0.0 to 5.2.6)
    [Fact]
    public async Task Making_a_note_shorter_in_a_later_fragment_brings_that_note_s_new_bottom_back_to_the_click()
    {
        await OpenAsync("NoteButtonsKeepPlaceLaterFragment.html", LongNoteAtTheTopOfEachFragment);
        await SetScenarioState("expanded");
        Assert.Equal("the same place", await Page.EvaluateAsync<string>("""
            () => {
                const f = document.querySelectorAll('.puml-fragment');
                if (f.length < 2) return 'fragments: ' + f.length;
                const a = f[0].querySelector('.note-hover-rect'), b = f[1].querySelector('.note-hover-rect');
                if (!a || !b) return 'a fragment without a note';
                const p = a.getBBox(), q = b.getBBox();
                return Math.abs(p.x - q.x) < 1 && Math.abs(p.y - q.y) < 1 ? 'the same place' : 'apart: ' + [p.x, p.y, q.x, q.y].join(' ');
            }
            """));

        // with the browser's own scroll anchoring off, as Safari has none, so that the place is the page's to keep: Chromium
        // anchors the scroll to what follows the note, and kept the place without the page
        await Page.AddStyleTagAsync(new() { Content = "html, body { overflow-anchor: none; }" });

        // the second note's bottom button in the middle of the window, then a click on it
        await Page.EvaluateAsync("() => { const r = document.querySelectorAll('.note-hover-rect')[1].getBoundingClientRect(); window.scrollBy(0, r.bottom - innerHeight / 2); }");
        await HoverNoteRect(1);
        await WaitForButtonsAsync();
        var y = await Page.EvaluateAsync<double>("""
            () => {
                window.__bottom = Array.from(document.querySelectorAll(".note-toggle-icon[data-note-btn='truncate-bottom']")).find(i => i.style.opacity === '1').querySelector('rect');
                const r = window.__bottom.getBoundingClientRect();
                return r.top + r.height / 2;
            }
            """);
        await Page.EvaluateAsync("""
            y => {
                const b = window.__bottom.getBoundingClientRect();
                window.__bottom.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: b.left + b.width / 2, clientY: y }));
            }
            """, y);

        await Page.WaitForFunctionAsync(
            "y => { const r = document.querySelectorAll('.note-hover-rect')[1]; if (!r) return false; const b = r.getBoundingClientRect(); return b.height < 400 && Math.abs(b.bottom - Math.min(y + 8, innerHeight - 24)) <= 2; }",
            y, new() { Timeout = 20000, PollingInterval = 200 });
    }
}
