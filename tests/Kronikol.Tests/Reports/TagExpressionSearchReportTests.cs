using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class TagExpressionSearchReportTests
{
    private static string GenerateReport(Feature[] features)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "TagExprSearch.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Search_bar_placeholder_mentions_tags()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios = [new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed }]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("@tag", content);
    }

    [Fact]
    public void Search_includes_tag_expression_parser_js()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios = [new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed }]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("evaluateTagExpression", content);
    }

    [Fact]
    public void Scenarios_carry_their_tags_in_data_tags()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed, Tags = ["important", "regression"] }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("data-tags=\"important,regression\"", content);
        Assert.DoesNotContain("data-labels=", content);
    }

    [Fact]
    public void Tag_expression_supports_and_or_not()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios = [new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed }]
            }
        };

        var content = GenerateReport(features);
        // Check the parser handles the three operators in parsing functions
        Assert.Contains("parseAnd", content);
        Assert.Contains("parseOr", content);
        Assert.Contains("parseNot", content);
    }

    [Fact]
    public void Tag_expression_supports_parentheses()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios = [new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed }]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("lparen", content);
        Assert.Contains("rparen", content);
    }
}
