using System.Text.RegularExpressions;

namespace Kronikol.Tracking;

/// <summary>
/// The line a capture appends after a complete document it capped: <c>... (N more rows not shown)</c> (SQL, Spanner),
/// <c>... (N more documents not shown)</c> (a MongoDB batch, an insert's documents) or <c>... (N more)</c> (a MongoDB
/// write's statements). The page's note formatter and the query tool's body reader both read past it, from this one
/// definition (plans/MONGODB_DETAILED_PAYLOADS_PLAN.md, F8).
/// </summary>
internal static partial class CaptureFootnote
{
    /// <summary>The footnote on its own line, unanchored, for a pattern that adds alternatives of its own.</summary>
    internal const string Pattern = @"\r?\n\.\.\. \(\d+ more(?: (?:rows|documents) not shown)?\)";

    [GeneratedRegex(Pattern + @"\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex AtTheEnd();

    /// <summary>The document before the footnote that ends <paramref name="content"/>, or null when none ends it.</summary>
    internal static string? DocumentBefore(string content) => Split(content)?.Document;

    /// <summary>
    /// The document before the footnote that ends <paramref name="content"/> and the footnote's own line, or null when
    /// none ends it. Only a JSON document (an object or an array) is followed by one, as the page reads it: a text body
    /// whose last line looks like a footnote is text.
    /// </summary>
    internal static (string Document, string Footnote)? Split(string content)
    {
        var match = AtTheEnd().Match(content);
        if (!match.Success)
            return null;
        var document = content[..match.Index];
        var start = document.AsSpan().TrimStart();
        return start.Length > 0 && start[0] is '{' or '[' ? (document, match.Value.Trim()) : null;
    }
}
