"""First cut of the #225 fix, applied to a throwaway worktree. Every replacement must match exactly once."""
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


edit("src/Kronikol/Tracking/StepCollector.cs", [
    ("""    private static readonly ConcurrentDictionary<string, TestStepState> States = new();
""", """    private static readonly ConcurrentDictionary<string, TestStepState> States = new();

    // The step this flow of execution is inside. It follows awaits, so two sub-steps running at once each see their own.
    private static readonly AsyncLocal<CollectedStep?> FlowStep = new();
"""),
    ("""        var isTopLevel = false;
        lock (state)
        {
            // Resolve effective keyword (Given/And/And sequencing)
            step.EffectiveKeyword = ResolveKeyword(state, keyword);

            isTopLevel = state.StepStack.Count == 0;
            state.StepStack.Push(step);
        }
""", """        step.State = state;
        var isTopLevel = false;
        lock (state)
        {
            var parent = CurrentOf(state);
            step.Parent = parent;
            step.Ordinal = ++state.LastOrdinal;

            // Resolve effective keyword (Given/And/And sequencing)
            step.EffectiveKeyword = ResolveKeyword(state, keyword, isSubStep: parent is not null);

            isTopLevel = parent is null;
            // Its place among its siblings is kept from the start, so sub-steps read in the order they began.
            parent?.SubSteps.Add(step);
            state.Open.Add(step);
        }

        FlowStep.Value = step;
"""),
    ("""        CollectedStep? completed;
        lock (state)
        {
            if (state.StepStack.Count == 0)
                return;

            completed = state.StepStack.Pop();
            completed.EndTime = Stopwatch.GetTimestamp();
            completed.Passed = passed;
            completed.ErrorMessage = errorMessage;

            if (state.StepStack.Count > 0)
            {
                // Nested: add as sub-step of parent
                state.StepStack.Peek().SubSteps.Add(completed);
            }
            else
            {
                // Top-level step
                state.CompletedSteps.Add(completed);
            }
        }
    }
""", """        CollectedStep? completed;
        lock (state)
        {
            completed = CurrentOf(state);
            if (completed is null)
                return;

            Complete(state, completed, passed, errorMessage);
        }

        if (ReferenceEquals(FlowStep.Value, completed))
            FlowStep.Value = completed.Parent;
    }

    private static void Complete(TestStepState state, CollectedStep step, bool passed, string? errorMessage)
    {
        step.EndTime = Stopwatch.GetTimestamp();
        step.Passed = passed;
        step.ErrorMessage = errorMessage;
        state.Open.Remove(step);
        if (step.Parent is null)
            state.CompletedSteps.Add(step);
    }

    /// <summary>
    /// The step a call or a nested step of this test belongs to: this flow's, or its nearest ancestor still open; with no
    /// such flow (a hook, a service's own thread), the step last opened, as a stack would have it. Call under the lock.
    /// </summary>
    private static CollectedStep? CurrentOf(TestStepState state)
    {
        for (var step = FlowStep.Value; step is not null; step = step.Parent)
            if (ReferenceEquals(step.State, state) && step.IsOpen)
                return step;

        return state.Open.Count > 0 ? state.Open[^1] : null;
    }

    /// <summary>
    /// The step a record logged now was made under, as a number the step keeps when the report is drawn (null: none). A
    /// flow inside one of this test's steps answers for itself, even once that step has ended: it is the step that started
    /// the work. A record from elsewhere, a service's own thread, reads what is open: the deepest step when one chain is
    /// open, and the nearest step they share when several run at once, which is the most that can be said of it.
    /// </summary>
    internal static int? OrdinalOfOpenStep(string? testId)
    {
        if (testId is null || !States.TryGetValue(testId, out var state))
            return null;

        if (FlowStep.Value is { } flow && ReferenceEquals(flow.State, state))
            return flow.Ordinal;

        lock (state)
        {
            CollectedStep? shared = null;
            foreach (var step in state.Open)
            {
                if (state.Open.Any(other => ReferenceEquals(other.Parent, step)))
                    continue;
                shared = shared is null ? step : NearestShared(shared, step);
                if (shared is null)
                    return null;
            }
            return shared?.Ordinal;
        }
    }

    private static CollectedStep? NearestShared(CollectedStep a, CollectedStep b)
    {
        var ancestors = new HashSet<CollectedStep>(ReferenceEqualityComparer.Instance);
        for (var step = a; step is not null; step = step.Parent)
            ancestors.Add(step);
        for (var step = b; step is not null; step = step.Parent)
            if (ancestors.Contains(step))
                return step;
        return null;
    }
"""),
    ("""        lock (state)
        {
            if (state.StepStack.Count == 0)
                return;

            var bypassed = state.StepStack.Pop();
            bypassed.EndTime = Stopwatch.GetTimestamp();
            bypassed.Bypassed = true;
            bypassed.BypassReason = reason;

            if (state.StepStack.Count > 0)
            {
                // Nested: add as sub-step of parent
                state.StepStack.Peek().SubSteps.Add(bypassed);
            }
            else
            {
                // Top-level step
                state.CompletedSteps.Add(bypassed);
            }
        }
    }
""", """        CollectedStep? bypassed;
        lock (state)
        {
            bypassed = CurrentOf(state);
            if (bypassed is null)
                return;

            bypassed.Bypassed = true;
            bypassed.BypassReason = reason;
            Complete(state, bypassed, passed: false, errorMessage: null);
        }

        if (ReferenceEquals(FlowStep.Value, bypassed))
            FlowStep.Value = bypassed.Parent;
    }
"""),
    ("""        lock (state)
        {
            if (state.StepStack.Count == 0)
                return;

            var subStep = new CollectedStep
""", """        lock (state)
        {
            var current = CurrentOf(state);
            if (current is null)
                return;

            var subStep = new CollectedStep
"""),
    ("""            state.StepStack.Peek().SubSteps.Add(subStep);
""", """            current.SubSteps.Add(subStep);
"""),
    ("""            if (state.StepStack.Count > 0)
            {
                var currentStep = state.StepStack.Peek();
                currentStep.Attachments ??= new List<FileAttachment>();
""", """            if (CurrentOf(state) is { } currentStep)
            {
                currentStep.Attachments ??= new List<FileAttachment>();
"""),
    ("""            return state.StepStack.Count > 0;
""", """            return state.Open.Count > 0;
"""),
    ("""    private static string? ResolveKeyword(TestStepState state, string? keyword)
""", """    private static string? ResolveKeyword(TestStepState state, string? keyword, bool isSubStep)
"""),
    ("""        if (state.StepStack.Count > 0)
            return displayKeyword; // Sub-steps keep their original keyword
""", """        if (isSubStep)
            return displayKeyword; // Sub-steps keep their original keyword
"""),
    ("""        var status = step.Bypassed
            ? ExecutionResult.Bypassed
            : step.Passed ? ExecutionResult.Passed : ExecutionResult.Failed;
""", """        // A sub-step still running when the report is drawn has failed at nothing: it reads as passed, with no duration.
        var status = step.Bypassed
            ? ExecutionResult.Bypassed
            : step.Passed || (step.EndTime == 0 && !step.IsAssertion) ? ExecutionResult.Passed : ExecutionResult.Failed;
"""),
    ("""            Keyword = step.EffectiveKeyword,
            IsAssertion = step.IsAssertion,
""", """            StepOrdinal = step.Ordinal > 0 ? step.Ordinal : null,
            Keyword = step.EffectiveKeyword,
            IsAssertion = step.IsAssertion,
"""),
    ("""    public static async Task CompleteStepAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
            CompleteStep(true, null);
        }
        catch (Exception ex)
        {
            CompleteStep(false, ex.Message);
            throw;
        }
    }
""", """    public static Task CompleteStepAsync(Task task) => AwaitStep(task, LeaveFlowStep());

    private static async Task AwaitStep(Task task, CollectedStep? step)
    {
        try
        {
            await task.ConfigureAwait(false);
            CompleteAwaited(step, true, null);
        }
        catch (Exception ex)
        {
            CompleteAwaited(step, false, ex.Message);
            throw;
        }
    }

    // Not async, on purpose: the caller's flow must leave the step before the caller carries on, so that the next
    // step it starts is this one's sibling, while the step's own body keeps it across its awaits.
    private static CollectedStep? LeaveFlowStep()
    {
        var step = FlowStep.Value;
        if (step is null || !step.IsOpen)
            return null;
        FlowStep.Value = step.Parent;
        return step;
    }

    private static void CompleteAwaited(CollectedStep? step, bool passed, string? errorMessage)
    {
        if (step is null)
        {
            CompleteStep(passed, errorMessage);
            return;
        }

        lock (step.State!)
            Complete(step.State!, step, passed, errorMessage);
    }
"""),
    ("""    public static async Task<T> CompleteStepAsync<T>(Task<T> task)
    {
        try
        {
            var result = await task.ConfigureAwait(false);
            CompleteStep(true, null);
            return result;
        }
        catch (Exception ex)
        {
            CompleteStep(false, ex.Message);
            throw;
        }
    }
""", """    public static Task<T> CompleteStepAsync<T>(Task<T> task) => AwaitStep(task, LeaveFlowStep());

    private static async Task<T> AwaitStep<T>(Task<T> task, CollectedStep? step)
    {
        try
        {
            var result = await task.ConfigureAwait(false);
            CompleteAwaited(step, true, null);
            return result;
        }
        catch (Exception ex)
        {
            CompleteAwaited(step, false, ex.Message);
            throw;
        }
    }
"""),
    ("""        public Stack<CollectedStep> StepStack { get; } = new();
""", """        public List<CollectedStep> Open { get; } = new();
        public int LastOrdinal { get; set; }
"""),
    ("""        public bool IsAssertion { get; set; }
        public string? OriginalKeyword { get; set; }
""", """        public int Ordinal { get; set; }
        public CollectedStep? Parent { get; set; }
        public TestStepState? State { get; set; }
        public bool IsOpen => EndTime == 0;
        public bool IsAssertion { get; set; }
        public string? OriginalKeyword { get; set; }
"""),
])

edit("src/Kronikol/Reports/ScenarioStep.cs", [
    ("""public record ScenarioStep
{
""", """public record ScenarioStep
{
    /// <summary>
    /// The number the step collector gave this step, which a call's record names to say the step it was made under. Null
    /// for a step no collector opened. Never written out.
    /// </summary>
    internal int? StepOrdinal { get; init; }

"""),
])

edit("src/Kronikol/Tracking/RequestResponseLog.cs", [
    ("""    public bool NoteOnRight { get; set; }
""", """    /// <summary>
    /// The step this record was logged under (<see cref="Reports.ScenarioStep.StepOrdinal"/>); null when none was open or
    /// the record did not come through the step collector's process. Never written out.
    /// </summary>
    internal int? StepOrdinal { get; init; }

    public bool NoteOnRight { get; set; }
"""),
])

edit("src/Kronikol/Tracking/RequestResponseLogger.cs", [
    ("""        // A call the owner window answered for (DocumentOwnership) is held by its flow until an operation on
""", """        // Which step it was made under is known now, and only now. The report reads it once the steps are final.
        if (!log.IsDiagramMarker && log.StepOrdinal is null && StepCollector.OrdinalOfOpenStep(log.TestId) is { } step)
            log = log with { StepOrdinal = step };

        // A call the owner window answered for (DocumentOwnership) is held by its flow until an operation on
"""),
])

edit("src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs", [
    ("""                Keyword = steps[i].Keyword,
                Text = steps[i].Text,
                Status = steps[i].Status.ToStepResult(priorFailure),
""", """                StepOrdinal = collectedSteps is not null && i < collectedSteps.Length
                    ? collectedSteps[i].StepOrdinal
                    : null,
                Keyword = steps[i].Keyword,
                Text = steps[i].Text,
                Status = steps[i].Status.ToStepResult(priorFailure),
"""),
])

edit("src/Kronikol.BDDfy.xUnit3/ScenarioInfoExtensions.cs", [
    ("""                Keyword = steps[i].Keyword,
                Text = steps[i].Text,
                Status = steps[i].Result.ToStepResult(priorFailure),
""", """                StepOrdinal = collectedSteps is not null && i < collectedSteps.Length
                    ? collectedSteps[i].StepOrdinal
                    : null,
                Keyword = steps[i].Keyword,
                Text = steps[i].Text,
                Status = steps[i].Result.ToStepResult(priorFailure),
"""),
])

edit("src/Kronikol/Reports/ReportGenerator.cs", [
    ("""            var ordered = stepsByTestId.TryGetValue(perTest.Key, out var s) ? s : [];
            var paths = new List<string?>();
""", """            var ordered = stepsByTestId.TryGetValue(perTest.Key, out var s) ? s : [];
            // Where each step the collector opened ended up. A record that names one of them needs nothing else.
            var byOrdinal = ordinalsByTestId.TryGetValue(perTest.Key, out var o) ? o : [];
            var paths = new List<string?>();
"""),
    ("""                if (!log.IsDiagramMarker)
                {
                    paths.Add(current);
                    interactionIndex++;
                    continue;
                }
""", """                if (!log.IsDiagramMarker)
                {
                    paths.Add(log.StepOrdinal is { } stamped && byOrdinal.TryGetValue(stamped, out var path) ? path : current);
                    interactionIndex++;
                    continue;
                }
"""),
    ("""        foreach (var perTest in trackedLogs.GroupBy(l => l.TestId))
        {
            var ordered = stepsByTestId""", """        var ordinalsByTestId = features
            .SelectMany(f => f.Scenarios)
            .GroupBy(s => s.Id)
            .ToDictionary(g => g.Key, g => PathsByOrdinal(g.First()));

        foreach (var perTest in trackedLogs.GroupBy(l => l.TestId))
        {
            var ordered = stepsByTestId"""),
    ("""    /// <summary>
    /// Whether a step delimiter's PlantUML belongs to a given step.""", """    /// <summary>The address of every step a collector opened, as the data file addresses it: b0, 0, 0.1.</summary>
    private static Dictionary<int, string> PathsByOrdinal(Scenario scenario)
    {
        var paths = new Dictionary<int, string>();
        void Walk(ScenarioStep[]? steps, string prefix, string separator)
        {
            for (var i = 0; i < (steps?.Length ?? 0); i++)
            {
                var path = $"{prefix}{separator}{i}";
                if (steps![i].StepOrdinal is { } ordinal)
                    paths[ordinal] = path;
                Walk(steps[i].SubSteps, path, ".");
            }
        }

        Walk(scenario.BackgroundSteps, "b", "");
        Walk(scenario.Steps, "", "");
        return paths;
    }

    /// <summary>
    /// Whether a step delimiter's PlantUML belongs to a given step."""),
])

edit("src/Kronikol/Reports/FailuresDigestGenerator.cs", [
    ("""    private static bool Covers(string attributed, string failingStep)
    {
        if (string.Equals(attributed, failingStep, StringComparison.Ordinal))
            return true;

        return failingStep.Length > attributed.Length
               && failingStep[attributed.Length] == '.'
               && failingStep.AsSpan(0, attributed.Length).SequenceEqual(attributed);
    }
""", """    private static bool Covers(string attributed, string failingStep) =>
        Query.Address.PathCoveredBy(attributed, failingStep) || Query.Address.PathCoveredBy(failingStep, attributed);
"""),
])

edit("src/Kronikol/Query/QueryCommand.Narrative.cs", [
    ("""            var failingSteps = scenario.AllSteps().Where(s => s.Step.Failed).ToArray();
""", """            var failingSteps = scenario.AllSteps().Where(s => s.Step.Failed).ToArray();
            // Each call under the deepest failing step that covers it, so none is listed twice and none is lost.
            bool InFailingStep(InteractionEntry i, string failing) =>
                Address.PathCoveredBy(i.StepPath, failing)
                && !failingSteps.Any(deeper => deeper.Path != failing
                                               && Address.PathCoveredBy(deeper.Path, failing)
                                               && Address.PathCoveredBy(i.StepPath, deeper.Path));
"""),
    ("""                var scoped = scenario.Interactions.Where(i => i.StepPath == path && i.Type == "Request").ToArray();
""", """                var scoped = scenario.Interactions.Where(i => InFailingStep(i, path) && i.Type == "Request").ToArray();
"""),
    ("""            if (byStep.TryGetValue(path, out var calls) && calls.Length > 0)
                range = calls.Length == 1
""", """            // A row answers for the step and everything under it, as a step's address does.
            var calls = byStep.Where(e => Address.PathCoveredBy(e.Key, path)).SelectMany(e => e.Value).OrderBy(c => c.Ordinal).ToArray();
            if (calls.Length > 0)
                range = calls.Length == 1
"""),
])

# LightBDD: the mapped step keeps the number of the collected step it pairs with.
edit("src/Kronikol.LightBDD.Core/FeatureResultExtensions.cs", [
    ("""            SubSteps = subSteps,
""", """            StepOrdinal = collected?.StepOrdinal,
            SubSteps = subSteps,
"""),
])
print("applied")
