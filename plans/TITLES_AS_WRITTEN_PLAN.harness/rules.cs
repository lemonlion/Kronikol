#:property PublishAot=false
#:property Nullable=enable

// Standalone copies of two title rules, for a deep dive (no Kronikol project referenced):
//   Today      = ScenarioTitleResolver.FormatScenarioDisplayName / Humanize, copied from
//                src/Kronikol/ScenarioTitleResolver.cs (unchanged since 4.16.3)
//   CandidateC = class CandidateC, copied from plans/TITLES_AS_WRITTEN_PLAN.harness/probe.cs
//
//   dotnet run rules.cs -- verify <names.tsv> <details.tsv>
//   dotnet run rules.cs -- dump <names.tsv> <out.tsv>
//   dotnet run rules.cs -- shapes

using System.Text;
using System.Text.RegularExpressions;

Console.OutputEncoding = Encoding.UTF8;
var mode = args.Length > 0 ? args[0] : "shapes";
switch (mode)
{
    case "verify": Verify(args[1], args[2]); break;
    case "dump": Dump(args[1], args[2]); break;
    case "one":
        foreach (var a in args.Skip(1))
            Console.WriteLine($"{a} | {Today.FormatScenarioDisplayName(a)} | {CandidateC.Format(a)}");
        break;
    case "variants": Variants(args[1], args[2]); break;
    case "vshapes": VariantShapes(); break;
    case "vone":
        foreach (var a in args.Skip(1))
        {
            var rec = Variant.Format(a, Sets.VariantSet.First(v => v.Name == "RECSEG").O);
            var c = CandidateC.Format(a);
            Console.WriteLine($"{(rec == c ? "same" : "DIFF")} {a} | C {c} | RECSEG {rec}");
        }
        break;
    case "extra":
        foreach (var n in new[] { "GIVEN_VALID_INPUT_WHEN_SAVED", "ParsesURLsTests", "MCAFundingTests", "Should_Return_A_Fresh_Batch" })
            Console.WriteLine($"FormatFeatureName {n} -> {FeatureToday.Titleize(n)}");
        foreach (var (title, words) in new (string, string[])[] {
            ("X unit3 assembly contains binding annotated types", ["xUnit"]),
            ("X unit3 assembly contains binding annotated types", ["xUnit3"]),
            ("Implements I tracking component", ["ITrackingComponent"]),
            ("AP is return json", ["APIs"]),
            ("Parses UR ls", ["URLs"]),
            ("A taken I pv6 port", ["IPv6"]),
            ("Dependency filter features contracted when more than10 visible scenarios", ["than 10"]) })
            Console.WriteLine($"TitleWords [{string.Join(", ", words)}] {title} -> {TitleWords.Apply(title, words)}");
        break;
    default: Shapes(); break;
}

static void Verify(string namesPath, string detailsPath)
{
    // details.tsv lists, for every corpus method name candidate C changes, 5.12.0's title and candidate C's (probe.cs)
    var expected = new Dictionary<(string, string), (string Today, string C)>();
    foreach (var line in File.ReadLines(detailsPath).Skip(1))
    {
        var f = line.Split('\t');
        if (f[1] == "method-C")
            expected[(f[0], f[2])] = (f[3], f[4]);
    }
    int rows = 0, listed = 0, todayMismatch = 0, cMismatch = 0, unlistedButDiffer = 0;
    var names = new HashSet<(string, string)>();
    foreach (var line in File.ReadLines(namesPath))
    {
        var f = line.Split('\t');
        if (f.Length < 5) continue;
        rows++;
        var method = f[3];
        var t = Today.FormatScenarioDisplayName(method);
        var c = CandidateC.Format(method);
        if (expected.TryGetValue((f[0], method), out var e))
        {
            listed++;
            names.Add((f[0], method));
            if (e.Today != t) { todayMismatch++; Console.WriteLine($"TODAY MISMATCH {method}: {e.Today} vs {t}"); }
            if (e.C != c) { cMismatch++; Console.WriteLine($"C MISMATCH {method}: {e.C} vs {c}"); }
        }
        else if (t != c)
        {
            unlistedButDiffer++;
            Console.WriteLine($"UNLISTED DIFFER {method}: {t} vs {c}");
        }
    }
    Console.WriteLine($"rows {rows}, rows listed in details.tsv {listed} ({names.Count} distinct (corpus, name)), " +
                      $"listed in details.tsv {expected.Count}, today mismatches {todayMismatch}, C mismatches {cMismatch}, " +
                      $"unlisted rows where today != C {unlistedButDiffer}");
}

static void Dump(string namesPath, string outPath)
{
    using var w = new StreamWriter(outPath, false, new UTF8Encoding(false));
    w.NewLine = "\n";
    w.WriteLine("corpus\tattribute\tclass\tmethod\ttoday\tC\tCwords");
    foreach (var line in File.ReadLines(namesPath))
    {
        var f = line.Split('\t');
        if (f.Length < 5) continue;
        var method = f[3];
        w.WriteLine($"{f[0]}\t{f[1]}\t{f[2]}\t{method}\t{Today.FormatScenarioDisplayName(method)}\t{CandidateC.Format(method)}\t{string.Join("|", CandidateC.Words(method))}");
    }
}

static void Shapes()
{
    string[] shapes =
    [
        "ParsesURLs", "GetsAllIDs", "ListsVMs", "APIsReturnJson",
        "GIVEN_VALID_INPUT_WHEN_SAVED", "Should_Return_OK",
        "Should_ReturnOK_WhenValid", "Get_HTTP_response",
        "NUnitRuns", "XUnitRuns", "MSTestRuns", "OAuthLogin", "IOStreamReads", "iOSApp", "IosApp", "macOSBuild",
        "eBayListing", "PostgreSQLQuery", "PostgreSqlQuery", "JavaScriptRuns",
        "OAuth2Token", "V2Api", "Http2Works", "S3Bucket", "Base64Encodes", "X509Certificate", "Utf8Bytes",
        "Returns404WhenMissing",
        "ReturnsAValue", "IAmAdmin", "IsXValid", "ATest",
        "ÜberCaseWorks", "ÄÖÜ_wird_gelesen", "Größe_ÄndertSich", "İstanbulIsChecked", "测试Works", "ΑλφαΒήτα",
        "Month's report", "O'Brien's order", "iOS users see the new checkout",
        "ShipsToTheUK(country: \"GB\")", "Ns.OrderTests.ShipsToTheUK(country: \"GB\")",
        "Order API returns 404 for v1.2", "System.Text.Json parses numbers",
    ];
    foreach (var s in shapes)
        Console.WriteLine($"{s} | {Today.FormatScenarioDisplayName(s)} | {CandidateC.Format(s)}");
}

static void Variants(string namesPath, string outPath)
{
    using var w = new StreamWriter(outPath, false, new UTF8Encoding(false));
    w.NewLine = "\n";
    w.WriteLine("corpus\tmethod\ttoday\tC\t" + string.Join("\t", Sets.VariantSet.Select(v => v.Name)));
    foreach (var line in File.ReadLines(namesPath))
    {
        var f = line.Split('\t');
        if (f.Length < 5) continue;
        var m = f[3];
        w.WriteLine($"{f[0]}\t{m}\t{Today.FormatScenarioDisplayName(m)}\t{CandidateC.Format(m)}\t" +
                    string.Join("\t", Sets.VariantSet.Select(v => Variant.Format(m, v.O))));
    }
}

static void VariantShapes()
{
    string[] shapes =
    [
        "ParsesURLs", "GetsAllIDs", "ListsVMs", "APIsReturnJson", "IDsAreUnique", "SKUsAreListed", "GUIDsDiffer",
        "WhenXIsNull", "TheIDAsString", "WhenURLIsInvalid", "WhenAPIIsDown", "Given_IDs_are_unique",
        "GIVEN_VALID_INPUT_WHEN_SAVED", "Should_Return_OK", "HTTP_200", "GET_API_KEY", "TEST",
        "Should_ReturnOK_WhenValid", "Get_HTTP_response",
        "NUnitRuns", "XUnitRuns", "MSTestRuns", "OAuthLogin", "IOStreamReads", "iOSApp", "IosApp", "macOSBuild",
        "eBayListing", "PostgreSQLQuery", "PostgreSqlQuery", "JavaScriptRuns",
        "OAuth2Token", "V2Api", "Http2Works", "S3Bucket", "Base64Encodes", "X509Certificate", "Utf8Bytes",
        "Returns404WhenMissing", "E2ETestsPass", "P2PSync", "Uses_F6F6F6", "Returns200OK", "Default_64MB_cache",
        "UseC4False", "Case1A",
        "ReturnsAValue", "IAmAdmin", "IsXValid", "ATest", "First_N_items", "Given_I_pay", "WhenIOpenTheApp",
        "Given_IAmLoggedIn_Then_ok", "Implements_ITrackingComponent", "A_taken_IPv6_port", "The_error_is_an_IOException",
        "Given_IDIsEmpty_Throws", "Two_gRPC_hops", "Sets_headersHidden_on_container", "Parses_xUnit_Assert_Equal_failure",
        "The_scenario_s_tags", "Ten_s_timeout",
        "ÜberCaseWorks", "ÄÖÜ_wird_gelesen", "İstanbulIsChecked", "ΑλφαΒήτα",
        "Month's report", "ShipsToTheUK(country: \"GB\")", "Order API returns 404 for v1.2",
        // C's own corrections from the plan, which a change must keep
        "The_Scalar_UI_endpoint_should_return_a_valid_page", "Response_Has_OK_StatusCode", "JSON_that_is_not_whole",
        "Implicit_AND_inserted", "GeneratePlantUml_UseC4False_DoesNotContainC4Include", "R1_renders_parameter_name_columns",
        "WhenMCAFundingActivates_TheLOCIsDrawn",
    ];
    Console.WriteLine("input | today | C | REC | ALL");
    foreach (var s in shapes)
        Console.WriteLine($"{s} | {Today.FormatScenarioDisplayName(s)} | {CandidateC.Format(s)} | " +
                          $"{Variant.Format(s, Sets.VariantSet.First(v => v.Name == "REC").O)} | {Variant.Format(s, Sets.VariantSet.First(v => v.Name == "ALL").O)}");
}

static class Sets
{
    public static readonly (string Name, Variant.Options O)[] VariantSet =
    [
        ("V1_caps", new(Screaming: true)),
        ("V2_plural", new(Plural: true)),
        ("V3_lone", new(Lone: true)),
        ("V4a_iface", new(InterfaceSegments: true)),
        ("V4b_camel", new(CamelSegments: true)),
        ("V5_digits", new(Digits: true)),
        ("V6_poss", new(Possessive: true)),
        ("REC", new(Screaming: true, Plural: true, Lone: true, Digits: true)),
        ("SEG", new(InterfaceSegments: true, CamelSegments: true)),
        ("RECSEG", new(Screaming: true, Plural: true, Lone: true, Digits: true, InterfaceSegments: true, CamelSegments: true)),
        ("ALL", new(Screaming: true, Plural: true, Lone: true, InterfaceSegments: true, CamelSegments: true, Digits: true, Possessive: true)),
        ("NONE", new()),
    ];
}

/// <summary>Candidate C with each proposed change behind a switch; every switch off is candidate C exactly.</summary>
static partial class Variant
{
    public record Options(bool Screaming = false, bool Plural = false, bool Lone = false, bool InterfaceSegments = false,
                          bool CamelSegments = false, bool Digits = false, bool Possessive = false);

    record struct Word(string Text, bool AsWritten);

    static List<Word> Words(string name, Options o)
    {
        var words = new List<Word>();
        var segmented = (o.InterfaceSegments || o.CamelSegments) && name.Contains('_');
        foreach (var segment in segmented ? name.Split('_') : [name])
        {
            if (segment.Length == 0) continue;
            if (segmented && ((o.CamelSegments && CamelSegment().IsMatch(segment)) ||
                              (o.InterfaceSegments && InterfaceSegment().IsMatch(segment))))
            {
                words.Add(new(segment, true));
                continue;
            }
            var split = (o.Digits ? DigitBeforeWord().Replace(segment, " ") : DigitToUpper().Replace(segment, "$1 $2"));
            split = LowerToUpper().Replace(split, "$1 $2");
            split = (o.Plural ? UpperSequenceKeepingPlural() : UpperSequence()).Replace(split, "$1 $2").Replace("_", " ");
            words.AddRange(split.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => new Word(w, false)));
        }
        if (o.Possessive)
        {
            // a lone s after a word that ends in a letter is that word's possessive: "the scenario s tags"
            for (var i = words.Count - 1; i > 0; i--)
                if (words[i] is { Text: "s", AsWritten: false } && char.IsLetter(words[i - 1].Text[^1]))
                {
                    words[i - 1] = words[i - 1] with { Text = words[i - 1].Text + "'s" };
                    words.RemoveAt(i);
                }
        }
        return words;
    }

    static bool InCapitals(string word, Options o)
    {
        var letters = word.Where(char.IsLetter).ToArray();
        if (letters.Length > 0 && letters.All(char.IsUpper) && (letters.Length > 1 || word.Any(char.IsDigit)))
            return true;
        if (o.Plural && PluralCapitals().IsMatch(word))
            return true;
        if (o.Lone)
            return letters.Length == 1 && char.IsUpper(letters[0]) && letters[0] != 'A';
        return word == "I";
    }

    public static string Humanize(string text, Options o)
    {
        var words = Words(text, o);
        if (words.Count == 0)
            return "";
        // a name in capitals throughout says nothing about which words are acronyms: sentence case, as Humanizer does
        var shouting = o.Screaming && words.All(w => !w.Text.Any(char.IsLower)) && words.Count(w => w.Text.Any(char.IsLetter)) > 1;
        return string.Join(" ", words.Select((w, i) =>
            w.AsWritten ? w.Text
            : !shouting && InCapitals(w.Text, o) ? w.Text
            : i == 0 ? char.ToUpperInvariant(w.Text[0]) + w.Text[1..].ToLowerInvariant()
            : w.Text.ToLowerInvariant()));
    }

    public static string Format(string name, Options o)
    {
        var arguments = "";
        var parenthesis = name.IndexOf('(');
        if (parenthesis >= 0 && name.EndsWith(')'))
        {
            if (name.Length - parenthesis > 2)
                arguments = $" [{name[(parenthesis + 1)..^1]}]";
            name = name[..parenthesis];
        }
        var dot = name.LastIndexOf('.');
        if (dot >= 0 && !name.Any(char.IsWhiteSpace) && dot + 1 < name.Length && (char.IsLetter(name[dot + 1]) || name[dot + 1] == '_'))
            name = name[(dot + 1)..];
        var shown = Humanize(name, o);
        return shown.Length == 0 ? name.Trim() : shown + arguments;
    }

    [GeneratedRegex(@"(\d)(\p{Lu})")]
    private static partial Regex DigitToUpper();

    // only before a capitalised word (404When, C4False) or two or more capitals that end a word (200OK, 64MB):
    // a lone capital after digits stays with them (E2E, P2P, F6F6F6)
    [GeneratedRegex(@"(?<=\d)(?=\p{Lu}\p{Ll}|\p{Lu}{2,}(?!\p{Ll}))")]
    private static partial Regex DigitBeforeWord();

    [GeneratedRegex(@"(\p{Ll})(\p{Lu})")]
    private static partial Regex LowerToUpper();

    [GeneratedRegex(@"(\p{Lu}+)(\p{Lu}\p{Ll})")]
    private static partial Regex UpperSequence();

    // the same, but not when the capital and lower-case letter are a plural s closing the run (URLs, IDs): "Is" and "As"
    // are words (XIs, IDAs), so APIs stays "AP is" for the word list
    [GeneratedRegex(@"(\p{Lu}+)(?![\p{Lu}-[IA]]s(?!\p{Ll}))(\p{Lu}\p{Ll})")]
    private static partial Regex UpperSequenceKeepingPlural();

    [GeneratedRegex(@"^\p{Lu}{2,}s$")]
    private static partial Regex PluralCapitals();

    // a camelCase identifier written as its own part of a snake name: stableId, gRPC, xUnit3, innerHTML
    [GeneratedRegex(@"^\p{Ll}[\p{L}\d]*\p{Lu}")]
    private static partial Regex CamelSegment();

    // an interface written as its own part: ITrackingComponent, IHttpClientFactory, and IPv6
    [GeneratedRegex(@"^I\p{Lu}(?!s(?:\p{Lu}|$))\p{Ll}")]
    private static partial Regex InterfaceSegment();
}

/// <summary>ScenarioTitleResolver as at 5.12.0, the parts FormatScenarioDisplayName runs, copied verbatim.</summary>
static partial class Today
{
    private const int MaxParameterLength = 200;

    public static string FormatScenarioDisplayName(string testDisplayName)
    {
        string methodPath;
        string? parameters = null;

        var parenIndex = testDisplayName.IndexOf('(');
        if (parenIndex >= 0 && testDisplayName.EndsWith(')'))
        {
            methodPath = testDisplayName[..parenIndex];
            var paramContent = WithoutClosingParenthesis(testDisplayName[(parenIndex + 1)..]);
            if (paramContent.Length > 0)
            {
                parameters = paramContent.Length > MaxParameterLength
                    ? paramContent[..MaxParameterLength] + "\u2026"
                    : paramContent;
            }
        }
        else
        {
            methodPath = testDisplayName;
        }

        var humanized = Humanize(MethodName(methodPath));
        if (humanized.Length == 0)
            return testDisplayName.Trim();

        return parameters is not null ? $"{humanized} [{parameters}]" : humanized;
    }

    private static string MethodName(string methodPath)
    {
        var lastDotIndex = methodPath.LastIndexOf('.');
        if (lastDotIndex < 0 || methodPath.Any(char.IsWhiteSpace))
            return methodPath;

        var methodName = methodPath[(lastDotIndex + 1)..];
        return methodName.Length > 0 && (char.IsLetter(methodName[0]) || methodName[0] == '_')
            ? methodName
            : methodPath;
    }

    private static string WithoutClosingParenthesis(string text) =>
        text.EndsWith(')') ? text[..^1] : text;

    public static string Humanize(string name)
    {
        var humanized = SplitPascalCase(name);
        humanized = humanized.Replace("_", " ");
        humanized = MultipleSpacesRegex().Replace(humanized, " ").Trim();
        return humanized.Length == 0
            ? humanized
            : char.ToUpperInvariant(humanized[0]) + humanized[1..].ToLowerInvariant();
    }

    private static string SplitPascalCase(string input)
    {
        var result = LowerToUpperRegex().Replace(input, "$1 $2");
        result = UpperSequenceRegex().Replace(result, "$1 $2");
        return result;
    }

    [GeneratedRegex(@"(\p{Ll})(\p{Lu})")]
    private static partial Regex LowerToUpperRegex();

    [GeneratedRegex(@"(\p{Lu}+)(\p{Lu}\p{Ll})")]
    private static partial Regex UpperSequenceRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpacesRegex();
}

/// <summary>probe.cs's CandidateC, copied verbatim.</summary>
static partial class CandidateC
{
    public static string[] Words(string name)
    {
        var split = DigitToUpper().Replace(name, "$1 $2");
        split = UpperSequence().Replace(LowerToUpper().Replace(split, "$1 $2"), "$1 $2").Replace("_", " ");
        return split.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    public static bool InCapitals(string word)
    {
        var letters = word.Where(char.IsLetter).ToArray();
        return letters.Length > 0 && letters.All(char.IsUpper) && (letters.Length > 1 || word.Any(char.IsDigit));
    }

    public static string Humanize(string text)
    {
        var words = Words(text);
        if (words.Length == 0)
            return "";
        return string.Join(" ", words.Select((w, i) =>
            InCapitals(w) || w == "I" ? w
            : i == 0 ? char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()
            : w.ToLowerInvariant()));
    }

    public static string Format(string name)
    {
        var arguments = "";
        var parenthesis = name.IndexOf('(');
        if (parenthesis >= 0 && name.EndsWith(')'))
        {
            if (name.Length - parenthesis > 2)
                arguments = $" [{name[(parenthesis + 1)..^1]}]";
            name = name[..parenthesis];
        }
        var dot = name.LastIndexOf('.');
        if (dot >= 0 && !name.Any(char.IsWhiteSpace) && dot + 1 < name.Length && (char.IsLetter(name[dot + 1]) || name[dot + 1] == '_'))
            name = name[(dot + 1)..];
        var shown = Humanize(name);
        return shown.Length == 0 ? name.Trim() : shown + arguments;
    }

    [GeneratedRegex(@"(\d)(\p{Lu})")]
    private static partial Regex DigitToUpper();

    [GeneratedRegex(@"(\p{Ll})(\p{Lu})")]
    private static partial Regex LowerToUpper();

    [GeneratedRegex(@"(\p{Lu}+)(\p{Lu}\p{Ll})")]
    private static partial Regex UpperSequence();
}

/// <summary>StringCasing.Titleize (FormatFeatureName) as at 5.12.0, copied from src/Kronikol/StringCasing.cs.</summary>
static partial class FeatureToday
{
    private const string PascalCaseWordPartsPattern =
        @"(\p{L}+(?:['\u2019]\p{L}+)+|\p{Lu}?\p{Ll}+|[0-9]+\p{Ll}*|\p{Lu}+(?=\p{Lu}|[0-9]|\b)|\p{Lo}+)[,;]?";

    [GeneratedRegex(PascalCaseWordPartsPattern, RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture)]
    private static partial Regex PascalCaseWordPartsRegex();

    [GeneratedRegex(@"\p{L}['\u2019]\p{L}")]
    private static partial Regex ApostropheInAWordRegex();

    public static string Titleize(string input)
    {
        var humanized = Humanize(input, PascalCaseWordPartsRegex(), keepApostropheWords: true);
        if (humanized.Length == 0)
            return input;
        return string.Join(" ", humanized.Split(' ').Select(word =>
            word.Length == 0 ? word
            : ApostropheInAWordRegex().IsMatch(word) ? char.ToUpperInvariant(word[0]) + word[1..]
            : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word)));
    }

    private static string Humanize(string input, Regex wordParts, bool keepApostropheWords)
    {
        if (input.All(char.IsUpper))
            return input;
        if (input.IndexOfAny(['_', '-']) >= 0)
            return FromPascalCase(input.Replace('_', ' ').Replace('-', ' '), wordParts, keepApostropheWords);
        return FromPascalCase(input, wordParts, keepApostropheWords);
    }

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
                var letters = keepApostropheWords ? value.TrimEnd(',', ';') : value;
                return letters.All(char.IsUpper) &&
                       (value.Length > 1 || (match.Index > 0 && input[match.Index - 1] == ' ') || value == "I")
                    ? value
                    : value.ToLowerInvariant();
            }));
        if (result.All(c => c == ' ' || char.IsUpper(c)) && result.Contains(' '))
            result = result.ToLowerInvariant();
        return result.Length > 0 ? $"{char.ToUpperInvariant(result[0])}{result[1..]}" : result;
    }
}

/// <summary>probe.cs's TitleWords prototype (R2), copied verbatim.</summary>
static class TitleWords
{
    public static string Apply(string title, IEnumerable<string> entries)
    {
        var keyed = entries
            .Select(e => (Entry: e, Key: new string(e.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray())))
            .Where(e => e.Key.Length > 0)
            .OrderByDescending(e => e.Key.Length)
            .ToArray();
        var words = title.Split(' ');
        var output = new List<string>();
        for (var i = 0; i < words.Length;)
        {
            var matched = false;
            var run = "";
            var best = (Length: 0, Entry: "");
            for (var j = i; j < words.Length && run.Length < 64; j++)
            {
                run += new string(words[j].Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
                if (keyed.FirstOrDefault(e => e.Key == run) is { Entry: { } entry })
                    best = (j - i + 1, entry);
            }
            if (best.Length > 0)
            {
                var last = words[i + best.Length - 1];
                var trailing = new string(last.Reverse().TakeWhile(c => !char.IsLetterOrDigit(c)).Reverse().ToArray());
                output.Add(best.Entry + trailing);
                i += best.Length;
                matched = true;
            }
            if (!matched)
                output.Add(words[i++]);
        }
        return string.Join(" ", output);
    }
}
