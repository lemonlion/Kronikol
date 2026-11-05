"""R1 over 5.29.0's weave, which no longer runs a stub's completion after the stub's own bypass: the flow leaves a bypassed step at once, and the
one stub still to come is that of a woven step that bypassed itself by its id.

Usage: r1_on_529.py <worktree>
"""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])


def edit(path, reps):
    p = root / path
    raw = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in raw
    s = raw.replace("\r\n", "\n")
    for old, new in reps:
        assert s.count(old) == 1, (path, old[:90], s.count(old))
        s = s.replace(old, new)
    p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))


edit("src/Kronikol/Tracking/StepCollector.cs", [
    ("""        CloseBypassed(ResolveTestId(), reason);
    }
""", """        if (CloseBypassed(ResolveTestId(), reason) is { } bypassed && ReferenceEquals(FlowStep.Value, bypassed))
            FlowStep.Value = FlowAfter(bypassed);
    }
"""),
    ("""        // A woven step is still in its stub, whose completion takes the flow up (LeftABypass). A hand has no stub to do it.
        if (CloseBypassed(testId, reason) is { ByHand: true } bypassed && ReferenceEquals(FlowStep.Value, bypassed))
            FlowStep.Value = FlowAfter(bypassed);
""", """        if (CloseBypassed(testId, reason) is not { } bypassed || !ReferenceEquals(FlowStep.Value, bypassed))
            return;

        // A woven step that bypasses itself by its id is still in its stub, whose completion must close nothing. A hand has no stub.
        if (bypassed.ByHand)
            FlowStep.Value = FlowAfter(bypassed);
        else
            bypassed.StubStillToComplete = true;
"""),
    ("""    // The woven stub of a bypassed step, on its way out: the step has closed already, and a completion would close the one above it or one running beside it.
    private static bool LeftABypass()
    {
        if (FlowStep.Value is not { Bypassed: true } bypassed)
            return false;

        FlowStep.Value = FlowAfter(bypassed);
        return true;
""", """    // The stub of a woven step that bypassed itself by its id, on its way out: the step has closed already, and a completion would close the one
    // above it or one running beside it. A stub's own bypass skips its completion, as the weave returns past it.
    private static bool LeftABypass()
    {
        if (FlowStep.Value is not { StubStillToComplete: true } bypassed)
            return false;

        FlowStep.Value = FlowAfter(bypassed);
        return true;
"""),
    ("""        public bool ByHand { get; set; }
""", """        public bool ByHand { get; set; }
        public bool StubStillToComplete { get; set; }
"""),
])

print("ok")
