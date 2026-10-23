using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class ErrorDiffReportTests
{
    private static Feature[] MakeFeatures(params (string id, string name, ExecutionResult result, string? errorMessage)[] scenarios) =>
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
                ErrorMessage = s.errorMessage
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
    public void Report_with_assert_equal_failure_shows_error_diff()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Failed,
                "Assert.Equal() Failure: Values differ\nExpected: Hello World\nActual:   Hello Mars"));
        var content = GenerateReport(features, "ErrorDiff.html");
        Assert.Contains("error-diff", content);
    }

    [Fact]
    public void Report_error_diff_shows_expected_and_actual()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Failed,
                "Assert.Equal() Failure: Values differ\nExpected: Hello World\nActual:   Hello Mars"));
        var content = GenerateReport(features, "ErrorDiffExpectedActual.html");
        Assert.Contains("diff-expected", content);
        Assert.Contains("diff-actual", content);
    }

    [Fact]
    public void Report_error_diff_highlights_differences()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Failed,
                "Assert.Equal() Failure: Values differ\nExpected: Hello World\nActual:   Hello Mars"));
        var content = GenerateReport(features, "ErrorDiffHighlight.html");
        Assert.Contains("diff-del", content);
        Assert.Contains("diff-ins", content);
    }

    [Fact]
    public void Report_without_diffable_error_has_no_error_diff()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Failed, "Something went wrong"));
        var content = GenerateReport(features, "NoDiff.html");
        Assert.DoesNotContain("class=\"error-diff\"", content);
    }

    [Fact]
    public void Report_error_diff_contains_css_styles()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Failed,
                "Assert.Equal() Failure: Values differ\nExpected: Hello World\nActual:   Hello Mars"));
        var content = GenerateReport(features, "ErrorDiffCss.html");
        Assert.Contains(".error-diff", content);
        Assert.Contains(".diff-del", content);
        Assert.Contains(".diff-ins", content);
    }

    [Fact]
    public void Report_passed_scenario_has_no_error_diff()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Passed, null));
        var content = GenerateReport(features, "PassedNoDiff.html");
        Assert.DoesNotContain("class=\"error-diff\"", content);
    }

    [Fact]
    public void Report_error_diff_with_FluentAssertions_pattern()
    {
        var features = MakeFeatures(
            ("t1", "Test 1", ExecutionResult.Failed,
                "Expected string to be \"Hello World\" with a length of 11, but \"Hello Mars\" has a length of 10."));
        var content = GenerateReport(features, "ErrorDiffFluent.html");
        Assert.Contains("error-diff", content);
    }
}
