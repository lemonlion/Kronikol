using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Tests for the duration filter feature.
/// Users should be able to filter scenarios by duration threshold,
/// with support for percentile-based thresholds (95th, 99th).
/// </summary>
public class DurationFilterReportTests
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
    public void Report_with_durations_contains_duration_filter_section()
    {
        var features = MakeFeatures(
            ("t1", "Fast test", ExecutionResult.Passed, TimeSpan.FromSeconds(1)),
            ("t2", "Slow test", ExecutionResult.Passed, TimeSpan.FromSeconds(10)));
        var content = GenerateReport(features, "DurationFilterSection.html");
        Assert.Contains("duration-filters", content);
    }

    [Fact]
    public void Report_contains_duration_threshold_input()
    {
        var features = MakeFeatures(
            ("t1", "Test A", ExecutionResult.Passed, TimeSpan.FromSeconds(1)));
        var content = GenerateReport(features, "DurationFilterInput.html");
        Assert.Contains("duration-threshold", content);
    }

    [Fact]
    public void Report_contains_percentile_buttons()
    {
        var features = MakeFeatures(
            ("t1", "Test A", ExecutionResult.Passed, TimeSpan.FromSeconds(1)),
            ("t2", "Test B", ExecutionResult.Passed, TimeSpan.FromSeconds(5)));
        var content = GenerateReport(features, "DurationFilterPercentiles.html");
        Assert.Contains("P50", content);
        Assert.Contains("P90", content);
        Assert.Contains("P95", content);
        Assert.Contains("P99", content);
        Assert.Contains("Custom", content);
    }

    [Fact]
    public void Report_contains_duration_filter_javascript()
    {
        var features = MakeFeatures(
            ("t1", "Test A", ExecutionResult.Passed, TimeSpan.FromSeconds(1)));
        var content = GenerateReport(features, "DurationFilterJs.html");
        Assert.Contains("filter_duration", content);
    }

    [Fact]
    public void Report_without_any_durations_has_no_duration_filter()
    {
        var features = MakeFeatures(
            ("t1", "Test A", ExecutionResult.Passed, null));
        var content = GenerateReport(features, "DurationFilterNone.html");
        // No duration-threshold input rendered (CSS class still exists in stylesheet)
        Assert.DoesNotContain("id=\"duration-threshold\"", content);
    }

    [Fact]
    public void Report_contains_precomputed_percentile_data()
    {
        var scenarios = Enumerable.Range(1, 100)
            .Select(i => ($"t{i}", $"Test {i}", ExecutionResult.Passed, (TimeSpan?)TimeSpan.FromSeconds(i)))
            .ToArray();
        var features = MakeFeatures(scenarios);
        var content = GenerateReport(features, "DurationFilterPrecomp.html");
        Assert.Contains("data-p50=", content);
        Assert.Contains("data-p90=", content);
        Assert.Contains("data-p95=", content);
        Assert.Contains("data-p99=", content);
    }

    [Fact]
    public void Filter_duration_hides_scenarios_without_duration_measurement()
    {
        var features = MakeFeatures(
            ("t1", "Fast test", ExecutionResult.Passed, TimeSpan.FromSeconds(1)),
            ("t2", "No duration", ExecutionResult.Passed, null));
        var content = GenerateReport(features, "DurationFilterNoDuration.html");

        // The filter_duration function should hide scenarios that have no
        // data-duration-ms attribute (i.e. no measurement) whenever a
        // threshold is active, rather than showing them alongside measured ones.
        Assert.Contains("!c.items[i].el.hasAttribute('data-duration-ms')", content);
    }
}
