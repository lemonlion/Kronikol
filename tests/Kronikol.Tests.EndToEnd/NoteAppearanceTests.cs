namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The per-note appearance controls: monospace payload text and full-width notes
/// (plans/NOTE_WRAP_AND_WIDTH_PLAN.md Part B). Every assertion here reads what the engine
/// <b>paints</b> — PlantUML emits one <c>&lt;text&gt;</c> element per word, so display rows are the
/// distinct <c>y</c> values and a row's text is its elements ordered by <c>x</c>. Asserting on the
/// source would pass for a source that draws nothing of the sort.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteAppearanceTests : DiagramNotePlaywrightBase
{
    public NoteAppearanceTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task NavigateToSqlNote(string fileName)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithWideSqlNote(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    /// <summary>Rows drawn inside the note at <paramref name="noteIndex"/>, top to bottom.</summary>
    private Task<string[]> NoteRows(int noteIndex = 0) =>
        Page.EvaluateAsync<string[]>("""
            (idx) => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                if (!svg) return [];
                var groups = window._findNoteGroups(svg);
                if (!groups[idx]) return [];
                var rows = new Map();
                groups[idx].texts.forEach(function(t) {
                    var y = parseFloat(t.getAttribute('y') || '0');
                    var x = parseFloat(t.getAttribute('x') || '0');
                    if (!rows.has(y)) rows.set(y, []);
                    rows.get(y).push({ x: x, text: t.textContent });
                });
                return Array.from(rows.keys()).sort(function(a, b) { return a - b; })
                    .map(function(y) {
                        return rows.get(y).sort(function(a, b) { return a.x - b.x; })
                            .map(function(p) { return p.text; }).join('');
                    });
            }
            """, noteIndex);

    private Task<double> NoteWidth(int noteIndex = 0) =>
        Page.EvaluateAsync<double>("""
            (idx) => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                if (!svg) return 0;
                var groups = window._findNoteGroups(svg);
                return groups[idx] ? window._getNoteBBox(groups[idx]).width : 0;
            }
            """, noteIndex);

    private Task<string[]> NoteFontFamilies(int noteIndex = 0) =>
        Page.EvaluateAsync<string[]>("""
            (idx) => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                if (!svg) return [];
                var groups = window._findNoteGroups(svg);
                if (!groups[idx]) return [];
                var seen = {};
                groups[idx].texts.forEach(function(t) { seen[t.getAttribute('font-family') || ''] = true; });
                return Object.keys(seen);
            }
            """, noteIndex);

    /// <summary>Hovers the note and clicks one of its top-right glyph buttons.</summary>
    private Task<string> ClickNoteButton(string button, int noteIndex = 0) =>
        Page.EvaluateAsync<string>("""
            (args) => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                if (!svg) return 'NO_SVG';
                var groups = window._findNoteGroups(svg);
                if (!groups[args.idx]) return 'NO_NOTE';
                var bbox = window._getNoteBBox(groups[args.idx]);
                groups[args.idx].paths[0].dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }));
                var icons = svg.querySelectorAll('.note-toggle-icon');
                for (var i = 0; i < icons.length; i++) {
                    if (icons[i].getAttribute('data-note-btn') !== args.button) continue;
                    if (icons[i].style.display === 'none') continue;
                    var rect = icons[i].querySelector('rect');
                    var ix = parseFloat(rect.getAttribute('x'));
                    if (ix < bbox.x - 5 || ix > bbox.x + bbox.width + 5) continue;
                    rect.dispatchEvent(new MouseEvent('click', { bubbles: true }));
                    return 'CLICKED';
                }
                return 'NOT_VISIBLE';
            }
            """, new { idx = noteIndex, button });

    private Task WaitForRenderIdle() =>
        Page.WaitForFunctionAsync("() => !window._plantumlRendering", null,
            new() { PollingInterval = 200, Timeout = 30000 });

    private Task<string> DiagramSource() =>
        Page.EvaluateAsync<string>("() => document.querySelector('[data-diagram-type=\"plantuml\"]').getAttribute('data-plantuml')");

    // ── The engine contract ───────────────────────────────────────────────
    // `.className { MaximumWidth }` is the ONLY per-note width handle PlantUML has: every element
    // selector (note { … }, note<<x>> { … }, sequenceDiagram { note { … } }) silently ignores it.
    // If an engine bump breaks this, the whole feature stops working with no error anywhere.

    [Fact]
    public async Task A_width_class_on_one_note_widens_only_that_note()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithNoteWidthClassDiagram(TempDir, OutputDir, "NoteWidthClass.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        var plainRows = await NoteRows(0);
        var classedRows = await NoteRows(1);

        Assert.True(plainRows.Length > classedRows.Length,
            $"the unclassed note should wrap more: {plainRows.Length} rows vs {classedRows.Length}");
        Assert.True(await NoteWidth(1) > await NoteWidth(0), "the classed note should be drawn wider");
    }

    [Fact]
    public async Task A_width_class_composes_with_the_notes_own_stereotype()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithNoteWidthClassDiagram(TempDir, OutputDir, "NoteWidthCompose.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        // Both notes keep the <<eventNote>> fill; only the classed one also takes the font.
        var fills = await Page.EvaluateAsync<string[]>("""
            () => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                return window._findNoteGroups(svg).map(function(g) {
                    return (g.paths[0].getAttribute('fill') || '').toLowerCase();
                });
            }
            """);
        Assert.Equal(2, fills.Length);
        Assert.Equal(fills[0], fills[1]);

        Assert.DoesNotContain(await NoteFontFamilies(0), f => f.Contains("Courier", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(await NoteFontFamilies(1), f => f.Contains("Courier", StringComparison.OrdinalIgnoreCase));
    }

    // ── The per-note controls ─────────────────────────────────────────────

    [Fact]
    public async Task Widening_a_note_draws_its_text_on_fewer_rows()
    {
        await NavigateToSqlNote("NoteWidenFewerRows.html");

        var before = await NoteRows();
        var beforeWidth = await NoteWidth();

        Assert.Equal("CLICKED", await ClickNoteButton("width"));
        await WaitForRenderIdle();
        await WaitForNoteElements();

        Assert.True(await NoteWidth() > beforeWidth,
            $"the note should be drawn wider than {beforeWidth}px");
        Assert.True((await NoteRows()).Length < before.Length,
            "a wider note should wrap onto fewer rows");
    }

    [Fact]
    public async Task Widening_stamps_the_class_and_contracting_restores_the_source_exactly()
    {
        await NavigateToSqlNote("NoteWidenRoundTrip.html");
        var original = await DiagramSource();
        Assert.DoesNotContain("kronNoteWide", original);

        Assert.Equal("CLICKED", await ClickNoteButton("width"));
        await WaitForRenderIdle();
        await WaitForNoteElements();

        var widened = await DiagramSource();
        Assert.Contains("<<kronNoteWide>>", widened);
        Assert.Contains("MaximumWidth", widened);
        // The style block's tags must stand alone on their lines or the fragment splitter drops it.
        Assert.Contains("\n<style>\n", widened.Replace("\r\n", "\n"));

        Assert.Equal("CLICKED", await ClickNoteButton("width"));
        await WaitForRenderIdle();
        await WaitForNoteElements();

        Assert.Equal(original, await DiagramSource());
    }

    [Fact]
    public async Task Monospace_paints_the_note_text_in_courier_new()
    {
        await NavigateToSqlNote("NoteMonoPaint.html");

        Assert.DoesNotContain(await NoteFontFamilies(), f => f.Contains("Courier", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("CLICKED", await ClickNoteButton("mono"));
        await WaitForRenderIdle();
        await WaitForNoteElements();

        // The name matters: the engine sizes the box and the browser paints the text, and measured
        // across seven candidates Courier New is the only one both of them resolve.
        Assert.Contains(await NoteFontFamilies(), f => f.Contains("Courier", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("<<kronNoteMono>>", await DiagramSource());
    }

    /// <summary>
    /// The measured reason monospace is the larger half of this feature: analytics SQL is written
    /// with its clauses padded into columns, and a proportional font destroys that alignment however
    /// wide the note is. Measured on the reported query, the same token's x scattered across 65.8px
    /// in the proportional font and sat at one x in Courier New.
    /// </summary>
    [Fact]
    public async Task Monospace_lines_up_text_that_the_proportional_font_scatters()
    {
        await NavigateToSqlNote("NoteMonoAlign.html");

        var spreadBefore = await AlignedTokenSpread("AS");
        Assert.True(spreadBefore > 20,
            $"the fixture must actually be misaligned to begin with (spread {spreadBefore}px)");

        Assert.Equal("CLICKED", await ClickNoteButton("mono"));
        await WaitForRenderIdle();
        await WaitForNoteElements();

        var spreadAfter = await AlignedTokenSpread("AS");

        Assert.True(spreadAfter < 1,
            $"the AS column should land at one x in monospace: {spreadBefore} -> {spreadAfter}");
    }

    /// <summary>
    /// How far apart a token that sits in the SAME SOURCE COLUMN on several note lines is actually
    /// drawn. PlantUML emits one element per word, so the token's own x is directly readable — this
    /// is the measurement that showed the proportional font scattering an aligned column across
    /// 65.8px. An interior token is the right probe: leading indentation alone does not move the
    /// first word's x.
    /// </summary>
    private Task<double> AlignedTokenSpread(string token) =>
        Page.EvaluateAsync<double>("""
            (tok) => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                var groups = window._findNoteGroups(svg);
                if (!groups[0]) return 0;
                var xs = [];
                groups[0].texts.forEach(function(t) {
                    if ((t.textContent || '').trim() !== tok) return;
                    xs.push(parseFloat(t.getAttribute('x') || '0'));
                });
                if (xs.length < 2) return -1;
                return Math.max.apply(null, xs) - Math.min.apply(null, xs);
            }
            """, token);

    [Fact]
    public async Task The_width_button_is_absent_on_a_note_that_already_fits()
    {
        // The response note here is a one-line JSON array: nothing wraps, so widening it would do
        // nothing and the glyph is not offered.
        await NavigateToSqlNote("NoteWidthNotOffered.html");

        var result = await ClickNoteButton("width", noteIndex: 1);

        Assert.Equal("NOT_VISIBLE", result);
    }

    [Fact]
    public async Task Width_survives_a_headers_toggle()
    {
        await NavigateToSqlNote("NoteWidthSurvivesHeaders.html");

        Assert.Equal("CLICKED", await ClickNoteButton("width"));
        await WaitForRenderIdle();
        await WaitForNoteElements();

        await Page.Locator(".toggle-btn[data-toggle='headers']").First.ClickAsync();
        await WaitForRenderIdle();
        await WaitForNoteElements();

        Assert.Contains("<<kronNoteWide>>", await DiagramSource());
    }

    // ── The bulk controls ─────────────────────────────────────────────────

    [Fact]
    public async Task The_report_level_font_select_switches_every_note()
    {
        await NavigateToSqlNote("NoteMonoBulk.html");

        await Page.Locator(".note-font-select").First.SelectOptionAsync("mono");
        await WaitForRenderIdle();
        await Page.WaitForFunctionAsync(
            "() => (document.querySelector('[data-diagram-type=\"plantuml\"]').getAttribute('data-plantuml') || '').indexOf('kronNoteMono') >= 0",
            null, new() { PollingInterval = 200, Timeout = 30000 });
        await WaitForNoteElements();

        Assert.Contains(await NoteFontFamilies(), f => f.Contains("Courier", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_report_level_width_select_widens_every_note()
    {
        await NavigateToSqlNote("NoteWidthBulk.html");
        var beforeWidth = await NoteWidth();

        await Page.Locator(".note-width-select").First.SelectOptionAsync("full");
        await WaitForRenderIdle();
        await Page.WaitForFunctionAsync(
            "() => (document.querySelector('[data-diagram-type=\"plantuml\"]').getAttribute('data-plantuml') || '').indexOf('kronNoteWide') >= 0",
            null, new() { PollingInterval = 200, Timeout = 30000 });
        await WaitForNoteElements();

        Assert.True(await NoteWidth() > beforeWidth, "the bulk command should widen the wide note");
    }
}
