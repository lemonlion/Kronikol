using System.Net;
using Microsoft.Playwright;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The <c>Background calls</c> section of the test-run report: closed by default with the count in its
/// summary, and a table of who made what after which scenario ended once opened.
/// </summary>
[Collection(PlaywrightCollections.Scenarios)]
public class BackgroundCallsSectionTests : PlaywrightTestBase
{
    public BackgroundCallsSectionTests(PlaywrightFixture fixture) : base(fixture) { }

    private static readonly DateTimeOffset Ended = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private const string PlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "Orders" as orders
        caller -> orders : GET /api/orders
        orders --> caller : 200 OK
        @enduml
        """;

    private static RequestResponseLog[] Pair(string testId, DateTimeOffset at, string path)
    {
        var pair = Guid.NewGuid();
        var trace = Guid.NewGuid();
        return
        [
            new RequestResponseLog("Pay by card", testId, HttpMethod.Get, null, new Uri("http://orders" + path), [], "orders", "Test",
                RequestResponseType.Request, trace, pair, false) { Timestamp = at, AttributionSource = AttributionSource.TestContext },
            new RequestResponseLog("Pay by card", testId, HttpMethod.Get, "[]", new Uri("http://orders" + path), [], "orders", "Test",
                RequestResponseType.Response, trace, pair, false, HttpStatusCode.OK) { Timestamp = at.AddMilliseconds(20), AttributionSource = AttributionSource.TestContext }
        ];
    }

    private string GenerateBackgroundReport(string fileName, params string[] latePaths)
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios = [new Scenario { Id = "bg-1", DisplayName = "Pay by card", Result = ExecutionResult.Passed, EndedAt = Ended }]
            }
        ];
        if (latePaths.Length == 0)
            latePaths = ["/api/orders/sweep"];
        var logs = BackgroundAttribution.Expire(features,
            [.. Pair("bg-1", Ended.AddSeconds(-2), "/api/orders"), .. latePaths.SelectMany((late, i) => Pair("bg-1", Ended.AddSeconds(30 + i), late))]);
        var background = BackgroundAttribution.Summarise(logs, features);
        var diagrams = new[] { new DiagramAsCode("bg-1", "", PlantUmlSource) };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Background Calls Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            background: background);

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    [Fact]
    public async Task The_section_is_closed_with_the_count_in_its_summary_and_opens_to_the_table()
    {
        var url = GenerateBackgroundReport("background-calls.html");

        await Page.GotoReportAsync(url);
        var section = Page.Locator("details.background-calls");
        await section.WaitForAsync();

        Assert.False(await section.EvaluateAsync<bool>("el => el.open"));
        Assert.Contains("Background calls (1 after a scenario ended)", await section.Locator("summary").InnerTextAsync());
        Assert.False(await section.Locator("table.background-calls-table").IsVisibleAsync());

        await section.Locator("summary").ClickAsync();

        await Page.WaitForFunctionAsync("() => document.querySelector('details.background-calls').open", null, new() { PollingInterval = 200 });
        var rows = section.Locator("table.background-calls-table tbody tr");
        Assert.Equal(1, await rows.CountAsync());
        var cells = await rows.First.Locator("td").AllInnerTextsAsync();
        Assert.Equal(["Pay by card", "orders", "GET", "/api/orders/sweep", "1", "2026-01-01T10:00:30.020Z"], cells);
    }

    [Fact]
    public async Task Calls_that_differ_only_by_an_id_are_one_row_with_the_path_history_reads()
    {
        // The table reads a path through the operation key history fingerprints by (#192 R1): an id is folded, so a sweep
        // over two orders is one row of two calls, not two rows of one.
        var url = GenerateBackgroundReport("background-calls-folded.html", "/api/orders/1001/sweep", "/api/orders/1002/sweep");

        await Page.GotoReportAsync(url);
        var section = Page.Locator("details.background-calls");
        await section.WaitForAsync();
        await section.Locator("summary").ClickAsync();

        await Page.WaitForFunctionAsync("() => document.querySelector('details.background-calls').open", null, new() { PollingInterval = 200 });
        var rows = section.Locator("table.background-calls-table tbody tr");
        Assert.Equal(1, await rows.CountAsync());
        var cells = await rows.First.Locator("td").AllInnerTextsAsync();
        Assert.Equal(["Pay by card", "orders", "GET", "/api/orders/{n}/sweep", "2", "2026-01-01T10:00:31.020Z"], cells);
    }
}
