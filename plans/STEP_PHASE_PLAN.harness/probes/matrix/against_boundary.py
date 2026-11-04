"""Lists every recorded phase that disagrees with where its call is drawn, for one variant.

    python -I against_boundary.py <results folder> <variant>

A call drawn before a test's first Setup/Action marker should not read Action, and one after it should not read Setup or
Unknown. This needs no key: it is the report contradicting itself, which plans/STEP_PHASE_PLAN.md section 2.3 lists as a cost.
Tests with no marker are skipped, and so are the tests whose steps go back to Setup on purpose (a [ButStep] or a second Given
after the boundary), which are named in the output with what they hold.
"""
import pathlib
import re
import sys

PROBES = ["reqnroll-xunit2", "xunit3-plain", "lightbdd-xunit2", "bddfy-xunit3"]
ROW = re.compile(r"^(?P<label>\S+)\s+(?P<http>Setup|Action|Unknown|-)\s+(?P<msg>Setup|Action|Unknown|-)\s*(?:IGNORED)?\s*$")


def main():
    root, variant = pathlib.Path(sys.argv[1]), sys.argv[2]
    total = 0
    for probe in PROBES:
        name, seen_marker, in_steps = None, False, False
        for line in (root / variant / f"{probe}.txt").read_text(encoding="utf-8").splitlines():
            if line.startswith("## "):
                name, seen_marker, in_steps = line[3:].rsplit(".", 1)[-1], False, False
            elif line.startswith("steps:"):
                in_steps = True
            elif in_steps or name is None:
                continue
            elif "ACTION START MARKER" in line:
                seen_marker = True
            else:
                m = ROW.match(line)
                if not m:
                    continue
                for column in ("http", "msg"):
                    value = m[column]
                    if value == "-":
                        continue
                    wrong = (seen_marker and value != "Action") or (not seen_marker and value == "Action" and marker_follows(root, variant, probe, name))
                    if wrong:
                        total += 1
                        print(f"{probe:<18} {name[:44]:<44} {m['label']:<30} {column:<5} {value:<8} {'after' if seen_marker else 'before'} the marker")
    print(f"{total} recorded phases disagree with where their call is drawn")


def marker_follows(root, variant, probe, name):
    text = (root / variant / f"{probe}.txt").read_text(encoding="utf-8")
    start = text.find(f".{name}\n") if f".{name}\n" in text else text.find(f"## {name}")
    end = text.find("\n## ", start + 1)
    return "ACTION START MARKER" in text[start:end if end > 0 else None]


main()
