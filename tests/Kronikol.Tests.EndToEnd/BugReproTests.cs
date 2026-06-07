namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Notes)]
public class BugReproTests : DiagramNotePlaywrightBase
{
    public BugReproTests(PlaywrightFixture fixture) : base(fixture) { }

    /// <summary>
    /// When a note is very tall (taller than the viewport), the hover buttons
    /// at the top-right must reposition to stay visible when the user scrolls
    /// to the middle of the note and hovers.
    /// </summary>
    [Fact]
    public async Task Tall_note_hover_buttons_reposition_to_viewport()
    {
        await Page.GotoAsync(GenerateChunkedDatabaseNoteReport("TallNoteRepos.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return c && !c._noteRendering && !window._plantumlRendering && c.querySelector('svg');
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });

        // Expand notes to trigger chunkLargeNotes
        await Page.Locator(".toolbar-row .details-radio-btn[data-state='expanded']").ClickAsync();

        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                if (!c || c._noteRendering || window._plantumlRendering) return false;
                var frags = c.querySelectorAll('.puml-fragment');
                if (frags.length < 2) return false;
                for (var i = 0; i < frags.length; i++) {
                    if (!frags[i].querySelector('svg')) return false;
                }
                return true;
            }
        """, null, new() { Timeout = 120000, PollingInterval = 200 });

        // Find a note taller than 500px, scroll to its MIDDLE, hover, and check
        // that a minus/plus button is visible in the viewport
        var result = await Page.EvaluateAsync<string>("""
            (() => {
                var frags = document.querySelectorAll('.puml-fragment');
                for (var fi = 0; fi < frags.length; fi++) {
                    var svg = frags[fi].querySelector('svg');
                    if (!svg) continue;
                    var groups = window._findNoteGroups(svg);
                    for (var gi = 0; gi < groups.length; gi++) {
                        var bbox = window._getNoteBBox(groups[gi]);
                        if (bbox.height < 500) continue;

                        // Found a tall note — scroll to its middle
                        var midText = groups[gi].texts[Math.floor(groups[gi].texts.length / 2)];
                        if (midText) midText.scrollIntoView({block: 'center'});

                        // Trigger hover
                        groups[gi].paths[0].dispatchEvent(new MouseEvent('mouseenter', {bubbles:true}));

                        // Check for visible minus/plus button in viewport
                        var icons = frags[fi].querySelectorAll('.note-toggle-icon');
                        var inViewport = 0;
                        for (var i = 0; i < icons.length; i++) {
                            if (icons[i].style.opacity === '0') continue;
                            var btn = icons[i].getAttribute('data-note-btn');
                            if (btn !== 'minus' && btn !== 'plus') continue;
                            var r = icons[i].getBoundingClientRect();
                            if (r.y >= -5 && r.y < window.innerHeight + 5) inViewport++;
                        }

                        return 'noteHeight=' + bbox.height.toFixed(0)
                            + ' minusPlusInViewport=' + inViewport;
                    }
                }
                return 'NO_TALL_NOTE';
            })()
        """);

        Assert.DoesNotContain("NO_TALL_NOTE", result);
        Assert.True(result.Contains("minusPlusInViewport=") && !result.Contains("minusPlusInViewport=0"),
            $"Minus/plus button not visible in viewport for tall note: {result}");
    }
}
