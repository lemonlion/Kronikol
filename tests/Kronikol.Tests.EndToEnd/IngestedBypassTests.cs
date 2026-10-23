using Kronikol.Ingestion;
using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// <c>plans/CUCUMBER_BYPASS_PLAN.md</c> T20 (#105): an ingested scenario with a bypassed step is drawn as bypassed, and
/// the step says why, read from the page as painted. The files are the issue's own: a tests file and Cucumber Messages
/// for one scenario, whose middle step was skipped over while the step after it ran.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class IngestedBypassTests(PlaywrightFixture fixture) : PlaywrightTestBase(fixture)
{
    private const string Reason = "Bypassed on dev: mock Gemini is not in the deployed path";
    // 5.0.0 (plans/OPTION_C_PLAN.md S1): a bypassed step's circle is the status's soft tint with its ink
    // (--kron-bypass-soft, --kron-bypass), and a skipped scenario's header takes --kron-skip.
    private const string BypassSoft = "rgb(232, 240, 252)";
    private const string BypassInk = "rgb(40, 95, 186)";
    private const string SkippedGrey = "rgb(91, 97, 112)";

    [Theory]
    [InlineData("both", "issue-105-tests.ndjson", "issue-105-messages.ndjson")]
    [InlineData("messages", null, "issue-105-messages.ndjson")]
    [InlineData("tests", "issue-105-tests.ndjson", null)]
    public async Task The_issues_scenario_is_drawn_bypassed_with_its_reason(string name, string? tests, string? messages)
    {
        await Page.GotoAsync(Ingest(name, tests, messages));
        await ExpandFirstScenarioWithDiagram();

        var step = Page.Locator(".step", new() { HasTextString = "mock Gemini served the summary" }).First;
        var status = step.Locator(".step-status").First;
        await status.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        Assert.Equal("↷", (await status.InnerTextAsync()).Trim());
        Assert.Equal(BypassSoft, await status.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor"));
        Assert.Equal(BypassInk, await status.EvaluateAsync<string>("el => getComputedStyle(el).color"));

        // The reason is drawn where a comment is, and can be read on the page.
        var reason = Page.GetByText($"Bypassed: {Reason}", new() { Exact = true }).First;
        await reason.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var box = await reason.BoundingBoxAsync();
        Assert.True(box is { Width: > 0, Height: > 0 }, "The bypass reason has no area on the page.");

        // The scenario is not drawn as skipped: its header keeps the colour of one that ran.
        var header = Page.Locator("details.scenario > summary").First;
        Assert.NotEqual(SkippedGrey, await header.EvaluateAsync<string>("el => getComputedStyle(el).color"));
        Assert.Equal("Bypassed", await Page.Locator("details.scenario").First.GetAttributeAsync("data-status"));
    }

    [Fact]
    public async Task A_step_that_attached_kronikol_bypass_is_drawn_bypassed_with_its_reason()
    {
        // playwright-bdd 9.2.0 as published: the step attached kronikol-bypass and returned, and was reported PASSED.
        await Page.GotoAsync(Ingest("attached", null, "playwright-bdd-9.2-bypass-messages.ndjson"));
        await ExpandFirstScenarioWithDiagram();

        var step = Page.Locator(".step", new() { HasTextString = "the local-only check attaches a bypass and returns" }).First;
        var status = step.Locator(".step-status").First;
        await status.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        Assert.Equal("↷", (await status.InnerTextAsync()).Trim());
        Assert.Equal(BypassSoft, await status.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor"));
        Assert.Equal(BypassInk, await status.EvaluateAsync<string>("el => getComputedStyle(el).color"));

        var reason = Page.GetByText($"Bypassed: {Reason}", new() { Exact = true }).First;
        await reason.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var box = await reason.BoundingBoxAsync();
        Assert.True(box is { Width: > 0, Height: > 0 }, "The bypass reason has no area on the page.");

        // The attachment is consumed: no link to a kronikol-bypass file is drawn anywhere. A link within the page is no
        // file's: the scenario's name says kronikol-bypass, so its link button's address does, and from 5.5.0 its
        // Timeline row's name, a link to it, does too.
        Assert.Equal(0, await Page.Locator("a:not([href^='#'])", new() { HasTextString = "kronikol-bypass" }).CountAsync());
        Assert.Equal(0, await Page.Locator("a[href*='kronikol-bypass']:not([href^='#'])").CountAsync());
    }

    /// <summary>Ingests the named fixtures into a report of their own; returns the page's address.</summary>
    private string Ingest(string name, string? tests, string? messages)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(TempDir, name);
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;

        // The whole pipeline memoises its diagrams process-wide, so it runs as every such fixture does: one at a time.
        IngestResult result;
        lock (ReportTestHelper.WholePipeline)
        result = IngestPipeline.Run(new IngestRequest
        {
            ClearExistingLogs = false,
            TestsFile = tests is null ? null : Fixture(tests),
            CucumberMessagesFiles = messages is null ? [] : [Fixture(messages)],
            Options = options,
        });

        Assert.True(result.Generated);
        File.Copy(result.TestRunReportHtml, Path.Combine(OutputDir, $"IngestedBypass-{name}.html"), true);
        return new Uri(result.TestRunReportHtml).AbsoluteUri;
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);
}
