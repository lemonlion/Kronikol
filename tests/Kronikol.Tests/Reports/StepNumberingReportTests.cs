using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class StepNumberingReportTests
{
    private static string GenerateReport(Feature[] features, bool showStepNumbers = false)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "StepNumbering.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            showStepNumbers: showStepNumbers);
        return ReportHtml.Read(path);
    }

    private static Feature[] FeaturesWithSteps(params ScenarioStep[] steps) =>
    [
        new Feature
        {
            DisplayName = "F1",
            Scenarios =
            [
                new Scenario
                {
                    Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                    Steps = steps
                }
            ]
        }
    ];

    [Fact]
    public void Steps_show_numbers_when_enabled()
    {
        var features = FeaturesWithSteps(
            new ScenarioStep { Text = "Given something", Status = ExecutionResult.Passed },
            new ScenarioStep { Text = "When action", Status = ExecutionResult.Passed },
            new ScenarioStep { Text = "Then result", Status = ExecutionResult.Passed });

        var content = GenerateReport(features, showStepNumbers: true);
        Assert.Contains("<span class=\"step-number\">1.</span>", content);
        Assert.Contains("<span class=\"step-number\">2.</span>", content);
        Assert.Contains("<span class=\"step-number\">3.</span>", content);
    }

    [Fact]
    public void Steps_omit_numbers_when_disabled()
    {
        var features = FeaturesWithSteps(
            new ScenarioStep { Text = "Given something", Status = ExecutionResult.Passed });

        var content = GenerateReport(features, showStepNumbers: false);
        Assert.DoesNotContain("<span class=\"step-number\">", content);
    }

    [Fact]
    public void Sub_steps_show_hierarchical_numbers()
    {
        var features = FeaturesWithSteps(
            new ScenarioStep { Text = "Step one", Status = ExecutionResult.Passed },
            new ScenarioStep
            {
                Text = "Step two", Status = ExecutionResult.Passed,
                SubSteps =
                [
                    new ScenarioStep { Text = "Sub A", Status = ExecutionResult.Passed },
                    new ScenarioStep { Text = "Sub B", Status = ExecutionResult.Passed }
                ]
            });

        var content = GenerateReport(features, showStepNumbers: true);
        Assert.Contains("<span class=\"step-number\">2.1.</span>", content);
        Assert.Contains("<span class=\"step-number\">2.2.</span>", content);
    }

    [Fact]
    public void Nested_sub_steps_show_three_level_numbers()
    {
        var features = FeaturesWithSteps(
            new ScenarioStep
            {
                Text = "Step one", Status = ExecutionResult.Passed,
                SubSteps =
                [
                    new ScenarioStep
                    {
                        Text = "Sub A", Status = ExecutionResult.Passed,
                        SubSteps =
                        [
                            new ScenarioStep { Text = "Deep X", Status = ExecutionResult.Passed },
                            new ScenarioStep { Text = "Deep Y", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            });

        var content = GenerateReport(features, showStepNumbers: true);
        Assert.Contains("<span class=\"step-number\">1.1.1.</span>", content);
        Assert.Contains("<span class=\"step-number\">1.1.2.</span>", content);
    }
}
