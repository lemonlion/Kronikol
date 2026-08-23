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
        Assert.Equal(1471, MaxBlock);
        Assert.Equal(1400, PlantUmlStatementLimits.MaxColouredNoteBarChars);
        Assert.Equal(16000, PlantUmlStatementLimits.MaxNoteLineChars);
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
        // The path is chunked for wrapWidth, so compare after removing the chunk boundaries.
        Assert.Contains(path.Replace("\n", ""), noteBody.Replace("\n", ""));
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
        var fromIngest = Kronikol.Ingestion.InteractionRecord.StepDelimiterPlantUml("Given", new string('s', 6000));

        Assert.True(fromIngest.Length <= PlantUmlStatementLimits.MaxColouredNoteBarChars, $"{fromIngest.Length} chars");
        Assert.StartsWith("hnote across <<stepDelimiter>> #black:<color:white>Given sss", fromIngest);
        Assert.EndsWith(PlantUmlStatementLimits.TruncationMarker, fromIngest);
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

    // ── Corpus invariant ────────────────────────────────────────

    [Fact]
    public void No_diagram_the_test_corpus_generates_exceeds_a_statement_limit()
    {
        var logs = new List<RequestResponseLog>
        {
            Request("http://example.com/api/orders"),
            Request($"http://example.com{LongPath(5000)}", method: "DELETE"),
            Request("http://example.com/api/orders?q=" + new string('q', 3000), method: "POST",
                content: "{\"payload\":\"" + new string('p', 4000) + "\"}"),
            UserAction("Click " + new string('u', 4000)),
            Override("hnote across <<stepDelimiter>> #black:<color:white>Given " + new string('g', 4000)),
            OverrideEnd(),
            Request("http://example.com/api/orders/final"),
        };

        foreach (var diagram in Diagrams(logs, internalFlowTracking: true))
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
        }
    }
}
