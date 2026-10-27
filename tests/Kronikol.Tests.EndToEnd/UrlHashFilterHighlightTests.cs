using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Search)]
public class UrlHashFilterHighlightTests : PlaywrightTestBase
{
    public UrlHashFilterHighlightTests(PlaywrightFixture fixture) : base(fixture) { }

    private string GenerateAndServeReport(string fileName)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Order Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t1", DisplayName = "Create order", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2)
                    },
                    new Scenario
                    {
                        Id = "t2", DisplayName = "Delete order", IsHappyPath = false,
                        Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(5)
                    },
                    new Scenario
                    {
                        Id = "t3", DisplayName = "List orders", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1)
                    }
                ]
            }
        };

        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task<string> GetComputedBgColor(string selector) =>
        await Page.Locator(selector).EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");

    // A chosen filter is drawn in the selected tint (5.0.0, plans/OPTION_C_PLAN.md S6; 4.x: a solid rgb(66, 133, 244)),
    // read from the page as it resolves the token, so the check holds in either theme.
    private async Task<string> SelectedBgColor() => await Page.EvaluateAsync<string>(
        "() => { const p = document.createElement('span'); p.style.backgroundColor = 'var(--kron-selected)'; document.body.appendChild(p); const c = getComputedStyle(p).backgroundColor; p.remove(); return c; }");

    private async Task<bool> IsHighlighted(string bgColor) => bgColor == await SelectedBgColor();

    [Fact]
    public async Task Status_filter_buttons_highlighted_after_url_hash_restore()
    {
        var url = GenerateAndServeReport("UrlHashStatusHighlight.html");
        await Page.GotoReportAsync(url + "#status=Passed");
        await OpenFilterRowsAsync();

        var passedBtn = Page.Locator(".status-toggle[data-status='Passed']");
        await Expect(passedBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("status-active"));

        var bgColor = await GetComputedBgColor(".status-toggle[data-status='Passed']");
        Assert.True(await IsHighlighted(bgColor),
            $"Expected 'Passed' button to be highlighted (the selected tint) but got: {bgColor}");
    }

    [Fact]
    public async Task Status_filter_hides_scenarios_and_highlights_button_on_url_restore()
    {
        var url = GenerateAndServeReport("UrlHashStatusHideHighlight.html");
        await Page.GotoReportAsync(url + "#status=Failed");
        await OpenFilterRowsAsync();

        var failedBtn = Page.Locator(".status-toggle[data-status='Failed']");
        await Expect(failedBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("status-active"));

        Assert.True(await IsHighlighted(await GetComputedBgColor(".status-toggle[data-status='Failed']")),
            "Failed button should be highlighted after URL hash restore");

        var allHidden = await Page.EvaluateAsync<bool>("""
            () => Array.from(document.querySelectorAll('.scenario[data-status="Passed"]'))
                .every(s => getComputedStyle(s).display === 'none')
        """);
        Assert.True(allHidden);
    }

    [Fact]
    public async Task Happy_path_button_highlighted_after_url_hash_restore()
    {
        var url = GenerateAndServeReport("UrlHashHappyPathHighlight.html");
        await Page.GotoReportAsync(url + "#hp=1");
        await OpenFilterRowsAsync();

        // 5.0.0: hp=1 is Happy paths Only (plans/OPTION_C_PLAN.md S6)
        var hpBtn = Page.Locator(".filtering-box .happy-seg[data-hp='in']");
        await hpBtn.WaitForAsync();
        await Expect(hpBtn).ToHaveAttributeAsync("aria-pressed", "true");
        Assert.True(await IsHighlighted(await GetComputedBgColor(".filtering-box .happy-seg[data-hp='in']")));
    }

    [Fact]
    public async Task Search_text_restored_after_url_hash_restore()
    {
        var url = GenerateAndServeReport("UrlHashSearchRestore.html");
        await Page.GotoReportAsync(url + "#q=" + Uri.EscapeDataString("Create order"));

        // q= is the filter field's, put back in the field in the box (5.12.0, as until 5.10.1); the search stays as it was
        await Expect(Page.Locator("#searchbar")).ToHaveValueAsync("Create order");
        await Expect(Page.Locator("#search-button .search-button-query")).ToBeHiddenAsync();
        await OpenFilterRowsAsync();
        await Expect(Page.Locator("#filtering-box #searchbar")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Status_filter_highlighted_after_click_then_refresh()
    {
        var url = GenerateAndServeReport("UrlHashClickRefresh.html");
        await Page.GotoReportAsync(url);
        await OpenFilterRowsAsync();

        var passedBtn = Page.Locator(".status-toggle[data-status='Passed']");
        await passedBtn.WaitForAsync();
        await passedBtn.ClickAsync();

        Assert.True(await IsHighlighted(await GetComputedBgColor(".status-toggle[data-status='Passed']")));
        Assert.Contains("#", Page.Url);

        await Page.ReloadReportAsync();
        await OpenFilterRowsAsync();

        await Expect(passedBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("status-active"));
        Assert.True(await IsHighlighted(await GetComputedBgColor(".status-toggle[data-status='Passed']")));
    }

    [Fact]
    public async Task Happy_path_highlighted_after_click_then_refresh()
    {
        var url = GenerateAndServeReport("UrlHashHPClickRefresh.html");
        await Page.GotoReportAsync(url);
        await OpenFilterRowsAsync();

        // 5.0.0: Happy paths Only (plans/OPTION_C_PLAN.md S6)
        const string only = ".filtering-box .happy-seg[data-hp='in']";
        var hpBtn = Page.Locator(only);
        await hpBtn.WaitForAsync();
        await hpBtn.ClickAsync();

        Assert.True(await IsHighlighted(await GetComputedBgColor(only)));

        await Page.ReloadReportAsync();
        await OpenFilterRowsAsync();

        await hpBtn.WaitForAsync(new() { Timeout = 10000 });
        await Expect(hpBtn).ToHaveAttributeAsync("aria-pressed", "true");
        Assert.True(await IsHighlighted(await GetComputedBgColor(only)));
    }

    [Fact]
    public async Task Multiple_status_filters_highlighted_after_click_then_refresh()
    {
        var url = GenerateAndServeReport("UrlHashMultiStatusRefresh.html");
        await Page.GotoReportAsync(url);
        await OpenFilterRowsAsync();

        await Page.Locator(".status-toggle[data-status='Passed']").ClickAsync();
        await Page.Locator(".status-toggle[data-status='Failed']").ClickAsync();

        await Page.ReloadReportAsync();
        await OpenFilterRowsAsync();

        var passedBtn = Page.Locator(".status-toggle[data-status='Passed']");
        await Expect(passedBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("status-active"));

        var failedBtn2 = Page.Locator(".status-toggle[data-status='Failed']");
        await Expect(failedBtn2).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("status-active"));

        var skippedCls = await Page.Locator(".status-toggle[data-status='Skipped']").GetAttributeAsync("class");
        Assert.DoesNotContain("status-active", skippedCls!);
    }

    [Fact]
    public async Task Status_and_happy_path_both_highlighted_after_click_then_refresh()
    {
        var url = GenerateAndServeReport("UrlHashComboRefresh.html");
        await Page.GotoReportAsync(url);
        await OpenFilterRowsAsync();

        await Page.Locator(".status-toggle[data-status='Failed']").ClickAsync();
        await Page.Locator(".filtering-box .happy-seg[data-hp='in']").ClickAsync();

        await Page.ReloadReportAsync();
        await OpenFilterRowsAsync();

        var failedBtn = Page.Locator(".status-toggle[data-status='Failed']");
        await Expect(failedBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("status-active"));

        var hpBtn2 = Page.Locator(".filtering-box .happy-seg[data-hp='in']");
        await Expect(hpBtn2).ToHaveAttributeAsync("aria-pressed", "true");
    }

    [Fact]
    public async Task Percentile_button_highlighted_after_click_then_refresh()
    {
        var url = GenerateAndServeReport("UrlHashP99Refresh.html");
        await Page.GotoReportAsync(url);
        await OpenFilterRowsAsync();

        var p99Btn = Page.Locator("button.percentile-btn", new() { HasTextString = "P99" });
        await p99Btn.WaitForAsync();
        await p99Btn.ClickAsync();

        await Expect(p99Btn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("percentile-active"));
        Assert.Contains("pctl=", Page.Url);

        await Page.ReloadReportAsync();
        await OpenFilterRowsAsync();

        await p99Btn.WaitForAsync(new() { Timeout = 10000 });
        await Expect(p99Btn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("percentile-active"));
        Assert.True(await IsHighlighted(await GetComputedBgColor("button.percentile-btn:has-text('P99')")));
    }

    [Fact]
    public async Task User_report_status_filter_highlighted_after_url_hash_restore()
    {
        var url = GenerateAndServeReport("UrlHashUserReport.html");
        await Page.GotoReportAsync(url + "#status=Passed");
        await OpenFilterRowsAsync();

        var passedBtn = Page.Locator(".status-toggle[data-status='Passed']");
        await Expect(passedBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("status-active"));

        var bgColor = await GetComputedBgColor(".status-toggle[data-status='Passed']");
        Assert.True(await IsHighlighted(bgColor),
            $"Expected 'Passed' button to be highlighted (the selected tint) but got: {bgColor}");
    }
}
