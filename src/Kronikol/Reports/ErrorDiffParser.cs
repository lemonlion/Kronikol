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
        var expectedText = CodePoints(expected);
        var actualText = CodePoints(actual);
        var sb = new StringBuilder();
        sb.Append("<div class=\"error-diff\">");

        // Expected line
        sb.Append("<div class=\"diff-expected\"><span class=\"diff-label\">Expected:</span><code>");
        AppendDiff(sb, expectedText, actualText, "diff-del");
        sb.Append("</code></div>");

        // Actual line
        sb.Append("<div class=\"diff-actual\"><span class=\"diff-label\">Actual:</span><code>");
        AppendDiff(sb, actualText, expectedText, "diff-ins");
        sb.Append("</code></div>");

        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>
    /// The most pairs of characters the diff compares one by one: its table holds a number for each pair, so two
    /// values of 2,000 characters take 16 MB, and two of 50,000, as Shouldly writes a response body whole, could not
    /// be held at all, which left the run without its HTML report. Past it the diff keeps the values' common start
    /// and end and marks the rest.
    /// </summary>
    internal const long MaxComparedPairs = 4_000_000;

    /// <summary>The text's characters as code points, so a character outside the Basic Multilingual Plane (an emoji)
    /// is compared, and encoded, whole rather than as two halves, each of which HTML encoding writes as U+FFFD.</summary>
    private static int[] CodePoints(string text)
    {
        var points = new List<int>(text.Length);
        foreach (var rune in text.EnumerateRunes())
            points.Add(rune.Value);
        return points.ToArray();
    }

    private static void AppendDiff(StringBuilder sb, int[] primary, int[] other, string cssClass)
    {
        if ((long)primary.Length * other.Length > MaxComparedPairs)
        {
            AppendEndsDiff(sb, primary, other, cssClass);
            return;
        }

        var lcs = ComputeLcs(primary, other);
        var run = new StringBuilder();
        var inDiff = false;
        var li = 0;
        foreach (var point in primary)
        {
            var common = li < lcs.Length && point == lcs[li];
            if (common == inDiff)
            {
                AppendRun(sb, run, inDiff, cssClass);
                inDiff = !common;
            }

            run.Append(char.ConvertFromUtf32(point));
            if (common)
                li++;
        }

        AppendRun(sb, run, inDiff, cssClass);
    }

    /// <summary>The diff past <see cref="MaxComparedPairs"/>: the start and the end the two values share, and between
    /// them the rest of <paramref name="primary"/>, marked.</summary>
    private static void AppendEndsDiff(StringBuilder sb, int[] primary, int[] other, string cssClass)
    {
        var shorter = Math.Min(primary.Length, other.Length);
        var start = 0;
        while (start < shorter && primary[start] == other[start])
            start++;
        var end = 0;
        while (end < shorter - start && primary[primary.Length - 1 - end] == other[other.Length - 1 - end])
            end++;

        var run = new StringBuilder();
        AppendPoints(run, primary, 0, start);
        AppendRun(sb, run, false, cssClass);
        AppendPoints(run, primary, start, primary.Length - start - end);
        AppendRun(sb, run, true, cssClass);
        AppendPoints(run, primary, primary.Length - end, end);
        AppendRun(sb, run, false, cssClass);
    }

    private static void AppendPoints(StringBuilder run, int[] points, int from, int count)
    {
        for (var i = from; i < from + count; i++)
            run.Append(char.ConvertFromUtf32(points[i]));
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

    private static int[] ComputeLcs(int[] a, int[] b)
    {
        var m = a.Length;
        var n = b.Length;
        var dp = new int[m + 1, n + 1];

        for (var i = 1; i <= m; i++)
        for (var j = 1; j <= n; j++)
            dp[i, j] = a[i - 1] == b[j - 1]
                ? dp[i - 1, j - 1] + 1
                : Math.Max(dp[i - 1, j], dp[i, j - 1]);

        // Backtrack to find the LCS, last point first
        var lcs = new List<int>();
        int x = m, y = n;
        while (x > 0 && y > 0)
        {
            if (a[x - 1] == b[y - 1])
            {
                lcs.Add(a[x - 1]);
                x--;
                y--;
            }
            else if (dp[x - 1, y] >= dp[x, y - 1])
                x--;
            else
                y--;
        }

        lcs.Reverse();
        return lcs.ToArray();
    }
}
