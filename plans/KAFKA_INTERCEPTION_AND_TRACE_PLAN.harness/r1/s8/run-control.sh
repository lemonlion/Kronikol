#!/bin/sh
# S8's control: the same lane on 4.16.4 (published, from nuget.org), the release before R1; its produce entries must lack ids.
S=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/77278bdc-858b-4173-96e8-50ec8ed0b557/scratchpad
cd $S && rm -f bp135/nuget.config && PYTHONUTF8=1 /c/Python314/python /c/Code/Kronikol/plans/WARM_UP_PLAN.harness/breakfastprovider/bp_pin.py bp135 4.17.0 4.16.4 || exit 1
rm -f $S/s8/control-probe.txt
KAFKA_PUBLISH_PROBE=$(cygpath -w $S/s8/control-probe.txt) BP=$S/bp135 bash /c/Code/Kronikol/plans/WARM_UP_PLAN.harness/breakfastprovider/runlanes.sh $S/s8/control xUnit:1
