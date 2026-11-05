#!/usr/bin/env bash
# The commit's own facts, on my patched copy of the source (rv3's patches), to see whether the proposed fixes break any of them.
cd /c/Code/k224-review1 || exit 1
free=$(df -B1G /c | tail -1 | awk '{print $4}')
if [ "$free" -lt 12 ]; then echo "STOP: $free GB free"; echo BATCH_DONE; exit 2; fi
git -C /c/Code/Kronikol-224rv archive --format=tar cd7412d9 -- templates tests/Kronikol.Tests tests/Kronikol.Tests.LightBDD.xUnit2 tests/Kronikol.Tests.StepTracking tests/Kronikol.Tests.xUnit2.Fixtures | tar -x -C wt
T=/c/Code/k224-review1/wt/tests
L=/c/Code/k224-review1/out/facts; mkdir -p "$L"
echo "== Kronikol.Tests.StepTracking"
dotnet test "$T/Kronikol.Tests.StepTracking" --nologo -v q -m:2 --filter "FullyQualifiedName~StepPhaseWeavingTests" > "$L/steptracking.log" 2>&1
grep -nE 'Passed!|Failed!|Failed:|error |No test' "$L/steptracking.log" | cut -c1-220 | head -30
df -B1G /c | tail -1
echo "== Kronikol.Tests.LightBDD.xUnit2"
dotnet test "$T/Kronikol.Tests.LightBDD.xUnit2" --nologo -v q -m:2 --filter "FullyQualifiedName~StepPhaseScenarioTests" > "$L/lightbdd.log" 2>&1
grep -nE 'Passed!|Failed!|Failed:|error |No test' "$L/lightbdd.log" | cut -c1-220 | head -30
df -B1G /c | tail -1
echo "== Kronikol.Tests (the phase facts and the handler's)"
dotnet test "$T/Kronikol.Tests" --nologo -v q -m:2 --filter "FullyQualifiedName~StepPhaseTests|FullyQualifiedName~StepPhaseHooksTests|FullyQualifiedName~BDDfyStepPhaseTests|FullyQualifiedName~PhaseConfigurationTests|FullyQualifiedName~TestPhaseContextTests|FullyQualifiedName~TestTrackingMessageHandler|FullyQualifiedName~StepCollector" > "$L/core.log" 2>&1
grep -nE 'Passed!|Failed!|Failed:|error |No test' "$L/core.log" | cut -c1-220 | head -40
df -B1G /c | tail -1
echo BATCH_DONE
