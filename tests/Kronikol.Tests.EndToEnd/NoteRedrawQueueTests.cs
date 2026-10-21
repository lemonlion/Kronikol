namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The report's controls draw every diagram again through one queue (collapsible-notes-script.js processRenderQueue),
/// and a note's own buttons draw its diagram again outside it. Each fact here is a way the two, or two queues, met on one
/// diagram and left it showing something other than what the controls say (5.2.5).
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class NoteRedrawQueueTests : PlaywrightTestBase
{
    public NoteRedrawQueueTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string Diagram = "[data-diagram-type=\"plantuml\"]";
    private const string HeadersButton = "button[data-toggle='headers'][onclick*='_toggleHeaders(']";

    private async Task OpenAsync(string url)
    {
        await Page.GotoAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await SettledAsync();
    }

    /// <summary>Waits until the diagram is drawn, whole or in parts, nothing is drawing, and it stays so for a while.</summary>
    private async Task SettledAsync()
    {
        const string settled = """
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                if (!c || c._noteRendering || window._plantumlRendering || c.querySelector('.puml-fragment-new')) return false;
                var parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
                return (parts.length ? parts : [c]).every(function(b) { return !!b.querySelector(':scope > svg'); });
            }
            """;
        for (var i = 0; i < 2; i++)
        {
            await Page.WaitForFunctionAsync(settled, null, new() { Timeout = 60000, PollingInterval = 200 });
            await Page.WaitForTimeoutAsync(600);
        }
        await TwoAnimationFramesAsync(Page);
    }

    private async Task ClickHeadersAsync()
    {
        await Page.EvaluateAsync("b => document.querySelector(b).click()", HeadersButton);
        await SettledAsync();
    }

    // Two clicks of a control in quick succession, on a diagram drawn in parts: the first's queue swaps the cached parts
    // in one task at a time, and the second's passed over the diagram as already drawn from its source (the old parts
    // were still first on the page), so the first's picture stayed on, with the headers the control said were hidden,
    // and the diagram's source said otherwise (5.2.5: 5.1.1 passed over a diagram drawn again meanwhile).
    [Fact]
    public async Task Two_clicks_of_a_control_in_quick_succession_leave_a_diagram_drawn_in_parts_as_the_second_says()
    {
        await OpenAsync(ReportTestHelper.GenerateReportWithWideLongNoteDiagram(TempDir, OutputDir, "QueueTwoClicks.html", 900, headerLine: true));
        await Page.EvaluateAsync("() => window._setReportDetails('expanded')");
        await SettledAsync();
        Assert.True(await Page.Locator($"{Diagram} > .puml-fragment").CountAsync() >= 2, "the expanded note has the diagram drawn in parts");
        var hidden = await Page.EvaluateAsync<bool>("() => !!window._headersHidden");
        await ClickHeadersAsync();
        await ClickHeadersAsync(); // back as it was: both pictures drawn and kept

        await Page.EvaluateAsync("b => { var button = document.querySelector(b); button.click(); button.click(); }", HeadersButton);
        await SettledAsync();

        var state = await Page.EvaluateAsync<bool[]>("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return [!!window._headersHidden, /X-Request/.test(c.textContent), /X-Request/.test(c.getAttribute('data-plantuml'))];
            }
            """);
        Assert.Equal(hidden, state[0]);
        Assert.Equal(!hidden, state[1]); // the picture
        Assert.Equal(!hidden, state[2]); // the source it says it was drawn from
    }

    // A folded note whose preview is all it shows has no + to open it while the headers are hidden. The Headers control
    // then draws the same source (a folded note shows its preview either way), the queue passes over the diagram, and the
    // note kept no + though it now has a header line to show (5.2.5: 5.1.1 passed over the buttons too).
    [Fact]
    public async Task A_control_that_draws_nothing_new_still_gives_a_notes_buttons_the_new_state()
    {
        await OpenAsync(ReportTestHelper.GenerateReportWithShortNoteAndHeader(TempDir, OutputDir, "QueueSkipButtons.html"));
        if (!await Page.EvaluateAsync<bool>("() => !!window._headersHidden")) await ClickHeadersAsync();
        await Page.EvaluateAsync("() => window._setReportDetails('collapsed')");
        await SettledAsync();
        const string plus = "() => document.querySelectorAll('[data-diagram-type=\"plantuml\"] [data-note-btn=\"plus\"]').length";
        Assert.Equal(0, await Page.EvaluateAsync<int>(plus)); // the preview is the whole note: no + to open it

        await ClickHeadersAsync();

        Assert.Equal(1, await Page.EvaluateAsync<int>(plus)); // with its header line shown, the folded note opens
    }

    // A diagram drawn whole while far from the screen waits to have its buttons made until it comes near; drawn again in
    // parts meanwhile, its parts wait for their own, and the diagram itself no longer waits: made for the whole diagram,
    // they went on its first part, with the whole diagram's notes (5.2.5: 5.1.1 left the diagram waiting).
    [Fact]
    public async Task A_diagram_drawn_again_in_parts_while_far_from_the_screen_waits_only_for_its_parts_buttons()
    {
        await OpenAsync(ReportTestHelper.GenerateReportWithWideLongNoteDiagram(TempDir, OutputDir, "QueueFarParts.html", 900, headerLine: true));
        await Page.EvaluateAsync("() => { var d = document.createElement('div'); d.style.height = '30000px'; document.body.appendChild(d); window.scrollTo(0, document.body.scrollHeight); }");
        await TwoAnimationFramesAsync(Page);
        await ClickHeadersAsync();
        Assert.True(await Page.EvaluateAsync<bool>($"() => !!document.querySelector('{Diagram}')._kDecorate"), "drawn whole far from the screen, the diagram waits for its buttons");

        await Page.EvaluateAsync("() => window._setReportDetails('expanded')");
        await SettledAsync();

        Assert.True(await Page.Locator($"{Diagram} > .puml-fragment").CountAsync() >= 2, "the expanded note has the diagram drawn in parts");
        Assert.False(await Page.EvaluateAsync<bool>($"() => !!document.querySelector('{Diagram}')._kDecorate"), "the diagram no longer waits for buttons of its own");
        Assert.True(await Page.EvaluateAsync<bool>($"() => Array.from(document.querySelectorAll('{Diagram} > .puml-fragment')).every(p => p._kDecorate)"), "its parts wait for theirs");
    }

    // The engine refusing a note's drawing (it threw): the note goes back to what it showed, and nothing is left running.
    // The poll for the drawing kept running and, half a minute later, put the note back again over anything done since
    // and said no drawing was running while one was (5.2.5: the catch never marked the drawing done).
    [Fact]
    public async Task A_note_drawing_the_engine_refuses_leaves_no_timer_running()
    {
        await OpenAsync(ReportTestHelper.GenerateReportWithWideLongNoteDiagram(TempDir, OutputDir, "NoteRedrawRefused.html", 30));
        var running = await Page.EvaluateAsync<int>("""
            async () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                c.querySelector('.note-hover-rect').dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }));
                for (var i = 0; i < 50 && !c.querySelector("[data-note-btn='expand-bottom'] rect"); i++) await new Promise(r => setTimeout(r, 100));
                var live = new Set(), si = window.setInterval, ci = window.clearInterval, render = window.plantuml.render;
                window.setInterval = function() { var id = si.apply(window, arguments); live.add(id); return id; };
                window.clearInterval = function(id) { live.delete(id); return ci.apply(window, arguments); };
                window.plantuml.render = function() { throw new Error('refused'); };
                try {
                    c.querySelector("[data-note-btn='expand-bottom'] rect").dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                } finally {
                    window.plantuml.render = render;
                }
                await new Promise(r => setTimeout(r, 1500));
                window.setInterval = si;
                window.clearInterval = ci;
                return live.size;
            }
            """);
        Assert.Equal(0, running);
    }

    // Every diagram a control draws again is asked for its zoom before its picture is replaced. A box that is not zoomed
    // has nothing to give, and reading its width and scroll in a scenario far from the screen lays that scenario out
    // (5.2.5: 5.2.3 read both for every diagram, the cost 5.1.1 took out of a control's redraw).
    [Fact]
    public async Task Reading_the_zoom_of_a_diagram_that_is_not_zoomed_reads_no_layout()
    {
        await OpenAsync(ReportTestHelper.GenerateReportWithWideLongNoteDiagram(TempDir, OutputDir, "ZoomStateNoLayout.html", 30));
        var reads = await Page.EvaluateAsync<string[]>("""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]'), seen = [];
                var names = ['clientWidth', 'clientHeight', 'scrollLeft', 'offsetWidth', 'offsetHeight'];
                names.forEach(function(name) {
                    var proto = name.indexOf('offset') === 0 ? HTMLElement.prototype : Element.prototype;
                    var d = Object.getOwnPropertyDescriptor(proto, name);
                    Object.defineProperty(c, name, { configurable: true,
                        get: function() { seen.push(name); return d.get.call(this); },
                        set: d.set ? function(v) { d.set.call(this, v); } : undefined });
                });
                var getRect = c.getBoundingClientRect;
                c.getBoundingClientRect = function() { seen.push('getBoundingClientRect'); return getRect.call(this); };
                window._zoomState.of(c);
                names.forEach(function(name) { delete c[name]; });
                delete c.getBoundingClientRect;
                return seen;
            }
            """);
        Assert.Empty(reads);
    }
}
