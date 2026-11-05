"""R8: two faults the claim audit found. python -I apply_r8.py <checkout>

A hand-built Reqnroll step paired by position carried no id, so a call stamped with that step read no step, unlike BDDfy and LightBDD. And a
bypass finding a frame on top popped it, against "only CompleteFrame closes a frame".
"""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])


def edit(path, old, new):
    p = root / path
    t = p.read_bytes().decode("utf-8")
    newline = "\r\n" if "\r\n" in t else "\n"
    t = t.replace("\r\n", "\n")
    assert t.count(old) == 1, old[:70]
    p.write_bytes(t.replace(old, new).replace("\n", newline).encode("utf-8"))


edit("src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
     """            mapped[i] = new ScenarioStep
            {
                Keyword = steps[i].Keyword,
""",
     """            mapped[i] = new ScenarioStep
            {
                CollectedId = collectedSteps is not null && i < collectedSteps.Length ? collectedSteps[i].CollectedId : null,
                Keyword = steps[i].Keyword,
""")
edit("src/Kronikol/Tracking/StepCollector.cs",
     """            if (state.StepStack.Count == 0)
                return;

            var bypassed = state.StepStack.Pop();
""",
     """            // Only CompleteFrame closes a frame: a second bypass of a step already gone must not take the one under it
            if (state.StepStack.Count == 0 || state.StepStack.Peek().IsFrame)
                return;

            var bypassed = state.StepStack.Pop();
""")
print("ok")
