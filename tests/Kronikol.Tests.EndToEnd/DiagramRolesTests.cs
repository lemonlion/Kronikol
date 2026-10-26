using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The role pass (5.0.0, plans/OPTION_C_PLAN.md S10, P24 to P30): once the page has drawn a sequence diagram, each
/// element is found by its structure and given a role, and the stylesheet colours the roles from the theme's tokens.
/// Lifelines and participants; step bars as light bands with dark ink, a step bar's table on its own ground with a
/// shaded header; every strip the drawing's full width; notes as rounded cards, an event note as an event card, its
/// kind read from the drawn source even with two stereotypes (F3). A diagram drawn again is tagged again.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class DiagramRolesTests : DiagramNotePlaywrightBase
{
    public DiagramRolesTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string Source = $$"""
        @startuml
        !pragma teoz true
        skinparam shadowing false
        skinparam SequenceMessagePadding 4
        skinparam sequenceMessageAlignment center
        <style>
        sequenceDiagram { participant { RoundCorner 12; Padding 8 14; Margin 0 10; LineThickness 1 } }
        sequenceDiagram { actor, database, queue, collections, boundary, control, entity { Margin 0 10; LineThickness 1 } }
        sequenceDiagram { lifeLine { LineStyle 0; LineThickness 1 } }
        sequenceDiagram { note { LineThickness 1 } }
        </style>
        <style>
         .eventNote {
             BackgroundColor #cfecf7
             FontSize 11
             RoundCorner 10
         }
        </style>
        {{Kronikol.PlantUml.StepBarPlantUml.BodyStyle}}
        actor "Caller" as caller
        participant "OrderService" as svc
        queue "Orders" as orders
        hnote across <<stepDelimiter>> #black:<color:white>Given the shop is open
        caller -> svc : POST /api/orders
        note right
        {"item":"Blueberry","qty":2}
        end note
        svc -> orders : Publish OrderPlaced
        note<<eventNote>> right
        {"event":"OrderPlaced"}
        end note
        hnote across <<stepDelimiter>><<stepBody>>: Given the following muffins exist\n\n|= name |= price |\n| Blueberry | 3.50 |\n| Double Chocolate | 4.00 |
        svc --> caller : 201 Created
        @enduml
        """;

    // A token's value and an element's computed paint, both as the browser resolves a colour
    private const string Paint = """
        window.__tok = window.__tok || function (name) {
            const p = document.createElement('div'); p.style.color = 'var(' + name + ')'; document.body.appendChild(p);
            const c = getComputedStyle(p).color; p.remove(); return c;
        };
        """;

    private async Task OpenAsync(string fileName, NoteWidthMode noteWidth = NoteWidthMode.Default, string source = Source)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios = [new() { Id = "s1", DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode("s1", "", source)],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: ResolvedToggleDefaults.BuiltIn with { NoteWidth = noteWidth });
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForRolesAsync();
    }

    private Task WaitForRolesAsync() => Page.WaitForFunctionAsync(
        "() => { const s = document.querySelector(\"[data-diagram-type='plantuml'] svg\"); return !!s && !!s.getAttribute('data-k-roles'); }",
        null, new() { Timeout = 15000, PollingInterval = 200 });

    private Task<T> InSvgAsync<T>(string body) => Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<T>(
        "el => { " + Paint + body + " }");

    [Fact]
    public async Task Lifelines_and_participants_take_their_roles_and_colours()
    {
        await OpenAsync("RolesLifelines.html");

        var r = await InSvgAsync<string[]>("""
            const roles = JSON.parse(el.getAttribute('data-k-roles'));
            const life = el.querySelector('.k-lifeline'), part = el.querySelector('.k-participant-fill');
            return [String(roles.lifelines), String(roles.participants),
                    getComputedStyle(life).stroke, __tok('--dg-lifeline'),
                    getComputedStyle(part).fill, __tok('--dg-participant')];
            """);

        Assert.True(int.Parse(r[0]) >= 3, $"lifelines: {r[0]}");
        Assert.True(int.Parse(r[1]) >= 6, $"participants: {r[1]}");
        Assert.Equal(r[3], r[2]);
        Assert.Equal(r[5], r[4]);
    }

    // SequenceDiagramParticipantColors writes a participant's dependency colour on its declaration, the real emitter's
    // source here: from 5.0.0 the role pass gave every filled participant the theme's participant colour, so the option
    // drew no colour at all, on the page or in a copy
    [Theory]
    [InlineData(null, "rgb(14, 124, 134)")] // the palette's database colour, #0E7C86
    [InlineData("#LightBlue", "rgb(173, 216, 230)")] // a colour's name, which the drawing writes as #ADD8E6
    [InlineData("#0AF", "rgb(0, 170, 255)")] // three digits, which the drawing writes as six
    public async Task A_participant_coloured_by_its_dependency_keeps_its_colour(string? colour, string painted)
    {
        var pairId = Guid.NewGuid();
        Kronikol.Tracking.RequestResponseLog Log(Kronikol.Tracking.RequestResponseType type) =>
            new("t1", "t1", "SELECT", null, new Uri("http://stock/items"), [], "Stock", "OrderService", type,
                Guid.NewGuid(), pairId, TrackingIgnore: false, DependencyCategory: "SqlServer");
        var source = Kronikol.PlantUml.PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
                [Log(Kronikol.Tracking.RequestResponseType.Request), Log(Kronikol.Tracking.RequestResponseType.Response)],
                sequenceDiagramParticipantColors: true,
                dependencyColors: colour is null ? null : new() { ["SqlServer"] = colour })
            .Single().PlantUmls.First().PlainText;
        Assert.Contains("database \"Stock\" as stock " + (colour ?? "#0E7C86"), source);
        await OpenAsync($"RolesParticipantColours{(colour ?? "").TrimStart('#')}.html", source: source);

        var r = await InSvgAsync<string[]>($"""
            const parts = [...el.querySelectorAll('.k-participant')];
            const own = parts.filter(e => getComputedStyle(e).fill === '{painted}').length;
            const themed = parts.filter(e => e.classList.contains('k-participant-fill')).map(e => getComputedStyle(e).fill);
            return [String(own), String(themed.length), String(themed.every(f => f === __tok('--dg-participant')))];
            """);

        Assert.True(int.Parse(r[0]) >= 2, $"participants painted in the database's colour: {r[0]}");
        Assert.True(int.Parse(r[1]) >= 1, $"participants in the theme's colour: {r[1]}");
        Assert.Equal("true", r[2]);
    }

    // A scenario that made no calls: its one participant is the generator's placeholder, so the drawing has one lifeline
    // (the owner's report, 2026-10-21: its step bars stayed black)
    private const string NoInteractions = """
        @startuml
        !pragma teoz true
        skinparam defaultFontName system-ui
        skinparam shadowing false
        skinparam SequenceMessagePadding 4
        skinparam sequenceMessageAlignment center
        <style>
        sequenceDiagram { participant { RoundCorner 12; Padding 8 14; Margin 0 10; LineThickness 1 } }
        sequenceDiagram { actor, database, queue, collections, boundary, control, entity { Margin 0 10; LineThickness 1 } }
        sequenceDiagram { lifeLine { LineStyle 0; LineThickness 1 } }
        sequenceDiagram { note { LineThickness 1 } }
        </style>

        <style>
         .assertionNote {
             FontSize 11
             RoundCorner 5
         }
        </style>
        skinparam wrapWidth 800
        autonumber 1

        participant "(no interactions)" as noInteractions


        hnote across <<stepDelimiter>> #black:<color:white>Given I have never signed in


        hnote across <<stepDelimiter>> #black:<color:white>When that visitor asks graphql for the canonical merchant's locations


        hnote across <<assertionNote>> #d4edda
        ✓ An unauthenticated caller is rejected outright
        end note


        hnote across <<stepDelimiter>> #black:<color:white>Then that visitor receives no customer locations

        @enduml
        """;

    [Fact]
    public async Task A_step_bar_is_a_light_band_with_dark_ink_in_a_drawing_with_one_lifeline()
    {
        await OpenAsync("RolesBandsOneLifeline.html", source: NoInteractions);

        var r = await InSvgAsync<string[]>("""
            const bands = Array.from(el.querySelectorAll('.k-step'));
            // the words drawn in a bar (wrapWidth draws one text per word), found by where they stand
            const boxes = bands.map(b => b.getBBox());
            const labels = Array.from(el.querySelectorAll('text')).filter(t => {
                const x = parseFloat(t.getAttribute('x')), y = parseFloat(t.getAttribute('y')) - 3;
                return boxes.some(b => x >= b.x - 1 && x <= b.x + b.width + 1 && y >= b.y - 1 && y <= b.y + b.height + 1);
            });
            const black = Array.from(el.querySelectorAll('rect, path, polygon')).filter(e => getComputedStyle(e).fill === 'rgb(0, 0, 0)');
            return [String(bands.length), bands.length ? getComputedStyle(bands[0]).fill : 'none', __tok('--dg-step'),
                    String(labels.length), String(labels.filter(t => t.classList.contains('k-step-text')).length),
                    labels.length ? getComputedStyle(labels[0]).fill : 'none', __tok('--dg-step-ink'), String(black.length)];
            """);

        Assert.Equal("3", r[0]);
        Assert.Equal(r[2], r[1]);
        Assert.True(int.Parse(r[3]) >= 3, $"words in the bars: {r[3]}");
        Assert.Equal(r[3], r[4]);
        Assert.Equal(r[6], r[5]);
        Assert.Equal("0", r[7]);
    }

    // One participant that calls itself, with an activation: the arrowheads and the activation bar touch the one lifeline
    // too, and only the step bar runs well past it on both sides
    private const string SelfCall = """
        @startuml
        !pragma teoz true
        skinparam shadowing false
        autonumber 1
        participant "Kitchen" as kitchen
        hnote across <<stepDelimiter>> #black:<color:white>When the kitchen checks its own oven
        kitchen -> kitchen : check the oven
        activate kitchen
        kitchen --> kitchen : hot enough
        deactivate kitchen
        @enduml
        """;

    [Fact]
    public async Task In_a_drawing_with_one_lifeline_only_the_step_bar_is_a_step_and_nothing_else_is_stretched()
    {
        await OpenAsync("RolesOneLifelineSelfCall.html", source: SelfCall);

        var r = await InSvgAsync<string[]>("""
            const roles = JSON.parse(el.getAttribute('data-k-roles'));
            const bands = Array.from(el.querySelectorAll('.k-step'));
            const label = Array.from(el.querySelectorAll('text')).find(t => t.textContent.includes('checks its own oven'));
            // an arrowhead or the activation bar taken for a strip would be stretched to the drawing's width
            const widest = Math.max(0, ...Array.from(el.querySelectorAll('polygon')).map(p => p.getBBox().width));
            const narrowSteps = bands.filter(b => b.getBBox().width < 40).length;
            return [String(roles.lifelines), String(bands.length), String(Math.round(widest)), String(narrowSteps),
                    label && label.classList.contains('k-step-text') ? 'tagged' : 'untagged'];
            """);

        Assert.Equal("1", r[0]);
        Assert.Equal("1", r[1]);
        Assert.True(int.Parse(r[2]) < 20, $"the widest arrowhead is {r[2]} wide");
        Assert.Equal("0", r[3]);
        Assert.Equal("tagged", r[4]);
    }

    [Fact]
    public async Task A_step_bar_is_a_light_band_with_dark_ink()
    {
        await OpenAsync("RolesBands.html");

        var r = await InSvgAsync<string[]>("""
            const band = el.querySelector('.k-step');
            const label = Array.from(el.querySelectorAll('text')).find(t => t.textContent.includes('shop is open'));
            return [band ? getComputedStyle(band).fill : 'none', __tok('--dg-step'),
                    label && label.classList.contains('k-step-text') ? getComputedStyle(label).fill : 'untagged', __tok('--dg-step-ink')];
            """);

        Assert.Equal(r[1], r[0]);
        Assert.Equal(r[3], r[2]);
    }

    [Fact]
    public async Task A_step_bars_table_sits_on_its_own_ground_with_a_shaded_header()
    {
        await OpenAsync("RolesTables.html");

        var r = await InSvgAsync<string[]>("""
            const table = el.querySelector('rect.k-step-table'), head = el.querySelector('rect.k-step-table-head');
            const name = Array.from(el.querySelectorAll('text')).find(t => t.textContent.trim() === 'name');
            return [table ? getComputedStyle(table).fill : 'none', __tok('--dg-step-table'),
                    head ? getComputedStyle(head).fill : 'none', __tok('--kron-strip'),
                    name && name.classList.contains('k-step-table-head-text') ? 'head' : 'body'];
            """);

        Assert.Equal(r[1], r[0]);
        Assert.Equal(r[3], r[2]);
        Assert.Equal("head", r[4]);
    }

    // The engine draws a strip across the participants, or as wide as its own text, centred on them; the note beside
    // the outer participant then stood out past every strip.
    [Fact]
    public async Task A_strip_runs_the_drawings_full_width_with_its_label_centred()
    {
        await OpenAsync("RolesStrips.html");

        var r = await InSvgAsync<double[]>("""
            const band = el.querySelector('.k-step'), b = band.getBBox();
            const label = Array.from(el.querySelectorAll('text')).find(t => t.textContent.includes('shop is open')).getBBox();
            // the rightmost card: the event note beside the outer participant, past the engine's strip
            const right = Math.max.apply(null, Array.from(el.querySelectorAll('.k-card')).map(c => { const n = c.getBBox(); return n.x + n.width; }));
            return [b.x, b.x + b.width, right, (label.x + label.width / 2) - (b.x + b.width / 2)];
            """);

        Assert.True(r[1] >= r[2] - 1, $"the band ends at {r[1]}, short of the note's right edge at {r[2]}");
        Assert.True(Math.Abs(r[3]) <= 1.5, $"the label is {r[3]} off the band's centre");
    }

    [Fact]
    public async Task A_note_is_drawn_as_a_card_and_an_event_note_as_an_event_card()
    {
        await OpenAsync("RolesCards.html");

        var r = await InSvgAsync<string[]>("""
            const cards = Array.from(el.querySelectorAll('.k-card'));
            const ev = el.querySelector('.k-card-event'), fold = el.querySelector('.k-fold');
            const plain = cards.find(c => !c.classList.contains('k-card-event'));
            return [String(cards.length), /A/.test(plain.getAttribute('d')) ? 'rounded' : 'square',
                    getComputedStyle(plain).fill, __tok('--dg-note'),
                    ev ? getComputedStyle(ev).fill : 'none', __tok('--dg-event'),
                    fold ? getComputedStyle(fold).visibility : 'none'];
            """);

        Assert.Equal("2", r[0]);
        Assert.Equal("rounded", r[1]);
        Assert.Equal(r[3], r[2]);
        Assert.Equal(r[5], r[4]);
        Assert.Equal("hidden", r[6]);
    }

    // A widened note carries two stereotypes (note<<kronNoteWide>><<eventNote>> right): read as one, the notes counted
    // from the source no longer matched the notes drawn, and every event card in the diagram lost its colour (F3).
    [Fact]
    public async Task A_widened_event_note_keeps_its_event_colour()
    {
        await OpenAsync("RolesWideEvent.html", NoteWidthMode.Full);

        Assert.Contains("<<kronNoteWide>><<eventNote>>", await Page.EvaluateAsync<string>(
            "() => document.querySelector('[data-plantuml]').getAttribute('data-plantuml')"));
        Assert.Equal(1, await Page.Locator("[data-diagram-type='plantuml'] svg .k-card-event").CountAsync());
    }

    // The fold stays for the report's note script, which finds a note by it: the card still takes the note's buttons.
    [Fact]
    public async Task A_card_keeps_its_note_buttons()
    {
        await OpenAsync("RolesCardButtons.html");

        await HoverNoteRect(0);
        await Page.WaitForFunctionAsync(
            "() => Array.from(document.querySelectorAll('.note-toggle-icon')).some(i => i.style.opacity === '1')", null,
            new() { Timeout = 5000, PollingInterval = 200 });
    }

    [Fact]
    public async Task The_dark_theme_gives_the_roles_their_dark_colours()
    {
        await OpenAsync("RolesDark.html");
        await Page.EvaluateAsync("() => document.documentElement.setAttribute('data-theme', 'dark')");

        var r = await InSvgAsync<string[]>("""
            const band = el.querySelector('.k-step');
            return [getComputedStyle(band).fill, __tok('--dg-step'), getComputedStyle(document.documentElement).getPropertyValue('--dg-step').trim()];
            """);

        Assert.Equal(r[1], r[0]);
        Assert.NotEqual("#e9eef6", r[2].ToLowerInvariant());
    }

    // Turning the step bars off and on again draws the diagram again: the new picture takes its roles too.
    [Fact]
    public async Task A_diagram_drawn_again_takes_its_roles_again()
    {
        await OpenAsync("RolesRedrawn.html");
        var steps = Page.Locator("button[data-toggle='steps']").First;

        // drawn again without the bars, and tagged; then with them, and tagged again
        await steps.ClickAsync();
        await WaitForSvgAsync("!s.querySelector('.k-step') && !!s.getAttribute('data-k-roles')");
        await steps.ClickAsync();
        await WaitForSvgAsync("!!s.querySelector('.k-step') && !!s.getAttribute('data-k-roles')");
    }

    private Task WaitForSvgAsync(string condition) => Page.WaitForFunctionAsync(
        "() => { const s = document.querySelector(\"[data-diagram-type='plantuml'] svg\"); return !!s && (" + condition + "); }",
        null, new() { Timeout = 15000, PollingInterval = 200 });
    // ── 5.9.1 (plans/REPORT_UI_FIXES_PLAN.md B4, B6, E10, E11): one table, one ground; a header told by its bold text ──

    // three bars from the emitter: two named tables, a header-only table and a doc string
    private static string TablesSource()
    {
        var two = Kronikol.PlantUml.StepBarPlantUml.Build("Given two tables",
            [new Kronikol.PlantUml.StepBarTable("inputs", [["a", "b"], ["1", "2"]]), new Kronikol.PlantUml.StepBarTable("outputs", [["c"], ["3"]])]);
        var headerOnly = Kronikol.PlantUml.StepBarPlantUml.Build("Given a header only", [new Kronikol.PlantUml.StepBarTable(null, [["only", "header"]])]);
        var doc = Kronikol.PlantUml.StepBarPlantUml.Build("When it is sent", docString: "{ \"muffin\": \"Blueberry\",\n  \"qty\": 2 }");
        return "@startuml\n!pragma teoz true\n" + Kronikol.PlantUml.StepBarPlantUml.BodyStyle + "\nactor \"Caller\" as caller\nparticipant \"OrderService\" as svc\n"
            + two + "\ncaller -> svc : POST /api/orders\n" + headerOnly + "\nsvc --> caller : 201 Created\n" + doc + "\ncaller -> svc : GET /api/orders\n@enduml";
    }

    // what the role pass drew in the bar whose text holds `label`: its grounds and header grounds, top to bottom, and the
    // name lines and texts in it, in screen px
    private const string GroundsOf = """
        const bars = [...el.querySelectorAll('.k-step')];
        const box = e => e.getBoundingClientRect();
        const text = [...el.querySelectorAll('text')].find(t => t.textContent.includes(label));
        const bar = bars.find(b => { const r = box(b), t = box(text); return t.top >= r.top - 1 && t.bottom <= r.bottom + 1; });
        const rb = box(bar);
        const inBar = e => { const r = box(e); return r.top >= rb.top - 1 && r.bottom <= rb.bottom + 1; };
        const grounds = [...el.querySelectorAll('.k-step-table')].filter(inBar).map(box).sort((a, c) => a.top - c.top);
        const heads = [...el.querySelectorAll('.k-step-table-head')].filter(inBar).map(box).sort((a, c) => a.top - c.top);
        const named = name => { const t = [...el.querySelectorAll('text')].find(t => t.textContent.trim() === name); return t ? box(t) : null; };
        return JSON.stringify({ grounds: grounds.map(g => [g.top, g.bottom, g.height]), heads: heads.map(h => [h.top, h.bottom, h.height]),
            outputs: named('outputs:') ? [named('outputs:').top, named('outputs:').bottom] : null });
        """;

    private async Task<System.Text.Json.JsonElement> GroundsAsync(string label)
    {
        var json = await InSvgAsync<string>("const label = " + System.Text.Json.JsonSerializer.Serialize(label) + ";" + GroundsOf);
        return System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
    }

    // B6, E10, E11: a bar holding two tables gets a ground and a shaded header for each, and the name line between them is
    // on neither; a header-only table's one row is shaded (until 5.9.1 one ground took in both tables and their name line,
    // only the first header was shaded, and a header-only table had none)
    [Fact]
    public async Task Two_tables_get_two_grounds_and_a_header_only_table_is_shaded()
    {
        await OpenAsync("RolesTwoTables.html", source: TablesSource());

        var two = await GroundsAsync("Given two tables");
        var grounds = two.GetProperty("grounds").EnumerateArray().Select(g => g.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
        Assert.Equal(2, grounds.Length);
        Assert.Equal(2, two.GetProperty("heads").GetArrayLength());
        var outputs = two.GetProperty("outputs").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        Assert.True(outputs[0] >= grounds[0][1] - 0.5 && outputs[1] <= grounds[1][0] + 0.5,
            $"the name line ({outputs[0]:F1} to {outputs[1]:F1}) is between the grounds, not on one ({grounds[0][1]:F1}, {grounds[1][0]:F1})");

        var only = await GroundsAsync("Given a header only");
        var ground = Assert.Single(only.GetProperty("grounds").EnumerateArray()).EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var head = Assert.Single(only.GetProperty("heads").EnumerateArray()).EnumerateArray().Select(v => v.GetDouble()).ToArray();
        Assert.True(Math.Abs(head[2] - ground[2]) <= 1, $"the header-only table's shading is {head[2]:F1} px of its {ground[2]:F1}");
    }

    // B4, B6 with B2: a doc string's one-cell box gets its ground and no header, and its lines stay where the engine drew
    // them: not centred, not re-weighted, the second still two spaces in (its rules alone read as a header-only table's)
    [Fact]
    public async Task A_doc_strings_box_gets_no_header_and_keeps_its_lines_in_place()
    {
        await OpenAsync("RolesDocBox.html", source: TablesSource());

        var doc = await GroundsAsync("When it is sent");
        Assert.Equal(1, doc.GetProperty("grounds").GetArrayLength());
        Assert.Equal(0, doc.GetProperty("heads").GetArrayLength());
        var r = await InSvgAsync<string>("""
            const ts = [...el.querySelectorAll('text')];
            const a = ts.find(t => t.textContent.includes('"muffin"')), b = ts.find(t => t.textContent.includes('"qty"'));
            // where a line's first visible character starts, in the picture's units: its indentation can be spaces inside
            // its text, which the text's box takes in
            const at = t => t.getStartPositionOfChar(Math.max(0, t.textContent.search(/[^\s\u200b]/))).x;
            return JSON.stringify({ head: [a, b].some(t => t.classList.contains('k-step-table-head-text')),
                indent: at(b) - at(a), char: b.getComputedTextLength() / b.textContent.length,
                weight: getComputedStyle(b).fontWeight });
            """);
        using var m = System.Text.Json.JsonDocument.Parse(r);
        Assert.False(m.RootElement.GetProperty("head").GetBoolean(), "a doc string line is no header");
        Assert.NotEqual("600", m.RootElement.GetProperty("weight").GetString());
        var indent = m.RootElement.GetProperty("indent").GetDouble();
        var ch = m.RootElement.GetProperty("char").GetDouble();
        Assert.True(indent >= 1.5 * ch && indent <= 2.5 * ch, $"the indented line starts {indent:F1} px in, two characters being {2 * ch:F1}");
    }
}
