#!/usr/bin/env bash
# The fifth review's shapes and pages on one build.
#   run.sh <label> <work-dir> <kronikol-exe> [dir-with-replacement-dlls] : Q01 to Q16 through `query steps`, then the page probes over gen's reports.
# Adds Features/Q5 and the two reviews' steps to a copy of the shape suite. gen's first reports are made from the options, as a run's are.
set -u
label=$1; work=$2; kronikol=$3; dlls=${4:-}
here=$(cd "$(dirname "$0")" && pwd)
suite=$work/review5-$label
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off GITHUB_STEP_SUMMARY=

rm -rf "$suite"; mkdir -p "$suite"; cp -r "$here/../shapes-suite/." "$suite/"
cp -r "$here/Features/Q5" "$suite/Features/Q5"
cp "$here/Steps/Review5Steps.cs" "$here/../review-4/suite/Steps/ReviewSteps.cs" "$suite/Steps/"
(cd "$suite" && dotnet build -c Release -v q 2>&1 | grep -E " error |Build succeeded" | head -5 | cut -c1-200)
bash "$here/../review-4/run.sh" "$suite" "$label" "$dlls" "FullyQualifiedName~Q0|FullyQualifiedName~Q1"
bash "$here/../review-4/steps.sh" "$kronikol" "$suite/out-$label" ' Q(0[1-9]|1[013-6]) '

gen=$work/review5-gen-$label
rm -rf "$gen"; mkdir -p "$gen"; cp "$here/gen/gen.csproj" "$here/gen/Program.cs" "$gen/"
(cd "$gen" && dotnet build -c Release -v q 2>&1 | grep -E " error |Build succeeded" | head -3 | cut -c1-200)
bin=$(dirname "$(find "$gen/bin/Release" -name gen.dll | head -1)")
[ -n "$dlls" ] && cp "$dlls/Kronikol.dll" "$bin/"
pages=$gen/pages; mkdir -p "$pages"
echo; echo "=== what a record does"
(cd "$pages" && "$bin/gen.exe" . 2>&1 | cut -c1-200)
echo; echo "=== rows: the reader's choices across rows, section by section"
node "$here/rows.js" "$pages/rows.html" 2>&1 | cut -c1-170
echo; echo "=== rows: the same with steps inline"
node "$here/rows.js" "$pages/rows-inline.html" 2>&1 | grep -A7 -E "^## Mixed: reader closes Steps" | cut -c1-170
echo; echo "=== filter search: a single match in a later row, the first row holding a failure"
node "$here/search.js" "$pages/rows.html" FirstFails zebraword 2>&1 | cut -c1-260
echo; echo "=== a details of the class constructor"
node "$here/classname.js" "$pages/rows.html" 2>&1 | cut -c1-200
