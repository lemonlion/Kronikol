using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class DiagramContextMenuTests
{
    private readonly string _script = DiagramContextMenu.GetContextMenuScript();

    // ═══════════════════════════════════════════════════════════
    // getBackgroundColor — SVG inline style detection
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void GetBackgroundColor_checks_svg_inline_style_for_background()
    {
        Assert.Contains("svg.getAttribute('style')", _script);
        Assert.Contains("/background\\s*:\\s*([^;]+)/", _script);
    }

    [Fact]
    public void GetBackgroundColor_checks_computed_style()
    {
        Assert.Contains("getComputedStyle(svg).backgroundColor", _script);
    }

    [Fact]
    public void GetBackgroundColor_falls_back_to_rect_fill()
    {
        Assert.Contains("svg.querySelectorAll('rect')", _script);
        Assert.Contains("rect.getAttribute('fill')", _script);
    }

    [Fact]
    public void GetBackgroundColor_skips_rects_with_zero_fill_opacity()
    {
        Assert.Contains("fill-opacity", _script);
        Assert.Contains("parseFloat(fo) === 0", _script);
    }

    [Fact]
    public void GetBackgroundColor_skips_rects_with_8digit_hex_zero_alpha()
    {
        // plantuml-js uses fill="#00000000" (8-digit hex, alpha=00)
        Assert.Contains("fill.slice(7)", _script);
        Assert.Contains("#[0-9a-fA-F]{8}", _script);
    }

    [Fact]
    public void GetBackgroundColor_defaults_to_white()
    {
        Assert.Contains("return '#ffffff'", _script);
    }

    // ═══════════════════════════════════════════════════════════
    // svgToCanvasWithBg — SVG clone + background rect injection
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void SvgToCanvasWithBg_clones_svg_and_injects_background_rect()
    {
        // Should clone the SVG and inject a background rect
        Assert.Contains("svg.cloneNode(true)", _script);
        Assert.Contains("createElementNS", _script);
        Assert.Contains("clone.insertBefore(bgRect, clone.firstChild)", _script);
    }

    [Fact]
    public void SvgToCanvasWithBg_reads_viewBox_for_rect_dimensions()
    {
        // Should read viewBox to get rect dimensions
        Assert.Contains("clone.getAttribute('viewBox')", _script);
        Assert.Contains("bgRect.setAttribute('width', bw)", _script);
        Assert.Contains("bgRect.setAttribute('height', bh)", _script);
    }

    [Fact]
    public void SvgToCanvasWithBg_does_not_use_canvas_fillRect()
    {
        // The new approach uses SVG rect injection, not canvas fillRect for background
        var funcStart = _script.IndexOf("function svgToCanvasWithBg(");
        var funcEnd = _script.IndexOf("function ", funcStart + 1);
        var funcBody = _script.Substring(funcStart, funcEnd - funcStart);
        Assert.DoesNotContain("ctx.fillStyle", funcBody);
        Assert.DoesNotContain("ctx.fillRect", funcBody);
    }

    [Fact]
    public void SvgToCanvasWithBg_serializes_clone_not_original()
    {
        // Should serialize the clone (with injected rect), not the original SVG
        var funcStart = _script.IndexOf("function svgToCanvasWithBg(");
        var funcEnd = _script.IndexOf("function ", funcStart + 1);
        var funcBody = _script.Substring(funcStart, funcEnd - funcStart);
        Assert.Contains("serializeSvg(clone)", funcBody);
    }

    // ═══════════════════════════════════════════════════════════
    // svgToCanvas (transparent) — unchanged, no background fill
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void SvgToCanvas_does_not_fill_background()
    {
        // The transparent variant should NOT have fillRect
        var svgToCanvasStart = _script.IndexOf("function svgToCanvas(");
        var svgToCanvasEnd = _script.IndexOf("function svgToCanvasWithBg(");
        var transparentSection = _script[svgToCanvasStart..svgToCanvasEnd];
        Assert.DoesNotContain("fillRect", transparentSection);
        Assert.DoesNotContain("fillStyle", transparentSection);
    }

    // ═══════════════════════════════════════════════════════════
    // Copy Highlighted Text — SVG selection normalization
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CopyHighlightedText_normalizes_selection_against_note_source()
    {
        // The context menu script should normalize selectedText against
        // the original note source to remove artificial newlines from word-wrap
        Assert.Contains("selChars = selectedText.replace(/\\s+/g, '')", _script);
        Assert.Contains("noteChars = noteSource.replace(/\\s+/g, '')", _script);
        Assert.Contains("noteChars.indexOf(selChars)", _script);
    }

    [Fact]
    public void CopyHighlightedText_uses_noteSource_from_fullNoteText_or_currentNoteText()
    {
        // Should use _currentNoteText for non-expanded notes, _fullNoteText for expanded
        Assert.Contains("_noteIsNotExpanded", _script);
        Assert.Contains("_currentNoteText || _fullNoteText", _script);
    }

    [Fact]
    public void CopyHighlightedText_guards_on_svg_and_clickedNoteIdx()
    {
        // Only normalizes when inside an SVG note (not for HTML text selections)
        Assert.Contains("selectedText && svg && clickedNoteIdx >= 0", _script);
    }

    // ═══════════════════════════════════════════════════════════
    // Collapsible notes script
    // ═══════════════════════════════════════════════════════════

    private readonly string _notesScript = DiagramContextMenu.GetCollapsibleNotesScript();

    private string GetFunction(string name, string? source = null)
    {
        source ??= _notesScript;
        var start = source.IndexOf($"function {name}(");
        if (start < 0) start = source.IndexOf($"window.{name} = function(");
        Assert.True(start >= 0, $"Could not find function {name}");
        // Find the next top-level function or window. assignment after this one
        var searchFrom = start + 10;
        var funcEnd = source.IndexOf("\n    function ", searchFrom);
        var windowEnd = source.IndexOf("\n    window.", searchFrom);
        var ends = new[] { funcEnd, windowEnd }.Where(x => x > 0).ToArray();
        var end = ends.Length > 0 ? ends.Min() : source.Length;
        return source[start..end];
    }

    // ─── buildSourceWithNoteStates ───────────────────────────

    [Fact]
    public void BuildSourceWithNoteStates_expanded_with_hideHeaders_removes_color_gray_lines()
    {
        var funcBody = GetFunction("buildSourceWithNoteStates");
        // When hideHeaders is true and note is in normal (expanded) mode,
        // lines matching <color:gray> should be skipped (setting justSkippedGray)
        Assert.Contains(@"if (inNote && hideHeaders && /^<color:gray>/.test(trimmed)) { justSkippedGray = true; continue; }", funcBody);
    }

    [Fact]
    public void BuildSourceWithNoteStates_expanded_without_hideHeaders_preserves_all_lines()
    {
        var funcBody = GetFunction("buildSourceWithNoteStates");
        // The <color:gray> skip only happens when hideHeaders is true
        // The normal mode path should only skip when hideHeaders is true
        Assert.Contains("hideHeaders", funcBody);
        // Should NOT unconditionally strip <color:gray> lines
        Assert.DoesNotContain("continue; // always strip gray", funcBody);
    }

    [Fact]
    public void BuildSourceWithNoteStates_uses_noteSteps_for_state_not_hideHeaders()
    {
        var funcBody = GetFunction("buildSourceWithNoteStates");
        // Note step determines collapsed/truncated/expanded — not hideHeaders
        Assert.Contains("var step = noteSteps[nIdx]", funcBody);
        Assert.Contains("noteStepState(step)", funcBody);
    }

    [Fact]
    public void BuildSourceWithNoteStates_truncated_mode_also_respects_hideHeaders()
    {
        var funcBody = GetFunction("buildSourceWithNoteStates");
        // In truncated mode, <color:gray> lines should be skippable when hideHeaders is true
        Assert.Contains(@"if (hideHeaders && /^<color:gray>/.test(trimmed)) { justSkippedGray = true; continue; }", funcBody);
    }

    // ─── _preProcessSource ──────────────────────────────────

    [Fact]
    public void PreProcessSource_always_initializes_noteSteps()
    {
        // _preProcessSource must always set _noteSteps for every note,
        // even when detailsDefault is 'expanded'. Without this, buildHeadersQueue
        // would use undefined steps → noteSteps[i] || 0 → 0 (collapsed),
        // causing headers toggle to collapse all notes.
        var funcBody = GetFunction("_preProcessSource");
        Assert.Contains("el._noteSteps", funcBody);
        // Step initialization must happen BEFORE the conditional source transformation,
        // not inside it. Verify _noteSteps assignment is outside the if block.
        var stepsInit = funcBody.IndexOf("el._noteSteps[i] = targetStep");
        var conditional = funcBody.IndexOf("if (state !== 'expanded'");
        Assert.True(stepsInit >= 0, "Should set el._noteSteps[i] = targetStep");
        Assert.True(conditional >= 0, "Should have conditional for source transformation");
        Assert.True(stepsInit < conditional,
            "_noteSteps initialization must happen BEFORE the conditional source transformation");
    }

    [Fact]
    public void PreProcessSource_sets_expanded_step_to_2()
    {
        var funcBody = GetFunction("_preProcessSource");
        // Step 2 = expanded in noteStepState
        Assert.Contains("targetStep = 2", funcBody);
    }

    [Fact]
    public void PreProcessSource_stores_noteOriginalSource()
    {
        var funcBody = GetFunction("_preProcessSource");
        Assert.Contains("el._noteOriginalSource = source", funcBody);
    }

    // ─── buildHeadersQueue ──────────────────────────────────

    [Fact]
    public void BuildHeadersQueue_sets_headersHidden_on_container()
    {
        var funcBody = GetFunction("buildHeadersQueue");
        Assert.Contains("container._headersHidden = hiding", funcBody);
    }

    [Fact]
    public void BuildHeadersQueue_initializes_noteSteps_from_detailsDefault_when_not_set()
    {
        var funcBody = GetFunction("buildHeadersQueue");
        // buildHeadersQueue should initialize noteSteps from window._detailsDefault
        // when container._noteSteps hasn't been set yet, to prevent note collapse
        Assert.Contains("_detailsDefault", funcBody);
        Assert.Contains("isLongNote", funcBody);
    }

    [Fact]
    public void BuildHeadersQueue_skips_container_when_state_unchanged()
    {
        var funcBody = GetFunction("buildHeadersQueue");
        // Should skip containers where the hiding state hasn't changed
        Assert.Contains("if (wasHidden === hiding) return", funcBody);
    }

    // ─── buildDetailsQueue ──────────────────────────────────

    [Fact]
    public void BuildDetailsQueue_propagates_headersHidden_from_global_when_not_set()
    {
        var funcBody = GetFunction("buildDetailsQueue");
        // Details queue should propagate window._headersHidden to containers
        // where _headersHidden hasn't been explicitly set, so re-rendering
        // preserves the report-level headers state
        Assert.Contains("_headersHidden", funcBody);
    }

    [Fact]
    public void BuildDetailsQueue_sets_expanded_step_2_truncated_step_1_collapsed_step_0()
    {
        var funcBody = GetFunction("buildDetailsQueue");
        Assert.Contains("targetStep = 2", funcBody); // expanded
        Assert.Contains("targetStep = 0", funcBody); // collapsed
        Assert.Contains("isLongNote", funcBody); // truncated uses isLongNote check
    }

    // ─── _setReportDetails ──────────────────────────────────

    [Fact]
    public void SetReportDetails_stores_detailsDefault()
    {
        var funcBody = GetFunction("_setReportDetails");
        Assert.Contains("window._detailsDefault = targetState", funcBody);
    }

    // ─── _toggleHeaders ──────────────────────────────────

    [Fact]
    public void ToggleHeaders_stores_headersHidden()
    {
        var funcBody = GetFunction("_toggleHeaders");
        Assert.Contains("window._headersHidden", funcBody);
    }

    [Fact]
    public void ToggleHeaders_does_not_modify_detailsDefault()
    {
        var funcBody = GetFunction("_toggleHeaders");
        Assert.DoesNotContain("_detailsDefault", funcBody);
    }

    // ─── noteStepState ──────────────────────────────────────

    [Fact]
    public void NoteStepState_maps_0_to_collapsed_2_to_expanded()
    {
        var funcBody = GetFunction("noteStepState");
        Assert.Contains("if (step === 0) return 'collapsed'", funcBody);
        Assert.Contains("if (step === 2) return 'expanded'", funcBody);
        Assert.Contains("return 'truncated'", funcBody);
    }

    // ─── processRenderQueue ─────────────────────────────────

    [Fact]
    public void ProcessRenderQueue_passes_headersHidden_to_buildSourceWithNoteStates()
    {
        var funcBody = GetFunction("processRenderQueue");
        Assert.Contains("container._headersHidden", funcBody);
    }

    // ─── syncRadioButtons ───────────────────────────────────

    [Fact]
    public void SyncRadioButtons_scopes_to_data_state_only()
    {
        var funcBody = GetFunction("syncRadioButtons");
        // Must only target buttons with data-state attribute, not headers buttons (data-hstate)
        Assert.Contains("[data-state]", funcBody);
    }

    // ─── Globals ────────────────────────────────────────────

    [Fact]
    public void Globals_headersHidden_defaults_to_false()
    {
        Assert.Contains("window._headersHidden = false", _notesScript);
    }

    [Fact]
    public void Globals_truncateLines_defaults_to_40()
    {
        Assert.Contains("window._truncateLines = 40", _notesScript);
    }

    [Fact]
    public void Globals_detailsDefault_defaults_to_truncated()
    {
        Assert.Contains("window._detailsDefault = 'truncated'", _notesScript);
    }

    // ─── createNoteButtons ──────────────────────────────────

    [Fact]
    public void CreateNoteButtons_expand_button_is_3x_width()
    {
        var funcBody = GetFunction("createNoteButtons");
        Assert.Contains("size * 3", funcBody);
    }

    [Fact]
    public void CreateNoteButtons_shows_contract_for_expanded_and_truncated()
    {
        var funcBody = GetFunction("createNoteButtons");
        // Contract button shown for expanded or truncated
        Assert.Contains("state === 'expanded' || state === 'truncated'", funcBody);
    }

    [Fact]
    public void CreateNoteButtons_shows_expand_for_collapsed_and_truncated()
    {
        var funcBody = GetFunction("createNoteButtons");
        // Expand button shown for collapsed or truncated
        Assert.Contains("state === 'collapsed' || state === 'truncated'", funcBody);
    }

    [Fact]
    public void CreateNoteButtons_all_buttons_are_hover_only()
    {
        var funcBody = GetFunction("createNoteButtons");
        // All buttons start with opacity 0 (hover only)
        Assert.DoesNotContain("opacity = '0.6'", funcBody);
        Assert.Contains("opacity = '0'", funcBody);
    }

    // ─── createNoteButtons — arrows and double-click ────────

    [Fact]
    public void CreateNoteButtons_expand_uses_large_downward_arrow()
    {
        var funcBody = GetFunction("createNoteButtons");
        // Bottom expand button uses ▼ (large downward triangle)
        Assert.Contains("\\u25BC", funcBody);
        Assert.DoesNotContain("\\u25BE", funcBody); // not small ▾
        Assert.DoesNotContain("textContent = '+'", funcBody);
    }

    [Fact]
    public void CreateNoteButtons_bottom_contract_uses_large_upward_arrow()
    {
        var funcBody = GetFunction("createNoteButtons");
        // Bottom-center ▲ contract button shown when expanded and long note
        Assert.Contains("state === 'expanded' && longNote", funcBody);
        Assert.Contains("\\u25B2", funcBody); // ▲ large
    }

    [Fact]
    public void CreateNoteButtons_top_right_arrow_for_expanded_long_notes()
    {
        var funcBody = GetFunction("createNoteButtons");
        // For expanded long notes: ▴ arrow appears to the left of −
        // The ▴ block uses offset: topSize * 2 + pad * 2
        Assert.Contains("bbox.x + bbox.width - topSize * 2 - pad * 2", funcBody);
    }

    [Fact]
    public void CreateNoteButtons_double_click_cycles_state()
    {
        var funcBody = GetFunction("createNoteButtons");
        // dblclick on note background paths cycles state
        Assert.Contains("dblclick", funcBody);
        Assert.Contains("onCycle", funcBody);
        // Uses note group paths for hover detection and dblclick
        Assert.Contains("grp.paths.forEach", funcBody);
        Assert.Contains("grp.texts.forEach", funcBody);
        // Delayed hide for smooth hover across gaps between note sub-elements
        Assert.Contains("_noteScheduleHide", funcBody);
    }

    [Fact]
    public void CreateNoteButtons_accepts_onTruncate_and_onCycle_parameters()
    {
        var funcBody = GetFunction("createNoteButtons");
        // Function signature includes onTruncate, onCycle, and grp parameters
        Assert.Contains("onExpand, onContract, onTruncate, onCycle, contentLines, grp", funcBody);
    }

    [Fact]
    public void CreateNoteButtons_top_right_arrow_calls_onTruncate()
    {
        var funcBody = GetFunction("createNoteButtons");
        // The top-right ▴ arrow on expanded long notes calls onTruncate, not onContract
        // Find the block for the ▴ arrow (uses bgA click handler)
        Assert.Contains("onTruncate()", funcBody);
    }

    [Fact]
    public void CreateNoteButtons_bottom_contract_calls_onTruncate()
    {
        var funcBody = GetFunction("createNoteButtons");
        // The bottom ▲ on expanded long notes calls onTruncate, not onContract
        // bgBC click handler should call onTruncate
        var bottomBlock = funcBody.Substring(funcBody.IndexOf("\\u25B2"));
        Assert.Contains("onTruncate()", bottomBlock);
    }

    [Fact]
    public void CreateNoteButtons_minus_button_calls_onContract()
    {
        var funcBody = GetFunction("createNoteButtons");
        // The − (minus) button calls onContract for full collapse
        Assert.Contains("onContract()", funcBody);
    }

    [Fact]
    public void CreateNoteButtons_minus_button_uses_svg_line()
    {
        var funcBody = GetFunction("createNoteButtons");
        // The minus button uses an SVG line element instead of text
        Assert.Contains("createElementNS(SVGNS, 'line')", funcBody);
        Assert.Contains("stroke-linecap", funcBody);
    }

    // ─── createNoteButtons — hover detection rect ───────────

    [Fact]
    public void CreateNoteButtons_adds_transparent_hover_rect()
    {
        var funcBody = GetFunction("createNoteButtons");
        // Transparent rect covers the note bounding box for reliable hover detection
        Assert.Contains("note-hover-rect", funcBody);
        Assert.Contains("fill', 'transparent'", funcBody);
        Assert.Contains("stroke', 'none'", funcBody);
    }

    [Fact]
    public void CreateNoteButtons_hover_rect_uses_note_bbox()
    {
        var funcBody = GetFunction("createNoteButtons");
        // hoverRect positioned using the note's bounding box
        Assert.Contains("hoverRect.setAttribute('x', bbox.x)", funcBody);
        Assert.Contains("hoverRect.setAttribute('y', bbox.y)", funcBody);
        Assert.Contains("hoverRect.setAttribute('width', bbox.width)", funcBody);
        Assert.Contains("hoverRect.setAttribute('height', bbox.height)", funcBody);
    }

    [Fact]
    public void CreateNoteButtons_hover_rect_captures_all_pointer_events()
    {
        var funcBody = GetFunction("createNoteButtons");
        // pointer-events: all ensures the rect captures hover even with overlapping SVG elements
        Assert.Contains("hoverRect.style.pointerEvents = 'all'", funcBody);
    }

    [Fact]
    public void CreateNoteButtons_hover_rect_has_mouseenter_and_mouseleave()
    {
        var funcBody = GetFunction("createNoteButtons");
        // hoverRect wired to the same show/hide functions as path/text elements
        Assert.Contains("hoverRect.addEventListener('mouseenter', _noteShowButtons)", funcBody);
        Assert.Contains("hoverRect.addEventListener('mouseleave', _noteScheduleHide)", funcBody);
    }

    [Fact]
    public void CreateNoteButtons_hover_rect_supports_dblclick_cycle()
    {
        var funcBody = GetFunction("createNoteButtons");
        // hoverRect also handles dblclick for toggle-cycling
        Assert.Contains("hoverRect.addEventListener('dblclick'", funcBody);
    }

    [Fact]
    public void CreateNoteButtons_hover_rect_inserted_inside_mainG_before_note_paths()
    {
        var funcBody = GetFunction("createNoteButtons");
        // hoverRect must be inserted inside mainG before the note's first path element.
        // This keeps text elements (which follow paths in SVG order) on top of the
        // hoverRect, allowing native text selection. If the hoverRect were appended
        // to the SVG root it would sit above mainG and block all pointer events to text.
        Assert.Contains("grp.paths[0].parentNode.insertBefore(hoverRect, grp.paths[0])", funcBody);
        Assert.DoesNotContain("svg.appendChild(hoverRect)", funcBody);
    }

    [Fact]
    public void CreateNoteButtons_buttons_appended_to_svg_root()
    {
        var funcBody = GetFunction("createNoteButtons");
        // Buttons must be appended to the SVG root so they render on top of everything
        Assert.Contains("svg.appendChild(b)", funcBody);
    }

    [Fact]
    public void CreateNoteButtons_hide_timeout_is_300ms()
    {
        var funcBody = GetFunction("createNoteButtons");
        // 300ms delay gives enough time for mouse to cross element boundaries
        Assert.Contains("}, 300)", funcBody);
        Assert.DoesNotContain("}, 100)", funcBody);
    }

    // ─── makeNotesCollapsible — double-click cycle logic ────

    [Fact]
    public void MakeNotesCollapsible_passes_cycle_callback()
    {
        var funcBody = GetFunction("makeNotesCollapsible");
        // The cycle callback calculates next step based on current step
        Assert.Contains("curStep === 2", funcBody);
        Assert.Contains("curStep === 1", funcBody);
        Assert.Contains("long ? 1 : 0", funcBody);
    }

    [Fact]
    public void MakeNotesCollapsible_passes_onTruncate_to_state_1()
    {
        var funcBody = GetFunction("makeNotesCollapsible");
        // onTruncate callback sets state to 1 (truncated)
        Assert.Contains("setNoteState(owner, globalIdx, 1)", funcBody);
    }

    // ─── buildSourceWithNoteStates — trailing space fix ─────

    [Fact]
    public void BuildSourceWithNoteStates_skips_blank_lines_after_hidden_headers()
    {
        var funcBody = GetFunction("buildSourceWithNoteStates");
        // justSkippedGray tracking removes blank lines left after hiding <color:gray> lines
        Assert.Contains("justSkippedGray", funcBody);
        Assert.Contains("justSkippedGray && trimmed === ''", funcBody);
    }

    // ─── setNoteState — performance ─────────────────────────

    [Fact]
    public void SetNoteState_short_circuits_unchanged_step()
    {
        var funcBody = GetFunction("setNoteState");
        // Should return early if step hasn't changed
        Assert.Contains("container._noteSteps[noteIdx] === targetStep", funcBody);
    }

    [Fact]
    public void SetNoteState_uses_svg_cache()
    {
        var funcBody = GetFunction("setNoteState");
        Assert.Contains("_svgCache[newSource]", funcBody);
    }

    [Fact]
    public void SetNoteState_caches_rendered_svg()
    {
        var funcBody = GetFunction("setNoteState");
        Assert.Contains("_svgCache[newSource] = newSvg", funcBody);
    }

    // ─── processRenderQueue — performance ───────────────────

    [Fact]
    public void ProcessRenderQueue_uses_svg_cache()
    {
        var funcBody = GetFunction("processRenderQueue");
        Assert.Contains("_svgCache[newSource]", funcBody);
    }

    [Fact]
    public void ProcessRenderQueue_caches_rendered_svg()
    {
        var funcBody = GetFunction("processRenderQueue");
        Assert.Contains("_svgCache[newSource] = newSvg", funcBody);
    }

    // ─── buildDetailsQueue — force parameter ────────────────

    [Fact]
    public void BuildDetailsQueue_accepts_force_parameter()
    {
        var funcBody = GetFunction("buildDetailsQueue");
        Assert.Contains("force", funcBody);
        Assert.Contains("needsUpdate || force", funcBody);
    }

    // ─── _setTruncateLines ──────────────────────────────────

    [Fact]
    public void SetTruncateLines_passes_force_true()
    {
        var funcBody = GetFunction("_setTruncateLines");
        Assert.Contains("'truncated', true", funcBody);
    }

    // ─── _setScenarioTruncateLines ──────────────────────────

    [Fact]
    public void SetScenarioTruncateLines_exists_and_scopes_to_scenario()
    {
        var funcBody = GetFunction("_setScenarioTruncateLines");
        Assert.Contains("sel.closest('details.scenario')", funcBody);
    }

    [Fact]
    public void SetScenarioTruncateLines_does_not_sync_all_dropdowns()
    {
        var funcBody = GetFunction("_setScenarioTruncateLines");
        // Scenario-level should NOT sync all dropdowns — only report-level does
        Assert.DoesNotContain(".truncate-lines-select", funcBody);
    }

    [Fact]
    public void SetScenarioTruncateLines_stores_per_container_truncate_lines()
    {
        var funcBody = GetFunction("_setScenarioTruncateLines");
        // Should store the value on each container rather than temporarily overriding global
        Assert.Contains("c._truncateLines = scenarioLines", funcBody);
        Assert.DoesNotContain("var prev = window._truncateLines", funcBody);
    }

    [Fact]
    public void SetScenarioTruncateLines_passes_force_true()
    {
        var funcBody = GetFunction("_setScenarioTruncateLines");
        Assert.Contains("'truncated', true", funcBody);
    }

    // ─── SVG cache global ───────────────────────────────────

    [Fact]
    public void SvgCache_is_declared()
    {
        Assert.Contains("var _svgCache = {}", _notesScript);
    }

    // ─── GetCollapsibleNotesStyles ──────────────────────────

    [Fact]
    public void CollapsibleNotesStyles_contains_radio_button_css()
    {
        var css = DiagramContextMenu.GetCollapsibleNotesStyles();
        Assert.Contains(".details-radio-btn", css);
        Assert.Contains(".details-active", css);
        Assert.Contains(".toggle-btn", css);
        Assert.Contains(".truncate-lines-select", css);
    }

    [Fact]
    public void CollapsibleNotesStyles_note_toggle_icon_user_select_none()
    {
        var css = DiagramContextMenu.GetCollapsibleNotesStyles();
        Assert.Contains(".note-toggle-icon", css);
        Assert.Contains("user-select: none", css);
    }

    // ─── getNotePreview — empty note handling ───────────────

    [Fact]
    public void GetNotePreview_returns_empty_string_for_empty_content()
    {
        var funcBody = GetFunction("getNotePreview");
        // When all content lines are empty (or only gray), the preview should be
        // empty — not '...' — so collapsed notes aren't bigger than expanded ones
        Assert.Contains("if (!raw) return '';", funcBody);
        Assert.DoesNotContain("if (!raw) return '...';", funcBody);
    }

    [Fact]
    public void BuildSourceWithNoteStates_skips_empty_preview_for_collapsed_notes()
    {
        var funcBody = GetFunction("buildSourceWithNoteStates");
        // When a collapsed note has an empty preview, don't push it into the output
        // (avoids an empty line between "note left" and "end note")
        Assert.Contains("if (preview)", funcBody);
    }

    // ═══════════════════════════════════════════════════════════
    // Context menu styles — submenu hover uses direct child selectors
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ContextMenu_styles_use_direct_child_selectors_for_hover()
    {
        var css = DiagramContextMenu.GetStyles();
        // Must use > div:hover to avoid highlighting the submenu container itself
        Assert.Contains("> div:hover", css);
        Assert.DoesNotContain(".diagram-ctx-menu div:hover", css);
    }

    [Fact]
    public void ContextMenu_submenu_styles_use_direct_child_selectors_for_hover()
    {
        var css = DiagramContextMenu.GetStyles();
        Assert.Contains(".submenu > div:hover", css);
        Assert.DoesNotContain(".submenu div:hover", css);
    }

    // ═══════════════════════════════════════════════════════════
    // Inline SVG styles — floating zoom button visibility
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ZoomControls_hidden_by_default_shown_on_container_hover()
    {
        var css = DiagramContextMenu.GetInlineSvgStyles();
        // Controls should be invisible by default
        Assert.Contains("opacity: 0", css);
        Assert.Contains("pointer-events: none", css);
        // Container hover reveals the controls
        Assert.Contains("[data-diagram-type]:hover > .diagram-zoom-controls", css);
    }

    // ═══════════════════════════════════════════════════════════
    // Zoom toggle — drag-to-pan and overflow
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ZoomToggle_sets_overflow_x_auto_when_zoomed()
    {
        Assert.Contains("container.style.overflowX = 'auto'", _script);
        Assert.Contains("container.style.cursor = 'grab'", _script);
    }

    [Fact]
    public void ZoomToggle_clears_overflow_x_when_unzoomed()
    {
        Assert.Contains("container.style.overflowX = ''", _script);
        Assert.Contains("container.style.cursor = ''", _script);
    }

    [Fact]
    public void ZoomToggle_has_drag_to_pan_handlers()
    {
        // Must have mousedown/mousemove/mouseup for drag panning
        Assert.Contains("cursor = 'grabbing'", _script);
        Assert.Contains("scrollLeft = scrollL - (e.pageX - startX)", _script);
        Assert.Contains("window.scrollBy(0, startY - e.clientY)", _script);
    }

    [Fact]
    public void ZoomButton_is_prepended_not_appended()
    {
        Assert.Contains("container.prepend(controls)", _script);
        Assert.DoesNotContain("container.appendChild(controls)", _script);
    }

    [Fact]
    public void ZoomObserver_setup_deferred_to_DOMContentLoaded()
    {
        // Zoom buttons are lazily added via IntersectionObserver so we avoid
        // forced reflows on page load. A per-container MutationObserver waits
        // for the SVG to render before checking isDiagramZoomable.
        Assert.Contains("new IntersectionObserver(", _script);
        Assert.Contains("addZoomButton(container)", _script);
        Assert.Contains("addEventListener('DOMContentLoaded', observeAll)", _script);
    }

    [Fact]
    public void ZoomButton_not_added_when_diagram_fits_container()
    {
        // addZoomButtons should check isDiagramZoomable before adding a button
        Assert.Contains("function isDiagramZoomable(container)", _script);
        Assert.Contains("if (!isDiagramZoomable(container)) return", _script);
    }

    [Fact]
    public void ZoomControls_skipped_for_non_zoomable_diagrams()
    {
        // Zoom controls should check isDiagramZoomable
        Assert.Contains("!isDiagramZoomable(container)", _script);
        Assert.Contains("diagram-natural-size", _script);
    }

    // ═══════════════════════════════════════════════════════════
    // Mobile — zoom controls hidden, context menu bottom-sheet
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ZoomControls_hidden_on_mobile_via_media_query()
    {
        var css = DiagramContextMenu.GetInlineSvgStyles();
        Assert.Contains("@media (max-width: 768px)", css);
        Assert.Contains(".diagram-zoom-controls", css);
        Assert.Contains("display: none !important", css);
    }

    [Fact]
    public void ContextMenu_mobile_styles_render_as_bottom_sheet()
    {
        var css = DiagramContextMenu.GetStyles();
        Assert.Contains("@media (max-width: 768px)", css);
        Assert.Contains("bottom: 0 !important", css);
        Assert.Contains("top: auto !important", css);
        Assert.Contains("border-radius: 12px 12px 0 0", css);
    }

    [Fact]
    public void ContextMenu_mobile_items_have_larger_tap_targets()
    {
        var css = DiagramContextMenu.GetStyles();
        Assert.Contains("padding: 12px 20px", css);
        Assert.Contains("font-size: 15px", css);
    }

    [Fact]
    public void ContextMenu_submenu_click_handler_for_mobile()
    {
        Assert.Contains("max-width: 768px", _script);
        Assert.Contains("sub.style.display", _script);
    }

    [Fact]
    public void ContextMenu_positioning_skipped_on_mobile()
    {
        // On mobile, the CSS bottom-sheet positioning takes over
        Assert.Contains("!window.matchMedia('(max-width: 768px)').matches", _script);
    }

    // ═══════════════════════════════════════════════════════════
    // PlantUML CDN — deferred loading
    // ═══════════════════════════════════════════════════════════

    private readonly string _plantUmlScript = DiagramContextMenu.GetPlantUmlBrowserRenderScript();

    [Fact]
    public void PlantUml_engine_is_fetched_into_workers_not_loaded_by_script_tags()
    {
        // The engine (7 MB) used to be two <script defer> tags — the page was not interactive until it had
        // run. Now the shim fetches it as text and builds Blob workers from it; script tags are only the
        // fallback the bootstrap injects itself when Workers/fetch are unavailable.
        Assert.DoesNotContain("<script defer src=", _plantUmlScript);
        Assert.Contains("fetch(", _plantUmlScript);
        Assert.Contains("new Worker(", _plantUmlScript);
        Assert.Contains("URL.createObjectURL(new Blob(", _plantUmlScript);
        Assert.Contains("https://cdn.jsdelivr.net/gh/lemonlion/plantuml-js-plantuml_limit_size_98304@v1.2026.3beta6-patched/viz-global.js", _plantUmlScript);
        Assert.Contains("https://cdn.jsdelivr.net/gh/lemonlion/plantuml-js-plantuml_limit_size_98304@v1.2026.3beta6-patched/plantuml.js", _plantUmlScript);
    }

    [Fact]
    public void Worker_bootstrap_is_registered_before_the_DOMContentLoaded_handler()
    {
        // The shim (window.plantuml, window.plantumlLoad, window.__kronikolRender) must exist before the
        // DOMContentLoaded handler that starts rendering runs.
        var bootstrapIndex = _plantUmlScript.IndexOf("window.__kronikolRender");
        var dclIndex = _plantUmlScript.IndexOf("DOMContentLoaded");
        Assert.True(bootstrapIndex >= 0 && dclIndex >= 0);
        Assert.True(bootstrapIndex < dclIndex, "worker bootstrap should be registered before the DOMContentLoaded handler");
        // plantumlLoad() stays a callable no-op for anything that still calls it; the shim owns the engine.
        Assert.Contains("window.plantumlLoad = function", _plantUmlScript);
        Assert.Contains("window.plantuml = ", _plantUmlScript);
        Assert.Contains("prefetch:", _plantUmlScript);
    }

    [Fact]
    public void Render_script_bakes_in_worker_cache_and_fragment_settings()
    {
        var script = DiagramContextMenu.GetPlantUmlBrowserRenderScript(2, 16, 6000);
        Assert.Contains("var WORKERS_REQUESTED = 2;", script);
        Assert.Contains("var CACHE_MEGABYTES = 16;", script);
        Assert.Contains("var _maxDiagramHeight = 6000;", script);
        Assert.DoesNotContain("__BROWSER_", script);
        Assert.DoesNotContain("__PLANTUML_", script);
    }

    [Fact]
    public void Render_script_defaults_match_TrackingDefaults()
    {
        Assert.Contains("var WORKERS_REQUESTED = 4;", _plantUmlScript);
        Assert.Contains("var CACHE_MEGABYTES = 64;", _plantUmlScript);
        Assert.Contains("var _maxDiagramHeight = 12000;", _plantUmlScript);
    }

    [Fact]
    public void Zero_workers_and_negative_values_select_the_main_thread_engine()
    {
        Assert.Contains("var WORKERS_REQUESTED = 0;", DiagramContextMenu.GetPlantUmlBrowserRenderScript(0, 64, 12000));
        Assert.Contains("var WORKERS_REQUESTED = 0;", DiagramContextMenu.GetPlantUmlBrowserRenderScript(-3, 64, 12000));
        // A non-positive fragment height is meaningless — the default stands.
        Assert.Contains("var _maxDiagramHeight = 12000;", DiagramContextMenu.GetPlantUmlBrowserRenderScript(4, 64, 0));
    }

    [Fact]
    public void Worker_host_is_inlined_as_a_JavaScript_string_that_cannot_close_the_script_tag()
    {
        // The host source is a JSON string literal inside the render script; `<` is escaped so the
        // inlined source can never contain a literal `</script>`, and the page has exactly its own tags.
        Assert.Contains("var WORKER_HOST_SOURCE = \"", _plantUmlScript);
        Assert.Contains("__kronikolWorkerHost", _plantUmlScript);
        var opens = System.Text.RegularExpressions.Regex.Matches(_plantUmlScript, "<script").Count;
        var closes = System.Text.RegularExpressions.Regex.Matches(_plantUmlScript, "</script>").Count;
        Assert.Equal(opens, closes);
        Assert.Equal(2, opens);
    }

    [Fact]
    public void Render_queue_keeps_several_fragments_in_flight_and_refills_its_slots()
    {
        // The queue used to be a boolean lock (one render at a time). With workers it is a counter
        // bounded by the shim's maxParallel, refilled after every dispatch so all worker slots fill.
        Assert.Contains("inFlight >= maxParallel()", _plantUmlScript);
        Assert.Contains("setTimeout(processQueue, 0)", _plantUmlScript);
        Assert.Contains("window._plantumlRendering = inFlight > 0", _plantUmlScript);
        Assert.DoesNotContain("var rendering = false;", _plantUmlScript);
    }

    [Fact]
    public void Render_failures_from_the_shim_take_the_same_path_as_a_synchronous_engine_throw()
    {
        // A worker error arrives asynchronously; processQueue hands the shim an onError so the too-large
        // re-split retry and the "Raw PlantUML" failure block behave exactly as for a synchronous throw.
        Assert.Contains("{ onError: handleRenderFailure }", _plantUmlScript);
        Assert.Contains("function handleRenderFailure(", _plantUmlScript);
        Assert.Contains("Diagram too large for client-side rendering.", _plantUmlScript);
        Assert.Contains("<summary>Raw PlantUML</summary>", _plantUmlScript);
    }

    [Fact]
    public void Shim_falls_back_to_script_tags_when_workers_or_fetch_are_unavailable()
    {
        Assert.Contains("function useMainThreadEngine(", _plantUmlScript);
        Assert.Contains("typeof Worker", _plantUmlScript);
        Assert.Contains("typeof OffscreenCanvas", _plantUmlScript);
        Assert.Contains("telemetry.mode = 'main-thread'", _plantUmlScript);
        Assert.Contains("'BrowserRenderWorkers = '", _plantUmlScript);
    }

    // ═══════════════════════════════════════════════════════════
    // Collapsible notes — re-render paths use the shim's cache/prefetch
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Note_toggle_paths_prefetch_their_new_fragments_in_parallel()
    {
        // setNoteState (fragQueue) and processRenderQueue (fragList) both build the list of new fragments
        // and then render them one by one; prefetching the list first lets the workers render them in
        // parallel so the sequential loop is a series of cache hits.
        Assert.Contains("window.plantuml.prefetch(fragQueue.map(function (f) { return f.source; }))", _notesScript);
        Assert.Contains("window.plantuml.prefetch(fragList.map(function (f) { return f.source; }))", _notesScript);
    }

    [Fact]
    public void SetNoteState_fragment_render_completes_on_engine_text_and_never_caches_a_failure()
    {
        // processRenderQueue already treated any engine output (svg OR failure text) as completion and
        // refused to cache a failure; setNoteState's fragment loop waited for an <svg> only, so a
        // too-large fragment (text, no svg) or a shim error left _noteRendering stuck forever.
        var start = _notesScript.IndexOf("function setNoteState(");
        var end = _notesScript.IndexOf("function processRenderQueue(");
        var setNoteState = _notesScript.Substring(start, end - start);
        Assert.Contains("if (!item.el.querySelector('svg') && !(item.el.textContent || '').trim()) return;", setNoteState);
        Assert.Contains("window._describeEngineFailure(item.el, item.source)", setNoteState);
        Assert.Contains("if (!failed && item.el.querySelector('svg')) _svgCache[item.source] = item.el.innerHTML;", setNoteState);
        Assert.Contains("Render timed out for this fragment.", setNoteState);
    }

    // ═══════════════════════════════════════════════════════════
    // PlantUML browser — loading placeholder
    // ═══════════════════════════════════════════════════════════

    private readonly string _inlineSvgStyles = DiagramContextMenu.GetInlineSvgStyles();

    [Fact]
    public void Plantuml_browser_shows_loading_placeholder_before_render()
    {
        // CSS pseudo-element on unrendered containers shows two states:
        // 1. Before page load: "Waiting for page load to complete..."
        // 2. After page load (body.plantuml-ready): "Rendering diagram..."
        Assert.Contains(".plantuml-browser:not([data-rendered])::before", _inlineSvgStyles);
        Assert.Contains("Waiting for page", _inlineSvgStyles);
        Assert.Contains("body.plantuml-ready .plantuml-browser:not([data-rendered])::before", _inlineSvgStyles);
        Assert.Contains("Rendering diagram", _inlineSvgStyles);
    }

    // ═══════════════════════════════════════════════════════════
    // Open in new tab — uses blob URLs not data URIs
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void OpenInNewTab_uses_blob_url_not_data_uri()
    {
        // PNG open should use createObjectURL, not FileReader + readAsDataURL
        Assert.DoesNotContain("readAsDataURL", _script);
        // SVG "Open in new tab" should not use window.open('data:image/svg+xml...')
        // (data:image/svg+xml;base64 still exists in svgToCanvas for img.src, which is fine)
        Assert.DoesNotContain("window.open('data:image/svg+xml", _script);
    }

    [Fact]
    public void OpenInNewTab_uses_createObjectURL_for_images()
    {
        // The "Open image in new tab" submenu items should use URL.createObjectURL
        Assert.Contains("window.open(URL.createObjectURL(blob))", _script);
    }

    // ═══════════════════════════════════════════════════════════
    // Copy box text — submenu for truncated/collapsed notes
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CopyBoxText_shows_submenu_when_note_is_not_expanded()
    {
        // When the note has a noteStep and is not expanded (step !== 2),
        // it should show a submenu with full and current options
        Assert.Contains("Copy full box text", _script);
        Assert.Contains("Copy current box text", _script);
    }

    [Fact]
    public void CopyBoxText_shows_simple_menu_when_note_is_expanded()
    {
        // When the note is expanded or has no noteStep tracking,
        // it should show a simple "Copy box text" item
        Assert.Contains("Copy box text", _script);
    }

    [Fact]
    public void CopyBoxText_checks_noteSteps_for_expanded_state()
    {
        // Should check container._noteSteps for whether note is expanded
        Assert.Contains("_noteSteps", _script);
    }

    [Fact]
    public void CopyBoxText_current_uses_visible_text_from_current_source()
    {
        // The "current" copy should use the current (potentially truncated) source
        // not the original source
        Assert.Contains("currentNoteBlocks", _script);
    }

    // ═══════════════════════════════════════════════════════════
    // Open box text in new tab
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void OpenBoxText_in_new_tab_exists()
    {
        Assert.Contains("Open box text in new tab", _script);
    }

    [Fact]
    public void OpenBoxText_shows_submenu_when_note_is_not_expanded()
    {
        Assert.Contains("Open full box text in new tab", _script);
        Assert.Contains("Open current box text in new tab", _script);
    }

    // ═══════════════════════════════════════════════════════════
    // Rename "original" to "full" in source menus
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Source_copy_menu_uses_full_not_original()
    {
        Assert.Contains("Copy full ", _script);
        Assert.DoesNotContain("Copy original ", _script);
    }

    [Fact]
    public void Source_open_menu_uses_full_not_original()
    {
        Assert.Contains("Open full ", _script);
        Assert.DoesNotContain("Open original ", _script);
    }
}
