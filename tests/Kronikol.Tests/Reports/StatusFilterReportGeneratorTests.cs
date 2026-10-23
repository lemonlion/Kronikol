using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class StatusFilterReportGeneratorTests
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
    public void Report_contains_status_filter_container()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "StatusFilterContainer.html");
        Assert.Contains("status-filters", content);
    }

    [Fact]
    public void Report_contains_passed_toggle_button()
    {
        var features = MakeFeatures(
            ("t1", "Create order", ExecutionResult.Passed),
            ("t2", "Fail order", ExecutionResult.Failed));
        var content = GenerateReport(features, "StatusFilterPassedBtn.html");
        Assert.Contains("data-status=\"Passed\"", content);
    }

    [Fact]
    public void Report_contains_failed_toggle_button()
    {
        var features = MakeFeatures(
            ("t1", "Create order", ExecutionResult.Passed),
            ("t2", "Fail order", ExecutionResult.Failed));
        var content = GenerateReport(features, "StatusFilterFailedBtn.html");
        Assert.Contains("data-status=\"Failed\"", content);
    }

    [Fact]
    public void Report_always_shows_all_status_buttons()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "StatusFilterOnlyPresent.html");
        Assert.Contains("data-status=\"Passed\"", content);
        Assert.Contains(">Failed</button>", content);
        Assert.Contains(">Skipped</button>", content);
    }

    [Fact]
    public void Report_scenario_has_data_status_attribute()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "StatusFilterDataAttr.html");
        Assert.Contains("data-status=\"Passed\"", content);
    }

    [Fact]
    public void Report_failed_scenario_has_failed_data_status()
    {
        var features = MakeFeatures(("t1", "Fail order", ExecutionResult.Failed));
        var content = GenerateReport(features, "StatusFilterFailedAttr.html");
        Assert.Matches("class=\"scenario[^\"]*\"[^>]*data-status=\"Failed\"", content);
    }

    [Fact]
    public void Report_contains_status_filter_javascript_function()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "StatusFilterJs.html");
        Assert.Contains("filter_statuses", content);
    }

    [Fact]
    public void Report_contains_status_hidden_css_class()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "StatusFilterCss.html");
        Assert.Contains("status-hidden", content);
    }

    [Fact]
    public void Report_skipped_scenario_has_skipped_data_status()
    {
        var features = MakeFeatures(("t1", "Skip order", ExecutionResult.Skipped));
        var content = GenerateReport(features, "StatusFilterSkippedAttr.html");
        Assert.Matches("data-status=\"Skipped\"", content);
    }

    [Fact]
    public void Report_always_shows_skipped_toggle_even_when_no_skipped_scenarios()
    {
        var features = MakeFeatures(
            ("t1", "Create order", ExecutionResult.Passed),
            ("t2", "Fail order", ExecutionResult.Failed));
        var content = GenerateReport(features, "StatusFilterNoSkipped.html");
        Assert.Contains(">Skipped</button>", content);
    }
}
