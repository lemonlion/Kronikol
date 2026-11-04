"""The second half of apply_with_224.py: #224's record shares its name with this plan's AsyncLocal, so the merge renames the record.

Usage: apply_with_224_rename.py <worktree>
"""
import pathlib
import sys

p = pathlib.Path(sys.argv[1]) / "src/Kronikol/Tracking/StepCollector.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
PAIRS = [
    ("private static readonly AsyncLocal<FlowStep?> CurrentFlowStep = new();", "private static readonly AsyncLocal<FlowLink?> CurrentFlowStep = new();"),
    ("private sealed record FlowStep(string TestId, CollectedStep Step, FlowStep? Parent);", "private sealed record FlowLink(string TestId, CollectedStep Step, FlowLink? Parent);"),
    ("private static FlowStep? OpenFlowStep(string testId)", "private static FlowLink? OpenFlowStep(string testId)"),
    ("CurrentFlowStep.Value = new FlowStep(testId, step, OpenFlowStep(testId));", "CurrentFlowStep.Value = new FlowLink(testId, step, OpenFlowStep(testId));"),
]
for old, new in PAIRS:
    assert s.count(old) == 1, old
    s = s.replace(old, new)
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("renamed")

# git's 3-way merge left OpenStep's return at the end of #224's NotePhase.
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
old = """            state.ActionStartDrawn |= phase == TestPhase.Action;
        }

        return step.Ordinal;
    }
"""
new = """            state.ActionStartDrawn |= phase == TestPhase.Action;
        }
    }
"""
assert s.count(old) == 1
p.write_bytes((s.replace(old, new).replace("\n", "\r\n") if crlf else s.replace(old, new)).encode("utf-8"))
print("return moved")
