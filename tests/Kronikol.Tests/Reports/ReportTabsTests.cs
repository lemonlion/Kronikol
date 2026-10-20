using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The report's tabs and panels (5.0.0, plans/OPTION_C_PLAN.md S3, D20): the sections that sat above the features as
/// toggled panels (the Scenario Timeline, the Component Diagram, History and Report diagnostics) are tabs of the report
/// bar, each with its panel, written by the generator; the features, with the failure clusters and the background calls
/// at their top (Q4), are the Features tab. A page with nothing beside its features has no tabs.
/// </summary>
public class ReportTabsTests
{
    private static readonly DateTime Start = new(2026, 10, 16, 17, 27, 11, DateTimeKind.Utc);

    private const string ComponentSource = "@startuml\nrectangle \"Caller\" as caller\nrectangle \"OrderService\" as svc\ncaller --> svc\n@enduml";

    private static Feature[] Features(bool durations = true, bool failure = false) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario
                {
                    Id = "a", DisplayName = "Create order", Result = failure ? ExecutionResult.Failed : ExecutionResult.Passed,
                    ErrorMessage = failure ? "Expected: rejected\nActual: created" : null, Duration = durations ? TimeSpan.FromMilliseconds(120) : null
                },
                new Scenario
                {
                    Id = "b", DisplayName = "Reject order", Result = failure ? ExecutionResult.Failed : ExecutionResult.Passed,
                    ErrorMessage = failure ? "Expected: rejected\nActual: created" : null, Duration = durations ? TimeSpan.FromMilliseconds(80) : null
                }
            ]
        },
        new Feature { DisplayName = "Refunds", Scenarios = [new Scenario { Id = "c", DisplayName = "Refund", Result = ExecutionResult.Passed }] }
    ];

    private static string Generate(string fileName, Feature[] features, bool includeTestRunData = true, string? component = ComponentSource,
        IReadOnlyList<DiagnosticEntry>? diagnostics = null, BackgroundCalls? background = null, ReportToggleDefaults? toggles = null)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features, Start, Start.AddSeconds(5), null, fileName, "Test", includeTestRunData,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            componentDiagramPlantUml: component, diagnostics: diagnostics, showReportDiagnostics: diagnostics is not null,
            background: background, toggleDefaults: toggles is null ? null
                : ReportToggleDefaultsResolver.Resolve(new ReportConfigurationOptions { TestRunReportToggleDefaults = toggles }, specifications: false));
        return File.ReadAllText(path);
    }

    private static readonly DiagnosticEntry[] TwoDiagnostics =
    [
        new(DiagnosticKind.CaptureDegraded, "tap-a: 1 oversize payload skipped"),
        new(DiagnosticKind.MalformedLine, "Skipped a capture line")
    ];

    // each tab titled with what it shows (5.0.0, plans/OPTION_C_PLAN.md D49); none of these runs paid a warm-up
    private static string Tab(string key, string label, bool selected, string count = "") =>
        $"<button class=\"report-tab\" id=\"tab-{key}\" role=\"tab\" aria-controls=\"panel-{key}\" aria-selected=\"{(selected ? "true" : "false")}\" tabindex=\"{(selected ? "0" : "-1")}\" data-tab=\"{key}\" title=\"{TabTitle(key)}\">{label}{count}</button>";

    private static string TabTitle(string key) => key switch
    {
        "features" => "The features and their scenarios, with the filters applied",
        "timeline" => "Every scenario as a bar, slowest first",
        "components" => "The services, databases and queues the scenarios called, drawn as one diagram",
        "history" => "This run against the earlier runs on the same branch: what is failing, flaky, fixed, slower, new or gone",
        _ => "What Kronikol noticed while building this report, such as calls it could not put under a scenario"
    };

    private static string Panel(string key, bool shown) =>
        $"<div class=\"report-panel\" id=\"panel-{key}\" role=\"tabpanel\" aria-labelledby=\"tab-{key}\" data-panel=\"{key}\"{(shown ? "" : " hidden")}>";

    [Fact]
    public void The_bar_opens_with_a_tab_for_each_section_and_features_selected()
    {
        var html = Generate("Tabs_All.html", Features(), diagnostics: TwoDiagnostics);

        // the tabs share the bar's top row with its filters (S7, D24), after the mark where the bar pins
        Assert.Contains("<div class=\"nav-mark\" aria-hidden=\"true\"></div><div class=\"toolbar-row report-nav on-features\"><div class=\"nav-row\"><div class=\"report-tabs\" role=\"tablist\" aria-label=\"Report sections\">"
                        + Tab("features", "Features", true, "<span class=\"tab-n\">2</span>")
                        + Tab("timeline", "Timeline", false)
                        + Tab("components", "Components", false)
                        + Tab("diagnostics", "Diagnostics", false, "<span class=\"tab-n\">2</span>")
                        + "</div><div class=\"nav-filters\"", html);
    }

    [Fact]
    public void Each_section_is_in_its_own_panel_after_the_bar_and_only_the_features_panel_shows()
    {
        var html = Generate("Tabs_Panels.html", Features(), diagnostics: TwoDiagnostics);

        var panels = html.IndexOf("<div class=\"report-panels\">", StringComparison.Ordinal);
        Assert.True(panels > html.IndexOf("<div class=\"report-tabs\"", StringComparison.Ordinal), "the panels follow the bar");
        var features = html.IndexOf(Panel("features", true), StringComparison.Ordinal);
        var timeline = html.IndexOf(Panel("timeline", false) + "<div id=\"scenario-timeline\" class=\"scenario-timeline\">", StringComparison.Ordinal);
        var components = html.IndexOf(Panel("components", false) + "<div id=\"component-diagram\" class=\"component-diagram-section\">", StringComparison.Ordinal);
        var diagnostics = html.IndexOf(Panel("diagnostics", false) + "<details class=\"report-diagnostics\" open><summary tabindex=\"-1\" title=\"What Kronikol noticed while building this report, such as calls it could not put under a scenario\">", StringComparison.Ordinal);
        Assert.True(features > panels, "the features panel is in the panels");
        Assert.True(timeline > html.IndexOf("<div id=\"report-content\"", StringComparison.Ordinal), "the timeline panel follows the features");
        Assert.True(components > timeline, "then the components panel");
        Assert.True(diagnostics > components, "then the diagnostics panel, open, its summary out of the tab order");
    }

    [Fact]
    public void The_timeline_and_component_toggles_are_gone_with_their_functions()
    {
        var html = Generate("Tabs_NoToggles.html", Features());

        Assert.DoesNotContain("class=\"timeline-toggle", html);
        Assert.DoesNotContain("function toggle_timeline(", html);
        Assert.DoesNotContain("function toggle_component_diagram(", html);
        Assert.DoesNotContain("<div id=\"scenario-timeline\" class=\"scenario-timeline\" style=", html);
        Assert.DoesNotContain("<div id=\"component-diagram\" class=\"component-diagram-section\" style=", html);
    }

    [Fact]
    public void The_failure_clusters_and_background_calls_open_the_features_panel()
    {
        var calls = Enumerable.Range(0, 2).Select(_ => new RequestResponseLog("Create order", "a", HttpMethod.Post, null,
            new Uri("https://ledger.example.com/api/orders"), [], "Ledger", "Caller", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)).ToList();
        var background = new BackgroundCalls(2, [new BackgroundCallGroup("a", "Create order", 2, new DateTimeOffset(Start))], calls);

        var html = Generate("Tabs_Clusters.html", Features(failure: true), background: background);

        var panel = html.IndexOf(Panel("features", true), StringComparison.Ordinal);
        var clusters = html.IndexOf("<details class=\"failure-clusters\"", StringComparison.Ordinal);
        var backgroundCalls = html.IndexOf("<details class=\"background-calls\"", StringComparison.Ordinal);
        var content = html.IndexOf("<div id=\"report-content\"", StringComparison.Ordinal);
        Assert.True(panel >= 0 && clusters > panel && backgroundCalls > clusters && content > backgroundCalls,
            $"panel {panel}, clusters {clusters}, background calls {backgroundCalls}, features {content}");
        // a clustered scenario's feature is named through a class the tokens colour, not an inline grey
        Assert.Contains("<li><span class=\"failure-cluster-feature\">Orders &rsaquo;</span> <a class=\"failure-cluster-scenario-link\"", html);
        Assert.DoesNotContain("color:rgb(100,100,100)", html);
    }

    [Theory]
    [InlineData(true, false, "timeline")]
    [InlineData(false, true, "components")]
    [InlineData(true, true, "timeline")]
    public void A_panel_configured_visible_is_the_tab_the_page_opens_on(bool timeline, bool component, string expected)
    {
        var html = Generate($"Tabs_Start_{timeline}_{component}.html", Features(),
            toggles: new ReportToggleDefaults { ScenarioTimelineVisible = timeline, ComponentDiagramVisible = component });

        Assert.Contains("<div class=\"toolbar-row report-nav\"><div class=\"nav-row\"><div class=\"report-tabs\"", html);
        Assert.Contains(Tab("features", "Features", false, "<span class=\"tab-n\">2</span>"), html);
        Assert.Contains(Tab(expected, expected == "timeline" ? "Timeline" : "Components", true), html);
        Assert.Contains(Panel("features", false), html);
        Assert.Contains(Panel(expected, true), html);
    }

    [Fact]
    public void A_page_with_nothing_beside_its_features_has_no_tabs()
    {
        var html = Generate("Tabs_None.html", Features(durations: false), component: null);

        Assert.DoesNotContain("class=\"report-tabs\"", html);
        Assert.DoesNotContain("<div class=\"toolbar-row report-nav", html);
        Assert.Contains("<div class=\"toolbar-row\">", html);
        // no tab names the panel, so it is no tab panel either; its views, the table and the cards, are (S4)
        // the empty state first (S7)
        Assert.Contains("<div class=\"report-panels\"><div class=\"report-panel\" id=\"panel-features\" data-panel=\"features\"><div class=\"filters-empty\" hidden><span>No scenarios match the filters.</span><button type=\"button\" class=\"filters-empty-clear\" title=\"Clear every filter\">Clear All</button></div><div class=\"features-panel\">"
            + "<div class=\"features-table-view\" id=\"features-table-view\" role=\"tabpanel\" aria-labelledby=\"view-summary\" hidden>", html);
    }

    [Fact]
    public void The_specifications_report_keeps_its_one_view()
    {
        var html = Generate("Tabs_Specs.html", Features(durations: false), includeTestRunData: false, component: null);

        Assert.DoesNotContain("class=\"report-tabs\"", html);
    }

    [Fact]
    public void The_tabs_script_is_in_the_page()
    {
        var html = Generate("Tabs_Script.html", Features());
        var at = html.IndexOf("<script id=\"kronikol-report-tabs\">", StringComparison.Ordinal);

        Assert.True(at >= 0, "the tabs script is in the page");
        var script = html[at..html.IndexOf("</script>", at, StringComparison.Ordinal)];
        Assert.Contains("aria-selected", script);
        Assert.Contains("_renderDiagramsInContainer", script);
        Assert.Contains("'ArrowRight'", script);
        Assert.Contains("--kron-nav-h", script);
    }
}
