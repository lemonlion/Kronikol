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

    private Task<double[]> ColumnWidthsAsync() => Page.EvaluateAsync<double[]>("""
        () => Array.from(document.querySelectorAll('.filtering-box [class$="-filters-label"]'))
            .filter(l => l.getClientRects().length && !l.classList.contains('more-filters-label'))
            .map(l => Math.round(l.getBoundingClientRect().width))
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
    }

    [Fact]
    public async Task Where_the_labels_fit_every_column_is_92_px()
    {
        await Page.GotoAsync(GenerateReport("FilterLabels_Fit.html"));
        await OpenFilterRowsAsync();

        var widths = await ColumnWidthsAsync();

        Assert.NotEmpty(widths);
        Assert.All(widths, w => Assert.Equal(92, w));
        Assert.All(await LabelsAsync(), l => Assert.EndsWith(": 1 line", l));
    }
}
