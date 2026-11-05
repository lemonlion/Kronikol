#!/usr/bin/env bash
cd /c/Code/k224-review1 || exit 1
(cd probes && python -I patch10.py)
bash probes/report-lightbdd.sh 5.28.2 5.28.2 5.28.2
bash probes/report-lightbdd.sh 5.28.3-k60rv1 5.28.3-k60rv1 5.28.3-k60rv1
bash probes/report-lightbdd.sh 5.28.3-k60rv1 5.28.2 mix-old-weaver
bash probes/report-lightbdd.sh 5.28.3-k60rv3 5.28.2 mix-old-weaver-rv3
bash probes/apidiff/run.sh > out-apidiff2.txt 2>&1
tail -12 out-apidiff2.txt | cut -c1-160
df -B1G /c | tail -1
echo BATCH_DONE
