using System.Text.RegularExpressions;
using Kronikol.ComponentDiagram;
using Kronikol.InternalFlow;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// PlantUML's message-statement parser refuses any statement longer than 2000 characters. It does not
/// say so: the statement matches no rule, the parser gives up on the whole diagram, and the engine draws
/// <c>Syntax Error?</c> — which is what a Redis <c>DELETE</c> of 41 cache keys produced, a 5,410-character
/// arrow label that took its whole fragment down with it.
/// <para>
/// The limit is per statement kind, not a global line limit: block openers (<c>loop</c>, <c>alt</c>) cap
/// lower, at ~1471, while <c>hnote across</c> bars, <c>note over</c> lines and note bodies are uncapped.
/// A backstop that trimmed those would be a gratuitous regression — a Gherkin step with a doc string is
/// legitimately long — so these tests pin what must be capped and what must be left alone.
/// </para>
/// </summary>
public class PlantUmlStatementLengthTests
{
    private const int MaxMessage = PlantUmlStatementLimits.MaxMessageStatementChars;
    private const int MaxBlock = PlantUmlStatementLimits.MaxBlockLabelChars;

    private static RequestResponseLog Request(string uri, string method = "GET", string? content = null,
        bool isUserAction = false, string? testId = "test-1") =>
        new(
            TestName: "My Test",
            TestId: testId!,
            Method: HttpMethod.Parse(method),
            Content: content,
            Uri: new Uri(uri),
            Headers: [],
            ServiceName: "OrderService",
            CallerName: "WebApp",
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false)
        {
            IsUserAction = isUserAction
        };

    private static RequestResponseLog UserAction(string label) =>
        new(
            TestName: "My Test",
            TestId: "test-1",
            Method: label,
            Content: null,
            Uri: new Uri("http://example.com/"),
            Headers: [],
            ServiceName: "Browser",
            CallerName: "User",
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false)
        {
            IsUserAction = true
        };

    private static RequestResponseLog Override(string plantUml) =>
        new(
            TestName: "My Test",
            TestId: "test-1",
            Method: "",
            Content: "",
            Uri: new Uri("http://override.com"),
            Headers: [],
            ServiceName: "",
            CallerName: "",
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false)
        {
            IsOverrideStart = true,
            PlantUml = $"\n{plantUml}\n\n"
        };

    private static RequestResponseLog OverrideEnd() =>
        new(
            TestName: "My Test",
            TestId: "test-1",
            Method: "",
            Content: "",
            Uri: new Uri("http://override.com"),
            Headers: [],
            ServiceName: "",
            CallerName: "",
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false)
        {
            IsOverrideEnd = true
        };

    private static string[] Diagrams(IEnumerable<RequestResponseLog> logs, bool internalFlowTracking = false) =>
        PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, internalFlowTracking: internalFlowTracking)
            .SelectMany(t => t.PlantUmls.Select(p => p.PlainText))
            .ToArray();

    private static string LongPath(int length) => "/data-insights-api/" + new string('k', length);

    /// <summary>Every physical line of the diagram, paired with the kind the engine's parser gives it.</summary>
    private static List<(PlantUmlStatementKind Kind, string Line)> Classify(string diagram) =>
        PlantUmlStatementGuard.ClassifyLines(diagram).ToList();

    // ── The limits, as constants ────────────────────────────────

    [Fact]
    public void The_measured_limits_are_recorded_as_constants()
    {
        Assert.Equal(2000, MaxMessage);
        Assert.Equal(600, MaxBlock);
        Assert.Equal(600, PlantUmlStatementLimits.MaxColouredNoteBarChars);
        Assert.Equal(16000, PlantUmlStatementLimits.MaxNoteLineChars);
        Assert.Equal(350, PlantUmlStatementLimits.MaxLinkedLabelChars);
        Assert.Equal(375, PlantUmlStatementLimits.MaxComponentEdgeLabelChars);
        Assert.Equal(200, PlantUmlStatementLimits.MaxParticipantNameChars);
        Assert.Equal(600, PlantUmlStatementLimits.MaxActivityActionChars);
    }

    // ── Layer 1: the request arrow ──────────────────────────────

    [Fact]
    public void A_five_thousand_character_url_does_not_produce_an_over_long_message_statement()
    {
        var diagram = Diagrams([Request($"http://example.com{LongPath(5000)}")]).Single();

        var messages = Classify(diagram).Where(l => l.Kind == PlantUmlStatementKind.Message).ToArray();
        Assert.NotEmpty(messages);
        Assert.All(messages, m => Assert.True(m.Line.Trim().Length <= MaxMessage,
            $"message statement is {m.Line.Trim().Length} chars: {m.Line.Trim()[..120]}…"));
    }

    [Fact]
    public void A_truncated_request_label_still_parses_as_a_message_and_ends_with_the_marker()
    {
        var diagram = Diagrams([Request($"http://example.com{LongPath(5000)}")]).Single();

        var message = Classify(diagram).First(l => l.Kind == PlantUmlStatementKind.Message).Line.Trim();
        Assert.StartsWith("webApp -", message);
        Assert.Contains("> orderService: GET: /data-insights-api/", message);
        Assert.EndsWith(PlantUmlStatementLimits.TruncationMarker, message);
    }

    [Fact]
    public void The_full_path_survives_in_the_request_note_when_the_label_is_truncated()
    {
        // For a DELETE with no body the path *is* the payload, so truncating the label without keeping it
        // anywhere would destroy the only record of what was called. Note bodies are uncapped.
        var path = LongPath(5000);
        var diagram = Diagrams([Request($"http://example.com{path}", method: "DELETE")]).Single();

        var noteBody = string.Concat(Classify(diagram)
            .Where(l => l.Kind == PlantUmlStatementKind.NoteBody)
            .Select(l => l.Line.Trim()));

        Assert.Contains("Full path", noteBody);
        // The path is chunked for wrapWidth, and each chunk boundary now carries the join marker, so
        // the comparison drops both. `noteBody` is already the lines concatenated, so the markers sit
        // inline rather than at line ends and RejoinMarkedLines has nothing to key on.
        Assert.Contains(path.Replace("\n", ""),
            noteBody.Replace(DiagramWidth.JoinMarker, "").Replace("\n", ""));
    }

    [Fact]
    public void A_short_request_label_is_left_exactly_as_it_was_and_gains_no_note()
    {
        var diagram = Diagrams([Request("http://example.com/api/orders")]).Single();

        Assert.Contains("> orderService: GET: /api/orders", diagram);
        Assert.DoesNotContain(PlantUmlStatementLimits.TruncationMarker, diagram);
        Assert.DoesNotContain("Full path", diagram);
    }

    [Fact]
    public void A_long_user_action_label_is_capped()
    {
        var diagram = Diagrams([UserAction("Click " + new string('x', 6000))]).Single();

        var messages = Classify(diagram).Where(l => l.Kind == PlantUmlStatementKind.Message).ToArray();
        Assert.NotEmpty(messages);
        Assert.All(messages, m => Assert.True(m.Line.Trim().Length <= MaxMessage, $"{m.Line.Trim().Length} chars"));
    }

    [Fact]
    public void The_cap_accounts_for_the_internal_flow_wrapper_and_the_graphql_suffix_together()
    {
        // This is where an off-by-one hides: the [[#iflow-{guid} …]] wrapper is ~45 characters and the
        // GraphQL suffix another `\n(query X)` — both added after the label is built.
        var query = "{\"query\":\"query GetInsights { insights { id } }\",\"operationName\":\"GetInsights\"}";
        var diagram = Diagrams([Request($"http://example.com{LongPath(5000)}", method: "POST", content: query)],
            internalFlowTracking: true).Single();

        var message = Classify(diagram).First(l => l.Kind == PlantUmlStatementKind.Message).Line.Trim();
        Assert.True(message.Length <= MaxMessage, $"{message.Length} chars");
        Assert.Contains("[[#iflow-", message);
        Assert.EndsWith("]]", message);
    }

    // ── The text inside the internal-flow link ──────────────────
    //
    // BrowserJs renders in a Chromium worker, whose stack is smaller than node's or the page's. There the
    // engine overflows it parsing a long `[[…]]` link and draws nothing of the diagram: from 980 characters
    // of link text in a worker whose JIT has not warmed up, and from 475 in one without the JIT at all
    // (plans/ENGINE_PIN_PLAN.md §10). The same label unlinked draws at every length up to the message limit.

    /// <summary>The request arrow's <c>[[#iflow-&lt;id&gt; text]]</c>, taken apart; fails when the link is not closed.</summary>
    private static (string Id, string Text) LinkedLabel(string diagram)
    {
        var message = Classify(diagram).First(l => l.Kind == PlantUmlStatementKind.Message).Line.Trim();
        var match = System.Text.RegularExpressions.Regex.Match(message, @"\[\[#iflow-(?<id>[0-9a-f-]+) (?<text>.*)\]\]$");
        Assert.True(match.Success, $"no closed internal-flow link: {message[..Math.Min(200, message.Length)]}");
        return (match.Groups["id"].Value, match.Groups["text"].Value);
    }

    private static string LongQuery(int parameters) =>
        "http://example.com/orders/search?" + string.Join("&", Enumerable.Range(0, parameters).Select(i => $"filter{i}=value{i}"));

    [Fact]
    public void The_text_inside_an_internal_flow_link_is_capped_at_the_linked_label_limit()
    {
        var request = Request(LongQuery(140));
        Assert.True(request.Uri.PathAndQuery.Length > 2300);

        var (id, text) = LinkedLabel(Diagrams([request], internalFlowTracking: true).Single());

        // The link keeps the request's own id, so the arrow still opens its internal flow.
        Assert.Equal(request.RequestResponseId.ToString(), id);
        Assert.True(text.Length <= PlantUmlStatementLimits.MaxLinkedLabelChars, $"{text.Length} characters inside the link");
        Assert.StartsWith("GET: /orders/search?filter0=value0", text);
        Assert.EndsWith(PlantUmlStatementLimits.TruncationMarker, text);
    }

    [Fact]
    public void A_linked_label_cut_at_the_limit_keeps_the_whole_path_in_its_note()
    {
        var path = LongPath(2300);
        var diagram = Diagrams([Request($"http://example.com{path}", method: "DELETE")], internalFlowTracking: true).Single();

        var noteBody = string.Concat(Classify(diagram)
            .Where(l => l.Kind == PlantUmlStatementKind.NoteBody)
            .Select(l => l.Line.Trim()));

        Assert.True(LinkedLabel(diagram).Text.Length <= PlantUmlStatementLimits.MaxLinkedLabelChars);
        Assert.Contains("Full path", noteBody);
        Assert.Contains(path, noteBody.Replace(DiagramWidth.JoinMarker, ""));
    }

    [Fact]
    public void A_linked_label_counts_its_escapes_and_is_never_cut_inside_one()
    {
        // `[` and `]` are written as code points (eight characters each) so the page can read the link; the cap
        // measures the label as written, and half of an escape would paint as text.
        var request = Request("http://example.com/articles?" + string.Join("&", Enumerable.Range(0, 200).Select(i => $"page[{i}]=~{i}")));

        var (_, text) = LinkedLabel(Diagrams([request], internalFlowTracking: true).Single());

        Assert.True(text.Length <= PlantUmlStatementLimits.MaxLinkedLabelChars, $"{text.Length} characters inside the link");
        Assert.Contains("<U+005B>", text);
        Assert.Equal(text.Count(c => c == '<'), System.Text.RegularExpressions.Regex.Matches(text, @"<U\+[0-9A-F]{4,6}>").Count);
    }

    [Fact]
    public void A_linked_label_that_fits_is_left_exactly_as_it_was()
    {
        var diagram = Diagrams([Request("http://example.com/api/orders?page=2")], internalFlowTracking: true).Single();

        Assert.Equal("GET: /api/orders?page=2", LinkedLabel(diagram).Text);
        Assert.DoesNotContain("Full path", diagram);
    }

    [Fact]
    public void Without_the_link_a_request_label_keeps_the_message_limit()
    {
        // The worker draws an unlinked sequence-diagram message at every length up to the message limit, so only the
        // linked form is cut shorter. (A component diagram's edge is another statement: the engine walks its label once
        // per character, linked or not; ComponentDiagramGeneratorTests holds it to its own cap.)
        var diagram = Diagrams([Request(LongQuery(140))]).Single();

        var message = Classify(diagram).First(l => l.Kind == PlantUmlStatementKind.Message).Line.Trim();
        Assert.DoesNotContain("[[#iflow-", message);
        Assert.True(message.Length > MaxMessage - 100, $"{message.Length} chars");
        Assert.True(message.Length <= MaxMessage, $"{message.Length} chars");
    }

    // ── What must NOT be capped ─────────────────────────────────

    [Fact]
    public void A_long_note_body_is_not_capped()
    {
        var body = "{\"blob\":\"" + string.Join(" ", Enumerable.Repeat("word", 3000)) + "\"}";
        var diagram = Diagrams([Request("http://example.com/api/orders", method: "POST", content: body)]).Single();

        Assert.DoesNotContain(PlantUmlStatementLimits.TruncationMarker, diagram);
        Assert.Contains("word word word", diagram);
    }

    [Fact]
    public void A_long_coloured_step_bar_is_capped_because_it_crashes_the_engine_outright()
    {
        // Measured: a coloured `hnote across` past ~1458 characters does not draw a syntax error — the
        // engine overflows its own JS stack (`RangeError: Maximum call stack size exceeded`) and produces
        // no SVG at all, so the scenario loses every diagram it had. The plain, uncoloured form runs to
        // ~16398, which is why only the coloured bar is capped.
        var bar = "hnote across <<stepDelimiter>> #black:<color:white>Given " + new string('s', 6000);
        var diagram = Diagrams([Override(bar), OverrideEnd(), Request("http://example.com/api/orders")]).Single();

        var emitted = Classify(diagram).Single(l => l.Kind == PlantUmlStatementKind.ColouredNoteBar).Line.Trim();
        Assert.True(emitted.Length <= PlantUmlStatementLimits.MaxColouredNoteBarChars, $"{emitted.Length} chars");
        Assert.StartsWith("hnote across <<stepDelimiter>> #black:<color:white>Given sss", emitted);
        Assert.EndsWith(PlantUmlStatementLimits.TruncationMarker, emitted);
    }

    [Fact]
    public void The_step_delimiter_emitters_cap_the_bar_they_build()
    {
        // A step with no whitespace in it — a JWT, a base64 blob, a GUID list — is now broken onto
        // display lines rather than drawn as one, which moves it from the coloured form to the styled
        // one. That is the whole point: the coloured bar had to be truncated at ~1400 characters
        // because past there the JS engine overflows its stack, and the survivor was still ONE line
        // about ten thousand pixels wide (measured 9450px), so the reader lost the step either way.
        // The styled form carries no inline colour tag, so it rides the far higher note-line ceiling.
        var fromIngest = Kronikol.Ingestion.InteractionRecord.StepDelimiterPlantUml("Given", new string('s', 6000));

        Assert.StartsWith(Kronikol.PlantUml.StepBarPlantUml.RichPrefix + "Given", fromIngest);
        Assert.True(fromIngest.Length <= PlantUmlStatementLimits.MaxNoteLineChars, $"{fromIngest.Length} chars");
        Assert.DoesNotContain("<color:white>", fromIngest);

        // Still one PHYSICAL line — the report's hide-steps strip regex and every line-oriented
        // consumer depend on that — with the breaks carried as \n escapes.
        Assert.DoesNotContain("\n", fromIngest);
        var displayLines = fromIngest[Kronikol.PlantUml.StepBarPlantUml.RichPrefix.Length..].Split(@"\n");
        Assert.True(displayLines.Length > 1, "a 6000-character unbroken step should be broken onto several display lines");
        Assert.All(displayLines, line => Assert.True(
            line.Length <= Kronikol.PlantUml.DiagramWidth.MaxNoteTextLineChars,
            $"display line of {line.Length} chars"));
    }

    [Fact]
    public void A_step_bar_that_already_fits_keeps_the_legacy_coloured_form()
    {
        // The flip above is only for steps that have to be broken. Anything that fits on one display
        // line stays byte-identical to what shipped before tables joined the bar.
        var bar = Kronikol.Ingestion.InteractionRecord.StepDelimiterPlantUml(
            "Given", "the order is placed with " + new string('s', 60));

        Assert.StartsWith(Kronikol.PlantUml.StepBarPlantUml.LegacyPrefix, bar);
        Assert.DoesNotContain(@"\n", bar);
    }

    [Fact]
    public void A_short_step_bar_is_left_exactly_as_it_was()
    {
        var bar = Kronikol.Ingestion.InteractionRecord.StepDelimiterPlantUml("Given", "the mock is armed");

        Assert.Equal("hnote across <<stepDelimiter>> #black:<color:white>Given the mock is armed", bar);
    }

    [Fact]
    public void An_uncoloured_hnote_across_bar_is_not_capped_at_the_coloured_limit()
    {
        var bar = "hnote across #black:" + new string('s', 6000);
        var diagram = Diagrams([Override(bar), OverrideEnd(), Request("http://example.com/api/orders")]).Single();

        Assert.Contains(bar, diagram);
    }

    [Fact]
    public void A_long_assertion_note_block_is_not_capped()
    {
        var body = new string('a', 6000);
        var note = $"hnote across <<assertionNote>> #00AA00\n✓ {body}\nend note";
        var diagram = Diagrams([Override(note), OverrideEnd(), Request("http://example.com/api/orders")]).Single();

        Assert.Contains(body, diagram);
    }

    // ── Layer 2: the DiagramBuilder backstop ────────────────────

    [Fact]
    public void The_backstop_caps_a_message_statement_no_call_site_capped()
    {
        var raw = "alice -> bob: " + new string('m', 6000);
        var diagram = Diagrams([Override(raw), OverrideEnd(), Request("http://example.com/api/orders")]).Single();

        var message = Classify(diagram).First(l => l.Line.TrimStart().StartsWith("alice ->", StringComparison.Ordinal)).Line.Trim();
        Assert.True(message.Length <= MaxMessage, $"{message.Length} chars");
        Assert.EndsWith(PlantUmlStatementLimits.TruncationMarker, message);
    }

    [Fact]
    public void The_backstop_caps_a_block_opener_at_the_lower_limit()
    {
        var raw = "loop " + new string('l', 6000);
        var diagram = Diagrams([Override(raw), OverrideEnd(), Request("http://example.com/api/orders")]).Single();

        var opener = Classify(diagram).First(l => l.Line.TrimStart().StartsWith("loop ", StringComparison.Ordinal)).Line.Trim();
        Assert.True(opener.Length <= MaxBlock, $"{opener.Length} chars");
    }

    [Fact]
    public void Truncating_a_statement_preserves_the_whitespace_that_surrounded_it()
    {
        // Diagram lines are written with CRLF, and the guard classifies them after splitting on the
        // line feed — so the carriage return has to survive the cut, along with any indentation.
        var capped = PlantUmlStatementLimits.TruncateStatement("    a -> b: " + new string('x', 6000) + "\r", 100);

        Assert.StartsWith("    a -> b: ", capped);
        Assert.EndsWith(PlantUmlStatementLimits.TruncationMarker + "\r", capped);
        Assert.Equal(100, capped.Trim().Length);
    }

    [Fact]
    public void The_backstop_never_leaves_a_dangling_escape_at_the_cut()
    {
        // `\n` inside a label is a two-character escape; cutting between them would emit a lone backslash.
        var raw = "alice -> bob: " + string.Concat(Enumerable.Repeat("ab\\n", 2000));
        var diagram = Diagrams([Override(raw), OverrideEnd(), Request("http://example.com/api/orders")]).Single();

        var message = Classify(diagram).First(l => l.Line.TrimStart().StartsWith("alice ->", StringComparison.Ordinal)).Line.Trim();
        var beforeMarker = message[..^PlantUmlStatementLimits.TruncationMarker.Length];
        Assert.False(beforeMarker.EndsWith('\\'), "the cut stranded a backslash from the character it escapes");
    }

    // ── Line classification ─────────────────────────────────────

    // The kind travels as a string: PlantUmlStatementKind is internal to the diagram generator, and an
    // internal parameter type would make this test method less accessible than its class.
    [Theory]
    [InlineData("a -> b: hello", "Message")]
    [InlineData("a --> b: hello", "Message")]
    [InlineData("a -[#F39C12]> b: hello", "Message")]
    [InlineData("a -[#F39C12]-> b: hello", "Message")]
    [InlineData("loop x3", "BlockOpener")]
    [InlineData("alt something", "BlockOpener")]
    [InlineData("partition Setup", "BlockOpener")]
    [InlineData("hnote across <<stepDelimiter>> #black:<color:white>Given a -> b: x", "ColouredNoteBar")]
    [InlineData("hnote across #black:Given a -> b: x", "Note")]
    [InlineData("note over a : text", "Note")]
    [InlineData("' a comment with a -> b: arrow", "Comment")]
    [InlineData("!$v = \"a -> b: x\"", "Directive")]
    [InlineData("@startuml", "Directive")]
    [InlineData("skinparam wrapWidth 800", "Other")]
    [InlineData("participant \"Order Service\" as os", "Other")]
    [InlineData("autonumber 3", "Other")]
    public void Lines_are_classified_the_way_the_engine_treats_them(string line, string expected)
    {
        var classified = PlantUmlStatementGuard.ClassifyLines(line).Single();
        Assert.Equal(expected, classified.Kind.ToString());
    }

    [Fact]
    public void Everything_between_a_note_opener_and_its_end_is_note_body()
    {
        var source = """
            note left
            a -> b: this is payload, not a statement
            end note
            """;

        var kinds = PlantUmlStatementGuard.ClassifyLines(source).Select(l => l.Kind).ToArray();
        Assert.Equal([PlantUmlStatementKind.Note, PlantUmlStatementKind.NoteBody, PlantUmlStatementKind.NoteBody], kinds);
    }

    [Fact]
    public void An_hnote_across_with_a_colour_and_a_body_opens_a_block()
    {
        var source = """
            hnote across <<assertionNote>> #00AA00
            a -> b: still note content
            end note
            """;

        var kinds = PlantUmlStatementGuard.ClassifyLines(source).Select(l => l.Kind).ToArray();
        Assert.Equal([PlantUmlStatementKind.Note, PlantUmlStatementKind.NoteBody, PlantUmlStatementKind.NoteBody], kinds);
    }

    // ── The render worker's caps (#162, plans/LONG_COMPONENT_EDGE_PLAN.md R3) ──
    //
    // The engine's regex library walks a declared name, an opener's label, a coloured bar's tag and an activity
    // action once per character on its stack, and the render worker's stack is half the page's: each of these drew
    // Syntax Error? or the engine's RangeError in the worker under caps that node had set, or with no cap at all.

    private static RequestResponseLog Named(string service, string caller = "WebApp", string? category = null) =>
        Request("http://example.com/api/orders") with { ServiceName = service, CallerName = caller, DependencyCategory = category };

    /// <summary>A host-shaped name with no whitespace, <paramref name="length"/> characters long.</summary>
    private static string HostName(int length, string start = "orders-archive-replica") =>
        string.Concat(Enumerable.Repeat(start + ".eu-west-1.internal.", length / 20 + 2))[..length];

    private static readonly Regex DeclarationRx = new(
        @"^(?<shape>participant|actor|entity|database|collections|queue|boundary|control|rectangle|hexagon) ""(?<name>.*)"" as (?<alias>[^\s<]+)",
        RegexOptions.Compiled);

    /// <summary>A declared name as the reader sees it: wrap breaks, bold markers and the stereotype line taken out.</summary>
    private static string ShownName(string written) =>
        Regex.Replace(written, @"\\n<size:10>\[[^\]]*\]</size>$", "").Replace("**", "", StringComparison.Ordinal).Replace("\\n", "", StringComparison.Ordinal);

    private static (string Name, string Alias)[] Declarations(string diagram) =>
        [.. diagram.Split('\n').Select(l => DeclarationRx.Match(l.Trim())).Where(m => m.Success).Select(m => (m.Groups["name"].Value, m.Groups["alias"].Value))];

    private static string ActivityDiagram(string spanName, string source = "Microsoft.EntityFrameworkCore")
    {
        var start = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
        FlowSpan[] spans =
        [
            new("trace1", "s1", null, "POST /orders", "OrderService", start, TimeSpan.FromMilliseconds(40)),
            new("trace1", "s2", "s1", spanName, source, start.AddMilliseconds(5), TimeSpan.FromMilliseconds(12)),
        ];
        var segment = new InternalFlowSegment(Guid.NewGuid(), RequestResponseType.Request, "t1", null, null, []) { FlowSpans = spans };
        return InternalFlowRenderer.RenderActivityDiagramBatched(segment).Single();
    }

    [Fact]
    public void A_long_service_name_is_cut_and_its_alias_with_it()
    {
        // The worker with V8's optimizing compilers off drew Syntax Error? from 280 characters of a participant's
        // name; the alias, derived from the name, is walked the same way in every arrow.
        var diagram = Diagrams([Named(HostName(420)), Named(HostName(420, "billing-ledger-primary"), category: "SQL")]).Single();

        var declared = Declarations(diagram).Where(d => !d.Name.StartsWith("WebApp", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, declared.Length);
        Assert.All(declared, d =>
        {
            Assert.True(ShownName(d.Name).Length <= PlantUmlStatementLimits.MaxParticipantNameChars, $"name of {ShownName(d.Name).Length}");
            Assert.EndsWith(PlantUmlStatementLimits.TruncationMarker, d.Name, StringComparison.Ordinal);
            Assert.True(d.Alias.Length <= PlantUmlStatementLimits.MaxParticipantNameChars, $"alias of {d.Alias.Length}");
            // The arrows name the participant by the same alias.
            Assert.Contains(Classify(diagram), l => l.Kind == PlantUmlStatementKind.Message && l.Line.Contains("> " + d.Alias + ":", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Two_long_names_that_share_their_start_stay_two_participants()
    {
        // Both are shown as the same cut name; their aliases take a hash of the whole name, so the two services keep
        // a lifeline each instead of merging into one.
        var common = HostName(320);
        var diagram = Diagrams([Named(common + "-orders"), Named(common + "-billing")]).Single();

        var declared = Declarations(diagram).Where(d => d.Name.StartsWith("orders-archive", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, declared.Length);
        Assert.Equal(declared[0].Name, declared[1].Name);
        Assert.NotEqual(declared[0].Alias, declared[1].Alias);
    }

    [Fact]
    public void A_name_that_fits_keeps_its_alias_as_it_was()
    {
        var diagram = Diagrams([Named("Order Service")]).Single();

        Assert.Contains(("Order Service", "orderService"), Declarations(diagram));
    }

    [Fact]
    public void A_long_component_name_is_cut_and_two_such_names_stay_two_nodes()
    {
        // Component diagram nodes: a <<system>> rectangle drew Syntax Error? from 310 characters of name and a
        // database from 340, in the worker with the optimizing compilers off.
        var common = HostName(320);
        ComponentRelationship[] relationships =
        [
            new("Caller", common + "-orders", "HTTP", ["GET /orders"], 3, 1),
            new("Caller", common + "-billing", "ClickHouse", ["INSERT INTO ledger"], 2, 1, "ClickHouse"),
        ];

        var source = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false);

        var declared = Declarations(source).Where(d => ShownName(d.Name).StartsWith("orders-archive", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, declared.Length);
        Assert.All(declared, d =>
        {
            Assert.True(ShownName(d.Name).Length <= PlantUmlStatementLimits.MaxParticipantNameChars, $"name of {ShownName(d.Name).Length}");
            Assert.True(d.Alias.Length <= PlantUmlStatementLimits.MaxParticipantNameChars, $"alias of {d.Alias.Length}");
            Assert.Contains(source.Split('\n'), l => l.Contains("> " + d.Alias + " : \"", StringComparison.Ordinal));
        });
        Assert.NotEqual(declared[0].Alias, declared[1].Alias);
    }

    private static readonly Regex C4DeclarationRx = new(@"^(?:Person|System)\((?<alias>[^,]+), ""(?<name>[^""]*)""", RegexOptions.Compiled);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_diff_diagram_cuts_a_long_name_and_its_alias_too(bool useC4)
    {
        // ComponentDiagramDiffer writes its own declarations, and had no cap on them (LONG_COMPONENT_EDGE_PLAN.md §8).
        var common = HostName(320);
        ComponentRelationship[] baseline = [new("Caller", common + "-orders", "HTTP", ["GET /orders"], 3, 1)];
        ComponentRelationship[] current = [.. baseline, new("Caller", common + "-billing", "HTTP", ["GET /ledger"], 2, 1)];

        var source = ComponentDiagramDiffer.GenerateDiffPlantUml(ComponentDiagramDiffer.Compare(baseline, current), useC4: useC4);

        var declarations = useC4
            ? [.. source.Split('\n').Select(l => C4DeclarationRx.Match(l.Trim())).Where(m => m.Success).Select(m => (Name: m.Groups["name"].Value, Alias: m.Groups["alias"].Value))]
            : Declarations(source);
        var declared = declarations.Where(d => ShownName(d.Name).StartsWith("orders-archive", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, declared.Length);
        Assert.All(declared, d =>
        {
            Assert.True(ShownName(d.Name).Length <= PlantUmlStatementLimits.MaxParticipantNameChars, $"name of {ShownName(d.Name).Length}");
            Assert.True(d.Alias.Length <= PlantUmlStatementLimits.MaxParticipantNameChars, $"alias of {d.Alias.Length}");
            Assert.True(Regex.Matches(source, Regex.Escape(d.Alias)).Count >= 2, "the edge names the declared alias");
        });
        Assert.NotEqual(declared[0].Alias, declared[1].Alias);
    }

    [Fact]
    public void A_span_named_after_a_whole_sql_statement_is_cut_to_the_action_cap()
    {
        // Instrumentations name a span after its SQL text. The activity diagram's action drew Syntax Error? from 820
        // characters in the worker with the optimizing compilers off.
        var sql = string.Concat(Enumerable.Repeat("SELECT o.Id, o.Total FROM Orders AS o WHERE o.CustomerId = @p0 ", 20));

        var action = ActivityDiagram(sql).Split('\n').Select(l => l.TrimEnd('\r')).Single(l => l.StartsWith(":SELECT", StringComparison.Ordinal));

        var text = action[1..action.LastIndexOf(" (", StringComparison.Ordinal)];
        Assert.True(text.Length <= PlantUmlStatementLimits.MaxActivityActionChars, $"{text.Length} characters");
        Assert.EndsWith(PlantUmlStatementLimits.TruncationMarker, text, StringComparison.Ordinal);
        Assert.EndsWith(" (12ms);", action, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_swimlane_name_is_cut_to_the_participant_cap()
    {
        // A swimlane is named after the span's source, and the engine walks it as it walks a participant's name:
        // Syntax Error? from 540 characters in the worker with the optimizing compilers off.
        var lane = ActivityDiagram("SELECT 1", HostName(700)).Split('\n').Select(l => l.TrimEnd('\r'))
            .Single(l => l.StartsWith("|orders-archive", StringComparison.Ordinal));

        Assert.True(lane[1..^1].Replace("\\n", "", StringComparison.Ordinal).Length <= PlantUmlStatementLimits.MaxParticipantNameChars, $"{lane.Length} characters");
    }

    [Fact]
    public void A_step_written_as_one_token_is_cut_to_the_coloured_bar_cap()
    {
        // A JSON array with no space in it: its quotes are written as escapes, which the wrap never splits, so the
        // step keeps the coloured form. The worker drew RangeError text in place of the diagram from 1,040
        // characters with the JIT on, the default, and from 880 with the optimizing compilers off.
        var token = "[" + string.Join(",", Enumerable.Range(1, 60).Select(i => $"{{\"sku\":\"SKU-{i:D4}\",\"qty\":{i % 7 + 1}}}")) + "]";

        var bar = Kronikol.Ingestion.InteractionRecord.StepDelimiterPlantUml(null, token);

        Assert.StartsWith(StepBarPlantUml.LegacyPrefix, bar, StringComparison.Ordinal);
        Assert.True(bar.Length <= PlantUmlStatementLimits.MaxColouredNoteBarChars, $"{bar.Length} characters");
        Assert.EndsWith(PlantUmlStatementLimits.TruncationMarker, bar, StringComparison.Ordinal);
    }

    [Fact]
    public void A_spliced_loop_label_is_cut_to_the_block_cap()
    {
        // A consumer's own PlantUML can open a block with any label: a `loop` opener drew Syntax Error? from 1,010
        // characters in a cold worker with the JIT on, the default, under the old cap of 1,471.
        var raw = "loop " + string.Concat(Enumerable.Repeat("retry the order sync ", 60))[..1195];
        var diagram = Diagrams([Override(raw + "\nwebApp -> orderService: again\nend"), OverrideEnd(), Request("http://example.com/api/orders")]).Single();

        var opener = Classify(diagram).Single(l => l.Kind == PlantUmlStatementKind.BlockOpener).Line.Trim();
        Assert.True(opener.Length <= MaxBlock, $"{opener.Length} chars");
        Assert.StartsWith("loop retry the order sync", opener, StringComparison.Ordinal);
        Assert.EndsWith(PlantUmlStatementLimits.TruncationMarker, opener, StringComparison.Ordinal);
    }

    [Fact]
    public void The_placeholder_note_is_held_to_the_note_ceiling()
    {
        // The note a scenario gets when its diagram fails drew at 15,900 characters in the worker with the optimizing
        // compilers off, so only the note ceiling holds it; an exception message can pass that.
        var source = DefaultDiagramsFetcher.RenderErrorPlantUml(new InvalidOperationException(new string('m', 20000)));

        var line = source.Split('\n').Single(l => l.Contains("could not be generated", StringComparison.Ordinal));
        Assert.True(line.Length <= PlantUmlStatementLimits.MaxNoteLineChars, $"{line.Length} characters");
        Assert.EndsWith(PlantUmlStatementLimits.TruncationMarker, line, StringComparison.Ordinal);
    }

    // ── Corpus invariant ────────────────────────────────────────

    /// <summary>A corpus that drives every field the emitters write to its longest: every message shape, names, steps.</summary>
    private static List<RequestResponseLog> LongestCorpus() =>
    [
        Request("http://example.com/api/orders"),
        Request($"http://example.com{LongPath(5000)}", method: "DELETE"),
        Request("http://example.com/api/orders?q=" + new string('q', 3000), method: "POST",
            content: "{\"payload\":\"" + new string('p', 4000) + "\"}"),
        UserAction("Click " + new string('u', 4000)),
        Override("hnote across <<stepDelimiter>> #black:<color:white>Given " + new string('g', 4000)),
        OverrideEnd(),
        Override("loop " + new string('l', 4000) + "\nwebApp -> orderService: again\nend"),
        OverrideEnd(),
        Override(Kronikol.Ingestion.InteractionRecord.StepDelimiterPlantUml(null, "[" + string.Join(",", Enumerable.Range(1, 200).Select(i => $"{{\"n\":{i}}}")) + "]")),
        OverrideEnd(),
        Named(HostName(900)),
        Named(HostName(900, "billing-ledger-primary"), caller: HostName(700, "checkout-frontend"), category: "SQL"),
        Named(HostName(900, "stock-events"), category: "MessageQueue") with { MetaType = RequestResponseMetaType.Event, Method = "Publish" },
        Request("http://example.com/api/orders/final"),
    ];

    /// <summary>The component diagrams the corpus's calls make, in both forms, each with every option that grows a line.</summary>
    private static IEnumerable<string> ComponentCorpus()
    {
        ComponentRelationship[] relationships =
        [
            new(HostName(900, "checkout-frontend"), HostName(900), "HTTP", [.. Enumerable.Range(0, 300).Select(i => $"GET /api/orders/{i}/" + new string('r', 60))], 300, 40),
            new("Caller", HostName(900, "billing-ledger-primary"), "ClickHouse", [.. Enumerable.Range(0, 300).Select(i => $"INSERT INTO ledger_{i:000}")], 300, 1, "ClickHouse"),
            new("Caller", "Broker", "MessageQueue", ["Produce \u2192 " + new string('t', 3000)], 1, 1, "MessageQueue"),
        ];
        var stats = relationships.ToDictionary(
            r => $"iflow-rel-{ComponentFlowSegmentBuilder.SanitizeKey(r.Caller)}-{ComponentFlowSegmentBuilder.SanitizeKey(r.Service)}",
            r => new RelationshipStats(r.CallCount, r.TestCount, 50, 45, 120, 999, 3, 1200, 0.12, [], [], null, null, false, 0.5, [], null, 10));
        foreach (var useC4 in new[] { false, true })
        {
            yield return ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: useC4);
            yield return ComponentDiagramGenerator.GeneratePlantUml(relationships, stats: stats, useC4: useC4);
            yield return ComponentDiagramGenerator.GeneratePlantUml(relationships, new ComponentDiagramOptions
            {
                Title = "Architecture " + new string('a', 3000),
                RelationshipLabelFormatter = r => string.Join(" ", Enumerable.Repeat(r.Service, 50)),
            }, useC4: useC4);
        }
    }

    private static IEnumerable<string> ActivityCorpus() =>
    [
        ActivityDiagram(string.Concat(Enumerable.Repeat("SELECT o.Id FROM Orders AS o WHERE o.CustomerId = @p0 ", 60))),
        ActivityDiagram(new string('s', 5000)),
        ActivityDiagram("SELECT 1", HostName(2000)),
    ];

    /// <summary>
    /// Every physical line of a component or activity diagram with the kind the worker's caps give it, and the length
    /// that kind is capped on: an edge's label, a declared name or alias, an action, a swimlane. A line no rule knows is
    /// <c>unknown</c>, which <see cref="Every_line_the_emitters_write_is_a_statement_kind_the_caps_know"/> refuses.
    /// </summary>
    private static IEnumerable<(string Kind, int Length, string Line)> WorkerKinds(string source)
    {
        var inBlock = false;
        foreach (var raw in source.Split('\n'))
        {
            var t = raw.Trim();
            Match m;
            if (inBlock)
            {
                inBlock = t != "}";
                yield return ("skinparam block", 0, t);
            }
            else if (t.Length == 0 || t is "@startuml" or "@enduml" or "left to right direction" || t.StartsWith("!include <C4/", StringComparison.Ordinal)
                     || t.StartsWith("!theme ", StringComparison.Ordinal) || Regex.IsMatch(t, @"^skinparam [\w<>.]+ [^{]+$"))
                yield return ("frame", 0, t);
            else if (Regex.IsMatch(t, @"^skinparam [\w<>.]+ \{$"))
            {
                inBlock = true;
                yield return ("skinparam block", 0, t);
            }
            else if (t.StartsWith("title ", StringComparison.Ordinal))
                yield return ("title", t.Length - 6, t);
            else if ((m = DeclarationRx.Match(t)).Success)
            {
                yield return ("name", ShownName(m.Groups["name"].Value).Length, t);
                yield return ("alias", m.Groups["alias"].Value.Length, t);
            }
            else if ((m = Regex.Match(t, @"^(?:Person|System|SystemDb|SystemQueue)\((?<alias>[^,]+), ""(?<name>.*)""\)$")).Success)
            {
                yield return ("name", ShownName(m.Groups["name"].Value).Length, t);
                yield return ("alias", m.Groups["alias"].Value.Length, t);
            }
            else if ((m = Regex.Match(t, @"^(?<a>\S+) (?:-\[#?\w+\]->|-->|\.\.>) (?<b>\S+) : ""(?<label>.*)""$")).Success
                     || (m = Regex.Match(t, @"^Rel\((?<a>[^,]+), (?<b>[^,]+), ""(?<label>.*?)""(?:, \$tags=""#?\w+"")?\)$")).Success)
                yield return ("edge label", m.Groups["label"].Length, t);
            else if ((m = Regex.Match(t, @"^\|(?<name>.*)\|$")).Success)
                yield return ("swimlane", m.Groups["name"].Value.Replace("\\n", "", StringComparison.Ordinal).Length, t);
            else if ((m = Regex.Match(t, @"^:(?<text>.*?)(?: \(\d+ms\))?;$")).Success)
                yield return ("action", m.Groups["text"].Length, t);
            else
                yield return ("unknown", t.Length, t);
        }
    }

    private static int? WorkerCap(string kind) => kind switch
    {
        "edge label" => PlantUmlStatementLimits.MaxComponentEdgeLabelChars,
        "name" or "alias" or "swimlane" => PlantUmlStatementLimits.MaxParticipantNameChars,
        "action" => PlantUmlStatementLimits.MaxActivityActionChars,
        _ => null,
    };

    [Fact]
    public void No_diagram_the_test_corpus_generates_exceeds_a_statement_limit()
    {
        foreach (var diagram in Diagrams(LongestCorpus(), internalFlowTracking: true))
        {
            foreach (var (kind, line) in PlantUmlStatementGuard.ClassifyLines(diagram))
            {
                var length = line.Trim().Length;
                if (kind == PlantUmlStatementKind.Message)
                    Assert.True(length <= MaxMessage, $"message statement is {length} chars");
                else if (kind == PlantUmlStatementKind.BlockOpener)
                    Assert.True(length <= MaxBlock, $"block label is {length} chars");
                else if (kind == PlantUmlStatementKind.ColouredNoteBar)
                    Assert.True(length <= PlantUmlStatementLimits.MaxColouredNoteBarChars, $"coloured note bar is {length} chars");
            }

            // Participants are declared in the diagram's prefix, which the guard does not see: the emitter caps them.
            var declared = Declarations(diagram);
            Assert.NotEmpty(declared);
            Assert.All(declared, d =>
            {
                Assert.True(ShownName(d.Name).Length <= PlantUmlStatementLimits.MaxParticipantNameChars, $"participant name of {ShownName(d.Name).Length}");
                Assert.True(d.Alias.Length <= PlantUmlStatementLimits.MaxParticipantNameChars, $"alias of {d.Alias.Length}");
            });
        }
    }

    [Fact]
    public void No_component_or_activity_diagram_the_corpus_generates_exceeds_a_worker_cap()
    {
        var seen = new HashSet<string>();
        foreach (var source in ComponentCorpus().Concat(ActivityCorpus()))
            foreach (var (kind, length, line) in WorkerKinds(source))
            {
                seen.Add(kind);
                if (WorkerCap(kind) is { } cap)
                    Assert.True(length <= cap, $"{kind} of {length} characters, past {cap}: {line[..Math.Min(100, line.Length)]}");
            }

        // The corpus reaches every capped kind, or the fact proves nothing about it.
        Assert.Superset(new HashSet<string> { "edge label", "name", "alias", "swimlane", "action" }, seen);
    }

    [Fact]
    public void Every_line_the_emitters_write_is_a_statement_kind_the_caps_know()
    {
        // A statement kind an emitter starts writing has to be measured in the worker and given a cap before it ships
        // (or be shown to need none): this fails on the first line of a kind the table above does not know.
        foreach (var source in ComponentCorpus().Concat(ActivityCorpus()))
            foreach (var (kind, _, line) in WorkerKinds(source))
                Assert.True(kind != "unknown", $"a line of no known kind: {line[..Math.Min(120, line.Length)]}");

        // The sequence diagram's prefix: every line the guard leaves as Other is a declaration, or one of the
        // directives and settings the emitter writes, none of which a run controls the length of.
        foreach (var diagram in Diagrams(LongestCorpus(), internalFlowTracking: true))
        {
            // Kronikol's own style sheets, between <style> and </style>: fixed text, nothing a run writes.
            var inStyle = false;
            foreach (var (kind, line) in PlantUmlStatementGuard.ClassifyLines(diagram))
            {
                var t = line.Trim();
                if (t == "<style>" || inStyle)
                {
                    inStyle = t != "</style>";
                    continue;
                }
                if (kind != PlantUmlStatementKind.Other || t.Length == 0 || DeclarationRx.IsMatch(t))
                    continue;
                Assert.True(KnownSequenceSetting.IsMatch(t), $"a sequence line of no known kind: {t[..Math.Min(120, t.Length)]}");
            }
        }
    }

    /// <summary>The settings and structure the sequence emitter writes around its statements.</summary>
    private static readonly Regex KnownSequenceSetting = new(
        @"^(?:skinparam [\w<>.]+(?: .*)?|autonumber \d+|hide (?:footbox|unlinked)|end)$",
        RegexOptions.Compiled);
}
