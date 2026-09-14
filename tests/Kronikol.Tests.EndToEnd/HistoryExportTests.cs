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
        await Page.GotoAsync(HistoryReportHelper.Generate(TempDir, OutputDir, "HistoryExport.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await Expect(Page.Locator("#history-section")).ToBeVisibleAsync();

        var download = await Page.RunAndWaitForDownloadAsync(async () =>
        {
            await Page.Locator("button.export-btn", new() { HasTextString = "Export Filtered HTML" }).ClickAsync();
        });
        var path = Path.Combine(TempDir, $"export_{Guid.NewGuid():N}.html");
        await download.SaveAsAsync(path);
        var exported = await File.ReadAllTextAsync(path);

        Assert.Contains("class=\"history-sparkline\"", exported);
        Assert.Contains($"data-history-verdicts=\"broke\"", exported);
        Assert.Contains(">broke</span>", exported);
        Assert.DoesNotContain("<details id=\"history-section\"", exported);

        // Opened in a clean page, the exported scenario still shows its history.
        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(d => d.setAttribute('open', ''))");
        var pay = Page.Locator($"details.scenario[data-stable-id='{HistoryReportHelper.PayId}']");
        await Expect(pay.Locator("summary .history-sparkline")).ToBeVisibleAsync();
        await Expect(pay.Locator("summary .history-verdict")).ToHaveTextAsync("broke");
        await Expect(Page.Locator("#history-section")).ToHaveCountAsync(0);
    }
}
