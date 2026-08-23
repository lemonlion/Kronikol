using System.Text.RegularExpressions;

namespace Kronikol.Tests.PlantUml.Ikvm;

/// <summary>
/// Which of the statement-length limits belong to PlantUML and which to the TeaVM JavaScript build
/// Kronikol ships for <c>BrowserJs</c> / <c>NodeJs</c> rendering. Measured against real Java PlantUML
/// through IKVM:
/// <list type="bullet">
/// <item><description>the <b>2,000-character message limit is PlantUML's own</b> — Java refuses 2,001
/// exactly as the JS build does;</description></item>
/// <item><description>the <b>block-opener limit (~1,476) and the coloured-note-bar crash (~1,458) are
/// artifacts of the JS build</b> — Java draws both far past those points.</description></item>
/// </list>
/// <para>
/// The distinction is worth pinning because it says which caps are load-bearing everywhere and which
/// exist only for the default renderer.
/// </para>
/// </summary>
public class IkvmStatementLimitTests
{
    private static string RenderSvg(string body)
        => System.Text.Encoding.UTF8.GetString(
            IkvmPlantUmlRenderer.Render($"@startuml\n{body}\n@enduml", PlantUmlImageFormat.Svg));

    /// <summary>
    /// Whether PlantUML actually drew a <em>sequence</em> diagram.
    /// <para>
    /// This cannot be "is the label in the SVG?". When a message statement is too long the parser falls
    /// back to reading the source as a <em>class</em> diagram, and that fallback often succeeds — drawing
    /// classes named after the tokens, echoing the label text, and emitting no <c>Syntax Error</c> banner
    /// at all. A substring check on the label passes for both, which is exactly how an earlier version of
    /// this file reported "Java has no limit" and had to be corrected by CI.
    /// </para>
    /// <para>
    /// The signal that separates them: a sequence diagram draws each participant <b>twice</b> — a head box
    /// and a foot box. The class-diagram fallback draws it once.
    /// </para>
    /// </summary>
    private static bool DrewSequenceDiagram(string body)
    {
        var svg = RenderSvg(body);
        if (svg.Contains("Syntax Error", StringComparison.Ordinal))
            return false;
        var texts = Regex.Matches(svg, @"<text\b[^>]*>([\s\S]*?)</text>").Select(m => m.Groups[1].Value);
        return texts.Count(t => t == "a") == 2;
    }

    /// <summary>A statement of exactly <paramref name="total"/> characters, padding the label.</summary>
    private static string StatementOf(string prefix, int total) => prefix + new string('x', total - prefix.Length);

    [Fact]
    public void The_two_thousand_character_message_limit_is_plantumls_own()
    {
        // Identical to the JS build's boundary, so this cap is load-bearing for every renderer — not a
        // concession to the engine that happens to be the default.
        Assert.True(DrewSequenceDiagram(StatementOf("a -> b: ", 2000)), "2000 characters should parse");
        Assert.False(DrewSequenceDiagram(StatementOf("a -> b: ", 2001)), "2001 characters should not");
    }

    [Fact]
    public void A_block_label_past_the_js_builds_limit_still_parses_in_java()
    {
        // The JS build gives up on a `loop` label somewhere around 1,476. Java does not, so
        // MaxBlockLabelChars is a JS-build concession.
        var opener = StatementOf("loop ", 2000);

        Assert.True(DrewSequenceDiagram($"a -> b: x\n{opener}\na -> b: y\nend"));
    }

    [Fact]
    public void A_long_coloured_note_bar_still_parses_in_java()
    {
        // The step-delimiter bar's own form. Past ~1,458 the JS build overflows its own stack and returns
        // no SVG at all; Java draws it. MaxColouredNoteBarChars is likewise a JS-build concession.
        var bar = "hnote across #black:<color:white>" + new string('s', 3000);

        Assert.True(DrewSequenceDiagram($"a -> b: x\n{bar}"));
    }

    [Fact]
    public void A_long_note_body_still_parses_in_java()
    {
        var body = new string('n', 18000);

        Assert.True(DrewSequenceDiagram($"a -> b: x\nnote left\n{body}\nend note"));
    }
}
