#!/usr/bin/env bash
cd "$(dirname "$0")"
until grep -q "PACK_DONE" ../packfix2.log; do sleep 3; done
tail -2 ../packfix2.log | cut -c1-100
F=5.28.3-rev5fix2
for s in rev5-a rev5-reqnroll rev5-bddfy rev5-lightbdd-x2 rev5-lightbdd-legacy rev3-lightbdd-legacy; do echo "== $s $F: $(MAXLINES=6 ./run-suite.sh $s $F 2>&1 | tail -1 | cut -c1-130)"; done
rm -f rev5-reqnroll/Features/*.feature.cs
for v in 5.28.2 5.28.3-rev5; do
  echo "== rev5-a $v: $(MAXLINES=6 ./run-suite.sh rev5-a $v 2>&1 | tail -1 | cut -c1-130)"
done
for v in 5.28.2 5.28.3-rev5r1 5.28.3-rev5 $F; do
  echo "== rev5-reqnroll-retry $v: $(MAXLINES=8 ./run-suite.sh rev5-reqnroll-retry $v 2>&1 | tail -1 | cut -c1-130)"
done
rm -f rev5-reqnroll-retry/Features/*.feature.cs
for v in 5.28.2 5.28.3-rev5 $F; do
  echo "== rev5-weave $v"; ./run-weave.sh $v
done
echo FINAL_DONE
