using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// Captured text is drawn as captured, whatever PlantUML would otherwise make of it (the escape patch that
/// DIAGRAM_COLOURS_PLAN §12 deferred, shipped in 3.30.1). Before creole reads a note, PlantUML's preprocessor
/// reads every line of the source: a line opening with <c>'</c> is a comment and vanishes, <c>/'</c> opens a
/// block comment, a line opening with <c>!</c> is a directive (<c>!include</c> draws a local file under the
/// Java renderer), <c>%name(…)</c> is a builtin call anywhere in a line, a line reading <c>end note</c> can
/// close the note, and under Java a line opening with <c>@end</c> or <c>@start</c> ends the diagram and a line
/// ending in <c>\</c> is joined to the next. Creole then takes a literal <c>~</c> as its escape, a line opening
/// with <c>=</c>, <c>|</c> or <c>..</c> as a heading, a table or a separator, <c>&lt;&lt;x&gt;&gt;</c> as
/// guillemets and <c>&amp;#39;</c> as a character reference. Each of these is escaped as <c>&lt;U+hhhh&gt;</c>,
/// which both engines paint as the one character, except the reference: under Kronikol's note wrap width the
/// engine decodes code points before references, so a zero-width space breaks it instead (measured:
/// <c>plans/DIAGRAM_COLOURS_PLAN.harness/preproc-probe.js</c>, with <c>PREFIX=kronikol</c>).
/// </summary>
public class CapturedTextEscapeTests
{
    [Theory]
    // A line comment: the preprocessor drops the line, indented or not.
    [InlineData("'a', 'b'", "<U+0027>a', 'b'")]
    [InlineData("  'a',", "  <U+0027>a',")]
    [InlineData("\t'a',", "\t<U+0027>a',")]
    [InlineData("x 'a'", "x 'a'")]
    // A block comment opens on a line that starts with it, and breaks the diagram when nothing closes it.
    [InlineData("/' not a comment", "<U+002F>' not a comment")]
    [InlineData("  /' x", "  <U+002F>' x")]
    [InlineData("x /' y", "x /' y")]
    // Every line-initial `!`: the directive set differs by engine version, and `!important` costs nothing.
    [InlineData("!define FOO BAR", "<U+0021>define FOO BAR")]
    [InlineData("  !include foo.puml", "  <U+0021>include foo.puml")]
    [InlineData("!important", "<U+0021>important")]
    [InlineData("x !define y", "x !define y")]
    // The Java engine ends (or starts) a diagram at a line opening with @end or @start.
    [InlineData("@enduml", "<U+0040>enduml")]
    [InlineData("  @startuml", "  <U+0040>startuml")]
    [InlineData("@EndJson", "<U+0040>EndJson")]
    [InlineData("@param x", "@param x")]
    [InlineData("x @enduml", "x @enduml")]
    // The note terminator, in every spelling the engine accepts, as a whole line.
    [InlineData("end note", "<U+0065>nd note")]
    [InlineData("END NOTE", "<U+0045>ND NOTE")]
    [InlineData("  endnote  ", "  <U+0065>ndnote  ")]
    [InlineData("end hnote", "<U+0065>nd hnote")]
    [InlineData("end\trnote", "<U+0065>nd\trnote")]
    [InlineData("end note x", "end note x")]
    [InlineData("the end note", "the end note")]
    // A line that is only `{{` opens an embedded diagram.
    [InlineData("{{", "<U+007B>{")]
    [InlineData("  {{json", "  <U+007B>{json")]
    [InlineData("{{ x", "{{ x")]
    [InlineData("{{name}}", "{{name}}")]
    // A builtin call is evaluated anywhere in a line: %date() paints the date.
    [InlineData("%upper(\"x\")", "<U+0025>upper(\"x\")")]
    [InlineData("a%date()b", "a<U+0025>date()b")]
    [InlineData("%_x(1)", "<U+0025>_x(1)")]
    [InlineData("100%(approx)", "100%(approx)")]
    [InlineData("q=%20a", "q=%20a")]
    [InlineData("%upper (\"x\")", "%upper (\"x\")")]
    [InlineData("%upper", "%upper")]
    // A literal tilde: creole's escape before / < . " ] # * _ - [ and a wave-underline pair as ~~x~~.
    [InlineData("~/.bashrc", "<U+007E>/.bashrc")]
    [InlineData("\"~\"", "\"<U+007E>\"")]
    [InlineData("~~x~~", "<U+007E><U+007E>x<U+007E><U+007E>")]
    [InlineData("a~b", "a<U+007E>b")]
    [InlineData("~<b>x</b>", "<U+007E>~<b>x~</b>")]
    // A heading, a table and a separator open at the start of a line; `~=` and `~|` paint their tilde.
    [InlineData("= heading", "<U+003D> heading")]
    [InlineData("  = x", "  <U+003D> x")]
    [InlineData("==x==", "<U+003D>=x==")]
    [InlineData("| a | b |", "<U+007C> a | b |")]
    [InlineData("  | c |", "  <U+007C> c |")]
    [InlineData("| a", "| a")]
    [InlineData("..x..", "<U+002E>.x..")]
    [InlineData("....", "<U+002E>...")]
    [InlineData("..", "<U+002E>.")]
    [InlineData("x..y..z", "x..y..z")]
    [InlineData("a | b", "a | b")]
    // Only what creole restyles: Kronikol's own cap footnote and a relative path open with dots too.
    [InlineData("... (90 more rows not shown)", "... (90 more rows not shown)")]
    [InlineData("../lib/x.js", "../lib/x.js")]
    [InlineData("...", "...")]
    // A rule of dashes or underscores draws without its characters; the pairs escape misses an odd one.
    [InlineData("--", "<U+002D>-")]
    [InlineData("---", "<U+002D>--")]
    [InlineData("___", "<U+005F>__")]
    [InlineData("--- x", "--- x")]
    // Guillemets: `<<` with a later `>>` on the line, unless the second `<` is escaped as a tag already.
    [InlineData("a << b >> c", "a <U+003C>< b >> c")]
    [InlineData("<<\"x\">>", "<U+003C><\"x\">>")]
    [InlineData("<<x>>", "<~<x>>")]
    [InlineData("cat <<EOF", "cat <~<EOF")]
    [InlineData("a << b", "a << b")]
    [InlineData("x >> y", "x >> y")]
    // A decimal character reference is decoded; a named or hex one is not.
    [InlineData("&#39;", "&<U+200B>#39;")]
    [InlineData("a &#128512; b", "a &<U+200B>#128512; b")]
    [InlineData("&amp; &#x27; &#; &#12", "&amp; &#x27; &#; &#12")]
    // The Java engine joins a line ending in an odd run of backslashes to the next line.
    [InlineData("abc\\", "abc<U+005C>")]
    [InlineData("abc\\\\", "abc\\\\")]
    [InlineData("abc\\\\\\", "abc\\\\<U+005C>")]
    [InlineData("\\", "<U+005C>")]
    [InlineData("abc\\\r\ndef", "abc<U+005C>\r\ndef")]
    // Several on one line, and the rules on every line of a body.
    [InlineData("'~%upper(x)", "<U+0027><U+007E><U+0025>upper(x)")]
    [InlineData("!%upper(x)", "<U+0021><U+0025>upper(x)")]
    [InlineData("x\n'a',\n@enduml\ny", "x\n<U+0027>a',\n<U+0040>enduml\ny")]
    public void EscapeCreoleMarkup_escapes_what_the_engine_would_act_on(string input, string expected)
    {
        Assert.Equal(expected, PlantUmlCreator.EscapeCreoleMarkup(input));
    }

    [Theory]
    // Step, test and assertion text keeps its markup, and its own tildes, but a builtin call is evaluated
    // wherever it sits in a line.
    [InlineData("%upper(\"x\") step", "<U+0025>upper(\"x\") step")]
    [InlineData("Discount of 10%(approx)", "Discount of 10%(approx)")]
    [InlineData("~%upper(x)", "~<U+0025>upper(x)")]
    [InlineData("**bold** and ~/", "**bold** and ~/")]
    public void EscapeLoaderMarkup_escapes_a_builtin_call(string input, string expected)
    {
        Assert.Equal(expected, PlantUmlCreator.EscapeLoaderMarkup(input));
    }

    // ── Lines Kronikol starts itself ────────────────────────────────────────

    [Theory]
    [InlineData("'bcc", "<U+0027>bcc")]
    [InlineData("/'x", "<U+002F>'x")]
    [InlineData("!x", "<U+0021>x")]
    [InlineData("@endx", "<U+0040>endx")]
    [InlineData("endnote", "<U+0065>ndnote")]
    [InlineData("=x", "<U+003D>x")]
    [InlineData("|x|", "<U+007C>x|")]
    [InlineData("..x..", "<U+002E>.x..")]
    [InlineData("---", "<U+002D>--")]
    [InlineData("*x", "~*x")]
    // Controls: what creole reads only as a whole line is left alone when the line says otherwise.
    [InlineData("|x", "|x")]
    [InlineData("..x", "..x")]
    [InlineData("bcc", "bcc")]
    public void A_run_cut_by_the_width_bound_escapes_the_line_it_starts(string tail, string escapedTail)
    {
        // No punctuation in the chunk's last 24 characters, so the cut falls at 120 and the tail is the next line.
        var run = new string('a', PlantUmlCreator.MaxUnbrokenRunChars) + tail;

        var lines = PlantUmlCreator.WrapUnbreakableRuns(run).Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.Equal(escapedTail, lines[1]);
    }

    [Fact]
    public void An_assertion_line_the_width_bound_starts_with_a_quote_is_not_a_comment()
    {
        var message = new string('x', 105) + " 'quoted' value";

        var lines = DiagramWidth.WrapBlockNoteBody(message).Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.StartsWith("<U+0027>quoted' value", lines[1]);
    }

    [Theory]
    [InlineData("end note", "<U+0065>nd note")]
    [InlineData("END NOTE", "<U+0045>ND NOTE")]
    [InlineData("!include secrets.txt", "<U+0021>include secrets.txt")]
    [InlineData("'dropped'", "<U+0027>dropped'")]
    [InlineData("@enduml", "<U+0040>enduml")]
    [InlineData("path\\", "path<U+005C>")]
    [InlineData("%date() now", "<U+0025>date() now")]
    // Assertion text keeps its creole: a heading is the author's own.
    [InlineData("= heading", "= heading")]
    public void An_assertion_note_escapes_what_the_preprocessor_would_act_on(string line, string expected)
    {
        Assert.Equal("first\n" + expected, DiagramWidth.WrapBlockNoteBody("first\n" + line));
    }

    [Fact]
    public void A_response_split_across_diagrams_is_cut_between_lines()
    {
        // Every line opens with a quote, so a cut inside a line would start a continuation with a raw one.
        var body = string.Join("\n", Enumerable.Range(0, 400).Select(i => $"'line {i:000}: " + new string('x', 40)));
        var logs = new[]
        {
            Request("GET", "http://example.com/api/report", null),
            Response(HttpStatusCode.OK, body, "text/plain"),
        };

        var diagrams = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.Select(p => p.PlainText).ToList();

        Assert.True(diagrams.Count > 1, "the fixture must be split");
        var bodyLines = diagrams.SelectMany(NoteBodyLines).Where(l => l.Contains("line ")).ToList();
        Assert.Equal(400, bodyLines.Count);
        Assert.All(bodyLines, l => Assert.Matches(@"^<U\+0027>line \d{3}: x{40}$", l));
    }

    // ── What the engines paint ──────────────────────────────────────────────

    /// <summary>A captured body holding every hazard this patch escapes, one per line.</summary>
    internal static readonly string[] HazardLines =
    [
        "'a', 'b'",
        "/' not a comment",
        "!define FOO BAR",
        "FOO stays",
        "%date() stays",
        "%upper(\"x\") stays",
        "@enduml",
        "@startuml",
        "end note",
        "{{",
        "~/.bashrc and \"~\" and ~~wave~~",
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
    [Trait("Category", "Integration")]
    public void The_node_renderer_paints_every_hazard_line_as_captured()
    {
        Assert.SkipWhen(!NodeIsAvailable(), "Node.js not available on PATH");

        var result = NodeJsPlantUmlRenderer.RenderMany([HazardDiagram(string.Join("\n", HazardLines))]).Single();

        Assert.True(result.Succeeded, result.Error);
        var painted = PaintedLines(result.Svg!);
        // The engine's error picture lists the source, so its lines can hold the captured text too.
        Assert.DoesNotContain(painted, l => l.StartsWith("PlantUML ", StringComparison.Ordinal));
        Assert.DoesNotContain(painted, l => l.Contains("Syntax Error", StringComparison.Ordinal));
        foreach (var line in HazardLines)
            Assert.Contains(Collapse(line), painted);
    }

    internal static string Collapse(string text) => Regex.Replace(text.Replace('\u00a0', ' ').Replace("\u200b", ""), @"\s+", " ").Trim();

    /// <summary>
    /// The PlantUML Kronikol writes for a call whose response carries <paramref name="body"/>, as text.
    /// </summary>
    internal static string HazardDiagram(string body)
    {
        var logs = new[]
        {
            Request("POST", "http://example.com/api/orders", "{\"sku\":\"A1\"}"),
            Response(HttpStatusCode.BadRequest, body, "text/plain"),
        };
        return PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.Single().PlainText;
    }

    /// <summary>The painted text of an SVG, one entry per drawn line (text elements sharing a baseline).</summary>
    internal static List<string> PaintedLines(string svg) =>
        Regex.Matches(svg, @"<text\b([^>]*)>([\s\S]*?)</text>")
            .Select(m => (
                Y: double.Parse(Regex.Match(m.Groups[1].Value, @"\by=""([\d.]+)""").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                X: double.Parse(Regex.Match(m.Groups[1].Value, @"\bx=""([\d.]+)""").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                Text: WebUtility.HtmlDecode(m.Groups[2].Value)))
            .GroupBy(t => Math.Round(t.Y))
            .OrderBy(g => g.Key)
            .Select(g => Collapse(string.Join(" ", g.OrderBy(t => t.X).Select(t => t.Text))))
            .ToList();

    private static IEnumerable<string> NoteBodyLines(string plantUml)
    {
        var inNote = false;
        foreach (var line in plantUml.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!inNote && trimmed.StartsWith("note", StringComparison.Ordinal)) { inNote = true; continue; }
            if (inNote && trimmed == "end note") { inNote = false; continue; }
            if (inNote) yield return line.TrimEnd('\r');
        }
    }

    private static RequestResponseLog Request(string method, string uri, string? content) => new(
        TestName: "Escapes", TestId: "escapes-1", Method: HttpMethod.Parse(method), Content: content, Uri: new Uri(uri),
        Headers: [], ServiceName: "Orders API", CallerName: "Caller", Type: RequestResponseType.Request,
        TraceId: Guid.NewGuid(), RequestResponseId: Guid.NewGuid(), TrackingIgnore: false);

    private static RequestResponseLog Response(HttpStatusCode status, string content, string contentType) => new(
        TestName: "Escapes", TestId: "escapes-1", Method: HttpMethod.Get, Content: content, Uri: new Uri("http://example.com/api/orders"),
        Headers: [("Content-Type", contentType)], ServiceName: "Orders API", CallerName: "Caller", Type: RequestResponseType.Response,
        TraceId: Guid.NewGuid(), RequestResponseId: Guid.NewGuid(), TrackingIgnore: false, StatusCode: status);

    private static bool NodeIsAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("node", "--version") { RedirectStandardOutput = true, UseShellExecute = false });
            process!.WaitForExit(10_000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
