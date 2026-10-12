#!/bin/sh
S=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/77278bdc-858b-4173-96e8-50ec8ed0b557/scratchpad
rm -f $S/s8/r1-probe.txt
KAFKA_PUBLISH_PROBE=$(cygpath -w $S/s8/r1-probe.txt) BP=$S/bp135 bash /c/Code/Kronikol/plans/WARM_UP_PLAN.harness/breakfastprovider/runlanes.sh $S/s8/r1 xUnit:1
