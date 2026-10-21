using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A scenario that is not a test (ingest's "Traffic outside any test", a stack's start-up) as the page shows it
/// (plans/RUN_WINDOW_PLAN.md R1, fact 11): drawn like any scenario with a "not a test" badge, and counted as no test by
/// the band, the readout the filters keep, the features sort, the duration filter and the CSV export.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class NotATestPaintTests : PlaywrightTestBase
{
    public NotATestPaintTests(PlaywrightFixture fixture) : base(fixture) { }

    private string Generate(string fileName)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "nat-create", DisplayName = "Create order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) },
                    new Scenario { Id = "nat-reject", DisplayName = "Reject order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) },
                ]
            },
            new Feature
            {
                DisplayName = "Ingested",
                Scenarios =
                [
                    new Scenario { Id = "nat-logged", DisplayName = "Logged only", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(500) },
                    new Scenario { Id = "nat-session", DisplayName = "Traffic outside any test", Result = ExecutionResult.Passed, NotATest = true, Duration = TimeSpan.FromSeconds(780) },
                ]
            },
            new Feature
            {
                DisplayName = "Stack start-up",
                Scenarios = [new Scenario { Id = "nat-start-up", DisplayName = "Stack start-up", Result = ExecutionResult.Passed, NotATest = true, Duration = TimeSpan.FromSeconds(25) }]
            },
        };
        var start = new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);
        var path = ReportGenerator.GenerateHtmlReport([], features, start, start.AddSeconds(9), null, Path.Combine(TempDir, fileName), "Not a test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task OpenAsync(string fileName)
    {
        await Page.GotoAsync(Generate(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
    }

    private Task<string> ReadoutAsync() => Page.Locator(".filter-readout").InnerTextAsync();

    private Task<string[]> ShownScenariosAsync() => Page.EvaluateAsync<string[]>(
        "() => Array.from(document.querySelectorAll('details.scenario')).filter(s => s.style.display !== 'none').map(s => s.querySelector('.copy-scenario-name').getAttribute('data-scenario-name'))");

    [Fact]
    public async Task The_band_counts_the_tests_and_the_scenario_that_is_not_one_paints_its_badge()
    {
        await OpenAsync("NotATest_Badge.html");

        await Expect(Page.Locator(".run-stat", new() { HasTextString = "Scenarios" }).Locator("dd")).ToHaveTextAsync("3");
        await Expect(Page.Locator(".run-stat", new() { HasTextString = "Features" }).Locator("dd")).ToHaveTextAsync("2");

        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(d => d.open = true)");
        var startUp = Page.Locator("details.scenario[data-not-a-test]", new() { Has = Page.Locator("[data-scenario-name='Stack start-up']") });
        var badge = startUp.Locator(":scope > summary .label.not-a-test");
        await Expect(badge).ToBeVisibleAsync();
        await Expect(badge).ToHaveTextAsync("not a test");
        // the badge is muted as every label is, and the scenario's own time badge has no speed colour
        var colours = await startUp.EvaluateAsync<string[]>(
            "s => { const b = s.querySelector(':scope > summary .label.not-a-test'), d = s.querySelector(':scope > summary .duration-badge'); " +
            "const probe = document.createElement('span'); probe.style.color = 'var(--kron-muted)'; document.body.appendChild(probe); " +
            "const muted = getComputedStyle(probe).color; probe.remove(); return [getComputedStyle(b).color, muted, d.className]; }");
        Assert.Equal(colours[1], colours[0]);
        Assert.Equal("duration-badge", colours[2]);
        await Expect(Page.Locator("details.scenario[data-not-a-test]")).ToHaveCountAsync(2);
    }

    [Fact]
    public async Task The_readout_keeps_counting_tests_as_the_filters_change()
    {
        await OpenAsync("NotATest_Readout.html");
        Assert.Equal("All 3 scenarios, 2 features", await ReadoutAsync());

        await FillSearchBar("order");
        await Page.WaitForFunctionAsync("() => document.querySelector('.filter-readout').classList.contains('is-filtered')", null, new() { PollingInterval = 200 });
        Assert.Equal("Filtered 2 of 3 scenarios, 1 of 2 features", await ReadoutAsync());

        // The start-up shows under a search for it, and is still no test: nothing the readout counts is shown.
        await FillSearchBar("stack");
        await Page.WaitForFunctionAsync("() => /0 of 3/.test(document.querySelector('.filter-readout').textContent)", null, new() { PollingInterval = 200 });
        Assert.Equal(["Stack start-up"], await ShownScenariosAsync());
        Assert.Equal("Filtered 0 of 3 scenarios, 0 of 2 features", await ReadoutAsync());
        // A scenario is shown, so the page does not say that none matches.
        await Expect(Page.Locator(".filters-empty")).ToBeHiddenAsync();

        await FillSearchBar("no scenario has this");
        await Page.WaitForFunctionAsync("() => Array.from(document.querySelectorAll('details.scenario')).every(s => s.style.display === 'none')", null, new() { PollingInterval = 200 });
        await Expect(Page.Locator(".filters-empty")).ToBeVisibleAsync();

        await FillSearchBar("");
        await Page.WaitForFunctionAsync("() => !document.querySelector('.filter-readout').classList.contains('is-filtered')", null, new() { PollingInterval = 200 });
        Assert.Equal("All 3 scenarios, 2 features", await ReadoutAsync());
    }

    [Fact]
    public async Task A_sort_by_time_puts_a_feature_of_no_test_last()
    {
        await OpenAsync("NotATest_Sort.html");

        await Page.Locator("#features-sort").SelectOptionAsync("duration");

        var rows = await Page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('#features-table-view tbody tr')).map(r => r.cells[0].textContent.trim())");
        Assert.Equal(["Orders", "Ingested", "Stack start-up"], rows);
        await Page.Locator("#features-sort").SelectOptionAsync("longest");
        rows = await Page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('#features-table-view tbody tr')).map(r => r.cells[0].textContent.trim())");
        Assert.Equal(["Orders", "Ingested", "Stack start-up"], rows);
    }

    [Fact]
    public async Task The_duration_filter_leaves_out_a_scenario_that_is_not_a_test()
    {
        await OpenAsync("NotATest_Duration.html");

        // P50 of the tests' 500 ms, 1 s and 2 s is 1 s: the fold's 13 minutes made it 2 s, and showed the fold.
        var p50 = Page.Locator("button.percentile-btn", new() { HasTextString = "P50" });
        await Expect(p50).ToHaveAttributeAsync("data-threshold-ms", "1000");
        await Page.Locator(".more-filters").ClickAsync();
        await p50.ClickAsync();
        await Page.WaitForFunctionAsync("() => document.querySelector('.filter-readout').classList.contains('is-filtered')", null, new() { PollingInterval = 200 });

        Assert.Equal(["Create order", "Reject order"], (await ShownScenariosAsync()).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task The_csv_export_leaves_out_a_scenario_that_is_not_a_test()
    {
        await OpenAsync("NotATest_Csv.html");

        var download = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.Locator("button.export-btn", new() { HasTextString = "Export Filtered CSV" }).ClickAsync());
        var path = Path.Combine(TempDir, $"not-a-test_{Guid.NewGuid():N}.csv");
        await download.SaveAsAsync(path);
        var lines = (await File.ReadAllTextAsync(path)).Split('\n');

        Assert.Equal(
        [
            "Feature,Scenario,Status,Duration",
            "\"Orders\",\"Create order\",\"Passed\",\"2000\"",
            "\"Orders\",\"Reject order\",\"Passed\",\"1000\"",
            "\"Ingested\",\"Logged only\",\"Passed\",\"500\"",
        ], lines);
    }
}
