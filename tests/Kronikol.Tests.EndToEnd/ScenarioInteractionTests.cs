using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Scenarios)]
public class ScenarioInteractionTests : PlaywrightTestBase
{
    public ScenarioInteractionTests(PlaywrightFixture fixture) : base(fixture) { }

    // ── Feature and scenario expand/collapse via click ──

    [Fact]
    public async Task Clicking_feature_summary_opens_feature()
    {
        await Page.GotoAsync(GenerateReport("ScenarioFeatureOpen.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var feature = Page.Locator("details.feature").First;
        Assert.Null(await feature.GetAttributeAsync("open"));

        await feature.Locator("summary").First.ClickAsync();
        Assert.NotNull(await feature.GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Clicking_scenario_summary_opens_scenario()
    {
        await Page.GotoAsync(GenerateReport("ScenarioOpen.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator("details.feature summary").First.ClickAsync();

        var scenario = Page.Locator("details.scenario").First;
        await scenario.WaitForAsync();
        Assert.Null(await scenario.GetAttributeAsync("open"));

        await scenario.Locator("summary").First.ClickAsync();
        Assert.NotNull(await scenario.GetAttributeAsync("open"));
    }

    // ── Copy scenario name button ──

    [Fact]
    public async Task Copy_scenario_name_button_exists_on_each_scenario()
    {
        await Page.GotoAsync(GenerateReport("ScenarioCopyBtn.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();

        var copyBtns = await Page.Locator(".copy-scenario-name").CountAsync();
        var scenarios = await Page.Locator("details.scenario").CountAsync();
        Assert.Equal(scenarios, copyBtns);
    }

    [Fact]
    public async Task Copy_scenario_name_button_shows_checkmark_after_click()
    {
        await Page.GotoAsync(GenerateReport("ScenarioCopyCheck.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // Grant clipboard permissions
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);

        await ExpandAllFeaturesAsync();

        var copyBtn = Page.Locator(".copy-scenario-name").First;
        await copyBtn.ClickAsync();

        await Expect(copyBtn).ToHaveTextAsync("\u2713", new() { Timeout = 2000 });
    }

    [Fact]
    public async Task Copy_scenario_name_button_reverts_after_delay()
    {
        await Page.GotoAsync(GenerateReport("ScenarioCopyRevert.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);

        await ExpandAllFeaturesAsync();

        var copyBtn = Page.Locator(".copy-scenario-name").First;
        var origText = await copyBtn.TextContentAsync();
        await copyBtn.ClickAsync();

        await Expect(copyBtn).ToHaveTextAsync("\u2713", new() { Timeout = 2000 });
        await Expect(copyBtn).ToHaveTextAsync(origText!, new() { Timeout = 5000 });
    }

    [Fact]
    public async Task Copy_scenario_name_has_correct_data_attribute()
    {
        await Page.GotoAsync(GenerateReport("ScenarioCopyData.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();

        var name = await Page.Locator(".copy-scenario-name").First.GetAttributeAsync("data-scenario-name");
        Assert.False(string.IsNullOrEmpty(name), "data-scenario-name should not be empty");
    }

    // ── Scenario link button ──

    [Fact]
    public async Task Scenario_link_exists_on_each_scenario()
    {
        await Page.GotoAsync(GenerateReport("ScenarioLink.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();

        var links = await Page.Locator(".scenario-link").CountAsync();
        var scenarios = await Page.Locator("details.scenario").CountAsync();
        Assert.Equal(scenarios, links);
    }

    [Fact]
    public async Task Scenario_link_href_points_to_scenario_id()
    {
        await Page.GotoAsync(GenerateReport("ScenarioLinkHref.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();

        var href = await Page.Locator(".scenario-link").First.GetAttributeAsync("href");
        Assert.Contains("#scenario-", href!);
    }

    // ── Duration badge ──

    [Fact]
    public async Task Duration_badge_shows_time_for_scenarios_with_duration()
    {
        await Page.GotoAsync(GenerateReport("ScenarioDuration.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();

        var badges = Page.Locator(".duration-badge");
        var count = await badges.CountAsync();
        Assert.True(count > 0, "Duration badges should exist");

        var anyHasText = false;
        for (var i = 0; i < count; i++)
        {
            var text = await badges.Nth(i).TextContentAsync();
            if (!string.IsNullOrWhiteSpace(text)) { anyHasText = true; break; }
        }
        Assert.True(anyHasText, "At least one duration badge should have text");
    }

    // ── Scenario status classes ──

    [Fact]
    public async Task Scenarios_have_correct_status_data_attribute()
    {
        await Page.GotoAsync(GenerateReport("ScenarioStatus.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var passed = await Page.Locator(".scenario[data-status='Passed']").CountAsync();
        var failed = await Page.Locator(".scenario[data-status='Failed']").CountAsync();
        var skipped = await Page.Locator(".scenario[data-status='Skipped']").CountAsync();

        Assert.True(passed >= 2, "Should have at least 2 passed scenarios");
        Assert.True(failed >= 1, "Should have at least 1 failed scenario");
        Assert.True(skipped >= 1, "Should have at least 1 skipped scenario");
    }

    // ── Happy path class ──

    [Fact]
    public async Task Happy_path_scenarios_have_class()
    {
        await Page.GotoAsync(GenerateReport("ScenarioHappyPath.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var happyPaths = await Page.Locator(".scenario.happy-path").CountAsync();
        Assert.True(happyPaths >= 2, "Should have at least 2 happy path scenarios");
    }

    // ── Steps rendering ──

    [Fact]
    public async Task Scenario_steps_render_inside_open_scenario()
    {
        await Page.GotoAsync(GenerateReport("ScenarioSteps.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        var steps = await Page.Locator(".scenario-steps .step").CountAsync();
        Assert.True(steps >= 3, "First scenario should have at least 3 steps");
    }

    [Fact]
    public async Task Steps_section_is_collapsible_details_element()
    {
        await Page.GotoAsync(GenerateReport("StepsCollapsible.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        var stepsDetails = Page.Locator("details.scenario-steps").First;
        Assert.NotNull(await stepsDetails.GetAttributeAsync("open"));

        var summary = stepsDetails.Locator("summary");
        await Expect(summary).ToContainTextAsync("Steps");
    }

    [Fact]
    public async Task Steps_section_can_be_collapsed_by_clicking_summary()
    {
        await Page.GotoAsync(GenerateReport("StepsCollapse.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        var stepsDetails = Page.Locator("details.scenario-steps").First;
        Assert.NotNull(await stepsDetails.GetAttributeAsync("open"));

        await stepsDetails.Locator("summary").ClickAsync();

        await Page.WaitForFunctionAsync("""
            () => !document.querySelector('details.scenario-steps').hasAttribute('open')
        """);
        Assert.Null(await stepsDetails.GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Steps_section_has_rounded_border()
    {
        await Page.GotoAsync(GenerateReport("StepsBorder.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        var stepsDetails = Page.Locator("details.scenario-steps").First;
        var borderWidth = await stepsDetails.EvaluateAsync<string>("el => getComputedStyle(el).borderWidth");
        Assert.Equal("1px", borderWidth);
        var borderRadius = await stepsDetails.EvaluateAsync<string>("el => getComputedStyle(el).borderRadius");
        Assert.Contains("14px", borderRadius); // 5.0.0: the Option C steps card (plans/OPTION_C_PLAN.md S1)
    }

    // ── Diagram container renders ──

    [Fact]
    public async Task Plantuml_browser_diagram_renders_svg()
    {
        await Page.GotoAsync(GenerateReport("ScenarioDiagramRender.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();

        var svg = await WaitForDiagramSvg();
        await Expect(svg).ToBeVisibleAsync();
    }

    // ── Right-click context menu on rendered diagram ──

    [Fact]
    public async Task Right_click_on_diagram_shows_context_menu()
    {
        await Page.GotoAsync(GenerateReport("ScenarioCtxMenu.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = await WaitForDiagramSvg();

        await DispatchContextMenu(svg);

        var menu = Page.Locator(".diagram-ctx-menu");
        await Expect(menu).ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Fact]
    public async Task Context_menu_has_copy_and_save_submenus()
    {
        await Page.GotoAsync(GenerateReport("ScenarioCtxMenuItems.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = await WaitForDiagramSvg();

        await DispatchContextMenu(svg);

        var menu = Page.Locator(".diagram-ctx-menu");
        await Expect(menu).ToBeVisibleAsync(new() { Timeout = 5000 });

        var menuText = await menu.TextContentAsync();
        Assert.Contains("Copy image", menuText!);
        Assert.Contains("Save image", menuText!);
    }

    [Fact]
    public async Task Context_menu_dismissed_by_clicking_elsewhere()
    {
        await Page.GotoAsync(GenerateReport("ScenarioCtxMenuDismiss.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = await WaitForDiagramSvg();

        await DispatchContextMenu(svg);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await Page.Locator("body").ClickAsync();

        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 3000 });
    }

    [Fact]
    public async Task Context_menu_dismissed_by_escape_key()
    {
        await Page.GotoAsync(GenerateReport("ScenarioCtxMenuEsc.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = await WaitForDiagramSvg();

        await DispatchContextMenu(svg);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await Page.Keyboard.PressAsync("Escape");

        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 3000 });
    }

    // ── Status filter buttons ──

    [Fact]
    public async Task Clicking_status_filter_hides_non_matching_scenarios()
    {
        await Page.GotoAsync(GenerateReport("ScenarioStatusFilter.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator(".status-toggle[data-status='Passed']").ClickAsync();

        await Page.WaitForFunctionAsync("""
            () => {
                var failed = document.querySelectorAll('.scenario[data-status="Failed"]');
                return Array.from(failed).every(s => getComputedStyle(s).display === 'none');
            }
        """, null, new() { Timeout = 3000, PollingInterval = 200 });

        var passedVisible = await Page.EvaluateAsync<bool>("""
            () => Array.from(document.querySelectorAll('.scenario[data-status="Passed"]'))
                .some(s => getComputedStyle(s).display !== 'none')
        """);
        Assert.True(passedVisible);
    }

    [Fact]
    public async Task Clicking_status_filter_again_deactivates_it()
    {
        await Page.GotoAsync(GenerateReport("ScenarioStatusToggle.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var passedBtn = Page.Locator(".status-toggle[data-status='Passed']");
        await passedBtn.ClickAsync();
        await Expect(passedBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("status-active"));

        await passedBtn.ClickAsync();
        var cls = await passedBtn.GetAttributeAsync("class");
        Assert.DoesNotContain("status-active", cls!);

        var allVisible = await Page.EvaluateAsync<bool>("""
            () => Array.from(document.querySelectorAll('.scenario'))
                .every(s => getComputedStyle(s).display !== 'none')
        """);
        Assert.True(allVisible);
    }

    // ── Happy path filter ──

    [Fact]
    public async Task Clicking_happy_path_filter_hides_non_happy_scenarios()
    {
        await Page.GotoAsync(GenerateReport("ScenarioHappyFilter.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var hpBtn = Page.Locator(".happy-path-toggle");
        await hpBtn.ClickAsync();

        await Page.WaitForFunctionAsync("""
            () => {
                var nonHappy = document.querySelectorAll('.scenario:not(.happy-path)');
                return Array.from(nonHappy).every(s => getComputedStyle(s).display === 'none');
            }
        """, null, new() { Timeout = 3000, PollingInterval = 200 });

        await Expect(hpBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("happy-path-active"));
    }

    // ── Category filter ──

    [Fact]
    public async Task Category_buttons_render_for_scenarios_with_categories()
    {
        await Page.GotoAsync(GenerateReport("ScenarioCategoryBtns.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var catBtns = await Page.Locator(".category-toggle").CountAsync();
        Assert.True(catBtns >= 2, $"Should have category buttons but found {catBtns}");
    }
}
