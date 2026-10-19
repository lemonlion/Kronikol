using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A step's status icon shows the tooltip 4.26.9 wrote as a title on every icon, from a text the page holds once
/// (<c>plans/REPORT_FORMAT_PLAN.md</c> R2): the page gives an icon its title the first time it is hovered, or its
/// step's summary is focused. Read from the page as the browser has it, for every status, and for a chain of passed
/// composite steps 20 deep over one skipped leaf, each of which carried the long text.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class StepStatusTooltipTests(PlaywrightFixture fixture) : PlaywrightTestBase(fixture)
{
    // 4.26.9's texts, as each icon carried them.
    private const string Passed = "Passed — all assertions in this step passed";
    private const string PassedSkipped = "Passed (with skipped sub-steps) — all assertions passed, but one or more sub-steps were skipped. Skipped steps did not execute and also prevented execution of subsequent steps";
    private const string PassedBypassed = "Passed (with bypassed sub-steps) — all assertions passed, but one or more sub-steps were bypassed (intentionally skipped over without preventing execution of subsequent steps)";
    private const string Failed = "Failed — this step threw an exception or an assertion failed";
    private const string Skipped = "Skipped — this step did not execute because it was intentionally skipped, either at the scenario level, or at the step level. In the latter case the skip also prevented execution of subsequent steps";
    private const string Bypassed = "Bypassed — some or all of the logic in this step was intentionally skipped over without preventing execution of subsequent steps";
    private const string SkippedAfterFailure = "Skipped after failure — this step was never reached because an earlier step failed";

    [Theory]
    [InlineData("a passed leaf", Passed)]
    [InlineData("a composite with a skipped child", PassedSkipped)]
    [InlineData("a composite with a bypassed child", PassedBypassed)]
    [InlineData("a failed leaf", Failed)]
    [InlineData("the skipped child", Skipped)]
    [InlineData("the bypassed child", Bypassed)]
    [InlineData("a step never reached", SkippedAfterFailure)]
    public async Task Hovering_a_status_icon_shows_its_text(string step, string expected)
    {
        await Page.GotoAsync(Generate("StatusHover.html"));
        await OpenEveryStep();
        var icon = Icon(step);

        Assert.Null(await icon.GetAttributeAsync("title"));
        await icon.HoverAsync();

        Assert.Equal(expected, await icon.GetAttributeAsync("title"));
    }

    [Fact]
    public async Task Focusing_a_composite_step_gives_its_icon_its_text()
    {
        await Page.GotoAsync(Generate("StatusFocus.html"));
        await OpenEveryStep();
        var summary = Summary("a composite with a bypassed child");

        await summary.FocusAsync();

        Assert.Equal(PassedBypassed, await Icon("a composite with a bypassed child").GetAttributeAsync("title"));
        // Only that step's own icon: its child's is given its text when it is hovered.
        Assert.Null(await Icon("the bypassed child").GetAttributeAsync("title"));
    }

    [Fact]
    public async Task Every_passed_step_above_a_skipped_leaf_20_deep_says_so()
    {
        await Page.GotoAsync(Generate("StatusDeep.html"));
        await OpenEveryStep();

        for (var depth = 0; depth < 20; depth++)
        {
            var icon = Icon($"level {depth}");
            await icon.HoverAsync();
            Assert.Equal(PassedSkipped, await icon.GetAttributeAsync("title"));
        }
        var leaf = Icon("the skipped leaf");
        await leaf.HoverAsync();
        Assert.Equal(Skipped, await leaf.GetAttributeAsync("title"));

        // Focus reaches the same text, from the keyboard's side.
        await Page.ReloadAsync();
        await OpenEveryStep();
        await Summary("level 12").FocusAsync();
        Assert.Equal(PassedSkipped, await Icon("level 12").GetAttributeAsync("title"));
    }

    [Fact]
    public async Task An_exported_page_gives_its_icons_their_text()
    {
        await Page.GotoAsync(Generate("StatusExport.html"));
        var download = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.Locator("button.export-btn", new() { HasTextString = "Export Filtered HTML" }).ClickAsync());
        var path = Path.Combine(TempDir, $"export_{Guid.NewGuid():N}.html");
        await download.SaveAsAsync(path);

        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        await OpenEveryStep();
        var icon = Icon("a failed leaf");
        await icon.HoverAsync();

        Assert.Equal(Failed, await icon.GetAttributeAsync("title"));
    }

    private const string StepText = "span[contains(concat(' ', @class, ' '), ' step-text ')]";

    // A composite step's summary, by its own text exactly: "level 1" is not "level 12".
    private ILocator Summary(string step) =>
        Page.Locator($"xpath=//details[contains(concat(' ', @class, ' '), ' step ')]/summary[{StepText}[normalize-space(.)='{step}']]").First;

    // The icon on the step's own line, a composite's summary or a leaf's div, beside the step's text.
    private ILocator Icon(string step) =>
        Page.Locator($"xpath=//span[contains(concat(' ', @class, ' '), ' step-status ')][following-sibling::{StepText}[normalize-space(.)='{step}']]").First;

    private async Task OpenEveryStep()
    {
        await Page.EvaluateAsync("() => document.querySelectorAll('details').forEach(d => d.open = true)");
        await Page.WaitForFunctionAsync("() => document.querySelectorAll('.step-status').length > 0", null,
            new() { PollingInterval = 200 });
    }

    private string Generate(string fileName)
    {
        var leaf = new ScenarioStep { Keyword = "And", Text = "the skipped leaf", Status = ExecutionResult.Skipped };
        for (var depth = 19; depth >= 0; depth--)
            leaf = new ScenarioStep { Keyword = "And", Text = $"level {depth}", Status = ExecutionResult.Passed, SubSteps = [leaf] };

        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Statuses",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "statuses", DisplayName = "Every status", Result = ExecutionResult.Failed,
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "a passed leaf", Status = ExecutionResult.Passed },
                            new ScenarioStep
                            {
                                Keyword = "And", Text = "a composite with a skipped child", Status = ExecutionResult.Passed,
                                SubSteps = [new ScenarioStep { Keyword = "And", Text = "the skipped child", Status = ExecutionResult.Skipped }]
                            },
                            new ScenarioStep
                            {
                                Keyword = "And", Text = "a composite with a bypassed child", Status = ExecutionResult.Passed,
                                SubSteps = [new ScenarioStep { Keyword = "And", Text = "the bypassed child", Status = ExecutionResult.Bypassed }]
                            },
                            new ScenarioStep { Keyword = "When", Text = "a failed leaf", Status = ExecutionResult.Failed },
                            new ScenarioStep { Keyword = "Then", Text = "a step never reached", Status = ExecutionResult.SkippedAfterFailure }
                        ]
                    },
                    new Scenario { Id = "deep", DisplayName = "Deep chain", Result = ExecutionResult.Passed, Steps = [leaf] }
                ]
            }
        ];

        var path = ReportGenerator.GenerateHtmlReport([], features, DateTime.UtcNow, DateTime.UtcNow, null,
            Path.Combine(TempDir, fileName), "Statuses", true, diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }
}
