"""R10: a BDDfy test that runs its steps more than once lists the first run's rows, as published does. python -I apply_r10.py <checkout>

Later runs under one test id (the next row of an example table, a retry, a second .BDDfy() call) fell into StepPairing's "no framework step opened it" branch, so an
example table with no overlap at all had three times the rows (kronikol-ca, B06, B13), and two .BDDfy() calls listed the second's steps under the first (third review, Z9).
A frame is listed only for a step of this scenario, and only the first time that step ran. A step running again is the table's next row, and whatever runs from there on is
not the first row's, so a first row that failed before its Then lists that Then as not run (third review, Z8). The later runs' calls read no step, as they do published.
"""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])


def edit(path, change):
    p = root / path
    t = p.read_bytes().decode("utf-8")
    newline = "\r\n" if "\r\n" in t else "\n"
    p.write_bytes(change(t.replace("\r\n", "\n")).replace("\n", newline).encode("utf-8"))


def replace(t, old, new):
    assert t.count(old) == 1, old[:70]
    return t.replace(old, new)


def executor(t):
    t = replace(t, """    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> HiddenFrames = new();

    /// <summary>What was collected, less the steps BDDfy does not report (a tear down), which draw a bar and list no row.</summary>
    internal static ScenarioStep[] Listed(ScenarioStep[] collected) =>
        collected.Where(c => c.CollectedId is not { } id || !HiddenFrames.ContainsKey(id)).ToArray();
""", """    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> StepOfFrame = new();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> RanAgain = new();

    /// <summary>
    /// What was collected, less the frames of what BDDfy does not list for this scenario: a step it does not report (a tear down), which draws a bar and
    /// lists no row, and a later run of the test's steps (the next row of an example table, a retry, a second BDDfy call), as the report lists the first.
    /// </summary>
    internal static ScenarioStep[] Listed(ScenarioStep[] collected, string testId, IReadOnlyCollection<string?> stepsOfTheScenario) =>
        collected.Where(c => c.CollectedId is not { } id
                             || !StepOfFrame.TryGetValue(id, out var stepId)
                             || (stepsOfTheScenario.Contains(stepId) && FrameOf(testId, stepId) == id)).ToArray();
""")
    t = replace(t, """        // An example table runs the same steps once per row under one test, and the report lists the first row's.
        if (testId is not null && frame is { } opened)
        {
            Frames.TryAdd((testId, step.Id), opened);
            if (!step.ShouldReport)
                HiddenFrames.TryAdd(opened, true);
        }
""", """        if (testId is not null && frame is { } opened)
        {
            StepOfFrame[opened] = step.Id;
            // An example table runs the same steps once per row under one test, and the report lists the first row's: a step
            // running again is the next row, and what runs from there on is not the first row's.
            if (Frames.ContainsKey((testId, step.Id)))
                RanAgain.TryAdd(testId, true);
            else if (!RanAgain.ContainsKey(testId))
                Frames.TryAdd((testId, step.Id), opened);
        }
""")
    return t


def extensions(t):
    t = replace(t, "MapSteps(x.Steps, BDDfyStepTrackingExecutor.Listed(StepCollector.GetSteps(x.TestId)), x.TestId)", "MapSteps(x.Steps, StepCollector.GetSteps(x.TestId), x.TestId)")
    t = replace(t, """        return StepPairing.InOrderTheyRan(steps, collectedSteps, s => BDDfyStepTrackingExecutor.FrameOf(testId, s.StepId), (step, collected) =>
""", """        var listed = BDDfyStepTrackingExecutor.Listed(collectedSteps, testId, steps.Select(s => s.StepId).ToArray());
        return StepPairing.InOrderTheyRan(steps, listed, s => BDDfyStepTrackingExecutor.FrameOf(testId, s.StepId), (step, collected) =>
""")
    return t


edit("src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs", executor)
edit("src/Kronikol.BDDfy.xUnit3/ScenarioInfoExtensions.cs", extensions)
print("ok")
