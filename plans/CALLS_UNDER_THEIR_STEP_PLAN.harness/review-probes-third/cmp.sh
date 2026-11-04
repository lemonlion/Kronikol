#!/usr/bin/env bash
# usage: cmp.sh <probe-dir> <exe-name> [config]   builds and runs control and change, dumps both to text, leaves them in the probe dir
P="$1"; EXE="$2"; CFG="${3:-Debug}"
cd "/c/Code/k225-review/$P" || exit 1
for side in control change; do
  (cd "$side" && dotnet build -c "$CFG" -v q --nologo 2>&1 | grep -E "error|Build succeeded|Elapsed" | cut -c1-400 | head -12)
  rm -rf "$side/bin/$CFG/net10.0/Reports"
  (cd "$side/bin/$CFG/net10.0" && timeout 240 ./"$EXE".exe > ../../../run.log 2>&1; echo "$side exit=$?"; tail -3 ../../../run.log | cut -c1-200)
  python C:/Code/k225-review/dump.py "$side/bin/$CFG/net10.0/Reports" "$side.txt"
done
df -h /c | tail -1
