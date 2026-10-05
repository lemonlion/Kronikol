using Microsoft.Playwright;
using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Export Filtered CSV writes a row per visible scenario: feature, scenario, status and duration. A name
/// column holds the name alone. A scenario's header also holds its labels, its duration badge, its history
/// pill and the copy and link buttons, and a feature's header its endpoint and labels; until 4.5.1 each name
/// column took its header's whole text, so a row read "Pay by card Payments 120ms📋🔗".
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class ExportFilteredCsvTests : PlaywrightTestBase
{
    public ExportFilteredCsvTests(PlaywrightFixture fixture) : base(fixture) { }

    private string GenerateCsvReport(string fileName)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders", Endpoint = "/api/orders", Labels = ["smoke"],
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "csv-pay", DisplayName = "Pay by card", IsHappyPath = true, Labels = ["Payments"],
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(120),
                    },
                    new Scenario
                    {
                        Id = "csv-refund", DisplayName = "Refund \"partial\", then full",
                        Result = ExecutionResult.Failed, ErrorMessage = "refused", Duration = TimeSpan.FromMilliseconds(80),
                    },
                ]
            },
            new Feature
            {
                DisplayName = "Inventory",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "csv-adjust-1", DisplayName = "Adjust stock(by: 1)", OutlineId = "Adjust stock",
                        ExampleValues = new Dictionary<string, string> { ["by"] = "1" },
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10),
                    },
                    new Scenario
                    {
                        Id = "csv-adjust-2", DisplayName = "Adjust stock(by: 2)", OutlineId = "Adjust stock",
                        ExampleValues = new Dictionary<string, string> { ["by"] = "2" },
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(20),
                    },
                ]
            },
        };

        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "CSV Export Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            groupParameterizedTests: true);

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task<string[]> ExportCsv()
    {
        var download = await Page.RunAndWaitForDownloadAsync(async () =>
        {
            await Page.Locator("button.export-btn", new() { HasTextString = "Export Filtered CSV" }).ClickAsync();
        });
        var path = Path.Combine(TempDir, $"export_{Guid.NewGuid():N}.csv");
        await download.SaveAsAsync(path);
        return (await File.ReadAllTextAsync(path)).Split('\n');
    }

    [Fact]
    public async Task Each_row_holds_the_feature_and_scenario_names_alone()
    {
        await Page.GotoAsync(GenerateCsvReport("CsvExport.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var lines = await ExportCsv();

        Assert.Equal(
        [
            "Feature,Scenario,Status,Duration",
            "\"Orders\",\"Pay by card\",\"Passed\",\"120\"",
            "\"Orders\",\"Refund \"\"partial\"\", then full\",\"Failed\",\"80\"",
            "\"Inventory\",\"Adjust stock\",\"Passed\",\"30\"",
        ], lines);
    }

    [Fact]
    public async Task A_filtered_export_lists_only_the_visible_scenarios()
    {
        await Page.GotoAsync(GenerateCsvReport("CsvExportFiltered.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await FillSearchBar("refund");
        await Page.WaitForFunctionAsync(
            "() => [...document.querySelectorAll('details.scenario')].filter(s => s.style.display !== 'none').length === 1",
            null, new() { PollingInterval = 200 });

        var lines = await ExportCsv();

        Assert.Equal(
        [
            "Feature,Scenario,Status,Duration",
            "\"Orders\",\"Refund \"\"partial\"\", then full\",\"Failed\",\"80\"",
        ], lines);
    }
}
