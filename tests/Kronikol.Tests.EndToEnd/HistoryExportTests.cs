using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Export Filtered HTML copies the head and the visible features and nothing else: the sparklines and
/// verdict pills live inside the scenario headers and ride along, the aggregate History section sits
/// beside the timeline and is left behind on purpose, like the timeline itself.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class HistoryExportTests : PlaywrightTestBase
{
    public HistoryExportTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task The_filtered_export_keeps_the_sparklines_and_leaves_the_section_behind()
    {
        await Page.GotoReportAsync(HistoryReportHelper.Generate(TempDir, OutputDir, "HistoryExport.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        // the section is the History tab's panel since 5.0.0; the export copies what the Features tab shows
        await OpenReportTabAsync("history");
        await Expect(Page.Locator("#history-section")).ToBeVisibleAsync();
        await OpenReportTabAsync("features");

        var download = await Page.RunAndWaitForDownloadAsync(async () =>
        {
            await PressBoxExportAsync(Page, "Export Filtered HTML");
        });
        var path = Path.Combine(TempDir, $"export_{Guid.NewGuid():N}.html");
        await download.SaveAsAsync(path);
        var exported = ReportFileText.Read(path);

        Assert.Contains("class=\"history-sparkline\"", exported);
        Assert.Contains($"data-history-verdicts=\"broke\"", exported);
        Assert.Contains(">broke</span>", exported);
        Assert.DoesNotContain("<details id=\"history-section\"", exported);

        // Opened in a clean page, the exported scenario still shows its history.
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(d => d.setAttribute('open', ''))");
        var pay = Page.Locator($"details.scenario[data-stable-id='{HistoryReportHelper.PayId}']");
        await Expect(pay.Locator("summary .history-sparkline")).ToBeVisibleAsync();
        await Expect(pay.Locator("summary .history-verdict")).ToHaveTextAsync("broke");
        await Expect(Page.Locator("#history-section")).ToHaveCountAsync(0);
    }
}
