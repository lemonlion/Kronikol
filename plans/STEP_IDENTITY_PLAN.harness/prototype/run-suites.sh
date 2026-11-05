#!/usr/bin/env bash
# The suites a cut of the prototype must keep green. usage: run-suites.sh <label> <prototype worktree>. Output: results/suites-<label>.txt
set -u
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
LABEL="$1"; W="$2"
HERE="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$HERE/results/suites-$LABEL.txt"
: > "$OUT"
cd "$W" || exit 1
git log --format='%h %s' -1 | cut -c1-90 >> "$OUT"
for t in Kronikol.Tests.StepTracking Kronikol.Tests.LightBDD.xUnit3 Kronikol.Tests.LightBDD.xUnit2 Kronikol.Tests.xUnit2 Kronikol.Tests.xUnit3 Kronikol.Tests.NUnit4 Kronikol.Tests.MSTest Kronikol.Tests; do
  echo "--- $t" >> "$OUT"
  # -v q, as a console logger at normal verbosity prints "Total tests:" and not the "Passed!" line this keeps
  dotnet test "tests/$t/$t.csproj" -p:GeneratePackageOnBuild=false --nologo -v q 2>&1 \
    | grep -a -E "^\s*(Passed|Failed)!|^\s+Failed |\[FAIL\]|error CS|Total: [0-9]+" | cut -c1-200 | head -30 >> "$OUT"
done
for t in Kronikol.Tests.LightBDD.TUnit Kronikol.Tests.TUnit; do
  echo "--- $t (dotnet run)" >> "$OUT"
  dotnet run --project "tests/$t/$t.csproj" -p:GeneratePackageOnBuild=false 2>&1 \
    | grep -a -E "^\s*(failed|succeeded|total|Total): |Total: [0-9]+|error CS" | cut -c1-140 | head -8 >> "$OUT"
done
echo "--- done" >> "$OUT"
df -h /c | tail -1
cat "$OUT" | cut -c1-120
