namespace Kronikol.Reports;

/// <summary>
/// The report's tags as its Tags row and its scenarios' <c>data-tags</c> carry them (plans/TAGS_PLAN.md §4.3, §4.8,
/// §4.9): each scenario's effective tags (<see cref="ScenarioTags.Effective"/>), with Happy Path first for a happy path;
/// one chip per tag under <see cref="ScenarioTags.Comparer"/>, spelled as the report's data first lists it (its features
/// in order and each feature's scenarios in order, not the order the page draws them in, which a sort changes), so the
/// page compares names exactly and never folds case itself; and the chips in <see cref="ScenarioTags.CompareForRow"/>'s
/// order.
/// </summary>
internal sealed class TagChips
{
    private readonly Dictionary<string, string> _spelling = new(ScenarioTags.Comparer);
    private readonly Dictionary<string, int> _counts = new(ScenarioTags.Comparer);

    private TagChips(IEnumerable<Feature> features)
    {
        foreach (var feature in features)
            foreach (var scenario in feature.Scenarios)
            {
                var tags = RowTagsOf(feature.Tags, scenario);
                foreach (var tag in tags)
                {
                    _spelling.TryAdd(tag, tag);
                    _counts[tag] = _counts.GetValueOrDefault(tag) + 1;
                }
                if (tags.Length == 0)
                    Untagged++;
            }
        var names = _spelling.Values.ToList();
        names.Sort(ScenarioTags.CompareForRow);
        Names = names.ToArray();
        Hidden = Names
            .Where(n => !ScenarioTags.Comparer.Equals(n, ScenarioTags.HappyPathTag))
            .Select((name, order) => (Name: name, Order: order))
            .OrderByDescending(n => _counts[n.Name]).ThenBy(n => n.Order)
            .Skip(MostShown)
            .Select(n => n.Name)
            .ToHashSet(ScenarioTags.Comparer);
    }

    public static TagChips For(IEnumerable<Feature> features) => new(features);

    /// <summary>
    /// The most named chips the Tags row starts with (plans/TAGS_PLAN.md §4.8, Q9). Measured on 2026-10-19, 400 tags
    /// painted 1,150 px at 1280 px and 5,640 px at 390 px, so a longer row starts with the tags the most scenarios carry
    /// and a chip that shows the rest; 30 chips are about three lines at 1280 px. Happy Path and Untagged always show and
    /// are not counted.
    /// </summary>
    public const int MostShown = 30;

    /// <summary>
    /// The named chips a long row starts without: all but the <see cref="MostShown"/> the most scenarios carry, a tie
    /// going to the row's order. Empty when the row fits.
    /// </summary>
    public IReadOnlySet<string> Hidden { get; }

    /// <summary>The row's chips, in its order.</summary>
    public string[] Names { get; }

    /// <summary>How many scenarios carry <paramref name="tag"/>: a parameterized scenario once per example.</summary>
    public int Count(string tag) => _counts.GetValueOrDefault(tag);

    /// <summary>How many scenarios carry no tag at all, Happy Path included: the Untagged chip's count.</summary>
    public int Untagged { get; }

    /// <summary>A scenario's tags, its feature's (<paramref name="featureTags"/>) with its own, as the row spells them.</summary>
    public string[] Of(string[]? featureTags, Scenario scenario) =>
        RowTagsOf(featureTags, scenario).Select(t => _spelling.GetValueOrDefault(t, t)).ToArray();

    /// <summary>
    /// The <c>data-tags</c> attribute of a scenario's element, with its leading space: its tags, each percent-encoded
    /// (so a comma, a quote or a space inside a name survives) and joined with commas. Empty for a scenario with none.
    /// </summary>
    public string Attribute(string[]? featureTags, Scenario scenario) => AttributeOf(Of(featureTags, scenario));

    /// <summary>A parameterized group's <c>data-tags</c>: what any of its rows carries, so the group matches a chip any
    /// of its rows matches (§4.8).</summary>
    public string GroupAttribute(string[]? featureTags, IEnumerable<Scenario> rows) =>
        AttributeOf(ScenarioTags.Merge(rows.Select(r => Of(featureTags, r)).ToArray()));

    private static string AttributeOf(string[] tags) =>
        tags.Length == 0 ? "" : $" data-tags=\"{System.Net.WebUtility.HtmlEncode(string.Join(",", tags.Select(Uri.EscapeDataString)))}\"";

    // The scenario's effective tags (ScenarioTags.Effective), from the feature's tags so that a parameterized group's rows,
    // which the report draws from copies, read as their scenarios do; Happy Path is spelled one way.
    private static string[] RowTagsOf(string[]? featureTags, Scenario scenario)
    {
        var effective = ScenarioTags.Merge(featureTags, ScenarioTags.OwnOf(scenario))
            .Select(t => ScenarioTags.Comparer.Equals(t, ScenarioTags.HappyPathTag) ? ScenarioTags.HappyPathTag : t);
        return scenario.IsHappyPath ? ScenarioTags.Merge([ScenarioTags.HappyPathTag], effective) : ScenarioTags.Merge(effective);
    }

    /// <summary>
    /// The badges of a scenario's own tags: its feature's are drawn on the feature, so they are left out, and so is Happy
    /// Path for a happy path, which has a badge of its own.
    /// </summary>
    public static string[] BadgeTags(string[]? featureTags, Scenario scenario)
    {
        var feature = new HashSet<string>(featureTags ?? [], ScenarioTags.Comparer);
        return ScenarioTags.OwnOf(scenario)
            .Where(t => !feature.Contains(t) && !(scenario.IsHappyPath && ScenarioTags.Comparer.Equals(t, ScenarioTags.HappyPathTag)))
            .ToArray();
    }

    /// <summary>The badges of a parameterized group: the tags every row carries, less its feature's and Happy Path (F23).</summary>
    public static string[] GroupBadgeTags(string[]? featureTags, IReadOnlyList<Scenario> rows)
    {
        var feature = new HashSet<string>(featureTags ?? [], ScenarioTags.Comparer);
        return ScenarioTags.Shared(rows.Select(ScenarioTags.OwnOf))
            .Where(t => !feature.Contains(t) && !ScenarioTags.Comparer.Equals(t, ScenarioTags.HappyPathTag))
            .ToArray();
    }

    /// <summary>The badges for <paramref name="tags"/>, each with its leading space.</summary>
    public static string Badges(IEnumerable<string> tags) =>
        string.Concat(tags.Select(t => $" <span class=\"label\">{System.Net.WebUtility.HtmlEncode(t)}</span>"));

    /// <summary>
    /// What a retried scenario shows in place of the <c>retry N</c> labels the ingest lanes used to give it (§4.6): one
    /// badge naming the retry its result came from, <c>retry 2</c> for the third attempt. Null for a first attempt.
    /// </summary>
    public static string? RetryText(Scenario scenario) =>
        scenario.Attempt is > 1 and var attempt ? $"retry {attempt - 1}" : null;

    /// <summary>The retry badge, with its leading space, or nothing for a first attempt.</summary>
    public static string RetryBadge(Scenario scenario) =>
        RetryText(scenario) is { } text
            ? $" <span class=\"label retry-label\" title=\"Shown: attempt {scenario.Attempt}. The attempts before it are not in the report.\">{text}</span>"
            : "";
}
