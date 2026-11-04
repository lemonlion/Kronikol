"""Applies the prototype of R1 to a checkout of 27cc0e46: python -I apply_r1.py <checkout>. Exact-string edits; any drift fails loudly.

Kept as a script, beside the patch it was diffed into (r1-first-cut.patch), so each edit reads with its reason.
"""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])


def edit(path, *pairs):
    p = root / path
    t = p.read_text(encoding="utf-8")
    for old, new in pairs:
        assert old in t, f"{path}: not found: {old[:70]!r}"
        t = t.replace(old, new, 1)
    p.write_text(t, encoding="utf-8", newline="\n")


# 1. What the Gherkin document says each scenario's Background is.
edit(
    "src/Kronikol.ReqNRoll.Core/ExamplesBlockResolver.cs",
    (
        """    /// <summary>
    /// The running row's name as Gherkin names its pickle (#101): the title with each <c>&lt;header&gt;</c> filled from the""",
        """    /// <summary>
    /// The texts of the steps the feature's <c>Background:</c> and the scenario's rule's give the running scenario, in the
    /// order they run, from the Gherkin document Reqnroll embeds (#222). Empty when no Background is written, or when
    /// Reqnroll's messages are unavailable: every step is then the scenario's own. Same reflection seam, same
    /// silent-degradation contract as <see cref="Resolve"/>.
    /// </summary>
    public static IReadOnlyList<string> ResolveBackgroundSteps(FeatureInfo? featureInfo, ScenarioInfo? scenarioInfo)
    {
        if (featureInfo is null || scenarioInfo is null)
            return [];
        try
        {
            var lookup = LookupCache.GetValue(featureInfo, BuildLookup);
            if (FindPickle(lookup, scenarioInfo) is { AstNodeIds.Count: > 0 } pickle
                && lookup.BackgroundByScenarioId.TryGetValue(pickle.AstNodeIds[0], out var byId))
            {
                return byId;
            }
            return scenarioInfo.Title is { Length: > 0 } title && lookup.BackgroundByScenarioName.TryGetValue(title, out var byName)
                ? byName
                : [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// The running row's name as Gherkin names its pickle (#101): the title with each <c>&lt;header&gt;</c> filled from the""",
    ),
    (
        """        public Dictionary<string, int> LinesByScenarioName { get; } = new(StringComparer.Ordinal);
    }
""",
        """        public Dictionary<string, int> LinesByScenarioName { get; } = new(StringComparer.Ordinal);

        /// <summary>A scenario's ast id to the texts of the Background steps it runs first: its feature's, then its rule's.</summary>
        public Dictionary<string, string[]> BackgroundByScenarioId { get; } = new(StringComparer.Ordinal);

        /// <summary>The same by title, for a scenario no pickle is found for: a title two rules share keeps the first.</summary>
        public Dictionary<string, string[]> BackgroundByScenarioName { get; } = new(StringComparer.Ordinal);
    }
""",
    ),
    (
        """                IndexScenario(lookup, scenario);
            }
        }
        catch
        {
            // A malformed or unexpected message shape must never break the scenario hook.""",
        """                IndexScenario(lookup, scenario);
            }

            IndexBackgrounds(lookup, messages.GherkinDocument?.Feature);
        }
        catch
        {
            // A malformed or unexpected message shape must never break the scenario hook.""",
    ),
    (
        """    private static void IndexScenario(FeatureBlockLookup lookup, Scenario scenario)
    {""",
        """    /// <summary>
    /// Which Background steps each scenario runs first. Read from the document rather than from its pickle, as Gherkin
    /// compiles a scenario with no steps of its own to a pickle with none, and Reqnroll still runs the Background for it.
    /// </summary>
    private static void IndexBackgrounds(FeatureBlockLookup lookup, Feature? feature)
    {
        var featureSteps = (feature?.Children ?? []).Select(c => c.Background).FirstOrDefault(b => b is not null)?.Steps ?? [];
        foreach (var child in feature?.Children ?? [])
        {
            if (child.Scenario is { } scenario)
                IndexBackground(lookup, scenario, featureSteps);
            if (child.Rule is not { } rule)
                continue;
            var ruleSteps = (rule.Children ?? []).Select(c => c.Background).FirstOrDefault(b => b is not null)?.Steps ?? [];
            foreach (var ruleChild in rule.Children ?? [])
            {
                if (ruleChild.Scenario is { } ruleScenario)
                    IndexBackground(lookup, ruleScenario, [.. featureSteps, .. ruleSteps]);
            }
        }
    }

    private static void IndexBackground(FeatureBlockLookup lookup, Scenario scenario, IReadOnlyList<Step> steps)
    {
        var texts = steps.Select(s => s.Text ?? "").ToArray();
        if (scenario.Id is { Length: > 0 } id)
            lookup.BackgroundByScenarioId[id] = texts;
        if (scenario.Name is { Length: > 0 } name)
            lookup.BackgroundByScenarioName.TryAdd(name, texts);
    }

    private static void IndexScenario(FeatureBlockLookup lookup, Scenario scenario)
    {""",
    ),
)

# 2. An internal count on the collected scenario, as RowName is internal: no new public surface.
edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollScenarioInfo.cs",
    (
        """    internal string? RowName { get; init; }
""",
        """    internal string? RowName { get; init; }

    /// <summary>
    /// How many of <see cref="Steps"/>, from the first, came from a <c>Background:</c> written in the feature or the
    /// scenario's rule (#222): what the Gherkin document says, as far as the steps that ran agree with it.
    /// </summary>
    internal int BackgroundStepCount { get; init; }
""",
    ),
)

# 3. The hooks count on the way out. A step a step runs stays the sub-step StepCollector already makes it (shape 11).
edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollConstants.cs",
    (
        """    internal const string RowNameKey = "Kronikol.RowName";
""",
        """    internal const string RowNameKey = "Kronikol.RowName";

    /// <summary>The stopwatches of the steps still open: more than one when a step definition runs a step.</summary>
    internal const string OpenStepsKey = "Kronikol.OpenSteps";
""",
    ),
)
edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
    (
        """        _scenarioContext[ReqNRollConstants.StepStopwatchKey] = Stopwatch.StartNew();

        // Push a step onto StepCollector""",
        """        _scenarioContext[ReqNRollConstants.StepStopwatchKey] = Stopwatch.StartNew();
        if (!_scenarioContext.TryGetValue(ReqNRollConstants.OpenStepsKey, out Stack<Stopwatch>? open))
            _scenarioContext[ReqNRollConstants.OpenStepsKey] = open = new Stack<Stopwatch>();
        open!.Push((Stopwatch)_scenarioContext[ReqNRollConstants.StepStopwatchKey]);

        // Push a step onto StepCollector""",
    ),
    (
        """        var stepStopwatch = (Stopwatch)_scenarioContext[ReqNRollConstants.StepStopwatchKey];
        stepStopwatch.Stop();
""",
        """        var open = (Stack<Stopwatch>)_scenarioContext[ReqNRollConstants.OpenStepsKey];
        var stepStopwatch = open.Pop();
        stepStopwatch.Stop();
""",
    ),
    (
        """        StepCollector.CompleteStep(scenarioId, passed);

        steps.Add(new ReqNRollStepInfo(""",
        """        StepCollector.CompleteStep(scenarioId, passed);

        // A step a step definition ran is that step's sub-step, which StepCollector has made it; it is not one of the
        // scenario's own, whose list and whose Background count run by the feature file.
        if (open.Count > 0)
            return;

        steps.Add(new ReqNRollStepInfo(""",
    ),
    (
        """        ReqNRollScenarioCollector.Collect(new ReqNRollScenarioInfo
        {
            ScenarioId = scenarioId,""",
        """        var writtenBackground = ExamplesBlockResolver.ResolveBackgroundSteps(_featureContext.FeatureInfo, _scenarioContext.ScenarioInfo);

        ReqNRollScenarioCollector.Collect(new ReqNRollScenarioInfo
        {
            ScenarioId = scenarioId,""",
    ),
    (
        """            SourceFile = source.File,
            SourceLine = source.Line,
""",
        """            SourceFile = source.File,
            SourceLine = source.Line,
            BackgroundStepCount = CountBackgroundSteps(steps, writtenBackground),
""",
    ),
    (
        """        ReqNRollTestContext.CurrentTestInfo = null;
    }
}""",
        """        ReqNRollTestContext.CurrentTestInfo = null;
    }

    /// <summary>
    /// How many steps, from the first, are the Background the document writes: a failed step ends a scenario early, and a
    /// step that disagrees with the document is never counted as it.
    /// </summary>
    internal static int CountBackgroundSteps(IReadOnlyList<ReqNRollStepInfo> steps, IReadOnlyList<string> writtenBackground)
    {
        var count = 0;
        while (count < steps.Count && count < writtenBackground.Count
               && string.Equals(steps[count].Text, writtenBackground[count], StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}""",
    ),
)

# 4. One list, split at the count. The guess is gone from this lane.
edit(
    "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
    (
        """                            var displayName = x.RowName ?? x.ScenarioTitle;

                            return new Scenario""",
        """                            var displayName = x.RowName ?? x.ScenarioTitle;

                            // Every step is this scenario's own, as it ran; those a Background: gave it come first (#222).
                            var ran = x.Steps.Count > 0 ? MapSteps(x.Steps, StepCollector.GetSteps(x.ScenarioId)) : [];
                            var backgroundCount = Math.Min(x.BackgroundStepCount, ran.Length);

                            return new Scenario""",
    ),
    (
        """                                Steps = x.Steps.Count > 0
                                    ? MapSteps(x.Steps, StepCollector.GetSteps(x.ScenarioId))
                                    : null,
""",
        """                                BackgroundSteps = backgroundCount > 0 ? ran[..backgroundCount] : null,
                                Steps = ran.Length > backgroundCount ? ran[backgroundCount..] : null,
""",
    ),
    (
        """                BackgroundStepsDetector.DetectAndExtract(scenarios);

                return new Feature""",
        """                return new Feature""",
    ),
)

# 5. The detector, which ingest still calls: steps that read alike stay each scenario's own.
edit(
    "src/Kronikol/Reports/BackgroundStepsDetector.cs",
    (
        """            // Use the first scenario's steps as the background template
            var backgroundSteps = members[0].Steps!.Take(commonPrefixLength).ToArray();

            foreach (var scenario in members)
            {
                scenario.BackgroundSteps = backgroundSteps;
                scenario.Steps = scenario.Steps!.Skip(commonPrefixLength).ToArray();
            }
""",
        """            // Steps that read alike are still each scenario's own: its result, its arguments, its sub-steps (#222).
            foreach (var scenario in members)
            {
                scenario.BackgroundSteps = scenario.Steps!.Take(commonPrefixLength).ToArray();
                scenario.Steps = scenario.Steps!.Skip(commonPrefixLength).ToArray();
            }
""",
    ),
)
print("applied")
