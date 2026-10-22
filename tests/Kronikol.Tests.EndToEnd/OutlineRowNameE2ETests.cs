using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// #101, facts W1 to W3: a Reqnroll outline row named as Gherkin names its pickle shows that name where the page lists a
/// row on its own, and still opens from its old address (plans/OUTLINE_ROW_NAMES_PLAN.md 4.4). The rows are built as the
/// Reqnroll converter builds them: filled names, the outline's title as <c>OutlineId</c>, and the mark.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class OutlineRowNameE2ETests : PlaywrightTestBase
{
    private const string Title = "A basket is weighed on the UK scale (<case>)";
    private static readonly DateTime T0 = new(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

    public OutlineRowNameE2ETests(PlaywrightFixture fixture) : base(fixture) { }

    private static Scenario Row(string id, string @case, string count, string grams, bool failed) => new()
    {
        Id = id,
        DisplayName = $"A basket is weighed on the UK scale ({@case})",
        KeyedByOutline = true,
        OutlineId = Title,
        ExampleValues = new() { ["case"] = @case, ["count"] = count, ["grams"] = grams },
        ExamplesBlockIndex = 0,
        Result = failed ? ExecutionResult.Failed : ExecutionResult.Passed,
        ErrorMessage = failed ? "Expected the scale to read the basket's weight" : null,
        Duration = TimeSpan.FromMilliseconds(16),
    };

    /// <summary>The issue's outline; its second and third rows fail with one message, so they share a failure cluster.</summary>
    private string Write(string fileName)
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Baskets",
                Scenarios = [Row("s0", "empty basket", "0", "0", false), Row("s1", "one apple", "1", "151", true), Row("s2", "two apples", "2", "301", true)],
            },
        ];
        var path = ReportGenerator.GenerateHtmlReport([], features, T0, T0.AddMinutes(1), null,
            Path.Combine(TempDir, fileName), "Baskets", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task OpenAsync(string fileName)
    {
        await Page.GotoAsync(Write(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
    }

    // W1
    [Fact]
    public async Task The_timeline_lists_each_row_by_its_filled_name()
    {
        await OpenAsync("OutlineRowTimeline.html");
        await OpenReportTabAsync("timeline");

        var names = await Page.EvaluateAsync<string[]>(
            "() => Array.from(document.querySelectorAll('#scenario-timeline > .timeline-row .timeline-label')).map(l => l.textContent)");

        Assert.Equal(
            ["A basket is weighed on the UK scale (empty basket)", "A basket is weighed on the UK scale (one apple)", "A basket is weighed on the UK scale (two apples)"],
            names.Order(StringComparer.Ordinal));
    }

    // W2
    [Fact]
    public async Task A_search_for_a_row_s_value_finds_that_row_alone()
    {
        await OpenAsync("OutlineRowSearch.html");

        await FillSearchBar("two apples");
        await Page.WaitForFunctionAsync("() => document.querySelectorAll('tr.row-search-match').length > 0",
            null, new() { Timeout = 5000, PollingInterval = 200 });

        var matches = Page.Locator("details.scenario-parameterized tr.row-search-match:visible");
        Assert.Equal(1, await matches.CountAsync());
        Assert.Contains("two apples", await matches.First.InnerTextAsync());
    }

    [Fact]
    public async Task A_search_for_the_placeholder_finds_the_outline_and_no_row()
    {
        // Each row's own search text is its filled name and its values: only the outline's heading still says <case>.
        await OpenAsync("OutlineRowPlaceholderSearch.html");

        await FillSearchBar("<case>");
        await Page.WaitForFunctionAsync("() => !document.querySelector('details.scenario-parameterized').classList.contains('search-hidden')",
            null, new() { Timeout = 5000, PollingInterval = 200 });

        await Expect(Page.Locator("details.scenario-parameterized").First).ToBeVisibleAsync();
        Assert.Equal(0, await Page.Locator("tr.row-search-match").CountAsync());
    }

    // W3
    [Fact]
    public async Task A_failure_cluster_names_each_row_and_its_link_opens_that_row()
    {
        await OpenAsync("OutlineRowCluster.html");
        await Page.Locator(".failure-cluster > summary").First.ClickAsync();

        var links = Page.Locator(".failure-cluster-scenario-link");
        Assert.Equal(["A basket is weighed on the UK scale (one apple)", "A basket is weighed on the UK scale (two apples)"],
            (await links.AllInnerTextsAsync()).Order(StringComparer.Ordinal));

        await links.Filter(new() { HasText = "(two apples)" }).ClickAsync();

        await Expect(Page.Locator("details.scenario-parameterized tr.row-active:visible")).ToContainTextAsync("two apples");
    }

    [Fact]
    public async Task The_outline_s_copy_button_still_copies_its_heading()
    {
        await OpenAsync("OutlineRowCopy.html");

        var name = await Page.Locator("details.scenario-parameterized .copy-scenario-name").First.GetAttributeAsync("data-scenario-name");

        Assert.Matches(new Regex("<case>"), name!);
    }
}
