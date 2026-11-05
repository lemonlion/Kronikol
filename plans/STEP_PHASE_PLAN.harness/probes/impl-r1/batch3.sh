#!/usr/bin/env bash
cd /c/Code/k224-review1 || exit 1
R=probes/run.sh
for i in 1 2 3; do
  bash $R parallel 5.28.3-k60rv1 5.28.3-k60rv1 parallel-rv1-$i
done
bash $R parallel 5.28.2 5.28.2 parallel-5282
grep -E 'read (Setup|Action|Unknown)' out/parallel-rv1-*/parallel.log | head -10 | cut -c1-260
T=/c/Code/k224-review1/out/stress-times.txt; rm -f "$T"
for V in 5.28.2 5.28.3-k60rv1; do
  for N in 2000 20000 100000; do
    echo "== $V N=$N" | tee -a "$T"
    PROBE_N=$N PROBE_TIME="$(cygpath -w "$T")" MAXLINES=4 bash $R stress "$V" "$V" "stress-$V-$N" 2>&1 | cut -c1-200 | grep -v assets
    grep -E 'Stack overflow|StackOverflow|crash|aborted' "out/stress-$V-$N/stress.log" | head -3 | cut -c1-200
  done
done
cat "$T"
df -B1G /c | tail -1
echo BATCH_DONE
