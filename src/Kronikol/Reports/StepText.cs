namespace Kronikol.Reports;

/// <summary>
/// The one place Kronikol decides how a step or assertion label — and a feature, rule or scenario title
/// (<see cref="ApplyToTitles"/>) — is cased for display.
/// <para>
/// Producers of step text are wildly inconsistent — a Playwright assertion label
/// (<c>could not arm the mock</c>), a LightBDD sub-step, a hand-written <c>expect(x, "message")</c>
/// message — so the report normalises them: the first <em>letter</em> of a step that carries no
/// Gherkin keyword is upper-cased. Gherkin steps are never touched: the rendered line already starts
/// with the capitalised keyword (<c>Given the mock is armed</c>) and the author's casing after it is
/// meaningful.
/// </para>
/// <para>The rule in full — see <see cref="Capitalise"/>:</para>
/// <list type="bullet">
/// <item>leading whitespace and marker glyphs (<c>✓ ✗ ⚠ • -</c>, and the spaces between them) are skipped,
/// so <c>✓ the envelope carried no text</c> becomes <c>✓ The envelope carried no text</c>;</item>
/// <item>text whose first non-marker character is an opening quote or bracket
/// (<c>" ' ( [ {</c> or the typographic ones) is left exactly as it is — the quoted literal is
/// the producer's content, and re-casing it would corrupt a locator, an identifier or a code snippet;</item>
/// <item>text whose first word is a camelCase identifier (<c>graphqlErrorMessages reads…</c>, <c>iPhone</c>) is
/// left alone for the same reason — and is not counted as a violation by <see cref="StartsWithCapitalOrQuote"/>;</item>
/// <item>the change is culture-invariant (<see cref="char.ToUpperInvariant(char)"/>), Unicode-aware
/// (<c>é</c> to <c>É</c>, <c>ł</c> to <c>Ł</c>) and idempotent.</item>
/// </list>
/// </summary>
/// <remarks>
/// Two switches gate the rule, because step text reaches the report by two very different routes:
/// <list type="number">
/// <item><b>the model</b> (<see cref="ScenarioStep.Text"/> in HTML/JSON/XML/YAML) — capitalised in one
/// pass over the finished <see cref="Feature"/>[] just before rendering
/// (<see cref="ApplyToFeatures"/>), gated by <see cref="ReportConfigurationOptions.CapitaliseStepText"/>;</item>
/// <item><b>the diagram</b> (step delimiter bars and ✓/✗ assertion notes, which are baked into PlantUML
/// while the test runs, long before any report options exist) — gated by the process-wide
/// <see cref="CapitaliseEnabled"/> switch, which <c>kronikol ingest</c> and
/// <see cref="Kronikol.Ingestion.IngestPipeline"/> set from the same option.</item>
/// </list>
/// Both default to on, so the two views agree unless a host deliberately splits them.
/// </remarks>
public static class StepText
{
    /// <summary>Marker glyphs that may precede the text of a step or assertion label.</summary>
    private static readonly char[] MarkerGlyphs = ['✓', '✗', '⚠', '•', '-', '–', '—', '*'];

    /// <summary>Opening quotes and brackets: text starting with one of these is left unchanged.</summary>
    private static readonly char[] OpeningQuotes = ['"', '\'', '(', '[', '{', '“', '‘', '«', '„', '‹', '`'];

    /// <summary>
    /// Whether the diagram-side rule is on: step delimiter bars and ✓/✗ assertion notes capitalise
    /// their text when no keyword is prepended. Process-wide (the emitters are static and run during
    /// the test, before any report options exist); default <c>true</c>.
    /// <c>kronikol ingest --no-capitalise</c> and
    /// <see cref="ReportConfigurationOptions.CapitaliseStepText"/> (via
    /// <see cref="Kronikol.Ingestion.IngestPipeline"/>) set it.
    /// </summary>
    public static bool CapitaliseEnabled { get; set; } = true;

    /// <summary>
    /// Upper-cases the first letter of <paramref name="text"/>, skipping leading whitespace and marker
    /// glyphs. Returns <paramref name="text"/> unchanged when it is null, empty, whitespace, already
    /// capitalised, starts with a quote/bracket, or has no letter to change.
    /// </summary>
    public static string? Capitalise(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var index = FirstContentIndex(text);
        if (index < 0)
            return text;

        var c = text[index];
        if (Array.IndexOf(OpeningQuotes, c) >= 0)
            return text;

        var upper = char.ToUpperInvariant(c);
        if (upper == c)
            return text;

        // `insightsLocationReportDates returns…`, `graphqlErrorMessages reads…`, `iPhone renders…`: a first
        // word with a capital inside it is an identifier, and re-casing an identifier corrupts it just as
        // re-casing a quoted literal would. Leave it to the producer (and do not count it as a violation).
        if (IsCamelCaseWord(text, index))
            return text;

        return string.Concat(text.AsSpan(0, index), upper.ToString(), text.AsSpan(index + 1));
    }

    /// <summary>
    /// Whether the word starting at <paramref name="start"/> (a lower-case letter) contains an upper-case
    /// letter before its end — the shape of a camelCase identifier (<c>graphqlErrorMessages</c>,
    /// <c>iPhone</c>). Digits, underscores and dots stay inside the word (<c>v2Api</c>, <c>my_iD</c>,
    /// <c>page.goBack</c>); any other character ends it.
    /// </summary>
    private static bool IsCamelCaseWord(string text, int start)
    {
        for (var i = start + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsUpper(c))
                return true;
            if (!(char.IsLetterOrDigit(c) || c is '_' or '.'))
                return false;
        }

        return false;
    }

    /// <summary>Applies <see cref="Capitalise"/> only when <see cref="CapitaliseEnabled"/> is on — the diagram-side gate.</summary>
    public static string? CapitaliseIfEnabled(string? text) => CapitaliseEnabled ? Capitalise(text) : text;

    /// <summary>
    /// Whether the rendered form of a step already reads as a sentence: it starts with a capital letter,
    /// a quoted literal, a digit or a symbol. Only a lower-case first letter counts as a violation, and a
    /// step with a keyword is judged on the keyword (the rendered line starts with it).
    /// </summary>
    public static bool StartsWithCapitalOrQuote(string? keyword, string? text)
    {
        var subject = string.IsNullOrWhiteSpace(keyword) ? text : keyword;
        if (string.IsNullOrEmpty(subject))
            return true;

        var index = FirstContentIndex(subject);
        if (index < 0)
            return true;

        return !char.IsLower(subject[index]) || IsCamelCaseWord(subject, index);
    }

    /// <summary>
    /// Capitalises every keyword-less step text in the model, in place — the single model-side pass
    /// that keeps HTML, JSON, XML and YAML in agreement. Sub-steps (including tracked assertion
    /// sub-steps, whose text starts with a ✓/✗ glyph) and background steps are included, and
    /// <see cref="ScenarioStep.TextSegments"/> are re-derived so inline-parameter highlighting still
    /// lines up with the changed text.
    /// </summary>
    public static void ApplyToFeatures(IEnumerable<Feature>? features)
    {
        if (features is null)
            return;
        foreach (var feature in features)
        {
            foreach (var scenario in feature.Scenarios ?? [])
            {
                ApplyToSteps(scenario.BackgroundSteps);
                ApplyToSteps(scenario.Steps);
            }
        }
    }

    /// <summary>
    /// Capitalises every feature, rule and scenario title in the model, in place — the
    /// <see cref="ReportConfigurationOptions.CapitaliseTitles"/> pass. An outline's template title
    /// (<see cref="Scenario.OutlineId"/>) is re-cased too, so its members still group together and the
    /// group header reads like its members. <see cref="Scenario.ExampleDisplayName"/> is left alone: it
    /// is example data, not prose.
    /// </summary>
    public static void ApplyToTitles(IEnumerable<Feature>? features)
    {
        if (features is null)
            return;
        foreach (var feature in features)
        {
            feature.DisplayName = Capitalise(feature.DisplayName) ?? feature.DisplayName;
            foreach (var scenario in feature.Scenarios ?? [])
            {
                scenario.DisplayName = Capitalise(scenario.DisplayName) ?? scenario.DisplayName;
                scenario.Rule = Capitalise(scenario.Rule);
                scenario.OutlineId = Capitalise(scenario.OutlineId);
            }
        }
    }

    /// <summary>
    /// Counts the feature, rule and scenario titles that still start with a lower-case letter after
    /// <see cref="ApplyToTitles"/> ran (or because it was off) — the sibling of
    /// <see cref="FindNotStartingWithCapital"/> for headings. Each distinct rule title is counted once.
    /// </summary>
    /// <returns>The total count and the first <paramref name="maxExamples"/> offending titles.</returns>
    public static (int Count, string[] Examples) FindTitlesNotStartingWithCapital(IEnumerable<Feature>? features, int maxExamples = 5)
    {
        var count = 0;
        var examples = new List<string>();
        var rulesSeen = new HashSet<string>(StringComparer.Ordinal);

        void Check(string? title)
        {
            if (string.IsNullOrWhiteSpace(title) || StartsWithCapitalOrQuote(null, title))
                return;
            count++;
            if (examples.Count < maxExamples)
                examples.Add(title);
        }

        foreach (var feature in features ?? [])
        {
            Check(feature.DisplayName);
            foreach (var scenario in feature.Scenarios ?? [])
            {
                Check(scenario.DisplayName);
                if (scenario.Rule is { Length: > 0 } rule && rulesSeen.Add(rule))
                    Check(rule);
            }
        }

        return (count, examples.ToArray());
    }

    /// <summary>Capitalises a step tree in place (see <see cref="ApplyToFeatures"/>).</summary>
    public static void ApplyToSteps(IEnumerable<ScenarioStep>? steps)
    {
        if (steps is null)
            return;
        foreach (var step in steps)
        {
            ApplyToStep(step);
            ApplyToSteps(step.SubSteps);
        }
    }

    private static void ApplyToStep(ScenarioStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.Keyword))
            return; // the rendered line starts with the keyword; the author's casing after it is meaningful

        // When the text is rendered from segments, the first segment decides what the reader sees.
        // Only a leading literal can be re-cased; a leading parameter value must stay verbatim, and
        // then Text is left alone too so the two representations cannot drift apart.
        if (step.TextSegments is { Length: > 0 })
        {
            var first = step.TextSegments[0];
            if (first.Text is null || first.Parameter is not null || first.TableReference is not null)
                return;

            var capitalisedSegment = Capitalise(first.Text);
            if (!ReferenceEquals(capitalisedSegment, first.Text))
                step.TextSegments[0] = first with { Text = capitalisedSegment };
        }

        step.Text = Capitalise(step.Text) ?? step.Text;
    }

    /// <summary>
    /// Index of the first character that is neither whitespace nor a marker glyph, or -1 when the text
    /// is nothing but markers and spaces.
    /// </summary>
    private static int FirstContentIndex(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c) || Array.IndexOf(MarkerGlyphs, c) >= 0)
                continue;
            return i;
        }

        return -1;
    }

    /// <summary>
    /// Counts the steps whose rendered text still does not start with a capital letter after the rule
    /// has been applied — the quoted literals it deliberately leaves alone, and anything a producer
    /// slipped past it. Recurses through sub-steps and background steps.
    /// </summary>
    /// <returns>The total count and the first <paramref name="maxExamples"/> offending texts.</returns>
    public static (int Count, string[] Examples) FindNotStartingWithCapital(IEnumerable<Feature>? features, int maxExamples = 5)
    {
        var count = 0;
        var examples = new List<string>();

        void Visit(IEnumerable<ScenarioStep>? steps)
        {
            foreach (var step in steps ?? [])
            {
                if (!StartsWithCapitalOrQuote(step.Keyword, step.Text))
                {
                    count++;
                    if (examples.Count < maxExamples)
                        examples.Add(step.Text);
                }

                Visit(step.SubSteps);
            }
        }

        foreach (var feature in features ?? [])
        {
            foreach (var scenario in feature.Scenarios ?? [])
            {
                Visit(scenario.BackgroundSteps);
                Visit(scenario.Steps);
            }
        }

        return (count, examples.ToArray());
    }
}
