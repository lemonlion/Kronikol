using Kronikol.Ingestion;
using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// <c>plans/ATTACHMENT_MERGE_PLAN.md</c> fact 17 (#175): a playwright-bdd run ingested from its tests file and its
/// Cucumber messages paints each artefact once, where the run made it, and every image it draws loads. The run is the
/// checked-in <c>playwright-bdd-9.2-attachments</c> fixture of <c>Kronikol.Tests</c>.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class IngestedAttachmentTests(PlaywrightFixture fixture) : PlaywrightTestBase(fixture)
{
    private const string FailingScenario = "A failing UI scenario keeps Playwright's artefacts";
    private const string BackgroundScenario = "A failing scenario under a background";

    [Fact]
    public async Task Each_artefact_is_painted_once_where_the_run_made_it()
    {
        var (address, result) = Ingest("attachments");
        await Page.GotoAsync(address);
        await ExpandFirstScenarioWithDiagram();

        var scenario = Page.Locator("details.scenario", new() { HasTextString = FailingScenario }).First;
        await scenario.Locator(".scenario-attachments").First.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        // The scenario's own, in one block: the fixture's two screenshots and Playwright's four artefacts, under the run's
        // names, each image drawn as an image and the rest as links.
        Assert.Equal(1, await scenario.Locator(".scenario-attachments").CountAsync());
        Assert.Equal(["img screenshot-start.png", "img screenshot", "img screenshot-end.png", "link video", "link error-context", "link trace"],
            await NamesIn(scenario.Locator(".scenario-attachments").First));

        // Each step's own, once: the AfterStep screenshot on every step, the step's own screenshot on the second.
        string[][] steps = [["img after-step.png"], ["img step-shot.png", "img after-step.png"], ["img after-step.png"]];
        string[] texts = ["the page is open", "a step attaches a named image", "the check fails"];
        for (var i = 0; i < texts.Length; i++)
            Assert.Equal(steps[i], await NamesIn(scenario.Locator("div.step", new() { HasTextString = texts[i] }).First));

        // A background step's screenshot is on the background step, and nowhere else in its scenario.
        var background = Page.Locator("details.scenario", new() { HasTextString = BackgroundScenario }).First;
        Assert.Equal(["img after-step.png"], await NamesIn(background.Locator("div.step.step-background").First));
        Assert.DoesNotContain("img after-step.png", await NamesIn(background.Locator(".scenario-attachments").First));

        // Every scenario paints exactly the entries the ingest made, so nothing is drawn twice or left out anywhere.
        foreach (var made in result.Features.SelectMany(f => f.Scenarios))
            Assert.Equal(Count(made), (await NamesIn(Page.Locator("details.scenario", new() { HasTextString = made.DisplayName }).First)).Length);

        // Every image the page draws has loaded: none of them links a file that is not there.
        await Page.WaitForFunctionAsync(
            "() => [...document.querySelectorAll('img.attachment-image')].every(img => img.complete)",
            null, new() { PollingInterval = 200 });
        var images = await Page.EvaluateAsync<int>("() => document.querySelectorAll('img.attachment-image').length");
        var broken = await Page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('img.attachment-image')].filter(img => img.naturalWidth === 0).map(img => img.getAttribute('src'))");
        Assert.True(images > 0, "The page draws no image at all.");
        Assert.Empty(broken);
    }

    /// <summary>The attachments drawn inside <paramref name="holder"/>, in order: <c>img</c> and its alt text, or <c>link</c> and its text.</summary>
    private static Task<string[]> NamesIn(ILocator holder) =>
        holder.EvaluateAsync<string[]>(
            "el => [...el.querySelectorAll('img.attachment-image, a.step-attachment')].map(e => e.tagName === 'IMG' ? 'img ' + e.getAttribute('alt') : 'link ' + e.textContent.trim())");

    /// <summary>How many attachments a scenario of the ingested model holds: its own and every step's.</summary>
    private static int Count(Scenario scenario)
    {
        return (scenario.Attachments ?? []).Length + Steps(scenario.BackgroundSteps) + Steps(scenario.Steps);

        static int Steps(ScenarioStep[]? steps) => (steps ?? []).Sum(step => (step.Attachments ?? []).Length + Steps(step.SubSteps));
    }

    /// <summary>Ingests the fixture into a report of its own; returns the page's address and what the ingest made.</summary>
    private (string Address, IngestResult Result) Ingest(string name)
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "TestData", "playwright-bdd-9.2-attachments");
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
            TestsFile = Path.Combine(fixtureDirectory, "tests.ndjson"),
            CucumberMessagesFiles = [Path.Combine(fixtureDirectory, "messages.ndjson")],
            AttachmentsBase = fixtureDirectory,
            Options = options,
        });

        Assert.True(result.Generated);
        File.Copy(result.TestRunReportHtml, Path.Combine(OutputDir, $"IngestedAttachments-{name}.html"), true);
        return (new Uri(result.TestRunReportHtml).AbsoluteUri, result);
    }
}
