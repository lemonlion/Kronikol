namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A filter row's label takes one line whatever face draws it. Each sits in a 92 px column so the rows' controls line
/// up, and a face wider than the one these tests run under draws DEPENDENCIES wider than that: the page broke the word
/// where it ran out (overflow-wrap: anywhere), and its S went to a line of its own (5.0.0). The column now widens for a
/// label that needs more, and keeps 92 px where the labels fit.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class FilterRowLabelTests : PlaywrightTestBase
{
    public FilterRowLabelTests(PlaywrightFixture fixture) : base(fixture) { }

    /// <summary>Each shown label: its text, how many lines it takes, its column's width, and whether its row's controls
    /// start clear of its text.</summary>
    private Task<string[]> LabelsAsync() => Page.EvaluateAsync<string[]>("""
        () => Array.from(document.querySelectorAll('.filtering-box :is(.status-filters-label, .duration-filters-label, .happy-filters-label, .tag-filters-label, .dependency-filters-label)'))
            .filter(l => l.getClientRects().length)
            .map(l => {
                const range = document.createRange();
                range.selectNodeContents(l);
                const lines = new Set(Array.from(range.getClientRects()).map(r => Math.round(r.top))).size;
                const text = range.getBoundingClientRect(), controls = l.nextElementSibling.getBoundingClientRect();
                return l.textContent + ': ' + lines + (lines === 1 ? ' line' : ' lines') + (controls.left >= text.right ? '' : ', under its controls');
            })
        """);

    /// <summary>Each shown label's column width, and the width of its text on one line.</summary>
    private Task<double[][]> ColumnsAsync() => Page.EvaluateAsync<double[][]>("""
        () => Array.from(document.querySelectorAll('.filtering-box [class$="-filters-label"]'))
            .filter(l => l.getClientRects().length && !l.classList.contains('more-filters-label'))
            .map(l => {
                const range = document.createRange();
                range.selectNodeContents(l);
                return [l.getBoundingClientRect().width, range.getBoundingClientRect().width];
            })
        """);

    [Fact]
    public async Task Every_label_takes_one_line_in_a_face_that_draws_it_wider_than_its_column()
    {
        await Page.GotoAsync(GenerateReport("FilterLabels_WideFace.html"));
        await OpenFilterRowsAsync();
        // the labels as a wider face draws them: DEPENDENCIES and HAPPY PATHS take more than 92 px
        await Page.AddStyleTagAsync(new() { Content = ".filtering-box [class$='-filters-label'] { letter-spacing: 2px; }" });

        var labels = await LabelsAsync();

        Assert.Contains(labels, l => l.StartsWith("Dependencies", StringComparison.Ordinal));
        Assert.All(labels, l => Assert.EndsWith(": 1 line", l));
        // and the column widens for it: no label runs under its row's controls
        var columns = await ColumnsAsync();
        Assert.Contains(columns, c => c[1] > 92);
        Assert.All(columns, c => Assert.InRange(c[0], Math.Max(92, c[1]) - 0.5, Math.Max(92, c[1]) + 0.5));
    }

    // 92 px where the label fits, so the rows' controls line up; the label's own width where it does not (the face of
    // CI's Linux runner draws DEPENDENCIES 95 px wide, the one this fact first assumed would fit)
    [Fact]
    public async Task Each_column_is_92_px_or_its_label_s_width_where_that_is_more()
    {
        await Page.GotoAsync(GenerateReport("FilterLabels_Fit.html"));
        await OpenFilterRowsAsync();

        var columns = await ColumnsAsync();

        Assert.NotEmpty(columns);
        Assert.All(columns, c => Assert.InRange(c[0], Math.Max(92, c[1]) - 0.5, Math.Max(92, c[1]) + 0.5));
        Assert.Contains(columns, c => c[1] < 92); // a label that fits keeps the 92 px column
        Assert.All(await LabelsAsync(), l => Assert.EndsWith(": 1 line", l));
    }
}
