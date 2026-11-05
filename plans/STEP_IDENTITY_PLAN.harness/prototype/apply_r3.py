"""First cut of R3 for plans/STEP_IDENTITY_PLAN.md, on top of apply_r1.py and apply_r2.py: BDDfy and LightBDD pair by identity too,
with the two LightBDD defects found on the way. usage: python apply_r3.py <repo root>."""
import pathlib
import re
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


(root / "src/Kronikol/Tracking/StepPairing.cs").write_text(
    """using Kronikol.Reports;

namespace Kronikol.Tracking;

/// <summary>
/// Puts a framework's own steps together with what <see cref="StepCollector"/> collected, by the frame each opened: what ran,
/// in the order it ran. A collected step no framework step opened (a step method outside one) stands as itself; a framework
/// step with no frame did not run, and stands where the framework lists it, after the last step that did.
/// </summary>
internal static class StepPairing
{
    public static List<ScenarioStep> InOrderTheyRan<T>(IReadOnlyList<T> natives, ScenarioStep[] collected, Func<T, int?> frameOf,
        Func<T, ScenarioStep?, ScenarioStep> map)
    {
        var indexOfFrame = new Dictionary<int, int>();
        for (var i = 0; i < natives.Count; i++)
            if (frameOf(natives[i]) is { } frame)
                indexOfFrame.TryAdd(frame, i);

        var lastFramed = Array.FindLastIndex(collected, c => c.CollectedId is { } id && indexOfFrame.ContainsKey(id));
        var merged = new List<ScenarioStep>();
        var next = 0;

        void Through(int end)
        {
            for (; next < end; next++)
                merged.Add(map(natives[next], null));
        }

        for (var k = 0; k < collected.Length; k++)
        {
            if (collected[k].CollectedId is { } id && indexOfFrame.TryGetValue(id, out var native))
            {
                Through(native);
                merged.Add(map(natives[native], collected[k]));
                next = native + 1;
            }
            else
            {
                merged.Add(collected[k]);
            }

            if (k == lastFramed)
                Through(natives.Count);
        }

        Through(natives.Count);
        return merged;
    }
}
""",
    encoding="utf-8",
    newline="\n",
)

# BDDfy
file = root / "src/Kronikol.BDDfy.xUnit3/BDDfyScenarioInfo.cs"
text = file.read_text(encoding="utf-8")
updated, count = re.subn(
    r"(public record BDDfyStepInfo\([^\n]*\));",
    lambda m: m.group(1)
    + "\n{\n    /// <summary>BDDfy's id for the step, which finds the frame the step executor opened for it.</summary>\n"
    + "    internal string? StepId { get; init; }\n}",
    text,
)
if count != 1:
    sys.exit("BDDfyStepInfo record not found")
file.write_text(updated, encoding="utf-8", newline="")

edit(
    "src/Kronikol.BDDfy.xUnit3/StepExtensions.cs",
    (
        """                return new BDDfyStepInfo(firstWord, title[(firstSpace + 1)..], step.Result, duration);""",
        """                return new BDDfyStepInfo(firstWord, title[(firstSpace + 1)..], step.Result, duration) { StepId = step.Id };""",
    ),
    (
        """        return new BDDfyStepInfo("Step", title, step.Result, duration);""",
        """        return new BDDfyStepInfo("Step", title, step.Result, duration) { StepId = step.Id };""",
    ),
)

edit(
    "src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs",
    (
        """    private static readonly AsyncLocal<string?> CurrentStepTypeLocal = new();
""",
        """    private static readonly AsyncLocal<string?> CurrentStepTypeLocal = new();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string TestId, string StepId), int> Frames = new();

    /// <summary>The frame opened for a BDDfy step of a test, if it ran.</summary>
    internal static int? FrameOf(string testId, string? stepId) =>
        stepId is not null && Frames.TryGetValue((testId, stepId), out var frame) ? frame : null;
""",
    ),
    (
        """        if (testId is not null)
            StepCollector.StartStep(testId, keyword, stepText, null, null);
""",
        """        var frame = StepCollector.StartFrame(testId, keyword, stepText);
        if (testId is not null && frame is { } opened)
            Frames[(testId, step.Id)] = opened;
""",
    ),
    (
        """            if (testId is not null)
                StepCollector.CompleteStep(testId, passed: false);
            throw;
""",
        """            StepCollector.CompleteFrame(testId, frame, passed: false);
            throw;
""",
    ),
    (
        """            if (testId is not null && StepCollector.HasActiveStep(testId))
                StepCollector.CompleteStep(testId, passed: true);
""",
        """            StepCollector.CompleteFrame(testId, frame, passed: true);
""",
    ),
)

edit(
    "src/Kronikol.BDDfy.xUnit3/ScenarioInfoExtensions.cs",
    (
        """                                    ? MapSteps(x.Steps, StepCollector.GetSteps(x.TestId))
""",
        """                                    ? MapSteps(x.Steps, StepCollector.GetSteps(x.TestId), x.TestId)
""",
    ),
    (
        """    private static ScenarioStep[] MapSteps(List<BDDfyStepInfo> steps, ScenarioStep[]? collectedSteps = null)
    {
        var mapped = new ScenarioStep[steps.Count];
        var priorFailure = false;
        for (var i = 0; i < steps.Count; i++)
        {
            mapped[i] = new ScenarioStep
            {
                Keyword = steps[i].Keyword,
                Text = steps[i].Text,
                Status = steps[i].Result.ToStepResult(priorFailure),
                Duration = steps[i].Duration,
                SubSteps = collectedSteps is not null && i < collectedSteps.Length
                    ? collectedSteps[i].SubSteps
                    : null,
                Attachments = collectedSteps is not null && i < collectedSteps.Length
                    ? collectedSteps[i].Attachments
                    : null,
            };
            if (steps[i].Result == TestStack.BDDfy.Result.Failed)
                priorFailure = true;
        }
        return mapped;
    }
""",
        """    private static ScenarioStep[] MapSteps(List<BDDfyStepInfo> steps, ScenarioStep[] collectedSteps, string testId)
    {
        var priorFailure = false;
        return StepPairing.InOrderTheyRan(steps, collectedSteps, s => BDDfyStepTrackingExecutor.FrameOf(testId, s.StepId), (step, collected) =>
        {
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
""",
    ),
)

# LightBDD
edit(
    "src/Kronikol.LightBDD.Core/StepTrackingStepDecorator.cs",
    (
        """internal sealed class StepTrackingStepDecorator : IStepDecorator
{
""",
        """internal sealed class StepTrackingStepDecorator : IStepDecorator
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> Frames = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> LastEnded = new();

    /// <summary>The frame opened for a LightBDD step, if it ran.</summary>
    internal static int? FrameOf(Guid runtimeId) => Frames.TryGetValue(runtimeId, out var frame) ? frame : null;

    /// <summary>When a step of the scenario last ended, by the clock calls are stamped with.</summary>
    internal static DateTimeOffset? LastStepEnded(string testId) => LastEnded.TryGetValue(testId, out var at) ? at : null;

""",
    ),
    (
        """        if (testId is not null)
            StepCollector.StartStep(testId, keyword, text, null, null);
""",
        """        var frame = StepCollector.StartFrame(testId, keyword, text);
        if (frame is { } opened)
            Frames[step.Info.RuntimeId] = opened;
""",
    ),
    (
        """            if (testId is not null)
                StepCollector.CompleteStep(testId, passed: false);
            throw;
""",
        """            StepCollector.CompleteFrame(testId, frame, passed: false);
            throw;
""",
    ),
    (
        """            if (testId is not null && StepCollector.HasActiveStep(testId))
                StepCollector.CompleteStep(testId, passed: true);
""",
        """            StepCollector.CompleteFrame(testId, frame, passed: true);
            if (testId is not null)
                LastEnded[testId] = DateTimeOffset.UtcNow;
""",
    ),
)

edit(
    "src/Kronikol.LightBDD.Core/FeatureResultExtensions.cs",
    (
        """            EndedAt = result.ExecutionTime?.End,
""",
        """            // LightBDD's own timer can read earlier than the clock the scenario's calls are stamped with
            EndedAt = StepTrackingStepDecorator.LastStepEnded(result.Info.RuntimeId.ToString()) is { } lastStep
                      && (result.ExecutionTime?.End is not { } end || lastStep > end)
                ? lastStep
                : result.ExecutionTime?.End,
""",
    ),
    (
        """        var stepArray = steps.ToArray();
        if (stepArray.Length == 0) return null;
        var mapped = new ScenarioStep[stepArray.Length];
        var priorFailure = false;
        for (var i = 0; i < stepArray.Length; i++)
        {
            var collected = collectedSteps is not null && i < collectedSteps.Length
                ? collectedSteps[i] : null;
            mapped[i] = MapStep(stepArray[i], priorFailure, scenarioSkipped, collected);
            if (stepArray[i].Status == ExecutionStatus.Failed)
                priorFailure = true;
        }
        return mapped;
""",
        """        var stepArray = steps.ToArray();
        if (stepArray.Length == 0) return null;
        var priorFailure = false;
        return StepPairing.InOrderTheyRan(stepArray, collectedSteps ?? [], s => StepTrackingStepDecorator.FrameOf(s.Info.RuntimeId), (step, collected) =>
        {
            var mapped = MapStep(step, priorFailure, scenarioSkipped, collected);
            if (step.Status == ExecutionStatus.Failed)
                priorFailure = true;
            return mapped;
        }).ToArray();
""",
    ),
    (
        """        var nativeSubSteps = MapSteps(step.GetSubSteps(), scenarioSkipped, collectedSubs);
        ScenarioStep[]? subSteps;
        if (nativeSubSteps is not null && collectedSubs is not null && collectedSubs.Length > nativeSubSteps.Length)
            subSteps = [..nativeSubSteps, ..collectedSubs[nativeSubSteps.Length..]];
        else
            subSteps = nativeSubSteps ?? collectedSubs;
""",
        """        var subSteps = MapSteps(step.GetSubSteps(), scenarioSkipped, collectedSubs) ?? collectedSubs;
""",
    ),
    (
        """        return new ScenarioStep
        {
            Keyword = keyword,
            Text = text,
            Status = MapStepStatus(step.Status, priorFailure, scenarioSkipped),""",
        """        return new ScenarioStep
        {
            CollectedId = collected?.CollectedId,
            Keyword = keyword,
            Text = text,
            Status = MapStepStatus(step.Status, priorFailure, scenarioSkipped),""",
    ),
)

edit(
    "src/Kronikol/Tracking/TestTrackingMessageHandler.cs",
    (
        """        var currentStepType = _currentStepTypeFetcher();
        if (currentStepType is null)""",
        """        // A fetcher may throw where it has no step to read, as LightBDD's does outside a step, in a scenario decorator. A
        // call made there is still a call and must not fail the test, as a throwing test identity fetcher does not.
        string? currentStepType;
        try
        {
            currentStepType = _currentStepTypeFetcher();
        }
        catch
        {
            currentStepType = null;
        }

        if (currentStepType is null)""",
    ),
)
print("applied")
