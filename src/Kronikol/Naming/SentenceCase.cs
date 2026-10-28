using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kronikol.Naming;

/// <summary>
/// The one rule that turns an identifier into words (plans/TITLES_AS_WRITTEN_PLAN.md 4.1): a scenario's title from its
/// method (<c>WhenMCAFundingActivates</c> reads "When MCA funding activates"), a woven step's text from its method, and
/// an assertion label's words from its code (<c>HTTPStatus</c> reads "HTTP status").
/// </summary>
/// <remarks>
/// <para>
/// It splits where 5.12.0's scenario humaniser split (a lower-case letter before a capital; the last capital of a run
/// before a capital and a lower-case letter), except that a plural of capitals stays whole (<c>URLs</c>), and also
/// between a digit and a capital that starts a word (<c>404When</c>, <c>200OK</c>, but <c>E2E</c> whole). A word in
/// capitals, its plural and a one-letter capital other than the article <c>A</c> keep their case; every other word is
/// lower-cased. A name with no lower-case letter at all is a sentence written in capitals, so each of its words is an
/// ordinary word. Letters and digits run together stay whole (<c>p95</c>, <c>utf8</c>, <c>Returns404</c>).
/// </para>
/// <para>
/// Culture-invariant, so a title and the id hashed from it are one on every machine. The step weaver compiles this same
/// file, and it targets netstandard2.0: no generated regexes and no ranges here.
/// </para>
/// </remarks>
internal static class SentenceCase
{
    // A digit before a capital that starts a word: a capital before a lower-case letter (404When, 3Times), or two or
    // more capitals with no lower-case letter after them (200OK, 64MB). A lone capital stays with its digits: E2E, P2P.
    private static readonly Regex DigitBeforeWord = new(@"(?<=\d)(?=\p{Lu}\p{Ll}|\p{Lu}{2,}(?!\p{Ll}))");

    private static readonly Regex LowerBeforeCapital = new(@"(\p{Ll})(\p{Lu})");

    // The last capital of a run before a capital and a lower-case letter (HTTPResponse), unless that pair is the s of a
    // plural closing the run (URLs, IDs); a closing Is or As is a word, so XIs reads "X is".
    private static readonly Regex CapitalRunBeforeWord = new(@"(\p{Lu}+)(?![\p{Lu}-[IA]]s(?!\p{Ll}))(\p{Lu}\p{Ll})");

    private static readonly Regex PluralOfCapitals = new(@"^\p{Lu}{2,}s$");

    private static readonly Regex Whitespace = new(@"\s+");

    private static readonly Regex WhitespaceKept = new(@"(\s+)");

    // What parts the words of code: a bracket, a comma, an operator; an underscore and an apostrophe are within a word
    private static readonly Regex NotOfAWordKept = new(@"([^\p{L}\p{N}_']+)");

    /// <summary>
    /// An identifier as a sentence: split into words, underscores and whitespace read as one space, each word cased by
    /// the rule and the first given a capital. Empty when the identifier holds no word.
    /// </summary>
    public static string Sentence(string identifier)
    {
        var words = Whitespace.Split(Split(identifier).Replace('_', ' ')).Where(w => w.Length > 0).ToArray();
        if (words.Length == 0)
            return "";

        var shouting = Shouting(words);
        var sentence = new StringBuilder();
        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0)
                sentence.Append(' ');
            var word = words[i];
            if (!shouting && InCapitals(word))
                sentence.Append(word);
            else if (i == 0)
                sentence.Append(char.ToUpperInvariant(word[0])).Append(word.Substring(1).ToLowerInvariant());
            else
                sentence.Append(word.ToLowerInvariant());
        }
        return sentence.ToString();
    }

    /// <summary>
    /// Code as words, for an assertion label: split and cased as a sentence's words, but no word given a capital, and
    /// every character that is not a letter's case (an underscore, the spaces already there) left as it is. Code's
    /// punctuation parts words too, so a word in capitals inside brackets keeps them: <c>Encode(1U)</c> reads
    /// "encode(1U)".
    /// </summary>
    public static string Words(string code)
    {
        if (code.Length == 0)
            return code;

        var parts = WhitespaceKept.Split(Split(code));
        var words = parts.Where(p => p.Length > 0 && !char.IsWhiteSpace(p[0])).ToArray();
        var shouting = Shouting(words);
        var text = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.Length == 0 || char.IsWhiteSpace(part[0]))
                text.Append(part);
            else if (shouting)
                text.Append(part.ToLowerInvariant());
            else
                foreach (var run in NotOfAWordKept.Split(part))
                    text.Append(InCapitals(run) ? run : run.ToLowerInvariant());
        }
        return text.ToString();
    }

    private static string Split(string name)
    {
        var split = DigitBeforeWord.Replace(name, " ");
        split = LowerBeforeCapital.Replace(split, "$1 $2");
        return CapitalRunBeforeWord.Replace(split, "$1 $2");
    }

    // A name in capitals throughout says nothing about which of its words are acronyms: each is an ordinary word.
    private static bool Shouting(string[] words) =>
        words.All(w => !w.Any(char.IsLower)) && words.Count(w => w.Any(char.IsLetter)) > 1;

    private static bool InCapitals(string word)
    {
        var letters = word.Where(char.IsLetter).ToArray();
        if (letters.Length == 0)
            return false;
        if (letters.All(char.IsUpper) && (letters.Length > 1 || word.Any(char.IsDigit)))
            return true;
        if (PluralOfCapitals.IsMatch(word))
            return true;
        return letters.Length == 1 && char.IsUpper(letters[0]) && letters[0] != 'A';
    }
}
