using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Each scenario carries its last runs as a sparkline beside its duration badge and a pill for any
/// verdict other than stable (plans/CROSS_RUN_HISTORY_PLAN.md §8.1). The sparkline is one element whose
/// background paints a hard colour stop per run, so it costs the page one node per scenario.
/// </summary>
[Collection(PlaywrightCollections.Scenarios)]
public class HistorySparklineTests : PlaywrightTestBase
{
    public HistorySparklineTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task Open(string fileName)
    {
        await Page.GotoAsync(HistoryReportHelper.Generate(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        // Features start collapsed, and a collapsed feature hides its scenario headers.
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(d => d.setAttribute('open', ''))");
    }

    [Fact]
    public async Task A_regression_shows_its_sparkline_and_a_broke_pill_in_its_header()
    {
        await Open("HistorySparkline_Broke.html");

        var pay = Page.Locator($"details.scenario[data-stable-id='{HistoryReportHelper.PayId}']");
        await Expect(pay).ToHaveAttributeAsync("data-history-verdicts", "broke");

        var sparkline = pay.Locator("summary .history-sparkline");
        await Expect(sparkline).ToBeVisibleAsync();
        var title = await sparkline.GetAttributeAsync("title");
        Assert.NotNull(title);
        Assert.Contains("PPPPPPF", title);
        Assert.Contains("broke: passed in e2e:6:1", title);

        var pill = pay.Locator("summary .history-verdict");
        await Expect(pill).ToHaveTextAsync("broke");
        await Expect(pill).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("history-verdict-broke"));
    }

    [Fact]
    public async Task A_stable_scenario_has_a_sparkline_and_no_pill()
    {
        await Open("HistorySparkline_Stable.html");

        var refund = Page.Locator($"details.scenario[data-stable-id='{HistoryReportHelper.RefundId}']");
        await Expect(refund).ToHaveAttributeAsync("data-history-verdicts", "stable");
        await Expect(refund.Locator("summary .history-sparkline")).ToBeVisibleAsync();
        await Expect(refund.Locator("summary .history-verdict")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task The_sparkline_paints_one_colour_stop_per_run_as_a_single_element()
    {
        await Open("HistorySparkline_Paint.html");

        var painted = await Page.EvaluateAsync<string>($$"""
            () => {
                var el = document.querySelector("details.scenario[data-stable-id='{{HistoryReportHelper.PayId}}'] .history-sparkline");
                return el.childElementCount + '|' + getComputedStyle(el).backgroundImage;
            }
        """);
        var parts = painted.Split('|', 2);
        Assert.Equal("0", parts[0]); // no children: the runs are paint, not nodes
        Assert.Contains("linear-gradient", parts[1]);
        // Six passes and one failure: the failure's colour appears as one hard-stop pair, the pass colour as six.
        Assert.Equal(2, CountOf(parts[1], "rgb(191, 0, 0)"));
        Assert.Equal(12, CountOf(parts[1], "rgb(34, 139, 34)"));

        // A flaky scenario reads its flips the same way.
        var flaky = await Page.EvaluateAsync<string>($$"""
            () => getComputedStyle(document.querySelector("details.scenario[data-stable-id='{{HistoryReportHelper.RetryId}}'] .history-sparkline")).backgroundImage
        """);
        Assert.Equal(6, CountOf(flaky, "rgb(191, 0, 0)"));
    }

    [Fact]
    public async Task A_run_that_was_partial_and_a_position_that_was_not_a_test_are_painted_and_named()
    {
        // 3.23.0: a position an ingest folded unattributed traffic into is written N, and used to fall to
        // the default arm - an unnamed bar in the "bypassed" colour. A partial run's row now says it was one.
        await Page.GotoAsync(HistoryReportHelper.Generate(TempDir, OutputDir, "HistorySparkline_NotATest.html",
            (i, run) => i == 2 ? run with { Results = "PNPP" } : i == 4 ? run with { Partial = true } : run));
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(d => d.setAttribute('open', ''))");

        var refund = Page.Locator($"details.scenario[data-stable-id='{HistoryReportHelper.RefundId}']");
        var sparkline = refund.Locator("summary .history-sparkline");
        await Expect(sparkline).ToBeVisibleAsync();
        var title = await sparkline.GetAttributeAsync("title");
        Assert.NotNull(title);
        Assert.Contains("PPNPPPP", title);
        Assert.Contains("e2e:3:1", title.Split('\n').Single(line => line.Contains("not a test")));
        Assert.Contains("e2e:5:1", title.Split('\n').Single(line => line.Contains("partial run")));
        Assert.DoesNotContain("bypassed", title);

        var painted = await Page.EvaluateAsync<string>($$"""
            () => getComputedStyle(document.querySelector("details.scenario[data-stable-id='{{HistoryReportHelper.RefundId}}'] .history-sparkline")).backgroundImage
        """);
        // One hard-stop pair in the not-a-test grey, six passes, and nothing in the bypassed yellow.
        Assert.Equal(2, CountOf(painted, "rgb(220, 220, 220)"));
        Assert.Equal(12, CountOf(painted, "rgb(34, 139, 34)"));
        Assert.Equal(0, CountOf(painted, "rgb(184, 160, 0)"));
        // An N is no verdict: the scenario is as stable as it was.
        await Expect(refund).ToHaveAttributeAsync("data-history-verdicts", "stable");
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
