using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The way to a match in a diagram (plans/SEARCH_PLAN.md 4.6.5): a deep search's DIAGRAM line followed from the Search tab
/// turns on what hides the match for its scenario alone, draws the diagram, opens the note, marks the words and says what
/// it did. The reports are written by the real generator from captured calls (<see cref="DiagramApiReports"/>).
/// </summary>
public partial class SearchHitTests
{
    private static readonly ResolvedToggleDefaults DatabasesHidden = ResolvedToggleDefaults.BuiltIn with { DatabasesShown = false };

    private async Task OpenDiagramsAsync(string url)
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await Page.GotoReportAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        // a page whose diagrams a server drew has no diagram API: the search and the way there are enough
        await Page.WaitForFunctionAsync("() => !!window.kronikol_search_results && !!window.kronikol_hit",
            null, new() { PollingInterval = 200 });
    }

    // the query run from the bar, its results shown, and the deep search's lines drawn on them
    private async Task SearchForDiagramLineAsync(string query)
    {
        await FillSearchBar(query, toSearchTab: true);
        await Page.WaitForFunctionAsync("() => !document.getElementById('panel-search').hidden && document.getElementById('search-count').textContent !== ''",
            null, new() { PollingInterval = 200 });
        await DiagramLine().WaitForAsync(new() { Timeout = 30000 });
    }

    private ILocator DiagramLine() =>
        Page.Locator("#panel-search .search-result .search-line:has(.search-line-kind:text-is('DIAGRAM'))").First;

    // a line followed, and the way there ended: the status line says where the match is
    private async Task<string> FollowToMatchAsync(ILocator line)
    {
        var hit = await line.GetAttributeAsync("data-hit");
        var href = await line.GetAttributeAsync("href");
        var errors = new List<string>();
        Page.Console += (_, m) => { if (m.Type == "error") errors.Add(m.Text); };
        await line.ClickAsync();
        try
        {
            await Page.WaitForFunctionAsync("() => { const s = document.querySelector('.kron-nav-status'); return !!s && s.textContent.indexOf('Match in') === 0; }",
                null, new() { Timeout = 60000, PollingInterval = 200 });
        }
        catch (TimeoutException)
        {
            Assert.Fail($"the way to {hit} did not end: the status line read \"{await Page.Locator(".kron-nav-status").First.InnerTextAsync()}\"; errors: {string.Join(" | ", errors)}; href {href}; now at {await Page.EvaluateAsync<string>("() => decodeURIComponent(location.hash) + ' tab ' + document.querySelector('.report-tabs > .report-tab[aria-selected=true]').dataset.tab + ' items ' + fc().items.length")}");
        }
        return await Page.Locator(".kron-nav-status").First.InnerTextAsync();
    }

    private ILocator ScenarioNamed(string name) => Page.Locator("details.scenario", new() { Has = Page.Locator($":scope > summary:has-text('{name}')") }).First;

    // H6
    [Fact]
    public async Task A_body_line_draws_the_diagram_expands_the_note_and_marks_the_words()
    {
        // the note is truncated at 10 lines (the report's start), and the match is on its 30th
        var url = DiagramApiReports.Write(TempDir, OutputDir, "hit-h6.html",
        [
            new("h6-a", "Read the pantry", DiagramApiReports.Call("h6-a", 0, "GET", "/pantry", DiagramApiReports.Body("flour", 40))),
        ]);
        await OpenDiagramsAsync(url);
        await SearchForDiagramLineAsync("flour_30");
        Assert.Contains("flour_30", await DiagramLine().InnerTextAsync());
        Assert.Contains(":note:", await DiagramLine().GetAttributeAsync("data-hit") ?? "");

        var said = await FollowToMatchAsync(DiagramLine());
        Assert.StartsWith("Match in the response body of GET: /pantry, in the diagram.", said);
        Assert.True(said.Contains("The note expanded.", StringComparison.Ordinal), $"the status line said: {said}");
        await Page.WaitForFunctionAsync("() => { const m = window.kronikol_marks.marked(); return !!m && m.count > 0; }", null,
            new() { Timeout = 30000, PollingInterval = 200 });
        // the words are drawn in the diagram: the note holds line 30 now
        Assert.True(await Page.EvaluateAsync<bool>("""
            () => [...document.querySelectorAll("details.scenario [data-diagram-type='plantuml'] svg text")]
                .map(t => t.textContent).join('').replace(/\s+/g, '').indexOf('flour_30') >= 0
            """), "the note was not drawn with its 30th line");
        Assert.Equal("The note is expanded.", (await Page.Locator(".search-turned-on").First.InnerTextAsync()).Trim());
    }

    // H14: a picture drawn by a server holds no text the page can mark; its source is in the Raw PlantUML section
    [Fact]
    public async Task A_server_drawn_diagram_s_line_opens_Raw_PlantUML_and_marks_the_words()
    {
        var url = DiagramApiReports.Write(TempDir, OutputDir, "hit-h14.html",
        [
            new("h14-a", "Read the cellar", DiagramApiReports.Call("h14-a", 0, "GET", "/cellar", DiagramApiReports.Body("cider", 4))),
        ], rendering: PlantUmlRendering.Server);
        await OpenDiagramsAsync(url);
        await SearchForDiagramLineAsync("cider_03");
        Assert.Contains(":raw:", await DiagramLine().GetAttributeAsync("data-hit") ?? "");

        await FollowToMatchAsync(DiagramLine());
        await Page.WaitForFunctionAsync("() => !!document.activeElement && !!document.activeElement.closest('.raw-plantuml')", null,
            new() { PollingInterval = 200 });
        Assert.True(await Page.EvaluateAsync<bool>("() => document.activeElement.closest('details').open"), "the Raw PlantUML section is not open");
        await Page.WaitForFunctionAsync("() => { const m = window.kronikol_marks.marked(); return !!m && m.count > 0; }", null,
            new() { Timeout = 30000, PollingInterval = 200 });
    }

    // H31: a participant's declaration is no message: the way goes to the diagram, and the marks find its drawn name
    [Fact]
    public async Task A_participant_or_loop_label_line_marks_it_in_the_diagram()
    {
        var url = DiagramApiReports.Write(TempDir, OutputDir, "hit-h31.html",
        [
            new("h31-a", "Weigh the grain", DiagramApiReports.Call("h31-a", 0, "GET", "/sacks", "{\"sacks\": 3}", service: "Granary Service")),
        ]);
        await OpenDiagramsAsync(url);
        await SearchForDiagramLineAsync("granary");

        var said = await FollowToMatchAsync(DiagramLine());
        Assert.DoesNotContain("does not draw", said);
        await Page.WaitForFunctionAsync("() => { const m = window.kronikol_marks.marked(); return !!m && m.count > 0; }", null,
            new() { Timeout = 30000, PollingInterval = 200 });
    }

    // H11: a diagram the page draws in parts; the match is in a note of its last call, in a later part
    [Fact]
    public async Task A_line_in_a_later_part_of_a_parted_diagram_goes_to_that_part()
    {
        var calls = Enumerable.Range(0, 12)
            .SelectMany(n => DiagramApiReports.Call("h11-a", n, "GET", $"/shelf/{n}", DiagramApiReports.Body($"jar{n:D2}", 6)))
            .ToArray();
        var url = DiagramApiReports.Write(TempDir, OutputDir, "hit-h11.html", [new("h11-a", "Count the shelves", calls)], partHeight: 600);
        await OpenDiagramsAsync(url);
        await SearchForDiagramLineAsync("jar11_04");

        await FollowToMatchAsync(DiagramLine());
        await Page.WaitForFunctionAsync("() => { const m = window.kronikol_marks.marked(); return !!m && m.count > 0; }", null,
            new() { Timeout = 30000, PollingInterval = 200 });
        // the part holding the words is drawn and in the window
        Assert.True(await Page.EvaluateAsync<bool>("""
            () => {
                const t = [...document.querySelectorAll("[data-diagram-type='plantuml'] svg text")].find(t => t.textContent.indexOf('jar11_04') >= 0);
                if (!t) return false;
                const r = t.getBoundingClientRect();
                return r.top >= 0 && r.bottom <= window.innerHeight;
            }
            """), "the words are not drawn in the window");
    }

    // H30: the match is in the scenario's second diagram, whose id the line names
    [Fact]
    public async Task A_line_in_a_scenario_s_second_diagram_goes_to_that_diagram()
    {
        var url = DiagramApiReports.Write(TempDir, OutputDir, "hit-h30.html",
            [new("h30-a", "Fill the jars", DiagramApiReports.Call("h30-a", 0, "GET", "/jars", DiagramApiReports.Body("honey", 3)))],
            moreDiagrams: [new("h30-a", "", DiagramApiReports.Call("h30-a", 1, "GET", "/lids", DiagramApiReports.Body("lid", 3)))]);
        await OpenDiagramsAsync(url);
        Assert.Equal(2, await Page.Locator("details.scenario [data-diagram-type='plantuml']").CountAsync());
        await SearchForDiagramLineAsync("lid_02");

        await FollowToMatchAsync(DiagramLine());
        await Page.WaitForFunctionAsync("() => { const m = window.kronikol_marks.marked(); return !!m && m.count > 0; }", null,
            new() { Timeout = 30000, PollingInterval = 200 });
        Assert.True(await Page.EvaluateAsync<bool>("""
            () => {
                const hosts = [...document.querySelectorAll("details.scenario [data-diagram-type='plantuml']")];
                const t = [...hosts[1].querySelectorAll('svg text')].find(t => t.textContent.indexOf('lid_02') >= 0);
                if (!t) return false;
                const r = t.getBoundingClientRect();
                return r.top >= 0 && r.bottom <= window.innerHeight;
            }
            """), "the second diagram's words are not drawn in the window");
    }

    // H9: a note longer than PayloadScrollLines draws its body in a box of its own that scrolls; the way scrolls it
    [Fact]
    public async Task A_long_note_line_scrolls_its_box_to_the_words()
    {
        var url = DiagramApiReports.Write(TempDir, OutputDir, "hit-h9.html",
            [new("h9-a", "Sift the bran", DiagramApiReports.Call("h9-a", 0, "GET", "/bran", DiagramApiReports.Body("bran", 320)))],
            ResolvedToggleDefaults.BuiltIn with { PayloadScrollLines = 40 });
        await OpenDiagramsAsync(url);
        await SearchForDiagramLineAsync("bran_300");

        await FollowToMatchAsync(DiagramLine());
        await Page.WaitForFunctionAsync("() => { const m = window.kronikol_marks.marked(); return !!m && m.count > 0; }", null,
            new() { Timeout = 30000, PollingInterval = 200 });
        try
        {
            await Page.WaitForFunctionAsync("""
                () => {
                    const box = [...document.querySelectorAll('.note-scroll')].find(b => b.textContent.indexOf('bran_300') >= 0);
                    if (!box || box.scrollTop <= 0) return false;
                    const walker = document.createTreeWalker(box, NodeFilter.SHOW_TEXT);
                    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
                        const at = n.data.indexOf('bran_300');
                        if (at < 0) continue;
                        const range = document.createRange();
                        range.setStart(n, at); range.setEnd(n, at + 8);
                        const r = range.getBoundingClientRect(), b = box.getBoundingClientRect();
                        return r.top >= b.top && r.bottom <= b.bottom;
                    }
                    return false;
                }
                """, null, new() { Timeout = 15000, PollingInterval = 200 });
        }
        catch (TimeoutException)
        {
            var boxes = await Page.EvaluateAsync<string>("() => JSON.stringify([...document.querySelectorAll('.note-scroll')].map(b => ({ has: b.textContent.indexOf('bran_300') >= 0, top: b.scrollTop, h: b.scrollHeight, ch: b.clientHeight })))");
            var marks = await Page.EvaluateAsync<string>("() => JSON.stringify(window.kronikol_marks.marked())");
            Assert.Fail($"the box was not scrolled to the words: boxes {boxes}, marks {marks}");
        }
    }

    // H13: a span's name is only in the whole-test flow; its FLOW line chooses the Flame view and marks the words there
    [Fact]
    public async Task A_flow_line_opens_the_Flame_view_at_its_bar()
    {
        await OpenDiagramsAsync(ReportTestHelper.GenerateWholeTestFlowToggleDefaultsReport(TempDir, OutputDir, "hit-h13.html", _ => { }));
        await FillSearchBar("InsertOrUpdate", toSearchTab: true);
        var line = Page.Locator("#panel-search .search-result .search-line:has(.search-line-kind:text-is('FLOW'))").First;
        await line.WaitForAsync(new() { Timeout = 30000 });
        Assert.StartsWith("w:", await line.GetAttributeAsync("data-hit") ?? "");

        await FollowToMatchAsync(line);
        await Page.WaitForFunctionAsync("() => { const v = document.querySelector('.diagram-view-flame'); return !!v && v.style.display !== 'none' && v.getClientRects().length > 0; }",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        await Page.WaitForFunctionAsync("() => { const m = window.kronikol_marks.marked(); return !!m && m.count > 0; }", null,
            new() { Timeout = 30000, PollingInterval = 200 });
    }

    // H8: each of the gear's toggles that can hide a match, the report starting with it off, and the word only in what it
    // hides (Databases is H7's)
    [Theory]
    [InlineData("events", "bus_02", "Events")]
    [InlineData("assertions", "plums", "Assertions")]
    [InlineData("steps", "scones", "Steps")]
    [InlineData("headers", "application/json", "Headers")]
    [InlineData("setup", "pumpkin", "Setup")]
    public async Task Each_toggle_that_hides_a_match_is_turned_on(string toggle, string word, string named)
    {
        RequestResponseLog[] calls = toggle switch
        {
            "events" =>
            [
                .. DiagramApiReports.Call("h8", 0, "POST", "/orders", "{\"id\": 1}"),
                .. DiagramApiReports.Call("h8", 1, "PUBLISH", "/order-placed", DiagramApiReports.Body("bus", 3), service: "Order Bus",
                    category: Kronikol.Constants.DependencyCategories.ServiceBus),
            ],
            "assertions" =>
            [
                .. DiagramApiReports.Call("h8", 0, "GET", "/basket", "{\"id\": 1}"),
                .. DiagramApiReports.Marker("h8", 1, DiagramMarkerKind.Assertion, InteractionRecord.AssertionNotePlantUml("the basket holds plums", true, null)),
            ],
            "steps" =>
            [
                .. DiagramApiReports.Marker("h8", 0, DiagramMarkerKind.Step, InteractionRecord.StepDelimiterPlantUml("Given", "the oven warms the scones")),
                .. DiagramApiReports.Call("h8", 1, "GET", "/oven", "{\"id\": 1}"),
            ],
            "setup" =>
            [
                .. DiagramApiReports.Marker("h8", 0, DiagramMarkerKind.Custom, "partition #F6F6F6 Setup"),
                .. DiagramApiReports.Call("h8", 1, "POST", "/seeds", "{\"seed\": \"pumpkin\"}"),
                .. DiagramApiReports.Marker("h8", 2, DiagramMarkerKind.Custom, "end"),
                .. DiagramApiReports.Call("h8", 3, "GET", "/patch", "{\"id\": 1}"),
            ],
            _ => DiagramApiReports.Call("h8", 0, "GET", "/pantry", "{\"id\": 1}"),
        };
        var start = toggle switch
        {
            "events" => ResolvedToggleDefaults.BuiltIn with { EventsShown = false },
            "assertions" => ResolvedToggleDefaults.BuiltIn with { AssertionsShown = false },
            "steps" => ResolvedToggleDefaults.BuiltIn with { StepsShown = false },
            "setup" => ResolvedToggleDefaults.BuiltIn with { SetupShown = false },
            _ => ResolvedToggleDefaults.BuiltIn with { HeadersShown = false },
        };
        await OpenDiagramsAsync(DiagramApiReports.Write(TempDir, OutputDir, $"hit-h8-{toggle}.html", [new("h8", "Bake the order", calls)], start));
        await SearchForDiagramLineAsync(word);

        var said = await FollowToMatchAsync(DiagramLine());
        Assert.Contains($"{named} turned on", said);
        Assert.StartsWith($"Turned on to show this match: {named}", (await Page.Locator(".search-turned-on").First.InnerTextAsync()).Trim());
        Assert.Equal(1, await Page.Locator($"details.scenario .toggle-btn[data-toggle='{toggle}'][data-shown='true']").CountAsync());
    }

    // the words of the scenario's drawn diagram, run together as the engine draws them (each word a text of its own)
    private Task<string> DrawnTextAsync() => Page.EvaluateAsync<string>(
        "() => [...document.querySelectorAll(\"details.scenario [data-diagram-type='plantuml'] svg text\")].map(t => t.textContent).join('').replace(/\\s+/g, '')");

    // H10: a word the YAML holds leaves the note in YAML; a token only the JSON holds (`7777,`, where a JSON value ends) shows
    // it as JSON. A phrase written with JSON's own quotes cannot be searched for: a phrase ends at the next quote.
    [Fact]
    public async Task A_JSON_phrase_shows_its_note_as_JSON_and_a_word_leaves_it_in_YAML()
    {
        var url = DiagramApiReports.Write(TempDir, OutputDir, "hit-h10.html",
            [new("h10-a", "Count the fruit", DiagramApiReports.Call("h10-a", 0, "GET", "/fruit", "{\"apples\": 7777, \"pears\": 9}"))]);
        await OpenDiagramsAsync(url);
        await SearchForDiagramLineAsync("pears");
        var said = await FollowToMatchAsync(DiagramLine());
        Assert.DoesNotContain("JSON", said);
        Assert.Contains("pears:9", await DrawnTextAsync());

        await OpenDiagramsAsync(url);
        await SearchForDiagramLineAsync("7777,");
        said = await FollowToMatchAsync(DiagramLine());
        Assert.Contains("the note shown as JSON", said, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"apples\":7777,", await DrawnTextAsync());
        Assert.Contains("The note is shown as JSON.", await Page.Locator(".search-turned-on").First.InnerTextAsync());
    }

    // H12: a long path is cut in the arrow's label, and written whole in the request's note, under [Full path]
    [Fact]
    public async Task A_call_line_past_the_label_s_cut_goes_to_its_Full_path_block()
    {
        // the word in the middle of the path, where the label is cut
        var path = "/catalog/" + string.Join("/", Enumerable.Range(1, 30).Select(n => n == 15 ? "zucchini" : $"aisle{n:D2}"));
        var url = DiagramApiReports.Write(TempDir, OutputDir, "hit-h12.html",
            [new("h12-a", "Find the zucchini", DiagramApiReports.Call("h12-a", 0, "GET", path, "{\"found\": true}"))]);
        await OpenDiagramsAsync(url);
        await SearchForDiagramLineAsync("zucchini");
        var hit = await DiagramLine().GetAttributeAsync("data-hit");

        await FollowToMatchAsync(DiagramLine());
        await Page.WaitForFunctionAsync("() => { const m = window.kronikol_marks.marked(); return !!m && m.count > 0; }", null,
            new() { Timeout = 30000, PollingInterval = 200 });
        Assert.True((await DrawnTextAsync()).Contains("zucchini", StringComparison.Ordinal), $"the word is not drawn, the line followed: {hit}");
    }

    // H34: the long note's scenario is the last of many, its box built only as it nears the window
    [Fact]
    public async Task A_long_note_line_far_down_the_page_builds_the_box_and_scrolls_it()
    {
        var scenarios = Enumerable.Range(0, 30)
            .Select(n => new DiagramApiReports.Scenario($"h34-{n:D2}", $"Shelf {n:D2}", DiagramApiReports.Call($"h34-{n:D2}", 0, "GET", $"/shelf/{n}", "{\"ok\": true}")))
            .Append(new DiagramApiReports.Scenario("h34-z", "Sift the rye", DiagramApiReports.Call("h34-z", 0, "GET", "/rye", DiagramApiReports.Body("rye", 320))))
            .ToArray();
        await OpenDiagramsAsync(DiagramApiReports.Write(TempDir, OutputDir, "hit-h34.html", scenarios, ResolvedToggleDefaults.BuiltIn with { PayloadScrollLines = 40 }));
        await SearchForDiagramLineAsync("rye_300");

        await FollowToMatchAsync(DiagramLine());
        await Page.WaitForFunctionAsync("""
            () => {
                const box = [...document.querySelectorAll('.note-scroll')].find(b => b.textContent.indexOf('rye_300') >= 0);
                if (!box || box.scrollTop <= 0) return false;
                const walker = document.createTreeWalker(box, NodeFilter.SHOW_TEXT);
                for (let n = walker.nextNode(); n; n = walker.nextNode()) {
                    const at = n.data.indexOf('rye_300');
                    if (at < 0) continue;
                    const range = document.createRange();
                    range.setStart(n, at); range.setEnd(n, at + 7);
                    const r = range.getBoundingClientRect(), b = box.getBoundingClientRect();
                    return r.top >= b.top && r.bottom <= b.bottom && r.top >= 0 && r.bottom <= window.innerHeight;
                }
                return false;
            }
            """, null, new() { Timeout = 30000, PollingInterval = 200 });
    }

    // H21: the engine stops at an @enduml a fragment wrote, so the note after it is in the source but never drawn: the way
    // goes to the diagram and says why, under the view tabs and in the status line
    [Fact]
    public async Task A_match_the_diagram_does_not_draw_goes_to_the_nearest_place_and_says_why()
    {
        RequestResponseLog[] calls =
        [
            .. DiagramApiReports.Call("h21-a", 0, "GET", "/garden", "{\"rows\": 4}"),
            .. DiagramApiReports.Marker("h21-a", 1, DiagramMarkerKind.Custom, "@enduml\nnote over caller\nthe hidden turnip\nend note"),
        ];
        await OpenDiagramsAsync(DiagramApiReports.Write(TempDir, OutputDir, "hit-h21.html", [new("h21-a", "Dig the garden", calls)]));
        await SearchForDiagramLineAsync("turnip");

        var said = await FollowToMatchAsync(DiagramLine());
        const string why = "This match is in the diagram’s source, in a part the diagram does not draw.";
        Assert.Contains(why, said);
        Assert.Contains(why, await Page.Locator(".search-turned-on").First.InnerTextAsync());
        // the nearest place reached: the diagram, in the window
        Assert.True(await Page.EvaluateAsync<bool>("() => { const r = document.querySelector(\"details.scenario [data-diagram-type='plantuml']\").getBoundingClientRect(); return r.bottom > 0 && r.top < window.innerHeight; }"));
    }

    // a long diagram, its database calls hidden at the start, so the way to its last call's body takes a while
    private string SlowReport(string fileName) => DiagramApiReports.Write(TempDir, OutputDir, fileName,
    [
        new("slow", "Stock the cellar", Enumerable.Range(0, 80).SelectMany(n => n % 4 == 3
            ? DiagramApiReports.Call("slow", n, "SELECT", $"/cellar/{n}", DiagramApiReports.Body($"cellar{n:D2}", 12), service: "Cellar DB", database: true)
            : DiagramApiReports.Call("slow", n, "GET", $"/rack/{n}", DiagramApiReports.Body($"rack{n:D2}", 12))).ToArray()),
    ], DatabasesHidden);

    // The way says it is still going once it has taken 300 ms: what the status line is told is recorded, as the line may say
    // it only for a moment; with `back`, Back is pressed the moment it is said.
    private Task ArmOpeningAsync(bool back = false) => Page.EvaluateAsync("""
        back => {
            window.__opening = false;
            const nav = window.kronikol_nav, say = nav.say;
            nav.say = function (t) {
                if (String(t).indexOf('Opening the match') === 0 && !window.__opening) { window.__opening = true; if (back) history.back(); }
                return say.apply(this, arguments);
            };
        }
        """, back);

    private Task WaitForOpeningAsync() => Page.WaitForFunctionAsync("() => window.__opening === true", null,
        new() { Timeout = 30000, PollingInterval = 200 });

    // what a way that went on would do, not done within 3 s: no marks, no "Match in", nothing said under the view tabs
    private async Task NeverArrivesAsync()
    {
        try
        {
            await Page.WaitForFunctionAsync("""
                () => !!window.kronikol_marks.marked() || !!document.querySelector('.search-turned-on')
                    || (document.querySelector('.kron-nav-status') || { textContent: '' }).textContent.indexOf('Match in') === 0
                """, null, new() { Timeout = 3000, PollingInterval = 200 });
        }
        catch (TimeoutException)
        {
            return;
        }
        Assert.Fail("the way went on to the match");
    }

    // H22
    [Fact]
    public async Task A_scroll_or_a_key_from_the_reader_stops_the_way_there()
    {
        await OpenDiagramsAsync(SlowReport("hit-h22.html"));
        await SearchForDiagramLineAsync("cellar79_05");
        await ArmOpeningAsync();
        await DiagramLine().ClickAsync();
        await WaitForOpeningAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        await NeverArrivesAsync();
    }

    // H35
    [Fact]
    public async Task Back_while_the_way_there_runs_stops_it()
    {
        await OpenDiagramsAsync(SlowReport("hit-h35.html"));
        await SearchForDiagramLineAsync("cellar79_05");
        await ArmOpeningAsync(back: true);
        await DiagramLine().ClickAsync();
        await WaitForOpeningAsync();
        await Page.WaitForFunctionAsync("() => !document.getElementById('panel-search').hidden", null, new() { PollingInterval = 200 });
        await NeverArrivesAsync();
    }

    // H29: an outline's rows differ only in a payload; the line found in the second row's diagram chooses that row
    [Fact]
    public async Task A_payload_line_from_an_outline_row_chooses_that_row_and_its_diagram()
    {
        var url = DiagramApiReports.Write(TempDir, OutputDir, "hit-h29.html",
        [
            new("h29-gold", "Pick a tier", DiagramApiReports.Call("h29-gold", 0, "GET", "/perks", "{\"perk\": \"lounge\"}"), Outline: "tiers", Row: "gold"),
            new("h29-silver", "Pick a tier", DiagramApiReports.Call("h29-silver", 1, "GET", "/perks", "{\"perk\": \"parking\"}"), Outline: "tiers", Row: "silver"),
        ]);
        await OpenDiagramsAsync(url);
        await SearchForDiagramLineAsync("parking");

        await FollowToMatchAsync(DiagramLine());
        await Page.WaitForFunctionAsync("() => { const m = window.kronikol_marks.marked(); return !!m && m.count > 0; }", null,
            new() { Timeout = 30000, PollingInterval = 200 });
        Assert.True(await Page.EvaluateAsync<bool>("""
            () => [...document.querySelectorAll('tr.row-active')].some(r => r.getClientRects().length && r.textContent.indexOf('silver') >= 0)
            """), "the silver row is not the one chosen");
        // the diagram shown is the silver row's, and it holds the words in the window
        Assert.True(await Page.EvaluateAsync<bool>("""
            () => {
                const t = [...document.querySelectorAll("[data-diagram-type='plantuml'] svg text")].find(t => t.textContent.indexOf('parking') >= 0 && t.getClientRects().length);
                if (!t) return false;
                const r = t.getBoundingClientRect();
                return r.top >= 0 && r.bottom <= window.innerHeight;
            }
            """), "the silver row's diagram is not shown with the words in the window");
    }

    // H33: the span's name is also in the Activity view's diagram; that line chooses the Activity view and marks it there
    [Fact]
    public async Task An_Activity_view_line_opens_the_view_and_marks_its_text()
    {
        await OpenDiagramsAsync(ReportTestHelper.GenerateWholeTestFlowToggleDefaultsReport(TempDir, OutputDir, "hit-h33.html", _ => { }));
        await FillSearchBar("InsertOrUpdate", toSearchTab: true);
        const string lines = "#panel-search .search-result .search-line[data-hit^='g:']";
        await Page.WaitForFunctionAsync("""
            s => [...document.querySelectorAll(s)].some(l => { const e = document.getElementById(l.getAttribute('data-hit').split(':')[1]); return !!e && !!e.closest('.diagram-view-activity'); })
            """, lines, new() { Timeout = 30000, PollingInterval = 200 });
        var index = await Page.EvaluateAsync<int>("""
            s => [...document.querySelectorAll(s)].findIndex(l => { const e = document.getElementById(l.getAttribute('data-hit').split(':')[1]); return !!e && !!e.closest('.diagram-view-activity'); })
            """, lines);
        Assert.False(await Page.Locator(".diagram-view-activity").First.IsVisibleAsync(), "the Activity view was shown before the line was followed");

        await FollowToMatchAsync(Page.Locator(lines).Nth(index));
        await Expect(Page.Locator(".diagram-view-activity").First).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => { const m = window.kronikol_marks.marked(); return !!m && m.count > 0; }", null,
            new() { Timeout = 30000, PollingInterval = 200 });
    }

    // H23: twenty services with long names make a diagram far wider than its column in a narrow window, drawn fit to it,
    // its words small
    [Fact]
    public async Task Words_drawn_under_10_px_are_zoomed_to_be_read()
    {
        RequestResponseLog[] calls =
        [
            .. Enumerable.Range(0, 20).SelectMany(n => DiagramApiReports.Call("h23-a", n, "GET", $"/svc{n}", "{\"ok\": true}",
                service: $"A Service With A Very Long Name Number {n:D2}")),
            .. DiagramApiReports.Call("h23-a", 20, "GET", "/last", "{\"quince\": 1}", service: "A Service With A Very Long Name Number 19"),
        ];
        await OpenDiagramsAsync(DiagramApiReports.Write(TempDir, OutputDir, "hit-h23.html", [new("h23-a", "Call everyone", calls)]));
        await Page.SetViewportSizeAsync(700, 800);
        await SearchForDiagramLineAsync("quince");

        await FollowToMatchAsync(DiagramLine());
        await Page.WaitForFunctionAsync("() => { const m = window.kronikol_marks.marked(); return !!m && m.count > 0; }", null,
            new() { Timeout = 30000, PollingInterval = 200 });
        // the words read at 10 px or more (their font in the picture's scale)
        await Page.WaitForFunctionAsync("""
            () => {
                const t = [...document.querySelectorAll("details.scenario [data-diagram-type='plantuml'] svg text")].find(t => t.textContent.indexOf('quince') >= 0);
                return !!t && parseFloat(t.getAttribute('font-size')) * t.ownerSVGElement.getScreenCTM().a >= 9.95;
            }
            """, null, new() { Timeout = 10000, PollingInterval = 200 });
        // and they stay so once the drawings that follow are done
        await Page.EvaluateAsync("async () => { await window.kronikol_diagrams.whenDrawn(document.querySelector(\"details.scenario [data-diagram-type='plantuml']\"), { timeout: 10000 }); await new Promise(r => setTimeout(r, 1000)); }");
        var font = await Page.EvaluateAsync<double>("""
            () => {
                const t = [...document.querySelectorAll("details.scenario [data-diagram-type='plantuml'] svg text")].find(t => t.textContent.indexOf('quince') >= 0);
                return parseFloat(t.getAttribute('font-size')) * t.ownerSVGElement.getScreenCTM().a;
            }
            """);
        Assert.True(font >= 9.95, $"the words went back to {font:F2} px");
    }

    // two scenarios each reading a database, which the report starts with hidden
    private string LedgerReport(string fileName) => DiagramApiReports.Write(TempDir, OutputDir, fileName,
    [
        new("h7-a", "Post the ledger",
        [
            .. DiagramApiReports.Call("h7-a", 0, "POST", "/orders", "{\"id\": 1}"),
            .. DiagramApiReports.Call("h7-a", 1, "SELECT", "/ledger", DiagramApiReports.Body("ledger", 3), service: "Ledger DB", database: true),
        ]),
        new("h7-b", "Post the stock",
        [
            .. DiagramApiReports.Call("h7-b", 0, "POST", "/orders", "{\"id\": 2}"),
            .. DiagramApiReports.Call("h7-b", 1, "SELECT", "/stock", DiagramApiReports.Body("stock", 3), service: "Stock DB", database: true),
        ]),
    ], DatabasesHidden);

    // H18: left on after the reader moves on (Q15), as an opened scenario stays open; the line saying so goes with the marks
    [Fact]
    public async Task What_was_turned_on_stays_and_belongs_to_the_scenario()
    {
        await OpenDiagramsAsync(LedgerReport("hit-h18.html"));
        await SearchForDiagramLineAsync("ledger_02");
        await FollowToMatchAsync(DiagramLine());
        await Page.Locator(".search-turned-on").First.WaitForAsync();

        await Page.GoBackAsync();
        await Page.WaitForFunctionAsync("() => !document.getElementById('panel-search').hidden", null, new() { PollingInterval = 200 });
        await Page.WaitForFunctionAsync("() => !window.kronikol_marks.marked() && !document.querySelector('.search-turned-on')", null,
            new() { Timeout = 10000, PollingInterval = 200 });
        Assert.Equal(1, await ScenarioNamed("Post the ledger").Locator(".toggle-btn[data-toggle='databases'][data-shown='true']").CountAsync());
        Assert.Equal(0, await ScenarioNamed("Post the stock").Locator(".toggle-btn[data-toggle='databases'][data-shown='true']").CountAsync());
    }

    // H7 and H17: the owner's example; another scenario's diagram and its gear are left as they were
    [Fact]
    public async Task A_database_call_line_turns_Databases_on_for_that_scenario_only()
    {
        await OpenDiagramsAsync(LedgerReport("hit-h7.html"));
        await SearchForDiagramLineAsync("ledger_02");

        var said = await FollowToMatchAsync(DiagramLine());
        Assert.StartsWith("Match in the response body of SELECT", said);
        Assert.Contains("Databases turned on", said);
        Assert.Equal("Turned on to show this match: Databases.", (await Page.Locator(".search-turned-on").First.InnerTextAsync()).Trim());

        // on for the scenario the match is in, and for no other
        var a = ScenarioNamed("Post the ledger");
        var b = ScenarioNamed("Post the stock");
        Assert.Equal(1, await a.Locator(".toggle-btn[data-toggle='databases'][data-shown='true']").CountAsync());
        Assert.Equal(0, await b.Locator(".toggle-btn[data-toggle='databases'][data-shown='true']").CountAsync());
        Assert.True(await a.EvaluateAsync<bool>("s => [...s.querySelectorAll(\"[data-diagram-type='plantuml'] svg text\")].some(t => t.textContent.indexOf('Ledger') >= 0)"),
            "the ledger's database is not drawn in its scenario's diagram");
    }

    // A diagram line the instant search alone found (a word of two letters, which deep search does not read) is the
    // diagram's part of the search text, lower-cased (plans/SEARCH_PLAN.md 4.6.4), cut as every line is: 160 characters
    // around the match, where it once kept 40 on each side
    [Fact]
    public async Task An_instant_diagram_line_is_cut_to_160_around_the_match()
    {
        var path = "/pantry/" + string.Join("/", Enumerable.Range(1, 30).Select(n => $"shelf{n}")) + "/zq/" +
                   string.Join("/", Enumerable.Range(31, 30).Select(n => $"shelf{n}"));
        var url = DiagramApiReports.Write(TempDir, OutputDir, "hit-instant-cut.html",
        [
            new("ic-a", "Walk the shelves", DiagramApiReports.Call("ic-a", 0, "GET", path, DiagramApiReports.Body("jam", 2))),
        ]);
        await OpenDiagramsAsync(url);
        await SearchForDiagramLineAsync("zq");

        var line = (await DiagramLine().Locator(".search-line-text").InnerTextAsync()).Trim();
        Assert.Contains("/zq/", line);
        Assert.True(line.Length is > 120 and <= 162, $"the line is {line.Length} characters long: {line}");
    }

    // H20 for a diagram: a diagram's id is its place in the page, so a hit= kept from another report, or from this one
    // before its scenarios moved, can name another scenario's diagram. The way does not go there: the scenario opens and
    // the status line says the match was not found, as for a step
    [Fact]
    public async Task A_hit_link_naming_another_scenario_s_diagram_opens_the_scenario_and_says_so()
    {
        IReadOnlyList<DiagramApiReports.Scenario> two =
        [
            new("st-a", "Read the pantry", DiagramApiReports.Call("st-a", 0, "GET", "/pantry", DiagramApiReports.Body("flour", 4))),
            new("st-b", "Read the cellar", DiagramApiReports.Call("st-b", 0, "GET", "/cellar", DiagramApiReports.Body("flour", 4))),
        ];
        await OpenDiagramsAsync(DiagramApiReports.Write(TempDir, OutputDir, "hit-stale-a.html", two));
        await SearchForDiagramLineAsync("flour_02");
        await FollowToMatchAsync(DiagramLine());
        // the scenario followed is the first result; the hit names its diagram, and the stale one names the other's
        var ids = new[]
        {
            await ScenarioNamed("Read the pantry").Locator("[id^='puml-']").First.GetAttributeAsync("id"),
            await ScenarioNamed("Read the cellar").Locator("[id^='puml-']").First.GetAttributeAsync("id")
        };
        var hash = await Page.EvaluateAsync<string>("() => location.hash");
        var followed = ids.Single(id => Uri.UnescapeDataString(hash).Contains("g:" + id + ":"));
        var other = ids.Single(id => id != followed);
        var stale = hash.Replace("g%3A" + followed + "%3A", "g%3A" + other + "%3A").Replace("g:" + followed + ":", "g:" + other + ":");
        Assert.Contains("g:" + other + ":", Uri.UnescapeDataString(stale));

        await OpenDiagramsAsync(DiagramApiReports.Write(TempDir, OutputDir, "hit-stale-b.html", two) + stale);
        await Page.WaitForFunctionAsync(
            "() => { const s = document.querySelector('.kron-nav-status'); return !!s && s.textContent.indexOf('not found in this report') >= 0; }",
            null, new() { Timeout = 10000, PollingInterval = 200 });
        Assert.Equal("This match was not found in this report; its scenario is shown.", await Page.Locator(".kron-nav-status").TextContentAsync());
        Assert.False(await Page.EvaluateAsync<bool>("() => !!window.kronikol_marks.marked()"), "a mark was put on the other scenario's diagram");
    }

    // The line under the view tabs is this page's, as the marks are: Export Filtered HTML copies the feature without it
    [Fact]
    public async Task An_export_taken_after_a_match_leaves_out_the_line_of_what_was_turned_on()
    {
        await OpenDiagramsAsync(LedgerReport("hit-export-line.html"));
        await SearchForDiagramLineAsync("ledger_02");
        await FollowToMatchAsync(DiagramLine());
        Assert.Equal(1, await Page.Locator(".search-turned-on").CountAsync());

        var exported = await ScenarioNamed("Post the ledger").EvaluateAsync<string>("s => export_undrawn(s.closest('details.feature'))");
        Assert.DoesNotContain("search-turned-on", exported);
        Assert.DoesNotContain("Turned on to show this match", exported);
        // the export took nothing off the page
        Assert.Equal(1, await Page.Locator(".search-turned-on").CountAsync());
    }
}
