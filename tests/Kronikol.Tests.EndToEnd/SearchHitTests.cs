using System.Text.Json;
using Kronikol.Constants;
using Kronikol.Ingestion;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The marks a match is shown with where it is drawn (plans/SEARCH_PLAN.md 4.6.5; facts H15, H16, H32, H36 and the cases
/// around them): the place it is in gets an outline that pulses on arrival, and its words a mark, in a drawn diagram a
/// rect behind each run of them on a row, in the page's HTML the CSS Custom Highlight API. The facts call
/// <c>window.kronikol_marks.mark</c> on a real report's drawn diagram, steps and failure, as the way to a match does. The
/// report is written by the generator from captured calls, so a note paints as a run's does: a word to a
/// <c>&lt;text&gt;</c>, a long line wrapped by the engine, a long run broken by the generator and marked.
/// </summary>
[Collection(PlaywrightCollections.Search)]
public partial class SearchHitTests : PlaywrightTestBase
{
    public SearchHitTests(PlaywrightFixture fixture) : base(fixture) { }

    private static readonly DateTimeOffset At = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private static readonly string[] Alphabet =
    [
        "alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel", "india", "juliet", "kilo", "lima", "mike",
        "november", "oscar", "papa", "quebec", "romeo", "sierra", "tango", "uniform", "victor", "whiskey", "xray", "yankee", "zulu"
    ];

    /// <summary>A sentence the engine wraps over several rows of its note: every word in it once.</summary>
    private static readonly string Story = string.Join(" ", Enumerable.Range(1, 3).SelectMany(n => Alphabet.Select(w => w + n)));

    /// <summary>A run with no space, which the generator breaks at its budget and marks with one zero-width space: every
    /// four characters of it once, so a piece across a break names one place.</summary>
    private static readonly string Token = string.Concat(Enumerable.Range(0, 75).Select(i => $"t{i:D3}"));

    /// <summary>An assertion longer than its note's line budget, broken by the generator at a space it marks with two
    /// zero-width spaces.</summary>
    private const string Assertion =
        "the pantry holds enough flour for every pancake in the order and the mill has ground the next sack before the bakery opens";

    /// <summary>A path longer than an arrow's label is drawn wide, which the generator breaks onto rows of 100 characters
    /// with the label's own line break (PlantUmlCreator, maxUrlLength).</summary>
    private static readonly string ShelfPath = "/api/pantry/" + string.Join("/", Enumerable.Range(1, 16).Select(i => $"shelf{i:D2}"));

    /// <summary>One GET to <see cref="ShelfPath"/> and its answer.</summary>
    private static RequestResponseLog[] ShelfCall(string testId)
    {
        (string Key, string? Value)[] headers = [("Content-Type", "application/json")];
        var uri = new Uri("http://pantry.internal" + ShelfPath);
        var trace = Guid.NewGuid();
        var pairId = Guid.NewGuid();
        return
        [
            new(testId, testId, HttpMethod.Get, null, uri, headers, "Shelf API", "Caller", RequestResponseType.Request, trace, pairId, false)
                { Timestamp = At.AddSeconds(3) },
            new(testId, testId, HttpMethod.Get, "{\"sacks\":2}", uri, headers, "Shelf API", "Caller", RequestResponseType.Response, trace,
                pairId, false, System.Net.HttpStatusCode.OK) { Timestamp = At.AddSeconds(3).AddMilliseconds(40) },
        ];
    }

    /// <summary>
    /// One failed scenario: two steps and an error naming flour, and a diagram of a pantry's JSON response (flour three
    /// times, the story, the token), a ledger of 150 lines whose 120th holds "deepword" (drawn 40 lines tall with its body
    /// in a scroll box), a Kafka event naming flour (to a queue, which the Events toggle takes out), a call to
    /// <see cref="ShelfPath"/>, a step bar and the assertion; without <paramref name="withLedger"/>, no ledger, so nothing
    /// in the picture is a scroll box.
    /// Expanded, in JSON, headers shown.
    /// </summary>
    private static string WriteReport(string tempDir, string outputDir, string fileName, bool withLedger = true)
    {
        const string id = "pantry";
        var pantry = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["flour"] = "Stoneground flour from the north mill, flour again",
            ["story"] = Story,
            ["token"] = Token,
            ["milk"] = "Some fresh milk",
        });
        var ledger = JsonSerializer.Serialize(Enumerable.Range(1, 148).ToDictionary(
            i => $"ledger_{i:D4}", i => i == 120 ? "the deepword sits here" : $"entry {i}"));
        var logs = LongPayloadReports.Call(id, 0, pantry, "Pantry API")
            .Concat(withLedger ? LongPayloadReports.Call(id, 1, ledger, "Ledger API") : Array.Empty<RequestResponseLog>())
            .Concat(LongPayloadReports.Message(id, 2, JsonSerializer.Serialize(new Dictionary<string, object> { ["event"] = "flour delivered", ["sacks"] = 3 }))
                .Select(l => l with { DependencyCategory = DependencyCategories.MessageQueue }))
            .Concat(ShelfCall(id))
            .ToArray();
        var bars = StepBarPlantUml.Build("When a pancake order for flour is placed") + "\n"
                   + InteractionRecord.AssertionNotePlantUml(Assertion, passed: true, message: null) + "\n@enduml";
        var diagrams = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, maxEncodedDiagramLength: 8000,
                clientSideSplitting: true, systemUiFont: true)
            .SelectMany(t => t.PlantUmls)
            .Select(p => new DiagramAsCode(id, "", p.PlainText.Replace("@enduml", bars)))
            .ToArray();
        Feature[] features =
        [
            new()
            {
                DisplayName = "Pantry",
                Scenarios =
                [
                    new()
                    {
                        Id = id, DisplayName = "A pancake order reads the pantry", Result = ExecutionResult.Failed,
                        Duration = TimeSpan.FromSeconds(1), ErrorMessage = "Expected the flour to be ground\nbut it was grain",
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "the pantry has flour in stock", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "a pancake order for flour is placed", Status = ExecutionResult.Failed },
                        ]
                    }
                ]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport(diagrams, features, At.UtcDateTime, At.UtcDateTime.AddSeconds(30),
            null, Path.Combine(tempDir, fileName), "Pantry", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: ReportTestHelper.ClassicStart with { Details = ReportDetailsState.Expanded, AssertionsShown = true });
        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>The page's helpers for these facts: the diagram, a note by a text it holds, a place's rows as the page draws
    /// them, the marks drawn, a run of characters' box, and the contrast of two colours.</summary>
    private const string Helpers = """
        () => {
            var h = window.__marks = {};
            h.ZW = String.fromCharCode(0x200b);
            h.container = () => document.querySelector('[data-diagram-type="plantuml"]');
            h.svgs = () => Array.from(h.container().querySelectorAll('svg')).filter(s => !s.closest('.note-scroll'));
            h.svg = () => h.svgs()[0];
            h.notes = () => { var out = []; h.svgs().forEach(s => window._findNoteGroups(s).forEach(g => out.push(g))); return out; };
            h.note = (needle) => h.notes().find(g => g.texts.some(t => t.textContent.indexOf(needle) >= 0)) || null;
            h.eventNote = () => h.notes().find(g => g.paths.some(p => p.classList.contains('k-card-event'))) || null;
            h.stepBar = () => h.svg().querySelector('.k-step');
            h.assertion = () => Array.from(h.svg().querySelectorAll('polygon, path')).find(e => /^#d4edda$/i.test(e.getAttribute('fill') || '')) || null;
            h.shown = (t) => {
                var cs = getComputedStyle(t);
                return cs.visibility !== 'hidden' && cs.display !== 'none' && !/^#[0-9a-f]{6}00$/i.test(t.getAttribute('fill') || '');
            };
            // the texts drawn inside a shape's box, by where their baselines start
            h.inside = (shape) => {
                var b = shape.getBBox();
                return Array.from(h.svg().querySelectorAll('text')).filter(t => {
                    var x = parseFloat(t.getAttribute('x')), y = parseFloat(t.getAttribute('y'));
                    return x >= b.x - 1 && x <= b.x + b.width + 1 && y >= b.y - 1 && y <= b.y + b.height + 1;
                });
            };
            // a place's shown texts as rows: one y each, top to bottom, each row left to right
            h.rows = (texts) => {
                var by = {}, rows = [];
                texts.filter(h.shown).forEach(t => {
                    var y = t.getAttribute('y');
                    if (!by[y]) { by[y] = []; rows.push({ y: parseFloat(y), texts: by[y] }); }
                    by[y].push(t);
                });
                rows.sort((a, b) => a.y - b.y);
                rows.forEach(r => r.texts.sort((a, b) => parseFloat(a.getAttribute('x')) - parseFloat(b.getAttribute('x'))));
                return rows;
            };
            h.words = (row) => row.texts.filter(t => t.textContent.replace(new RegExp(h.ZW, 'g'), '').trim() !== '');
            h.grounds = () => Array.from(document.querySelectorAll('.kron-mark-ground'));
            h.edges = () => Array.from(document.querySelectorAll('.kron-mark-edge'));
            h.places = () => Array.from(document.querySelectorAll('.kron-mark-place'));
            h.box = (e) => { var r = e.getBoundingClientRect(); return { l: r.left, t: r.top, r: r.right, b: r.bottom, w: r.width, h: r.height }; };
            // characters [s, e) of a text as the page draws them (a Range places them right in Chromium)
            h.chars = (t, s, e) => {
                var node = Array.from(t.childNodes).find(n => n.nodeType === 3);
                var r = document.createRange(); r.setStart(node, s); r.setEnd(node, e);
                return h.box(r);
            };
            // every occurrence of a word inside one text of the place
            h.found = (texts, word) => {
                var out = [];
                texts.filter(h.shown).forEach(t => {
                    var s = t.textContent.toLowerCase(), i = s.indexOf(word);
                    while (i >= 0) { out.push({ t: t, box: h.chars(t, i, i + word.length) }); i = s.indexOf(word, i + word.length); }
                });
                return out;
            };
            h.covers = (o, i, tol) => o.l <= i.l + tol && o.r >= i.r - tol && o.t <= i.t + tol && o.b >= i.b - tol;
            // the mark over a box: covering it, and not wider than it by more than its own ends
            h.markOver = (box) => h.grounds().find(m => { var b = h.box(m); return h.covers(b, box, 2) && b.w <= box.w + 4 && b.h <= box.h + 6; }) || null;
            // a message's label by a text it holds
            h.message = (needle) => Array.from(h.svg().querySelectorAll('g.message')).find(g => g.textContent.indexOf(needle) >= 0) || null;
            // an arrow's number, drawn as a text of its label's own
            h.numberOf = (g) => Array.from(g.querySelectorAll('text')).map(t => t.textContent.trim()).find(s => /^[0-9]+$/.test(s)) || '';
            h.rgb = (v) => (String(v).match(/[\d.]+/g) || []).slice(0, 3).map(Number);
            h.contrast = (a, b) => {
                var lum = (c) => { var x = h.rgb(c).map(v => { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }); return 0.2126 * x[0] + 0.7152 * x[1] + 0.0722 * x[2]; };
                var la = lum(a), lb = lum(b);
                return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
            };
            h.token = (name) => { var p = document.createElement('i'); p.style.color = 'var(' + name + ')'; document.body.appendChild(p); var c = getComputedStyle(p).color; p.remove(); return c; };
            h.settled = () => {
                var c = h.container();
                return !!c && !c._noteRendering && !window._plantumlRendering && h.svgs().length > 0
                    && h.svgs().every(s => s.querySelector('.note-hover-rect') && s.getAttribute('data-k-roles'));
            };
            return true;
        }
        """;

    /// <summary>Opens the report on <paramref name="page"/> (the test's Chromium page unless given), in a theme, its scenario
    /// open and its diagram drawn and settled.</summary>
    private async Task OpenAsync(string fileName, string theme = "light", IPage? page = null)
    {
        if (page is null) await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        page ??= Page;
        await page.GotoReportAsync(WriteReport(TempDir, OutputDir, fileName));
        await page.EvaluateAsync("t => document.documentElement.setAttribute('data-theme', t)", theme);
        await page.Locator("details.feature").First.WaitForAsync();
        await FoldAllAsync(page, "features", expand: true);
        await FoldAllAsync(page, "scenarios", expand: true);
        await page.EvaluateAsync("() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");
        await page.Locator("[data-diagram-type='plantuml'] svg:visible").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60000 });
        await page.EvaluateAsync(Helpers);
        await SettledAsync(page);
    }

    private async Task SettledAsync(IPage? page = null)
    {
        page ??= Page;
        await page.WaitForFunctionAsync("() => window.__marks && window.__marks.settled()", null,
            new() { Timeout = 90000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(page);
    }

    /// <summary>Marks <paramref name="terms"/> in the note holding <paramref name="needle"/>, and waits for the marks to be
    /// drawn (they are drawn in the call; the frames let them paint). Returns what the call answered, as JSON.</summary>
    private async Task<JsonElement> MarkNoteAsync(string needle, string[] terms, int current = -1)
    {
        var result = await Page.EvaluateAsync<JsonElement>("""
            ([needle, terms, current]) => {
                var r = window.kronikol_marks.mark(window.__marks.note(needle), terms, current);
                return { count: r ? r.count : -1, current: r ? r.current : -2 };
            }
            """, new object[] { needle, terms, current });
        await TwoAnimationFramesAsync(Page);
        return result;
    }

    private Task<int> GroundsAsync() => Page.EvaluateAsync<int>("() => window.__marks.grounds().length");

    /// <summary>What is wrong with the marks of <paramref name="word"/> in the note holding <paramref name="needle"/>: each
    /// of its occurrences covered by one mark no wider than it, as many marks as occurrences, and the occurrence's text
    /// read at 4.5:1 on its mark. Empty when nothing is.</summary>
    private Task<string> MarksOverAsync(string needle, string word) => Page.EvaluateAsync<string>("""
        ([needle, word]) => {
            var h = window.__marks, g = h.note(needle), bad = [];
            var want = h.found(g.texts, word), grounds = h.grounds();
            if (grounds.length !== want.length) bad.push(grounds.length + ' marks for ' + want.length + ' occurrences');
            want.forEach(w => {
                var m = h.markOver(w.box);
                if (!m) { bad.push('nothing marks "' + w.t.textContent + '" at ' + JSON.stringify(w.box) + ', marks at ' + JSON.stringify(grounds.map(h.box))); return; }
                var ink = getComputedStyle(w.t).fill, ground = getComputedStyle(m).fill, ratio = h.contrast(ink, ground);
                if (ratio < 4.5) bad.push('"' + w.t.textContent + '" is ' + ink + ' on ' + ground + ', ' + ratio.toFixed(2) + ':1');
            });
            return bad.join('; ');
        }
        """, new object[] { needle, word });

    private async Task ZoomAsync(string to)
    {
        await Page.EvaluateAsync("""
            (to) => {
                var s = window.__marks.container().querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                var min = parseFloat(s.min), max = parseFloat(s.max);
                s.value = String(to === 'min' ? min : to === 'max' ? max : Math.round((min + max) / 2));
                s.dispatchEvent(new Event('input', { bubbles: true }));
            }
            """, to);
        await TwoAnimationFramesAsync(Page);
    }

    // H15: each mark covers its words' boxes, and its words read at 4.5:1 on it, in both themes and at every zoom
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task H15_The_marks_sit_on_the_words_in_both_themes_and_at_every_zoom(string theme)
    {
        await OpenAsync($"marks-h15-{theme}.html", theme);
        await ZoomSliderOf(Page.Locator("[data-diagram-type='plantuml']").First).WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 15000 });

        var result = await MarkNoteAsync("\"flour\":", ["flour"]);
        Assert.Equal(3, result.GetProperty("count").GetInt32());
        Assert.Equal("", await MarksOverAsync("\"flour\":", "flour"));

        foreach (var level in new[] { "max", "mid", "min" })
        {
            await ZoomAsync(level);
            Assert.Equal("", await MarksOverAsync("\"flour\":", "flour"));
        }
    }

    // H16
    [Fact]
    public async Task H16_The_marks_come_back_after_a_redraw_and_go_on_Escape_a_click_outside_or_another_search()
    {
        await OpenAsync("marks-h16.html");
        await MarkNoteAsync("\"flour\":", ["flour"]);
        Assert.Equal(3, await GroundsAsync());

        // Headers turned off from the scenario's own settings draws the diagram again: clicks inside the scenario, which keep
        // the marks, and the marks are drawn on the new picture, over the words
        await Page.EvaluateAsync("() => { window.__before = window.__marks.svg(); }");
        var scenario = Page.Locator("details.scenario").First;
        await OpenScenarioSettingsAsync(scenario);
        await scenario.Locator(".toggle-btn[data-toggle='headers'][data-shown='true']").First.ClickAsync();
        await Page.WaitForFunctionAsync("() => !document.contains(window.__before) && window.__marks.settled()", null,
            new() { Timeout = 60000, PollingInterval = 200 });
        await Page.WaitForFunctionAsync(
            "() => window.__marks.grounds().length === 3 && window.__marks.grounds().every(r => r.ownerSVGElement === window.__marks.svg())",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        Assert.Equal("", await MarksOverAsync("\"flour\":", "flour"));
        // a redraw is no arrival: the place does not pulse again
        Assert.Equal(0, await Page.EvaluateAsync<int>("() => document.querySelectorAll('.kron-mark-arrive').length"));

        // Escape that closes a layer (the query builder) leaves them; Escape with nothing open takes them away
        await Page.EvaluateAsync("() => window.kronikol_search.open()");
        await Page.WaitForFunctionAsync("() => document.getElementById('search-dialog').open", null, new() { PollingInterval = 200 });
        await Page.Keyboard.PressAsync("Escape");
        await Page.WaitForFunctionAsync("() => !document.getElementById('search-dialog').open", null, new() { PollingInterval = 200 });
        Assert.Equal(3, await GroundsAsync());
        await Page.Keyboard.PressAsync("Escape");
        Assert.Equal(0, await GroundsAsync());
        Assert.Equal(0, await Page.EvaluateAsync<int>("() => window.__marks.places().length"));

        // a click inside the scenario leaves them; a click outside it takes them away
        await MarkNoteAsync("\"flour\":", ["flour"]);
        await Page.Locator("details.scenario .step-text").First.ClickAsync();
        Assert.Equal(3, await GroundsAsync());
        await Page.Locator("div.title-line h1").ClickAsync();
        Assert.Equal(0, await GroundsAsync());

        // another search takes them away
        await MarkNoteAsync("\"flour\":", ["flour"]);
        Assert.Equal(3, await GroundsAsync());
        await FillSearchBar("pancake", toSearchTab: true);
        await Page.WaitForFunctionAsync("() => window.__marks.grounds().length === 0", null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    // the diagram's menu, opened on the picture, then "Copy image" and the item named
    private async Task CopyAsync(string item)
    {
        await Page.EvaluateAsync("() => navigator.clipboard.writeText('nothing copied yet')");
        await Page.EvaluateAsync("""
            () => {
                var svg = window.__marks.svg(), r = svg.getBoundingClientRect();
                svg.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: r.left + 8, clientY: r.top + 8 }));
            }
            """);
        var menu = Page.Locator(".diagram-ctx-menu");
        await menu.WaitForAsync(new() { Timeout = 5000 });
        var parent = menu.Locator(".submenu-parent", new() { HasTextRegex = new System.Text.RegularExpressions.Regex("^Copy image") }).First;
        await parent.HoverAsync();
        await parent.Locator(".submenu").GetByText(item, new() { Exact = true }).First.ClickAsync();
    }

    // the PNG on the clipboard, read once it is there: its size and a digest of its pixels
    private Task<string> PngDigestAsync() => Page.EvaluateAsync<string>("""
        async () => {
            let item = null;
            for (let k = 0; k < 50 && !item; k++) {
                item = (await navigator.clipboard.read()).find(i => i.types.includes('image/png')) || null;
                if (!item) await new Promise(res => setTimeout(res, 200));
            }
            if (!item) return 'no picture';
            const bmp = await createImageBitmap(await item.getType('image/png'));
            const c = document.createElement('canvas'); c.width = bmp.width; c.height = bmp.height;
            const ctx = c.getContext('2d'); ctx.drawImage(bmp, 0, 0);
            const d = ctx.getImageData(0, 0, c.width, c.height).data;
            let h = 2166136261;
            for (let i = 0; i < d.length; i++) h = Math.imul(h ^ d[i], 16777619);
            return c.width + 'x' + c.height + ':' + (h >>> 0).toString(16);
        }
        """);

    // H32: Copy as SVG and as PNG copy the diagram as it is drawn without its marks, which stay on the page
    [Fact]
    public async Task H32_Copies_of_a_marked_diagram_carry_no_marks()
    {
        await OpenAsync("marks-h32.html");
        await MarkNoteAsync("\"flour\":", ["flour"], current: 1);
        Assert.Equal(3, await GroundsAsync());

        await CopyAsync("Copy as SVG");
        var copied = await Page.EvaluateAsync<string[]>("""
            async () => {
                let text = '';
                for (let k = 0; k < 50 && !text.startsWith('<svg'); k++) {
                    text = await navigator.clipboard.readText();
                    if (!text.startsWith('<svg')) await new Promise(res => setTimeout(res, 200));
                }
                const doc = new DOMParser().parseFromString(text, 'image/svg+xml');
                return [String(doc.querySelectorAll('text').length), String(doc.querySelectorAll('[class*="kron-mark"]').length),
                        String(text.indexOf('kron-mark') >= 0)];
            }
            """);
        Assert.NotEqual("0", copied[0]);
        Assert.Equal("0", copied[1]);
        Assert.Equal("False", copied[2], ignoreCase: true);
        // the copy took nothing off the page
        Assert.Equal(3, await GroundsAsync());

        // the picture copied with the marks on is the picture copied with none
        await CopyAsync("Copy as PNG");
        var marked = await PngDigestAsync();
        Assert.Equal(3, await GroundsAsync());
        await Page.EvaluateAsync("() => window.kronikol_marks.clear()");
        Assert.Equal(0, await GroundsAsync());
        await CopyAsync("Copy as PNG");
        var plain = await PngDigestAsync();
        Assert.NotEqual("no picture", plain);
        Assert.Equal(plain, marked);
    }

    // H36: the underline against a payload's card, an event's card, a step bar and the card the diagram is drawn on, at
    // 3:1 at least, in both themes (the token sheet's values are a unit fact, SearchMarksTests)
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task H36_The_mark_shows_against_every_note_fill(string theme)
    {
        await OpenAsync($"marks-h36-{theme}.html", theme);
        var ratios = await Page.EvaluateAsync<string>("""
            () => {
                var h = window.__marks, out = [], card = h.token('--kron-card');
                var places = [
                    ['payload', h.note('"flour":'), (g) => getComputedStyle(g.paths[0]).fill],
                    ['event', h.eventNote(), (g) => getComputedStyle(g.paths[0]).fill],
                    ['step', h.stepBar(), (s) => getComputedStyle(s).fill]
                ];
                places.forEach(p => {
                    var r = window.kronikol_marks.mark(p[1], ['flour'], -1);
                    var edge = h.edges()[0];
                    if (!r || !r.count || !edge) { out.push(p[0] + ': no mark'); return; }
                    var stroke = getComputedStyle(edge).stroke, fill = p[2](p[1]);
                    [['fill', fill], ['card', card]].forEach(f => {
                        var ratio = h.contrast(stroke, f[1]);
                        if (ratio < 3) out.push(p[0] + ': the edge ' + stroke + ' on the ' + f[0] + ' ' + f[1] + ' is ' + ratio.toFixed(2) + ':1');
                    });
                });
                window.kronikol_marks.clear();
                return out.join('; ');
            }
            """);
        Assert.Equal("", ratios);
    }

    /// <summary>What is wrong with the two marks of one match either side of a row's end: the runs each must cover are in
    /// <c>window.__expect</c> ({ t, s, e }: a text and its characters [s, e)), one on each row.</summary>
    private Task<string> TwoRowMarksAsync() => Page.EvaluateAsync<string>("""
        () => {
            var h = window.__marks, w = window.__expect, bad = [];
            var grounds = h.grounds();
            if (grounds.length !== 2) bad.push(grounds.length + ' marks, not 2');
            w.forEach(x => { if (!h.markOver(h.chars(x.t, x.s, x.e))) bad.push('nothing marks "' + x.t.textContent.slice(x.s, x.e) + '"'); });
            var ys = grounds.map(m => h.box(m).t);
            if (ys.length === 2 && Math.abs(ys[0] - ys[1]) < 4) bad.push('both marks on one row');
            return bad.join('; ');
        }
        """);

    // a match runs on to the next row: where the engine wrapped a long line at a space, and where the generator broke a run
    // with no space (one zero-width space ends the row, and nothing stands between the pieces)
    [Fact]
    public async Task A_match_runs_on_to_the_next_row_of_a_wrapped_note()
    {
        await OpenAsync("marks-wrap.html");
        // the engine's wrap: the last word of a row of the story and the first of the next
        var words = await Page.EvaluateAsync<string[]>("""
            () => {
                var h = window.__marks, rows = h.rows(h.note('"story":').texts), story = /^[a-z]+[1-3]$/;
                for (var i = 0; i + 1 < rows.length; i++) {
                    var a = h.words(rows[i]), b = h.words(rows[i + 1]);
                    var last = a[a.length - 1], first = b[0];
                    if (last && first && story.test(last.textContent) && story.test(first.textContent)) {
                        window.__expect = [{ t: last, s: 0, e: last.textContent.length }, { t: first, s: 0, e: first.textContent.length }];
                        return [last.textContent, first.textContent];
                    }
                }
                return [];
            }
            """);
        Assert.True(words.Length == 2, "premise: the engine wraps the story between two of its words");
        var r = await Page.EvaluateAsync<int>("(t) => window.kronikol_marks.mark(window.__marks.note('\"story\":'), [t], -1).count", $"{words[0]} {words[1]}");
        await TwoAnimationFramesAsync(Page);
        Assert.Equal(1, r);
        Assert.Equal("", await TwoRowMarksAsync());
        // the two words with nothing between them are no match
        Assert.Equal(0, await Page.EvaluateAsync<int>("(t) => window.kronikol_marks.mark(window.__marks.note('\"story\":'), [t], -1).count", words[0] + words[1]));

        // the generator's break inside the token: four characters either side of it
        var pieces = await Page.EvaluateAsync<string[]>("""
            () => {
                var h = window.__marks, rows = h.rows(h.note('"token":').texts);
                for (var i = 0; i + 1 < rows.length; i++) {
                    var a = h.words(rows[i]), b = h.words(rows[i + 1]);
                    var last = a[a.length - 1], first = b[0], text = last ? last.textContent : '';
                    if (!last || !first || text.slice(-1) !== h.ZW || text.slice(-2) === h.ZW + h.ZW) continue;
                    var end = text.length - 1;
                    window.__expect = [{ t: last, s: end - 4, e: end }, { t: first, s: 0, e: 4 }];
                    return [text.slice(end - 4, end), first.textContent.slice(0, 4)];
                }
                return [];
            }
            """);
        Assert.True(pieces.Length == 2, "premise: the generator breaks the token and marks the break");
        r = await Page.EvaluateAsync<int>("(t) => window.kronikol_marks.mark(window.__marks.note('\"token\":'), [t], -1).count", pieces[0] + pieces[1]);
        await TwoAnimationFramesAsync(Page);
        Assert.Equal(1, r);
        Assert.Equal("", await TwoRowMarksAsync());
        // the break is no space: the pieces with a space between them are no match
        Assert.Equal(0, await Page.EvaluateAsync<int>("(t) => window.kronikol_marks.mark(window.__marks.note('\"token\":'), [t], -1).count", pieces[0] + " " + pieces[1]));
    }

    // a pair of zero-width spaces ending a row stands for the space the generator's wrap took (CN:96)
    [Fact]
    public async Task A_pair_of_zero_width_spaces_ending_a_row_is_the_space_the_wrap_took()
    {
        await OpenAsync("marks-pair.html");
        var words = await Page.EvaluateAsync<string[]>("""
            () => {
                var h = window.__marks, rows = h.rows(h.inside(h.assertion()));
                for (var i = 0; i + 1 < rows.length; i++) {
                    var a = h.words(rows[i]), b = h.words(rows[i + 1]);
                    var last = a[a.length - 1], first = b[0], text = last ? last.textContent : '';
                    if (!last || !first || text.slice(-2) !== h.ZW + h.ZW) continue;
                    window.__expect = [{ t: last, s: 0, e: text.length - 2 }, { t: first, s: 0, e: first.textContent.length }];
                    return [text.slice(0, -2), first.textContent];
                }
                return [];
            }
            """);
        Assert.True(words.Length == 2, "premise: the assertion's row ends in the generator's pair of zero-width spaces");
        var r = await Page.EvaluateAsync<int>("(t) => window.kronikol_marks.mark(window.__marks.assertion(), [t], -1).count", $"{words[0]} {words[1]}");
        await TwoAnimationFramesAsync(Page);
        Assert.Equal(1, r);
        Assert.Equal("", await TwoRowMarksAsync());
        Assert.Equal(0, await Page.EvaluateAsync<int>("(t) => window.kronikol_marks.mark(window.__marks.assertion(), [t], -1).count", words[0] + words[1]));
    }

    /// <summary>Waits for the ledger note's box to hold "deepword" as its current mark, then reads the box: how far it is
    /// scrolled, how far the word is from its middle and how tall it is, whether the mark is in this box, its marks as
    /// elements, and whether its text is as the note script wrote it.</summary>
    private async Task<double[]> BoxMarkAsync()
    {
        await Page.WaitForFunctionAsync("""
            () => {
                var box = window.__marks.container().querySelector('.note-scroll-frame .note-scroll');
                var hl = window.CSS && CSS.highlights && CSS.highlights.get('kron-mark-current');
                return !!box && !!hl && Array.from(hl).some(r => r.toString() === 'deepword' && box.contains(r.startContainer));
            }
            """, null, new() { Timeout = 15000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        return await Page.EvaluateAsync<double[]>("""
            () => {
                var box = window.__marks.container().querySelector('.note-scroll-frame .note-scroll');
                var range = Array.from(CSS.highlights.get('kron-mark-current'))[0];
                var b = box.getBoundingClientRect(), r = range.getBoundingClientRect();
                return [box.scrollTop, (r.top + r.bottom) / 2 - (b.top + b.bottom) / 2, b.height,
                        box.contains(range.startContainer) ? 1 : 0, box.querySelectorAll('mark').length,
                        box.textContent === window.__boxText ? 1 : 0];
            }
            """);
    }

    // a word in a long note's scroll box: marked with the Highlight API in the box, the box scrolled to it, its text left
    // as it was; drawn again (the box is built anew when its note is near the window), the box takes the marks again
    [Fact]
    public async Task A_match_in_a_long_note_s_scroll_box_is_marked_in_the_box_and_the_box_scrolled_to_it()
    {
        await OpenAsync("marks-box.html");
        await Page.EvaluateAsync("() => window.__marks.note('\"ledger_0001\"').paths[0].scrollIntoView({ block: 'center' })");
        await Page.WaitForFunctionAsync("() => !!window.__marks.container().querySelector('.note-scroll-frame .note-scroll')", null,
            new() { Timeout = 15000, PollingInterval = 200 });
        await Page.EvaluateAsync("() => { window.__boxText = window.__marks.container().querySelector('.note-scroll').textContent; }");
        var r = await Page.EvaluateAsync<int>("() => window.kronikol_marks.mark(() => window.__marks.note('\"ledger_0001\"'), ['deepword'], 0).count");
        Assert.Equal(1, r);

        var state = await BoxMarkAsync();
        Assert.True(state[0] > 0, "the box is scrolled");
        Assert.True(Math.Abs(state[1]) < state[2] / 4, $"the word is {state[1]:F1} px from the box's middle, in a box {state[2]:F1} px tall");
        Assert.Equal(1, state[3]);
        Assert.Equal(0, state[4]);
        Assert.Equal(1, state[5]);
        // the rows the box lies over carry no mark: a word of the note's first lines, drawn under the box and held in it,
        // is marked once, in the box
        Assert.Equal(0, await GroundsAsync());
        var early = await Page.EvaluateAsync<string[]>("""
            () => {
                var a = window.kronikol_marks.mark(() => window.__marks.note('"ledger_0001"'), ['ledger_0005'], 0);
                var hl = Array.from(CSS.highlights.get('kron-mark-current') || []);
                return [String(a.count), String(window.__marks.grounds().length), String(hl.length === 1 && window.__marks.container().querySelector('.note-scroll').contains(hl[0].startContainer))];
            }
            """);
        Assert.Equal(["1", "0", "True"], early, StringComparer.OrdinalIgnoreCase);
        await Page.EvaluateAsync("() => window.kronikol_marks.mark(() => window.__marks.note('\"ledger_0001\"'), ['deepword'], 0)");

        // Headers turned off for the report (its command run, as the way to a match runs it) draws the diagram again, and
        // the box is built again once the new picture is near
        await Page.EvaluateAsync("() => { window.__before = window.__marks.svg(); }");
        await Page.EvaluateAsync("() => window._toggleHeaders(document.querySelector(\"button[data-toggle='headers'][onclick*='_toggleHeaders(']\"))");
        await Page.WaitForFunctionAsync("() => !document.contains(window.__before) && window.__marks.settled()", null,
            new() { Timeout = 60000, PollingInterval = 200 });
        await Page.EvaluateAsync("() => window.__marks.note('\"ledger_0001\"').paths[0].scrollIntoView({ block: 'center' })");
        await Page.WaitForFunctionAsync("() => !!window.__marks.container().querySelector('.note-scroll-frame .note-scroll')", null,
            new() { Timeout = 15000, PollingInterval = 200 });
        await Page.EvaluateAsync("() => { window.__boxText = window.__marks.container().querySelector('.note-scroll').textContent; }");
        state = await BoxMarkAsync();
        Assert.True(Math.Abs(state[1]) < state[2] / 4, $"drawn again, the word is {state[1]:F1} px from the box's middle, in a box {state[2]:F1} px tall");
        Assert.Equal(1, state[3]);
        Assert.Equal(1, state[5]);
    }

    // A long note's scroll box given as the place by its frame is the HTML it is, though drawn inside the picture: outlined
    // inside its edge (the frame clips what lies outside it), focused or not, its words highlighted and the box brought to
    // the one named; drawn anew and the box built again, it is found again by its note
    [Fact]
    public async Task A_scroll_box_given_by_its_frame_is_outlined_inside_it_and_found_again()
    {
        await OpenAsync("marks-box-place.html");
        await Page.EvaluateAsync("() => window.__marks.note('\"ledger_0001\"').paths[0].scrollIntoView({ block: 'center' })");
        await Page.WaitForFunctionAsync("() => !!window.__marks.container().querySelector('.note-scroll-frame .note-scroll')", null,
            new() { Timeout = 15000, PollingInterval = 200 });
        await Page.EvaluateAsync("() => { window.__boxText = window.__marks.container().querySelector('.note-scroll').textContent; }");
        var r = await Page.EvaluateAsync<string[]>("""
            () => {
                var frame = window.__marks.container().querySelector('.note-scroll-frame'), box = frame.querySelector('.note-scroll');
                var a = window.kronikol_marks.mark(frame, ['deepword'], 0), cs = getComputedStyle(box);
                var out = [String(a.count), String(a.place === box), cs.outlineStyle, cs.outlineOffset, String(window.__marks.grounds().length)];
                if (!box.hasAttribute('tabindex')) box.setAttribute('tabindex', '-1');
                box.focus({ preventScroll: true });
                out.push(String(document.activeElement === box), getComputedStyle(box).outlineStyle);
                return out;
            }
            """);
        Assert.Equal(["1", "True", "solid", "-2px", "0", "True", "solid"], r, StringComparer.OrdinalIgnoreCase);
        var state = await BoxMarkAsync();
        Assert.True(Math.Abs(state[1]) < state[2] / 4, $"the word is {state[1]:F1} px from the box's middle, in a box {state[2]:F1} px tall");

        // Headers turned off for the report (its command run, as the way to a match runs it) draws the diagram again, and
        // the box built again once the new picture is near takes the marks again
        await Page.EvaluateAsync("() => { window.__before = window.__marks.svg(); }");
        await Page.EvaluateAsync("() => window._toggleHeaders(document.querySelector(\"button[data-toggle='headers'][onclick*='_toggleHeaders(']\"))");
        await Page.WaitForFunctionAsync("() => !document.contains(window.__before) && window.__marks.settled()", null,
            new() { Timeout = 60000, PollingInterval = 200 });
        await Page.EvaluateAsync("() => window.__marks.note('\"ledger_0001\"').paths[0].scrollIntoView({ block: 'center' })");
        await Page.WaitForFunctionAsync("() => !!window.__marks.container().querySelector('.note-scroll-frame .note-scroll')", null,
            new() { Timeout = 15000, PollingInterval = 200 });
        await Page.EvaluateAsync("() => { window.__boxText = window.__marks.container().querySelector('.note-scroll').textContent; }");
        state = await BoxMarkAsync();
        Assert.Equal(1, state[3]);
        Assert.Equal(1, await Page.EvaluateAsync<int>("() => window.__marks.container().querySelectorAll('.note-scroll.kron-mark-place').length"));
    }

    // in the page's HTML the words are marked with the Highlight API: a step and the failure keep their text and markup
    [Fact]
    public async Task HTML_places_are_marked_with_the_highlight_API_and_keep_their_text()
    {
        await OpenAsync("marks-html.html");
        var r = await Page.EvaluateAsync<string[]>("""
            () => {
                var pre = document.querySelector('details.failure-result > pre'), step = document.querySelectorAll('details.scenario .step')[1];
                var html = [pre.innerHTML, step.innerHTML];
                var a = window.kronikol_marks.mark(pre, ['flour'], 0);
                var marked = Array.from(CSS.highlights.get('kron-mark-current') || []).map(x => x.toString()).join('|');
                var outlined = pre.classList.contains('kron-mark-place');
                var b = window.kronikol_marks.mark(step, ['FLOUR', 'pancake order'], -1);
                var words = Array.from(CSS.highlights.get('kron-mark') || []).map(x => x.toString()).join('|');
                var stepOutlined = step.classList.contains('kron-mark-place') && !pre.classList.contains('kron-mark-place');
                // a term never runs across a line the failure breaks
                var c = window.kronikol_marks.mark(pre, ['ground but'], -1);
                return [String(a.count), marked, String(outlined), String(b.count), words, String(stepOutlined), String(c.count),
                        String(pre.innerHTML === html[0] && step.innerHTML === html[1]), String(document.querySelectorAll('mark').length)];
            }
            """);
        Assert.Equal("1", r[0]);
        Assert.Equal("flour", r[1]);
        Assert.Equal("True", r[2], ignoreCase: true);
        Assert.Equal("2", r[3]);
        Assert.Equal("pancake order|flour", r[4]);
        Assert.Equal("True", r[5], ignoreCase: true);
        Assert.Equal("0", r[6]);
        Assert.Equal("True", r[7], ignoreCase: true);
        Assert.Equal("0", r[8]);
    }

    // a browser without the Highlight API gets the place's outline and no word marks, never <mark> elements
    [Fact]
    public async Task Without_the_highlight_API_an_HTML_place_is_outlined_and_its_words_left_alone()
    {
        await OpenAsync("marks-nohighlight.html");
        var r = await Page.EvaluateAsync<string[]>("""
            () => {
                window.Highlight = undefined;
                var pre = document.querySelector('details.failure-result > pre'), html = pre.innerHTML;
                var a = window.kronikol_marks.mark(pre, ['flour'], 0);
                return [String(pre.classList.contains('kron-mark-place')), String(CSS.highlights.size), String(pre.innerHTML === html),
                        String(document.querySelectorAll('mark').length), String(a ? a.count : -1)];
            }
            """);
        Assert.Equal("True", r[0], ignoreCase: true);
        Assert.Equal("0", r[1]);
        Assert.Equal("True", r[2], ignoreCase: true);
        Assert.Equal("0", r[3]);
        // the occurrence is found, for the page to bring into view, though it cannot be painted
        Assert.Equal("1", r[4]);
    }

    // a <text> is never split into <tspan>s, nor its text changed: the marks are drawn beside the texts
    [Fact]
    public async Task A_mark_leaves_every_text_of_the_diagram_whole()
    {
        await OpenAsync("marks-whole.html");
        var r = await Page.EvaluateAsync<string[]>("""
            () => {
                var svg = window.__marks.svg(), read = () => {
                    var texts = Array.from(svg.querySelectorAll('text'));
                    return [texts.length, svg.querySelectorAll('tspan').length, texts.map(t => t.innerHTML).join('\n')];
                };
                var before = read();
                // the key's flour, the value's "Stoneground flour" (its flour inside it), mill, the value's second flour
                var a = window.kronikol_marks.mark(window.__marks.note('"flour":'), ['flour', 'stoneground flour', 'mill'], 1);
                var after = read();
                // each mark is drawn in the svg, behind the text it marks: the first element after it that is no mark is a
                // text the mark lies under (they share more than a pixel across), so the words paint over it
                var h = window.__marks;
                var behind = (m) => { var n = m.nextElementSibling; while (n && n.classList.contains('kron-mark-svg')) n = n.nextElementSibling; return n; };
                var under = (m) => { var t = behind(m); if (!t || t.tagName !== 'text') return false; var a = h.box(m), b = h.box(t); return Math.min(a.r, b.r) - Math.max(a.l, b.l) > 1; };
                return [String(a.count), String(before[0] === after[0]), String(after[1]), String(before[2] === after[2]),
                        String(h.grounds().every(m => m.tagName === 'rect' && m.getAttribute('pointer-events') === 'none' && under(m)))];
            }
            """);
        Assert.Equal("4", r[0]);
        Assert.Equal("True", r[1], ignoreCase: true);
        Assert.Equal("0", r[2]);
        Assert.Equal("True", r[3], ignoreCase: true);
        Assert.Equal("True", r[4], ignoreCase: true);
    }

    // the occurrence the line named gets an outline of the mark's edge, the others an underline
    [Fact]
    public async Task The_named_occurrence_is_outlined_and_the_others_underlined()
    {
        await OpenAsync("marks-current.html");
        var result = await MarkNoteAsync("\"flour\":", ["flour"], current: 1);
        Assert.Equal(1, result.GetProperty("current").GetInt32());
        var r = await Page.EvaluateAsync<string[]>("""
            () => {
                var h = window.__marks, grounds = h.grounds(), edge = h.token('--kron-mark-edge');
                var current = grounds.filter(m => m.classList.contains('kron-mark-current'));
                // reading order: the key on the first row, then the value's two
                var order = grounds.slice().sort((a, b) => { var x = h.box(a), y = h.box(b); return x.t - y.t || x.l - y.l; });
                return [String(current.length), String(order.indexOf(current[0])), getComputedStyle(current[0]).stroke, edge,
                        String(h.edges().length), String(parseFloat(getComputedStyle(current[0]).strokeWidth))];
            }
            """);
        Assert.Equal("1", r[0]);
        Assert.Equal("1", r[1]);
        Assert.Equal(r[3], r[2]);
        Assert.Equal("2", r[4]);
        Assert.Equal("2", r[5]);
    }

    // the place pulses twice over 0.8 s on arrival, in the accent; under reduced motion it holds still
    [Fact]
    public async Task The_place_pulses_twice_on_arrival_and_holds_still_under_reduced_motion()
    {
        await OpenAsync("marks-pulse.html");
        await MarkNoteAsync("\"flour\":", ["flour"]);
        var moving = await Page.EvaluateAsync<string[]>("""
            () => {
                var p = document.querySelector('.kron-mark-place'), cs = getComputedStyle(p);
                return [cs.animationName, cs.animationIterationCount, cs.animationDuration, cs.stroke, window.__marks.token('--kron-accent'),
                        String(p.getAttribute('pointer-events'))];
            }
            """);
        Assert.NotEqual("none", moving[0]);
        Assert.Equal("2", moving[1]);
        Assert.Equal("0.4s", moving[2]);
        Assert.Equal(moving[4], moving[3]);
        Assert.Equal("none", moving[5]);

        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await MarkNoteAsync("\"flour\":", ["flour"]);
        Assert.Equal("none", await Page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('.kron-mark-place')).animationName"));
    }

    // An arrow's label is a one-line form: the generator breaks a long path onto rows with the label's own line break, which
    // the search reads as nothing between them (report-search-index.js, rule 5a), so a match runs on across that row's end
    // with nothing between its pieces; the label is outlined round its words
    [Fact]
    public async Task A_match_runs_on_across_the_rows_of_an_arrow_s_label_as_the_search_reads_them()
    {
        await OpenAsync("marks-label.html");
        var pieces = await Page.EvaluateAsync<string[]>("""
            () => {
                // the label's words: the arrow's number, drawn half way down beside a label of two rows, is no part of them
                var h = window.__marks, rows = h.rows(Array.from(h.message('shelf01').querySelectorAll('text')).filter(t => !/^[0-9]+$/.test(t.textContent)));
                for (var i = 0; i + 1 < rows.length; i++) {
                    var a = h.words(rows[i]), b = h.words(rows[i + 1]);
                    var last = a[a.length - 1], first = b[0];
                    if (!last || !first || last.textContent.indexOf('shelf') < 0) continue;
                    var end = last.textContent.length;
                    window.__expect = [{ t: last, s: end - 4, e: end }, { t: first, s: 0, e: 4 }];
                    return [last.textContent.slice(end - 4), first.textContent.slice(0, 4)];
                }
                return [];
            }
            """);
        Assert.True(pieces.Length == 2, "premise: the label's path is drawn on two rows");
        var r = await Page.EvaluateAsync<string[]>("""
            (t) => {
                var h = window.__marks, label = h.message('shelf01'), a = window.kronikol_marks.mark(label, [t], 0);
                var ring = h.box(a.place), words = Array.from(label.querySelectorAll('text')).map(h.box);
                var round = words.every(w => h.covers(ring, w, 0.5));
                return [String(a.count), String(round), String(a.place.parentNode === label.parentNode || label.contains(a.place))];
            }
            """, pieces[0] + pieces[1]);
        await TwoAnimationFramesAsync(Page);
        Assert.Equal("1", r[0]);
        Assert.Equal("True", r[1], ignoreCase: true);
        Assert.Equal("", await TwoRowMarksAsync());
    }

    // A label given as an element is found again in a picture drawn anew by its words, not its arrow's number: Events turned
    // off for the scenario takes the event's arrow out and numbers the arrows after it anew, and the marks come back on it
    [Fact]
    public async Task A_marked_label_comes_back_after_a_redraw_that_numbers_its_arrow_anew()
    {
        await OpenAsync("marks-renumber.html");
        var before = await Page.EvaluateAsync<string[]>("""
            () => {
                var h = window.__marks, label = h.message('shelf01'), a = window.kronikol_marks.mark(label, ['shelf03'], -1);
                window.__before = h.svg();
                return [String(a.count), String(h.grounds().length), h.numberOf(label)];
            }
            """);
        Assert.Equal("1", before[0]);
        Assert.Equal("1", before[1]);
        Assert.Matches("^[0-9]+$", before[2]);

        // from the scenario's own settings: clicks inside the scenario, which keep the marks
        var scenario = Page.Locator("details.scenario").First;
        await OpenScenarioSettingsAsync(scenario);
        await scenario.Locator(".toggle-btn[data-toggle='events'][data-shown='true']").First.ClickAsync();
        await Page.WaitForFunctionAsync("() => !document.contains(window.__before) && window.__marks.settled()", null,
            new() { Timeout = 60000, PollingInterval = 200 });
        var after = await Page.EvaluateAsync<string>("() => window.__marks.numberOf(window.__marks.message('shelf01'))");
        Assert.Matches("^[0-9]+$", after);
        Assert.True(after != before[2], "premise: the arrow is numbered anew (" + before[2] + " before, " + after + " after)");

        await Page.WaitForFunctionAsync(
            "() => window.__marks.grounds().length === 1 && window.__marks.grounds()[0].ownerSVGElement === window.__marks.svg()",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        Assert.Equal("", await Page.EvaluateAsync<string>("""
            () => {
                var h = window.__marks, hits = h.found(Array.from(h.message('shelf01').querySelectorAll('text')), 'shelf03');
                if (hits.length !== 1) return hits.length + ' shelf03 drawn in the label';
                return h.markOver(hits[0].box) ? '' : 'nothing marks shelf03 at ' + JSON.stringify(hits[0].box) + ', marks at ' + JSON.stringify(h.grounds().map(h.box));
            }
            """));
    }

    // A diagram given whole (the way to a match names its participants and calls, not one note) is outlined, and the words
    // in its picture marked as a picture's are, never with ::highlight on SVG text: each note, each label and each row read
    // as a text of its own, so two participants drawn side by side are no one word, and the first in reading order named.
    // Drawn anew, the marks come back.
    [Fact]
    public async Task A_diagram_given_whole_is_outlined_and_the_words_in_its_picture_marked()
    {
        await OpenAsync("marks-whole.html");
        const string mark = """
            (terms) => {
                window.__before = window.__marks.svg();
                return window.kronikol_marks.mark(window.__marks.container(), terms, 0).count;
            }
            """;
        await Page.EvaluateAsync<int>(mark, new[] { "flour" });
        await TwoAnimationFramesAsync(Page);
        Assert.Equal("", await Page.EvaluateAsync<string>(WholeFlourMarks));

        // the participants side by side, "Pantry API" then "Ledger API", are no "apiledger"
        Assert.Equal(0, await Page.EvaluateAsync<int>(mark, new[] { "apiledger" }));

        // pantry is drawn in the shelf call's label before the participants in the reading of the groups (notes, labels,
        // the rest), but the participant's name at the top is the first in reading order
        Assert.Equal("", await Page.EvaluateAsync<string>("""
            () => {
                window.kronikol_marks.mark(window.__marks.container(), ['pantry'], 0);
                var h = window.__marks, grounds = h.grounds(), named = document.querySelector('.kron-mark-ground.kron-mark-current');
                if (grounds.length < 3 || !named) return grounds.length + ' marks of pantry, ' + (named ? 'one' : 'none') + ' named';
                return grounds.some(g => h.box(g).t < h.box(named).t - 1) ? 'the one named is not the first in reading order' : '';
            }
            """));

        // Headers turned off for the scenario draws the picture again, and the marks come back on it
        await Page.EvaluateAsync<int>(mark, new[] { "flour" });
        var scenario = Page.Locator("details.scenario").First;
        await OpenScenarioSettingsAsync(scenario);
        await scenario.Locator(".toggle-btn[data-toggle='headers'][data-shown='true']").First.ClickAsync();
        await Page.WaitForFunctionAsync("() => !document.contains(window.__before) && window.__marks.settled()", null,
            new() { Timeout = 60000, PollingInterval = 200 });
        await Page.WaitForFunctionAsync(
            "() => window.__marks.grounds().length > 0 && window.__marks.grounds().every(r => r.ownerSVGElement === window.__marks.svg())",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        Assert.Equal("", await Page.EvaluateAsync<string>(WholeFlourMarks));
    }

    /// <summary>What is wrong with the marks of flour on the whole diagram, marked with the first named: one mark over each
    /// flour drawn and as many occurrences, the named one the first in reading order, no highlight on SVG text, and the
    /// diagram outlined. Empty when nothing is.</summary>
    private const string WholeFlourMarks = """
        () => {
            var h = window.__marks, host = h.container(), bad = [], marked = window.kronikol_marks.marked();
            var want = h.found(Array.from(h.svg().querySelectorAll('text')).filter(t => !t.closest('.note-toggle-icon')), 'flour');
            var grounds = h.grounds();
            if (want.length < 5) bad.push("premise: " + want.length + " flour drawn, not the pantry's three, the event's, the step bar's and the assertion's");
            if (!marked || marked.count !== want.length) bad.push((marked ? marked.count : 'no') + ' occurrences for ' + want.length + ' drawn');
            if (grounds.length !== want.length) bad.push(grounds.length + ' marks for ' + want.length + ' drawn');
            want.forEach(w => { if (!h.markOver(w.box)) bad.push('nothing marks "' + w.t.textContent + '" at ' + JSON.stringify(w.box)); });
            var named = document.querySelector('.kron-mark-ground.kron-mark-current');
            if (!named) bad.push('none named');
            else if (grounds.some(g => h.box(g).t < h.box(named).t - 1)) bad.push('the one named is not the first in reading order');
            var onSvgText = ['kron-mark', 'kron-mark-current'].some(n => Array.from(CSS.highlights.get(n) || [])
                .some(r => r.startContainer.parentElement && r.startContainer.parentElement.namespaceURI === 'http://www.w3.org/2000/svg'));
            if (onSvgText) bad.push('a highlight on SVG text');
            if (!host.classList.contains('kron-mark-place')) bad.push('the diagram is not outlined');
            return bad.join('; ');
        }
        """;

    // A diagram given whole before its picture is drawn (its scenario only just opened, as the way to a match may give it
    // when it cannot wait for the drawing) is outlined at once, and its words marked once the picture is drawn. The report
    // has no ledger, so no scroll box is built with the picture to set the marks going again.
    [Fact]
    public async Task A_diagram_marked_before_its_picture_is_drawn_is_marked_once_it_is()
    {
        // The page starts drawing its first scenario's diagrams as soon as it is parsed (the render script's preload), and
        // this report's one diagram is there: marked once the page had loaded, the mark raced that drawing, and under a
        // full suite's load it lost (5.13.0's full run: 310 texts already drawn). A listener that runs before the page's
        // own marks it before any drawing can start. The report is a compressed page, whose loader puts the report in
        // place with document.open, write and close, and document.open erases every listener added before it; the first
        // write comes after it and before the report's own scripts run, so the listener is added there.
        await Page.AddInitScriptAsync("""
            (() => {
                var done = false;
                function early() {
                    if (done || !document.querySelector('[data-diagram-type="plantuml"]')) return;
                    done = true;
                    try {
                        document.querySelectorAll('details.feature, details.scenario').forEach(d => { d.open = true; });
                        var host = document.querySelector('[data-diagram-type="plantuml"]');
                        var drawn = host.querySelectorAll('svg text').length;
                        var a = window.kronikol_marks.mark(host, ['flour'], 0);
                        window.__early = [String(drawn), String(a ? a.count : -1), String(host.classList.contains('kron-mark-place'))];
                    } catch (e) {
                        window.__early = ['the early mark threw: ' + e, '', ''];
                    }
                }
                var write = Document.prototype.write;
                Document.prototype.write = function () {
                    if (this === document) document.addEventListener('DOMContentLoaded', early);
                    return write.apply(this, arguments);
                };
                document.addEventListener('DOMContentLoaded', early);
            })();
            """);
        await Page.GotoReportAsync(WriteReport(TempDir, OutputDir, "marks-early.html", withLedger: false));
        await Page.Locator("details.feature").First.WaitForAsync();
        var early = await Page.EvaluateAsync<string[]>("() => window.__early");
        Assert.True(early[0] == "0", "premise: the picture is not drawn yet (" + early[0] + " texts)");
        Assert.Equal("0", early[1]);
        Assert.Equal("True", early[2], ignoreCase: true);

        await Page.EvaluateAsync("() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");
        await Page.Locator("[data-diagram-type='plantuml'] svg:visible").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60000 });
        await Page.EvaluateAsync(Helpers);
        await SettledAsync();
        await Page.WaitForFunctionAsync("() => window.__marks.grounds().length >= 5", null, new() { Timeout = 15000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        Assert.Equal("", await Page.EvaluateAsync<string>(WholeFlourMarks));
    }

    // the marks hold in Firefox and WebKit as in Chromium: the rects over the words (a word is a <text> of its own, whose
    // box is the reference there, as a Range's rects on scaled SVG text are misplaced in both) and the HTML's highlight
    [Theory]
    [InlineData("firefox")]
    [InlineData("webkit")]
    public Task The_marks_sit_on_the_words_in_Firefox_and_WebKit(string engine) => InEngineAsync(engine, async page =>
    {
        await OpenAsync($"marks-{engine}.html", page: page);
        var r = await page.EvaluateAsync<string>("""
            () => {
                var h = window.__marks, g = h.note('"flour":'), bad = [];
                var a = window.kronikol_marks.mark(g, ['flour'], -1);
                if (a.count !== 3 || h.grounds().length !== 3) bad.push(a.count + ' occurrences, ' + h.grounds().length + ' marks');
                g.texts.filter(t => t.textContent === 'flour').forEach(t => {
                    if (!h.markOver(h.box(t))) bad.push('nothing marks a flour at ' + JSON.stringify(h.box(t)) + ', marks at ' + JSON.stringify(h.grounds().map(h.box)));
                });
                var pre = document.querySelector('details.failure-result > pre'), html = pre.innerHTML;
                var b = window.kronikol_marks.mark(pre, ['flour'], 0);
                var hl = Array.from(CSS.highlights.get('kron-mark-current') || []).map(x => x.toString()).join('|');
                if (b.count !== 1 || hl !== 'flour') bad.push('the failure: ' + b.count + ' occurrences, highlighted ' + hl);
                if (pre.innerHTML !== html) bad.push('the text of the failure changed');
                return bad.join('; ');
            }
            """);
        Assert.Equal("", r);
    });

    // Export Filtered HTML copies the features as the page shows them: the export carries no marks, neither an HTML
    // place's outline nor the rects a marked picture holds (the export draws the page's own diagrams again, but keeps a
    // picture drawn when the report was written as it is)
    [Fact]
    public async Task An_export_taken_while_marks_are_on_carries_none()
    {
        await OpenAsync("marks-export.html");
        var r = await Page.EvaluateAsync<string[]>("""
            () => {
                var feature = document.querySelector('details.feature'), pre = document.querySelector('details.failure-result > pre');
                var a = window.kronikol_marks.mark(pre, ['flour'], 0);
                var withHtml = export_undrawn(feature);
                // a picture drawn when the report was written, which the export keeps: the drawn diagram, its marks of
                // having been drawn by the page taken off while the export is taken
                var host = window.__marks.container(), kept = ['data-rendered', 'data-queued'].map(n => [n, host.getAttribute(n)]);
                var b = window.kronikol_marks.mark(window.__marks.note('"flour":'), ['flour'], 0);
                kept.forEach(k => host.removeAttribute(k[0]));
                var withSvg = export_undrawn(feature);
                kept.forEach(k => { if (k[1] !== null) host.setAttribute(k[0], k[1]); });
                return [String(a.count), String(b.count), String(withHtml.indexOf('kron-mark') >= 0), String(withSvg.indexOf('kron-mark') >= 0),
                        String(document.querySelectorAll('.kron-mark-ground').length)];
            }
            """);
        Assert.Equal("1", r[0]);
        Assert.Equal("3", r[1]);
        Assert.Equal("False", r[2], ignoreCase: true);
        Assert.Equal("False", r[3], ignoreCase: true);
        // the export took nothing off the page
        Assert.Equal("3", r[4]);
    }

    // Back that leaves the scenario (to the Search tab the reader came from) takes the marks away
    [Fact]
    public async Task The_marks_go_when_Back_leaves_the_scenario()
    {
        await OpenAsync("marks-back.html");
        await FillSearchBar("pancake", toSearchTab: true);
        await Page.WaitForFunctionAsync("() => !document.getElementById('panel-search').hidden", null, new() { PollingInterval = 200 });
        await OpenReportTabAsync("features");
        await SettledAsync();
        await MarkNoteAsync("\"flour\":", ["flour"]);
        Assert.Equal(3, await GroundsAsync());

        await Page.GoBackAsync();
        await Page.WaitForFunctionAsync("() => !document.getElementById('panel-search').hidden", null, new() { PollingInterval = 200 });
        await Page.WaitForFunctionAsync("() => window.__marks.grounds().length === 0", null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    // A place not laid out when it is marked (its tab not shown) is marked once it is. (A closed section is laid out when
    // it is read, its content being content-visibility: hidden in Chromium, so its marks are drawn at once.)
    [Fact]
    public async Task A_place_not_laid_out_is_marked_once_it_is()
    {
        await OpenAsync("marks-later.html");
        await Page.EvaluateAsync("""
            () => {
                window.__place = window.__marks.note('"flour":');
                window.kronikol_nav.go({ tab: 'timeline' });
            }
            """);
        await TwoAnimationFramesAsync(Page);
        var count = await Page.EvaluateAsync<int>("""
            () => window.__marks.svg().getBoundingClientRect().width === 0 ? window.kronikol_marks.mark(window.__place, ['flour'], -1).count : -1
            """);
        Assert.Equal(3, count);
        Assert.Equal(0, await GroundsAsync());

        await Page.EvaluateAsync("() => window.kronikol_nav.go({ tab: 'features' })");
        await Page.WaitForFunctionAsync("() => window.__marks.grounds().length === 3", null, new() { Timeout = 5000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);
        Assert.Equal("", await MarksOverAsync("\"flour\":", "flour"));
    }

    // the marks go when the reader closes the scenario
    [Fact]
    public async Task The_marks_go_when_the_scenario_is_closed()
    {
        await OpenAsync("marks-close.html");
        await MarkNoteAsync("\"flour\":", ["flour"]);
        Assert.Equal(3, await GroundsAsync());
        await Page.EvaluateAsync("() => { document.querySelector('details.scenario').open = false; }");
        await Page.WaitForFunctionAsync("() => window.__marks.grounds().length === 0 && window.__marks.places().length === 0", null,
            new() { Timeout = 5000, PollingInterval = 200 });
    }
}
