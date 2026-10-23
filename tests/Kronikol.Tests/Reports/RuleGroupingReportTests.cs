using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class RuleGroupingReportTests
{
    private static string GenerateReport(Feature[] features)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "RuleGrouping.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return ReportHtml.Read(path);
    }

    [Fact]
    public void Scenarios_with_rules_are_grouped_under_rule_heading()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Create account", Result = ExecutionResult.Passed, Rule = "Account creation" },
                    new Scenario { Id = "s2", DisplayName = "Verify email", Result = ExecutionResult.Passed, Rule = "Account creation" }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("<details class=\"rule\"", content);
        Assert.Contains("Account creation", content);
    }

    [Fact]
    public void Scenarios_without_rules_render_directly_under_feature()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.DoesNotContain("<details class=\"rule\"", content);
    }

    [Fact]
    public void Mixed_rule_and_no_rule_scenarios_render_correctly()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Ungrouped", Result = ExecutionResult.Passed },
                    new Scenario { Id = "s2", DisplayName = "Grouped", Result = ExecutionResult.Passed, Rule = "My Rule" }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("<details class=\"rule\"", content);
        Assert.Contains("My Rule", content);
        Assert.Contains("Ungrouped", content);
    }

    [Fact]
    public void Multiple_rules_render_separate_groups()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed, Rule = "Rule A" },
                    new Scenario { Id = "s2", DisplayName = "S2", Result = ExecutionResult.Passed, Rule = "Rule B" }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("Rule A", content);
        Assert.Contains("Rule B", content);
        // Count rule details elements
        var ruleCount = 0;
        var idx = 0;
        while ((idx = content.IndexOf("<details class=\"rule\"", idx)) != -1) { ruleCount++; idx++; }
        Assert.Equal(2, ruleCount);
    }
}
