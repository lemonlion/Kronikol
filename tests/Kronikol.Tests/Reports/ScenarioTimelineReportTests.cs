using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class ScenarioTimelineReportTests
{
    private static Feature[] MakeFeatures(params (string id, string name, ExecutionResult result, TimeSpan? duration)[] scenarios) =>
    [
        new Feature
        {
            DisplayName = "Test Feature",
            Scenarios = scenarios.Select(s => new Scenario
            {
                Id = s.id,
                DisplayName = s.name,
                IsHappyPath = false,
                Result = s.result,
                Duration = s.duration
            }).ToArray()
        }
    ];

    private static string GenerateReport(Feature[] features, string fileName)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return ReportHtml.Read(path);
    }

    [Fact]
    public void Report_with_durations_has_a_timeline_tab()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Passed, TimeSpan.FromSeconds(2)),
            ("t2", "Test 2", ExecutionResult.Passed, TimeSpan.FromSeconds(3)));
        var content = GenerateReport(features, "TimelineToggle.html");
        // 5.0.0 (plans/OPTION_C_PLAN.md S3): a tab of the report bar in place of the toolbar's toggle
        Assert.Contains("<button class=\"report-tab\" id=\"tab-timeline\"", content);
    }

    [Fact]
    public void Report_without_durations_has_no_timeline()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Passed, null));
        var content = GenerateReport(features, "NoTimeline.html");
        Assert.DoesNotContain("onclick=\"toggle_timeline", content);
        Assert.DoesNotContain("id=\"tab-timeline\"", content);
        Assert.DoesNotContain("id=\"scenario-timeline\"", content);
    }

    [Fact]
    public void Report_timeline_is_hidden_by_default()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Passed, TimeSpan.FromSeconds(2)),
            ("t2", "Test 2", ExecutionResult.Passed, TimeSpan.FromSeconds(3)));
        var content = GenerateReport(features, "TimelineHidden.html");
        Assert.Contains("scenario-timeline", content);
        Assert.Contains("display:none", content.Replace(" ", ""));
    }

    [Fact]
    public void Report_timeline_contains_scenario_bars()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Passed, TimeSpan.FromSeconds(2)),
            ("t2", "Test 2", ExecutionResult.Failed, TimeSpan.FromSeconds(3)));
        var content = GenerateReport(features, "TimelineBars.html");
        Assert.Contains("timeline-bar", content);
    }

    [Fact]
    public void Report_timeline_shows_scenario_names()
    {
        var features = MakeFeatures(
            ("t1", "Create Order", ExecutionResult.Passed, TimeSpan.FromSeconds(2)),
            ("t2", "Delete Order", ExecutionResult.Passed, TimeSpan.FromSeconds(1)));
        var content = GenerateReport(features, "TimelineNames.html");
        // The timeline should have scenario names somewhere
        Assert.Contains("timeline-label", content);
    }

    [Fact]
    public void Report_timeline_is_its_tabs_panel_shown_by_the_tabs_script()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Passed, TimeSpan.FromSeconds(2)));
        var content = GenerateReport(features, "TimelineToggleJs.html");
        Assert.Contains("data-panel=\"timeline\" hidden><div id=\"scenario-timeline\"", content);
        Assert.Contains("<script id=\"kronikol-report-tabs\">", content);
    }

    [Fact]
    public void Report_timeline_has_css_styles()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Passed, TimeSpan.FromSeconds(2)));
        var content = GenerateReport(features, "TimelineCss.html");
        Assert.Contains(".scenario-timeline", content);
    }

    [Fact]
    public void Report_timeline_bars_have_status_css_class()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Passed, TimeSpan.FromSeconds(2)),
            ("t2", "Test 2", ExecutionResult.Failed, TimeSpan.FromSeconds(3)));
        var content = GenerateReport(features, "TimelineStatus.html");
        Assert.Contains("timeline-bar-passed", content);
        Assert.Contains("timeline-bar-failed", content);
    }

    [Fact]
    public void Report_timeline_bars_have_width_percentages()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Passed, TimeSpan.FromSeconds(2)),
            ("t2", "Test 2", ExecutionResult.Passed, TimeSpan.FromSeconds(4)));
        var content = GenerateReport(features, "TimelineWidth.html");
        // The bar widths should be proportional; the longer scenario should have a wider bar
        Assert.Matches(@"width:\s*\d+(\.\d+)?%", content);
    }
}
