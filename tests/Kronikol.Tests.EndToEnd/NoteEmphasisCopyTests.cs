using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A payload drawn with FocusFields emphasis carries Kronikol's own markup in its note's source: the focused field's line
/// in <c>&lt;b&gt;</c>, every other line in <c>&lt;color:lightgray&gt;</c> (FocusEmphasis.Bold and FocusDeEmphasis.LightGray,
/// the defaults). A payload's own <c>&lt;</c> is always escaped, so a tag in a note line is Kronikol's, and the note's
/// copies hand back the payload. Until 5.4.0 "Copy box text" copied the tags as text (found while building the payload
/// scroll box, <c>plans/LONG_NOTE_SCROLL_PLAN.md</c>).
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteEmphasisCopyTests : PlaywrightTestBase
{
    public NoteEmphasisCopyTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Copy_box_text_on_a_FocusFields_payload_copies_the_payload_without_the_emphasis_markup()
    {
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        var logs = LongPayloadReports.Call("emphasis", 0, LongPayloadReports.JsonOfLines(12, "emph"));
        logs[1].FocusFields = ["emph_0003"];
        await Page.GotoAsync(LongPayloadReports.Write(TempDir, OutputDir, "emphasis-copy.html", [("emphasis", "A focused field", logs)],
            ReportTestHelper.ClassicStart with { Details = ReportDetailsState.Expanded }));
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
        var source = await Page.EvaluateAsync<string>("() => document.querySelector('[data-diagram-type=\"plantuml\"]').getAttribute('data-plantuml')");
        Assert.Contains("<b>", source);
        Assert.Contains("<color:lightgray>", source);

        // the response's note: the last one drawn
        await Page.EvaluateAsync("""
            () => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                var groups = window._findNoteGroups(svg), g = groups[groups.length - 1], r = g.paths[0].getBoundingClientRect();
                g.paths[0].dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true,
                    clientX: r.left + r.width / 2, clientY: r.top + r.height / 2 }));
            }
            """);
        var item = Page.Locator(".diagram-ctx-menu").GetByText("Copy box text", new() { Exact = true });
        await item.WaitForAsync(new() { Timeout = 5000 });
        await item.ClickAsync();
        var copied = (await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()")).Replace("\r\n", "\n");

        Assert.Contains("  \"emph_0003\": \"value 3\",", copied);
        Assert.Contains("  \"emph_0004\": \"value 4\",", copied);
        Assert.DoesNotContain("<b>", copied);
        Assert.DoesNotContain("</b>", copied);
        Assert.DoesNotContain("<color:", copied);
        Assert.DoesNotContain("</color>", copied);
    }
}
