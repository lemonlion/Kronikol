using System.Globalization;
using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// What the report paints from its durations (plans/WARM_UP_PLAN.md R0). A report generated on a comma-decimal machine
/// wrote the Scenario Timeline's widths as <c>width:12,3%</c> and the pie chart's lengths as <c>r="40,0"</c>, which a
/// browser drops: every bar was painted at its 2 px minimum and the chart lost its rings. And the Features Summary
/// table sorted its duration columns on the number at the start of each cell's text, so <c>1m 5s</c> sorted as 1 and
/// below <c>500ms</c>.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class DurationPaintTests : PlaywrightTestBase
{
    public DurationPaintTests(PlaywrightFixture fixture) : base(fixture) { }

    private string Generate(string fileName, Feature[] features, string culture = "en-US")
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var path = ReportGenerator.GenerateHtmlReport(
                [], features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Durations", true,
                diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
            File.Copy(path, Path.Combine(OutputDir, fileName), true);
            return new Uri(path).AbsoluteUri;
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static Scenario Scenario(string id, ExecutionResult result, double ms) =>
        new() { Id = id, DisplayName = $"Scenario {id}", Result = result, Duration = TimeSpan.FromMilliseconds(ms) };

    private static Feature[] TimelineFeatures() =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                Scenario("long", ExecutionResult.Passed, 1260),
                Scenario("half", ExecutionResult.Failed, 630),
                Scenario("tenth", ExecutionResult.Passed, 126),
                new Scenario { Id = "skipped", DisplayName = "Scenario skipped", Result = ExecutionResult.Skipped }
            ]
        }
    ];

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public async Task The_timeline_paints_each_bar_at_its_share_of_the_longest(string culture)
    {
        await Page.GotoAsync(Generate($"TimelineWidths_{culture}.html", TimelineFeatures(), culture));
        await Page.Locator(".timeline-toggle").ClickAsync();
        await Expect(Page.Locator("#scenario-timeline")).ToBeVisibleAsync();

        var shares = await Page.EvaluateAsync<double[]>("""
            () => Array.from(document.querySelectorAll('#scenario-timeline .timeline-bar')).map(bar =>
                bar.getBoundingClientRect().width / bar.parentElement.getBoundingClientRect().width)
            """);

        // Painted, not just written: 100%, 50% and 10% of the track, each within a pixel's worth.
        Assert.Equal(3, shares.Length);
        Assert.InRange(shares[0], 0.99, 1.01);
        Assert.InRange(shares[1], 0.49, 0.51);
        Assert.InRange(shares[2], 0.09, 0.11);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public async Task The_pie_chart_paints_its_rings(string culture)
    {
        await Page.GotoAsync(Generate($"PieChart_{culture}.html", TimelineFeatures(), culture));
        await Page.Locator(".summary-chart svg").WaitForAsync();

        var rings = await Page.EvaluateAsync<double[][]>("""
            () => Array.from(document.querySelectorAll('.summary-chart circle')).map(c =>
                [c.r.baseVal.value, c.getTotalLength(), c.getBoundingClientRect().width])
            """);

        // Passed, failed and skipped: three rings of radius 40, each drawn (a dropped r paints nothing).
        Assert.Equal(3, rings.Length);
        Assert.All(rings, ring =>
        {
            Assert.Equal(40, ring[0], 3);
            Assert.True(ring[2] > 0, "a ring with no painted width");
        });
        var dashes = await Page.EvaluateAsync<string[]>(
            "() => Array.from(document.querySelectorAll('.summary-chart circle')).map(c => getComputedStyle(c).strokeDasharray)");
        // Half the circle passed (1 of 4 is skipped and has no duration, but counts): two numbers per ring, not four.
        Assert.All(dashes, dash => Assert.Equal(2, dash.Split(',', StringSplitOptions.TrimEntries).Length));
    }

    [Fact]
    public async Task The_features_summary_sorts_its_duration_columns_by_time()
    {
        Feature Feature(string name, params double[] ms) => new()
        {
            DisplayName = name,
            Scenarios = ms.Select((m, i) => Scenario($"{name}-{i}", ExecutionResult.Passed, m)).ToArray()
        };
        // Written as "500ms", "1m 5s" and "2s": the number at the start of each is 500, 1 and 2.
        await Page.GotoAsync(Generate("SummarySort.html", [Feature("Short", 500), Feature("Long", 65_400), Feature("Middle", 1200, 800)]));

        await Page.Locator("details.features-summary-details > summary").ClickAsync();
        var table = Page.Locator("table.feature-summary-table");
        await Expect(table).ToBeVisibleAsync();

        async Task<string[]> SortBy(string column)
        {
            await table.Locator("th", new() { HasTextString = column }).First.ClickAsync();
            return await table.EvaluateAsync<string[]>("t => Array.from(t.tBodies[0].rows).map(r => r.cells[0].textContent)");
        }

        Assert.Equal(["Long", "Middle", "Short"], await SortBy("Duration"));
        Assert.Equal(["Short", "Middle", "Long"], await SortBy("Duration"));
        Assert.Equal(["Long", "Middle", "Short"], await SortBy("Longest"));
        Assert.Equal(["Long", "Middle", "Short"], await SortBy("Avg"));
        // The counts still sort as numbers, and the names as text.
        Assert.Equal(["Short", "Middle", "Long"], await SortBy("Feature"));
    }
}
