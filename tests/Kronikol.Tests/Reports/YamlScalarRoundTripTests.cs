using Kronikol.Reports;
using YamlDotNet.RepresentationModel;

namespace Kronikol.Tests.Reports;

/// <summary>
/// <see cref="YamlExtensions.ToYamlScalar"/> decides between three forms, and the decision is a
/// predicate — the kind of code that is right for every case anyone thought of and wrong for the one
/// they did not. So it is not reviewed, it is measured: every value below is written, handed to a real
/// YAML parser, and compared with what went in.
///
/// <para>What it replaced did not escape at all. <c>SanitiseForYml</c> substituted characters —
/// a captured body of <c>{"item":"Widget"}</c> was written as <c>("item":"Widget")</c> — and left
/// newlines alone, which is what actually broke the file: a multi-line assertion message put its second
/// line at column 0 and the document stopped parsing. Every failing run produces one of those, so
/// <c>TestRunReport.yml</c> did not parse for exactly the runs anyone would want to read it for.</para>
/// </summary>
public class YamlScalarRoundTripTests
{
    [Theory]
    // Ordinary values, which must stay plain or the format loses the point of being YAML.
    [InlineData("Orders")]
    [InlineData("Place an order for <item>")]
    [InlineData("/api/orders")]
    [InlineData("Microsoft Windows 10.0.26200")]
    [InlineData("OrderTests.cs:line 42")]
    // The characters SanitiseForYml used to delete.
    [InlineData("{\"item\":\"Widget\"}")]
    [InlineData("[1, 2, 3]")]
    [InlineData("Expected: 4173")]
    [InlineData("#hashtag")]
    [InlineData("a & b")]
    [InlineData("SELECT * FROM orders")]
    [InlineData("100%")]
    [InlineData("user@example.com")]
    [InlineData("`code`")]
    [InlineData("a | b")]
    [InlineData("!important")]
    // Newlines: the shape that stopped the document parsing at all.
    [InlineData("Expected: 4173\nActual: 3902")]
    [InlineData("   at OrderTests.Place() in OrderTests.cs:line 42\n   at Xunit.Run()")]
    [InlineData("first\n\nthird")]
    [InlineData("trailing newline\n")]
    [InlineData("two trailing newlines\n\n")]
    [InlineData("@startuml\nA -> B\n@enduml")]
    // A first line that is already indented cannot be a literal block without an explicit indicator.
    [InlineData("   indented first line\nsecond")]
    [InlineData("\ttab first\nsecond")]
    // Carriage returns, which a literal block would silently normalise away.
    [InlineData("windows\r\nline endings")]
    [InlineData("bare\rcarriage")]
    // Values that a plain scalar would hand back as some other type.
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("yes")]
    [InlineData("No")]
    [InlineData("on")]
    [InlineData("off")]
    [InlineData("null")]
    [InlineData("~")]
    [InlineData("n")]
    [InlineData("123")]
    [InlineData("-17")]
    [InlineData("1.250")]
    [InlineData("1e10")]
    [InlineData("0x1F")]
    [InlineData("1:30:00")]
    [InlineData(".inf")]
    [InlineData(".nan")]
    [InlineData("2026-01-01")]
    [InlineData("2026-01-01T10:00:00Z")]
    // Whitespace at the edges, which a plain scalar strips.
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("  ")]
    [InlineData("")]
    // Quotes and backslashes, which the double-quoted form has to escape.
    [InlineData("he said \"hi\"")]
    [InlineData("C:\\Code\\Kronikol")]
    [InlineData("mixed \" and \\ and \n")]
    // Control characters, which only the double-quoted form can carry.
    [InlineData("bell\u0007here")]
    [InlineData("null\u0000byte")]
    // Non-ASCII, which must survive untouched.
    [InlineData("naïve café")]
    [InlineData("日本語のシナリオ")]
    [InlineData("emoji 🎯 here")]
    public void A_value_reads_back_as_itself(string value)
    {
        const string prefix = "  Key: ";

        var yaml = "Root:\n" + prefix + value.ToYamlScalar(new string(' ', prefix.Length)) + "\n";

        Assert.Equal(value, ReadBack(yaml));
    }

    /// <summary>
    /// The point of the block form is that it is readable. If everything ended up double-quoted the
    /// round-trip test above would still pass, and a stack trace in the report would be one long line
    /// with <c>\n</c> in it — so the choice itself is pinned, not just its correctness.
    /// </summary>
    [Fact]
    public void A_multi_line_value_is_written_as_a_literal_block()
    {
        var rendered = "Expected: 4173\nActual: 3902".ToYamlScalar("    ");

        Assert.StartsWith("|", rendered, StringComparison.Ordinal);
        Assert.Contains("\n    Expected: 4173\n    Actual: 3902", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ordinary_value_is_written_plain()
    {
        Assert.Equal("Place an order", "Place an order".ToYamlScalar("    "));
    }

    /// <summary>
    /// Reads the single scalar back out of <c>Root: {Key: ...}</c>, so the assertion compares the value a
    /// consumer would receive rather than the text Kronikol wrote.
    /// </summary>
    private static string ReadBack(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));

        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        var inner = (YamlMappingNode)root.Children[new YamlScalarNode("Root")];
        return ((YamlScalarNode)inner.Children[new YamlScalarNode("Key")]).Value ?? "";
    }
}
