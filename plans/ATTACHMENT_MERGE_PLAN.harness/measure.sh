#!/usr/bin/env bash
# The #175 matrix: every build against every variant of the probe's tests file, one consistent run.
# Usage: bash measure.sh <control Kronikol.Tool.dll> <prototype Kronikol.Tool.dll>   (from the probe directory)
# Rows: control = the released rule; issue-1 = the issue's first suggestion (names with the extension on both sides, the
# scenario's own list only, the reporter's copy always kept); name-only = the prototype without byte verification;
# prototype = the plan's rule. Columns: entries listed per scenario, total, files in attachments/, bytes, dead links.
set -u
control=$1 prototype=$2
python -I variants.py
for variant in "" -gone -noattempt -noname; do
  for build in control issue-1 name-only prototype; do
    dll=$prototype; envs=(KRONIKOL_175_PASS2=0)
    case $build in
      control) dll=$control ;;
      issue-1) envs+=(KRONIKOL_175_ISSUE1=1) ;;
      name-only) envs+=(KRONIKOL_175_VERIFY=0) ;;
    esac
    out="m-$build$variant"; rm -rf "$out"
    env "${envs[@]}" dotnet "$dll" ingest out/empty.ndjson --tests "out/tests$variant.jsonl" \
      --cucumber-messages out/messages.ndjson -o "$out" > "$out.log" 2>&1 || echo "  ($build$variant: exit $?)"
    python -I - "$out" "$build" "${variant:--normal}" <<'EOF'
import json, os, sys
d, build, variant = sys.argv[1:4]
r = json.load(open(os.path.join(d, "TestRunReport.json"), encoding="utf-8-sig"))
def walk(steps):
    for st in steps or []:
        yield from st.get("attachments") or []
        yield from walk(st.get("subSteps"))
counts, dead = [], 0
for s in r["features"][0]["scenarios"]:
    entries = (s.get("attachments") or []) + list(walk(s.get("steps"))) + list(walk(s.get("backgroundSteps")))
    counts.append(len(entries))
    dead += sum(1 for a in entries if not os.path.exists(os.path.join(d, a["relativePath"])))
folder = os.path.join(d, "attachments")
files = os.listdir(folder) if os.path.isdir(folder) else []
size = sum(os.path.getsize(os.path.join(folder, f)) for f in files)
print(f"{variant[1:]:10} {build:10} {'/'.join(map(str, counts)):12} {sum(counts):>3} listed {len(files):>3} files {size:>8} B {dead:>3} dead")
EOF
  done
done
