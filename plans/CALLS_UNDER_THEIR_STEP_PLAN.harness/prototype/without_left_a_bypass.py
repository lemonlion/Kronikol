"""What whichever of R1 and #226's weave lands second does: with no pass-through, nothing should take a bypass's completion, and the bypass moves the flow itself.

Usage: without_left_a_bypass.py <worktree holding both>
"""
import pathlib
import sys

p = pathlib.Path(sys.argv[1]) / "src/Kronikol/Tracking/StepCollector.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
PAIRS = [
    ("""        if (LeftABypass())
            return;

""", ""),
    ("LeftABypass() || FlowIsInAFrame() ? task : AwaitStep(task, LeaveFlowStep());", "FlowIsInAFrame() ? task : AwaitStep(task, LeaveFlowStep());"),
    ("""        // The stub's completion comes through next, with nothing to close, so the flow stays on this step until it does (LeftABypass).
        CloseBypassed(ResolveTestId(), reason);
""",
     """        BypassStep(ResolveTestId(), reason);
"""),
]
for old, new in PAIRS:
    count = s.count(old)
    assert count >= 1, old[:80]
    s = s.replace(old, new)
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("LeftABypass is gone")
