"""The fourth cut of plans/STEP_IDENTITY_PLAN.md, on top of apply_r1.py to apply_r5.py, from the independent review of the third.
The flow leaves this plan: FlowStep regressed plain lanes when step methods overlap and has holes for non-async wrappers, and
#225's R1 has the exact rule. What stays is what frames need: only CompleteFrame closes a frame. Plain lanes have no frames, so
their collector is as published by construction. Also: a skipped step is nested only under a frame, the event handlers catch, and
LightBDD's fallback by position needs results that ran. usage: python apply_r6.py <repo root>. Every replacement must match
exactly once or the script stops."""
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
    (
        """    /// <summary>The step this flow last opened through the weaver's pair.</summary>
    private static readonly AsyncLocal<CollectedStep?> FlowStep = new();

""",
        "",
    ),
    (
        """        var testId = ResolveTestId();
        var id = OpenStep(testId, keyword, text, paramNames, paramValues, table: null, docString: null);
        if (id is not null && testId is not null && States.TryGetValue(testId, out var state))
            lock (state)
                FlowStep.Value = state.StepStack.FirstOrDefault(s => s.Id == id);
""",
        """        StartStep(ResolveTestId(), keyword, text, paramNames, paramValues);
""",
    ),
    (
        """        var testId = ResolveTestId();
        if (FlowStep.Value is { } flow && testId is not null && States.TryGetValue(testId, out var state))
        {
            lock (state)
            {
                // A step method its parent's step ended without has no frame left on the stack: this is that step finishing,
                // and popping the stack would close whatever step is open now.
                if (flow.LeftRunning)
                {
                    flow.EndTime = Stopwatch.GetTimestamp();
                    flow.Passed = passed;
                    flow.ErrorMessage = errorMessage;
                    flow.LeftRunning = false;
                    return;
                }

                // Steps opened after this one are still open (two not awaited): this one closes, and they stay.
                if (state.StepStack.Count > 0 && !ReferenceEquals(state.StepStack.Peek(), flow) && state.StepStack.Contains(flow))
                {
                    var above = new Stack<CollectedStep>();
                    while (!ReferenceEquals(state.StepStack.Peek(), flow))
                        above.Push(state.StepStack.Pop());

                    state.StepStack.Pop();
                    flow.EndTime = Stopwatch.GetTimestamp();
                    flow.Passed = passed;
                    flow.ErrorMessage = errorMessage;
                    (state.StepStack.Count > 0 ? state.StepStack.Peek().SubSteps : state.CompletedSteps).Add(flow);

                    while (above.Count > 0)
                        state.StepStack.Push(above.Pop());
                    return;
                }
            }
        }

        CompleteStep(testId, passed, errorMessage);
""",
        """        CompleteStep(ResolveTestId(), passed, errorMessage);
""",
    ),
    (
        """    /// Closes the frame <paramref name="frameId"/>. A step still open above it was left running: it keeps its place under
    /// its parent, and its own completion, when it comes, closes that step and no other.
""",
        """    /// Closes the frame <paramref name="frameId"/>. A step still open above it was left running: it keeps its place under
    /// its parent, and reads as passed with no duration until a completion finds no step of its own to close (<see cref="CompleteStep(string?, bool, string?)"/>).
""",
    ),
    (
        """    private static void LeaveRunning(TestStepState state, CollectedStep open)
    {
        open.Passed = true;
        open.LeftRunning = true;
""",
        """    /// <summary>The framework step open for this test now, if any: a step that did not run while one is open is that step's.</summary>
    internal static int? OpenFrameOf(string? testId)
    {
        if (testId is null || !States.TryGetValue(testId, out var state))
            return null;

        lock (state)
            return state.StepStack.FirstOrDefault(s => s.IsFrame)?.Id;
    }

    private static void LeaveRunning(TestStepState state, CollectedStep open)
    {
        open.Passed = true;
        state.LeftRunning.Add(open);
""",
    ),
    (
        """            if (state.StepStack.Count == 0)
                return;

            completed = state.StepStack.Pop();
            completed.EndTime = Stopwatch.GetTimestamp();
""",
        """            // Only CompleteFrame closes a frame. A completion that finds one on top, or nothing, is a step the frame's close
            // left running finishing; which one is the flow's to know (#225), so it takes the one left last.
            if (state.StepStack.Count == 0 || state.StepStack.Peek().IsFrame)
            {
                if (state.LeftRunning.Count > 0)
                {
                    var finished = state.LeftRunning[^1];
                    state.LeftRunning.RemoveAt(state.LeftRunning.Count - 1);
                    finished.EndTime = Stopwatch.GetTimestamp();
                    finished.Passed = passed;
                    finished.ErrorMessage = errorMessage;
                }
                return;
            }

            completed = state.StepStack.Pop();
            completed.EndTime = Stopwatch.GetTimestamp();
""",
    ),
    (
        """        public List<CollectedStep> CompletedSteps { get; } = new();
        public string? LastKeywordCategory { get; set; }
""",
        """        public List<CollectedStep> CompletedSteps { get; } = new();
        public List<CollectedStep> LeftRunning { get; } = new();
        public string? LastKeywordCategory { get; set; }
""",
    ),
    (
        """        public int Id { get; set; }
        public bool LeftRunning { get; set; }
        public bool IsFrame { get; set; }
""",
        """        public int Id { get; set; }
        public bool IsFrame { get; set; }
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollStepInfo.cs",
    (
        """    /// <summary>For a step that did not run with a step open (a Gherkin step that runs another): the id of that step.</summary>
""",
        """    /// <summary>For a step that did not run with a Gherkin step open (one that runs another): the id of that step.</summary>
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
    (
        """                        CloseStep(scenario);
                });
""",
        """                        Quietly(() => CloseStep(scenario));
                });
""",
    ),
    (
        """                        NoteStepThatDidNotRun(scenario);
""",
        """                        Quietly(() => NoteStepThatDidNotRun(scenario));
""",
    ),
    (
        """                        CollectScenario(scenario, feature);
""",
        """                        Quietly(() => CollectScenario(scenario, feature));
""",
    ),
    (
        """    [BeforeStep(Order = int.MinValue)]
    public void BeforeStep()
""",
        """    // Reqnroll runs an event's handlers inline, so a throw here would fail the step it reports on.
    private static void Quietly(Action handle)
    {
        try
        {
            handle();
        }
        catch
        {
            // What the report loses is not worth the test
        }
    }

    [BeforeStep(Order = int.MinValue)]
    public void BeforeStep()
""",
    ),
    (
        """            NotRunUnder = StepCollector.OpenStepOf(scenarioId),
""",
        """            NotRunUnder = StepCollector.OpenFrameOf(scenarioId),
""",
    ),
)

edit(
    "src/Kronikol.LightBDD.Core/FeatureResultExtensions.cs",
    (
        """        // No step decorator ran (or the results were built by hand): no frames, and they pair by position.
        if (stepArray.All(s => StepTrackingStepDecorator.FrameOf(s.Info.RuntimeId) is null))
""",
        """        // No step decorator ran (or the results were built by hand): no frames, and they pair by position. Steps that never
        // ran are not that: a step method ahead of them (a scenario decorator's) stands as itself.
        if (stepArray.All(s => StepTrackingStepDecorator.FrameOf(s.Info.RuntimeId) is null)
            && stepArray.Any(s => s.Status != ExecutionStatus.NotRun))
""",
    ),
)
print("applied")
