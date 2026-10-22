using Kronikol.Ingestion;
using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The stack's start-up an ingest keeps (<see cref="IngestRequest.SetupWindow"/>, plans/RUN_WINDOW_PLAN.md R2, fact 27)
/// as the page shows it: the first feature painted, with the "not a test" badge, and with SeparateSetup drawn inside the
/// Setup partition, so the page's Setup filter takes its calls out and leaves its participants (harness P5).
/// </summary>
// The ingest replays into the process-global tracking store, as the Diagrams collection's classes write to it; its names
// and ids are its own, since a record left there is in every later report's component diagram (a "Orders Api" left by
// this class was drawn for another class's "OrdersApi").
[Collection(PlaywrightCollections.Diagrams)]
public class SetupWindowPaintTests : PlaywrightTestBase
{
    public SetupWindowPaintTests(PlaywrightFixture fixture) : base(fixture) { }

    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    private static IEnumerable<InteractionRecord> Call(string testId, string service, string caller, string method, string uri, DateTimeOffset at)
    {
        var (request, response) = InteractionRecord.Pair(testId, testId, method, uri, service, caller, statusCode: "200",
            requestTimestamp: at, responseTimestamp: at.AddMilliseconds(40));
        return [request, response];
    }

    private async Task OpenAsync(string name)
    {
        var records = new List<InteractionRecord>();
        // The stack's start-up, 13 minutes before the run: a seeder, then the stack's check that its API is up.
        for (var i = 0; i < 3; i++)
            records.AddRange(Call("sw-session", "Start-up Db", "Start-up Seeder", "POST", $"db://start-up/insert?seed={i}", T0.AddMinutes(-13).AddSeconds(i)));
        records.AddRange(Call("sw-session", "Start-up Api", "Start-up Compose", "GET", "http://start-up-api/health", T0.AddMinutes(-13).AddSeconds(20)));
        // Ids of each fact's own: the store keeps every class's records, and a scenario's diagram draws its id's.
        var test = $"sw-b1-{name}";
        records.AddRange(Call(test, "Start-up Api", "Start-up Browser", "GET", "http://start-up-api/orders", T0.AddSeconds(2)));

        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(TempDir, name);
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;
        options.SeparateSetup = true;
        // The whole pipeline memoises its diagrams process-wide, so it runs as every such fixture does: one at a time, and
        // without clearing what the other classes logged.
        IngestResult result;
        lock (ReportTestHelper.WholePipeline)
        result = IngestPipeline.Run(new IngestRequest
        {
            ClearExistingLogs = false,
            Interactions = records,
            TestRecords =
            [
                new TestRunRecord { Event = "testrun", TestId = "__run__", Status = "started", Timestamp = T0 },
                new TestRunRecord { Event = "start", TestId = test, TestName = "Lists orders", Feature = "Orders", Timestamp = T0.AddSeconds(1) },
                new TestRunRecord { Event = "end", TestId = test, Status = "passed", Timestamp = T0.AddSeconds(5) },
                new TestRunRecord { Event = "testrun", TestId = "__run__", Status = "passed", Timestamp = T0.AddSeconds(6) },
            ],
            Options = options,
            SetupWindow = new SetupWindow(T0.AddMinutes(-14), T0.AddMinutes(-12), ScenarioId: $"stack-start-up-{name.ToLowerInvariant()}"),
        });
        File.Copy(result.TestRunReportHtml, Path.Combine(OutputDir, name + ".html"), true);

        await Page.GotoAsync(new Uri(result.TestRunReportHtml).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
    }

    private Task<string[]> FeatureNamesAsync() => Page.EvaluateAsync<string[]>(
        "() => Array.from(document.querySelectorAll('details.feature > summary')).map(s => s.firstChild.textContent.trim())");

    [Fact]
    public async Task The_start_up_is_the_first_feature_painted_with_its_badge()
    {
        await OpenAsync("SetupWindowFirst");

        Assert.Equal(["Stack start-up", "Orders"], await FeatureNamesAsync());
        var startUp = Page.Locator("details.feature").First.Locator("details.scenario").First;
        await Expect(startUp).ToHaveAttributeAsync("data-not-a-test", "1");
        await Expect(startUp.Locator(".label.not-a-test")).ToHaveTextAsync("not a test");
    }

    [Fact]
    public async Task The_Setup_filter_takes_the_start_ups_calls_out_and_leaves_its_participants()
    {
        await OpenAsync("SetupWindowFilter");
        await ExpandFirstScenarioWithDiagram();
        var svg = await WaitForDiagramSvg();
        Assert.Contains("seed=0", await svg.TextContentAsync());

        await Page.Locator("button[data-toggle='setup']").First.ClickAsync();
        await Page.WaitForFunctionAsync(
            "() => { const s = document.querySelector(\"[data-diagram-type='plantuml'] svg\"); return s && !s.textContent.includes('seed=0'); }",
            null, new() { Timeout = 20000, PollingInterval = 200 });

        var drawn = await Page.EvaluateAsync<string>("() => document.querySelector(\"[data-diagram-type='plantuml'] svg\").textContent");
        Assert.DoesNotContain("health", drawn);
        Assert.Contains("Start-up Db", drawn);
        Assert.Contains("Start-up Api", drawn);
    }
}
