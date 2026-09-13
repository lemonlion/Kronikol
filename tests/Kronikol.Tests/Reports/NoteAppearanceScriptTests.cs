using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Structural tests for the per-note appearance controls — monospace payload text and full-width
/// notes (plans/NOTE_WRAP_AND_WIDTH_PLAN.md Part B). Behavioural coverage is in the Playwright suite
/// (<c>NoteAppearanceTests</c>), which renders real diagrams and reads the painted SVG back; these
/// pin the wiring that a refactor could quietly break.
/// </summary>
public class NoteAppearanceScriptTests
{
    private readonly string _script = DiagramContextMenu.GetCollapsibleNotesScript();

    /// <summary>
    /// The <b>only</b> per-note width handle PlantUML has. Measured on the shipped engine and on real
    /// Java PlantUML: the element selectors — <c>note { … }</c>, <c>note&lt;&lt;x&gt;&gt; { … }</c>,
    /// and <c>sequenceDiagram { note { … } }</c> — all silently ignore <c>MaximumWidth</c>. Only a
    /// class selector plus a stereotype on the note header carries it.
    /// </summary>
    [Fact]
    public void Width_and_font_ride_a_class_style_block_and_a_note_stereotype()
    {
        Assert.Contains("MaximumWidth", _script);
        Assert.Contains("FontName", _script);
        Assert.Contains("kronNoteWide", _script);
        Assert.Contains("kronNoteMono", _script);
    }

    /// <summary>
    /// Courier New is the only name the engine and the browser both resolve — the engine sizes the
    /// note box from its own metrics and the browser paints the text, and every other candidate
    /// measured (monospace, Consolas, DejaVu Sans Mono, Menlo) sized a box for one font and painted
    /// another into it. An unresolved name is not a no-op: it has its own, third, metric.
    /// </summary>
    [Fact]
    public void The_monospace_font_is_named_explicitly()
    {
        Assert.Contains("Courier New", _script);
        Assert.DoesNotContain("FontName \"monospace\"", _script);
    }

    /// <summary>
    /// Every matcher that recognises a note header has to allow MORE than one stereotype, or the note
    /// blocks and the rendered note groups skew apart the moment a note carries both its generated
    /// class and an appearance class — the failure the note-button index tests exist to catch.
    /// </summary>
    [Fact]
    public void Every_note_header_matcher_allows_several_stereotypes()
    {
        Assert.DoesNotContain("note(?:<<\\w+>>)?", _script);
        Assert.DoesNotContain("(?:<<[^>]*>>)?", _script);
        Assert.Equal(4, CountOf(_script, "/^note(?:<<[^>]*>>)*\\s+(left|right)/"));
        Assert.Contains("/^note\\s*(?:<<[^>]*>>)*\\s+(?:left|right)\\s*$/", _script);
    }

    /// <summary>
    /// The fragment splitter recognises a style block by its opening and closing tags standing alone
    /// on a line, and replicates it into every fragment's prefix. A one-line block would reach the
    /// first fragment only, and every other fragment would draw its notes unstyled.
    /// </summary>
    [Fact]
    public void The_injected_style_block_puts_its_tags_on_their_own_lines()
    {
        var idx = _script.IndexOf("function noteAppearanceStyle(", StringComparison.Ordinal);
        Assert.True(idx >= 0);
        var body = _script[idx..(idx + 600)];
        Assert.Contains("'<' + 'style>'", body);
        Assert.Contains("'</' + 'style>'", body);
        Assert.Contains("open + '\\n", body);
    }

    /// <summary>
    /// The script must never contain a literal style tag: a report-wide scan cannot tell one in a
    /// script or a comment from a real block (the bug class the 3.0.82 work named).
    /// </summary>
    [Fact]
    public void The_script_never_writes_a_literal_style_tag()
    {
        Assert.DoesNotContain("<style>", _script);
        Assert.DoesNotContain("</style>", _script);
    }

    [Fact]
    public void Both_appearances_have_a_dedicated_setter_beside_setNoteState()
    {
        Assert.Contains("function setNoteWidth(container, noteIdx, mode", _script);
        Assert.Contains("function setNoteFont(container, noteIdx, font", _script);
        Assert.Contains("rerenderWithNoteStates(container", _script);
    }

    [Fact]
    public void Both_buttons_are_wired_into_the_hover_cluster()
    {
        var singleQuoted = _script.Replace("\"", "'");
        // Both are built by the shared glyph factory, which stamps data-note-btn from its btnName.
        Assert.Contains("g.setAttribute('data-note-btn', btnName);", singleQuoted);
        Assert.Contains("'mono', false)", singleQuoted);
        Assert.Contains("'width', appearanceInfo.width !== 'full')", singleQuoted);
        // …and the tall-note repositioning sweep has to move them with the rest of the cluster.
        Assert.Contains("btn === 'mono' || btn === 'width'", singleQuoted);
    }

    /// <summary>
    /// The width button only does something for a note whose text actually wraps, and the check is
    /// painted rows against source lines — measured to be exact, and to survive both shapes that
    /// could have broken it (a blank line paints its own whitespace-only row, and each grey header
    /// line paints one row). It runs on first hover, never as a per-render sweep.
    /// </summary>
    [Fact]
    public void Width_eligibility_is_painted_rows_against_source_lines_computed_lazily()
    {
        Assert.Contains("function paintedRowCount(grp)", _script);
        Assert.Contains("paintedRowCount(grp) > sourceLines", _script);
        Assert.Contains("widthBtn._eligible === undefined", _script);
    }

    /// <summary>
    /// Width is measured from the diagram's SLACK, never from the note's x offset: Kronikol draws a
    /// request payload as <c>note left</c>, which is anchored at x=0, so widening it pushes the
    /// participants right instead of extending the canvas and the note's x is not stable across the
    /// change. The natural-width measurement is the zoom code's, not a second one.
    /// </summary>
    [Fact]
    public void The_width_target_is_the_containers_slack_measured_once()
    {
        Assert.Contains("noteWidth + (inner - drawn)", _script);
        Assert.Contains("window._getDiagramNaturalWidth", _script);
        Assert.Contains("MIN_NOTE_WIDTH_PX", _script);
        Assert.Contains("MAX_NOTE_WIDTH_PX", _script);
    }

    [Fact]
    public void The_correction_pass_runs_at_most_once()
    {
        var idx = _script.IndexOf("if (container._refineNoteWidth", StringComparison.Ordinal);
        Assert.True(idx >= 0);
        var body = _script[idx..(idx + 800)];
        // Cleared before the retry, so a retry cannot schedule another.
        Assert.True(body.IndexOf("container._refineNoteWidth = false;", StringComparison.Ordinal)
            < body.IndexOf("rerenderWithNoteStates(container", StringComparison.Ordinal));
    }

    [Fact]
    public void Bulk_controls_exist_at_report_and_scenario_level_and_reuse_the_pending_state()
    {
        Assert.Contains("window._setNoteFont = function", _script);
        Assert.Contains("window._setScenarioNoteFont = function", _script);
        Assert.Contains("window._setNoteWidth = function", _script);
        Assert.Contains("window._setScenarioNoteWidth = function", _script);
        // The report-wide command costs one re-render per container; the format dropdown already
        // charges it (measured 4.7s over 110 containers, worst main-thread task 223ms) and the
        // pending state is what keeps it legible.
        Assert.Contains("renderWithPending(", _script);
        Assert.Contains("buildNoteAppearanceQueue(", _script);
    }

    /// <summary>The embedded component diagram is not a scenario diagram; no note control may rewrite it.</summary>
    [Fact]
    public void The_component_diagram_panel_is_excluded_from_bulk_appearance_commands()
    {
        var idx = _script.IndexOf("function buildNoteAppearanceQueue(", StringComparison.Ordinal);
        Assert.True(idx >= 0);
        var body = _script[idx..(idx + 900)];
        Assert.Contains("isComponentDiagramContainer(container)", body);
        Assert.Contains("puml-fragment", body);
    }

    [Fact]
    public void Appearance_default_tokens_are_substituted()
    {
        Assert.Contains("window._noteFontDefault = 'default'", _script);
        Assert.Contains("window._noteWidthDefault = 'default'", _script);
        Assert.DoesNotContain("__NOTE_FONT_DEFAULT__", _script);
        Assert.DoesNotContain("__NOTE_WIDTH_DEFAULT__", _script);

        var configured = DiagramContextMenu.GetCollapsibleNotesScript(ResolvedToggleDefaults.BuiltIn with
        {
            NoteFont = NoteFontFamily.Monospace,
            NoteWidth = NoteWidthMode.Full
        });
        Assert.Contains("window._noteFontDefault = 'mono'", configured);
        Assert.Contains("window._noteWidthDefault = 'full'", configured);
    }

    /// <summary>
    /// A container decompressed after a bulk command, or under a configured default, must render
    /// straight into that appearance rather than flashing the default first.
    /// </summary>
    [Fact]
    public void PreProcessSource_honours_the_seeded_appearance_defaults()
    {
        var idx = _script.IndexOf("window._preProcessSource", StringComparison.Ordinal);
        Assert.True(idx >= 0);
        var body = _script[idx.._script.IndexOf("function processRenderQueue(", idx, StringComparison.Ordinal)];
        Assert.Contains("_noteFontPreference", body);
        Assert.Contains("_noteWidthPreference", body);
        Assert.Contains("setAllNoteFonts(", body);
        Assert.Contains("setAllNoteWidths(", body);
    }

    /// <summary>
    /// A report whose notes are all at the default appearance must produce byte-identical diagram
    /// sources — the pass returns the source untouched rather than rebuilding it.
    /// </summary>
    [Fact]
    public void The_appearance_pass_is_a_no_op_until_something_is_toggled()
    {
        var idx = _script.IndexOf("function applyNoteAppearance(source, container)", StringComparison.Ordinal);
        Assert.True(idx >= 0);
        Assert.Contains("if (!containerHasAppearance(container)) return source;", _script[idx..(idx + 300)]);
    }

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
        return count;
    }
}
