#!/bin/bash
# The third review's BDDfy and Reqnroll probes on #223's prototype alone, to say whose a tree change is.
# Usage: run_223_alone.sh <probes dir> <worktree name under C:\Code, at #223's commit>
set -u
export MSBUILDDISABLENODEREUSE=1
R=$1; W=$2
(cd "/c/Code/$W" && dotnet build src/Kronikol.StepTracking/Kronikol.StepTracking.csproj -c Debug -p:WarningLevel=0 -nologo 2>&1 | grep -E " error |Build succeeded" | sort -u | head -4 | cut -c1-200)
for pair in bddfy:bddfyprobe reqnroll:rnprobe; do
  P=${pair%%:*}; EXE=${pair##*:}; D=$R/$P/c223
  mkdir -p "$D"
  for f in "$R/$P/change"/*.csproj; do sed -E "s/Kronikol-225[a-z]/$W/g" "$f" > "$D/$(basename "$f")"; done
  for f in "$R/$P/change"/*.feature "$R/$P/change"/*.cs; do [ -f "$f" ] && cp "$f" "$D/"; done
  [ -d "$R/$P/change/Features" ] && cp -r "$R/$P/change/Features" "$D/"
  rm -rf "$D/bin/Debug/net10.0/Reports"
  echo "=== $P $(date +%H:%M:%S)"
  if [ "$P" = reqnroll ]; then
    (cd "$D" && timeout 500 dotnet test -c Debug --nologo -p:WarningLevel=0 2>&1 | grep -E " error |^(Passed!|Failed!)" | sort -u | head -5 | cut -c1-200)
  else
    (cd "$D" && dotnet build -c Debug -v q --nologo -p:WarningLevel=0 2>&1 | grep -E " error |Build succeeded" | sort -u | head -5 | cut -c1-200)
    (cd "$D/bin/Debug/net10.0" && timeout 400 ./"$EXE".exe > ../../../run.log 2>&1; echo "exit=$?"; tail -1 ../../../run.log | cut -c1-160)
  fi
  python "$R/dump.py" "$D/bin/Debug/net10.0/Reports" "$R/$P/c223.txt"
done
echo "== done $(date +%H:%M:%S)"
