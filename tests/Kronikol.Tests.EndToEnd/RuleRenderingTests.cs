namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Reports)]
public class RuleRenderingTests : PlaywrightTestBase
{
    public RuleRenderingTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Scenarios_with_rules_are_grouped_under_rule_details()
    {
        await Page.GotoReportAsync(GenerateReportWithRules("RuleGrouping.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // Expand the first feature
        await Page.Locator("details.feature summary").First.ClickAsync();

        // Should have 2 rule sections in the first feature
        var rules = Page.Locator("details.feature").First.Locator("details.rule");
        Assert.Equal(2, await rules.CountAsync());
    }

    [Fact]
    public async Task Rule_sections_display_rule_title_in_summary()
    {
        await Page.GotoReportAsync(GenerateReportWithRules("RuleTitles.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator("details.feature summary").First.ClickAsync();

        var ruleSummaries = Page.Locator("details.feature").First.Locator("details.rule > summary");
        Assert.Equal(2, await ruleSummaries.CountAsync());

        var firstRuleTitle = await ruleSummaries.Nth(0).InnerTextAsync();
        var secondRuleTitle = await ruleSummaries.Nth(1).InnerTextAsync();
        Assert.Contains("Valid Order Creation", firstRuleTitle);
        Assert.Contains("Invalid Order Handling", secondRuleTitle);
    }

    [Fact]
    public async Task Rule_sections_are_open_by_default()
    {
        await Page.GotoReportAsync(GenerateReportWithRules("RuleOpen.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator("details.feature summary").First.ClickAsync();

        var rules = Page.Locator("details.feature").First.Locator("details.rule");
        var firstRule = rules.First;
        await firstRule.WaitForAsync();

        // Rule sections should have the 'open' attribute
        Assert.NotNull(await firstRule.GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Rule_summary_has_h2_5_class()
    {
        await Page.GotoReportAsync(GenerateReportWithRules("RuleSummaryClass.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator("details.feature summary").First.ClickAsync();

        var ruleSummary = Page.Locator("details.feature").First.Locator("details.rule > summary.h2-5");
        Assert.True(await ruleSummary.CountAsync() >= 1);
    }

    [Fact]
    public async Task Scenarios_outside_rules_render_without_rule_wrapper()
    {
        await Page.GotoReportAsync(GenerateReportWithRules("RuleNoWrapper.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator("details.feature summary").First.ClickAsync();

        // The first feature has 1 scenario outside any rule ("Health check returns OK")
        // It should be a direct child of the feature, not inside a details.rule
        var firstFeature = Page.Locator("details.feature").First;
        var directScenarios = firstFeature.Locator(":scope > details.scenario");
        Assert.True(await directScenarios.CountAsync() >= 1,
            "Expected at least 1 scenario as a direct child of the feature (not inside a rule)");
    }

    [Fact]
    public async Task Rule_sections_contain_correct_number_of_scenarios()
    {
        await Page.GotoReportAsync(GenerateReportWithRules("RuleScenarioCount.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator("details.feature summary").First.ClickAsync();

        var firstRule = Page.Locator("details.feature").First.Locator("details.rule").First;
        await firstRule.WaitForAsync();

        // "Valid Order Creation" should contain 2 scenarios
        var scenariosInFirstRule = firstRule.Locator("details.scenario");
        Assert.Equal(2, await scenariosInFirstRule.CountAsync());

        // "Invalid Order Handling" should contain 2 scenarios
        var secondRule = Page.Locator("details.feature").First.Locator("details.rule").Nth(1);
        var scenariosInSecondRule = secondRule.Locator("details.scenario");
        Assert.Equal(2, await scenariosInSecondRule.CountAsync());
    }

    [Fact]
    public async Task Feature_with_only_rules_has_no_direct_scenarios()
    {
        await Page.GotoReportAsync(GenerateReportWithRules("RuleOnlyFeature.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // Expand the second feature ("Payment Feature") which has only rules
        await Page.Locator("details.feature").Nth(1).Locator("summary").First.ClickAsync();

        var secondFeature = Page.Locator("details.feature").Nth(1);
        var directScenarios = secondFeature.Locator(":scope > details.scenario");
        Assert.Equal(0, await directScenarios.CountAsync());

        // But should have scenarios inside the rule
        var rule = secondFeature.Locator("details.rule").First;
        var scenariosInRule = rule.Locator("details.scenario");
        Assert.Equal(2, await scenariosInRule.CountAsync());
    }

    [Fact]
    public async Task Multiple_rules_render_as_separate_collapsible_sections()
    {
        await Page.GotoReportAsync(GenerateReportWithRules("RuleMultiple.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator("details.feature summary").First.ClickAsync();

        // Both rules should be open and collapsible (have the details element)
        var rules = Page.Locator("details.feature").First.Locator("details.rule");
        Assert.Equal(2, await rules.CountAsync());

        // Click to collapse the first rule
        await rules.First.Locator("summary").First.ClickAsync();

        // The first rule should now be closed (no 'open' attribute)
        Assert.Null(await rules.First.GetAttributeAsync("open"));

        // The second rule should still be open
        Assert.NotNull(await rules.Nth(1).GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Search_hides_rule_when_all_child_scenarios_filtered_out()
    {
        await Page.GotoReportAsync(GenerateReportWithRules("RuleSearchHide.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // Search for "express" which only matches 1 scenario in "Valid Order Creation" rule
        await FillSearchBar("express");

        // Wait for the filter to settle — rule[0] visible, rule[1] hidden
        await Page.WaitForFunctionAsync("""
            () => {
                var rules = document.querySelectorAll('.rule');
                if (rules.length < 2) return false;
                return getComputedStyle(rules[0]).display !== 'none' &&
                       getComputedStyle(rules[1]).display === 'none';
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        // Verify the hidden rule is truly invisible in the viewport
        await Expect(Page.Locator("details.rule").Nth(1)).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Search_shows_rule_when_cleared()
    {
        await Page.GotoReportAsync(GenerateReportWithRules("RuleSearchRestore.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await FillSearchBar("express");

        await Page.WaitForFunctionAsync("""
            () => {
                var rules = document.querySelectorAll('.rule');
                return rules.length >= 2 &&
                    getComputedStyle(rules[1]).display === 'none';
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        // Clear search
        await FillSearchBar("");

        // Wait for rules to be restored — none should be display:none
        await Page.WaitForFunctionAsync("""
            () => {
                var rules = document.querySelectorAll('.rule');
                return Array.from(rules).every(r => getComputedStyle(r).display !== 'none');
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Status_filter_hides_rule_when_all_child_scenarios_filtered()
    {
        await Page.GotoReportAsync(GenerateReportWithRules("RuleStatusFilter.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await OpenFilterRowsAsync();
        // Click the "Failed" status toggle to only show failed scenarios
        await Page.Locator(".status-toggle[data-status='Failed']").ClickAsync();

        // Wait for filter to settle
        await Page.WaitForFunctionAsync("""
            () => {
                var rules = document.querySelectorAll('.rule');
                if (rules.length < 2) return false;
                return getComputedStyle(rules[0]).display === 'none' &&
                       getComputedStyle(rules[1]).display !== 'none';
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        // Verify the hidden rule is truly invisible
        await Expect(Page.Locator("details.rule").First).ToBeHiddenAsync();
    }
}
