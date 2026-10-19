using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The Features tab's tools (5.0.0, plans/OPTION_C_PLAN.md S4, D21, D22, D35): the Full and Summary views, the sort over
/// the cards and the table's rows alike, a row opening its feature, expand and collapse per level, and the phone's
/// display settings gear. Two bugs found on the way are pinned here as well: returning to the Features tab queued every
/// diagram in the report for drawing (S3), and a dropdown's arrow keys moved between scenarios instead of changing it.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class FeaturesToolsTests : PlaywrightTestBase
{
    public FeaturesToolsTests(PlaywrightFixture fixture) : base(fixture) { }

    private static Scenario Run(string id, string name, ExecutionResult result, double ms) => new()
    {
        Id = id, DisplayName = name, Result = result, Duration = TimeSpan.FromMilliseconds(ms),
        ErrorMessage = result == ExecutionResult.Failed ? "the batch was not baked" : null
    };

    // written out of name order, with no steps, so the duration group starts at the sixth column; two share a name
    private static Feature[] Bakery() =>
    [
        new Feature { DisplayName = "Muffins", Scenarios = [Run("m1", "Bake muffins", ExecutionResult.Passed, 300)] },
        new Feature
        {
            DisplayName = "Croissants",
            Scenarios = [Run("c1", "Fold the dough", ExecutionResult.Failed, 40), Run("c2", "Bake croissants", ExecutionResult.Failed, 60)]
        },
        new Feature { DisplayName = "Bagels", Scenarios = [Run("b1", "Boil bagels", ExecutionResult.Passed, 900)] },
        new Feature
        {
            DisplayName = "Bagels",
            Scenarios = [Run("b2", "Seed bagels", ExecutionResult.Failed, 10), Run("b3", "Toast bagels", ExecutionResult.Passed, 20), Run("b4", "Slice bagels", ExecutionResult.Passed, 90)]
        }
    ];

    private string Generate(string fileName, Feature[] features)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task OpenBakery(string fileName)
    {
        await Page.GotoAsync(Generate(fileName, Bakery()));
        await Page.Locator("details.feature").First.WaitForAsync();
    }

    // the cards' names and the rows' first cells, in page order, each with the scenario count it carries
    private Task<string[]> Cards() => Page.EvaluateAsync<string[]>(
        "() => Array.from(document.querySelectorAll('#report-content > details.feature')).map(f => f.querySelector(':scope > summary').firstChild.textContent.trim() + ':' + f.querySelectorAll('details.scenario').length)");
    private Task<string[]> Rows() => Page.EvaluateAsync<string[]>(
        "() => Array.from(document.querySelectorAll('#features-table-view tbody tr')).map(r => r.cells[0].textContent.trim() + ':' + r.cells[1].textContent.trim())");

    [Fact]
    public async Task Full_and_Summary_show_the_cards_or_the_table_and_the_tools_for_the_cards_go_with_them()
    {
        await OpenBakery("FeaturesTools_Views.html");
        await Expect(Page.Locator("#features-table-view")).ToBeHiddenAsync();
        await Expect(Page.Locator(".fold-group").First).ToBeVisibleAsync();
        await Expect(Page.Locator("#display-settings")).ToBeVisibleAsync();

        await Page.Locator("#view-summary").ClickAsync();
        await Expect(Page.Locator("#features-table-view")).ToBeVisibleAsync();
        await Expect(Page.Locator("#report-content")).ToBeHiddenAsync();
        await Expect(Page.Locator("#view-summary")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Page.Locator("#view-full")).ToHaveAttributeAsync("tabindex", "-1");
        // item R17.28: no fold controls, and no display settings, over a table that shows neither
        await Expect(Page.Locator(".fold-group").First).ToBeHiddenAsync();
        await Expect(Page.Locator("#display-settings")).ToBeHiddenAsync();
        await Expect(Page.Locator("#features-sort")).ToBeVisibleAsync();

        await Page.Locator("#view-full").ClickAsync();
        await Expect(Page.Locator("#report-content")).ToBeVisibleAsync();
        await Expect(Page.Locator("#features-table-view")).ToBeHiddenAsync();
        await Expect(Page.Locator(".fold-group").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_views_move_with_the_arrow_keys_Home_and_End()
    {
        await OpenBakery("FeaturesTools_ViewKeys.html");
        await Page.Locator("#view-full").FocusAsync();

        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(Page.Locator("#view-summary")).ToBeFocusedAsync();
        await Expect(Page.Locator("#features-table-view")).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("ArrowLeft");
        await Expect(Page.Locator("#view-full")).ToBeFocusedAsync();
        await Expect(Page.Locator("#report-content")).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("End");
        await Expect(Page.Locator("#view-summary")).ToHaveAttributeAsync("aria-selected", "true");
        await Page.Keyboard.PressAsync("Home");
        await Expect(Page.Locator("#view-full")).ToHaveAttributeAsync("aria-selected", "true");
    }

    [Fact]
    public async Task The_sort_orders_the_cards_and_the_rows_alike_and_report_order_puts_them_back()
    {
        await OpenBakery("FeaturesTools_Sort.html");
        string[] written = ["Muffins:1", "Croissants:2", "Bagels:1", "Bagels:3"];
        Assert.Equal(written, await Cards());
        Assert.Equal(written, await Rows());

        // two features named Bagels keep a row each, each beside its own card: rows pair with cards by position
        await Page.Locator("#features-sort").SelectOptionAsync("failed");
        Assert.Equal(["Croissants:2", "Bagels:3", "Muffins:1", "Bagels:1"], await Cards());
        Assert.Equal(["Croissants:2", "Bagels:3", "Muffins:1", "Bagels:1"], await Rows());

        await Page.Locator("#features-sort").SelectOptionAsync("name");
        Assert.Equal(["Bagels:1", "Bagels:3", "Croissants:2", "Muffins:1"], await Cards());

        await Page.Locator("#features-sort").SelectOptionAsync("scenarios");
        Assert.Equal(["Bagels:3", "Croissants:2", "Muffins:1", "Bagels:1"], await Rows());

        await Page.Locator("#features-sort").SelectOptionAsync("order");
        Assert.Equal(written, await Cards());
        Assert.Equal(written, await Rows());
    }

    [Fact]
    public async Task A_time_sort_reads_the_duration_columns_of_a_table_without_steps()
    {
        // the preview sorted on fixed column numbers that held only with step columns, so here it did nothing
        await OpenBakery("FeaturesTools_TimeSort.html");

        // totals 300, 100, 900 and 120 ms; averages 300, 50, 900 and 40; longest 300, 60, 900 and 90
        await Page.Locator("#features-sort").SelectOptionAsync("duration");
        Assert.Equal(["Bagels:1", "Muffins:1", "Bagels:3", "Croissants:2"], await Cards());
        await Page.Locator("#features-sort").SelectOptionAsync("longest");
        Assert.Equal(["Bagels:1", "Muffins:1", "Bagels:3", "Croissants:2"], await Rows());
        await Page.Locator("#features-sort").SelectOptionAsync("average");
        Assert.Equal(["Bagels:1", "Muffins:1", "Croissants:2", "Bagels:3"], await Rows());
    }

    [Fact]
    public async Task A_column_sort_is_cleared_by_the_sort_and_a_later_sort_still_pairs_each_row_with_its_card()
    {
        await OpenBakery("FeaturesTools_ColumnSort.html");
        await Page.Locator("#view-summary").ClickAsync();
        // a click on Failed reorders the rows only, before the sort has run once
        await Page.Locator("#features-table-view thead tr:last-child th", new() { HasTextString = "Failed" }).ClickAsync();
        Assert.Equal("3", await Page.Locator(".feature-summary-table").GetAttributeAsync("data-sort-col"));

        await Page.Locator("#features-sort").SelectOptionAsync("order");
        Assert.Null(await Page.Locator(".feature-summary-table").GetAttributeAsync("data-sort-col"));
        Assert.Equal(["Muffins:1", "Croissants:2", "Bagels:1", "Bagels:3"], await Rows());
        Assert.Equal(await Cards(), await Rows());
    }

    [Fact]
    public async Task A_row_opens_its_feature_in_the_Full_view_from_a_click_or_the_keyboard()
    {
        await OpenBakery("FeaturesTools_RowOpen.html");
        await Page.Locator("#view-summary").ClickAsync();

        await Page.Locator("#features-table-view tbody tr").Nth(3).Locator("td").Nth(2).ClickAsync();
        await Expect(Page.Locator("#report-content")).ToBeVisibleAsync();
        var fourth = Page.Locator("#report-content > details.feature").Nth(3);
        await Expect(fourth).ToHaveAttributeAsync("open", "");
        await Expect(fourth.Locator(":scope > summary")).ToBeFocusedAsync();
        await Expect(fourth.Locator(":scope > summary")).ToBeInViewportAsync();

        await Page.Locator("#view-summary").ClickAsync();
        await Page.Locator("#features-table-view tbody tr").Nth(1).Locator("button.feature-open").FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.Locator("#report-content > details.feature").Nth(1)).ToHaveAttributeAsync("open", "");
        await Expect(Page.Locator("#view-full")).ToHaveAttributeAsync("aria-selected", "true");
    }

    [Fact]
    public async Task Expanding_the_scenarios_opens_their_features_too()
    {
        await OpenBakery("FeaturesTools_ScenariosOpenFeatures.html");
        await Page.Locator("button.fold-expand[data-fold='scenarios']").ClickAsync();

        Assert.True(await Page.EvaluateAsync<bool>("() => Array.from(document.querySelectorAll('details.feature')).every(d => d.open)"));
        Assert.True(await Page.EvaluateAsync<bool>("() => Array.from(document.querySelectorAll('details.scenario')).every(d => d.open)"));
        await Expect(Page.Locator("button.fold-expand[data-fold='features']")).ToHaveAttributeAsync("aria-disabled", "true");
        await Expect(Page.Locator("button.fold-expand[data-fold='scenarios']")).ToHaveAttributeAsync("aria-disabled", "true");
    }

    [Fact]
    public async Task Next_Failure_from_the_Summary_view_goes_to_the_failure_in_its_card()
    {
        await OpenBakery("FeaturesTools_NextFailure.html");
        await Page.Locator("#view-summary").ClickAsync();

        await Page.Locator(".jump-to-failure").ClickAsync();
        await Expect(Page.Locator("#report-content")).ToBeVisibleAsync();
        await Expect(Page.Locator("#view-full")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Page.Locator("details.scenario[data-status='Failed']").First).ToBeInViewportAsync();
    }

    [Fact]
    public async Task On_a_phone_the_gear_shows_and_hides_the_display_settings()
    {
        await Page.SetViewportSizeAsync(400, 800);
        await Page.GotoAsync(GenerateReport("FeaturesTools_PhoneGear.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        var gear = Page.Locator(".tools-gear");

        await Expect(gear).ToBeVisibleAsync();
        await Expect(Page.Locator("#display-settings")).ToBeHiddenAsync();
        await gear.ClickAsync();
        await Expect(Page.Locator("#display-settings")).ToBeVisibleAsync();
        await Expect(gear).ToHaveAttributeAsync("aria-expanded", "true");
        await gear.ClickAsync();
        await Expect(Page.Locator("#display-settings")).ToBeHiddenAsync();
        await Expect(gear).ToHaveAttributeAsync("aria-expanded", "false");

        // a desktop shows the settings and no gear
        await Page.SetViewportSizeAsync(1280, 800);
        await Expect(gear).ToBeHiddenAsync();
        await Expect(Page.Locator("#display-settings")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Returning_to_the_Features_tab_draws_only_the_diagrams_that_show()
    {
        // S3 drew everything the Features tab held when it showed again: every diagram in every closed scenario
        await Page.GotoAsync(GenerateReport("FeaturesTools_TabDraws.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await OpenReportTabAsync("timeline");
        await OpenReportTabAsync("features");
        await TwoAnimationFramesAsync(Page);

        // the first scenario's diagrams are drawn as the page opens; the second scenario is closed
        var queued = await Page.EvaluateAsync<int>(
            "() => Array.from(document.querySelectorAll('details.scenario')).slice(1).filter(s => !s.open).flatMap(s => Array.from(s.querySelectorAll('.plantuml-browser[data-queued]'))).length");
        Assert.Equal(0, queued);
    }

    [Fact]
    public async Task A_dropdown_keeps_its_arrow_keys()
    {
        // the report's scenario keys took ArrowDown and ArrowUp from every dropdown on the page (4.x as well)
        await OpenBakery("FeaturesTools_SelectKeys.html");
        var sort = Page.Locator("#features-sort");
        await sort.FocusAsync();

        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(sort).ToHaveValueAsync("name");
        Assert.Equal(0, await Page.Locator(".scenario-focused").CountAsync());
        await Expect(sort).ToBeFocusedAsync();
    }
}
