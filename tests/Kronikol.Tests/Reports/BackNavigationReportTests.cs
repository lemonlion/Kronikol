using System.Text.RegularExpressions;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// What the generator writes for Back and Forward as the report's navigation (5.2.0, plans/BACK_BUTTON_NAVIGATION_PLAN.md
/// section 7.2, C1 to C6): the navigation script in every page the report's scripts run in, plain links where the
/// History tab and the failure clusters had scripts of their own, where the page starts, and two fixes found on the way
/// (a parameterized group's id, and the sort a browser could restore). What Back does is held by the BackForward*
/// classes in the end-to-end project.
/// </summary>
public class BackNavigationReportTests
{
    private static readonly DateTime Start = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private const string ComponentSource = "@startuml\nrectangle \"Caller\" as caller\nrectangle \"OrderService\" as svc\ncaller --> svc\n@enduml";

    private static string Script => ReportGenerator.LoadResource("report-navigation-script.js");

    private static Feature[] Features(bool failures = false) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario
                {
                    Id = "a", DisplayName = "Create order", Result = failures ? ExecutionResult.Failed : ExecutionResult.Passed,
                    ErrorMessage = failures ? "Expected: rejected\nActual: created" : null, Duration = TimeSpan.FromMilliseconds(120)
                },
                new Scenario
                {
                    Id = "b", DisplayName = "Reject order", Result = failures ? ExecutionResult.Failed : ExecutionResult.Passed,
                    ErrorMessage = failures ? "Expected: rejected\nActual: created" : null, Duration = TimeSpan.FromMilliseconds(80)
                }
            ]
        },
        new Feature { DisplayName = "Refunds", Scenarios = [new Scenario { Id = "c", DisplayName = "Refund", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(40) }] }
    ];

    private static string Generate(string fileName, Feature[] features, bool includeTestRunData = true, ReportToggleDefaults? toggles = null,
        bool groupParameterizedTests = false)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features, Start, Start.AddSeconds(5), null, Path.Combine(Path.GetTempPath(), "kronikol-backnav-" + Guid.NewGuid().ToString("N")[..8] + "-" + fileName),
            "Test", includeTestRunData,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            componentDiagramPlantUml: ComponentSource, groupParameterizedTests: groupParameterizedTests,
            toggleDefaults: toggles is null ? null
                : ReportToggleDefaultsResolver.Resolve(new ReportConfigurationOptions { TestRunReportToggleDefaults = toggles }, specifications: false));
        try { return ReportHtml.Read(path); }
        finally { File.Delete(path); }
    }

    private static string Head(string html)
    {
        var start = html.IndexOf("<head>", StringComparison.Ordinal);
        // the head's own close, the one a line break parts from <body>: report-export-function.js, which the head holds, carries
        // "</head><body>" in a string, ahead of the scripts that follow it
        var close = Regex.Match(html, @"</head>\s+<body>");
        var end = close.Success ? close.Index : -1;
        Assert.True(start >= 0 && end > start, "the page has a head");
        return html[start..end];
    }

    [Fact]
    public void C1_The_navigation_script_is_in_the_head_of_a_run_report_and_of_the_specifications_report()
    {
        // the head is what Export Filtered HTML copies (report-export-function.js), so an exported page has it too
        Assert.Contains(Script, Head(Generate("Nav_Run.html", Features())));
        Assert.Contains(Script, Head(Generate("Nav_Specs.html", Features(), includeTestRunData: false)));
        Assert.Contains("window.kronikol_nav = {", Script);
    }

    [Fact]
    public void C2_The_failure_clusters_links_are_plain_links()
    {
        var html = Generate("Nav_Links.html", Features(failures: true));
        var clusters = Regex.Matches(html, @"<a class=""failure-cluster-scenario-link""[^>]*>");
        Assert.Equal(2, clusters.Count);
        Assert.All(clusters, m => Assert.DoesNotContain("onclick", m.Value));
        Assert.All(clusters, m => Assert.Matches(@"^<a class=""failure-cluster-scenario-link"" href=""#scenario-[a-z0-9-]+"" title=""Go to this scenario"">$", m.Value));
        // the History tab's links: HistoryHtmlTests.The_history_tabs_links_are_plain_links_that_keep_their_address
    }

    [Theory]
    [InlineData(null, null, null, "features", "full")]
    [InlineData(true, null, null, "timeline", "full")]
    [InlineData(null, true, null, "components", "full")]
    [InlineData(null, null, true, "features", "summary")]
    public void C3_The_page_says_which_tab_and_view_it_opens_on(bool? timeline, bool? components, bool? summary, string tab, string view)
    {
        var toggles = new ReportToggleDefaults { ScenarioTimelineVisible = timeline, ComponentDiagramVisible = components, FeaturesSummaryOpen = summary };
        var html = Generate($"Nav_Start_{tab}_{view}.html", Features(), toggles: toggles);

        Assert.Contains($"<div class=\"report-tabs\" role=\"tablist\" aria-label=\"Report sections\" data-start-tab=\"{tab}\">", html);
        Assert.Contains($"<div class=\"view-switch\" role=\"tablist\" aria-label=\"Features view\" data-start-view=\"{view}\">", html);
    }

    [Fact]
    public void C4_The_navigation_script_names_no_tab_and_no_history_section()
    {
        // a report without history names no `history-` anywhere (DefaultReportCleanTests); the script reads the tabs from
        // the page, so a tab added later needs nothing of it
        Assert.DoesNotContain("history-", Script);
        foreach (var key in new[] { "features", "timeline", "components", "history", "diagnostics" })
        {
            Assert.DoesNotContain($"'{key}'", Script);
            Assert.DoesNotContain($"\"{key}\"", Script);
            Assert.DoesNotContain($"#tab-{key}", Script);
            Assert.DoesNotContain($"panel-{key}", Script);
        }
    }

    [Fact]
    public void C5_Two_parameterized_groups_of_one_name_get_two_ids()
    {
        static Scenario Example(string id, string tier) => new()
        {
            Id = id, DisplayName = "Discount applies", Result = ExecutionResult.Passed, OutlineId = "discount",
            ExampleValues = new Dictionary<string, string> { ["tier"] = tier }, Duration = TimeSpan.FromMilliseconds(10)
        };
        var features = new[]
        {
            new Feature { DisplayName = "Pricing", Scenarios = [Example("p1", "gold"), Example("p2", "silver")] },
            new Feature { DisplayName = "Promotions", Scenarios = [Example("q1", "gold"), Example("q2", "bronze")] }
        };
        var html = Generate("Nav_Groups.html", features, groupParameterizedTests: true);

        var groups = Regex.Matches(html, @"<details class=""scenario scenario-parameterized[^>]* id=""([^""]+)""").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(2, groups.Length);
        Assert.Equal(2, groups.Distinct().Count());
        var ids = Regex.Matches(html, @"\sid=""(scenario-[^""]+)""").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void C6_The_features_sort_is_not_restored_by_the_browser()
    {
        // every other control of the page already says so: a browser that restores form values over a reload or a
        // return by Back could show an order the cards are not in
        var html = Generate("Nav_Sort.html", Features());
        Assert.Contains("<select id=\"features-sort\" class=\"sort-select\" autocomplete=\"off\"", html);
    }
}
