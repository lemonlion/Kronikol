#!/bin/bash
C=C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/0d815929-a6f1-4696-b789-7a0444d6f82d/scratchpad/consumer
bash $C/run_examples.sh C:/Code/Kronikol-xunit2-133 $C/after > $C/after.log 2>&1
bash $C/run_examples.sh C:/Code/Kronikol-133-red $C/before > $C/before.log 2>&1
cat $C/after.log $C/before.log
