#!/bin/bash
# The join between R1's bypass rule and #226's weave, on the third review's flows probe.
# Usage: run_bypass_with_226.sh <probes dir> <worktree name under C:\Code, final cut + #226's patch> <out file>
# Runs it as it stands (R1 keeps LeftABypass while the weave no longer passes through), then with LeftABypass answering false.
set -u
export MSBUILDDISABLENODEREUSE=1  # a reused node keeps the weaver loaded from the last build, and the next one cannot copy over it
R=$1; W=$2; OUT=$3
HERE=$(cd "$(dirname "$0")/.." && pwd)
SRC=$R/flows
D=$R/flows/c226
mkdir -p "$D"
sed -E "s/Kronikol-225[a-z]/$W/g" "$SRC/change/flows.csproj" > "$D/flows.csproj"
: > "$OUT"
(cd "/c/Code/$W" && dotnet build src/Kronikol.StepTracking/Kronikol.StepTracking.csproj -c Debug -p:WarningLevel=0 -nologo 2>&1 | grep -E " error |Build succeeded" | sort -u | head -4 | cut -c1-200)
run() {
  rm -rf "$D/bin/Debug/net10.0/Reports"
  (cd "$D" && dotnet build -c Debug -v q --nologo -p:WarningLevel=0 2>&1 | grep -E " error |Build succeeded" | sort -u | head -5 | cut -c1-200)
  (cd "$D/bin/Debug/net10.0" && timeout 400 ./flows.exe > ../../../run.log 2>&1; echo "exit=$?"; tail -1 ../../../run.log | cut -c1-140)
  python "$R/dump.py" "$D/bin/Debug/net10.0/Reports" "$D/out.txt"
  for s in F16 F17 F18 G14 S1_ S2_ "S1 " "S2 "; do python "$R/show.py" "$D/out.txt" "$s" | cut -c1-74; done >> "$OUT"
}
echo "=== R1 with LeftABypass, #226's weave, no pass-through" >> "$OUT"
run
[ "${ONE:-}" = 1 ] && { echo "== done"; exit 0; }
python "$HERE/prototype/without_left_a_bypass.py" "/c/Code/$W"
echo "=== LeftABypass removed, #226's weave" >> "$OUT"
run
echo "== done"
