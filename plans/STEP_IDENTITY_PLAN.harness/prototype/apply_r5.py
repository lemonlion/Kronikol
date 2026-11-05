"""The third cut of plans/STEP_IDENTITY_PLAN.md, on top of apply_r1.py to apply_r4.py: a step run by another step (ITestRunner),
from kronikol-af's review of the same shapes on #222: its arguments, its outcome and its skipped sibling. And the mark a row no
framework step opened carries, as such a row can have a keyword. usage: python apply_r5.py <repo root>. Every replacement must
match exactly once or the script stops."""
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
    "src/Kronikol/Reports/ScenarioStep.cs",
    (
        """    /// <summary>The id <see cref="Tracking.StepCollector"/> gave the step, within its test; never written to a report.</summary>
    internal int? CollectedId { get; init; }
""",
        """    /// <summary>The id <see cref="Tracking.StepCollector"/> gave the step; never written to a report.</summary>
    internal int? CollectedId { get; init; }

    /// <summary>A row no framework step opened: a step method that ran outside one. It can have a keyword; never written to a report.</summary>
    internal bool OutsideFrameworkStep { get; init; }
""",
    ),
)

edit(
    "src/Kronikol/Reports/BackgroundStepsDetector.cs",
    (
        """    // A row with no keyword ran outside the scenario's own steps (a hook's step method), and says nothing about how they start.
    private static string? FirstKeyword(IEnumerable<ScenarioStep> steps) =>
        steps.FirstOrDefault(step => step.Keyword is not null)?.Keyword;
""",
        """    // A row no framework step opened (a hook's step method) says nothing about how the scenario's own steps start.
    private static string? FirstKeyword(IEnumerable<ScenarioStep> steps) =>
        steps.FirstOrDefault(step => !step.OutsideFrameworkStep)?.Keyword;
""",
    ),
)

edit(
    "src/Kronikol/Tracking/StepPairing.cs",
    (
        """/// <paramref name="notRunAfter"/>, the id of the step opened before it, where the adapter knows that. A framework step whose
/// frame opened inside another step is listed there, as that step's sub-step, and not here.
/// </summary>
""",
        """/// <paramref name="notRunAfter"/>, the id of the step opened before it, where the adapter knows that. A framework step whose
/// frame opened inside another step is listed there, as that step's sub-step, with what the framework knows about it (its
/// arguments, its outcome), and so is one that did not run with a step open, by <paramref name="notRunUnder"/>.
/// </summary>
""",
    ),
    (
        """        Func<T, ScenarioStep?, ScenarioStep> map, Func<T, int?>? notRunAfter = null)
    {
""",
        """        Func<T, ScenarioStep?, ScenarioStep> map, Func<T, int?>? notRunAfter = null, Func<T, int?>? notRunUnder = null)
    {
""",
    ),
    (
        """        var lastFramed = Array.FindLastIndex(collected, c => c.CollectedId is { } id && indexOfFrame.ContainsKey(id));
""",
        """        var notRunInside = notRunUnder is null
            ? null
            : natives.Where(n => notRunUnder(n) is not null).ToLookup(n => notRunUnder(n)!.Value);

        ScenarioStep WithNested(ScenarioStep step)
        {
            var notRun = step.CollectedId is { } own && notRunInside is not null ? notRunInside[own].ToArray() : [];
            if (step.SubSteps is not { Length: > 0 } && notRun.Length == 0)
                return step;

            var subSteps = new List<ScenarioStep>();
            foreach (var sub in step.SubSteps ?? [])
            {
                var nested = WithNested(sub);
                subSteps.Add(sub.CollectedId is { } id && indexOfFrame.TryGetValue(id, out var native) ? map(natives[native], nested) : nested);
            }
            subSteps.AddRange(notRun.Select(n => map(n, null)));
            return step with { SubSteps = subSteps.ToArray() };
        }

        bool ListedInside(T native) => (frameOf(native) ?? notRunUnder?.Invoke(native)) is { } step && InATree(collected, step);

        var lastFramed = Array.FindLastIndex(collected, c => c.CollectedId is { } id && indexOfFrame.ContainsKey(id));
""",
    ),
    (
        """                if (frameOf(natives[next]) is not { } frame || !InATree(collected, frame))
                    merged.Add(map(natives[next], null));
""",
        """                if (!ListedInside(natives[next]))
                    merged.Add(map(natives[next], null));
""",
    ),
    (
        """                merged.Add(map(natives[native], collected[k]));
""",
        """                merged.Add(map(natives[native], WithNested(collected[k])));
""",
    ),
    (
        """                merged.Add(collected[k]);
""",
        """                merged.Add(WithNested(collected[k]) with { OutsideFrameworkStep = true });
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollStepInfo.cs",
    (
        """    internal int? NotRunAfter { get; init; }
""",
        """    internal int? NotRunAfter { get; init; }

    /// <summary>For a step that did not run with a step open (a Gherkin step that runs another): the id of that step.</summary>
    internal int? NotRunUnder { get; init; }
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
    (
        """                    if (evt.HookType == HookType.AfterStep && evt.ScenarioContext is ScenarioContext scenario)
                        CloseStep(scenario, evt.HookException);
""",
        """                    if (evt.HookType == HookType.AfterStep && evt.ScenarioContext is ScenarioContext scenario)
                        CloseStep(scenario);
""",
    ),
    (
        """        CloseStep(_scenarioContext, hookException: null);
""",
        """        CloseStep(_scenarioContext);
""",
    ),
    (
        """    /// threw before that was reached, from the hooks' finished event. A hook's failure is the step's failure.
    /// </summary>
    private static void CloseStep(ScenarioContext scenario, Exception? hookException)
""",
        """    /// threw before that was reached, from the hooks' finished event. A frame opens only while the scenario is well, so the
    /// scenario failing by now failed in this step: a hook's failure is the step's, and so is a step's it ran, as Reqnroll
    /// writes that one to the inner step's context alone.
    /// </summary>
    private static void CloseStep(ScenarioContext scenario)
""",
    ),
    (
        """        var status = hookException is null ? stepContext.Status : scenario.ScenarioExecutionStatus;
""",
        """        var status = stepContext.Status != ScenarioExecutionStatus.OK ? stepContext.Status : scenario.ScenarioExecutionStatus;
""",
    ),
    (
        """            stepContext.StepInfo.MultilineText) { NotRunAfter = StepCollector.LastStepIdOf(scenarioId) });
""",
        """            stepContext.StepInfo.MultilineText)
        {
            NotRunAfter = StepCollector.LastStepIdOf(scenarioId),
            NotRunUnder = StepCollector.OpenStepOf(scenarioId),
        });
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
    (
        """        }, s => s.NotRunAfter);
""",
        """        }, s => s.NotRunAfter, s => s.NotRunUnder);
""",
    ),
)
print("applied")
