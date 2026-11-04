#!/bin/bash
# On the final cut with R2's exact compare: the probe suite, bars off, hand-built shapes, the issue's commands and the diagrams, on packages holding every id the probe suite takes. Then #224's cut 11, checked.
set -u
export MSBUILDDISABLENODEREUSE=1 KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
H=/c/Code/Kronikol-225/plans/CALLS_UNDER_THEIR_STEP_PLAN.harness
r="$H/results"
W=/c/Code/k225-w
PR=/c/Code/k225-pr
FEED=/c/Code/k225-bp/feed
VER=5.28.3-proto225f14
TOOL="C:/Code/k225-w/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll"

git -C "$W" diff 2>/dev/null | cmp - "$H/prototype/on-223-638f275d.patch" && echo "the worktree is the final cut"
cd "$W" || exit 1
for id in Kronikol Kronikol.StepTracking Kronikol.AssertionTracking Kronikol.ReqNRoll.Core Kronikol.ReqNRoll.xUnit2; do
  dotnet build "src/$id/$id.csproj" -c Release -p:Version=$VER -p:WarningLevel=0 -v q -nologo 2>&1 | grep -E " error " | sort -u | head -3 | cut -c1-160
  dotnet pack "src/$id/$id.csproj" -c Release --no-build -p:Version=$VER -o "$FEED" -v q -nologo 2>&1 | grep -E " error " | head -3 | cut -c1-160
done
ls "$FEED" | grep -c "$VER"

echo "== repro $(date +%H:%M:%S)"
bash "$H/scripts/run_repro.sh" "$PR" final "$VER" "$FEED" dotnet "$TOOL" > "$r/repro-638f275d.txt" 2>&1
python -I - "$PR/repro-final/obj/project.assets.json" <<'EOF'
import json, sys
d = json.load(open(sys.argv[1], encoding="utf-8"))
print(sorted(k for k in d["targets"][list(d["targets"])[0]] if k.lower().startswith("kronikol")))
EOF
python -I "$H/scripts/compare_repro.py" "$r/repro-c7252f6c.txt" "$r/repro-638f275d.txt" 2>&1 | tail -1 | cut -c1-100
PROBE_NO_BARS=1 bash "$H/scripts/run_repro.sh" "$PR" final-nobars "$VER" "$FEED" dotnet "$TOOL" > "$r/repro-638f275d-nobars.txt" 2>&1
python -I "$H/scripts/compare_repro.py" "$r/repro-638f275d.txt" "$r/repro-638f275d-nobars.txt" 2>&1 | tail -1 | cut -c1-100

echo "== issue's commands $(date +%H:%M:%S)"
REP="$(cygpath -m "$PR/repro-final/bin/Release/net10.0/Reports")"
{
  echo "# The issue's own commands on the final cut's report of its own suite: #223's 638f275d with this plan's layer and R2."
  grep -a '^#####' "$r/issue-commands-c7252f6c.txt" | sed 's/^##### query //' | while IFS= read -r line; do
    echo; echo "##### query $line"; args="${line/<reports>/$REP}"; dotnet "$TOOL" query $args 2>&1 | cut -c1-140
  done
} > "$r/issue-commands-638f275d.txt" 2>&1
grep -a -E "calls?( shown)?( ·|$)" "$r/issue-commands-638f275d.txt" | cut -c1-60

echo "== diagrams $(date +%H:%M:%S)"
bash "$H/scripts/run_diagrams.sh" "$PR" final "$r/diagram-source-compare-638f275d.txt" "$TOOL" 2>&1 | tail -1 | cut -c1-100
grep -a '^DIFF' "$r/diagram-source-compare-638f275d.txt" | cut -c1-70

echo "== shapes $(date +%H:%M:%S)"
{ echo "# Hand-built scenarios: published 5.28.2 (pub) against the final cut, #223's 638f275d with this plan's layer and R2 (r2). scripts/run_consumer_shapes.sh"; bash "$H/scripts/run_consumer_shapes.sh" "$W" "$PR/con-w" "$TOOL"; } > "$r/consumer-shapes-638f275d.txt" 2>&1

echo "== #224 cut 11 $(date +%H:%M:%S)"
{
  echo "# #224's final patch, cut 11 (prototype/224-cut11-r1-r2-r3.patch), checked with git apply --check --3way on the final cut, #223's 638f275d with this plan's layer and R2. Nothing was settled, built or run."
  git -C "$W" add -u -- src 2>/dev/null
  git -C "$W" apply --check --3way "$(cygpath -m "$H/prototype/224-cut11-r1-r2-r3.patch")" 2>&1 | grep -E "^(Applied patch|error|U )" | cut -c1-140
  git -C "$W" reset -q
} > "$r/224-cut11-patch-apply.txt" 2>&1
cut -c1-100 "$r/224-cut11-patch-apply.txt"
git -C "$W" diff 2>/dev/null | cmp - "$H/prototype/on-223-638f275d.patch" && echo "the worktree is the final cut"
echo "== done $(date +%H:%M:%S)"
df -B1M /c | tail -1 | cut -c20-
