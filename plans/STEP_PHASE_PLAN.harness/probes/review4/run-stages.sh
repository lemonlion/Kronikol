#!/usr/bin/env bash
HERE="$(cd "$(dirname "$0")" && pwd)"
cd "$HERE"
for v in 5.28.3-rev4r1 5.28.3-rev4r1r2; do
  for s in rev4-a rev4-b rev4-c; do
    echo "== $s $v"; ./run-suite.sh $s $v 2>&1 | tail -2 | cut -c1-160
  done
done
echo DONE-RUN-STAGES
