using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Tests for the URL-anchored filters feature.
/// Filter state should be encoded in the URL hash so filtered views can be shared via link.
/// </summary>
public class UrlAnchoredFilterReportTests
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

    [Fact]
    public void Report_contains_update_url_hash_function()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "UrlHashUpdate.html");
        Assert.Contains("update_url_hash", content);
    }

    [Fact]
    public void Report_contains_apply_url_hash_function()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "UrlHashParse.html");
        Assert.Contains("function apply_url_hash(", content);
    }

    [Fact]
    public void Report_updates_hash_on_filter_change()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "UrlHashOnFilter.html");
        // since 5.2.0 through the navigation script, the page's one writer of the session history, with absolute addresses
        Assert.Contains("window.kronikol_nav.wrote(url_hash_filters().join('&'))", content);
        Assert.Contains("sh.replaceState(wrapped(entry, true), '', hrefOf(hash))", content);
        Assert.Contains("sh.pushState(wrapped(entry, false), '', hrefOf(hash))", content);
        Assert.DoesNotContain("history.replaceState(null", content);
    }

    [Fact]
    public void Report_restores_filters_from_hash_on_load()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "UrlHashRestore.html");
        // On load the navigation script puts the address on the page, its filters through apply_url_hash
        Assert.Contains("window.kronikol_nav.start()", content);
        Assert.Contains("apply_url_hash(a.filters)", content);
    }
}
