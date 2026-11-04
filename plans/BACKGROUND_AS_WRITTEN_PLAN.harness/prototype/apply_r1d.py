#!/usr/bin/env python
"""R1, fourth part: a step's frame belongs to its own level (apply_r1d.py <worktree>).

The edge shapes found this. A suite's BeforeStep hook that sorts ahead of the adapter's and throws keeps the adapter's BeforeStep from running, and
Reqnroll still runs AfterStep, so a stack popped there threw "Stack empty", which replaced the suite's own error as the test's failure, and lost the
failing step (E36). 5.27.13 reused a stale stopwatch there and listed the step. A suite's AfterStep hook that throws on an inner step leaves that frame
open, and the stack then handed its frame to the outer step (E40). So a frame is kept on the step context, which is per level, with a link to its parent.
"""
import sys

root = sys.argv[1]


def edit(path, *pairs):
    full = root + "/" + path
    text = open(full, encoding="utf-8", newline="").read()
    for old, new in pairs:
        assert text.count(old) == 1, old[:90]
        text = text.replace(old, new)
    open(full, "w", encoding="utf-8", newline="").write(text)


edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollConstants.cs",
    (
        """    /// <summary>The stopwatches of the steps still open: more than one when a step definition runs a step.</summary>
    internal const string OpenStepsKey = "Kronikol.OpenSteps";""",
        """    /// <summary>A step under way: on its own step context, and on the scenario's for the innermost, as a step definition can run a step.</summary>
    internal const string OpenStepKey = "Kronikol.OpenStep";""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
    (
        """        _scenarioContext[ReqNRollConstants.StepStopwatchKey] = Stopwatch.StartNew();
        if (!_scenarioContext.TryGetValue(ReqNRollConstants.OpenStepsKey, out Stack<OpenStep>? open))
            _scenarioContext[ReqNRollConstants.OpenStepsKey] = open = new Stack<OpenStep>();
        open!.Push(new OpenStep((Stopwatch)_scenarioContext[ReqNRollConstants.StepStopwatchKey]));
""",
        """        var stepStopwatch = Stopwatch.StartNew();
        _scenarioContext[ReqNRollConstants.StepStopwatchKey] = stepStopwatch;
        var opened = new OpenStep(stepStopwatch, InnermostOpenStep);
        _scenarioContext[ReqNRollConstants.OpenStepKey] = opened;
        _scenarioContext.StepContext[ReqNRollConstants.OpenStepKey] = opened;
""",
    ),
    (
        """        var open = (Stack<OpenStep>)_scenarioContext[ReqNRollConstants.OpenStepsKey];
        var opened = open.Pop();
        var stepStopwatch = opened.Stopwatch;
        stepStopwatch.Stop();
        var stepContext = _scenarioContext.StepContext;
""",
        """        // None when a suite's hook sorted ahead of BeforeStep and threw: Reqnroll still runs this, and the step is still the
        // one that failed, so it is listed, with no frame of the collector's to close and no time.
        var stepContext = _scenarioContext.StepContext;
        stepContext.TryGetValue(ReqNRollConstants.OpenStepKey, out OpenStep? opened);
        opened?.Stopwatch.Stop();
""",
    ),
    (
        """            ? opened.NestedOutcome ?? ScenarioExecutionStatus.OK
            : stepContext.Status;
        var passed = status == ScenarioExecutionStatus.OK;
        StepCollector.CompleteStep(scenarioId, passed);
""",
        """            ? opened?.NestedOutcome ?? ScenarioExecutionStatus.OK
            : stepContext.Status;
        var passed = status == ScenarioExecutionStatus.OK;
        if (opened is not null)
            StepCollector.CompleteStep(scenarioId, passed);
""",
    ),
    (
        """            stepStopwatch.Elapsed,
            tableText,
            docString,
            inlineParams) { Nested = opened.Nested };
""",
        """            opened?.Stopwatch.Elapsed,
            tableText,
            docString,
            inlineParams) { Nested = opened?.Nested };
""",
    ),
    (
        """        if (open.TryPeek(out var parent))
        {
""",
        """        var parent = opened is not null ? opened.Parent : InnermostOpenStep;
        if (opened is not null)
            _scenarioContext[ReqNRollConstants.OpenStepKey] = parent!;
        if (parent is not null)
        {
""",
    ),
    (
        """    private sealed class OpenStep(Stopwatch stopwatch)
    {
        public Stopwatch Stopwatch { get; } = stopwatch;
""",
        """    private sealed class OpenStep(Stopwatch stopwatch, OpenStep? parent)
    {
        public Stopwatch Stopwatch { get; } = stopwatch;
        public OpenStep? Parent { get; } = parent;
""",
    ),
    (
        """    /// <summary>A step under way. Its definition can run more of them, which finish inside it.</summary>
""",
        """    private OpenStep? InnermostOpenStep =>
        _scenarioContext.TryGetValue(ReqNRollConstants.OpenStepKey, out OpenStep? open) ? open : null;

    /// <summary>A step under way. Its definition can run more of them, which finish inside it.</summary>
""",
    ),
)
print("ok")
