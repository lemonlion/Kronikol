using Kronikol.PlantUml;
using Kronikol.Reports;

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

    // ── The syntax tips: the query builder's since the search left the box (plans/SEARCH_PLAN.md 4.3.5) ──

    private async Task OpenBuilderAsync()
    {
        await Page.Locator("#search-button").ClickAsync();
        await Page.WaitForFunctionAsync("() => document.getElementById('search-dialog').open", null, new() { PollingInterval = 200 });
    }

    [Fact]
    public async Task Search_help_icon_is_present()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchHelpIcon.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await OpenBuilderAsync();

        await Expect(Page.Locator(".search-tips-toggle")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Search_help_panel_toggles_on_click()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchHelpToggle.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await OpenBuilderAsync();

        var toggle = Page.Locator(".search-tips-toggle");
        var tips = Page.Locator("#search-tips");
        await Expect(tips).ToBeHiddenAsync();

        await toggle.ClickAsync();
        await Expect(tips).ToBeVisibleAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");

        await toggle.ClickAsync();
        await Expect(tips).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Search_help_panel_contains_syntax_reference()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchHelpContent.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await OpenBuilderAsync();
        await Page.Locator(".search-tips-toggle").ClickAsync();

        var text = await Page.Locator("#search-tips").TextContentAsync();
        Assert.Contains("&&", text!);
        Assert.Contains("||", text!);
        Assert.Contains("!!", text!);
        Assert.Contains("@tag", text!);
        Assert.Contains("$status", text!);
        Assert.Contains("tag:name", text!);
        Assert.Contains("status:failed", text!);
        Assert.Contains("grouping", text!.ToLowerInvariant());
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

    // ── The forms SEARCH_PLAN 4.4 adds: tag:, status:, feature: and a - before a qualifier ──

    /// <summary>
    /// A report whose first feature has an endpoint and a tag drawn after its name on its card, and a scenario skipped
    /// after a failure: Order Feature (Create order, tagged smoke; Delete order, failed; Order history after the failure,
    /// skipped after a failure), Payment Feature (Pay by card; Refund, skipped).
    /// </summary>
    private string GenerateFormsReport(string fileName)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Order Feature", Endpoint = "/api/payments", Tags = ["payments"],
                Scenarios =
                [
                    new Scenario { Id = "q1", DisplayName = "Create order", Result = ExecutionResult.Passed, Tags = ["smoke"] },
                    new Scenario { Id = "q2", DisplayName = "Delete order", Result = ExecutionResult.Failed, ErrorMessage = "the order was not deleted" },
                    new Scenario { Id = "q3", DisplayName = "Order history after the failure", Result = ExecutionResult.SkippedAfterFailure }
                ]
            },
            new Feature
            {
                DisplayName = "Payment Feature",
                Scenarios =
                [
                    new Scenario { Id = "q4", DisplayName = "Pay by card", Result = ExecutionResult.Passed },
                    new Scenario { Id = "q5", DisplayName = "Refund", Result = ExecutionResult.Skipped }
                ]
            }
        };
        var path = ReportGenerator.GenerateHtmlReport(
            [], features, DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Forms Report", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    // the names of the scenarios the search leaves showing, in page order, once they are exactly these
    private async Task SearchAndWaitForNames(string query, params string[] names)
    {
        await FillSearchBar(query);
        await Page.WaitForFunctionAsync(
            "names => JSON.stringify(Array.from(document.querySelectorAll('.scenario')).filter(s => getComputedStyle(s).display !== 'none')"
            + ".map(s => s.querySelector('summary').childNodes[0].textContent.trim())) === JSON.stringify(names)",
            names, new() { Timeout = 5000, PollingInterval = 200 });
    }

    // L3: the name is read as the text it was, and the field's edge turns amber as for a qualifier it cannot read
    [Fact]
    public async Task An_unknown_status_name_is_text_and_marks_the_input()
    {
        await Page.GotoReportAsync(GenerateReport("AdvSearchUnknownStatus.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForCount("status:nonsense", 0);
        await Expect(Page.Locator("#searchbar")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bkron-q-bad\b"));
        await Expect(Page.Locator("#searchbar")).ToHaveAttributeAsync("data-q-bad", "status:nonsense");

        await SearchAndWaitForCount("status:failed", 1);
        await Expect(Page.Locator("#searchbar")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bkron-q-bad\b"));
        Assert.Null(await Page.Locator("#searchbar").GetAttributeAsync("data-q-bad"));
    }

    // B4: the search agrees with the Status buttons, which count a scenario skipped after a failure as skipped since 5.9.1 (E3)
    [Fact]
    public async Task Status_skipped_finds_a_scenario_skipped_after_a_failure()
    {
        await Page.GotoReportAsync(GenerateFormsReport("AdvSearchSkippedAfterFailure.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForNames("$failed", "Delete order");
        await SearchAndWaitForNames("status:failed", "Delete order");
        await SearchAndWaitForNames("$skipped", "Order history after the failure", "Refund");
        await SearchAndWaitForNames("status:skipped", "Order history after the failure", "Refund");
        await SearchAndWaitForNames("$skippedafterfailure", "Order history after the failure");
    }

    // L6 on a real card: the feature's name, never the endpoint or the tag drawn after it in its summary
    [Fact]
    public async Task Feature_colon_keeps_the_scenarios_whose_feature_name_holds_it()
    {
        await Page.GotoReportAsync(GenerateFormsReport("AdvSearchFeature.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForNames("feature:payment", "Pay by card", "Refund");
        await SearchAndWaitForNames("feature:\"order feature\"", "Create order", "Delete order", "Order history after the failure");
        await SearchAndWaitForNames("feature:ORDER,payment", "Create order", "Delete order", "Order history after the failure", "Pay by card", "Refund");
        await SearchAndWaitForNames("-feature:order", "Pay by card", "Refund");
        Assert.Null(await Page.Locator("#searchbar").GetAttributeAsync("data-q-bad"));
    }

    // L1 and L4 on a real page, with its deep search running
    [Fact]
    public async Task Tag_colon_and_a_minus_before_it_read_as_at_tag_and_its_negation()
    {
        await Page.GotoReportAsync(GenerateFormsReport("AdvSearchTagColon.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForNames("tag:smoke", "Create order");
        await SearchAndWaitForNames("tag:payments -tag:smoke", "Delete order", "Order history after the failure");
        // a scenario skipped after a failure is skipped, not failed, since 5.9.1 (E3, B4)
        await SearchAndWaitForNames("order -status:failed", "Create order", "Order history after the failure");
    }
}