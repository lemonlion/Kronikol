using System.Text;
using System.Text.RegularExpressions;
using Kronikol.Reports.SearchIndex;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The shipped <see cref="SearchNormalizer"/> is a hand-rolled scan for speed (the regex passes
/// measured as the dominant index-build cost on monster corpora — M3). This suite pins it against
/// a straightforward transliteration of the reference regex implementation
/// (tools/search-bench/normalize.js) over stress content and a deterministic pseudo-corpus, on
/// top of the byte-exact shared vectors.
/// </summary>
public partial class SearchNormalizerEquivalenceTests
{
    // ---- reference implementation: literal transliteration of normalize.js ----

    // Rule 3 since 3.30.1: a creole escape or a code point, decoded in one pass (normalize.js decodeCreoleEscapes).
    [GeneratedRegex("~([/*_\\-\"\\[<#=])|<U\\+([0-9A-Fa-f]{4,6})>")]
    private static partial Regex CreoleEscapeOrCodePoint();

    private static string RefDecodeCreoleEscapes(string s) => CreoleEscapeOrCodePoint().Replace(s, m =>
    {
        if (m.Groups[1].Success) return m.Groups[1].Value;
        var codePoint = Convert.ToInt32(m.Groups[2].Value, 16);
        if (codePoint == 0x200B) return "";
        if (codePoint is >= 0xD800 and <= 0xDFFF or > 0x10FFFF) return m.Value;
        return char.ConvertFromUtf32(codePoint);
    });

    [GeneratedRegex("</?(?:color|font|i|b|size|back)[^>]*>")]
    private static partial Regex MarkupTag();

    [GeneratedRegex(@"\\n[ \t]*")]
    private static partial Regex ArrowLabelBreak();

    [GeneratedRegex("[ \t]+")]
    private static partial Regex WhitespaceRun();

    // Rule 1b, transliterated from normalize.js. Deliberately NOT a call to
    // DiagramWidth.RejoinMarkedLines: this suite exists to check the shipped normalizer against an
    // independent restatement of the reference, and sharing the implementation would check nothing.
    private const string RefJoin = "<U+200B>";
    private const string RefJoinSpace = RefJoin + RefJoin;

    private static bool RefEndsWithOwnMarker(string line, string marker) =>
        line.EndsWith(marker, StringComparison.Ordinal)
        && !(line.Length > marker.Length && line[line.Length - marker.Length - 1] == '~');

    private static string RefRejoinMarkedBreaks(string s)
    {
        if (!s.Contains(RefJoin, StringComparison.Ordinal)) return s;
        var lines = s.Split('\n');
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var last = i == lines.Length - 1;
            if (!last && RefEndsWithOwnMarker(line, RefJoinSpace))
                sb.Append(line, 0, line.Length - RefJoinSpace.Length).Append(' ');
            else if (!last && RefEndsWithOwnMarker(line, RefJoin))
                sb.Append(line, 0, line.Length - RefJoin.Length);
            else
                sb.Append(line).Append(last ? "" : "\n");
        }
        return sb.ToString();
    }

    private static string ReferenceNormalize(string text)
    {
        var s = RefRejoinMarkedBreaks(text.Replace("\r\n", "\n"));
        s = RefDecodeCreoleEscapes(s);
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] is >= 'A' and <= 'Z') chars[i] = (char)(chars[i] + 32);
        s = new string(chars);
        s = MarkupTag().Replace(s, "");
        s = ArrowLabelBreak().Replace(s, "");
        return WhitespaceRun().Replace(s + "\n", " ");
    }

    // ---- pins ----

    public static TheoryData<string> StressInputs() => new(
        "",
        "plain text",
        "MIXED Case İstanbul ẞ",
        "a\r\nb\r\n\r\nc",
        "~*~*bold~*~* ~/~/it~/~/ ~_~_u~_~_ ~-~-strike~-~- ~[link~] ~\"q~\" ~#lead\n~=lead2",
        "<color:gray>[H=v]</color> <font color=\"red\">x</font> <i>bin</i> <b>b</b> <size:10>s</size> <back:yellow>y</back>",
        "<int> <industrial> <band> <notatag> <Color:Gray>UP</Color>",
        "<color:never-closed and text goes on",
        "label\\n        continued\\N\tmore",
        "note left\n<color:gray>[X=aaaa<U+200B>\n<color:gray>bbbb]\n\n{\n  \"k\": \"AAAA<U+200B>\nBBBB\"\n}\nend note\nafter -> b: x",
        // 3.30.0 writes header lines in a computed ink instead of `gray` (NotePalette.HeaderTag).
        "note left\n<color:#686868>[X=aaaa<U+200B>\n<color:#686868>bbbb]\n\n{\n  \"k\": \"v\"\n}\nend note",
        "note right\nflush\nleft\nend note",
        "note over A,B\npayload\nend note",
        "note<<eventNote>> right\nchunkA<U+200B>\nchunkB\nend note",
        "hnote across <<assertionNote>> #90EE90\nexpected X<U+200B><U+200B>\nactual Y\nend note",
        "hnote across <<stepDelimiter>> #black:<color:white>Step 1\nflush\nnext",
        "hnote across #lightyellow : Row 2\nflush",
        "note left\nAAAA\nnote leftovers glue\nend note",
        "alpha~<U+200B>\nbeta",
        "<U+200B>\nleading marker line",
        "a<U+200B>b<U+200B>\nc",
        "trailing marker on last line<U+200B>",
        "notnote left\nx\ny",
        "  a\tb  c   \n\t\n d",
        "~x ~~ ~ tilde survivors",
        "trailing backslash \\",
        "\\n",
        "~",
        "<",
        "a<b&c>d<e",
        // 3.30.1: the code points the generator writes for captured text, and the edges of the rule.
        "<U+0027>a', <U+0021>define X\n<U+0040>enduml <U+0025>date() <U+007E>/x <U+003D> h &<U+200B>#39; c<U+005C>",
        "~<U+0027> <U+0045>ND NOTE <U+007E>~< <U+007E><U+007E>x",
        "<U+D800> <U+FFFFFF> <U+1234567> <U+27> <u+0027> <U+0027 <U+>",
        "<U+1F600> <U+01F600> <U+10FFFF> <U+110000>"
    );

    [Theory]
    [MemberData(nameof(StressInputs))]
    public void Shipped_normalizer_equals_reference_on_stress_inputs(string input)
    {
        Assert.Equal(ReferenceNormalize(input), SearchNormalizer.Normalize(input));
    }

    [Fact]
    public void Shipped_normalizer_equals_reference_on_a_deterministic_pseudo_corpus()
    {
        // xorshift-seeded soup over the alphabet the rules care about, plus realistic fragments
        ulong state = 0x2545F4914F6CDD1DUL;
        ulong Next() { state ^= state << 13; state ^= state >> 7; state ^= state << 17; return state; }
        string[] fragments =
        [
            "note left\n", "end note\n", "~*", "~/", "\\n   ", "<color:gray>", "<color:#686868>", "</font>", "<i>", "\r\n",
            "  ", "\t", "{ \"k\": \"v\" }\n", "POST: /api/x\n", "~[", "~\"", "AAAA\n", "bbbb\n", "<", ">", "~", "\\",
            "note<<eventNote>> right\n", "hnote across <<assertionNote>> #x\n", "hnote across #y : Row\n",
            "across ", "\u0085", "\ufeff", "<U+200B>\n", "<U+200B><U+200B>\n", "~<U+200B>\n",
            "<U+0027>", "<U+0045>", "<U+007E>", "~<U+0027>", "<U+", "0027>", "<U+D800>", "<U+1F600>"
        ];
        for (var doc = 0; doc < 50; doc++)
        {
            var sb = new StringBuilder();
            var pieces = 40 + (int)(Next() % 60);
            for (var p = 0; p < pieces; p++)
                sb.Append(fragments[(int)(Next() % (ulong)fragments.Length)]);
            var input = sb.ToString();
            var expected = ReferenceNormalize(input);
            var actual = SearchNormalizer.Normalize(input);
            Assert.True(expected == actual,
                $"pseudo-corpus doc {doc} diverged:\nINPUT:\n{input}\nEXPECTED:\n{expected}\nACTUAL:\n{actual}");
        }
    }
}
