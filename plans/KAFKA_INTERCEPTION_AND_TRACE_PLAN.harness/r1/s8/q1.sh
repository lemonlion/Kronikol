#!/bin/sh
# Q1: the cost of quick JIT off on BreakfastProvider's xUnit in-memory lane, on 4.17.0's local packages.
# Alternates on/off so drift falls on both; runlanes.sh times dotnet test alone (its build is a no-op after the first).
S=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/77278bdc-858b-4173-96e8-50ec8ed0b557/scratchpad
RUN=/c/Code/Kronikol/plans/WARM_UP_PLAN.harness/breakfastprovider/runlanes.sh
for i in 1 2 3; do
  BP=$S/bp135 bash $RUN $S/s8/q1-on-$i xUnit:1
  DOTNET_TC_QuickJit=0 BP=$S/bp135 bash $RUN $S/s8/q1-off-$i xUnit:1
done
for d in $S/s8/q1-*/; do echo "$(basename $d) $(grep -h "rc=" $d/log.txt) $(grep -ho "Duration: [0-9a-z .]*" $d/xUnit-1.test.txt | tail -1)"; done > $S/s8/q1-summary.txt
