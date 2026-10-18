using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Diagrams)]
public class DependencyColoringTests : PlaywrightTestBase
{
    public DependencyColoringTests(PlaywrightFixture fixture) : base(fixture) { }

    // ═══════════════════════════════════════════════════════════
    // Sequence Diagram — Colored Arrows
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Sequence_diagram_renders_SVG_with_colored_arrows()
    {
        await Page.GotoAsync(GenerateReport("DepColorArrows.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = (await WaitForDiagramSvg()).First;

        var svgHtml = await svg.EvaluateAsync<string>("el => el.outerHTML");
        Assert.Contains("svg", svgHtml);
        Assert.True(svgHtml.Length > 100, "SVG should contain rendered diagram content");
    }

    // ═══════════════════════════════════════════════════════════
    // Embedded Component Diagram
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Component_diagram_is_hidden_by_default()
    {
        await Page.GotoAsync(GenerateReportWithEmbeddedComponentDiagram("ComponentHiddenDefault.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // since 5.0.0 hidden as its tab's panel until the tab is chosen (plans/OPTION_C_PLAN.md S3)
        await Expect(Page.Locator("#component-diagram")).ToBeHiddenAsync();
        await Expect(Page.Locator("#panel-components")).ToHaveAttributeAsync("hidden", "");
    }

    // ── The Components tab (5.0.0, plans/OPTION_C_PLAN.md S3): the Component Diagram toggle became a tab of the
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

        var svg = Page.Locator("#component-diagram [data-diagram-type='plantuml'] svg");
        await svg.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        var svgHtml = await svg.EvaluateAsync<string>("el => el.outerHTML");
        Assert.Contains("svg", svgHtml);
    }

    [Fact]
    public async Task Report_without_component_diagram_has_no_section()
    {
        await Page.GotoAsync(GenerateReport("NoComponentSection.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var count = await Page.Locator(".component-diagram-section").CountAsync();
        Assert.Equal(0, count);
    }
}