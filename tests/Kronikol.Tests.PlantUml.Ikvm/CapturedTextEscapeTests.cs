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

    /// <summary>The painted text, one entry per drawn line, spaces collapsed.</summary>
    private static List<string> PaintedLines(string svg) =>
        Regex.Matches(svg, @"<text\b([^>]*)>([\s\S]*?)</text>")
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
