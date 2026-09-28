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
    /// spans, and that scenario's whole-test flow (the Activity and Flame tabs). Links show as <paramref name="links"/>
    /// says; the facts written for 3.31.8 used <c>ShowLink</c>, and the default is <c>ShowLinkOnHover</c>.
    /// </summary>
    private async Task Open(PlantUmlRendering rendering, string svg,
        InternalFlowHasDataBehavior links = InternalFlowHasDataBehavior.ShowLink,
        Func<string, string>? rewrite = null,
        [System.Runtime.CompilerServices.CallerMemberName] string? testName = null)
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

        var dataScript = DiagramContextMenu.GetInternalFlowConfigScript(links)
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
        if (rewrite is not null)
            File.WriteAllText(path, rewrite(File.ReadAllText(path)));
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

    /// <summary>
    /// What the Java engine (IKVM, which <c>Local</c> runs; <c>Server</c> runs the same engine) draws for
    /// <see cref="SequenceSource"/>, as the report inlines it: each link an &lt;a&gt; round a blue, underlined text, the
    /// second naming a segment the report has no entry for. Drawn with plans/DIAGRAM_COLOURS_PLAN.harness/ikvm-render.cs.
    /// </summary>
    private const string JavaSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" contentStyleType="text/css" height="215px" preserveAspectRatio="none" style="width:199px;height:215px;background:#FFFFFF;" version="1.1" viewBox="0 0 199 215" width="199px" zoomAndPan="magnify"><defs/><g><line style="stroke:#181818;stroke-width:0.5;stroke-dasharray:5.0,5.0;" x1="30" x2="30" y1="37.6094" y2="179.0156"/><line style="stroke:#181818;stroke-width:0.5;stroke-dasharray:5.0,5.0;" x1="164.4404" x2="164.4404" y1="37.6094" y2="179.0156"/><rect fill="#E2E2F0" height="31.6094" rx="2.5" ry="2.5" style="stroke:#181818;stroke-width:0.5;" width="50.5654" x="5" y="5"/><text fill="#000000" font-family="sans-serif" font-size="14" lengthAdjust="spacing" textLength="36.5654" x="12" y="26.5332">Caller</text><rect fill="#E2E2F0" height="31.6094" rx="2.5" ry="2.5" style="stroke:#181818;stroke-width:0.5;" width="50.5654" x="5" y="178.0156"/><text fill="#000000" font-family="sans-serif" font-size="14" lengthAdjust="spacing" textLength="36.5654" x="12" y="199.5488">Caller</text><rect fill="#E2E2F0" height="31.6094" rx="2.5" ry="2.5" style="stroke:#181818;stroke-width:0.5;" width="56.7861" x="136.4404" y="5"/><text fill="#000000" font-family="sans-serif" font-size="14" lengthAdjust="spacing" textLength="42.7861" x="143.4404" y="26.5332">Orders</text><rect fill="#E2E2F0" height="31.6094" rx="2.5" ry="2.5" style="stroke:#181818;stroke-width:0.5;" width="56.7861" x="136.4404" y="178.0156"/><text fill="#000000" font-family="sans-serif" font-size="14" lengthAdjust="spacing" textLength="42.7861" x="143.4404" y="199.5488">Orders</text><polygon fill="#181818" points="152.8335,65.9609,162.8335,69.9609,152.8335,73.9609,156.8335,69.9609" style="stroke:#181818;stroke-width:1.0;"/><line style="stroke:#181818;stroke-width:1.0;" x1="30.2827" x2="158.8335" y1="69.9609" y2="69.9609"/><a href="#iflow-seg-1" target="_top" title="#iflow-seg-1" xlink:actuate="onRequest" xlink:href="#iflow-seg-1" xlink:show="new" xlink:title="#iflow-seg-1" xlink:type="simple"><text fill="#0000FF" font-family="sans-serif" font-size="13" lengthAdjust="spacing" text-decoration="underline" textLength="95.3672" x="37.2827" y="65.1045">GET: /api/orders</text></a><polygon fill="#181818" points="41.2827,96.3125,31.2827,100.3125,41.2827,104.3125,37.2827,100.3125" style="stroke:#181818;stroke-width:1.0;"/><line style="stroke:#181818;stroke-width:1.0;stroke-dasharray:2.0,2.0;" x1="35.2827" x2="163.8335" y1="100.3125" y2="100.3125"/><text fill="#000000" font-family="sans-serif" font-size="13" lengthAdjust="spacing" textLength="44.0845" x="47.2827" y="95.4561">200 OK</text><polygon fill="#181818" points="152.8335,126.6641,162.8335,130.6641,152.8335,134.6641,156.8335,130.6641" style="stroke:#181818;stroke-width:1.0;"/><line style="stroke:#181818;stroke-width:1.0;" x1="30.2827" x2="158.8335" y1="130.6641" y2="130.6641"/><a href="#iflow-seg-none" target="_top" title="#iflow-seg-none" xlink:actuate="onRequest" xlink:href="#iflow-seg-none" xlink:show="new" xlink:title="#iflow-seg-none" xlink:type="simple"><text fill="#0000FF" font-family="sans-serif" font-size="13" lengthAdjust="spacing" text-decoration="underline" textLength="110.5508" x="37.2827" y="125.8076">POST: /api/refunds</text></a><polygon fill="#181818" points="41.2827,157.0156,31.2827,161.0156,41.2827,165.0156,37.2827,161.0156" style="stroke:#181818;stroke-width:1.0;"/><line style="stroke:#181818;stroke-width:1.0;stroke-dasharray:2.0,2.0;" x1="35.2827" x2="163.8335" y1="161.0156" y2="161.0156"/><text fill="#000000" font-family="sans-serif" font-size="13" lengthAdjust="spacing" textLength="91.0635" x="47.2827" y="156.1592">204 No Content</text></g></svg>
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

    /// <summary>A link text's paint: its fill, lower-cased, and its text-decoration, or "none".</summary>
    private Task<string> Look(string label) =>
        Page.EvaluateAsync<string>($"() => {{ var t = {LinkText(label)}; return (t.getAttribute('fill') || '').toLowerCase() + ' ' + (t.getAttribute('text-decoration') || 'none'); }}");

    /// <summary>A mouse event on a link text, dispatched as the repo's rule for SVG text asks (a click bubbles, a hover does not).</summary>
    private Task Dispatch(string label, string type) =>
        Page.EvaluateAsync($"() => {LinkText(label)}.dispatchEvent(new MouseEvent('{type}', {{ bubbles: {(type == "click" ? "true" : "false")}, cancelable: true }}))");

    private ILocator Popup => Page.Locator(".iflow-overlay .iflow-popup");

    /// <summary>Clicks Export Filtered HTML, and opens the export in this page as a reader would open the file.</summary>
    private async Task OpenTheFilteredExport()
    {
        var download = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.Locator("button.export-btn", new() { HasTextString = "Export Filtered HTML" }).ClickAsync());
        var path = Path.Combine(TempDir, $"export_{Guid.NewGuid():N}.html");
        await download.SaveAsAsync(path);
        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        // The export carries no toolbar to expand with.
        await Page.EvaluateAsync("() => document.querySelectorAll('details').forEach(d => d.setAttribute('open', ''))");
        await Page.Locator(".plantuml-inline-svg").First.ScrollIntoViewIfNeededAsync();
    }

    /// <summary>
    /// Waits, in a page showing links on hover, until hovering <paramref name="label"/> shows it as a link: the page binds
    /// a diagram once it is in view. The attribute the page marks a bound diagram with is no signal in an export, which
    /// copies it; a page that never binds times out here and fails on the assertions after.
    /// </summary>
    private async Task WaitUntilHoverShowsTheLink(string label)
    {
        try
        {
            await Page.WaitForFunctionAsync($$"""
                () => {
                    var t = {{LinkText(label)}};
                    if (!t) return false;
                    t.dispatchEvent(new MouseEvent('mouseenter', { bubbles: false, cancelable: true }));
                    var shown = (t.getAttribute('fill') || '').toLowerCase() === '#0000ff';
                    t.dispatchEvent(new MouseEvent('mouseleave', { bubbles: false, cancelable: true }));
                    return shown;
                }
                """, null, new() { Timeout = 10_000, PollingInterval = 200 });
        }
        catch (TimeoutException)
        {
            // Nothing bound it: the assertions say what the reader sees.
        }
    }

    /// <summary>The look at rest, while hovered and after, then that a click opens the popup: the default mode's whole promise.</summary>
    private async Task AssertTheLinkShowsOnHoverAndOpensItsPopup(string label)
    {
        Assert.Equal("#000000 none", await Look(label));
        await Dispatch(label, "mouseenter");
        Assert.Equal("#0000ff underline", await Look(label));
        await Dispatch(label, "mouseleave");
        Assert.Equal("#000000 none", await Look(label));

        await Dispatch(label, "click");
        await Popup.WaitForAsync(new() { Timeout = 5_000 });
        Assert.Equal(0, await Popup.Locator(".iflow-no-data").CountAsync());
    }

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
        // The whole pipeline memoises its diagrams process-wide, so it runs as every such fixture does: one at a time,
        // from a fresh cache (ReportTestHelper.WholePipeline).
        lock (ReportTestHelper.WholePipeline)
        {
        DefaultDiagramsFetcher.Reset();
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

    // The default, ShowLinkOnHover, which none of the facts above used (DIAGRAM_COLOURS_PLAN §12.7).

    [Fact]
    public async Task In_the_default_mode_a_node_drawn_link_rests_in_the_text_ink_until_it_is_hovered()
    {
        Assert.SkipWhen(!NodeAvailable.Value, "Node.js not available on PATH");
        await Open(PlantUmlRendering.NodeJs, NodeSvg(), InternalFlowHasDataBehavior.ShowLinkOnHover);
        await ScrollToDiagram();

        await AssertTheLinkShowsOnHoverAndOpensItsPopup("/api/orders");
    }

    [Fact]
    public async Task In_the_default_mode_a_node_drawn_link_still_opens_its_popup_in_a_filtered_export()
    {
        // The page repaints a bound link in the text's ink, and the export copies that paint without the listeners: until
        // 3.32.3 the export found no link-coloured text, bound nothing, and its links neither showed nor opened.
        Assert.SkipWhen(!NodeAvailable.Value, "Node.js not available on PATH");
        await Open(PlantUmlRendering.NodeJs, NodeSvg(), InternalFlowHasDataBehavior.ShowLinkOnHover);
        await ScrollToDiagram();
        Assert.Equal("#000000 none", await Look("/api/orders"));

        await OpenTheFilteredExport();
        await WaitUntilHoverShowsTheLink("/api/orders");

        await AssertTheLinkShowsOnHoverAndOpensItsPopup("/api/orders");
        Assert.Equal("#000000 none", await Look("/api/refunds"));
    }

    [Fact]
    public async Task A_node_drawn_link_shown_always_still_opens_its_popup_in_a_filtered_export()
    {
        Assert.SkipWhen(!NodeAvailable.Value, "Node.js not available on PATH");
        await Open(PlantUmlRendering.NodeJs, NodeSvg());
        await ScrollToDiagram();

        await OpenTheFilteredExport();
        await Page.WaitForFunctionAsync($$"""
            () => {
                if (document.querySelector('.iflow-overlay .iflow-popup')) return true;
                var t = {{LinkText("/api/orders")}};
                if (t) t.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                return !!document.querySelector('.iflow-overlay .iflow-popup');
            }
            """, null, new() { Timeout = 10_000, PollingInterval = 200 });

        Assert.Equal("#0000ff underline", await Look("/api/orders"));
        Assert.Equal("#000000 none", await Look("/api/refunds"));
    }

    [Fact]
    public async Task Under_local_rendering_in_the_default_mode_a_link_rests_in_the_text_ink_until_it_is_hovered()
    {
        // The Java engine draws a link as an <a>. The page left such a diagram to the popup script, which opens a link but
        // never applied ShowLinkOnHover: until 3.32.3 every link of a Server or Local report rested blue and underlined.
        await Open(PlantUmlRendering.Local, JavaSvg, InternalFlowHasDataBehavior.ShowLinkOnHover);
        await ScrollToDiagram();

        await AssertTheLinkShowsOnHoverAndOpensItsPopup("/api/orders");
    }

    [Theory]
    [InlineData(InternalFlowHasDataBehavior.ShowLinkOnHover)]
    [InlineData(InternalFlowHasDataBehavior.ShowLink)]
    public async Task Under_local_rendering_a_link_with_no_flow_rests_in_the_text_ink_and_opens_nothing(InternalFlowHasDataBehavior links)
    {
        // The popup script takes such a link's href (P5 R2), so it opens nothing, but it kept the look of a link. A page
        // drawn in the browser gives a link with no segment the text's ink (DIAGRAM_COLOURS_PLAN S2); so does this one now.
        await Open(PlantUmlRendering.Local, JavaSvg, links);
        await ScrollToDiagram();

        Assert.Equal("#000000 none", await Look("/api/refunds"));
        await Dispatch("/api/refunds", "mouseenter");
        Assert.Equal("#000000 none", await Look("/api/refunds"));
        await Dispatch("/api/refunds", "click");
        await Page.WaitForTimeoutAsync(300);
        Assert.Equal(0, await Popup.CountAsync());
        // The link with a flow keeps the look the mode gives it.
        Assert.Equal(links == InternalFlowHasDataBehavior.ShowLink ? "#0000ff underline" : "#000000 none", await Look("/api/orders"));
    }

    [Fact]
    public async Task Under_local_rendering_in_the_default_mode_a_link_still_shows_on_hover_in_a_filtered_export()
    {
        await Open(PlantUmlRendering.Local, JavaSvg, InternalFlowHasDataBehavior.ShowLinkOnHover);
        await ScrollToDiagram();

        await OpenTheFilteredExport();
        await WaitUntilHoverShowsTheLink("/api/orders");

        await AssertTheLinkShowsOnHoverAndOpensItsPopup("/api/orders");
        Assert.Equal("#000000 none", await Look("/api/refunds"));
    }

    [Fact]
    public async Task An_on_demand_view_says_so_when_the_engine_cannot_be_fetched()
    {
        // Offline: the engine's address refuses the connection. The wiki promises the view says the engine could not be
        // loaded, where until 3.31.8 it was blank; no fact held it.
        Assert.SkipWhen(!NodeAvailable.Value, "Node.js not available on PATH");
        const string unreachable = "http://127.0.0.1:9/plantuml-offline";
        await Open(PlantUmlRendering.NodeJs, NodeSvg(), rewrite: html =>
        {
            Assert.Contains(Constants.TrackingDefaults.PlantUmlJsCdnBase, html);
            return html.Replace(Constants.TrackingDefaults.PlantUmlJsCdnBase, unreachable);
        });

        await Page.Locator("button.diagram-toggle-btn[data-dtype='activity']").First.ClickAsync();

        var view = Page.Locator(".diagram-view-activity .plantuml-browser").First;
        await Expect(view).ToContainTextAsync("PlantUML engine unavailable", new() { Timeout = 60_000 });
        Assert.Equal(0, await view.Locator("svg").CountAsync());
    }
}
