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

    private string GenerateReport() =>
        GenerateReport("result.ShouldBe(expected)", "result\n    should be\n5\n    but was\n3", 5, "ShouldlyNote.html");

    private string GenerateReport(string expression, string message, object expected, string fileName) =>
        GenerateReport(expression, message, ["expected"], [expected], fileName);

    private string GenerateReport(string expression, string message, string[] names, object?[] values, string fileName)
    {
        // Track's own call, the one the weave makes after a failing statement, under a test of its own.
        var testId = "shouldly-note-" + Guid.NewGuid().ToString("N");
        using (TestIdentityScope.Begin(testId, testId))
            Track.AssertionFailedWithValues(expression, message, names, values, "OrderTests.cs", 42);
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
            null, Path.Combine(TempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: ReportTestHelper.ClassicStart);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>The words the first scenario's diagram paints with the Assertions toggle on, one text run per word.</summary>
    private async Task<string> PaintedWords()
    {
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await RenderAllDiagramsAndWait(minCount: 1);

        var scenario = Page.Locator("details.scenario").First;
        await OpenScenarioSettingsAsync(scenario);
        var show = scenario.Locator(".toggle-btn[data-toggle='assertions'][data-shown='false']");
        if (await show.CountAsync() > 0)
        {
            var renders = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
            await show.First.ClickAsync();
            await Page.WaitForFunctionAsync(
                "(prev) => !window._plantumlRendering && (window._renderCompleteCount || 0) > prev",
                renders, new() { Timeout = 90000, PollingInterval = 200 });
        }

        var painted = await scenario.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<string>(
            "svg => Array.from(svg.querySelectorAll('text')).map(t => t.textContent).join(' ')");
        return string.Join(' ', painted.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task A_method_call_argument_note_paints_its_value()
    {
        // #163: the expected value read before the call, where 4.14.6 painted 'null'() whatever the value.
        await Page.GotoAsync(GenerateReport(
            "actual.Colour.ToString().ShouldBe(expected.Colour.ToString())",
            "actual.Colour.ToString()\n    should be\n\"Blue\"\n    but was\n\"Red\"",
            new Expected(Colour.Blue), "MethodCallNote.html"));

        var words = await PaintedWords();

        Assert.Contains("Actual colour should be 'Blue'.ToString()", words);
        Assert.DoesNotContain("'null'", words);
        Assert.Contains("should be \"Blue\" but was \"Red\"", words);
    }

    [Fact]
    public async Task An_argument_value_note_paints_the_value_the_assertion_received()
    {
        // From 4.16.0 the weave passes each argument's value under the argument's text, and the note paints that
        // value in place of the whole argument, where 4.14.8 painted the value read before the call.
        await Page.GotoAsync(GenerateReport(
            "actual.Colour.ToString().ShouldBe(expected.Colour.ToString())",
            "actual.Colour.ToString()\n    should be\n\"Blue\"\n    but was\n\"Red\"",
            ["expected", "expected.Colour.ToString()"], [new Expected(Colour.Blue), "Blue"], "ArgumentValueNote.html"));

        var words = await PaintedWords();

        Assert.Contains("Actual colour should be 'Blue'", words);
        Assert.DoesNotContain("'Blue'.ToString()", words);
        Assert.Contains("should be \"Blue\" but was \"Red\"", words);
    }

    private enum Colour { Red, Blue }

    private sealed record Expected(Colour Colour);

    [Fact]
    public async Task A_Shouldly_assertion_note_paints_its_sentence()
    {
        await Page.GotoAsync(GenerateReport());
        // What the SVG paints, one text run per word: the sentence, then Shouldly's message line by line.
        var words = await PaintedWords();

        Assert.Contains("Result should be '5'", words);
        Assert.Contains("result should be 5 but was 3", words);
    }
}
