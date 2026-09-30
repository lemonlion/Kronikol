namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Reports)]
public class ReportToolbarTests : PlaywrightTestBase
{
    public ReportToolbarTests(PlaywrightFixture fixture) : base(fixture) { }

    // ── Expand / Collapse All buttons ──

    [Fact]
    public async Task Expand_all_features_opens_all_feature_details()
    {
        await Page.GotoAsync(GenerateReport("ToolbarExpandFeatures.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var expandBtn = Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" });
        await expandBtn.ClickAsync();

        var features = Page.Locator("details.feature");
        var count = await features.CountAsync();
        Assert.True(count >= 2, "Should have at least 2 features");
        for (var i = 0; i < count; i++)
            Assert.NotNull(await features.Nth(i).GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Collapse_all_features_closes_all_feature_details()
    {
        await Page.GotoAsync(GenerateReport("ToolbarCollapseFeatures.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var btn = Page.Locator("button.collapse-expand-all").Nth(0);
        await btn.ClickAsync();
        await Expect(btn).ToContainTextAsync("Collapse");

        await btn.ClickAsync();
        var features = Page.Locator("details.feature");
        var count = await features.CountAsync();
        for (var i = 0; i < count; i++)
            Assert.Null(await features.Nth(i).GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Expand_all_scenarios_opens_all_scenario_details()
    {
        await Page.GotoAsync(GenerateReport("ToolbarExpandScenarios.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();

        var scenarios = Page.Locator("details.scenario");
        var count = await scenarios.CountAsync();
        Assert.True(count >= 3, "Should have scenarios");
        for (var i = 0; i < count; i++)
            Assert.NotNull(await scenarios.Nth(i).GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Collapse_all_scenarios_closes_all_scenario_details()
    {
        await Page.GotoAsync(GenerateReport("ToolbarCollapseScenarios.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        var btn = Page.Locator("button.collapse-expand-all").Nth(1);
        await btn.ClickAsync();
        await Expect(btn).ToContainTextAsync("Collapse");

        await btn.ClickAsync();
        var scenarios = Page.Locator("details.scenario");
        var count = await scenarios.CountAsync();
        for (var i = 0; i < count; i++)
            Assert.Null(await scenarios.Nth(i).GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Expand_collapse_button_text_toggles()
    {
        await Page.GotoAsync(GenerateReport("ToolbarToggleText.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var btn = Page.Locator("button.collapse-expand-all").Nth(0);
        await Expect(btn).ToContainTextAsync("Expand");
        await btn.ClickAsync();
        await Expect(btn).ToContainTextAsync("Collapse");
        await btn.ClickAsync();
        await Expect(btn).ToContainTextAsync("Expand");
    }

    // ── Details radio buttons (Expanded / Collapsed / Truncated) ──

    [Fact]
    public async Task Truncated_is_active_by_default()
    {
        await Page.GotoAsync(GenerateReport("ToolbarTruncDefault.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var truncBtn = Page.Locator(".toolbar-row .details-radio-btn[data-state='truncated']");
        await Expect(truncBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));
    }

    [Fact]
    public async Task Clicking_expanded_activates_it_and_deactivates_truncated()
    {
        await Page.GotoAsync(GenerateReport("ToolbarExpandedRadio.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var expandedBtn = Page.Locator(".toolbar-row .details-radio-btn[data-state='expanded']");
        var truncBtn = Page.Locator(".toolbar-row .details-radio-btn[data-state='truncated']");

        await expandedBtn.ClickAsync();
        await Expect(expandedBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));
        var truncClass = await truncBtn.GetAttributeAsync("class");
        Assert.DoesNotContain("details-active", truncClass!);
    }

    [Fact]
    public async Task Clicking_collapsed_activates_it()
    {
        await Page.GotoAsync(GenerateReport("ToolbarCollapsedRadio.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var collapsedBtn = Page.Locator(".toolbar-row .details-radio-btn[data-state='collapsed']");
        await collapsedBtn.ClickAsync();
        await Expect(collapsedBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));
    }

    // ── Truncate lines dropdown ──

    [Fact]
    public async Task Changing_line_count_syncs_all_dropdowns()
    {
        await Page.GotoAsync(GenerateReport("ToolbarLineSync.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();

        var selects = Page.Locator(".truncate-lines-select");
        var selectCount = await selects.CountAsync();
        Assert.True(selectCount >= 2, "Need report-level + scenario-level dropdowns");

        await selects.First.SelectOptionAsync("10");

        for (var i = 0; i < selectCount; i++)
        {
            var value = await selects.Nth(i).InputValueAsync();
            Assert.Equal("10", value);
        }
    }

    // ── Headers shown / hidden ──

    [Fact]
    public async Task Headers_hidden_is_the_start_state()
    {
        // 4.0.0 (plans/V4_PLAN.md R8); headers started shown through 3.x.
        await Page.GotoAsync(GenerateReport("ToolbarHeaderDefault.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var headersBtn = Page.Locator(".toolbar-row .toggle-btn[data-toggle='headers']");
        await Expect(headersBtn).ToHaveTextAsync("Headers Hidden");
        await Expect(headersBtn).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));
    }

    [Fact]
    public async Task Clicking_headers_toggle_shows_then_hides_headers_and_updates_text()
    {
        await Page.GotoAsync(GenerateReport("ToolbarHiddenHeaders.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var headersBtn = Page.Locator(".toolbar-row .toggle-btn[data-toggle='headers']");

        await headersBtn.ClickAsync();
        await Expect(headersBtn).ToHaveTextAsync("Headers Shown");
        await Expect(headersBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));

        await headersBtn.ClickAsync();
        await Expect(headersBtn).ToHaveTextAsync("Headers Hidden");
        var headersBtnClass = await headersBtn.GetAttributeAsync("class");
        Assert.DoesNotContain("details-active", headersBtnClass!);
    }

    // ── Search bar ──

    [Fact]
    public async Task Search_filters_scenarios_by_name()
    {
        await Page.GotoAsync(GenerateReport("ToolbarSearch.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await FillSearchBar("Delete");

        // Wait for debounce + filter
        await Page.WaitForFunctionAsync("""
            () => {
                var passed = document.querySelectorAll('.scenario[data-status="Passed"]');
                return Array.from(passed).every(s => getComputedStyle(s).display === 'none');
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        var deleteScenario = Page.Locator(".scenario[data-search*='delete']");
        await Expect(deleteScenario.First).Not.ToHaveCSSAsync("display", "none");
    }

    [Fact]
    public async Task Search_single_match_auto_expands_scenario()
    {
        await Page.GotoAsync(GenerateReport("ToolbarSearchAutoExpand.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await FillSearchBar("Process payment");

        await Page.WaitForFunctionAsync("""
            () => {
                var visible = Array.from(document.querySelectorAll('.scenario'))
                    .filter(s => getComputedStyle(s).display !== 'none');
                return visible.length === 1;
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        var scenario = Page.Locator(".scenario").Filter(new()
        {
            Has = Page.Locator(":scope:not([style*='display: none'])")
        });
        // When only one match, scenario and feature should auto-expand
        var firstVisible = await Page.EvaluateAsync<bool>("""
            () => {
                var visible = Array.from(document.querySelectorAll('.scenario'))
                    .filter(s => getComputedStyle(s).display !== 'none');
                return visible.length === 1 && visible[0].hasAttribute('open');
            }
        """);
        Assert.True(firstVisible);
    }

    [Fact]
    public async Task Search_with_quoted_phrase_matches_scenarios()
    {
        await Page.GotoAsync(GenerateReport("ToolbarSearchQuoted.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await FillSearchBar("\"Delete order\"");

        await Page.WaitForFunctionAsync("""
            () => {
                var visible = Array.from(document.querySelectorAll('.scenario'))
                    .filter(s => getComputedStyle(s).display !== 'none');
                return visible.length === 1;
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        var visible = await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('.scenario'))
                .filter(s => getComputedStyle(s).display !== 'none')
                .map(s => s.getAttribute('data-search'))
        """);
        Assert.Single(visible);
        Assert.Contains("delete order", visible[0]);
    }

    [Fact]
    public async Task Search_with_quoted_phrase_that_does_not_match_hides_all()
    {
        await Page.GotoAsync(GenerateReport("ToolbarSearchQuotedNoMatch.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await FillSearchBar("\"order create\"");

        await Page.WaitForFunctionAsync("""
            () => {
                var visible = Array.from(document.querySelectorAll('.scenario'))
                    .filter(s => getComputedStyle(s).display !== 'none');
                return visible.length === 0;
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Search_by_step_text_matches_scenario()
    {
        await Page.GotoAsync(GenerateReport("ToolbarSearchStepText.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await FillSearchBar("non-existent");

        var visible = await Page.WaitForFunctionAsync("""
            () => {
                var visible = Array.from(document.querySelectorAll('.scenario'))
                    .filter(s => getComputedStyle(s).display !== 'none');
                return visible.length === 1 ? visible[0].getAttribute('data-search') : null;
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        Assert.Contains("delete order", visible.ToString()!);
    }

    // ── Clear All button ──

    [Fact]
    public async Task Clear_all_resets_search_and_status_filters()
    {
        await Page.GotoAsync(GenerateReport("ToolbarClearAll.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await FillSearchBar("Delete");
        await Page.Locator(".status-toggle[data-status='Failed']").ClickAsync();
        await Page.GetByText("Clear All").ClickAsync();

        var searchValue = await Page.Locator("#searchbar").InputValueAsync();
        Assert.Equal("", searchValue);

        var failedClass = await Page.Locator(".status-toggle[data-status='Failed']").GetAttributeAsync("class");
        Assert.DoesNotContain("status-active", failedClass!);

        var scenarios = Page.Locator(".scenario");
        var count = await scenarios.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var display = await scenarios.Nth(i).EvaluateAsync<string>("el => getComputedStyle(el).display");
            Assert.NotEqual("none", display);
        }
    }

    // ── Scenario Timeline info icon ──

    [Fact]
    public async Task Timeline_info_icon_is_present_with_tooltip()
    {
        await Page.GotoAsync(GenerateReport("ToolbarTimelineInfo.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator(".timeline-toggle").ClickAsync();

        var timeline = Page.Locator("#scenario-timeline");
        await Expect(timeline).ToBeVisibleAsync();

        var infoIcon = timeline.Locator(".timeline-info");
        await Expect(infoIcon).ToBeVisibleAsync();

        var tooltip = await infoIcon.GetAttributeAsync("title");
        Assert.False(string.IsNullOrWhiteSpace(tooltip));
        Assert.Contains("duration", tooltip!, StringComparison.OrdinalIgnoreCase);
    }
}
