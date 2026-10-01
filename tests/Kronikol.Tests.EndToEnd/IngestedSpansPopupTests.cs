using Kronikol.Extensions.Otlp;
using Kronikol.Ingestion;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// <c>plans/INGEST_FIDELITY_PLAN.md</c> T21: an ingest handed the spans of a service in another language
/// (<c>kronikol ingest --spans</c>, <see cref="IngestRequest.Spans"/>) shows them as an in-process run shows its
/// activities. The spans are the OpenTelemetry-under-Jest spike's (S0): a GraphQL request to a Fastify and Mercurius
/// service, whose resolver called a payment provider and a risk service. Before 4.4.0 an ingested report had no internal
/// flow at all.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class IngestedSpansPopupTests(PlaywrightFixture fixture) : PlaywrightTestBase(fixture)
{
    private const string Trace = "eb7b756166d44b0d1306973d5a79985f";
    private const string ResolverSpan = "8b7f9746a6a508c9";
    private static readonly DateTimeOffset Second = DateTimeOffset.FromUnixTimeMilliseconds(1790755298000);
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "TestData", "otel-jest-v2.spans.jsonl");

    protected override int ViewportWidth => 1280;
    protected override int ViewportHeight => 900;

    [Fact]
    public async Task The_graphql_arrow_opens_a_popup_whose_diagram_and_flame_chart_name_the_resolver()
    {
        await OpenAndDraw(Ingest("popup"));

        // SVG rule: a dispatched click, on the arrow's own text.
        await Page.Locator(".plantuml-browser:not(.iflow-diagram) svg text", new() { HasTextString = "/graphql" }).First
            .EvaluateAsync("t => t.dispatchEvent(new MouseEvent('click', { bubbles: true }))");

        var popup = Page.Locator(".iflow-popup");
        await Expect(popup.Locator("h3")).ToHaveTextAsync("Internal Flow (15 spans)");
        var diagram = popup.Locator(".iflow-diagram svg").First;
        await diagram.WaitForAsync(new() { Timeout = PopupFirstDrawTimeout });
        var drawn = await diagram.TextContentAsync();
        Assert.Contains("graphql.resolve charge", drawn);
        Assert.Contains("@opentelemetry/instrumentation-graphql", drawn);

        await popup.Locator(".iflow-toggle-btn[data-view='flame']").ClickAsync();
        var resolverBar = popup.Locator(".iflow-flame-bar[title^='[@opentelemetry/instrumentation-graphql] graphql.resolve charge (63ms']");
        await Expect(resolverBar).ToBeVisibleAsync();
        await Expect(resolverBar.Locator(".iflow-flame-label")).ToHaveTextAsync("graphql.resolve charge (63ms)");
    }

    [Fact]
    public async Task The_scenarios_whole_flow_renders_from_the_ingested_spans()
    {
        await OpenAndDraw(Ingest("whole-flow"));

        var scenario = Page.Locator("details.scenario", new() { Has = Page.Locator("summary", new() { HasTextString = "Charges a card" }) }).First;
        await scenario.Locator(".diagram-toggle-btn[data-dtype='activity']").ClickAsync();
        var activity = scenario.Locator(".diagram-view-activity .iflow-diagram svg").First;
        await activity.WaitForAsync(new() { Timeout = PopupFirstDrawTimeout });
        Assert.Contains("graphql.resolve charge", await activity.TextContentAsync());

        await scenario.Locator(".diagram-toggle-btn[data-dtype='flame']").ClickAsync();
        var bars = scenario.Locator(".diagram-view-flame .iflow-flame-bar");
        await Expect(bars.First).ToBeVisibleAsync();
        Assert.Equal(15, await bars.CountAsync());
        // The service's start-up span is a trace of its own, which no call carries.
        Assert.Equal(0, await scenario.Locator(".iflow-flame-bar[title*='graphql.parseSchema']").CountAsync());
    }

    private async Task OpenAndDraw(string uri)
    {
        await Page.GotoAsync(uri);
        await Page.EvaluateAsync("() => document.querySelectorAll('details').forEach(d => d.open = true)");
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync(BrowserRenderWorkerTests.AllRenderedJs, null, new() { Timeout = 60000, PollingInterval = 200 });
    }

    /// <summary>The spike's three calls, stamped as its harness stamped them, ingested with its spans; returns the page's address.</summary>
    private string Ingest(string name)
    {
        // Unique per fact: the store is process-wide, and this ingest leaves other facts' logs where they are. So is the
        // trace: another fact's calls on the spike's own trace, at the same times, would share its spans with these.
        var testId = "ingested-spans-" + Guid.NewGuid().ToString("N");
        var trace = System.Diagnostics.ActivityTraceId.CreateRandom().ToString();
        InteractionRecord[] Call(string method, string uri, string service, string caller, int fromMs, int toMs, string span)
        {
            var (request, response) = InteractionRecord.Pair(testId, null, method, uri, service, caller,
                responseContent: "{\"ok\":true}", statusCode: "200",
                requestTimestamp: Second.AddMilliseconds(fromMs), responseTimestamp: Second.AddMilliseconds(toMs),
                activityTraceId: trace, activitySpanId: span);
            return [request, response];
        }

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
            Interactions =
            [
                .. Call("POST", "http://payments.local/graphql", "Payments GraphQL", "Jest", 365, 449, "c6103b1e3b3c954e"),
                .. Call("POST", "https://psp.example.test/charges/c2", "PSP", "Payments GraphQL", 382, 416, ResolverSpan),
                .. Call("GET", "http://127.0.0.1:45053/score/c2", "Risk", "Payments GraphQL", 418, 433, ResolverSpan),
            ],
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = testId, TestName = "charges a card", Feature = "charge.test.ts", Timestamp = Second.AddMilliseconds(300) },
                new TestRunRecord { Event = "end", TestId = testId, Status = "passed", Timestamp = Second.AddMilliseconds(500) },
            ],
            Options = options,
            Spans = OtlpTraceReader.ReadJsonLines(Fixture, malformed: null)
                .Select(s => s.ToFlowSpan())
                .Select(s => s.TraceId == Trace ? s with { TraceId = trace } : s)
                .ToList(),
        });

        Assert.True(result.Generated);
        File.Copy(result.TestRunReportHtml, Path.Combine(OutputDir, $"IngestedSpans-{name}.html"), true);
        return new Uri(result.TestRunReportHtml).AbsoluteUri;
    }
}
