"""Scores each variant against the rule of plans/STEP_PHASE_PLAN.md section 5.1.

    python -I score.py <results folder> <variant holding the rule's answers> <variant> [<variant> ...]

A cell is one recorded phase: the HTTP call's or the message's at one labelled point of one test. The answers are the
reference variant's, which were read line by line against the rule before this was run. Also counts the tests whose diagram
has a Setup/Action boundary, and the Setup traces that sit in a test with none.
"""
import pathlib
import re
import sys

PROBES = ["reqnroll-xunit2", "xunit3-plain", "lightbdd-xunit2", "bddfy-xunit3"]
ROW = re.compile(r"^(?P<label>\S+)\s+(?P<http>Setup|Action|Unknown|-)\s+(?P<msg>Setup|Action|Unknown|-)\s*(?P<ignored>IGNORED)?\s*$")


def read(path):
    tests, current = {}, None
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("## "):
            current = line[3:].rsplit(".", 1)[-1]
            tests[current] = {"rows": {}, "marker": 0}
        elif current is None or line.startswith("steps:"):
            current = None if line.startswith("steps:") else current
        elif "ACTION START MARKER" in line:
            tests[current]["marker"] += 1
        else:
            m = ROW.match(line)
            if m:
                tests[current]["rows"][m["label"]] = (m["http"], m["msg"], m["ignored"] or "")
    return tests


def main():
    root, reference, variants = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3:]
    print(f"{'variant':<12}" + "".join(f"{p:>18}" for p in PROBES) + f"{'all':>10}  (cells that differ from the rule's answer, of the total)")
    answers = {p: read(root / reference / f"{p}.txt") for p in PROBES}
    for variant in variants:
        cells, wrongs = [], []
        for probe in PROBES:
            got = read(root / variant / f"{probe}.txt")
            total = wrong = 0
            for test, want in answers[probe].items():
                have = got.get(test, {"rows": {}})["rows"]
                for label, (http, msg, ignored) in want["rows"].items():
                    h = have.get(label, ("-", "-", ""))
                    for a, b in ((http, h[0]), (msg, h[1])):
                        if a == "-" and b == "-":
                            continue
                        total += 1
                        wrong += a != b
            cells.append(f"{wrong}/{total}")
            wrongs.append((wrong, total))
        print(f"{variant:<12}" + "".join(f"{c:>18}" for c in cells) + f"{sum(w for w, _ in wrongs)}/{sum(t for _, t in wrongs):>6}".rjust(10))
    print()
    print(f"{'variant':<12}" + "".join(f"{p:>18}" for p in PROBES) + f"{'all':>10}  (cells right at {variants[0]} and wrong here: what the variant breaks)")
    baseline = {p: read(root / variants[0] / f"{p}.txt") for p in PROBES}
    broken_cells = {}
    for variant in variants[1:]:
        counts = []
        for probe in PROBES:
            got = read(root / variant / f"{probe}.txt")
            broken = 0
            for test, want in answers[probe].items():
                have = got.get(test, {"rows": {}})["rows"]
                was = baseline[probe].get(test, {"rows": {}})["rows"]
                for label, (http, msg, _) in want["rows"].items():
                    h, b = have.get(label, ("-", "-", "")), was.get(label, ("-", "-", ""))
                    for column, (a, now, before) in enumerate(((http, h[0], b[0]), (msg, h[1], b[1]))):
                        if before == a and now != a:
                            broken += 1
                            broken_cells.setdefault(variant, []).append(f"{label}/{'http' if column == 0 else 'message'}")
            counts.append(broken)
        print(f"{variant:<12}" + "".join(f"{c:>18}" for c in counts) + f"{sum(counts):>10}")
    for variant, cells in broken_cells.items():
        print(f"  {variant} breaks: " + ", ".join(cells))
    print()
    print(f"{'variant':<12}" + "".join(f"{p:>18}" for p in PROBES) + "  (tests with a Setup/Action boundary, of the tests)")
    for variant in variants:
        row = ""
        for probe in PROBES:
            got = read(root / variant / f"{probe}.txt")
            row += f"{sum(1 for t in got.values() if t['marker'])}/{len(got)}".rjust(18)
        print(f"{variant:<12}{row}")


main()
