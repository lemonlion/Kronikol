#!/bin/bash
# The third review's probes (kept in review-probes-third/) on a worktree: the change side only, as control.txt holds published's.
# Usage: run_third_review.sh <probes dir> <worktree name under C:\Code>, e.g. run_third_review.sh /c/Code/k225-review Kronikol-225y
# Each probe's old dump stays as change-<previous>.txt.
set -u
export MSBUILDDISABLENODEREUSE=1  # a reused node keeps the weaver loaded from the last build, and the next one cannot copy over it
R=$1; W=$2; PREV=${3:-225r}
cd "$R" || exit 1
sed -i -E "s/Kronikol-225[a-z]/$W/g" dump.py stable.py diagdiff.py
(cd "/c/Code/$W" && dotnet build src/Kronikol.StepTracking/Kronikol.StepTracking.csproj -c Debug -p:WarningLevel=0 -nologo 2>&1 | grep -E " error |Build succeeded" | sort -u | head -4 | cut -c1-200)
(cd "/c/Code/$W" && dotnet build src/Kronikol.Tool/Kronikol.Tool.csproj -c Release -p:WarningLevel=0 -nologo 2>&1 | grep -E " error |Build succeeded" | sort -u | head -4 | cut -c1-200)
for pair in flows:flows hosts:hostsprobe lightbdd:lbprobe bddfy:bddfyprobe reqnroll:rnprobe; do
  P=${pair%%:*}; EXE=${pair##*:}
  [ -f "$P/change-$PREV.txt" ] || cp "$P/change.txt" "$P/change-$PREV.txt"
  sed -i -E "s/Kronikol-225[a-z]/$W/g" "$P"/change/*.csproj
  echo "=== $P $(date +%H:%M:%S)"
  rm -rf "$P/change/bin/Debug/net10.0/Reports"
  if [ "$P" = reqnroll ]; then
    (cd "$P/change" && timeout 500 dotnet test -c Debug --nologo -p:WarningLevel=0 2>&1 | grep -E " error |^(Passed!|Failed!)" | sort -u | head -5 | cut -c1-200)
  else
    (cd "$P/change" && dotnet build -c Debug -v q --nologo -p:WarningLevel=0 2>&1 | grep -E " error |Build succeeded" | sort -u | head -5 | cut -c1-200)
    (cd "$P/change/bin/Debug/net10.0" && timeout 400 ./"$EXE".exe > ../../../run.log 2>&1; echo "exit=$?"; tail -2 ../../../run.log | cut -c1-160)
  fi
  python dump.py "$P/change/bin/Debug/net10.0/Reports" "$P/change.txt"
done
echo "== done $(date +%H:%M:%S)"
