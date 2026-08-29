using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// E2E tests for the JSON/YAML note payload format dropdowns beside the
/// filter toggles: report-level bulk conversion + dropdown sync, scenario
/// isolation, ineligible-note safety, lazy-container preferences and the
/// pending no-op contract. See NOTE_YAML_TOGGLE_PLAN.md follow-ups.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteFormatDropdownTests : DiagramNotePlaywrightBase
{
    public NoteFormatDropdownTests(PlaywrightFixture fixture) : base(fixture) { }

    private ILocator ReportSelect => Page.Locator(".toolbar-right .note-format-select");

    private ILocator ScenarioSelect(int index) =>
        Page.Locator("details.scenario").Nth(index).Locator(".note-format-select");

    private async Task NavigateSingleScenario(string fileName)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithJsonYamlNotes(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    private async Task NavigateTwoScenariosAndRenderBoth(string fileName)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithJsonNotesInTwoScenarios(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await RenderAllDiagramsAndWait();
        await WaitForNoteElements();
    }

    private async Task<string> ScenarioSvgText(int index)
    {
        var text = await Page.Locator("details.scenario").Nth(index)
            .Locator("[data-diagram-type='plantuml'] svg").First
            .EvaluateAsync<string>("el => el.textContent");
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
    }

    // ═══════════════════════════════════════════════════════════
    // Presence and defaults
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Dropdowns_are_present_with_json_selected_by_default()
    {
        await NavigateSingleScenario("FormatDropdown_Defaults.html");

        Assert.Equal("json", await ReportSelect.InputValueAsync());
        Assert.Equal("json", await Page.Locator("details.scenario .note-format-select").First.InputValueAsync());
        Assert.Equal("Note payload format", await ReportSelect.GetAttributeAsync("aria-label"));
    }

    [Fact]
    public async Task Dropdown_is_not_emitted_for_a_report_without_json_payloads()
    {
        await Page.GotoAsync(GenerateReportWithWideDiagram("FormatDropdown_NoJson.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        Assert.Equal(0, await Page.Locator(".note-format-select").CountAsync());
    }

    // ═══════════════════════════════════════════════════════════
    // Report-level bulk conversion
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Report_yaml_selection_converts_all_scenarios_and_syncs_every_dropdown()
    {
        await NavigateTwoScenariosAndRenderBoth("FormatDropdown_ReportYaml.html");

        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await ReportSelect.SelectOptionAsync("yaml");
        await WaitForRenderCountIncrease(renderCount);

        Assert.Contains("alphaField: |-", await ScenarioSvgText(0));
        Assert.Contains("betaField: |-", await ScenarioSvgText(1));
        Assert.Equal("yaml", await ScenarioSelect(0).InputValueAsync());
        Assert.Equal("yaml", await ScenarioSelect(1).InputValueAsync());
    }

    [Fact]
    public async Task Report_json_selection_restores_the_original_view()
    {
        await NavigateSingleScenario("FormatDropdown_BackToJson.html");
        var textBefore = await GetNormalizedSvgText();

        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await ReportSelect.SelectOptionAsync("yaml");
        await WaitForRenderCountIncrease(renderCount);
        Assert.Contains("query: |-", await GetNormalizedSvgText());

        renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await ReportSelect.SelectOptionAsync("json");
        await WaitForRenderCountIncrease(renderCount);

        Assert.Equal(textBefore, await GetNormalizedSvgText());
    }

    [Fact]
    public async Task Ineligible_notes_are_untouched_by_a_bulk_yaml_command()
    {
        await NavigateSingleScenario("FormatDropdown_Ineligible.html");

        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await ReportSelect.SelectOptionAsync("yaml");
        await WaitForRenderCountIncrease(renderCount);

        var text = await GetNormalizedSvgText();
        Assert.Contains("query: |-", text);
        Assert.Contains("plain text response body", text);
        Assert.Contains("not json at all", text);
    }

    // ═══════════════════════════════════════════════════════════
    // Scenario-level isolation
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Scenario_yaml_selection_only_affects_its_own_scenario()
    {
        await NavigateTwoScenariosAndRenderBoth("FormatDropdown_ScenarioIsolation.html");

        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await ScenarioSelect(0).SelectOptionAsync("yaml");
        await WaitForRenderCountIncrease(renderCount);

        Assert.Contains("alphaField: |-", await ScenarioSvgText(0));
        var betaText = await ScenarioSvgText(1);
        Assert.DoesNotContain("betaField: |-", betaText);
        Assert.Contains("\"betaField\":", betaText);

        // A scenario command does not move the report dropdown, the other
        // scenario's dropdown, or the report-wide default for lazy containers.
        Assert.Equal("json", await ReportSelect.InputValueAsync());
        Assert.Equal("json", await ScenarioSelect(1).InputValueAsync());
        Assert.Equal("json", await Page.EvaluateAsync<string>("() => window._noteFormatDefault"));
    }

    [Fact]
    public async Task Per_note_toggle_does_not_move_the_dropdowns()
    {
        await NavigateSingleScenario("FormatDropdown_PerNoteIndependent.html");
        await ClickNoteFormatButton();

        Assert.Contains("query: |-", await GetNormalizedSvgText());
        Assert.Equal("json", await ReportSelect.InputValueAsync());
        Assert.Equal("json", await Page.Locator("details.scenario .note-format-select").First.InputValueAsync());
    }

    // ═══════════════════════════════════════════════════════════
    // Lazy containers — preference must survive later decompression
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Lazy_scenario_opened_after_report_yaml_selection_renders_straight_into_yaml()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithJsonNotesInTwoScenarios(TempDir, OutputDir, "FormatDropdown_LazyScenario.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();

        // Open and render ONLY the first scenario — the second stays lazy.
        await Page.Locator("details.scenario").Nth(0).Locator("summary").First.ClickAsync();
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.querySelectorAll('details.scenario')[0])");
        await WaitForNoteElements();

        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await ReportSelect.SelectOptionAsync("yaml");
        await WaitForRenderCountIncrease(renderCount);
        Assert.Contains("alphaField: |-", await ScenarioSvgText(0));

        // The second scenario's diagram was never decompressed by the bulk command
        var lazyStillLazy = await Page.EvaluateAsync<bool>("""
            () => {
                var c = document.querySelectorAll('details.scenario')[1].querySelector('[data-diagram-type="plantuml"]');
                return !!c && !c.hasAttribute('data-plantuml');
            }
        """);
        Assert.True(lazyStillLazy, "second scenario should still be lazy after the report-level command");

        // Opening it now must render straight into YAML with zero further clicks
        await Page.Locator("details.scenario").Nth(1).Locator("summary").First.ClickAsync();
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.querySelectorAll('details.scenario')[1])");
        await Page.WaitForFunctionAsync("""
            () => {
                var sc = document.querySelectorAll('details.scenario')[1];
                var svg = sc && sc.querySelector('[data-diagram-type="plantuml"] svg');
                return !!svg && !window._plantumlRendering && svg.textContent.indexOf('betaField: |-') >= 0;
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Scenario_yaml_selection_before_its_diagram_renders_applies_on_decompression()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithJsonNotesInTwoScenarios(TempDir, OutputDir, "FormatDropdown_LazyInScenario.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();

        // Open the first scenario and issue its scenario-level YAML command
        // BEFORE its diagram has been decompressed — _noteFormatPreference is
        // stamped on the lazy container and honoured when it renders.
        await Page.Locator("details.scenario").Nth(0).Locator("summary").First.ClickAsync();
        await ScenarioSelect(0).SelectOptionAsync("yaml");

        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.querySelectorAll('details.scenario')[0])");
        await Page.WaitForFunctionAsync("""
            () => {
                var sc = document.querySelectorAll('details.scenario')[0];
                var svg = sc && sc.querySelector('[data-diagram-type="plantuml"] svg');
                return !!svg && !window._plantumlRendering && svg.textContent.indexOf('alphaField: |-') >= 0;
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });

        // The command was scenario-scoped: the report default is untouched, so
        // the second scenario still decompresses into JSON.
        Assert.Equal("json", await Page.EvaluateAsync<string>("() => window._noteFormatDefault"));
        await Page.Locator("details.scenario").Nth(1).Locator("summary").First.ClickAsync();
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.querySelectorAll('details.scenario')[1])");
        await Page.WaitForFunctionAsync("""
            () => {
                var sc = document.querySelectorAll('details.scenario')[1];
                var svg = sc && sc.querySelector('[data-diagram-type="plantuml"] svg');
                return !!svg && !window._plantumlRendering && svg.textContent.indexOf('betaField') >= 0;
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });
        Assert.DoesNotContain("betaField: |-", await ScenarioSvgText(1));
    }

    // ═══════════════════════════════════════════════════════════
    // Pending contract
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Reselecting_the_current_format_is_an_instant_no_op()
    {
        await NavigateSingleScenario("FormatDropdown_NoOp.html");

        // Re-issuing the current format queues nothing; the pending decoration
        // must clear synchronously (empty queue completes immediately).
        var stillPending = await Page.EvaluateAsync<bool>("""
            () => {
                var sel = document.querySelector('.toolbar-right .note-format-select');
                window._setNoteFormat(sel);
                return document.querySelectorAll('.note-format-select.details-pending').length > 0;
            }
        """);
        Assert.False(stillPending, "a no-op format command must not leave dropdowns pending");
    }
}
