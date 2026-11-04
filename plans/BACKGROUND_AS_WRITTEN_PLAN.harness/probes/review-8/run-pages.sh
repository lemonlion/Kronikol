#!/usr/bin/env bash
# run-pages.sh <work-root> <dll-dir> : pagesuite on the published packages and with dll-dir's assemblies, with the diagrams section open and closed by option,
# each report copied to <work-root>/pagesuite-run/pages-old|pages-new as calls.html and callsclosed.html.
set -u
work=$1; dlls=$2
here=$(cd "$(dirname "$0")" && pwd)
suite=$work/pagesuite-run
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off GITHUB_STEP_SUMMARY=
mkdir -p "$suite"; cp -r "$here/pagesuite/." "$suite/"
cd "$suite" || exit 1
dotnet build -c Release -v q -p:GeneratePackageOnBuild=false 2>&1 | grep -E " error |Build succeeded" | head -3 | cut -c1-200
bin=bin/Release/net10.0
mkdir -p published-dlls pages-old pages-new
cp "$bin/Kronikol.dll" "$bin/Kronikol.ReqNRoll.Core.dll" published-dlls/
for build in old new; do
  if [ "$build" = old ]; then src=published-dlls; else src=$dlls; fi
  cp "$src/Kronikol.dll" "$src/Kronikol.ReqNRoll.Core.dll" "$bin/"
  for closed in 0 1; do
    rm -rf "$bin/Reports"
    KRK_DIAGCLOSED=$closed dotnet test -c Release --no-build 2>&1 | tail -1 | cut -c1-90
    if [ "$closed" = 1 ]; then name=callsclosed.html; else name=calls.html; fi
    cp "$bin/Reports/TestRunReport.html" "pages-$build/$name"
  done
done
ls -la pages-old pages-new | cut -c30-90
