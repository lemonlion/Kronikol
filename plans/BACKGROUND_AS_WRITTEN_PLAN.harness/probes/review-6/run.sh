#!/usr/bin/env bash
# The sixth review's shapes and page sequences on one build, beside 5.28.2.
#   run.sh <label> <work-dir> <kronikol-exe> <dir-with-replacement-dlls>
# mk-features.py writes R01 to R19 into a copy of the shape suite, with the fourth and sixth reviews' steps; gen makes reports from the options.
set -u
label=$1; work=$2; kronikol=$3; dlls=$4
here=$(cd "$(dirname "$0")" && pwd)
suite=$work/review6-$label
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off GITHUB_STEP_SUMMARY=

rm -rf "$suite"; mkdir -p "$suite"; cp -r "$here/../shapes-suite/." "$suite/"
python -I "$here/mk-features.py" "$suite/Features"
cp "$here/Steps/Review6Steps.cs" "$here/../review-4/suite/Steps/ReviewSteps.cs" "$suite/Steps/"
(cd "$suite" && dotnet build -c Release -v q 2>&1 | grep -E " error |Build succeeded" | head -5 | cut -c1-200)
bash "$here/../review-4/run.sh" "$suite" "$label" "$dlls" "FullyQualifiedName~R0|FullyQualifiedName~R1"
bash "$here/../review-4/steps.sh" "$kronikol" "$suite/out-$label" ' R[0-9][0-9] '

gen=$work/review6-gen-$label
rm -rf "$gen"; mkdir -p "$gen"; cp "$here/gen/gen.csproj" "$here/gen/Program.cs" "$gen/"
(cd "$gen" && dotnet build -c Release -v q 2>&1 | grep -E " error |Build succeeded" | head -3 | cut -c1-200)
bin=$(dirname "$(find "$gen/bin/Release" -name gen.dll | head -1)")
mkdir -p "$gen/pages-old" "$gen/pages-new"
(cd "$gen/pages-old" && "$bin/gen.exe" . > /dev/null 2>&1)
cp "$dlls/Kronikol.dll" "$bin/"
(cd "$gen/pages-new" && "$bin/gen.exe" . > /dev/null 2>&1)
for page in rows.html rows-inline.html; do
  echo; echo "=== $page: each sequence on 5.28.2 and on this build"
  node "$here/drive.js" "$gen/pages-old" "$gen/pages-new" "$page" 2>&1 | cut -c1-210
done
