namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Background steps rendered inline with the scenario's own — the default since background steps
/// stopped living behind their own disclosure. The separated form has its own suite in
/// <see cref="SeparatedBackgroundRenderingTests"/>.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class BackgroundRenderingTests : PlaywrightTestBase
{
    public BackgroundRenderingTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task OpenReport(string fileName)
    {
        await Page.GotoAsync(GenerateReportWithBackground(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();
    }

    [Fact]
    public async Task Scenarios_with_background_steps_render_no_separate_background_section()
    {
        await OpenReport("BgSection.html");

        var firstScenario = Page.Locator("details.scenario").First;
        Assert.Equal(0, await firstScenario.Locator("details.scenario-background").CountAsync());
        Assert.Equal(2, await firstScenario.Locator("details.scenario-steps .step-background").CountAsync());
    }

    [Fact]
    public async Task The_single_step_section_summary_reads_Steps()
    {
        await OpenReport("BgSummary.html");

        var summaries = Page.Locator("details.scenario").First.Locator("details.scenario-steps > summary");
        Assert.Equal(1, await summaries.CountAsync());
        Assert.Equal("Steps", await summaries.First.InnerTextAsync());
    }

    [Fact]
    public async Task The_combined_step_list_is_open_by_default_and_needs_no_extra_click()
    {
        // Expand All Scenarios is wired to details.feature and details.scenario only, so the old
        // Background Steps disclosure was never reachable from it. The combined list has no such dead end.
        await OpenReport("BgCollapsed.html");

        var steps = Page.Locator("details.scenario").First.Locator("details.scenario-steps");
        await steps.WaitForAsync();
        Assert.NotNull(await steps.GetAttributeAsync("open"));
        Assert.True(await steps.Locator(".step").First.IsVisibleAsync());
    }

    [Fact]
    public async Task The_combined_list_holds_the_background_and_the_scenario_steps()
    {
        await OpenReport("BgStepCount.html");

        var steps = Page.Locator("details.scenario").First.Locator("details.scenario-steps > .step");
        Assert.Equal(4, await steps.CountAsync());
    }

    [Fact]
    public async Task Background_steps_display_correct_text_without_opening_anything()
    {
        await OpenReport("BgStepText.html");

        var stepsText = await Page.Locator("details.scenario").First.Locator("details.scenario-steps .step").AllInnerTextsAsync();
        Assert.Contains(stepsText, t => t.Contains("the registration service is running"));
        Assert.Contains(stepsText, t => t.Contains("the database is available"));
        Assert.Contains(stepsText, t => t.Contains("I register with a valid email"));
    }

    [Fact]
    public async Task Background_steps_are_the_first_entries_of_the_combined_list()
    {
        await OpenReport("BgOrder.html");

        var backgroundFlags = await Page.Locator("details.scenario").First.EvaluateAsync<bool[]>("""
            (el) => [...el.querySelectorAll('details.scenario-steps > .step')]
                .map(s => s.classList.contains('step-background'))
        """);

        Assert.Equal([true, true, false, false], backgroundFlags);
    }

    [Fact]
    public async Task Scenario_without_background_has_no_background_marked_steps()
    {
        await OpenReport("BgAbsent.html");

        // Third scenario (bg3) has no background steps
        var thirdScenario = Page.Locator("details.scenario").Nth(2);
        Assert.Equal(0, await thirdScenario.Locator(".step-background").CountAsync());
        Assert.Equal(3, await thirdScenario.Locator("details.scenario-steps > .step").CountAsync());
    }

    [Fact]
    public async Task Multiple_scenarios_with_same_background_each_show_it_inline()
    {
        await OpenReport("BgMultiple.html");

        Assert.Equal(2, await Page.Locator("details.scenario").Nth(0).Locator(".step-background").CountAsync());
        Assert.Equal(2, await Page.Locator("details.scenario").Nth(1).Locator(".step-background").CountAsync());
    }

    [Fact]
    public async Task Repeated_given_keyword_renders_as_and()
    {
        await OpenReport("BgRepeatedGiven.html");

        // bg4 — background "Given … / And …" followed by scenario steps opening on "Given".
        var fourth = Page.Locator("details.scenario").Nth(3);
        var keywords = await fourth.EvaluateAsync<string[]>("""
            (el) => [...el.querySelectorAll('details.scenario-steps > .step .step-keyword')]
                .map(k => k.textContent.trim())
        """);

        Assert.Equal(["Given", "And", "And", "When", "Then"], keywords);
    }

    [Fact]
    public async Task Each_scenario_renders_exactly_one_step_section()
    {
        await OpenReport("BgOneSection.html");

        var sections = await Page.EvaluateAsync<int[]>("""
            () => [...document.querySelectorAll('details.scenario')]
                .map(s => s.querySelectorAll(':scope > details.scenario-steps').length)
        """);

        Assert.All(sections, count => Assert.Equal(1, count));
        Assert.Equal(0, await Page.Locator("details.scenario-background").CountAsync());
    }

    [Fact]
    public async Task Search_matches_text_that_only_appears_in_a_background_step()
    {
        await Page.GotoAsync(GenerateReportWithBackground("BgSearch.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // "the database is available" lives only in the three backgrounds; bg3 has none.
        await FillSearchBar("the database is available");
        await Page.WaitForFunctionAsync(
            "() => Array.from(document.querySelectorAll('.scenario')).filter(s => getComputedStyle(s).display !== 'none').length === 3",
            null, new() { Timeout = 5000, PollingInterval = 200 });

        var visible = await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('.scenario'))
                .filter(s => getComputedStyle(s).display !== 'none')
                .map(s => s.querySelector('summary').textContent.trim())
        """);

        Assert.Contains(visible, v => v.Contains("Register with valid email"));
        Assert.DoesNotContain(visible, v => v.Contains("View profile without background"));
    }
}
