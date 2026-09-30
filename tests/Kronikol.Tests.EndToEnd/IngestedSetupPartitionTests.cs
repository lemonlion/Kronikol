using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// <c>plans/INGEST_FIDELITY_PLAN.md</c> T6: an ingested run with <see cref="ReportConfigurationOptions.SeparateSetup"/> on
/// renders its Setup group in the browser, the group an in-process run draws. Until 4.1.0 ingest drew it only from a
/// hand-written <c>Phase</c> marker line.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class IngestedSetupPartitionTests(PlaywrightFixture fixture) : PlaywrightTestBase(fixture)
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_ingested_report_draws_the_setup_group_when_SeparateSetup_is_on()
    {
        await Page.GotoAsync(Ingest("separated", separateSetup: true));
        var setup = await SetupGroupAsync();
        Assert.True(setup.Label, "The rendered diagram has no Setup label.");
        Assert.True(setup.Fill, "The rendered diagram has no #F6F6F6 group behind the setup call.");
    }

    [Fact]
    public async Task An_ingested_report_draws_no_setup_group_when_SeparateSetup_is_off()
    {
        await Page.GotoAsync(Ingest("plain", separateSetup: false));
        var setup = await SetupGroupAsync();
        Assert.False(setup.Label);
        Assert.False(setup.Fill);
    }

    private async Task<(bool Label, bool Fill)> SetupGroupAsync()
    {
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = await WaitForDiagramSvg();
        var label = await svg.First.EvaluateAsync<bool>(
            "el => Array.from(el.querySelectorAll('text')).some(t => t.textContent.trim() === 'Setup')");
        var fill = await svg.First.EvaluateAsync<bool>(
            "el => Array.from(el.querySelectorAll('[fill]')).some(e => e.getAttribute('fill').toLowerCase() === '#f6f6f6')");
        return (label, fill);
    }

    /// <summary>A Given step with a call in it, then a When step with a call in it, ingested; returns the page's address.</summary>
    private string Ingest(string name, bool separateSetup)
    {
        // Unique per fact: the store is process-wide, and this ingest leaves other facts' logs where they are.
        var testId = "ingested-setup-partition-" + Guid.NewGuid().ToString("N");
        var (cardRequest, cardResponse) = InteractionRecord.Pair(testId, null, "GET", "http://localhost:5000/cards/1", "cards", "web",
            responseContent: "{\"ok\":true}", statusCode: "200", requestTimestamp: T0.AddMilliseconds(1500), responseTimestamp: T0.AddMilliseconds(1600));
        var (chargeRequest, chargeResponse) = InteractionRecord.Pair(testId, null, "POST", "http://localhost:5000/charges", "psp", "web",
            responseContent: "{\"ok\":true}", statusCode: "200", requestTimestamp: T0.AddMilliseconds(3500), responseTimestamp: T0.AddMilliseconds(3600));
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(TempDir, name);
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;
        options.SeparateSetup = separateSetup;

        // The whole pipeline memoises its diagrams process-wide, so it runs as every such fixture does: one at a time.
        IngestResult result;
        lock (ReportTestHelper.WholePipeline)
        result = IngestPipeline.Run(new IngestRequest
        {
            ClearExistingLogs = false,
            Interactions = [cardRequest, cardResponse, chargeRequest, chargeResponse],
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = testId, TestName = "charges a card", Feature = "charge.test.ts", Timestamp = T0 },
                new TestRunRecord { Event = "step", TestId = testId, Text = "a saved card", Keyword = "Given", Status = "passed", DurationMs = 2000, Timestamp = T0.AddMilliseconds(1000) },
                new TestRunRecord { Event = "step", TestId = testId, Text = "the card is charged", Keyword = "When", Status = "passed", DurationMs = 2000, Timestamp = T0.AddMilliseconds(3000) },
                new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 6000, Timestamp = T0.AddMilliseconds(6000) },
            ],
            Options = options,
        });

        Assert.True(result.Generated);
        File.Copy(result.TestRunReportHtml, Path.Combine(OutputDir, $"IngestedSetupPartition-{name}.html"), true);
        return new Uri(result.TestRunReportHtml).AbsoluteUri;
    }
}
