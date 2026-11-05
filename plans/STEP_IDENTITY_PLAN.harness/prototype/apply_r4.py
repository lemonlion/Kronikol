"""The second cut of plans/STEP_IDENTITY_PLAN.md, on top of apply_r1.py to apply_r3.py: the fixes from the independent review
(REVIEW.md). usage: python apply_r4.py <repo root>. Every replacement must match exactly once or the script stops."""
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
        """        => OpenStep(testId, keyword, text, null, null, table, docString);
""",
        """        => OpenStep(testId, keyword, text, null, null, table, docString, isFrame: true);
""",
    ),
    (
        """        string[][]? table, string? docString)
    {
        if (testId is null)
            return null;
""",
        """        string[][]? table, string? docString, bool isFrame = false)
    {
        if (testId is null)
            return null;
""",
    ),
    # 11: ids never repeat, whatever ClearSteps does; they are never written, so nothing needs them per test
    (
        """    /// <summary>The step this flow last opened through the weaver's pair.</summary>
""",
        """    private static int _lastStepId;

    /// <summary>The step this flow last opened through the weaver's pair.</summary>
""",
    ),
    (
        """            step.Id = ++state.LastStepId;
""",
        """            step.Id = Interlocked.Increment(ref _lastStepId);
            state.LastStepId = step.Id;

            // A framework step opening with no framework step open: whatever is open was left running (a hook's step not
            // awaited), and it is not this step's parent.
            if (isFrame && state.StepStack.Count > 0 && state.StepStack.All(s => !s.IsFrame))
                while (state.StepStack.Count > 0)
                    LeaveRunning(state, state.StepStack.Pop());
            step.IsFrame = isFrame;
""",
    ),
    # 6: the flow's step closes by identity, and the flag is read under the lock
    (
        """        // A step method its parent's step ended without has no frame left on the stack: this is that step finishing,
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
""",
        """        if (FlowStep.Value is { } flow && testId is not null && States.TryGetValue(testId, out var state))
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
    ),
    (
        """                else
                {
                    open.Passed = true;
                    open.LeftRunning = true;
                }

                (state.StepStack.Count > 0 ? state.StepStack.Peek().SubSteps : state.CompletedSteps).Add(open);
                if (isFrame)
                    return;
""",
        """                else
                {
                    LeaveRunning(state, open);
                    continue;
                }

                (state.StepStack.Count > 0 ? state.StepStack.Peek().SubSteps : state.CompletedSteps).Add(open);
                return;
""",
    ),
    # 1, 4: no step open is not an answer of its own; such a record reads as it does today
    (
        """    /// <summary>
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
""",
        """    /// <summary>
    /// The step open for this test now, which a call logged now happened under; null when none is, and that call reads as
    /// it always has, as the step before it.
    /// </summary>
    internal static int? OpenStepOf(string? testId)
    {
        if (testId is null || !States.TryGetValue(testId, out var state))
            return null;

        lock (state)
            return state.StepStack.Count > 0 ? state.StepStack.Peek().Id : null;
    }

    private static void LeaveRunning(TestStepState state, CollectedStep open)
    {
        open.Passed = true;
        open.LeftRunning = true;
        (state.StepStack.Count > 0 ? state.StepStack.Peek().SubSteps : state.CompletedSteps).Add(open);
    }
""",
    ),
    (
        """        public bool LeftRunning { get; set; }
""",
        """        public bool LeftRunning { get; set; }
        public bool IsFrame { get; set; }
""",
    ),
)

edit(
    "src/Kronikol/Tracking/RequestResponseLog.cs",
    (
        """    /// The step open when this was logged (<see cref="StepCollector.OpenStepOf"/>): 0 for none, null where the test has no
    /// collected steps. Never written to a report; a call's stepPath is worked out from it.
""",
        """    /// The step open when this was logged (<see cref="StepCollector.OpenStepOf"/>), null when none was. Never written to a
    /// report; a call's stepPath is worked out from it.
""",
    ),
)

edit(
    "src/Kronikol/Reports/ReportGenerator.cs",
    (
        """                    // The step open when the call was logged, where this scenario's steps carry their ids; else by the bars.
                    paths.Add(collected is not null && log.OpenStep is { } open
                        ? collected.GetValueOrDefault(open)
                        : current);
                    interactionIndex++;
""",
        """                    // The step open when the call was logged, where this scenario's steps carry their ids. With none open,
                    // or no ids to go by, the step before it.
                    if (collected is not null && log.OpenStep is { } open)
                        current = collected.GetValueOrDefault(open);
                    paths.Add(current);
                    interactionIndex++;
""",
    ),
    (
        """                    // A bar a collected step drew only draws: the calls name their own step.
                    case DiagramMarkerKind.Step when collected is not null && log.OpenStep is not null:
                        break;
""",
        """                    // A bar a collected step drew names its step, with no counting and no reading of its text.
                    case DiagramMarkerKind.Step when collected is not null && log.OpenStep is { } drawn:
                        current = collected.GetValueOrDefault(drawn);
                        break;
""",
    ),
)

edit(
    "src/Kronikol/Tracking/StepPairing.cs",
    (
        """/// step with no frame did not run, and stands where the framework lists it, after the last step that did.
/// </summary>""",
        """/// step with no frame did not run, and stands where the framework lists it, after the last step that did, or by
/// <paramref name="notRunAfter"/>, the id of the step opened before it, where the adapter knows that. A framework step whose
/// frame opened inside another step is listed there, as that step's sub-step, and not here.
/// </summary>""",
    ),
    (
        """        Func<T, ScenarioStep?, ScenarioStep> map)
    {
""",
        """        Func<T, ScenarioStep?, ScenarioStep> map, Func<T, int?>? notRunAfter = null)
    {
""",
    ),
    (
        """        void Through(int end)
        {
            for (; next < end; next++)
                merged.Add(map(natives[next], null));
        }

        for (var k = 0; k < collected.Length; k++)
        {
            if (collected[k].CollectedId""",
        """        void Through(int end)
        {
            for (; next < end; next++)
                if (frameOf(natives[next]) is not { } frame || !InATree(collected, frame))
                    merged.Add(map(natives[next], null));
        }

        for (var k = 0; k < collected.Length; k++)
        {
            while (next < natives.Count && frameOf(natives[next]) is null && notRunAfter?.Invoke(natives[next]) is { } after && collected[k].CollectedId > after)
                Through(next + 1);

            if (collected[k].CollectedId""",
    ),
    (
        """        Through(natives.Count);
        return merged;
    }
""",
        """        Through(natives.Count);
        return merged;
    }

    private static bool InATree(ScenarioStep[]? steps, int id) =>
        steps is not null && steps.Any(s => s.CollectedId == id || InATree(s.SubSteps, id));
""",
    ),
)

# 1b: a row with no keyword ran outside the scenario's own steps, and says nothing about how they start
edit(
    "src/Kronikol/Reports/BackgroundStepsDetector.cs",
    (
        """            if (members.Any(s => s.Steps![0].Keyword is "And" or "When"))
""",
        """            if (members.Any(s => FirstKeyword(s.Steps!) is "And" or "When"))
""",
    ),
    (
        """            if (members.Any(s => s.Steps!.Length > commonPrefixLength &&
                                 s.Steps![commonPrefixLength].Keyword is "Given" or "When"))
""",
        """            if (members.Any(s => s.Steps!.Length > commonPrefixLength &&
                                 FirstKeyword(s.Steps!.Skip(commonPrefixLength)) is "Given" or "When"))
""",
    ),
    (
        """    private static int FindCommonPrefixLength(Scenario[] scenarios)
""",
        """    // A row with no keyword ran outside the scenario's own steps (a hook's step method), and says nothing about how they start.
    private static string? FirstKeyword(IEnumerable<ScenarioStep> steps) =>
        steps.FirstOrDefault(step => step.Keyword is not null)?.Keyword;

    private static int FindCommonPrefixLength(Scenario[] scenarios)
""",
    ),
)

# 2: one frame per level, as a Gherkin step can run another
edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
    (
        """        _scenarioContext[ReqNRollConstants.StepStopwatchKey] = Stopwatch.StartNew();
""",
        """        // Per level, as a Gherkin step can run another. Still set on the scenario: the key is public, and a suite may read it there.
        var stepStopwatch = Stopwatch.StartNew();
        _scenarioContext[ReqNRollConstants.StepStopwatchKey] = stepStopwatch;
        _scenarioContext.StepContext[ReqNRollConstants.StepStopwatchKey] = stepStopwatch;
""",
    ),
    (
        """        _scenarioContext[ReqNRollConstants.StepFrameKey] = StepCollector.StartFrame(scenarioId, stepType, stepInfo.Text,""",
        """        _scenarioContext.StepContext[ReqNRollConstants.StepFrameKey] = StepCollector.StartFrame(scenarioId, stepType, stepInfo.Text,""",
    ),
    (
        """        if (!scenario.TryGetValue(ReqNRollConstants.StepFrameKey, out int? frame) || frame is null)
            return;
        scenario.Remove(ReqNRollConstants.StepFrameKey);

        var stepStopwatch = (Stopwatch)scenario[ReqNRollConstants.StepStopwatchKey];
        stepStopwatch.Stop();
        var stepContext = scenario.StepContext;
""",
        """        var stepContext = scenario.StepContext;
        if (!stepContext.TryGetValue(ReqNRollConstants.StepFrameKey, out int? frame) || frame is null)
            return;
        stepContext.Remove(ReqNRollConstants.StepFrameKey);

        var stepStopwatch = (Stopwatch)stepContext[ReqNRollConstants.StepStopwatchKey];
        stepStopwatch.Stop();
""",
    ),
    (
        """        if (scenario.ContainsKey(ReqNRollConstants.StepFrameKey)
            || !scenario.TryGetValue""",
        """        if (scenario.StepContext.ContainsKey(ReqNRollConstants.StepFrameKey)
            || !scenario.TryGetValue""",
    ),
)

# 3: Reqnroll pairs through the one rule
edit(
    "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
    (
        """        var byFrame = steps.Where(s => s.FrameId is not null).ToDictionary(s => s.FrameId!.Value);
        var notRun = new Queue<ReqNRollStepInfo>(steps.Where(s => s.FrameId is null));
        var mapped = new List<ScenarioStep>();
        var priorFailure = false;
        foreach (var collected in collectedSteps)
        {
            // A step that did not run stands where it came in the scenario: after the step opened last before it.
            while (notRun.TryPeek(out var next) && next.NotRunAfter < collected.CollectedId)
                mapped.Add(MapStep(notRun.Dequeue(), null, priorFailure));

            if (collected.CollectedId is not { } id || !byFrame.TryGetValue(id, out var step))
            {
                mapped.Add(collected);
                priorFailure |= collected.Status == ExecutionResult.Failed;
                continue;
            }

            mapped.Add(MapStep(step, collected, priorFailure));
            if (step.Status == ScenarioExecutionStatus.TestError || step.Status == ScenarioExecutionStatus.BindingError)
                priorFailure = true;
        }

        while (notRun.TryDequeue(out var rest))
            mapped.Add(MapStep(rest, null, priorFailure));

        return mapped.Count > 0 ? mapped.ToArray() : null;
""",
        """        var priorFailure = collectedSteps.Any(s => s.Status == ExecutionResult.Failed && steps.All(g => g.FrameId != s.CollectedId));
        var mapped = StepPairing.InOrderTheyRan(steps, collectedSteps, s => s.FrameId, (step, collected) =>
        {
            var row = MapStep(step, collected, priorFailure);
            if (step.Status == ScenarioExecutionStatus.TestError || step.Status == ScenarioExecutionStatus.BindingError)
                priorFailure = true;
            return row;
        }, s => s.NotRunAfter);

        return mapped.Count > 0 ? mapped.ToArray() : null;
""",
    ),
)

# 7, 8, 9: BDDfy
edit(
    "src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs",
    (
        """    /// <summary>The frame opened for a BDDfy step of a test, if it ran.</summary>
""",
        """    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> HiddenFrames = new();

    /// <summary>What was collected, less the steps BDDfy does not report (a tear down), which draw a bar and list no row.</summary>
    internal static ScenarioStep[] Listed(ScenarioStep[] collected) =>
        collected.Where(c => c.CollectedId is not { } id || !HiddenFrames.ContainsKey(id)).ToArray();

    /// <summary>The frame opened for a BDDfy step of a test, if it ran.</summary>
""",
    ),
    (
        """        if (testId is not null && frame is { } opened)
            Frames[(testId, step.Id)] = opened;
""",
        """        // An example table runs the same steps once per row under one test, and the report lists the first row's.
        if (testId is not null && frame is { } opened)
        {
            Frames.TryAdd((testId, step.Id), opened);
            if (!step.ShouldReport)
                HiddenFrames.TryAdd(opened, true);
        }
""",
    ),
)

edit(
    "src/Kronikol.BDDfy.xUnit3/ScenarioInfoExtensions.cs",
    (
        """                                    ? MapSteps(x.Steps, StepCollector.GetSteps(x.TestId), x.TestId)
""",
        """                                    ? MapSteps(x.Steps, BDDfyStepTrackingExecutor.Listed(StepCollector.GetSteps(x.TestId)), x.TestId)
""",
    ),
    (
        """        var priorFailure = false;
        return StepPairing.InOrderTheyRan(steps, collectedSteps, s => BDDfyStepTrackingExecutor.FrameOf(testId, s.StepId), (step, collected) =>
""",
        """        var priorFailure = false;
        // Steps built by hand have no frames, and pair by position.
        if (steps.All(s => BDDfyStepTrackingExecutor.FrameOf(testId, s.StepId) is null))
        {
            return steps.Select((step, i) =>
            {
                var collected = i < collectedSteps.Length ? collectedSteps[i] : null;
                var mapped = new ScenarioStep
                {
                    CollectedId = collected?.CollectedId,
                    Keyword = step.Keyword,
                    Text = step.Text,
                    Status = step.Result.ToStepResult(priorFailure),
                    Duration = step.Duration,
                    SubSteps = collected?.SubSteps,
                    Attachments = collected?.Attachments,
                };
                if (step.Result == TestStack.BDDfy.Result.Failed)
                    priorFailure = true;
                return mapped;
            }).ToArray();
        }

        return StepPairing.InOrderTheyRan(steps, collectedSteps, s => BDDfyStepTrackingExecutor.FrameOf(testId, s.StepId), (step, collected) =>
""",
    ),
)

# 9: LightBDD
edit(
    "src/Kronikol.LightBDD.Core/FeatureResultExtensions.cs",
    (
        """        if (stepArray.Length == 0) return null;
        var priorFailure = false;
        return StepPairing.InOrderTheyRan(stepArray, collectedSteps ?? [], s => StepTrackingStepDecorator.FrameOf(s.Info.RuntimeId), (step, collected) =>
""",
        """        if (stepArray.Length == 0) return null;
        var priorFailure = false;
        // No step decorator ran (or the results were built by hand): no frames, and they pair by position.
        if (stepArray.All(s => StepTrackingStepDecorator.FrameOf(s.Info.RuntimeId) is null))
        {
            var byPosition = new ScenarioStep[stepArray.Length];
            for (var i = 0; i < stepArray.Length; i++)
            {
                var collected = collectedSteps is not null && i < collectedSteps.Length ? collectedSteps[i] : null;
                byPosition[i] = MapStep(stepArray[i], priorFailure, scenarioSkipped, collected);
                if (stepArray[i].Status == ExecutionStatus.Failed)
                    priorFailure = true;
            }

            return byPosition;
        }

        return StepPairing.InOrderTheyRan(stepArray, collectedSteps ?? [], s => StepTrackingStepDecorator.FrameOf(s.Info.RuntimeId), (step, collected) =>
""",
    ),
    (
        """        var subSteps = MapSteps(step.GetSubSteps(), scenarioSkipped, collectedSubs) ?? collectedSubs;
""",
        """        var nativeSteps = step.GetSubSteps().ToArray();
        var nativeSubSteps = MapSteps(nativeSteps, scenarioSkipped, collectedSubs);
        ScenarioStep[]? subSteps;
        if (nativeSubSteps is not null && collectedSubs is not null && collectedSubs.Length > nativeSubSteps.Length
            && nativeSteps.All(s => StepTrackingStepDecorator.FrameOf(s.Info.RuntimeId) is null))
            subSteps = [..nativeSubSteps, ..collectedSubs[nativeSubSteps.Length..]];
        else
            subSteps = nativeSubSteps ?? collectedSubs;
""",
    ),
)
print("applied")
