using Kronikol.Reports;

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
        await Page.GotoReportAsync(GenerateReportWithBackground(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();
    }

    /// <summary>A scenario from the options, as a run writes one: a Background, then its own steps.</summary>
    private async Task OpenScenario(string name, ExecutionResult result, ScenarioStep[] background, ScenarioStep[] steps)
    {
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = Path.Combine(TempDir, name),
            GenerateSpecificationsReport = false,
            GenerateSpecificationsData = false,
        };
        var scenario = new Scenario
        {
            Id = name,
            DisplayName = "Pay",
            Result = result,
            BackgroundSteps = background,
            Steps = steps,
            ErrorMessage = result == ExecutionResult.Failed ? "it failed" : null,
        };
        lock (ReportTestHelper.WholePipeline)
        {
            DefaultDiagramsFetcher.Reset();
            ReportGenerator.CreateStandardReportsWithDiagrams([new Feature { DisplayName = "Orders", Scenarios = [scenario] }], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
            DefaultDiagramsFetcher.Reset();
        }
        await Page.GotoReportAsync(new Uri(Path.Combine(options.ReportsFolderPath, "TestRunReport.html")).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();
    }

    private static ScenarioStep Plain(string keyword, string text, ExecutionResult status = ExecutionResult.Passed) =>
        new() { Keyword = keyword, Text = text, Status = status };

    private static ScenarioStep Composite(string keyword, string text) =>
        Plain(keyword, text) with { SubSteps = [Plain("Given", text + " inside")] };

    [Fact]
    public async Task A_background_step_is_painted_with_its_accent_with_and_without_sub_steps()
    {
        await OpenScenario("BgAccent", ExecutionResult.Passed,
            [Plain("Given", "a customer is signed in"), Composite("And", "the basket is full")],
            [Plain("When", "the customer pays"), Composite("Then", "the receipt is sent")]);

        var borders = await Page.Locator("details.scenario").First.EvaluateAsync<string[]>("""
            (el) => [...el.querySelectorAll('details.scenario-steps > .step')].map(s => {
                const style = getComputedStyle(s);
                return style.borderLeftWidth + ' ' + style.borderLeftStyle;
            })
        """);

        Assert.Equal(["3px solid", "3px solid", "0px none", "0px none"], borders);
    }

    [Fact]
    public async Task The_accent_clears_a_steps_triangle_and_moves_it_out_of_line_with_none()
    {
        await OpenScenario("BgAccentPlace", ExecutionResult.Passed,
            [Plain("Given", "a customer is signed in"), Composite("And", "the basket is full")],
            [Plain("When", "the customer pays"), Composite("Then", "the receipt is sent")]);

        var places = await Page.Locator("details.scenario").First.EvaluateAsync<double[][]>("""
            (el) => [...el.querySelectorAll('details.scenario-steps > .step')].map(s => {
                const content = s.querySelector('summary') ?? s.querySelector('.step-status');
                const left = content.getBoundingClientRect().left;
                const accentEnd = s.getBoundingClientRect().left + parseFloat(getComputedStyle(s).borderLeftWidth);
                const triangle = content.tagName === 'SUMMARY' ? parseFloat(getComputedStyle(content, '::before').left) : 0;
                return [left, left + triangle - accentEnd];
            })
        """);

        // A plain step against a plain one, a step with sub-steps against one: each reads straight down the list.
        Assert.Equal(places[2][0], places[0][0], 1);
        Assert.Equal(places[3][0], places[1][0], 1);
        // Past the end of the accent, the status icon of a plain step and the triangle of one with sub-steps.
        Assert.InRange(places[0][1], 1, 16);
        Assert.InRange(places[1][1], 1, 16);
    }

    [Fact]
    public async Task A_failed_background_step_shows_the_failed_icon_on_its_muted_text()
    {
        await OpenScenario("BgFailed", ExecutionResult.Failed,
            [Plain("Given", "a customer is signed in", ExecutionResult.Failed)],
            []);

        var row = Page.Locator("details.scenario").First.Locator("details.scenario-steps > .step-background");
        Assert.True(await row.Locator(".step-status.failed").IsVisibleAsync());
        Assert.True(await row.Locator(".step-text").EvaluateAsync<bool>("""
            (text) => {
                const muted = document.createElement('span');
                muted.style.color = 'var(--kron-muted)';
                text.parentElement.appendChild(muted);
                const same = getComputedStyle(muted).color === getComputedStyle(text).color;
                muted.remove();
                return same;
            }
        """));
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
        // Expanding all scenarios opens features, rules and scenarios only, so the old
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
        await Page.GotoReportAsync(GenerateReportWithBackground("BgSearch.html"));
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
