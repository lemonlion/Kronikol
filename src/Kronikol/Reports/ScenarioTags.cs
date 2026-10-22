using System.Globalization;

namespace Kronikol.Reports;

/// <summary>
/// The tag conventions Kronikol's adapters share, in one place so an external producer (the widened tests NDJSON, a
/// Cucumber Messages importer, a Playwright reporter) classifies tags exactly the way the adapters do, and the one rule
/// every reader of a report uses to tell whether two tags are the same tag (<see cref="Comparer"/>).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>@endpoint:x</c>: the feature's endpoint, not a tag.</item>
/// <item><c>@happy-path</c> (also <c>@happy_path</c>, <c>@happypath</c> and <c>Happy Path</c>): marks the happy path, not
/// a tag.</item>
/// <item><c>@category:x</c>: the tag <c>x</c>, whatever <c>x</c> spells. 4.x read it as a category; a category is a tag
/// in 5.0.0, so the prefix is accepted and dropped.</item>
/// <item>anything else: a tag.</item>
/// </list>
/// A leading <c>@</c> and surrounding whitespace are removed; the prefixes and the happy-path names match whatever their
/// case.
/// </remarks>
public static class ScenarioTags
{
    /// <summary>
    /// The prefix 4.x gave a category tag (<c>@category:smoke</c>). A category is a tag in 5.0.0: the prefix is accepted
    /// and dropped, so <c>@category:smoke</c> is the tag <c>smoke</c>.
    /// </summary>
    public const string CategoryPrefix = "category:";

    /// <summary>Prefix of an endpoint tag (<c>@endpoint:/customers</c>).</summary>
    public const string EndpointPrefix = "endpoint:";

    /// <summary>The name the report gives the happy-path flag where it shows it as a tag: its chip, its badge.</summary>
    internal const string HappyPathTag = "Happy Path";

    /// <summary>What <see cref="Classify"/> made of a scenario's or a feature's tags.</summary>
    /// <param name="Tags">The tags, each once under <see cref="Comparer"/> in the first spelling met, in source order.</param>
    /// <param name="Endpoint">Value of the first <c>@endpoint:</c> tag, if any.</param>
    /// <param name="IsHappyPath">Whether a happy-path tag was present.</param>
    public sealed record Classified(string[] Tags, string? Endpoint, bool IsHappyPath);

    /// <summary>Strips a leading <c>@</c> and surrounding whitespace from a tag.</summary>
    public static string Normalise(string tag) => tag.Trim().TrimStart('@');

    /// <summary>Splits raw tags into the tags, the endpoint and the happy-path flag.</summary>
    public static Classified Classify(IEnumerable<string>? tags)
    {
        var kept = new List<string>();
        var seen = new HashSet<string>(Comparer);
        string? endpoint = null;
        var happyPath = false;

        foreach (var raw in tags ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var tag = Normalise(raw);
            // A value written with the category prefix is a tag whatever it spells: 4.x never read @category:happy-path
            // as the happy path, nor @category:endpoint:/x as the endpoint.
            var category = tag.StartsWith(CategoryPrefix, StringComparison.OrdinalIgnoreCase);
            if (category)
                tag = tag[CategoryPrefix.Length..].Trim();
            if (tag.Length == 0)
                continue;

            if (!category && tag.StartsWith(EndpointPrefix, StringComparison.OrdinalIgnoreCase))
                endpoint ??= tag[EndpointPrefix.Length..].Trim() is { Length: > 0 } value ? value : null;
            else if (!category && HappyPathDetection.IsHappyPathTag(tag))
                happyPath = true;
            else if (seen.Add(tag))
                kept.Add(tag);
        }

        return new Classified(kept.ToArray(), endpoint, happyPath);
    }

    /// <summary>
    /// Whether two names are one tag: equal once their ASCII letters are folded, A to Z read as a to z and nothing else
    /// changed. The three runtimes that write or read a report fold the rest of Unicode differently (.NET keeps the Kelvin
    /// sign apart from <c>k</c> where JavaScript and Java do not, and Java alone folds the dotless ı into <c>i</c>), so a
    /// wider rule would let a report's chips differ from the scenarios they pick, or Kronikol4J's chips from these.
    /// </summary>
    public static IEqualityComparer<string> Comparer { get; } = new AsciiFoldComparer();

    /// <summary>
    /// The tags of every list in turn, each once under <see cref="Comparer"/>: the first spelling met is the one kept, and
    /// so is the order the names are met in. Null lists and blank names are skipped.
    /// </summary>
    public static string[] Merge(params IEnumerable<string>?[] lists)
    {
        var seen = new HashSet<string>(Comparer);
        var merged = new List<string>();
        foreach (var list in lists)
            foreach (var tag in list ?? [])
                if (!string.IsNullOrWhiteSpace(tag) && seen.Add(tag))
                    merged.Add(tag);
        return merged.ToArray();
    }

    /// <summary>
    /// The tags every list holds, under <see cref="Comparer"/>, in the first list's order and spelling: what all of a
    /// feature's scenarios carry. Empty when there is no list, or when any list is null or empty.
    /// </summary>
    public static string[] Shared(IEnumerable<IEnumerable<string>?> lists)
    {
        string[]? shared = null;
        foreach (var list in lists)
        {
            var tags = Merge(list);
            if (tags.Length == 0)
                return [];
            if (shared is null)
            {
                shared = tags;
                continue;
            }
            var these = new HashSet<string>(tags, Comparer);
            shared = shared.Where(these.Contains).ToArray();
            if (shared.Length == 0)
                return [];
        }
        return shared ?? [];
    }

    /// <summary>
    /// The order of the report's Tags row: Happy Path first, then by the name with its ASCII letters folded, then by UTF-16
    /// code unit to break a tie. The same comparison in .NET, Java and JavaScript, and no culture decides it.
    /// </summary>
    internal static int CompareForRow(string x, string y)
    {
        var xHappy = Comparer.Equals(x, HappyPathTag);
        var yHappy = Comparer.Equals(y, HappyPathTag);
        if (xHappy != yHappy)
            return xHappy ? -1 : 1;
        var length = Math.Min(x.Length, y.Length);
        for (var i = 0; i < length; i++)
        {
            var difference = Fold(x[i]) - Fold(y[i]);
            if (difference != 0)
                return difference;
        }
        return x.Length != y.Length ? x.Length - y.Length : string.CompareOrdinal(x, y);
    }

    /// <summary>
    /// A scenario's effective tags: its feature's, then its own, each once (<see cref="Merge"/>), which is what the
    /// report's Tags row, its <c>@tag</c> search and its link filter on, and what CTRF sends as tags.
    /// </summary>
    internal static string[] Effective(Feature feature, Scenario scenario) => Merge(feature.Tags, scenario.Tags);

    /// <summary>A scenario's own tags, each once.</summary>
    internal static string[] OwnOf(Scenario scenario) => Merge(scenario.Tags);

    /// <summary>
    /// Whether <paramref name="label"/> is one of the <c>retry N</c> labels 4.x's ingest lanes gave a scenario for each
    /// attempt before the one it shows: <c>retry k</c> with <c>k</c> from 1 to one less than <paramref name="attempt"/>.
    /// No lane makes them in 5.0.0 (the report draws the retry from <see cref="Scenario.Attempt"/>); the readers of a 4.x
    /// file leave them out of its tags with this. Any other <c>retry 2</c> is an author's own tag.
    /// </summary>
    internal static bool IsGeneratedRetryLabel(string label, int? attempt) =>
        attempt is > 1
        && label.StartsWith("retry ", StringComparison.Ordinal)
        && int.TryParse(label.AsSpan("retry ".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
        && number >= 1 && number < attempt;

    /// <summary>
    /// Whether a scenario's label in a file written before 5.0.0 is a happy-path name, which the readers of a 4.x file
    /// read as <see cref="Scenario.IsHappyPath"/> and leave out of its tags, as <see cref="Classify"/> does on every lane
    /// since 5.0.0 (5.2.7): 4.x's LightBDD wrote <c>[HappyPath]</c> as the label Happy Path. A 4.x category is not one,
    /// since it was written with its prefix.
    /// </summary>
    internal static bool IsHappyPathLabel(string label) => HappyPathDetection.IsHappyPathTag(Normalise(label));

    private static char Fold(char c) => c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;

    private sealed class AsciiFoldComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null || x.Length != y.Length)
                return false;
            for (var i = 0; i < x.Length; i++)
                if (Fold(x[i]) != Fold(y[i]))
                    return false;
            return true;
        }

        public int GetHashCode(string obj)
        {
            var hash = new HashCode();
            foreach (var c in obj)
                hash.Add(Fold(c));
            return hash.ToHashCode();
        }
    }
}
