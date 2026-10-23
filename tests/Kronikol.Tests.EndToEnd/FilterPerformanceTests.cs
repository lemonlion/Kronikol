using System.Text;
using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Search)]
public class FilterPerformanceTests : PlaywrightTestBase
{
    public FilterPerformanceTests(PlaywrightFixture fixture) : base(fixture) { }

    private static string GenerateLargeReport(int featureCount, int scenariosPerFeature, string[] dependencies)
    {
        var sb = new StringBuilder();
        sb.Append($$"""
            <!DOCTYPE html>
            <html>
            <head>
            <style>{{Stylesheets.HtmlReportStyleSheet}}</style>
            <script>
            var _filterCache;
            function fc() {
                if (_filterCache) return _filterCache;
                var scenarios = document.getElementsByClassName('scenario');
                var features = document.getElementsByClassName('feature');
                var items = [];
                var fMap = new Map();
                for (var fi = 0; fi < features.length; fi++) {
                    var sc = features[fi].getElementsByClassName('scenario');
                    var arr = [];
                    for (var si = 0; si < sc.length; si++) {
                        var s = sc[si];
                        var raw = s.getAttribute('data-dependencies') || '';
                        var d = raw ? new Set(raw.split(',')) : new Set();
                        var item = { el: s, deps: d, status: s.getAttribute('data-status') || '', isHappy: s.classList.contains('happy-path'), f: features[fi] };
                        items.push(item);
                        arr.push(item);
                        fMap.set(s, features[fi]);
                    }
                }
                _filterCache = { items: items, features: features, scenarios: scenarios, fMap: fMap };
                return _filterCache;
            }

            function toggle_dependency(btn) {
                btn.classList.toggle('dependency-active');
                filter_dependencies();
            }

            function filter_dependencies() {
                var c = fc();
                var activeSet = new Set();
                document.querySelectorAll('.dependency-toggle.dependency-active').forEach(function(b) {
                    activeSet.add(b.getAttribute('data-dependency'));
                });

                for (var i = 0; i < c.items.length; i++) {
                    c.items[i].el.classList.remove('dep-hidden');
                }
                for (var i = 0; i < c.features.length; i++) {
                    var f = c.features[i];
                    f.classList.remove('dep-hidden');
                    if (f.classList.contains('dep-opened')) {
                        f.removeAttribute('open');
                        f.classList.remove('dep-opened');
                    }
                }

                if (activeSet.size === 0) return;

                var activeArr = Array.from(activeSet);
                var featureVisibleCounts = new Map();
                var totalVisible = 0;
                for (var i = 0; i < c.features.length; i++) featureVisibleCounts.set(c.features[i], 0);

                for (var i = 0; i < c.items.length; i++) {
                    var d = c.items[i];
                    var matchesAll = d.deps.size > 0;
                    if (matchesAll) {
                        for (var j = 0; j < activeArr.length; j++) {
                            if (!d.deps.has(activeArr[j])) { matchesAll = false; break; }
                        }
                    }
                    if (!matchesAll) {
                        d.el.classList.add('dep-hidden');
                    } else if (!d.el.classList.contains('search-hidden') && !d.el.classList.contains('status-hidden') && !d.el.classList.contains('hp-hidden')) {
                        featureVisibleCounts.set(d.f, (featureVisibleCounts.get(d.f) || 0) + 1);
                        totalVisible++;
                    }
                }

                var shouldOpen = totalVisible <= 10;
                for (var i = 0; i < c.features.length; i++) {
                    var f = c.features[i];
                    if ((featureVisibleCounts.get(f) || 0) === 0) {
                        f.classList.add('dep-hidden');
                    } else if (shouldOpen && !f.hasAttribute('open')) {
                        f.setAttribute('open', '');
                        f.classList.add('dep-opened');
                    }
                }
            }
            </script>
            </head>
            <body>
            <h1>Performance Test Report</h1>
            <div class="filters">
            <div class="dependency-filters"><span class="dependency-filters-label">Dependencies:</span>
            """);

        foreach (var dep in dependencies)
            sb.Append($"""<button class="dependency-toggle" data-dependency="{dep}" onclick="toggle_dependency(this)">{dep}</button>""");
        sb.Append("</div></div><div id=\"report-content\">");

        var svgTemplate = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 800 400" width="800" height="400">
            <rect width="800" height="400" fill="#fefefe"/>
            <line x1="200" y1="80" x2="200" y2="380" stroke="#888" stroke-dasharray="5"/>
            <text x="200" y="60" text-anchor="middle" font-size="12">Caller</text>
            <text x="400" y="60" text-anchor="middle" font-size="12">Service</text>
            </svg>
            """;

        var rng = new Random(42);
        for (var fi = 0; fi < featureCount; fi++)
        {
            sb.Append($"""<details class="feature"><summary class="h2">Feature {fi + 1}</summary>""");
            for (var si = 0; si < scenariosPerFeature; si++)
            {
                var scenarioDeps = new List<string>();
                for (var di = 0; di < dependencies.Length; di++)
                    if (rng.NextDouble() < 0.4) scenarioDeps.Add(dependencies[di]);
                if (scenarioDeps.Count == 0) scenarioDeps.Add(dependencies[0]);

                sb.Append($"""
                    <details class="scenario{(si == 0 ? " happy-path" : "")}" data-dependencies="{string.Join(",", scenarioDeps)}" data-status="Passed">
                    <summary class="h3">Scenario {fi + 1}.{si + 1}</summary>
                    <details class="example-diagrams" open>
                    <summary class="h4">Sequence Diagrams</summary>
                    <div class="plantuml-inline-svg" data-diagram-type="plantuml">{svgTemplate}</div>
                    </details>
                    </details>
                    """);
            }
            sb.Append("</details>");
        }

        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    [Fact]
    public async Task DependencyFilter_CompletesWithin1Second_With100Scenarios()
    {
        var dependencies = new[] { "OrderService", "PaymentGateway", "NotificationService", "CacheService", "AuditService" };
        var html = GenerateLargeReport(20, 5, dependencies);
        await Page.GotoReportAsync(ServePage(html));
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await Page.EvaluateAsync("fc()");

        var ms = await Page.EvaluateAsync<long>("""
            () => {
                var btn = document.querySelector('.dependency-toggle[data-dependency="OrderService"]');
                var t0 = performance.now(); btn.click(); return Math.round(performance.now() - t0);
            }
        """);

        var hiddenCount = await Page.Locator(".scenario.dep-hidden").CountAsync();
        Assert.True(hiddenCount > 0);
        Assert.True(ms < 1000, $"Dependency filter took {ms}ms, expected <1000ms");
    }

    [Fact]
    public async Task DependencyFilter_CompletesWithin1Second_With200Scenarios()
    {
        var dependencies = new[] { "OrderService", "PaymentGateway", "NotificationService", "CacheService", "AuditService", "UserService", "InventoryService" };
        var html = GenerateLargeReport(20, 10, dependencies);
        await Page.GotoReportAsync(ServePage(html));
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await Page.EvaluateAsync("fc()");

        var ms = await Page.EvaluateAsync<long>("""
            () => {
                var btn = document.querySelector('.dependency-toggle[data-dependency="OrderService"]');
                var t0 = performance.now(); btn.click(); return Math.round(performance.now() - t0);
            }
        """);

        Assert.True(await Page.Locator(".scenario.dep-hidden").CountAsync() > 0);
        Assert.True(ms < 1000, $"Dependency filter with 200 scenarios took {ms}ms, expected <1000ms");
    }

    [Fact]
    public async Task DependencyFilter_ToggleOffCompletesWithin1Second()
    {
        var dependencies = new[] { "OrderService", "PaymentGateway", "NotificationService", "CacheService", "AuditService" };
        var html = GenerateLargeReport(20, 5, dependencies);
        await Page.GotoReportAsync(ServePage(html));
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await Page.EvaluateAsync("fc()");
        await Page.EvaluateAsync("document.querySelector('.dependency-toggle[data-dependency=\"OrderService\"]').click()");

        var ms = await Page.EvaluateAsync<long>("""
            () => {
                var btn = document.querySelector('.dependency-toggle[data-dependency="OrderService"]');
                var t0 = performance.now(); btn.click(); return Math.round(performance.now() - t0);
            }
        """);

        Assert.Equal(0, await Page.Locator(".scenario.dep-hidden").CountAsync());
        Assert.True(ms < 1000, $"Dependency filter toggle-off took {ms}ms, expected <1000ms");
    }

    [Fact]
    public async Task DependencyFilter_FeaturesContracted_WhenMoreThan10Matches()
    {
        var dependencies = new[] { "OrderService", "PaymentGateway" };
        var html = GenerateLargeReport(5, 5, dependencies);
        await Page.GotoReportAsync(ServePage(html));
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await Page.Locator(".dependency-toggle[data-dependency='OrderService']").ClickAsync();

        var openedCount = await Page.Locator(".feature[open]:not(.dep-hidden)").CountAsync();
        var visibleCount = await Page.Locator(".feature:not(.dep-hidden)").CountAsync();

        Assert.True(visibleCount > 0);
        Assert.Equal(0, openedCount);
    }
}
