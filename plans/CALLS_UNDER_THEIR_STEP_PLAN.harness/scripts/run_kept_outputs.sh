#!/bin/bash
# Reruns the few outputs the plan quotes that an earlier run did not keep, and keeps them this time.
# Usage: run_kept_outputs.sh <third review's probes dir, its project files at the final cut> <work dir> <results dir> <Kronikol.Tool.dll>
set -u
export MSBUILDDISABLENODEREUSE=1
R=$1; WORK=$2; RESULTS=$3; TOOL=$4
HERE=$(cd "$(dirname "$0")/.." && pwd)
THIRD="$HERE/review-probes-third"
Q() { dotnet "$TOOL" query "$@" 2>&1; }
mkdir -p "$WORK"

# F2: what Failures.md says on published, for the scenario whose failure lands on the wrong step.
bash "$HERE/scripts/run_repro.sh" "$WORK" published 5.28.2 - dotnet "$TOOL" > "$WORK/repro-published.txt" 2>&1
P="$WORK/repro-published/bin/Release/net10.0/Reports"
{
  echo "# published 5.28.2, the probe suite's Failures.md: the lines naming the failing step of 'One of two sub-steps at once fails'."
  grep -n -A3 "One of two sub-steps at once fails" "$P/Failures.md" | cut -c1-160 | head -12
  grep -n "Failing step" "$P/Failures.md" | cut -c1-160
} > "$RESULTS/failures-md-published-s14.txt"

# F20: failures --json against the text, on the final cut.
(cd "$R/flows/change" && dotnet build -c Debug -v q --nologo -p:WarningLevel=0 2>&1 | grep -E " error |Build succeeded" | sort -u | head -3 | cut -c1-160)
rm -rf "$R/flows/change/bin/Debug/net10.0/Reports"
(cd "$R/flows/change/bin/Debug/net10.0" && timeout 300 ./flows.exe > ../../../run.log 2>&1)
F="$R/flows/change/bin/Debug/net10.0/Reports"
{
  echo "# flows F20 on the final cut: a failing step with a passing sub-step that made a call."
  echo "## failures --json"
  Q failures "$F" --json --max-bytes 0 | python -c "
import json,sys,io
sys.stdout=io.TextIOWrapper(sys.stdout.buffer,encoding='utf-8')
d=json.load(sys.stdin)
for it in d['items']:
    if str(it.get('address',''))=='s20' or 'F20' in str(it.get('scenario','')) or 'F20' in str(it.get('name','')):
        for f in it['failingSteps']:
            print(f['path'],f['text'],'callsTotal=',f['callsTotal'],[c['summary'] for c in f['calls']])
"
  echo "## failures, text"
  Q failures "$F" --max-bytes 0 | grep -A14 "F20" | cut -c1-100 | head -18
} > "$THIRD/flows/failures-F20.txt"

# BV2: an address in a shared Background, with no #222 under it.
(cd "$R/reqnroll/change" && rm -rf bin/Debug/net10.0/Reports; timeout 400 dotnet test -c Debug --nologo -p:WarningLevel=0 > run.log 2>&1)
N="$R/reqnroll/change/bin/Debug/net10.0/Reports"
{
  echo "# reqnroll BV2 on the final cut, no #222 under it: a call's address, then that address asked of the report."
  python "$R/show.py" "$R/reqnroll/change.txt" BV2 | cut -c1-90
  echo "## query steps s5/b0.1"
  Q steps "$N" s5/b0.1 | cut -c1-110 | head -6
} > "$THIRD/reqnroll/bv2-address.txt"
echo "== done $(date +%H:%M:%S)"
