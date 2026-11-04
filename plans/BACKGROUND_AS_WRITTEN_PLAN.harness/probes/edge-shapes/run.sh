#!/usr/bin/env bash
# Shapes E24 to E46, kept out of the matrix so its counts stay the matrix's: how a scenario can stop, or read oddly, that its five modes do not reach.
#   run.sh <label> <work-dir> <kronikol-exe> [dir-with-replacement-dlls] [extra dotnet build args...]
# KRK_PROBE_LANE=xunit3|tunit runs them on that adapter, through lane-switch.py.
# Adds this folder to a copy of the shape suite, runs the new features, and prints each scenario's steps, whether a report came out, and the digest's
# failing steps. Then the whole suite with stopAtFirstError on, through the matrix's own table.
set -u
label=$1; work=$2; kronikol=$3; dlls=${4:-}; shift $(( $# < 4 ? $# : 4 )); extra=("$@")
here=$(cd "$(dirname "$0")" && pwd)
suite=$work/edge-$label
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off

rm -rf "$suite"; mkdir -p "$suite"; cp -r "$here/../shapes-suite/." "$suite/"
python -I - "$here/Features/E24_E34.feature.txt" "$suite/Features" <<'PY'
import os, sys
name = None; lines = []
def flush():
    if name:
        open(os.path.join(sys.argv[2], name), "w", encoding="utf-8", newline="\n").write("".join(lines).strip() + "\n")
for line in open(sys.argv[1], encoding="utf-8"):
    if line.startswith("#file "):
        flush(); name = line[6:].strip(); lines = []
    else:
        lines.append(line)
flush()
PY
cp "$here"/Steps/*.cs "$suite/Steps/"
cd "$suite" || exit 1
[ -n "${KRK_PROBE_LANE:-}" ] && python -I "$here/../lane-switch.py" "$KRK_PROBE_LANE"
dotnet build -c Release "${extra[@]}" 2>&1 | grep -E " error |Build succeeded" | head -5 | cut -c1-200
if [ -n "$dlls" ]; then
  for name in Kronikol.dll Kronikol.ReqNRoll.Core.dll; do cp "$dlls/$name" bin/Release/net10.0/; done
fi

# TUnit refuses VSTest and its runner takes no such filter: it runs every scenario, and only the rows asked for are read.
run_tests() { # <filter>
  if [ "${KRK_PROBE_LANE:-}" = tunit ]; then
    (cd bin/Release/net10.0 && dotnet Shapes.Suite.dll 2>&1)
  else
    dotnet test -c Release --no-build "${extra[@]}" --filter "$1" --logger "console;verbosity=normal" 2>&1
  fi
}

out=bin/Release/net10.0/Reports
rm -rf "$out"
run_tests "DisplayName~E2|DisplayName~E3|DisplayName~E4" > "$work/edge-$label.console.txt"
echo "dotnet test exit code: $?"
grep -E "^(Passed!|Failed!)|Total tests:|Test Assembly Cleanup Failure|Unhandled" "$work/edge-$label.console.txt" | head -6 | cut -c1-120
echo "report files: $(ls "$out" 2>/dev/null | grep -c -E '^TestRunReport\.(html|krkl|json)$')"

"$kronikol" query scenarios "$out" 2>&1 | grep -E " E[0-9]{2} " | cut -d' ' -f1 > "$work/edge-$label.addresses.txt"
for address in $(cat "$work/edge-$label.addresses.txt"); do
  echo
  "$kronikol" query steps "$out" "$address" 2>&1 | grep -v -E "^(stableId|at Features|open:|\s*$)" | cut -c1-120
done

echo
echo "=== query failures, only Background steps listed and none failed (E49): the failure, and the hint under it"
"$kronikol" query failures "$out" --max-bytes 2000000 2>&1 | grep -E -A6 ' E49 ' | grep -E 'E49|no step is marked' | cut -c1-140

echo
echo "=== a suite's AfterScenario hook that throws (E47): rows in the report, then whether dotnet test saw it fail"
"$kronikol" query scenarios "$out" 2>&1 | grep -c -E ' E47 '
grep -E "E47 " "$work/edge-$label.console.txt" | head -3 | cut -c1-120

echo
echo "=== the digest: scenario, failingSteps, callsScope"
python -I - "$out/Failures.jsonl" <<'PY'
import json, sys
for line in open(sys.argv[1], encoding="utf-8"):
    row = json.loads(line)
    if "failingSteps" not in row and "scenario" not in row:
        continue
    name = row.get("scenario") or row.get("name") or row.get("displayName") or ""
    steps = [(s.get("path") or s.get("stepPath"), s.get("text")) for s in row.get("failingSteps") or []]
    print(str(name)[:34].ljust(34), "failing:", steps, "scope:", row.get("callsScope"))
PY

echo
echo "=== the whole suite with stopAtFirstError on"
python -I - bin/Release/net10.0/reqnroll.json <<'PY'
import json, sys
config = json.load(open(sys.argv[1], encoding="utf-8"))
config["runtime"] = {"stopAtFirstError": True}
json.dump(config, open(sys.argv[1], "w", encoding="utf-8"), indent=2)
PY
for mode in fail-and fail-when fail-bg fail-bg2; do
  rm -rf "$out"
  KRK_MODE=$mode run_tests "DisplayName!~E2&DisplayName!~E3&DisplayName!~E4" | tail -1 | cut -c1-90
  python -I "$here/../shapes-table.py" "$kronikol" "$out" > "$work/edge-$label-stop-$mode.txt" 2>&1
  printf 'stopAtFirstError %s: rows=%s ' "$mode" "$(grep -c -E '^[0-9]{2} ' "$work/edge-$label-stop-$mode.txt")"
  for verdict in MISSED INVENTED BORROWED; do printf '%s=%s ' "$verdict" "$(grep -c "$verdict" "$work/edge-$label-stop-$mode.txt")"; done
  echo
done
echo FINISHED
