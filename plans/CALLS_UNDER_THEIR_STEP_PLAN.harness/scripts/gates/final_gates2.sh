#!/bin/bash
# After R2's compare became exact: what can move. A: the 93 cases on the final cut. B: BreakfastProvider on the final cut, read forced. C: the probe suite. D: #226's newest patch.
# E: R2 alone with its read forced, on the 93 cases and BreakfastProvider. Restores both worktrees.
set -u
export MSBUILDDISABLENODEREUSE=1 KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
SP="C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/84bdf88c-b056-4543-a3d5-5d3d039eba20/scratchpad/m129"
H=/c/Code/Kronikol-225/plans/CALLS_UNDER_THEIR_STEP_PLAN.harness
r="$H/results"
W=/c/Code/k225-w
M=/c/Code/k225-m
R2="C:/Code/k225-m/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll"
CUT="C:/Code/k225-w/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll"
PATCH_W="$(cygpath -m "$H/prototype/on-223-638f275d.patch")"
PR=/c/Code/k225-pr
PUB="C:/Users/cex/.nuget/packages/kronikol.tool/5.28.2/tools/net10.0/any/Kronikol.Tool.dll"
CUCUMBER="C:/Code/k225-m/tests/Kronikol.Tests/TestData/Cucumber"
ALL="cases cases-af cases-pos cases-pos2 cases-store cases-rv/cases cases-rv/cases2 cases-rv/cases3 cases-rv/cases4 cases-rv/cases5"
ok() { python -I - "$1" <<'EOF'
import json, sys
d = json.load(open(sys.argv[1], encoding="utf-8"))
print(json.dumps((d.get("run") or {}).get("degraded")))
EOF
}

echo "== A cases on the cut $(date +%H:%M:%S)"
{
  echo "# The ingest cases: R2 alone on 9d994a4b (r2) against the final cut, #223's 638f275d with this plan's layer and R2 (cut). scripts/run_cases.py. A line 'all builds the same' means the two read alike."
  for d in $ALL; do python -I "$H/scripts/run_cases.py" "$SP/$d" "$PR/cut-$(basename "$d")" "r2=dotnet $R2" "cut=dotnet $CUT"; done
} > "$r/cases-on-final-cut.txt" 2>&1

{ echo "# The checked-in Cucumber runs through published 5.28.2 and R2 alone on 9d994a4b. scripts/run_cucumber.py"; python -I "$H/scripts/run_cucumber.py" "$CUCUMBER" "$SP/cuc-caps" "$PR/cuc" "pub=dotnet $PUB" "r2=dotnet $R2"; } > "$r/r2-cucumber.txt" 2>&1
tail -1 "$r/r2-cucumber.txt" | cut -c1-80

echo "== B BreakfastProvider on the cut, forced $(date +%H:%M:%S)"
python -I "$H/prototype/apply_forced_read.py" "$W"
VER=5.28.3-proto225f13 SKIP_CONTROL=1 bash "$H/scripts/run_breakfastprovider.sh" "$W" /c/Code/k225-bp "$SP/bp-results-f13" "$(cygpath -m "$H/scripts/bp-bump.py")" > /c/Code/k225-bp-f13.log 2>&1
{
  echo "# BreakfastProvider's four in-memory lanes: published 5.28.2 against the final cut, #223's 638f275d with this plan's layer and R2, with #129's read forced (prototype/apply_forced_read.py), which moves no call. scripts/run_breakfastprovider.sh, scripts/compare_reports.py."
  grep -a -E "^== proto|^ *failed:|^ *succeeded:" /c/Code/k225-bp-f13.log | cut -c1-60
  for lane in ReqNRoll LightBDD BDDfy NUnit; do
    echo; echo "=== $lane: published against the final cut, scenario by scenario"
    python -I "$H/scripts/compare_reports.py" "/c/Code/k225-bp/out/published/$lane" "/c/Code/k225-bp/out/proto/$lane" dotnet "$CUT" decode 2>&1 | cut -c1-170
    echo "flags: $(ok "/c/Code/k225-bp/out/proto/$lane/service.json")"
  done
} > "$r/breakfastprovider-638f275d.txt" 2>&1
git -C "$W" checkout -q -- . && git -C "$W" apply "$PATCH_W" && git -C "$W" diff 2>/dev/null | cmp - "$H/prototype/on-223-638f275d.patch" && echo "cut restored"

echo "== C probe suite $(date +%H:%M:%S)"
bash "$H/scripts/run_repro.sh" "$PR" final 5.28.3-proto225f13 /c/Code/k225-bp/feed dotnet "$CUT" > "$r/repro-638f275d.txt" 2>&1
sed -i '1i # The probe suite on the final cut, #223'"'"'s 638f275d with this plan'"'"'s layer and R2. scripts/run_repro.sh' "$r/repro-638f275d.txt"
python -I "$H/scripts/compare_repro.py" "$r/repro-c7252f6c.txt" "$r/repro-638f275d.txt" 2>&1 | tail -1 | cut -c1-100

echo "== D #226 at 305e03ff $(date +%H:%M:%S)"
sha1sum /c/Code/Kronikol-226/plans/STEP_METHOD_SHAPES_PLAN.prototype.patch | cut -c1-12
bash "$H/scripts/run_with_226_newer.sh" "$W" /c/Code/k225-review "$(cygpath -m "$H/prototype/226-305e03ff.patch")" 305e03ff "$PR/ra2" "$CUT" > /c/Code/k225-226b.log 2>&1
sed -i '1i # R1 against #226'"'"'s patch 305e03ff on the final cut. scripts/run_bypass_with_226.sh. S1 and S2 are a parent whose last act is a skip (flows/src/FlowsLastActSkip.cs).' "$r/bypass-with-226-305e03ff.txt"
git -C "$W" checkout -q -- . ; find "$W" -name '*.rej' -not -path '*/.git/*' -delete; git -C "$W" apply "$PATCH_W" && git -C "$W" diff 2>/dev/null | cmp - "$H/prototype/on-223-638f275d.patch" && echo "cut restored"
(cd "$W" && dotnet build src/Kronikol.Tool/Kronikol.Tool.csproj -c Release -v q -nologo -p:WarningLevel=0 2>&1 | grep -E "rror\(s\)| error " | sort -u | head -2 | cut -c1-100)

echo "== E R2 alone, forced $(date +%H:%M:%S)"
python -I "$H/prototype/apply_forced_read.py" "$M"
(cd "$M" && dotnet build src/Kronikol.Tool/Kronikol.Tool.csproj -c Release -v q -nologo -p:WarningLevel=0 2>&1 | grep -E "rror\(s\)| error " | sort -u | head -2 | cut -c1-100)
{
  echo "# The ingest cases on R2 alone on 9d994a4b with #129's read forced (prototype/apply_forced_read.py): each call's step and what the run flagged. scripts/run_cases.py"
  for d in $ALL; do python -I "$H/scripts/run_cases.py" "$SP/$d" "$PR/forced-$(basename "$d")" "r2=dotnet $R2"; done
} > "$r/r2-forced-read-cases.txt" 2>&1
python -I "$H/scripts/flags_table.py" "$r/r2-forced-read-cases.txt" 2>&1 | tail -1 | cut -c1-100
{ echo "# The checked-in Cucumber runs on R2 alone with #129's read forced, twice. scripts/run_cucumber.py"; python -I "$H/scripts/run_cucumber.py" "$CUCUMBER" "$SP/cuc-caps" "$PR/cuc-forced" "r2=dotnet $R2" "again=dotnet $R2"; } > "$r/r2-forced-read-cucumber.txt" 2>&1
grep -a '^===' "$r/r2-forced-read-cucumber.txt" | cut -c1-110
VER=5.28.3-proto225r2g SKIP_CONTROL=1 bash "$H/scripts/run_breakfastprovider.sh" "$M" /c/Code/k225-bp "$SP/bp-results-r2g" "$(cygpath -m "$H/scripts/bp-bump.py")" > /c/Code/k225-bp-r2g.log 2>&1
{
  echo "# BreakfastProvider's four in-memory lanes: published 5.28.2 against R2 alone on 9d994a4b with #129's read forced (prototype/apply_r2.py + prototype/apply_forced_read.py), to read #129's flags. scripts/run_breakfastprovider.sh, scripts/compare_reports.py."
  grep -a -E "^== proto|^ *failed:|^ *succeeded:" /c/Code/k225-bp-r2g.log | cut -c1-60
  for lane in ReqNRoll LightBDD BDDfy NUnit; do
    echo; echo "=== $lane: published against R2 alone, scenario by scenario"
    python -I "$H/scripts/compare_reports.py" "/c/Code/k225-bp/out/published/$lane" "/c/Code/k225-bp/out/proto/$lane" dotnet "$R2" decode 2>&1 | cut -c1-170
    echo "flags: $(ok "/c/Code/k225-bp/out/proto/$lane/service.json")"
  done
  echo
  echo "=== LightBDD's flags by scenario"
  dotnet "$R2" query summary "C:/Code/k225-bp/out/proto/LightBDD" --json 2>/dev/null > /c/Code/k225-bp/out/lb-summary.json
  python -I - /c/Code/k225-bp/out/lb-summary.json "$R2" "C:/Code/k225-bp/out/proto/LightBDD" <<'EOF'
import collections, json, re, subprocess, sys
sys.stdout.reconfigure(encoding="utf-8")
d = json.load(open(sys.argv[1], encoding="utf-8"))
tool, rep = sys.argv[2], sys.argv[3]
total = 0
for g in d["diagnostics"]:
    by = collections.Counter(e["scenario"] for e in g["entries"])
    print(g["kind"], g["count"], "in", len(by), "scenarios")
    for s, n in by.items():
        rows = json.loads(subprocess.run(["dotnet", tool, "query", "interactions", rep, s, "--json", "--max-bytes", "0"], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout)["items"]
        none = sum(1 for r in rows if r.get("stepPath") is None)
        total += none
        print(f"{s}: flags {n}, records {len(rows)}, with no step {none}")
print("records with no step in those scenarios", total)
EOF
} > "$r/bp-r2-alone-forced.txt" 2>&1
git -C "$M" checkout -q -- . && python -I "$SP/apply_r2.py" "$M" | tail -1 && git -C "$M" diff | cmp - "$H/prototype/r2-alone.patch" && echo "R2 restored"
(cd "$M" && dotnet build src/Kronikol.Tool/Kronikol.Tool.csproj -c Release -v q -nologo -p:WarningLevel=0 2>&1 | grep -E "rror\(s\)| error " | sort -u | head -2 | cut -c1-100)
echo "== done $(date +%H:%M:%S)"
df -B1M /c | tail -1 | cut -c20-
