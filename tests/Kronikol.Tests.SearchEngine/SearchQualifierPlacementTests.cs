using Jint;

namespace Kronikol.Tests.SearchEngine;

/// <summary>
/// The search's qualifiers and its tag expressions, run in the shipped scripts (5.2.7, from the review of 5.0.0). A
/// qualifier narrows the search by AND whatever operator stands beside it, as the wiki's Search-Syntax says, and the
/// operator it leaves dangling goes with it, a legacy and or or too (@smoke and dep:x left a dangling and that matched
/// nothing, until 5.2.7). A not or !! before it negates it; in quotes it is text; inside a group that !! or not negates
/// it stays text and the bar says so (until 5.2.7 !!(dep:x) showed the scenarios calling x, and not dep:x those too).
/// A tag expression reads both sides of each and and or (until 5.2.7 a side it could answer without was left unread,
/// and the expression stopped there), and a bracket written against a tag is a word of its own.
/// </summary>
public class SearchQualifierPlacementTests : IDisposable
{
    private readonly Engine _engine = new();

    public SearchQualifierPlacementTests()
    {
        foreach (var resource in new[] { "advanced-search.js", "report-search-function.js" })
            _engine.Execute(Load(resource));
        // the percentiles come from the page, and there is none here
        _engine.Execute("var document = { querySelector: function () { return null; } };");
        _engine.Execute("""
            function __split(input) { var p = split_search_qualifiers(input); return JSON.stringify({ quals: p.quals.length, rest: p.rest, bad: p.bad }); }
            function __first(input, deps) { return split_search_qualifiers(input).quals[0]({ deps: new Set(deps.split('|')) }); }
            function __tags(expr, tags) { return evaluateTagExpression(expr, new Set(tags.split(',').filter(Boolean))); }
            function __legacy(input, tags) {
                var s = splitLegacyTagExpression(input);
                return JSON.stringify([s.textInput, s.tagExpr === null ? null : evaluateTagExpression(s.tagExpr, new Set(tags.split(',').filter(Boolean)))]);
            }
            """);
    }

    private static string Load(string name)
    {
        var assembly = typeof(Kronikol.Reports.ReportGenerator).Assembly;
        var resourceName = assembly.GetManifestResourceNames().First(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resourceName)!);
        return reader.ReadToEnd();
    }

    // the qualifiers read, the rest of the search, and the qualifiers that stay text
    private (int Quals, string Remainder, string Bad) Split(string input)
    {
        using var json = System.Text.Json.JsonDocument.Parse(_engine.Invoke("__split", input).AsString());
        var root = json.RootElement;
        return (root.GetProperty("quals").GetInt32(), root.GetProperty("rest").GetString()!,
            string.Join(" | ", root.GetProperty("bad").EnumerateArray().Select(b => b.GetString())));
    }

    [Theory]
    [InlineData("dep:payment", 1, "")]
    [InlineData("@smoke && dep:payment", 1, "@smoke")]
    [InlineData("@smoke and dep:payment", 1, "@smoke")]
    [InlineData("dep:payment and @smoke", 1, "@smoke")]
    [InlineData("@smoke and dep:payment and @api", 1, "@smoke and @api")]
    [InlineData("@smoke and not dep:payment", 1, "@smoke")]
    [InlineData("@smoke or dep:payment", 1, "@smoke")]
    [InlineData("dep:a || @smoke", 1, "@smoke")]
    [InlineData("(dep:a || dep:b)", 2, "")]
    [InlineData("@smoke && (duration:>1s)", 1, "@smoke")]
    [InlineData("!!(@slow) && dep:payment", 1, "!!(@slow)")]
    public void A_qualifier_narrows_the_search_and_takes_the_operator_it_leaves_with_it(string input, int quals, string rest)
    {
        Assert.Equal((quals, rest, ""), Split(input));
    }

    [Theory]
    [InlineData("!!(dep:payment)", "dep:payment")]
    [InlineData("not (dep:payment)", "dep:payment")]
    [InlineData("@smoke && !!(dep:a || @b)", "dep:a")]
    [InlineData("!!(@a && (dep:a || dep:b))", "dep:a", "dep:b")]
    public void A_qualifier_inside_a_negated_group_stays_text_and_marks_the_search(string input, params string[] bad)
    {
        Assert.Equal((0, input, string.Join(" | ", bad)), Split(input));
    }

    [Fact]
    public void A_qualifier_inside_quotes_is_text()
    {
        Assert.Equal((0, "\"order dep:x\"", ""), Split("\"order dep:x\""));
    }

    [Theory]
    [InlineData("dep:payment", true)]
    [InlineData("!!dep:payment", false)]
    [InlineData("not dep:payment", false)]
    [InlineData("NOT dep:payment", false)]
    public void Not_or_bangs_before_a_qualifier_negate_it(string input, bool calls)
    {
        Assert.Equal(calls, _engine.Invoke("__first", input, "Payment Service").AsBoolean());
    }

    // duration:"" threw from the split, and the search did nothing at all
    [Fact]
    public void An_empty_duration_is_a_qualifier_that_cannot_be_read()
    {
        Assert.Equal((0, "duration:\"\"", "duration:\"\""), Split("duration:\"\""));
    }

    // a dependency's name was compared with every letter outside a to z dropped: dep:Ödeme matched Deme Service, and a
    // name in other letters (支付) was no name at all, so the qualifier could not be read
    [Theory]
    [InlineData("dep:ödeme", "Ödeme Servisi", true)]
    [InlineData("dep:ödeme", "Deme Service", false)]
    [InlineData("dep:支付", "支付服务", true)]
    [InlineData("dep:支付", "Payment Service", false)]
    public void A_dependency_s_name_keeps_its_letters_in_every_alphabet(string input, string dependency, bool calls)
    {
        Assert.Equal(calls, _engine.Invoke("__first", input, dependency).AsBoolean());
    }

    [Theory]
    [InlineData("@a and @b or @c", "c", true)]
    [InlineData("(@a or @b) and @c", "a", false)]
    [InlineData("(@a or @b) and @c", "b,c", true)]
    [InlineData("@a or @b and @c", "a", true)]
    [InlineData("@a and @b and @c", "c", false)]
    [InlineData("not @a and @b", "b", true)]
    [InlineData("@a or @b or @c", "c", true)]
    public void A_tag_expression_reads_both_sides_of_each_operator(string expr, string tags, bool matches)
    {
        Assert.Equal(matches, _engine.Invoke("__tags", expr, tags).AsBoolean());
    }

    // the wiki's Search-Syntax writes (@ui or @api) and not @slow: until 5.2.7 its brackets were read into the words
    // beside them, (@ui became text and @api) a tag named api), and the expression matched nothing it should
    [Theory]
    [InlineData("(@ui or @api) and not @slow", "api", "", true)]
    [InlineData("(@ui or @api) and not @slow", "ui,slow", "", false)]
    [InlineData("(@ui or @api) and not @slow", "smoke", "", false)]
    [InlineData("((@a or @b)) and @c", "b,c", "", true)]
    [InlineData("(@\"a tag\" or @b) and @c", "a tag,c", "", true)]
    [InlineData("@issue(12) and @api", "issue(12),api", "", true)]
    [InlineData("(@issue(12)) and @api", "issue(12),api", "", true)]
    [InlineData("@smoke getOrder(id)", "smoke", "getOrder(id)", true)]
    public void A_bracket_written_against_a_tag_is_a_word_of_its_own(string input, string tags, string text, bool matches)
    {
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(new object[] { text, matches }), _engine.Invoke("__legacy", input, tags).AsString());
    }

    public void Dispose() => _engine.Dispose();
}
