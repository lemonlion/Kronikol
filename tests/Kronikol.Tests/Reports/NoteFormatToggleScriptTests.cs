using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Structural tests for the note JSON ⇄ YAML format toggle in
/// collapsible-notes-script.js (NOTE_YAML_TOGGLE_PLAN.md). Behavioral
/// coverage lives in the Playwright E2E suite (NoteYamlInternalsTests,
/// NoteYamlToggleTests); these pin the wiring that keeps per-note format
/// state flowing through every rebuild path.
/// </summary>
public class NoteFormatToggleScriptTests
{
    private readonly string _script = DiagramContextMenu.GetCollapsibleNotesScript();

    [Fact]
    public void Internals_are_exposed_for_test_drive()
    {
        Assert.Contains("window._noteFormatInternals", _script);
        Assert.Contains("reconstructNoteJson", _script);
        Assert.Contains("jsonTextToYamlLines", _script);
        Assert.Contains("escapeYamlLinesForNote", _script);
    }

    [Fact]
    public void SetNoteState_path_applies_note_formats_before_building_source()
    {
        // The shared rebuild helper must swap YAML payload lines into the
        // source before buildSourceWithNoteStates truncates/collapses.
        Assert.Contains("buildSourceWithNoteStates(applyNoteFormats(", _script);
    }

    [Fact]
    public void ProcessRenderQueue_applies_note_formats()
    {
        // Header/filter/details rebuilds must preserve the YAML state too.
        var idx = _script.IndexOf("function processRenderQueue(", StringComparison.Ordinal);
        Assert.True(idx >= 0);
        var body = _script[idx.._script.IndexOf("\n    function buildDetailsQueue(", idx, StringComparison.Ordinal)];
        Assert.Contains("applyNoteFormats(", body);
    }

    [Fact]
    public void Format_button_is_wired_into_the_hover_cluster()
    {
        Assert.Contains("data-note-btn', 'format'", _script.Replace("\"", "'"));
    }

    [Fact]
    public void Format_state_uses_a_dedicated_setter_beside_setNoteState()
    {
        Assert.Contains("function setNoteFormat(", _script);
        Assert.Contains("_noteFormats", _script);
    }
}
