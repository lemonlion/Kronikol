#!/usr/bin/env bash
# usage: cmptest.sh <probe-dir> [config]   like cmp.sh for a project run with dotnet test
P="$1"; CFG="${2:-Debug}"
cd "/c/Code/k225-review/$P" || exit 1
for side in control change; do
  [ -d src ] && ls src/*.feature >/dev/null 2>&1 && cp src/*.feature "$side"/
  (cd "$side" && dotnet build -c "$CFG" -v q --nologo 2>&1 | grep -E "error|Build succeeded|Elapsed" | cut -c1-400 | head -12)
  rm -rf "$side/bin/$CFG/net10.0/Reports"
  (cd "$side" && timeout 240 dotnet test -c "$CFG" --no-build -v q --nologo > run.log 2>&1; echo "$side exit=$?"; tail -2 run.log | cut -c1-200)
  python C:/Code/k225-review/dump.py "$side/bin/$CFG/net10.0/Reports" "$side.txt"
done
df -h /c | tail -1
