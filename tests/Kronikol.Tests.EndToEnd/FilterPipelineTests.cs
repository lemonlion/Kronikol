using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The filters (5.0.0, plans/OPTION_C_PLAN.md S5 and S6): tags and dependencies as tri-state chips (off, has, has not)
/// with an ANY / ALL switch and a chip for the scenarios with none, a category being one more tag (plans/TAGS_PLAN.md
/// §4.8); Happy paths as a three-way control over the
/// Happy Path tag; a Clear per row; what the filters leave showing and the event that says so; every filter in the
/// page's link, which a fresh page reads back, links from 4.x reports included; the search's qualifiers (dep:, duration:,
/// is:); Clear All; and the exports.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class FilterPipelineTests : PlaywrightTestBase
{
    public FilterPipelineTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string OrderSource = "@startuml\nactor \"Caller\" as caller\nparticipant \"Order Service\" as svc\ndatabase \"Order Db\" as db\ncaller -> svc : POST /orders\nsvc -> db : INSERT\n@enduml";
    private const string PaymentSource = "@startuml\nactor \"Caller\" as caller\nparticipant \"Payment Service\" as pay\ncaller -> pay : POST /refunds\n@enduml";
    private const string MarkerOnlySource = "@startuml\nparticipant \"(no interactions)\" as noInteractions\n@enduml";

    // the scenarios' anchors: A happy, smoke and api, Fast, calls Caller, Order Service and Order Db, 100 ms; B api, no
    // call, failed, 300 ms; P a parameterized scenario of two examples, untagged, 50 ms each; R smoke, Fast and Slow, calls
    // Caller and Payment Service, 20 ms. Fast and Slow are categories, which are tags. A feature lists its happy paths
    // first, then the rest by name, so the page order is A, P, B, R.
    private const string A = "scenario-create-order", B = "scenario-reject-order", P = "scenario-price", R = "scenario-refund";
    private static readonly string[] All = [A, P, B, R];

    private static Scenario Run(string id, string name, ExecutionResult result, int ms, string[]? tags = null,
        bool happy = false, string? outline = null) => new()
    {
        Id = id, DisplayName = name, Result = result, Duration = TimeSpan.FromMilliseconds(ms), Tags = tags,
        IsHappyPath = happy, OutlineId = outline, ExampleValues = outline is null ? null : new() { ["size"] = name[^2..^1] },
        ErrorMessage = result == ExecutionResult.Failed ? "boom" : null
    };

    private string Generate(string fileName)
    {
        Feature[] features =
        [
            new()
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
            new() { DisplayName = "Refunds", Scenarios = [Run("s5", "Refund", ExecutionResult.Passed, 20, ["smoke", "Fast", "Slow"])] }
        ];
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("s1", "", OrderSource), new DefaultDiagramsFetcher.DiagramAsCode("s2", "", MarkerOnlySource),
             new DefaultDiagramsFetcher.DiagramAsCode("s5", "", PaymentSource)],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task OpenAsync(string fileName, string hash = "")
    {
        await Page.GotoReportAsync(Generate(fileName) + hash);
        await Page.Locator("details.feature").First.WaitForAsync();
        await OpenFilterRowsAsync();
    }

    // the scenarios the filters leave showing, by anchor, in page order
    private Task<string[]> Shown() => Page.EvaluateAsync<string[]>(
        "() => Array.from(document.querySelectorAll('#report-content details.scenario')).filter(s => s.style.display !== 'none' && s.closest('details.feature').style.display !== 'none').map(s => s.id)");

    private ILocator Chip(string row, string name) => Page.Locator($".filtering-box .{row}-toggle[data-{row}='{name}']");
    private ILocator Tag(string name) => Page.Locator($".filtering-box .tag-toggle[data-tag='{name}']");

    private async Task RightClick(ILocator chip) =>
        await chip.EvaluateAsync("c => c.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }))");

    [Fact]
    public async Task A_tag_chip_cycles_through_has_has_not_and_off()
    {
        await OpenAsync("Filters_TagCycle.html");
        var api = Tag("api");

        await api.ClickAsync();
        await Expect(api).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(api).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\btag-active\\b"));
        Assert.Equal([A, B], await Shown());

        await api.ClickAsync();
        await Expect(api).ToHaveAttributeAsync("aria-pressed", "mixed");
        await Expect(api).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\btag-exclude\\b"));
        Assert.Equal([P, R], await Shown());

        await api.ClickAsync();
        await Expect(api).ToHaveAttributeAsync("aria-pressed", "false");
        Assert.Equal(All, await Shown());
    }

    [Fact]
    public async Task Alt_click_and_right_click_go_straight_to_has_not()
    {
        await OpenAsync("Filters_Exclude.html");
        await Tag("smoke").ClickAsync(new() { Modifiers = [KeyboardModifier.Alt] });
        await Expect(Tag("smoke")).ToHaveAttributeAsync("aria-pressed", "mixed");
        Assert.Equal([P, B], await Shown());

        await RightClick(Tag("api"));
        await Expect(Tag("api")).ToHaveAttributeAsync("aria-pressed", "mixed");
        Assert.Equal([P], await Shown());
        // right-click on a chip is the chip's: the browser's menu does not open over it
        Assert.True(await Tag("api").EvaluateAsync<bool>("c => !c.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }))"));
    }

    [Fact]
    public async Task The_switch_reads_ANY_or_ALL_and_combines_the_chosen_tags()
    {
        await OpenAsync("Filters_TagMode.html");
        await Tag("api").ClickAsync();
        await Tag("smoke").ClickAsync();
        Assert.Equal([A, B, R], await Shown());

        var mode = Page.Locator(".tag-mode-toggle");
        await Expect(mode).ToHaveTextAsync("ANY");
        await mode.ClickAsync();
        await Expect(mode).ToHaveTextAsync("ALL");
        await Expect(mode).ToHaveAttributeAsync("data-mode", "AND");
        Assert.Equal([A], await Shown());
    }

    [Fact]
    public async Task The_chips_for_none_pick_out_the_scenarios_with_no_tag_or_no_dependency()
    {
        await OpenAsync("Filters_None.html");
        await Page.Locator(".tag-toggle[data-none='1']").ClickAsync();
        Assert.Equal([P], await Shown());
        await Page.Locator(".tag-toggle[data-none='1']").ClickAsync(new() { Modifiers = [KeyboardModifier.Alt] });
        await Page.Locator(".tag-toggle[data-none='1']").ClickAsync(new() { Modifiers = [KeyboardModifier.Alt] });
        Assert.Equal(All, await Shown());

        await Page.Locator(".dependency-toggle[data-none='1']").ClickAsync();
        Assert.Equal([P, B], await Shown());
        await RightClick(Page.Locator(".dependency-toggle[data-none='1']"));
        Assert.Equal([A, R], await Shown());
    }

    [Fact]
    public async Task Dependencies_are_tri_state_and_a_category_is_a_tag_chip()
    {
        await OpenAsync("Filters_DepCat.html");
        await RightClick(Chip("dependency", "Order Service"));
        await Expect(Chip("dependency", "Order Service")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bdependency-exclude\\b"));
        Assert.Equal([P, B, R], await Shown());
        await RightClick(Chip("dependency", "Order Service"));
        Assert.Equal(All, await Shown());

        await RightClick(Tag("Slow"));
        await Expect(Tag("Slow")).ToHaveAttributeAsync("aria-pressed", "mixed");
        Assert.Equal([A, P, B], await Shown());
        await Tag("Fast").ClickAsync();
        Assert.Equal([A], await Shown());
        Assert.Equal(0, await Page.Locator(".category-toggle, .cat-mode-toggle, .category-filters").CountAsync());
    }

    [Fact]
    public async Task Happy_paths_and_the_Happy_Path_chip_are_one_filter()
    {
        await OpenAsync("Filters_Happy.html");
        var only = Page.Locator(".happy-seg[data-hp='in']");
        await only.ClickAsync();
        await Expect(only).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Tag("Happy Path")).ToHaveAttributeAsync("aria-pressed", "true");
        Assert.Equal([A], await Shown());

        await Page.Locator(".happy-seg[data-hp='out']").ClickAsync();
        await Expect(Tag("Happy Path")).ToHaveAttributeAsync("aria-pressed", "mixed");
        Assert.Equal([P, B, R], await Shown());

        // the chip moves the control too: has not, then off
        await Tag("Happy Path").ClickAsync();
        await Expect(Page.Locator(".happy-seg[data-hp='off']")).ToHaveAttributeAsync("aria-pressed", "true");
        Assert.Equal(All, await Shown());
    }

    // Only means only: the Happy Path chip set to has asks for a happy path whatever the Tags row's mode, as 4.x's Happy
    // Paths Only did beside its categories. From 5.0.0 to 5.2.6 the chip joined the row's ANY, and Only with the smoke
    // chip showed every smoke scenario as well
    [Fact]
    public async Task Happy_paths_Only_asks_for_a_happy_path_beside_chips_in_the_ANY_mode()
    {
        await OpenAsync("Filters_HappyOnlyAny.html");
        await Expect(Page.Locator(".tag-mode-toggle")).ToHaveTextAsync("ANY");
        await Tag("Slow").ClickAsync();
        Assert.Equal([R], await Shown());

        // a happy path with Slow: there is none
        await Page.Locator(".happy-seg[data-hp='in']").ClickAsync();
        Assert.Empty(await Shown());

        // a happy path with Slow or smoke
        await Tag("smoke").ClickAsync();
        Assert.Equal([A], await Shown());
    }

    // A tag is one tag whatever the case of its ASCII letters (ScenarioTags.Comparer), and its chip carries the first
    // spelling met: a 4.x report's fast and Fast were two categories, and a link that chose fast chose no chip (5.0.0 to
    // 5.2.6)
    [Fact]
    public async Task A_link_finds_a_tag_s_chip_whatever_the_case_of_its_letters()
    {
        await OpenAsync("Filters_LinkCase.html", "#cats=fast&tags=SMOKE&tagmode=AND");
        await Expect(Tag("Fast")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Tag("smoke")).ToHaveAttributeAsync("aria-pressed", "true");
        Assert.Equal([A, R], await Shown());
    }

    // 4.x combined Happy Paths Only with the chosen categories whatever their mode, and a 4.x link keeps its meaning (Q5)
    [Fact]
    public async Task A_link_from_a_4x_report_with_Happy_Paths_Only_and_a_category_shows_its_happy_paths()
    {
        await OpenAsync("Filters_OldLinkAny.html", "#cats=Fast&hp=1");
        await Expect(Page.Locator(".tag-mode-toggle")).ToHaveTextAsync("ANY");
        Assert.Equal([A], await Shown());
    }

    [Fact]
    public async Task A_row_has_a_Clear_while_one_of_its_chips_is_set()
    {
        await OpenAsync("Filters_RowClear.html");
        var clear = Page.Locator(".tag-filters .group-clear");
        await Expect(clear).ToBeHiddenAsync();

        await Tag("api").ClickAsync();
        await RightClick(Tag("smoke"));
        await Expect(clear).ToBeVisibleAsync();
        await Expect(Page.Locator(".dependency-filters .group-clear")).ToBeHiddenAsync();

        await clear.ClickAsync();
        await Expect(clear).ToBeHiddenAsync();
        await Expect(Tag("api")).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(Tag("smoke")).ToHaveAttributeAsync("aria-pressed", "false");
        Assert.Equal(All, await Shown());
        // focus goes to the row's first chip, not to the page
        await Expect(Page.Locator(".tag-filters .tag-toggle").First).ToBeFocusedAsync();
    }

    [Fact]
    public async Task The_readout_and_its_event_follow_the_filters_counting_each_example_as_a_run()
    {
        await OpenAsync("Filters_Readout.html");
        await Page.EvaluateAsync("() => { window.__changes = []; document.addEventListener('kron-filter-change', e => window.__changes.push(e.detail.shown + '/' + e.detail.total)); }");
        var readout = Page.Locator(".filter-readout");
        await Expect(readout).ToHaveTextAsync("All 5 scenarios, 2 features");

        await Page.Locator(".status-toggle[data-status='Failed']").ClickAsync();
        await Expect(readout).ToHaveTextAsync("Filtered 1 of 5 scenarios, 1 of 2 features");
        await Expect(readout).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bis-filtered\\b"));
        await Page.Locator(".status-toggle[data-status='Failed']").ClickAsync();

        await Tag("api").ClickAsync();
        await Expect(readout).ToHaveTextAsync("Filtered 2 of 5 scenarios, 1 of 2 features");
        await Tag("api").ClickAsync(new() { Modifiers = [KeyboardModifier.Alt] });
        await Expect(readout).ToHaveTextAsync("Filtered 3 of 5 scenarios, 2 of 2 features");
        await Tag("api").ClickAsync(new() { Modifiers = [KeyboardModifier.Alt] });
        await Expect(readout).ToHaveTextAsync("All 5 scenarios, 2 features");

        // one event per change of what shows, none for a filter that changes nothing
        Assert.Equal(["1/5", "5/5", "2/5", "3/5", "5/5"], await Page.EvaluateAsync<string[]>("() => window.__changes"));
    }

    [Fact]
    public async Task The_link_carries_every_filter_and_a_fresh_page_reads_it_back()
    {
        await OpenAsync("Filters_Link.html");
        await Tag("api").ClickAsync();
        await RightClick(Tag("smoke"));
        await Page.Locator(".tag-mode-toggle").ClickAsync();
        await RightClick(Chip("dependency", "Payment Service"));
        await RightClick(Tag("Fast"));
        await Page.Locator(".happy-seg[data-hp='out']").ClickAsync();
        var shown = await Shown();
        var hash = await Page.EvaluateAsync<string>("() => location.hash");
        Assert.Contains("tags=api", hash);
        // the chips left out, in the row's order
        Assert.Contains("tagx=Fast,smoke", hash);
        Assert.Contains("tagmode=AND", hash);
        Assert.Contains("depx=Payment%20Service", hash);
        Assert.DoesNotContain("cat", hash);
        Assert.Contains("hpx=1", hash);

        var url = await Page.EvaluateAsync<string>("() => location.href");
        var fresh = await OpenPageAsync(Page.Context);
        await fresh.GotoReportAsync(url);
        await fresh.Locator("details.feature").First.WaitForAsync();
        Assert.Equal(shown, await fresh.EvaluateAsync<string[]>(
            "() => Array.from(document.querySelectorAll('#report-content details.scenario')).filter(s => s.style.display !== 'none' && s.closest('details.feature').style.display !== 'none').map(s => s.id)"));
        await Expect(fresh.Locator(".tag-toggle[data-tag='api']")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(fresh.Locator(".tag-toggle[data-tag='smoke']")).ToHaveAttributeAsync("aria-pressed", "mixed");
        await Expect(fresh.Locator(".tag-mode-toggle")).ToHaveTextAsync("ALL");
        await Expect(fresh.Locator(".dependency-toggle[data-dependency='Payment Service']")).ToHaveAttributeAsync("aria-pressed", "mixed");
        await Expect(fresh.Locator(".tag-toggle[data-tag='Fast']")).ToHaveAttributeAsync("aria-pressed", "mixed");
        await Expect(fresh.Locator(".happy-seg[data-hp='out']")).ToHaveAttributeAsync("aria-pressed", "true");
        await fresh.CloseAsync();
    }

    [Fact]
    public async Task A_link_from_a_4x_report_still_reads()
    {
        // 4.x wrote hp=1 for Happy Paths Only, deps= and cats= for the chosen chips, and the modes as AND and OR; its
        // categories are tags now (plans/TAGS_PLAN.md §4.10), so cats= chooses tags and catmode= sets the tags' mode
        await OpenAsync("Filters_OldLink.html", "#deps=Caller&depmode=AND&cats=Fast&catmode=AND&hp=1");
        await Expect(Page.Locator(".happy-seg[data-hp='in']")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Chip("dependency", "Caller")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Tag("Fast")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Page.Locator(".dep-mode-toggle")).ToHaveTextAsync("ALL");
        await Expect(Page.Locator(".tag-mode-toggle")).ToHaveTextAsync("ALL");
        Assert.Equal([A], await Shown());
    }

    [Theory]
    [InlineData("dep:order", new[] { A })]
    [InlineData("dep:\"payment service\"", new[] { R })]
    [InlineData("dep:payment,order", new[] { A, R })]
    // a parameterized scenario's time is its examples' together (100 ms here), as the duration filter reads it
    [InlineData("duration:>=100ms", new[] { A, P, B })]
    [InlineData("duration:<60ms", new[] { R })]
    [InlineData("duration:50ms..150ms", new[] { A, P })]
    [InlineData("duration:>=p90", new[] { B })]
    [InlineData("is:happy", new[] { A })]
    [InlineData("!!is:happy", new[] { P, B, R })]
    [InlineData("smoke dep:caller", new[] { A, R })]
    [InlineData("dep:caller && !!is:happy", new[] { R })]
    public async Task A_qualifier_narrows_the_search_and_stays_in_the_link(string query, string[] expected)
    {
        await OpenAsync($"Filters_Qualifier_{Math.Abs(query.GetHashCode()):x}.html");
        await FillSearchBar(query);
        await Page.WaitForFunctionAsync("n => Array.from(document.querySelectorAll('#report-content details.scenario')).filter(s => s.style.display !== 'none').length === n",
            expected.Length, new() { PollingInterval = 200 });
        Assert.Equal(expected, await Shown());
        // the link keeps the search as it was typed, qualifiers and all
        Assert.Equal(query, await Page.EvaluateAsync<string>("() => decodeURIComponent((location.hash.match(/[#&]q=([^&]*)/) || [])[1] || '')"));
    }

    [Fact]
    public async Task A_qualifier_that_cannot_be_read_is_searched_as_text_and_marked()
    {
        await OpenAsync("Filters_BadQualifier.html");
        await FillSearchBar("duration:>fast");
        await Expect(Page.Locator("#searchbar")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bkron-q-bad\\b"));
        await Expect(Page.Locator("#searchbar")).ToHaveAttributeAsync("data-q-bad", "duration:>fast");
        await FillSearchBar("duration:>1s");
        await Expect(Page.Locator("#searchbar")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bkron-q-bad\\b"));
    }

    [Fact]
    public async Task Clear_All_clears_the_qualifiers_the_tri_state_chips_and_the_modes()
    {
        await OpenAsync("Filters_ClearAll.html");
        await FillSearchBar("dep:order");
        await Page.WaitForFunctionAsync("() => document.querySelectorAll('#report-content details.scenario:not([style*=\"none\"])').length === 1", null, new() { PollingInterval = 200 });
        await RightClick(Tag("api"));
        await RightClick(Chip("dependency", "Order Db"));
        await Page.Locator(".tag-mode-toggle").ClickAsync();

        await Page.Locator(".export-btn", new() { HasTextString = "Clear All" }).ClickAsync();
        Assert.Equal(All, await Shown());
        await Expect(Page.Locator("#searchbar")).ToHaveValueAsync("");
        await Expect(Tag("api")).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(Chip("dependency", "Order Db")).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(Page.Locator(".tag-mode-toggle")).ToHaveTextAsync("ANY");
        await Expect(Page.Locator(".filter-readout")).ToHaveTextAsync("All 5 scenarios, 2 features");
    }

    [Fact]
    public async Task The_csv_export_leaves_out_what_a_tri_state_chip_hides()
    {
        await OpenAsync("Filters_Export.html");
        await RightClick(Tag("api"));
        var download = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.Locator("button.export-btn", new() { HasTextString = "Export Filtered CSV" }).ClickAsync());
        var path = Path.Combine(TempDir, $"export_{Guid.NewGuid():N}.csv");
        await download.SaveAsAsync(path);
        var lines = (await File.ReadAllTextAsync(path)).Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.DoesNotContain(lines, l => l.Contains("Create order") || l.Contains("Reject order"));
        Assert.Contains(lines, l => l.Contains("\"Refund\""));
    }
}
