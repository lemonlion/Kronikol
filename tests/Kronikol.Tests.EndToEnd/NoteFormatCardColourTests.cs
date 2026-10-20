namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A note keeps the report's card look (<c>k-card</c>, the page's note colour) however many times its payload is
/// switched between JSON and YAML: each switch draws the diagram again, and every note of the new picture is a card,
/// in the colour it had. In 5.0.0 switching a note JSON, YAML, JSON, YAML gave it the engine's own yellow again.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteFormatCardColourTests : DiagramNotePlaywrightBase
{
    public NoteFormatCardColourTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task OpenAsync(string report)
    {
        await Page.GotoAsync(report);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    /// <summary>How many of the first diagram's notes are drawn as cards, and in which colours, once it is drawn.</summary>
    private async Task<string> CardsAsync()
    {
        const string read = """
            () => {
                var host = document.querySelector('[data-diagram-type="plantuml"]');
                if (!host || host._noteRendering || window._plantumlRendering) return null;
                var cards = Array.from(host.querySelectorAll('svg .k-card'));
                var fills = {};
                cards.forEach(function (c) { var f = getComputedStyle(c).fill; fills[f] = (fills[f] || 0) + 1; });
                return JSON.stringify(fills);
            }
            """;
        try
        {
            // a picture drawn out of sight is tagged as it comes into view, so the cards are waited for
            await Page.WaitForFunctionAsync($"() => {{ var r = ({read})(); return r && r !== '{{}}'; }}", null,
                new() { Timeout = 15_000, PollingInterval = 200 });
            await Page.WaitForTimeoutAsync(500);
        }
        catch (TimeoutException) { }
        return await Page.EvaluateAsync<string?>(read) ?? "(still drawing)";
    }

    private async Task SwitchesKeepTheCardsAsync()
    {
        var before = await CardsAsync();
        Assert.NotEqual("{}", before);
        for (var i = 1; i <= 4; i++)
        {
            await ClickNoteFormatButton();
            Assert.Equal((i, before), (i, await CardsAsync()));
        }
    }

    [Fact]
    public async Task A_note_keeps_its_card_colour_through_four_format_switches()
    {
        await OpenAsync(ReportTestHelper.GenerateReportWithJsonYamlNotes(TempDir, OutputDir, "NoteCard_FourSwitches.html", teoz: true));
        await SwitchesKeepTheCardsAsync();
    }

    [Fact]
    public async Task A_note_in_a_diagram_split_into_fragments_keeps_its_card_colour_through_four_format_switches()
    {
        await OpenAsync(GenerateFragmentedDiagramReport("NoteCard_FourSwitches_Fragments.html"));
        await SwitchesKeepTheCardsAsync();
    }

    // the report's own JSON/YAML list redraws every diagram through the page's render queue, as the filters do
    [Fact]
    public async Task The_notes_of_a_diagram_split_into_fragments_stay_cards_through_the_report_s_format_list()
    {
        await OpenAsync(GenerateFragmentedDiagramReport("NoteCard_ReportList_Fragments.html"));
        var before = await CardsAsync();
        Assert.NotEqual("{}", before);
        var list = Page.Locator(".toolbar-right .note-format-select");
        foreach (var (format, i) in new[] { "yaml", "json", "yaml", "json" }.Select((f, i) => (f, i + 1)))
        {
            var count = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
            await list.SelectOptionAsync(format);
            await WaitForRenderCountIncrease(count);
            Assert.Equal((i, before), (i, await CardsAsync()));
        }
    }
}
