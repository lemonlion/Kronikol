using System.Diagnostics;
using System.Net;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml;

public class NodeJsPlantUmlRendererTests
{
    internal static bool IsNodeAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("node", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Renders_sequence_diagram_svg()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        var plantUml = """
            @startuml
            Alice -> Bob : Hello
            @enduml
            """;

        var svgBytes = NodeJsPlantUmlRenderer.Render(plantUml, PlantUmlImageFormat.Svg);
        var svg = System.Text.Encoding.UTF8.GetString(svgBytes);

        Assert.Contains("<svg", svg);
        Assert.Contains("Alice", svg);
        Assert.Contains("Bob", svg);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Non_ascii_text_survives_the_round_trip_to_node()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // The loop fragment label Kronikol emits for collapsed calls, plus an accented participant:
        // stdin must be UTF-8 or these come out as `x`, `�` and `?` on Windows (cp1252 console page).
        var plantUml = """
            @startuml
            participant "Zoë" as z
            loop ×2 · 27–43 ms
            z -> Bob : généré
            end
            @enduml
            """;

        var svg = System.Text.Encoding.UTF8.GetString(NodeJsPlantUmlRenderer.Render(plantUml, PlantUmlImageFormat.Svg));

        Assert.Contains("<svg", svg);
        Assert.Contains("×2", svg);
        Assert.Contains("27–43", svg);
        Assert.Contains("Zoë", svg);
        Assert.Contains("généré", svg);
        Assert.DoesNotContain("�", svg);
    }

    // ═══════════════════════════════════════════════════════════
    // Batch mode (one node process per report) + V8 code cache
    // ═══════════════════════════════════════════════════════════

    internal static string Seq(string a, string b) => $"@startuml\nparticipant {a}\nparticipant {b}\n{a} -> {b} : hello from {a}\n@enduml";

    [Fact]
    [Trait("Category", "Integration")]
    public void Batch_returns_one_svg_per_input_in_input_order()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        var results = NodeJsPlantUmlRenderer.RenderMany([Seq("Alpha", "One"), Seq("Beta", "Two"), Seq("Gamma", "Three")]);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Succeeded, r.Error));
        Assert.Contains("Alpha", results[0].Svg);
        Assert.Contains("Beta", results[1].Svg);
        Assert.Contains("Gamma", results[2].Svg);
        Assert.DoesNotContain("Beta", results[0].Svg);
        Assert.Empty(NodeJsPlantUmlRenderer.RenderMany([]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Batch_isolates_an_engine_failure_to_its_own_diagram()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // One unbreakable 60,000-character note line is wider than the engine's canvas: it answers with
        // "Diagram too large for browser rendering…" as text. That must become this diagram's error only.
        var tooLarge = "@startuml\nA -> B : x\nnote right\n" + new string('x', 60000) + "\nend note\n@enduml";
        var results = NodeJsPlantUmlRenderer.RenderMany([Seq("First", "One"), tooLarge, Seq("Third", "Three")]);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].Succeeded, results[0].Error);
        Assert.False(results[1].Succeeded);
        Assert.Contains("too large", results[1].Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(results[2].Succeeded, results[2].Error);
        Assert.Contains("Third", results[2].Svg);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Batch_of_five_is_faster_than_five_single_spawns()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        var sources = Enumerable.Range(0, 5).Select(i => Seq("P" + i, "Q" + i)).ToList();
        NodeJsPlantUmlRenderer.Render(sources[0], PlantUmlImageFormat.Svg); // warm: engine download, code cache

        // Measured 1.9 s vs 5.1 s (node start + engine compile + warm-up paid once instead of five times); the
        // bound is loose so a loaded box does not fail it. Both halves are wall-clock on a shared machine, so a
        // load spike during one of them can still flip a single measurement (seen once, with a Chromium render
        // probe running beside the suite). A real regression loses every attempt and a spike does not repeat,
        // so the first of up to three attempts that shows the batch ahead passes.
        var attempts = new List<string>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var single = Stopwatch.StartNew();
            foreach (var s in sources) NodeJsPlantUmlRenderer.Render(s, PlantUmlImageFormat.Svg);
            single.Stop();

            var batch = Stopwatch.StartNew();
            var results = NodeJsPlantUmlRenderer.RenderMany(sources);
            batch.Stop();

            Assert.All(results, r => Assert.True(r.Succeeded, r.Error));
            if (batch.ElapsedMilliseconds * 1.3 < single.ElapsedMilliseconds)
                return;
            attempts.Add($"batch of 5 took {batch.ElapsedMilliseconds} ms, five single spawns {single.ElapsedMilliseconds} ms");
        }

        Assert.Fail("The batch was not faster in any of three attempts: " + string.Join("; ", attempts));
    }

    // ── The engine's script loader (DIAGRAM_COLOURS_PLAN S3a) ─────────────────────────────────────
    //
    // The engine loads four bundles by appending a <script> to document.head: themes.js (`!theme`), a
    // stdlib module (`!include <…>`), openiconic.js (`<&icon>`) and emoji.js (`<:emoji:>`). The Node
    // host can load none of them. Until 3.29.6 its mock head answered nothing, so each such diagram
    // waited for the 20 s poll, and in a batch every diagram after it did too.

    private static string WithoutProcessingInstructions(string svg) =>
        System.Text.RegularExpressions.Regex.Replace(svg, @"<\?[\s\S]*?\?>", "");

    [Fact]
    [Trait("Category", "Integration")]
    public void A_themed_source_renders_unthemed_instead_of_timing_out()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");
        NodeJsPlantUmlRenderer.Render(Seq("Warm", "Up"), PlantUmlImageFormat.Svg); // engine download, code cache

        var plain = "@startuml\nAlice -> Bob : hello\n@enduml";
        var themed = "@startuml\n!theme cerulean\nAlice -> Bob : hello\n@enduml";

        // The defect this guards waited for the 20 s poll on the themed diagram, and on the plain one after it in
        // the batch too, so 10 s separates the two. It is wall-clock on a shared machine: a full suite beside another
        // session's suites once took 16 s. A real regression loses every attempt and a spike does not repeat, so the
        // first of up to three attempts under the bound passes.
        var attempts = new List<long>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var watch = Stopwatch.StartNew();
            var results = NodeJsPlantUmlRenderer.RenderMany([themed, plain]);
            watch.Stop();

            Assert.All(results, r => Assert.True(r.Succeeded, r.Error));
            // The theme bundle cannot load here, so the engine draws the diagram without it, exactly as the
            // unthemed source draws.
            Assert.Equal(WithoutProcessingInstructions(results[1].Svg!), WithoutProcessingInstructions(results[0].Svg!));
            if (watch.ElapsedMilliseconds < 10_000)
                return;
            attempts.Add(watch.ElapsedMilliseconds);
        }

        Assert.Fail($"Two diagrams took {string.Join(", ", attempts)} ms in three attempts, as if the themed one waited for the theme bundle.");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void A_stdlib_include_draws_the_engines_own_picture_instead_of_timing_out()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // ComponentDiagramReportGenerator avoids the C4 flavour under the JS engines for this reason. The
        // stdlib content is not in the engine build, so what it draws is its own error picture: the
        // point is that it answers.
        var watch = Stopwatch.StartNew();
        var result = NodeJsPlantUmlRenderer.RenderMany(["@startuml\n!include <C4/C4_Context>\nPerson(u, \"User\")\n@enduml"])[0];
        watch.Stop();

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("<svg", result.Svg);
        Assert.True(watch.ElapsedMilliseconds < 10_000, $"the include took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void A_diagram_that_asks_for_a_bundle_fails_alone_and_the_batch_after_it_still_draws()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");
        NodeJsPlantUmlRenderer.Render(Seq("Warm", "Up"), PlantUmlImageFormat.Svg);

        // A user's own markup asking for an OpenIconic icon. Before 3.29.6 all three timed out, about 70 s.
        var icon = "@startuml\nAlice -> Bob : <&check> done\n@enduml";

        var watch = Stopwatch.StartNew();
        var results = NodeJsPlantUmlRenderer.RenderMany([icon, Seq("Second", "Two"), Seq("Third", "Three")]);
        watch.Stop();

        Assert.False(results[0].Succeeded);
        Assert.Contains("Failed to load openiconic.js", results[0].Error);
        Assert.True(results[1].Succeeded, results[1].Error);
        Assert.Contains("Second", results[1].Svg);
        Assert.True(results[2].Succeeded, results[2].Error);
        Assert.Contains("Third", results[2].Svg);
        Assert.True(watch.ElapsedMilliseconds < 10_000, $"three diagrams took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Loader_markup_copied_in_from_payloads_and_tests_is_drawn_as_written()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");
        NodeJsPlantUmlRenderer.Render(Seq("Warm", "Up"), PlantUmlImageFormat.Svg);

        // Every form the emitter writes captured text into, built by the emitter itself: a response body,
        // a request body, both step-bar forms (LightBDD's <$name>), a test delimiter and an assertion note.
        var diagram = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
        [
            MakeLog(RequestResponseType.Request, """{"launch":"<:rocket:>"}"""),
            MakeLog(RequestResponseType.Response, """{"error":"expected Vec<&str>, found String","tpl":"a <$foo> b"}"""),
        ]).Single().PlantUmls.First().PlainText;
        var markers = string.Join("\n",
            StepBarPlantUml.Build("Given I have data [inputs: \"<$inputs>\"]"),
            StepBarPlantUml.Build("Given I have data [inputs: \"<$inputs>\"]", [new StepBarTable(null, [["a"], ["1"]])]),
            "hnote across #black:<color:white>Test " + PlantUmlCreator.EscapeLoaderMarkup("Returns Vec<&str>"),
            Kronikol.Ingestion.InteractionRecord.AssertionNotePlantUml("Parses Vec<&str>", passed: false, "got <:rocket:>"));
        var withMarkers = diagram.Replace("@enduml", "<style>\n .stepBody {\n BackgroundColor black\n FontColor white\n }\n</style>\n" + markers + "\n@enduml");

        var watch = Stopwatch.StartNew();
        var results = NodeJsPlantUmlRenderer.RenderMany([diagram, withMarkers]);
        watch.Stop();

        Assert.All(results, r => Assert.True(r.Succeeded, r.Error));
        var drawn = System.Text.RegularExpressions.Regex.Replace(string.Join(" ", System.Text.RegularExpressions.Regex
            .Matches(results[1].Svg!, @"<text\b[^>]*>([\s\S]*?)</text>")
            .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value))), @"\s+", " ");
        Assert.Contains("Vec<&str>,", drawn);
        Assert.Contains("<:rocket:>", drawn);
        Assert.Contains("<$foo>", drawn);
        Assert.Contains("[inputs: \"<$inputs>\"]", drawn);
        Assert.Contains("Test Returns Vec<&str>", drawn);
        Assert.Contains("got <:rocket:>", drawn);
        Assert.True(watch.ElapsedMilliseconds < 10_000, $"two diagrams took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Renders_class_diagram_svg()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        var plantUml = """
            @startuml
            class Foo {
              +bar(): void
            }
            class Bar
            Foo --> Bar
            @enduml
            """;

        var svgBytes = NodeJsPlantUmlRenderer.Render(plantUml, PlantUmlImageFormat.Svg);
        var svg = System.Text.Encoding.UTF8.GetString(svgBytes);

        Assert.Contains("<svg", svg);
        Assert.Contains("Foo", svg);
        Assert.Contains("Bar", svg);
    }
    [Fact]
    [Trait("Category", "Integration")]
    public void Creole_markup_in_a_captured_body_reaches_the_svg_as_text()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // A BigQuery job body: the query is one PlantUML line, so its `--` comments used to pair up into
        // creole strikethrough — markers deleted, the span between them struck through. Same for two URLs
        // on a line (`//` → italic) and for a tag PlantUML knows (`<b>` → bold).
        var body = """
            {"query":"SELECT a,\n  -- domestic values\n  m.x,\n  -- change values\n  m.y",
             "links":"https://a.example/x and https://b.example/y","label":"<b>raw</b>"}
            """;
        var logs = new[]
        {
            MakeLog(RequestResponseType.Request, null),
            MakeLog(RequestResponseType.Response, body),
        };
        var plantUml = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.First().PlainText;

        var svg = System.Text.Encoding.UTF8.GetString(NodeJsPlantUmlRenderer.Render(plantUml, PlantUmlImageFormat.Svg));
        // PlantUML breaks a note line into one <text> per whitespace-separated piece, so compare on the
        // text it drew with runs of whitespace collapsed. Read through an XML parser: the SVG is XML, so
        // `<b>raw</b>` is escaped in it, and the parser is what gives the text back as drawn.
        var rendered = System.Text.RegularExpressions.Regex.Replace(DrawnText(svg), @"\s+", " ");

        Assert.Contains("-- domestic values", rendered);
        Assert.Contains("-- change values", rendered);
        Assert.Contains("https://a.example/x and https://b.example/y", rendered);
        Assert.Contains("<b>raw</b>", rendered);
        Assert.DoesNotContain("line-through", svg);
    }

    /// <summary>
    /// The text of every &lt;text&gt; element, read through an XML parser (which also proves the SVG is XML),
    /// with runs of whitespace collapsed: the engine draws each space between words as its own element.
    /// </summary>
    private static string DrawnText(string svg) => System.Text.RegularExpressions.Regex.Replace(
        string.Join(" ", System.Xml.Linq.XDocument.Parse(svg).Descendants().Where(e => e.Name.LocalName == "text").Select(e => e.Value)),
        @"\s+", " ");

    [Fact]
    [Trait("Category", "Integration")]
    public void An_ampersand_and_an_xml_body_come_out_as_well_formed_svg_that_keeps_their_text()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // Built by the emitter: a query string and a JSON body carrying `&`, and XML bodies. With internal-flow
        // tracking off a NodeJs report embeds each SVG as a data: image, which a bare `&` failed to parse, and
        // with it on (the default) the SVG is inlined, where the XML body's tags became elements that paint
        // nothing (DIAGRAM_COLOURS_PLAN R26).
        static RequestResponseLog Log(string uri, RequestResponseType type, string? content, Guid rr) =>
            new(TestName: "Xml", TestId: "xml-1", Method: HttpMethod.Post, Content: content, Uri: new Uri(uri),
                Headers: [], ServiceName: "Orders", CallerName: "Api", Type: type, TraceId: Guid.NewGuid(),
                RequestResponseId: rr, TrackingIgnore: false, StatusCode: type == RequestResponseType.Response ? HttpStatusCode.OK : null);
        var ampId = Guid.NewGuid();
        var xmlId = Guid.NewGuid();
        var amp = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
        [
            Log("http://example.com/api/items?page=1&size=10", RequestResponseType.Request, null, ampId),
            Log("http://example.com/api/items?page=1&size=10", RequestResponseType.Response, """{"dish":"fish & chips"}""", ampId),
        ]).Single().PlantUmls.First().PlainText;
        var xml = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
        [
            Log("http://example.com/api/orders", RequestResponseType.Request, "<order><id>A1</id></order>", xmlId),
            Log("http://example.com/api/orders", RequestResponseType.Response, "<result>true</result>", xmlId),
        ]).Single().PlantUmls.First().PlainText;

        var results = NodeJsPlantUmlRenderer.RenderMany([amp, xml]);

        Assert.All(results, r => Assert.True(r.Succeeded, r.Error));
        var ampText = DrawnText(results[0].Svg!);
        Assert.Contains("?page=1&size=10", ampText);
        Assert.Contains("fish & chips", ampText);
        var xmlDoc = System.Xml.Linq.XDocument.Parse(results[1].Svg!);
        Assert.DoesNotContain(xmlDoc.Descendants(), e => e.Name.LocalName is "order" or "result");
        Assert.Contains("<order><id>A1</id></order>", DrawnText(results[1].Svg!));
        Assert.Contains("<result>true</result>", DrawnText(results[1].Svg!));
    }

    private static readonly Guid CreoleRequestResponseId = Guid.NewGuid();

    private static RequestResponseLog MakeLog(RequestResponseType type, string? content) =>
        new(
            TestName: "Creole", TestId: "creole-1",
            Method: HttpMethod.Get, Content: content,
            Uri: new Uri("http://example.com/api/jobs"),
            Headers: [], ServiceName: "BigQuery", CallerName: "Api",
            Type: type, TraceId: Guid.NewGuid(), RequestResponseId: CreoleRequestResponseId,
            TrackingIgnore: false, StatusCode: HttpStatusCode.OK);

    // ── Statement-length boundaries, measured against the real engine ────────────────────────────
    //
    // These pin the constants in PlantUmlStatementLimits. Neither failure mode announces itself as a
    // length problem: an over-long message or block opener matches no rule, so the parser abandons the
    // diagram and the engine draws "Syntax Error?" over the whole fragment (and where the fallback
    // *class* parse happens to succeed, it silently draws the wrong diagram with no banner at all); an
    // over-long coloured note bar overflows the engine's own JS stack and yields no SVG. Without these
    // pins an engine bump that moved a limit would surface as a mystery report, not a red test.

    private static string RenderBody(string body)
    {
        var result = NodeJsPlantUmlRenderer.RenderMany([$"@startuml\n{body}\n@enduml"])[0];
        return result.Svg ?? "";
    }

    /// <summary>
    /// The engine drew a real sequence message — the silent class-diagram fallback does not. Teoz
    /// (the default sequence engine since 1.2026.7) emits no CSS classes, so the durable signal is
    /// that a sequence diagram draws participant <c>b</c> twice — head box and foot box — while the
    /// class fallback draws its <c>b</c> box once.
    /// </summary>
    private static bool DrewMessage(string body)
    {
        var svg = RenderBody(body);
        return svg.Length > 0
               && !svg.Contains("Syntax Error", StringComparison.Ordinal)
               && System.Text.RegularExpressions.Regex.Matches(svg, ">b</text>").Count >= 2;
    }

    private static bool Renders(string body)
    {
        var svg = RenderBody(body);
        return svg.Length > 0 && !svg.Contains("Syntax Error", StringComparison.Ordinal);
    }

    /// <summary>A statement of exactly <paramref name="total"/> characters, padding the label.</summary>
    private static string StatementOf(string prefix, int total) => prefix + new string('x', total - prefix.Length);

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("a -> b: ")]
    [InlineData("a --> b: ")]
    [InlineData("a -[#F39C12]> b: ")]
    [InlineData("a -[#F39C12]-> b: ")]
    [InlineData("aaaaaaaaaaaaaaaaaaaa -> b: ")]
    public void The_message_limit_is_two_thousand_characters_of_whole_statement(string prefix)
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // The cap is on the statement, not the label: a 27-character prefix leaves a 1973-character
        // label, not a longer statement. That is why the emitter subtracts its own prefix.
        var max = PlantUmlStatementLimits.MaxMessageStatementChars;
        Assert.True(DrewMessage(StatementOf(prefix, max)), $"{max} characters should parse");
        Assert.False(DrewMessage(StatementOf(prefix, max + 1)), $"{max + 1} characters should not");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Leading_and_trailing_whitespace_does_not_count_toward_the_message_limit()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        var statement = StatementOf("a -> b: ", PlantUmlStatementLimits.MaxMessageStatementChars);
        Assert.True(DrewMessage("    " + statement + new string(' ', 500)));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void A_block_opener_caps_lower_than_a_message_statement()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // On the 1.2026.6 build this was a parse limit around 1476 (loop) to 1484 (opt); from the
        // 1.2026.8beta1 build the parse accepts far more but the engine crashes on its own JS stack
        // instead (RangeError): about 2,000 on node 25.9 with its default stack on both 1.2026.8 builds,
        // 3660 to 5641 on the runtime of 2026-09-04 (plans/ENGINE_PIN_PLAN.md §1.11). A stack edge moves
        // with the runtime and wobbles between processes. The constant sits well under the lowest measurement
        // rather than on it, so the pins are the two facts that matter and survive a small engine
        // drift: the constant parses, and a runaway block label still kills the whole diagram.
        var safe = StatementOf("loop ", PlantUmlStatementLimits.MaxBlockLabelChars);
        var runaway = StatementOf("loop ", 8000);

        Assert.True(Renders($"a -> b: x\n{safe}\na -> b: y\nend"), $"{PlantUmlStatementLimits.MaxBlockLabelChars} should parse");
        Assert.False(Renders($"a -> b: x\n{runaway}\na -> b: y\nend"),
            "8000 should not — an over-long block opener still takes the diagram down");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void A_coloured_note_bar_crashes_the_engine_rather_than_reporting_a_syntax_error()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // The step-delimiter bar's own form. Past the cap the engine throws `RangeError: Maximum call
        // stack size exceeded` and returns no SVG at all, so the scenario loses every diagram it had
        // rather than one statement. Measured 1458 on the 1.2026.6 build, around 4124 on 1.2026.8beta1
        // on 2026-09-04 and 2005 to 2008 on node 25.9 with both 1.2026.8 builds, and in the render worker,
        // whose stack is half node's, from 880 with V8's optimizing compilers off (plans/LONG_COMPONENT_EDGE_PLAN.md
        // R3): a stack-overflow edge, so it moves with the runtime; the crash probe sits far past it and the
        // constant far under it. The bar is a step written as one token, a JSON array, which keeps the
        // coloured form at any length: a step with a space wraps, and a wrapped step takes the styled form.
        var token = "[" + string.Join(",", Enumerable.Range(1, 60).Select(i => $"{{\"sku\":\"SKU-{i:D4}\"}}")) + "]";
        var safe = Kronikol.Ingestion.InteractionRecord.StepDelimiterPlantUml(null, token);
        Assert.StartsWith(Kronikol.PlantUml.StepBarPlantUml.LegacyPrefix, safe);
        Assert.Equal(PlantUmlStatementLimits.MaxColouredNoteBarChars, safe.Length);

        Assert.True(Renders($"a -> b: x\n{safe}"), "the capped bar renders");
        Assert.Equal("", RenderBody("a -> b: x\nhnote across <<stepDelimiter>> #black:<color:white>" + new string('s', 6000)));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void An_uncoloured_note_bar_and_a_note_body_run_far_past_the_message_limit()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // Measured ~16 400 and ~16 371 — which is why the backstop leaves them alone below its own
        // 16 000 ceiling. A Gherkin step with a doc string is legitimately long.
        var long6000 = new string('n', 6000);
        Assert.True(Renders($"a -> b: x\nhnote across #black:{long6000}"), "an uncoloured bar has no low cap");
        Assert.True(Renders($"a -> b: x\nnote left\n{long6000}\nend note"), "note bodies have no low cap");
        Assert.True(Renders($"a -> b: x\nnote over a : {long6000}"), "a one-line note has no low cap");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void A_five_thousand_character_url_trace_renders_without_a_syntax_error()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // The regression: a Redis DELETE of 41 cache keys, ~5,300 characters of path, produced one
        // 5,410-character arrow statement and took its whole diagram fragment down with it.
        var log = new RequestResponseLog(
            TestName: "Long URL", TestId: "long-url-1",
            Method: HttpMethod.Delete, Content: null,
            Uri: new Uri("http://example.com/data-insights-api/" + new string('k', 5000)),
            Headers: [], ServiceName: "redis", CallerName: "dataInsights",
            Type: RequestResponseType.Request, TraceId: Guid.NewGuid(), RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false);

        var plantUml = PlantUmlCreator.GetPlantUmlImageTagsPerTestId([log]).Single().PlantUmls.First().PlainText;
        var svg = System.Text.Encoding.UTF8.GetString(NodeJsPlantUmlRenderer.Render(plantUml, PlantUmlImageFormat.Svg));

        // `!pragma teoz true` — which every Kronikol diagram carries — renders without CSS classes, so the
        // signal here is the absence of the error banner plus a drawn diagram of a plausible size.
        Assert.DoesNotContain("Syntax Error", svg);
        Assert.Contains("<svg", svg);
        Assert.True(svg.Length > 10_000, $"the diagram drew only {svg.Length} bytes — it probably failed");
        // The full path is still in the report — the note beside the arrow carries it, chunked into
        // 80-character pieces so wrapWidth can break it, each piece drawn as its own <text>.
        Assert.Contains(new string('k', 80), svg);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void A_long_request_label_inside_its_internal_flow_link_renders_in_node()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // plans/ENGINE_PIN_PLAN.md S0: the text inside the link is cut to MaxLinkedLabelChars because a Chromium
        // worker overflowed its stack parsing a longer one. Node drew the longer link, and draws the cut one.
        var url = "http://orders.internal/orders/search?" + string.Join("&", Enumerable.Range(0, 140).Select(i => $"filter{i}=value{i}"));
        var pairId = Guid.NewGuid();
        var request = new RequestResponseLog("Search orders", "long-linked-1", HttpMethod.Get, null, new Uri(url), [],
            "OrderService", "Api", RequestResponseType.Request, Guid.NewGuid(), pairId, TrackingIgnore: false);
        var response = request with { Type = RequestResponseType.Response, Content = "{ \"orders\": [] }", StatusCode = HttpStatusCode.OK };
        var plantUml = PlantUmlCreator.GetPlantUmlImageTagsPerTestId([request, response], internalFlowTracking: true)
            .Single().PlantUmls.First().PlainText;
        Assert.Contains($"[[#iflow-{pairId} GET: /orders/search?filter0=value0", plantUml);

        var svg = NodeJsPlantUmlRenderer.RenderMany([plantUml])[0].Svg ?? "";

        Assert.DoesNotContain("Syntax Error", svg);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(svg, ">OrderService</text>").Count >= 2,
            "the sequence diagram was not drawn (a sequence diagram draws each participant twice)");
        // The engine draws a link as underlined text (the page binds it by its text, there is no <a>), and the
        // whole path is in the note beside the arrow.
        Assert.Matches("text-decoration=\"underline\"[^>]*>GET: /orders/search\\?filter0=value0", svg);
        Assert.Contains(">[Full<", svg);
    }

    // ── ES-module engine builds (npm @plantuml/core line) ────────────────────────────────────────

    private static string RenderScriptSource()
    {
        var assembly = typeof(NodeJsPlantUmlRenderer).Assembly;
        var name = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("plantuml-render.js", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }

    private static byte[] RenderScriptBytes()
    {
        var assembly = typeof(NodeJsPlantUmlRenderer).Assembly;
        var name = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("plantuml-render.js", StringComparison.OrdinalIgnoreCase));
        using var stream = new MemoryStream();
        assembly.GetManifestResourceStream(name)!.CopyTo(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// An engine cache on a directory of the test's own, whose downloader serves <paramref name="files"/> and whose known
    /// hashes are theirs, so nothing the machine shares is touched and no network is needed.
    /// </summary>
    private static EngineCache CacheServing(string dir, IReadOnlyDictionary<string, byte[]> files, List<string>? downloads = null) =>
        new(dir, url =>
            {
                var file = url[(url.LastIndexOf('/') + 1)..];
                lock (files) downloads?.Add(file);
                return files[file];
            }, Kronikol.Constants.TrackingDefaults.PlantUmlJsCdnBase,
            files.ToDictionary(f => f.Key, f => EngineCache.Integrity(f.Value)));

    [Fact]
    public void Node_is_started_on_the_render_script_of_this_build()
    {
        // The cache directory is named for the engine, so every Kronikol version on one engine pin shares it, the
        // kronikol tool beside a test project on another version included. Each process wrote its build's script to
        // one fixed name on its first render and ran whatever that name held from then on: another version's script,
        // or one another process was halfway through writing.
        var dir = Directory.CreateTempSubdirectory("kronikol-node-start-").FullName;
        try
        {
            var engine = CacheServing(dir, new Dictionary<string, byte[]>
            {
                ["viz-global.js"] = "globalThis.Viz = {};"u8.ToArray(),
                ["plantuml.js"] = "export{C as render,D as renderToString};"u8.ToArray(),
            });

            var start = NodeJsPlantUmlRenderer.NodeStartInfo(engine, batch: true);

            var script = start.ArgumentList[0];
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(RenderScriptBytes()))[..16];
            Assert.Equal(Path.Combine(dir, $"plantuml-render.{hash}.js"), script);
            Assert.Equal(RenderScriptBytes(), File.ReadAllBytes(script));
            Assert.Equal(Path.Combine(dir, "viz-global.js"), start.ArgumentList[1]);
            Assert.Equal(Path.Combine(dir, "plantuml.js"), start.ArgumentList[2]);
            Assert.Equal("--batch", start.ArgumentList[^1]);
            // The machine's directory holds its own under the same name.
            Assert.Equal(Path.GetDirectoryName(NodeJsPlantUmlRenderer.CodeCachePath), Path.GetDirectoryName(NodeJsPlantUmlRenderer.RenderScriptPath));
            Assert.Equal(Path.GetFileName(script), Path.GetFileName(NodeJsPlantUmlRenderer.RenderScriptPath));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void A_render_after_its_engine_directory_was_deleted_fills_it_again()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // Deleting the directory is the remedy a failed check names. A process that had rendered once never checked its
        // files again, so node was started on a script that was gone, and every later diagram of the process became a
        // placeholder. The render runs on a directory of its own, filled from the machine's verified copies: the machine's
        // directory is shared by every class that renders, and none of it is deleted here.
        NodeJsPlantUmlRenderer.RenderMany([Seq("Warm", "Up")]);
        var shared = Path.GetDirectoryName(NodeJsPlantUmlRenderer.CodeCachePath)!;
        var files = new[] { "viz-global.js", "plantuml.js" }.ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(shared, f)));
        var downloads = new List<string>();
        var dir = Directory.CreateTempSubdirectory("kronikol-engine-deleted-").FullName;
        try
        {
            var engine = CacheServing(dir, files, downloads);
            Assert.True(NodeJsPlantUmlRenderer.RenderMany([Seq("Before", "Delete")], engine)[0].Succeeded);

            Directory.Delete(dir, recursive: true);
            var after = NodeJsPlantUmlRenderer.RenderMany([Seq("After", "Delete")], engine)[0];

            Assert.True(after.Succeeded, after.Error);
            Assert.Contains("After", after.Svg);
            Assert.Equal(["plantuml.js", "plantuml.js", "viz-global.js", "viz-global.js"], downloads.Order());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void A_render_leaves_this_builds_script_under_its_own_name()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        Assert.True(NodeJsPlantUmlRenderer.RenderMany([Seq("Script", "Own")])[0].Succeeded);

        Assert.Equal(RenderScriptBytes(), File.ReadAllBytes(NodeJsPlantUmlRenderer.RenderScriptPath));
    }

    [Fact]
    public void An_esm_engine_build_is_rewritten_and_drives_the_renderer()
    {
        Assert.SkipWhen(!NodeProbe.IsAvailable, "Node.js not available on PATH");

        // The npm @plantuml/core line ships as an ES module ending in `export{C as render,D as
        // renderToString};` — vm.Script cannot evaluate an export statement, so plantuml-render.js must
        // rewrite the tail into an assignment and drive the exports directly (that build has no
        // plantumlLoad). Probed with stubs so the test needs node but neither network nor the real engine.
        var dir = Directory.CreateTempSubdirectory("kronikol-esm-stub-").FullName;
        try
        {
            var viz = Path.Combine(dir, "viz-global.js");
            var engine = Path.Combine(dir, "plantuml.js");
            File.WriteAllText(viz,
                "globalThis.Viz = { instance: function () { return Promise.resolve({ renderString: function () { return '<svg xmlns=\"http://www.w3.org/2000/svg\"/>'; } }); } };");
            File.WriteAllText(engine,
                """
                "use strict";
                let C=(lines,id,options)=>{var t=document.getElementById(id);t.innerHTML='<svg xmlns="http://www.w3.org/2000/svg"><text>'+lines.join(' ')+' '+JSON.stringify(options||{})+'</text></svg>';},D=(lines,options)=>'unused';
                export{C as render,D as renderToString};
                """);

            var stdout = NodeProbe.RunWithStdin(RenderScriptSource(), "@startuml\na -> b: esm probe\n@enduml", viz, engine);

            Assert.Contains("<svg", stdout);
            Assert.Contains("esm probe", stdout);
            // A stock engine build defaults to an 8192px size limit; the driver must raise it to
            // Kronikol's 98304px through the maxSvgSize render option (the stub echoes its options).
            Assert.Contains("\"maxSvgSize\":98304", stdout);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Runs <c>plantuml-render.js</c> on one source against a stub viz and a stub ES-module engine whose
    /// render is <paramref name="renderBody"/> (the body of <c>(lines, id, options) =&gt; { … }</c>).
    /// </summary>
    private static string RunWithStubEngine(string renderBody, string source = "@startuml\na -> b: stub\n@enduml")
    {
        var dir = Directory.CreateTempSubdirectory("kronikol-stub-engine-").FullName;
        try
        {
            var viz = Path.Combine(dir, "viz-global.js");
            var engine = Path.Combine(dir, "plantuml.js");
            File.WriteAllText(viz,
                "globalThis.Viz = { instance: function () { return Promise.resolve({ renderString: function () { return '<svg xmlns=\"http://www.w3.org/2000/svg\"/>'; } }); } };");
            File.WriteAllText(engine,
                "\"use strict\";\nlet C=(lines,id,options)=>{" + renderBody + "},D=(lines,options)=>'unused';\nexport{C as render,D as renderToString};\n");
            return NodeProbe.RunWithStdin(RenderScriptSource(), source, viz, engine);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void A_script_the_engine_appends_to_head_is_answered_with_onerror()
    {
        Assert.SkipWhen(!NodeProbe.IsAvailable, "Node.js not available on PATH");

        // What the engine's loader does for `!theme`, `!include <…>`, `<&icon>` and `<:emoji:>`: append a
        // <script> to document.head and wait for onload or onerror. Nothing can load in this host, so a
        // head that answers nothing leaves the render waiting until the 20 s poll gives up, and in a batch
        // every diagram after it too (DIAGRAM_COLOURS_PLAN F10, F17). The stub gives up after 2 s itself,
        // so a head that never answers fails this fact quickly.
        var stdout = RunWithStubEngine("""
            var t=document.getElementById(id);
            var s=document.createElement('script'); s.src='themes.js'; s.async=true;
            s.onload=function(){ t.innerHTML='<svg xmlns="http://www.w3.org/2000/svg"><text>loaded</text></svg>'; };
            s.onerror=function(){ t.innerHTML='<svg xmlns="http://www.w3.org/2000/svg"><text>refused</text></svg>'; };
            document.head.appendChild(s);
            setTimeout(function(){ if(!t.innerHTML) t.textContent='the head never answered'; }, 2000);
            """);

        Assert.Contains("refused", stdout);
        Assert.DoesNotContain("loaded", stdout);
    }

    [Fact]
    public void The_svg_is_written_as_xml_with_its_text_and_attributes_escaped()
    {
        Assert.SkipWhen(!NodeProbe.IsAvailable, "Node.js not available on PATH");

        // The engine builds its SVG through DOM calls, as text nodes, as an element's textContent and as
        // attributes, plus a processing instruction carrying its encoded source. The serializer used to
        // write all of it as it was: a `&` in a URL made the data: image fail to parse, an XML body's tags
        // became elements that paint nothing, and the instruction came out as an HTML <div>, which an
        // HTML page reads as the end of an inline <svg> (DIAGRAM_COLOURS_PLAN F20).
        var stdout = RunWithStubEngine("""
            var NS='http://www.w3.org/2000/svg', t=document.getElementById(id);
            var svg=document.createElementNS(NS,'svg'); svg.setAttribute('xmlns', NS);
            var a=document.createElementNS(NS,'text'); a.setAttribute('data-q','a "q" & <b>');
            a.appendChild(document.createTextNode('fish & chips <order>1</order> x > y'));
            var b=document.createElementNS(NS,'text'); b.textContent='?page=1&size=10';
            svg.appendChild(a); svg.appendChild(b);
            svg.appendChild(document.createProcessingInstruction('plantuml-src','SyfFKj2rKt3CoKnELR1Io4ZDoSa70000'));
            t.appendChild(svg);
            """);

        var doc = System.Xml.Linq.XDocument.Parse(stdout);
        var texts = doc.Root!.Elements().ToList();
        Assert.Equal(2, texts.Count);
        Assert.Equal("fish & chips <order>1</order> x > y", texts[0].Value);
        Assert.Equal("a \"q\" & <b>", texts[0].Attribute("data-q")!.Value);
        Assert.Equal("?page=1&size=10", texts[1].Value);
        Assert.DoesNotContain("<div", stdout);
    }

    [Fact]
    public void An_element_that_is_not_a_script_gets_no_answer_from_head()
    {
        Assert.SkipWhen(!NodeProbe.IsAvailable, "Node.js not available on PATH");

        var stdout = RunWithStubEngine("""
            var t=document.getElementById(id);
            var st=document.createElement('style'); var fired='none';
            st.onload=function(){ fired='load'; }; st.onerror=function(){ fired='error'; };
            document.head.appendChild(st);
            setTimeout(function(){ t.innerHTML='<svg xmlns="http://www.w3.org/2000/svg"><text>style:'+fired+'</text></svg>'; }, 50);
            """);

        Assert.Contains("style:none", stdout);
    }

    /// <summary>
    /// Runs <c>plantuml-render.js</c> once against a stub viz and a stub ES-module engine kept in
    /// <paramref name="dir"/>, so the engine's code cache (<c>plantuml.js.v8cache</c> beside the stub) carries over
    /// from one run to the next; returns the run and the code-cache status it reported.
    /// </summary>
    private static (NodeProbe.NodeRun Run, string? Status) RunStubEngineIn(string dir)
    {
        var viz = Path.Combine(dir, "viz-global.js");
        var engine = Path.Combine(dir, "plantuml.js");
        if (!File.Exists(engine))
        {
            File.WriteAllText(viz,
                "globalThis.Viz = { instance: function () { return Promise.resolve({ renderString: function () { return '<svg xmlns=\"http://www.w3.org/2000/svg\"/>'; } }); } };");
            File.WriteAllText(engine,
                "\"use strict\";\nlet C=(lines,id,options)=>{var t=document.getElementById(id);t.innerHTML='<svg xmlns=\"http://www.w3.org/2000/svg\"><text>'+lines.join(' ')+'</text></svg>';},D=(lines,options)=>'unused';\nexport{C as render,D as renderToString};\n");
        }
        var run = NodeProbe.RunCaptured(RenderScriptSource(), "@startuml\na -> b: cached\n@enduml", viz, engine);
        var status = System.Text.RegularExpressions.Regex.Match(run.Stderr, @"\[plantuml-render\] code cache: (\w+)");
        return (run, status.Success ? status.Groups[1].Value : null);
    }

    [Fact]
    public void A_code_cache_damaged_in_its_middle_is_rejected_and_rebuilt_instead_of_run()
    {
        Assert.SkipWhen(!NodeProbe.IsAvailable, "Node.js not available on PATH");

        // A release node checks a code cache's header and never its payload (plans/ENGINE_PIN_PLAN.md §1.14): one
        // byte flipped in the real engine's cache crashed node in 6 of 10 flips, before any output, and the crash
        // never rewrote the file, so every later render on that machine crashed the same way. The file carries a
        // SHA-256 of its data now, checked before V8 is handed the data.
        var dir = Directory.CreateTempSubdirectory("kronikol-code-cache-").FullName;
        try
        {
            var cachePath = Path.Combine(dir, NodeJsPlantUmlRenderer.CodeCacheFileName);
            Assert.Equal("miss", RunStubEngineIn(dir).Status);
            Assert.Equal("hit", RunStubEngineIn(dir).Status);

            var bytes = File.ReadAllBytes(cachePath);
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllBytes(cachePath, bytes);
            var (damaged, status) = RunStubEngineIn(dir);

            Assert.True(damaged.ExitCode == 0, $"node exited {damaged.ExitCode}: {damaged.Stderr}");
            Assert.Contains("<svg", damaged.Stdout);
            Assert.Contains("cached", damaged.Stdout);
            Assert.Equal("rejected", status);
            Assert.Equal("hit", RunStubEngineIn(dir).Status);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void The_engine_rewrite_the_shared_code_cache_is_compiled_from_stays_as_every_version_wrote_it()
    {
        // The code cache sits in the directory every Kronikol version on one engine shares, and V8 checks a cache against
        // the source's length only (plans/ENGINE_PIN_PLAN.md §1.12). It is right for all of them because each compiles the
        // same source: the engine with its trailing export rewritten by these two lines, unchanged since 3.0.50. A version
        // that rewrote it to other text of the same length would run another version's compiled code (§10.5). To change
        // them, give the cache a file of its own first.
        var source = RenderScriptSource();

        Assert.Contains("""var em = /export\s*\{\s*([A-Za-z_$][\w$]*)\s+as\s+render\s*,\s*([A-Za-z_$][\w$]*)\s+as\s+renderToString\s*\}\s*;?\s*$/.exec(tail);""", source);
        Assert.Contains("""+ 'globalThis.__plantumlExports = { render: ' + em[1] + ', renderToString: ' + em[2] + ' };\n';""", source);
        Assert.Contains("var cachePath = filePath + '.v8cache';", source);
    }

    [Fact]
    public void The_render_script_says_what_v8_checks_a_code_cache_against()
    {
        // V8 checks cached data against the source's length, not its bytes (plans/ENGINE_PIN_PLAN.md §1.12). The
        // script's comments said a changed file was rejected, which holds only for a change of length.
        var source = RenderScriptSource();

        Assert.DoesNotContain("a changed file", source);
        Assert.Contains("length, not its bytes", source);
    }

    /// <summary>
    /// Runs <c>plantuml-render.js</c> once under <paramref name="nodeOptions"/> against a stub viz whose instance needs
    /// WebAssembly, as the real one does, and a stub engine that draws the layout it would use: Graphviz when
    /// <c>Viz</c> is there, and its Smetana port when it is not, which it says on <c>console.info</c> as the real
    /// engine does.
    /// </summary>
    private static NodeProbe.NodeRun RunLayoutStub(IReadOnlyList<string> nodeOptions)
    {
        var dir = Directory.CreateTempSubdirectory("kronikol-layout-stub-").FullName;
        try
        {
            var viz = Path.Combine(dir, "viz-global.js");
            var engine = Path.Combine(dir, "plantuml.js");
            File.WriteAllText(viz,
                "globalThis.Viz = { instance: function () { return typeof WebAssembly === 'undefined' ? Promise.reject(new ReferenceError('WebAssembly is not defined')) : Promise.resolve({ renderString: function () { return '<svg xmlns=\"http://www.w3.org/2000/svg\"/>'; } }); } };");
            File.WriteAllText(engine,
                "\"use strict\";\nlet C=(lines,id,options)=>{var smetana=typeof Viz==='undefined';if(smetana)console.info('PlantUML: viz-global.js is not loaded, falling back to the Smetana layout engine');document.getElementById(id).innerHTML='<svg xmlns=\"http://www.w3.org/2000/svg\"><text>'+(smetana?'smetana':'graphviz')+'</text></svg>';},D=(lines,options)=>'unused';\nexport{C as render,D as renderToString};\n");
            return NodeProbe.RunCaptured(nodeOptions, RenderScriptSource(), "@startuml\ncomponent a\n@enduml", viz, engine);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Under_jitless_the_render_script_leaves_graphviz_out_and_keeps_stdout_to_the_svg()
    {
        Assert.SkipWhen(!NodeProbe.IsAvailable, "Node.js not available on PATH");

        // Graphviz is WebAssembly, and node --jitless (NODE_OPTIONS can carry it) has none: viz-global.js loaded, its
        // instance never did, and the script stopped before its first diagram, sequence diagrams included. It leaves
        // viz out there now, and the engine lays the diagram out with its Smetana port. The engine says so on
        // console.info, which node writes to stdout, where the SVG goes (in batch mode, one JSON line per diagram).
        var jitless = RunLayoutStub(["--jitless"]);

        Assert.True(jitless.ExitCode == 0, $"node exited {jitless.ExitCode}: {jitless.Stderr}");
        Assert.StartsWith("<svg", jitless.Stdout);
        Assert.Contains("smetana", jitless.Stdout);
        Assert.DoesNotContain("PlantUML:", jitless.Stdout);
        Assert.Contains("falling back to the Smetana layout engine", jitless.Stderr);
        Assert.Contains("[plantuml-render] layout: smetana (WebAssembly is not defined", jitless.Stderr);

        // Where WebAssembly works nothing moves: Graphviz is loaded and used.
        var usual = RunLayoutStub([]);
        Assert.True(usual.ExitCode == 0, $"node exited {usual.ExitCode}: {usual.Stderr}");
        Assert.Contains("graphviz", usual.Stdout);
        Assert.DoesNotContain("layout: smetana", usual.Stderr);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Under_jitless_the_real_engine_draws_every_diagram_and_the_component_diagram_with_smetana()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // The renderer downloads and checks the real files on its first render. The run works on copies, so the code
        // cache it writes under --jitless (V8 rejects one made under other flags) stays out of the shared directory.
        NodeJsPlantUmlRenderer.RenderMany([Seq("Warm", "Up")]);
        var cacheDir = Path.GetDirectoryName(NodeJsPlantUmlRenderer.CodeCachePath)!;
        var dir = Directory.CreateTempSubdirectory("kronikol-jitless-").FullName;
        try
        {
            foreach (var file in new[] { "viz-global.js", "plantuml.js" })
                File.Copy(Path.Combine(cacheDir, file), Path.Combine(dir, file));
            var batch = System.Text.Json.JsonSerializer.Serialize(new { id = "sequence", source = Seq("Alpha", "Beta") }) + "\n"
                        + System.Text.Json.JsonSerializer.Serialize(new { id = "component", source = "@startuml\ncomponent Orders\ndatabase Stock\nOrders --> Stock : reads\n@enduml" }) + "\n";

            var run = NodeProbe.RunCaptured(["--jitless"], RenderScriptSource(), batch,
                Path.Combine(dir, "viz-global.js"), Path.Combine(dir, "plantuml.js"), "--batch");

            Assert.True(run.ExitCode == 0, $"node exited {run.ExitCode}: {run.Stderr}");
            // Every stdout line is a result: the engine's fallback notice went to stderr.
            var results = run.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => System.Text.Json.JsonDocument.Parse(line).RootElement)
                .ToDictionary(r => r.GetProperty("id").GetString()!, r => r.TryGetProperty("svg", out var svg) ? svg.GetString()! : "error: " + r.GetProperty("error").GetString());
            Assert.Equal(2, results.Count);
            Assert.Contains("Alpha", results["sequence"]);
            Assert.StartsWith("<svg", results["component"]);
            Assert.Contains("Orders", results["component"]);
            Assert.Contains("Stock", results["component"]);
            Assert.DoesNotContain("has crashed", results["component"]);
            Assert.Contains("falling back to the Smetana layout engine", run.Stderr);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void The_engine_cache_is_versioned_by_the_cdn_tag()
    {
        // The cache directory carries the engine's version. Before 3.31.1 the renderer kept any file already there, so a
        // change of TrackingDefaults.PlantUmlJsCdnBase kept every machine on the old engine for good; the known-hash check
        // (EngineCache) would now replace it, but a machine running two Kronikol versions on two engines would then
        // replace one with the other on every run.
        var tag = Kronikol.Constants.TrackingDefaults.PlantUmlJsCdnBase.Split('/')[^1].Split('@')[^1];
        Assert.Contains($"{Path.DirectorySeparatorChar}{tag}{Path.DirectorySeparatorChar}",
            NodeJsPlantUmlRenderer.CodeCachePath);
        // On the npm route the last segment is `core@1.2026.8`: the directory is the release version, distinct
        // from every fork tag's directory (plans/ENGINE_PIN_PLAN.md §1.6).
        Assert.Equal("1.2026.8", tag);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void The_engine_files_on_the_cdn_hash_to_the_known_constants()
    {
        // The Node cache refuses a file that does not match these constants, and the report page hands them to the
        // browser's own integrity check (plans/ENGINE_PIN_PLAN.md S2, S3): one wrong character in a constant would
        // cost every diagram. Checked against what the CDN serves.
        using var http = new HttpClient();
        foreach (var (file, expected) in NodeJsPlantUmlRenderer.ExpectedIntegrity)
        {
            var bytes = http.GetByteArrayAsync($"{Kronikol.Constants.TrackingDefaults.PlantUmlJsCdnBase}/{file}").GetAwaiter().GetResult();
            var actual = EngineCache.Integrity(bytes);
            Assert.True(actual == expected, $"{file}: the CDN serves {actual} ({bytes.Length:N0} bytes), the constant says {expected}");
        }
    }
}
