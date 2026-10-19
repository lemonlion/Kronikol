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

    private const string Source = """
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
        <style>
         .stepBody {
             BackgroundColor black
             FontColor white
             LineColor white
         }
        </style>
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

    private async Task OpenAsync(string fileName, NoteWidthMode noteWidth = NoteWidthMode.Default)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios = [new() { Id = "s1", DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode("s1", "", Source)],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: ResolvedToggleDefaults.BuiltIn with { NoteWidth = noteWidth });
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        await Page.GotoAsync(new Uri(path).AbsoluteUri);
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
}
