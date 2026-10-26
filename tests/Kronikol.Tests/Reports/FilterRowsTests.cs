using AngleSharp.Dom;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The filter box's rows (5.0.0, plans/OPTION_C_PLAN.md S5 and S6, D6 to D14, D16, D19): labels without colons and each
/// row's controls in a group of their own; Duration and Happy paths on the Status line; Tags and Dependencies as rows of
/// tri-state chips with an ANY / ALL switch, a chip for the scenarios with none and a Clear, a category being one more tag
/// (plans/TAGS_PLAN.md §4.8); the counts in
/// the chips' titles, counted as the run counts scenarios (a parameterized scenario once per example); what the filters
/// leave showing, beside Clear All; and the search's qualifiers in its help.
/// </summary>
public class FilterRowsTests
{
    private static readonly DateTime Start = new(2026, 10, 16, 17, 27, 11, DateTimeKind.Utc);

    private const string OrderSource = "@startuml\nactor \"Caller\" as caller\nparticipant \"Order Service\" as svc\ndatabase \"Order Db\" as db\ncaller -> svc : POST /orders\nsvc -> db : INSERT\n@enduml";
    private const string PaymentSource = "@startuml\nactor \"Caller\" as caller\nparticipant \"Payment Service\" as pay\ncaller -> pay : POST /refunds\n@enduml";
    private static readonly string MarkerOnlySource = "@startuml\n" + global::Kronikol.PlantUml.PlantUmlCreator.MarkerOnlyParticipant + "\n@enduml";

    private static Scenario Run(string id, string name, ExecutionResult result, int ms, string[]? tags = null,
        bool happy = false, string? outline = null) => new()
    {
        Id = id, DisplayName = name, Result = result, Duration = TimeSpan.FromMilliseconds(ms), Tags = tags,
        IsHappyPath = happy, OutlineId = outline, ExampleValues = outline is null ? null : new() { ["size"] = name[^2..^1] },
        ErrorMessage = result == ExecutionResult.Failed ? "boom" : null
    };

    // five runs in two features: a parameterized scenario of two examples with no tag, and a marker-only diagram
    private static Feature[] Bakery() =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                Run("s1", "Create order", ExecutionResult.Passed, 100, ["smoke", "api", "Fast"], happy: true),
                Run("s2", "Reject order", ExecutionResult.Failed, 300, ["api"]),
                Run("s3", "Price(size: S)", ExecutionResult.Passed, 50, outline: "Price"),
                Run("s4", "Price(size: L)", ExecutionResult.Passed, 50, outline: "Price")
            ]
        },
        new Feature { DisplayName = "Refunds", Scenarios = [Run("s5", "Refund", ExecutionResult.Passed, 20, ["smoke", "Fast", "Slow"])] }
    ];

    private static string Generate(string fileName, Feature[]? features = null, bool includeTestRunData = true)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("s1", "", OrderSource), new DefaultDiagramsFetcher.DiagramAsCode("s2", "", MarkerOnlySource),
             new DefaultDiagramsFetcher.DiagramAsCode("s5", "", PaymentSource)],
            features ?? Bakery(), Start, Start.AddSeconds(5), null, fileName, "Test", includeTestRunData,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return ReportHtml.Read(path);
    }

    private static string Box(string html)
    {
        // written at the top of the bar since plans/SEARCH_PLAN.md F2, so the bar's tab row follows it
        var start = html.IndexOf("<div class=\"filtering-box\" id=\"filtering-box\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "the filter box is missing");
        return html[start..html.IndexOf("<div class=\"nav-row\">", start, StringComparison.Ordinal)];
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
    public void Status_duration_and_happy_paths_share_the_first_row_with_labels_that_carry_no_colon()
    {
        var box = Box(Generate("FilterRows_Line.html"));
        InOrder(box, "<div class=\"filter-row\">",
            "<div class=\"status-filters\"><span class=\"status-filters-label\">Status</span><span class=\"fltr-controls\"><button class=\"status-toggle\"",
            "<div class=\"duration-filters\"", "<span class=\"duration-filters-label\">Duration ≥</span><span class=\"fltr-controls\"><button class=\"percentile-btn\"",
            "<div class=\"happy-filters\"><span class=\"happy-filters-label\">Happy paths</span><span class=\"fltr-controls\" role=\"group\" aria-label=\"Happy paths\">",
            "<div class=\"tag-filters\">", "<div class=\"dependency-filters\">");
        Assert.DoesNotContain("Status:", box);
        Assert.DoesNotContain("happy-path-toggle", box);
    }

    [Fact]
    public void Happy_paths_are_a_three_way_control_that_starts_on_Show()
    {
        var box = Box(Generate("FilterRows_Happy.html"));
        Assert.Contains("<button type=\"button\" class=\"happy-seg\" data-hp=\"off\" aria-pressed=\"true\" title=\"Happy-path scenarios and the rest\">Show</button>"
            + "<button type=\"button\" class=\"happy-seg\" data-hp=\"in\" aria-pressed=\"false\" title=\"Only the scenarios tagged as happy paths\">Only</button>"
            + "<button type=\"button\" class=\"happy-seg\" data-hp=\"out\" aria-pressed=\"false\" title=\"Leave out the scenarios tagged as happy paths\">Hide</button>", box);

        // a run with no happy path has nothing to show only or hide
        var none = Box(Generate("FilterRows_NoHappy.html", [new Feature { DisplayName = "F", Scenarios = [Run("x", "X", ExecutionResult.Passed, 5, ["api"])] }]));
        Assert.DoesNotContain("happy-filters", none);
        Assert.DoesNotContain("data-tag=\"Happy Path\"", none);
    }

    [Fact]
    public void The_tags_row_has_a_switch_a_chip_per_tag_Happy_Path_first_and_one_for_the_untagged()
    {
        var box = Box(Generate("FilterRows_Tags.html"));
        Assert.Contains("<div class=\"tag-filters\"><span class=\"tag-filters-label\">Tags</span><span class=\"fltr-controls\">"
            + "<button type=\"button\" class=\"tag-mode-toggle\" data-mode=\"OR\" title=\"ANY: scenarios with any of the chosen tags. ALL: scenarios with every one of them. Click to switch.\">ANY</button>"
            + "<button type=\"button\" class=\"tag-ways-toggle\" data-ways=\"2\" title=\"Two-way chips: a click shows only a chip's scenarios, a second click shows every scenario again. Click for three-way chips, where the second click leaves the chip's scenarios out (red) and a third shows them again.\">2-WAY</button>"
            + "<button type=\"button\" class=\"tag-toggle\" data-tag=\"Happy Path\" data-scenarios=\"1\" aria-pressed=\"false\" title=\"1 scenario. Click: only these. Click again: off.\">Happy Path</button>"
            + "<button type=\"button\" class=\"tag-toggle\" data-tag=\"api\" data-scenarios=\"2\" aria-pressed=\"false\" title=\"2 scenarios. Click: only these. Click again: off.\">api</button>"
            + "<button type=\"button\" class=\"tag-toggle\" data-tag=\"Fast\" data-scenarios=\"2\" aria-pressed=\"false\" title=\"2 scenarios. Click: only these. Click again: off.\">Fast</button>"
            + "<button type=\"button\" class=\"tag-toggle\" data-tag=\"Slow\" data-scenarios=\"1\" aria-pressed=\"false\" title=\"1 scenario. Click: only these. Click again: off.\">Slow</button>"
            + "<button type=\"button\" class=\"tag-toggle\" data-tag=\"smoke\" data-scenarios=\"2\" aria-pressed=\"false\" title=\"2 scenarios. Click: only these. Click again: off.\">smoke</button>"
            + "<button type=\"button\" class=\"tag-toggle tag-none\" data-tag=\"__untagged__\" data-none=\"1\" data-scenarios=\"2\" aria-pressed=\"false\" title=\"2 scenarios. Click: only these. Click again: off.\">Untagged</button>"
            + "<button type=\"button\" class=\"group-clear\" title=\"Clear the tag filters\" hidden>Clear</button></span></div>", box);
    }

    // 5.10.0 (plans/REPORT_UI_FIXES_PLAN.md C1, C9, C12): each row's ways switch follows its ANY / ALL, on the run's report
    // and on the Specifications page. Its text says the row's ways and is its name: no aria-label and no aria-pressed, as
    // ANY / ALL has neither. A chip carries its count, from which the script rebuilds its title when the row's ways change.
    [Fact]
    public void The_rows_write_a_ways_switch_after_ANY_ALL()
    {
        foreach (var testRun in new[] { true, false })
        {
            var box = Box(Generate($"FilterRows_Ways_{testRun}.html", includeTestRunData: testRun));
            Assert.Contains("Click to switch.\">ANY</button><button type=\"button\" class=\"tag-ways-toggle\" data-ways=\"2\" title=\"" + TwoWayTitle + "\">2-WAY</button>", box);
            Assert.Contains("Click to switch.\">ANY</button><button type=\"button\" class=\"dep-ways-toggle\" data-ways=\"2\" title=\"" + TwoWayTitle + "\">2-WAY</button>", box);
            var page = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(box);
            foreach (var ways in page.QuerySelectorAll(".tag-ways-toggle, .dep-ways-toggle"))
            {
                Assert.Null(ways.GetAttribute("aria-label"));
                Assert.Null(ways.GetAttribute("aria-pressed"));
            }
            Assert.All(page.QuerySelectorAll(".tag-toggle, .dependency-toggle"), chip =>
                Assert.StartsWith(chip.GetAttribute("data-scenarios") + (chip.GetAttribute("data-scenarios") == "1" ? " scenario. " : " scenarios. "), chip.GetAttribute("title")));
        }
    }

    private const string TwoWayTitle = "Two-way chips: a click shows only a chip's scenarios, a second click shows every scenario again. Click for three-way chips, where the second click leaves the chip's scenarios out (red) and a third shows them again.";

    [Fact]
    public void The_dependencies_row_counts_each_chip_and_gathers_the_scenarios_with_none()
    {
        var box = Box(Generate("FilterRows_Deps.html"));
        // the marker-only diagram's "(no interactions)" participant is no dependency: its scenario has none
        Assert.Contains("<div class=\"dependency-filters\"><span class=\"dependency-filters-label\">Dependencies</span><span class=\"fltr-controls\">"
            + "<button type=\"button\" class=\"dep-mode-toggle\" data-mode=\"OR\" title=\"ANY: scenarios that call any of the chosen dependencies. ALL: scenarios that call every one of them. Click to switch.\">ANY</button>"
            + "<button type=\"button\" class=\"dep-ways-toggle\" data-ways=\"2\" title=\"Two-way chips: a click shows only a chip's scenarios, a second click shows every scenario again. Click for three-way chips, where the second click leaves the chip's scenarios out (red) and a third shows them again.\">2-WAY</button>"
            + "<button type=\"button\" class=\"dependency-toggle\" data-dependency=\"Caller\" data-scenarios=\"2\" aria-pressed=\"false\" title=\"2 scenarios. Click: only these. Click again: off.\">Caller</button>"
            + "<button type=\"button\" class=\"dependency-toggle\" data-dependency=\"Order Db\" data-scenarios=\"1\" aria-pressed=\"false\" title=\"1 scenario. Click: only these. Click again: off.\">Order Db</button>"
            + "<button type=\"button\" class=\"dependency-toggle\" data-dependency=\"Order Service\" data-scenarios=\"1\" aria-pressed=\"false\" title=\"1 scenario. Click: only these. Click again: off.\">Order Service</button>"
            + "<button type=\"button\" class=\"dependency-toggle\" data-dependency=\"Payment Service\" data-scenarios=\"1\" aria-pressed=\"false\" title=\"1 scenario. Click: only these. Click again: off.\">Payment Service</button>"
            + "<button type=\"button\" class=\"dependency-toggle dependency-none\" data-dependency=\"__none__\" data-none=\"1\" data-scenarios=\"3\" aria-pressed=\"false\" title=\"3 scenarios. Click: only these. Click again: off.\">No dependencies</button>"
            + "<button type=\"button\" class=\"group-clear\" title=\"Clear the dependency filters\" hidden>Clear</button></span></div>", box);
        Assert.DoesNotContain("data-dependency=\"(no interactions)\"", box);
    }

    [Fact]
    public void The_report_has_no_categories_row()
    {
        var html = Generate("FilterRows_Cats.html");
        var box = Box(html);
        // Fast is the tag a 4.x run held as a category
        Assert.Contains("<button type=\"button\" class=\"tag-toggle\" data-tag=\"Fast\" data-scenarios=\"2\" aria-pressed=\"false\" title=\"2 scenarios. Click: only these. Click again: off.\">Fast</button>", box);
        // (a 4.x link's __uncategorized__ is still read, as the Untagged chip)
        foreach (var gone in new[] { "category-filters", "category-toggle", "cat-mode-toggle", "data-category=", "data-categories=", "_catMode" })
            Assert.DoesNotContain(gone, html);
    }

    [Fact]
    public void A_feature_s_tags_pick_out_every_one_of_its_scenarios()
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Checkout", Tags = ["checkout"],
                Scenarios = [Run("c1", "Pay", ExecutionResult.Passed, 5, ["smoke"]), Run("c2", "Refuse", ExecutionResult.Passed, 5)]
            },
            new Feature { DisplayName = "Other", Scenarios = [Run("o1", "Browse", ExecutionResult.Passed, 5)] }
        ];
        var box = Box(Generate("FilterRows_FeatureTags.html", features));
        Assert.Contains("<button type=\"button\" class=\"tag-toggle\" data-tag=\"checkout\" data-scenarios=\"2\" aria-pressed=\"false\" title=\"2 scenarios. Click: only these. Click again: off.\">checkout</button>"
            + "<button type=\"button\" class=\"tag-toggle\" data-tag=\"smoke\" data-scenarios=\"1\" aria-pressed=\"false\" title=\"1 scenario. Click: only these. Click again: off.\">smoke</button>"
            + "<button type=\"button\" class=\"tag-toggle tag-none\" data-tag=\"__untagged__\" data-none=\"1\" data-scenarios=\"1\" aria-pressed=\"false\" title=\"1 scenario. Click: only these. Click again: off.\">Untagged</button>", box);
    }

    [Fact]
    public void Names_that_differ_only_in_the_case_of_ASCII_letters_are_one_chip_in_the_spelling_the_report_meets_first()
    {
        Feature[] features =
        [
            new Feature { DisplayName = "A", Scenarios = [Run("a1", "First", ExecutionResult.Passed, 5, ["Smoke"])] },
            new Feature { DisplayName = "B", Scenarios = [Run("b1", "Second", ExecutionResult.Passed, 5, ["smoke", "SMOKE", "über"]), Run("b2", "Third", ExecutionResult.Passed, 5, ["Über"])] }
        ];
        var box = Box(Generate("FilterRows_Case.html", features));
        Assert.Contains("<button type=\"button\" class=\"tag-toggle\" data-tag=\"Smoke\" data-scenarios=\"2\" aria-pressed=\"false\" title=\"2 scenarios. Click: only these. Click again: off.\">Smoke</button>", box);
        Assert.DoesNotContain("data-tag=\"smoke\"", box);
        Assert.DoesNotContain("data-tag=\"SMOKE\"", box);
        // a letter outside ASCII is not folded (§4.3): two chips (HtmlEncode writes U+00A0 to U+00FF as numbers)
        Assert.Contains("data-tag=\"&#252;ber\" data-scenarios=\"1\" aria-pressed=\"false\" title=\"1 scenario. Click: only these. Click again: off.\">", box);
        Assert.Contains("data-tag=\"&#220;ber\" data-scenarios=\"1\" aria-pressed=\"false\" title=\"1 scenario. Click: only these. Click again: off.\">", box);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("sv-SE")]
    [InlineData("tr-TR")]
    [InlineData("cs-CZ")]
    public void The_chips_are_in_the_same_order_under_every_culture(string culture)
    {
        Feature[] features = [new Feature { DisplayName = "F", Scenarios = [Run("x", "X", ExecutionResult.Passed, 5, ["Äpfel", "ice", "Ich", "chip", "hat", "Zebra", "apple"])] }];
        CultureRun.Under(culture, () =>
        {
            var box = Box(Generate($"FilterRows_Order_{culture}.html", features));
            InOrder(box, "data-tag=\"apple\"", "data-tag=\"chip\"", "data-tag=\"hat\"", "data-tag=\"ice\"", "data-tag=\"Ich\"",
                "data-tag=\"Zebra\"", "data-tag=\"&#196;pfel\"");
        });
    }

    [Fact]
    public void The_readout_says_what_the_filters_leave_showing_counting_runs_and_a_parameterized_scenario_carries_its_runs()
    {
        var html = Generate("FilterRows_Readout.html");
        Assert.Contains("<div class=\"filtering-box-export\"><span class=\"filter-readout\" aria-hidden=\"true\" title=\"The scenarios and features the filters leave showing\">All 5 scenarios, 2 features</span>"
            + "<button class=\"export-btn\" onclick=\"clear_all_filters()\" title=\"Clear every filter: the search with its tag:, status:, feature:, dep:, duration: and is: terms, and the statuses, duration, happy paths, tags and dependencies\">Clear All</button>", html);
        Assert.Contains(" data-runs=\"2\"", html);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(html, " data-runs=\""));

        var one = Generate("FilterRows_ReadoutOne.html", [new Feature { DisplayName = "F", Scenarios = [Run("x", "X", ExecutionResult.Passed, 5)] }]);
        Assert.Contains("<span class=\"filter-readout\" aria-hidden=\"true\" title=\"The scenarios and features the filters leave showing\">All 1 scenario, 1 feature</span>", one);
    }

    [Fact]
    public void The_exports_say_Filtered_where_there_is_room()
    {
        var box = Box(Generate("FilterRows_Exports.html"));
        Assert.Contains("<button class=\"export-btn\" onclick=\"export_html()\" title=\"Download a copy of this report holding only the features the filters show\">Export <span class=\"wide-only\">Filtered </span>HTML</button>", box);
        Assert.Contains("<button class=\"export-btn\" onclick=\"export_csv()\" title=\"Download the scenarios the filters show as a CSV file: feature, scenario, status and duration in milliseconds\">Export <span class=\"wide-only\">Filtered </span>CSV</button>", box);
    }

    // the search's help is the query builder's tips since the search left the box (plans/SEARCH_PLAN.md 4.3.5)
    [Fact]
    public void The_search_names_its_qualifiers_in_its_tips()
    {
        var html = Generate("FilterRows_Search.html");
        var tips = html[html.IndexOf("<div id=\"search-tips\" class=\"search-tips\" hidden>", StringComparison.Ordinal)..];
        Assert.DoesNotContain("placeholder=\"Search... (@tag", html);
        Assert.Contains("<tr><td><code>dep:name</code></td><td>A dependency the scenario calls: part of its name is enough; commas for any of several</td>", tips);
        Assert.Contains("<tr><td><code>duration:&gt;2s</code></td>", tips);
        Assert.Contains("<tr><td><code>is:happy</code></td><td>Happy paths</td>", tips);
        Assert.Contains("<code>feature:</code>, <code>dep:</code>, <code>duration:</code> and <code>is:</code> always narrow the search: they combine with the rest of it by AND, and can be left out with <code>-</code>, <code>!!</code> or <code>not</code>. Inside a group that <code>!!</code> or <code>not</code> negates, one is read as text, and the search says so.</p>", tips);
    }

    // 5.9.1 (plans/REPORT_UI_FIXES_PLAN.md I9, W1 and W4): what filters in the box says filter, never search. The box's own
    // field went with plans/SEARCH_PLAN.md F1, so the one search it names is the search the query builder applied: the line
    // that shows it (F10) and Clear All, which clears it with the rows
    [Fact]
    public void The_filter_box_calls_only_the_applied_search_a_search()
    {
        foreach (var (name, includeTestRunData) in new[] { ("FilterRows_Words.html", true), ("FilterRows_WordsSpecifications.html", false) })
        {
            var html = Generate(name, includeTestRunData: includeTestRunData);
            var page = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);
            var scopes = page.QuerySelectorAll("#filtering-box, .nav-filters").ToArray();
            Assert.Contains(scopes, s => s.Id == "filtering-box");
            var said = scopes.SelectMany(s => s.Descendants().Prepend(s))
                .SelectMany(n => n is AngleSharp.Dom.IElement e
                    ? new[] { e.GetAttribute("placeholder"), e.GetAttribute("title"), e.GetAttribute("aria-label") }
                    : n.NodeType == AngleSharp.Dom.NodeType.Text ? new[] { n.TextContent } : [])
                .Where(t => t is not null && t.Contains("search", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            string[] applied =
            [
                "Search", "Clear the search", "Clear the search; the other filters stay as they are",
            ];
            Assert.True(said.All(t => applied.Contains(t) || t!.StartsWith("Clear every filter: the search with its tag:", StringComparison.Ordinal)),
                name + " calls the filtering a search: " + string.Join(" | ", said.Where(t => !applied.Contains(t))));
            Assert.Equal(["Search", "Clear the search", "Clear the search; the other filters stay as they are"],
                page.QuerySelectorAll("#filtering-box .box-search .box-search-label, #filtering-box .box-search .box-search-clear")
                    .SelectMany(e => new[] { e.GetAttribute("aria-label"), e.GetAttribute("title"), e.ClassList.Contains("box-search-label") ? e.TextContent : null })
                    .Where(t => t is not null));

            // the deep index tells the Search tab how far it has read (4.5.7); the field's chip went with the field
            Assert.DoesNotContain("kron-deep-chip", html);
            Assert.DoesNotContain("searching everything", html);
        }
    }

    [Fact]
    public void The_duration_buttons_say_what_they_show()
    {
        var box = Box(Generate("FilterRows_DurationTitles.html"));
        // P50 of 20, 50, 50, 100 and 300 ms is 50 ms
        Assert.Contains("title=\"Scenarios taking 50ms or more (the slowest half)\">P50 (50ms)</button>", box);
        Assert.Contains("data-custom=\"1\" onclick=\"set_percentile(this)\" title=\"Scenarios taking at least the seconds you enter\">Custom</button>", box);
    }

    [Fact]
    public void A_report_with_no_tags_categories_or_dependencies_has_none_of_their_rows()
    {
        var box = Box(Generate("FilterRows_Bare.html", [new Feature { DisplayName = "F", Scenarios = [Run("x", "X", ExecutionResult.Passed, 5)] }]));
        Assert.DoesNotContain("tag-filters", box);
        Assert.DoesNotContain("category-filters", box);
        // the diagrams passed belong to scenarios this run does not have: no scenario has a dependency
        Assert.DoesNotContain("dependency-filters", box);
    }
}
