namespace Kronikol.Reports;

/// <summary>
/// The tag conventions Kronikol's Gherkin adapters share, in one place so an external producer
/// (the widened tests NDJSON, a Cucumber Messages importer, a Playwright reporter) classifies tags
/// exactly the way <c>Kronikol.ReqNRoll</c> does.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>@category:x</c> — a report category (the category filter chips).</item>
/// <item><c>@endpoint:x</c> — the feature's endpoint.</item>
/// <item><c>@happy-path</c> (also <c>@happy_path</c>, <c>@happypath</c>) — marks the happy path.</item>
/// <item>anything else — a label.</item>
/// </list>
/// A leading <c>@</c> is optional and stripped; matching is case-insensitive.
/// </remarks>
public static class ScenarioTags
{
    /// <summary>Prefix of a category tag (<c>@category:smoke</c>).</summary>
    public const string CategoryPrefix = "category:";

    /// <summary>Prefix of an endpoint tag (<c>@endpoint:/customers</c>).</summary>
    public const string EndpointPrefix = "endpoint:";

    /// <summary>What <see cref="Classify"/> made of a scenario's tags.</summary>
    /// <param name="Labels">Tags that are neither category, endpoint nor happy-path markers.</param>
    /// <param name="Categories">Values of the <c>@category:</c> tags.</param>
    /// <param name="Endpoint">Value of the first <c>@endpoint:</c> tag, if any.</param>
    /// <param name="IsHappyPath">Whether a happy-path tag was present.</param>
    public sealed record Classified(string[] Labels, string[] Categories, string? Endpoint, bool IsHappyPath);

    /// <summary>Strips a leading <c>@</c> and surrounding whitespace from a tag.</summary>
    public static string Normalise(string tag) => tag.Trim().TrimStart('@');

    /// <summary>Splits raw tags into labels, categories, the endpoint and the happy-path flag.</summary>
    public static Classified Classify(IEnumerable<string>? tags)
    {
        var labels = new List<string>();
        var categories = new List<string>();
        string? endpoint = null;
        var happyPath = false;

        foreach (var raw in tags ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var tag = Normalise(raw);
            if (tag.Length == 0)
                continue;

            if (tag.StartsWith(CategoryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = tag[CategoryPrefix.Length..].Trim();
                if (value.Length > 0)
                    categories.Add(value);
            }
            else if (tag.StartsWith(EndpointPrefix, StringComparison.OrdinalIgnoreCase))
            {
                endpoint ??= tag[EndpointPrefix.Length..].Trim() is { Length: > 0 } value ? value : null;
            }
            else if (HappyPathDetection.IsHappyPathTag(tag))
            {
                happyPath = true;
            }
            else
            {
                labels.Add(tag);
            }
        }

        return new Classified(labels.ToArray(), categories.ToArray(), endpoint, happyPath);
    }
}
