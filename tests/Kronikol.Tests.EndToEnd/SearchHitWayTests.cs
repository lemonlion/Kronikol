using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The way to a match (plans/SEARCH_PLAN.md 4.6.5, the owner's A9) for the places a page holds as HTML: a result's line on
/// the Search tab names its match (hit= in the address), and following it opens what hides the match, brings it into the
/// window, puts the focus on it and says so. H1 to H5 and R30; the marks themselves are SearchHitTests'.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class SearchHitWayTests : PlaywrightTestBase
{
    public SearchHitWayTests(PlaywrightFixture fixture) : base(fixture) { }

    private static readonly DateTime T0 = new(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);

    private static ScenarioStep Step(string keyword, string text, ExecutionResult status = ExecutionResult.Passed, ScenarioStep[]? sub = null) =>
        new() { Keyword = keyword, Text = text, Status = status, SubSteps = sub };

    internal static Feature[] Kitchen() =>
    [
        new Feature
        {
            DisplayName = "Kitchen",
            Scenarios =
            [
                new Scenario
                {
                    Id = "k1", DisplayName = "Breakfast is served", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(40),
                    Steps =
                    [
                        Step("Given", "the oven is hot"),
                        // a composite step that passed is drawn closed, its sub-steps held until it first opens: the page holds
                        // them once their HTML is long enough to be worth it (StepDeferral.Threshold), so the pans make it so
                        Step("When", "breakfast is cooked", sub:
                        [
                            Step("Given", "the eggs are fried"),
                            .. Enumerable.Range(1, 14).Select(n => Step("And", $"pan {n} is warmed on the stove")),
                            Step("And", "the toast is buttered"),
                        ]),
                        Step("Then", "the plate holds marmalade"),
                    ]
                },
                new Scenario
                {
                    Id = "k2", DisplayName = "Lunch is refused", Result = ExecutionResult.Failed, Duration = TimeSpan.FromMilliseconds(30),
                    ErrorMessage = "Expected the soup\nbut got a sandwich with pickles\nat the counter",
                    Steps = [Step("Given", "the soup is ordered", ExecutionResult.Failed)]
                },
                new Scenario
                {
                    Id = "k3", DisplayName = "The settings are applied", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(20),
                    Steps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "When", Status = ExecutionResult.Passed,
                            Text = "I apply a large AppConfig { Host = localhost, Port = 8080, Timeout = 30, RetryCount = 3, Debug = True }",
                            TextSegments = [StepTextSegment.Literal("I apply a large "), StepTextSegment.TableRef("config")],
                            Parameters =
                            [
                                new StepParameter
                                {
                                    Name = "config", Kind = StepParameterKind.Inline,
                                    InlineValue = new InlineParameterValue("AppConfig { Host = localhost, Port = 8080, Timeout = 30, RetryCount = 3, Debug = True }",
                                        null, VerificationStatus.NotApplicable)
                                }
                            ]
                        }
                    ]
                },
                Row("b1", "empty basket", "0"),
                Row("b2", "jam jar", "350"),
            ]
        }
    ];

    private static Scenario Row(string id, string @case, string grams) => new()
    {
        Id = id, DisplayName = $"A basket is weighed ({@case})", KeyedByOutline = true, OutlineId = "A basket is weighed (<case>)",
        ExampleValues = new() { ["case"] = @case, ["grams"] = grams }, ExamplesBlockIndex = 0, Result = ExecutionResult.Passed,
        Duration = TimeSpan.FromMilliseconds(10),
    };

    // the kitchen report, written to the temp folder and copied beside the other pages; its address
    internal static string WriteKitchen(string tempDir, string outputDir, string fileName)
    {
        var path = ReportGenerator.GenerateHtmlReport([], Kitchen(), T0, T0.AddMinutes(1), null, Path.Combine(tempDir, fileName), "Kitchen", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task OpenAsync(string fileName)
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await Page.GotoReportAsync(WriteKitchen(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Page.WaitForFunctionAsync("() => !!window.kronikol_search_results && !!window.kronikol_hit", null, new() { PollingInterval = 200 });
    }

    private async Task SearchAsync(string query)
    {
        await FillSearchBar(query, toSearchTab: true);
        await Page.WaitForFunctionAsync("() => !document.getElementById('panel-search').hidden && document.getElementById('search-count').textContent !== ''",
            null, new() { PollingInterval = 200 });
    }

    // a line of the first result, by its kind label
    private ILocator Line(string kind) => Page.Locator($"#panel-search .search-result .search-line:has(.search-line-kind:text-is('{kind}'))").First;

    private async Task FollowAsync(ILocator link)
    {
        await link.ClickAsync();
        await Page.WaitForFunctionAsync("() => document.querySelector('.report-tabs > .report-tab[aria-selected=\"true\"]').dataset.tab === 'features'",
            null, new() { PollingInterval = 200 });
    }

    private Task<string> HashAsync() => Page.EvaluateAsync<string>("() => decodeURIComponent(location.hash)");

    // the focused element's own text, once the way has put the focus somewhere other than the link followed
    private async Task<string> FocusedTextAsync(string within)
    {
        await Page.WaitForFunctionAsync("s => !!document.activeElement && !!document.activeElement.closest(s)", within, new() { PollingInterval = 200 });
        return await Page.EvaluateAsync<string>("() => document.activeElement.textContent.replace(/\\s+/g, ' ').trim()");
    }

    private Task<bool> InWindowAsync(string selector) => Page.EvaluateAsync<bool>(
        "s => { const r = document.activeElement.closest(s).getBoundingClientRect(); return r.top >= 0 && r.bottom <= window.innerHeight; }", selector);

    // H1
    [Fact]
    public async Task A_step_line_opens_the_steps_scrolls_to_the_step_and_marks_its_words()
    {
        await OpenAsync("Hit_H1.html");
        await SearchAsync("marmalade");

        await FollowAsync(Line("STEP"));

        Assert.Contains("hit=s:3", await HashAsync());
        Assert.Contains("the plate holds marmalade", await FocusedTextAsync("details.scenario-steps"));
        Assert.True(await Page.EvaluateAsync<bool>("() => document.querySelector('#scenario-k1 details.scenario-steps, details.scenario details.scenario-steps').open"));
        Assert.True(await InWindowAsync(".step"));
    }

    // H2
    [Fact]
    public async Task A_sub_step_line_opens_each_composite_step_and_draws_held_sub_steps_first()
    {
        await OpenAsync("Hit_H2.html");
        Assert.True(await Page.EvaluateAsync<bool>("() => !!document.querySelector('details.step-collapsible > .sub-steps[data-steps]')"),
            "the composite step's sub-steps are not held, so this fact proves nothing");
        await SearchAsync("buttered");

        await FollowAsync(Line("STEP"));

        Assert.Contains("the toast is buttered", await FocusedTextAsync(".sub-steps"));
        Assert.True(await Page.EvaluateAsync<bool>("() => document.querySelector('details.step-collapsible').open"));
        Assert.False(await Page.EvaluateAsync<bool>("() => !!document.querySelector('details.step-collapsible > .sub-steps[data-steps]')"));
    }

    // H2: a composite step whose held sub-steps never draw (here its toggle is stopped before the page's drawer hears it)
    // is tried once: the way goes on to the next held step, and with none left it ends, saying so, where it once drew
    // the same step again every three seconds for as long as the page stayed open
    [Fact]
    public async Task A_held_step_that_never_draws_is_tried_once()
    {
        await OpenAsync("Hit_H2_never.html");
        await Page.EvaluateAsync("""
            () => {
                const d = document.querySelector('details.step-collapsible');
                window.__toggles = 0;
                window.addEventListener('toggle', e => { if (e.target === d) { window.__toggles++; e.stopImmediatePropagation(); } }, true);
            }
            """);
        await SearchAsync("buttered");

        await FollowAsync(Line("STEP"));

        await Page.WaitForFunctionAsync(
            "() => { const s = document.querySelector('.kron-nav-status'); return !!s && s.textContent.indexOf('not found in this report') >= 0; }",
            null, new() { Timeout = 10000, PollingInterval = 200 });
        Assert.Equal(1, await Page.EvaluateAsync<int>("() => window.__toggles"));
    }

    // H3
    [Fact]
    public async Task A_long_value_line_shows_the_value_and_marks_it()
    {
        await OpenAsync("Hit_H3.html");
        await SearchAsync("retrycount");

        await FollowAsync(Line("STEP"));

        await FocusedTextAsync(".step");
        await Page.WaitForFunctionAsync("() => { const b = document.querySelector('button.step-table-ref[data-param=\"config\"]'); const e = b && b.nextElementSibling; return !!e && e.classList.contains('step-param-expand') && getComputedStyle(e).display !== 'none'; }",
            null, new() { PollingInterval = 200 });
        Assert.Contains("RetryCount", await Page.Locator(".step-param-expand").First.TextContentAsync());
    }

    // H4
    [Fact]
    public async Task An_error_line_opens_the_failure_and_marks_the_words_in_it()
    {
        await OpenAsync("Hit_H4.html");
        // the reader had closed the failure
        await Page.EvaluateAsync("() => document.querySelectorAll('details.failure-result').forEach(d => d.open = false)");
        await SearchAsync("pickles");

        await FollowAsync(Line("ERROR"));

        Assert.Contains("hit=e:1", await HashAsync());
        Assert.Contains("pickles", await FocusedTextAsync("details.failure-result"));
        Assert.True(await Page.EvaluateAsync<bool>("() => document.activeElement.closest('details.failure-result').open"));
    }

    // H5
    [Fact]
    public async Task An_example_line_chooses_its_row_and_opens_its_cell()
    {
        await OpenAsync("Hit_H5.html");
        await SearchAsync("jam");

        await FollowAsync(Line("EXAMPLE"));

        Assert.Contains("hit=x:", await HashAsync());
        Assert.Equal("jam jar", await FocusedTextAsync("tr"));
        Assert.True(await Page.EvaluateAsync<bool>("() => document.activeElement.closest('tr').classList.contains('row-active')"));
    }

    // R30
    [Fact]
    public async Task A_result_s_title_goes_to_its_first_line_s_match()
    {
        await OpenAsync("Hit_R30.html");
        await SearchAsync("marmalade");

        await FollowAsync(Page.Locator("#panel-search .search-result-title").First);
        Assert.Contains("hit=s:3", await HashAsync());
        Assert.Contains("the plate holds marmalade", await FocusedTextAsync("details.scenario-steps"));

        // and Enter on a focused card goes the same way
        await Page.GoBackAsync();
        await Page.WaitForFunctionAsync("() => !document.getElementById('panel-search').hidden", null, new() { PollingInterval = 200 });
        await Page.Locator("#panel-search .search-result").First.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Page.WaitForFunctionAsync("() => document.querySelector('.report-tabs > .report-tab[aria-selected=\"true\"]').dataset.tab === 'features'",
            null, new() { PollingInterval = 200 });
        Assert.Contains("the plate holds marmalade", await FocusedTextAsync("details.scenario-steps"));
    }
    // Escape in the filter box is the box's (F4): it closes the box, and the marks a followed line left stay, as they do for
    // any layer's Escape; with the box shut, Escape clears them (4.6.5). The bar's Filters, clicked to open the box, does
    // not leave the scenario either.
    [Fact]
    public async Task Escape_in_the_filter_box_closes_it_and_leaves_the_marks()
    {
        await OpenAsync("Hit_BoxEscape.html");
        await SearchAsync("marmalade");
        await FollowAsync(Line("STEP"));
        await Page.WaitForFunctionAsync("() => !!window.kronikol_marks.marked()", null, new() { PollingInterval = 200 });

        await OpenFilterRowsAsync();
        Assert.True(await Page.EvaluateAsync<bool>("() => !!window.kronikol_marks.marked()"), "opening the filter box cleared the marks");
        await Page.Locator("#filtering-box .status-toggle").First.FocusAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator("#filtering-box")).ToBeHiddenAsync();
        Assert.True(await Page.EvaluateAsync<bool>("() => !!window.kronikol_marks.marked()"), "Escape in the box cleared the marks");

        await Page.Keyboard.PressAsync("Escape");
        await Page.WaitForFunctionAsync("() => !window.kronikol_marks.marked()", null, new() { PollingInterval = 200 });
    }

    // A line is cut to 160 characters around its match (4.6.3): one with no spaces (a token, a minified body) is cut where it
    // must be, not widened to its whole length looking for a word's edge
    [Fact]
    public async Task A_line_with_no_spaces_is_cut_to_160_around_the_match()
    {
        var token = new string('x', 500) + "plum" + new string('y', 500);
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Pantry",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "p1", DisplayName = "The token is stored", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10),
                        Steps = [Step("Given", "the token " + token + " is stored")]
                    }
                ]
            }
        };
        var path = ReportGenerator.GenerateHtmlReport([], features, T0, T0.AddMinutes(1), null, Path.Combine(TempDir, "Hit_Cut.html"), "Pantry", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, "Hit_Cut.html"), true);
        await Page.SetViewportSizeAsync(1280, 800);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Page.WaitForFunctionAsync("() => !!window.kronikol_search_results", null, new() { PollingInterval = 200 });
        await SearchAsync("plum");

        var line = (await Line("STEP").Locator(".search-line-text").InnerTextAsync()).Trim();
        Assert.Contains("plum", line);
        Assert.True(line.Length <= 162, $"the line is {line.Length} characters long");
        Assert.StartsWith("…", line);
        Assert.EndsWith("…", line);
    }

    // `not` before a word is a word the search finds (only !! negates; and, or and not are operators only in a legacy
    // query's tag expression), so a result's line marks it with the word after it
    [Fact]
    public async Task Not_before_a_word_is_a_word_the_lines_mark()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Bakery",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "d1", DisplayName = "The dough rests", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10),
                        Steps = [Step("Then", "do not knead the dough")]
                    }
                ]
            }
        };
        var path = ReportGenerator.GenerateHtmlReport([], features, T0, T0.AddMinutes(1), null, Path.Combine(TempDir, "Hit_Not.html"), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, "Hit_Not.html"), true);
        await Page.SetViewportSizeAsync(1280, 800);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Page.WaitForFunctionAsync("() => !!window.kronikol_search_results", null, new() { PollingInterval = 200 });
        await SearchAsync("not knead");

        var marked = await Line("STEP").Locator(".search-line-text b").AllInnerTextsAsync();
        Assert.Equal(["not", "knead"], marked.Select(m => m.Trim().ToLowerInvariant()).ToArray());
    }

    // a click in the filter box keeps the marks, but a filter that hides the marked scenario leaves it (4.6.5)
    [Fact]
    public async Task A_filter_that_hides_the_marked_scenario_takes_the_marks_off()
    {
        await OpenAsync("Hit_BoxHides.html");
        await SearchAsync("marmalade");
        await FollowAsync(Line("STEP"));
        await Page.WaitForFunctionAsync("() => !!window.kronikol_marks.marked()", null, new() { PollingInterval = 200 });

        await OpenFilterRowsAsync();
        await Page.Locator("#filtering-box .status-toggle[data-status='Failed']").ClickAsync();
        await Page.WaitForFunctionAsync("() => !window.kronikol_marks.marked()", null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    private async Task OpenFeaturesAsync(Feature[] features, string fileName)
    {
        var path = ReportGenerator.GenerateHtmlReport([], features, T0, T0.AddMinutes(1), null, Path.Combine(TempDir, fileName), "Pantry", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        await Page.SetViewportSizeAsync(1280, 800);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Page.WaitForFunctionAsync("() => !!window.kronikol_search_results && !!window.kronikol_hit", null, new() { PollingInterval = 200 });
    }

    // the texts of each result's lines of one kind, a result to a row
    private Task<string[][]> LinesOfEachResultAsync(string kind) => Page.EvaluateAsync<string[][]>("""
        kind => [...document.querySelectorAll('#panel-search .search-result')].map(r =>
            [...r.querySelectorAll('.search-line')].filter(l => (l.querySelector('.search-line-kind') || {}).textContent === kind)
                .map(l => l.querySelector('.search-line-text').textContent.trim()))
        """, kind);

    // A DESCRIPTION line is the result's own description or its feature's, never a sibling scenario's
    [Fact]
    public async Task A_description_line_is_the_result_s_own_description()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Pantry",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "d1", DisplayName = "The shelves are stocked", Description = "Keeps the marmalade on the top shelf",
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10), Steps = [Step("Given", "a shelf")]
                    },
                    new Scenario
                    {
                        Id = "d2", DisplayName = "The jars are counted", Description = "Counts the marmalade jars each week",
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10), Steps = [Step("Given", "a jar")]
                    }
                ]
            }
        };
        await OpenFeaturesAsync(features, "Hit_Description.html");
        await SearchAsync("marmalade");

        var lines = (await LinesOfEachResultAsync("DESCRIPTION")).Select(r => string.Join(" | ", r)).Order().ToArray();
        Assert.Equal(["Counts the marmalade jars each week", "Keeps the marmalade on the top shelf"], lines);
    }

    // An ENDPOINT line is the feature's endpoint as its summary shows it, not the search's lower-cased copy
    [Fact]
    public async Task An_endpoint_line_is_the_endpoint_as_written()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Pantry", Endpoint = "/api/Pantry/Shelves",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "e1", DisplayName = "The pantry is listed", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10),
                        Steps = [Step("Given", "a pantry")]
                    }
                ]
            }
        };
        await OpenFeaturesAsync(features, "Hit_Endpoint.html");
        await SearchAsync("shelves");

        Assert.Equal("/api/Pantry/Shelves", (await Line("ENDPOINT").Locator(".search-line-text").InnerTextAsync()).Trim());
    }
}
