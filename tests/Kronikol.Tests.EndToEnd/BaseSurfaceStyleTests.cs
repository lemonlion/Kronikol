using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The report's base surfaces in the Option C look (5.0.0, plans/OPTION_C_PLAN.md S1): feature and scenario cards,
/// the failure and its diff (expected green, actual red: round 17 item 17), steps lined up on one column (items 16 and
/// 18), and the parameter table's one even border in a rounded frame (round 16), each read from the browser's computed
/// styles and compared with the token it should use, in both themes.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class BaseSurfaceStyleTests : PlaywrightTestBase
{
    public BaseSurfaceStyleTests(PlaywrightFixture fixture) : base(fixture) { }

    private string GenerateSurfacesReport(string fileName)
    {
        ScenarioStep Step(string keyword, string text, ExecutionResult status, params ScenarioStep[] sub) =>
            new() { Keyword = keyword, Text = text, Status = status, SubSteps = sub.Length > 0 ? sub : null };
        Scenario Example(string id, string region, ExecutionResult result) => new()
        {
            Id = id, DisplayName = $"Process(region: {region})", Result = result, Duration = TimeSpan.FromSeconds(1),
            OutlineId = "Process", ExampleValues = new Dictionary<string, string> { ["region"] = region, ["amount"] = "100" },
            Steps = [Step("Given", $"a valid region {region}", result)]
        };
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "create", DisplayName = "Create order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(80),
                        Steps =
                        [
                            Step("Given", "a customer", ExecutionResult.Passed),
                            Step("When", "they order", ExecutionResult.Passed,
                                Step("Given", "a basket", ExecutionResult.Passed), Step("When", "it is paid", ExecutionResult.Passed)),
                            Step("Then", "the order is created", ExecutionResult.Passed)
                        ]
                    },
                    new Scenario
                    {
                        Id = "reject", DisplayName = "Reject order", Result = ExecutionResult.Failed, Duration = TimeSpan.FromMilliseconds(90),
                        ErrorMessage = "Expected:    the order is rejected\nActual:      the order is created",
                        Steps = [Step("Given", "a blocked customer", ExecutionResult.Passed), Step("Then", "the order is rejected", ExecutionResult.Failed)]
                    },
                    new Scenario
                    {
                        Id = "ruled", DisplayName = "Order within the limit", Rule = "Limits", Result = ExecutionResult.Passed,
                        Steps = [Step("Given", "a limit", ExecutionResult.Passed)]
                    },
                    Example("p1", "UK", ExecutionResult.Passed),
                    Example("p2", "US", ExecutionResult.Passed),
                    Example("p3", "DE", ExecutionResult.Passed)
                ]
            },
            new Feature
            {
                DisplayName = "Refunds",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "refund", DisplayName = "Refund an order", Result = ExecutionResult.Skipped,
                        Steps = [Step("Given", "a paid order", ExecutionResult.Skipped)]
                    }
                ]
            }
        };
        var path = ReportGenerator.GenerateHtmlReport(
            [], features, DateTime.UtcNow, DateTime.UtcNow, null, fileName, "Surfaces", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        var target = Path.Combine(TempDir, fileName);
        File.Copy(path, target, true);
        return new Uri(target).AbsoluteUri;
    }

    private async Task OpenAsync(string fileName, string theme)
    {
        await Context.AddInitScriptAsync($"try {{ localStorage.setItem('kronikol.theme', '{theme}'); }} catch (e) {{ }}");
        await Page.GotoAsync(GenerateSurfacesReport(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature, details.rule, details.scenario, details.scenario-steps, details.failure-result, details.step-collapsible').forEach(d => d.open = true)");
    }

    // A token resolved to the computed form a property takes, so the two can be compared.
    private Task<string> TokenAsync(string token, string property = "background-color") =>
        Page.EvaluateAsync<string>(
            $$"""
            () => {
                const probe = document.createElement('div');
                probe.style.setProperty('{{property}}', 'var({{token}})');
                document.body.appendChild(probe);
                const value = getComputedStyle(probe).getPropertyValue('{{property}}');
                probe.remove();
                return value;
            }
            """);

    private Task<string> StyleAsync(string selector, string property, string? pseudo = null) =>
        Page.EvaluateAsync<string>(
            "([s, p, ps]) => getComputedStyle(document.querySelector(s), ps).getPropertyValue(p)",
            new object?[] { selector, property, pseudo });

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task Features_are_cards_on_the_ground_and_scenarios_rows_inside_them(string theme)
    {
        await OpenAsync($"Surfaces_Cards_{theme}.html", theme);

        Assert.Equal(await TokenAsync("--kron-card"), await StyleAsync("details.feature", "background-color"));
        Assert.Equal("12px", await StyleAsync("details.feature", "border-top-left-radius"));
        Assert.Equal(await TokenAsync("--kron-hairline", "border-top-color"), await StyleAsync("details.feature", "border-top-color"));
        Assert.NotEqual("none", await StyleAsync("details.feature", "box-shadow"));
        Assert.Equal(await TokenAsync("--kron-strip"), await StyleAsync("details.feature > summary", "background-color"));
        Assert.Equal("rgba(0, 0, 0, 0)", await StyleAsync("#scenario-create-order", "background-color"));
        Assert.Equal("1px", await StyleAsync("#scenario-create-order", "border-bottom-width"));
        Assert.Equal(await TokenAsync("--kron-ground"), await StyleAsync("body", "background-color"));
    }

    [Fact]
    public async Task A_failed_scenario_is_marked_with_a_cross_in_the_fail_colour()
    {
        await OpenAsync("Surfaces_Failed.html", "light");

        Assert.Equal("\"✗\"", await StyleAsync("details.scenario > summary.h3.failed", "content", "::before"));
        Assert.Equal(await TokenAsync("--kron-fail", "color"), await StyleAsync("details.scenario > summary.h3.failed", "color"));
    }

    /// <summary>The card's header rules name the ink, and out-rank the status class's colour: the preview drew a
    /// skipped feature or scenario like one that ran, which 4.x greyed. Each keeps the skipped colour.</summary>
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task A_skipped_feature_and_scenario_keep_the_skipped_colour(string theme)
    {
        await OpenAsync($"Surfaces_Skipped_{theme}.html", theme);

        var skip = await TokenAsync("--kron-skip", "color");
        Assert.Equal(skip, await StyleAsync("details.feature > summary.h2.skipped", "color"));
        Assert.Equal(skip, await StyleAsync("#scenario-refund-an-order > summary.h3.skipped", "color"));
        Assert.Equal(await TokenAsync("--kron-ink", "color"), await StyleAsync("#scenario-create-order > summary.h3", "color"));
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_diff_shows_expected_green_and_actual_red_under_labels_without_colons(string theme)
    {
        await OpenAsync($"Surfaces_Diff_{theme}.html", theme);

        Assert.Equal(await TokenAsync("--kron-pass-soft"), await StyleAsync(".diff-expected", "background-color"));
        Assert.Equal(await TokenAsync("--kron-fail-soft"), await StyleAsync(".diff-actual", "background-color"));
        Assert.Equal("uppercase", await StyleAsync(".diff-label", "text-transform"));
        Assert.Equal(["Expected", "Actual"], await Page.Locator(".diff-label").AllTextContentsAsync());
        Assert.Equal(await TokenAsync("--kron-fail-soft"), await StyleAsync("details.failure-result", "background-color"));
    }

    [Fact]
    public async Task Every_step_of_a_scenario_has_its_status_on_one_column()
    {
        await OpenAsync("Surfaces_StepColumn.html", "light");

        var lefts = await Page.EvaluateAsync<double[]>(
            "() => Array.from(document.querySelectorAll('#scenario-create-order .scenario-steps > .step > .step-status, #scenario-create-order .scenario-steps > .step-collapsible > summary > .step-status')).map(e => e.getBoundingClientRect().left)");
        Assert.Equal(3, lefts.Length);
        Assert.True(lefts.Max() - lefts.Min() < 0.5, "status circles at " + string.Join(", ", lefts));
        // the collapsible step's triangle sits in the gutter to the left of that column
        var triangle = await Page.EvaluateAsync<double>(
            "() => { const s = document.querySelector('#scenario-create-order .scenario-steps > .step-collapsible > summary'); const r = s.getBoundingClientRect(); return r.left + parseFloat(getComputedStyle(s, '::before').left); }");
        Assert.True(triangle < lefts.Min(), $"triangle at {triangle}, circles at {lefts.Min()}");
        Assert.Equal(await TokenAsync("--kron-pass-soft"), await StyleAsync("#scenario-create-order .step-status.passed", "background-color"));
    }

    [Fact]
    public async Task The_parameter_table_has_one_even_border_in_a_rounded_frame()
    {
        await OpenAsync("Surfaces_ParamTable.html", "light");

        Assert.Equal("8px", await StyleAsync(".param-table-wrapper", "border-top-left-radius"));
        Assert.Equal("1px", await StyleAsync(".param-table-wrapper", "border-top-width"));
        var edges = await Page.EvaluateAsync<string[]>(
            """
            () => {
                const t = document.querySelector('.param-table-wrapper > .param-test-table');
                const first = t.querySelector('tbody tr td'), lastRow = t.querySelectorAll('tbody tr');
                const lastCell = lastRow[lastRow.length - 1].lastElementChild;
                const c = getComputedStyle;
                return [c(t).borderCollapse, c(first).borderLeftWidth, c(first).borderTopWidth, c(lastCell).borderRightWidth, c(lastCell).borderBottomWidth, c(first).borderRightWidth];
            }
            """);
        Assert.Equal(["separate", "0px", "0px", "0px", "0px", "1px"], edges);
    }
}
