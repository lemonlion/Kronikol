#!/usr/bin/env bash
HERE="$(cd "$(dirname "$0")" && pwd)"
cd "$HERE"
R="$HERE/.."
for v in 5.28.3-rev4fix; do
  for s in rev4-a rev4-b rev4-c rev4-reqnroll rev4-bddfy rev4-reentrant rev4-weave; do
    echo "== $s $v"; ./run-suite.sh $s $v 2>&1 | tail -2 | cut -c1-160; rm -rf $s/bin $s/obj
  done
  (cd rev4-diagram && rm -rf bin obj && PHASE_PROBE_OUT="$R/out/$v" dotnet run -p:KronikolVersion=$v --nologo 2>&1 | grep -E "error|Unhandled" | cut -c1-200 | head -5; rm -rf bin obj)
  rm -f "$R/out/$v/rev4-fsharp.txt"
  (cd rev4-fsharp && rm -rf bin obj && PHASE_PROBE_OUT="$R/out/$v" dotnet test -c Release -p:KronikolVersion=$v --nologo -v q 2>&1 | grep -E "^(Passed|Failed)!|error " | cut -c1-200 | head -3; rm -rf bin obj)
done
for v in 5.28.3-rev4 5.28.3-rev4fix; do
  for s in xunit3-plain lightbdd-xunit2 bddfy-xunit3 reqnroll-xunit2; do
    echo "== $s $v"; ./run-suite.sh $s $v 2>&1 | tail -2 | cut -c1-160; rm -rf $s/bin $s/obj
  done
done
echo DONE-TRIAL
