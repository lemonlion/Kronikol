using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The filter box folded under More filters and the report bar carrying the filters (5.0.0, plans/OPTION_C_PLAN.md S7,
/// D15, D17, D24, D25): the search line ends with More filters and the rows start folded; the search field holds the
/// deep search's chip; the bar's top row carries the tabs and a group with what the filters leave showing, Clear All
/// and Filters, after a mark where the bar pins; the box has a close for when it rides in the bar; and the Features
/// panel opens with the empty state, hidden until no scenario is left.
/// </summary>
public class FilterBarTests
{
    private static readonly DateTime Start = new(2026, 10, 16, 17, 27, 11, DateTimeKind.Utc);

    private static Scenario Run(string id, string name, int? ms, string[]? tags = null, bool happy = false) => new()
    {
        Id = id, DisplayName = name, Result = ExecutionResult.Passed, Duration = ms is null ? null : TimeSpan.FromMilliseconds(ms.Value),
        Tags = tags, IsHappyPath = happy
    };

    private static Feature[] Bakery(bool durations = true) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                Run("s1", "Create order", durations ? 100 : null, ["smoke", "Fast"], happy: true),
                Run("s2", "Reject order", durations ? 300 : null)
            ]
        }
    ];

    private static string Generate(string fileName, Feature[]? features = null)
    {
        var path = ReportGenerator.GenerateHtmlReport([], features ?? Bakery(), Start, Start.AddSeconds(5), null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
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
    public void On_a_phone_the_rows_fold_under_a_Filters_button_on_its_own_line()
    {
        var html = Generate("FilterBar_Phone.html");

        // a button, which the keyboard reaches and which says whether the rows are open, on a line of its own between the
        // search line and the rows: on a phone the readout moves onto it (report-filter-bar-script.js)
        InOrder(html,
            "<div class=\"filter-search\">",
            "<div class=\"mobile-filter-line\"><button type=\"button\" class=\"mobile-filter-toggle\" aria-expanded=\"false\" aria-controls=\"filter-rows\"",
            ">Filters</button></div>",
            "<div class=\"filters\" id=\"filter-rows\">");
        Assert.DoesNotContain("<div class=\"mobile-filter-toggle\"", html);
    }

    [Fact]
    public void The_rows_start_folded_under_More_filters_at_the_end_of_the_search_line()
    {
        var html = Generate("FilterBar_More.html");

        Assert.Contains("<div class=\"filtering-box rows-folded\" id=\"filtering-box\">", html);
        InOrder(html,
            "<div class=\"filter-search\"><span class=\"search-field\"><input id=\"searchbar\" ",
            "</span><button type=\"button\" class=\"search-help-toggle\"",
            "<button type=\"button\" class=\"more-filters\" aria-controls=\"filter-rows\" aria-expanded=\"false\" title=\"Status, duration, happy paths and tags\"><span class=\"more-filters-label\">More filters</span><span class=\"more-filters-count\" hidden></span><svg ",
            "</svg></button></div>",
            "<div class=\"filters\" id=\"filter-rows\">");
    }

    [Fact]
    public void More_filters_names_only_the_rows_the_box_has()
    {
        var html = Generate("FilterBar_MoreRows.html", [new Feature { DisplayName = "F", Scenarios = [Run("x", "X", null)] }]);

        Assert.Contains("class=\"more-filters\" aria-controls=\"filter-rows\" aria-expanded=\"false\" title=\"Status\">", html);
    }

    [Fact]
    public void The_bar_carries_the_filters_beside_the_tabs_after_the_mark_where_it_pins()
    {
        var html = Generate("FilterBar_Nav.html");

        InOrder(html,
            "<div class=\"nav-mark\" aria-hidden=\"true\"></div><div class=\"toolbar-row report-nav on-features\"><div class=\"nav-row\"><div class=\"report-tabs\" role=\"tablist\"",
            "</div><div class=\"nav-filters\" role=\"group\" aria-label=\"Filters\">"
            + "<span class=\"nav-readout\" aria-hidden=\"true\" title=\"The scenarios and features the filters leave showing\"><span class=\"nav-readout-long\">All 2 scenarios, 1 feature</span><span class=\"nav-readout-short\">All 2 scenarios</span></span>"
            + "<button type=\"button\" class=\"nav-clear\" title=\"Clear every filter: the search with its dep:, duration: and is: terms, and the statuses, duration, happy paths and tags\" hidden>Clear All</button>"
            + "<button type=\"button\" class=\"nav-filters-toggle\" aria-expanded=\"false\" aria-controls=\"filtering-box\" title=\"Show the filter box in this bar, so it stays in view as you scroll\"><svg ",
            "</svg><span>Filters</span></button></div></div>",
            "<div class=\"features-tools\">");
    }

    [Fact]
    public void The_box_carries_a_hidden_close_for_when_it_rides_in_the_bar()
    {
        var html = Generate("FilterBar_Close.html");

        InOrder(html,
            "<div class=\"filtering-box-export\">",
            "onclick=\"export_csv()\" title=\"Download the scenarios the filters show as a CSV file: feature, scenario, status and duration in milliseconds\">",
            "<button type=\"button\" class=\"filters-close\" aria-label=\"Take the filters out of the bar\" title=\"Put the filter box back above the bar (Esc)\" hidden><svg ",
            "</svg></button></div></div>",
            "<div class=\"filter-search\">");
    }

    [Fact]
    public void The_features_panel_opens_with_the_empty_state_hidden()
    {
        var html = Generate("FilterBar_Empty.html");

        Assert.Contains("data-panel=\"features\"><div class=\"filters-empty\" hidden><span>No scenarios match the filters.</span><button type=\"button\" class=\"filters-empty-clear\" title=\"Clear every filter\">Clear All</button></div>", html);
    }

    [Fact]
    public void A_page_without_tabs_has_no_bar_group_but_keeps_More_filters_and_the_empty_state()
    {
        var html = Generate("FilterBar_NoTabs.html", Bakery(durations: false));

        Assert.DoesNotContain("<div class=\"toolbar-row report-nav", html);
        Assert.DoesNotContain("<div class=\"nav-filters\"", html);
        Assert.DoesNotContain("<div class=\"nav-mark\"", html);
        Assert.Contains("<button type=\"button\" class=\"more-filters\"", html);
        Assert.Contains("<div class=\"filters-empty\" hidden>", html);
    }
}
