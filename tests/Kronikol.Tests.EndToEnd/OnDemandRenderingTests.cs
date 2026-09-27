using System.Diagnostics;
using System.Net;
using Kronikol.InternalFlow;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.Playwright;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A report whose diagrams were drawn when it was written (<c>PlantUmlRendering.NodeJs</c>, <c>Server</c>, <c>Local</c>)
/// still has views only the page can draw, and until 3.31.8 it carried no engine for them (DIAGRAM_COLOURS_PLAN §12.6):
/// the Activity tab and the internal-flow popups stayed blank, and so did the embedded component diagram, which was
/// left for the page under every renderer. Under <c>NodeJs</c> an internal-flow link opened nothing, since the engine
/// draws a link as blue text and the popup script catches clicks on <c>&lt;a&gt;</c> links only. The page now fetches the
/// engine the first time one of those views is shown, never when it opens; a NodeJs diagram's links are bound as a
/// diagram drawn in the page is; and the component diagram is drawn by the report's renderer, as ComponentDiagram.html is.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class OnDemandRenderingTests : PlaywrightTestBase
{
    public OnDemandRenderingTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string ScenarioId = "on-demand-1";
    private const string LinkedSegment = "iflow-seg-1";

    // Two calls: the first links a segment with spans, the second a segment the report has no entry for.
    private const string SequenceSource = """
        @startuml
        participant "Caller" as Caller
        participant "Orders" as Orders
        Caller -> Orders: [[#iflow-seg-1 GET: /api/orders]]
        Orders --> Caller: 200 OK
        Caller -> Orders: [[#iflow-seg-none POST: /api/refunds]]
        Orders --> Caller: 204 No Content
        @enduml
        """;

    private static readonly Lazy<bool> NodeAvailable = new(() =>
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("node", "--version")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    });

    private readonly List<string> _engineRequests = [];

    /// <summary>
    /// A report page as <see cref="ReportGenerator.GenerateHtmlReport"/> writes it for <paramref name="rendering"/>, with
    /// internal-flow tracking: one scenario whose diagram (drawn by <paramref name="svg"/>) links a segment with two
    /// spans, and that scenario's whole-test flow (the Activity and Flame tabs).
    /// </summary>
    private async Task Open(PlantUmlRendering rendering, string svg, [System.Runtime.CompilerServices.CallerMemberName] string? testName = null)
    {
        using var activitySource = new ActivitySource("Kronikol.Tests.EndToEnd.OnDemand");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Kronikol.Tests.EndToEnd.OnDemand",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        Activity.Current = null;
        var start = DateTime.UtcNow;
        var root = activitySource.StartActivity("HTTP GET /api/orders", ActivityKind.Server)!;
        root.SetStartTime(start);
        root.SetEndTime(start.AddMilliseconds(150));
        var child = activitySource.StartActivity("SELECT * FROM Orders", ActivityKind.Client,
            new ActivityContext(root.TraceId, root.SpanId, ActivityTraceFlags.Recorded))!;
        child.SetStartTime(start.AddMilliseconds(20));
        child.SetEndTime(start.AddMilliseconds(80));

        var dataScript = DiagramContextMenu.GetInternalFlowConfigScript(InternalFlowHasDataBehavior.ShowLink)
            + InternalFlowHtmlGenerator.GenerateSegmentDataScript(
                new Dictionary<string, InternalFlowSegment>
                {
                    [LinkedSegment] = new(Guid.NewGuid(), RequestResponseType.Request, ScenarioId, start, start.AddMilliseconds(150), [root, child])
                },
                InternalFlowDiagramStyle.ActivityDiagram);
        var wholeTest = new Dictionary<string, InternalFlowSegment>
        {
            [$"iflow-test-{ScenarioId}"] = new(Guid.Empty, RequestResponseType.Request, ScenarioId, start, start.AddMilliseconds(150), [root, child])
        };
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios = [new Scenario { Id = ScenarioId, DisplayName = "Look an order up", Result = ExecutionResult.Passed }]
            }
        ];

        var fileName = $"OnDemand_{testName}.html";
        var path = ReportGenerator.GenerateHtmlReport(
            [new DiagramAsCode(ScenarioId, svg, SequenceSource)], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "On-demand Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: rendering,
            inlineSvgRendering: true,
            internalFlowTracking: true,
            internalFlowDataScript: dataScript,
            wholeTestSegments: wholeTest,
            wholeTestVisualization: WholeTestFlowVisualization.Both);
        child.Dispose();
        root.Dispose();
        File.Copy(path, Path.Combine(OutputDir, fileName), true);

        WatchEngineRequests();
        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
    }

    private void WatchEngineRequests() =>
        Page.Request += (_, request) =>
        {
            if (request.Url.EndsWith("/plantuml.js", StringComparison.Ordinal) || request.Url.EndsWith("/viz-global.js", StringComparison.Ordinal))
                lock (_engineRequests) _engineRequests.Add(request.Url);
        };

    private string[] EngineRequests() { lock (_engineRequests) return [.. _engineRequests]; }

    private static string NodeSvg()
    {
        var rendered = NodeJsPlantUmlRenderer.RenderMany([SequenceSource]);
        return rendered[0].Svg ?? throw new InvalidOperationException(rendered[0].Error);
    }

    /// <summary>What the Java engine draws for <see cref="SequenceSource"/>'s first call, as Server and Local inline it: the link is an &lt;a&gt;.</summary>
    private const string JavaLikeSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="220" height="90" viewBox="0 0 220 90"><g><line x1="30" y1="50" x2="190" y2="50" stroke="#181818"/><a href="#iflow-seg-1" target="_top" title="#iflow-seg-1" xlink:actuate="onRequest" xlink:href="#iflow-seg-1" xlink:show="new" xlink:title="#iflow-seg-1" xlink:type="simple"><text x="40" y="45" fill="#0000FF" font-size="13" text-decoration="underline">GET: /api/orders</text></a></g></svg>
        """;

    /// <summary>The diagram's link texts (the engine's link colour), scrolled into view, once the page has had the chance to bind them.</summary>
    private async Task<ILocator> ScrollToDiagram()
    {
        var diagram = Page.Locator(".plantuml-inline-svg").First;
        await diagram.ScrollIntoViewIfNeededAsync();
        try
        {
            await Page.WaitForFunctionAsync("() => document.querySelector('.plantuml-inline-svg').dataset.iflowBound === '1'", null,
                new() { Timeout = 10_000, PollingInterval = 200 });
        }
        catch (TimeoutException)
        {
            // A page that binds nothing: the assertions below say what the reader sees.
        }
        return diagram;
    }

    private static string LinkText(string label) =>
        $"Array.from(document.querySelectorAll('.plantuml-inline-svg text')).find(t => t.textContent.indexOf('{label}') >= 0)";

    [Fact]
    public async Task A_link_the_node_renderer_drew_opens_its_popup()
    {
        Assert.SkipWhen(!NodeAvailable.Value, "Node.js not available on PATH");
        await Open(PlantUmlRendering.NodeJs, NodeSvg());
        await ScrollToDiagram();

        await Page.EvaluateAsync($"() => {LinkText("/api/orders")}.dispatchEvent(new MouseEvent('click', {{ bubbles: true, cancelable: true }}))");

        var popup = Page.Locator(".iflow-overlay .iflow-popup");
        await popup.WaitForAsync(new() { Timeout = 5_000 });
        Assert.Equal(0, await popup.Locator(".iflow-no-data").CountAsync());
    }

    [Fact]
    public async Task A_node_drawn_link_that_opens_nothing_rests_in_the_text_ink()
    {
        Assert.SkipWhen(!NodeAvailable.Value, "Node.js not available on PATH");
        await Open(PlantUmlRendering.NodeJs, NodeSvg());
        await ScrollToDiagram();

        var looks = await Page.EvaluateAsync<string[]>($$"""
            () => [{{LinkText("/api/orders")}}, {{LinkText("/api/refunds")}}]
                .map(t => (t.getAttribute('fill') || '').toLowerCase() + ' ' + (t.getAttribute('text-decoration') || 'none'))
            """);

        Assert.Equal("#0000ff underline", looks[0]);
        Assert.Equal("#000000 none", looks[1]);
    }

    [Fact]
    public async Task The_page_fetches_no_engine_until_the_activity_tab_is_shown()
    {
        Assert.SkipWhen(!NodeAvailable.Value, "Node.js not available on PATH");
        await Open(PlantUmlRendering.NodeJs, NodeSvg());
        await ScrollToDiagram();

        Assert.Equal("idle", await Page.EvaluateAsync<string>("() => window.__kronikolRender ? window.__kronikolRender.mode : 'no engine on the page'"));
        Assert.Empty(EngineRequests());

        await Page.Locator("button.diagram-toggle-btn[data-dtype='activity']").First.ClickAsync();

        var activity = Page.Locator(".diagram-view-activity .plantuml-browser svg").First;
        await activity.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        Assert.True(await activity.EvaluateAsync<double>("el => el.getBoundingClientRect().width") > 0);
        Assert.NotEmpty(EngineRequests());
    }

    [Fact]
    public async Task A_popup_draws_its_activity_diagram_under_nodejs()
    {
        Assert.SkipWhen(!NodeAvailable.Value, "Node.js not available on PATH");
        await Open(PlantUmlRendering.NodeJs, NodeSvg());

        await Page.EvaluateAsync($"() => window._iflowShowPopup('{LinkedSegment}')");

        var diagram = Page.Locator(".iflow-popup .plantuml-browser svg").First;
        await diagram.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        Assert.True(await diagram.EvaluateAsync<double>("el => el.getBoundingClientRect().width") > 0);
    }

    [Fact]
    public async Task Under_local_rendering_a_link_opens_a_popup_that_draws_and_the_activity_tab_draws()
    {
        await Open(PlantUmlRendering.Local, JavaLikeSvg);
        Assert.Empty(EngineRequests());

        // The Java engine's <a> is the popup script's to catch, as it always was.
        await Page.EvaluateAsync($"() => {LinkText("/api/orders")}.dispatchEvent(new MouseEvent('click', {{ bubbles: true, cancelable: true }}))");
        var popupDiagram = Page.Locator(".iflow-popup .plantuml-browser svg").First;
        await popupDiagram.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        await Page.Keyboard.PressAsync("Escape");

        await Page.Locator("button.diagram-toggle-btn[data-dtype='activity']").First.ClickAsync();
        await Page.Locator(".diagram-view-activity .plantuml-browser svg").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
    }

    [Fact]
    public async Task A_nodejs_run_report_shows_its_component_diagram_without_the_engine()
    {
        Assert.SkipWhen(!NodeAvailable.Value, "Node.js not available on PATH");
        var reportsDir = Path.Combine(TempDir, "nodejs-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(reportsDir);
        var order = "od-order-" + Guid.NewGuid().ToString("N");
        // The whole pipeline reads process-wide state, so it runs as every such fixture does: one at a time, from
        // nothing (ReportTestHelper.StartFromNothing).
        lock (ReportTestHelper.WholePipeline)
        {
        ReportTestHelper.StartFromNothing();
        RequestResponseLogger.LogPair("Order", order, HttpMethod.Post, new Uri("http://orders-api/orders"), "OrdersApi", "Test", statusCode: HttpStatusCode.Created);
        RequestResponseLogger.LogPair("Order", order, HttpMethod.Get, new Uri("http://stock-api/stock"), "StockApi", "OrdersApi");

        ReportGenerator.CreateStandardReportsWithDiagrams(
            [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = order, DisplayName = "Order", Result = ExecutionResult.Passed }] }],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
            new ReportConfigurationOptions
            {
                ReportsFolderPath = reportsDir,
                PlantUmlRendering = PlantUmlRendering.NodeJs,
                GenerateComponentDiagram = true,
                GenerateSpecificationsReport = false,
                GenerateSpecificationsData = false,
            });
        DefaultDiagramsFetcher.Reset();
        }
        var html = Path.Combine(reportsDir, "TestRunReport.html");
        File.Copy(html, Path.Combine(OutputDir, "OnDemand_component_panel.html"), true);

        WatchEngineRequests();
        await Page.GotoAsync(new Uri(html).AbsoluteUri);
        await Page.Locator("button[onclick*='toggle_component_diagram']").First.ClickAsync();

        var svg = Page.Locator("#component-diagram svg").First;
        await svg.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.True(await svg.EvaluateAsync<double>("el => el.getBoundingClientRect().width") > 0);
        // The plain syntax, drawn: under C4 the Node renderer drew its error picture, which quotes the include.
        var text = await svg.TextContentAsync();
        Assert.Contains("OrdersApi", text);
        Assert.DoesNotContain("include", text);
        Assert.Empty(EngineRequests());
    }
}
