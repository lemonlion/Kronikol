#!/bin/bash
# A real sharded run of a real consumer's lane: one test executable run twice per CI run, split by namespace, under one
# GitHub run id, each shard in its own process, as CI runs two jobs. The clone is BreakfastProvider at 640c190 with its
# pins moved to 5.27.13 and GenerateMergeableData on. Nothing from the reports is printed here.
#   r1 r2 r3: no ledger at run time.
#   r4 r5: the ledger of r1..r3 visible at run time, as the history action's read step does, and a real change in one test
#   (one more call) from r4 on.
# args: <work dir> <published tool dll>
M=$1; PUB=$2
T="$M/bp/tests/BreakfastProvider.Tests.Component.xUnit"
OUT="$T/bin/Debug/net10.0"
EXE=./BreakfastProvider.Tests.Component.xUnit.exe
NS=BreakfastProvider.Tests.Component.xUnit.Scenarios
A=(-namespace "$NS.Orders*" -namespace "$NS.Specifications*" -namespace "$NS.Toppings*" -namespace "$NS.Reporting*")
B=(-nonamespace "$NS.Orders*" -nonamespace "$NS.Specifications*" -nonamespace "$NS.Toppings*" -nonamespace "$NS.Reporting*")
RUNS="$M/bp-shard-runs"
mkdir -p "$RUNS"

shard() { # <run number> <a|b> <filter args...>
    local number=$1 which=$2; shift 2
    (
        cd "$OUT" || exit 1
        export KRONIKOL_SUITE=xunit-in-memory GITHUB_ACTIONS=true GITHUB_RUN_ID=$((9100+number)) GITHUB_RUN_ATTEMPT=1 GITHUB_REF_NAME=main GITHUB_SHA=abc$number GITHUB_REPOSITORY=x/y GITHUB_SERVER_URL=https://github.example
        unset GITHUB_STEP_SUMMARY GITHUB_OUTPUT
        rm -rf Reports
        "$EXE" "$@" -noColor > "$M/bp-shard-r$number-$which.log" 2>&1
        echo "exit $?" >> "$M/bp-shard-r$number-$which.log"
        rm -rf "$RUNS/r$number-$which"
        cp -r Reports "$RUNS/r$number-$which"
    )
    tail -n 2 "$M/bp-shard-r$number-$which.log" | cut -c1-120
}

for n in 1 2 3; do
    shard $n a "${A[@]}"
    shard $n b "${B[@]}"
done

# What a merge reads r3 against, and what the later runs see at run time.
rm -f "$M/bp-shard-ledger-2.jsonl" "$M/bp-shard-ledger-3.jsonl"
dotnet "$PUB" history record "$RUNS/r1-a" "$RUNS/r1-b" "$RUNS/r2-a" "$RUNS/r2-b" --history "$M/bp-shard-ledger-2.jsonl" | cut -c1-110
cp "$M/bp-shard-ledger-2.jsonl" "$M/bp-shard-ledger-3.jsonl"
dotnet "$PUB" history record "$RUNS/r3-a" "$RUNS/r3-b" --history "$M/bp-shard-ledger-3.jsonl" | cut -c1-110

# A real change: one more call in one test, from r4 on.
TEST="$T/Scenarios/Menu/Menu_Availability_Tests.cs"
cp "$TEST" "$M/bp-menu-test.orig"
sed -i 's|await _menuSteps.ParseResponse();|await _menuSteps.ParseResponse();\n        await Client.GetAsync(Endpoints.Menu + "?mrt=real-change");|' "$TEST"
grep -q "using BreakfastProvider.Tests.Component.Shared.Constants;" "$TEST" || exit 1
dotnet build "$T/BreakfastProvider.Tests.Component.xUnit.csproj" -p:WarningLevel=0 -v q 2>&1 | tail -n 3 | cut -c1-200

export KRONIKOL_HISTORY="$M/bp-shard-ledger-seen.jsonl"
cp "$M/bp-shard-ledger-3.jsonl" "$KRONIKOL_HISTORY"
shard 4 a "${A[@]}"
shard 4 b "${B[@]}"
cp "$M/bp-shard-ledger-3.jsonl" "$M/bp-shard-ledger-4.jsonl"
env -u KRONIKOL_HISTORY dotnet "$PUB" history record "$RUNS/r4-a" "$RUNS/r4-b" --history "$M/bp-shard-ledger-4.jsonl" | cut -c1-110
cp "$M/bp-shard-ledger-4.jsonl" "$KRONIKOL_HISTORY"
shard 5 a "${A[@]}"
shard 5 b "${B[@]}"
cp "$M/bp-menu-test.orig" "$TEST"
echo all done
