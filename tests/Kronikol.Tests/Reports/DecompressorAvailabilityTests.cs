using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Any emitted script that calls <c>decompressGzipBase64</c> must ship with a definition of it in
/// the same document. Historically the definition lived only in the BrowserJs render script, while
/// the context-menu script (emitted for InlineSvg too) and the internal-flow popup script (emitted
/// for every rendering mode when internal flow tracking is on) both call it — a latent
/// <c>ReferenceError</c> class. The shared decompressor helper closes it for good.
/// </summary>
public class DecompressorAvailabilityTests
{
    private static Feature[] SingleScenarioFeature() =>
    [
        new Feature
        {
            DisplayName = "F1",
            Scenarios =
            [
                new Scenario
                {
                    Id = "s1", DisplayName = "Create order", Result = ExecutionResult.Passed,
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "a valid request", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];

    private static readonly DefaultDiagramsFetcher.DiagramAsCode[] Diagrams =
        [new DefaultDiagramsFetcher.DiagramAsCode("s1", "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>", "@startuml\nA -> B : hi\n@enduml")];

    private static bool References(string html) => Regex.IsMatch(html, @"decompressGzipBase64\s*\(");

    private static bool Defines(string html) =>
        html.Contains("function decompressGzipBase64") || html.Contains("window.decompressGzipBase64 =");

    [Fact]
    public void InlineSvg_report_defines_the_decompressor_its_context_menu_calls()
    {
        var path = ReportGenerator.GenerateHtmlReport(
            Diagrams, SingleScenarioFeature(),
            DateTime.UtcNow, DateTime.UtcNow,
            null, "DecompressorInlineSvg.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.Local,
            inlineSvgRendering: true);
        var content = File.ReadAllText(path);

        Assert.True(References(content), "expected the context-menu script (which calls decompressGzipBase64) to be emitted for InlineSvg");
        Assert.True(Defines(content), "InlineSvg report calls decompressGzipBase64 but never defines it (ReferenceError)");
    }

    [Fact]
    public void Img_mode_report_with_internal_flow_defines_the_decompressor_its_popup_calls()
    {
        var path = ReportGenerator.GenerateHtmlReport(
            Diagrams, SingleScenarioFeature(),
            DateTime.UtcNow, DateTime.UtcNow,
            null, "DecompressorIflowImg.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.Server,
            internalFlowTracking: true);
        var content = File.ReadAllText(path);

        Assert.True(References(content), "expected the internal-flow popup script (which calls decompressGzipBase64) to be emitted");
        Assert.True(Defines(content), "internal-flow report calls decompressGzipBase64 but never defines it (ReferenceError)");
    }

    [Fact]
    public void Standalone_component_diagram_page_defines_the_decompressor_its_scripts_call()
    {
        var logs = new[]
        {
            new Kronikol.Tracking.RequestResponseLog(
                "Scenario", "t1", HttpMethod.Get, "{}",
                new Uri("https://api.example.test/orders"),
                [], "OrderService", "Test", Kronikol.Tracking.RequestResponseType.Request,
                Guid.NewGuid(), Guid.NewGuid(), TrackingIgnore: false)
        };
        var result = Kronikol.ComponentDiagram.ComponentDiagramReportGenerator.GenerateComponentDiagramReport(
            logs, new ReportConfigurationOptions
            {
                ComponentDiagramOptions = new Kronikol.ComponentDiagram.ComponentDiagramOptions { FileName = "DecompressorCompDiagram" },
                PlantUmlRendering = PlantUmlRendering.BrowserJs
            });
        var content = File.ReadAllText(result.HtmlFilePath);

        Assert.True(References(content), "expected the browser render script (which calls decompressGzipBase64) to be emitted");
        Assert.True(Defines(content), "standalone component diagram page calls decompressGzipBase64 but never defines it");
    }

    [Theory]
    // These are PUBLIC composition points (TestPageGenerator-style standalone pages, the
    // component-diagram page, external consumers): each script that calls the decompressor
    // must be self-sufficient. The helper redefines an identical global, so double inclusion
    // in the full report is a no-op.
    [InlineData("flame")]
    [InlineData("render")]
    [InlineData("popup")]
    [InlineData("contextmenu")]
    public void Every_standalone_script_that_references_the_decompressor_also_defines_it(string script)
    {
        var content = script switch
        {
            "flame" => DiagramContextMenu.GetFlameChartRenderScript(),
            "render" => DiagramContextMenu.GetPlantUmlBrowserRenderScript(),
            "popup" => DiagramContextMenu.GetInternalFlowPopupScript(),
            _ => DiagramContextMenu.GetContextMenuScript()
        };
        Assert.True(References(content), $"expected {script} script to call decompressGzipBase64");
        Assert.True(Defines(content), $"{script} script calls decompressGzipBase64 but does not carry the shared helper");
    }

    [Fact]
    public void BrowserJs_report_still_defines_the_decompressor()
    {
        var path = ReportGenerator.GenerateHtmlReport(
            Diagrams, SingleScenarioFeature(),
            DateTime.UtcNow, DateTime.UtcNow,
            null, "DecompressorBrowserJs.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        var content = File.ReadAllText(path);

        Assert.True(Defines(content));
    }
}
