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
        await Page.GotoReportAsync(HistoryReportHelper.Generate(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        // since 5.0.0 the section is the History tab's panel (plans/OPTION_C_PLAN.md S3)
        await OpenReportTabAsync("history");
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

        // their text, not as drawn: the headings are uppercase labels since 5.0.0 (S11)
        var headings = await section.Locator(".history-list h4").AllTextContentsAsync();
        Assert.Contains(headings, h => h.StartsWith("New failures", StringComparison.Ordinal));
        Assert.Contains(headings, h => h.StartsWith("Flaky", StringComparison.Ordinal));
        // the two charts: a ring per run and a bar per run since 5.0.0, where 4.x drew bars in two stretched svgs (S11)
        await Expect(section.Locator(".history-chart")).ToHaveCountAsync(2);
        // the list shown, newest first (5.10.0: each card writes its runs in both orders)
        await Expect(section.Locator(".history-order-newest-first .history-rings > .history-ring")).ToHaveCountAsync(7);
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
    public async Task A_degraded_run_says_so_on_its_bars_and_a_healthy_one_does_not()
    {
        // 3.24.0 (#83): the fourth earlier run took three times as long in every scenario. Its ring and its bar
        // name it in their tooltips (since 5.0.0); the run being reported is healthy, so nothing is said above the charts.
        await Page.GotoReportAsync(HistoryReportHelper.Generate(TempDir, OutputDir, "HistorySection_Degraded.html",
            (i, run) => i == 3 ? run with { Durations = [.. run.Durations!.Select(d => d * 3)] } : run, wide: true));
        await OpenReportTabAsync("history");
        var section = Page.Locator("#history-section");
        await section.WaitForAsync();

        // the lists shown, newest first (5.10.0: each card writes its runs in both orders)
        var titles = await Page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('#history-section .history-chart .history-order-newest-first [role=listitem]')).map(t => t.getAttribute('title'))");
        var degraded = titles.Where(t => t.Contains("degraded")).ToArray();
        Assert.Equal(2, degraded.Length);
        Assert.All(degraded, title => Assert.StartsWith("e2e:4:1 ", title));
        Assert.All(degraded, title => Assert.EndsWith(" · degraded 3.0×", title));
        await Expect(section.Locator(".history-meta")).Not.ToContainTextAsync("degraded");
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
