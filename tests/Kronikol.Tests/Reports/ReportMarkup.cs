using System.Text.RegularExpressions;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Searches a generated report's <em>markup</em>, ignoring the code it ships alongside it.
///
/// <para>A Kronikol report is one self-contained HTML file: its behaviour travels as inline
/// <c>&lt;script&gt;</c> and its look as inline <c>&lt;style&gt;</c>. Those scripts address elements with
/// the very attributes the markup carries — <c>querySelectorAll('.toggle-btn[data-toggle="databases"]')</c>
/// is a character-for-character match for the attribute on the button it selects — so a substring search
/// over the whole document cannot tell an element from a selector that mentions it, or a rendered heading
/// from a CSS comment naming it.</para>
///
/// <para>That is not hypothetical: five tests broke at once when a feature added
/// <c>renderWithPending(queue, document.querySelectorAll('.toggle-btn[data-toggle="…"]'))</c> to the
/// toggle scripts. The element markup was correct throughout; only the searches were wrong. Assertions
/// about what a report <em>renders</em> belong here.</para>
/// </summary>
internal static class ReportMarkup
{
    private static readonly Regex CodeBlocks = new(
        @"<script\b[^>]*>.*?</script\s*>|<style\b[^>]*>.*?</style\s*>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The document with every inline script and stylesheet removed.</summary>
    public static string Only(string html) => CodeBlocks.Replace(html, "");

    /// <summary>
    /// The opening tags of every <paramref name="tag"/> element carrying <paramref name="attribute"/>,
    /// e.g. <c>Elements(html, "button", "data-toggle=\"databases\"")</c>. Empty when the report renders none.
    /// </summary>
    public static string[] Elements(string html, string tag, string attribute) =>
        Regex.Matches(Only(html), $@"<{Regex.Escape(tag)}\b[^>]*{Regex.Escape(attribute)}[^>]*>",
                RegexOptions.IgnoreCase)
            .Select(m => m.Value)
            .ToArray();

    /// <summary>
    /// The opening tags of every element whose <c>class</c> list carries <paramref name="className"/> as a
    /// whole token — <c>step</c> matches <c>class="step step-background"</c> and not <c>class="step-number"</c>.
    /// </summary>
    public static List<string> ElementsWithClass(string html, string className) =>
        Regex.Matches(Only(html), @"<\w+\b[^>]*\bclass=""(?<classes>[^""]*)""[^>]*>", RegexOptions.IgnoreCase)
            .Where(m => m.Groups["classes"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(className))
            .Select(m => m.Value)
            .ToList();

    /// <summary>
    /// The text inside every <c>&lt;span class="…"&gt;</c> whose class attribute is exactly
    /// <paramref name="className"/>, in document order, with any nested markup stripped.
    /// </summary>
    public static List<string> InnerTextsOfClass(string html, string className) =>
        Regex.Matches(Only(html), $@"<span class=""{Regex.Escape(className)}""[^>]*>(?<text>.*?)</span>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(m => StripTags(m.Groups["text"].Value).Trim())
            .ToList();

    /// <summary>The value of <paramref name="attribute"/> on the first <paramref name="tag"/> element carrying it.</summary>
    public static string AttributeValue(string html, string tag, string attribute)
    {
        var match = Regex.Match(Only(html),
            $@"<{Regex.Escape(tag)}\b[^>]*\b{Regex.Escape(attribute)}=""(?<value>[^""]*)""",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value : "";
    }

    /// <summary>
    /// The cell texts of the Features Summary row for <paramref name="featureName"/>, in column order.
    /// Empty when the report renders no such row.
    /// </summary>
    public static List<string> FeatureSummaryRow(string html, string featureName)
    {
        var table = Regex.Match(Only(html),
            @"<table class=""feature-summary-table"">.*?</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!table.Success) return [];

        foreach (Match row in Regex.Matches(table.Value, @"<tr\b[^>]*>(?<cells>.*?)</tr>", RegexOptions.Singleline))
        {
            var cells = Regex.Matches(row.Groups["cells"].Value, @"<td\b[^>]*>(?<text>.*?)</td>", RegexOptions.Singleline)
                .Select(c => StripTags(c.Groups["text"].Value).Trim())
                .ToList();
            if (cells.Count > 0 && cells[0] == featureName)
                return cells;
        }
        return [];
    }

    private static string StripTags(string html) => Regex.Replace(html, "<[^>]*>", "");
}
