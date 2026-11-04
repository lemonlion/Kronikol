"""Fourth cut, on the second cut, from the independent review: no Root; a step knows when its caller has moved on; closing never takes a step twice;
top-level steps keep their place from the start; a record reads the flow's nearest open step, never one that has ended. Includes #222's suggested line."""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])


def edit(path, pairs):
    p = root / path
    raw = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in raw
    s = raw.replace("\r\n", "\n")
    for old, new in pairs:
        count = s.count(old)
        if count != 1:
            raise SystemExit(f"{path}: expected one match, found {count}:\n{old[:200]}")
        s = s.replace(old, new)
    p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))


p = root / "src/Kronikol/Tracking/StepCollector.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")

# The block from Complete's helpers through NearestShared is replaced whole.
start = s.index("    private static void Complete(TestStepState state, CollectedStep step, bool passed, string? errorMessage)")
end = s.index("    private static CollectedStep? NearestShared(CollectedStep a, CollectedStep b)")
s = s[:start] + """    private static void Complete(TestStepState state, CollectedStep step, bool passed, string? errorMessage)
    {
        if (!step.IsOpen)
            return;

        step.EndTime = Stopwatch.GetTimestamp();
        step.Passed = passed;
        step.ErrorMessage = errorMessage;
        state.Open.Remove(step);
    }

    /// <summary>This flow's own step of this test, or its nearest ancestor still open; null when it has none. A flow whose step has ended knows nothing about now.</summary>
    private static CollectedStep? NearestOpenInFlow(TestStepState state)
    {
        for (var step = FlowStep.Value; step is not null && ReferenceEquals(step.State, state); step = step.Parent)
            if (step.IsOpen)
                return step;

        return null;
    }

    /// <summary>The step last opened whose caller has not moved on: what a stack would have on top, less a step left running, which closes by itself.</summary>
    private static CollectedStep? LastOpenInItsCaller(TestStepState state)
    {
        for (var i = state.Open.Count - 1; i >= 0; i--)
            if (!state.Open[i].Left)
                return state.Open[i];

        return null;
    }

    /// <summary>The step an assertion or an attachment belongs to. Call under the lock.</summary>
    private static CollectedStep? CurrentOf(TestStepState state) =>
        NearestOpenInFlow(state) ?? (state.Open.Count > 0 ? state.Open[^1] : null);

    /// <summary>
    /// The step a record logged now was made under, as a number the step keeps when the report is drawn (null: none). A
    /// flow inside an open step of this test answers for itself. A record from elsewhere, a service's own thread or a loop whose
    /// step has ended, reads what is open: the deepest step when one chain is open, and the nearest step they share when several run
    /// at once, which is the most that can be said of it. With nothing open it stays with the last top-level step, the bar above it.
    /// </summary>
    internal static int? OrdinalOfOpenStep(string? testId)
    {
        if (testId is null || !States.TryGetValue(testId, out var state))
            return null;

        lock (state)
        {
            if (NearestOpenInFlow(state) is { } own)
                return own.Ordinal;

            CollectedStep? shared = null;
            var any = false;
            foreach (var step in state.Open)
            {
                if (state.Open.Any(other => ReferenceEquals(other.Parent, step)))
                    continue;
                shared = any ? (shared is null ? null : NearestShared(shared, step)) : step;
                any = true;
            }

            return (shared ?? state.LastTopLevel)?.Ordinal;
        }
    }

""" + s[end:]

PAIRS = [
    ("""    private static readonly AsyncLocal<CollectedStep?> FlowStep = new();
""", """    private static readonly AsyncLocal<CollectedStep?> FlowStep = new();

    // Process-wide: a per-test count restarts when ClearSteps runs, and a stale stamp would read a new step.
    private static int _lastOrdinal;
"""),
    ("""            var parent = CurrentOf(state);
            // A top-level step's parent is the test's root, so a flow leaving it knows it is outside every step.
            step.Parent = parent ?? state.Root;
            step.Ordinal = ++state.LastOrdinal;
            if (parent is null)
                state.LastTopLevel = step;
""", """            var parent = NearestOpenInFlow(state) ?? LastOpenInItsCaller(state);
            step.Parent = parent;
            step.Ordinal = Interlocked.Increment(ref _lastOrdinal);
            if (parent is null)
                state.LastTopLevel = step;
"""),
    ("""            // Its place among its siblings is kept from the start, so sub-steps read in the order they began.
            parent?.SubSteps.Add(step);
            state.Open.Add(step);
""", """            // Its place among its siblings is kept from the start, so steps read in the order they began.
            (parent?.SubSteps ?? state.TopLevel).Add(step);
            state.Open.Add(step);
"""),
    ("""    public static void CompleteStep(bool passed, string? errorMessage = null)
    {
        CompleteStep(ResolveTestId(), passed, errorMessage);
    }
""", """    public static void CompleteStep(bool passed, string? errorMessage = null)
    {
        CompleteStep(ResolveTestId(), passed, errorMessage, fromTheStepsFlow: true);
    }
"""),
    ("""    public static void CompleteStep(string? testId, bool passed, string? errorMessage = null)
    {
        if (testId is null)
""", """    public static void CompleteStep(string? testId, bool passed, string? errorMessage = null) =>
        CompleteStep(testId, passed, errorMessage, fromTheStepsFlow: false);

    // A woven method ends in its own flow. Anyone else names only the test, so last in, first out, as they always had it.
    private static void CompleteStep(string? testId, bool passed, string? errorMessage, bool fromTheStepsFlow)
    {
        if (testId is null)
"""),
    ("""            completed = CurrentOf(state);
            if (completed is null)
                return;

            Complete(state, completed, passed, errorMessage);
""", """            completed = fromTheStepsFlow ? NearestOpenInFlow(state) ?? LastOpenInItsCaller(state) : LastOpenInItsCaller(state);
            if (completed is null)
                return;

            Complete(state, completed, passed, errorMessage);
"""),
    ("""            bypassed = CurrentOf(state);
            if (bypassed is null)
                return;
""", """            bypassed = NearestOpenInFlow(state) ?? LastOpenInItsCaller(state);
            if (bypassed is null)
                return;
"""),
    ("""            return state.CompletedSteps.Select(ToScenarioStep).ToArray();
""", """            return state.TopLevel.Select(ToScenarioStep).ToArray();
"""),
    ("""        // A sub-step still running when the report is drawn has failed at nothing: it reads as passed, with no duration.
        var status = step.Bypassed
            ? ExecutionResult.Bypassed
            : step.Passed || (step.EndTime == 0 && !step.IsAssertion) ? ExecutionResult.Passed : ExecutionResult.Failed;
""", """        // A step still running when the report is drawn has no result yet, and says so by having none.
        ExecutionResult? status = step.Bypassed
            ? ExecutionResult.Bypassed
            : step.IsOpen ? null : step.Passed ? ExecutionResult.Passed : ExecutionResult.Failed;
"""),
    ("""    private static CollectedStep? LeaveFlowStep()
    {
        var step = FlowStep.Value;
        if (step is null || !step.IsOpen)
            return null;
        FlowStep.Value = step.Parent;
        return step;
    }
""", """    private static CollectedStep? LeaveFlowStep()
    {
        var step = FlowStep.Value;
        if (step is null || !step.IsOpen)
            return null;
        // A stub whose StartStep found no test opened nothing: the flow's step is someone else's.
        if (ResolveTestId() is not { } testId || !States.TryGetValue(testId, out var state) || !ReferenceEquals(step.State, state))
            return null;
        step.Left = true;
        FlowStep.Value = step.Parent;
        return step;
    }
"""),
    ("""        public TestStepState() => Root = new CollectedStep { State = this };

        public CollectedStep Root { get; }
        public CollectedStep? LastTopLevel { get; set; }
        public List<CollectedStep> Open { get; } = new();
        public int LastOrdinal { get; set; }
        public List<CollectedStep> CompletedSteps { get; } = new();
""", """        public CollectedStep? LastTopLevel { get; set; }
        public List<CollectedStep> Open { get; } = new();
        public List<CollectedStep> TopLevel { get; } = new();
"""),
    ("""        public bool IsOpen => EndTime == 0;
""", """        public bool IsOpen => EndTime == 0;
        public bool Left { get; set; }
"""),
]
for old, new in PAIRS:
    count = s.count(old)
    if count != 1:
        raise SystemExit(f"StepCollector.cs: expected one match, found {count}:\n{old[:200]}")
    s = s.replace(old, new)
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))

# #222's suggested line: this plan comes after it.
edit("src/Kronikol/Reports/BackgroundStepsDetector.cs", [
    ("""            // Use the first scenario's steps as the background template
            var backgroundSteps = members[0].Steps!.Take(commonPrefixLength).ToArray();

            foreach (var scenario in members)
            {
                scenario.BackgroundSteps = backgroundSteps;
""", """            foreach (var scenario in members)
            {
                scenario.BackgroundSteps = scenario.Steps!.Take(commonPrefixLength).ToArray();
"""),
])
print("fourth cut applied")
