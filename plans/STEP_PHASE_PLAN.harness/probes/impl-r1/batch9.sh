#!/usr/bin/env bash
cd /c/Code/k224-review1 || exit 1
git -C /c/Code/Kronikol-224rv archive --format=tar cd7412d9 -- tests/history-off.runsettings | tar -x -C wt
T=/c/Code/k224-review1/wt/tests
L=/c/Code/k224-review1/out/facts; mkdir -p "$L"
echo "== Kronikol.Tests.StepTracking"
dotnet test "$T/Kronikol.Tests.StepTracking" --nologo -v q -m:2 --filter "FullyQualifiedName~StepPhaseWeavingTests" > "$L/steptracking.log" 2>&1
grep -nE 'Passed!|Failed!|\[FAIL\]|error |No test' "$L/steptracking.log" | cut -c1-220 | head -30
echo "== Kronikol.Tests.LightBDD.xUnit2"
dotnet test "$T/Kronikol.Tests.LightBDD.xUnit2" --nologo -v q -m:2 --filter "FullyQualifiedName~StepPhaseScenarioTests" > "$L/lightbdd.log" 2>&1
grep -nE 'Passed!|Failed!|\[FAIL\]|error |No test' "$L/lightbdd.log" | cut -c1-220 | head -30
df -B1G /c | tail -1
(cd probes && python -I patch9.py)
bash probes/report-lightbdd.sh 5.28.2 5.28.2 5.28.2
bash probes/report-lightbdd.sh 5.28.3-k60rv1 5.28.3-k60rv1 5.28.3-k60rv1
bash probes/report-lightbdd.sh 5.28.3-k60rv3 5.28.3-k60rv1 5.28.3-k60rv3
df -B1G /c | tail -1
echo BATCH_DONE
