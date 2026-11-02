#!/bin/bash
# merge-round-trip-shards.sh's r4 and r5 again, on a candidate Kronikol.dll: the same real lane, the same real change, the
# ledger of r1..r3 visible at run time. Does a fragment that carries only the host's Partial close the false alarm?
# args: <work dir> <candidate tool dll>
M=$1; TOOL=$2
T="$M/bp/tests/BreakfastProvider.Tests.Component.xUnit"
OUT="$T/bin/Debug/net10.0"
EXE=./BreakfastProvider.Tests.Component.xUnit.exe
NS=BreakfastProvider.Tests.Component.xUnit.Scenarios
A=(-namespace "$NS.Orders*" -namespace "$NS.Specifications*" -namespace "$NS.Toppings*" -namespace "$NS.Reporting*")
B=(-nonamespace "$NS.Orders*" -nonamespace "$NS.Specifications*" -nonamespace "$NS.Toppings*" -nonamespace "$NS.Reporting*")
RUNS="$M/bp-fix-runs"
mkdir -p "$RUNS"
cp "$OUT/Kronikol.dll" "$M/bp-kronikol-dll.orig"
cp "$(dirname "$TOOL")/Kronikol.dll" "$OUT/Kronikol.dll"

shard() { # <run number> <a|b> <filter args...>
    local number=$1 which=$2; shift 2
    (
        cd "$OUT" || exit 1
        export KRONIKOL_SUITE=xunit-in-memory GITHUB_ACTIONS=true GITHUB_RUN_ID=$((9100+number)) GITHUB_RUN_ATTEMPT=1 GITHUB_REF_NAME=main GITHUB_SHA=abc$number GITHUB_REPOSITORY=x/y GITHUB_SERVER_URL=https://github.example
        unset GITHUB_STEP_SUMMARY GITHUB_OUTPUT
        rm -rf Reports
        "$EXE" "$@" -noColor > "$M/bp-fix-r$number-$which.log" 2>&1
        echo "exit $?" >> "$M/bp-fix-r$number-$which.log"
        rm -rf "$RUNS/r$number-$which"
        cp -r Reports "$RUNS/r$number-$which"
    )
    tail -n 2 "$M/bp-fix-r$number-$which.log" | cut -c1-120
}

export KRONIKOL_HISTORY="$M/bp-fix-ledger-seen.jsonl"
cp "$M/bp-shard-ledger-3.jsonl" "$KRONIKOL_HISTORY"
shard 4 a "${A[@]}"
shard 4 b "${B[@]}"
cp "$M/bp-shard-ledger-3.jsonl" "$M/bp-fix-ledger-4.jsonl"
env -u KRONIKOL_HISTORY dotnet "$TOOL" history record "$RUNS/r4-a" "$RUNS/r4-b" --history "$M/bp-fix-ledger-4.jsonl" | cut -c1-110
cp "$M/bp-fix-ledger-4.jsonl" "$KRONIKOL_HISTORY"
shard 5 a "${A[@]}"
shard 5 b "${B[@]}"
cp "$M/bp-kronikol-dll.orig" "$OUT/Kronikol.dll"
echo all done
