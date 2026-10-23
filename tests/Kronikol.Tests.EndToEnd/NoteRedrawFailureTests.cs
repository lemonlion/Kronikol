namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A note's redraw of a diagram drawn whole ends on any answer from the engine, as the report's controls and a diagram
/// drawn in parts already did. Until 5.2.4 it waited for an <c>&lt;svg&gt;</c>: an answer in text (a failure) held every
/// note button on the page for 30 s, and a failure drawn as a picture (the engine's stack overflow) was shown raw and
/// cached under its source, so drawing that source again drew the failure from the cache
/// (<c>plans/LONG_NOTE_SCROLL_PLAN.md</c> F2 and F7). The engine is stubbed in the page for one call, never on the network.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteRedrawFailureTests : PlaywrightTestBase
{
    public NoteRedrawFailureTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string StackOverflowPicture =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"320\" height=\"50\"><text x=\"5\" y=\"15\">PlantUML 1.2026.10beta1</text>"
        + "<text x=\"5\" y=\"35\">Maximum call stack size exceeded</text></svg>";

    private async Task OpenTwoDiagramsAsync(string fileName)
    {
        await Page.GotoReportAsync(LongPayloadReports.YamlLongerThanJson(TempDir, OutputDir, fileName, scenarios: 2));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg(60000);
        await Page.WaitForFunctionAsync("""
            () => {
                var cs = document.querySelectorAll('[data-diagram-type="plantuml"]');
                return cs.length === 2 && !window._plantumlRendering
                    && Array.from(cs).every(c => !c._noteRendering && c.querySelector('svg .note-hover-rect'));
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
    }

    /// <summary>The engine's next drawing writes <paramref name="answer"/> (HTML) into its target instead, a moment later as
    /// a worker would; the drawing after it is the engine's own.</summary>
    private Task StubNextDrawingAsync(string answer) => Page.EvaluateAsync("""
        answer => {
            var real = window.plantuml.render;
            window.plantuml.render = function(lines, id) {
                window.plantuml.render = real;
                setTimeout(function() { document.getElementById(id).innerHTML = answer; }, 0);
            };
        }
        """, answer);

    /// <summary>Expands the response note of diagram <paramref name="index"/>, cut at the line limit, with the button at its
    /// foot, which the page shows when the pointer reaches the note.</summary>
    private async Task ExpandNoteAsync(int index)
    {
        await Page.EvaluateAsync("""
            index => {
                var c = document.querySelectorAll('[data-diagram-type="plantuml"]')[index];
                var rects = c.querySelectorAll('.note-hover-rect');
                rects[rects.length - 1].dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }));
            }
            """, index);
        await Page.WaitForFunctionAsync("""
            index => document.querySelectorAll('[data-diagram-type="plantuml"]')[index]
                .querySelector('.note-toggle-icon[data-note-btn="expand-bottom"] rect')
            """, index, new() { Timeout = 10000, PollingInterval = 200 });
        await Page.EvaluateAsync("""
            index => {
                var c = document.querySelectorAll('[data-diagram-type="plantuml"]')[index];
                window['__before' + index] = c.querySelector('svg');
                var expand = c.querySelectorAll('.note-toggle-icon[data-note-btn="expand-bottom"] rect');
                expand[expand.length - 1].dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
            }
            """, index);
    }

    /// <summary>Waits until diagram <paramref name="index"/>'s picture was replaced and nothing is drawing.</summary>
    private Task RedrawnAsync(int index, int timeoutMs) => Page.WaitForFunctionAsync("""
        index => {
            var c = document.querySelectorAll('[data-diagram-type="plantuml"]')[index];
            return !document.contains(window['__before' + index]) && !c._noteRendering && !window._plantumlRendering;
        }
        """, index, new() { Timeout = timeoutMs, PollingInterval = 200 });

    private Task<int> ResponseNoteRowsAsync(int index) => Page.EvaluateAsync<int>("""
        index => {
            var svg = document.querySelectorAll('[data-diagram-type="plantuml"]')[index].querySelector('svg');
            var groups = svg ? window._findNoteGroups(svg) : [];
            return groups.length ? window._paintedNoteRowCount(groups[groups.length - 1]) : -1;
        }
        """, index);

    [Fact]
    public async Task An_answer_in_text_frees_every_note_button_at_once()
    {
        await OpenTwoDiagramsAsync("note-redraw-text-failure.html");
        await StubNextDrawingAsync("Render error: a stubbed engine failure");
        await ExpandNoteAsync(0);

        // the failed drawing ends at once: nothing is left drawing
        await RedrawnAsync(0, 2000);
        var failed = await Page.Locator("[data-diagram-type='plantuml']").First.TextContentAsync();
        Assert.Contains("a stubbed engine failure", failed);

        // and the other diagram's note buttons work
        await ExpandNoteAsync(1);
        await RedrawnAsync(1, 30000);
        Assert.True(await ResponseNoteRowsAsync(1) > 11, "the second diagram's note was expanded");
    }

    [Fact]
    public async Task A_failure_drawn_as_a_picture_is_described_and_never_drawn_from_the_cache()
    {
        await OpenTwoDiagramsAsync("note-redraw-picture-failure.html");
        await StubNextDrawingAsync(StackOverflowPicture);
        await ExpandNoteAsync(0);
        await RedrawnAsync(0, 30000);

        var first = Page.Locator("[data-diagram-type='plantuml']").First;
        Assert.Equal(1, await first.Locator("[data-engine-failure]").CountAsync());

        // the same source again, through the scenario's Payloads control: the engine draws it, not the cache
        await Page.EvaluateAsync("""
            () => {
                var sc = document.querySelector('details.scenario');
                window._setAllNotes(sc.querySelector('.details-radio-btn[data-state="truncated"]'), 'truncated');
            }
            """);
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return !c._noteRendering && !window._plantumlRendering && c.querySelector('svg .note-hover-rect');
            }
            """, null, new() { Timeout = 30000, PollingInterval = 200 });
        await Page.EvaluateAsync("""
            () => {
                var sc = document.querySelector('details.scenario');
                window.__before0 = sc.querySelector('[data-diagram-type="plantuml"] svg');
                window._setAllNotes(sc.querySelector('.details-radio-btn[data-state="expanded"]'), 'expanded');
            }
            """);
        await RedrawnAsync(0, 30000);
        Assert.Equal(0, await first.Locator("[data-engine-failure]").CountAsync());
        Assert.True(await ResponseNoteRowsAsync(0) > 11, "the note was drawn expanded by the engine");
    }
}
