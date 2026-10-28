#:project ../../src/Kronikol/Kronikol.csproj
#:property AssemblyName=Kronikol.Tests
#:property PublishAot=false
#:property Nullable=enable

// What Kronikol 5.12.0's title formatters make of test names, and what a rule that keeps the source's capitals would.
// AssemblyName=Kronikol.Tests borrows the test project's InternalsVisibleTo, for the internal xUnit formatter.
//
//   dotnet run --file probe.cs -- examples
//   dotnet run --file probe.cs -- corpus names.tsv [details.tsv]

using System.Text.RegularExpressions;
using Kronikol;

var mode = args.Length > 0 ? args[0] : "examples";
if (mode == "examples")
    Examples();
else if (mode == "words")
    Words();
else
    Corpus(args[1], args.Length > 2 ? args[2] : null);

static void Words()
{
    // The issue's suite words, and the words BreakfastProvider's own names call for
    string[] issueWords = ["AcmePay", "API", "LOC", "MCA", "SBR"];
    string[] bpWords = ["gRPC", "OpenAPI", "AsyncAPI", "ClickHouse", "SQL", "DuckDB", "UI"];
    (string Kind, string Name, string[] Words)[] cases =
    [
        ("feature", "AcmePayWebhookTests", issueWords),
        ("scenario", "Webhook_LeadCreatedViaApiJourney_ForAnEligibleCustomer", issueWords),
        ("scenario", "WhenMcaFundingActivates_TheLocIsDrawn", issueWords),
        ("scenario", "ApiKeyIsRotated", issueWords),
        ("scenario", "TheSbrIsFiledForTheLoc", issueWords),
        ("scenario", "TheApisAreListed", issueWords),
        ("feature", "Grpc_Order_Status_Tests", bpWords),
        ("feature", "Specifications_Open_Api_Scalar_UI_Tests", bpWords),
        ("feature", "Specifications__Async_Api_Feature", bpWords),
        ("feature", "ClickHouseSqlTranslatorTests", bpWords),
        ("feature", "DuckDbClickHouseQueryEngineTests", bpWords),
        ("scenario", "The_Scalar_UI_endpoint_should_return_a_valid_page", bpWords),
        ("scenario", "GrpcStreamsTheOrderUpdates", bpWords),
        ("scenario", "Two_gRPC_hops_carry_the_identity_to_the_third_host", bpWords),
        ("scenario", "Check_out_the_basket", ["CheckOut"]),
        // the rest of BreakfastProvider's 14 feature names with a Pascal acronym (corpus.txt)
        ("feature", "Grpc_Recipe_Summary_Tests", bpWords),
        ("feature", "Grpc_Stream_Order_Updates_Tests", bpWords),
        ("feature", "Specifications_Async_Api_Tests", bpWords),
        ("feature", "Specifications_Open_Api_Tests", bpWords),
        ("feature", "Grpc__Order_Status_Feature", bpWords),
        ("feature", "Grpc__Recipe_Summary_Feature", bpWords),
        ("feature", "Grpc__Stream_Order_Updates_Feature", bpWords),
        ("feature", "Specifications__Open_Api_Feature", bpWords),
        ("feature", "Specifications__Open_Api_Scalar_UI_Feature", bpWords),
    ];
    Console.WriteLine("kind | name | shown by the candidate C rule | with the words");
    foreach (var (kind, name, words) in cases)
    {
        var shown = kind == "feature" ? ScenarioTitleResolver.FormatFeatureName(name) : CandidateC.Format(name);
        Console.WriteLine($"{kind} | {name} | {shown} | {TitleWords.Apply(shown, words)}");
    }

    // BDDfy's own default humaniser writes "When m c a funding activates" for WhenMCAFundingActivates (plan 4.6)
    Console.WriteLine($"bddfy title | When m c a funding activates | When m c a funding activates | {TitleWords.Apply("When m c a funding activates", issueWords)}");
    // the feature formatter splits digits where the scenario one glues them (plan F3)
    Console.WriteLine($"FormatFeatureName(Returns404WhenMissing) = {ScenarioTitleResolver.FormatFeatureName("Returns404WhenMissing")}");
}

static void Examples()
{
    string[] classes =
    [
        "AcmePayWebhookTests", "MCAFundingTests", "McaFundingTests", "ApiKeyTests", "HTTPClientTests",
        "PostgreSQLStoreTests", "OAuth2LoginTests", "IOStreamTests", "UKShippingTests", "GraphQLTests",
    ];
    Console.WriteLine("## Feature names: FormatFeatureName (StringCasing.Titleize)");
    foreach (var name in classes)
        Console.WriteLine($"{name,-34} -> {ScenarioTitleResolver.FormatFeatureName(name)}");

    string[] methods =
    [
        "Webhook_LeadCreatedViaApiJourney_ForAnEligibleCustomer",
        "WhenMcaFundingActivates_TheLocIsDrawn",
        "WhenMCAFundingActivates_TheLOCIsDrawn",
        "Webhook_LeadCreatedViaAPIJourney_ForAnEligibleCustomer",
        "Returns_404_when_the_API_key_is_missing",
        "Given_the_UK_store_when_I_pay_then_VAT_is_added",
        "WhenIOpenTheApp",
        "Returns404WhenMissing",
        "Retries3TimesThenFails",
        "V2ApiReturnsOk",
        "AcmePayRejectsTheWebhook",
        "PostgreSQLStoreKeepsTheOrder",
        "OrdersAreSentToKafka",
        "ShipsToTheUK(country: \"GB\")",
        "Ns.OrderTests.ShipsToTheUK(country: \"GB\")",
    ];
    Console.WriteLine();
    Console.WriteLine("## Scenario names from a method: FormatScenarioDisplayName  |  keep-capitals prototype");
    foreach (var name in methods)
        Console.WriteLine($"{name,-56} -> {ScenarioTitleResolver.FormatScenarioDisplayName(name),-52} | B {OneReader.Format(name),-52} | C {CandidateC.Format(name)}");

    string[] written =
    [
        "Order API returns 404 for a missing SKU",
        "Orders are sent to Kafka and stored in MongoDB",
        "AcmePay rejects a webhook without a signature",
        "When MCA funding activates, the LOC is drawn",
        "iOS users see the new checkout",
    ];
    Console.WriteLine();
    Console.WriteLine("## A display name a person wrote");
    Console.WriteLine("   xUnit [Fact(DisplayName = ..)]: FormatXunitDisplayName(dn, dn); a theory row adds its arguments");
    Console.WriteLine("   TUnit [DisplayName(..)] and MSTest DisplayName: FormatScenarioDisplayName(dn)");
    foreach (var name in written)
    {
        Console.WriteLine($"{name}");
        Console.WriteLine($"   xUnit       -> {ScenarioTitleResolver.FormatXunitDisplayName(name, name)}");
        Console.WriteLine($"   xUnit row   -> {ScenarioTitleResolver.FormatXunitDisplayName(name + "(sku: \"A1\")", name)}");
        Console.WriteLine($"   TUnit/MSTest-> {ScenarioTitleResolver.FormatScenarioDisplayName(name)}");
        Console.WriteLine($"   candidate B -> {OneReader.Format(name)}   (why a written name must not be humanised at all)");
    }

    Console.WriteLine();
    Console.WriteLine("## Feature ids: TitleizeAsOf590 vs Titleize (5.9.1 keeps ids on the frozen copy)");
    foreach (var name in new[] { "Month's report", "HTTP, HTTPS and gRPC", "AcmePayWebhookTests" })
        Console.WriteLine($"{name,-24} shown {name.Titleize(),-26} keyed {StringCasing.TitleizeAsOf590(name)}");
}

static void Corpus(string path, string? detailsPath)
{
    var rows = File.ReadAllLines(path).Select(l => l.Split('\t')).Where(f => f.Length >= 5).ToList();
    using var details = detailsPath is null ? null : new StreamWriter(detailsPath);
    details?.WriteLine("corpus\tkind\tinput\tshown_5.12.0\tkeep_capitals");

    Console.WriteLine("corpus | tests | names with a word in capitals | of them lowered by 5.12.0 | names with a digit against a letter | " +
                      "names with a Pascal acronym (a word list's case) | written display names | of them re-cased | " +
                      "titles candidate B changes | of them case only | titles candidate C changes (ids that move unless frozen) | " +
                      "of them case only | feature names | with a Pascal acronym");
    foreach (var corpus in rows.GroupBy(r => r[0]))
    {
        int tests = 0, capitalWords = 0, lowered = 0, glued = 0, pascalAcronyms = 0, written = 0, recased = 0, changes = 0, caseOnly = 0;
        int changesC = 0, caseOnlyC = 0;
        foreach (var r in corpus)
        {
            tests++;
            var method = r[3];
            var display = r[4];
            var shown = ScenarioTitleResolver.FormatScenarioDisplayName(method);
            var candidate = OneReader.Format(method);
            var sourceWords = KeepCapitals.Words(method);
            if (sourceWords.Skip(1).Any(KeepCapitals.IsCapitalWord) || sourceWords.Skip(1).Contains("I"))
            {
                capitalWords++;
                if (!shown.Split(' ').Skip(1).Any(KeepCapitals.IsCapitalWord))
                    lowered++;
            }
            if (Regex.IsMatch(method, @"\p{L}\d|\d\p{L}"))
                glued++;
            if (sourceWords.Any(w => KeepCapitals.PascalAcronyms.Contains(w)))
                pascalAcronyms++;
            if (shown != candidate)
            {
                changes++;
                if (string.Equals(shown, candidate, StringComparison.OrdinalIgnoreCase))
                    caseOnly++;
                details?.WriteLine($"{r[0]}\tmethod-B\t{method}\t{shown}\t{candidate}");
            }
            var candidateC = CandidateC.Format(method);
            if (shown != candidateC)
            {
                changesC++;
                if (string.Equals(shown, candidateC, StringComparison.OrdinalIgnoreCase))
                    caseOnlyC++;
                details?.WriteLine($"{r[0]}\tmethod-C\t{method}\t{shown}\t{candidateC}");
            }

            if (display.Length > 0)
            {
                written++;
                var displayShown = r[1] is "Fact" or "Theory" or "SkippableFact" or "SkippableTheory"
                    ? ScenarioTitleResolver.FormatXunitDisplayName(display, display)
                    : ScenarioTitleResolver.FormatScenarioDisplayName(display);
                if (displayShown != display.Trim())
                {
                    recased++;
                    details?.WriteLine($"{r[0]}\tdisplay\t{display}\t{displayShown}\t{display.Trim()}");
                }
            }
        }

        var classes = corpus.Select(r => r[2]).Where(c => c.Length > 0).Distinct().ToList();
        var featureLoss = classes.Count(c =>
        {
            var shown = ScenarioTitleResolver.FormatFeatureName(c);
            var lost = KeepCapitals.Words(c).Any(w => KeepCapitals.PascalAcronyms.Contains(w));
            if (lost)
                details?.WriteLine($"{corpus.Key}\tfeature\t{c}\t{shown}\t");
            return lost;
        });
        Console.WriteLine($"{corpus.Key} | {tests} | {capitalWords} | {lowered} | {glued} | {pascalAcronyms} | {written} | {recased} | " +
                          $"{changes} | {caseOnly} | {changesC} | {caseOnlyC} | {classes.Count} | {featureLoss}");
    }
}

/// <summary>
/// The candidate rule (candidate B in the plan): the feature formatter's own word reader, StringCasing's private
/// Humanize with 5.9.1's pattern, read by reflection, so a scenario title is the feature reading in sentence case.
/// It keeps a word in capitals and the pronoun I, and splits digits from letters.
/// </summary>
static class OneReader
{
    static readonly System.Reflection.MethodInfo HumanizeMethod = typeof(StringCasing).GetMethod("Humanize",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    static readonly Regex WordParts = (Regex)typeof(StringCasing).GetMethod("PascalCaseWordPartsRegex",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null)!;

    public static string Humanize(string text) => (string)HumanizeMethod.Invoke(null, [text, WordParts, true])!;

    /// <summary>FormatScenarioDisplayName's reading of the method path and its arguments, with the one word reader.</summary>
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
        var shown = Humanize(name).Trim();
        return shown.Length == 0 ? name.Trim() : shown + arguments;
    }
}

/// <summary>
/// The recommended rule (candidate C): 5.12.0's splitter, plus a break between digits and a capital after them
/// ("404When", "C4False"); then a word in capitals (its letters all upper case, and two or more of them or a digit
/// among them: API, UK, C4) and the pronoun I keep their case, every other word is lower-cased, and the first word gets
/// a capital. Nothing else changes: "p95", "utf8" and "Returns404" stay whole, as 5.12.0 has them.
/// </summary>
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

    /// <summary>FormatScenarioDisplayName's reading of the method path and its arguments, with this rule's casing.</summary>
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

/// <summary>
/// The first prototype rule (candidate A): split as 5.12.0 splits, then lower-case only a word that is capitalised (one
/// capital, first), never a word the source wrote with two or more capitals, nor the pronoun I; the first word gets a
/// capital. Kept to show why the splitter has to change too: it glues digits ("more Than10Visible").
/// </summary>
static partial class KeepCapitals
{
    public static readonly HashSet<string> PascalAcronyms =
    [
        "Api", "Apis", "Http", "Https", "Json", "Xml", "Html", "Sql", "Url", "Uri", "Jwt", "Grpc", "Csv", "Pdf", "Dto",
        "Sku", "Tcp", "Udp", "Dns", "Ssl", "Tls", "Aws", "Gcp", "Sms", "Otp", "Ui", "Io", "Db", "Iban", "Vat", "Gdpr",
        "Crud", "Cqrs", "Saml", "Oidc", "Ttl", "Utc", "Iso", "Cors", "Csrf", "Smtp", "Ftp", "Ssh", "Yaml", "Svg", "Png",
    ];

    public static string[] Words(string name)
    {
        var parenthesis = name.IndexOf('(');
        if (parenthesis >= 0 && name.EndsWith(')'))
            name = name[..parenthesis];
        var dot = name.LastIndexOf('.');
        if (dot >= 0 && !name.Any(char.IsWhiteSpace))
            name = name[(dot + 1)..];
        var split = UpperSequence().Replace(LowerToUpper().Replace(name, "$1 $2"), "$1 $2").Replace("_", " ");
        return split.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    public static bool IsCapitalWord(string word) => word.Count(char.IsUpper) >= 2;

    public static string Format(string name)
    {
        var arguments = "";
        var parenthesis = name.IndexOf('(');
        if (parenthesis >= 0 && name.EndsWith(')') && name.Length - parenthesis > 2)
            arguments = $" [{name[(parenthesis + 1)..^1]}]";
        var words = Words(name);
        if (words.Length == 0)
            return name.Trim();
        var shown = words.Select((w, i) =>
            i == 0 ? char.ToUpperInvariant(w[0]) + w[1..]
            : IsCapitalWord(w) || w == "I" ? w
            : w.ToLowerInvariant());
        return string.Join(" ", shown) + arguments;
    }

    [GeneratedRegex(@"(\p{Ll})(\p{Lu})")]
    private static partial Regex LowerToUpper();

    [GeneratedRegex(@"(\p{Lu}+)(\p{Lu}\p{Ll})")]
    private static partial Regex UpperSequence();
}

/// <summary>
/// The word list as prototyped: an entry matches a run of whole words in the title whose letters and digits, read
/// without case, are the entry's own, and the run is replaced by the entry as written. Longer entries first, and the
/// longest run at each place. A title's own spaces are the only places a run can start or end.
/// </summary>
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
                // a word's own punctuation stays outside the entry: "UI," keeps its comma
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
