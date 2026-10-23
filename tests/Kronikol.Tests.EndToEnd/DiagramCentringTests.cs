using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A diagram narrower than its box stands in the middle of it (5.0.0, plans/OPTION_C_PLAN.md S10, the owner's round 17
/// item 19), whether the page draws it whole or in fragments: the box holds its drawing 1em in from either side, where
/// it held it 1em from the left and 0 from the right.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class DiagramCentringTests : DiagramNotePlaywrightBase
{
    public DiagramCentringTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 1280;
    protected override int ViewportHeight => 900;

    private const string Narrow = """
        @startuml
        !pragma teoz true
        participant "Caller" as caller
        participant "OrderService" as svc
        caller -> svc : POST /api/orders
        svc --> caller : 201 Created
        @enduml
        """;

    // How far the middle of each picture in a diagram box stands from the middle of the box, in CSS pixels
    private const string Offsets = """
        () => Array.from(document.querySelectorAll('.plantuml-browser:not(.iflow-diagram) svg')).map(s => {
            const box = s.closest('.plantuml-browser').getBoundingClientRect(), r = s.getBoundingClientRect();
            return (r.left + r.right) / 2 - (box.left + box.right) / 2;
        })
        """;

    [Fact]
    public async Task A_diagram_narrower_than_its_box_stands_in_the_middle_of_it()
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios = [new() { Id = "s1", DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode("s1", "", Narrow)],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, "CentredWhole.html"), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, "CentredWhole.html"), true);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        var offsets = await Page.EvaluateAsync<double[]>(Offsets);
        var widths = await Page.EvaluateAsync<double[]>(
            "() => { const s = document.querySelector('.plantuml-browser svg'); return [s.getBoundingClientRect().width, s.closest('.plantuml-browser').getBoundingClientRect().width]; }");

        Assert.True(widths[0] < widths[1] - 100, $"the drawing ({widths[0]} px) is narrower than its box ({widths[1]} px)");
        Assert.Single(offsets);
        Assert.True(Math.Abs(offsets[0]) <= 1, $"the drawing's middle is {offsets[0]} px from the box's");
    }

    // A diagram past the fragment height is drawn in parts, each in a box of its own inside the diagram's: each part
    // stands in the middle as a whole diagram does.
    [Fact]
    public async Task A_diagram_drawn_in_fragments_stands_in_the_middle_of_its_box()
    {
        var (uri, _) = ReportTestHelper.GenerateRunReportWithFlowArrows(TempDir, OutputDir, "CentredFragments.html",
            withFlow: 6, withoutFlow: 0, paths: Enumerable.Repeat("/health", 6).ToArray(), browserFragmentMaxHeight: 250);
        await Page.GotoReportAsync(uri);
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync(BrowserRenderWorkerTests.AllRenderedJs, null, new() { Timeout = 300000, PollingInterval = 200 });
        Assert.True(await Page.Locator(".plantuml-browser:not(.iflow-diagram) .puml-fragment svg").CountAsync() > 1, "the diagram is drawn in fragments");

        var offsets = await Page.EvaluateAsync<double[]>(Offsets);

        Assert.All(offsets, o => Assert.True(Math.Abs(o) <= 1, $"a part's middle is {o} px from the box's"));
    }
}
