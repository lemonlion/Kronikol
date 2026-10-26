using System.Globalization;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// What a scenario's markup carries of its tags (plans/TAGS_PLAN.md §4.8, §4.9, §4.6): one <c>data-tags</c> attribute
/// holding its effective tags, each in its chip's spelling and percent-encoded so that a comma, a quote or a space inside
/// a name survives (F4, F5); badges for each level's own tags, the scenario's leaving out its feature's; a retry badge
/// drawn from the attempt in place of the generated <c>retry N</c> labels; and the tags a parameterized group's rows
/// share (F23).
/// </summary>
public class TagsMarkupTests
{
    private static readonly DateTime Start = new(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);

    private static Scenario Run(string id, string name, string[]? tags = null, bool happy = false,
        string? outline = null, int? attempt = null) => new()
    {
        Id = id, DisplayName = name, Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10), Tags = tags,
        IsHappyPath = happy, Attempt = attempt, OutlineId = outline,
        ExampleValues = outline is null ? null : new() { ["size"] = name[^2..^1] }
    };

    private static string Generate(string fileName, params Feature[] features)
    {
        var path = ReportGenerator.GenerateHtmlReport([], features, Start, Start.AddSeconds(5), null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return ReportHtml.Read(path);
    }

    /// <summary>Where the <c>&lt;summary&gt;</c> of class <paramref name="cls"/> whose text starts with
    /// <paramref name="name"/> opens.</summary>
    private static int SummaryAt(string html, string cls, string name)
    {
        var marker = $"<summary class=\"{cls}";
        for (var at = html.IndexOf(marker, StringComparison.Ordinal); at >= 0; at = html.IndexOf(marker, at + 1, StringComparison.Ordinal))
        {
            var text = html.IndexOf('>', at) + 1;
            if (string.CompareOrdinal(html, text, name, 0, name.Length) == 0 && html[text + name.Length] is ' ' or '<')
                return at;
        }
        Assert.Fail($"no {cls} summary for {name}");
        return -1;
    }

    /// <summary>The opening tag of the scenario element whose summary names <paramref name="name"/>.</summary>
    private static string ScenarioElement(string html, string name)
    {
        var open = html.LastIndexOf("<details class=\"scenario", SummaryAt(html, "h3", name), StringComparison.Ordinal);
        return html[open..(html.IndexOf('>', open) + 1)];
    }

    /// <summary>The summary line of the scenario named <paramref name="name"/>, up to its copy button.</summary>
    private static string ScenarioSummary(string html, string name)
    {
        var at = SummaryAt(html, "h3", name);
        return html[at..html.IndexOf("<button class=\"copy-scenario-name\"", at, StringComparison.Ordinal)];
    }

    private static string FeatureSummary(string html, string name)
    {
        var at = SummaryAt(html, "h2", name);
        return html[at..html.IndexOf("</summary>", at, StringComparison.Ordinal)];
    }

    [Fact]
    public void A_scenario_carries_its_effective_tags_in_one_attribute_each_name_percent_encoded()
    {
        var html = Generate("Tags_DataTags.html", new Feature
        {
            DisplayName = "Shop", Tags = ["checkout"],
            Scenarios = [Run("s1", "Pay", ["a,b", "say \"hi\"", "x y", "50%", "Checkout"], happy: true)]
        });
        var element = ScenarioElement(html, "Pay");
        Assert.Contains(" data-tags=\"Happy%20Path,checkout,a%2Cb,say%20%22hi%22,x%20y,50%25\"", element);
        Assert.DoesNotContain(" data-labels=", element);
        Assert.DoesNotContain(" data-categories=", element);
    }

    [Fact]
    public void A_scenario_with_no_tags_carries_no_attribute()
    {
        var html = Generate("Tags_None.html", new Feature { DisplayName = "Shop", Scenarios = [Run("s1", "Browse")] });
        Assert.DoesNotContain(" data-tags=", ScenarioElement(html, "Browse"));
    }

    [Fact]
    public void A_scenario_s_attribute_spells_each_tag_as_its_chip_does()
    {
        var html = Generate("Tags_Spelling.html",
            new Feature { DisplayName = "A", Scenarios = [Run("a1", "First", ["Smoke"])] },
            new Feature { DisplayName = "B", Scenarios = [Run("b1", "Second", ["smoke", "Fast"])] });
        Assert.Contains(" data-tags=\"Smoke,Fast\"", ScenarioElement(html, "Second"));
        // the badge keeps the scenario's own spelling
        Assert.Contains("<span class=\"label\">smoke</span>", ScenarioSummary(html, "Second"));
    }

    [Fact]
    public void A_feature_shows_its_own_tags_and_a_scenario_shows_its_own_but_not_its_feature_s()
    {
        var html = Generate("Tags_Badges.html", new Feature
        {
            DisplayName = "Shop", Tags = ["checkout"],
            Scenarios = [Run("s1", "Pay", ["Checkout", "smoke", "Slow"], happy: true)]
        });
        Assert.Contains(" <span class=\"label\">checkout</span>", FeatureSummary(html, "Shop"));
        var summary = ScenarioSummary(html, "Pay");
        Assert.Contains(">Pay <span class=\"label\">Happy Path</span> <span class=\"label\">smoke</span> <span class=\"label\">Slow</span>", summary);
        Assert.DoesNotContain(">Checkout</span>", summary);
        Assert.DoesNotContain(">checkout</span>", summary);
    }

    [Fact]
    public void A_retried_scenario_has_a_retry_badge_from_its_attempt_and_no_retry_tag()
    {
        var html = Generate("Tags_Retry.html", new Feature
        {
            DisplayName = "Shop",
            Scenarios = [Run("s1", "Pay", ["flaky"], attempt: 3), Run("s2", "Browse", attempt: 2), Run("s3", "Look"),
                Run("s4", "Once", attempt: 1)]
        });
        Assert.Contains("<span class=\"label\">flaky</span> <span class=\"label retry-label\" title=\"Shown: attempt 3. The attempts before it are not in the report.\">retry 2</span>",
            ScenarioSummary(html, "Pay"));
        Assert.Contains("<span class=\"label retry-label\" title=\"Shown: attempt 2. The attempts before it are not in the report.\">retry 1</span>",
            ScenarioSummary(html, "Browse"));
        Assert.DoesNotContain("retry-label", ScenarioSummary(html, "Look"));
        // a first attempt that says so is not a retry
        Assert.DoesNotContain("retry-label", ScenarioSummary(html, "Once"));
        Assert.Contains(" data-tags=\"flaky\"", ScenarioElement(html, "Pay"));
        Assert.DoesNotContain("data-tag=\"retry", html);
        // still found by searching for it, as the label was
        Assert.Contains("retry 2", ScenarioElement(html, "Pay").Split(" data-search=\"")[1].Split('"')[0]);
    }

    [Fact]
    public void A_parameterized_group_is_found_by_its_rows_tags()
    {
        // the group's own search text holds every row's tags, as 4.x's held their labels and categories
        var html = Generate("Tags_GroupSearch.html", new Feature
        {
            DisplayName = "Shop",
            Scenarios = [Run("s1", "Price(size: S)", ["smoke"], outline: "Price"), Run("s2", "Price(size: L)", ["Slow"], outline: "Price")]
        });
        var open = html.IndexOf("<details class=\"scenario scenario-parameterized", StringComparison.Ordinal);
        Assert.True(open >= 0, "no parameterized group");
        var words = html[open..(html.IndexOf('>', open) + 1)].Split(" data-search=\"")[1].Split('"')[0].Split(' ');
        Assert.Contains("smoke", words);
        Assert.Contains("slow", words);
    }

    [Fact]
    public void A_tag_spelled_happy_path_in_another_case_is_the_Happy_Path_chip()
    {
        // the row's Happy Path chip, which the Happy paths control and the link find by its name
        var html = Generate("Tags_HappyCase.html", new Feature { DisplayName = "Shop", Scenarios = [Run("s1", "Pay", ["happy path", "smoke"])] });
        Assert.Contains(" data-tags=\"Happy%20Path,smoke\"", ScenarioElement(html, "Pay"));
        Assert.Contains("<button type=\"button\" class=\"tag-toggle\" data-tag=\"Happy Path\"", html);
        Assert.DoesNotContain("data-tag=\"happy path\"", html);
    }

    [Fact]
    public void A_happy_path_labelled_Happy_Path_draws_that_badge_once()
    {
        // LightBDD's [HappyPath] is the label "Happy Path" as well as the flag (F11)
        var html = Generate("Tags_HappyLabel.html", new Feature { DisplayName = "Shop", Scenarios = [Run("s1", "Pay", ["Happy Path", "smoke"], happy: true)] });
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(ScenarioSummary(html, "Pay"), ">Happy Path</span>"));
        Assert.Contains(" data-tags=\"Happy%20Path,smoke\"", ScenarioElement(html, "Pay"));
    }

    [Fact]
    public void A_report_whose_only_tags_are_a_feature_s_names_tags_in_the_filter_rows_title()
    {
        var html = Generate("Tags_FeatureOnlyTitle.html", new Feature { DisplayName = "Shop", Tags = ["checkout"], Scenarios = [Run("s1", "Pay")] });
        // named in Clear All's title, which lists the rows the box has (More filters' title did until plans/SEARCH_PLAN.md F5)
        Assert.Matches("onclick=\"clear_all_filters[(][)]\" title=\"Clear every filter: [^\"]* tags\"", html);
        Assert.Contains("<div class=\"tag-filters\">", html);
    }

    /// <summary>The Tags row's chips in page order, each as its name and whether it starts hidden.</summary>
    private static List<(string Tag, bool Hidden)> TagRow(string html)
    {
        var row = html[html.IndexOf("<div class=\"tag-filters\">", StringComparison.Ordinal)..];
        row = row[..row.IndexOf("</div>", StringComparison.Ordinal)];
        return System.Text.RegularExpressions.Regex.Matches(row, "<button type=\"button\" class=\"tag-toggle[^\"]*\" data-tag=\"([^\"]*)\"([^>]*)>")
            .Select(m => (System.Net.WebUtility.HtmlDecode(m.Groups[1].Value), m.Groups[2].Value.Contains(" data-more=\"1\" hidden", StringComparison.Ordinal)))
            .ToList();
    }

    [Fact]
    public void A_long_tags_row_shows_the_tags_most_scenarios_carry_and_a_chip_for_the_rest()
    {
        // Q9: 400 tags painted 1,150 px at 1280 px, more than a screen, so the row is cut (plans/TAGS_PLAN.md §4.8)
        var shown = TagChips.MostShown;
        var names = Enumerable.Range(0, shown + 5).Select(i => string.Create(CultureInfo.InvariantCulture, $"T{i:000}")).ToArray();
        var scenarios = names.Select((t, i) => Run($"s{i}", $"Scenario {i:000}", [t])).ToList();
        // the last five names on a second scenario each: the most carried, though last in the row's order
        scenarios.AddRange(names[^5..].Select((t, i) => Run($"x{i}", $"Again {i}", [t])));
        scenarios.Add(Run("h", "Happy", happy: true));
        scenarios.Add(Run("u", "Untagged"));
        var html = Generate("Tags_LongRow.html", new Feature { DisplayName = "Shop", Scenarios = [.. scenarios] });

        var row = TagRow(html);
        // every chip in the row's order, Happy Path first and Untagged last, whether shown or not
        Assert.Equal(["Happy Path", .. names, "__untagged__"], row.Select(c => c.Tag));
        // shown: Happy Path, Untagged, the five carried twice, then the rest in the row's order until the row is full
        string[] hidden = names[(shown - 5)..shown];
        Assert.Equal(hidden, row.Where(c => c.Hidden).Select(c => c.Tag));
        // after Untagged, before the row's Clear
        Assert.Contains(string.Create(CultureInfo.InvariantCulture,
            $"data-none=\"1\" data-scenarios=\"1\" aria-pressed=\"false\" title=\"1 scenario. Click: only these. Click again: off.\">Untagged</button><button type=\"button\" class=\"tag-more\" data-shown=\"{shown}\" aria-expanded=\"false\" title=\"Show the other 5 tags. The row starts with the {shown} tags the most scenarios carry.\">5 more</button><button type=\"button\" class=\"group-clear\""),
            html);
    }

    [Fact]
    public void A_row_one_tag_too_long_says_one_more_in_the_singular()
    {
        var names = Enumerable.Range(0, TagChips.MostShown + 1).Select(i => string.Create(CultureInfo.InvariantCulture, $"T{i:000}")).ToArray();
        var html = Generate("Tags_OneMore.html", new Feature
        {
            DisplayName = "Shop", Scenarios = [.. names.Select((t, i) => Run($"s{i}", $"Scenario {i:000}", [t]))]
        });
        // every tag carried once: the last in the row's order is the one left out
        Assert.Equal([names[^1]], TagRow(html).Where(c => c.Hidden).Select(c => c.Tag));
        Assert.Contains(string.Create(CultureInfo.InvariantCulture,
            $"title=\"Show the other 1 tag. The row starts with the {TagChips.MostShown} tags the most scenarios carry.\">1 more</button>"), html);
    }

    [Fact]
    public void A_tags_row_that_fits_shows_every_chip_and_no_more_chip()
    {
        var names = Enumerable.Range(0, TagChips.MostShown).Select(i => string.Create(CultureInfo.InvariantCulture, $"T{i:000}")).ToArray();
        var html = Generate("Tags_FullRow.html", new Feature
        {
            DisplayName = "Shop", Scenarios = [.. names.Select((t, i) => Run($"s{i}", $"Scenario {i:000}", [t]))]
        });
        Assert.Equal(names, TagRow(html).Select(c => c.Tag));
        Assert.DoesNotContain(TagRow(html), c => c.Hidden);
        Assert.DoesNotContain("class=\"tag-more\"", html);
    }

    [Fact]
    public void An_author_s_own_retry_tag_is_a_tag()
    {
        var html = Generate("Tags_OwnRetry.html", new Feature { DisplayName = "Shop", Scenarios = [Run("s1", "Pay", ["retry 2"])] });
        Assert.Contains(" data-tags=\"retry%202\"", ScenarioElement(html, "Pay"));
        Assert.Contains("<button type=\"button\" class=\"tag-toggle\" data-tag=\"retry 2\"", html);
    }

    [Fact]
    public void A_parameterized_group_shows_the_tags_all_its_rows_share_less_its_feature_s_and_filters_on_all_of_them()
    {
        var html = Generate("Tags_Group.html", new Feature
        {
            DisplayName = "Shop", Tags = ["checkout"],
            Scenarios = [Run("s1", "Price(size: S)", ["smoke", "fast", "checkout"], outline: "Price"), Run("s2", "Price(size: L)", ["Smoke", "Checkout", "slow"], outline: "Price")]
        });
        var open = html.IndexOf("<details class=\"scenario scenario-parameterized", StringComparison.Ordinal);
        Assert.True(open >= 0, "no parameterized group");
        var element = html[open..(html.IndexOf('>', open) + 1)];
        // the rows' tags in the chips' spellings (the data lists s1 first, so its "smoke" is the chip's), in the order the
        // group draws its rows
        var tags = element.Split(" data-tags=\"")[1].Split('"')[0].Split(',');
        Assert.Equal(["checkout", "fast", "slow", "smoke"], tags.Order(StringComparer.Ordinal));
        var summary = html[open..html.IndexOf("</summary>", open, StringComparison.Ordinal)];
        // the shared badge keeps the spelling of the row the group draws first
        Assert.Matches(">Price <span class=\"label\">(smoke|Smoke)</span> <span class=\"label\">", summary);
        Assert.DoesNotContain(">fast</span>", summary);
        Assert.DoesNotContain(">slow</span>", summary);
        // both rows carry the feature's tag, so only leaving the feature's out keeps it off the group
        Assert.DoesNotContain(">checkout</span>", summary);
        Assert.DoesNotContain(">Checkout</span>", summary);
    }
}
