namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Regression tests for the user-reported defect where a multi-line SQL request note came out with
/// line breaks in the middle of words. Every non-JSON request body used to fall into the
/// form-url-encoded formatter, which sliced the whole string — its own newlines included — every 80
/// characters and wove grey <c>&amp;</c> dividers through it.
///
/// Judged on what the engine PAINTS, not on the source bytes: the assertions read the drawn
/// <c>&lt;text&gt;</c> runs back into display lines (PlantUML emits one element per word, so they
/// are grouped by <c>y</c> and ordered by <c>x</c>) and check that no SQL identifier is split
/// across two of them.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class SqlNoteWrappingTests : DiagramNotePlaywrightBase
{
    public SqlNoteWrappingTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task NavigateAndSetup(string fileName)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithMultiLineSqlNote(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    /// <summary>The diagram's painted text, one string per drawn row, top to bottom.</summary>
    private Task<string[]> PaintedLines() =>
        Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<string[]>("""
            el => {
                const rows = new Map();
                for (const t of el.querySelectorAll('text')) {
                    const y = parseFloat(t.getAttribute('y') || '0');
                    const x = parseFloat(t.getAttribute('x') || '0');
                    if (!rows.has(y)) rows.set(y, []);
                    rows.get(y).push({ x, text: t.textContent });
                }
                return Array.from(rows.keys()).sort((a, b) => a - b)
                    .map(y => rows.get(y).sort((a, b) => a.x - b.x).map(p => p.text).join(''));
            }
        """);

    [Fact]
    public async Task Multi_line_sql_note_is_drawn_without_mid_word_breaks()
    {
        await NavigateAndSetup("SqlNote_NoMidWordBreaks.html");
        var lines = await PaintedLines();

        // Each of these used to straddle a chunk boundary at a different offset.
        foreach (var whole in new[]
                 {
                     "subtractDays(t.transaction_period, 7)",
                     "subtractMonths(t.transaction_period, 1)",
                     "END AS comparison_date",
                     "WHERE t.location_id = {LocationId:String}",
                     "ORDER BY report_date",
                 })
            Assert.True(lines.Any(l => l.Contains(whole)),
                $"expected '{whole}' drawn on one line; painted lines were:\n{string.Join("\n", lines)}");
    }

    [Fact]
    public async Task Bitwise_ampersand_is_drawn_as_itself_not_as_a_divider()
    {
        await NavigateAndSetup("SqlNote_Ampersand.html");
        var lines = await PaintedLines();

        Assert.True(lines.Any(l => l.Contains("AND bitAnd(t.flags, 4) & 4 = 4")),
            $"expected the bitwise & kept inline; painted lines were:\n{string.Join("\n", lines)}");
    }

    [Fact]
    public async Task Copy_box_text_on_a_sql_note_yields_the_query_as_captured()
    {
        await NavigateAndSetup("SqlNote_CopyBoxText.html");
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);

        await DispatchContextMenu(Page.Locator(".note-hover-rect").First);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });
        var menuItem = Page.Locator(".diagram-ctx-menu").GetByText("Copy box text");
        await menuItem.WaitForAsync(new() { Timeout = 5000 });
        await menuItem.ClickAsync();
        var clipboard = await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

        // No Kronikol markup, and every line of the query intact — this is SQL someone pastes into
        // a client and runs.
        Assert.DoesNotContain("lightgray", clipboard);
        Assert.Contains("subtractDays(t.transaction_period, 7)", clipboard);
        Assert.Contains("AND bitAnd(t.flags, 4) & 4 = 4", clipboard);
        Assert.Contains("FROM `sme`.`location_performance_weekly` t", clipboard);
    }
}
