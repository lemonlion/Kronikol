using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Tests that the report keeps no filter state in the browser: filters are only restored via the URL hash. Since 5.0.0
/// the one thing it stores is the reader's light or dark choice (plans/OPTION_C_PLAN.md Q1).
/// </summary>
public class PersistentFilterStateReportTests
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
    public void Report_stores_nothing_but_the_theme()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "NoLocalStorage.html");

        // every mention is a call naming the theme's key, so no filter or toggle is ever remembered
        var calls = Regex.Matches(content, @"localStorage\.(\w+)\(([^)]*)\)");
        Assert.NotEmpty(calls);
        Assert.All(calls, c => Assert.StartsWith("'kronikol.theme'", c.Groups[2].Value));
        Assert.Equal(calls.Count, Regex.Matches(content, "localStorage").Count);
        Assert.DoesNotContain("kronikol-filter-state", content);
    }

    [Fact]
    public void Report_does_not_call_restore_filter_state_on_load()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "NoRestoreOnLoad.html");
        // restore_filter_state exists as no-op stub but is never called in DOMContentLoaded
        Assert.DoesNotContain("restore_filter_state();", content);
    }

    [Fact]
    public void Report_still_parses_url_hash_on_load()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "UrlHashOnLoad.html");
        Assert.Contains("function apply_url_hash(", content);
    }
}
