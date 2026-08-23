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
}
