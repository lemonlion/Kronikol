#!/usr/bin/env bash
cd /c/Code/k224-review1 || exit 1
python -I probes/patch7.py || exit 1
PROJECTS="Kronikol Kronikol.xUnit3 Kronikol.xUnit2 Kronikol.ReqNRoll.Core Kronikol.ReqNRoll.xUnit2 Kronikol.LightBDD.Core Kronikol.LightBDD.xUnit2 Kronikol.BDDfy.xUnit3" bash pack.sh 5.28.3-k60rv3 > pack3.log 2>&1
tail -3 pack3.log | cut -c1-200
ls feed | grep -c 'k60rv3'
S=probes/stress/run-shapes.sh
R=probes/run.sh
W=5.28.3-k60rv1
bash $S 5.28.3-k60rv3 $W "20000 100000" "A_skips_under_a_sync_parent C_async_skips_under_a_hand_step_on_the_test_flow E_keyworded_skips_at_the_top"
bash $S 5.28.3-k60rv1 $W "16000 20000" "A_skips_under_a_sync_parent"
bash $S 5.28.3-k60rv1 $W "4000 8000 12000 16000" "C_async_skips_under_a_hand_step_on_the_test_flow"
bash $R plain 5.28.3-k60rv3 $W 5.28.3-k60rv3
bash $R bddfy 5.28.3-k60rv3 $W 5.28.3-k60rv3
bash $R reqnroll 5.28.3-k60rv3 $W 5.28.3-k60rv3
bash probes/report-lightbdd.sh 5.28.2 5.28.2 5.28.2
bash probes/report-lightbdd.sh 5.28.3-k60rv1 5.28.3-k60rv1 5.28.3-k60rv1
bash probes/report-lightbdd.sh 5.28.3-k60rv3 $W 5.28.3-k60rv3
df -B1G /c | tail -1
echo BATCH_DONE
