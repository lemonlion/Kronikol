using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Tests for the export filtered view feature.
/// Users should be able to export the currently visible (filtered) scenarios
/// as standalone HTML or CSV.
/// </summary>
public class ExportFilteredViewReportTests
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
    public void Report_contains_export_button()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "ExportBtn.html");
        Assert.Contains("export-filtered", content);
    }

    [Fact]
    public void Report_contains_export_html_option()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "ExportHtml.html");
        Assert.Contains("export_html", content);
    }

    [Fact]
    public void Report_contains_export_csv_option()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "ExportCsv.html");
        Assert.Contains("export_csv", content);
    }

    [Fact]
    public void Report_export_uses_blob_download()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "ExportBlob.html");
        Assert.Contains("Blob", content);
        Assert.Contains("download", content);
    }
}
