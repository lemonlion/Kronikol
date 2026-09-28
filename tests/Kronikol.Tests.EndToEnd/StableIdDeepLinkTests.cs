using Microsoft.Playwright;
using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// <c>#sid-&lt;stableId&gt;</c> — the deep link <c>kronikol query</c> and <c>Failures.md</c> print
/// (LLM_FRIENDLY_PLAN M2.1). The report already answered to <c>#scenario-&lt;slug&gt;</c>, but a slug
/// is the display name: it moves on a rename and two features naming a scenario the same thing get
/// the same one. These facts drive the browser rather than the markup — the anchor has to survive
/// the filter pass that runs after it, and an example row has to be reachable inside a group that
/// starts collapsed.
/// </summary>
[Collection(PlaywrightCollections.Scenarios)]
public class StableIdDeepLinkTests : PlaywrightTestBase
{
    public StableIdDeepLinkTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string PlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "Service" as svc
        caller -> svc : GET /api/test
        svc --> caller : 200 OK
        @enduml
        """;

    /// <summary>Two features naming a scenario the same thing — one slug, two stable ids.</summary>
    private static Feature[] SameNameInTwoFeatures() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario
                {
                    Id = "c1", DisplayName = "Pay", Result = ExecutionResult.Failed,
                    Duration = TimeSpan.FromSeconds(2), ErrorMessage = "card declined",
                    Categories = ["billing"],
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "it charges", Status = ExecutionResult.Failed }]
                }
            ]
        },
        new Feature
        {
            DisplayName = "Refunds",
            Scenarios =
            [
                new Scenario
                {
                    Id = "r1", DisplayName = "Pay", Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromSeconds(1), Categories = ["billing"],
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "it refunds", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];

    private static Feature[] AnOutline() =>
    [
        new Feature
        {
            DisplayName = "Pricing",
            Scenarios =
            [
                new Scenario
                {
                    Id = "p1", DisplayName = "Discount applies", Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromSeconds(1), OutlineId = "discount",
                    ExampleValues = new Dictionary<string, string> { ["tier"] = "gold" },
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "10 percent off", Status = ExecutionResult.Passed }]
                },
                new Scenario
                {
                    Id = "p2", DisplayName = "Discount applies", Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromSeconds(1), OutlineId = "discount",
                    ExampleValues = new Dictionary<string, string> { ["tier"] = "silver" },
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "5 percent off", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];

    private static Feature[] AnOutlineWithFlatValues()
    {
        var features = AnOutline();
        features[0].Scenarios[0].ExampleFlatValues = new Dictionary<string, string> { ["tier"] = "gold" };
        features[0].Scenarios[1].ExampleFlatValues = new Dictionary<string, string> { ["tier"] = "silver" };
        return features;
    }

    /// <summary>
    /// The id the report will actually carry, resolved the way the generator resolves it.
    ///
    /// <para>Since 3.1.0 a stableId is scoped to the test SUITE, and <c>GenerateHtmlReport</c> resolves
    /// that suite itself when the caller does not name one - so a fact that recomputes an id has to
    /// resolve it the same way. Passing <c>null</c> here asserted against ids no report has written since
    /// the suite landed: the element simply was not there, and the browser said
    /// <c>Cannot read properties of null</c> seven times.</para>
    /// </summary>
    private static string Sid(string feature, string scenario, string? outlineId = null,
        IReadOnlyDictionary<string, string>? exampleValues = null) =>
        ScenarioStableId.Compute(RunSuite.Current, feature, scenario, outlineId, exampleValues);

    private string Generate(string fileName, Feature[] features)
    {
        var diagrams = features.SelectMany(f => f.Scenarios)
            .Select(s => new DiagramAsCode(s.Id, "", PlantUmlSource)).ToArray();

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Deep Link Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            groupParameterizedTests: true);

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    [Fact]
    public async Task A_sid_link_opens_the_scenario_it_names_and_not_its_namesake()
    {
        var url = Generate("SidDeepLink.html", SameNameInTwoFeatures());
        var wanted = Sid("Refunds", "Pay");
        var other = Sid("Checkout", "Pay");

        await Page.GotoAsync(url + "#sid-" + wanted);
        await Page.WaitForFunctionAsync(
            $"() => document.querySelector('[data-stable-id=\"{wanted}\"]').hasAttribute('open')",
            null, new() { PollingInterval = 200 });

        // The namesake in the other feature stays shut — which the slug anchor could never manage,
        // because both scenarios answer to `#scenario-pay`.
        Assert.False(await Page.Locator($"[data-stable-id='{other}']").First.EvaluateAsync<bool>("el => el.hasAttribute('open')"));
        // And the feature around the target is opened, so the scenario is actually on screen.
        Assert.True(await Page.Locator($"[data-stable-id='{wanted}']").First.EvaluateAsync<bool>(
            "el => el.closest('details.feature').hasAttribute('open')"));
    }

    [Fact]
    public async Task Filtering_after_following_a_link_keeps_the_link_in_the_url()
    {
        var url = Generate("SidKeptOnFilter.html", SameNameInTwoFeatures());
        var wanted = Sid("Refunds", "Pay");

        await Page.GotoAsync(url + "#sid-" + wanted);
        await Page.WaitForFunctionAsync(
            $"() => document.querySelector('[data-stable-id=\"{wanted}\"]').hasAttribute('open')",
            null, new() { PollingInterval = 200 });

        // Narrowing the list rewrites the hash. Before M2.1 that rewrite threw the anchor away, so
        // the URL somebody copied back out no longer pointed at anything.
        await Page.Locator(".status-toggle[data-status='Passed']").First.ClickAsync();
        await Page.WaitForFunctionAsync("() => window.location.hash.indexOf('status=') > 0",
            null, new() { PollingInterval = 200 });

        var hash = await Page.EvaluateAsync<string>("() => window.location.hash");
        Assert.StartsWith("#sid-" + wanted, hash);
        Assert.Contains("status=Passed", hash);
    }

    [Fact]
    public async Task A_link_that_carries_filter_state_applies_both()
    {
        var url = Generate("SidWithFilters.html", SameNameInTwoFeatures());
        var wanted = Sid("Checkout", "Pay");

        await Page.GotoAsync($"{url}#sid-{wanted}&status=Failed");
        await Page.WaitForFunctionAsync(
            $"() => document.querySelector('[data-stable-id=\"{wanted}\"]').hasAttribute('open')",
            null, new() { PollingInterval = 200 });

        // The filter is live — the anchor rides in front of the grammar rather than replacing it.
        Assert.True(await Page.Locator(".status-toggle[data-status='Failed']").First
            .EvaluateAsync<bool>("el => el.classList.contains('status-active')"));
    }

    [Fact]
    public async Task Clearing_every_filter_keeps_the_link_but_drops_the_filters()
    {
        var url = Generate("SidSurvivesClearAll.html", SameNameInTwoFeatures());
        var wanted = Sid("Checkout", "Pay");

        await Page.GotoAsync($"{url}#sid-{wanted}&status=Failed");
        await Page.WaitForFunctionAsync(
            $"() => document.querySelector('[data-stable-id=\"{wanted}\"]').hasAttribute('open')",
            null, new() { PollingInterval = 200 });

        // Clear All wipes the whole fragment today, link and all. Clearing filters is not the same
        // act as leaving the scenario somebody navigated to.
        await Page.Locator("button.export-btn", new() { HasTextString = "Clear All" }).First.ClickAsync();
        await Page.WaitForFunctionAsync("() => window.location.hash.indexOf('status=') < 0",
            null, new() { PollingInterval = 200 });

        Assert.Equal("#sid-" + wanted, await Page.EvaluateAsync<string>("() => window.location.hash"));
    }

    [Fact]
    public async Task A_link_pasted_into_an_open_report_still_resolves()
    {
        // parse_url_hash only ever ran on DOMContentLoaded, so pasting `#sid-…` into the address bar
        // of a report that was already open did nothing at all — the one way a human actually uses
        // a link an agent handed them mid-session.
        var url = Generate("SidHashChange.html", SameNameInTwoFeatures());
        var wanted = Sid("Refunds", "Pay");

        await Page.GotoAsync(url);
        Assert.False(await Page.Locator($"[data-stable-id='{wanted}']").First
            .EvaluateAsync<bool>("el => el.hasAttribute('open')"));

        await Page.EvaluateAsync($"() => {{ window.location.hash = 'sid-{wanted}'; }}");

        await Page.WaitForFunctionAsync(
            $"() => document.querySelector('[data-stable-id=\"{wanted}\"]').hasAttribute('open')",
            null, new() { PollingInterval = 200 });
    }

    [Fact]
    public async Task A_sid_link_to_an_example_row_opens_the_group_and_selects_the_row()
    {
        var url = Generate("SidExampleRow.html", AnOutline());
        var silver = Sid("Pricing", "Discount applies", "discount",
            new Dictionary<string, string> { ["tier"] = "silver" });

        await Page.GotoAsync(url + "#sid-" + silver);

        // The row lives inside a parameterized group that starts collapsed; the deep link has to open
        // every enclosing <details>, not only the feature.
        await Page.WaitForFunctionAsync(
            $"() => {{ var r = document.querySelector('[data-stable-id=\"{silver}\"]'); return r && r.closest('details.scenario-parameterized').hasAttribute('open'); }}",
            null, new() { PollingInterval = 200 });

        Assert.True(await Page.Locator($"tr[data-stable-id='{silver}']").First
            .EvaluateAsync<bool>("el => el.classList.contains('row-active')"));
    }

    [Fact]
    public async Task A_link_to_a_row_lands_on_the_copy_that_is_actually_displayed()
    {
        // With flattened values the outline renders its rows twice and hides one of the two tables.
        // Both copies carry the stable id, so the link has to pick the displayed one — otherwise it
        // selects a row inside `display:none` and the reader sees nothing happen.
        var url = Generate("SidFlatRow.html", AnOutlineWithFlatValues());
        var silver = Sid("Pricing", "Discount applies", "discount",
            new Dictionary<string, string> { ["tier"] = "silver" });

        await Page.GotoAsync(url + "#sid-" + silver);

        await Page.WaitForFunctionAsync(
            $"() => {{ var r = document.querySelector('table.param-table-flat tr[data-stable-id=\"{silver}\"]'); return r && r.classList.contains('row-active'); }}",
            null, new() { PollingInterval = 200 });

        // The hidden grouped copy is left alone.
        Assert.False(await Page.Locator($"table.param-table-grouped tr[data-stable-id='{silver}']").First
            .EvaluateAsync<bool>("el => el.classList.contains('row-active')"));
    }

    [Fact]
    public async Task The_slug_anchor_for_an_example_row_now_opens_its_group_too()
    {
        // Ride-along fix: `#scenario-<row slug>` found the <tr> by id, set `open` on it (a no-op on a
        // table row) and scrolled to something still hidden inside a collapsed group. It never opened
        // the group. Same walk as the sid path fixes both.
        var url = Generate("SlugRowAnchor.html", AnOutline());
        var slug = ReportGenerator.GenerateScenarioAnchorId("Discount applies");

        await Page.GotoAsync(url + "#" + slug);

        await Page.WaitForFunctionAsync(
            $"() => {{ var r = document.getElementById('{slug}'); return r && r.closest('details.scenario-parameterized').hasAttribute('open'); }}",
            null, new() { PollingInterval = 200 });
    }

    [Fact]
    public async Task A_sid_link_far_down_a_long_report_lands_on_its_scenario()
    {
        // A feature has content-visibility: auto, so until the browser first draws one it counts as a
        // 500 px placeholder, where a closed feature is about 60 px. A smooth scroll aimed across such
        // features is aimed too far: they shrink to their true size as it passes them, and it ends at the
        // bottom of the page with the scenario far above the screen (plans/DOORSTEP_PLAN.md F28).
        var url = GenerateLongReport("SidDeepLinkFarDown.html");
        var wanted = Sid("Feature 30", "Scenario 30.2");

        await Page.GotoAsync(url + "#sid-" + wanted);
        await WaitForScrollToSettleAsync();

        Assert.True(await SummaryIsOnScreenAsync($"[data-stable-id='{wanted}']"),
            "The linked scenario's title should be on screen once the page has stopped scrolling");
    }

    [Fact]
    public async Task At_a_phone_width_a_sid_link_lands_on_the_page_the_folded_filters_leave()
    {
        // At 768 px and below the report folds its filter panel away as it loads. The link's scroll has
        // to be aimed at the page that fold leaves: aimed before it, the scroll ends the panel's height
        // too far down, with the scenario above the screen (plans/DOORSTEP_PLAN.md F28). The scenario is
        // in the first feature, so no placeholder lies between it and the top of the page.
        await Page.SetViewportSizeAsync(390, 844);
        var url = GenerateLongReport("SidDeepLinkPhone.html");
        var wanted = Sid("Feature 01", "Scenario 01.2");

        await Page.GotoAsync(url + "#sid-" + wanted);
        await WaitForScrollToSettleAsync();

        Assert.True(await SummaryIsOnScreenAsync($"[data-stable-id='{wanted}']"),
            "The linked scenario's title should be on screen once the page has stopped scrolling");
    }
}
