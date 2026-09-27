using Microsoft.Playwright;
using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Scenarios)]
public class FailureClusterLinkTests : PlaywrightTestBase
{
    public FailureClusterLinkTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string PlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "Service" as svc
        caller -> svc : GET /api/test
        svc --> caller : 200 OK
        @enduml
        """;

    private string GenerateClusterReport(string fileName, Feature[] features, DiagramAsCode[]? diagrams = null)
    {
        diagrams ??= features
            .SelectMany(f => f.Scenarios)
            .Select(s => new DiagramAsCode(s.Id, "", PlantUmlSource))
            .ToArray();

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Cluster Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private Feature[] CreateBasicClusterData() =>
    [
        new Feature
        {
            DisplayName = "Order Feature",
            Scenarios =
            [
                new Scenario
                {
                    Id = "c1", DisplayName = "Create order fails on timeout",
                    Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(5),
                    ErrorMessage = "Connection refused (Stock Service:5001)"
                },
                new Scenario
                {
                    Id = "c2", DisplayName = "Update order fails on timeout",
                    Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(3),
                    ErrorMessage = "Connection refused (Stock Service:5001)"
                },
                new Scenario
                {
                    Id = "c3", DisplayName = "List orders succeeds",
                    Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                    IsHappyPath = true
                }
            ]
        }
    ];

    private Feature[] CreateMultiFeatureClusterData() =>
    [
        new Feature
        {
            DisplayName = "Order Feature",
            Scenarios =
            [
                new Scenario
                {
                    Id = "mf1", DisplayName = "Create order fails",
                    Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(5),
                    ErrorMessage = "Database connection timeout after 30s"
                },
                new Scenario
                {
                    Id = "mf2", DisplayName = "List orders succeeds",
                    Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                    IsHappyPath = true
                }
            ]
        },
        new Feature
        {
            DisplayName = "Payment Feature",
            Scenarios =
            [
                new Scenario
                {
                    Id = "mf3", DisplayName = "Process payment fails",
                    Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(4),
                    ErrorMessage = "Database connection timeout after 30s"
                },
                new Scenario
                {
                    Id = "mf4", DisplayName = "Refund succeeds",
                    Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                    IsHappyPath = true
                }
            ]
        },
        new Feature
        {
            DisplayName = "Shipping Feature",
            Scenarios =
            [
                new Scenario
                {
                    Id = "mf5", DisplayName = "Calculate shipping fails",
                    Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(3),
                    ErrorMessage = "Database connection timeout after 30s"
                },
                new Scenario
                {
                    Id = "mf6", DisplayName = "Free shipping applied",
                    Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                    IsHappyPath = true
                }
            ]
        }
    ];

    private Feature[] CreateParameterizedClusterData() =>
    [
        new Feature
        {
            DisplayName = "Checkout Feature",
            Scenarios =
            [
                new Scenario
                {
                    Id = "p1", DisplayName = "Checkout completes (UK, GBP)",
                    Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(2),
                    ErrorMessage = "Payment gateway unavailable",
                    ExampleValues = new Dictionary<string, string> { ["Country"] = "UK", ["Currency"] = "GBP" },
                    ExampleRawValues = new Dictionary<string, object?> { ["Country"] = "UK", ["Currency"] = "GBP" }
                },
                new Scenario
                {
                    Id = "p2", DisplayName = "Checkout completes (US, USD)",
                    Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(2),
                    ErrorMessage = "Payment gateway unavailable",
                    ExampleValues = new Dictionary<string, string> { ["Country"] = "US", ["Currency"] = "USD" },
                    ExampleRawValues = new Dictionary<string, object?> { ["Country"] = "US", ["Currency"] = "USD" }
                },
                new Scenario
                {
                    Id = "p3", DisplayName = "Checkout completes (DE, EUR)",
                    Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                    ExampleValues = new Dictionary<string, string> { ["Country"] = "DE", ["Currency"] = "EUR" },
                    ExampleRawValues = new Dictionary<string, object?> { ["Country"] = "DE", ["Currency"] = "EUR" }
                }
            ]
        }
    ];

    private Feature[] CreateDuplicateNameClusterData() =>
    [
        new Feature
        {
            DisplayName = "Order API",
            Scenarios =
            [
                new Scenario
                {
                    Id = "dn1", DisplayName = "Health check fails",
                    Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(1),
                    ErrorMessage = "Service unavailable (503)"
                },
                new Scenario
                {
                    Id = "dn2", DisplayName = "Normal operation succeeds",
                    Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                    IsHappyPath = true
                }
            ]
        },
        new Feature
        {
            DisplayName = "Payment API",
            Scenarios =
            [
                new Scenario
                {
                    Id = "dn3", DisplayName = "Health check fails",
                    Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(1),
                    ErrorMessage = "Service unavailable (503)"
                },
                new Scenario
                {
                    Id = "dn4", DisplayName = "Payment processed",
                    Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                    IsHappyPath = true
                }
            ]
        }
    ];

    private Feature[] CreateMultiClusterData() =>
    [
        new Feature
        {
            DisplayName = "API Feature",
            Scenarios =
            [
                new Scenario
                {
                    Id = "mc1", DisplayName = "Endpoint A timeout",
                    Result = ExecutionResult.Failed,
                    ErrorMessage = "Connection refused (Stock Service:5001)"
                },
                new Scenario
                {
                    Id = "mc2", DisplayName = "Endpoint B timeout",
                    Result = ExecutionResult.Failed,
                    ErrorMessage = "Connection refused (Stock Service:5001)"
                },
                new Scenario
                {
                    Id = "mc3", DisplayName = "Endpoint C auth error",
                    Result = ExecutionResult.Failed,
                    ErrorMessage = "Unauthorized: token expired"
                },
                new Scenario
                {
                    Id = "mc4", DisplayName = "Endpoint D auth error",
                    Result = ExecutionResult.Failed,
                    ErrorMessage = "Unauthorized: token expired"
                },
                new Scenario
                {
                    Id = "mc5", DisplayName = "Endpoint E works",
                    Result = ExecutionResult.Passed, IsHappyPath = true
                }
            ]
        }
    ];

    // ── Basic cluster link navigation ──

    [Fact]
    public async Task Cluster_link_scrolls_to_correct_scenario()
    {
        var url = GenerateClusterReport("ClusterBasicScroll.html", CreateBasicClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();

        var links = Page.Locator(".failure-cluster-scenario-link");
        Assert.True(await links.CountAsync() >= 2);
        await links.First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        var inViewport = await Page.Locator("#scenario-create-order-fails-on-timeout")
            .EvaluateAsync<bool>("el => { var r = el.getBoundingClientRect(); return r.top >= -10 && r.top < window.innerHeight; }");
        Assert.True(inViewport, "Target scenario should be in viewport after cluster link click");
    }

    [Fact]
    public async Task Cluster_link_reaches_a_scenario_far_down_a_long_report()
    {
        // A long report's features have mostly never been drawn, so each counts as its 500 px
        // content-visibility placeholder, and a smooth scroll aimed across them ends past the scenario
        // (StableIdDeepLinkTests has the same fault for a #sid- link).
        await Page.GotoAsync(GenerateLongReport("ClusterFarDown.html", "Scenario 30.2", "Scenario 31.1"));
        await Page.Locator(".failure-cluster > summary").First.ClickAsync();
        var link = Page.Locator(".failure-cluster-scenario-link").First;
        var target = (await link.GetAttributeAsync("href"))!.TrimStart('#');

        await link.ClickAsync();
        await WaitForScrollToSettleAsync();

        Assert.True(await SummaryIsOnScreenAsync($"[id='{target}']"),
            "The scenario's title should be on screen once the page has stopped scrolling");
    }

    [Fact]
    public async Task Cluster_link_opens_scenario_details()
    {
        var url = GenerateClusterReport("ClusterOpensScenario.html", CreateBasicClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();
        await Page.Locator(".failure-cluster-scenario-link").First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        Assert.NotNull(await Page.Locator("#scenario-create-order-fails-on-timeout").GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Cluster_link_opens_parent_feature()
    {
        var url = GenerateClusterReport("ClusterOpensFeature.html", CreateBasicClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        var feature = Page.Locator("details.feature").First;
        Assert.Null(await feature.GetAttributeAsync("open"));

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();
        await Page.Locator(".failure-cluster-scenario-link").First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        Assert.NotNull(await feature.GetAttributeAsync("open"));
    }

    // ── Multi-feature navigation ──

    [Fact]
    public async Task Cluster_link_navigates_to_scenario_in_second_feature()
    {
        var url = GenerateClusterReport("ClusterSecondFeature.html", CreateMultiFeatureClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();

        var links = Page.Locator(".failure-cluster-scenario-link");
        Assert.True(await links.CountAsync() >= 2);
        await links.Nth(1).ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        var target = Page.Locator("#scenario-process-payment-fails");
        var inViewport = await target.EvaluateAsync<bool>("el => { var r = el.getBoundingClientRect(); return r.top >= -10 && r.top < window.innerHeight; }");
        Assert.True(inViewport, "Scenario in second feature should be in viewport");
        Assert.NotNull(await target.GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Cluster_link_navigates_to_scenario_in_third_feature()
    {
        var url = GenerateClusterReport("ClusterThirdFeature.html", CreateMultiFeatureClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();

        var links = Page.Locator(".failure-cluster-scenario-link");
        Assert.True(await links.CountAsync() >= 3);
        await links.Nth(2).ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        var inViewport = await Page.Locator("#scenario-calculate-shipping-fails")
            .EvaluateAsync<bool>("el => { var r = el.getBoundingClientRect(); return r.top >= -10 && r.top < window.innerHeight; }");
        Assert.True(inViewport, "Scenario in third feature should be in viewport");
    }

    // ── Sequential navigation ──

    [Fact]
    public async Task Clicking_second_cluster_link_scrolls_to_second_scenario()
    {
        var url = GenerateClusterReport("ClusterSequentialNav.html", CreateMultiFeatureClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();
        var links = Page.Locator(".failure-cluster-scenario-link");

        await links.First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        var firstInViewport = await Page.Locator("#scenario-create-order-fails")
            .EvaluateAsync<bool>("el => { var r = el.getBoundingClientRect(); return r.top >= -10 && r.top < window.innerHeight; }");
        Assert.True(firstInViewport, "First target should be in viewport");

        await Page.EvaluateAsync("window.scrollTo(0,0)");
        await Page.WaitForTimeoutAsync(300);

        await links.Nth(1).ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        var secondInViewport = await Page.Locator("#scenario-process-payment-fails")
            .EvaluateAsync<bool>("el => { var r = el.getBoundingClientRect(); return r.top >= -10 && r.top < window.innerHeight; }");
        Assert.True(secondInViewport, "Second target should be in viewport after clicking second link");
    }

    [Fact]
    public async Task Sequential_cluster_link_clicks_without_scrolling_back()
    {
        var url = GenerateClusterReport("ClusterSequentialDirect.html", CreateMultiFeatureClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();
        var links = Page.Locator(".failure-cluster-scenario-link");

        await links.First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        await links.Nth(1).EvaluateAsync("el => el.click()");
        await Page.WaitForTimeoutAsync(800);

        var inViewport = await Page.Locator("#scenario-process-payment-fails")
            .EvaluateAsync<bool>("el => { var r = el.getBoundingClientRect(); return r.top >= -10 && r.top < window.innerHeight; }");
        Assert.True(inViewport, "Second target should be in viewport even when clicking link without scrolling back first");
    }

    // ── Parameterized scenario navigation ──

    [Fact]
    public async Task Cluster_link_navigates_to_parameterized_row()
    {
        var url = GenerateClusterReport("ClusterParamRow.html", CreateParameterizedClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();
        var links = Page.Locator(".failure-cluster-scenario-link");
        Assert.True(await links.CountAsync() >= 2);

        await links.First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        Assert.NotNull(await Page.Locator("details.scenario-parameterized").GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Cluster_link_activates_correct_parameterized_row()
    {
        var url = GenerateClusterReport("ClusterParamActive.html", CreateParameterizedClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();
        await Page.Locator(".failure-cluster-scenario-link").Nth(1).ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        var cls = await Page.Locator("#scenario-checkout-completes-us-usd").GetAttributeAsync("class");
        Assert.Contains("row-active", cls!);
    }

    [Fact]
    public async Task Cluster_link_to_parameterized_row_shows_correct_detail_panel()
    {
        var url = GenerateClusterReport("ClusterParamPanel.html", CreateParameterizedClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();
        await Page.Locator(".failure-cluster-scenario-link").Nth(1).ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        var activePanel = await Page.EvaluateAsync<string>("""
            () => {
                var row = document.getElementById('scenario-checkout-completes-us-usd');
                if (!row) return 'row-not-found';
                var idx = row.getAttribute('data-row-idx');
                var onclick = row.getAttribute('onclick');
                if (!onclick) return 'no-onclick';
                var match = onclick.match(/selectRow\(this,'([^']+)'\)/);
                if (!match) return 'no-prefix-match';
                var prefix = match[1];
                var panel = document.getElementById(prefix + '-detail-' + idx);
                if (!panel) return 'panel-not-found:' + prefix + '-detail-' + idx;
                return panel.style.display !== 'none' ? 'visible' : 'hidden';
            }
        """);
        Assert.Equal("visible", activePanel);
    }

    // ── Duplicate scenario names across features ──

    [Fact]
    public async Task Cluster_link_with_duplicate_names_navigates_to_correct_feature()
    {
        var url = GenerateClusterReport("ClusterDuplicateNames.html", CreateDuplicateNameClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();
        var links = Page.Locator(".failure-cluster-scenario-link");
        Assert.True(await links.CountAsync() >= 2);

        await links.Nth(1).ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        var features = Page.Locator("details.feature");
        Assert.True(await features.CountAsync() >= 2);
        Assert.NotNull(await features.Nth(1).GetAttributeAsync("open"));

        var secondScenario = Page.Locator("#scenario-health-check-fails-2");
        var inViewport = await secondScenario.EvaluateAsync<bool>("el => { var r = el.getBoundingClientRect(); return r.top >= -10 && r.top < window.innerHeight; }");
        Assert.True(inViewport, "Second occurrence of duplicate-named scenario should be in viewport");
    }

    // ── URL hash update ──

    [Fact]
    public async Task Cluster_link_updates_url_hash()
    {
        var url = GenerateClusterReport("ClusterUrlHash.html", CreateBasicClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();
        await Page.Locator(".failure-cluster-scenario-link").First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        Assert.Contains("#scenario-create-order-fails-on-timeout", Page.Url);
    }

    // ── Multi-cluster navigation ──

    [Fact]
    public async Task Clicking_links_between_different_clusters()
    {
        var url = GenerateClusterReport("ClusterMultiCluster.html", CreateMultiClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        var clusterSummaries = Page.Locator(".failure-cluster > summary");
        var summaryCount = await clusterSummaries.CountAsync();
        for (var i = 0; i < summaryCount; i++)
            await clusterSummaries.Nth(i).ClickAsync();

        var allLinks = Page.Locator(".failure-cluster-scenario-link");
        Assert.True(await allLinks.CountAsync() >= 4);

        await allLinks.First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        var firstInViewport = await Page.Locator("#scenario-endpoint-a-timeout")
            .EvaluateAsync<bool>("el => { var r = el.getBoundingClientRect(); return r.top >= -10 && r.top < window.innerHeight; }");
        Assert.True(firstInViewport);

        await Page.EvaluateAsync("window.scrollTo(0,0)");
        await Page.WaitForTimeoutAsync(300);

        await allLinks.Nth(2).ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        var secondInViewport = await Page.Locator("#scenario-endpoint-c-auth-error")
            .EvaluateAsync<bool>("el => { var r = el.getBoundingClientRect(); return r.top >= -10 && r.top < window.innerHeight; }");
        Assert.True(secondInViewport, "Should navigate to scenario in second cluster");
    }

    // ── Edge case: cluster link when feature is already open ──

    [Fact]
    public async Task Cluster_link_works_when_feature_already_expanded()
    {
        var url = GenerateClusterReport("ClusterFeatureAlreadyOpen.html", CreateBasicClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        await Page.Locator("details.feature > summary").First.ClickAsync();
        await Page.WaitForTimeoutAsync(200);

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();
        await Page.Locator(".failure-cluster-scenario-link").First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        var target = Page.Locator("#scenario-create-order-fails-on-timeout");
        var inViewport = await target.EvaluateAsync<bool>("el => { var r = el.getBoundingClientRect(); return r.top >= -10 && r.top < window.innerHeight; }");
        Assert.True(inViewport);
        Assert.NotNull(await target.GetAttributeAsync("open"));
    }

    // ── Verify cluster feature prefix is displayed ──

    [Fact]
    public async Task Cluster_links_show_feature_name_prefix()
    {
        var url = GenerateClusterReport("ClusterFeaturePrefix.html", CreateMultiFeatureClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();

        var prefixSpans = Page.Locator(".failure-cluster-scenarios li span");
        Assert.True(await prefixSpans.CountAsync() >= 2);
        await Expect(prefixSpans.First).ToContainTextAsync("Order Feature");
        await Expect(prefixSpans.Nth(1)).ToContainTextAsync("Payment Feature");
    }

    // ── Parameterized group parent opening ──

    [Fact]
    public async Task Cluster_link_opens_parameterized_group_parent_details()
    {
        var url = GenerateClusterReport("ClusterParamParentOpen.html", CreateParameterizedClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        var paramGroup = Page.Locator("details.scenario-parameterized");
        Assert.Null(await paramGroup.GetAttributeAsync("open"));

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();
        await Page.Locator(".failure-cluster-scenario-link").First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        Assert.NotNull(await Page.Locator("details.feature").First.GetAttributeAsync("open"));
        Assert.NotNull(await paramGroup.GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Cluster_link_to_parameterized_row_scrolls_to_visible_row()
    {
        var url = GenerateClusterReport("ClusterParamVisible.html", CreateParameterizedClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();
        await Page.Locator(".failure-cluster-scenario-link").First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        var inViewport = await Page.Locator("#scenario-checkout-completes-uk-gbp")
            .EvaluateAsync<bool>("el => { var r = el.getBoundingClientRect(); return r.top >= -10 && r.top < window.innerHeight; }");
        Assert.True(inViewport, "Parameterized row should be visible in viewport after cluster link click");
    }

    // ── Second click after first (the primary bug in the original report) ──

    [Fact]
    public async Task Second_cluster_link_works_after_first_click_without_manual_scroll()
    {
        var url = GenerateClusterReport("ClusterSecondNoScroll.html", CreateMultiFeatureClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();
        var links = Page.Locator(".failure-cluster-scenario-link");

        await links.First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        var firstInViewport = await Page.Locator("#scenario-create-order-fails")
            .EvaluateAsync<bool>("el => { var r = el.getBoundingClientRect(); return r.top >= -10 && r.top < window.innerHeight; }");
        Assert.True(firstInViewport, "First link should work");

        await links.Nth(1).EvaluateAsync("el => el.click()");
        await Page.WaitForTimeoutAsync(800);

        var secondTarget = Page.Locator("#scenario-process-payment-fails");
        var secondInViewport = await secondTarget.EvaluateAsync<bool>("el => { var r = el.getBoundingClientRect(); return r.top >= -10 && r.top < window.innerHeight; }");
        Assert.True(secondInViewport, "Second cluster link should navigate correctly without needing to scroll back first");
        Assert.NotNull(await secondTarget.GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Third_cluster_link_works_after_first_two_clicks()
    {
        var url = GenerateClusterReport("ClusterThirdClick.html", CreateMultiFeatureClusterData());
        await Page.GotoAsync(url);
        await Page.Locator(".failure-clusters").WaitForAsync();

        await Page.Locator(".failure-cluster > summary").First.ClickAsync();
        var links = Page.Locator(".failure-cluster-scenario-link");
        Assert.True(await links.CountAsync() >= 3);

        await links.First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        await links.Nth(1).EvaluateAsync("el => el.click()");
        await Page.WaitForTimeoutAsync(800);

        await links.Nth(2).EvaluateAsync("el => el.click()");
        await Page.WaitForTimeoutAsync(800);

        var inViewport = await Page.Locator("#scenario-calculate-shipping-fails")
            .EvaluateAsync<bool>("el => { var r = el.getBoundingClientRect(); return r.top >= -10 && r.top < window.innerHeight; }");
        Assert.True(inViewport, "Third cluster link should navigate correctly after two prior clicks");
    }
}
