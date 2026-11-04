#!/usr/bin/env bash
HERE="$(cd "$(dirname "$0")" && pwd)"
cd "$HERE"
R="$HERE/.."
for round in 1 2; do
for v in 5.28.2 5.28.3-rev4 5.28.3-rev4fix; do
  echo "== rev4-d $v round $round"
  out="$R/out/$v"; mkdir -p "$out"
  (cd rev4-d && rm -rf bin obj && PHASE_PROBE_OUT="$out" dotnet test -p:KronikolVersion=$v --nologo -v q 2>&1 | grep -E "^(Passed|Failed)!|error " | cut -c1-170 | head -5)
  cp "$out/rev4-d.txt" "$out/rev4-d.round$round.txt"
  rm -rf "$out/reports-d"; mkdir -p "$out/reports-d"
  reports=$(find rev4-d/bin -type d -name Reports | head -1)
  [ -n "$reports" ] && cp -r "$reports/." "$out/reports-d/"
  rm -rf rev4-d/bin rev4-d/obj
done
done
echo DONE-D2
