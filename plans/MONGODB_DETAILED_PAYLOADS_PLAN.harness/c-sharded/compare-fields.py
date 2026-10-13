"""Compares the top-level command and reply fields of the c-sharded runs with the replica-set runs in
../b-wire-shapes/results, reading each output's "## Field summary" section (wire-shapes.cs and sharded-extras.cs
print it in the same layout). Prints, per run, the fields that appear there and not on the replica set (per command
name, then across all commands) and the replica-set fields that are missing there, then where each field on the
plan's strip lists, and each field asked about, appears. sharded-extras runs other operations than wire-shapes, so
for those only added fields are listed. Usage: python compare-fields.py > results/field-diff.txt
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
B = os.path.join(HERE, "..", "b-wire-shapes", "results")
C = os.path.join(HERE, "results")
VERSIONS = ("3.12.0", "2.30.0")

BASELINES = {v: os.path.join(B, f"wire-shapes-driver-{v}-mongo8.txt") for v in VERSIONS}
# (label, driver version, path, same operations as the baseline)
RUNS = [(f"wire-shapes {v} mongos8", v, os.path.join(C, f"wire-shapes-driver-{v}-mongos8.txt"), True) for v in VERSIONS] \
     + [(f"wire-shapes {v} shard8", v, os.path.join(C, f"wire-shapes-driver-{v}-shard8.txt"), True) for v in VERSIONS] \
     + [(f"sharded-extras {v} mongos8", v, os.path.join(C, f"sharded-extras-driver-{v}-mongos8.txt"), False) for v in VERSIONS]

REPLY_STRIP = ["ok", "$clusterTime", "operationTime", "electionId", "opTime",
               "$configServerState", "$topologyTime", "lastCommittedOpTime", "$gleStats"]
COMMAND_STRIP = ["$db", "lsid", "txnNumber", "$clusterTime", "startTransaction", "autocommit", "writeConcern",
                 "readConcern", "$readPreference", "apiVersion", "apiStrict", "apiDeprecationErrors", "ordered"]
ASKED = ["$configServerState", "$topologyTime", "lastCommittedOpTime", "$gleStats", "electionId", "opTime",
         "recoveryToken", "$configTime"]


def summary(path):
    """{command name: (command fields, reply fields)} from the file's Field summary section."""
    text = open(path, encoding="utf-8").read()
    section = text.split("## Field summary", 1)[1].split("\n## ", 1)[0]
    out, name = {}, None
    for line in section.splitlines():
        m = re.match(r"^- (\S+) \(x\d+\)$", line)
        if m:
            name = m.group(1)
            out[name] = ([], [])
            continue
        m = re.match(r"^    (command|reply):\s*(.*)$", line)
        if m and name:
            out[name][0 if m.group(1) == "command" else 1].extend(f for f in m.group(2).split(", ") if f)
    return out


def union(s, i):
    seen = []
    for name in s:
        for f in s[name][i]:
            if f not in seen:
                seen.append(f)
    return seen


def presence(s, i, field):
    """Where the field appears among a run's command names, compactly."""
    names = sorted(n for n in s if s[n][i])
    hits = [n for n in names if field in s[n][i]]
    if not hits:
        return None
    if len(hits) == len(names):
        return f"all {len(names)} command names"
    if len(hits) * 2 > len(names):
        return f"all {len(names)} command names but {', '.join(n for n in names if n not in hits)}"
    return ", ".join(hits)


def main():
    base = {v: summary(p) for v, p in BASELINES.items()}
    runs = [(label, v, summary(p), same) for label, v, p, same in RUNS]
    print("# Top-level field differences: sharded cluster (mongo 8.0.32) against the replica set (mongo 8.0.32)")
    print("Baselines: ../b-wire-shapes/results/wire-shapes-driver-<v>-mongo8.txt; each run is compared with the baseline")
    print("of its own driver version, from each file's '## Field summary' section (fields per command name, unioned")
    print("over every event of that name). sharded-extras runs other operations, so only its added fields are listed.")
    print()
    for label, v, s, same in runs:
        b = base[v]
        print(f"## {label} (baseline: wire-shapes-driver-{v}-mongo8.txt)")
        for i, kind in ((0, "command"), (1, "reply")):
            print(f"   {kind} fields per command name:")
            lines = []
            for name in sorted(s):
                mine, theirs = s[name][i], b.get(name, ([], []))[i]
                if name not in b:
                    lines.append(f"     {name}: not run on the replica set; fields {', '.join(mine) or '(none)'}")
                    continue
                added = [f for f in mine if f not in theirs]
                missing = [f for f in theirs if f not in mine] if same else []
                parts = (["added " + ", ".join(added)] if added else []) + (["missing " + ", ".join(missing)] if missing else [])
                if parts:
                    lines.append(f"     {name}: {'; '.join(parts)}")
            print("\n".join(lines) if lines else "     (no difference)")
            all_mine, all_theirs = union(s, i), union(b, i)
            print(f"   {kind} fields in no {kind} on the replica set: {', '.join(f for f in all_mine if f not in all_theirs) or '(none)'}")
        print()

    everything = [(f"replica set {v}", base[v]) for v in VERSIONS] + [(label, s) for label, _, s, _ in runs]
    width = max(len(label) for label, _ in everything)

    def report(field, i):
        rows = [(label, presence(s, i, field)) for label, s in everything]
        if all(p is None for _, p in rows):
            print(f"- {field}: never")
            return
        print(f"- {field}:")
        for label, p in rows:
            print(f"    {label.ljust(width)}  {p or 'never'}")

    print("## The plan's reply strip list: where each field appears in replies")
    for f in REPLY_STRIP:
        report(f, 1)
    print()
    print("## The plan's command strip list: where each field appears in commands")
    for f in COMMAND_STRIP:
        report(f, 0)
    rows = [(label, "aggregate" if "cursor" in s.get("aggregate", ([], []))[0] else None) for label, s in everything]
    print("- cursor (on aggregate):")
    for label, p in rows:
        print(f"    {label.ljust(width)}  {p or 'never'}")
    print()
    print("## Fields asked about")
    for f in ASKED:
        print(f"### {f}")
        print("  in commands:")
        if all(presence(s, 0, f) is None for _, s in everything):
            print("    never")
        else:
            for label, s in everything:
                print(f"    {label.ljust(width)}  {presence(s, 0, f) or 'never'}")
        print("  in replies:")
        if all(presence(s, 1, f) is None for _, s in everything):
            print("    never")
        else:
            for label, s in everything:
                print(f"    {label.ljust(width)}  {presence(s, 1, f) or 'never'}")


if __name__ == "__main__":
    sys.stdout.reconfigure(newline="\n")
    main()
