namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The opt-in <c>SeparateBackgroundSteps</c> form — background steps behind their own collapsible
/// disclosure, as they rendered before the combined list became the default. The same fixture as
/// <see cref="BackgroundRenderingTests"/>, generated with the flag on.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class SeparatedBackgroundRenderingTests : PlaywrightTestBase
{
    public SeparatedBackgroundRenderingTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task OpenReport(string fileName)
    {
        await Page.GotoAsync(GenerateReportWithSeparatedBackground(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();
    }

    [Fact]
    public async Task Scenarios_with_background_steps_render_background_section()
    {
        await OpenReport("SepBgSection.html");

        var background = Page.Locator("details.scenario").First.Locator("details.scenario-background");
        Assert.Equal(1, await background.CountAsync());
    }

    [Fact]
    public async Task Background_section_has_correct_summary_text()
    {
        await OpenReport("SepBgSummary.html");

        var bgSummary = Page.Locator("details.scenario").First.Locator("details.scenario-background > summary");
        Assert.Equal("Background Steps", await bgSummary.InnerTextAsync());
    }

    [Fact]
    public async Task Background_section_is_collapsed_by_default()
    {
        await OpenReport("SepBgCollapsed.html");

        var background = Page.Locator("details.scenario").First.Locator("details.scenario-background");
        await background.WaitForAsync();
        Assert.Null(await background.GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Background_section_contains_correct_number_of_steps()
    {
        await OpenReport("SepBgStepCount.html");

        var background = Page.Locator("details.scenario").First.Locator("details.scenario-background");
        await background.Locator("summary").ClickAsync();

        Assert.Equal(2, await background.Locator(".step").CountAsync());
    }

    [Fact]
    public async Task Background_steps_display_correct_text()
    {
        await OpenReport("SepBgStepText.html");

        var background = Page.Locator("details.scenario").First.Locator("details.scenario-background");
        await background.Locator("summary").ClickAsync();

        var stepsText = await background.Locator(".step").AllInnerTextsAsync();
        Assert.Contains(stepsText, t => t.Contains("the registration service is running"));
        Assert.Contains(stepsText, t => t.Contains("the database is available"));
    }

    [Fact]
    public async Task Background_section_renders_before_steps_section()
    {
        await OpenReport("SepBgOrder.html");

        var firstScenario = Page.Locator("details.scenario").First;
        var order = await firstScenario.EvaluateAsync<int[]>("""
            (el) => {
                var children = [...el.querySelectorAll(':scope > details')];
                return [
                    children.findIndex(d => d.classList.contains('scenario-background')),
                    children.findIndex(d => d.classList.contains('scenario-steps'))
                ];
            }
        """);

        Assert.True(order[0] >= 0, "Background section should exist");
        Assert.True(order[1] >= 0, "Steps section should exist");
        Assert.True(order[0] < order[1], "Background should come before Steps");
    }

    [Fact]
    public async Task Scenario_without_background_has_no_background_section()
    {
        await OpenReport("SepBgAbsent.html");

        // Third scenario (bg3) has no background steps
        var thirdScenario = Page.Locator("details.scenario").Nth(2);
        Assert.Equal(0, await thirdScenario.Locator("details.scenario-background").CountAsync());
    }

    [Fact]
    public async Task Multiple_scenarios_with_same_background_each_render_background()
    {
        await OpenReport("SepBgMultiple.html");

        Assert.Equal(1, await Page.Locator("details.scenario").Nth(0).Locator("details.scenario-background").CountAsync());
        Assert.Equal(1, await Page.Locator("details.scenario").Nth(1).Locator("details.scenario-background").CountAsync());
    }

    [Fact]
    public async Task Each_section_collapses_its_keywords_independently()
    {
        await OpenReport("SepBgKeywords.html");

        // bg4 — the Steps section still opens with its own Given rather than inheriting the background's.
        var fourth = Page.Locator("details.scenario").Nth(3);
        await fourth.Locator("details.scenario-background > summary").ClickAsync();

        var backgroundKeywords = await fourth.EvaluateAsync<string[]>("""
            (el) => [...el.querySelectorAll('details.scenario-background > .step .step-keyword')]
                .map(k => k.textContent.trim())
        """);
        var stepKeywords = await fourth.EvaluateAsync<string[]>("""
            (el) => [...el.querySelectorAll('details.scenario-steps > .step .step-keyword')]
                .map(k => k.textContent.trim())
        """);

        Assert.Equal(["Given", "And"], backgroundKeywords);
        Assert.Equal(["Given", "When", "Then"], stepKeywords);
    }

    [Fact]
    public async Task Search_matches_text_that_only_appears_in_a_background_step()
    {
        await Page.GotoAsync(GenerateReportWithSeparatedBackground("SepBgSearch.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

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
