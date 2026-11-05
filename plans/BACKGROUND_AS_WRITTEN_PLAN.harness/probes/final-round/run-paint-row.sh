#!/usr/bin/env bash
# E44 with the separate section, on a build: run-paint-row.sh <label> <kronikol> [dlls]
H=<tree>/plans/BACKGROUND_AS_WRITTEN_PLAN.harness; W=<tree>m; label=$1; dlls=${2:-}
suite=$W/edge-$label; out=$suite/bin/Release/net10.0/Reports
cd $suite || exit 1
if [ -n "$dlls" ]; then for name in Kronikol.dll Kronikol.ReqNRoll.Core.dll; do cp "$dlls/$name" bin/Release/net10.0/; done; fi
git -C /dev/null status >/dev/null 2>&1
python -X utf8 - bin/Release/net10.0/reqnroll.json <<'PY'
import json, sys
config = json.load(open(sys.argv[1], encoding="utf-8"))
config.pop("runtime", None)
json.dump(config, open(sys.argv[1], "w", encoding="utf-8"), indent=2)
PY
rm -rf $out
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
KRK_SEPARATE=1 dotnet test -c Release --no-build --filter "DisplayName~E44" 2>&1 | tail -1 | cut -c1-100
node $H/probes/paint-row-switch.js $out "E44 row"
