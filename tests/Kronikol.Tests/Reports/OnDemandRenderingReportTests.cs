using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Kronikol.ComponentDiagram;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// What a report drawn when it was written (<c>PlantUmlRendering.NodeJs</c>, <c>Server</c>, <c>Local</c>) carries for the
/// views only a page can draw, and how its embedded component diagram is drawn (DIAGRAM_COLOURS_PLAN §12.6). Until 3.31.8
/// such a page carried no engine, so the Activity tab and the internal-flow popups stayed blank, and the component panel
/// was left for the page to draw under every renderer, so it stayed blank too, written for the C4 library under NodeJs.
/// The browser half is <c>OnDemandRenderingTests</c> in the end-to-end project.
/// </summary>
[Collection("DiagramsFetcher")]
public class OnDemandRenderingReportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-ondemand-" + Guid.NewGuid().ToString("N"));

    public OnDemandRenderingReportTests()
    {
        DefaultDiagramsFetcher.Reset();
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static readonly Lazy<bool> NodeAvailable = new(() =>
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("node", "--version")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    });

    private const string ComponentSource = "@startuml\nleft to right direction\nrectangle \"OrdersApi\" as OrdersApi\nrectangle \"StockApi\" as StockApi\nOrdersApi --> StockApi\n@enduml";

    private static Feature[] OneScenario(string testId) =>
        [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = testId, DisplayName = "Order", Result = ExecutionResult.Passed }] }];

    /// <summary>A run through the whole pipeline: the scenario calls OrdersApi, which calls StockApi.</summary>
    private string RunReport(Action<ReportConfigurationOptions> configure)
    {
        var testId = "od-" + Guid.NewGuid().ToString("N");
        RequestResponseLogger.LogPair("Order", testId, HttpMethod.Post, new Uri("http://orders-api/orders"), "OrdersApi", "Test", statusCode: HttpStatusCode.Created);
        RequestResponseLogger.LogPair("Order", testId, HttpMethod.Get, new Uri("http://stock-api/stock"), "StockApi", "OrdersApi");
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = _dir,
            GenerateComponentDiagram = true,
            GenerateSpecificationsReport = false,
            GenerateSpecificationsData = false,
        };
        configure(options);
        ReportGenerator.CreateStandardReportsWithDiagrams(OneScenario(testId), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
        return File.ReadAllText(Path.Combine(_dir, "TestRunReport.html"));
    }

    /// <summary>The component panel's markup, up to the report's content.</summary>
    private static string Panel(string html)
    {
        var start = html.IndexOf("<div id=\"component-diagram\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "the report has no component panel");
        var end = html.IndexOf("<div id=\"report-content\">", start, StringComparison.Ordinal);
        return html[start..end];
    }

    /// <summary>A local renderer that draws every source as an SVG naming it: a C4 diagram, a component diagram or a sequence.</summary>
    private static byte[] DrawNamingTheSource(string plantUml, PlantUmlImageFormat format)
    {
        var kind = plantUml.Contains("!include <C4/", StringComparison.Ordinal) ? "c4"
            : plantUml.Contains("rectangle", StringComparison.Ordinal) || plantUml.Contains("component", StringComparison.Ordinal) ? "plain-component"
            : "sequence";
        return format == PlantUmlImageFormat.Png
            ? [0x89, 0x50, 0x4E, 0x47]
            : Encoding.UTF8.GetBytes($"<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\" data-drawn=\"{kind}\"></svg>");
    }

    [Fact]
    public void Under_local_rendering_the_panel_holds_the_renderers_drawing_of_the_diagram_the_standalone_page_draws()
    {
        var html = RunReport(o =>
        {
            o.PlantUmlRendering = PlantUmlRendering.Local;
            o.LocalDiagramRenderer = DrawNamingTheSource;
            o.PlantUmlImageFormat = PlantUmlImageFormat.Base64Svg;
        });

        var panel = Panel(html);
        // Internal-flow tracking (the default) inlines the SVG, as it does every diagram under Local.
        Assert.Contains("<svg xmlns=\"http://www.w3.org/2000/svg\" data-drawn=\"c4\"></svg>", panel);
        Assert.DoesNotContain("<?xml", panel);
        Assert.DoesNotContain("class=\"plantuml-browser\"", panel);
    }

    [Fact]
    public void Without_internal_flow_tracking_a_local_panel_is_an_image_of_the_configured_format()
    {
        var html = RunReport(o =>
        {
            o.PlantUmlRendering = PlantUmlRendering.Local;
            o.LocalDiagramRenderer = DrawNamingTheSource;
            o.PlantUmlImageFormat = PlantUmlImageFormat.Base64Png;
            o.InternalFlowTracking = false;
        });

        Assert.Contains("<img src=\"data:image/png;base64,iVBORw==\" alt=\"Component diagram\"", Panel(html));
    }

    [Fact]
    public void Under_server_rendering_the_panel_is_the_servers_image_the_standalone_page_shows()
    {
        var html = RunReport(o =>
        {
            o.PlantUmlRendering = PlantUmlRendering.Server;
            o.PlantUmlServerBaseUrl = "https://plantuml.example/plantuml";
            o.PlantUmlImageFormat = PlantUmlImageFormat.Svg;
            o.InternalFlowTracking = false;
        });

        var src = Regex.Match(Panel(html), "<img src=\"([^\"]+)\" alt=\"Component diagram\"").Groups[1].Value;
        Assert.StartsWith("https://plantuml.example/plantuml/svg/", src);
        var standalone = File.ReadAllText(Path.Combine(_dir, "ComponentDiagram.html"));
        Assert.Contains($"<img src=\"{src}\"", standalone);
    }

    /// <summary>
    /// A PlantUML server on a free local port that answers every request with <see cref="Served"/>, as a real server
    /// answers <c>/svg/</c>, XML declaration first. It speaks HTTP over a bare socket: <see cref="HttpListener"/> refuses a
    /// path segment longer than 260 characters with a 400, and an encoded C4 diagram is longer than that.
    /// </summary>
    private sealed class LocalPlantUmlServer : IDisposable
    {
        public const string Served = "<svg xmlns=\"http://www.w3.org/2000/svg\" data-drawn-by=\"the-server\"></svg>";
        private readonly System.Net.Sockets.TcpListener _listener = new(IPAddress.Loopback, 0);
        public string BaseUrl { get; }

        public LocalPlantUmlServer()
        {
            _listener.Start();
            BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/plantuml";
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    System.Net.Sockets.TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync(); }
                    catch { return; }
                    _ = Task.Run(async () =>
                    {
                        using (client)
                        {
                            var stream = client.GetStream();
                            var request = new StringBuilder();
                            var buffer = new byte[8192];
                            while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                            {
                                var read = await stream.ReadAsync(buffer);
                                if (read == 0) return;
                                request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                            }
                            var body = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>" + Served);
                            var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: image/svg+xml\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                            await stream.WriteAsync(head);
                            await stream.WriteAsync(body);
                        }
                    });
                }
            });
        }

        public void Dispose() => _listener.Stop();
    }

    [Fact]
    public void Under_server_rendering_with_inline_svg_the_panel_is_the_servers_svg_fetched_when_the_report_is_written()
    {
        // Internal-flow tracking (the default) inlines every diagram the server draws, fetched when the report is written, so
        // the page needs no server to show them. Until 3.32.3 the component panel alone stayed an <img> of the server's
        // address, drawn only where the reader could reach that server, although DrawEmbedded's own summary and the wiki's
        // Inline SVG Rendering page said it was inlined with the rest.
        using var server = new LocalPlantUmlServer();
        var html = RunReport(o =>
        {
            o.PlantUmlRendering = PlantUmlRendering.Server;
            o.PlantUmlServerBaseUrl = server.BaseUrl;
        });

        var panel = Panel(html);
        Assert.False(panel.Contains("component-diagram-failure", StringComparison.Ordinal), panel);
        Assert.Contains($"<div class=\"plantuml-inline-svg\" id=\"puml-", panel);
        Assert.Contains(LocalPlantUmlServer.Served, panel);
        Assert.DoesNotContain("<?xml", panel);
        Assert.DoesNotContain("<img", panel);
    }

    [Fact]
    public void Under_server_rendering_a_panel_the_server_cannot_draw_says_why_and_the_run_records_it()
    {
        // Nothing listens on the address: the fetch fails when the report is written, as a sequence diagram's does.
        var collector = new ReportDiagnosticsCollector();
        ComponentDiagramReportGenerator.DrawnDiagram? drawn;
        using (ReportDiagnosticsScope.Begin(collector))
        {
            drawn = ComponentDiagramReportGenerator.DrawEmbedded(ComponentSource, new ReportConfigurationOptions
            {
                PlantUmlRendering = PlantUmlRendering.Server,
                PlantUmlServerBaseUrl = "http://127.0.0.1:9/plantuml",
                InlineSvgRendering = true,
            });
        }

        Assert.NotNull(drawn);
        Assert.Null(drawn.InlineSvg);
        Assert.Null(drawn.ImageSource);
        Assert.StartsWith("The component diagram could not be drawn: HttpRequestException", drawn.Failure);
        var entry = Assert.Single(collector.Entries, e => e.Kind == DiagnosticKind.RenderFailure);
        Assert.Contains("Drawing the embedded component diagram failed", entry.Message);
    }

    [Fact]
    public void Under_nodejs_the_panel_is_the_node_renderers_svg_of_the_plain_syntax()
    {
        Assert.SkipWhen(!NodeAvailable.Value, "Node.js not available on PATH");

        var drawn = ComponentDiagramReportGenerator.DrawEmbedded(ComponentSource,
            new ReportConfigurationOptions { PlantUmlRendering = PlantUmlRendering.NodeJs, InlineSvgRendering = true });

        Assert.NotNull(drawn);
        Assert.Null(drawn.Failure);
        Assert.StartsWith("<svg", drawn.InlineSvg);
        Assert.Contains("StockApi", drawn.InlineSvg);
    }

    [Theory]
    [InlineData(PlantUmlRendering.BrowserJs, false)]
    [InlineData(PlantUmlRendering.NodeJs, false)]
    [InlineData(PlantUmlRendering.Server, true)]
    [InlineData(PlantUmlRendering.Local, true)]
    public void Only_the_java_renderers_are_given_the_c4_syntax(PlantUmlRendering rendering, bool c4)
    {
        // Neither JavaScript engine can load the C4 library: the run report embedded it for NodeJs until 3.31.8.
        Assert.Equal(c4, ComponentDiagramReportGenerator.UsesC4(rendering));
    }

    [Fact]
    public void Under_browserjs_the_page_draws_the_panel_as_before()
    {
        Assert.Null(ComponentDiagramReportGenerator.DrawEmbedded(ComponentSource, new ReportConfigurationOptions()));
    }

    [Fact]
    public void A_panel_the_renderer_cannot_draw_says_why_and_the_run_records_it()
    {
        var collector = new ReportDiagnosticsCollector();
        ComponentDiagramReportGenerator.DrawnDiagram? drawn;
        using (ReportDiagnosticsScope.Begin(collector))
        {
            drawn = ComponentDiagramReportGenerator.DrawEmbedded(ComponentSource, new ReportConfigurationOptions
            {
                PlantUmlRendering = PlantUmlRendering.Local,
                InlineSvgRendering = true,
                LocalDiagramRenderer = (_, _) => throw new InvalidOperationException("the jar is missing"),
            });
        }

        Assert.NotNull(drawn);
        Assert.Equal("The component diagram could not be drawn: InvalidOperationException: the jar is missing", drawn.Failure);
        var entry = Assert.Single(collector.Entries, e => e.Kind == DiagnosticKind.RenderFailure);
        Assert.Contains("Drawing the embedded component diagram failed", entry.Message);
        Assert.Contains("the jar is missing", entry.Message);
    }

    [Theory]
    [InlineData(PlantUmlRendering.NodeJs, true, "true")]
    [InlineData(PlantUmlRendering.Local, true, "true")]
    [InlineData(PlantUmlRendering.Server, true, "true")]
    [InlineData(PlantUmlRendering.BrowserJs, true, "false")]
    [InlineData(PlantUmlRendering.BrowserJs, false, "false")]
    [InlineData(PlantUmlRendering.NodeJs, false, null)]
    public void A_page_carries_the_engine_at_once_on_demand_or_not_at_all(PlantUmlRendering rendering, bool internalFlowTracking, string? onDemand)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("t1", "<svg></svg>", "@startuml\nA -> B\n@enduml")], OneScenario("t1"),
            DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(_dir, $"engine-{rendering}-{internalFlowTracking}.html"), "Test", true,
            plantUmlRendering: rendering, inlineSvgRendering: true, internalFlowTracking: internalFlowTracking);

        var html = File.ReadAllText(path);
        var flag = Regex.Match(html, @"var ON_DEMAND = (true|false);");
        if (onDemand is null)
            Assert.False(flag.Success, "the page carries the engine");
        else
            Assert.Equal(onDemand, flag.Groups[1].Value);
    }
}
