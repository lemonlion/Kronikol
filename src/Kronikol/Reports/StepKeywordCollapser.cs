namespace Kronikol.Reports;

/// <summary>
/// Decides the keyword each step is *displayed* with, collapsing a repeat of the primary keyword
/// already in force to <c>And</c> — so a background <c>Given</c> followed by a scenario <c>Given</c>
/// reads <c>Given / And</c> once the two lists are rendered as one.
/// <para>
/// This is a render-time projection and never touches the model: background steps are shared by
/// reference across every scenario of a Rule group (<see cref="BackgroundStepsDetector"/>) and the
/// HTML and data outputs are written concurrently, so rewriting a keyword in place would race the
/// data writers and leak <c>And</c> into the JSON/XML/YAML files.
/// </para>
/// <para>
/// Localisation is a deliberate non-goal: an unrecognised keyword (a non-English Gherkin dialect)
/// passes through untouched and disables collapsing until the next recognised primary, degrading to
/// the literal keywords the producer recorded.
/// </para>
/// </summary>
public static class StepKeywordCollapser
{
    /// <summary>
    /// The keyword to display for each step, positionally. Entries are the step's own keyword except
    /// where it repeats the primary keyword currently in force, which becomes <c>And</c> in the same
    /// casing. The input array is never modified.
    /// </summary>
    public static string?[] DisplayKeywords(IReadOnlyList<ScenarioStep> steps)
    {
        var displayed = new string?[steps.Count];
        string? current = null;

        for (var i = 0; i < steps.Count; i++)
        {
            var keyword = steps[i].Keyword;
            var word = keyword?.Trim();
            displayed[i] = keyword;

            if (string.IsNullOrEmpty(word))
                continue;

            var kind = Classify(word);
            switch (kind)
            {
                case KeywordKind.Primary:
                    if (current is not null && string.Equals(current, word, StringComparison.OrdinalIgnoreCase))
                        displayed[i] = MatchCasing("And", word);
                    else
                        current = word;
                    break;
                case KeywordKind.Conjunction:
                    // A conjunction inherits whatever came before, so the primary in force is unchanged.
                    break;
                default:
                    // Unrecognised — most likely a localised dialect. Pass it through and stop
                    // collapsing until a keyword we understand re-establishes the primary.
                    current = null;
                    break;
            }
        }

        return displayed;
    }

    private enum KeywordKind { Primary, Conjunction, Unknown }

    /// <summary>
    /// The same vocabulary <see cref="Ingestion.IngestAttribution.PhaseForStep"/> uses, so one table
    /// decides both which keywords establish a phase and which ones collapse.
    /// </summary>
    private static KeywordKind Classify(string word) => word.ToLowerInvariant() switch
    {
        "given" or "context" or "when" or "then" or "action" or "outcome" or "butwhen" => KeywordKind.Primary,
        "and" or "but" or "conjunction" or "*" => KeywordKind.Conjunction,
        _ => KeywordKind.Unknown,
    };

    /// <summary>Renders <paramref name="replacement"/> in the casing of the keyword it stands in for.</summary>
    private static string MatchCasing(string replacement, string source)
    {
        var letters = source.Where(char.IsLetter).ToArray();
        if (letters.Length == 0)
            return replacement;
        if (letters.All(char.IsUpper))
            return replacement.ToUpperInvariant();
        if (letters.All(char.IsLower))
            return replacement.ToLowerInvariant();
        return replacement;
    }
}
