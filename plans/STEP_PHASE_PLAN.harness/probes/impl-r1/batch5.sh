#!/usr/bin/env bash
cd /c/Code/k224-review1 || exit 1
S=probes/stress/run-shapes.sh
rm -f out/stress-times-*.txt
bash $S 5.28.3-k60rv1 5.28.3-k60rv1 "1000 4000 8000 12000" "A_skips_under_a_sync_parent"
bash $S 5.28.3-k60rv1 5.28.3-k60rv1 "20000" "B_skips_under_an_async_parent C_async_skips_under_a_hand_step_on_the_test_flow D_skips_at_the_top E_keyworded_skips_at_the_top F_plain_steps_at_the_top"
bash $S 5.28.2 5.28.2 "20000" "A_skips_under_a_sync_parent B_skips_under_an_async_parent C_async_skips_under_a_hand_step_on_the_test_flow D_skips_at_the_top E_keyworded_skips_at_the_top F_plain_steps_at_the_top"
df -B1G /c | tail -1
echo BATCH_DONE
