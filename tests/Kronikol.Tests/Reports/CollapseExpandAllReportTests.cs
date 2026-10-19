using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Expand and collapse all features, or all scenarios (5.0.0, plans/OPTION_C_PLAN.md S4, D22): each level has a label and
/// two icon buttons, expand and collapse, in place of the 4.x button whose label flipped between the two.
/// </summary>
public class CollapseExpandAllReportTests
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
        return File.ReadAllText(path);
    }

    [Theory]
    [InlineData("features")]
    [InlineData("scenarios")]
    public void Report_has_an_expand_and_a_collapse_button_for_each_level(string noun)
    {
        var content = GenerateReport(MakeFeatures(("t1", "Create order", ExecutionResult.Passed)), $"CollapseExpand_{noun}.html");
        Assert.Contains($"<span class=\"fold-group\" role=\"group\" aria-label=\"Expand or collapse all {noun}\">", content);
        Assert.Contains($"<button type=\"button\" class=\"fold-btn fold-expand\" data-fold=\"{noun}\" title=\"Expand all {noun}\" aria-label=\"Expand all {noun}\"", content);
        Assert.Contains($"<button type=\"button\" class=\"fold-btn fold-collapse\" data-fold=\"{noun}\" title=\"Collapse all {noun}\" aria-label=\"Collapse all {noun}\"", content);
    }

    [Fact]
    public void Report_carries_the_script_the_buttons_need_and_none_of_the_4x_toggle()
    {
        var content = GenerateReport(MakeFeatures(("t1", "Create order", ExecutionResult.Passed)), "CollapseExpandJs.html");
        Assert.Contains("t.closest('.fold-btn[data-fold]')", content);
        Assert.DoesNotContain("toggle_expand_collapse", content);
        Assert.DoesNotContain("class=\"collapse-expand-all\"", content);
    }

    [Fact]
    public void A_report_with_no_scenarios_dims_both_buttons()
    {
        var content = GenerateReport([new Feature { DisplayName = "Empty", Scenarios = [] }], "CollapseExpandNone.html");
        Assert.Contains("data-fold=\"scenarios\" title=\"Expand all scenarios\" aria-label=\"Expand all scenarios\" aria-disabled=\"true\"", content);
        Assert.Contains("data-fold=\"scenarios\" title=\"Collapse all scenarios\" aria-label=\"Collapse all scenarios\" aria-disabled=\"true\"", content);
    }
}
