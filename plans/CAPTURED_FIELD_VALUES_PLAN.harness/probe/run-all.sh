#!/usr/bin/env bash
# The catalogue on one worktree's weaver: build, weaver suite (TRX, probe facts filtered out), dive, scratch fixture,
# cases (3 rounds), values. Usage: probe/run-all.sh <worktree> <label> [scratch classes to skip, comma-separated]
# The worktree may be another one: the probe, the scratch fixture and the two scripts are copied into it, and its
# results land in its own copy of this folder. The scratch fixture writes to the path in Proto213ScratchTests.cs.
set -u
W=$1; L=$2; SKIP=${3:-}
H213="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
T=$W/tests/Kronikol.Tests.AssertionTracking
HW=$W/plans/CAPTURED_FIELD_VALUES_PLAN.harness
mkdir -p "$HW/probe" "$HW/results"
if [ "$(cd "$HW" && pwd)" != "$H213" ]; then cp "$H213/probe/run-cases.sh" "$H213/probe/run-values.sh" "$HW/probe/"; fi
cp "$H213/probe/Probe213Tests.cs" "$H213/probe/Proto213ScratchTests.cs" "$T/"
R=$HW/results; RW=$(cygpath -w "$R")
cd "$W"
echo "build $(date -u +%T)"
timeout 1500 dotnet build tests/Kronikol.Tests.AssertionTracking -c Debug -f net10.0 -nologo > "$R/build-$L.txt" 2>&1; echo "build rc=$?"
echo "suite $(date -u +%T)"
timeout 3000 dotnet test tests/Kronikol.Tests.AssertionTracking -c Debug -f net10.0 --no-build \
  --filter "FullyQualifiedName!~Probe213Tests&FullyQualifiedName!~Proto213ScratchTests" \
  --logger "trx;LogFileName=$RW\suite-$L.trx" > "$R/suite-$L.txt" 2>&1; echo "suite rc=$?"
BIN=$T/bin/Debug/net10.0
echo "dive $(date -u +%T)"
(cd "$BIN" && KRONIKOL_213_DIVE="$RW\dive-$L.txt" timeout 1800 ./Kronikol.Tests.AssertionTracking.exe \
  -method Kronikol.Tests.AssertionTracking.Probe213Tests.Dive) > "$R/dive-run-$L.txt" 2>&1; echo "dive rc=$?"
echo "scratch $(date -u +%T)"
(cd "$BIN" && PROTO213_LABEL=$L PROTO213_OUT="$RW\scratch-runs-$L.txt" PROTO213_SKIP="$SKIP" timeout 1800 ./Kronikol.Tests.AssertionTracking.exe \
  -class Kronikol.Tests.AssertionTracking.Proto213ScratchTests) > "$R/scratch-run-$L.txt" 2>&1; echo "scratch rc=$?"
echo "cases $(date -u +%T)"
PROBE213_LABEL=$L bash "$HW/probe/run-cases.sh" > "$R/cases-run-$L.txt" 2>&1; echo "cases rc=$?"
echo "values $(date -u +%T)"
PROBE213_LABEL=$L bash "$HW/probe/run-values.sh" > "$R/values-run-$L.txt" 2>&1; echo "values rc=$?"
echo "DONE $(date -u +%T)"
