namespace Kronikol.Ingestion.Cucumber;

/// <summary>One scenario (or scenario outline) node, with the rule and feature it sits under.</summary>
/// <param name="Node">The Gherkin scenario node.</param>
/// <param name="Rule">The enclosing <c>Rule:</c> name, when there is one.</param>
/// <param name="Feature">The enclosing feature node.</param>
/// <param name="Uri">The feature file the node came from.</param>
internal sealed record GherkinScenario(
    CucumberScenarioNode Node,
    string? Rule,
    CucumberFeatureNode? Feature,
    string? Uri)
{
    /// <summary>
    /// The tags the scenario declares itself (plus the tags of the <c>Examples:</c> block the pickle came
    /// from), with the leading <c>@</c> removed — feature and rule tags are deliberately excluded, which
    /// is what makes <c>Labels</c> and happy-path detection behave the way the ReqNRoll adapter does.
    /// </summary>
    public string[] OwnTagNames(IEnumerable<string>? examplesTags)
    {
        var own = (Node.Tags ?? [])
            .Select(t => CucumberFeatureSynthesizer.Strip(t.Name))
            .Where(t => t is not null)
            .Select(t => t!);
        return examplesTags is null ? own.ToArray() : own.Concat(examplesTags).Distinct(StringComparer.Ordinal).ToArray();
    }
}

/// <summary>One row of an <c>Examples:</c> table — the values a scenario outline was instantiated with.</summary>
/// <param name="Values">Header → cell for the row, in column order.</param>
/// <param name="ExamplesTags">Tags declared on the <c>Examples:</c> block.</param>
internal sealed record ExampleRow(Dictionary<string, string> Values, string[] ExamplesTags);

/// <summary>
/// Flattens every <c>gherkinDocument</c> envelope into the lookups the synthesiser needs: scenario nodes
/// by ast id, steps by ast id, the set of background step ids, and example rows by row id.
/// </summary>
internal sealed class GherkinIndex
{
    private readonly Dictionary<string, GherkinScenario> _scenarios = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CucumberGherkinStep> _steps = new(StringComparer.Ordinal);
    private readonly HashSet<string> _backgroundSteps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExampleRow> _exampleRows = new(StringComparer.Ordinal);

    /// <summary>Indexes every Gherkin document in the message stream.</summary>
    public static GherkinIndex Build(CucumberMessages messages)
    {
        var index = new GherkinIndex();
        foreach (var document in messages.GherkinDocuments)
        {
            if (document.Feature is not { } feature)
                continue;
            index.AddChildren(feature.Children, feature, rule: null, document.Uri);
        }

        return index;
    }

    private void AddChildren(CucumberFeatureChild[]? children, CucumberFeatureNode feature, string? rule, string? uri)
    {
        foreach (var child in children ?? [])
        {
            if (child.Background is { } background)
            {
                foreach (var step in background.Steps ?? [])
                    AddStep(step, isBackground: true);
            }
            else if (child.Rule is { } ruleNode)
            {
                AddChildren(ruleNode.Children, feature, CucumberFeatureSynthesizer.NullIfBlank(ruleNode.Name), uri);
            }
            else if (child.Scenario is { } scenario)
            {
                if (scenario.Id is { Length: > 0 } id)
                    _scenarios[id] = new GherkinScenario(scenario, rule, feature, uri);
                foreach (var step in scenario.Steps ?? [])
                    AddStep(step, isBackground: false);
                AddExamples(scenario);
            }
        }
    }

    private void AddStep(CucumberGherkinStep step, bool isBackground)
    {
        if (step.Id is not { Length: > 0 } id)
            return;
        _steps[id] = step;
        if (isBackground)
            _backgroundSteps.Add(id);
    }

    private void AddExamples(CucumberScenarioNode scenario)
    {
        foreach (var examples in scenario.Examples ?? [])
        {
            var headers = (examples.TableHeader?.Cells ?? []).Select(c => c.Value ?? "").ToArray();
            if (headers.Length == 0)
                continue;
            var tags = (examples.Tags ?? [])
                .Select(t => CucumberFeatureSynthesizer.Strip(t.Name))
                .Where(t => t is not null)
                .Select(t => t!)
                .ToArray();

            foreach (var row in examples.TableBody ?? [])
            {
                if (row.Id is not { Length: > 0 } rowId)
                    continue;
                var cells = (row.Cells ?? []).Select(c => c.Value ?? "").ToArray();
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < headers.Length; i++)
                    values[headers[i]] = i < cells.Length ? cells[i] : "";
                _exampleRows[rowId] = new ExampleRow(values, tags);
            }
        }
    }

    /// <summary>The scenario node a pickle was compiled from (its first ast node id).</summary>
    public GherkinScenario? FindScenario(CucumberPickle pickle) =>
        pickle.AstNodeIds is { Length: > 0 } ids && _scenarios.TryGetValue(ids[0], out var scenario) ? scenario : null;

    /// <summary>The authored Gherkin step a pickle step was compiled from (its first ast node id).</summary>
    public CucumberGherkinStep? FindStep(CucumberPickleStep step) =>
        step.AstNodeIds is { Length: > 0 } ids && _steps.TryGetValue(ids[0], out var gherkinStep) ? gherkinStep : null;

    /// <summary>True when the authored step belongs to a <c>Background:</c> block.</summary>
    public bool IsBackgroundStep(string stepId) => _backgroundSteps.Contains(stepId);

    /// <summary>The example row with this id, when the pickle came from a scenario outline.</summary>
    public ExampleRow? FindExampleRow(string rowId) => _exampleRows.GetValueOrDefault(rowId);
}
