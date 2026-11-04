#!/usr/bin/env bash
cd "$(dirname "$0")"
for v in 5.28.3-rev5r1 5.28.3-rev5r2; do
  for s in rev5-reqnroll rev5-bddfy rev5-lightbdd-x2 rev5-a; do
    echo "== $s $v: $(MAXLINES=6 ./run-suite.sh $s $v 2>&1 | tail -1 | cut -c1-110)"
  done
done
rm -f rev5-reqnroll/Features/*.feature.cs
echo STAGES_RUN_DONE
