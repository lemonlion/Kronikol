namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Scenarios)]
public class JumpToFailureTests : PlaywrightTestBase
{
    public JumpToFailureTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Jump_to_failure_scrolls_scenario_title_into_view()
    {
        await Page.GotoAsync(GenerateReport("JumpToFailureScroll.html"));

        var jumpBtn = Page.Locator("button.jump-to-failure");
        await jumpBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await jumpBtn.ClickAsync();

        // Wait for smooth scroll to settle
        await Page.WaitForTimeoutAsync(1000);

        var failedSummary = Page.Locator("details.scenario[data-status='Failed'] > summary");
        var isInViewport = await failedSummary.EvaluateAsync<bool>("""
            el => {
                var rect = el.getBoundingClientRect();
                return rect.top >= 0 && rect.top < window.innerHeight;
            }
        """);

        Assert.True(isInViewport, "Failed scenario summary should be visible in viewport after jump-to-failure");
    }

    [Fact]
    public async Task Jump_to_failure_reaches_a_failure_far_down_a_long_report()
    {
        // The report above has two features, so every feature between the button and the failure had
        // been drawn. In a long report most have not: each counts as its 500 px content-visibility
        // placeholder, a smooth scroll aimed across them ends past the failure, at the bottom of the page
        // (StableIdDeepLinkTests has the same fault for a #sid- link).
        await Page.GotoAsync(GenerateLongReport("JumpToFailureFarDown.html", "Scenario 30.2"));

        await Page.Locator("button.jump-to-failure").ClickAsync();
        await WaitForScrollToSettleAsync();

        Assert.True(await SummaryIsOnScreenAsync("details.scenario[data-status='Failed']"),
            "The failure's title should be on screen once the page has stopped scrolling");
    }

    [Fact]
    public async Task Jump_to_failure_counter_updates()
    {
        await Page.GotoAsync(GenerateReport("JumpToFailureCounter.html"));

        var jumpBtn = Page.Locator("button.jump-to-failure");
        await jumpBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var counter = Page.Locator("#failure-counter");
        var text = await counter.TextContentAsync();
        Assert.Contains("0/", text!);

        // Use JS dispatch because Playwright click may be intercepted by fixed-position overlay
        await Page.EvaluateAsync("() => document.querySelector('button.jump-to-failure').click()");

        // Wait for the counter to update (the click handler updates textContent)
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.getElementById('failure-counter');
                return c && c.textContent.includes('1/');
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        text = await counter.TextContentAsync();
        Assert.Contains("1/", text!);
    }

    [Fact]
    public async Task Jump_to_failure_opens_feature_and_scenario()
    {
        await Page.GotoAsync(GenerateReport("JumpToFailureOpens.html"));

        var jumpBtn = Page.Locator("button.jump-to-failure");
        await jumpBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Use JS dispatch because Playwright click may be intercepted by fixed-position overlay
        await Page.EvaluateAsync("() => document.querySelector('button.jump-to-failure').click()");

        // Wait for the scenario to open
        await Page.WaitForFunctionAsync("""
            () => {
                var s = document.querySelector('details.scenario[data-status="Failed"]');
                return s && s.hasAttribute('open');
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        var scenario = Page.Locator("details.scenario[data-status='Failed']");
        Assert.True(await scenario.GetAttributeAsync("open") is not null, "Failed scenario should be open");

        var feature = Page.Locator("details.feature:has(details.scenario[data-status='Failed'])");
        Assert.True(await feature.GetAttributeAsync("open") is not null, "Parent feature should be open");
    }
}
