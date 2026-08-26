using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// E2E interaction tests for the note payload JSON ⇄ YAML hover toggle:
/// button eligibility, toggling both ways, and state survival across the
/// note-state, header and filter machinery. See NOTE_YAML_TOGGLE_PLAN.md.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteYamlToggleTests : DiagramNotePlaywrightBase
{
    public NoteYamlToggleTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task NavigateAndSetup(string fileName)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithJsonYamlNotes(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    private async Task WaitForFormatButtonVisible()
    {
        await Page.WaitForFunctionAsync("""
            () => {
                var btns = document.querySelectorAll('[data-note-btn="format"]');
                return Array.from(btns).some(b => b.style.display !== 'none' && b.style.opacity === '1');
            }
        """, null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    private async Task ClickFormatButton()
    {
        var htmlBefore = await GetSvgHtml();
        await HoverNoteRect(0);
        await WaitForFormatButtonVisible();
        await Page.EvaluateAsync("""
            () => {
                var btn = Array.from(document.querySelectorAll('[data-note-btn="format"]'))
                    .find(b => b.style.display !== 'none' && b.style.opacity === '1');
                btn.querySelector('rect').dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true}));
            }
        """);
        await WaitForSvgReRender(htmlBefore);
    }

    private async Task<string> GetNormalizedSvgText()
    {
        var text = await Page.Locator("[data-diagram-type='plantuml'] svg").First
            .EvaluateAsync<string>("el => el.textContent");
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
    }

    // ═══════════════════════════════════════════════════════════
    // Button eligibility
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Format_button_appears_on_hover_for_json_note()
    {
        await NavigateAndSetup("YamlToggle_ButtonAppears.html");
        await HoverNoteRect(0);
        await WaitForFormatButtonVisible();

        var glyph = await Page.EvaluateAsync<string>("""
            () => Array.from(document.querySelectorAll('[data-note-btn="format"]'))
                .find(b => b.style.display !== 'none' && b.style.opacity === '1')
                .querySelector('text').textContent
        """);
        Assert.Equal("Y", glyph);
    }

    [Fact]
    public async Task Format_button_does_not_appear_for_plain_text_note()
    {
        await NavigateAndSetup("YamlToggle_NoButtonPlainText.html");
        // Note index 1 is the plain-text note
        await HoverNoteRect(1);

        // The hover must have taken effect (its minus button shows) …
        await Page.WaitForFunctionAsync("""
            () => Array.from(document.querySelectorAll('[data-note-btn="minus"]'))
                .some(b => b.style.opacity === '1')
        """, null, new() { Timeout = 10000, PollingInterval = 200 });

        // … but no format button becomes visible for the ineligible note.
        var visibleFormatButtons = await Page.EvaluateAsync<int>("""
            () => Array.from(document.querySelectorAll('[data-note-btn="format"]'))
                .filter(b => b.style.display !== 'none' && b.style.opacity === '1').length
        """);
        Assert.Equal(0, visibleFormatButtons);
    }

    // ═══════════════════════════════════════════════════════════
    // Toggling
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Click_renders_note_as_yaml_with_multiline_sql_visible()
    {
        await NavigateAndSetup("YamlToggle_ToYaml.html");
        await ClickFormatButton();

        var text = await GetNormalizedSvgText();
        Assert.Contains("query: |-", text);
        Assert.Contains("SELECT o.id,", text);
        Assert.Contains("FROM orders o", text);
        Assert.Contains("id: 9007199254740993", text);
        // The literal \n escapes of the JSON view are gone
        Assert.DoesNotContain("SELECT o.id,\\n", text);
    }

    [Fact]
    public async Task Yaml_block_scalar_lines_render_as_separate_text_lines()
    {
        await NavigateAndSetup("YamlToggle_SeparateLines.html");
        await ClickFormatButton();

        // "FROM orders o" must be its own rendered line (the SVG copy-text
        // feature hands readers exactly these lines)
        var isOwnLine = await Page.EvaluateAsync<bool>("""
            () => Array.from(document.querySelectorAll('[data-diagram-type="plantuml"] svg text'))
                .some(t => t.textContent.trim() === 'FROM orders o')
        """);
        Assert.True(isOwnLine, "'FROM orders o' should render as its own line in YAML view");
    }

    [Fact]
    public async Task Toggle_back_restores_exact_json_view()
    {
        await NavigateAndSetup("YamlToggle_BackToJson.html");
        var textBefore = await GetNormalizedSvgText();

        await ClickFormatButton();
        Assert.NotEqual(textBefore, await GetNormalizedSvgText());

        // Button now shows 'J'
        await HoverNoteRect(0);
        await WaitForFormatButtonVisible();
        var glyph = await Page.EvaluateAsync<string>("""
            () => Array.from(document.querySelectorAll('[data-note-btn="format"]'))
                .find(b => b.style.display !== 'none' && b.style.opacity === '1')
                .querySelector('text').textContent
        """);
        Assert.Equal("J", glyph);

        await ClickFormatButton();

        // The original note lines are restored from _noteOriginalSource —
        // the rendered text is byte-identical to the pre-toggle view.
        var text = await GetNormalizedSvgText();
        Assert.Equal(textBefore, text);
        Assert.DoesNotContain("query: |-", text);
    }

    // ═══════════════════════════════════════════════════════════
    // State survival
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Yaml_state_survives_collapse_and_expand()
    {
        await NavigateAndSetup("YamlToggle_SurvivesCollapse.html");
        await ClickFormatButton();

        await ClickNoteButton("[data-note-btn='minus']");   // collapse
        await ClickNoteButton("[data-note-btn='plus']");    // expand again

        var text = await GetNormalizedSvgText();
        Assert.Contains("query: |-", text);
    }

    [Fact]
    public async Task Yaml_state_survives_header_hide_toggle()
    {
        await NavigateAndSetup("YamlToggle_SurvivesHeaderHide.html");
        await ClickFormatButton();

        var scenario = Page.Locator("details.scenario");
        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await scenario.Locator(".toggle-btn[data-toggle='headers'][data-shown='true']").ClickAsync();
        await Page.WaitForFunctionAsync(
            "(prev) => !window._plantumlRendering && (window._renderCompleteCount || 0) > prev",
            renderCount, new() { Timeout = 60000, PollingInterval = 200 });

        var text = await GetNormalizedSvgText();
        Assert.Contains("query: |-", text);
        Assert.DoesNotContain("content-type=application/json", text);
    }

    [Fact]
    public async Task Yaml_state_survives_steps_filter_toggle()
    {
        await NavigateAndSetup("YamlToggle_SurvivesStepsFilter.html");
        await ClickFormatButton();

        var scenario = Page.Locator("details.scenario");
        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await scenario.Locator(".toggle-btn[data-toggle='steps'][data-shown='true']").ClickAsync();
        await Page.WaitForFunctionAsync(
            "(prev) => !window._plantumlRendering && (window._renderCompleteCount || 0) > prev",
            renderCount, new() { Timeout = 60000, PollingInterval = 200 });

        var text = await GetNormalizedSvgText();
        Assert.Contains("query: |-", text);
        Assert.DoesNotContain("Given an order request", text);
    }

    // ═══════════════════════════════════════════════════════════
    // Active-format line count
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Yaml_view_that_unfolds_past_the_truncation_limit_becomes_a_long_note()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithLongSqlJsonNote(
            TempDir, OutputDir, "YamlToggle_LongSql.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        // JSON view: 3 content lines — no ▲ contract arrow on hover
        await HoverNoteRect(0);
        var hasUpArrowJson = await Page.EvaluateAsync<bool>("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => Array.from(i.querySelectorAll('text')).some(t => t.textContent.includes('▲')));
        """);
        Assert.False(hasUpArrowJson, "JSON view of a short note should have no ▲ button");

        await ClickFormatButton();

        // YAML view unfolds to 45+ lines — the note is now "long": ▲ appears
        await HoverNoteRect(0);
        await Page.WaitForFunctionAsync("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => Array.from(i.querySelectorAll('text')).some(t => t.textContent.includes('▲')));
        """, null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    // ═══════════════════════════════════════════════════════════
    // Fragment-split diagrams
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Toggle_works_on_a_fragment_split_diagram()
    {
        await Page.GotoAsync(GenerateFragmentedDiagramReport("YamlToggle_Fragments.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.note-hover-rect').length > 0 && !window._plantumlRendering",
            null, new() { Timeout = 60000, PollingInterval = 200 });

        var htmlBefore = await Page.Locator("[data-diagram-type='plantuml'] svg").First
            .EvaluateAsync<string>("el => el.outerHTML");
        await HoverNoteRect(0);
        await WaitForFormatButtonVisible();
        await Page.EvaluateAsync("""
            () => {
                var btn = Array.from(document.querySelectorAll('[data-note-btn="format"]'))
                    .find(b => b.style.display !== 'none' && b.style.opacity === '1');
                btn.querySelector('rect').dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true}));
            }
        """);
        await Page.WaitForFunctionAsync(
            $"() => {{ var svg = document.querySelector('[data-diagram-type=\"plantuml\"] svg'); " +
            $"return svg && !window._plantumlRendering && svg.outerHTML !== {System.Text.Json.JsonSerializer.Serialize(htmlBefore)}; }}",
            null, new() { Timeout = 60000, PollingInterval = 200 });

        var text = await Page.EvaluateAsync<string>(
            "() => Array.from(document.querySelectorAll('[data-diagram-type=\"plantuml\"] svg')).map(s => s.textContent).join(' ')");
        Assert.Contains("action: create", text);
    }
}
