"""Reads the probes' dump files as test name -> list of lines, and says which tests vary among several dumps of one suite."""
import collections
import os
import re

SCRATCH = r"C:\Users\cex\AppData\Local\Temp\claude\c--Code-Kronikol\31780689-26c4-4084-b1b5-c4efb9a6fd42\scratchpad"
MUT = os.path.join(SCRATCH, "mut")

# A hash a dump prints that depends on the run, not the test: normalised before comparing. Decided from the control runs.
VOLATILE = [
    (re.compile(r"resolver=\S+"), "resolver=*"),
    (re.compile(r"thread=\d+"), "thread=*"),
    (re.compile(r"scope=[0-9a-f]{8,}"), "scope=*"),
]


def norm(line):
    for pattern, repl in VOLATILE:
        line = pattern.sub(repl, line)
    return line.rstrip()


def load(path):
    tests, current = collections.OrderedDict(), None
    with open(path, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            line = line.rstrip("\r\n")
            if line.startswith("## "):
                current = line[3:]
                # The plantuml dump's header carries the test's name field after the name: a real output, so it stays a line.
                match = re.match(r"^(.*?) \(TestName field: (.*)$", current)
                if match:
                    current = match.group(1)
                    tests[current] = ["(TestName field: " + match.group(2)]
                else:
                    tests[current] = []
            elif current is not None:
                if line.strip():
                    tests[current].append(norm(line))
    return tests


def load_flat(path):
    with open(path, encoding="utf-8", errors="replace") as handle:
        return [norm(l.rstrip("\r\n")) for l in handle if l.strip()]


def variants(paths):
    """test -> Counter of its output (tuple of lines) over the dumps."""
    seen = collections.defaultdict(collections.Counter)
    for path in paths:
        for test, lines in load(path).items():
            seen[test][tuple(lines)] += 1
    return seen
