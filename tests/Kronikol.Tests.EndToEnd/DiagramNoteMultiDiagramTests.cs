namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Tests for note hover button behavior in multi-diagram scenarios.
/// These tests verify that note state changes work correctly when
/// multiple diagrams are present in the same scenario or report.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class DiagramNoteMultiDiagramTests : DiagramNotePlaywrightBase
{
    public DiagramNoteMultiDiagramTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Two_diagram_minus_button_collapses_first_note()
    {
        await Page.GotoReportAsync(GenerateTwoLongNoteReport("TwoDiagMinus.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        var firstContainer = Page.Locator("[data-diagram-type='plantuml']").First;
        var svg = firstContainer.Locator("svg");

        var textBefore = await svg.EvaluateAsync<int>("el => el.querySelectorAll('text').length");

        // Click minus on first diagram's first note
        await HoverNoteRect(0);
        await Page.Locator("[data-note-btn='minus']").First.WaitForAsync(
            new() { State = Microsoft.Playwright.WaitForSelectorState.Visible });

        var htmlBefore = await svg.EvaluateAsync<string>("el => el.outerHTML");
        await Page.Locator("[data-note-btn='minus'] rect").First.EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true}))");
        await Page.WaitForFunctionAsync(
            $"() => {{ var svg = document.querySelector('[data-diagram-type=\"plantuml\"] svg'); " +
            $"return svg && svg.outerHTML !== {System.Text.Json.JsonSerializer.Serialize(htmlBefore)} " +
            $"&& svg.querySelectorAll('.note-toggle-icon').length > 0; }}",
            null, new() { Timeout = 15000, PollingInterval = 200 });

        var textAfter = await firstContainer.Locator("svg").EvaluateAsync<int>("el => el.querySelectorAll('text').length");
        Assert.True(textAfter < textBefore,
            $"First diagram text count should decrease. Before: {textBefore}, After: {textAfter}");
    }

    [Fact]
    public async Task Split_diagram_minus_collapses_and_shows_plus()
    {
        await Page.GotoReportAsync(GenerateSplitDiagramReport("SplitDiagMinus.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        // Render all diagrams in the scenario
        await RenderAllDiagramsAndWait(2);

        // Wait for note elements on any diagram
        await Page.Locator(".note-hover-rect").First.WaitForAsync(new() { Timeout = 15000 });
        await Page.Locator(".note-toggle-icon").First.WaitForAsync(new() { Timeout = 15000 });

        // Wait for rendering to complete
        await Page.WaitForFunctionAsync("""
            () => !window._plantumlRendering
        """, null, new() { Timeout = 30000, PollingInterval = 200 });

        // Click minus on first note
        var htmlBefore = await GetSvgHtml();
        await HoverNoteRect(0);
        await Page.Locator("[data-note-btn='minus']").First.WaitForAsync(
            new() { State = Microsoft.Playwright.WaitForSelectorState.Visible });
        await Page.Locator("[data-note-btn='minus'] rect").First.EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true}))");

        await WaitForSvgReRender(htmlBefore);

        // Verify state consistency
        var stateOk = await Page.EvaluateAsync<bool>("""
            () => {
                var containers = document.querySelectorAll('[data-diagram-type="plantuml"]');
                var first = containers[0];
                if (!first || !first._noteSteps) return false;
                // At least one note should be collapsed (step 0)
                // (no + where the collapsed note already shows all of itself, since 5.0.0; NoteButtonLayoutTests pins which
                // notes get one)
                return Object.values(first._noteSteps).some(s => s === 0);
            }
        """);
        Assert.True(stateOk, "First diagram should have a collapsed note");
    }

    [Fact]
    public async Task Three_diagram_rendering_flags_clear_after_all_render()
    {
        await Page.GotoReportAsync(GenerateThreeDiagramSplitReport("ThreeDiagFlags.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await RenderAllThreeDiagramsAndWait();

        var flagsClear = await Page.EvaluateAsync<bool>("""
            () => {
                var containers = document.querySelectorAll('[data-diagram-type="plantuml"]');
                for (var i = 0; i < containers.length; i++) {
                    if (containers[i]._noteRendering) return false;
                }
                return !window._plantumlRendering;
            }
        """);
        Assert.True(flagsClear, "All rendering flags should be clear after rendering completes");
    }

    [Fact]
    public async Task Header_notes_report_minus_reduces_text_in_first_diagram()
    {
        await Page.GotoReportAsync(GenerateLongNoteWithHeadersReport("HeaderMinusContent.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        await SetScenarioState("truncated");

        var textBefore = await GetSvgTextCount();

        await ClickNoteButton("[data-note-btn='minus']");

        var textAfter = await GetSvgTextCount();
        Assert.True(textAfter < textBefore,
            $"Text elements should decrease after collapse. Before: {textBefore}, After: {textAfter}");
    }

    [Fact]
    public async Task Two_diagram_collapse_does_not_affect_second_diagram()
    {
        await Page.GotoReportAsync(GenerateTwoLongNoteReport("TwoDiagIsolation.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        // Render second diagram
        await RenderAllDiagramsAndWait(2, 60000);

        // Get second diagram's text count
        var secondTextBefore = await Page.EvaluateAsync<int>("""
            () => {
                var containers = document.querySelectorAll('[data-diagram-type="plantuml"]');
                if (containers.length < 2) return -1;
                var svg = containers[1].querySelector('svg');
                return svg ? svg.querySelectorAll('text').length : -1;
            }
        """);

        // Collapse first diagram's first note
        var htmlBefore = await GetSvgHtml();
        await HoverNoteRect(0);
        await Page.Locator("[data-note-btn='minus']").First.WaitForAsync(
            new() { State = Microsoft.Playwright.WaitForSelectorState.Visible });
        await Page.Locator("[data-note-btn='minus'] rect").First.EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true}))");
        await WaitForSvgReRender(htmlBefore);

        // Second diagram should be unaffected
        var secondTextAfter = await Page.EvaluateAsync<int>("""
            () => {
                var containers = document.querySelectorAll('[data-diagram-type="plantuml"]');
                if (containers.length < 2) return -1;
                var svg = containers[1].querySelector('svg');
                return svg ? svg.querySelectorAll('text').length : -1;
            }
        """);
        Assert.Equal(secondTextBefore, secondTextAfter);
    }

    private async Task<int> GetSvgTextCount()
    {
        return await Page.EvaluateAsync<int>("""
            () => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                return svg ? svg.querySelectorAll('text').length : 0;
            }
        """);
    }
}
