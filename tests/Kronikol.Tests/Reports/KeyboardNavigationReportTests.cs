using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Tests for keyboard navigation feature.
/// Arrow keys move between scenarios, Enter expands/collapses, / focuses search.
/// </summary>
public class KeyboardNavigationReportTests
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
    public void Report_contains_keyboard_navigation_script()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "KeyboardNavScript.html");
        Assert.Contains("addEventListener", content);
        Assert.Contains("keydown", content);
    }

    [Fact]
    public void Report_keyboard_handler_supports_arrow_keys()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "KeyboardNavArrows.html");
        Assert.Contains("ArrowDown", content);
        Assert.Contains("ArrowUp", content);
    }

    [Fact]
    public void Report_keyboard_handler_supports_enter_key()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "KeyboardNavEnter.html");
        Assert.Contains("Enter", content);
    }

    [Fact]
    public void Report_keyboard_handler_supports_slash_to_focus_search()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "KeyboardNavSlash.html");
        Assert.Contains("searchbar", content);
        // The / key should focus the search bar
        Assert.Contains("'/'", content);
    }

    [Fact]
    public void Report_scenarios_have_tabindex_for_focus()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "KeyboardNavTabindex.html");
        Assert.Contains("tabindex", content);
    }
}
