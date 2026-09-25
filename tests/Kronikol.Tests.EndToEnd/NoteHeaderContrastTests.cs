namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// What a note's header lines paint (DIAGRAM_COLOURS_PLAN S1), read from the rendered SVG rather than the
/// source. Header lines were <c>gray</c>, which the engine paints <c>#808080</c>: 3.87 : 1 on the default note
/// fill and 3.20 on the event note, below the 4.5 : 1 WCAG 2.1 AA asks of text under 18 pt. From 3.30.0 they
/// are written in a computed ink. This is the test that fails if an engine change moves a note fill.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class NoteHeaderContrastTests : PlaywrightTestBase
{
    public NoteHeaderContrastTests(PlaywrightFixture fixture) : base(fixture) { }

    private sealed record Paint(string Text, string Ink, string Fill, double Ratio, string BodyInk);

    /// <summary>
    /// For the painted word <paramref name="word"/>: its fill, the fill of the note shape it sits in (the
    /// smallest filled path or polygon whose box holds it), the WCAG 2.1 ratio of the two, and the fill of the
    /// body text in the same note.
    /// </summary>
    private async Task<Paint> PaintOf(string word, string bodyWord)
    {
        var result = await Page.EvaluateAsync<string[]>("""
            ([word, bodyWord]) => {
                const svg = document.querySelector("[data-diagram-type='plantuml'] svg");
                const texts = Array.from(svg.querySelectorAll('text'));
                const find = w => texts.find(t => t.textContent.includes(w));
                const header = find(word), body = find(bodyWord);
                if (!header || !body) return null;
                const hb = header.getBBox();
                const cx = hb.x + hb.width / 2, cy = hb.y + hb.height / 2;
                const shapes = Array.from(svg.querySelectorAll('path, polygon, rect'))
                    .filter(s => /^#[0-9a-f]{6}$/i.test(s.getAttribute('fill') || ''))
                    .map(s => ({ s, b: s.getBBox() }))
                    .filter(({ b }) => b.x <= cx && cx <= b.x + b.width && b.y <= cy && cy <= b.y + b.height)
                    .sort((a, b) => a.b.width * a.b.height - b.b.width * b.b.height);
                if (!shapes.length) return null;
                const fill = shapes[0].s.getAttribute('fill');
                const ink = header.getAttribute('fill');
                const lin = c => { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
                const lum = h => 0.2126 * lin(parseInt(h.substr(1, 2), 16)) + 0.7152 * lin(parseInt(h.substr(3, 2), 16)) + 0.0722 * lin(parseInt(h.substr(5, 2), 16));
                const [a, b] = [lum(ink), lum(fill)].sort((x, y) => y - x);
                return [header.textContent, ink, fill, String((a + 0.05) / (b + 0.05)), body.getAttribute('fill')];
            }
            """, new[] { word, bodyWord });
        Assert.True(result is not null, $"no painted '{word}' or '{bodyWord}', or no note shape under it");
        return new Paint(result![0], result[1], result[2], double.Parse(result[3], System.Globalization.CultureInfo.InvariantCulture), result[4]);
    }

    private async Task Open([System.Runtime.CompilerServices.CallerMemberName] string? testName = null)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithHeaderInkNotes(TempDir, OutputDir, $"HeaderContrast_{testName}.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
    }

    [Fact]
    public async Task A_request_notes_header_line_clears_aa_on_its_fill_and_stays_quieter_than_the_body()
    {
        await Open();

        var paint = await PaintOf("Authorization", "kkkkkkkk");

        Assert.Equal("#FEFFDD", paint.Fill.ToUpperInvariant());
        Assert.True(paint.Ratio >= 4.5, $"header '{paint.Text}' {paint.Ink} on {paint.Fill} is {paint.Ratio:F2} : 1");
        Assert.NotEqual(paint.BodyInk.ToUpperInvariant(), paint.Ink.ToUpperInvariant());
    }

    [Fact]
    public async Task The_full_path_block_is_painted_in_the_same_ink()
    {
        await Open();

        var header = await PaintOf("Authorization", "kkkkkkkk");
        var fullPath = await PaintOf("Full", "kkkkkkkk");

        Assert.Equal(header.Ink.ToUpperInvariant(), fullPath.Ink.ToUpperInvariant());
        Assert.True(fullPath.Ratio >= 4.5, $"[Full path] {fullPath.Ink} on {fullPath.Fill} is {fullPath.Ratio:F2} : 1");
    }

    [Fact]
    public async Task An_event_notes_header_line_clears_aa_on_the_event_fill()
    {
        await Open();

        var paint = await PaintOf("X-Event-Type", "OrderDeleted");

        Assert.Equal("#CFECF7", paint.Fill.ToUpperInvariant());
        Assert.True(paint.Ratio >= 4.5, $"header '{paint.Text}' {paint.Ink} on {paint.Fill} is {paint.Ratio:F2} : 1");
    }
}
