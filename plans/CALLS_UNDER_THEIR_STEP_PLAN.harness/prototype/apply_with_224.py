"""Resolves #224's prototype laid on this plan's end state: a worktree where end-state-on-9d994a4b.patch and then r1-r2-r3.patch have gone in with --3way.

Usage: apply_with_224.py <worktree>

Both plans make CompleteStepAsync plain and add an AsyncLocal. This keeps both flows side by side, the most direct test of whether the two fit: #224's chain decides phases and consumes a bypass, and this plan's decides the tree. One flow can serve both as a refactor once they are shown to agree.
"""
import pathlib
import re
import sys

root = pathlib.Path(sys.argv[1])


def read(path):
    raw = (root / path).read_bytes().decode("utf-8")
    return raw.replace("\r\n", "\n"), "\r\n" in raw


def write(path, s, crlf):
    assert "<<<<<<<" not in s and ">>>>>>>" not in s, path
    (root / path).write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))


BLOCK = re.compile(r"<<<<<<< ours\n(.*?)=======\n(.*?)>>>>>>> theirs\n", re.S)


def resolve(path, choices, extra=()):
    s, crlf = read(path)
    blocks = BLOCK.findall(s)
    assert len(blocks) == len(choices), (path, len(blocks), len(choices))
    it = iter(choices)
    s = BLOCK.sub(lambda m: next(it), s)
    for old, new in extra:
        assert s.count(old) == 1, (path, old[:70], s.count(old))
        s = s.replace(old, new)
    write(path, s, crlf)


resolve("src/Kronikol/Tracking/StepCollector.cs", [
    """        // A sync stub's success path comes here after a bypass too, and the bypass has closed the step: this would close the one above it.
        // Both flows hold the bypassed step until this comes through, so both let it go here: a merge that left one on it would take the parent's completion for the bypass's.
        var leftOnTheChain = CurrentFlowStep.Value is { Step.Bypassed: true } bypassed;
        if (leftOnTheChain)
            CurrentFlowStep.Value = CurrentFlowStep.Value!.Parent;
        if (LeftABypass() || leftOnTheChain)
            return;

        CompleteStep(ResolveTestId(), passed, errorMessage, fromTheStepsFlow: true);
""",
    """        => OpenStep(testId, keyword, text, paramNames, paramValues, table, docString);

    /// <summary>
    /// Starts a new step whose phase a framework adapter names. <paramref name="phaseKeyword"/> stands in for
    /// <paramref name="keyword"/> where the phase is concerned.
    /// </summary>
    internal static void StartStep(string? testId, string? keyword, string text, string[]? paramNames, object?[]? paramValues,
        string[][]? table, string? docString, string? phaseKeyword)
        => OpenStep(testId, keyword, text, paramNames, paramValues, table, docString, phaseKeyword: phaseKeyword);

    /// <summary>
    /// Starts a step that a framework's adapter opens for a step of the framework's own, and returns the id the adapter
    /// closes it by (<see cref="CompleteFrame"/>) and finds it by (<see cref="ScenarioStep.CollectedId"/>).
    /// </summary>
    internal static int? StartFrame(string? testId, string? keyword, string text, string[][]? table = null, string? docString = null, string? phaseKeyword = null)
        => OpenStep(testId, keyword, text, null, null, table, docString, isFrame: true, phaseKeyword: phaseKeyword);

    /// <summary>Closes the frame <paramref name="frameId"/>, by id. A step still running under it finishes by itself.</summary>
    internal static void CompleteFrame(string? testId, int? frameId, bool passed, string? errorMessage = null)
""",
    """            isTopLevel = parent is null;

            // The outermost open step that says anything about phases decides the test's phase, and a keyword under it is a
            // label (#224). A step with no keyword says nothing, so the keywords under it still decide.
            var says = PhaseConfiguration.PhaseOfStep(phaseKeyword ?? (Options.WhenTriggersAction ? keyword : null), state.Phase);
            step.SaysPhase = says is not (null or TestPhase.Unknown);
            if (step.SaysPhase && !OpenStepSaysPhase(testId))
            {
                // The Setup/Action boundary: the first time this test's steps go from Setup to Action.
                startsAction = says == TestPhase.Action && (state.Phase == TestPhase.Setup || state.CallInSetupStep) && !state.ActionStartDrawn;
                state.ActionStartDrawn |= startsAction;
                state.StepsSetPhase = true;
                phaseNow = state.Phase = state.CarriedPhase = says!.Value;
            }
            else if (!step.SaysPhase)
                fillWith = state.CarriedPhase;

            // Its place among its siblings is kept from the start, so steps read in the order they began.
            (parent?.SubSteps ?? state.TopLevel).Add(step);
            state.Open.Add(step);
        }

        FlowStep.Value = step;
        EnterFlow(testId, step);
""",
    """    public static Task CompleteStepAsync(Task task)
    {
        // Not async: this runs in the woven stub, which is the caller's flow. A bypassed step has closed already.
        var left = !LeaveFlow();
        if (LeftABypass() || left)
            return task;
        return FlowIsInAFrame() ? task : AwaitStep(task, LeaveFlowStep());
    }

    private static async Task AwaitStep(Task task, CollectedStep? step)
""",
    """    public static Task<T> CompleteStepAsync<T>(Task<T> task)
    {
        var left = !LeaveFlow();
        if (LeftABypass() || left)
            return task;
        return FlowIsInAFrame() ? task : AwaitStep(task, LeaveFlowStep());
    }

    private static async Task<T> AwaitStep<T>(Task<T> task, CollectedStep? step)
""",
], [
    ("""        string[][]? table, string? docString, bool isFrame = false)
""",
     """        string[][]? table, string? docString, bool isFrame = false, string? phaseKeyword = null)
"""),
    ("""        if (startsAction)
            DefaultTrackingDiagramOverride.StartAction(testId);
    }

    /// <summary>
    /// Whether an HTTP handler still has to ask""",
     """        if (startsAction)
            DefaultTrackingDiagramOverride.StartAction(testId);

        return step.Ordinal;
    }

    /// <summary>
    /// Whether an HTTP handler still has to ask"""),
])

resolve("src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs", [
    """        var frame = StepCollector.StartFrame(testId, keyword, stepText, phaseKeyword: keyword);
        // An example table runs the same steps once per row under one test, and the report lists the first row's.
        if (testId is not null && frame is { } opened)
        {
            Frames.TryAdd((testId, step.Id), opened);
            if (!step.ShouldReport)
                HiddenFrames.TryAdd(opened, true);
        }
""",
    """            if (!openedAsync)
                StepCollector.CompleteFrame(testId, frame, passed: true);
""",
], [
    ("""                return CloseWhenDone(testId, task);
""",
     """                return CloseWhenDone(testId, frame, task);
"""),
    ("""    private static async Task CloseWhenDone(string testId, Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            StepCollector.CompleteStep(testId, passed: false);
            throw;
        }

        if (StepCollector.HasActiveStep(testId))
            StepCollector.CompleteStep(testId, passed: true);
    }
""",
     """    private static async Task CloseWhenDone(string testId, int? frame, Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            StepCollector.CompleteFrame(testId, frame, passed: false);
            throw;
        }

        StepCollector.CompleteFrame(testId, frame, passed: true);
    }
"""),
])

resolve("src/Kronikol.LightBDD.Core/StepTrackingStepDecorator.cs", [
    """        // A suite's BUT continues the step before it, as LightBDD's own AND does.
        var phaseKeyword = string.Equals(keyword, "BUT", StringComparison.OrdinalIgnoreCase) ? "AND" : keyword;
        var frame = StepCollector.StartFrame(testId, keyword, text, phaseKeyword: phaseKeyword);
        if (frame is { } opened)
            Frames[step.Info.RuntimeId] = opened;
""",
    """        finally
        {
            StepCollector.CompleteFrame(testId, frame, passed: true);
            if (testId is not null)
                LastEnded[testId] = DateTimeOffset.UtcNow;
        }
""",
])

resolve("src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs", [
    """        // Per level, as a Gherkin step can run another. Still set on the scenario: the key is public, and a suite may read it there.
        var stepStopwatch = Stopwatch.StartNew();
        _scenarioContext[ReqNRollConstants.StepStopwatchKey] = stepStopwatch;
        _scenarioContext.StepContext[ReqNRollConstants.StepStopwatchKey] = stepStopwatch;
""",
    """        // The keyword as written is the step's label. Its phase is the type Reqnroll resolved, And and But to the keyword
        // they continue (#195).
        _scenarioContext.StepContext[ReqNRollConstants.StepFrameKey] = StepCollector.StartFrame(scenarioId, stepType, stepInfo.Text,
            ReqNRollStepArguments.ToBarRows(stepInfo.Table), stepInfo.MultilineText, phaseKeyword: stepInfo.StepDefinitionType.ToString());
""",
])

resolve("src/Kronikol/Tracking/TestTrackingMessageHandler.cs", [
    """        // A fetcher can throw for a call outside a scenario, as LightBDD's does, and a call under a TestIdentityScope is one. A call
        // must not fail for want of a step type.
        string? currentStepType;
        try
""",
])

print("#224 resolved on the end state")
