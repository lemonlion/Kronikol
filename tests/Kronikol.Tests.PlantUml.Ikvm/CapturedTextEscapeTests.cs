using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml.Ikvm;

/// <summary>
/// Captured text through real Java PlantUML, the engine <c>PlantUmlRendering.Local</c> renders with (the escape
/// patch of 3.30.1). The Java engine acts on more of a note's text than the browser engine does. It reads a
/// captured <c>!include</c> line as a directive and draws the named local file into the diagram, and it ends the
/// diagram at a line opening with <c>@end</c>. It also joins a line ending in <c>\</c> to the next. A report
/// published from a machine that renders this way would carry the file.
/// </summary>
public class CapturedTextEscapeTests : IDisposable
{
    private readonly string _secretFile = Path.Combine(Path.GetTempPath(), "kronikol-include-" + Guid.NewGuid().ToString("N") + ".txt");

    public CapturedTextEscapeTests() => File.WriteAllText(_secretFile, "KRONIKOL-SECRET-MARKER\n");

    public void Dispose()
    {
        try { File.Delete(_secretFile); } catch { /* best effort */ }
    }

    private string[] HazardLines() =>
    [
        "!include " + _secretFile.Replace('\\', '/'),
        "'a', 'b'",
        "/' not a comment",
        "!define FOO BAR",
        "FOO stays",
        "%upper(\"x\") stays",
        "@enduml",
        "@startuml",
        "end note",
        "{{",
        "~/.bashrc and ~~wave~~",
        "= not a heading",
        "| not | a table |",
        "..not a separator..",
        "....",
        "--",
        "___",
        "a << b >> c",
        "&#39; stays",
        // What the first audit missed (DIAGRAM_COLOURS_PLAN §12.5).
        "R says <U+00E9>t and <U+0041>",
        "C:\\temp\\new and a\\tb",
        "grep '\\<word\\>' and \\[[x]]",
        "|_ tree item",
        "progress 10%\r20%",
        "trailing backslash \\",
        "next line",
    ];

    [Fact]
    public void The_java_engine_paints_every_hazard_line_as_captured_and_includes_no_file()
    {
        var lines = HazardLines();

        var svg = RenderedDiagram.Svg(DiagramFor(string.Join("\n", lines)));

        Assert.DoesNotContain("KRONIKOL-SECRET-MARKER", svg);
        var painted = PaintedLines(svg);
        // The engine's error picture lists the source, so its lines can hold the captured text too.
        Assert.DoesNotContain(painted, l => l.StartsWith("PlantUML ", StringComparison.Ordinal));
        foreach (var line in lines)
            Assert.Contains(Collapse(line), painted);
    }

    [Fact]
    public void A_captured_include_line_does_not_draw_the_file_it_names()
    {
        // Before 3.30.1 this drew the file's text into the note: the diagram is valid either way.
        var include = "!include " + _secretFile.Replace('\\', '/');

        var svg = RenderedDiagram.Svg(DiagramFor("{\n  \"note\": \"before\"\n}\n" + include + "\nafter"));

        Assert.DoesNotContain("KRONIKOL-SECRET-MARKER", svg);
        Assert.Contains(include, PaintedLines(svg));
    }

    [Fact]
    public void A_captured_carriage_return_does_not_start_a_line_the_engine_acts_on()
    {
        // The Java preprocessor reads a lone carriage return as a line break. The text after one began a line of its own
        // past every line-start escape: `!include` drew the named file, `'` dropped the rest as a comment and `@enduml`
        // ended the diagram (DIAGRAM_COLOURS_PLAN §12.5).
        string[] lines = ["x\r!include " + _secretFile.Replace(Path.DirectorySeparatorChar, '/'), "y\r'kept", "z\r@enduml"];

        var svg = RenderedDiagram.Svg(DiagramFor(string.Join("\n", lines)));

        Assert.DoesNotContain("KRONIKOL-SECRET-MARKER", svg);
        // The pin paints the carriage return and this engine does not, so spaces are left out of the comparison.
        var painted = PaintedLines(svg).Select(l => l.Replace(" ", "")).ToList();
        Assert.DoesNotContain(painted, l => l.StartsWith("PlantUML", StringComparison.Ordinal));
        foreach (var line in lines)
            Assert.Contains(line.Replace("\r", "").Replace(" ", ""), painted);
    }

    [Fact]
    public void A_step_bar_draws_a_backslash_as_a_backslash()
    {
        // A bar wrote `\<U+200B>` after every backslash from 3.0.78, and this engine reads the `\<` as an escape inside a
        // one-line statement: each backslash in a doc string or a table cell painted "U+200B>" (DIAGRAM_COLOURS_PLAN §12.5).
        var bar = StepBarPlantUml.Build("Given a path", [new StepBarTable(null, [["Col"], [@"C:\temp"]])], @"C:\temp\new");

        var svg = RenderedDiagram.Svg(BarDiagram(bar));

        var painted = PaintedLines(svg);
        Assert.DoesNotContain(painted, l => l.StartsWith("PlantUML ", StringComparison.Ordinal) || l.Contains("U+200B", StringComparison.Ordinal));
        Assert.Contains(@"C:\temp\new", painted);
        Assert.Contains(@"C:\temp", painted);
    }

    [Fact]
    public void The_render_error_placeholder_draws_its_note()
    {
        // With no participant for `hnote across` to span, the Java engine drew its syntax-error picture instead.
        var svg = RenderedDiagram.Svg(DefaultDiagramsFetcher.RenderErrorPlantUml(new TimeoutException("no answer")));

        var painted = PaintedLines(svg);
        Assert.DoesNotContain(painted, l => l.StartsWith("PlantUML ", StringComparison.Ordinal) || l.Contains("Syntax Error", StringComparison.Ordinal));
        Assert.Contains(painted, l => l.Contains("diagram could not be generated: TimeoutException: no answer", StringComparison.Ordinal));
    }

    /// <summary>The PlantUML Kronikol writes for a 400 whose captured body is <paramref name="body"/>.</summary>
    private static string DiagramFor(string body)
    {
        var logs = new[]
        {
            new RequestResponseLog("Escapes", "escapes-1", HttpMethod.Post, "{\"sku\":\"A1\"}", new Uri("http://example.com/api/orders"),
                [], "Orders API", "Caller", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false),
            new RequestResponseLog("Escapes", "escapes-1", HttpMethod.Post, body, new Uri("http://example.com/api/orders"),
                [("Content-Type", "text/plain")], "Orders API", "Caller", RequestResponseType.Response, Guid.NewGuid(), Guid.NewGuid(), false,
                StatusCode: HttpStatusCode.BadRequest),
        };
        return PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.Single().PlainText;
    }

    /// <summary>The PlantUML Kronikol writes for a scenario whose step bar is <paramref name="bar"/>, then one call.</summary>
    private static string BarDiagram(string bar)
    {
        RequestResponseLog Marker(bool start) => new(
            TestName: "Escapes", TestId: "escapes-1", Method: "", Content: "", Uri: new Uri("http://override.com"), Headers: [],
            ServiceName: "", CallerName: "", Type: RequestResponseType.Request, TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(), TrackingIgnore: false)
        {
            IsOverrideStart = start, IsOverrideEnd = !start, MarkerKind = DiagramMarkerKind.Step,
            PlantUml = start ? "\n" + bar + "\n\n\n" : null,
        };
        var logs = new[]
        {
            Marker(start: true), Marker(start: false),
            new RequestResponseLog("Escapes", "escapes-1", HttpMethod.Get, null, new Uri("http://example.com/api/orders"),
                [], "Orders API", "Caller", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false),
            new RequestResponseLog("Escapes", "escapes-1", HttpMethod.Get, "{}", new Uri("http://example.com/api/orders"),
                [("Content-Type", "application/json")], "Orders API", "Caller", RequestResponseType.Response, Guid.NewGuid(), Guid.NewGuid(), false,
                StatusCode: HttpStatusCode.OK),
        };
        return PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.Single().PlainText;
    }

    /// <summary>
    /// The painted text, one entry per drawn line, spaces collapsed. A self-closing element is a run drawn at no width,
    /// such as this engine's carriage return; the old pattern read it as the start of a run holding the next element.
    /// </summary>
    private static List<string> PaintedLines(string svg) =>
        Regex.Matches(svg, @"<text\b([^>]*?)(?:/>|>([\s\S]*?)</text>)")
            .Select(m => (
                Y: double.Parse(Regex.Match(m.Groups[1].Value, @"\by=""([\d.]+)""").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                X: double.Parse(Regex.Match(m.Groups[1].Value, @"\bx=""([\d.]+)""").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                Text: WebUtility.HtmlDecode(m.Groups[2].Value)))
            .GroupBy(t => Math.Round(t.Y))
            .OrderBy(g => g.Key)
            .Select(g => Collapse(string.Join(" ", g.OrderBy(t => t.X).Select(t => t.Text))))
            .ToList();

    private static string Collapse(string text) => Regex.Replace(text.Replace('\u00a0', ' ').Replace("\u200b", ""), @"\s+", " ").Trim();
}
