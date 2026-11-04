#!/bin/bash
# Every gate again on R2's final form (fix_no_pass_over.py). Log: /c/Code/k225-all.log. Old results copied to m129/prev-results for compares.
set -u
export MSBUILDDISABLENODEREUSE=1 KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
SP="C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/84bdf88c-b056-4543-a3d5-5d3d039eba20/scratchpad/m129"
H=/c/Code/Kronikol-225/plans/CALLS_UNDER_THEIR_STEP_PLAN.harness
r="$H/results"; W=/c/Code/k225-w; PR=/c/Code/k225-pr; FEED=/c/Code/k225-bp/feed
TOOL="C:/Code/k225-w/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll"
P="$SP/prev-results"
norm() { sed -E 's/[0-9.]+ ?(ms|s)\b//g; s/ +/ /g; s/\r$//' "$1" | grep -av "^Kronikol: reports\|^Report diagnostics\|^$"; }
same() { echo "$1 against the last form: $(diff <(norm "$P/$1") <(norm "$r/$1") | grep -c '^[<>]') lines differ"; diff <(norm "$P/$1") <(norm "$r/$1") | grep '^[<>]' | cut -c1-130 | head -12; }

echo "== core $(date +%H:%M:%S)"; bash "$SP/core_both.sh"
echo "== matcher $(date +%H:%M:%S)"
sed -i 's#probes-matcher/fresh-check-cases/\*; do#probes-matcher/fresh-check-cases/*/; do#' "$SP/rerun_matcher_gates.sh"
bash "$SP/rerun_matcher_gates.sh"
echo "== lanes $(date +%H:%M:%S)"; bash "$H/scripts/run_lanes.sh" "$W" yes > "$r/lanes-638f275d.txt" 2>&1
grep -a -E '^Failed!|Test run summary: Failed|^\s+Failed [A-Z]' "$r/lanes-638f275d.txt" | head -5 | cut -c1-130
echo "== e2e + release $(date +%H:%M:%S)"
sed -n '/^echo "== e2e/,/^tail -1 "$r\/release-slnf/p' "$SP/final_gates.sh" > "$SP/_e2e.sh"; (W="$W" r="$r"; source "$SP/_e2e.sh")
echo "== gates2 $(date +%H:%M:%S)"
sed -e 's/proto225f13/proto225f17/g; s/proto225r2g/proto225r2i/g; s/bp-results-f13/bp-results-f17/; s/k225-bp-f13/k225-bp-f17/g; s/bp-results-r2g/bp-results-r2i/; s/k225-bp-r2g/k225-bp-r2i/g' -e '/^echo "== C probe suite/,/^python -I "$H\/scripts\/compare_repro.py"/d' "$SP/final_gates2.sh" > "$SP/_gates2.sh"; bash "$SP/_gates2.sh"
echo "== gates3 $(date +%H:%M:%S)"
sed -e 's/proto225f14/proto225f18/' "$SP/final_gates3.sh" > "$SP/_gates3.sh"; bash "$SP/_gates3.sh"
sed -i "1i # The probe suite on the final cut, #223's 638f275d with this plan's layer and R2. scripts/run_repro.sh" "$r/repro-638f275d.txt"
echo "== service $(date +%H:%M:%S)"
bash "$H/scripts/run_service_probes.sh" "$PR" final 5.28.3-proto225f18 "$FEED" dotnet "$TOOL" > "$r/service-638f275d.txt" 2>&1; same service-638f275d.txt
echo "== third review $(date +%H:%M:%S)"
for p in flows hosts reqnroll lightbdd bddfy; do cp "/c/Code/k225-review/$p/change.txt" "/c/Code/k225-review/$p/change-exact.txt"; done
bash "$H/scripts/run_third_review.sh" /c/Code/k225-review k225-w exact 2>&1 | tail -2 | cut -c1-120
for p in flows hosts reqnroll lightbdd bddfy; do echo "--- $p"; python -I "$H/scripts/compare_dumps.py" "/c/Code/k225-review/$p/change-exact.txt" "/c/Code/k225-review/$p/change.txt" 2>&1 | cut -c1-130 | tail -4; cp "/c/Code/k225-review/$p/change.txt" "$H/review-probes-third/$p/change-638f275d.txt"; done
T="$H/review-probes-third"; (cd "$T" && { echo "# Every call's stepPath against the rows its scenario lists (scripts/round_trip.py), the final cut: #223's 638f275d with this plan's layer and R2, no #222 under it."; python -I "$H/scripts/round_trip.py" reqnroll/change-638f275d.txt flows/change-638f275d.txt hosts/change-638f275d.txt lightbdd/change-638f275d.txt bddfy/change-638f275d.txt; } > "$r/round-trip-638f275d.txt" 2>&1); tail -1 "$r/round-trip-638f275d.txt" | cut -c1-110
echo "== sibling $(date +%H:%M:%S)"
find /c/Code/k225-review "$PR" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null
mkdir -p /c/Code/k225-sib; bash "$H/scripts/run_sibling_review.sh" "$H/review-probes/sibling-223" /c/Code/k225-sib "$W" "$TOOL" "$(cygpath -m "$r")" 638f275d > /c/Code/k225-sib.log 2>&1
for p in xu rq lb; do same "sibling-review-$p-638f275d.txt"; done
echo "== review probes $(date +%H:%M:%S)"
mkdir -p /c/Code/k225-r1/probes; powershell.exe -NoProfile -Command "New-Item -ItemType Junction -Path 'C:\Code\k225-r1\proto' -Target 'C:\Code\k225-w' | Out-Null"
python -I "$H/review-probes/gen-projects.py" "$(cygpath -w /c/Code/k225-r1/probes)" proto 2>&1 | tail -1 | cut -c1-110
bash "$H/scripts/run_review_probes.sh" /c/Code/k225-r1 "$(cygpath -m "$r/review-probes-638f275d.txt")" rq lb bddfy 2>&1 | tail -1 | cut -c1-110; same review-probes-638f275d.txt
names=$(grep -a '^#### ' "$P/review-plain-638f275d.txt" | sed 's/^#### //' | tr '\n' ' ')
(cd /c/Code/k225-r1/probes/plain-proto && timeout 280 dotnet build -c Release --nologo -v q 2>&1 | grep -E " error " | sort -u | head -3 | cut -c1-140; cd bin/Release/net10.0 && : > "$r/review-plain-638f275d.txt"; for n in $names; do { echo; echo "#### $n"; timeout 120 ./Kronikol.Tests.exe "$n" 2>&1; } >> "$r/review-plain-638f275d.txt"; done); same review-plain-638f275d.txt
echo "== bddfy fluent on the cut $(date +%H:%M:%S)"
{ echo "# BDDfy on the real adapter, fluent And and But with titles and without, and reflective names: published 5.28.2 (pub) against the final cut, #223's 638f275d with this plan's layer and R2 (cut). scripts/run_bddfy_fluent.sh, review-probes/bddfy-fluent-src."; bash "$H/scripts/run_bddfy_fluent.sh" /c/Code/k225-bd2 "$W" cut; } > "$r/bddfy-fluent-638f275d.txt" 2>&1; grep -a -c "=None" "$r/bddfy-fluent-638f275d.txt"
git -C "$W" diff 2>/dev/null | cmp - "$H/prototype/on-223-638f275d.patch" && echo "the worktree is the final cut"
git -C /c/Code/k225-m diff 2>/dev/null | cmp - "$H/prototype/r2-alone.patch" && echo "k225-m is R2"
echo "== ALL DONE $(date +%H:%M:%S)"; df -B1M /c | tail -1 | cut -c20-
