using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// What the instant search does to a generated report (SEARCH_PLAN B8). These facts were SearchFunctionTests', which ran
/// a C# port of the search over a page of their own: the port hid with a search-hidden class the page does not use,
/// closed every scenario on each search, which 5.2.0 stopped on purpose, and read a happy path's .hide class, gone since
/// 5.0.0's chips, so they passed whatever the shipped script did. Here the report's own scripts run: a scenario the
/// search leaves out has display:none written on it, a rule or a feature none of whose scenarios shows is hidden the same
/// way (report-scenario-feature-map-helper.js applyVisibility), one match is opened where it is, and emptying the search
/// shows the page as the other filters leave it. The search's words and what it matches are
/// ShippedSearchFunctionTests'.
/// </summary>
[Collection(PlaywrightCollections.Search)]
public class SearchFunctionPageTests : PlaywrightTestBase
{
    public SearchFunctionPageTests(PlaywrightFixture fixture) : base(fixture) { }

    // The bakery, in page order: a feature lists its happy paths first, then the rest by name. "chocolate" is in four
    // names, "cocoa" only in the first one's steps, and the phrase "chocolate cake" only in its name.
    private const string ChocolateCake = "scenario-making-chocolate-cake", Glaze = "scenario-chocolate-glaze-on-a-cake",
        Brownies = "scenario-making-chocolate-brownies", Tart = "scenario-making-strawberry-tart",
        Vanilla = "scenario-making-vanilla-cake", Lemonade = "scenario-making-lemonade", HotChocolate = "scenario-hot-chocolate";
    private static readonly string[] Everything = [ChocolateCake, Glaze, Brownies, Tart, Vanilla, Lemonade, HotChocolate];
    private static readonly string[] Chocolate = [ChocolateCake, Glaze, Brownies, HotChocolate];

    // the scenarios showing, in page order: one the filters hide has display:none written on it
    private const string ShownScript =
        "Array.from(document.querySelectorAll('#report-content details.scenario')).filter(s => s.style.display !== 'none').map(s => s.id)";

    private static Scenario Run(string id, string name, bool happy = false, string? rule = null, string[]? steps = null) => new()
    {
        Id = id, DisplayName = name, IsHappyPath = happy, Rule = rule, Result = ExecutionResult.Passed,
        Steps = steps?.Select(text => new ScenarioStep { Keyword = "Given", Text = text, Status = ExecutionResult.Passed }).ToArray()
    };

    private string Generate(string fileName, Feature[] features, DiagramAsCode[]? diagrams = null,
        PlantUmlRendering rendering = PlantUmlRendering.BrowserJs, ResolvedToggleDefaults? toggles = null, bool separateBackgroundSteps = false)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams ?? [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: rendering,
            separateBackgroundSteps: separateBackgroundSteps, toggleDefaults: toggles);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private string GenerateBakery(string fileName) => Generate(fileName,
    [
        new()
        {
            DisplayName = "Baking",
            Scenarios =
            [
                Run("s1", "Making Chocolate Cake", happy: true, steps: ["the oven is hot", "I fold in the cocoa"]),
                Run("s2", "Making Chocolate Brownies"),
                Run("s3", "Making Vanilla Cake"),
                Run("s4", "Chocolate glaze on a cake"),
                Run("s5", "Making Strawberry Tart")
            ]
        },
        new() { DisplayName = "Drinks", Scenarios = [Run("s6", "Hot Chocolate"), Run("s7", "Making Lemonade", happy: true)] }
    ]);

    private async Task OpenAsync(string url)
    {
        await Page.GotoReportAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
    }

    private Task<string[]> ShownAsync() => Page.EvaluateAsync<string[]>($"() => {ShownScript}");

    // Waits until the page shows the scenarios expected, and fails with the ones it shows instead
    private async Task WaitForShownAsync(params string[] shown)
    {
        try
        {
            await Page.WaitForFunctionAsync($"expected => JSON.stringify({ShownScript}) === JSON.stringify(expected)", shown,
                new() { PollingInterval = 200, Timeout = 15000 });
        }
        catch (TimeoutException)
        {
            Assert.Equal(shown, await ShownAsync());
            throw;
        }
    }

    // Types a search as a reader does and waits for its answer, which comes 300 ms after the last key, and at once when
    // the field is emptied
    private async Task SearchAsync(string query, params string[] shown)
    {
        await FillSearchBar(query);
        await WaitForShownAsync(shown);
    }

    // what the page writes on each scenario, rule or feature: its own display, by its id or its title
    private async Task<Dictionary<string, string>> DisplaysAsync(string selector) => (await Page.EvaluateAsync<string[][]>("""
        selector => Array.from(document.querySelectorAll('#report-content ' + selector)).map(e =>
            [e.id || e.querySelector(':scope > summary').textContent.trim(), e.style.display])
        """, selector)).ToDictionary(e => e[0], e => e[1]);

    // ---- what a search shows and hides ----

    [Fact]
    public async Task A_search_shows_the_scenarios_it_matches_and_writes_display_none_on_the_rest()
    {
        await OpenAsync(GenerateBakery("SearchPage_Hides.html"));

        await SearchAsync("chocolate", Chocolate);

        var displays = await DisplaysAsync("details.scenario");
        Assert.Equal(["", "", "", "none", "none", "none", ""], Everything.Select(id => displays[id]).ToArray());
    }

    // SearchFunctionTests' port closed every scenario on each search, and a search of several matches found them closed;
    // 5.2.0 stopped the closing on purpose (report-search-function.js). What the page does is open only a single match:
    // several are left as they were, the one the reader opened too.
    [Fact]
    public async Task Several_matches_are_left_as_they_were_none_opened_and_none_closed()
    {
        await OpenAsync(GenerateBakery("SearchPage_Several.html"));
        await Page.Locator("#report-content details.feature > summary").First.ClickAsync();
        await Page.Locator($"#{Brownies} > summary").ClickAsync();

        await SearchAsync("chocolate", Chocolate);

        Assert.Equal(new[] { false, false, true, false }, await Page.EvaluateAsync<bool[]>("ids => ids.map(id => document.getElementById(id).open)", Chocolate));
    }

    [Fact]
    public async Task A_feature_shows_while_one_of_its_scenarios_matches_and_is_hidden_when_none_does()
    {
        await OpenAsync(GenerateBakery("SearchPage_Features.html"));

        await SearchAsync("chocolate", Chocolate);
        Assert.Equal(new Dictionary<string, string> { ["Baking"] = "", ["Drinks"] = "" }, await DisplaysAsync("details.feature"));

        await SearchAsync("lemonade", Lemonade);
        Assert.Equal(new Dictionary<string, string> { ["Baking"] = "none", ["Drinks"] = "" }, await DisplaysAsync("details.feature"));
    }

    [Fact]
    public async Task A_search_matching_nothing_hides_every_feature()
    {
        await OpenAsync(GenerateBakery("SearchPage_Nothing.html"));

        await SearchAsync("marzipan");

        Assert.Equal(new Dictionary<string, string> { ["Baking"] = "none", ["Drinks"] = "none" }, await DisplaysAsync("details.feature"));
    }

    [Fact]
    public async Task A_word_only_in_a_scenario_s_steps_shows_it()
    {
        await OpenAsync(GenerateBakery("SearchPage_Steps.html"));

        await SearchAsync("cocoa", ChocolateCake);
    }

    [Fact]
    public async Task A_quoted_phrase_matches_only_where_its_words_stand_together()
    {
        await OpenAsync(GenerateBakery("SearchPage_Phrase.html"));

        await SearchAsync("\"chocolate cake\"", ChocolateCake);
        // unquoted, each word anywhere in the scenario
        await SearchAsync("chocolate cake", ChocolateCake, Glaze);
    }

    // SearchFunctionTests' port read the scenario's whole text, its PlantUML source with it. The instant search reads a
    // diagram's participants' names and calls' paths, never names written only in its arrows, as here; the deep search
    // reads the whole of each diagram and shows the scenario after it (DeepSearchTests)
    [Fact]
    public async Task A_word_only_in_a_scenario_s_diagram_shows_it_once_the_deep_search_has_read_it()
    {
        await OpenAsync(Generate("SearchPage_Diagram.html",
            [new() { DisplayName = "Orders", Scenarios = [Run("s1", "Create order"), Run("s2", "Delete order")] }],
            [new("s1", "", "@startuml\nOrderService -> PaymentGateway : POST /payments\n@enduml")]));

        await SearchAsync("paymentgateway", "scenario-create-order");

        Assert.Equal(1, await Page.EvaluateAsync<int>("() => window.__kronikolSearch.lastQuery.added"));
        Assert.Equal("none", (await DisplaysAsync("details.scenario"))["scenario-delete-order"]);
    }

    // ---- one match ----

    // Everything the match sits in starts closed: its feature, as on any report, and its rule, steps, background and
    // diagrams, as a report can be configured to start them. Its diagrams are drawn by a PlantUML server, the rendering
    // with a Raw Plant UML disclosure; no request leaves the page, the image being a file beside it that is not there.
    private string GenerateReveal(string fileName)
    {
        const string Source = "@startuml\nparticipant \"Oven\" as oven\noven -> oven : bake\n@enduml";
        ScenarioStep[] background = [new() { Keyword = "Given", Text = "the oven is on", Status = ExecutionResult.Passed }];
        return Generate(fileName,
            [
                new()
                {
                    DisplayName = "Baking",
                    Scenarios =
                    [
                        Run("s1", "Making Chocolate Cake", rule: "Cakes", steps: ["I fold in the cocoa"]) with { BackgroundSteps = background },
                        Run("s2", "Making Vanilla Cake", rule: "Cakes", steps: ["I add the vanilla"]) with { BackgroundSteps = background }
                    ]
                }
            ],
            [new("s1", "stub.png", Source), new("s2", "stub.png", Source)],
            PlantUmlRendering.Server,
            ResolvedToggleDefaults.BuiltIn with { RulesOpen = false, StepsSectionOpen = false, DiagramsSectionOpen = false },
            separateBackgroundSteps: true);
    }

    // the places a scenario sits in, and its sections, that are open
    private Task<string> OpenedAsync(string id) => Page.EvaluateAsync<string>("""
        id => {
            const s = document.getElementById(id);
            const places = {
                feature: s.closest('details.feature'), rule: s.closest('details.rule'), scenario: s,
                steps: s.querySelector('details.scenario-steps'), background: s.querySelector('details.scenario-background'),
                diagrams: s.querySelector('details.example-diagrams'), raw: s.querySelector('details.example')
            };
            return Object.keys(places).filter(k => places[k].open).join(' ');
        }
        """, id);

    [Fact]
    public async Task One_match_is_opened_where_it_is_with_its_steps_and_diagrams_but_not_its_raw_PlantUml()
    {
        await OpenAsync(GenerateReveal("SearchPage_Reveal.html"));
        Assert.Equal("", await OpenedAsync(ChocolateCake));

        await SearchAsync("chocolate", ChocolateCake);

        Assert.Equal("feature rule scenario steps background diagrams", await OpenedAsync(ChocolateCake));
        Assert.Equal("none", (await DisplaysAsync("details.scenario"))[Vanilla]);
    }

    // ---- emptying the search ----

    [Fact]
    public async Task Emptying_the_search_shows_every_scenario_and_feature_again()
    {
        await OpenAsync(GenerateBakery("SearchPage_Empty.html"));
        await SearchAsync("lemonade", Lemonade);

        await SearchAsync("", Everything);

        Assert.Equal(new Dictionary<string, string> { ["Baking"] = "", ["Drinks"] = "" }, await DisplaysAsync("details.feature"));
    }

    [Fact]
    public async Task A_search_of_spaces_is_no_search()
    {
        await OpenAsync(GenerateBakery("SearchPage_Spaces.html"));
        await SearchAsync("chocolate", Chocolate);

        await SearchAsync("   ", Everything);
    }

    // SearchFunctionTests' port read a happy path filter's .hide class, gone since 5.0.0: Happy paths is the Happy Path
    // chip under another face (report-filter-chips-script.js), and its flag and the search's each hide a scenario
    [Fact]
    public async Task A_search_keeps_to_what_Happy_paths_shows_and_emptying_it_leaves_Happy_paths_on()
    {
        await OpenAsync(GenerateBakery("SearchPage_Happy.html"));
        await OpenFilterRowsAsync();
        await Page.Locator(".filtering-box .happy-seg[data-hp='in']").First.ClickAsync();
        await WaitForShownAsync(ChocolateCake, Lemonade);

        // the brownies, the glaze and the hot chocolate match, and are not happy paths
        await SearchAsync("chocolate", ChocolateCake);

        await SearchAsync("", ChocolateCake, Lemonade);
    }

    // ---- rules ----

    // Shop has a rule with a match and a rule with none; Payments has two rules with none; Returns has a rule with none
    // and a match outside its rules. A feature lists its scenarios by name, so each rule's scenarios stand together.
    private string GenerateRules(string fileName) => Generate(fileName,
    [
        new()
        {
            DisplayName = "Shop",
            Scenarios =
            [
                Run("r1", "Create account", rule: "Account creation"),
                Run("r2", "Create admin account", rule: "Account creation"),
                Run("r3", "Place chocolate order", rule: "Order processing"),
                Run("r4", "Place vanilla order", rule: "Order processing")
            ]
        },
        new() { DisplayName = "Payments", Scenarios = [Run("r5", "Charge card", rule: "Card payments"), Run("r6", "Refund card", rule: "Refunds")] },
        new() { DisplayName = "Returns", Scenarios = [Run("r7", "Check account", rule: "Account checks"), Run("r8", "Return chocolate box")] }
    ]);

    [Fact]
    public async Task A_rule_or_a_feature_shows_while_one_of_its_scenarios_matches_and_is_hidden_when_none_does()
    {
        await OpenAsync(GenerateRules("SearchPage_Rules.html"));

        await SearchAsync("chocolate", "scenario-place-chocolate-order", "scenario-return-chocolate-box");

        Assert.Equal(new Dictionary<string, string>
        {
            ["Account creation"] = "none", ["Order processing"] = "", ["Card payments"] = "none", ["Refunds"] = "none",
            ["Account checks"] = "none"
        }, await DisplaysAsync("details.rule"));
        Assert.Equal(new Dictionary<string, string> { ["Shop"] = "", ["Payments"] = "none", ["Returns"] = "" }, await DisplaysAsync("details.feature"));
    }

    [Fact]
    public async Task Emptying_the_search_shows_every_rule_again()
    {
        await OpenAsync(GenerateRules("SearchPage_RulesEmpty.html"));
        await SearchAsync("chocolate", "scenario-place-chocolate-order", "scenario-return-chocolate-box");

        await SearchAsync("", "scenario-create-account", "scenario-create-admin-account", "scenario-place-chocolate-order",
            "scenario-place-vanilla-order", "scenario-charge-card", "scenario-refund-card", "scenario-check-account",
            "scenario-return-chocolate-box");

        Assert.Equal(new Dictionary<string, string>
        {
            ["Account creation"] = "", ["Order processing"] = "", ["Card payments"] = "", ["Refunds"] = "", ["Account checks"] = ""
        }, await DisplaysAsync("details.rule"));
    }
}
