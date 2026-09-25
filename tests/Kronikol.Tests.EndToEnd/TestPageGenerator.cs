using System.Diagnostics;
using System.Text;
using Kronikol.ComponentDiagram;
using Kronikol.InternalFlow;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Generates minimal self-contained HTML test pages using the real
/// DiagramContextMenu scripts/styles and InternalFlowHtmlGenerator output.
/// </summary>
public static class TestPageGenerator
{
    /// <summary>
    /// Creates a test page with iflow popup system, fake segment data, and a clickable trigger button.
    /// </summary>
    public static string GenerateIflowPopupTestPage(
        bool includeToggle = false,
        bool includeCallTree = false,
        bool includeEmptySegment = false,
        bool includeContextMenu = false)
    {
        using var activitySource = new ActivitySource("Kronikol.Tests.EndToEnd");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        Activity.Current = null;
        var parentSpan = activitySource.StartActivity("HTTP GET /api/orders", ActivityKind.Server)!;
        parentSpan.SetStartTime(DateTime.UtcNow);
        parentSpan.SetEndTime(DateTime.UtcNow.AddMilliseconds(150));

        var childSpan = activitySource.StartActivity("SELECT * FROM Orders", ActivityKind.Client,
            new ActivityContext(parentSpan.TraceId, parentSpan.SpanId, ActivityTraceFlags.Recorded))!;
        childSpan.SetStartTime(DateTime.UtcNow.AddMilliseconds(20));
        childSpan.SetEndTime(DateTime.UtcNow.AddMilliseconds(80));

        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-seg-1"] = new(
                Guid.NewGuid(), RequestResponseType.Request, "test-1",
                parentSpan.StartTimeUtc, parentSpan.StartTimeUtc + parentSpan.Duration,
                [parentSpan, childSpan])
        };

        if (includeEmptySegment)
        {
            segments["iflow-seg-empty"] = new(
                Guid.NewGuid(), RequestResponseType.Request, "test-2",
                null, null, []);
        }

        var diagramStyle = includeCallTree
            ? InternalFlowDiagramStyle.CallTree
            : InternalFlowDiagramStyle.ActivityDiagram;

        var segmentScript = DiagramContextMenu.GetInternalFlowConfigScript(InternalFlowHasDataBehavior.ShowLinkOnHover)
            + InternalFlowHtmlGenerator.GenerateSegmentDataScript(
            segments, diagramStyle,
            showFlameChart: includeToggle,
            flameChartPosition: InternalFlowFlameChartPosition.BehindWithToggle,
            noDataBehavior: includeEmptySegment ? InternalFlowNoDataBehavior.ShowMessage : InternalFlowNoDataBehavior.HideLink);

        var styles = DiagramContextMenu.GetInternalFlowPopupStyles();
        var popupScript = DiagramContextMenu.GetInternalFlowPopupScript();
        var plantUmlBrowserScript = diagramStyle == InternalFlowDiagramStyle.ActivityDiagram
            ? DiagramContextMenu.GetPlantUmlBrowserRenderScript()
            : "";

        var contextMenuScript = includeContextMenu ? DiagramContextMenu.GetContextMenuScript() : "";
        var contextMenuStyles = includeContextMenu ? DiagramContextMenu.GetStyles() : "";
        var flameChartRenderScript = includeToggle ? DiagramContextMenu.GetFlameChartRenderScript() : "";

        parentSpan.Dispose();
        childSpan.Dispose();

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <title>Playwright Test Page</title>
                <style>
                    {{styles}}
                    {{contextMenuStyles}}
                </style>
                {{plantUmlBrowserScript}}
                {{flameChartRenderScript}}
                {{contextMenuScript}}
                <style>
                    body { font-family: sans-serif; padding: 20px; }
                    .test-trigger { padding: 10px 20px; cursor: pointer; margin: 5px; }
                </style>
            </head>
            <body>
                <h1 id="page-title">iflow Popup Test Page</h1>

                <button id="trigger-seg-1" class="test-trigger"
                    onclick="window._iflowShowPopup('iflow-seg-1')">
                    Open Segment 1
                </button>

                <button id="trigger-seg-empty" class="test-trigger"
                    onclick="window._iflowShowPopup('iflow-seg-empty')">
                    Open Empty Segment
                </button>

                <button id="trigger-seg-missing" class="test-trigger"
                    onclick="window._iflowShowPopup('iflow-nonexistent')">
                    Open Missing Segment
                </button>

                {{segmentScript}}
                {{popupScript}}
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Creates a test page showing the whole-test flow details block with toggle.
    /// </summary>
    public static string GenerateWholeTestFlowPage(WholeTestFlowVisualization visualization = WholeTestFlowVisualization.Both)
    {
        using var activitySource = new ActivitySource("Kronikol.Tests.EndToEnd.WholeTest");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        Activity.Current = null;
        var span1 = activitySource.StartActivity("HTTP GET /api/orders", ActivityKind.Server)!;
        span1.SetStartTime(DateTime.UtcNow);
        span1.SetEndTime(DateTime.UtcNow.AddMilliseconds(150));

        var span2 = activitySource.StartActivity("SELECT * FROM Orders", ActivityKind.Client,
            new ActivityContext(span1.TraceId, span1.SpanId, ActivityTraceFlags.Recorded))!;
        span2.SetStartTime(DateTime.UtcNow.AddMilliseconds(20));
        span2.SetEndTime(DateTime.UtcNow.AddMilliseconds(80));

        var testId = "test-whole-1";
        var segments = new Dictionary<string, InternalFlowSegment>
        {
            [$"iflow-test-{testId}"] = new(
                Guid.Empty, RequestResponseType.Request, testId,
                span1.StartTimeUtc, span1.StartTimeUtc + span1.Duration,
                [span1, span2])
        };

        var boundaryLogs = new[]
        {
            ("GET: /api/orders", new DateTimeOffset(span1.StartTimeUtc.AddMilliseconds(5), TimeSpan.Zero))
        };

        var flowHtml = InternalFlowHtmlGenerator.GenerateWholeTestFlowHtml(
            segments, testId, boundaryLogs, visualization);

        var styles = DiagramContextMenu.GetInternalFlowPopupStyles();
        var toggleScript = DiagramContextMenu.GetToggleScript();
        var flameChartRenderScript = visualization is WholeTestFlowVisualization.FlameChart or WholeTestFlowVisualization.Both
            ? DiagramContextMenu.GetFlameChartRenderScript()
            : "";
        var plantUmlBrowserScript = visualization is WholeTestFlowVisualization.ActivityDiagram or WholeTestFlowVisualization.Both
            ? DiagramContextMenu.GetPlantUmlBrowserRenderScript()
            : "";

        span1.Dispose();
        span2.Dispose();

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <title>Whole Test Flow Test Page</title>
                <style>
                    {{styles}}
                </style>
                {{plantUmlBrowserScript}}
                {{flameChartRenderScript}}
                {{toggleScript}}
                <style>
                    body { font-family: sans-serif; padding: 20px; }
                </style>
            </head>
            <body>
                <h1 id="page-title">Whole Test Flow Test Page</h1>
                {{flowHtml}}
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Creates a test page simulating the component diagram with relationship flow list and popups.
    /// </summary>
    public static string GenerateComponentFlowPage()
    {
        using var activitySource = new ActivitySource("Kronikol.Tests.EndToEnd.CompFlow");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        Activity.Current = null;
        var span = activitySource.StartActivity("DB Query", ActivityKind.Client)!;
        span.SetStartTime(DateTime.UtcNow);
        span.SetEndTime(DateTime.UtcNow.AddMilliseconds(50));

        var segment = new InternalFlowSegment(
            Guid.Empty, RequestResponseType.Request, "aggregated",
            span.StartTimeUtc, span.StartTimeUtc + span.Duration,
            [span]);

        var flowData = new RelationshipFlowData(segment, [
            new RelationshipTestSummary("t1", "Order Creation Test", 1, 45),
            new RelationshipTestSummary("t2", "Payment Flow Test", 1, 23)
        ]);

        var popupContent = InternalFlowHtmlGenerator.GenerateRelationshipPopupContent(
            flowData, InternalFlowDiagramStyle.ActivityDiagram);

        var popupData = new Dictionary<string, object>
        {
            ["iflow-rel-API-DB"] = new { title = "API → DB", content = popupContent }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(popupData,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
        var dataScript = DiagramContextMenu.GetInternalFlowConfigScript(InternalFlowHasDataBehavior.ShowLinkOnHover)
            + $"<script>window.__iflowSegments = {json};</script>";

        var styles = DiagramContextMenu.GetInternalFlowPopupStyles();
        var plantUmlBrowserScript = DiagramContextMenu.GetPlantUmlBrowserRenderScript();
        var popupScript = DiagramContextMenu.GetInternalFlowPopupScript();
        var toggleScript = DiagramContextMenu.GetToggleScript();

        span.Dispose();

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <title>Component Flow Test Page</title>
                <style>
                    {{styles}}
                </style>
                {{plantUmlBrowserScript}}
                <style>
                    body { font-family: sans-serif; padding: 20px; }
                    .iflow-rel-list { list-style: none; padding: 0; margin: 16px 0; }
                    .iflow-rel-list li { padding: 6px 12px; border: 1px solid #e0e0e0; border-radius: 4px; margin: 4px 0; cursor: pointer; }
                    .iflow-rel-list li:hover { background: #f0f4ff; border-color: #4285f4; }
                    .iflow-rel-summary-table { width: 100%; border-collapse: collapse; margin-top: 12px; }
                    .iflow-rel-summary-table th { text-align: left; padding: 4px 8px; border-bottom: 2px solid #ddd; }
                    .iflow-rel-summary-table td { padding: 4px 8px; border-bottom: 1px solid #eee; }
                </style>
            </head>
            <body>
                <h1 id="page-title">Component Flow Test Page</h1>

                <h2>Relationship Flows</h2>
                <ul class="iflow-rel-list">
                    <li id="rel-api-db" onclick="window._iflowShowPopup('iflow-rel-API-DB')">
                        API → DB (1 span, 2 tests)
                    </li>
                </ul>

                {{dataScript}}
                {{popupScript}}
                {{toggleScript}}
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Creates a test page that mimics TestRunReport with a BrowserJs-rendered sequence diagram
    /// inside a plantuml-browser container, including InlineSvgStyles and context menu.
    /// </summary>
    public static string GenerateBrowserJsSequenceDiagramPage()
    {
        var plantUmlSource = """
            @startuml
            participant Alice
            participant Bob
            Alice -> Bob: Hello
            Bob --> Alice: Hi
            @enduml
            """;

        var encoded = System.Net.WebUtility.HtmlEncode(plantUmlSource);
        var plantUmlBrowserScript = DiagramContextMenu.GetPlantUmlBrowserRenderScript();
        var contextMenuScript = DiagramContextMenu.GetContextMenuScript();
        var contextMenuStyles = DiagramContextMenu.GetStyles();
        var inlineSvgStyles = DiagramContextMenu.GetInlineSvgStyles();

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <title>BrowserJs Sequence Diagram Test</title>
                <style>
                    {{contextMenuStyles}}
                    {{inlineSvgStyles}}
                    body { font-family: sans-serif; padding: 20px; background: #fff; }
                </style>
                {{plantUmlBrowserScript}}
                {{contextMenuScript}}
            </head>
            <body>
                <h1>BrowserJs Sequence Diagram</h1>
                <div class="plantuml-browser" id="puml-1" data-plantuml="{{encoded}}" data-diagram-type="plantuml"></div>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// A page of BrowserJs diagrams, one <c>.plantuml-browser</c> element per source in the given
    /// order, rendered through the shipped render script and worker host (the real engine in a Blob
    /// worker). No element is inside a <c>.scenario</c>, so nothing renders until the test calls
    /// <c>window._renderDiagramsInContainer(document.body)</c> or an element scrolls into view.
    /// </summary>
    public static string GenerateBrowserJsPage(params (string Id, string Source)[] diagrams)
    {
        var plantUmlBrowserScript = DiagramContextMenu.GetPlantUmlBrowserRenderScript();
        var contextMenuScript = DiagramContextMenu.GetContextMenuScript();
        var contextMenuStyles = DiagramContextMenu.GetStyles();
        var inlineSvgStyles = DiagramContextMenu.GetInlineSvgStyles();
        var elements = string.Join("\n", diagrams.Select(d =>
            $"""<div class="plantuml-browser" id="{d.Id}" data-plantuml="{System.Net.WebUtility.HtmlEncode(d.Source)}" data-diagram-type="plantuml"></div>"""));

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <title>BrowserJs Diagrams Test</title>
                <style>
                    {{contextMenuStyles}}
                    {{inlineSvgStyles}}
                    body { font-family: sans-serif; padding: 20px; background: #fff; }
                </style>
                {{plantUmlBrowserScript}}
                {{contextMenuScript}}
            </head>
            <body>
                <h1>BrowserJs Diagrams</h1>
                {{elements}}
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Creates a test page that mimics TestRunReport with an inline SVG sequence diagram
    /// inside a plantuml-inline-svg container, including InlineSvgStyles and context menu.
    /// </summary>
    public static string GenerateInlineSvgSequenceDiagramPage()
    {
        // Minimal SVG mimicking PlantUML server output (background via style attribute, no bg rect,
        // first rect has fill-opacity="0" like real PlantUML participant lifeline rects)
        var inlineSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"
                 style="width:300px;height:200px;background:#FFFFFF;" width="300px" height="200px"
                 viewBox="0 0 300 200">
                <defs/>
                <g>
                    <rect fill="#000000" fill-opacity="0.00000" height="160" width="8" x="80" y="30"/>
                    <rect fill="#E2E2F0" height="30" rx="4" width="80" x="40" y="0"/>
                    <text fill="#000000" font-size="13" x="60" y="20">Alice</text>
                    <rect fill="#E2E2F0" height="30" rx="4" width="80" x="180" y="0"/>
                    <text fill="#000000" font-size="13" x="200" y="20">Bob</text>
                    <line stroke="#181818" x1="84" x2="224" y1="60" y2="60"/>
                    <text fill="#000000" font-size="13" x="130" y="55">Hello</text>
                    <line stroke="#181818" stroke-dasharray="2,2" x1="224" x2="84" y1="90" y2="90"/>
                    <text fill="#000000" font-size="13" x="145" y="85">Hi</text>
                </g>
            </svg>
            """;

        var sourceEncoded = System.Net.WebUtility.HtmlEncode("@startuml\nparticipant Alice\nparticipant Bob\nAlice -> Bob: Hello\nBob --> Alice: Hi\n@enduml");
        var contextMenuScript = DiagramContextMenu.GetContextMenuScript();
        var contextMenuStyles = DiagramContextMenu.GetStyles();
        var inlineSvgStyles = DiagramContextMenu.GetInlineSvgStyles();

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <title>InlineSvg Sequence Diagram Test</title>
                <style>
                    {{contextMenuStyles}}
                    {{inlineSvgStyles}}
                    body { font-family: sans-serif; padding: 20px; background: #fff; }
                </style>
                {{contextMenuScript}}
            </head>
            <body>
                <h1>InlineSvg Sequence Diagram</h1>
                <div class="plantuml-inline-svg" data-plantuml="{{sourceEncoded}}" data-diagram-type="plantuml">{{inlineSvg}}</div>
            </body>
            </html>
            """;
    }
}
