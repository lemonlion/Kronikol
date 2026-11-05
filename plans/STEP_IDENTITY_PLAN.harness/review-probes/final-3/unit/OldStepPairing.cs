using Kronikol.Reports;

namespace ReviewProbes;

/// <summary>
/// Puts a framework's own steps together with what <see cref="StepCollector"/> collected, by the frame each opened: what ran,
/// in the order it ran. A collected step no framework step opened (a step method outside one) stands as itself, and a frame whose
/// framework step is no longer listed (a consumer removed it from the list) lists no row; a framework
/// step with no frame did not run, and stands where the framework lists it, after the last step that did, or by
/// <paramref name="notRunAfter"/>, the id of the step opened before it, where the adapter knows that. A framework step whose
/// frame opened inside another step is listed there, as that step's sub-step, with what the framework knows about it (its
/// arguments, its outcome).
/// </summary>
internal static class OldStepPairing
{
    public static List<ScenarioStep> InOrderTheyRan<T>(IReadOnlyList<T> natives, ScenarioStep[] collected, Func<T, int?> frameOf,
        Func<T, ScenarioStep?, ScenarioStep> map, Func<T, int?>? notRunAfter = null)
    {
        var indexOfFrame = new Dictionary<int, int>();
        for (var i = 0; i < natives.Count; i++)
            if (frameOf(natives[i]) is { } frame)
                indexOfFrame.TryAdd(frame, i);

        ScenarioStep WithNested(ScenarioStep step)
        {
            if (step.SubSteps is not { Length: > 0 })
                return step;

            var subSteps = new List<ScenarioStep>();
            foreach (var sub in step.SubSteps)
            {
                var nested = WithNested(sub);
                subSteps.Add(sub.CollectedId is { } id && indexOfFrame.TryGetValue(id, out var native) ? map(natives[native], nested) : nested);
            }
            return step with { SubSteps = subSteps.ToArray() };
        }

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
                Through(native);
                merged.Add(map(natives[native], WithNested(collected[k])));
                next = native + 1;
            }
            else if (!collected[k].OpenedAsFrame)
            {
                merged.Add(WithNested(collected[k]) with { OutsideFrameworkStep = true });
            }

            if (k == lastFramed)
                Through(natives.Count);
        }

        Through(natives.Count);
        return merged;
    }

    private static bool InATree(ScenarioStep[]? steps, int id) =>
        steps is not null && steps.Any(s => s.CollectedId == id || InATree(s.SubSteps, id));
}
