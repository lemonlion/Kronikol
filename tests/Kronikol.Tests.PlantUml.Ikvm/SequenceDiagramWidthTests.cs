using System.Diagnostics;
using System.Text.RegularExpressions;
using Kronikol.ComponentDiagram;
using Kronikol.Ingestion;
using Kronikol.InternalFlow;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml.Ikvm;

/// <summary>
/// Every remaining unbounded width axis in the diagrams Kronikol generates, measured against
/// <b>real</b> Java PlantUML — the engine <c>PlantUmlRendering.Local</c> renders through, and the one
/// a reader reaches when they copy a diagram's source out of the report and paste it into
/// plantuml.com. Asserting on the source cannot see this class of defect at all: the source is always
/// valid, and the failure is purely one of size.
/// <para>
/// 4096 is PlantUML's default <c>PLANTUML_LIMIT_SIZE</c>. It is a <b>raster</b> limit — measured, the
/// same source drew SVG at 24 185 px uncropped and PNG at exactly 4 096 — so it bites
/// <see cref="PlantUmlImageFormat.Png"/> rendering and a copied-out source, never the in-report
/// browser render (whose engine runs at <c>maxSvgSize: 98304</c>). It is used here as the width budget
/// every diagram family has to hold to, because a diagram that wants five thousand pixels is already
/// unreadable inside a report container — <c>svg { max-width: 100% }</c> scales it down — long before
/// it is cropped anywhere.
/// </para>
/// </summary>
public class SequenceDiagramWidthTests : IDisposable
{
    private const int PlantUmlLimitSize = 4096;

    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    /// <summary>The size real PlantUML draws <paramref name="plantUml"/> at, off the SVG viewBox.</summary>
    private static (int Width, int Height) DrawnSize(string plantUml)
    {
        var svg = System.Text.Encoding.UTF8.GetString(
            IkvmPlantUmlRenderer.Render(plantUml, PlantUmlImageFormat.Svg));
        Assert.DoesNotContain("Syntax Error", svg, StringComparison.Ordinal);
        var viewBox = Regex.Match(svg, @"viewBox=""0 0 (\d+) (\d+)""");
        Assert.True(viewBox.Success, "no viewBox in the rendered SVG");
        return (int.Parse(viewBox.Groups[1].Value), int.Parse(viewBox.Groups[2].Value));
    }

    private static void AssertFits(string plantUml, string what)
    {
        var (width, height) = DrawnSize(plantUml);
        Assert.True(width < PlantUmlLimitSize, $"{what} drew {width}px wide — PlantUML crops at {PlantUmlLimitSize}");
        Assert.True(height < PlantUmlLimitSize, $"{what} drew {height}px tall — PlantUML crops at {PlantUmlLimitSize}");
    }

    private static string MarkerDiagram(string markerPlantUml) =>
        $"@startuml\n!pragma teoz true\nskinparam wrapWidth 800\nparticipant \"A\" as a\nparticipant \"B\" as b\na -> b: go\n{markerPlantUml}\n@enduml";

    private static RequestResponseLog Log(
        RequestResponseType type,
        string serviceName = "OrderService",
        string callerName = "WebApp",
        string? content = null,
        string method = "GET",
        bool isUserAction = false) =>
        new(
            TestName: "Width test",
            TestId: "width-1",
            Method: InteractionRecord.ParseMethod(method),
            Content: content,
            Uri: new Uri("http://example.com/api/orders"),
            Headers: [],
            ServiceName: serviceName,
            CallerName: callerName,
            Type: type,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false)
        {
            IsUserAction = isUserAction
        };

    private static string[] Diagrams(params RequestResponseLog[] logs) =>
        [.. PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.Select(x => x.PlainText)];

    private static string Diagram(params RequestResponseLog[] logs) => Diagrams(logs)[0];

    // ── 1. The user-action message label ───────────────────────────────────
    // The one message label in the codebase that is neither chunked nor constant. Its text is a
    // Playwright locator chain or a UI adapter action string, routinely 150-400 characters, and it is
    // bounded only by the 2000-character parse cap: ~5.6px/char, crossing 4096 at ~730 characters.

    [Fact]
    public void A_very_long_user_action_label_still_fits()
    {
        var locator = "Click " + string.Join(" >> ", Enumerable.Range(0, 40)
            .Select(i => $"getByRole(button, name Submit order number {i})"));

        AssertFits(Diagram(Log(RequestResponseType.Request, isUserAction: true, method: locator)),
            "a user-action label");
    }

    [Fact]
    public void A_user_action_label_with_no_spaces_at_all_still_fits()
    {
        // A CSS selector chain has no whitespace for any wrap to break at.
        var locator = string.Concat(Enumerable.Range(0, 60).Select(i => $"div.panel-{i}+ul.items+li.nth-child-{i}"));

        AssertFits(Diagram(Log(RequestResponseType.Request, isUserAction: true, method: locator)),
            "an unbroken user-action label");
    }

    // ── 6. The user-action note body ───────────────────────────────────────
    // trace.Content bypasses FormatNoteContent, so it gets neither EscapeCreoleMarkup nor
    // WrapUnbreakableRuns — the protection every request/response note has.

    [Fact]
    public void A_user_action_note_body_with_an_unbroken_run_still_fits()
    {
        var detail = "Locator: " + new string('x', 2000);

        AssertFits(Diagram(Log(RequestResponseType.Request, isUserAction: true, method: "Click", content: detail)),
            "a user-action note body");
    }

    [Fact]
    public void A_user_action_note_body_of_many_words_still_fits()
    {
        var detail = "Locator: " + string.Join(" ", Enumerable.Range(0, 300).Select(i => $"segment-{i:D3}"));

        AssertFits(Diagram(Log(RequestResponseType.Request, isUserAction: true, method: "Click", content: detail)),
            "a wordy user-action note body");
    }

    [Fact]
    public void Creole_markup_in_a_user_action_note_body_is_drawn_as_written()
    {
        var plantUml = Diagram(Log(RequestResponseType.Request, isUserAction: true, method: "Click",
            content: "Locator: menu **bold** item"));

        var svg = System.Text.Encoding.UTF8.GetString(
            IkvmPlantUmlRenderer.Render(plantUml, PlantUmlImageFormat.Svg));

        // The stars are data, not emphasis: unescaped, creole eats them and bolds the word instead.
        Assert.Contains("**bold**", System.Net.WebUtility.HtmlDecode(svg), StringComparison.Ordinal);
    }

    // ── 7. Sequence-diagram participant declarations ───────────────────────
    // ServiceName / CallerName are emitted verbatim, the statement guard classifies them as Other so
    // there is no cap, and sequence participant boxes never wrap.

    [Theory]
    [InlineData("service")]
    [InlineData("caller")]
    public void A_participant_with_an_enormous_name_still_fits(string which)
    {
        var name = "Contoso.Reporting.Analytics." + new string('x', 700) + "Gateway";

        AssertFits(Diagram(
                Log(RequestResponseType.Request,
                    serviceName: which == "service" ? name : "OrderService",
                    callerName: which == "caller" ? name : "WebApp"),
                Log(RequestResponseType.Response,
                    serviceName: which == "service" ? name : "OrderService",
                    callerName: which == "caller" ? name : "WebApp")),
            $"a long {which} name");
    }

    // ── 7b. Participant COUNT ──────────────────────────────────────────────
    // Width accumulates ~144px per participant and there is no width or participant-count guard
    // anywhere — about 28 participants crop with perfectly ordinary short names.

    // Measured before the guard: 10 services drew 904px, 20 → 1687, 30 → 2469, 40 → 3252, 50 → 4035,
    // 60 → 4817, 80 → 6382 — about 78px per participant with names as short as "Service37", and all in
    // ONE diagram, because the only split guards were encoded length and estimated HEIGHT.
    [Theory]
    [InlineData(40)]
    [InlineData(60)]
    [InlineData(120)]
    public void A_test_that_touches_many_services_still_fits(int serviceCount)
    {
        var logs = Enumerable.Range(0, serviceCount)
            .SelectMany(i => new[]
            {
                Log(RequestResponseType.Request, serviceName: $"Service{i:D2}"),
                Log(RequestResponseType.Response, serviceName: $"Service{i:D2}"),
            })
            .ToArray();

        var diagrams = Diagrams(logs);

        Assert.All(diagrams, d => AssertFits(d, $"one of {diagrams.Length} fragments for {serviceCount} services"));
    }

    [Fact]
    public void A_split_fragment_declares_only_the_participants_it_draws()
    {
        // Splitting is no width bound on its own while every fragment re-declares every participant
        // the whole test touched — the prefix is rebuilt from the full trace list.
        var logs = Enumerable.Range(0, 60)
            .SelectMany(i => new[]
            {
                Log(RequestResponseType.Request, serviceName: $"Service{i:D2}"),
                Log(RequestResponseType.Response, serviceName: $"Service{i:D2}"),
            })
            .ToArray();

        var diagrams = Diagrams(logs);

        Assert.True(diagrams.Length > 1, "60 services should not be drawn as one diagram");

        static string[] Declared(string diagram) =>
        [
            .. Regex.Matches(diagram, @"^(?:actor|entity|participant|database|collections|queue|control) ""[^""]*"" as (\w+)",
                    RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value)
        ];

        // Every fragment declares a strict subset — before this, each one re-declared all 61.
        Assert.All(diagrams, d => Assert.True(Declared(d).Length < 61,
            $"a fragment declared {Declared(d).Length} participants of the 61 the test touches"));

        // And specifically: a service only the FIRST fragment calls is not drawn into the last one.
        Assert.Contains("service00", Declared(diagrams[0]));
        Assert.DoesNotContain("service00", Declared(diagrams[^1]));

        // The caller is in every fragment, because every fragment draws an arrow from it.
        Assert.All(diagrams, d => Assert.Contains("webApp", Declared(d)));
    }

    [Fact]
    public void A_client_side_split_report_gets_the_same_participant_bound()
    {
        // BrowserJs sets clientSideSplitting, which switches the encoded-length and height guards off
        // and lets the browser re-split after every note toggle. Participant count is not affected by
        // note state, so that guard has to apply on both paths or the default renderer keeps the axis.
        var logs = Enumerable.Range(0, 120)
            .SelectMany(i => new[]
            {
                Log(RequestResponseType.Request, serviceName: $"Service{i:D2}"),
                Log(RequestResponseType.Response, serviceName: $"Service{i:D2}"),
            })
            .ToArray();

        var diagrams = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, clientSideSplitting: true)
            .Single().PlantUmls.Select(x => x.PlainText).ToArray();

        Assert.All(diagrams, d => AssertFits(d, $"one of {diagrams.Length} client-split fragments"));
    }

    // ── 2. Internal-flow activity diagrams ─────────────────────────────────
    // The only diagram family with no character cap at all and no wrapWidth.

    private InternalFlowSegment Segment(string sourceName, params string[] displayNames)
    {
        var source = new ActivitySource(sourceName);
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        _disposables.Add(listener);
        _disposables.Add(source);

        var spans = new List<Activity>();
        foreach (var name in displayNames)
        {
            var span = source.StartActivity("op")!;
            span.DisplayName = name;
            span.SetEndTime(span.StartTimeUtc + TimeSpan.FromMilliseconds(5));
            _disposables.Add(span);
            spans.Add(span);
        }

        return new InternalFlowSegment(Guid.NewGuid(), RequestResponseType.Request, "width-1",
            spans[0].StartTimeUtc, spans[^1].StartTimeUtc + spans[^1].Duration, [.. spans]);
    }

    [Fact]
    public void A_long_activity_node_label_still_fits()
    {
        var sql = "SELECT " + string.Join(", ", Enumerable.Range(0, 60)
            .Select(i => $"o.CustomerReferenceNumber{i:D2}")) + " FROM Orders AS o";

        AssertFits(InternalFlowRenderer.RenderActivityDiagram(Segment("EntityFrameworkCore", sql)),
            "an activity node label");
    }

    [Fact]
    public void An_activity_node_label_with_no_spaces_still_fits()
    {
        AssertFits(InternalFlowRenderer.RenderActivityDiagram(Segment("EntityFrameworkCore", new string('x', 1500))),
            "an unbroken activity node label");
    }

    [Fact]
    public void A_long_activity_swimlane_name_still_fits()
    {
        AssertFits(
            InternalFlowRenderer.RenderActivityDiagram(
                Segment("Contoso.Data." + new string('y', 800) + ".Provider", "Query")),
            "an activity swimlane name");
    }

    [Fact]
    public void A_batched_activity_diagram_keeps_the_same_width_bound()
    {
        // An EF-shaped DisplayName, at the ~197 characters one really runs to, across enough spans to
        // batch. Height is the axis this fixture deliberately stays clear of: an activity diagram
        // has no height guard at all (~57px per node), and the batcher splits on span count, not
        // height. AssertFits still measures height, because the SVG viewBox is the crop proxy for
        // every axis — but 4096 is raster-only and these diagrams always render client-side, so past
        // about seventy nodes it is a PNG render or a copied-out source that suffers, never the
        // report. A separate, pre-existing axis, recorded in DIAGRAM_WIDTH_PLAN.md rather than
        // fixed here.
        var sql = "SELECT " + string.Join(", ", Enumerable.Range(0, 7)
            .Select(i => $"o.CustomerReferenceNumber{i:D2}")) + " FROM Orders AS o WHERE o.Status = @p0";
        var names = Enumerable.Range(0, 40).Select(_ => sql).ToArray();

        var diagrams = InternalFlowRenderer.RenderActivityDiagramBatched(Segment("EntityFrameworkCore", names));

        Assert.All(diagrams, d => AssertFits(d, "a batched activity diagram"));
    }

    // ── 3 & 4. Step-delimiter bars ─────────────────────────────────────────

    [Fact]
    public void A_step_bar_quoting_an_unbroken_token_still_fits()
    {
        var jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." + new string('Q', 900) + ".signature";

        AssertFits(MarkerDiagram(StepBarPlantUml.Build($"Given the token {jwt} is presented")),
            "a step bar quoting a token");
    }

    [Fact]
    public void A_step_bar_table_cell_holding_a_payload_still_fits()
    {
        var cell = "{\"orderId\":\"" + new string('7', 700) + "\"}";
        var bar = StepBarPlantUml.Build("Given the order is placed",
            [new StepBarTable("Request", [["field", "value"], ["payload", cell]])]);

        AssertFits(MarkerDiagram(bar), "a step bar table cell");
    }

    [Fact]
    public void A_step_bar_table_row_of_many_cells_still_fits()
    {
        // A row is the sum of its cells: creole table cells never wrap, not even at spaces.
        var header = Enumerable.Range(0, 12).Select(i => $"Column heading number {i:D2}").ToArray();
        var row = Enumerable.Range(0, 12).Select(i => $"a fairly wordy value for column {i:D2}").ToArray();
        var bar = StepBarPlantUml.Build("Given the grid is loaded", [new StepBarTable(null, [header, row])]);

        AssertFits(MarkerDiagram(bar), "a wide step bar table row");
    }

    [Fact]
    public void A_step_bar_doc_string_line_still_fits()
    {
        var docString = "{\n  \"token\": \"" + new string('k', 900) + "\"\n}";
        var bar = StepBarPlantUml.Build("Given the payload", docString: docString);

        AssertFits(MarkerDiagram(bar), "a step bar doc string");
    }

    // ── 5. Assertion notes ─────────────────────────────────────────────────
    // The body never passes through FormatNoteContent, so it misses WrapUnbreakableRuns.

    [Fact]
    public void An_assertion_note_quoting_an_unbroken_run_still_fits()
    {
        var connectionString = "Server=" + new string('z', 1200) + ";Database=Orders";
        var note = InteractionRecord.AssertionNotePlantUml(
            "the connection string matches", passed: false, message: connectionString);

        AssertFits(MarkerDiagram(note), "an assertion note");
    }

    [Fact]
    public void An_assertion_note_with_a_very_long_message_still_fits()
    {
        var message = string.Join(" ", Enumerable.Range(0, 220).Select(i => $"expected-token-{i:D3}"));
        var note = InteractionRecord.AssertionNotePlantUml("the tokens match", passed: false, message: message);

        AssertFits(MarkerDiagram(note), "a wordy assertion note");
    }

    [Fact]
    public void An_assertion_note_with_an_unbroken_expression_still_fits()
    {
        var note = InteractionRecord.AssertionNotePlantUml(
            "value_" + new string('v', 1200) + "_matches", passed: true, message: null);

        AssertFits(MarkerDiagram(note), "an assertion note expression");
    }

    // ── 8. Component diagram title ─────────────────────────────────────────

    [Fact]
    public void A_very_long_component_diagram_title_still_fits()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "Api", "HTTP", ["GET"], 5, 3, null),
        };
        var options = new ComponentDiagramOptions
        {
            Title = "Architecture overview for " + string.Join(" ", Enumerable.Range(0, 80).Select(i => $"subsystem-{i:D2}")),
        };

        AssertFits(ComponentDiagramGenerator.GeneratePlantUml(relationships, options, useC4: false),
            "a component diagram title");
    }

    [Fact]
    public void A_component_diagram_title_with_no_spaces_still_fits()
    {
        var relationships = new[]
        {
            new ComponentRelationship("Caller", "Api", "HTTP", ["GET"], 5, 3, null),
        };
        var options = new ComponentDiagramOptions { Title = new string('T', 900) };

        AssertFits(ComponentDiagramGenerator.GeneratePlantUml(relationships, options, useC4: false),
            "an unbroken component diagram title");
    }
}
