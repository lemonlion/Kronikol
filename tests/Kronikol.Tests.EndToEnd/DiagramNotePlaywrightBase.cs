using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Base class for diagram note tests, providing Playwright equivalents
/// of the Selenium DiagramNoteTestBase helpers.
/// </summary>
public abstract class DiagramNotePlaywrightBase : PlaywrightTestBase
{
    protected DiagramNotePlaywrightBase(PlaywrightFixture fixture) : base(fixture) { }

    protected string GenerateLongNoteReport(string fileName) =>
        ReportTestHelper.GenerateReportWithLongNotes(TempDir, OutputDir, fileName);

    protected string GeneratePartitionReport(string fileName) =>
        ReportTestHelper.GenerateReportWithPartitionDiagram(TempDir, OutputDir, fileName);

    protected string GeneratePartitionLongNoteReport(string fileName) =>
        ReportTestHelper.GenerateReportWithPartitionLongNotes(TempDir, OutputDir, fileName);

    protected string GenerateTwoLongNoteReport(string fileName) =>
        ReportTestHelper.GenerateReportWithTwoLongNoteDiagrams(TempDir, OutputDir, fileName);

    protected string GenerateSplitDiagramReport(string fileName) =>
        ReportTestHelper.GenerateReportWithSplitDiagramLongNotes(TempDir, OutputDir, fileName);

    protected string GenerateThreeDiagramSplitReport(string fileName) =>
        ReportTestHelper.GenerateReportWithThreeDiagramSplit(TempDir, OutputDir, fileName);

    protected string GenerateLongNoteWithHeadersReport(string fileName) =>
        ReportTestHelper.GenerateReportWithLongNotesAndHeaders(TempDir, OutputDir, fileName);

    protected string GenerateBarelyOverLimitWithHeadersReport(string fileName) =>
        ReportTestHelper.GenerateReportWithBarelyOverLimitHeaders(TempDir, OutputDir, fileName);

    protected string GenerateMixedParticipantNotesReport(string fileName) =>
        ReportTestHelper.GenerateReportWithMixedParticipantNotes(TempDir, OutputDir, fileName);

    protected string GenerateStepDelimitersAndNotesReport(string fileName) =>
        ReportTestHelper.GenerateReportWithStepDelimitersAndNotes(TempDir, OutputDir, fileName);

    protected string GenerateDatabaseParticipantReport(string fileName) =>
        ReportTestHelper.GenerateReportWithDatabaseParticipant(TempDir, OutputDir, fileName);

    protected string GenerateWideDatabaseParticipantReport(string fileName) =>
        ReportTestHelper.GenerateReportWithWideDatabaseParticipant(TempDir, OutputDir, fileName);

    protected string GenerateCollectionsParticipantReport(string fileName) =>
        ReportTestHelper.GenerateReportWithCollectionsParticipant(TempDir, OutputDir, fileName);

    protected string GenerateMixedDatabaseCollectionsReport(string fileName) =>
        ReportTestHelper.GenerateReportWithMixedDatabaseCollections(TempDir, OutputDir, fileName);

    protected string GenerateFragmentedDiagramReport(string fileName) =>
        ReportTestHelper.GenerateReportWithFragmentedDiagram(TempDir, OutputDir, fileName);

    protected string GenerateDatabaseStepNoteCollapseReport(string fileName) =>
        ReportTestHelper.GenerateReportWithDatabaseStepNoteCollapse(TempDir, OutputDir, fileName);

    protected string GenerateColoredArrowReport(string fileName) =>
        ReportTestHelper.GenerateReportWithColoredArrows(TempDir, OutputDir, fileName);

    protected string GenerateLargeNoteReport(string fileName) =>
        ReportTestHelper.GenerateReportWithLargeNote(TempDir, OutputDir, fileName);

    protected string GenerateDatabaseContinuationSplitReport(string fileName) =>
        ReportTestHelper.GenerateReportWithDatabaseContinuationSplit(TempDir, OutputDir, fileName);

    protected string GenerateChunkedDatabaseNoteReport(string fileName) =>
        ReportTestHelper.GenerateReportWithChunkedDatabaseNote(TempDir, OutputDir, fileName);

    protected string GenerateThreeFragmentContinuationReport(string fileName) =>
        ReportTestHelper.GenerateReportWithThreeFragmentContinuation(TempDir, OutputDir, fileName);

    protected async Task ExpandAndRenderLongNoteDiagram(string fileName)
    {
        await Page.GotoAsync(GenerateLongNoteReport(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    protected async Task SetScenarioState(string state)
    {
        await Page.Locator($".diagram-toggle .details-radio-btn[data-state='{state}']").First.ClickAsync();
        await WaitForNoteElements();
    }

    protected async Task<string> GetSvgHtml()
    {
        return await Page.Locator("[data-diagram-type='plantuml'] svg").First
            .EvaluateAsync<string>("el => el.outerHTML");
    }

    protected async Task DoubleClickFirstNoteAndWait()
    {
        var htmlBefore = await GetSvgHtml();
        // JS dispatch avoids SVG <text> elements intercepting pointer events
        await Page.Locator(".note-hover-rect").First.EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('dblclick', {bubbles:true, cancelable:true}))");
        await WaitForSvgReRender(htmlBefore);
    }

    protected async Task HoverNoteRect(int index)
    {
        // JS dispatch avoids SVG <text> elements intercepting pointer events
        await Page.Locator(".note-hover-rect").Nth(index).EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('mouseenter', {bubbles:true}))");
    }

    protected async Task WaitForSvgReRender(string previousHtml, int timeoutMs = 15000)
    {
        await Page.WaitForFunctionAsync(
            $"() => {{ var container = document.querySelector('[data-plantuml]'); " +
            $"if (!container || container._noteRendering || window._plantumlRendering) return false; " +
            $"var svg = document.querySelector('[data-diagram-type=\"plantuml\"] svg'); " +
            $"return svg && svg.outerHTML !== {System.Text.Json.JsonSerializer.Serialize(previousHtml)} " +
            $"&& svg.querySelectorAll('.note-toggle-icon').length > 0; }}",
            null, new() { Timeout = timeoutMs, PollingInterval = 200 });
    }

    protected async Task ClickNoteButton(string cssSelector)
    {
        var htmlBefore = await GetSvgHtml();
        await HoverNoteRect(0);
        await Page.Locator(cssSelector).First.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        // JS-dispatched click to avoid SVG path interception
        await Page.Locator(cssSelector + " rect").First
            .EvaluateAsync("el => el.dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true}))");
        await WaitForSvgReRender(htmlBefore);
    }

    protected async Task ClickDownArrowAndWait()
    {
        var htmlBefore = await GetSvgHtml();
        await HoverNoteRect(0);

        // Wait for down arrow to be visible
        await Page.WaitForFunctionAsync(
            "() => { var icons = document.querySelectorAll('.note-toggle-icon'); " +
            "return Array.from(icons).some(i => { " +
            "var texts = i.querySelectorAll('text'); " +
            "return Array.from(texts).some(t => t.textContent.includes('▼')) && i.style.opacity !== '0'; }); }");

        // Click down arrow via JS
        await Page.EvaluateAsync("""
            () => {
                var icons = document.querySelectorAll('.note-toggle-icon');
                var downArrow = Array.from(icons).find(i =>
                    Array.from(i.querySelectorAll('text')).some(t => t.textContent.includes('▼')));
                if (downArrow) {
                    var rect = downArrow.querySelector('rect');
                    rect.dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true}));
                }
            }
        """);
        await WaitForSvgReRender(htmlBefore);
    }

    protected async Task WaitForNoteFormatButtonVisible()
    {
        await Page.WaitForFunctionAsync("""
            () => {
                var btns = document.querySelectorAll('[data-note-btn="format"]');
                return Array.from(btns).some(b => b.style.display !== 'none' && b.style.opacity === '1');
            }
        """, null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    protected async Task ClickNoteFormatButton()
    {
        var htmlBefore = await GetSvgHtml();
        await HoverNoteRect(0);
        await WaitForNoteFormatButtonVisible();
        await Page.EvaluateAsync("""
            () => {
                var btn = Array.from(document.querySelectorAll('[data-note-btn="format"]'))
                    .find(b => b.style.display !== 'none' && b.style.opacity === '1');
                btn.querySelector('rect').dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true}));
            }
        """);
        await WaitForSvgReRender(htmlBefore);
    }

    protected async Task<string> GetNormalizedSvgText()
    {
        var text = await Page.Locator("[data-diagram-type='plantuml'] svg").First
            .EvaluateAsync<string>("el => el.textContent");
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
    }

    protected async Task WaitForRenderCountIncrease(int previousCount, int timeoutMs = 60000)
    {
        await Page.WaitForFunctionAsync(
            "(prev) => !window._plantumlRendering && (window._renderCompleteCount || 0) > prev",
            previousCount, new() { Timeout = timeoutMs, PollingInterval = 200 });
    }

    protected async Task RenderAllDiagramsAndWait(int minCount = 2, int timeoutMs = 60000)
    {
        await Page.EvaluateAsync("""
            () => document.querySelectorAll('[data-diagram-type="plantuml"]').forEach(c => {
                if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(c.parentElement);
            })
        """);

        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-diagram-type=\"plantuml\"] svg').length >= {minCount} && !window._plantumlRendering",
            null, new() { Timeout = timeoutMs, PollingInterval = 200 });
    }

    protected async Task RenderAllThreeDiagramsAndWait()
    {
        await RenderAllDiagramsAndWait(3, 120000);

        // Wait for note processing to finish
        await Page.WaitForFunctionAsync("""
            () => {
                var containers = document.querySelectorAll('[data-diagram-type="plantuml"]');
                for (var i = 0; i < containers.length; i++) {
                    if (containers[i]._noteRendering || window._plantumlRendering) return false;
                }
                return true;
            }
        """, null, new() { Timeout = 120000, PollingInterval = 200 });
    }
}