#!/usr/bin/env bash
# usage: real-con.sh shape:bars ... : one shape per process, a real report, on each build. What the console, the query banner, the files and the calls say.
R=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/6c95b3da-8d7f-4468-aabd-55a1f293cc1f/scratchpad/review-7
K=/c/Code/Kronikol-223r/src/Kronikol.Tool/bin/Debug/net10.0/Kronikol.Tool.exe
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
cd "$R/rv7-con"
for item in "$@"; do
  shape=${item%%:*}; bars=${item##*:}
  for v in bin/Debug/net10.0 v-2720 v-pub; do
    name=${v##*/}; [ "$name" = net10.0 ] && name=f9
    D="$R/real-$shape-$bars-$name"
    rm -rf "$D"
    ./$v/ConsumerRows.exe "$shape" "$bars" "$D" > "$D.out" 2>&1
    echo "=== $shape bars=$bars build=$name console-does-not-match=$(grep -a -c 'does not match' "$D.out") files: $(ls "$D" | grep -a -E 'labs|Failures.md' | tr '\n' ' ')"
    "$K" query summary "$D" 2>&1 | grep -a -E '^!|Mismatch' | cut -c1-130
    "$K" query summary "$D" --json 2>/dev/null | python -c "import json,sys; d=json.load(sys.stdin); print('  diagnostics in the data file:', [x.get('kind') for x in d.get('diagnostics') or []])" 2>&1 | cut -c1-160
    python - "$D/service.json" <<'PY' 2>&1 | cut -c1-140
import json,sys
try:
    d=json.load(open(sys.argv[1],encoding='utf-8-sig'))
    print('  service.json run.degraded:', d['run'].get('degraded'))
except Exception as e:
    print('  no service.json:', type(e).__name__)
PY
    "$K" query interactions "$D" s0 --group-by step,method,path 2>&1 | grep -a -E '^\S+\s+POST' | awk '{printf "  %s=%s", $3, $1} END {print ""}'
    rm -rf "$D" "$D.out"
  done
done
