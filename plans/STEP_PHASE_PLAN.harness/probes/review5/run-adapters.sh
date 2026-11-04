#!/usr/bin/env bash
cd "$(dirname "$0")"
for v in 5.28.2 5.28.3-rev5; do
  for s in adapter-nunit4 adapter-mstest adapter-xunit2; do
    echo "== $s $v: $(MAXLINES=40 ./run-suite.sh $s $v 2>&1 | grep -E '^(Passed|Failed)!| error ' | cut -c1-140 | head -3 | tr '\n' ' ')"
  done
  echo "== adapter-tunit $v: $(PHASE_PROBE_NAME=adapter-tunit ./run-tunit.sh adapter-tunit $v 2>&1 | tail -2 | cut -c1-140 | tr '\n' ' ')"
  echo "== rev5-reqnroll $v: $(MAXLINES=6 ./run-suite.sh rev5-reqnroll $v 2>&1 | tail -1 | cut -c1-110)"
done
rm -f rev5-reqnroll/Features/*.feature.cs
echo ADAPTERS_DONE
