using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The Assertions and Steps toggles take the markers out of a scenario's sequence diagram source before it is drawn: an
/// assertion note (hnote ... end note) and a step bar (one hnote line). Taking one out must leave the statements around
/// it on lines of their own; it once took the line break on both sides, so where a diagram's lines end in LF (a report
/// written on Linux or macOS) the statement before each marker ran on into the one after it. A diagram that holds
/// nothing but hidden markers has nothing to draw, and the page says so where the diagram would be, naming the toggles
/// by the words they show since 5.0.0 (plans/OPTION_C_PLAN.md S8).
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class FilteredMarkerTests : PlaywrightTestBase
{
    public FilteredMarkerTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string NoteBetweenArrows = "@startuml\nparticipant \"Caller\" as caller\nparticipant \"Orders\" as orders\n" +
        "caller -> orders : one\nhnote across <<assertionNote>> #E8F5E9\n✓ The order is taken\nend note\n'__^*__:OrderTests.cs:L12\n" +
        "orders -> caller : two\n@enduml";
    private const string StepBetweenArrows = "@startuml\nparticipant \"Caller\" as caller\nparticipant \"Orders\" as orders\n" +
        "caller -> orders : one\nhnote across <<stepDelimiter>> : When the order is sent\norders -> caller : two\n@enduml";
    // what the generator writes for a test that asserted but called nothing: one lifeline for the note to span
    private const string AssertionOnly = "@startuml\nparticipant \"(no interactions)\" as noInteractions\n" +
        "hnote across <<assertionNote>> #E8F5E9\n✓ The mock answers 200\nend note\n@enduml";

    // assertions start shown since 5.0.0; these tests write the report with them hidden, the first render being the
    // path the strip took
    private async Task OpenAsync(string fileName, string source, bool assertionsShown = true)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios = [new() { Id = "s1", DisplayName = "Take an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode("s1", "", source)],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: ResolvedToggleDefaults.BuiltIn with { AssertionsShown = assertionsShown });
        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        await ExpandFirstScenarioWithDiagram();
    }

    private static Task<string[]> Texts(Microsoft.Playwright.ILocator svg) =>
        svg.EvaluateAsync<string[]>("s => [...s.querySelectorAll('text')].map(t => t.textContent)");

    [Fact]
    public async Task Hiding_assertions_keeps_the_statement_after_a_note_on_a_line_of_its_own()
    {
        await OpenAsync("FilteredMarkerAssertion.html", NoteBetweenArrows, assertionsShown: false);

        // assertions hidden: the note goes, both arrows stay, and neither label takes in the next line
        var texts = await Texts(await WaitForDiagramSvg());
        Assert.DoesNotContain(texts, t => t.Contains("The order is taken"));
        Assert.Contains("one", texts);
        Assert.Contains("two", texts);
        Assert.DoesNotContain(texts, t => t.Contains("__^*__") || t.Contains("orders"));
    }

    [Fact]
    public async Task Hiding_steps_keeps_the_statement_after_a_step_bar_on_a_line_of_its_own()
    {
        await OpenAsync("FilteredMarkerStep.html", StepBetweenArrows);
        var svg = await WaitForDiagramSvg();
        Assert.Contains(await Texts(svg), t => t.Contains("When the order is sent"));

        await OpenScenarioSettingsAsync();
        await Page.Locator(".scenario-settings .toggle-btn[data-toggle='steps']").ClickAsync();
        await Page.WaitForFunctionAsync(
            "() => { const s = document.querySelector(\"[data-diagram-type='plantuml'] svg\"); return s && !s.textContent.includes('When the order is sent'); }",
            null, new() { Timeout = 30000, PollingInterval = 200 });

        var texts = await Texts(Page.Locator("[data-diagram-type='plantuml'] svg").First);
        Assert.Contains("one", texts);
        Assert.Contains("two", texts);
        Assert.DoesNotContain(texts, t => t.Contains("orders"));
    }

    [Fact]
    public async Task A_diagram_of_nothing_but_hidden_markers_says_so_and_names_the_toggles_by_their_words()
    {
        await OpenAsync("FilteredMarkerNothingToDraw.html", AssertionOnly, assertionsShown: false);
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");

        // assertions hidden, so the note is filtered away and the lifeline alone is not drawn
        var message = Page.Locator("[data-nothing-to-draw]").First;
        await message.WaitForAsync(new() { Timeout = 30000 });
        var text = await message.InnerTextAsync();
        Assert.StartsWith("Nothing to draw with the current filters", text);
        Assert.Contains("Assertions", text);
        Assert.True(await Page.Locator(".btn-label[data-label='Assertions']").CountAsync() > 0, "the page has no toggle that says Assertions");
        Assert.DoesNotContain("Shown", text);
        Assert.DoesNotContain("Hidden", text);
    }
}
