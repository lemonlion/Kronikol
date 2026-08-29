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

    [Fact]
    public void Eligibility_and_emission_are_hoisted_into_shared_helpers()
    {
        // The hover closure, the toolbar queue builder and _preProcessSource
        // all delegate to the same container-level helpers.
        Assert.Contains("function ensureNoteFormatEligible(", _script);
        Assert.Contains("function ensureNoteYamlLines(", _script);
        Assert.Contains("function setAllNoteFormats(", _script);
    }

    [Fact]
    public void Toolbar_format_dropdown_handlers_are_wired()
    {
        Assert.Contains("window._setNoteFormat", _script);
        Assert.Contains("window._setScenarioNoteFormat", _script);
        Assert.Contains("function buildNoteFormatQueue(", _script);
    }

    [Fact]
    public void PreProcessSource_applies_note_formats_for_a_yaml_default()
    {
        // A lazy container decompressed after a bulk YAML command (or under a
        // yaml config default) must render straight into YAML.
        var idx = _script.IndexOf("window._preProcessSource", StringComparison.Ordinal);
        Assert.True(idx >= 0);
        var body = _script[idx.._script.IndexOf("function processRenderQueue(", idx, StringComparison.Ordinal)];
        Assert.Contains("setAllNoteFormats(", body);
        Assert.Contains("applyNoteFormats(", body);
        Assert.Contains("_noteFormatPreference", body);
    }

    [Fact]
    public void Note_format_default_token_is_substituted()
    {
        // Parameterless overload = json default; yaml overload flips it; the
        // raw token never leaks into a generated page.
        Assert.Contains("window._noteFormatDefault = 'json'", _script);
        Assert.DoesNotContain("__NOTE_FORMAT_DEFAULT__", _script);

        var yamlScript = DiagramContextMenu.GetCollapsibleNotesScript(NotePayloadFormat.Yaml);
        Assert.Contains("window._noteFormatDefault = 'yaml'", yamlScript);
        Assert.DoesNotContain("__NOTE_FORMAT_DEFAULT__", yamlScript);
    }

    [Fact]
    public void Unescape_helper_is_exposed_for_the_copy_text_path()
    {
        Assert.Contains("function unescapeNoteDisplayLine(", _script);
        Assert.Contains("window._noteUnescapeDisplayLine", _script);
    }
}
