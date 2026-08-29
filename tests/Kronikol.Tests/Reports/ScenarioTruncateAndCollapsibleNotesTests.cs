using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class ScenarioTruncateAndCollapsibleNotesTests
{
    private readonly string _script = DiagramContextMenu.GetCollapsibleNotesScript();

    // ═══════════════════════════════════════════════════════════
    // Fix: _setScenarioTruncateLines stores per-container
    //       truncateLines instead of temporarily overriding global
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void SetScenarioTruncateLines_stores_per_container_truncate_lines()
    {
        // The truncation line count must be stored per-container so that
        // headers toggle and details toggle preserve the scenario's value
        // instead of reverting to the global default.
        // Old approach: temporarily override window._truncateLines, restore in callback
        // New approach: set container._truncateLines on each container

        var fnBody = ExtractFunctionBody(_script, "_setScenarioTruncateLines");

        // Should store per-container
        Assert.Contains("_truncateLines = scenarioLines", fnBody);

        // Should NOT use the old save/restore pattern
        Assert.DoesNotContain("var prev = window._truncateLines", fnBody);
        Assert.DoesNotContain("_truncateLines = prev", fnBody);
    }

    // ═══════════════════════════════════════════════════════════
    // Bug 2: makeNotesCollapsible must remove existing toggle
    //         icons and hover rects before adding new ones
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void MakeNotesCollapsible_removes_existing_toggle_icons_before_adding_new_ones()
    {
        var fnBody = ExtractFunctionBody(_script, "makeNotesCollapsible");
        // Should remove old .note-toggle-icon elements
        Assert.Contains(".note-toggle-icon", fnBody);
        Assert.Matches(@"querySelectorAll\(['""]\.note-toggle-icon['""].*forEach.*remove", fnBody);
    }

    [Fact]
    public void MakeNotesCollapsible_removes_existing_hover_rects_before_adding_new_ones()
    {
        var fnBody = ExtractFunctionBody(_script, "makeNotesCollapsible");
        // Should remove old .note-hover-rect elements
        Assert.Contains(".note-hover-rect", fnBody);
        Assert.Matches(@"querySelectorAll\(['""]\.note-hover-rect['""].*forEach.*remove", fnBody);
    }

    // ═══════════════════════════════════════════════════════════
    // Per-container truncation: buildSourceWithNoteStates accepts
    // truncateLines parameter; buildDetailsQueue/buildHeadersQueue
    // use container._truncateLines
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BuildSourceWithNoteStates_accepts_truncateLines_parameter()
    {
        var fnBody = ExtractFunctionBody(_script, "buildSourceWithNoteStates");
        // Should have truncateLines as 5th parameter
        Assert.Matches(@"function buildSourceWithNoteStates\(origSource,\s*noteSteps,\s*noteBlocks,\s*hideHeaders,\s*truncateLines\)", _script);
        // Should use local limit instead of global
        Assert.Contains("var limit = truncateLines || window._truncateLines", fnBody);
    }

    [Fact]
    public void BuildDetailsQueue_uses_per_container_truncateLines()
    {
        var fnBody = ExtractFunctionBody(_script, "buildDetailsQueue");
        Assert.Contains("container._truncateLines", fnBody);
        Assert.Matches(@"isLongNote\(noteBlocks\[i\]\.contentLines,\s*containerLines", fnBody);
    }

    [Fact]
    public void BuildHeadersQueue_uses_per_container_truncateLines()
    {
        var fnBody = ExtractFunctionBody(_script, "buildHeadersQueue");
        Assert.Contains("container._truncateLines", fnBody);
    }

    [Fact]
    public void ProcessRenderQueue_passes_container_truncateLines()
    {
        var fnBody = ExtractFunctionBody(_script, "processRenderQueue");
        Assert.Contains("container._truncateLines", fnBody);
    }

    [Fact]
    public void IsLongNote_accepts_truncateLines_parameter()
    {
        var fnBody = ExtractFunctionBody(_script, "isLongNote");
        Assert.Contains("truncateLines", fnBody);
        Assert.Contains("headersHidden", fnBody);
    }

    [Fact]
    public void SetAllNotes_reads_dropdown_when_switching_to_truncated()
    {
        var fnBody = ExtractFunctionBody(_script, "_setAllNotes");
        // When switching to truncated, should read the scenario's dropdown value
        Assert.Contains(".truncate-lines-select", fnBody);
        Assert.Contains("c._truncateLines = scenarioLines", fnBody);
    }

    // ═══════════════════════════════════════════════════════════
    // Fix: isLongNote excludes gray header lines when
    //       headersHidden is true
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void IsLongNote_accepts_headersHidden_parameter()
    {
        var fnBody = ExtractFunctionBody(_script, "isLongNote");
        // Should accept 3 parameters: contentLines, truncateLines, headersHidden
        Assert.Matches(@"function isLongNote\(contentLines,\s*truncateLines,\s*headersHidden\)", _script);
    }

    [Fact]
    public void IsLongNote_with_headersHidden_excludes_gray_lines()
    {
        var fnBody = ExtractFunctionBody(_script, "isLongNote");
        // When headersHidden is true, should count only non-gray lines
        Assert.Contains("<color:gray>", fnBody);
        Assert.Contains("headersHidden", fnBody);
    }

    [Fact]
    public void IsLongNote_with_headersHidden_skips_blank_after_gray()
    {
        var fnBody = ExtractFunctionBody(_script, "isLongNote");
        // Should skip blank lines that follow gray lines (same as buildSourceWithNoteStates)
        Assert.Matches(@"afterGray.*===\s*''", fnBody);
    }

    [Fact]
    public void IsLongNote_without_headersHidden_returns_early_with_length_check()
    {
        var fnBody = ExtractFunctionBody(_script, "isLongNote");
        // When headersHidden is false/undefined, should use simple length check
        Assert.Contains("contentLines.length > limit", fnBody);
    }

    [Fact]
    public void CreateNoteButtons_passes_headersHidden_to_isLongNote()
    {
        var fnBody = ExtractFunctionBody(_script, "createNoteButtons");
        // Should pass headers-hidden state to isLongNote
        Assert.Matches(@"isLongNote\(contentLines,\s*container\._truncateLines,\s*\w+", fnBody);
    }

    [Fact]
    public void MakeNotesCollapsible_passes_headersHidden_to_isLongNote()
    {
        // All isLongNote calls within the note-collapsible loop should pass owner._headersHidden
        var fnBody = ExtractFunctionBody(_script, "makeNotesCollapsible");
        Assert.Matches(@"isLongNote\(origContentLines,\s*container\._truncateLines,\s*owner\._headersHidden\)", fnBody);
    }

    [Fact]
    public void BuildDetailsQueue_passes_headersHidden_to_isLongNote()
    {
        var fnBody = ExtractFunctionBody(_script, "buildDetailsQueue");
        Assert.Matches(@"isLongNote\(noteBlocks\[i\]\.contentLines,\s*containerLines,\s*\w+", fnBody);
    }

    [Fact]
    public void BuildHeadersQueue_passes_hiding_to_isLongNote()
    {
        var fnBody = ExtractFunctionBody(_script, "buildHeadersQueue");
        // Should pass the `hiding` parameter (the target state) to isLongNote
        Assert.Matches(@"isLongNote\(noteBlocks\[i\]\.contentLines,\s*containerLines,\s*hiding\)", fnBody);
    }

    [Fact]
    public void ProcessRenderQueue_passes_headersHidden_to_isLongNote()
    {
        var fnBody = ExtractFunctionBody(_script, "_preProcessSource");
        // Since 3.0.66 the counted lines are the note's ACTIVE-format lines
        // (YAML when a format preference/default applies); the pin here is
        // that headersHidden still reaches isLongNote at pre-process time.
        Assert.Matches(@"isLongNote\(activeLines,\s*el\._truncateLines,\s*window\._headersHidden\)", fnBody);
        Assert.Contains("activeNoteContentLines(el, i, origNoteBlocks[i].contentLines)", fnBody);
    }

    // ═══════════════════════════════════════════════════════════
    // Helper
    // ═══════════════════════════════════════════════════════════

    private static string ExtractFunctionBody(string content, string functionName)
    {
        // Handle both regular functions and window.X = function(...)
        var idx = content.IndexOf($"function {functionName}(", StringComparison.Ordinal);
        if (idx < 0) idx = content.IndexOf($".{functionName} = function(", StringComparison.Ordinal);
        if (idx < 0) idx = content.IndexOf($"_{functionName} = function(", StringComparison.Ordinal);
        Assert.True(idx >= 0, $"Function '{functionName}' not found in content");

        var braceStart = content.IndexOf('{', idx);
        Assert.True(braceStart >= 0);

        var depth = 0;
        for (var i = braceStart; i < content.Length; i++)
        {
            if (content[i] == '{') depth++;
            else if (content[i] == '}') depth--;
            if (depth == 0) return content[braceStart..(i + 1)];
        }
        throw new Exception($"Unmatched braces in function '{functionName}'");
    }
}
