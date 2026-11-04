"""Fifth cut, on the fourth, from the reviewer's plain probes: a straggler's new step goes under whatever is running now."""
import pathlib
import sys

p = pathlib.Path(sys.argv[1]) / "src/Kronikol/Tracking/StepCollector.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
PAIRS = [
    ("""            var parent = NearestOpenInFlow(state) ?? LastOpenInItsCaller(state);
            step.Parent = parent;
""", """            var parent = ParentOfNewStep(state);
            step.Parent = parent;
"""),
    ("""    /// <summary>The step an assertion or an attachment belongs to. Call under the lock.</summary>
""", """    /// <summary>
    /// The parent of a step starting now. A flow inside an open step: that step. A flow whose steps have all ended is a straggler, and
    /// goes under whatever is running now, as a stack had it. A flow that was never in one, or has left them, passes over a step left
    /// running. Call under the lock.
    /// </summary>
    private static CollectedStep? ParentOfNewStep(TestStepState state)
    {
        if (NearestOpenInFlow(state) is { } own)
            return own;

        var straggler = FlowStep.Value is { } flow && ReferenceEquals(flow.State, state);
        return straggler ? (state.Open.Count > 0 ? state.Open[^1] : null) : LastOpenInItsCaller(state);
    }

    /// <summary>The step an assertion or an attachment belongs to. Call under the lock.</summary>
"""),
]
for old, new in PAIRS:
    assert s.count(old) == 1, old[:80]
    s = s.replace(old, new)
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("fifth cut applied")
