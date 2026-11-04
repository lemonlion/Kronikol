#!/usr/bin/env python
"""How often each outcome of a racing shape happens, over the dumps racy.sh keeps.

Usage: racy_scan.py <out/racy> <dump file name> <test regex> [<version> ...]
One outcome per run: which call the boundary marker stands before, the phase of every call that is not a poll, and how
many polls read each phase. Outcomes are counted across runs, so '5x' beside a line means five runs of that version
gave exactly that line.
"""
import collections
import os
import re
import sys

ROW = re.compile(r"^\s*(\S+)\s+(http|msg)\s+(\w+)\s*(IGNORED)?\s*$")


def load(path):
    tests, current = {}, None
    for line in open(path, encoding="utf-8"):
        line = line.rstrip("\n")
        if line.startswith("## "):
            current = line[3:]
            tests[current] = []
        elif current is not None and line.strip():
            tests[current].append(line)
    return tests


def outcome(lines):
    marker = "no marker"
    polls = collections.Counter()
    calls = []
    waiting = False
    for line in lines:
        if line.strip() == "steps:":
            break
        if "ACTION START MARKER" in line:
            waiting = True
            marker = "marker before ?"
            continue
        row = ROW.match(line)
        if not row:
            continue
        label, kind, phase, ignored = row.groups()
        if kind != "http":
            continue
        if waiting:
            marker = f"marker before {label}"
            waiting = False
        state = phase + ("/IGNORED" if ignored else "")
        if re.search(r"-poll\d+$", label):
            polls[state] += 1
        else:
            calls.append(f"{label}={state}")
    poll_text = ", ".join(f"{k} x{v}" for k, v in sorted(polls.items()))
    return f"{marker} | {' '.join(calls)}" + (f" | polls: {poll_text}" if poll_text else "")


root, name, pattern = sys.argv[1], sys.argv[2], sys.argv[3]
versions = sys.argv[4:] or sorted(os.listdir(root))
for version in versions:
    per_test = collections.defaultdict(collections.Counter)
    runs = 0
    for run in sorted(os.listdir(os.path.join(root, version))):
        path = os.path.join(root, version, run, name)
        if not os.path.exists(path):
            continue
        runs += 1
        for test, lines in load(path).items():
            if re.search(pattern, test):
                per_test[test][outcome(lines)] += 1
    print(f"=== {version}: {runs} runs of {name}")
    for test in sorted(per_test):
        print(f"  {test[-70:]}")
        for text, count in per_test[test].most_common():
            print(f"    {count}x {text}")
