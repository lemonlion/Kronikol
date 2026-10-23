namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Search)]
public class AdvancedSearchTests : PlaywrightTestBase
{
    public AdvancedSearchTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task<int> GetVisibleScenarioCount()
    {
        return await Page.EvaluateAsync<int>("""
            () => Array.from(document.querySelectorAll('.scenario'))
                .filter(s => getComputedStyle(s).display !== 'none').length
        """);
    }

    private async Task SearchAndWaitForCount(string query, int expectedCount)
    {
        await FillSearchBar(query);
        await Page.WaitForFunctionAsync(
            $"() => Array.from(document.querySelectorAll('.scenario')).filter(s => getComputedStyle(s).display !== 'none').length === {expectedCount}",
            null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    // ── AND operator (&&) ──

    [Fact]
    public async Task And_operator_matches_scenarios_containing_both_terms()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchAnd.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForCount("order && successfully", 1);

        var search = await Page.EvaluateAsync<string>("""
            () => Array.from(document.querySelectorAll('.scenario'))
                .filter(s => getComputedStyle(s).display !== 'none')[0]
                .getAttribute('data-search')
        """);
        Assert.Contains("create order", search);
    }

    [Fact]
    public async Task And_operator_hides_all_when_no_scenario_matches_both_terms()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchAndNoMatch.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForCount("payment && order", 0);
    }

    // ── OR operator (||) ──

    [Fact]
    public async Task Or_operator_matches_scenarios_containing_either_term()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchOr.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await FillSearchBar("payment || delete");

        await Page.WaitForFunctionAsync("""
            () => {
                var vis = Array.from(document.querySelectorAll('.scenario'))
                    .filter(s => getComputedStyle(s).display !== 'none');
                return vis.length === 3 && vis.every(s => {
                    var d = s.getAttribute('data-search');
                    return d.includes('payment') || d.includes('delete');
                });
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        Assert.Equal(3, await GetVisibleScenarioCount());
    }

    // ── NOT operator (!!) ──

    [Fact]
    public async Task Not_operator_excludes_matching_scenarios()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchNot.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await FillSearchBar("order && !!delete");

        await Page.WaitForFunctionAsync("""
            () => {
                var vis = Array.from(document.querySelectorAll('.scenario'))
                    .filter(s => getComputedStyle(s).display !== 'none');
                return vis.length === 2 && vis.every(s => !s.getAttribute('data-search').includes('delete'));
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        Assert.Equal(2, await GetVisibleScenarioCount());
    }

    // ── $status filter ──

    [Fact]
    public async Task Status_filter_matches_scenarios_by_status()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchStatus.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForCount("$failed", 1);

        var status = await Page.EvaluateAsync<string>("""
            () => Array.from(document.querySelectorAll('.scenario'))
                .filter(s => getComputedStyle(s).display !== 'none')[0]
                .getAttribute('data-status')
        """);
        Assert.Equal("Failed", status);
    }

    [Fact]
    public async Task Status_filter_combined_with_text_using_and()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchStatusAnd.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForCount("$passed && order", 2);
    }

    // ── @tag filter ──

    [Fact]
    public async Task Tag_filter_matches_scenarios_by_category()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchTag.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForCount("@smoke", 2);
    }

    [Fact]
    public async Task Tag_with_and_operator_narrows_results()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchTagAnd.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForCount("@api && @smoke", 1);
    }

    // ── Parenthesized grouping ──

    [Fact]
    public async Task Parentheses_change_evaluation_order()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchParens.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForCount("payment || (order && delete)", 3);
    }

    // ── Mixed operators ──

    [Fact]
    public async Task Mixed_text_tag_and_status_operators()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchMixed.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForCount("@api && $passed", 1);
    }

    // ── Help icon / tooltip ──

    [Fact]
    public async Task Search_help_icon_is_present()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchHelpIcon.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Expect(Page.Locator(".search-help-toggle")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Search_help_panel_toggles_on_click()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchHelpToggle.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var helpIcon = Page.Locator(".search-help-toggle");
        var helpPanel = Page.Locator(".search-help-panel");

        await Expect(helpPanel).ToHaveCSSAsync("display", "none");

        await helpIcon.ClickAsync();
        await Expect(helpPanel).Not.ToHaveCSSAsync("display", "none");

        await helpIcon.ClickAsync();
        await Expect(helpPanel).ToHaveCSSAsync("display", "none");
    }

    [Fact]
    public async Task Search_help_panel_contains_syntax_reference()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchHelpContent.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator(".search-help-toggle").ClickAsync();

        var text = await Page.Locator(".search-help-panel").TextContentAsync();
        Assert.Contains("&&", text!);
        Assert.Contains("||", text!);
        Assert.Contains("!!", text!);
        Assert.Contains("@tag", text!);
        Assert.Contains("$status", text!);
        Assert.Contains("parentheses", text!.ToLowerInvariant());
    }

    // ── Feature name search ──

    [Fact]
    public async Task Search_by_feature_name_shows_all_scenarios_in_that_feature()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchFeatureName.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForCount("order feature", 3);
    }

    [Fact]
    public async Task Search_by_feature_name_hides_other_features()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchFeatureNameFilter.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForCount("payment feature", 2);

        var visibleFeatures = await Page.EvaluateAsync<int>("""
            () => Array.from(document.querySelectorAll('details.feature'))
                .filter(f => getComputedStyle(f).display !== 'none').length
        """);
        Assert.Equal(1, visibleFeatures);
    }
}