using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Zoom)]
public class DiagramZoomTests : PlaywrightTestBase
{
    public DiagramZoomTests(PlaywrightFixture fixture) : base(fixture) { }

    private ILocator GetDiagramContainer() =>
        Page.Locator("[data-diagram-type='plantuml']").First;

    private async Task<string> GetComputedStyle(ILocator el, string prop) =>
        await el.EvaluateAsync<string>($"(e, p) => window.getComputedStyle(e).getPropertyValue(p)", prop);

    // ── Zoom slider ──

    [Fact]
    public async Task Zoom_slider_appears_on_diagram_container()
    {
        await Page.GotoAsync(GenerateReportWithWideDiagram("ZoomSliderExists.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.diagram-zoom-slider').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        var count = await Page.Locator(".diagram-zoom-slider").CountAsync();
        Assert.True(count > 0, "Zoom slider should exist on diagram container");
    }

    [Fact]
    public async Task Zoom_controls_hidden_until_hover()
    {
        await Page.GotoAsync(GenerateReportWithWideDiagram("ZoomHoverHidden.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.diagram-zoom-controls').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        var zoomControls = Page.Locator(".diagram-zoom-controls").First;
        var opacity = await GetComputedStyle(zoomControls, "opacity");
        Assert.Equal("0", opacity);
    }

    [Fact]
    public async Task Zoom_controls_become_visible_on_container_hover()
    {
        await Page.GotoAsync(GenerateReportWithWideDiagram("ZoomHoverVisible.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.diagram-zoom-controls').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        var container = GetDiagramContainer();
        await container.HoverAsync();

        await Page.WaitForFunctionAsync("""
            () => {
                var ctrl = document.querySelector('.diagram-zoom-controls');
                return ctrl && window.getComputedStyle(ctrl).opacity !== '0';
            }
        """, null, new() { Timeout = 3000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Slider_at_max_adds_natural_size_class()
    {
        await Page.GotoAsync(GenerateReportWithWideDiagram("ZoomSliderNatural.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.diagram-zoom-slider').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        var container = GetDiagramContainer();

        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelector('.diagram-zoom-slider');
                slider.value = '100';
                slider.dispatchEvent(new Event('input', { bubbles: true }));
            }
        """);

        await Expect(container).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("diagram-natural-size"));
    }

    [Fact]
    public async Task Slider_at_min_removes_natural_size_class()
    {
        await Page.GotoAsync(GenerateReportWithWideDiagram("ZoomSliderToggle.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.diagram-zoom-slider').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        var container = GetDiagramContainer();

        // Zoom in
        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelector('.diagram-zoom-slider');
                slider.value = '100';
                slider.dispatchEvent(new Event('input', { bubbles: true }));
            }
        """);
        await Expect(container).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("diagram-natural-size"));

        // Zoom back to min
        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelector('.diagram-zoom-slider');
                slider.value = slider.min;
                slider.dispatchEvent(new Event('input', { bubbles: true }));
            }
        """);
        await Expect(container).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("diagram-natural-size"));
    }

    [Fact]
    public async Task Zoomed_container_has_overflow_x_auto()
    {
        await Page.GotoAsync(GenerateReportWithWideDiagram("ZoomOverflow.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.diagram-zoom-slider').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        var container = GetDiagramContainer();

        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelector('.diagram-zoom-slider');
                slider.value = '100';
                slider.dispatchEvent(new Event('input', { bubbles: true }));
            }
        """);

        var overflowX = await container.EvaluateAsync<string>("e => e.style.overflowX");
        Assert.Equal("auto", overflowX);
        var overflowY = await container.EvaluateAsync<string>("e => e.style.overflowY");
        Assert.True(overflowY is "" or "visible", $"Expected no vertical overflow constraint but got: {overflowY}");
    }

    [Fact]
    public async Task Unzooming_clears_overflow()
    {
        await Page.GotoAsync(GenerateReportWithWideDiagram("ZoomClearOverflow.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.diagram-zoom-slider').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        var container = GetDiagramContainer();

        // Zoom in then out via slider
        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelector('.diagram-zoom-slider');
                slider.value = '100';
                slider.dispatchEvent(new Event('input', { bubbles: true }));
                slider.value = slider.min;
                slider.dispatchEvent(new Event('input', { bubbles: true }));
            }
        """);

        var overflowX = await container.EvaluateAsync<string>("e => e.style.overflowX");
        Assert.True(overflowX is "" or "visible", $"Expected overflow-x to be cleared but got: {overflowX}");
    }

    // ── Click-to-deselect ──

    [Fact]
    public async Task Clicking_selected_diagram_deselects_it()
    {
        await Page.GotoAsync(GenerateReportWithWideDiagram("ClickDeselect.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.diagram-zoom-controls').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        var container = GetDiagramContainer();

        // First click selects
        await container.EvaluateAsync("el => el.click()");
        await Expect(container).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("diagram-selected"));

        // Second click deselects
        await container.EvaluateAsync("el => el.click()");
        await Expect(container).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("diagram-selected"));
    }

    // ── Non-zoomable diagrams ──

    [Fact]
    public async Task Non_zoomable_diagram_has_no_zoom_slider()
    {
        await Page.GotoAsync(GenerateReport("NonZoomableNoSlider.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        // Wait a bit for any zoom controls to potentially appear
        await Page.WaitForTimeoutAsync(500);

        var count = await Page.Locator(".diagram-zoom-slider").CountAsync();
        Assert.Equal(0, count);
    }



    [Fact]
    public async Task Text_in_steps_can_still_be_highlighted()
    {
        await Page.GotoAsync(GenerateReportWithWideDiagram("StepTextSelectable.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        var stepElement = Page.Locator(".scenario-steps li, .step, .scenario li").First;
        await Page.EvaluateAsync("""
            (el) => {
                var range = document.createRange();
                range.selectNodeContents(el);
                var sel = window.getSelection();
                sel.removeAllRanges();
                sel.addRange(range);
            }
        """, await stepElement.ElementHandleAsync());

        var selectedText = await Page.EvaluateAsync<string>("() => window.getSelection().toString()");
        Assert.NotEqual("", selectedText.Trim());
    }

    // ── Zoom slider persists after note collapse ──

    [Fact]
    public async Task Zoom_slider_persists_after_note_collapse()
    {
        await Page.GotoAsync(GenerateReportWithWideNoteDiagram("ZoomAfterNoteCollapse.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.diagram-zoom-slider').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        var container = GetDiagramContainer();
        var before = await container.Locator(".diagram-zoom-slider").CountAsync();
        Assert.Equal(1, before);

        // Collapse notes
        await OpenScenarioSettingsAsync();
        await Page.Locator(".diagram-toggle .details-radio-btn[data-state='collapsed']").First.ClickAsync();

        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                var svg = c && c.querySelector('svg');
                return svg && svg.offsetParent !== null && c.querySelectorAll('.diagram-zoom-slider').length > 0;
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });

        var after = await container.Locator(".diagram-zoom-slider").CountAsync();
        Assert.Equal(1, after);
    }

    [Fact]
    public async Task Zoom_slider_works_after_note_collapse()
    {
        await Page.GotoAsync(GenerateReportWithWideNoteDiagram("ZoomWorksAfterNoteCollapse.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.diagram-zoom-slider').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        // Collapse notes
        await OpenScenarioSettingsAsync();
        await Page.Locator(".diagram-toggle .details-radio-btn[data-state='collapsed']").First.ClickAsync();

        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                var svg = c && c.querySelector('svg');
                return svg && svg.offsetParent !== null && c.querySelectorAll('.diagram-zoom-slider').length > 0;
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });

        // Zoom via slider
        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelector('[data-diagram-type="plantuml"] .diagram-zoom-slider');
                slider.value = '100';
                slider.dispatchEvent(new Event('input', { bubbles: true }));
            }
        """);

        await Expect(GetDiagramContainer()).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("diagram-natural-size"));
    }
}