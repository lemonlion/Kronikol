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
        // text it drew with runs of whitespace collapsed.
        var drawn = string.Join(" ", System.Text.RegularExpressions.Regex
            .Matches(svg, @"<text\b[^>]*>([\s\S]*?)</text>")
            .Select(m => m.Groups[1].Value));
        var rendered = System.Text.RegularExpressions.Regex.Replace(drawn, @"\s+", " ");

        Assert.Contains("-- domestic values", rendered);
        Assert.Contains("-- change values", rendered);
        Assert.Contains("https://a.example/x and https://b.example/y", rendered);
        Assert.Contains("<b>raw</b>", rendered);
        Assert.DoesNotContain("line-through", svg);
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
}
