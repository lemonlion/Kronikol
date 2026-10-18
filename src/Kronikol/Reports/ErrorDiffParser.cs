using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kronikol.Reports;

/// <summary>
/// Extracts expected/actual value pairs from assertion failure messages
/// for side-by-side diff display in the report. Supports xUnit, NUnit,
/// FluentAssertions, and Shouldly assertion formats; Shouldly's "should be" is read whether its text names the
/// subject's code (Shouldly read the caller's source) or begins with the actual value (it could not).
/// </summary>
public static class ErrorDiffParser
{
    public record DiffResult(string Expected, string Actual);

    /// <summary>A Shouldly value: any text, over several lines too, that never crosses the line of one of Shouldly's
    /// markers (<c>    should …</c>, <c>    but was …</c>).</summary>
    internal const string ShouldlyValue = @"(?:(?!\r?\n {4}(?:should |but was)).)*?";

    // Patterns ordered by specificity
    private static readonly Regex[] ExpectedActualPatterns =
    [
        // xUnit Assert.Equal: Expected: value / Actual: value (with optional quotes)
        new(@"Expected:\s*""?(.+?)""?\s*\r?\nActual:\s*""?(.+?)""?\s*$", RegexOptions.Multiline),

        // NUnit: Expected: value / But was: value
        new(@"Expected:\s*""?(.+?)""?\s*\r?\n\s*But was:\s*""?(.+?)""?\s*$", RegexOptions.Multiline),

        // Shouldly: the subject's code, then "should be", the expected value, "but was" and the actual value,
        // each marker on a line of its own indented by four spaces. A value is written bare (5, True,
        // Colour.Green, null) unless it is a string, and can run over several lines; a "difference" table or
        // the custom message's "Additional Info" may follow it. A comparison with options names them on the check's
        // line (should be with options: Ignoring case), and a string collection's "but was" names its comparison
        // (but was (case sensitive comparison)). A value never crosses a marker's line, so a text that quotes the
        // message before giving it whole (an AggregateException's, as the xUnit adapters join it) is read from the
        // whole message, not across both.
        new(@"^ {4}should be(?: with options: [^\r\n]*)?\r?\n(?<expected>" + ShouldlyValue + @")\r?\n {4}but was(?: \([^)\r\n]*\))?\r?\n(?<actual>" + ShouldlyValue + @")(?=\r?\n {4}difference(?:\r?\n|\z)|\r?\n\r?\nAdditional Info:|\s*\z)",
            RegexOptions.Multiline | RegexOptions.Singleline),

        // Shouldly without the caller's source: when it cannot read the file (its path map not handed to the test
        // process, as when a CI build's test DLL is run directly, the tests run where the sources are not, or
        // ShouldlyConfiguration.DisableSourceInErrors()), it writes the actual value where the code would be, then
        // "should be", the expected value and "but was not" (Shouldly 4.3.0, WEAVER_SOURCE_PATHS_PLAN F8). Only the
        // "should be" check is read, as with the source, with its options and its comparison as above. The actual
        // value starts a line that is not a marker's: the text's first, or the whole message's after a quoted one;
        // NUnit writes the exception's type before it (Shouldly.ShouldAssertException : 4), which is not the value.
        new(@"^(?! {4}(?:should |but was))(?:Shouldly\.\w+ : )?(?<actual>" + ShouldlyValue + @")\r?\n {4}should be(?: with options: [^\r\n]*)?\r?\n(?<expected>" + ShouldlyValue + @")\r?\n {4}but was not(?: \([^)\r\n]*\))?(?=\r?\n {4}difference(?:\r?\n|\z)|\r?\n\r?\nAdditional Info:|\s*\z)",
            RegexOptions.Multiline | RegexOptions.Singleline),

        // FluentAssertions, a string, whatever the subject is called:
        // Expected text to be "b", but "a" differs near "a" (index 0).
        new(@"^Expected .+? to be (?:equivalent to )?("".*?"")(?: with a length of \d+)?, but ("".*?"")", RegexOptions.Singleline),

        // FluentAssertions, any other value: Expected result to be 5[ because ...], but found 3[ (difference of -2)].
        new(@"^Expected .+? to be (.+?)(?: because .+?)?, but found (.+?)(?: \(difference of [^)]*\))?\.?\s*\z", RegexOptions.Singleline),

        // FluentAssertions: Expected string to be [equivalent to] "expected" ... but "actual"
        new(@"Expected string to be(?:\s+equivalent to)?\s+""(.+?)"".+?but\s+""(.+?)""", RegexOptions.Singleline),

        // Shouldly, quoted and on any lines: should be "expected" but was "actual"
        new(@"should be\s+""(.+?)""\s*\r?\n\s*but was\s+""(.+?)""", RegexOptions.Singleline),
    ];

    public static DiffResult? TryParseExpectedActual(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return null;

        foreach (var pattern in ExpectedActualPatterns)
        {
            var match = pattern.Match(errorMessage);
            if (match.Success)
            {
                // A pattern that reads the actual value first names its groups.
                var expected = Unquote((match.Groups["expected"] is { Success: true } named ? named : match.Groups[1]).Value.Trim());
                var actual = Unquote((match.Groups["actual"] is { Success: true } other ? other : match.Groups[2]).Value.Trim());
                return new DiffResult(expected, actual);
            }
        }

        return null;
    }

    /// <summary>A string value without the one pair of quotes it was written in; any other value as it is.</summary>
    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value.Trim('"');

    public static string GenerateDiffHtml(string expected, string actual)
    {
        var expectedTokens = Tokens(expected);
        var actualTokens = Tokens(actual);
        var (expectedCommon, actualCommon) = Common(expectedTokens, actualTokens);
        var sb = new StringBuilder();
        sb.Append("<div class=\"error-diff\">");

        // Expected line
        sb.Append("<div class=\"diff-expected\"><span class=\"diff-label\">Expected:</span><code>");
        AppendDiff(sb, expectedTokens, expectedCommon, "diff-del");
        sb.Append("</code></div>");

        // Actual line
        sb.Append("<div class=\"diff-actual\"><span class=\"diff-label\">Actual:</span><code>");
        AppendDiff(sb, actualTokens, actualCommon, "diff-ins");
        sb.Append("</code></div>");

        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>
    /// The most pairs of tokens the diff compares one by one: its table holds a number for each pair, so two values
    /// of 2,000 tokens take 16 MB, and two of 50,000, as Shouldly writes a response body whole, could not be held at
    /// all, which left the run without its HTML report. Past it the diff keeps the values' common start and end and
    /// marks the rest.
    /// </summary>
    internal const long MaxComparedPairs = 4_000_000;

    /// <summary>
    /// The text as the diff compares it: a word or a number (a run of letters, combining marks, digits and connectors
    /// such as the underscore, in any script), a run of whitespace, or one other character. Each is whole code points,
    /// so a character outside the Basic Multilingual Plane (an emoji) is never split.
    /// </summary>
    internal static string[] Tokens(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var kind = TokenKind.Other;
        foreach (var rune in text.EnumerateRunes())
        {
            var next = KindOf(rune);
            if (current.Length > 0 && (next != kind || next == TokenKind.Other))
            {
                tokens.Add(current.ToString());
                current.Clear();
            }

            current.Append(rune.ToString());
            kind = next;
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());
        return tokens.ToArray();
    }

    private enum TokenKind { Word, Space, Other }

    private static TokenKind KindOf(Rune rune)
    {
        if (Rune.IsWhiteSpace(rune))
            return TokenKind.Space;
        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
                or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
                or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
                or UnicodeCategory.ConnectorPunctuation => TokenKind.Word,
            _ => TokenKind.Other,
        };
    }

    /// <summary>Which tokens of each value the two share: a longest common subsequence of tokens, or past
    /// <see cref="MaxComparedPairs"/> the start and the end they share.</summary>
    private static (bool[] A, bool[] B) Common(string[] a, string[] b)
    {
        var inA = new bool[a.Length];
        var inB = new bool[b.Length];
        if ((long)a.Length * b.Length > MaxComparedPairs)
        {
            var shorter = Math.Min(a.Length, b.Length);
            var start = 0;
            while (start < shorter && a[start] == b[start])
            {
                inA[start] = inB[start] = true;
                start++;
            }

            for (var end = 1; end <= shorter - start && a[^end] == b[^end]; end++)
                inA[a.Length - end] = inB[b.Length - end] = true;
            return (inA, inB);
        }

        // lengths[i, j]: the longest common subsequence of a[i..] and b[j..]
        var lengths = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        for (var j = b.Length - 1; j >= 0; j--)
            lengths[i, j] = a[i] == b[j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);

        for (int i = 0, j = 0; i < a.Length && j < b.Length;)
        {
            if (a[i] == b[j])
            {
                inA[i] = inB[j] = true;
                i++;
                j++;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
                i++;
            else
                j++;
        }

        return (inA, inB);
    }

    /// <summary>The value's tokens, each run of changed ones in one marking span. A space between two changed
    /// tokens is part of the change: two changed words read as one change, not as two around a common space.</summary>
    private static void AppendDiff(StringBuilder sb, string[] tokens, bool[] common, string cssClass)
    {
        var changed = new bool[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
            changed[i] = !common[i];
        for (var i = 1; i < tokens.Length - 1; i++)
            if (!changed[i] && changed[i - 1] && !common[i + 1] && string.IsNullOrWhiteSpace(tokens[i]))
                changed[i] = true;

        var run = new StringBuilder();
        var inDiff = false;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (changed[i] != inDiff)
            {
                AppendRun(sb, run, inDiff, cssClass);
                inDiff = changed[i];
            }

            run.Append(tokens[i]);
        }

        AppendRun(sb, run, inDiff, cssClass);
    }

    /// <summary>Writes the run encoded, inside the marking span when it differs, and empties it.</summary>
    private static void AppendRun(StringBuilder sb, StringBuilder run, bool differs, string cssClass)
    {
        if (run.Length == 0)
            return;
        var encoded = System.Net.WebUtility.HtmlEncode(run.ToString());
        if (differs)
            sb.Append("<span class=\"").Append(cssClass).Append("\">").Append(encoded).Append("</span>");
        else
            sb.Append(encoded);
        run.Clear();
    }
}
