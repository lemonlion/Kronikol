"""Writes probe/D.Perf/Generated/: 20 classes x 100 facts, each awaiting Task.Yield then making one tracked call.

D.Perf (prototype framework) compiles them in place; D.PerfA (Kronikol's own framework) links them.
Idempotent: rewrites the same 20 files. Usage: python tools/gen_perf.py
"""
import pathlib

CLASSES, FACTS = 20, 100
root = pathlib.Path(__file__).resolve().parent.parent / "probe" / "D.Perf" / "Generated"
root.mkdir(parents=True, exist_ok=True)
for c in range(1, CLASSES + 1):
    lines = ["using Probe;", "using Xunit;", "", "namespace Perf;", "", f"public class PerfClass{c:02d}", "{"]
    for f in range(1, FACTS + 1):
        lines.append(f'    [Fact] public async Task F{f:03d}() {{ await Task.Yield(); await ProbeHttp.GetAsync("/perf/{c:02d}/{f:03d}"); }}')
    lines += ["}", ""]
    (root / f"PerfClass{c:02d}.cs").write_text("\n".join(lines), encoding="utf-8", newline="\n")
print(f"wrote {CLASSES} classes x {FACTS} facts to {root}")
