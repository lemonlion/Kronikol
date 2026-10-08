using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.Playwright;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A Shouldly assertion's note as a reader sees it (SHOULDLY_ASSERTIONS_PLAN fact 26): the note Track records for a
/// woven <c>result.ShouldBe(expected)</c>, with the values the weave reads and Shouldly's own message, painted in the
/// report with the Assertions toggle on. Shouldly's message spans lines and indents its checks
/// (<c>result⏎    should be⏎5⏎    but was⏎3</c>), and the page must keep each line.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class ShouldlyAssertionNoteTests : DiagramNotePlaywrightBase
{
    public ShouldlyAssertionNoteTests(PlaywrightFixture fixture) : base(fixture) { }

    private string GenerateReport()
    {
        // Track's own call, the one the weave makes after a failing statement, under a test of its own.
        var testId = "shouldly-note-" + Guid.NewGuid().ToString("N");
        using (TestIdentityScope.Begin(testId, testId))
            Track.AssertionFailedWithValues("result.ShouldBe(expected)", "result\n    should be\n5\n    but was\n3",
                ["expected"], [5], "OrderTests.cs", 42);
        var note = RequestResponseLogger.RequestAndResponseLogs
            .Single(l => l.TestId == testId && l.PlantUml is not null && l.PlantUml.Contains("<<assertionNote>>"))
            .PlantUml!;

        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "The total is right", IsHappyPath = true,
                        Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(1),
                        Steps = [new ScenarioStep { Keyword = "Then", Text = "the total is right", Status = ExecutionResult.Failed }]
                    }
                ]
            }
        };
        var diagram = $"""
            @startuml
            actor "Caller" as caller
            participant "OrderService" as svc
            caller -> svc : GET /api/orders/1
            svc --> caller : 200 OK
            {note}
            @enduml
            """;

        var path = ReportGenerator.GenerateHtmlReport(
            [new DiagramAsCode("s1", "", diagram)], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, "ShouldlyNote.html"), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: ReportTestHelper.ClassicStart);
        File.Copy(path, Path.Combine(OutputDir, "ShouldlyNote.html"), true);
        return new Uri(path).AbsoluteUri;
    }

    [Fact]
    public async Task A_Shouldly_assertion_note_paints_its_sentence()
    {
        await Page.GotoAsync(GenerateReport());
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await RenderAllDiagramsAndWait(minCount: 1);

        var scenario = Page.Locator("details.scenario").First;
        var show = scenario.Locator(".toggle-btn[data-toggle='assertions'][data-shown='false']");
        if (await show.CountAsync() > 0)
        {
            var renders = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
            await show.First.ClickAsync();
            await Page.WaitForFunctionAsync(
                "(prev) => !window._plantumlRendering && (window._renderCompleteCount || 0) > prev",
                renders, new() { Timeout = 90000, PollingInterval = 200 });
        }

        // What the SVG paints, one text run per word: the sentence, then Shouldly's message line by line.
        var painted = await scenario.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<string>(
            "svg => Array.from(svg.querySelectorAll('text')).map(t => t.textContent).join(' ')");
        var words = string.Join(' ', painted.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains("Result should be '5'", words);
        Assert.Contains("result should be 5 but was 3", words);
    }
}
