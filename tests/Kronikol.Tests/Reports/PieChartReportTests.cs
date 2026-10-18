using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class PieChartReportTests
{
    private static string GenerateReport(Feature[] features, bool includeTestRunData = true)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "PieChart.html", "Test", includeTestRunData,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Report_contains_svg_pie_chart()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed },
                    new Scenario { Id = "s2", DisplayName = "S2", Result = ExecutionResult.Failed, ErrorMessage = "err" }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("<svg", content);
        Assert.Contains("<div class=\"run-ring\">", content);
    }

    [Fact]
    public void Pie_chart_shows_pass_rate_percentage()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios = Enumerable.Range(1, 8).Select(i => new Scenario { Id = $"p{i}", DisplayName = $"P{i}", Result = ExecutionResult.Passed })
                    .Concat([
                        new Scenario { Id = "f1", DisplayName = "F1", Result = ExecutionResult.Failed, ErrorMessage = "err" },
                        new Scenario { Id = "f2", DisplayName = "F2", Result = ExecutionResult.Failed, ErrorMessage = "err" }
                    ]).ToArray()
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("80%", content);
    }

    [Fact]
    public void Pie_chart_omits_zero_count_segments()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed },
                    new Scenario { Id = "s2", DisplayName = "S2", Result = ExecutionResult.Passed }
                ]
            }
        };

        var content = GenerateReport(features);
        // The SVG pie chart should have only a green segment
        var svgStart = content.IndexOf("<div class=\"run-ring\"");
        var svgEnd = content.IndexOf("</div>", svgStart);
        var svgContent = content[svgStart..svgEnd];
        Assert.Contains("class=\"ring-pass\"", svgContent); // passed, coloured by the token (5.0.0)
        Assert.DoesNotContain("class=\"ring-fail\"", svgContent); // no failed arc
    }

    [Fact]
    public void Pie_chart_not_shown_in_specifications()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios = [new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed }]
            }
        };

        var content = GenerateReport(features, includeTestRunData: false);
        Assert.DoesNotContain("<div class=\"run-ring\"", content);
    }

    [Fact]
    public void Pie_chart_shows_correct_colors()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed },
                    new Scenario { Id = "s2", DisplayName = "S2", Result = ExecutionResult.Failed, ErrorMessage = "err" },
                    new Scenario { Id = "s3", DisplayName = "S3", Result = ExecutionResult.Skipped },
                    new Scenario { Id = "s4", DisplayName = "S4", Result = ExecutionResult.Bypassed }
                ]
            }
        };

        var content = GenerateReport(features);
        // 5.0.0 (plans/OPTION_C_PLAN.md S2): an arc per status, coloured through its class by the theme's tokens
        Assert.Contains("<circle class=\"ring-pass\"", content);
        Assert.Contains("<circle class=\"ring-fail\"", content);
        Assert.Contains("<circle class=\"ring-skip\"", content);
        Assert.Contains("<circle class=\"ring-bypass\"", content);
    }
}
