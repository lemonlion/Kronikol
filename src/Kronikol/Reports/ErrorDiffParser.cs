using System.Text;
using System.Text.RegularExpressions;

namespace Kronikol.Reports;

/// <summary>
/// Extracts expected/actual value pairs from assertion failure messages
/// for side-by-side diff display in the report. Supports xUnit, NUnit,
/// FluentAssertions, and Shouldly assertion formats.
/// </summary>
public static class ErrorDiffParser
{
    public record DiffResult(string Expected, string Actual);

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
        // the custom message's "Additional Info" may follow it.
        new(@"^ {4}should be\r?\n(.*?)\r?\n {4}but was\r?\n(.*?)(?=\r?\n {4}difference(?:\r?\n|\z)|\r?\n\r?\nAdditional Info:|\s*\z)",
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
                var expected = Unquote(match.Groups[1].Value.Trim());
                var actual = Unquote(match.Groups[2].Value.Trim());
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
        var sb = new StringBuilder();
        sb.Append("<div class=\"error-diff\">");

        // Expected line
        sb.Append("<div class=\"diff-expected\"><span class=\"diff-label\">Expected:</span><code>");
        AppendDiffChars(sb, expected, actual, isDeletion: true);
        sb.Append("</code></div>");

        // Actual line
        sb.Append("<div class=\"diff-actual\"><span class=\"diff-label\">Actual:</span><code>");
        AppendDiffChars(sb, actual, expected, isDeletion: false);
        sb.Append("</code></div>");

        sb.Append("</div>");
        return sb.ToString();
    }

    private static void AppendDiffChars(StringBuilder sb, string primary, string other, bool isDeletion)
    {
        var lcs = ComputeLcs(primary, other);
        var cssClass = isDeletion ? "diff-del" : "diff-ins";

        var pi = 0;
        var li = 0;
        var inDiff = false;

        while (pi < primary.Length)
        {
            if (li < lcs.Length && primary[pi] == lcs[li])
            {
                // Common character
                if (inDiff) { sb.Append("</span>"); inDiff = false; }
                sb.Append(System.Net.WebUtility.HtmlEncode(primary[pi].ToString()));
                pi++;
                li++;
            }
            else
            {
                // Differing character
                if (!inDiff) { sb.Append($"<span class=\"{cssClass}\">"); inDiff = true; }
                sb.Append(System.Net.WebUtility.HtmlEncode(primary[pi].ToString()));
                pi++;
            }
        }

        if (inDiff) sb.Append("</span>");
    }

    private static string ComputeLcs(string a, string b)
    {
        var m = a.Length;
        var n = b.Length;
        var dp = new int[m + 1, n + 1];

        for (var i = 1; i <= m; i++)
        for (var j = 1; j <= n; j++)
            dp[i, j] = a[i - 1] == b[j - 1]
                ? dp[i - 1, j - 1] + 1
                : Math.Max(dp[i - 1, j], dp[i, j - 1]);

        // Backtrack to find the LCS string
        var sb = new StringBuilder();
        int x = m, y = n;
        while (x > 0 && y > 0)
        {
            if (a[x - 1] == b[y - 1])
            {
                sb.Insert(0, a[x - 1]);
                x--;
                y--;
            }
            else if (dp[x - 1, y] >= dp[x, y - 1])
                x--;
            else
                y--;
        }

        return sb.ToString();
    }
}
