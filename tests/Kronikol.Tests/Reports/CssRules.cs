using System.Text.RegularExpressions;

namespace Kronikol.Tests.Reports;

/// <summary>One style rule of a stylesheet: its position in source order, the media query around it
/// (null at top level), its selectors and its declarations.</summary>
internal sealed record CssRule(int Index, string? Media, IReadOnlyList<string> Selectors, IReadOnlyDictionary<string, string> Declarations);

/// <summary>
/// Reads the report's stylesheets rule by rule, so a test can ask what a selector's block declares
/// instead of matching a substring that a comment, another selector or a script could also contain.
/// Enough CSS for the sheets Kronikol ships: comments, style rules, <c>@media</c> blocks one level
/// deep, and other at-rules (<c>@keyframes</c>) skipped whole.
/// </summary>
internal static partial class CssRules
{
    public static IReadOnlyList<CssRule> Parse(string css)
    {
        var text = Comment().Replace(css, "");
        var rules = new List<CssRule>();
        ParseBlock(text, 0, text.Length, null, rules);
        return rules;
    }

    /// <summary>The rules whose selector list holds <paramref name="selector"/> exactly, inside
    /// <paramref name="media"/> (null: at top level), in source order.</summary>
    public static IReadOnlyList<CssRule> For(string css, string selector, string? media = null) =>
        Parse(css).Where(r => r.Media == media && r.Selectors.Contains(selector)).ToList();

    /// <summary>What the cascade gives <paramref name="property"/> among the rules for
    /// <paramref name="selector"/> in <paramref name="media"/>: the last declaration, or null.</summary>
    public static string? Value(string css, string selector, string property, string? media = null) =>
        For(css, selector, media).Select(r => r.Declarations.GetValueOrDefault(property)).LastOrDefault(v => v is not null);

    /// <summary>The source position of the first rule for <paramref name="selector"/> at top level, or -1.</summary>
    public static int IndexOf(string css, string selector) =>
        For(css, selector).Select(r => r.Index).DefaultIfEmpty(-1).First();

    /// <summary>Every media query in the sheet, as written.</summary>
    public static IReadOnlyList<string> MediaQueries(string css) =>
        Parse(css).Select(r => r.Media).OfType<string>().Distinct().ToList();

    private static void ParseBlock(string css, int start, int end, string? media, List<CssRule> rules)
    {
        var i = start;
        while (i < end)
        {
            var open = css.IndexOf('{', i, end - i);
            if (open < 0) break;
            var prelude = Normalise(css[i..open]);
            var close = MatchingBrace(css, open);
            if (prelude.StartsWith("@media", StringComparison.Ordinal))
                ParseBlock(css, open + 1, close, Normalise(prelude["@media".Length..]), rules);
            else if (prelude.Length > 0 && !prelude.StartsWith('@'))
                rules.Add(new CssRule(rules.Count, media, prelude.Split(',').Select(Normalise).ToArray(), Declarations(css[(open + 1)..close])));
            i = close + 1;
        }
    }

    private static int MatchingBrace(string css, int open)
    {
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{') depth++;
            else if (css[i] == '}' && --depth == 0) return i;
        }
        throw new FormatException($"Unbalanced brace at {open}.");
    }

    private static Dictionary<string, string> Declarations(string body)
    {
        var declarations = new Dictionary<string, string>();
        foreach (var part in body.Split(';'))
        {
            var colon = part.IndexOf(':');
            if (colon < 0) continue;
            declarations[Normalise(part[..colon]).ToLowerInvariant()] = Normalise(part[(colon + 1)..]);
        }
        return declarations;
    }

    private static string Normalise(string s) => Whitespace().Replace(s, " ").Trim();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
