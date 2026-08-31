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

    // The reference's note-opener regex, with JS \b transliterated to an explicit ASCII
    // word-boundary group (.NET \b is Unicode-aware, JS \b is ASCII-\w-based).
    [GeneratedRegex("^[hr]?note(?:<<[^>]*>>)? (left|right|over|across)([^a-zA-Z0-9_]|$)")]
    private static partial Regex NoteOpener();

    // JS /\s/ and String.trim() semantics, NOT char.IsWhiteSpace/string.Trim — they disagree on
    // U+0085 (NEL) and U+FEFF, and the shipped normalizer must match the client JS exactly.
    private static bool RefIsJsWhitespace(char c) =>
        c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r' or '\u00a0' or '\u1680'
          or (>= '\u2000' and <= '\u200a') or '\u2028' or '\u2029' or '\u202f' or '\u205f' or '\u3000' or '\ufeff';

    private static string RefJsTrim(string line)
    {
        var start = 0;
        var end = line.Length;
        while (start < end && RefIsJsWhitespace(line[start])) start++;
        while (end > start && RefIsJsWhitespace(line[end - 1])) end--;
        return line[start..end];
    }

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
            var trimmed = RefJsTrim(line);
            if (NoteOpener().IsMatch(trimmed) && !trimmed.Contains(':'))
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
            else if (inNote && !first && line.Length > 0 && !RefIsJsWhitespace(line[0]))
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
        "note<<eventNote>> right\nchunkA\nchunkB\nend note",
        "hnote across <<assertionNote>> #90EE90\nexpected X\nactual Y\nend note",
        "hnote across <<stepDelimiter>> #black:<color:white>Step 1\nflush\nnext",
        "hnote across #lightyellow : Row 2\nflush",
        "note left\nAAAA\nnote leftovers glue\nend note",
        "note left\nAAAA\n\u0085NEL-continuation\nend note",
        "note left\nAAAA\n\ufeffFEFF-continuation\nend note",
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
            "  ", "\t", "{ \"k\": \"v\" }\n", "POST: /api/x\n", "~[", "~\"", "AAAA\n", "bbbb\n", "<", ">", "~", "\\",
            "note<<eventNote>> right\n", "hnote across <<assertionNote>> #x\n", "hnote across #y : Row\n",
            "across ", "\u0085", "\ufeff"
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
