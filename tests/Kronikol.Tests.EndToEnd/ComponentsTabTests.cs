using System.Net;
using System.Text.RegularExpressions;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The Components tab (5.0.0, plans/OPTION_C_PLAN.md S11, D26, D28): headed by the diagram's title, which its drawing
/// leaves out, with Show (Filtered, the start, or Full), Direction (Left to right or Top to bottom) and Call summaries
/// (Shown or Hidden) at the right of the heading's line, and a note while Filtered draws less than the whole run. The page
/// makes each form from the source as written and the generator's model of it, which says which scenarios made each
/// connection's calls; the preview matched each scenario's dependencies to the names the drawing gave its components, and
/// redrew by clearing the diagram first. A new form here is drawn out of sight and swapped in.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class ComponentsTabTests : PlaywrightTestBase
{
    public ComponentsTabTests(PlaywrightFixture fixture) : base(fixture) { }

    private sealed record Run(string Url, string Shop, string Payments, string Orders, string Stock);

    /// <summary>
    /// A run report written by the whole pipeline (<see cref="ReportGenerator.CreateStandardReportsWithDiagrams"/>): Pays
    /// (passed) calls Payments from the shop, Ships (failed) calls Orders, which calls Stock, and Browses (skipped) makes
    /// no call. With <paramref name="outline"/>, Pays is an outline of two examples, of which only the first (visa) calls
    /// Payments, Ships calls Orders alone, and there is no Browses. The diagram is held to the four names by a
    /// ParticipantFilter, since the process's call log holds every fixture's calls; each name is one word, so the diagram
    /// draws it on one line.
    /// </summary>
    private Run Write(string fileName, bool outline = false)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var run = new Run("", "Shop" + tag, "Payments" + tag, "Orders" + tag, "Stock" + tag);
        var reportsDir = Path.Combine(TempDir, "components-" + tag);
        Directory.CreateDirectory(reportsDir);
        var (pays, visa, amex, ships, browses) = ("ct-pays-" + tag, "ct-visa-" + tag, "ct-amex-" + tag, "ct-ships-" + tag, "ct-browses-" + tag);
        var names = new HashSet<string> { run.Shop, run.Payments, run.Orders, run.Stock };
        static Scenario Example(string id, string card) => new()
        {
            Id = id, DisplayName = "Pays", Result = ExecutionResult.Passed,
            OutlineId = "pays", ExampleValues = new Dictionary<string, string> { ["card"] = card }
        };
        var scenarios = outline
            ? new[] { Example(visa, "visa"), Example(amex, "amex"), new Scenario { Id = ships, DisplayName = "Ships", Result = ExecutionResult.Failed } }
            : new[]
            {
                new Scenario { Id = pays, DisplayName = "Pays", Result = ExecutionResult.Passed },
                new Scenario { Id = ships, DisplayName = "Ships", Result = ExecutionResult.Failed },
                new Scenario { Id = browses, DisplayName = "Browses", Result = ExecutionResult.Skipped }
            };
        // The whole pipeline memoises its diagrams process-wide, so it runs as every such fixture does: one at a time,
        // from a fresh cache (ReportTestHelper.WholePipeline).
        lock (ReportTestHelper.WholePipeline)
        {
            DefaultDiagramsFetcher.Reset();
            RequestResponseLogger.LogPair("Pays", outline ? visa : pays, HttpMethod.Post, new Uri("http://payments/pay"), run.Payments, run.Shop, statusCode: HttpStatusCode.OK);
            RequestResponseLogger.LogPair("Ships", ships, HttpMethod.Post, new Uri("http://orders/orders"), run.Orders, run.Shop, statusCode: HttpStatusCode.Created);
            if (!outline)
                RequestResponseLogger.LogPair("Ships", ships, HttpMethod.Get, new Uri("http://stock/stock"), run.Stock, run.Orders, statusCode: HttpStatusCode.OK);
            ReportGenerator.CreateStandardReportsWithDiagrams(
                [new Feature { DisplayName = "Shop", Scenarios = scenarios }],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
                new ReportConfigurationOptions
                {
                    ReportsFolderPath = reportsDir,
                    ComponentDiagramOptions = new Kronikol.ComponentDiagram.ComponentDiagramOptions { ParticipantFilter = names.Contains },
                    GenerateSpecificationsReport = false,
                    GenerateSpecificationsData = false,
                });
            DefaultDiagramsFetcher.Reset();
        }
        var report = Path.Combine(reportsDir, "TestRunReport.html");
        File.Copy(report, Path.Combine(OutputDir, fileName), true);
        return run with { Url = new Uri(report).AbsoluteUri };
    }

    private async Task OpenAsync(string url)
    {
        await Page.GotoAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
    }

    /// <summary>Shows only the scenarios of <paramref name="status"/>, through the filter box's Status row.</summary>
    private async Task ShowOnlyAsync(string status)
    {
        await OpenFilterRowsAsync();
        await Page.Locator($".status-toggle[data-status='{status}']").ClickAsync();
    }

    private ILocator Segment(string key, string value) => Page.Locator($"#component-diagram .tl-seg-btn[data-{key}='{value}']");

    private ILocator Note => Page.Locator("#component-diagram > .filtered-note");

    // the note's sentence, and after it the way back: Clear All, as where no scenario is left (5.1.0)
    private ILocator NoteText => Note.Locator(":scope > .filtered-note-text");

    private ILocator NoteClear => Note.Locator(":scope > .filtered-note-text + .filtered-note-clear");

    private ILocator Diagram => Page.Locator("#component-diagram .plantuml-browser");

    /// <summary>
    /// The text the diagram draws, once its drawing shows each of <paramref name="drawn"/> and none of
    /// <paramref name="notDrawn"/>. Only the drawing in sight is read: a form on its way is drawn in a hidden child.
    /// </summary>
    private async Task<string> DrawingAsync(string[] drawn, string[] notDrawn)
    {
        await Page.WaitForFunctionAsync("""
            ([drawn, notDrawn]) => {
                const svg = document.querySelector('#component-diagram .plantuml-browser > svg');
                if (!svg) return false;
                const text = svg.textContent || '';
                return drawn.every(t => text.includes(t)) && !notDrawn.some(t => text.includes(t));
            }
            """, new object[] { drawn, notDrawn }, new() { Timeout = 60000, PollingInterval = 200 });
        return await Page.EvaluateAsync<string>("() => document.querySelector('#component-diagram .plantuml-browser > svg').textContent");
    }

    /// <summary>The source of the drawing in sight: the page writes it with the drawing it swaps in.</summary>
    private Task<string> SourceAsync() =>
        Page.EvaluateAsync<string>("() => document.querySelector('#component-diagram .plantuml-browser').getAttribute('data-plantuml') || ''");

    /// <summary>Waits until the drawing in sight was drawn from a source that does, or does not, hold <paramref name="text"/>.</summary>
    private Task WaitForSourceAsync(string text, bool holds) => Page.WaitForFunctionAsync("""
        ([text, holds]) => {
            const el = document.querySelector('#component-diagram .plantuml-browser');
            return !!el && !!el.querySelector(':scope > svg') && (el.getAttribute('data-plantuml') || '').includes(text) === holds;
        }
        """, new object[] { text, holds }, new() { Timeout = 60000, PollingInterval = 200 });

    /// <summary>How many times as wide as it is tall the drawing in sight is.</summary>
    private Task<double> AspectAsync() => Page.EvaluateAsync<double>("""
        () => {
            const svg = document.querySelector('#component-diagram .plantuml-browser > svg');
            const vb = svg.viewBox && svg.viewBox.baseVal;
            const box = vb && vb.width ? vb : svg.getBBox();
            return box.width / box.height;
        }
        """);

    /// <summary>Each [what, selector, property, token]: what the element paints and what the token gives in the page's
    /// theme, each read as a colour.</summary>
    private Task<string[][]> PaintedAsync(string[][] checks) => Page.EvaluateAsync<string[][]>("""
        checks => checks.map(([what, selector, property, token]) => {
            const el = document.querySelector(selector);
            const probe = document.createElement('div');
            probe.style.setProperty('color', `var(${token})`);
            document.body.appendChild(probe);
            const wanted = getComputedStyle(probe).color;
            probe.remove();
            return [what, el ? getComputedStyle(el)[property] : 'missing', wanted];
        })
        """, checks);

    private const string OneScenarioNote =
        "Filtered: only the components and connections used by the 1 scenario that matches the filters. The call counts on the connections are for the whole run.";

    [Fact]
    public async Task The_tab_is_headed_by_the_diagrams_title_which_its_drawing_leaves_out()
    {
        var run = Write("ComponentsHeading.html");
        await OpenAsync(run.Url);
        await OpenReportTabAsync("components");

        var text = await DrawingAsync([run.Shop, run.Payments, run.Orders, run.Stock], []);

        Assert.DoesNotContain("Component Diagram", text);
        var header = Page.Locator("#component-diagram > .timeline-header");
        Assert.StartsWith("Component Diagram", (await header.TextContentAsync())!.Trim());
        await Expect(header.Locator(".timeline-info")).ToHaveAttributeAsync("title", new Regex("^The Component Diagram shows"));
        await Expect(header.Locator(".timeline-controls > .tl-seg")).ToHaveCountAsync(3);
        await Expect(Note).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Filtered_follows_the_filters_and_says_what_it_leaves_out()
    {
        var run = Write("ComponentsFiltered.html");
        await OpenAsync(run.Url);
        await ShowOnlyAsync("Failed");
        await OpenReportTabAsync("components");

        // Ships alone: the shop's call to Payments was Pays', so Payments and the connection to it are left out
        await DrawingAsync([run.Shop, run.Orders, run.Stock], [run.Payments]);
        await Expect(Note).ToBeVisibleAsync();
        await Expect(NoteText).ToHaveTextAsync(OneScenarioNote);
        await Expect(Segment("view", "filtered")).ToHaveAttributeAsync("aria-pressed", "true");

        // a change made while the tab shows: Clear All puts the whole run back, and the note goes
        await Page.Locator(".filtering-box-export .export-btn", new() { HasTextString = "Clear All" }).ClickAsync();

        await DrawingAsync([run.Shop, run.Payments, run.Orders, run.Stock], []);
        await Expect(Note).ToBeHiddenAsync();
    }

    [Fact]
    public async Task The_note_ends_with_Clear_All_which_draws_the_whole_run_again()
    {
        var run = Write("ComponentsNoteClear.html");
        await OpenAsync(run.Url);
        await ShowOnlyAsync("Failed");
        await OpenReportTabAsync("components");
        await DrawingAsync([run.Shop, run.Orders, run.Stock], [run.Payments]);

        // straight after the sentence, on its line
        await Expect(NoteText).ToHaveTextAsync(OneScenarioNote);
        await Expect(NoteClear).ToBeVisibleAsync();
        await Expect(NoteClear).ToHaveTextAsync("Clear All");
        await Expect(NoteClear).ToHaveAttributeAsync("title", "Clear every filter");
        var text = (await NoteText.BoundingBoxAsync())!;
        var clear = (await NoteClear.BoundingBoxAsync())!;
        Assert.True(clear.X > text.X, "the button comes after the sentence");
        Assert.InRange(clear.Y + clear.Height / 2, text.Y, text.Y + text.Height);

        await NoteClear.ClickAsync();

        await DrawingAsync([run.Shop, run.Payments, run.Orders, run.Stock], []);
        await Expect(Note).ToBeHiddenAsync();
        await Expect(Page.Locator(".status-toggle.status-active")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Full_draws_the_whole_run_whatever_the_filters()
    {
        var run = Write("ComponentsFull.html");
        await OpenAsync(run.Url);
        await ShowOnlyAsync("Failed");
        await OpenReportTabAsync("components");
        await DrawingAsync([run.Orders], [run.Payments]);

        await Segment("view", "full").ClickAsync();

        await DrawingAsync([run.Shop, run.Payments, run.Orders, run.Stock], []);
        await Expect(Note).ToBeHiddenAsync();
        await Expect(Segment("view", "full")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Segment("view", "filtered")).ToHaveAttributeAsync("aria-pressed", "false");

        await Segment("view", "filtered").ClickAsync();

        await DrawingAsync([run.Orders], [run.Payments]);
        await Expect(NoteText).ToHaveTextAsync(OneScenarioNote);
    }

    [Fact]
    public async Task Top_to_bottom_turns_the_diagram_and_a_form_drawn_before_comes_back_from_the_cache()
    {
        var run = Write("ComponentsDirection.html");
        await OpenAsync(run.Url);
        await OpenReportTabAsync("components");
        await DrawingAsync([run.Stock], []);
        var across = await AspectAsync();

        await Segment("dir", "tb").ClickAsync();

        await WaitForSourceAsync("left to right direction", holds: false);
        var down = await AspectAsync();
        Assert.True(down < across, $"drawn top to bottom the diagram is {down:F2} times as wide as it is tall, left to right {across:F2}");
        await Expect(Segment("dir", "tb")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Segment("dir", "lr")).ToHaveAttributeAsync("aria-pressed", "false");

        // each form is its own source, so the render cache holds the first, and it comes back without a render
        var hits = await Page.EvaluateAsync<int>("() => window.__kronikolRender.cacheHits");
        await Segment("dir", "lr").ClickAsync();

        await WaitForSourceAsync("left to right direction", holds: true);
        Assert.Equal(across, await AspectAsync(), 3);
        Assert.True(await Page.EvaluateAsync<int>("() => window.__kronikolRender.cacheHits") > hits, "the first form was rendered again");
    }

    [Fact]
    public async Task Hidden_call_summaries_draw_the_connections_without_their_labels()
    {
        var run = Write("ComponentsCalls.html");
        await OpenAsync(run.Url);
        await OpenReportTabAsync("components");
        await DrawingAsync(["calls"], []);

        await Segment("calls", "hidden").ClickAsync();

        await DrawingAsync([run.Shop, run.Payments, run.Orders, run.Stock], ["calls", "POST"]);
        await Expect(Segment("calls", "hidden")).ToHaveAttributeAsync("aria-pressed", "true");

        await Segment("calls", "shown").ClickAsync();

        await DrawingAsync(["calls", "POST"], []);
    }

    [Fact]
    public async Task A_filter_changed_on_another_tab_reaches_the_diagram_when_its_tab_opens()
    {
        var run = Write("ComponentsLater.html");
        await OpenAsync(run.Url);
        await OpenReportTabAsync("components");
        await DrawingAsync([run.Payments], []);
        await OpenReportTabAsync("features");

        await ShowOnlyAsync("Failed");
        // the change is taken while the tab is hidden (its note says so) and drawn only when the tab shows again;
        // opening the tab within the change's 150 ms settle would have it drawn by the settle itself
        await Expect(NoteText).ToHaveTextAsync(OneScenarioNote);
        await OpenReportTabAsync("components");

        await DrawingAsync([run.Orders], [run.Payments]);
        await Expect(NoteText).ToHaveTextAsync(OneScenarioNote);
    }

    [Fact]
    public async Task A_links_filters_reach_the_first_drawing()
    {
        var run = Write("ComponentsLink.html");
        // the link's filters are applied as the page loads, before the tab's script starts
        await OpenAsync(run.Url + "#status=Failed");

        // the form is handed to the page's renderer before the tab is opened, so the diagram is drawn once, filtered
        await Page.WaitForFunctionAsync("""
            payments => {
                const el = document.querySelector('#component-diagram .plantuml-browser');
                const source = el && el.getAttribute('data-plantuml');
                return !!source && source.includes('left to right direction') && !source.includes(payments);
            }
            """, run.Payments, new() { Timeout = 30000, PollingInterval = 200 });
        await OpenReportTabAsync("components");

        await DrawingAsync([run.Orders], [run.Payments]);
        await Expect(NoteText).ToHaveTextAsync(OneScenarioNote);
    }

    [Fact]
    public async Task An_outlines_example_rows_answer_for_their_scenario()
    {
        // the outline's element carries no stable id of its own: each example's row does, and the visa example made the call
        var run = Write("ComponentsOutline.html", outline: true);
        await OpenAsync(run.Url);
        await Expect(Page.Locator("details.scenario-parameterized")).ToHaveCountAsync(1);
        await ShowOnlyAsync("Passed");
        await OpenReportTabAsync("components");

        await DrawingAsync([run.Shop, run.Payments], [run.Orders]);
        await Expect(NoteText).ToHaveTextAsync("Filtered: only the components and connections used by the 2 scenarios that match the filters. The call counts on the connections are for the whole run.");
    }

    [Fact]
    public async Task When_no_scenario_the_filters_show_made_a_call_the_drawing_is_put_away_and_the_note_says_so()
    {
        var run = Write("ComponentsNothing.html");
        await OpenAsync(run.Url);
        await OpenReportTabAsync("components");
        await DrawingAsync([run.Payments], []);

        await ShowOnlyAsync("Skipped");

        await Expect(Diagram).ToBeHiddenAsync();
        await Expect(NoteText).ToHaveTextAsync("Filtered: the 1 scenario that matches the filters made none of the calls this diagram draws.");

        await Segment("view", "full").ClickAsync();

        await Expect(Diagram).ToBeVisibleAsync();
        await Expect(Note).ToBeHiddenAsync();
        await DrawingAsync([run.Shop, run.Payments, run.Orders, run.Stock], []);
    }

    [Fact]
    public async Task A_diagram_toggle_leaves_the_form_the_tab_draws_alone()
    {
        var run = Write("ComponentsToggles.html");
        await OpenAsync(run.Url);
        await OpenReportTabAsync("components");
        await DrawingAsync([run.Stock], []);
        await Segment("dir", "tb").ClickAsync();
        await WaitForSourceAsync("left to right direction", holds: false);
        // the drawing is marked: a toggle that drew it again would replace it
        await Page.EvaluateAsync("() => { document.querySelector('#component-diagram .plantuml-browser > svg').dataset.mark = '1'; }");

        await OpenReportTabAsync("features");
        var headers = Page.Locator("#display-settings button[data-toggle='headers']");
        var before = await headers.GetAttributeAsync("aria-pressed");
        await headers.ClickAsync();
        await Expect(headers).Not.ToHaveAttributeAsync("aria-pressed", before!);
        await OpenReportTabAsync("components");

        Assert.True(await Page.EvaluateAsync<bool>(
            "() => document.querySelector('#component-diagram .plantuml-browser > svg')?.dataset.mark === '1'"), "a diagram toggle drew the component diagram again");
        Assert.DoesNotContain("left to right direction", await SourceAsync());
        await Expect(Segment("dir", "tb")).ToHaveAttributeAsync("aria-pressed", "true");
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_card_the_controls_and_the_note_take_the_themes_colours(string theme)
    {
        var run = Write($"ComponentsColours_{theme}.html");
        await Context.AddInitScriptAsync($"try {{ localStorage.setItem('kronikol.theme', '{theme}'); }} catch (e) {{ }}");
        await OpenAsync(run.Url);
        await ShowOnlyAsync("Failed");
        await OpenReportTabAsync("components");
        await Expect(Note).ToBeVisibleAsync();

        var painted = await PaintedAsync(
        [
            ["the card", "#component-diagram", "backgroundColor", "--kron-card"],
            ["the card's edge", "#component-diagram", "borderTopColor", "--kron-hairline"],
            ["the heading", "#component-diagram > .timeline-header", "color", "--kron-label"],
            ["a group's label", "#component-diagram .tl-seg-label", "color", "--kron-label"],
            ["the pressed segment's ground", "#component-diagram .tl-seg-btn[aria-pressed='true']", "backgroundColor", "--kron-selected"],
            ["the pressed segment's text", "#component-diagram .tl-seg-btn[aria-pressed='true']", "color", "--kron-selected-ink"],
            ["a segment's ground", "#component-diagram .tl-seg-btn[aria-pressed='false']", "backgroundColor", "--kron-card"],
            ["a segment's text", "#component-diagram .tl-seg-btn[aria-pressed='false']", "color", "--kron-ink-soft"],
            ["the note's ground", "#component-diagram > .filtered-note", "backgroundColor", "--kron-warn-soft"],
            ["the note's text", "#component-diagram > .filtered-note", "color", "--kron-warn"]
        ]);

        foreach (var p in painted)
            Assert.True(p[1] == p[2], $"{p[0]} is painted {p[1]}, where the {theme} theme's token gives {p[2]}");
    }
}
