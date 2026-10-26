using AngleSharp;
using AngleSharp.Dom;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The search button on the title line and the query builder it opens (plans/SEARCH_PLAN.md 4.2, 4.3; facts T1, T5, T9,
/// B3): the title, then the search, then the theme switch, as GitHub's search is the leftmost of its header's right-hand
/// controls; a button that says what it does and its key and that it opens a dialog; the applied query in a hidden field
/// every script already reads; and the syntax tips in the builder, saying which parts of a scenario each search reads.
/// </summary>
public class SearchButtonMarkupTests
{
    private static readonly DateTime Start = new(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);

    private static string Generate(string fileName, bool includeTestRunData = true)
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Pancakes",
                Scenarios = [new Scenario { Id = "p1", DisplayName = "A batch is made", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(12) }]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([], features, Start, Start.AddSeconds(5), null, fileName, "Bakery", includeTestRunData,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return ReportHtml.Read(path);
    }

    private static IDocument Parse(string html) =>
        BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();

    // T1
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_title_line_holds_the_title_then_the_search_then_the_theme_switch(bool includeTestRunData)
    {
        var page = Parse(Generate($"SearchButton_Line_{includeTestRunData}.html", includeTestRunData));

        var line = page.QuerySelector("div.title-line")!;
        Assert.Equal(["H1", "DIV"], line.Children.Select(c => c.TagName).ToArray());
        var actions = line.Children[1];
        Assert.Equal("title-actions", actions.ClassName);
        Assert.Equal(["search-box", "theme-toggle"], actions.Children.Select(c => c.ClassName!).ToArray());
        Assert.NotNull(actions.QuerySelector(".search-box > button#search-button.search-button"));
        Assert.NotNull(actions.QuerySelector(".search-box > button#search-clear.search-clear[hidden]"));
    }

    // T5
    [Fact]
    public void The_search_button_names_its_key_and_says_it_opens_a_dialog()
    {
        var page = Parse(Generate("SearchButton_Names.html"));
        var button = page.QuerySelector("#search-button")!;

        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal("dialog", button.GetAttribute("aria-haspopup"));
        Assert.Equal("false", button.GetAttribute("aria-expanded"));
        Assert.Equal("search-dialog", button.GetAttribute("aria-controls"));
        Assert.Equal("/", button.GetAttribute("aria-keyshortcuts"));
        Assert.Equal("Search or jump to, type / to search", button.GetAttribute("aria-label"));
        Assert.Equal("Search this report, or jump to a scenario or a feature (/)", button.GetAttribute("title"));
        Assert.Equal("Type / to search", button.QuerySelector(".search-button-hint")!.TextContent);
        Assert.Equal("true", button.QuerySelector(".search-button-hint kbd")!.GetAttribute("aria-hidden"));
        Assert.NotNull(button.QuerySelector(".search-button-query[hidden]"));
        Assert.Equal("Clear the search", page.QuerySelector("#search-clear")!.GetAttribute("aria-label"));
        Assert.Equal("Clear the search; the filters stay as they are", page.QuerySelector("#search-clear")!.GetAttribute("title"));
    }

    // 4.1: the applied query stays where every script reads it, a hidden field now; the box keeps no search line
    [Fact]
    public void The_applied_query_is_a_hidden_field_and_the_box_has_no_search_line()
    {
        var page = Parse(Generate("SearchButton_Hidden.html"));

        var field = page.QuerySelector("#searchbar")!;
        Assert.Equal("hidden", field.GetAttribute("type"));
        Assert.Equal("off", field.GetAttribute("autocomplete"));
        Assert.Null(page.QuerySelector("#filtering-box #searchbar"));
        Assert.Null(page.QuerySelector("#filtering-box .filter-search"));
        Assert.Null(page.QuerySelector("#filtering-box .search-help-toggle"));
        Assert.Null(page.QuerySelector(".search-help-panel"));
    }

    // 4.3.1, 4.3.2, 4.3.4: a native dialog, labelled; its input a combobox over a listbox; a polite live region
    [Fact]
    public void The_builder_is_a_labelled_dialog_with_a_combobox_over_a_listbox()
    {
        var page = Parse(Generate("SearchButton_Dialog.html"));

        var dialog = page.QuerySelector("dialog#search-dialog.search-dialog")!;
        Assert.Equal("search-dialog-title", dialog.GetAttribute("aria-labelledby"));
        Assert.Equal("Search this report", page.QuerySelector("#search-dialog-title")!.TextContent);
        var input = dialog.QuerySelector("input#search-input")!;
        Assert.Equal("combobox", input.GetAttribute("role"));
        Assert.Equal("false", input.GetAttribute("aria-expanded"));
        Assert.Equal("search-suggestions", input.GetAttribute("aria-controls"));
        Assert.Equal("list", input.GetAttribute("aria-autocomplete"));
        Assert.Equal("false", input.GetAttribute("spellcheck"));
        Assert.Equal("off", input.GetAttribute("autocomplete"));
        Assert.Equal("Search or jump to…", input.GetAttribute("placeholder"));
        var list = dialog.QuerySelector("#search-suggestions")!;
        Assert.Equal("listbox", list.GetAttribute("role"));
        Assert.Equal("Suggestions", list.GetAttribute("aria-label"));
        var live = dialog.QuerySelector(".search-live")!;
        Assert.Equal("status", live.GetAttribute("role"));
        Assert.Equal("true", live.GetAttribute("aria-atomic"));
        Assert.Equal("search-tips", dialog.QuerySelector(".search-tips-toggle")!.GetAttribute("aria-controls"));
    }

    // T9 (the name holds the applied query once one is applied: written by the script, so here the button starts
    // without one, and the script's rule is the E2E fact)
    [Fact]
    public void The_search_script_is_in_the_page_after_the_navigation_script()
    {
        var html = Generate("SearchButton_Script.html");

        var ui = html.IndexOf("<script id=\"kronikol-search-ui\">", StringComparison.Ordinal);
        Assert.True(ui >= 0, "the search script is in the page");
        Assert.True(ui > html.IndexOf("window.kronikol_nav =", StringComparison.Ordinal), "after the navigation script");
    }

    // B4 follows 5.9.1's E3: a scenario skipped after a failure counts as skipped for status:, as the Status buttons count
    // it, and the tips say so
    [Fact]
    public void The_syntax_tips_count_a_scenario_skipped_after_a_failure_as_skipped()
    {
        var page = Parse(Generate("SearchButton_TipsStatus.html"));
        var row = page.QuerySelectorAll("#search-dialog #search-tips tr").Single(r => r.TextContent.Contains("status:failed", StringComparison.Ordinal));

        Assert.Contains("a scenario skipped after a failure counts as skipped", row.TextContent);
        Assert.DoesNotContain("counts as failed", row.TextContent);
    }

    // B3: the tips say which parts of a scenario the instant search reads and which only deep search reads; until then
    // the help said the instant search read "diagram source"
    [Fact]
    public void The_syntax_tips_say_what_the_instant_search_reads()
    {
        var page = Parse(Generate("SearchButton_Tips.html"));
        var tips = page.QuerySelector("#search-dialog #search-tips")!;
        var text = tips.TextContent;

        Assert.DoesNotContain("diagram source)", text);
        Assert.Contains("names, descriptions, tags, steps, example values, error messages, the services a scenario calls and the paths of its HTTP calls", text);
        Assert.Contains("payloads, headers, SQL, whole diagrams and stack traces", text);
        foreach (var form in new[] { "tag:", "status:", "feature:", "dep:", "duration:", "is:happy", "&&", "||", "!!", "@tag", "$status" })
            Assert.Contains(form, text);
    }

    // the labs page has no search (4.2): its title line keeps the switch alone
    [Fact]
    public void The_search_markup_is_written_once()
    {
        var page = Parse(Generate("SearchButton_Once.html"));

        Assert.Single(page.QuerySelectorAll("#search-button"));
        Assert.Single(page.QuerySelectorAll("#search-dialog"));
        Assert.Single(page.QuerySelectorAll("#searchbar"));
    }
}
