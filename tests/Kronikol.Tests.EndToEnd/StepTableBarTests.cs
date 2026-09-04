namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Step-delimiter bars carrying a Gherkin data table / doc string (the
/// <c>&lt;&lt;stepDelimiter&gt;&gt;&lt;&lt;stepBody&gt;&gt;</c> styled form): the real engine must draw
/// the table as a table — white on the black bar, no syntax error — and the hide-steps toggle must
/// strip the styled bar exactly like the legacy one.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class StepTableBarTests : DiagramNotePlaywrightBase
{
    public StepTableBarTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task OpenStepTableReport(string fileName)
    {
        await Page.GotoAsync(GenerateReportWithStepTableBars(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    private string GenerateReportWithStepTableBars(string fileName) =>
        ReportTestHelper.GenerateReportWithStepTableBars(TempDir, OutputDir, fileName);

    private async Task<string> GetDataPlantuml()
    {
        return await Page.EvaluateAsync<string>("""
            () => document.querySelector('[data-diagram-type="plantuml"]').getAttribute('data-plantuml') || ''
        """);
    }

    [Fact]
    public async Task The_table_and_doc_string_render_inside_the_step_bars()
    {
        await OpenStepTableReport("StepTableBars.html");

        var text = await GetNormalizedSvgText();

        // The step text, every table cell and the doc string all drew — and as a parsed table, not
        // as literal creole markup.
        Assert.Contains("Given the following muffins exist", text);
        Assert.Contains("Blueberry", text);
        Assert.Contains("3.50", text);
        Assert.Contains("Double Chocolate", text);
        Assert.Contains("4.00", text);
        Assert.Contains("\"qty\": 2", text);
        Assert.DoesNotContain("|=", text);
        Assert.DoesNotContain("\\n", text);
        Assert.DoesNotContain("Syntax Error", text);

        // The table's cell text is white (the .stepBody style), like the bar it sits in.
        var whiteCellCount = await Page.Locator("[data-diagram-type='plantuml'] svg").First
            .EvaluateAsync<int>("""
                el => Array.from(el.querySelectorAll('text'))
                    .filter(t => t.textContent.includes('Double Chocolate'))
                    .filter(t => (t.getAttribute('fill') || '').toUpperCase().includes('FFF')).length
            """);
        Assert.True(whiteCellCount > 0, "expected the table cell text to be drawn white on the black bar");
    }

    [Fact]
    public async Task Tables_and_doc_strings_get_a_blank_line_of_breathing_room_in_the_bar()
    {
        await OpenStepTableReport("StepTableBarPadding.html");

        // Judged on what the engine PAINTS, not the source bytes: the blank padding line above a
        // table pushes its header row a full extra text line below the step label (measured 32px
        // padded vs 17px unpadded on the pinned engine), the trailing blank line paints as a
        // whitespace-only text line just below the last data row, and the doc-string bar gets the
        // same gap between its label and first payload line.
        var m = await Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<double[]>("""
            el => {
                const ts = Array.from(el.querySelectorAll('text'))
                    .map(t => ({ y: parseFloat(t.getAttribute('y') || '0'), text: t.textContent }));
                const yOf = f => { const t = ts.find(f); return t ? t.y : -1; };
                const label = yOf(t => t.text.includes('muffins exist'));
                const header = yOf(t => t.text.trim() === 'name');
                const lastRow = yOf(t => t.text.trim() === '4.00');
                const spacerBelow = ts.some(t => t.text.trim() === '' && t.y > lastRow && t.y - lastRow < 30);
                const docLabel = yOf(t => t.text.includes('order payload is submitted'));
                const docFirst = yOf(t => t.text.includes('"muffin"'));
                return [label, header, lastRow, spacerBelow ? 1 : 0, docLabel, docFirst];
            }
        """);
        var (label, header, lastRow, spacerBelow, docLabel, docFirst) = (m[0], m[1], m[2], m[3], m[4], m[5]);

        Assert.True(label > 0 && header > 0 && lastRow > 0 && docLabel > 0 && docFirst > 0,
            $"expected all bar texts drawn, got y = [{string.Join(", ", m)}]");
        Assert.True(header - label >= 25, $"expected a blank line between step text and table header, gap was {header - label}px");
        Assert.True(spacerBelow == 1, "expected a blank spacer line painted below the last table row");
        Assert.True(docFirst - docLabel >= 25, $"expected a blank line between step text and doc string, gap was {docFirst - docLabel}px");
    }

    [Fact]
    public async Task Hiding_steps_removes_the_styled_bars_and_showing_them_brings_them_back()
    {
        await OpenStepTableReport("StepTableBarsToggle.html");

        Assert.Contains("<<stepBody>>", await GetDataPlantuml());

        // Hide steps: the styled bar (and its table) must strip with the legacy bars.
        await Page.Locator("button[data-toggle='steps']").First.ClickAsync();
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var source = container.getAttribute('data-plantuml');
                return source && !source.includes('stepDelimiter');
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });

        // The bar lines (and the table they carry) are stripped; the orphaned .stepBody style block
        // is harmless and may stay.
        var hidden = await GetDataPlantuml();
        Assert.DoesNotContain("stepDelimiter", hidden);
        Assert.DoesNotContain("<<stepBody>>", hidden);
        Assert.DoesNotContain("Double Chocolate", hidden);
        Assert.DoesNotContain("Double Chocolate", await GetNormalizedSvgText());

        // Show them again: the table comes back.
        await Page.Locator("button[data-toggle='steps']").First.ClickAsync();
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var source = container.getAttribute('data-plantuml');
                return source && source.includes('<<stepBody>>');
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });

        Assert.Contains("Double Chocolate", await GetNormalizedSvgText());
    }
}
