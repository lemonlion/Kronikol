using System.Diagnostics;
using System.Net;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml;

public class NodeJsPlantUmlRendererTests
{
    private static bool IsNodeAvailable()
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

    private static string Seq(string a, string b) => $"@startuml\nparticipant {a}\nparticipant {b}\n{a} -> {b} : hello from {a}\n@enduml";

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

        var single = Stopwatch.StartNew();
        foreach (var s in sources) NodeJsPlantUmlRenderer.Render(s, PlantUmlImageFormat.Svg);
        single.Stop();

        var batch = Stopwatch.StartNew();
        var results = NodeJsPlantUmlRenderer.RenderMany(sources);
        batch.Stop();

        Assert.All(results, r => Assert.True(r.Succeeded, r.Error));
        // Measured 1.9 s vs 5.1 s (node start + engine compile + warm-up paid once instead of five times);
        // the bound is loose so a loaded box does not fail it.
        Assert.True(batch.ElapsedMilliseconds * 1.3 < single.ElapsedMilliseconds,
            $"batch of 5 took {batch.ElapsedMilliseconds} ms, five single spawns {single.ElapsedMilliseconds} ms");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Code_cache_is_created_on_first_run_reused_afterwards_and_regenerated_when_v8_rejects_it()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        var cachePath = NodeJsPlantUmlRenderer.CodeCachePath;
        NodeJsPlantUmlRenderer.Render(Seq("Warm", "Up"), PlantUmlImageFormat.Svg); // makes sure the engine is downloaded
        if (File.Exists(cachePath)) File.Delete(cachePath);

        NodeJsPlantUmlRenderer.Render(Seq("Cold", "One"), PlantUmlImageFormat.Svg);
        Assert.True(File.Exists(cachePath), "code cache should be written on the first run");
        Assert.Equal("miss", NodeJsPlantUmlRenderer.LastCodeCacheStatus);
        var written = new FileInfo(cachePath).Length;
        Assert.True(written > 1024, $"code cache is suspiciously small: {written} bytes");

        NodeJsPlantUmlRenderer.Render(Seq("Warm", "Two"), PlantUmlImageFormat.Svg);
        Assert.Equal("hit", NodeJsPlantUmlRenderer.LastCodeCacheStatus);

        // A cache V8 refuses (here: garbage; in real life a node upgrade) is rebuilt, and the render still works.
        File.WriteAllBytes(cachePath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var svg = System.Text.Encoding.UTF8.GetString(NodeJsPlantUmlRenderer.Render(Seq("Rejected", "Three"), PlantUmlImageFormat.Svg));
        Assert.Contains("<svg", svg);
        Assert.Equal("rejected", NodeJsPlantUmlRenderer.LastCodeCacheStatus);
        Assert.True(new FileInfo(cachePath).Length > 1024, "a rejected code cache should be regenerated");

        var batch = NodeJsPlantUmlRenderer.RenderMany([Seq("Batch", "Four")]);
        Assert.True(batch[0].Succeeded, batch[0].Error);
        Assert.Equal("hit", NodeJsPlantUmlRenderer.LastCodeCacheStatus);
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

        var watch = Stopwatch.StartNew();
        var results = NodeJsPlantUmlRenderer.RenderMany([themed, plain]);
        watch.Stop();

        Assert.All(results, r => Assert.True(r.Succeeded, r.Error));
        // The theme bundle cannot load here, so the engine draws the diagram without it, exactly as the
        // unthemed source draws.
        Assert.Equal(WithoutProcessingInstructions(results[1].Svg!), WithoutProcessingInstructions(results[0].Svg!));
        Assert.True(watch.ElapsedMilliseconds < 10_000, $"two diagrams took {watch.ElapsedMilliseconds} ms");
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

        // On the 1.2026.6 build this was a parse limit around 1476 (loop) to 1484 (opt); on the
        // 1.2026.8beta1 build the parse accepts far more but the engine crashes on its own JS stack
        // instead (RangeError), measured around 3660 (loop) to 5641 (opt) — stack-dependent, so the
        // exact edge wobbles between processes. The constant sits well under the lowest measurement
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
        // rather than one statement. Measured 1458 on the 1.2026.6 build and around 4124 on
        // 1.2026.8beta1 — a stack-overflow edge, so it wobbles between processes; the crash probe sits
        // far past it and the constant far under it.
        var safe = Kronikol.Ingestion.InteractionRecord.StepDelimiterPlantUml("Given", new string('s', 1200));
        Assert.True(safe.Length <= PlantUmlStatementLimits.MaxColouredNoteBarChars);

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

    // ── ES-module engine builds (npm @plantuml/core line) ────────────────────────────────────────

    private static string RenderScriptSource()
    {
        var assembly = typeof(NodeJsPlantUmlRenderer).Assembly;
        var name = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("plantuml-render.js", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
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

    [Fact]
    public void The_engine_cache_is_versioned_by_the_cdn_tag()
    {
        // DownloadJsFiles skips files that already exist: with an unversioned cache directory a change
        // of TrackingDefaults.PlantUmlJsCdnBase would silently keep every existing machine on the old
        // engine forever. The cache directory must therefore carry the CDN tag.
        var tag = Kronikol.Constants.TrackingDefaults.PlantUmlJsCdnBase.Split('/')[^1].Split('@')[^1];
        Assert.Contains($"{Path.DirectorySeparatorChar}{tag}{Path.DirectorySeparatorChar}",
            NodeJsPlantUmlRenderer.CodeCachePath);
    }
}
