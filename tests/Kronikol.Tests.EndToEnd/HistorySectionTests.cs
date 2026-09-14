using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The History section beside the timeline: the run's summary, the trend of the last runs, and the
/// lists of what changed, each scenario linked by its stable id so the link opens the scenario the way
/// <c>#sid-</c> links from Failures.md and <c>kronikol query</c> do.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class HistorySectionTests : PlaywrightTestBase
{
    public HistorySectionTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task Open(string fileName)
    {
        await Page.GotoAsync(HistoryReportHelper.Generate(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
    }

    [Fact]
    public async Task The_section_summarises_the_run_and_opens_when_there_is_something_to_say()
    {
        await Open("HistorySection_Summary.html");

        var section = Page.Locator("#history-section");
        await Expect(section).ToBeVisibleAsync();
        await Expect(section).ToHaveAttributeAsync("open", "");

        var summary = await section.Locator("summary").InnerTextAsync();
        Assert.Contains("1 broke", summary);
        Assert.Contains("1 flaky", summary);
        Assert.Contains("against 6 earlier runs", summary);

        var headings = await section.Locator(".history-list h4").AllInnerTextsAsync();
        Assert.Contains(headings, h => h.StartsWith("New failures", StringComparison.Ordinal));
        Assert.Contains(headings, h => h.StartsWith("Flaky", StringComparison.Ordinal));
        await Expect(section.Locator("svg.history-chart-svg")).ToHaveCountAsync(2);
        await Expect(section.Locator("svg.history-chart-svg").First.Locator("rect")).ToHaveCountAsync(7);
    }

    [Fact]
    public async Task A_link_in_the_section_opens_the_scenario_it_names()
    {
        await Open("HistorySection_Link.html");

        var pay = Page.Locator($"details.scenario[data-stable-id='{HistoryReportHelper.PayId}']");
        Assert.False(await pay.EvaluateAsync<bool>("d => d.hasAttribute('open')"));

        var link = Page.Locator($"#history-section a.history-link[href='#sid-{HistoryReportHelper.PayId}']");
        await Expect(link).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("Pay by card"));
        await link.ClickAsync();

        await Page.WaitForFunctionAsync(
            $"() => document.querySelector(\"details.scenario[data-stable-id='{HistoryReportHelper.PayId}']\").hasAttribute('open')",
            null, new() { Timeout = 5000, PollingInterval = 200 });
        Assert.EndsWith($"#sid-{HistoryReportHelper.PayId}", Page.Url);
    }

    [Fact]
    public async Task The_flaky_entry_carries_its_evidence_and_series()
    {
        await Open("HistorySection_Flaky.html");

        var entry = Page.Locator($"#history-section a.history-link[href='#sid-{HistoryReportHelper.RetryId}']").Locator("..");
        await Expect(entry.Locator(".history-verdict")).ToHaveTextAsync("flaky");
        var evidence = await entry.Locator(".history-evidence").InnerTextAsync();
        Assert.Contains("flips", evidence);
        await Expect(entry.Locator(".history-series")).ToHaveTextAsync("FPFPFPP");
    }
}
