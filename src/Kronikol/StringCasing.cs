// Ported from Humanizer (https://github.com/Humanizr/Humanizer) under the MIT License.
// Original Copyright (c) .NET Foundation and Contributors

using System.Globalization;
using System.Text.RegularExpressions;

namespace Kronikol;

/// <summary>
/// String casing utilities for formatting scenario and feature display names.
/// </summary>
public static partial class StringCasing
{
    // From Humanizer's StringHumanizeExtensions, as 5.9.0 had it: the pattern TitleizeAsOf590 keeps
    private const string PascalCaseWordPartsPattern590 =
        @"(\p{Lu}?\p{Ll}+|[0-9]+\p{Ll}*|\p{Lu}+(?=\p{Lu}|[0-9]|\b)|\p{Lo}+)[,;]?";

    // The same, first taking a run of letters joined by apostrophes (' or U+2019) as one word, which is then kept as
    // written: until 5.9.1 "Month's" was read as "Month" and "s" (plans/REPORT_UI_FIXES_PLAN.md N2)
    private const string PascalCaseWordPartsPattern =
        @"(\p{L}+(?:['\u2019]\p{L}+)+|\p{Lu}?\p{Ll}+|[0-9]+\p{Ll}*|\p{Lu}+(?=\p{Lu}|[0-9]|\b)|\p{Lo}+)[,;]?";

    // From Humanizer's InflectorExtensions
    private const string PascalizePattern = @"(?:[ _-]+|^)(.)";

    [GeneratedRegex(PascalCaseWordPartsPattern590, RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture)]
    private static partial Regex PascalCaseWordPartsRegex590();

    [GeneratedRegex(PascalCaseWordPartsPattern, RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture)]
    private static partial Regex PascalCaseWordPartsRegex();

    [GeneratedRegex(@"\p{L}['\u2019]\p{L}")]
    private static partial Regex ApostropheInAWordRegex();

    [GeneratedRegex(PascalizePattern)]
    private static partial Regex PascalizeRegex();

    /// <summary>
    /// Humanizes and applies Title Case. Based on Humanizer's Titleize() / Humanize(LetterCasing.Title), with two
    /// differences: a word with an apostrophe between letters (<c>Month's</c>, <c>O'Brien's</c>) keeps its letters as
    /// written but for its first, which is upper-cased, and an all-capitals word before a comma stays in capitals.
    /// </summary>
    public static string Titleize(this string input)
    {
        var humanized = Humanize(input, PascalCaseWordPartsRegex(), keepApostropheWords: true);
        if (humanized.Length == 0)
            return input;
        // Word by word, as ToTitleCase reads words, but a word with an apostrophe never goes through it: it reads U+2019
        // as a word break ("Month’S") and lower-cases what follows a U+0027 ("O'brien's"). Invariant, as every casing
        // here: Turkish and Azeri case 'i' otherwise, so a feature's name and stableId differed by machine (until 4.16.3).
        return string.Join(" ", humanized.Split(' ').Select(word =>
            word.Length == 0 ? word
            : ApostropheInAWordRegex().IsMatch(word) ? char.ToUpperInvariant(word[0]) + word[1..]
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word)));
    }

    /// <summary>
    /// 5.9.0's <see cref="Titleize"/>, frozen: the name a scenario's stable id was computed from on every lane that
    /// titleizes a feature name, so a feature 5.9.1 shows differently (as written on the Gherkin lanes, an apostrophe kept
    /// on the others) keeps its scenarios' ids and their history (plans/REPORT_UI_FIXES_PLAN.md N3, Q5). Never change it:
    /// a change gives every such scenario a new id.
    /// </summary>
    internal static string TitleizeAsOf590(string input)
    {
        var humanized = Humanize(input, PascalCaseWordPartsRegex590(), keepApostropheWords: false);
        return humanized.Length == 0
            ? input
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(humanized);
    }

    /// <summary>
    /// Converts to camelCase (PascalCase with lowercase first character).
    /// </summary>
    internal static string Camelize(this string input)
    {
        var word = Pascalize(input);
        return word.Length > 0
            ? $"{char.ToLowerInvariant(word[0])}{word[1..]}"
            : word;
    }

    private static string Pascalize(string input) =>
        PascalizeRegex().Replace(input, match => match.Groups[1].Value.ToUpperInvariant());

    private static string Humanize(string input, Regex wordParts, bool keepApostropheWords)
    {
        if (input.All(char.IsUpper))
            return input;

        if (input.IndexOfAny(['_', '-']) >= 0)
            return FromPascalCase(FromUnderscoreDashSeparatedWords(input), wordParts, keepApostropheWords);

        return FromPascalCase(input, wordParts, keepApostropheWords);
    }

    private static string FromUnderscoreDashSeparatedWords(string input) =>
        string.Create(input.Length, input, (span, state) =>
        {
            state.AsSpan().CopyTo(span);
            span.Replace('_', ' ');
            span.Replace('-', ' ');
        });

    // keepApostropheWords is 5.9.1's reading (N2 and E16), false is 5.9.0's
    private static string FromPascalCase(string input, Regex wordParts, bool keepApostropheWords)
    {
        var result = string.Join(" ", wordParts
            .Matches(input)
            .Cast<Match>()
            .Select(match =>
            {
                var value = match.Value;
                if (keepApostropheWords && ApostropheInAWordRegex().IsMatch(value))
                    return value;
                // the letters decide whether a word is in capitals: until 5.9.1 the comma after "HTTP," made it "Http,"
                var letters = keepApostropheWords ? value.TrimEnd(',', ';') : value;
                return letters.All(char.IsUpper) &&
                       (value.Length > 1 || (match.Index > 0 && input[match.Index - 1] == ' ') || value == "I")
                    ? value
                    : value.ToLowerInvariant();
            }));

        if (result.All(c => c == ' ' || char.IsUpper(c)) && result.Contains(' '))
            result = result.ToLowerInvariant();

        return result.Length > 0
            ? $"{char.ToUpperInvariant(result[0])}{result[1..]}"
            : result;
    }
}
