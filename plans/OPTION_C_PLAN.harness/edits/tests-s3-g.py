# S3: DependencyColoringTests' component diagram toggle tests, as tab tests (5.0.0: the Components tab).
P = 'tests/Kronikol.Tests.EndToEnd/DependencyColoringTests.cs'
s = open(P, 'rb').read().decode()
a = s.index('    [Fact]\n    public async Task Component_diagram_toggle_button_exists()')
end_marker = '''        var button = Page.Locator("button[onclick*='toggle_component_diagram']");
        await button.WaitForAsync(new() { Timeout = 5000 });
        await button.ClickAsync();

        var svg = Page.Locator("#component-diagram [data-diagram-type='plantuml'] svg");'''
b = s.index(end_marker, a)
new = '''    // ── The Components tab (5.0.0, plans/OPTION_C_PLAN.md S3): the Component Diagram toggle became a tab of the
    // report bar, one panel at a time, so the toggle's own facts are the tab's ──

    [Fact]
    public async Task Component_diagram_tab_exists()
    {
        await Page.GotoAsync(GenerateReportWithEmbeddedComponentDiagram("ComponentToggleBtn.html"));

        var tab = Page.Locator("#tab-components");
        await Expect(tab).ToBeVisibleAsync();
        await Expect(tab).ToHaveTextAsync("Components");
    }

    [Fact]
    public async Task Component_diagram_tab_not_present_without_diagram()
    {
        await Page.GotoAsync(GenerateReport("NoComponentToggle.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        Assert.Equal(0, await Page.Locator("#tab-components").CountAsync());
        Assert.Equal(0, await Page.Locator("button[onclick*='toggle_component_diagram']").CountAsync());
    }

    [Fact]
    public async Task Component_diagram_tab_shows_its_panel_and_another_tab_hides_it()
    {
        await Page.GotoAsync(GenerateReportWithEmbeddedComponentDiagram("ComponentToggleShowHide.html"));
        var panel = Page.Locator("#panel-components");
        await Expect(panel).ToBeHiddenAsync();

        await OpenReportTabAsync("components");
        await Expect(panel).ToBeVisibleAsync();

        await OpenReportTabAsync("features");
        await Expect(panel).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Choosing_the_components_tab_hides_the_timeline()
    {
        await Page.GotoAsync(GenerateReportWithEmbeddedComponentDiagram("ComponentRadioTimeline.html"));

        await OpenReportTabAsync("timeline");
        await Expect(Page.Locator("#scenario-timeline")).ToBeVisibleAsync();

        await OpenReportTabAsync("components");
        await Expect(Page.Locator("#component-diagram")).ToBeVisibleAsync();
        await Expect(Page.Locator("#scenario-timeline")).ToBeHiddenAsync();
        await Expect(Page.Locator("#tab-timeline")).ToHaveAttributeAsync("aria-selected", "false");
        await Expect(Page.Locator("#tab-components")).ToHaveAttributeAsync("aria-selected", "true");
    }

    [Fact]
    public async Task Choosing_the_timeline_tab_hides_the_component_diagram()
    {
        await Page.GotoAsync(GenerateReportWithEmbeddedComponentDiagram("TimelineRadioComponent.html"));

        await OpenReportTabAsync("components");
        await Expect(Page.Locator("#component-diagram")).ToBeVisibleAsync();

        await OpenReportTabAsync("timeline");
        await Expect(Page.Locator("#scenario-timeline")).ToBeVisibleAsync();
        await Expect(Page.Locator("#component-diagram")).ToBeHiddenAsync();
        await Expect(Page.Locator("#tab-components")).ToHaveAttributeAsync("aria-selected", "false");
        await Expect(Page.Locator("#tab-timeline")).ToHaveAttributeAsync("aria-selected", "true");
    }

    [Fact]
    public async Task The_components_tab_is_selected_while_its_panel_shows()
    {
        await Page.GotoAsync(GenerateReportWithEmbeddedComponentDiagram("ComponentToggleActive.html"));
        var tab = Page.Locator("#tab-components");
        await Expect(tab).ToHaveAttributeAsync("aria-selected", "false");

        await OpenReportTabAsync("components");
        await Expect(tab).ToHaveAttributeAsync("aria-selected", "true");

        await OpenReportTabAsync("features");
        await Expect(tab).ToHaveAttributeAsync("aria-selected", "false");
    }

    [Fact]
    public async Task Component_diagram_renders_SVG_after_toggle()
    {
        await Page.GotoAsync(GenerateReportWithEmbeddedComponentDiagram("ComponentSvgToggle.html"));

        await OpenReportTabAsync("components");

        var svg = Page.Locator("#component-diagram [data-diagram-type='plantuml'] svg");'''
s = s[:a] + new + s[b + len(end_marker):]
open(P, 'wb').write(s.encode())
print('ok')
