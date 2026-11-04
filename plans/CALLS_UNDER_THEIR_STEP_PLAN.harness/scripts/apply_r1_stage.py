"""Cuts a copy of a prototype back to one stage of the release order, to see what each stage reads on its own.

Usage: apply_r1_stage.py <copy> <stage>

alone          On the final cut: R1 with no stamp, so a call's step comes from the walk over the bars.
deepest        On the two built as one: R1 on #223's R1, with its stamp reading what its stack had on top (the step last opened and still open), at the top level.
flow           On the two built as one: R1 on #223's R1, with the stamp reading the flow, at the top level.
"""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
stage = sys.argv[2]


def edit(path, old, new):
    p = root / path
    raw = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in raw
    s = raw.replace("\r\n", "\n")
    assert s.count(old) == 1, (path, old[:80], s.count(old))
    p.write_bytes((s.replace(old, new).replace("\n", "\r\n") if crlf else s.replace(old, new)).encode("utf-8"))


TOP_LEVEL = (
    "src/Kronikol/Reports/ReportGenerator.cs",
    """            var index = 0;
            foreach (var sub in step.SubSteps ?? [])
                Add(sub, $"{path}.{index++}");
""",
    """            foreach (var sub in step.SubSteps ?? [])
                Add(sub, path);
""",
)

if stage == "alone":
    edit(
        "src/Kronikol/Tracking/RequestResponseLogger.cs",
        """        if (!log.IsDiagramMarker && log.StepOrdinal is null && StepCollector.OrdinalOfOpenStep(log.TestId) is { } step)
            log = log with { StepOrdinal = step };
""",
        "",
    )
elif stage == "deepest":
    edit(*TOP_LEVEL)
    edit(
        "src/Kronikol/Tracking/StepCollector.cs",
        "    internal static int? OpenStepOf(string? testId) => OrdinalOfOpenStep(testId);\n",
        """    internal static int? OpenStepOf(string? testId)
    {
        if (testId is null || !States.TryGetValue(testId, out var state))
            return null;

        lock (state)
            return state.Open.Count > 0 ? state.Open[^1].Ordinal : null;
    }
""",
    )
elif stage == "flow":
    edit(*TOP_LEVEL)
else:
    raise SystemExit(stage)
print(stage)
