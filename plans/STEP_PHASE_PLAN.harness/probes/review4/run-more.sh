#!/usr/bin/env bash
HERE="$(cd "$(dirname "$0")" && pwd)"
cd "$HERE"
R="$HERE/.."
for v in 5.28.2 5.28.3-rev4; do
  out="$R/out/$v"; rm -f "$out/rev4-fsharp.txt"
  echo "== rev4-fsharp $v"
  (cd rev4-fsharp && rm -rf bin obj && PHASE_PROBE_OUT="$out" dotnet test -c Release -p:KronikolVersion=$v --nologo -v q 2>&1 | grep -E "^(Passed|Failed)!|error " | cut -c1-170 | head -3; rm -rf bin obj)
done
for v in 5.28.3-rev4r1 5.28.3-rev4r1r2; do
  for s in rev4-bddfy rev4-reqnroll xunit3-plain lightbdd-xunit2 bddfy-xunit3 reqnroll-xunit2; do
    echo "== $s $v"; ./run-suite.sh $s $v 2>&1 | tail -1 | cut -c1-140; rm -rf $s/bin $s/obj
  done
done
for s in xunit3-plain lightbdd-xunit2 bddfy-xunit3 reqnroll-xunit2; do
  echo "== $s 5.28.2"; ./run-suite.sh $s 5.28.2 2>&1 | tail -1 | cut -c1-140; rm -rf $s/bin $s/obj
done
echo DONE-MORE
