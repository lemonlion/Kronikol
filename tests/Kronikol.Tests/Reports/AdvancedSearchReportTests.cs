using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class AdvancedSearchReportTests
{
    private static string GenerateReport(Feature[] features)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "AdvancedSearch.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return ReportHtml.Read(path);
    }

    [Fact]
    public void Report_contains_advanced_search_functions()
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
        Assert.Contains("isAdvancedSearch", content);
        Assert.Contains("advancedSearchTokenise", content);
        Assert.Contains("advancedSearchParse", content);
        Assert.Contains("advancedSearchEvaluate", content);
        Assert.Contains("advancedSearchMatch", content);
    }

    [Fact]
    public void Run_search_scenarios_calls_isAdvancedSearch()
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
        // The run_search_scenarios function should check for advanced search
        Assert.Contains("isAdvancedSearch(input)", content);
    }

    [Fact]
    public void Search_bar_placeholder_mentions_advanced_syntax()
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
        Assert.Contains("&&", content);
        Assert.Contains("||", content);
        Assert.Contains("!!", content);
        Assert.Contains("$status", content);
    }

    [Fact]
    public void Report_still_contains_legacy_search_functions()
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
        // Legacy functions must still be present for backward compatibility
        Assert.Contains("parseSearchTokensIncludingQuotes", content);
        Assert.Contains("evaluateTagExpression", content);
    }
}
