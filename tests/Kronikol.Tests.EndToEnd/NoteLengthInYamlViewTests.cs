using System.Text.Json;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The Payloads control and the Headers toggle judge how long a note is by the lines it shows. A JSON body holding a
/// string with line breaks is short as JSON and long in the YAML view a report starts in (the string becomes a block, a
/// line each). Until 5.2.4 both judged such a body by its JSON lines, so Payloads -> Truncated left it whole and the
/// Headers toggle expanded it, while the first drawing and the note's own buttons cut it
/// (<c>plans/LONG_NOTE_SCROLL_PLAN.md</c> F1).
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteLengthInYamlViewTests : PlaywrightTestBase
{
    public NoteLengthInYamlViewTests(PlaywrightFixture fixture) : base(fixture) { }

    private sealed record NoteRows(int Rows, bool Cut);

    private async Task OpenAsync(string fileName)
    {
        await Page.GotoAsync(LongPayloadReports.YamlLongerThanJson(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg(60000);
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return c && !c._noteRendering && !window._plantumlRendering && c.querySelector('svg .note-hover-rect');
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
    }

    /// <summary>The response note's drawn rows, and whether it ends with the cut's "...".</summary>
    private async Task<NoteRows> ResponseNoteAsync()
    {
        var json = await Page.EvaluateAsync<string>("""
            () => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                var groups = window._findNoteGroups(svg);
                var g = groups[groups.length - 1];
                var text = g.texts.map(t => t.textContent).join('');
                return JSON.stringify({ Rows: window._paintedNoteRowCount(g), Cut: /\.\.\.\s*$/.test(text) });
            }
            """);
        return JsonSerializer.Deserialize<NoteRows>(json)!;
    }

    /// <summary>Runs <paramref name="action"/>, a control's command, and waits for its queue to end: the diagram drawn
    /// again, or passed over when the command leaves its drawing as it was (a control marks itself pending until then).</summary>
    private async Task RedrawAsync(string action)
    {
        await Page.EvaluateAsync(action);
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return !document.querySelector('[data-pending]') && !c._noteRendering && !window._plantumlRendering
                    && c.querySelector('svg .note-hover-rect');
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
    }

    private async Task<NoteRows> AtLoadAsync()
    {
        var atLoad = await ResponseNoteAsync();
        Assert.True(atLoad.Cut, $"premise: the report starts Truncated at 10 lines, and the YAML view is longer ({atLoad.Rows} rows)");
        Assert.Equal(11, atLoad.Rows);
        return atLoad;
    }

    [Fact]
    public async Task Payloads_Truncated_cuts_a_note_that_is_long_in_its_YAML_view()
    {
        await OpenAsync("yaml-length-report-control.html");
        var atLoad = await AtLoadAsync();

        await RedrawAsync("() => window._setReportDetails('expanded')");
        var expanded = await ResponseNoteAsync();
        Assert.False(expanded.Cut);
        Assert.True(expanded.Rows > 11, $"premise: the YAML view is longer than the limit ({expanded.Rows} rows)");

        await RedrawAsync("() => window._setReportDetails('truncated')");
        Assert.Equal(atLoad, await ResponseNoteAsync());
    }

    [Fact]
    public async Task The_scenario_s_Payloads_Truncated_cuts_it_too()
    {
        await OpenAsync("yaml-length-scenario-control.html");
        var atLoad = await AtLoadAsync();

        await RedrawAsync("() => window._setAllNotes(document.querySelector('details.scenario .details-radio-btn[data-state=\"expanded\"]'), 'expanded')");
        Assert.False((await ResponseNoteAsync()).Cut);

        await RedrawAsync("() => window._setAllNotes(document.querySelector('details.scenario .details-radio-btn[data-state=\"truncated\"]'), 'truncated')");
        Assert.Equal(atLoad, await ResponseNoteAsync());
    }

    [Fact]
    public async Task The_Headers_toggle_keeps_a_note_that_is_long_in_its_YAML_view_cut()
    {
        await OpenAsync("yaml-length-headers.html");
        await AtLoadAsync();

        await RedrawAsync("() => window._toggleHeaders(document.querySelector('.toolbar-right .toggle-btn[data-toggle=\"headers\"]'))");
        var shown = await ResponseNoteAsync();
        Assert.True(shown.Cut, $"the note was expanded when its headers were shown ({shown.Rows} rows)");
    }
}
