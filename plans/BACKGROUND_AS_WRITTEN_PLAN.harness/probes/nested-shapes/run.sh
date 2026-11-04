#!/usr/bin/env bash
# Shapes 21 to 23, kept out of the matrix so its counts stay the matrix's: run.sh <label> <work-dir> <kronikol-exe> [dir-with-replacement-dlls] [extra dotnet build args...]
# KRK_PROBE_LANE=xunit3|tunit runs them on that adapter, through lane-switch.py.
# Adds this folder to a copy of the shape suite, runs the three features green and with the inner step failing, and prints their steps.
set -u
label=$1; work=$2; kronikol=$3; dlls=${4:-}; shift $(( $# < 4 ? $# : 4 )); extra=("$@")
here=$(cd "$(dirname "$0")" && pwd)
suite=$work/nested-$label
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off

rm -rf "$suite"; mkdir -p "$suite"; cp -r "$here/../shapes-suite/." "$suite/"
cp "$here"/Features/*.feature "$suite/Features/"; cp "$here"/Steps/*.cs "$suite/Steps/"
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

show() { # mode, names...
  local mode=$1; shift
  KRK_MODE=$mode run_tests "DisplayName~21|DisplayName~22|DisplayName~23" | tail -1 | cut -c1-70
  for name in "$@"; do
    address=$("$kronikol" query scenarios bin/Release/net10.0/Reports 2>/dev/null | grep "$name" | head -1 | cut -d' ' -f1)
    echo "--- $mode, $name"
    "$kronikol" query steps bin/Release/net10.0/Reports "$address" 2>&1 | grep -v -E "^(stableId|at Features|open:)" | cut -c1-110
  done
}

show pass "21 one" "23 gherkin from a hook" "23 woven from a hook"
show fail-nested "22 breaks"
echo "--- fail-nested, the digest"
grep -E "^#|failingSteps|Failing step|first inner|second inner" bin/Release/net10.0/Reports/Failures.md | head -12 | cut -c1-110
python -I - bin/Release/net10.0/Reports/Failures.jsonl <<'PY'
import json, sys
for line in open(sys.argv[1], encoding="utf-8"):
    row = json.loads(line)
    if row.get("failingSteps") is not None:
        print("failingSteps:", json.dumps(row["failingSteps"])[:300])
PY
