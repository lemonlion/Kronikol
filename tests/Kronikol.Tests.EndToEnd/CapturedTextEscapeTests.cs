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
