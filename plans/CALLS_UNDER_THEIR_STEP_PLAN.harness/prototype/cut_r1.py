"""Cuts the final cut back to what R1 ships: the flow, the stamp's reading and the join, with a stamp resolved to the top-level step.

Usage: cut_r1.py <worktree holding the final cut>
This is the one line §6.4 says lies between R1 and R3. The readers' changes are left in: they read deep paths and flat paths the same.
"""
import pathlib
import sys

p = pathlib.Path(sys.argv[1]) / "src/Kronikol/Reports/ReportGenerator.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
old = """            var index = 0;
            foreach (var sub in step.SubSteps ?? [])
                Add(sub, $"{path}.{index++}");
"""
new = """            foreach (var sub in step.SubSteps ?? [])
                Add(sub, path);
"""
assert s.count(old) == 1
p.write_bytes((s.replace(old, new).replace("\n", "\r\n") if crlf else s.replace(old, new)).encode("utf-8"))
print("a stamp resolves to the top-level step")
