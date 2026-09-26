using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Captured text drawn and copied as captured, in the render worker (3.30.1). PlantUML's preprocessor reads a
/// note before creole does: a line opening with <c>'</c> vanished, <c>/'</c> or a line that is only <c>{{</c>
/// broke the diagram, <c>!define</c> ran, <c>%date()</c> painted the date, a literal <c>~</c> was eaten, and a
/// line opening with <c>=</c>, <c>|</c> or <c>..</c> was restyled. The generator writes each as a code point;
/// copy and the YAML view read the code points back.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class CapturedTextEscapeTests : PlaywrightTestBase
{
    public CapturedTextEscapeTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task OpenReport(string uri)
    {
        await Page.GotoAsync(uri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync(BrowserRenderWorkerTests.AllRenderedJs, null,
            new() { Timeout = 60_000, PollingInterval = 200 });
    }

    /// <summary>Every diagram's painted text, whitespace collapsed and zero-width spaces dropped, in page order.</summary>
    private Task<string[]> PaintedTexts() => Page.EvaluateAsync<string[]>("""
        () => Array.from(document.querySelectorAll('.plantuml-browser')).map(el =>
            Array.from(el.querySelectorAll('svg text')).map(t => t.textContent).join(' ')
                .replace(/\u200b/g, '').replace(/\s+/g, ' '))
        """);

    private static string Collapse(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    private async Task<string> CopyBoxTextOfDiagramPainting(string marker)
    {
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        var index = await Page.EvaluateAsync<int>("""
            m => Array.from(document.querySelectorAll('.plantuml-browser')).findIndex(el =>
                Array.from(el.querySelectorAll('svg text')).some(t => (t.textContent || '').indexOf(m) >= 0))
            """, marker);
        Assert.True(index >= 0, $"no diagram paints {marker}");
        await Page.WaitForFunctionAsync(
            "i => document.querySelectorAll('.plantuml-browser')[i].querySelector('.note-hover-rect')", index,
            new() { Timeout = 20_000, PollingInterval = 200 });
        await DispatchContextMenu(Page.Locator(".plantuml-browser").Nth(index).Locator(".note-hover-rect").First);
        var item = Page.Locator(".diagram-ctx-menu").GetByText("Copy box text");
        await item.WaitForAsync(new() { Timeout = 5000 });
        await item.ClickAsync();
        return (await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()")).Replace("\r\n", "\n");
    }

    [Fact]
    public async Task A_collapsed_notes_tooltip_reads_the_captured_lines()
    {
        // The tooltip of a collapsed note was built from the note's source with only the header tag removed, so it showed
        // the escapes (<U+0027>a', 'b') and the width bound's join markers (DIAGRAM_COLOURS_PLAN §12.5).
        await OpenReport(ReportTestHelper.GenerateReportWithCapturedTextHazards(TempDir, OutputDir, "CapturedTextHazards_Tooltip.html"));

        var collapse = Page.Locator(".diagram-toggle .details-radio-btn[data-state='collapsed']").First;
        await collapse.ClickAsync();
        await Page.WaitForFunctionAsync(
            "() => Array.from(document.querySelectorAll('.plantuml-browser svg path > title')).some(t => (t.textContent || '').indexOf('not a comment') >= 0)",
            null, new() { Timeout = 30_000, PollingInterval = 200 });

        var tooltip = await Page.EvaluateAsync<string>(
            "() => Array.from(document.querySelectorAll('.plantuml-browser svg path > title')).map(t => t.textContent).find(t => t.indexOf('not a comment') >= 0)");
        Assert.DoesNotContain("<U+", tooltip, StringComparison.Ordinal);
        Assert.Contains("'a', 'b'", tooltip, StringComparison.Ordinal);
        Assert.Contains("/' not a comment", tooltip, StringComparison.Ordinal);
        Assert.Contains("!define FOO BAR", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_diagram_is_drawn_and_paints_the_captured_lines()
    {
        await OpenReport(ReportTestHelper.GenerateReportWithCapturedTextHazards(TempDir, OutputDir, "CapturedTextHazards_Drawn.html"));

        var states = await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('.plantuml-browser')).map(el =>
                (el.querySelector('svg') ? 'svg' : 'none') + (el.querySelector('.engine-failure') ? ':failure' : ''))
            """);
        Assert.Equal(["svg", "svg", "svg"], states);
        var texts = await PaintedTexts();
        var hazards = Assert.Single(texts, t => t.Contains("trailing backslash", StringComparison.Ordinal));
        Assert.DoesNotContain("Syntax Error", hazards, StringComparison.Ordinal);
        foreach (var line in ReportTestHelper.CapturedTextHazardLines)
            Assert.Contains(Collapse(line), hazards, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copy_box_text_returns_the_captured_lines()
    {
        await OpenReport(ReportTestHelper.GenerateReportWithCapturedTextHazards(TempDir, OutputDir, "CapturedTextHazards_Copy.html"));

        var clipboard = await CopyBoxTextOfDiagramPainting("trailing");

        Assert.DoesNotContain("U+", clipboard, StringComparison.Ordinal);
        Assert.DoesNotContain("\u200b", clipboard, StringComparison.Ordinal);
        Assert.Contains(string.Join("\n", ReportTestHelper.CapturedTextHazardLines), clipboard, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_yaml_view_paints_and_copies_the_captured_text()
    {
        await OpenReport(ReportTestHelper.GenerateReportWithCapturedTextHazards(TempDir, OutputDir, "CapturedTextHazards_Yaml.html", NotePayloadFormat.Yaml));

        string[] values = ["'a',", "!important", "%upper(x)", "end note", "= heading", "~/.bashrc", "&#39;", "a << b >> c"];
        var yaml = Assert.Single(await PaintedTexts(), t => t.Contains("SELECT", StringComparison.Ordinal));
        foreach (var value in values)
            Assert.Contains(value, yaml, StringComparison.Ordinal);

        var clipboard = await CopyBoxTextOfDiagramPainting("SELECT");
        Assert.DoesNotContain("U+", clipboard, StringComparison.Ordinal);
        foreach (var value in values)
            Assert.Contains(value, clipboard, StringComparison.Ordinal);
    }

    // ── What else reaches the engine as captured (3.30.2) ──────────────────────────────

    /// <summary>Renders <paramref name="source"/> alone in the worker and returns its painted lines (text sharing a baseline).</summary>
    private async Task<string[]> RenderAlone(string source)
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateBrowserJsPage(("d1", source))));
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync("() => { const el = document.getElementById('d1'); return el && el.dataset.rendered === '1' && el.querySelector('svg'); }",
            null, new() { Timeout = 60_000, PollingInterval = 200 });
        return await Page.EvaluateAsync<string[]>("""
            () => {
                const rows = new Map();
                for (const t of document.querySelectorAll('#d1 svg text')) {
                    const y = Math.round(parseFloat(t.getAttribute('y')));
                    if (!rows.has(y)) rows.set(y, []);
                    rows.get(y).push([parseFloat(t.getAttribute('x')), t.textContent]);
                }
                return [...rows.keys()].sort((a, b) => a - b).map(y => rows.get(y).sort((a, b) => a[0] - b[0]).map(p => p[1]).join(' ')
                    .replace(/\u200b/g, '').replace(/\u00a0/g, ' ').replace(/\s+/g, ' ').trim());
            }
            """);
    }

    private static void AssertNoErrorPicture(string[] painted) =>
        Assert.DoesNotContain(painted, l => l.StartsWith("PlantUML ", StringComparison.Ordinal) || l.Contains("Syntax Error", StringComparison.Ordinal));

    /// <summary>A doc string holding every hazard a step bar's body can meet, one per display line.</summary>
    private static readonly string[] StepBarLines =
    [
        "~/.bashrc and \"~\" and ~~wave~~",
        "%date() and %upper(\"x\") stay",
        "it&#39;s",
        "..not a separator..",
        "....",
        "= not a heading",
        "a << b >> c",
        "{{",
        "trailing backslash \\",
    ];

    private static readonly string[] StepBarCells = ["~/.bashrc", "%date()", "&#39;", "~~w~~"];

    [Fact]
    public async Task A_step_bars_doc_string_and_table_cells_are_painted_as_written()
    {
        // A bar's body had a lighter escaper than a payload's: the engine ate a `~`, painted the date for `%date()`, a `'`
        // for `&#39;`, a separator for `..x..`, a rule for `....`, and `{{` alone swallowed the bar (to 3.30.1).
        var bar = Kronikol.PlantUml.StepBarPlantUml.Build("Given hazards",
            [new Kronikol.PlantUml.StepBarTable(null, [["Col"], .. StepBarCells.Select(c => new[] { c })])],
            string.Join("\n", StepBarLines));

        var painted = await RenderAlone(BarDiagram(bar));

        AssertNoErrorPicture(painted);
        foreach (var line in StepBarLines.Concat(StepBarCells))
            Assert.Contains(Collapse(line), painted);
    }

    [Fact]
    public async Task The_render_error_placeholder_draws_its_note()
    {
        // `hnote across` spans every lifeline and the placeholder declared none, so the worker drew its syntax-error
        // picture, listing the placeholder's source, where the red note belonged (2026-08-22 to 3.30.1).
        var painted = await RenderAlone(DefaultDiagramsFetcher.RenderErrorPlantUml(new TimeoutException("no answer")));

        AssertNoErrorPicture(painted);
        Assert.Contains(painted, l => l.Contains("diagram could not be generated: TimeoutException: no answer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_wikis_bearer_recipe_as_a_mid_processor_paints_no_part_of_a_token_that_holds_a_tilde()
    {
        // 3.30.1 escaped the body before the mid-processor saw it, so the recipe stopped at the token's first `~`
        // (written `<U+007E>`) and the rest of the token was drawn.
        var pair = Guid.NewGuid();
        Kronikol.Tracking.RequestResponseLog Log(Kronikol.Tracking.RequestResponseType type, string body) =>
            new("t1", "t1", "POST", body, new Uri("http://localhost/api/echo"), [("Content-Type", "application/json")], "EchoService", "Caller",
                type, Guid.NewGuid(), pair, TrackingIgnore: false,
                StatusCode: type == Kronikol.Tracking.RequestResponseType.Response ? System.Net.HttpStatusCode.OK : null);
        var source = Kronikol.PlantUml.PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
                [Log(Kronikol.Tracking.RequestResponseType.Request, """{"echo":"Bearer abc~def~ghi"}"""),
                 Log(Kronikol.Tracking.RequestResponseType.Response, """{"ok":true}""")],
                requestMidFormattingProcessor: c => System.Text.RegularExpressions.Regex.Replace(c, @"Bearer [A-Za-z0-9\-._~+/]+=*", "Bearer ***"))
            .Single().PlantUmls.Single().PlainText;

        var painted = await RenderAlone(source);

        AssertNoErrorPicture(painted);
        Assert.Contains(painted, l => l.Contains("\"echo\": \"Bearer ***\"", StringComparison.Ordinal));
        Assert.DoesNotContain(painted, l => l.Contains("def", StringComparison.Ordinal) || l.Contains("ghi", StringComparison.Ordinal));
    }

    /// <summary>The PlantUML Kronikol writes for a scenario whose step bar is <paramref name="bar"/>, then one call.</summary>
    private static string BarDiagram(string bar)
    {
        Kronikol.Tracking.RequestResponseLog Marker(bool start) => new(
            TestName: "Escapes", TestId: "escapes-1", Method: "", Content: "", Uri: new Uri("http://override.com"), Headers: [],
            ServiceName: "", CallerName: "", Type: Kronikol.Tracking.RequestResponseType.Request, TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(), TrackingIgnore: false)
        {
            IsOverrideStart = start, IsOverrideEnd = !start, MarkerKind = Kronikol.Tracking.DiagramMarkerKind.Step,
            PlantUml = start ? "\n" + bar + "\n\n\n" : null,
        };
        var pair = Guid.NewGuid();
        Kronikol.Tracking.RequestResponseLog Call(Kronikol.Tracking.RequestResponseType type) =>
            new("Escapes", "escapes-1", "GET", "{}", new Uri("http://example.com/api/orders"), [("Content-Type", "application/json")],
                "Orders API", "Caller", type, Guid.NewGuid(), pair, TrackingIgnore: false,
                StatusCode: type == Kronikol.Tracking.RequestResponseType.Response ? System.Net.HttpStatusCode.OK : null);
        return Kronikol.PlantUml.PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
                [Marker(start: true), Marker(start: false), Call(Kronikol.Tracking.RequestResponseType.Request), Call(Kronikol.Tracking.RequestResponseType.Response)])
            .Single().PlantUmls.Single().PlainText;
    }

    [Fact]
    public async Task A_note_split_by_the_browser_draws_every_part_when_its_text_quotes_a_diagram()
    {
        await OpenReport(ReportTestHelper.GenerateReportWithPlantUmlQuotingLongNote(TempDir, OutputDir, "CapturedTextHazards_Split.html"));

        var parts = await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('.plantuml-browser svg')).map(svg =>
                (svg.closest('.plantuml-browser').querySelector('.engine-failure') ? 'failure:' : '')
                + Array.from(svg.querySelectorAll('text')).map(t => t.textContent).join(' '))
            """);
        Assert.True(parts.Length > 1, "the note must be split");
        Assert.DoesNotContain(parts, p => p.StartsWith("failure:", StringComparison.Ordinal) || p.Contains("Syntax Error", StringComparison.Ordinal));
        var all = Collapse(string.Join(" ", parts));
        var missing = new[] { 0, 100, 200, 319 }.Where(i => !all.Contains($"step {i}\\n", StringComparison.Ordinal)).ToList();
        var at = all.IndexOf("step", StringComparison.Ordinal);
        Assert.True(missing.Count == 0, $"{parts.Length} parts; not drawn: step {string.Join(", ", missing)}; "
            + $"first: {(at < 0 ? "(none)" : all.Substring(at, Math.Min(60, all.Length - at)))}; part lengths {string.Join("/", parts.Select(p => p.Length))}");
    }
}
