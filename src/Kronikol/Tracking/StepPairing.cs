using Kronikol.Reports;

namespace Kronikol.Tracking;

/// <summary>
/// Puts a framework's own steps together with what <see cref="StepCollector"/> collected, by the frame each opened: what ran,
/// in the order it ran. A collected step no framework step opened (a step method outside one) stands as itself, and a frame whose
/// framework step is no longer listed (a consumer removed it from the list) lists no row; a framework
/// step with no frame did not run, and stands where the framework lists it, after the last step that did, or by
/// the id of the step opened before it, where the adapter knows that. A framework step whose
/// frame opened inside another step is listed there, as that step's sub-step, with what the framework knows about it (its
/// arguments, its outcome).
/// </summary>
internal static class StepPairing
{
    public static List<ScenarioStep> InOrderTheyRan<T>(IReadOnlyList<T> natives, ScenarioStep[] collected, Func<T, int?> frameOf,
        Func<T, ScenarioStep?, ScenarioStep> map, Func<T, int?>? notRunAfter = null)
    {
        var indexOfFrame = new Dictionary<int, int>();
        for (var i = 0; i < natives.Count; i++)
            if (frameOf(natives[i]) is { } frame)
                indexOfFrame.TryAdd(frame, i);

        bool Named(ScenarioStep step) => step.CollectedId is { } id && indexOfFrame.ContainsKey(id);

        IEnumerable<ScenarioStep> NamedInside(ScenarioStep step) => (step.SubSteps ?? []).SelectMany(sub => Named(sub) ? [sub] : NamedInside(sub));

        // A frame no framework step names lists no row, so the frames the list does name inside it (a BDDfy story run in a step of another) stand where it stood.
        collected = collected.SelectMany(c => c.OpenedAsFrame && !Named(c) && NamedInside(c).Any() ? NamedInside(c) : [c]).ToArray();

        // A framework that lists its steps by kind, as BDDfy does, says nothing by that order about where what did not run belongs: it goes after the last step that ran.
        // Its list shows as either the steps that ran out of order, or a step that did not run between two that did.
        var framed = collected.Where(Named).Select(c => indexOfFrame[c.CollectedId!.Value]).ToArray();
        var inRunOrder = framed.Zip(framed.Skip(1)).All(pair => pair.First < pair.Second)
            && !natives.Where((_, i) => framed.Length > 0 && i > framed[0] && i < framed[^1]).Any(n => frameOf(n) is null && notRunAfter?.Invoke(n) is null);

        bool ListedInside(T native) => frameOf(native) is { } step && InATree(collected, step);

        var lastFramed = Array.FindLastIndex(collected, c => c.CollectedId is { } id && indexOfFrame.ContainsKey(id));
        var merged = new List<ScenarioStep>();
        var next = 0;

        void Through(int end)
        {
            for (; next < end; next++)
                if (!ListedInside(natives[next]))
                    merged.Add(map(natives[next], null));
        }

        for (var k = 0; k < collected.Length; k++)
        {
            while (next < natives.Count && frameOf(natives[next]) is null && notRunAfter?.Invoke(natives[next]) is { } after && collected[k].CollectedId > after)
                Through(next + 1);

            if (collected[k].CollectedId is { } id && indexOfFrame.TryGetValue(id, out var native))
            {
                if (inRunOrder)
                {
                    Through(native);
                    next = native + 1;
                }
                merged.Add(map(natives[native], WithNested(collected[k], natives, indexOfFrame, map)));
            }
            else if (!collected[k].OpenedAsFrame)
            {
                merged.Add(WithNested(collected[k], natives, indexOfFrame, map) with { OutsideFrameworkStep = true });
            }

            // What did not run with no id to go by stands after the last step that did. What has one waits for it.
            if (k == lastFramed)
                while (next < natives.Count && notRunAfter?.Invoke(natives[next]) is null)
                    Through(next + 1);
        }

        Through(natives.Count);
        return merged;
    }

    /// <summary>Whether a frame opened inside another step, so what was collected holds it under that step.</summary>
    public static bool OpenedInsideAStep(ScenarioStep[] collected, int id) => collected.Any(c => InATree(c.SubSteps, id));

    /// <summary>A step, each framework step among its sub-steps with what the framework knows about it.</summary>
    public static ScenarioStep WithNested<T>(ScenarioStep step, IReadOnlyList<T> natives, Func<T, int?> frameOf, Func<T, ScenarioStep?, ScenarioStep> map)
    {
        var indexOfFrame = new Dictionary<int, int>();
        for (var i = 0; i < natives.Count; i++)
            if (frameOf(natives[i]) is { } frame)
                indexOfFrame.TryAdd(frame, i);

        return WithNested(step, natives, indexOfFrame, map);
    }

    private static ScenarioStep WithNested<T>(ScenarioStep step, IReadOnlyList<T> natives, Dictionary<int, int> indexOfFrame, Func<T, ScenarioStep?, ScenarioStep> map)
    {
        if (step.SubSteps is not { Length: > 0 })
            return step;

        var subSteps = new List<ScenarioStep>();
        foreach (var sub in step.SubSteps)
        {
            var nested = WithNested(sub, natives, indexOfFrame, map);
            subSteps.Add(sub.CollectedId is { } id && indexOfFrame.TryGetValue(id, out var native) ? map(natives[native], nested) : nested);
        }
        return step with { SubSteps = subSteps.ToArray() };
    }

    private static bool InATree(ScenarioStep[]? steps, int id) =>
        steps is not null && steps.Any(s => s.CollectedId == id || InATree(s.SubSteps, id));
}
