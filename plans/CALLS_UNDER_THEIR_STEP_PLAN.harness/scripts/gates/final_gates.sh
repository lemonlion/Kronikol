#!/bin/bash
# The gates still to repeat on the final cut: consumer shapes, the end-to-end Step facts, release.slnf, then BreakfastProvider's lanes with #129's read forced.
set -u
export MSBUILDDISABLENODEREUSE=1 KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
SP="C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/84bdf88c-b056-4543-a3d5-5d3d039eba20/scratchpad/m129"
H=/c/Code/Kronikol-225/plans/CALLS_UNDER_THEIR_STEP_PLAN.harness
W=/c/Code/k225-w
TOOL="C:/Code/k225-w/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll"
r="$H/results"

echo "== shapes $(date +%H:%M:%S)"
{ echo "# Hand-built scenarios: published 5.28.2 (pub) against the final cut, #223's 638f275d with this plan's layer and R2 (r2). scripts/run_consumer_shapes.sh"; bash "$H/scripts/run_consumer_shapes.sh" "$W" /c/Code/k225-pr/con-w "$TOOL"; } > "$r/consumer-shapes-638f275d.txt" 2>&1

echo "== e2e $(date +%H:%M:%S)"
cd "$W" || exit 1
{ echo "# Kronikol.Tests.EndToEnd on #223's 638f275d with this plan's layer and R2, the classes and facts with Step in their names (bundled Chromium). dotnet test --filter FullyQualifiedName~Step"; timeout 1500 dotnet test tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj --filter FullyQualifiedName~Step -p:WarningLevel=0 2>&1 | grep -E "^(Passed!|Failed!)|^\s+Failed [A-Z]| error " | cut -c1-180 | sort -u | head -30; } > "$r/e2e-step-638f275d.txt" 2>&1
tail -1 "$r/e2e-step-638f275d.txt" | cut -c1-110

echo "== release $(date +%H:%M:%S)"
{ echo "# dotnet build release.slnf -c Release on #223's 638f275d with this plan's layer and R2, every target framework"; timeout 1500 dotnet build release.slnf -c Release -nologo -v q 2>&1 | grep -E "rror\(s\)| error " | cut -c1-180 | sort -u | head -12; } > "$r/release-slnf-638f275d.txt" 2>&1
tail -1 "$r/release-slnf-638f275d.txt" | cut -c1-110

echo "== forced $(date +%H:%M:%S)"
python -I "$H/prototype/apply_forced_read.py" "$W"
VER=5.28.3-proto225f12 SKIP_CONTROL=1 bash "$H/scripts/run_breakfastprovider.sh" "$W" /c/Code/k225-bp "$SP/bp-results-f12" "$(cygpath -m "$H/scripts/bp-bump.py")" > /c/Code/k225-bp-f12.log 2>&1
{
  echo "# BreakfastProvider's four in-memory lanes on the final cut with #129's read forced (prototype/apply_forced_read.py): what the lanes flag."
  grep -E "^== proto|^ *failed:|^ *succeeded:" /c/Code/k225-bp-f12.log | cut -c1-60
  for lane in ReqNRoll LightBDD BDDfy NUnit; do
    python -I - "/c/Code/k225-bp/out/proto/$lane/service.json" "$lane" <<'EOF'
import json, sys
d = json.load(open(sys.argv[1], encoding="utf-8"))
print(sys.argv[2], "flags:", json.dumps((d.get("run") or {}).get("degraded")))
EOF
  done
} > "$r/bp-final-cut-forced.txt" 2>&1
cat "$r/bp-final-cut-forced.txt" | cut -c1-110
git -C "$W" checkout -q -- . && git -C "$W" apply "$(cygpath -m "$H/prototype/on-223-638f275d.patch")" && git -C "$W" diff | cmp - "$H/prototype/on-223-638f275d.patch" && echo "restored"
echo "== done $(date +%H:%M:%S)"
df -B1M /c | tail -1 | cut -c20-
