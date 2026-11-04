#!/bin/bash
# BDDfy on a real adapter: fluent And and But, with titles and without, and reflective names, plain, escaped and long, on published and on a worktree. Prints each call's step.
# Usage: run_bddfy_fluent.sh <work dir> <worktree, under C:/Code> <label for the worktree's side>
set -u
export MSBUILDDISABLENODEREUSE=1 KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
HERE=$(cd "$(dirname "$0")/.." && pwd)
D=$1; W=$2; LABEL=$3
S="$(cygpath -w "$HERE/review-probes/bddfy-fluent-src")"
WIN="$(cygpath -w "$W")"
TOOL="$(cygpath -m "$W")/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll"
rm -rf "$D/pub" "$D/$LABEL"; mkdir -p "$D/pub" "$D/$LABEL"
sed -e "s#\.\.\\\\src\\\\\*\.cs#$(printf '%s' "$S" | sed 's#\\#\\\\#g')\\\\*.cs#" "$HERE/review-probes-third/bddfy/control/bddfy.csproj" > "$D/pub/bddfy.csproj"
python -I "$HERE/scripts/bddfy_fluent_project.py" "$HERE/review-probes-third/bddfy/change/bddfy.csproj" "$D/$LABEL/bddfy.csproj" "$S" "$WIN"
sed -e "s#^TOOL = .*#TOOL = [\"dotnet\", \"$TOOL\", \"query\"]#" "$HERE/review-probes-third/dump.py" > "$D/dump.py"
(cd "$W" && dotnet build src/Kronikol.StepTracking/Kronikol.StepTracking.csproj -c Debug -p:WarningLevel=0 -nologo -v q 2>&1 | grep -E " error " | sort -u | head -2 | cut -c1-140)
for side in pub "$LABEL"; do
  (cd "$D/$side" && timeout 280 dotnet test -c Debug --nologo -p:WarningLevel=0 2>&1 | grep -E " error |^(Passed!|Failed!)" | sort -u | head -4 | cut -c1-140)
  python -I "$D/dump.py" "$D/$side/bin/Debug/net10.0/Reports" "$D/$side.txt" 2>&1 | tail -1 | cut -c1-120
done
python -I "$HERE/scripts/bddfy_fluent_read.py" "$D/pub.txt" "$D/$LABEL.txt" pub "$LABEL"
