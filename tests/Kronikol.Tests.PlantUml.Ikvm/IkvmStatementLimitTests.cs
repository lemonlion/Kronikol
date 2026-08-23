namespace Kronikol.Tests.PlantUml.Ikvm;

/// <summary>
/// Where the statement-length limits live. The TeaVM JavaScript build Kronikol ships for `BrowserJs`
/// and `NodeJs` rendering refuses a message statement past 2,000 characters and overflows its own stack
/// on a long coloured <c>hnote</c> — but **real Java PlantUML has neither limit**, which makes them
/// artifacts of that build rather than of PlantUML's parser.
/// <para>
/// That is worth pinning rather than assuming. It says the truncation `PlantUmlStatementLimits` applies
/// is a concession to the JS engine, not a property of the format: a user rendering through
/// <c>PlantUmlRendering.Local</c> or <c>Server</c> is paying for a limit their renderer does not have,
/// and if the JS engine ever gains parity these caps become dead weight rather than load-bearing.
/// </para>
/// </summary>
public class IkvmStatementLimitTests
{
    private static string RenderSvg(string body)
        => System.Text.Encoding.UTF8.GetString(
            IkvmPlantUmlRenderer.Render($"@startuml\n{body}\n@enduml", PlantUmlImageFormat.Svg));

    /// <summary>A statement of exactly <paramref name="total"/> characters whose label ends in a findable marker.</summary>
    private static string StatementOf(string prefix, int total)
    {
        const string marker = "ENDOFLABEL";
        return prefix + new string('x', total - prefix.Length - marker.Length) + marker;
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(2001)]
    [InlineData(6000)]
    public void Real_java_plantuml_draws_a_message_statement_of_any_length(int length)
    {
        var svg = RenderSvg(StatementOf("a -> b: ", length));

        Assert.DoesNotContain("Syntax Error", svg);
        Assert.Contains("ENDOFLABEL", svg);
    }

    [Fact]
    public void Real_java_plantuml_draws_a_long_coloured_note_bar()
    {
        // The exact form the step-delimiter bar uses. The JS engine dies on this with
        // `RangeError: Maximum call stack size exceeded` past ~1,458 characters.
        var bar = "hnote across #black:<color:white>" + new string('s', 3000) + "ENDOFLABEL";
        var svg = RenderSvg($"a -> b: x\n{bar}");

        Assert.DoesNotContain("Syntax Error", svg);
        Assert.Contains("ENDOFLABEL", svg);
    }

    [Fact]
    public void Real_java_plantuml_draws_a_block_label_past_the_js_engine_limit()
    {
        // 2,000 — the JS engine's *message* limit, and far past its ~1,476 block-opener limit.
        var opener = StatementOf("loop ", 2000);
        var svg = RenderSvg($"a -> b: x\n{opener}\na -> b: y\nend");

        Assert.DoesNotContain("Syntax Error", svg);
        Assert.Contains("ENDOFLABEL", svg);
    }
}
