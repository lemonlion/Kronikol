using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The Features tab's tools line (5.0.0, plans/OPTION_C_PLAN.md S4, D21, D22, D30, D35): the Full and Summary views as
/// tabs, the summary table as the Summary view beside the feature cards, expand and collapse for features and for
/// scenarios as two icon pairs, the sort, the phone's Display settings gear, and the report bar's controls in zones.
/// </summary>
public class FeaturesToolsTests
{
    private static readonly DateTime Start = new(2026, 10, 16, 17, 27, 11, DateTimeKind.Utc);

    private static Feature[] Features(bool durations = true) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario { Id = "a", DisplayName = "Create order", Result = ExecutionResult.Failed, ErrorMessage = "boom", Duration = durations ? TimeSpan.FromMilliseconds(120) : null },
                new Scenario { Id = "b", DisplayName = "Reject order", Result = ExecutionResult.Passed, Duration = durations ? TimeSpan.FromMilliseconds(80) : null }
            ]
        },
        new Feature { DisplayName = "Refunds", Scenarios = [new Scenario { Id = "c", DisplayName = "Refund", Result = ExecutionResult.Passed, Duration = durations ? TimeSpan.FromMilliseconds(5) : null }] }
    ];

    // a diagram with a JSON payload, a step, an assertion and a database, so the report bar has every control
    private const string AllControlsSource = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc
        database "OrderDb" as db
        hnote across #black <<stepDelimiter>>: <color:white>Given an order request</color>
        caller -> svc : POST /api/orders
        note left
        {
          "id": 1
        }
        end note
        svc -> db : INSERT INTO orders
        svc --> caller : 200 OK
        note right <<assertionNote>>: status code should be OK
        @enduml
        """;

    private static string Generate(string fileName, Feature[]? features = null, bool includeTestRunData = true, ReportToggleDefaults? toggles = null,
        bool diagrams = false)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams ? [new DefaultDiagramsFetcher.DiagramAsCode("a", "", AllControlsSource)] : [],
            features ?? Features(), Start, Start.AddSeconds(5), null, fileName, "Test", includeTestRunData,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: toggles is null ? null
                : ReportToggleDefaultsResolver.Resolve(new ReportConfigurationOptions { TestRunReportToggleDefaults = toggles }, specifications: false));
        return File.ReadAllText(path);
    }

    private static string Line(string html)
    {
        var start = html.IndexOf("<div class=\"toolbar-left features-tools-line\">", StringComparison.Ordinal);
        Assert.True(start >= 0, "the tools line is missing");
        var end = html.IndexOf("<div class=\"toolbar-right\"", start, StringComparison.Ordinal);
        return html[start..end];
    }

    private static void InOrder(string text, params string[] parts)
    {
        var at = -1;
        foreach (var part in parts)
        {
            var next = text.IndexOf(part, at + 1, StringComparison.Ordinal);
            Assert.True(next > at, $"'{part}' is missing or out of order");
            at = next;
        }
    }

    [Fact]
    public void The_tools_line_holds_the_views_the_fold_groups_the_sort_and_the_gear_in_that_order()
    {
        var line = Line(Generate("FeaturesTools_Order.html"));
        InOrder(line, "<div class=\"view-switch\"", "<span class=\"fold-group\" role=\"group\" aria-label=\"Expand or collapse all features\">",
            "<span class=\"fold-group\" role=\"group\" aria-label=\"Expand or collapse all scenarios\">", "<span class=\"tools-spacer\"></span>",
            "<label class=\"sort-group\"><span class=\"sort-label\">Sort</span>", "<button type=\"button\" class=\"tools-gear\"");
        Assert.DoesNotContain("collapse-expand-all", line);
    }

    [Fact]
    public void Full_and_Summary_are_tabs_with_Full_selected()
    {
        var html = Generate("FeaturesTools_Views.html");
        Assert.Contains("<div class=\"view-switch\" role=\"tablist\" aria-label=\"Features view\">"
            + "<button type=\"button\" class=\"view-btn\" id=\"view-full\" role=\"tab\" aria-controls=\"report-content\" aria-selected=\"true\" tabindex=\"0\" data-view=\"full\" title=\"Each feature as a card with its scenarios\">Full</button>"
            + "<button type=\"button\" class=\"view-btn\" id=\"view-summary\" role=\"tab\" aria-controls=\"features-table-view\" aria-selected=\"false\" tabindex=\"-1\" data-view=\"summary\" title=\"One table row per feature, with its counts and times\">Summary</button></div>", html);
    }

    [Fact]
    public void The_summary_table_is_the_Summary_view_beside_the_feature_cards()
    {
        var html = Generate("FeaturesTools_Table.html");
        InOrder(html, "<div class=\"features-panel\">",
            "<div class=\"features-table-view\" id=\"features-table-view\" role=\"tabpanel\" aria-labelledby=\"view-summary\" hidden><div class=\"features-summary-table-wrapper\"><table class=\"feature-summary-table\">",
            "<div id=\"report-content\" role=\"tabpanel\" aria-labelledby=\"view-full\">", "<details class=\"feature\"");
        Assert.DoesNotContain("<details class=\"features-summary-details\"", html);
        // the table sits in the Features tab now, after the run band, not above it
        InOrder(html, "<section class=\"run-band\"", "<table class=\"feature-summary-table\">");
    }

    [Fact]
    public void FeaturesSummaryOpen_opens_the_page_on_the_Summary_view()
    {
        var html = Generate("FeaturesTools_SummaryStart.html", toggles: new ReportToggleDefaults { FeaturesSummaryOpen = true });
        Assert.Contains("<div class=\"toolbar-row report-nav on-features features-table-mode\">", html);
        Assert.Contains("aria-selected=\"false\" tabindex=\"-1\" data-view=\"full\" title=", html);
        Assert.Contains("aria-selected=\"true\" tabindex=\"0\" data-view=\"summary\" title=", html);
        Assert.Contains("<div class=\"features-table-view\" id=\"features-table-view\" role=\"tabpanel\" aria-labelledby=\"view-summary\">", html);
        Assert.Contains("<div id=\"report-content\" role=\"tabpanel\" aria-labelledby=\"view-full\" hidden>", html);
    }

    [Fact]
    public void The_table_names_its_column_groups_and_each_group_starts_with_Total()
    {
        var html = Generate("FeaturesTools_Groups.html");
        Assert.Contains("<thead><tr class=\"table-groups\"><th></th><th colspan=\"4\">Scenarios</th><th class=\"grp-start\" colspan=\"3\">Duration</th></tr>", html);
        Assert.Contains("<th onclick=\"sort_table(1)\">Total</th>", html);
        Assert.Contains("<th class=\"grp-start\" onclick=\"sort_table(5)\">Total</th>", html);
        Assert.Contains("<td class=\"grp-start\" data-sort=\"200\">", html);
    }

    [Fact]
    public void The_table_has_a_row_per_feature_card_in_the_same_order()
    {
        var html = Generate("FeaturesTools_RowOrder.html");
        InOrder(html, "<tr class=\"failed\"><td><button type=\"button\" class=\"feature-open\" title=\"Open this feature's card in the Full view\">Orders</button></td>",
            "<tr><td><button type=\"button\" class=\"feature-open\" title=\"Open this feature's card in the Full view\">Refunds</button></td>", "<summary class=\"h2 failed\">Orders", "<summary class=\"h2\">Refunds");
    }

    [Fact]
    public void The_sort_offers_the_columns_the_table_has_and_names_each_by_its_column()
    {
        // Report order is the page's own; Feature A-Z sorts by name (the preview's Feature A-Z was the report's order)
        var html = Generate("FeaturesTools_Sort.html");
        Assert.Contains("<select id=\"features-sort\" class=\"sort-select\" aria-label=\"Sort features\" title=\"The order the features are listed in\">"
            + "<option value=\"order\">Report order</option><option value=\"name\" data-col=\"0\">Feature A–Z</option>"
            + "<option value=\"failed\" data-col=\"3\">Failures first</option><option value=\"scenarios\" data-col=\"1\">Most scenarios</option>"
            + "<option value=\"duration\" data-col=\"5\">Slowest total</option><option value=\"average\" data-col=\"6\">Slowest average</option>"
            + "<option value=\"longest\" data-col=\"7\">Longest scenario</option></select>", html);

        var untimed = Generate("FeaturesTools_SortUntimed.html", Features(durations: false));
        Assert.Contains("<option value=\"scenarios\" data-col=\"1\">Most scenarios</option></select>", untimed);
        Assert.DoesNotContain("Slowest total", untimed);
    }

    [Theory]
    [InlineData(false, "false", "true")]
    [InlineData(true, "true", "false")]
    public void A_fold_group_starts_with_the_button_that_has_nothing_to_do_dimmed(bool expanded, string expandIdle, string collapseIdle)
    {
        var html = Generate($"FeaturesTools_Fold_{expanded}.html", toggles: new ReportToggleDefaults { FeaturesExpanded = expanded });
        Assert.Contains("<span class=\"fold-label\" aria-hidden=\"true\">features</span>"
            + $"<button type=\"button\" class=\"fold-btn fold-expand\" data-fold=\"features\" title=\"Expand all features\" aria-label=\"Expand all features\" aria-disabled=\"{expandIdle}\">", html);
        Assert.Contains($"<button type=\"button\" class=\"fold-btn fold-collapse\" data-fold=\"features\" title=\"Collapse all features\" aria-label=\"Collapse all features\" aria-disabled=\"{collapseIdle}\">", html);
    }

    [Fact]
    public void The_gear_opens_the_display_settings_and_the_report_bar_reads_payloads_then_format_then_flags()
    {
        var html = Generate("FeaturesTools_Zones.html", diagrams: true);
        Assert.Contains("<button type=\"button\" class=\"tools-gear\" aria-label=\"Display settings\" title=\"Display settings\" aria-expanded=\"false\" aria-controls=\"display-settings\">", html);
        var start = html.IndexOf("<div class=\"toolbar-right\" id=\"display-settings\">", StringComparison.Ordinal);
        Assert.True(start >= 0, "the display settings are missing");
        var right = html[start..html.IndexOf("<div class=\"report-panels\">", start, StringComparison.Ordinal)];
        InOrder(right, "<span class=\"details-radio\">", "<span class=\"diagram-format\"><select class=\"note-format-select\"",
            "<select class=\"note-width-select\"", "</span><span class=\"diagram-flags\">", "data-toggle=\"headers\"",
            "data-toggle=\"assertions\"", "data-toggle=\"steps\"", "data-toggle=\"databases\"");
    }

    [Fact]
    public void A_page_without_a_summary_table_has_no_views_and_no_sort()
    {
        var html = Generate("FeaturesTools_Specs.html", includeTestRunData: false);
        Assert.DoesNotContain("<div class=\"view-switch\"", html);
        Assert.DoesNotContain("<select id=\"features-sort\"", html);
        Assert.Contains("<span class=\"fold-group\" role=\"group\" aria-label=\"Expand or collapse all features\">", html);
    }
}
