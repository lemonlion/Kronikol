using System.Text.Json;
using Kronikol.Reports;

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
        await Page.GotoReportAsync(GenerateReportWithStepTableBars(fileName));
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
    public async Task Tables_and_doc_strings_get_a_blank_line_above_and_none_below_in_the_bar()
    {
        await OpenStepTableReport("StepTableBarPadding.html");

        // Judged on what the engine PAINTS, not the source bytes: the blank padding line above a
        // table pushes its header row a full extra text line below the step label (measured 32px
        // padded vs 17px unpadded on the pinned engine), and the doc-string bar gets the same gap
        // between its label and first payload line. No blank line is painted below the last data row
        // from 5.0.0, as the owner asked (plans/OPTION_C_PLAN.md R17.6).
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
        Assert.True(spacerBelow == 0, "expected no blank line painted below the last table row");
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
    // ── 5.9.1 (plans/REPORT_UI_FIXES_PLAN.md I6, B1 to B5): step bars like the step-param-table ──

    private async Task OpenRealStepBarsAsync(string fileName)
    {
        var table = Kronikol.PlantUml.StepBarPlantUml.Build("Given the following muffins exist",
            [new Kronikol.PlantUml.StepBarTable(null, [["name", "price"], ["Blueberry", "3.50"], ["Double Chocolate", "4.00"]])]);
        var doc = Kronikol.PlantUml.StepBarPlantUml.Build("When the order payload is submitted",
            docString: "{ \"muffin\": \"Blueberry\",\n  \"qty\": 2 }");
        var source = "@startuml\n!pragma teoz true\n" + Kronikol.PlantUml.StepBarPlantUml.BodyStyle
            + "\nactor \"Caller\" as caller\nparticipant \"OrderService\" as svc\n" + table + "\ncaller -> svc : POST /api/orders\n"
            + doc + "\nsvc --> caller : 201 Created\n" + Kronikol.PlantUml.StepBarPlantUml.Build("Then the order is confirmed") + "\n@enduml";
        Feature[] features =
        [
            new() { DisplayName = "Orders", Scenarios = [new() { Id = "s1", DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }] }
        ];
        var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode("s1", "", source)],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await Page.WaitForFunctionAsync(
            "() => { const s = document.querySelector(\"[data-diagram-type='plantuml'] svg\"); return !!s && !!s.getAttribute('data-k-roles'); }",
            null, new() { Timeout = 15000, PollingInterval = 200 });
    }

    // Each step bar with a table in it, measured as the plan's probe measures it (harness probe/i6-stepbars.js), in screen
    // px: the label's centre and each table's against the bar's, and for each cell text its room to the rules around it,
    // its offset from its column's centre, its weight, its colour, whether it is in the header row, and the doc string box's
    // line starts. A text is one with ink (a strut's zero-width text is not).
    private const string MeasureBars = """
        () => {
            const svg = document.querySelector("[data-diagram-type='plantuml'] svg");
            const r = el => { const b = el.getBoundingClientRect(); return { l: b.left, t: b.top, r: b.right, b: b.bottom }; };
            const texts = [...svg.querySelectorAll('text')].filter(t => /[^\s\u200b]/.test(t.textContent))
                .map(t => ({ s: t.textContent, el: t, ...r(t) }));
            const rules = [...svg.querySelectorAll('.k-step-rule')].map(r);
            const bars = [...svg.querySelectorAll('.k-step')].map(r).sort((a, c) => a.t - c.t);
            const out = [];
            for (const bar of bars) {
                const inBar = o => o.t >= bar.t - 1 && o.b <= bar.b + 1 && o.l >= bar.l - 1 && o.r <= bar.r + 1;
                const bt = texts.filter(inBar);
                const br = rules.filter(inBar);
                if (!bt.length || !br.length) continue;
                const firstY = Math.min(...bt.map(t => t.t));
                const label = bt.filter(t => Math.abs(t.t - firstY) < 2);
                const labelL = Math.min(...label.map(t => t.l)), labelR = Math.max(...label.map(t => t.r));
                const hs = br.filter(o => o.b - o.t <= 0.8), vs = br.filter(o => o.r - o.l <= 0.8);
                const keys = [...new Set(hs.map(h => Math.round(h.l) + ':' + Math.round(h.r)))];
                const tables = keys.map(k => {
                    const [l, rr] = k.split(':').map(Number);
                    const own = hs.filter(h => Math.round(h.l) === l && Math.round(h.r) === rr).sort((a, c) => a.t - c.t);
                    const top = own[0].t, bottom = own[own.length - 1].b;
                    const cols = [...new Set(vs.filter(v => v.t >= top - 1 && v.b <= bottom + 1 && v.l >= l - 1 && v.r <= rr + 1).map(v => v.l))].sort((a, c) => a - c);
                    const cells = bt.filter(t => t.t >= top && t.b <= bottom && t.l >= l && t.r <= rr).map(t => {
                        const left = Math.max(...cols.filter(x => x <= t.l + 0.5)), right = Math.min(...cols.filter(x => x >= t.r - 0.5));
                        const above = Math.max(...own.filter(h => h.t <= t.t + 0.5).map(h => h.b)), below = Math.min(...own.filter(h => h.t >= t.b - 0.5).map(h => h.t));
                        const cs = getComputedStyle(t.el);
                        return { s: t.s, left: t.l - left, top: t.t - above, bottom: below - t.b, offCentre: (t.l + t.r) / 2 - (left + right) / 2,
                            head: t.el.classList.contains('k-step-table-head-text'), weight: cs.fontWeight, fill: cs.fill, x: t.l };
                    });
                    return { centreOffset: (l + rr) / 2 - (bar.l + bar.r) / 2, cells };
                });
                out.push({ label: label.map(t => t.s).join(' '), labelOffset: (labelL + labelR) / 2 - (bar.l + bar.r) / 2, tables });
            }
            const probe = document.createElement('span'); probe.style.color = 'var(--kron-ink)'; document.body.appendChild(probe);
            const ink = getComputedStyle(probe).color; probe.remove();
            return JSON.stringify({ bars: out, ink });
        }
        """;

    private sealed record Cell(string S, double Left, double Top, double Bottom, double OffCentre, bool Head, string Weight, string Fill, double X);
    private sealed record Table(double CentreOffset, Cell[] Cells);
    private sealed record Bar(string Label, double LabelOffset, Table[] Tables);
    private sealed record Bars(Bar[] List, string Ink);

    private async Task<Bars> MeasureBarsAsync()
    {
        var json = await Page.EvaluateAsync<string>(MeasureBars);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var bars = doc.RootElement.GetProperty("bars").Deserialize<Bar[]>(opts)!;
        return new Bars(bars, doc.RootElement.GetProperty("ink").GetString()!);
    }

    private static Bar BarOf(Bars bars, string label) =>
        bars.List.Single(b => b.Label.Contains(label, StringComparison.Ordinal));

    // B1: every line and table of a bar centred in it (the engine's default alignment is left: a table sat 33 px left of
    // its bar's centre, M-I6)
    [Fact]
    public async Task A_bars_label_and_each_table_are_centred_in_the_bar()
    {
        await OpenRealStepBarsAsync("StepBarsCentred.html");
        var bars = await MeasureBarsAsync();

        foreach (var label in new[] { "muffins exist", "order payload is submitted" })
        {
            var bar = BarOf(bars, label);
            Assert.True(Math.Abs(bar.LabelOffset) <= 1, $"{label}: the label is {bar.LabelOffset:F1} px off the bar's centre");
            var table = Assert.Single(bar.Tables);
            Assert.True(Math.Abs(table.CentreOffset) <= 1, $"{label}: the table is {table.CentreOffset:F1} px off the bar's centre");
        }
    }

    // B3: a cell has the HTML step-param-table's room, 8 px each side of its text, and the struts' 2 px above it and 3 px
    // below on a real bar, toward the HTML's 3 and 3 (one space and nothing above or below until 5.9.1, M-I6)
    [Fact]
    public async Task A_cell_has_the_html_tables_room()
    {
        await OpenRealStepBarsAsync("StepBarsCellRoom.html");
        var table = Assert.Single(BarOf(await MeasureBarsAsync(), "muffins exist").Tables);

        var body = table.Cells.Where(c => !c.Head).ToArray();
        Assert.NotEmpty(body);
        foreach (var c in body)
        {
            Assert.True(c.Left >= 7.5, $"'{c.S}' starts {c.Left:F1} px from its column's rule");
            Assert.True(c.Top >= 1.5 && c.Bottom >= 2.5, $"'{c.S}' has {c.Top:F1} px above and {c.Bottom:F1} px below");
        }
    }

    // B2: a doc string is one framed cell, so its lines keep their indentation in a centred bar (a guard: green on 5.9.0,
    // whose lines started at the left; red with B1 and without B2, which centred each line)
    [Fact]
    public async Task A_doc_strings_lines_keep_their_indentation()
    {
        await OpenRealStepBarsAsync("StepBarsDocIndent.html");
        var bars = await MeasureBarsAsync();
        var lines = await Page.EvaluateAsync<double[]>("""
            () => {
                const ts = [...document.querySelectorAll("[data-diagram-type='plantuml'] svg text")];
                const x = f => { const t = ts.find(f); return t ? t.getBoundingClientRect().left : NaN; };
                // where a line's first visible character starts, in the picture's units: its indentation can be spaces
                // inside its text, which the text's box takes in
                const at = t => t.getStartPositionOfChar(Math.max(0, t.textContent.search(/[^\s\u200b]/))).x;
                const a = ts.find(t => t.textContent.trimStart().startsWith('{ "muffin"')), q = ts.find(t => t.textContent.trimStart().startsWith('"qty"'));
                return a && q ? [at(a), at(q), q.getComputedTextLength() / q.textContent.length] : [NaN, NaN, NaN];
            }
            """);
        Assert.True(!double.IsNaN(lines[0]) && !double.IsNaN(lines[1]), "both doc string lines are drawn");
        // the second line is written two spaces in: it starts two characters' width right of the first
        Assert.True(lines[1] - lines[0] >= 1.5 * lines[2], $"the indented line starts {lines[1] - lines[0]:F1} px right of the first");
        Assert.NotNull(bars);
    }

    // B4, B5: the header row's texts centred in their columns at the HTML th's weight, the body's at the left of theirs in
    // the page's ink, as the step-param-table (the engine draws every cell's text at its left: the header's median text
    // sat 13 px off its column's centre, M-I6)
    [Fact]
    public async Task A_tables_header_is_centred_in_its_columns_and_its_body_starts_at_the_left()
    {
        await OpenRealStepBarsAsync("StepBarsHeaderCentred.html");
        var bars = await MeasureBarsAsync();
        var table = Assert.Single(BarOf(bars, "muffins exist").Tables);

        var head = table.Cells.Where(c => c.Head).ToArray();
        Assert.Equal(["name", "price"], head.Select(c => c.S.Trim()).OrderBy(s => s, StringComparer.Ordinal));
        foreach (var c in head)
        {
            Assert.True(Math.Abs(c.OffCentre) <= 1, $"header '{c.S}' is {c.OffCentre:F1} px off its column's centre");
            Assert.Equal("600", c.Weight);
        }
        foreach (var c in table.Cells.Where(c => !c.Head))
        {
            Assert.True(Math.Abs(c.Left - 8) <= 1.5, $"'{c.S}' starts {c.Left:F1} px into its column, where the HTML table starts at 8");
            Assert.Equal(bars.Ink, c.Fill);
        }
    }
}
