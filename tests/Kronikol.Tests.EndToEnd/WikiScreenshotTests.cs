namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Captures specific screenshots for the wiki pages.
/// Run: dotnet test Kronikol.Tests.EndToEnd --filter "WikiScreenshotTests"
/// Output goes to PlaywrightOutput/wiki-screenshots/
/// </summary>
[Collection(PlaywrightCollections.Scenarios)]
public class WikiScreenshotTests : PlaywrightTestBase
{
    private readonly string _screenshotDir;

    private static readonly string ReportsDir = Path.Combine(
        Path.GetDirectoryName(typeof(WikiScreenshotTests).Assembly.Location)!,
        "..", "..", "..", "..", "Kronikol.Tests", "bin", "Debug", "net10.0", "Reports");

    private static readonly string ShowcaseReportPath = Path.Combine(
        OutputDir, "ShowcaseReport.html");

    public WikiScreenshotTests(PlaywrightFixture fixture) : base(fixture)
    {
        _screenshotDir = Path.Combine(OutputDir, "wiki-screenshots");
        Directory.CreateDirectory(_screenshotDir);
    }

    protected override int ViewportWidth => 1280;
    protected override int ViewportHeight => 900;

    private async Task SaveScreenshot(string name) =>
        await Page.ScreenshotAsync(new() { Path = Path.Combine(_screenshotDir, name) });

    private async Task OpenFile(string path)
    {
        var uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
        await Page.GotoReportAsync(uri);
        await Task.Delay(500);
    }

    private async Task ExpandAll()
    {
        await Page.EvaluateAsync("document.querySelectorAll('details').forEach(d => d.open = true);");
        await Task.Delay(300);
    }

    private async Task ScrollTo(string selector)
    {
        await Page.EvaluateAsync("""
            (sel) => {
                var el = document.querySelector(sel);
                if (el) el.scrollIntoView({block:'start',behavior:'instant'});
            }
        """, selector);
        await Task.Delay(300);
    }

    private async Task ScrollToTop()
    {
        await Page.EvaluateAsync("window.scrollTo(0,0);");
        await Task.Delay(300);
    }

    // ── Feature 7: Component Diagram ──
    [Fact(Skip = "Long-running GIF/screenshot generation — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Capture_component_diagram_report()
    {
        var file = Path.Combine(ReportsDir, "ComponentSpecificationsWithExamples.html");
        if (!File.Exists(file))
        {
            var archived = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(typeof(WikiScreenshotTests).Assembly.Location)!,
                "..", "..", "..", "..", "Example.Api", "TestResults", "ArchivedReports", "BDDfy.xUnit3",
                "ComponentDiagram.Project_report_contains_expected_scenarios.html"));
            if (File.Exists(archived)) file = archived;
        }
        Assert.True(File.Exists(file), $"Report file not found: {file}");
        await OpenFile(file);
        await Page.Locator("details.feature, details, body").First.WaitForAsync();
        await ExpandAll();
        await Task.Delay(1000);

        try
        {
            await Page.Locator("svg, [data-diagram-type] img").First.WaitForAsync(new() { Timeout = 15000 });
        }
        catch { /* Proceed even if diagrams don't render */ }

        await ScrollToTop();
        await Task.Delay(500);
        await SaveScreenshot("whats-new-component-diagram.png");
    }

    // ── Feature 8: Internal Flow (OpenTelemetry) ──
    [Fact(Skip = "Long-running GIF/screenshot generation — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Capture_internal_flow_visualization()
    {
        var file = Path.Combine(OutputDir, "Whole_test_flow_Both_shows_toggle_buttons.html");
        if (!File.Exists(file))
            file = Path.Combine(ReportsDir, "ToggleBoth.html");
        if (!File.Exists(file))
            file = Path.Combine(ReportsDir, "ToggleFlowOnly.html");
        Assert.True(File.Exists(file), "Internal flow report not found");
        await OpenFile(file);
        await Task.Delay(500);

        try
        {
            await ExpandAll();
            await Page.EvaluateAsync("""
                document.querySelectorAll('details.wtf-block, details').forEach(d => d.open = true);
            """);
        }
        catch { /* best effort */ }
        await Task.Delay(500);
        await SaveScreenshot("whats-new-internal-flow.png");
    }

    // ── Feature 9: CI/CD Summary ──
    [Fact(Skip = "Long-running GIF/screenshot generation — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Capture_ci_summary()
    {
        var file = Path.Combine(ReportsDir, "CiSummaryInteractive.html");
        Assert.True(File.Exists(file), "CI Summary report not found");
        await OpenFile(file);
        await Page.Locator("body").WaitForAsync();
        await Task.Delay(500);
        await SaveScreenshot("whats-new-ci-summary.png");
    }

    // ── Feature 10: Machine-Readable Formats ──
    [Fact(Skip = "Long-running GIF/screenshot generation — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Capture_test_run_report()
    {
        var file = Path.Combine(ReportsDir, "TestRunReport.html");
        if (!File.Exists(file))
            file = Path.Combine(ReportsDir, "Specifications.html");
        Assert.True(File.Exists(file), "TestRunReport not found");
        await OpenFile(file);
        await Page.Locator("body").WaitForAsync();
        await ExpandAll();
        await Task.Delay(500);
        await SaveScreenshot("whats-new-machine-readable.png");
    }

    // ── Feature 12: DiagramFocus ──
    [Fact(Skip = "Long-running GIF/screenshot generation — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Capture_diagram_focus()
    {
        var candidates = new[]
        {
            Path.Combine(ReportsDir, "TestRunReport.FocusBoldEmphasis_HighlightsRequestFields.html"),
            Path.Combine(ReportsDir, "TestRunReport.FocusBoldAndColoredEmphasis_CombinesMarkup.html"),
            Path.Combine(ReportsDir, "TestRunReport.FocusColoredEmphasis_HighlightsRequestFields.html"),
        };
        var file = candidates.FirstOrDefault(File.Exists);

        if (file == null)
        {
            var archiveBase = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(typeof(WikiScreenshotTests).Assembly.Location)!,
                "..", "..", "..", "..", "Example.Api", "TestResults", "ArchivedReports", "BDDfy.xUnit3"));
            candidates =
            [
                Path.Combine(archiveBase, "TestRunReport.FocusBoldEmphasis_HighlightsRequestFields.html"),
                Path.Combine(archiveBase, "TestRunReport.FocusBoldAndColoredEmphasis_CombinesMarkup.html"),
            ];
            file = candidates.FirstOrDefault(File.Exists);
        }

        Assert.NotNull(file);
        await OpenFile(file!);
        await Page.Locator("body").WaitForAsync();
        await ExpandAll();
        await Task.Delay(500);

        try
        {
            await Page.Locator("svg").First.WaitForAsync(new() { Timeout = 15000 });
        }
        catch { /* Proceed anyway */ }

        try
        {
            await ScrollTo("[data-diagram-type], .scenario");
        }
        catch { /* stay at current position */ }
        await Task.Delay(300);
        await SaveScreenshot("whats-new-diagram-focus.png");
    }

    // ── Feature 19: Scenario Timeline ──
    [Fact(Skip = "Long-running GIF/screenshot generation — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Capture_scenario_timeline()
    {
        var file = Path.Combine(ReportsDir, "TimelineBars.html");
        if (!File.Exists(file)) file = Path.Combine(ReportsDir, "TimelineToggle.html");
        Assert.True(File.Exists(file), "Timeline report not found");
        await OpenFile(file);
        await Page.Locator("body").WaitForAsync();
        await Task.Delay(500);

        try
        {
            var timelineBtn = Page.Locator("button:has-text('Scenario Timeline'), button:has-text('Timeline')").First;
            await timelineBtn.ClickAsync();
            await Task.Delay(500);
        }
        catch
        {
            await ExpandAll();
            await Task.Delay(500);
        }

        await SaveScreenshot("whats-new-scenario-timeline.png");
    }

    // ── Feature 20: Export ──
    [Fact(Skip = "Long-running GIF/screenshot generation — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Capture_export_buttons()
    {
        if (File.Exists(ShowcaseReportPath))
        {
            await OpenFile(ShowcaseReportPath);
        }
        else
        {
            var file = Path.Combine(ReportsDir, "ExportBtn.html");
            if (!File.Exists(file)) file = Path.Combine(ReportsDir, "ExportHtml.html");
            Assert.True(File.Exists(file), "Export report not found");
            await OpenFile(file);
        }
        await Page.Locator("body").WaitForAsync();
        await Task.Delay(500);

        try
        {
            await ScrollTo("button:has-text('Export')");
        }
        catch { /* stay at top */ }
        await Task.Delay(300);

        await ScrollToTop();
        await Task.Delay(300);
        await SaveScreenshot("whats-new-export.png");
    }

    // ── Feature 15: Violet Theme ──
    [Fact(Skip = "Long-running GIF/screenshot generation — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Capture_violet_theme_overview()
    {
        if (File.Exists(ShowcaseReportPath))
        {
            await OpenFile(ShowcaseReportPath);
        }
        else
        {
            var file = Path.Combine(ReportsDir, "PieChart.html");
            if (!File.Exists(file)) file = Path.Combine(ReportsDir, "Specifications.html");
            Assert.True(File.Exists(file), "Report not found");
            await OpenFile(file);
        }
        await Page.Locator("body").WaitForAsync();
        await Task.Delay(500);

        try
        {
            await Page.EvaluateAsync("""
                (() => {
                    // 5.0.0: the features table is the Features tab's Summary view (plans/OPTION_C_PLAN.md S4)
                    var summary = document.getElementById('view-summary');
                    if (summary) summary.click();
                })()
            """);
            await Task.Delay(300);
        }
        catch { /* best effort */ }

        await ScrollToTop();
        await Task.Delay(300);
        await SaveScreenshot("whats-new-violet-theme.png");
    }

    // ── Feature 11: DispatchProxy / MediatR ──
    [Fact(Skip = "Long-running GIF/screenshot generation — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Capture_dispatch_proxy_tracking()
    {
        var file = Path.Combine(ReportsDir, "DepFilterButtons.html");
        if (!File.Exists(file)) file = Path.Combine(ReportsDir, "DepFilterMultiScenario.html");
        Assert.True(File.Exists(file), "Dependency filter report not found");
        await OpenFile(file);
        await Page.Locator("body").WaitForAsync();
        await ExpandAll();
        await Task.Delay(500);

        try
        {
            await ScrollTo(".dep-filter, [data-dep]");
        }
        catch { await ScrollToTop(); }
        await Task.Delay(300);
        await SaveScreenshot("whats-new-dispatch-proxy.png");
    }
}
