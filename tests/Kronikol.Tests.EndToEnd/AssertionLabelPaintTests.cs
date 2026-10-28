using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.Playwright;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Assertion labels as a reader sees them (#210, ASSERTION_LABELS_PLAN fact 15): the notes Track records for statements
/// whose labels keep their code, painted in the report with the Assertions toggle on. A note's text was drawn with its
/// creole live, so code holding a tag (a type argument <c>&lt;b&gt;</c>), two <c>""</c>, two URLs' <c>//</c> or
/// <c>&lt;&lt;…&gt;&gt;</c> on one line styled the label instead of drawing it (Q9).
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class AssertionLabelPaintTests : DiagramNotePlaywrightBase
{
    public AssertionLabelPaintTests(PlaywrightFixture fixture) : base(fixture) { }

    /// <summary>Each statement, and the label its note paints.</summary>
    private static readonly (string Expression, string Label)[] Labels =
    [
        ("result.SplitPercentage.ToString(CultureInfo.InvariantCulture).ShouldNotBeNullOrEmpty()", "Result split percentage to string(CultureInfo.InvariantCulture) should not be null or empty"),
        ("result.CurrencyIsoAlpha3.ShouldBe(nameof(CurrencyCode.GBP))", "Result currency iso alpha3 should be 'GBP'"),
        ("result.Items.Count(x => x.IsActive).ShouldBe(2)", "Result items count(x => x.IsActive) should be 2"),
        ("result.Get<Item>().Name.ShouldBe(\"a\")", "Result get<Item>() name should be \"a\""),
        ("result.Map[Keys.Primary].ShouldBe(1)", "Result map[Keys.Primary] should be 1"),
        ("dict.Should().ContainKey(\"a\").WhoseValue.Should().Be(1)", "Dict should contain key \"a\", whose value should be 1"),
        ("await Assert.That(price).IsEqualTo(10.0).Within(0.5)", "Price is equal to 10.0 within 0.5"),
        ("result.Get<b>().Name.ShouldBe(\"\" + \"\")", "Result get<b>() name should be \"\" + \"\""),
        ("client.Get(\"http://a\").Text.ShouldBe(\"http://b\")", "Client get(\"http://a\") text should be \"http://b\""),
        ("result.Flags.ShouldBe((a<<2)>>1)", "Result flags should be (a<<2)>>1"),
    ];

    private string GenerateReport(string fileName)
    {
        // Track's own call, the one the weave makes after a passing statement, under a test of its own.
        var testId = "assertion-labels-" + Guid.NewGuid().ToString("N");
        using (TestIdentityScope.Begin(testId, testId))
        {
            foreach (var (expression, _) in Labels)
                Track.AssertionPassed(expression, "OrderTests.cs", 42);
        }
        var notes = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml is not null && l.PlantUml.Contains("<<assertionNote>>"))
            .Select(l => l.PlantUml!)
            .ToList();
        Assert.Equal(Labels.Length, notes.Count);

        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "The order reads right", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps = [new ScenarioStep { Keyword = "Then", Text = "the order reads right", Status = ExecutionResult.Passed }]
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
            {string.Join("\n", notes)}
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

    /// <summary>The text the first scenario's diagram paints with the Assertions toggle on, its runs joined by a space and
    /// its zero-width spaces dropped.</summary>
    private async Task<string> PaintedText()
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

        return await scenario.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<string>(
            "svg => Array.from(svg.querySelectorAll('text')).map(t => t.textContent).join(' ').replace(/\\u200b/g, '').replace(/\\u00a0/g, ' ')");
    }

    private static string WithoutSpaces(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    [Fact]
    public async Task Each_label_paints_its_code_as_written()
    {
        await Page.GotoReportAsync(GenerateReport("AssertionLabels.html"));

        var painted = await PaintedText();

        // The engine's error picture lists the source, so it would hold every label: it must not be what was drawn.
        Assert.DoesNotContain("PlantUML ", painted, StringComparison.Ordinal);
        Assert.DoesNotContain("Syntax Error", painted, StringComparison.Ordinal);
        Assert.Contains("OrderService", painted, StringComparison.Ordinal);

        // A label wraps at its spaces, so the comparison leaves spaces out: a character creole took, a tag it
        // swallowed or a pair it turned into guillemets fails it.
        var drawn = WithoutSpaces(painted);
        var missing = Labels.Where(l => !drawn.Contains(WithoutSpaces(l.Label), StringComparison.Ordinal)).Select(l => l.Label).ToList();
        Assert.True(missing.Count == 0, $"not painted as written:\n{string.Join("\n", missing)}\npainted:\n{painted}");
    }
}
