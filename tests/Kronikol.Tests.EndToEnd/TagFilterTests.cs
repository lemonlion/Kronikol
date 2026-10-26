using System.Globalization;
using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// One Tags row in the browser (plans/TAGS_PLAN.md §4.8 to §4.10): a feature's tags filter every one of its scenarios
/// (F3); a tag holding a comma, a quote or a space filters, is found by <c>@</c> and survives the page's link (F4, F5,
/// F8); <c>@</c> finds Happy Path (F2); a link whose value does not decode loses only that value; a 4.x link's
/// Uncategorized chip is the Untagged one; a retried scenario's badge comes from its attempt; and a long row starts with
/// the tags the most scenarios carry and shows the rest behind its More chip (Q9).
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class TagFilterTests : PlaywrightTestBase
{
    public TagFilterTests(PlaywrightFixture fixture) : base(fixture) { }

    // page order: a feature's happy paths first, then the rest by name
    private const string Pay = "scenario-pay-by-card", Refuse = "scenario-refuse-a-card", Leave = "scenario-leave", Look = "scenario-look-around";
    private static readonly string[] All = [Pay, Refuse, Leave, Look];

    private static Scenario Run(string id, string name, string[]? tags = null, bool happy = false, int? attempt = null) => new()
    {
        Id = id, DisplayName = name, Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(20), Tags = tags,
        IsHappyPath = happy, Attempt = attempt
    };

    private string Generate(string fileName)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Checkout", Tags = ["checkout"],
                Scenarios =
                [
                    Run("c1", "Pay by card", ["a,b", "x y"], happy: true),
                    // the words of Pay's tag "x y" in its text, not in its tags: a quoted @ must not read as a quoted word search
                    Run("c2", "Refuse a card", ["say \"hi\"", "Slow"]) with { Description = "Says x y." }
                ]
            },
            new()
            {
                DisplayName = "Browse",
                Scenarios = [Run("b1", "Look around", ["smoke"]), Run("b2", "Leave", attempt: 3)]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([], features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Shop", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task OpenAsync(string fileName, string hash = "")
    {
        await Page.GotoReportAsync(Generate(fileName) + hash);
        // attached, not visible: a link can hide the first feature
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await OpenFilterRowsAsync();
    }

    private static Task<string[]> ShownOn(IPage page) => page.EvaluateAsync<string[]>(
        "() => Array.from(document.querySelectorAll('#report-content details.scenario')).filter(s => s.style.display !== 'none' && s.closest('details.feature').style.display !== 'none').map(s => s.id)");

    private Task<string[]> Shown() => ShownOn(Page);

    // a chip by its name, found by comparing the attribute, so any character in the name is safe
    private ILocator Tag(string name) => Page.Locator(".filtering-box .tag-toggle").Filter(new() { HasTextString = name }).First;

    private async Task<IPage> FreshPageAt(string url)
    {
        var fresh = await OpenPageAsync(Page.Context);
        await fresh.GotoReportAsync(url);
        await fresh.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        return fresh;
    }

    private static string T(int i) => string.Create(CultureInfo.InvariantCulture, $"T{i:000}");

    // MostShown + 5 tags from T000, the last five on two scenarios each, so the row starts without the five before them
    private static (string Tag, int Scenarios)[] LongRow() =>
        [.. Enumerable.Range(0, TagChips.MostShown + 5).Select(i => (T(i), i >= TagChips.MostShown ? 2 : 1))];

    // one scenario per carrier of each tag, named after its tag ("T026 1"), so its anchor is scenario-t026-1
    private async Task OpenWithTagsAsync(string fileName, (string Tag, int Scenarios)[] tags, string hash = "")
    {
        Scenario[] scenarios =
        [
            .. tags.SelectMany(t => Enumerable.Range(1, t.Scenarios)
                .Select(k => Run(string.Create(CultureInfo.InvariantCulture, $"{t.Tag}-{k}"), string.Create(CultureInfo.InvariantCulture, $"{t.Tag} {k}"), [t.Tag])))
        ];
        var path = ReportGenerator.GenerateHtmlReport([], [new Feature { DisplayName = "Long", Scenarios = scenarios }], DateTime.UtcNow,
            DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Long", true, diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri + hash);
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await OpenFilterRowsAsync();
    }

    private Task<int> ChipsShown() => Page.Locator(".filtering-box .tag-toggle:visible").CountAsync();

    [Fact]
    public async Task A_long_tags_row_shows_the_rest_behind_its_more_chip_and_hides_them_again()
    {
        await OpenWithTagsAsync("Tags_Long.html", LongRow());
        var more = Page.Locator(".filtering-box .tag-more");
        await Expect(more).ToHaveTextAsync("5 more");
        await Expect(Tag(T(TagChips.MostShown - 1))).ToBeHiddenAsync();
        Assert.Equal(TagChips.MostShown, await ChipsShown());

        await more.ClickAsync();
        await Expect(more).ToHaveTextAsync("Fewer");
        await Expect(more).ToHaveAttributeAsync("aria-expanded", "true");
        Assert.Equal(TagChips.MostShown + 5, await ChipsShown());

        // a chip chosen while the row is open stays when it closes
        var chosen = Tag(T(TagChips.MostShown - 2));
        await chosen.ClickAsync();
        await more.ClickAsync();
        await Expect(more).ToHaveTextAsync("4 more");
        await Expect(more).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(chosen).ToBeVisibleAsync();
        await Expect(chosen).ToHaveAttributeAsync("aria-pressed", "true");
        Assert.Equal(TagChips.MostShown + 1, await ChipsShown());
    }

    [Fact]
    public async Task A_link_that_chooses_a_tag_the_long_row_starts_without_shows_that_chip_chosen()
    {
        var name = T(TagChips.MostShown - 4);
        await OpenWithTagsAsync("Tags_LongLink.html", LongRow(), "#tags=" + name);
        await Expect(Tag(name)).ToBeVisibleAsync();
        await Expect(Tag(name)).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Page.Locator(".filtering-box .tag-more")).ToHaveTextAsync("4 more");
        Assert.Equal(["scenario-" + name.ToLowerInvariant() + "-1"], await Shown());
    }

    [Fact]
    public async Task Clear_puts_focus_on_the_first_chip_that_shows()
    {
        // A000 is first in the row's order and carried least, so a full row starts without it
        await OpenWithTagsAsync("Tags_LongClear.html", [("A000", 1), .. Enumerable.Range(0, TagChips.MostShown).Select(i => (T(i), 2))]);
        await Expect(Tag("A000")).ToBeHiddenAsync();
        await Tag(T(3)).ClickAsync();
        await Page.Locator(".filtering-box .tag-filters .group-clear").ClickAsync();
        await Expect(Tag(T(0))).ToBeFocusedAsync();
    }

    [Fact]
    public async Task A_feature_s_tag_filters_every_one_of_its_scenarios()
    {
        await OpenAsync("Tags_FeatureTag.html");
        // the second click leaves the tag's scenarios out on a three-way row (5.10.0: a row starts two-way)
        await Page.Locator(".filtering-box .tag-ways-toggle").ClickAsync();
        await Expect(Page.Locator(".filtering-box .tag-ways-toggle")).ToHaveTextAsync("3-WAY");
        await Expect(Tag("checkout")).ToHaveAttributeAsync("data-tag", "checkout");
        await Tag("checkout").ClickAsync();
        Assert.Equal([Pay, Refuse], await Shown());
        await Tag("checkout").ClickAsync();
        Assert.Equal([Leave, Look], await Shown());
    }

    [Theory]
    [InlineData("a,b", "tags=a%2Cb", new[] { Pay })]
    [InlineData("say \"hi\"", "tags=say%20%22hi%22", new[] { Refuse })]
    [InlineData("x y", "tags=x%20y", new[] { Pay })]
    public async Task A_tag_with_a_comma_a_quote_or_a_space_filters_and_survives_the_link(string name, string inHash, string[] expected)
    {
        await OpenAsync($"Tags_Odd_{Math.Abs(name.GetHashCode()):x}.html");
        await Expect(Tag(name)).ToHaveAttributeAsync("data-tag", name);
        await Tag(name).ClickAsync();
        Assert.Equal(expected, await Shown());
        var hash = await Page.EvaluateAsync<string>("() => location.hash");
        Assert.Contains(inHash, hash);

        var fresh = await FreshPageAt(await Page.EvaluateAsync<string>("() => location.href"));
        Assert.Equal(expected, await ShownOn(fresh));
        await Expect(fresh.Locator(".filtering-box .tag-toggle").Filter(new() { HasTextString = name }).First).ToHaveAttributeAsync("aria-pressed", "true");
        await fresh.CloseAsync();
    }

    [Fact]
    public async Task Two_chosen_tags_with_a_comma_and_a_space_stay_two_in_the_link()
    {
        await OpenAsync("Tags_TwoOdd.html");
        await Tag("a,b").ClickAsync();
        await Tag("x y").ClickAsync();
        Assert.Equal([Pay], await Shown());
        // each name encoded on its own and the names joined with a comma (plans/TAGS_PLAN.md §4.10)
        Assert.Contains("tags=a%2Cb,x%20y", await Page.EvaluateAsync<string>("() => location.hash"));

        var fresh = await FreshPageAt(await Page.EvaluateAsync<string>("() => location.href"));
        await Expect(fresh.Locator(".filtering-box .tag-toggle").Filter(new() { HasTextString = "a,b" }).First).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(fresh.Locator(".filtering-box .tag-toggle").Filter(new() { HasTextString = "x y" }).First).ToHaveAttributeAsync("aria-pressed", "true");
        Assert.Equal([Pay], await ShownOn(fresh));
        await fresh.CloseAsync();
    }

    [Theory]
    [InlineData("@a,b", new[] { Pay })]
    [InlineData("@\"x y\"", new[] { Pay })]
    [InlineData("@\"happy path\"", new[] { Pay })]
    [InlineData("@checkout", new[] { Pay, Refuse })]
    [InlineData("@slow", new[] { Refuse })]
    [InlineData("@\"x y\" && @checkout", new[] { Pay })]
    [InlineData("@smoke || @\"Happy Path\"", new[] { Pay, Look })]
    public async Task At_finds_the_tags_the_row_shows(string query, string[] expected)
    {
        await OpenAsync($"Tags_At_{Math.Abs(query.GetHashCode()):x}.html");
        await FillSearchBar(query);
        await Page.WaitForFunctionAsync("n => Array.from(document.querySelectorAll('#report-content details.scenario')).filter(s => s.style.display !== 'none' && s.closest('details.feature').style.display !== 'none').length === n",
            expected.Length, new() { PollingInterval = 200 });
        Assert.Equal(expected, await Shown());
    }

    [Fact]
    public async Task A_link_value_that_does_not_decode_loses_only_itself()
    {
        // %E0%A4%A is a broken UTF-8 sequence: decodeURIComponent throws on it
        await OpenAsync("Tags_BadLink.html", "#q=%E0%A4%A&tags=%E0%A4%A,smoke&status=Passed");
        await Expect(Tag("smoke")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Page.Locator(".status-toggle[data-status='Passed']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bstatus-active\\b"));
        Assert.Equal([Look], await Shown());
    }

    [Fact]
    public async Task A_4x_link_s_Uncategorized_chip_is_the_Untagged_one()
    {
        await OpenAsync("Tags_Uncategorized.html", "#cats=__uncategorized__");
        await Expect(Page.Locator(".filtering-box .tag-toggle[data-none='1']")).ToHaveAttributeAsync("aria-pressed", "true");
        Assert.Equal([Leave], await Shown());
    }

    [Fact]
    public async Task Under_430_px_each_filter_row_s_label_takes_a_line_of_its_own()
    {
        // the rule the Categories row's label closed: removing that label must leave the rule's declarations in place
        await Page.SetViewportSizeAsync(390, 844);
        await OpenAsync("Tags_NarrowLabels.html");
        var labels = await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('.filtering-box :is(.status-filters-label, .duration-filters-label, .happy-filters-label, .tag-filters-label, .dependency-filters-label)'))
                .map(l => { const s = getComputedStyle(l); return l.className + ' ' + s.flexBasis + ' ' + s.marginTop + ' ' + s.marginRight + ' ' + s.marginBottom; })
            """);
        Assert.Equal(
        [
            "status-filters-label 100% 0px 8px 2px", "duration-filters-label 100% 0px 8px 2px", "happy-filters-label 100% 0px 8px 2px",
            "tag-filters-label 100% 0px 8px 2px"
        ], labels);
    }

    [Fact]
    public async Task A_retried_scenario_shows_the_retry_its_result_came_from_and_no_retry_chip()
    {
        await OpenAsync("Tags_Retry.html");
        await ExpandAllFeaturesAsync();
        var badge = Page.Locator($"#{Leave} > summary .retry-label");
        await Expect(badge).ToBeVisibleAsync();
        await Expect(badge).ToHaveTextAsync("retry 2");
        await Expect(badge).ToHaveAttributeAsync("title", "Shown: attempt 3. The attempts before it are not in the report.");
        Assert.Equal(0, await Page.Locator(".filtering-box .tag-toggle[data-tag^='retry']").CountAsync());
        Assert.Equal(All, await Shown());
    }
}
