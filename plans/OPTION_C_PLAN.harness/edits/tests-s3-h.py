# S3: ToggleDefaultsTests' panel facts, for the tab the page opens on (Q2 as built).
P = 'tests/Kronikol.Tests.EndToEnd/ToggleDefaultsTests.cs'
s = open(P, 'rb').read().decode()
a = s.index('    [Fact]\n    public async Task Timeline_visible_default_shows_the_panel_and_seeds_the_button()')
b = s.index('    // ═══════════════════════════════════════════════════════════\n    // Tier 2', a)
new = '''    // Since 5.0.0 the two panels are tabs of the report bar, and each option chooses the tab the page opens on
    // (plans/OPTION_C_PLAN.md S3, Q2 as built).

    [Fact]
    public async Task Timeline_visible_default_opens_the_page_on_the_timeline_tab()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateComponentTimelineToggleDefaultsReport(
            TempDir, OutputDir, "Toggle_TimelineVisible.html",
            o => o.TestRunReportToggleDefaults.ScenarioTimelineVisible = true));
        await Page.Locator(".report-tabs").WaitForAsync();

        Assert.True(await Page.Locator("#scenario-timeline").IsVisibleAsync());
        await Expect(Page.Locator("#tab-timeline")).ToHaveAttributeAsync("aria-selected", "true");
        Assert.False(await Page.Locator("#panel-features").IsVisibleAsync());

        // the page that opened on the timeline still goes to the features
        await OpenReportTabAsync("features");
        Assert.False(await Page.Locator("#scenario-timeline").IsVisibleAsync());
    }

    [Fact]
    public async Task Component_diagram_visible_default_opens_the_page_on_the_components_tab()
    {
        await Page.GotoAsync(ReportTestHelper.GenerateComponentTimelineToggleDefaultsReport(
            TempDir, OutputDir, "Toggle_ComponentVisible.html",
            o => o.TestRunReportToggleDefaults.ComponentDiagramVisible = true));
        await Page.Locator(".report-tabs").WaitForAsync();

        Assert.True(await Page.Locator("#component-diagram").IsVisibleAsync());
        Assert.False(await Page.Locator("#scenario-timeline").IsVisibleAsync());
        await Expect(Page.Locator("#tab-components")).ToHaveAttributeAsync("aria-selected", "true");
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
        await Page.Locator(".report-tabs").WaitForAsync();

        Assert.True(await Page.Locator("#scenario-timeline").IsVisibleAsync());
        Assert.False(await Page.Locator("#component-diagram").IsVisibleAsync());
        // The component diagram stays one click away
        await OpenReportTabAsync("components");
        Assert.True(await Page.Locator("#component-diagram").IsVisibleAsync());
        Assert.False(await Page.Locator("#scenario-timeline").IsVisibleAsync());
    }

'''
s = s[:a] + new + s[b:]
open(P, 'wb').write(s.encode())
print('ok')
