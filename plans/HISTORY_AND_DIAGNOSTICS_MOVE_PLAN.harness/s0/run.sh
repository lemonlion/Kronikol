#!/usr/bin/env bash
# Usage: run.sh <label> <KRONIKOL_HISTORY value>
# Runs the ReqNRoll.xUnit3 example suite once (no build), copies the run's TestRunReport.html to
# s0/run-<label>.html and appends the test runner's summary line to s0/runs.txt.
set -u
S0="${S0:?set S0 to the folder for the ledger and the copies}"
PROJ="${KRONIKOL_ROOT:?set KRONIKOL_ROOT to a checkout}/examples/Example.Api/tests/Example.Api.Tests.Component.ReqNRoll.xUnit3"
REPORTS="$PROJ/bin/Debug/net10.0/Reports"
label="$1"
export KRONIKOL_HISTORY="$2"

start=$(date +%s)
out=$(cd "$PROJ" && dotnet test --no-build 2>&1)
code=$?
end=$(date +%s)
summary=$(printf '%s\n' "$out" | grep -E "(Passed|Failed)!|Total tests|Failed:|Passed:" | tail -3 | tr '\n' ' ' | cut -c1-300)
cp "$REPORTS/TestRunReport.html" "$S0/run-$label.html"
lines=$( [ -f "$S0/ledger.jsonl" ] && wc -l < "$S0/ledger.jsonl" || echo 0 )
echo "run-$label exit=$code secs=$((end-start)) KRONIKOL_HISTORY=$KRONIKOL_HISTORY ledger_lines=$lines | $summary" | tee -a "$S0/runs.txt"
