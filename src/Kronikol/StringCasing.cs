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
    // From Humanizer's StringHumanizeExtensions
    private const string PascalCaseWordPartsPattern =
        @"(\p{Lu}?\p{Ll}+|[0-9]+\p{Ll}*|\p{Lu}+(?=\p{Lu}|[0-9]|\b)|\p{Lo}+)[,;]?";


    // From Humanizer's InflectorExtensions
    private const string PascalizePattern = @"(?:[ _-]+|^)(.)";

    [GeneratedRegex(PascalCaseWordPartsPattern, RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture)]
    private static partial Regex PascalCaseWordPartsRegex();


    [GeneratedRegex(PascalizePattern)]
    private static partial Regex PascalizeRegex();

    /// <summary>
    /// Humanizes and applies Title Case. Equivalent to Humanizer's Titleize() / Humanize(LetterCasing.Title).
    /// </summary>
    public static string Titleize(this string input)
    {
        var humanized = Humanize(input);
        return humanized.Length == 0
            ? input
            // Invariant, as every casing here: Turkish and Azeri case 'i' otherwise, so a feature's name and stableId
            // differed by machine (until 4.16.3).
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

    private static string Humanize(string input)
    {
        if (input.All(char.IsUpper))
            return input;

        if (input.IndexOfAny(['_', '-']) >= 0)
            return FromPascalCase(FromUnderscoreDashSeparatedWords(input));

        return FromPascalCase(input);
    }

    private static string FromUnderscoreDashSeparatedWords(string input) =>
        string.Create(input.Length, input, (span, state) =>
        {
            state.AsSpan().CopyTo(span);
            span.Replace('_', ' ');
            span.Replace('-', ' ');
        });

    private static string FromPascalCase(string input)
    {
        var result = string.Join(" ", PascalCaseWordPartsRegex()
            .Matches(input)
            .Cast<Match>()
            .Select(match =>
            {
                var value = match.Value;
                return value.All(char.IsUpper) &&
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
