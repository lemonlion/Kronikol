"""First cut of R1 for plans/STEP_IDENTITY_PLAN.md: a step gets an identity, and the joins use it.
usage: python apply_r1.py <repo root>. Every replacement must match exactly once or the script stops."""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])


def edit(path, *pairs):
    file = root / path
    text = file.read_text(encoding="utf-8")
    newline = "\r\n" if "\r\n" in text else "\n"
    text = text.replace("\r\n", "\n")
    for old, new in pairs:
        count = text.count(old)
        if count != 1:
            sys.exit(f"{path}: expected one match, found {count}:\n{old}")
        text = text.replace(old, new)
    file.write_text(text.replace("\n", newline), encoding="utf-8", newline="")


edit(
    "src/Kronikol/Tracking/StepCollector.cs",
    # the ambient pair the weaver calls: the flow knows the step it opened
    (
        """        StartStep(ResolveTestId(), keyword, text, paramNames, paramValues);
    }
""",
        """        var testId = ResolveTestId();
        var id = OpenStep(testId, keyword, text, paramNames, paramValues, table: null, docString: null);
        if (id is not null && testId is not null && States.TryGetValue(testId, out var state))
            lock (state)
                FlowStep.Value = state.StepStack.FirstOrDefault(s => s.Id == id);
    }
""",
    ),
    (
        """        CompleteStep(ResolveTestId(), passed, errorMessage);
    }
""",
        """        var testId = ResolveTestId();
        // A step method its parent's step ended without has no frame left on the stack: this is that step finishing,
        // and popping the stack would close whatever step is open now.
        if (FlowStep.Value is { LeftRunning: true } left && testId is not null && States.TryGetValue(testId, out var state))
        {
            lock (state)
            {
                left.EndTime = Stopwatch.GetTimestamp();
                left.Passed = passed;
                left.ErrorMessage = errorMessage;
                left.LeftRunning = false;
            }
            return;
        }

        CompleteStep(testId, passed, errorMessage);
    }
""",
    ),
    (
        """        string[][]? table, string? docString)
    {
        if (testId is null)
            return;

        var state = States.GetOrAdd(testId, _ => new TestStepState());
""",
        """        string[][]? table, string? docString)
        => OpenStep(testId, keyword, text, paramNames, paramValues, table, docString);

    /// <summary>
    /// Starts a step that a framework's adapter opens for a step of the framework's own, and returns the id the adapter
    /// closes it by (<see cref="CompleteFrame"/>) and finds it by (<see cref="ScenarioStep.CollectedId"/>). A step method
    /// that runs with no frame open is not a framework step, whatever the order of the top-level steps says.
    /// </summary>
    internal static int? StartFrame(string? testId, string? keyword, string text, string[][]? table = null, string? docString = null)
        => OpenStep(testId, keyword, text, null, null, table, docString);

    /// <summary>
    /// Closes the frame <paramref name="frameId"/>. A step still open above it was left running: it keeps its place under
    /// its parent, and its own completion, when it comes, closes that step and no other.
    /// </summary>
    internal static void CompleteFrame(string? testId, int? frameId, bool passed, string? errorMessage = null)
    {
        if (testId is null || frameId is null || !States.TryGetValue(testId, out var state))
            return;

        lock (state)
        {
            if (state.StepStack.All(s => s.Id != frameId))
                return;

            while (state.StepStack.Count > 0)
            {
                var open = state.StepStack.Pop();
                var isFrame = open.Id == frameId;
                if (isFrame)
                {
                    open.EndTime = Stopwatch.GetTimestamp();
                    open.Passed = passed;
                    open.ErrorMessage = errorMessage;
                }
                else
                {
                    open.Passed = true;
                    open.LeftRunning = true;
                }

                (state.StepStack.Count > 0 ? state.StepStack.Peek().SubSteps : state.CompletedSteps).Add(open);
                if (isFrame)
                    return;
            }
        }
    }

    /// <summary>
    /// The step a call logged now happened under: its id, 0 when this test has steps and none is open, null when this test
    /// has none, where the call is read against the bars instead (a capture replayed by an ingest, a test without steps).
    /// </summary>
    internal static int? OpenStepOf(string? testId)
    {
        if (testId is null || !States.TryGetValue(testId, out var state))
            return null;

        lock (state)
            return state.StepStack.Count > 0 ? state.StepStack.Peek().Id : 0;
    }

    private static int? OpenStep(string? testId, string? keyword, string text, string[]? paramNames, object?[]? paramValues,
        string[][]? table, string? docString)
    {
        if (testId is null)
            return null;

        var state = States.GetOrAdd(testId, _ => new TestStepState());
""",
    ),
    (
        """            // Resolve effective keyword (Given/And/And sequencing)
            step.EffectiveKeyword = ResolveKeyword(state, keyword);

            isTopLevel""",
        """            step.Id = ++state.LastStepId;

            // Resolve effective keyword (Given/And/And sequencing)
            step.EffectiveKeyword = ResolveKeyword(state, keyword);

            isTopLevel""",
    ),
    (
        """                    TestPhaseContext.Current = TestPhase.Action;
                    break;
            }
        }
    }

    /// <summary>
    /// Completes the current active step for the given test.""",
        """                    TestPhaseContext.Current = TestPhase.Action;
                    break;
            }
        }

        return step.Id;
    }

    /// <summary>
    /// Completes the current active step for the given test.""",
    ),
    (
        """        return new ScenarioStep
        {
            Keyword = step.EffectiveKeyword,""",
        """        return new ScenarioStep
        {
            CollectedId = step.Id == 0 ? null : step.Id,
            Keyword = step.EffectiveKeyword,""",
    ),
    (
        """        public string? LastKeywordCategory { get; set; }
""",
        """        public string? LastKeywordCategory { get; set; }
        public int LastStepId { get; set; }
""",
    ),
    (
        """        public bool IsAssertion { get; set; }
        public string? OriginalKeyword { get; set; }""",
        """        public int Id { get; set; }
        public bool LeftRunning { get; set; }
        public bool IsAssertion { get; set; }
        public string? OriginalKeyword { get; set; }""",
    ),
    (
        """    private static readonly ConcurrentDictionary<string, TestStepState> States = new();
""",
        """    private static readonly ConcurrentDictionary<string, TestStepState> States = new();

    /// <summary>The step this flow last opened through the weaver's pair.</summary>
    private static readonly AsyncLocal<CollectedStep?> FlowStep = new();
""",
    ),
)

edit(
    "src/Kronikol/Reports/ScenarioStep.cs",
    (
        """public record ScenarioStep
{
""",
        """public record ScenarioStep
{
    /// <summary>The id <see cref="Tracking.StepCollector"/> gave the step, within its test; never written to a report.</summary>
    internal int? CollectedId { get; init; }

""",
    ),
)

edit(
    "src/Kronikol/Tracking/RequestResponseLog.cs",
    (
        """    internal bool NamedByRemoteHostFallback { get; init; }
""",
        """    internal bool NamedByRemoteHostFallback { get; init; }

    /// <summary>
    /// The step open when this was logged (<see cref="StepCollector.OpenStepOf"/>): 0 for none, null where the test has no
    /// collected steps. Never written to a report; a call's stepPath is worked out from it.
    /// </summary>
    internal int? OpenStep { get; init; }
""",
    ),
)

edit(
    "src/Kronikol/Tracking/RequestResponseLogger.cs",
    (
        """        // A call the owner window answered for (DocumentOwnership) is held by its flow until an operation on
""",
        """        // Which step this happened under is known now and only now; before a hold, which can release it much later.
        if (log.OpenStep is null && StepCollector.OpenStepOf(log.TestId) is { } openStep)
            log = log with { OpenStep = openStep };

        // A call the owner window answered for (DocumentOwnership) is held by its flow until an operation on
""",
    ),
)

edit(
    "src/Kronikol/Reports/Scenario.cs",
    (
        """    public DateTimeOffset? EndedAt { get; set; }
""",
        """    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>
    /// This scenario's own copies of the steps <see cref="BackgroundStepsDetector"/> took as its background, which holds
    /// the first scenario's: the ids of the steps this scenario's calls happened under are on these.
    /// </summary>
    internal ScenarioStep[]? OwnBackgroundSteps { get; set; }
""",
    ),
)

edit(
    "src/Kronikol/Reports/BackgroundStepsDetector.cs",
    (
        """                scenario.BackgroundSteps = backgroundSteps;
""",
        """                scenario.OwnBackgroundSteps = scenario.Steps!.Take(commonPrefixLength).ToArray();
                scenario.BackgroundSteps = backgroundSteps;
""",
    ),
)

edit(
    "src/Kronikol/Reports/ReportGenerator.cs",
    (
        """            var ordered = stepsByTestId.TryGetValue(perTest.Key, out var s) ? s : [];
""",
        """            var ordered = stepsByTestId.TryGetValue(perTest.Key, out var s) ? s : [];
            var collected = collectedByTestId.GetValueOrDefault(perTest.Key);
""",
    ),
    (
        """                if (!log.IsDiagramMarker)
                {
                    paths.Add(current);
                    interactionIndex++;
""",
        """                if (!log.IsDiagramMarker)
                {
                    // The step open when the call was logged, where this scenario's steps carry their ids; else by the bars.
                    paths.Add(collected is not null && log.OpenStep is { } open
                        ? collected.GetValueOrDefault(open)
                        : current);
                    interactionIndex++;
""",
    ),
    (
        """                    case DiagramMarkerKind.Step:
                        current = stepMarkerCount""",
        """                    // A bar a collected step drew only draws: the calls name their own step.
                    case DiagramMarkerKind.Step when collected is not null && log.OpenStep is not null:
                        break;

                    case DiagramMarkerKind.Step:
                        current = stepMarkerCount""",
    ),
    (
        """            .ToDictionary(g => g.Key, g => OrderedStepPaths(g.First()));

        foreach (var perTest in trackedLogs.GroupBy(l => l.TestId))
""",
        """            .ToDictionary(g => g.Key, g => OrderedStepPaths(g.First()));

        var collectedByTestId = features
            .SelectMany(f => f.Scenarios)
            .GroupBy(s => s.Id)
            .Select(g => (g.Key, Paths: CollectedStepPaths(g.First())))
            .Where(x => x.Paths is not null)
            .ToDictionary(x => x.Key, x => x.Paths!);

        foreach (var perTest in trackedLogs.GroupBy(l => l.TestId))
""",
    ),
    (
        """    /// <summary>
    /// Whether a step delimiter's PlantUML belongs to a given step.""",
        """    /// <summary>
    /// The address a call under each collected step gets: the top-level step it is in. Null for a scenario whose steps
    /// carry no ids (built by hand through the public model), whose calls are read against the bars.
    /// </summary>
    private static Dictionary<int, string?>? CollectedStepPaths(Scenario scenario)
    {
        var paths = new Dictionary<int, string?>();

        void Add(ScenarioStep step, string path)
        {
            if (step.CollectedId is { } id)
                paths.TryAdd(id, path);
            foreach (var sub in step.SubSteps ?? [])
                Add(sub, path);
        }

        var background = scenario.OwnBackgroundSteps ?? scenario.BackgroundSteps ?? [];
        for (var i = 0; i < background.Length; i++)
            Add(background[i], $"b{i}");
        for (var i = 0; i < (scenario.Steps?.Length ?? 0); i++)
            Add(scenario.Steps![i], $"{i}");
        return paths.Count > 0 ? paths : null;
    }

    /// <summary>
    /// Whether a step delimiter's PlantUML belongs to a given step.""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollStepInfo.cs",
    (
        """    InlineParamCapture[]? InlineParams = null);
""",
        """    InlineParamCapture[]? InlineParams = null)
{
    /// <summary>The step the hooks opened for this Gherkin step (<c>StepCollector.StartFrame</c>); null on a hand-built one.</summary>
    internal int? FrameId { get; init; }
}
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollConstants.cs",
    (
        """    public const string OwnerHooksKey = "Kronikol.OwnerHooks";
""",
        """    public const string OwnerHooksKey = "Kronikol.OwnerHooks";

    /// <summary>The frame <c>BeforeStep</c> opened for the running Gherkin step.</summary>
    internal const string StepFrameKey = "Kronikol.StepFrame";
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
    (
        """        StepCollector.StartStep(scenarioId, stepType, stepInfo.Text, null, null,
            ReqNRollStepArguments.ToBarRows(stepInfo.Table), stepInfo.MultilineText);
""",
        """        _scenarioContext[ReqNRollConstants.StepFrameKey] = StepCollector.StartFrame(scenarioId, stepType, stepInfo.Text,
            ReqNRollStepArguments.ToBarRows(stepInfo.Table), stepInfo.MultilineText);
""",
    ),
    (
        """        StepCollector.CompleteStep(scenarioId, passed);

        steps.Add(new ReqNRollStepInfo(""",
        """        var frame = _scenarioContext.TryGetValue(ReqNRollConstants.StepFrameKey, out int? opened) ? opened : null;
        StepCollector.CompleteFrame(scenarioId, frame, passed);

        steps.Add(new ReqNRollStepInfo(""",
    ),
    (
        """            docString,
            inlineParams));
""",
        """            docString,
            inlineParams) { FrameId = frame });
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
    (
        """                                Steps = x.Steps.Count > 0
                                    ? MapSteps(x.Steps, StepCollector.GetSteps(x.ScenarioId))
                                    : null,
""",
        """                                Steps = MapSteps(x.Steps, StepCollector.GetSteps(x.ScenarioId)),
""",
    ),
    (
        """    private static ScenarioStep[] MapSteps(List<ReqNRollStepInfo> steps, ScenarioStep[]? collectedSteps = null)
    {
        var mapped = new ScenarioStep[steps.Count];
""",
        """    /// <summary>
    /// What ran, in the order it ran: each Gherkin step on the step its hooks opened, and a step method that ran with no
    /// Gherkin step open (a hook's) as itself. Steps built by hand through the public record carry no frame, and pair by
    /// position, as they did.
    /// </summary>
    private static ScenarioStep[]? MapSteps(List<ReqNRollStepInfo> steps, ScenarioStep[] collectedSteps)
    {
        if (steps.Count > 0 && steps.All(s => s.FrameId is null))
            return MapStepsByPosition(steps, collectedSteps);

        var byFrame = steps.ToDictionary(s => s.FrameId!.Value);
        var mapped = new List<ScenarioStep>();
        var priorFailure = false;
        foreach (var collected in collectedSteps)
        {
            if (collected.CollectedId is not { } id || !byFrame.TryGetValue(id, out var step))
            {
                mapped.Add(collected);
                continue;
            }

            mapped.Add(MapStep(step, collected, priorFailure));
            if (step.Status == ScenarioExecutionStatus.TestError || step.Status == ScenarioExecutionStatus.BindingError)
                priorFailure = true;
        }

        return mapped.Count > 0 ? mapped.ToArray() : null;
    }

    private static ScenarioStep MapStep(ReqNRollStepInfo step, ScenarioStep collected, bool priorFailure) => new()
    {
        CollectedId = collected.CollectedId,
        Keyword = step.Keyword,
        Text = step.Text,
        Status = step.Status.ToStepResult(priorFailure),
        Duration = step.Duration,
        DocString = step.DocString,
        Parameters = ParseTableText(step.TableText),
        TextSegments = BuildTextSegments(step),
        SubSteps = collected.SubSteps,
        Attachments = collected.Attachments,
    };

    private static ScenarioStep[] MapStepsByPosition(List<ReqNRollStepInfo> steps, ScenarioStep[]? collectedSteps = null)
    {
        var mapped = new ScenarioStep[steps.Count];
""",
    ),
)
print("applied")
