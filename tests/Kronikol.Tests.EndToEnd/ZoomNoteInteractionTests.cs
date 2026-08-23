using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Zoom)]
public class ZoomNoteInteractionTests : PlaywrightTestBase
{
    public ZoomNoteInteractionTests(PlaywrightFixture fixture) : base(fixture) { }

    private new string GenerateReport(string fileName) =>
        GenerateReportWithWideNoteDiagram(fileName);

    private ILocator GetDiagramContainer() =>
        Page.Locator("[data-diagram-type='plantuml']").First;

    private async Task ZoomIn()
    {
        // Wait for ALL diagram rendering to complete before interacting
        await WaitForAllDiagramsRendered();

        var container = GetDiagramContainer();
        await Page.WaitForFunctionAsync(
            "() => document.querySelector('[data-diagram-type=\"plantuml\"] .diagram-zoom-slider') !== null",
            null, new() { Timeout = 10000, PollingInterval = 200 });
        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelector('[data-diagram-type="plantuml"] .diagram-zoom-slider');
                slider.value = '100';
                slider.dispatchEvent(new Event('input', { bubbles: true }));
            }
        """);
        await Page.WaitForFunctionAsync(
            "() => document.querySelector('[data-diagram-type=\"plantuml\"]').classList.contains('diagram-natural-size')",
            null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    private async Task WaitForAllDiagramsRendered()
    {
        await Page.WaitForFunctionAsync("""
            () => {
                var cs = document.querySelectorAll('[data-diagram-type="plantuml"]');
                if (cs.length === 0) return false;
                for (var i = 0; i < cs.length; i++) {
                    if (!cs[i].querySelector('svg')) return false;
                }
                return !window._plantumlRendering;
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });
    }

    /// <summary>
    /// True once the diagram has re-rendered <em>and</em> the zoom state has been put back on it.
    /// <para>
    /// The second half matters. <c>setNoteState</c> clears its render flags on the line straight after
    /// <c>container.innerHTML = newSvg</c> — which has just destroyed the zoom controls and the inline
    /// <c>maxWidth</c>/<c>overflow</c> styles — and re-applies the zoom only from a
    /// <c>requestAnimationFrame</c> callback scheduled two lines later. Waiting on the flags alone
    /// therefore returns <em>inside</em> that one-frame window, where the container has no zoom class,
    /// the SVG's <c>maxWidth</c> is still <c>""</c>, and the slider is the one the previous layout built.
    /// Every assertion in this file reads exactly that state, which is why two of them failed in CI
    /// while passing locally.
    /// </para>
    /// <para>
    /// <c>restoreZoomState</c> runs in the same synchronous block that prepends the rebuilt controls, so
    /// their presence is a precise marker that the zoom is back.
    /// </para>
    /// </summary>
    private const string RenderSettled = """
        () => {
            var c = document.querySelector('[data-diagram-type="plantuml"]');
            if (!c) return false;
            if (!c.querySelector('svg')) return false;
            if (c._noteRendering || window._plantumlRendering) return false;
            return !!c.querySelector('.diagram-zoom-controls');
        }
        """;

    private async Task WaitForReRender(int timeoutMs = 30000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            var remaining = (float)Math.Max(1000, (deadline - DateTime.UtcNow).TotalMilliseconds);
            await Page.WaitForFunctionAsync(RenderSettled, null, new() { Timeout = remaining, PollingInterval = 200 });

            // Let any already-queued requestAnimationFrame callback run — then confirm it did not kick
            // off (or land in the middle of) another render. Without the re-check this could still
            // return against the *previous* render when the click's own work has not started yet.
            await Page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
            if (await Page.EvaluateAsync<bool>($"({RenderSettled})()"))
                return;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Diagram did not settle within {timeoutMs} ms.");
        }
    }

    private async Task ClickRadioButton(string state)
    {
        await Page.Locator($".diagram-toggle .details-radio-btn[data-state='{state}']").First.ClickAsync();
    }

    /// <summary>
    /// Drags the zoom slider to its minimum and waits for the zoom to come off.
    /// <para>
    /// The slider is destroyed and rebuilt by every re-render, so dispatching at it without first
    /// confirming it is there reads as a null dereference rather than as a failed expectation — and the
    /// zoom class is cleared by the handler, which is worth waiting for rather than sampling.
    /// </para>
    /// </summary>
    private async Task ZoomOutViaSlider()
    {
        await Page.WaitForFunctionAsync(
            "() => document.querySelector('[data-diagram-type=\"plantuml\"] .diagram-zoom-slider') !== null",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelector('[data-diagram-type="plantuml"] .diagram-zoom-slider');
                slider.value = slider.min;
                slider.dispatchEvent(new Event('input', { bubbles: true }));
            }
        """);

        await Page.WaitForFunctionAsync(
            "() => !document.querySelector('[data-diagram-type=\"plantuml\"]').classList.contains('diagram-natural-size')",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    private async Task<bool> IsZoomedIn() =>
        await Page.EvaluateAsync<bool>(
            "() => document.querySelector('[data-diagram-type=\"plantuml\"]').classList.contains('diagram-natural-size')");

    private async Task<string> GetSvgMaxWidth() =>
        await Page.EvaluateAsync<string>("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                var svg = c && c.querySelector('svg');
                return svg ? svg.style.maxWidth : '';
            }
        """);

    private async Task<string> GetContainerOverflow() =>
        await Page.EvaluateAsync<string>(
            "() => document.querySelector('[data-diagram-type=\"plantuml\"]').style.overflowX");

    // ── Zoom state preserved after note collapse ──

    [Fact]
    public async Task Zoom_state_preserved_after_note_collapse_via_radio()
    {
        await Page.GotoAsync(GenerateReport("ZoomPreservedAfterCollapse.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ZoomIn();
        Assert.True(await IsZoomedIn());
        Assert.Equal("none", await GetSvgMaxWidth());

        await ClickRadioButton("collapsed");
        await WaitForReRender();

        Assert.True(await IsZoomedIn(), "Container should still have diagram-natural-size class");
        Assert.Equal("none", await GetSvgMaxWidth());
        Assert.Equal("auto", await GetContainerOverflow());
    }

    [Fact]
    public async Task Zoom_state_preserved_after_truncation_change()
    {
        await Page.GotoAsync(GenerateReport("ZoomPreservedAfterTruncation.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ZoomIn();
        Assert.True(await IsZoomedIn());

        await ClickRadioButton("expanded");
        await WaitForReRender();

        Assert.True(await IsZoomedIn());
        Assert.Equal("none", await GetSvgMaxWidth());
        Assert.Equal("auto", await GetContainerOverflow());
    }

    [Fact]
    public async Task Zoom_state_preserved_after_headers_toggle()
    {
        await Page.GotoAsync(GenerateReport("ZoomPreservedAfterHeaders.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ZoomIn();
        Assert.True(await IsZoomedIn());

        var headersHidden = Page.Locator(".diagram-toggle .toggle-btn[data-toggle='headers'][data-shown='true']");
        if (await headersHidden.CountAsync() == 0) return;
        await headersHidden.First.ClickAsync();
        await WaitForReRender(60000);

        Assert.True(await IsZoomedIn());
        Assert.Equal("none", await GetSvgMaxWidth());
    }

    // ── Zoom toggle correct after re-render ──

    [Fact]
    public async Task Zoom_toggle_out_works_correctly_after_note_collapse()
    {
        await Page.GotoAsync(GenerateReport("ZoomToggleAfterCollapse.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ZoomIn();
        await ClickRadioButton("collapsed");
        await WaitForReRender();

        // Toggle zoom OFF via slider
        await ZoomOutViaSlider();
        Assert.False(await IsZoomedIn(), "Should have zoomed out");
        Assert.Equal("100%", await GetSvgMaxWidth());
        Assert.Equal("", await GetContainerOverflow());
    }

    [Fact]
    public async Task Zoom_toggle_in_again_after_collapse_and_unzoom()
    {
        await Page.GotoAsync(GenerateReport("ZoomToggleInAgain.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ZoomIn();
        await ClickRadioButton("collapsed");
        await WaitForReRender();

        // Zoom out via slider
        await ZoomOutViaSlider();
        Assert.False(await IsZoomedIn());

        // Zoom in again via slider
        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelector('[data-diagram-type="plantuml"] .diagram-zoom-slider');
                slider.value = '100';
                slider.dispatchEvent(new Event('input', { bubbles: true }));
            }
        """);
        Assert.True(await IsZoomedIn());
        Assert.Equal("none", await GetSvgMaxWidth());
    }

    // ── Note button interactions while zoomed ──

    [Fact]
    public async Task Note_collapse_button_works_while_zoomed()
    {
        await Page.GotoAsync(GenerateReport("NoteCollapseWhileZoomed.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ZoomIn();

        // Double-click first note to cycle state
        await Page.EvaluateAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                var rects = c.querySelectorAll('.note-hover-rect');
                if (rects.length > 0) rects[0].dispatchEvent(new MouseEvent('dblclick', {bubbles: true}));
            }
        """);
        await WaitForReRender();

        Assert.True(await IsZoomedIn());
        Assert.Equal("none", await GetSvgMaxWidth());
    }

    [Fact]
    public async Task Multiple_note_state_changes_while_zoomed_preserves_zoom()
    {
        await Page.GotoAsync(GenerateReport("MultiNoteChangesZoomed.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ZoomIn();

        await ClickRadioButton("collapsed");
        await WaitForReRender();
        Assert.True(await IsZoomedIn());

        await ClickRadioButton("expanded");
        await WaitForReRender();
        Assert.True(await IsZoomedIn());

        await ClickRadioButton("truncated");
        await WaitForReRender();
        Assert.True(await IsZoomedIn());
        Assert.Equal("none", await GetSvgMaxWidth());
    }

    // ── Zoom after unzoomed state + re-render ──

    [Fact]
    public async Task Unzoomed_state_not_affected_by_note_collapse()
    {
        await Page.GotoAsync(GenerateReport("UnzoomedNotAffected.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        Assert.False(await IsZoomedIn());

        await ClickRadioButton("collapsed");
        await WaitForReRender();

        Assert.False(await IsZoomedIn());
        Assert.Equal("100%", await GetSvgMaxWidth());
    }

    [Fact]
    public async Task Zoom_in_works_after_unzoomed_collapse()
    {
        await Page.GotoAsync(GenerateReport("ZoomInAfterCollapse.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ClickRadioButton("collapsed");
        await WaitForReRender();

        await Page.WaitForFunctionAsync(
            "() => document.querySelector('[data-diagram-type=\"plantuml\"] .diagram-zoom-slider') !== null",
            null, new() { Timeout = 10000, PollingInterval = 200 });
        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelector('[data-diagram-type="plantuml"] .diagram-zoom-slider');
                slider.value = '100';
                slider.dispatchEvent(new Event('input', { bubbles: true }));
            }
        """);

        Assert.True(await IsZoomedIn());
        Assert.Equal("none", await GetSvgMaxWidth());
    }

    // ── Rapid toggling ──

    [Fact]
    public async Task Rapid_zoom_toggle_produces_consistent_state()
    {
        await Page.GotoAsync(GenerateReport("RapidZoomToggle.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync(
            "() => document.querySelector('[data-diagram-type=\"plantuml\"] .diagram-zoom-slider') !== null",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        // Toggle zoom state rapidly via slider (max, min, max, min -> ends unzoomed)
        await Page.EvaluateAsync("""
            () => {
                var slider = document.querySelector('[data-diagram-type="plantuml"] .diagram-zoom-slider');
                for (var i = 0; i < 4; i++) {
                    slider.value = (i % 2 === 0) ? '100' : slider.min;
                    slider.dispatchEvent(new Event('input', { bubbles: true }));
                }
            }
        """);

        Assert.False(await IsZoomedIn());
        Assert.Equal("100%", await GetSvgMaxWidth());
    }

    [Fact]
    public async Task Zoom_slider_value_correct_after_collapse_re_render()
    {
        await Page.GotoAsync(GenerateReport("ZoomSliderAfterCollapse.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ZoomIn();
        await ClickRadioButton("collapsed");
        await WaitForReRender();

        await Page.WaitForFunctionAsync(
            "() => document.querySelector('[data-diagram-type=\"plantuml\"] .diagram-zoom-slider') !== null",
            null, new() { Timeout = 10000, PollingInterval = 200 });
        var sliderVal = await Page.EvaluateAsync<string>(
            "() => document.querySelector('[data-diagram-type=\"plantuml\"] .diagram-zoom-slider').value");
        Assert.Equal("100", sliderVal); // Should still be at max
    }

    [Fact]
    public async Task Zoom_slider_at_min_when_unzoomed_after_collapse()
    {
        await Page.GotoAsync(GenerateReport("ZoomSliderUnzoomedCollapse.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ClickRadioButton("collapsed");
        await WaitForReRender();

        await Page.WaitForFunctionAsync(
            "() => document.querySelector('[data-diagram-type=\"plantuml\"] .diagram-zoom-slider') !== null",
            null, new() { Timeout = 10000, PollingInterval = 200 });
        var sliderVal = await Page.EvaluateAsync<string>(
            "() => document.querySelector('[data-diagram-type=\"plantuml\"] .diagram-zoom-slider').value");
        var sliderMin = await Page.EvaluateAsync<string>(
            "() => document.querySelector('[data-diagram-type=\"plantuml\"] .diagram-zoom-slider').min");
        Assert.Equal(sliderMin, sliderVal); // Should be at min (fit-to-width)
    }

    // ── Report-level controls ──

    [Fact]
    public async Task Report_level_truncation_change_preserves_zoom()
    {
        await Page.GotoAsync(GenerateReport("ReportTruncationPreservesZoom.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ZoomIn();

        var reportExpandedBtn = Page.Locator(".toolbar-row .details-radio-btn[data-state='expanded']");
        if (await reportExpandedBtn.CountAsync() > 0)
        {
            await reportExpandedBtn.First.ClickAsync();
            await WaitForReRender();
            Assert.True(await IsZoomedIn());
            Assert.Equal("none", await GetSvgMaxWidth());
        }
    }

    [Fact]
    public async Task Report_level_headers_toggle_preserves_zoom()
    {
        await Page.GotoAsync(GenerateReport("ReportHeadersPreservesZoom.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await ZoomIn();

        var reportHeadersHidden = Page.Locator(".toolbar-row .toggle-btn[data-toggle='headers'][data-shown='true']");
        if (await reportHeadersHidden.CountAsync() > 0)
        {
            await reportHeadersHidden.First.ClickAsync();
            await WaitForReRender(60000);
            Assert.True(await IsZoomedIn());
            Assert.Equal("none", await GetSvgMaxWidth());
        }
    }
}