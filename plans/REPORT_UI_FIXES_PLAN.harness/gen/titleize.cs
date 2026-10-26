// N2, measured: 5.9.0's Titleize against the plan's change, on the names of section 2.4 and section 4.4.
// usage: dotnet run --file gen/titleize.cs
// Old is a copy of src/Kronikol/StringCasing.cs at 37428293 (5.9.0). New takes a run of letters joined by an apostrophe
// (U+0027 or U+2019) as one word, upper-cases its first letter and leaves the rest as written; every other word is cased
// as Old cases it. E16: a word of capitals before a comma is tested on its letters.
using System.Globalization;
using System.Text.RegularExpressions;

string[] names =
[
    "Month's birthday", "Month’s birthday", "O'Brien's order", "Don't bake twice", "Customer's Orders",
    "Order Status via gRPC", "Orders: retry on 503", "HTTP, then the order", "HTTP request returns 200",
    "OrderProcessing", "my_feature_name", "AlternativeEvidenceScenarios", "rock 'n' roll", "the 'quoted' word",
    "v1.2 release", "3d printing", "Pancakes \U0001F95E flip",
];
Console.WriteLine($"ToTitleCase(\"month’s\") = {CultureInfo.InvariantCulture.TextInfo.ToTitleCase("month’s")}");
Console.WriteLine($"ToTitleCase(\"month's\")  = {CultureInfo.InvariantCulture.TextInfo.ToTitleCase("month's")}");
Console.WriteLine($"ToTitleCase(\"o'brien's\") = {CultureInfo.InvariantCulture.TextInfo.ToTitleCase("o'brien's")}");
Console.WriteLine();
Console.WriteLine("| Written | 5.9.0 | N2 |");
Console.WriteLine("|---|---|---|");
foreach (var n in names) Console.WriteLine($"| `{n}` | {Casing.Titleize(n, false)} | {Casing.Titleize(n, true)} |");

static partial class Casing
{
    const string Old = @"(\p{Lu}?\p{Ll}+|[0-9]+\p{Ll}*|\p{Lu}+(?=\p{Lu}|[0-9]|\b)|\p{Lo}+)[,;]?";
    // N2: a word with an apostrophe between letters is matched whole, first.
    const string New = @"(\p{L}+(?:['’]\p{L}+)+|\p{Lu}?\p{Ll}+|[0-9]+\p{Ll}*|\p{Lu}+(?=\p{Lu}|[0-9]|\b)|\p{Lo}+)[,;]?";
    [GeneratedRegex(Old, RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture)] private static partial Regex OldRx();
    [GeneratedRegex(New, RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture)] private static partial Regex NewRx();
    static bool Apostrophe(string w) => Regex.IsMatch(w, @"\p{L}['’]\p{L}");

    public static string Titleize(string input, bool n2)
    {
        var h = Humanize(input, n2);
        if (h.Length == 0) return input;
        if (!n2) return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(h);
        // N2: ToTitleCase word by word, but a word with an apostrophe between letters only gains a capital first letter.
        return string.Join(" ", h.Split(' ').Select(w => w.Length == 0 ? w
            : Apostrophe(w) ? char.ToUpperInvariant(w[0]) + w[1..]
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w)));
    }

    static string Humanize(string input, bool n2)
    {
        if (input.All(char.IsUpper)) return input;
        if (input.IndexOfAny(['_', '-']) >= 0) return FromPascalCase(input.Replace('_', ' ').Replace('-', ' '), n2);
        return FromPascalCase(input, n2);
    }

    static string FromPascalCase(string input, bool n2)
    {
        var rx = n2 ? NewRx() : OldRx();
        var result = string.Join(" ", rx.Matches(input).Cast<Match>().Select(match =>
        {
            var value = match.Value;
            if (n2 && Apostrophe(value)) return value; // kept as written
            // E16 (N2 only): the capitals test reads the letters, so a trailing comma does not lower-case "HTTP,"
            var letters = n2 ? value.TrimEnd(',', ';') : value;
            return letters.All(char.IsUpper) &&
                   (value.Length > 1 || (match.Index > 0 && input[match.Index - 1] == ' ') || value == "I")
                ? value
                : value.ToLowerInvariant();
        }));
        if (result.All(c => c == ' ' || char.IsUpper(c)) && result.Contains(' ')) result = result.ToLowerInvariant();
        return result.Length > 0 ? $"{char.ToUpperInvariant(result[0])}{result[1..]}" : result;
    }
}
