using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Steps that overlap, read on the page (#225): the rows and bars come from the collector as a run leaves them. Two top-level steps at
/// once are listed side by side in the order they began, and the diagram's bars read the same. A step still running when the report is
/// drawn is listed with no status icon, and the rows beside it keep theirs.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class StepsAtOnceTests(PlaywrightFixture fixture) : PlaywrightTestBase(fixture)
{
    private const string First = "the step that began first";
    private const string Second = "the step that began second";
    private const string Done = "the step that ended";
    private const string Running = "the step still running";

    [Fact]
    public async Task Two_top_level_steps_at_once_are_listed_in_the_order_they_began_and_the_bars_agree()
    {
        await Page.GotoReportAsync(Generate(await TwoAtOnce(), "StepsAtOnce.html"));
        await ExpandFirstScenarioWithDiagram();

        var rows = await Rows();
        Assert.Equal([First, Second], rows.Select(row => row.Text));
        Assert.Equal(["passed", "failed"], rows.Select(row => row.Status));
        Assert.Equal(0, await Page.Locator(".step .step .step-text").CountAsync());

        var svg = await WaitForDiagramSvg();
        // The engine may draw a label word by word, so spaces say nothing.
        var drawn = string.Concat((await svg.TextContentAsync() ?? "").Where(c => !char.IsWhiteSpace(c)));
        var firstBar = drawn.IndexOf(First.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
        Assert.True(firstBar >= 0, drawn);
        Assert.True(drawn.IndexOf(Second.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) > firstBar, drawn);
    }

    [Fact]
    public async Task A_step_still_running_is_listed_with_no_status_icon()
    {
        await Page.GotoReportAsync(Generate(OneLeftRunning(), "StepStillRunning.html"));
        await ExpandFirstScenarioWithDiagram();

        var rows = await Rows();
        Assert.Equal([Done, Running], rows.Select(row => row.Text));
        Assert.Equal(["passed", null], rows.Select(row => row.Status));
    }

    // Each row's text and the status class of its icon, as the page has them.
    private async Task<(string Text, string? Status)[]> Rows()
    {
        var rows = await Page.EvaluateAsync<string[][]>("""
            () => Array.from(document.querySelectorAll('.step')).map(step => [
                step.querySelector('.step-text').textContent.trim(),
                step.querySelector('.step-status')?.className.replace('step-status', '').trim() ?? ''
            ])
            """);
        return rows.Select(row => (row[0], row[1].Length == 0 ? null : row[1])).Select(row => (row.Item1, (string?)row.Item2)).ToArray();
    }

    private static async Task<string> TwoAtOnce()
    {
        var id = "steps-at-once-" + Guid.NewGuid().ToString("N");
        using var scope = TestIdentityScope.Begin(id, id);
        // What a woven async step does: starts, hands its body's task over, and its caller carries on.
        var firstsBody = new TaskCompletionSource();
        StepCollector.StartStep(id, "Given", First, null, null);
        var first = StepCollector.CompleteStepAsync(firstsBody.Task);
        StepCollector.StartStep(id, "When", Second, null, null);
        var second = StepCollector.CompleteStepAsync(Task.FromException(new InvalidOperationException("It went wrong")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        firstsBody.SetResult();
        await first;
        return id;
    }

    private static string OneLeftRunning()
    {
        var id = "step-still-running-" + Guid.NewGuid().ToString("N");
        StepCollector.StartStep(id, "Given", Done, null, null);
        StepCollector.CompleteStep(id, true);
        StepCollector.StartStep(id, "When", Running, null, null);
        return id;
    }

    private string Generate(string id, string fileName)
    {
        var bars = RequestResponseLogger.RequestAndResponseLogs
            .Where(log => log.TestId == id && log.IsOverrideStart && log.MarkerKind == DiagramMarkerKind.Step)
            .Select(log => log.PlantUml!)
            .ToArray();
        var source = "@startuml\nactor Tests\nparticipant Svc\n" + string.Join("\nTests -> Svc: a call\n", bars) + "\nTests -> Svc: a call\n@enduml";

        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Steps",
                Scenarios = [new Scenario { Id = id, DisplayName = "Overlapping steps", Result = ExecutionResult.Failed, Steps = StepCollector.GetSteps(id) }]
            }
        ];

        var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode(id, "", source)], features, DateTime.UtcNow, DateTime.UtcNow, null,
            Path.Combine(TempDir, fileName), "Steps", true, diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }
}
