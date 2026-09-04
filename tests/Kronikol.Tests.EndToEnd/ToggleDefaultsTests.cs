using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// E2E coverage for the configurable toggle default start states (TOGGLE_DEFAULTS_PLAN): every
/// Tier-1 control's zero-click render honours the seed, the control shows the seeded state, and
/// toggling away (and back) still works. Fixtures resolve the defaults through the real options
/// record (<c>ReportToggleDefaultsResolver</c>), exactly as report generation does.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class ToggleDefaultsTests : DiagramNotePlaywrightBase
{
    public ToggleDefaultsTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task NavigateWide(string fileName, Action<Kronikol.Reports.ReportToggleDefaults> configure)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateToggleDefaultsReport(
            TempDir, OutputDir, fileName, o => configure(o.TestRunReportToggleDefaults)));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    private async Task NavigateLongNote(string fileName, Action<Kronikol.Reports.ReportToggleDefaults> configure)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateLongNoteToggleDefaultsReport(
            TempDir, OutputDir, fileName, o => configure(o.TestRunReportToggleDefaults)));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    private Task<int> CountNoteLinesInSource() => Page.EvaluateAsync<int>("""
        () => {
            var container = document.querySelector('[data-diagram-type="plantuml"]');
            var source = container.getAttribute('data-plantuml') || '';
            return (source.match(/^Line \d+/gm) || []).length;
        }
    """);

    // ═══════════════════════════════════════════════════════════
    // Control 1 — Details radio
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Expanded_details_default_renders_full_notes_with_zero_clicks()
    {
        await NavigateLongNote("Toggle_DetailsExpanded.html", t => t.Details = Kronikol.Reports.ReportDetailsState.Expanded);

        // Zero-click render honours the seed: all 45 note lines are in the render source
        Assert.Equal(45, await CountNoteLinesInSource());

        // The toolbar shows the seeded state at both levels, and the truncate select is disabled
        Assert.Equal("true", await Page.EvaluateAsync<string>(
            "() => String(document.querySelector('.toolbar-right .details-radio-btn[data-state=\"expanded\"]').classList.contains('details-active'))"));
        Assert.Equal("true", await Page.EvaluateAsync<string>(
            "() => String(document.querySelector('details.scenario .details-radio-btn[data-state=\"expanded\"]').classList.contains('details-active'))"));
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => document.querySelector('.toolbar-right .truncate-lines-select').disabled"));
    }

    [Fact]
    public async Task Expanded_details_default_still_toggles_away_and_back()
    {
        await NavigateLongNote("Toggle_DetailsExpandedRoundTrip.html", t => t.Details = Kronikol.Reports.ReportDetailsState.Expanded);
        Assert.Equal(45, await CountNoteLinesInSource());

        await SetScenarioState("truncated");
        var truncated = await CountNoteLinesInSource();
        Assert.True(truncated < 45, $"truncating away from the seeded state must work (got {truncated} lines)");

        await SetScenarioState("expanded");
        Assert.Equal(45, await CountNoteLinesInSource());
    }

    [Fact]
    public async Task Collapsed_details_default_renders_collapsed_notes_with_zero_clicks()
    {
        await NavigateLongNote("Toggle_DetailsCollapsed.html", t => t.Details = Kronikol.Reports.ReportDetailsState.Collapsed);

        var lines = await CountNoteLinesInSource();
        Assert.True(lines < 5, $"collapsed seed should strip the note body from the render source (got {lines} lines)");
        Assert.Equal("true", await Page.EvaluateAsync<string>(
            "() => String(document.querySelector('.toolbar-right .details-radio-btn[data-state=\"collapsed\"]').classList.contains('details-active'))"));

        // The per-note plus button expands away from the seeded state (a long note goes
        // collapsed → truncated, so the body lines come back up to the truncation limit)
        await ClickNoteButton("[data-note-btn='plus']");
        var reopened = await CountNoteLinesInSource();
        Assert.True(reopened > 30, $"plus on the seeded-collapsed note should restore the body (got {reopened} lines)");
    }

    // ═══════════════════════════════════════════════════════════
    // Control 2 — Truncate lines
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Truncate_lines_default_governs_the_zero_click_truncation()
    {
        await NavigateLongNote("Toggle_TruncateLines10.html", t => t.TruncateLines = Kronikol.Reports.TruncateLineCount.Lines10);

        var lines = await CountNoteLinesInSource();
        Assert.True(lines is > 5 and < 15, $"the 45-line note should truncate near the seeded 10 lines (got {lines})");

        // Both dropdowns read the seeded value
        Assert.Equal("10", await Page.Locator(".toolbar-right .truncate-lines-select").InputValueAsync());
        Assert.Equal("10", await Page.Locator("details.scenario .truncate-lines-select").First.InputValueAsync());
        Assert.Equal(10, await Page.EvaluateAsync<int>("() => window._truncateLines"));

        // Changing the dropdown still works
        await Page.Locator("details.scenario .truncate-lines-select").First.SelectOptionAsync("40");
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container || window._plantumlRendering) return false;
                var source = container.getAttribute('data-plantuml') || '';
                return (source.match(/^Line \d+/gm) || []).length > 30;
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });
    }

    // ═══════════════════════════════════════════════════════════
    // Controls 3–6 — Headers / Assertions / Steps / Databases
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Headers_hidden_default_strips_headers_with_zero_clicks()
    {
        await NavigateWide("Toggle_HeadersHidden.html", t => t.HeadersShown = false);

        var text = await GetNormalizedSvgText();
        Assert.DoesNotContain("traceparent", text);
        Assert.Contains("customerId", text);

        var btn = Page.Locator(".toolbar-right .toggle-btn[data-toggle='headers']");
        Assert.Equal("Headers Hidden", (await btn.TextContentAsync())!.Trim());
        Assert.Equal("false", await btn.GetAttributeAsync("data-shown"));

        // Toggling back on works from the seeded state
        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await Page.Locator("details.scenario .toggle-btn[data-toggle='headers'][data-shown='false']").ClickAsync();
        await WaitForRenderCountIncrease(renderCount);
        Assert.Contains("traceparent", await GetNormalizedSvgText());
    }

    [Fact]
    public async Task Assertions_shown_default_renders_assertions_with_zero_clicks()
    {
        await NavigateWide("Toggle_AssertionsShown.html", t => t.AssertionsShown = true);

        var text = await GetNormalizedSvgText();
        Assert.Contains("✓ Put steps response message status code should be OK", text);

        var btn = Page.Locator(".toolbar-right .toggle-btn[data-toggle='assertions']");
        Assert.Equal("Assertions Shown", (await btn.TextContentAsync())!.Trim());

        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await Page.Locator("details.scenario .toggle-btn[data-toggle='assertions'][data-shown='true']").ClickAsync();
        await WaitForRenderCountIncrease(renderCount);
        Assert.DoesNotContain("✓ Put steps response message status code should be OK", await GetNormalizedSvgText());
    }

    [Fact]
    public async Task Steps_hidden_default_strips_step_bars_with_zero_clicks()
    {
        await NavigateWide("Toggle_StepsHidden.html", t => t.StepsShown = false);

        var text = await GetNormalizedSvgText();
        Assert.DoesNotContain("Given a valid customer preference request", text);
        Assert.Contains("customerId", text);

        var btn = Page.Locator(".toolbar-right .toggle-btn[data-toggle='steps']");
        Assert.Equal("Steps Hidden", (await btn.TextContentAsync())!.Trim());

        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await Page.Locator("details.scenario .toggle-btn[data-toggle='steps'][data-shown='false']").ClickAsync();
        await WaitForRenderCountIncrease(renderCount);
        Assert.Contains("Given a valid customer preference request", await GetNormalizedSvgText());
    }

    [Fact]
    public async Task Databases_hidden_default_strips_database_calls_with_zero_clicks()
    {
        await NavigateWide("Toggle_DatabasesHidden.html", t => t.DatabasesShown = false);

        var text = await GetNormalizedSvgText();
        Assert.DoesNotContain("UPSERT CustomerPreferences", text);
        Assert.Contains("customerId", text);

        var btn = Page.Locator(".toolbar-right .toggle-btn[data-toggle='databases']");
        Assert.Equal("Databases Hidden", (await btn.TextContentAsync())!.Trim());

        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await Page.Locator("details.scenario .toggle-btn[data-toggle='databases'][data-shown='false']").ClickAsync();
        await WaitForRenderCountIncrease(renderCount);
        Assert.Contains("UPSERT CustomerPreferences", await GetNormalizedSvgText());
    }

    // ═══════════════════════════════════════════════════════════
    // Control 7 — Note payload format via the toggle group
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Note_format_group_default_beats_the_flat_option()
    {
        // Flat option says JSON; the toggle group says YAML — the group wins (Q6), and the
        // zero-click render plus both dropdowns follow it.
        await Page.GotoAsync(ReportTestHelper.GenerateToggleDefaultsReport(
            TempDir, OutputDir, "Toggle_NoteFormatGroup.html", o =>
            {
                o.NotePayloadFormat = Kronikol.Reports.NotePayloadFormat.Json;
                o.TestRunReportToggleDefaults.NotePayloadFormat = Kronikol.Reports.NotePayloadFormat.Yaml;
            }));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        var text = await GetNormalizedSvgText();
        Assert.Contains("customerId: d37d5aba2a244807b7fe008d01f6ba0f", text);
        Assert.Equal("yaml", await Page.Locator(".toolbar-right .note-format-select").InputValueAsync());
        Assert.Equal("yaml", await Page.EvaluateAsync<string>("() => window._noteFormatDefault"));
    }

    // ═══════════════════════════════════════════════════════════
    // Controls 8–9 — Features / Scenarios expanded
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Features_and_scenarios_expanded_defaults_open_everything_and_seed_the_buttons()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateToggleDefaultsReport(
            TempDir, OutputDir, "Toggle_Expanded.html", o =>
            {
                o.TestRunReportToggleDefaults.FeaturesExpanded = true;
                o.TestRunReportToggleDefaults.ScenariosExpanded = true;
            }));
        await Page.Locator("details.feature").First.WaitForAsync();

        // Zero clicks: everything is open …
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => Array.from(document.querySelectorAll('details.feature')).every(d => d.open)"));
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => Array.from(document.querySelectorAll('details.scenario')).every(d => d.open)"));

        // … and the expand-all buttons carry the flip labels, so their first click works.
        // (Positional locators: a text-filtered locator would stop matching once the label flips.)
        var featuresBtn = Page.Locator("button.collapse-expand-all").Nth(0);
        var scenariosBtn = Page.Locator("button.collapse-expand-all").Nth(1);
        Assert.Equal("Collapse All Features", (await featuresBtn.TextContentAsync())!.Trim());
        Assert.Equal("Collapse All Scenarios", (await scenariosBtn.TextContentAsync())!.Trim());

        await scenariosBtn.ClickAsync();
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => Array.from(document.querySelectorAll('details.scenario')).every(d => !d.open)"));
        Assert.Equal("Expand All Scenarios", (await scenariosBtn.TextContentAsync())!.Trim());

        await featuresBtn.ClickAsync();
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => Array.from(document.querySelectorAll('details.feature')).every(d => !d.open)"));
    }

    // ═══════════════════════════════════════════════════════════
    // Control 10 — Diagram-type tab
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Activity_diagram_tab_default_starts_on_the_activity_view()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateWholeTestFlowToggleDefaultsReport(
            TempDir, OutputDir, "Toggle_DiagramTabActivity.html",
            o => o.TestRunReportToggleDefaults.DiagramTab = Kronikol.Reports.DiagramTabKind.Activity));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        // Zero clicks: activity button active, seq view hidden, activity view visible
        Assert.True(await Page.EvaluateAsync<bool>("""
            () => {
                var scenario = document.querySelector('details.scenario');
                var activityBtn = scenario.querySelector('.diagram-toggle-btn[data-dtype="activity"]');
                var seqView = scenario.querySelector('.diagram-view-seq');
                var activityView = scenario.querySelector('.diagram-view-activity');
                return activityBtn.classList.contains('diagram-toggle-active')
                    && seqView.style.display === 'none'
                    && activityView.style.display !== 'none';
            }
        """));

        // Clicking the sequence tab still swaps the views
        await Page.Locator("details.scenario .diagram-toggle-btn[data-dtype='seq']").First.ClickAsync();
        await Page.WaitForFunctionAsync("""
            () => {
                var scenario = document.querySelector('details.scenario');
                return scenario.querySelector('.diagram-view-seq').style.display !== 'none'
                    && scenario.querySelector('.diagram-view-activity').style.display === 'none';
            }
        """, null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Requested_tab_falls_back_where_the_view_does_not_exist()
    {
        // The wide fixture alone has no whole-test flow: DiagramTab=Activity resolves to the
        // sequence view (today's fallback order), and no tab bar is emitted at all.
        await NavigateWide("Toggle_DiagramTabFallback.html", t => t.DiagramTab = Kronikol.Reports.DiagramTabKind.Activity);
        Assert.Equal(0, await Page.EvaluateAsync<int>(
            "() => document.querySelectorAll('details.scenario .diagram-toggle-btn').length"));
        Assert.Contains("customerId", await GetNormalizedSvgText());
    }

    // ═══════════════════════════════════════════════════════════
    // Controls 11–12 — Timeline / Component Diagram panels
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Timeline_visible_default_shows_the_panel_and_seeds_the_button()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateComponentTimelineToggleDefaultsReport(
            TempDir, OutputDir, "Toggle_TimelineVisible.html",
            o => o.TestRunReportToggleDefaults.ScenarioTimelineVisible = true));
        await Page.Locator("details.feature").First.WaitForAsync();

        Assert.True(await Page.Locator("#scenario-timeline").IsVisibleAsync());
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => document.querySelector('button[onclick=\"toggle_timeline(this)\"]').classList.contains('timeline-toggle-active')"));

        // The seeded-visible panel still toggles off
        await Page.Locator("button[onclick='toggle_timeline(this)']").ClickAsync();
        Assert.False(await Page.Locator("#scenario-timeline").IsVisibleAsync());
    }

    [Fact]
    public async Task Component_diagram_visible_default_shows_the_panel()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateComponentTimelineToggleDefaultsReport(
            TempDir, OutputDir, "Toggle_ComponentVisible.html",
            o => o.TestRunReportToggleDefaults.ComponentDiagramVisible = true));
        await Page.Locator("details.feature").First.WaitForAsync();

        Assert.True(await Page.Locator("#component-diagram").IsVisibleAsync());
        Assert.False(await Page.Locator("#scenario-timeline").IsVisibleAsync());
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => document.querySelector('button[onclick=\"toggle_component_diagram(this)\"]').classList.contains('timeline-toggle-active')"));
    }

    [Fact]
    public async Task Both_panels_configured_visible_the_timeline_wins()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateComponentTimelineToggleDefaultsReport(
            TempDir, OutputDir, "Toggle_BothPanels.html", o =>
            {
                o.TestRunReportToggleDefaults.ScenarioTimelineVisible = true;
                o.TestRunReportToggleDefaults.ComponentDiagramVisible = true;
            }));
        await Page.Locator("details.feature").First.WaitForAsync();

        Assert.True(await Page.Locator("#scenario-timeline").IsVisibleAsync());
        Assert.False(await Page.Locator("#component-diagram").IsVisibleAsync());
        // The component diagram stays one click away
        await Page.Locator("button[onclick='toggle_component_diagram(this)']").ClickAsync();
        Assert.True(await Page.Locator("#component-diagram").IsVisibleAsync());
        Assert.False(await Page.Locator("#scenario-timeline").IsVisibleAsync());
    }

    // ═══════════════════════════════════════════════════════════
    // Tier 2 — disclosure sections
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Steps_section_closed_default_collapses_steps_but_search_reveal_opens_them()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateToggleDefaultsReport(
            TempDir, OutputDir, "Toggle_StepsSectionClosed.html",
            o => o.TestRunReportToggleDefaults.StepsSectionOpen = false));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        Assert.True(await Page.EvaluateAsync<bool>(
            "() => Array.from(document.querySelectorAll('details.scenario-steps')).every(d => !d.open)"));

        // A single-match search must reveal into the closed-by-default section (M7 rule):
        // the deep-search corpus covers step text, so the match cannot stay hidden.
        await FillSearchBar("\"I delete a non-existent order\"");
        await Page.WaitForFunctionAsync("""
            () => {
                var open = Array.from(document.querySelectorAll('details.scenario'))
                    .filter(d => d.style.display !== 'none' && d.open);
                return open.length === 1 && open[0].querySelector('details.scenario-steps')?.open === true;
            }
        """, null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Diagrams_section_closed_default_collapses_the_diagram_disclosure()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateToggleDefaultsReport(
            TempDir, OutputDir, "Toggle_DiagramsSectionClosed.html",
            o => o.TestRunReportToggleDefaults.DiagramsSectionOpen = false));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        Assert.True(await Page.EvaluateAsync<bool>(
            "() => Array.from(document.querySelectorAll('details.example-diagrams')).every(d => !d.open)"));

        // Opening it by hand still renders the diagram
        await Page.EvaluateAsync("() => document.querySelector('details.example-diagrams').setAttribute('open','')");
        await WaitForDiagramSvg();
    }

    [Fact]
    public async Task Features_summary_open_default_opens_the_summary_table()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateToggleDefaultsReport(
            TempDir, OutputDir, "Toggle_FeaturesSummaryOpen.html",
            o => o.TestRunReportToggleDefaults.FeaturesSummaryOpen = true));
        await Page.Locator("details.feature").First.WaitForAsync();

        Assert.True(await Page.EvaluateAsync<bool>(
            "() => document.querySelector('details.features-summary-details').open"));
        Assert.True(await Page.Locator(".feature-summary-table").IsVisibleAsync());
    }
}
