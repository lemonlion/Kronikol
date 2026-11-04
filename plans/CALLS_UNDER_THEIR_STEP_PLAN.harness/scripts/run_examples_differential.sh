#!/bin/bash
# The example suites on a probe build: the walk against the path a record ends with, one TSV per suite.
# Usage: run_examples_differential.sh <worktree with apply_probe_one.py and probe_final_path.py> <out dir>
set -u
export MSBUILDDISABLENODEREUSE=1
W=$1; OUT=$2
mkdir -p "$OUT"
cd "$W" || exit 1
for suite in BDDfy.xUnit3 LightBDD.xUnit3 LightBDD.xUnit2 LightBDD.TUnit ReqNRoll.xUnit2 ReqNRoll.xUnit3 ReqNRoll.TUnit xUnit3 xUnit2 NUnit4 TUnit; do
  project="examples/Example.Api/tests/Example.Api.Tests.Component.$suite/Example.Api.Tests.Component.$suite.csproj"
  file=$(cygpath -w "$OUT/$suite.tsv")
  : > "$OUT/$suite.tsv"
  echo "=== $suite $(date +%H:%M:%S)"
  case "$suite" in
    *TUnit) KRONIKOL_PROBE_ATTRIBUTION="$file" timeout 900 dotnet run --project "$project" -c Release -p:WarningLevel=0 2>&1 | grep -E " error |Test run summary|total:|failed:|succeeded:" | cut -c1-130 | sort -u | tail -5 ;;
    *) KRONIKOL_PROBE_ATTRIBUTION="$file" timeout 900 dotnet test "$project" -c Release -p:WarningLevel=0 2>&1 | grep -E " error |^(Passed!|Failed!)" | cut -c1-130 | sort -u | tail -5 ;;
  esac
done
echo "== done $(date +%H:%M:%S)"
