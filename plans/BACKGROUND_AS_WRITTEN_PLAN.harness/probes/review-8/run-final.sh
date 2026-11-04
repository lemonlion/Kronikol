#!/usr/bin/env bash
# The eighth review's fault shapes on a build, against published 5.28.2: its pages, on the sequences that showed each fault, and
# its hook suite on both Reqnroll versions, into results/eighth-review-hooks-final-<version>.txt.
# Usage: run-final.sh <work-root> <kronikol-exe> <dll-dir>
set -u
work=$1; tool=$2; dlls=$3
here=$(cd "$(dirname "$0")" && pwd)
results=$here/../../results
out=$work/r8final; rm -rf "$out"; mkdir -p "$out"
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off GITHUB_STEP_SUMMARY=

cp -r "$here/gen" "$out/gen"
( cd "$out/gen" || exit 1
  dotnet build -c Release -v q -p:GeneratePackageOnBuild=false 2>&1 | grep -E " error |Build succeeded" | head -3 | cut -c1-200
  bin=bin/Release/net10.0
  "$bin/gen" "$out/pages-old" > /dev/null 2>&1
  cp "$dlls/Kronikol.dll" "$bin/"
  "$bin/gen" "$out/pages-new" > /dev/null 2>&1 )
bash "$here/run-pages.sh" "$out" "$dlls" > /dev/null 2>&1

node "$here/drive8.js" "$out/pages-old" "$out/pages-new" rows.html "$here/seq8-faults.js" 2>&1 | cut -c1-170
node "$here/drive8.js" "$out/pagesuite-run/pages-old" "$out/pagesuite-run/pages-new" callsclosed.html "$here/seq8-diagrams.js" 2>&1 | cut -c1-170

for version in 3.3.4 3.0.1; do
  bash "$here/run.sh" "hooks-$version" "$out" "$tool" "$dlls" "$version" > "$results/eighth-review-hooks-final-$version.txt" 2>&1
  echo "hooks on $version: $(grep -E '^=== hooks' "$results/eighth-review-hooks-final-$version.txt" | cut -c1-100)"
  grep -E -A3 "^--- s[0-9]+ .*H23 " "$results/eighth-review-hooks-final-$version.txt" | cut -c1-120
done
