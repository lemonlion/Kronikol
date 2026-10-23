using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Tests for the jump-to-failure feature.
/// A sticky "Next Failure" button scrolls to the next failing scenario.
/// </summary>
public class JumpToFailureReportTests
{
    private static Feature[] MakeFeatures(params (string id, string name, ExecutionResult result)[] scenarios) =>
    [
        new Feature
        {
            DisplayName = "Test Feature",
            Scenarios = scenarios.Select(s => new Scenario
            {
                Id = s.id,
                DisplayName = s.name,
                IsHappyPath = false,
                Result = s.result
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
    public void Report_with_failures_contains_jump_to_failure_button()
    {
        var features = MakeFeatures(
            ("t1", "Create order", ExecutionResult.Passed),
            ("t2", "Fail order", ExecutionResult.Failed));
        var content = GenerateReport(features, "JumpToFailureBtn.html");
        Assert.Contains("jump-to-failure", content);
    }

    [Fact]
    public void Report_without_failures_has_no_jump_to_failure_button()
    {
        var features = MakeFeatures(
            ("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "JumpToFailureNone.html");
        // No jump-to-failure button element rendered (CSS class still exists in stylesheet)
        Assert.DoesNotContain("onclick=\"jump_to_next_failure", content);
    }

    [Fact]
    public void Report_jump_to_failure_button_is_sticky()
    {
        var features = MakeFeatures(
            ("t1", "Create order", ExecutionResult.Passed),
            ("t2", "Fail order", ExecutionResult.Failed));
        var content = GenerateReport(features, "JumpToFailureSticky.html");
        // The jump-to-failure CSS class has position: fixed in the stylesheet
        Assert.Contains(".jump-to-failure", content);
        Assert.Contains("position: fixed", content);
    }

    [Fact]
    public void Report_contains_jump_to_failure_javascript()
    {
        var features = MakeFeatures(
            ("t1", "Create order", ExecutionResult.Passed),
            ("t2", "Fail order", ExecutionResult.Failed));
        var content = GenerateReport(features, "JumpToFailureJs.html");
        Assert.Contains("jump_to_next_failure", content);
    }

    [Fact]
    public void Report_jump_to_failure_displays_failure_count()
    {
        var features = MakeFeatures(
            ("t1", "Pass", ExecutionResult.Passed),
            ("t2", "Fail 1", ExecutionResult.Failed),
            ("t3", "Fail 2", ExecutionResult.Failed));
        var content = GenerateReport(features, "JumpToFailureCount.html");
        // Button should show "Next Failure (1/2)" or similar
        Assert.Contains("failure-counter", content);
    }
}
