using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Tests for the scenario duration badge feature.
/// Each scenario should display its execution time next to the title,
/// with colour coding: green (fast), amber (moderate), red (slow).
/// </summary>
public class DurationBadgeReportTests
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
    public void Report_scenario_with_duration_contains_duration_badge()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed, TimeSpan.FromSeconds(1.5)));
        var content = GenerateReport(features, "DurationBadge.html");
        Assert.Contains("duration-badge", content);
    }

    [Fact]
    public void Report_scenario_without_duration_has_no_duration_badge()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed, null));
        var content = GenerateReport(features, "DurationBadgeNull.html");
        // The scenario summary should not contain any badge span
        Assert.DoesNotContain("<span class=\"duration-badge", content);
        // The scenario element should not have a data-duration-ms attribute
        Assert.DoesNotContain("data-duration-ms=\"", content);
    }

    [Fact]
    public void Report_fast_scenario_has_green_badge()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed, TimeSpan.FromMilliseconds(500)));
        var content = GenerateReport(features, "DurationBadgeGreen.html");
        Assert.Contains("duration-fast", content);
    }

    [Fact]
    public void Report_moderate_scenario_has_amber_badge()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed, TimeSpan.FromSeconds(3)));
        var content = GenerateReport(features, "DurationBadgeAmber.html");
        Assert.Contains("duration-moderate", content);
    }

    [Fact]
    public void Report_slow_scenario_has_red_badge()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed, TimeSpan.FromSeconds(6)));
        var content = GenerateReport(features, "DurationBadgeRed.html");
        Assert.Contains("duration-slow", content);
    }

    [Fact]
    public void Report_scenario_badge_displays_formatted_duration()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed, TimeSpan.FromSeconds(2.45)));
        var content = GenerateReport(features, "DurationBadgeFmt.html");
        Assert.Contains("2.5s", content);
    }

    [Fact]
    public void Report_sub_second_duration_displays_in_milliseconds()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed, TimeSpan.FromMilliseconds(150)));
        var content = GenerateReport(features, "DurationBadgeMs.html");
        Assert.Contains("150ms", content);
    }

    [Fact]
    public void Report_scenario_has_data_duration_attribute()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed, TimeSpan.FromSeconds(3)));
        var content = GenerateReport(features, "DurationBadgeAttr.html");
        Assert.Contains("data-duration-ms=", content);
    }

    [Fact]
    public void Report_contains_duration_badge_css()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed, TimeSpan.FromSeconds(1)));
        var content = GenerateReport(features, "DurationBadgeCss.html");
        Assert.Contains(".duration-badge", content);
    }
}
