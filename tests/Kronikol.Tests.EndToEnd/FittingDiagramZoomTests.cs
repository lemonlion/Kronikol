using Kronikol.Reports;
using Microsoft.Playwright;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Every drawn diagram zooms, and one that fits its box zooms out (<c>plans/ZOOM_EVERY_DIAGRAM_PLAN.md</c> R1, facts 1
/// to 6 and 8 to 12 of section 6.2). Until 5.7.0 a diagram got a slider only when its picture was wider than its box, and
/// Ctrl with the wheel over any other diagram was left to the browser, which zoomed the whole page: on a report with a
/// wide diagram followed by one that fits, the wheel zoomed the first diagram and then the page.
/// </summary>
[Collection(PlaywrightCollections.Zoom)]
public class FittingDiagramZoomTests : PlaywrightTestBase
{
    public FittingDiagramZoomTests(PlaywrightFixture fixture) : base(fixture) { }

    private const int Wide = 0, Fits = 1;

    /// <summary>The owner's case: the first diagram wide, the second fitting its box, both drawn with their sliders.</summary>
    private async Task OpenWideThenFittingAsync(string fileName)
    {
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithWideThenFittingDiagram(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await ZoomFacts.WaitForSliderAsync(Page, Wide);
        await ZoomFacts.WaitForDrawnAsync(Page, Fits);
        await Page.WaitForTimeoutAsync(300);
    }

    // 1
    [Fact]
    public async Task A_diagram_that_fits_has_a_slider_at_100_whose_lowest_level_is_the_floor()
    {
        await OpenWideThenFittingAsync("FitsSlider.html");

        var d = await ZoomFacts.ReadAsync(Page, Fits);
        Assert.True(d.Slider >= 0, "a diagram that fits its box has a slider");
        Assert.Equal(100, d.Slider);
        Assert.Equal(50, d.Min);
        Assert.Equal(100, d.Max);
        Assert.False(d.In || d.Out, "at 100% a diagram that fits is zoomed neither way");
    }

    // 2
    [Fact]
    public async Task Ctrl_wheel_down_over_a_diagram_that_fits_shrinks_it_in_the_middle_of_its_box()
    {
        await OpenWideThenFittingAsync("FitsWheelOut.html");
        var before = await ZoomFacts.ReadAsync(Page, Fits);
        var (x, y) = await ZoomFacts.PointInAsync(Page, Fits);
        await ZoomFacts.WatchWheelAsync(Page);

        await ZoomFacts.CtrlWheelAsync(Page, x, y, -2);

        var after = await ZoomFacts.ReadAsync(Page, Fits);
        Assert.True(after.Widths[0] < before.Widths[0] - 1, $"the picture shrinks: {before.Widths[0]:F1} -> {after.Widths[0]:F1}");
        Assert.True(after.Slider < 100, $"the slider follows: {after.Slider}");
        Assert.True(after.Out, "the diagram is marked zoomed out");
        Assert.True(Math.Abs(after.Centres[0]) <= 1, $"the picture stays in the middle of its box ({after.Centres[0]:F1} px off)");
        var wheels = await ZoomFacts.WheelsPreventedAsync(Page);
        Assert.NotEmpty(wheels);
        Assert.All(wheels, p => Assert.True(p, "the page's zoom is not asked"));
    }

    // 3
    [Fact]
    public async Task Ctrl_wheel_up_over_a_diagram_that_fits_at_100_is_kept_from_the_browser_and_stays_at_100()
    {
        await OpenWideThenFittingAsync("FitsWheelInAtTop.html");
        var before = await ZoomFacts.ReadAsync(Page, Fits);
        var (x, y) = await ZoomFacts.PointInAsync(Page, Fits);
        await ZoomFacts.WatchWheelAsync(Page);

        await ZoomFacts.CtrlWheelAsync(Page, x, y, 2);

        var after = await ZoomFacts.ReadAsync(Page, Fits);
        Assert.Equal(100, after.Slider);
        Assert.Equal(before.Widths[0], after.Widths[0], 1);
        var prevented = await ZoomFacts.WheelsPreventedAsync(Page);
        Assert.NotEmpty(prevented);
        Assert.All(prevented, p => Assert.True(p, "the wheel over a diagram is the diagram's, at the end of its range too"));
    }

    // 4
    [Fact]
    public async Task Ctrl_minus_and_plus_on_a_selected_diagram_that_fits_shrink_it_and_bring_it_back_to_100_and_no_further()
    {
        await OpenWideThenFittingAsync("FitsKeys.html");
        var natural = (await ZoomFacts.ReadAsync(Page, Fits)).Widths[0];
        var (x, y) = await ZoomFacts.PointInAsync(Page, Fits);
        await Page.Mouse.ClickAsync(x, y);
        Assert.True((await ZoomFacts.ReadAsync(Page, Fits)).Selected);

        for (var i = 0; i < 3; i++) await Page.Keyboard.PressAsync("Control+-");
        await TwoAnimationFramesAsync(Page);
        var shrunk = await ZoomFacts.ReadAsync(Page, Fits);
        Assert.True(shrunk.Slider < 100 && shrunk.Widths[0] < natural - 1, $"Ctrl and - shrink it: {shrunk.Slider}, {shrunk.Widths[0]:F1} of {natural:F1}");

        for (var i = 0; i < 6; i++) await Page.Keyboard.PressAsync("Control+=");
        await TwoAnimationFramesAsync(Page);
        var back = await ZoomFacts.ReadAsync(Page, Fits);
        Assert.Equal(100, back.Slider);
        Assert.Equal(natural, back.Widths[0], 1);
        Assert.False(back.In || back.Out);
    }

    // 5
    [Fact]
    public async Task A_diagram_zoomed_out_has_no_grab_cursor_no_scroll_and_no_pan_and_a_drag_selects_its_text()
    {
        await OpenWideThenFittingAsync("FitsNoPan.html");
        await ZoomFacts.SetLevelAsync(Page, Fits, 70);
        var d = await ZoomFacts.ReadAsync(Page, Fits);
        Assert.True(d.Out);
        // the grab cursor is the page's style for the picture under the pointer of a diagram zoomed in (5.7.0), so it is
        // read there, hovered; the wide diagram zoomed in shows that the reading sees it (until 5.7.3 this read the box,
        // which no rule styles, and could not fail)
        await ZoomFacts.SetLevelAsync(Page, Wide, 100);
        var (wx, wy) = await ZoomFacts.PointInAsync(Page, Wide);
        await Page.Mouse.MoveAsync(wx, wy);
        Assert.Equal("grab", await Page.EvaluateAsync<string>("() => getComputedStyle(document.querySelectorAll(\"[data-diagram-type='plantuml']\")[0].querySelector('svg')).cursor"));
        var (fx, fy) = await ZoomFacts.PointInAsync(Page, Fits);
        await Page.Mouse.MoveAsync(fx, fy);
        Assert.NotEqual("grab", await Page.EvaluateAsync<string>("() => getComputedStyle(document.querySelectorAll(\"[data-diagram-type='plantuml']\")[1].querySelector('svg')).cursor"));
        Assert.True(d.ScrollWidth <= d.ClientWidth, $"no scroll: {d.ScrollWidth} against {d.ClientWidth}");

        // a drag across the first message's label
        var label = await Page.EvaluateAsync<float[]>("""
            () => {
                const c = document.querySelectorAll("[data-diagram-type='plantuml']")[1];
                const t = Array.from(c.querySelectorAll('svg text')).find(e => e.textContent.includes('POST'));
                t.scrollIntoView({ block: 'center' });
                const r = t.getBoundingClientRect();
                return [r.left - 2, r.top + r.height / 2, r.right + 2];
            }
            """);
        await TwoAnimationFramesAsync(Page);
        await Page.Mouse.MoveAsync(label[0], label[1]);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(label[2], label[1], new() { Steps = 8 });
        await Page.Mouse.UpAsync();

        var after = await ZoomFacts.ReadAsync(Page, Fits);
        Assert.Equal(0, after.ScrollLeft);
        var selected = await Page.EvaluateAsync<string>("() => window.getSelection().toString()");
        Assert.Contains("POST", selected);
    }

    // 6
    [Fact]
    public async Task Ctrl_wheel_over_the_diagram_that_fits_after_a_wide_one_zooms_that_diagram_alone_and_never_the_page()
    {
        await OpenWideThenFittingAsync("FitsOwnersCase.html");
        var wideBefore = await ZoomFacts.ReadAsync(Page, Wide);
        var (x, y) = await ZoomFacts.PointInAsync(Page, Fits);
        await ZoomFacts.WatchWheelAsync(Page);

        await ZoomFacts.CtrlWheelAsync(Page, x, y, -3);

        var wideAfter = await ZoomFacts.ReadAsync(Page, Wide);
        var fits = await ZoomFacts.ReadAsync(Page, Fits);
        Assert.True(fits.Slider < 100, $"the second diagram zooms out: {fits.Slider}");
        Assert.Equal(wideBefore.Slider, wideAfter.Slider);
        Assert.Equal(wideBefore.Widths[0], wideAfter.Widths[0], 1);
        var prevented = await ZoomFacts.WheelsPreventedAsync(Page);
        Assert.Equal(3, prevented.Length);
        Assert.All(prevented, p => Assert.True(p, "the page's zoom is not asked"));
    }

    // A diagram that fits its box in every state the facts below put it in: three participants, a note with a header line
    // (the Headers control draws it again) and a note of twenty short lines, cut at the line limit until it is expanded.
    private const string FittingWithNotes = """
        @startuml
        !pragma teoz true
        autonumber 1
        actor "Caller" as caller
        participant "OrderService" as svc
        database "Database" as db
        caller -> svc : POST /api/orders
        note left
        <color:gray>[Content-Type=application/json]

        {"item":"Widget","qty":2}
        end note
        svc -> db : INSERT INTO Orders
        db --> svc : OK
        svc --> caller : 201 Created
        note right
        {
          "line01": 1,
          "line02": 2,
          "line03": 3,
          "line04": 4,
          "line05": 5,
          "line06": 6,
          "line07": 7,
          "line08": 8,
          "line09": 9,
          "line10": 10,
          "line11": 11,
          "line12": 12,
          "line13": 13,
          "line14": 14,
          "line15": 15,
          "line16": 16,
          "line17": 17,
          "line18": 18
        }
        end note
        @enduml
        """;

    private async Task OpenSourceAsync(string fileName, string source)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios = [new() { Id = "s1", DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([new DiagramAsCode("s1", "", source)],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await ZoomFacts.WaitForSliderAsync(Page, 0);
    }

    /// <summary>Runs <paramref name="redraw"/> and waits for diagram 0 drawn again, with its slider.</summary>
    private async Task RedrawAsync(string redraw)
    {
        await Page.EvaluateAsync("() => { window.__before = document.querySelector(\"[data-diagram-type='plantuml'] svg\"); }");
        await Page.EvaluateAsync(redraw);
        await Page.WaitForFunctionAsync("() => !document.contains(window.__before)", null, new() { Timeout = 60000, PollingInterval = 200 });
        await ZoomFacts.WaitForSliderAsync(Page, 0);
    }

    private const string ToggleHeaders = "() => document.querySelector(\"button[data-toggle='headers'][onclick*='_toggleHeaders(']\").click()";

    private const string ExpandTheLongNote = """
        async () => {
            const c = document.querySelector("[data-diagram-type='plantuml']");
            const rects = c.querySelectorAll('.note-hover-rect');
            rects[rects.length - 1].dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }));
            for (let i = 0; i < 50 && !c.querySelector("[data-note-btn='expand-bottom'] rect"); i++) await new Promise(r => setTimeout(r, 100));
            c.querySelector("[data-note-btn='expand-bottom'] rect").dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
        }
        """;

    // 8
    [Fact]
    public async Task A_zoomed_out_level_is_kept_when_the_headers_a_notes_button_and_a_report_control_draw_the_diagram_again()
    {
        await OpenSourceAsync("FitsKeptOnRedraw.html", FittingWithNotes);
        Assert.Equal(100, (await ZoomFacts.ReadAsync(Page, 0)).Slider);
        await ZoomFacts.SetLevelAsync(Page, 0, 70);

        foreach (var (redraw, by) in new[]
                 {
                     (ToggleHeaders, "the Headers control"),
                     (ExpandTheLongNote, "a note's button"),
                     ("() => window._setReportDetails('collapsed')", "the Details control")
                 })
        {
            await RedrawAsync(redraw);
            var d = await ZoomFacts.ReadAsync(Page, 0);
            Assert.True(d.Out, $"drawn again by {by}, the diagram is still zoomed out");
            Assert.Equal(70, d.Slider);
            Assert.True(Math.Abs(d.Levels[0] - 70) <= 1, $"drawn again by {by}, its picture is drawn at {d.Levels[0]:F1}%");
        }
    }

    // 8, frame by frame: the level is given back in the task that puts the new picture in, so no frame shows it at full size
    [Fact]
    public async Task A_zoomed_out_diagram_drawn_again_is_at_its_level_in_the_first_frame_that_shows_its_new_picture()
    {
        await OpenSourceAsync("FitsKeptFirstFrame.html", FittingWithNotes);
        await ZoomFacts.SetLevelAsync(Page, 0, 70);

        foreach (var (redraw, by) in new[] { (ToggleHeaders, "the Headers control"), ("() => window._setReportDetails('collapsed')", "the Details control") })
        {
            // watched from a frame callback asked for inside the last one, so it runs before any the page asks for once
            // the new picture is in
            await Page.EvaluateAsync("""
                () => {
                    const c = document.querySelector("[data-diagram-type='plantuml']");
                    c.querySelectorAll('svg').forEach(s => { s._before = true; });
                    window.__firstWidth = null;
                    const watch = () => {
                        const s = c.querySelector(':scope > svg, :scope > .puml-fragment > svg');
                        if (s && !s._before && s.getBoundingClientRect().width > 0) { window.__firstWidth = s.getBoundingClientRect().width; return; }
                        requestAnimationFrame(watch);
                    };
                    requestAnimationFrame(watch);
                }
                """);
            await RedrawAsync(redraw);
            var widths = await Page.EvaluateAsync<double[]>("""
                () => [window.__firstWidth || 0,
                       document.querySelector("[data-diagram-type='plantuml']").querySelector(':scope > svg, :scope > .puml-fragment > svg').getBoundingClientRect().width]
                """);
            Assert.True(widths[0] > 0, $"drawn again by {by}, a frame showed the new picture");
            Assert.True(Math.Abs(widths[0] - widths[1]) <= 1,
                $"drawn again by {by}, the first frame showed the picture {widths[0]:F1} px wide, and it settled at {widths[1]:F1} px");
            Assert.Equal(70, (await ZoomFacts.ReadAsync(Page, 0)).Slider);
        }
    }

    // A slider made again at the level its picture is drawn at (every redraw makes one, and every drawn diagram has one
    // now) changes no size: the browser's scroll anchoring is left on and the page is not scrolled (SplitDiagramZoomTests
    // has the same for a diagram in parts)
    [Fact]
    public async Task A_slider_made_again_at_the_level_its_picture_is_drawn_at_leaves_the_scroll_alone()
    {
        await OpenSourceAsync("FitsAnchoring.html", FittingWithNotes);
        foreach (var level in new[] { 100, 70 })
        {
            await ZoomFacts.SetLevelAsync(Page, 0, level);
            var r = await Page.EvaluateAsync<string>("""
                () => new Promise(done => requestAnimationFrame(() => setTimeout(() => {
                    const c = document.querySelector("[data-diagram-type='plantuml']");
                    const y = scrollY;
                    window._addZoomButton(c);
                    done(document.documentElement.style.overflowAnchor + '|' + (scrollY - y));
                }, 0)))
                """);
            Assert.True(r == "|0", $"at {level}, made again: overflow-anchor|scrolled = {r}");
        }
    }

    // 9
    [Fact]
    public async Task A_zoomed_out_diagram_drawn_again_wider_than_its_box_starts_at_its_fit_level()
    {
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithNarrowDiagramAndWideNote(TempDir, OutputDir, "FitsThenWide.html", noteLines: 10, callPairs: 3));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await ZoomFacts.WaitForSliderAsync(Page, 0);
        // folded to its preview, the wide note leaves a diagram that fits
        await RedrawAsync("() => window._setReportDetails('collapsed')");
        var folded = await ZoomFacts.ReadAsync(Page, 0);
        Assert.Equal(50, folded.Min);
        // zoomed out a little, to a level above the fit level the picture will have: kept, the level would show
        await ZoomFacts.SetLevelAsync(Page, 0, 95);
        var zoomedOut = await ZoomFacts.ReadAsync(Page, 0);
        Assert.True(zoomedOut.Out && zoomedOut.Slider == 95, $"premise: zoomed out to 95, the slider at {zoomedOut.Slider}");

        await RedrawAsync("() => window._setReportDetails('truncated')");

        var d = await ZoomFacts.ReadAsync(Page, 0);
        Assert.True(d.Min < 95, $"premise: the note shown, the diagram is wide, with a fit level below 95: its slider runs from {d.Min}");
        Assert.Equal(d.Min, d.Slider);
        Assert.False(d.In, "a zoom out is never turned into a zoom in");
        Assert.False(d.Out, "a wide picture is not zoomed out");
        Assert.True(Math.Abs(d.Levels[0] - d.Min) <= 1, $"the picture is drawn at its fit level: {d.Levels[0]:F1}% against {d.Min}");
    }

    // 10
    [Fact]
    public async Task A_diagram_that_fits_in_a_box_not_laid_out_gets_its_slider_at_100_when_it_shows()
    {
        await Page.GotoReportAsync(GenerateReport("FitsNotLaidOut.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await ZoomFacts.WaitForDrawnAsync(Page, 0);

        var result = await Page.EvaluateAsync<string>("""
            () => {
                const c = document.querySelector("[data-diagram-type='plantuml']");
                c.querySelectorAll('.diagram-zoom-controls').forEach(z => z.remove());
                const wrap = c.parentElement;
                wrap.style.display = 'none';
                window._addZoomButton(c);
                const hidden = c.querySelectorAll('.diagram-zoom-controls').length;
                wrap.style.display = '';
                window._addZoomButton(c);
                const s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                return hidden + '|' + (s ? s.value + '|' + s.min : 'none');
            }
            """);

        Assert.Equal("0|100|50", result);
    }

    // 11, the Components tab: a diagram of two services fits the page
    [Fact]
    public async Task A_component_diagram_that_fits_has_a_slider_at_100()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var reportsDir = Path.Combine(TempDir, "components-fits-" + tag);
        Directory.CreateDirectory(reportsDir);
        var scenario = "cf-pays-" + tag;
        var (shop, pay) = ("Shop" + tag, "Payments" + tag);
        lock (ReportTestHelper.WholePipeline)
        {
            DefaultDiagramsFetcher.Reset();
            Kronikol.Tracking.RequestResponseLogger.LogPair("Pays", scenario, HttpMethod.Post, new Uri("http://payments/pay"), pay, shop, statusCode: System.Net.HttpStatusCode.OK);
            ReportGenerator.CreateStandardReportsWithDiagrams(
                [new Feature { DisplayName = "Shop", Scenarios = [new Scenario { Id = scenario, DisplayName = "Pays", Result = ExecutionResult.Passed }] }],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
                new ReportConfigurationOptions
                {
                    ReportsFolderPath = reportsDir,
                    ComponentDiagramOptions = new Kronikol.ComponentDiagram.ComponentDiagramOptions { ParticipantFilter = n => n == shop || n == pay },
                    GenerateSpecificationsReport = false,
                    GenerateSpecificationsData = false,
                });
            DefaultDiagramsFetcher.Reset();
        }
        var report = Path.Combine(reportsDir, "TestRunReport.html");
        File.Copy(report, Path.Combine(OutputDir, "ComponentsFits.html"), true);
        await Page.GotoReportAsync(new Uri(report).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await OpenReportTabAsync("components");

        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector('#component-diagram .plantuml-browser > .diagram-zoom-controls .diagram-zoom-slider')",
            null, new() { Timeout = 60000, PollingInterval = 200 });
        var slider = await Page.EvaluateAsync<int[]>("""
            () => {
                const c = document.querySelector('#component-diagram .plantuml-browser');
                const s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                return [parseInt(s.value), parseInt(s.min), c.querySelector(':scope > svg').width.baseVal.value <= c.clientWidth ? 1 : 0];
            }
            """);
        Assert.Equal(1, slider[2]);
        Assert.Equal(100, slider[0]);
        Assert.Equal(50, slider[1]);
    }

    // 11, the internal-flow popup's activity diagram
    [Fact]
    public async Task An_activity_diagram_in_the_internal_flow_popup_has_a_slider()
    {
        // the page with the diagram menu's script, which makes the zoom slider
        await Page.GotoReportAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeContextMenu: true)));
        await Page.Locator("#trigger-seg-1").ClickAsync();
        await Page.Locator(".iflow-diagram svg").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = PopupFirstDrawTimeout });

        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector('.iflow-popup .iflow-diagram > .diagram-zoom-controls .diagram-zoom-slider')",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        var slider = await Page.EvaluateAsync<int[]>("""
            () => {
                const s = document.querySelector('.iflow-popup .iflow-diagram > .diagram-zoom-controls .diagram-zoom-slider');
                return [parseInt(s.value), parseInt(s.min), parseInt(s.max)];
            }
            """);
        Assert.Equal(100, slider[2]);
        Assert.True(slider[1] is > 0 and <= 100, $"its slider runs from {slider[1]}");
    }

    // 12
    [Fact]
    public async Task Copy_as_PNG_of_a_zoomed_out_diagram_gives_the_picture_at_its_natural_size()
    {
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await OpenWideThenFittingAsync("FitsCopyPng.html");
        var natural = await Page.EvaluateAsync<double[]>("""
            () => {
                const svg = document.querySelectorAll("[data-diagram-type='plantuml']")[1].querySelector(':scope > svg');
                return [svg.width.baseVal.value, svg.height.baseVal.value];
            }
            """);
        await ZoomFacts.SetLevelAsync(Page, Fits, 60);
        var zoomedOut = await ZoomFacts.ReadAsync(Page, Fits);
        Assert.True(zoomedOut.Out && Math.Abs(zoomedOut.Levels[0] - 60) <= 1, $"premise: drawn at 60%, not {zoomedOut.Levels[0]:F1}%");
        await Page.EvaluateAsync("() => navigator.clipboard.writeText('nothing copied yet')");

        await Page.EvaluateAsync("""
            () => {
                const svg = document.querySelectorAll("[data-diagram-type='plantuml']")[1].querySelector(':scope > svg');
                const r = svg.getBoundingClientRect();
                svg.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: r.left + 8, clientY: r.top + 8 }));
            }
            """);
        var menu = Page.Locator(".diagram-ctx-menu");
        await menu.WaitForAsync(new() { Timeout = 5000 });
        var parent = menu.Locator(".submenu-parent", new() { HasTextRegex = new System.Text.RegularExpressions.Regex("^Copy image") }).First;
        await parent.HoverAsync();
        await parent.Locator(".submenu").GetByText("Copy as PNG", new() { Exact = true }).First.ClickAsync();

        var size = await Page.EvaluateAsync<int[]>("""
            async () => {
                let item = null;
                for (let k = 0; k < 50 && !item; k++) {
                    item = (await navigator.clipboard.read()).find(i => i.types.includes('image/png')) || null;
                    if (!item) await new Promise(res => setTimeout(res, 200));
                }
                if (!item) return [0, 0];
                const bmp = await createImageBitmap(await item.getType('image/png'));
                return [bmp.width, bmp.height];
            }
            """);
        Assert.True(Math.Abs(size[0] - 2 * natural[0]) <= 2, $"the image is twice the natural width: {size[0]} for {natural[0]}");
        Assert.True(Math.Abs(size[1] - 2 * natural[1]) <= 2, $"and twice the natural height: {size[1]} for {natural[1]}");
    }
}
