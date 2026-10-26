using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The filter box and the report bar carrying it (5.0.0, plans/OPTION_C_PLAN.md S7, D24, D25; plans/SEARCH_PLAN.md 4.7):
/// the box is written hidden at the top of the bar, after a mark where the bar pins, with every row and no More filters
/// (F1, F2, F5); the bar's top row carries the tabs and a group with what the filters leave showing, Clear All and
/// Filters with its count (F3, F6); the bar's own status element announces the filters (F8); the box has a close; a page
/// with only its features has the bar (F7); and the Features panel opens with the empty state, hidden until no scenario
/// is left.
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
        return ReportHtml.Read(path);
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

    // F-a, F-d (plans/SEARCH_PLAN.md F1, F2, F5): the box is written hidden at the top of the bar with every row showing;
    // More filters, the rows' fold and the phone's own Filters button are gone
    [Fact]
    public void The_box_is_hidden_when_the_page_opens()
    {
        var html = Generate("FilterBar_Hidden.html");

        InOrder(html,
            "<div class=\"nav-mark\" aria-hidden=\"true\"></div><div class=\"toolbar-row report-nav on-features\">"
            + "<div class=\"filtering-box\" id=\"filtering-box\" role=\"region\" aria-label=\"Filters\" tabindex=\"-1\" hidden>",
            "<div class=\"filters\" id=\"filter-rows\">",
            "<div class=\"nav-row\">");
    }

    [Fact]
    public void There_is_no_More_filters_and_no_phone_Filters_button()
    {
        var html = Generate("FilterBar_NoMore.html");

        Assert.DoesNotContain("more-filters", html);
        Assert.DoesNotContain("rows-folded", html);
        Assert.DoesNotContain("mobile-filter", html);
        Assert.DoesNotContain("help-open", html);
    }

    [Fact]
    public void The_bar_carries_the_filters_beside_the_tabs_after_the_mark_where_it_pins()
    {
        var html = Generate("FilterBar_Nav.html");

        InOrder(html,
            "<div class=\"nav-mark\" aria-hidden=\"true\"></div><div class=\"toolbar-row report-nav on-features\"><div class=\"filtering-box\"",
            "<div class=\"nav-row\"><div class=\"report-tabs\" role=\"tablist\"",
            "</div><div class=\"nav-filters\" role=\"group\" aria-label=\"Filters\">"
            + "<span class=\"nav-readout\" aria-hidden=\"true\" title=\"The scenarios and features the filters leave showing\"><span class=\"nav-readout-long\">All 2 scenarios, 1 feature</span><span class=\"nav-readout-short\">All 2 scenarios</span></span>"
            + "<button type=\"button\" class=\"nav-clear\" title=\"Clear every filter: the search with its tag:, status:, feature:, dep:, duration: and is: terms, and the statuses, duration, happy paths and tags\" hidden>Clear All</button>"
            + "<button type=\"button\" class=\"nav-filters-toggle\" aria-expanded=\"false\" aria-controls=\"filtering-box\" aria-label=\"Filters\" title=\"Show the filters\"><svg ",
            "</svg><span>Filters</span><span class=\"filters-count\" aria-hidden=\"true\" hidden></span></button></div></div>",
            "<div class=\"nav-status\" role=\"status\">All 2 scenarios, 1 feature</div>",
            "<div class=\"features-tools\">");
    }

    // F-k: the filters are announced by the bar's own status element; both readouts are seen, not heard
    [Fact]
    public void The_bar_s_status_element_is_the_live_region_and_the_readouts_are_not()
    {
        var html = Generate("FilterBar_Live.html");

        Assert.Contains("<div class=\"nav-status\" role=\"status\">", html);
        Assert.DoesNotContain("<span class=\"filter-readout\" role=", html);
        Assert.Contains("<span class=\"filter-readout\" aria-hidden=\"true\"", html);
        Assert.Contains("<span class=\"nav-readout\" aria-hidden=\"true\"", html);
    }

    [Fact]
    public void The_box_carries_its_close_which_hides_it()
    {
        var html = Generate("FilterBar_Close.html");

        InOrder(html,
            "<div class=\"filtering-box-export\">",
            "onclick=\"export_csv()\" title=\"Download the scenarios the filters show as a CSV file: feature, scenario, status and duration in milliseconds\">",
            "<button type=\"button\" class=\"filters-close\" aria-label=\"Hide the filters\" title=\"Hide the filters (Esc)\"><svg ",
            "</svg></button></div></div>",
            "<div class=\"box-search\" hidden>");
    }

    [Fact]
    public void The_features_panel_opens_with_the_empty_state_hidden()
    {
        var html = Generate("FilterBar_Empty.html");

        Assert.Contains("data-panel=\"features\"><div class=\"filters-empty\" hidden><span>No scenarios match the filters.</span><button type=\"button\" class=\"filters-empty-clear\" title=\"Clear every filter\">Clear All</button></div>", html);
    }

    // F-j (plans/SEARCH_PLAN.md F7, Q10): a page with only its features has the bar, its one tab and Filters
    [Fact]
    public void A_page_with_only_its_features_has_the_bar_with_Features_and_Filters()
    {
        var html = Generate("FilterBar_FeaturesOnly.html", Bakery(durations: false));

        Assert.Contains("<div class=\"nav-mark\" aria-hidden=\"true\"></div><div class=\"toolbar-row report-nav on-features\">", html);
        // its one tab that shows, beside the Search tab written hidden until a search is applied (4.5.1)
        var tabs = System.Text.RegularExpressions.Regex.Matches(html, "<button class=\"report-tab\"[^>]*>").Select(m => m.Value).ToArray();
        Assert.Single(tabs, t => !t.EndsWith(" hidden>", StringComparison.Ordinal));
        Assert.Contains("data-tab=\"features\"", html);
        Assert.Contains("<button type=\"button\" class=\"nav-filters-toggle\"", html);
        Assert.Contains("<div class=\"filters-empty\" hidden>", html);
    }
}
