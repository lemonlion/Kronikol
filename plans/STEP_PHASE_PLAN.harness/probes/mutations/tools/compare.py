"""Compares a mutant's dumps with every baseline dump of the same suite, test by test, and writes results/<id>/diff.txt.

solid: the test's output is new, its content (not only its order) differs, and it never varied over the baselines.
order: the same lines in a new order, in a test that never varied. Two flows' rows can interleave in a way the baselines did not catch, so this alone is weak.
racing-new: the test varies by itself, and this output has a shape no baseline showed (polls' counts and order left out).
racing-seen: the test varies by itself, and the shape is one a baseline showed. Proves nothing.

Usage: compare.py <id> [<id> ...]   (prints each mutant's verdict, rewrites its diff.txt)
"""
import collections
import difflib
import glob
import os
import re
import sys

import dumps
from dumps import MUT

RESULTS = os.path.join(MUT, "results")
GUID = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
# A test id in the TestName field: xUnit's hash of the test, which moves with the assembly's folder. That it is an id and not a name is the output.
TEST_ID = re.compile(r"[0-9a-f]{64}")
LOOP_TIME = re.compile(r"^(\s*loop .*?)\s*·\s*[\d.,–-]+\s*(ms|s)\b.*$")
SKIP_FILES = (".trace.txt",)


def clean(line):
    line = GUID.sub("<guid>", line)
    line = TEST_ID.sub("<test id>", line)
    match = LOOP_TIME.match(line)
    if match:
        line = match.group(1)
    return line


def read(path):
    tests = dumps.load(path)
    if not tests:
        tests = collections.OrderedDict([("(whole file)", dumps.load_flat(path))])
    return collections.OrderedDict((name, [clean(l) for l in lines]) for name, lines in tests.items())


BASELINE = {"root": "n00"}


def baseline_dirs(family, suite):
    """The cut's own unmutated rounds: n00 for cut 10, m00 for cut 9. The cut 8 baselines do not apply."""
    return [], sorted(glob.glob(os.path.join(RESULTS, BASELINE["root"], "*", f"{family}-{suite}")))


POLL = re.compile(r"poll", re.I)
ROW2 = re.compile(r"^\s*(\S+)\s+(http|msg)\s+(\w+)\s*(IGNORED)?\s*$")
ROW1 = re.compile(r"^(\S+)\s+(Setup|Action|Unknown|-)\s+(Setup|Action|Unknown|-)\s*(IGNORED)?\s*$")
ROW3 = re.compile(r"^\s*(Request|Response)\s+(\S+)\s+phase=(\w+)\s+src=\S*(?:\s+\+variants)?\s*(IGNORED)?\s*$")


def shape(lines, name=""):
    """A racing test's outcome, as racy_scan.py reads it: the phase of every call that is no poll, and which of those the boundary stands before.
    The bars, the step tree and the drawn diagram follow when each step happened to run, so a racing test's diagram says nothing here."""
    if ".plantuml." in name:
        return None
    calls, marker, waiting = [], "no marker", False
    for line in lines:
        if line.strip() == "steps:":
            break
        if "MARKER ---" in line:
            waiting, marker = True, "marker before ?"
            continue
        skeleton = ROW3.match(line)
        if skeleton:
            if POLL.search(skeleton.group(2)):
                continue
            if waiting:
                marker, waiting = f"marker before {skeleton.group(2)}", False
            calls.append(" ".join(g for g in skeleton.groups() if g))
            continue
        two, one = ROW2.match(line), ROW1.match(line)
        if not two and not one:
            continue
        label = (two or one).group(1)
        if POLL.search(label):
            continue
        if waiting:
            marker, waiting = f"marker before {label}", False
        calls.append(" ".join(g for g in (two or one).groups() if g))
    return marker, tuple(sorted(set(calls)))


class Verdict:
    def __init__(self):
        self.solid = 0
        self.order = 0
        self.first_order = None
        self.racing_new = 0
        self.racing_seen = 0
        self.first = "-"
        self.first_racing = None

    @property
    def word(self):
        if self.solid:
            return "KILLED"
        if self.order:
            return "ORDER-ONLY"
        if self.racing_new:
            return "RACING-ONLY"
        return "SURVIVED"


def changed_rows(old, new):
    out = []
    for index, line in enumerate(difflib.unified_diff(old, new, lineterm="", n=0)):
        # By place, not by text: the boundary's own row reads '--- ACTION START MARKER ---'.
        if index < 2 or line.startswith("@@"):
            continue
        out.append(re.sub(r"\s+", " ", line).strip()[:150])
    return out


def describe(old, new):
    rows = changed_rows(old, new)
    minus = [r[1:].strip() for r in rows if r.startswith("-")]
    plus = [r[1:].strip() for r in rows if r.startswith("+")]
    moves, used = [], set()
    for line in minus:
        parts = line.split()
        same = next((p for p in plus if p.split()[:1] == parts[:1] and p not in used), None)
        if same is not None:
            used.add(same)
            moves.append(f"{parts[0]}: {' '.join(parts[1:])} > {' '.join(same.split()[1:])}")
        else:
            moves.append(f"gone: {line}")
    moves += [f"new: {p}" for p in plus if p not in used]
    return "; ".join(moves)[:400]


COUNTS = re.compile(r"Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+), Total:\s+(\d+)")
TUNIT_COUNT = re.compile(r"^(total|failed|succeeded|skipped): (\d+)")


# Tests that move with no mutation although the unmutated rounds happened to agree: X09 moved under 35 of 48 mutants of cut 9, BDDfy-only ones among them, and on cut 8's baselines;
# Z02's rows raced on cut 8's baselines and under mutants that cannot reach them. They are read by their shape.
KNOWN_RACING = {"X09_a_step_left_open_then_a_When", "Z02_async_Given_leaves_a_worker_running"}


_RACING_CACHE = {}


def racing_on_cut9(family, suite, name):
    """Tests that varied over cut 9's unmutated rounds: on cut 10, with fewer rounds, they still count as racing."""
    older = {"n00": ("m00",), "p00": ("m00", "n00")}.get(BASELINE["root"], ())
    if not older:
        return ()
    key = (family, suite, name)
    if key not in _RACING_CACHE:
        varied = set()
        for root in older:
            seen = collections.defaultdict(set)
            for directory in glob.glob(os.path.join(RESULTS, root, "*", f"{family}-{suite}")):
                path = os.path.join(directory, name)
                if os.path.exists(path):
                    for test, lines in read(path).items():
                        seen[test].add(tuple(lines))
            varied |= {test for test, outputs in seen.items() if len(outputs) > 1}
        _RACING_CACHE[key] = varied
    return _RACING_CACHE[key]


def outcome_key(lines):
    """The exit code and the test counts: a build's warnings and a run's duration are not the outcome."""
    key = [line for line in lines if line.startswith("exit=")]
    for line in lines:
        counts = COUNTS.search(line)
        if counts:
            key.append(counts.groups())
        tunit = TUNIT_COUNT.match(line.strip())
        if tunit:
            key.append(tunit.groups())
    return tuple(key)


def compare_suite(family, suite, folder, verdict, report):
    fixed, controls = baseline_dirs(family, suite)
    outcome_path = os.path.join(folder, "outcome.txt")
    outcome = open(outcome_path, encoding="utf-8").read().splitlines() if os.path.exists(outcome_path) else []
    base_outcomes = set()
    for directory in controls:
        path = os.path.join(directory, "outcome.txt")
        if os.path.exists(path):
            base_outcomes.add(outcome_key(open(path, encoding="utf-8").read().splitlines()))
    if base_outcomes and outcome_key(outcome) not in base_outcomes:
        report.append(f"[{family}/{suite}] RUN OUTCOME CHANGED: {' | '.join(outcome)[:300]}")
        verdict.solid += 1
        if verdict.first == "-":
            verdict.first = f"{family}/{suite}: run outcome: {' | '.join(outcome)[:140]}"
    files = [f for f in sorted(os.listdir(folder)) if f.endswith(".txt") and f not in ("outcome.txt",) and not f.endswith(SKIP_FILES)]
    all_dirs = fixed + controls
    expected = set()
    for directory in controls:
        expected |= {f for f in os.listdir(directory) if f.endswith(".txt") and f != "outcome.txt" and not f.endswith(SKIP_FILES)}
    for missing in sorted(expected - set(files)):
        report.append(f"[{family}/{suite}] DUMP MISSING: {missing}")
        verdict.solid += 1
        if verdict.first == "-":
            verdict.first = f"{family}/{suite}: {missing} not written"
    for name in files:
        now = read(os.path.join(folder, name))
        seen = collections.defaultdict(collections.Counter)
        dump_count = 0
        for directory in all_dirs:
            path = os.path.join(directory, name)
            if os.path.exists(path):
                dump_count += 1
                for test, lines in read(path).items():
                    seen[test][tuple(lines)] += 1
        if dump_count == 0:
            report.append(f"[{family}/{suite}] no baseline for {name}: not compared")
            continue
        for test in list(now) + [t for t in seen if t not in now]:
            lines = tuple(now.get(test, ()))
            outputs = seen.get(test)
            if outputs is None:
                # A test only the control or a later probe has, and no baseline: nothing to compare with.
                if not controls:
                    report.append(f"[{family}/{suite}] {name}: {test[:80]}: no baseline row")
                    continue
                verdict.solid += 1
                report.append(f"[{family}/{suite}] {name}: NEW TEST OUTPUT {test[:100]}")
                continue
            if test not in now:
                # Baselines older or newer than the copied probes may differ: only a test the control ran counts as gone.
                if any(os.path.exists(os.path.join(d, name)) and test in read(os.path.join(d, name)) for d in controls):
                    verdict.solid += 1
                    report.append(f"[{family}/{suite}] {name}: TEST GONE {test[:100]}")
                    if verdict.first == "-":
                        verdict.first = f"{family}/{suite}: {test[:60]}: gone from {name}"
                continue
            if lines in outputs:
                continue
            common = list(outputs.most_common(1)[0][0])
            known = test.rsplit(".", 1)[-1] in KNOWN_RACING or test in racing_on_cut9(family, suite, name)
            if len(outputs) == 1 and known:
                shapes = {shape(list(o), name) for o in outputs}
                if shape(list(lines), name) in shapes:
                    verdict.racing_seen += 1
                    report.append(f"[{family}/{suite}] {name}: racing, shape seen {test[:90]}")
                else:
                    verdict.racing_new += 1
                    text = describe(common, list(lines))
                    report.append(f"[{family}/{suite}] {name}: RACING-NEW {test[:100]} (known to race)\n    {text}")
                    report.extend("      " + r for r in changed_rows(common, list(lines))[:24])
                    if verdict.first_racing is None:
                        verdict.first_racing = f"{family}/{suite}: {shorten(test)}: {text[:150]}"
            elif len(outputs) == 1 and sorted(common) == sorted(lines):
                verdict.order += 1
                rows = changed_rows(common, list(lines))
                report.append(f"[{family}/{suite}] {name}: ORDER {test[:110]}")
                report.extend("      " + r for r in rows[:12])
                if verdict.first_order is None:
                    verdict.first_order = f"{family}/{suite}: {shorten(test)}: {' '.join(rows[:4])[:170]}"
            elif len(outputs) == 1:
                verdict.solid += 1
                text = describe(common, list(lines))
                report.append(f"[{family}/{suite}] {name}: SOLID {test[:110]}\n    {text}")
                report.extend("      " + r for r in changed_rows(common, list(lines))[:24])
                if verdict.first == "-":
                    verdict.first = f"{family}/{suite}: {shorten(test)}: {text[:190]}"
            else:
                shapes = {shape(list(o), name) for o in outputs}
                if shape(list(lines), name) in shapes:
                    verdict.racing_seen += 1
                    report.append(f"[{family}/{suite}] {name}: racing, shape seen {test[:90]}")
                else:
                    verdict.racing_new += 1
                    text = describe(common, list(lines))
                    report.append(f"[{family}/{suite}] {name}: RACING-NEW {test[:100]} (baseline variants {len(outputs)}, {len(shapes)} shapes)\n    {text}")
                    report.extend("      " + r for r in changed_rows(common, list(lines))[:24])
                    if verdict.first_racing is None:
                        verdict.first_racing = f"{family}/{suite}: {shorten(test)}: {text[:150]}"


def shorten(test):
    return test.rsplit(".", 1)[-1][:60]


def compare_mutant(mutant_id, out=None):
    BASELINE["root"] = {"p": "p00", "n": "n00"}.get(mutant_id[0], "m00")
    out = out or os.path.join(RESULTS, mutant_id)
    verdict = Verdict()
    report = []
    for folder in sorted(glob.glob(os.path.join(out, "*-*"))):
        base = os.path.basename(folder)
        if not os.path.isdir(folder) or "-" not in base:
            continue
        family, suite = base.split("-", 1)
        compare_suite(family, suite, folder, verdict, report)
    if verdict.first == "-" and verdict.first_order:
        verdict.first = "(order) " + verdict.first_order
    if verdict.first == "-" and verdict.first_racing:
        verdict.first = "(racing) " + verdict.first_racing
    with open(os.path.join(out, "diff.txt"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write(f"{verdict.word} solid={verdict.solid} order={verdict.order} racing-new={verdict.racing_new} racing-seen={verdict.racing_seen}\nfirst: {verdict.first}\n\n")
        handle.write("\n".join(report) + "\n")
    return verdict


if __name__ == "__main__":
    for mutant in sys.argv[1:]:
        result = compare_mutant(mutant)
        print(f"{mutant} {result.word} solid={result.solid} order={result.order} racing-new={result.racing_new} racing-seen={result.racing_seen} first: {result.first}")
