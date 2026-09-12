using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class ClearAllFiltersReportTests
{
    private static Feature[] MakeFeatures() =>
    [
        new Feature
        {
            DisplayName = "Test Feature",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t1",
                    DisplayName = "Test A",
                    IsHappyPath = false,
                    Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromSeconds(1)
                }
            ]
        }
    ];

    private static string GenerateReport(string fileName)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], MakeFeatures(),
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Report_contains_clear_all_button()
    {
        var content = GenerateReport("ClearAllButton.html");
        Assert.Contains("clear_all_filters()", content);
        Assert.Contains(">Clear All</button>", content);
    }

    [Fact]
    public void Report_contains_clear_all_filters_function()
    {
        var content = GenerateReport("ClearAllFunction.html");
        Assert.Contains("function clear_all_filters()", content);
    }

    [Fact]
    public void Clear_all_function_clears_search()
    {
        var content = GenerateReport("ClearAllSearch.html");
        Assert.Contains("sb.value = ''", content);
    }

    [Fact]
    public void Clear_all_function_clears_status_toggles()
    {
        var content = GenerateReport("ClearAllStatus.html");
        Assert.Contains(".status-toggle.status-active", content);
    }

    [Fact]
    public void Clear_all_function_clears_the_filters_and_keeps_the_anchor()
    {
        // Since 3.1.0 the fragment is not simply wiped: a `#sid-`/`#scenario-` anchor is not filter
        // state, and somebody who followed a link to one scenario and then pressed Clear All has
        // cleared filters, not navigated away from it.
        var content = GenerateReport("ClearAllUrl.html");
        Assert.Contains("var keptAnchor = current_url_anchor();", content);
        Assert.Contains("history.replaceState(null, '', window.location.pathname + window.location.search + (keptAnchor ? '#' + keptAnchor : ''))", content);
    }
}
