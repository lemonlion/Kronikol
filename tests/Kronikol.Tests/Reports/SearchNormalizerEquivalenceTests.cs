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

    [GeneratedRegex("~(?=[/*_\\-\"\\[<#=])")]
    private static partial Regex CreoleEscape();

    [GeneratedRegex("</?(?:color|font|i|b|size|back)[^>]*>")]
    private static partial Regex MarkupTag();

    [GeneratedRegex(@"\\n[ \t]*")]
    private static partial Regex ArrowLabelBreak();

    [GeneratedRegex("[ \t]+")]
    private static partial Regex WhitespaceRun();

    private static string ReferenceNormalize(string text)
    {
        var s = text.Replace("\r\n", "\n");
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] is >= 'A' and <= 'Z') chars[i] = (char)(chars[i] + 32);
        s = new string(chars);
        s = CreoleEscape().Replace(s, "");
        s = MarkupTag().Replace(s, "");
        s = ArrowLabelBreak().Replace(s, "");
        var sb = new StringBuilder();
        var inNote = false;
        var first = true;
        foreach (var line in s.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("note left") || trimmed.StartsWith("note right") || trimmed.StartsWith("note over"))
            {
                inNote = true;
                if (!first) sb.Append('\n');
                sb.Append(line);
            }
            else if (trimmed == "end note")
            {
                inNote = false;
                if (!first) sb.Append('\n');
                sb.Append(line);
            }
            else if (inNote && !first && line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                sb.Append(line);
            }
            else
            {
                if (!first) sb.Append('\n');
                sb.Append(line);
            }
            first = false;
        }
        sb.Append('\n');
        return WhitespaceRun().Replace(sb.ToString(), " ");
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
        "note left\n<color:gray>[X=aaaa\n<color:gray>bbbb]\n\n{\n  \"k\": \"AAAA\nBBBB\"\n}\nend note\nafter -> b: x",
        "note right\nflush\nleft\nend note",
        "note over A,B\npayload\nend note",
        "notnote left\nx\ny",
        "  a\tb  c   \n\t\n d",
        "~x ~~ ~ tilde survivors",
        "trailing backslash \\",
        "\\n",
        "~",
        "<",
        "a<b&c>d<e"
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
            "note left\n", "end note\n", "~*", "~/", "\\n   ", "<color:gray>", "</font>", "<i>", "\r\n",
            "  ", "\t", "{ \"k\": \"v\" }\n", "POST: /api/x\n", "~[", "~\"", "AAAA\n", "bbbb\n", "<", ">", "~", "\\"
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
