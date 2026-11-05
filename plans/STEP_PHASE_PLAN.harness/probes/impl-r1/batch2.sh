#!/usr/bin/env bash
cd /c/Code/k224-review1 || exit 1
R=probes/run.sh
bash $R reqnroll 5.28.2
bash $R reqnroll 5.28.3-k60rv1
bash $R bddfy 5.28.2
bash $R bddfy 5.28.3-k60rv1
df -B1G /c | tail -1
echo BATCH_DONE
