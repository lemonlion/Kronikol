using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A filter that takes notes out leaves the drawn notes fewer than the source's, and a note's buttons act on the source's
/// note they were drawn from (plans/OPTION_C_PLAN.md S9, P9, F6). The page found it by matching the notes' text, the
/// first source note reading the same, so a note that read like one the filter had taken out before it ("1 row" in the
/// setup and again later) was taken for that one, and its buttons changed the hidden note. Each note now carries a mark
/// through the filters, and the marks that come out give the map.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class ExactNoteMapTests : PlaywrightTestBase
{
    public ExactNoteMapTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string Head = "@startuml\nparticipant \"Orders\" as orders\nparticipant \"Stock\" as stock\ndatabase \"Order Db\" as db\n";
    private const string Note = "note right\n1 row\nend note\n";

    // the databases filter takes out the second note, which reads like the third
    private const string DatabaseSource = Head + "orders -> stock : reserve\nnote right\nX item\nend note\n" +
        "orders -> db : INSERT\n" + Note + "stock -> orders : reserved\n" + Note + "@enduml";
    // the setup filter takes out the first note, which reads like the second (step bars as the emitter writes them)
    private const string SetupSource = Head + "hnote across <<stepDelimiter>> #black:<color:white>Given stock exists\n" +
        "orders -> stock : seed\n" + Note + "hnote across <<stepDelimiter>> #black:<color:white>When an order is placed\n" +
        "orders -> stock : reserve\n" + Note + "@enduml";

    // the text match ran when a diagram was first drawn with a filter off (a toggle's redraw maps its notes another
    // way), so the reports start with the filter off
    private async Task OpenAsync(string fileName, string source, ResolvedToggleDefaults defaults)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios = [new() { Id = "s1", DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode("s1", "", source)],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs, toggleDefaults: defaults);
        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    private Task WaitForReRenderAsync() => Page.WaitForFunctionAsync("""
        () => {
            var container = document.querySelector('[data-plantuml]');
            if (!container || container._noteRendering || window._plantumlRendering) return false;
            var svg = container.querySelector('svg');
            return svg && svg.querySelectorAll('.note-toggle-icon').length > 0;
        }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });

    private async Task ClickMinusAsync(int drawnNote)
    {
        await Page.EvaluateAsync(
            "idx => document.querySelectorAll('.note-hover-rect')[idx].dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }))", drawnNote);
        await Page.WaitForFunctionAsync(
            "() => Array.from(document.querySelectorAll('.note-toggle-icon[data-note-btn=\"minus\"]')).some(i => i.style.opacity === '1')",
            null, new() { Timeout = 5000, PollingInterval = 200 });
        await Page.EvaluateAsync("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon[data-note-btn="minus"]'))
                .filter(i => i.style.opacity === '1')[0].querySelector('rect').dispatchEvent(new MouseEvent('click', { bubbles: true }))
            """);
    }

    private Task<int> StepAsync(int sourceNote) => Page.EvaluateAsync<int>(
        "i => { var c = document.querySelector('[data-plantuml]'); return (c._noteSteps && c._noteSteps[i]) || 0; }", sourceNote);

    [Theory]
    [InlineData("databases", DatabaseSource, 1, 2, 1)]
    [InlineData("setup", SetupSource, 0, 1, 0)]
    public async Task A_notes_buttons_act_on_its_own_note_when_a_filter_took_out_one_that_reads_the_same(
        string toggle, string source, int drawnNote, int sourceNote, int lookalike)
    {
        await OpenAsync($"ExactNoteMap-{toggle}.html", source, toggle == "databases"
            ? ResolvedToggleDefaults.BuiltIn with { DatabasesShown = false, Details = ReportDetailsState.Expanded }
            : ResolvedToggleDefaults.BuiltIn with { SetupShown = false, Details = ReportDetailsState.Expanded });

        await ClickMinusAsync(drawnNote);
        // the collapsed "1 row" shows all of itself, so it has no buttons to wait for (5.0.0): wait for a note's state
        await Page.WaitForFunctionAsync(
            "() => { var c = document.querySelector('[data-plantuml]'); return c && !c._noteRendering && c._noteSteps && Object.keys(c._noteSteps).length > 0; }",
            null, new() { Timeout = 15000, PollingInterval = 200 });

        Assert.NotEqual(0, await StepAsync(sourceNote));
        Assert.Equal(0, await StepAsync(lookalike));
    }
}
