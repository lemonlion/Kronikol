# S4: the tests that opened the 4.x Features Summary disclosure open the Features tab's Summary view (D21).
def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode('utf-8')
    assert s.count(old) == count, (path, s.count(old), old[:90])
    open(path, 'wb').write(s.replace(old, new).encode('utf-8'))

E = 'tests/Kronikol.Tests.EndToEnd/'
D = E + 'DurationPaintTests.cs'
sub(D, '''        await Page.Locator("details.features-summary-details > summary").ClickAsync();
        var table = Page.Locator("table.feature-summary-table");
        await Expect(table).ToBeVisibleAsync();

        async Task<string[]> SortBy(string column)
        {
            await table.Locator("th", new() { HasTextString = column }).First.ClickAsync();''', '''        // 5.0.0: the table is the Features tab's Summary view, and the Duration group's first column reads Total, the
        // last of the header row's Total columns (plans/OPTION_C_PLAN.md S4)
        await Page.Locator("#view-summary").ClickAsync();
        var table = Page.Locator("table.feature-summary-table");
        await Expect(table).ToBeVisibleAsync();

        async Task<string[]> SortBy(string column)
        {
            await table.Locator("thead tr:last-child th", new() { HasTextString = column }).Last.ClickAsync();''')
sub(D, '''        Assert.Equal(["Long", "Middle", "Short"], await SortBy("Duration"));
        Assert.Equal(["Short", "Middle", "Long"], await SortBy("Duration"));''', '''        Assert.Equal(["Long", "Middle", "Short"], await SortBy("Total"));
        Assert.Equal(["Short", "Middle", "Long"], await SortBy("Total"));''')

M = E + 'MobileResponsiveTests.cs'
sub(M, '''        await Page.Locator(".features-summary-details").WaitForAsync(new() { Timeout = 5000 });

        await Page.Locator(".features-summary-details > summary").ClickAsync();''', '''        // 5.0.0: the table is the Features tab's Summary view (plans/OPTION_C_PLAN.md S4)
        await Page.Locator("#view-summary").ClickAsync();''')

T = E + 'ToggleDefaultsTests.cs'
sub(T, '''    public async Task Features_summary_open_default_opens_the_summary_table()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateToggleDefaultsReport(
            TempDir, OutputDir, "Toggle_FeaturesSummaryOpen.html",
            o => o.TestRunReportToggleDefaults.FeaturesSummaryOpen = true));
        await Page.Locator("details.feature").First.WaitForAsync();

        Assert.True(await Page.EvaluateAsync<bool>(
            "() => document.querySelector('details.features-summary-details').open"));
        Assert.True(await Page.Locator(".feature-summary-table").IsVisibleAsync());
    }''', '''    public async Task Features_summary_open_default_opens_the_summary_table()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateToggleDefaultsReport(
            TempDir, OutputDir, "Toggle_FeaturesSummaryOpen.html",
            o => o.TestRunReportToggleDefaults.FeaturesSummaryOpen = true));
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });

        // 5.0.0: the Features tab opens on its Summary view, the table in place of the cards (plans/OPTION_C_PLAN.md S4)
        await Expect(Page.Locator("#view-summary")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Page.Locator(".feature-summary-table")).ToBeVisibleAsync();
        await Expect(Page.Locator("#report-content")).ToBeHiddenAsync();
    }''')

W = E + 'WikiGifTests.cs'
sub(W, '''        try { await JsClick(".features-summary-details"); await Hold(2.5); } catch { }''',
    '''        try { await JsClick("#view-summary"); await Hold(2.5); } catch { } // 5.0.0: the Summary view (OPTION_C_PLAN S4)''')

S = E + 'WikiScreenshotTests.cs'
sub(S, '''                    var details = document.querySelectorAll('details');
                    details.forEach(d => { if (d.textContent.includes('Features Summary')) d.open = true; });''',
    '''                    // 5.0.0: the features table is the Features tab's Summary view (plans/OPTION_C_PLAN.md S4)
                    var summary = document.getElementById('view-summary');
                    if (summary) summary.click();''')

sub(D, '''/// browser drops: every bar was painted at its 2 px minimum and the chart lost its rings. And the Features Summary''',
    '''/// browser drops: every bar was painted at its 2 px minimum and the chart lost its rings. And the features table''')
print('ok')
