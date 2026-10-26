using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The search and what it leaves on the page (plans/SEARCH_PLAN.md). B5: emptying the search takes off the marks it put
/// on an outline's rows and puts back the row it chose, as it closes only what it opened (5.2.0); until then the empty
/// search returned before its clean-up, so the rows stayed marked and the row it chose stayed chosen.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public partial class SearchResultsTests : PlaywrightTestBase
{
    private const string Title = "A basket is weighed on the UK scale (<case>)";
    private static readonly DateTime T0 = new(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);

    public SearchResultsTests(PlaywrightFixture fixture) : base(fixture) { }

    private static Scenario Row(string id, string @case, string count, string grams) => new()
    {
        Id = id,
        DisplayName = $"A basket is weighed on the UK scale ({@case})",
        KeyedByOutline = true,
        OutlineId = Title,
        ExampleValues = new() { ["case"] = @case, ["count"] = count, ["grams"] = grams },
        ExamplesBlockIndex = 0,
        Result = ExecutionResult.Passed,
        Duration = TimeSpan.FromMilliseconds(16),
    };

    private string WriteOutline(string fileName)
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Baskets",
                Scenarios = [Row("s0", "empty basket", "0", "0"), Row("s1", "one apple", "1", "151"), Row("s2", "two apples", "2", "301")],
            },
            new Feature
            {
                DisplayName = "Scales",
                Scenarios = [new Scenario { Id = "s3", DisplayName = "A scale reads zero when empty", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(9) }],
            },
        ];
        var path = ReportGenerator.GenerateHtmlReport([], features, T0, T0.AddMinutes(1), null,
            Path.Combine(TempDir, fileName), "Baskets", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task OpenOutlineAsync(string fileName)
    {
        await Page.GotoReportAsync(WriteOutline(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
    }

    private Task<string[]> ActiveRowsAsync() => Page.EvaluateAsync<string[]>(
        "() => Array.from(document.querySelectorAll('details.scenario-parameterized tr.row-active')).map(r => r.getAttribute('data-row-idx'))");

    // B5
    [Fact]
    public async Task Emptying_the_search_unmarks_its_outline_rows()
    {
        await OpenOutlineAsync("SearchEmptyUnmarksRows.html");

        await FillSearchBar("two apples");
        await Page.WaitForFunctionAsync("() => document.querySelectorAll('tr.row-search-match').length > 0",
            null, new() { Timeout = 5000, PollingInterval = 200 });

        await FillSearchBar("");
        await Page.WaitForFunctionAsync("() => document.querySelectorAll('tr.row-search-match').length === 0",
            null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    // B5: the row the search chose goes back to the one the reader had
    [Fact]
    public async Task Emptying_the_search_puts_back_the_row_it_chose()
    {
        await OpenOutlineAsync("SearchEmptyPutsBackRow.html");
        Assert.Equal(["0"], await ActiveRowsAsync());

        await FillSearchBar("two apples");
        await Page.WaitForFunctionAsync("() => document.querySelector('details.scenario-parameterized tr.row-active')?.getAttribute('data-row-idx') === '2'",
            null, new() { Timeout = 5000, PollingInterval = 200 });

        await FillSearchBar("");
        await Page.WaitForFunctionAsync("() => document.querySelector('details.scenario-parameterized tr.row-active')?.getAttribute('data-row-idx') === '0'",
            null, new() { Timeout = 5000, PollingInterval = 200 });
        Assert.Equal(["0"], await ActiveRowsAsync());
    }

    // B5: a row the reader chose while the search was on is the reader's, and stays
    [Fact]
    public async Task Emptying_the_search_keeps_a_row_the_reader_chose()
    {
        await OpenOutlineAsync("SearchEmptyKeepsReaderRow.html");

        await FillSearchBar("apple");
        await Page.WaitForFunctionAsync("() => document.querySelectorAll('tr.row-search-match').length === 2",
            null, new() { Timeout = 5000, PollingInterval = 200 });
        await Page.Locator("details.scenario-parameterized tr[data-row-idx='2']").First.ClickAsync();

        await FillSearchBar("");
        await Page.WaitForFunctionAsync("() => document.querySelectorAll('tr.row-search-match').length === 0",
            null, new() { Timeout = 5000, PollingInterval = 200 });
        Assert.Equal(["2"], await ActiveRowsAsync());
    }
}
