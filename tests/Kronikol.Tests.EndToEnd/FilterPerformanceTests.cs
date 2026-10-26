using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// How long the dependency filter takes on a report of many scenarios. The timings run the scripts a report ships, on a
/// generated report (SEARCH_PLAN B17): until then this class built a page of its own carrying its own fc() and
/// filter_dependencies, which hid with a dep-hidden class the shipped scripts no longer use, so it timed code the report
/// does not run. A dependency chip is clicked as a reader clicks it, and the filter hides a scenario with an inline
/// display:none (report-scenario-feature-map-helper.js applyVisibility).
/// </summary>
[Collection(PlaywrightCollections.Search)]
public class FilterPerformanceTests : PlaywrightTestBase
{
    public FilterPerformanceTests(PlaywrightFixture fixture) : base(fixture) { }

    // Each function the timings run, by the script that ships it
    private static readonly (string Resource, string Function)[] Timed =
    [
        ("report-dependency-filter-function.js", "filter_dependencies"),
        ("report-scenario-feature-map-helper.js", "fc"),
        ("report-scenario-feature-map-helper.js", "applyVisibility"),
        ("report-scenario-feature-map-helper.js", "filter_readout")
    ];

    // A report of featureCount closed features of scenariosPerFeature scenarios each, the first of each a happy path.
    // A scenario's dependencies are the participants its diagram names, each of the given ones with a chance of 0.4 and
    // the first where none is drawn, with a seeded draw so every run times the same report.
    private string GenerateLargeReport(string fileName, int featureCount, int scenariosPerFeature, string[] dependencies)
    {
        var rng = new Random(42);
        var diagrams = new List<DefaultDiagramsFetcher.DiagramAsCode>();
        var features = new Feature[featureCount];
        for (var fi = 0; fi < featureCount; fi++)
        {
            var scenarios = new Scenario[scenariosPerFeature];
            for (var si = 0; si < scenariosPerFeature; si++)
            {
                var scenarioDeps = dependencies.Where(_ => rng.NextDouble() < 0.4).ToList();
                if (scenarioDeps.Count == 0) scenarioDeps.Add(dependencies[0]);
                var id = $"f{fi + 1}s{si + 1}";
                scenarios[si] = new Scenario
                {
                    Id = id, DisplayName = $"Scenario {fi + 1}.{si + 1}", IsHappyPath = si == 0, Result = ExecutionResult.Passed
                };
                diagrams.Add(new(id, "", "@startuml\n"
                    + string.Concat(scenarioDeps.Select((dep, i) => $"participant \"{dep}\" as p{i}\n"))
                    + "p0 -> p0 : call\n@enduml"));
            }
            features[fi] = new Feature { DisplayName = $"Feature {fi + 1}", Scenarios = scenarios };
        }

        var path = ReportGenerator.GenerateHtmlReport(
            [.. diagrams], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Performance Test Report", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task OpenAsync(string url)
    {
        await Page.GotoReportAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
        // fc() builds the filters' list of the page's scenarios once and keeps it: built before the timing, so a click
        // times the filter alone
        await Page.EvaluateAsync("fc()");
    }

    // Clicks a dependency's chip as a reader does and returns the milliseconds the click took: the filter runs in it
    private Task<long> TimeChipClickAsync(string dependency) => Page.EvaluateAsync<long>("""
        dep => {
            var chip = document.querySelector('.filtering-box .dependency-toggle[data-dependency="' + dep + '"]');
            var t0 = performance.now(); chip.click(); return Math.round(performance.now() - t0);
        }
        """, dependency);

    private Task<int> HiddenScenarioCountAsync() => Page.EvaluateAsync<int>(
        "() => Array.from(document.querySelectorAll('#report-content details.scenario')).filter(s => s.style.display === 'none').length");

    // The timings run the scripts a report ships (SEARCH_PLAN B17): each function the page holds is the shipped one, its
    // text read from the resource as the generator loads it for a report without history
    [Fact]
    public async Task The_filter_timings_run_the_shipped_scripts()
    {
        await OpenAsync(GenerateLargeReport("FilterPerf_Shipped.html", 2, 2, ["OrderService", "PaymentGateway"]));
        foreach (var (resource, function) in Timed)
        {
            var shipped = await Page.EvaluateAsync<string>("([source, name]) => new Function(source + '\\nreturn ' + name + '.toString();')()",
                new object[] { ReportGenerator.LoadScript(resource, verdicts: false), function });
            Assert.Equal(shipped, await Page.EvaluateAsync<string?>("name => typeof window[name] === 'function' ? window[name].toString() : null", function));
        }
    }

    [Fact]
    public async Task DependencyFilter_CompletesWithin1Second_With100Scenarios()
    {
        var dependencies = new[] { "OrderService", "PaymentGateway", "NotificationService", "CacheService", "AuditService" };
        await OpenAsync(GenerateLargeReport("FilterPerf_100.html", 20, 5, dependencies));

        var ms = await TimeChipClickAsync("OrderService");

        Assert.True(await HiddenScenarioCountAsync() > 0);
        Assert.True(ms < 1000, $"Dependency filter took {ms}ms, expected <1000ms");
    }

    [Fact]
    public async Task DependencyFilter_CompletesWithin1Second_With200Scenarios()
    {
        var dependencies = new[] { "OrderService", "PaymentGateway", "NotificationService", "CacheService", "AuditService", "UserService", "InventoryService" };
        await OpenAsync(GenerateLargeReport("FilterPerf_200.html", 20, 10, dependencies));

        var ms = await TimeChipClickAsync("OrderService");

        Assert.True(await HiddenScenarioCountAsync() > 0);
        Assert.True(ms < 1000, $"Dependency filter with 200 scenarios took {ms}ms, expected <1000ms");
    }

    // A chip of a two-way row, as every row starts since 5.10.0, goes off, has and off again (report-filter-chips-script.js):
    // the click timed is the one that turns the filter off
    [Fact]
    public async Task DependencyFilter_ToggleOffCompletesWithin1Second()
    {
        var dependencies = new[] { "OrderService", "PaymentGateway", "NotificationService", "CacheService", "AuditService" };
        await OpenAsync(GenerateLargeReport("FilterPerf_Off.html", 20, 5, dependencies));
        await TimeChipClickAsync("OrderService");
        Assert.True(await HiddenScenarioCountAsync() > 0);

        var ms = await TimeChipClickAsync("OrderService");

        Assert.Equal(0, await HiddenScenarioCountAsync());
        Assert.True(ms < 1000, $"Dependency filter toggle-off took {ms}ms, expected <1000ms");
    }

    [Fact]
    public async Task DependencyFilter_FeaturesContracted_WhenMoreThan10Matches()
    {
        var dependencies = new[] { "OrderService", "PaymentGateway" };
        await OpenAsync(GenerateLargeReport("FilterPerf_Contracted.html", 5, 5, dependencies));
        await OpenFilterRowsAsync();

        await Page.Locator(".filtering-box .dependency-toggle[data-dependency='OrderService']").First.ClickAsync();

        var shown = await Page.EvaluateAsync<int>(
            "() => Array.from(document.querySelectorAll('#report-content details.scenario')).filter(s => s.style.display !== 'none').length");
        var features = await Page.EvaluateAsync<int[]>("""
            () => {
                var shown = Array.from(document.querySelectorAll('#report-content details.feature')).filter(f => f.style.display !== 'none');
                return [shown.length, shown.filter(f => f.open).length];
            }
            """);

        Assert.True(shown > 10, $"{shown} scenarios call OrderService");
        Assert.True(features[0] > 0);
        Assert.Equal(0, features[1]);
    }
}
