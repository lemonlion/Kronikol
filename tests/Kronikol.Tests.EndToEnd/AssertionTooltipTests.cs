using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Tests that assertion source-location tooltips (SVG title elements) are added
/// to assertion note shapes after rendering, based on __^*__ comments
/// in the PlantUML source.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class AssertionTooltipTests : DiagramNotePlaywrightBase
{
    public AssertionTooltipTests(PlaywrightFixture fixture) : base(fixture) { }

    private string GenerateAssertionReport(string fileName) =>
        ReportTestHelper.GenerateReportWithAssertionNotes(TempDir, OutputDir, fileName);

    private async Task ShowAssertionsAndWait()
    {
        var showBtn = Page.Locator("details.scenario").First
            .Locator(".toggle-btn[data-toggle='assertions'][data-shown='false']");

        // Wait for the render to COMPLETE, not for the SVG to differ: the previous render's post-render
        // hooks mutate the old SVG on their own schedule, so "differs" can be true before the click's
        // render has begun (the same race that flaked AssertionToggleTests on CI, 2026-09-14).
        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await showBtn.ClickAsync();
        await Page.WaitForFunctionAsync(
            "(prev) => !window._plantumlRendering && (window._renderCompleteCount || 0) > prev",
            renderCount,
            new() { Timeout = 30000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Assertion_notes_have_source_location_tooltips()
    {
        await Page.GotoAsync(GenerateAssertionReport("AssertTooltip1.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await RenderAllDiagramsAndWait();

        // Assertions are hidden by default — show them
        await ShowAssertionsAndWait();

        // Wait for render queue to finish (tooltips are added post-render)
        await Page.WaitForFunctionAsync("() => !window._plantumlRendering",
            null, new() { Timeout = 15000, PollingInterval = 200 });

        // Verify SVG title elements exist with correct source locations
        var titles = await Page.EvaluateAsync<string[]>("""
            () => {
                var container = document.querySelector('details.scenario [data-diagram-type="plantuml"]');
                if (!container) return [];
                var svg = container.querySelector('svg');
                if (!svg) return [];
                // Tooltips are added to polygon/path elements as child <title>
                var titleEls = svg.querySelectorAll('polygon > title, path > title');
                return Array.from(titleEls).map(t => t.textContent);
            }
        """);

        Assert.Contains("OrderTests.cs L:42", titles);
        Assert.Contains("OrderTests.cs L:45", titles);
    }

    [Fact]
    public async Task Assertion_tooltips_present_after_report_level_show()
    {
        await Page.GotoAsync(GenerateAssertionReport("AssertTooltip2.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await RenderAllDiagramsAndWait();

        // Use report-level Show button
        var reportShowBtn = Page.Locator(".toolbar-right .toggle-btn[data-toggle='assertions'][data-shown='false']");
        await reportShowBtn.ClickAsync();

        // Wait for both scenarios to render with assertions
        await Page.WaitForFunctionAsync(
            @"() => {
                var svgs = document.querySelectorAll('details.scenario [data-diagram-type=""plantuml""] svg');
                return svgs.length >= 2 &&
                    Array.from(svgs).every(s => s.outerHTML.includes('status code'));
            }",
            null, new() { Timeout = 30000, PollingInterval = 200 });

        // Wait for render queue to fully complete
        await Page.WaitForFunctionAsync("() => !window._plantumlRendering",
            null, new() { Timeout = 15000, PollingInterval = 200 });

        // Verify tooltips exist in both scenarios
        var tooltipCounts = await Page.EvaluateAsync<int[]>("""
            () => {
                var scenarios = document.querySelectorAll('details.scenario');
                return Array.from(scenarios).map(sc => {
                    var svg = sc.querySelector('[data-diagram-type="plantuml"] svg');
                    if (!svg) return 0;
                    var titles = svg.querySelectorAll('polygon > title, path > title');
                    return Array.from(titles).filter(t =>
                        t.textContent.includes('OrderTests.cs')).length;
                });
            }
        """);

        Assert.Equal(2, tooltipCounts.Length);
        Assert.True(tooltipCounts[0] >= 2, "Scenario 1 should have at least 2 assertion tooltips");
        Assert.True(tooltipCounts[1] >= 2, "Scenario 2 should have at least 2 assertion tooltips");
    }
}
