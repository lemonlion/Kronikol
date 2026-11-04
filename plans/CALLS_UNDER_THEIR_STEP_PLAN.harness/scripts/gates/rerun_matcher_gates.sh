#!/bin/bash
# The gates R2's exact compare touches, on R2 alone (k225-m): labels, the 93 cases against published, responses and trees, the fuzz, the consumer shapes.
set -u
export MSBUILDDISABLENODEREUSE=1 KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
SP="C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/84bdf88c-b056-4543-a3d5-5d3d039eba20/scratchpad/m129"
H=/c/Code/Kronikol-225/plans/CALLS_UNDER_THEIR_STEP_PLAN.harness
r="$H/results"
PUB="C:/Users/cex/.nuget/packages/kronikol.tool/5.28.2/tools/net10.0/any/Kronikol.Tool.dll"
R2="C:/Code/k225-m/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll"
CUT="C:/Code/k225-w/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll"
W=/c/Code/k225-pr/gates
mkdir -p "$W"
cases() { # out-file header dirs...
  local out=$1 header=$2; shift 2
  echo "# $header" > "$out"
  for d in "$@"; do python -I "$H/scripts/run_cases.py" "$SP/$d" "$W/$(basename "$d")" "pub=dotnet $PUB" "r2=dotnet $R2" >> "$out" 2>&1; done
}
echo "== cases $(date +%H:%M:%S)"
cases "$r/r2-peer-cases.txt" "Published 5.28.2 (pub) against R2 alone on 9d994a4b (r2): #223's session's cases A to W (probes-matcher/generators/make*.py), then #222's session's own (probes-matcher/peer-case-af). scripts/run_cases.py" cases cases-af
cases "$r/r2-position.txt" "Published 5.28.2 (pub) against R2 alone on 9d994a4b (r2): the rows with no bar, X, Y and Z (probes-matcher/generators/make_position*.py). scripts/run_cases.py" cases-pos cases-pos2
cases "$r/r2-stores.txt" "Published 5.28.2 (pub) against R2 alone on 9d994a4b (r2): a store's own captures (probes-matcher/store-cases). scripts/run_cases.py" cases-store
cases "$r/r2-review-cases.txt" "Published 5.28.2 (pub) against R2 alone on 9d994a4b (r2): the fresh review's cases (probes-matcher/review-cases). Calls and flags only: trees and responses are in r2-responses-and-trees.txt. scripts/run_cases.py" cases-rv/cases cases-rv/cases2 cases-rv/cases3 cases-rv/cases4 cases-rv/cases5
echo "== extra $(date +%H:%M:%S)"
{
  echo "# Each record's step, responses included, and the step rows, on published 5.28.2 (pub), R2 alone on 9d994a4b (r2) and the final cut (cut). scripts/run_one_case.py"
  for c in cases-rv/cases2/H1-reused-id-chronological cases-rv/cases2/H2-bar-in-flight-chronological cases-rv/cases4/K1-identical-rows-substeps cases-rv/cases3/J15-capture-case-only; do
    python -I "$H/scripts/run_one_case.py" "$SP/$c" "$W/one" "pub=dotnet $PUB" "r2=dotnet $R2" "cut=dotnet $CUT"
  done
  for c in "$H"/probes-matcher/fresh-check-cases/*/; do
    python -I "$H/scripts/run_one_case.py" "$c" "$W/one" "pub=dotnet $PUB" "r2=dotnet $R2"
  done
} > "$r/r2-responses-and-trees.txt" 2>&1
echo "== labels $(date +%H:%M:%S)"
{ echo "# The ingest labels (probes-ingest/) on R2 alone on 9d994a4b. scripts/run_labels.sh"; bash "$H/scripts/run_labels.sh" r2 "$W/labels" "$R2" dotnet "$R2"; } > "$r/r2-labels.txt" 2>&1
echo "== fuzz $(date +%H:%M:%S)"
(cd "$SP/fuzz" && dotnet build -c Release -v q -nologo -p:WarningLevel=0 2>&1 | grep -E " error |rror\(s\)" | sort -u | head -3 | cut -c1-140)
FUZZ=$(ls "$SP"/fuzz/bin/Release/net10.0/Fuzz.dll)
: > "$r/r2-fuzz.txt"
for seed in 225 128; do echo "# seed $seed, R2 alone on 9d994a4b" >> "$r/r2-fuzz.txt"; dotnet "$FUZZ" "C:/Code/k225-m/src/Kronikol/bin/Release/net10.0/Kronikol.dll" 40000 $seed >> "$r/r2-fuzz.txt" 2>&1; done
cp "$SP/fuzz/Program.cs" "$H/probes-matcher/fuzz/Program.cs"
echo "== shapes $(date +%H:%M:%S)"
{ echo "# Hand-built scenarios: published 5.28.2 (pub) against R2 alone on 9d994a4b (r2). scripts/run_consumer_shapes.sh"; bash "$H/scripts/run_consumer_shapes.sh" /c/Code/k225-m "$W/con" "$R2"; } > "$r/r2-consumer-shapes.txt" 2>&1
echo "== done $(date +%H:%M:%S)"
df -B1M /c | tail -1 | cut -c20-
