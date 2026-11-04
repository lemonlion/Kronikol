#!/usr/bin/env bash
# Usage: run-examples.sh <worktree> <label>
# Runs the repo's own component suites from a worktree (they reference its src/ by project) and keeps what a reader sees:
# each scenario's phases, its diagrams' PlantUML, and the run's history fragment, under results/examples/<label>/<suite>/.
# Compare two labels with compare-examples.sh. Nothing in a suite uses Kronikol.StepTracking, a nested keyword or a
# per-phase option, so this asks what the fix changes for a suite that has none of the shapes the issue is about.
set -u
WORKTREE="$1"
LABEL="$2"
HERE="$(cd "$(dirname "$0")" && pwd)"
for suite in ReqNRoll.xUnit2 ReqNRoll.xUnit3 LightBDD.xUnit2 LightBDD.xUnit3 BDDfy.xUnit3 xUnit3 NUnit4; do
    project="Example.Api.Tests.Component.$suite"
    dir="$WORKTREE/examples/Example.Api/tests/$project"
    out="$HERE/../results/examples/$LABEL/$suite"
    rm -rf "$out"; mkdir -p "$out/diagrams"
    (cd "$dir" && rm -rf bin/Debug/net10.0/Reports && dotnet test --nologo -v q 2>&1 | grep -E "^(Passed|Failed)!| error " | cut -c1-160 | head -3 | sed "s/^/$suite: /")
    reports="$dir/bin/Debug/net10.0/Reports"
    [ -d "$reports" ] || { echo "$suite: no Reports folder"; continue; }
    cp "$reports/History.run.json" "$out/" 2>/dev/null
    (cd "$reports" && dotnet run --file query.cs -- interactions . --group-by phase,caller,service 2>&1 | cut -c1-110 > "$out/phases.txt")
    (cd "$reports" && dotnet run --file query.cs -- diagram . --out "$out/diagrams" > "$out/diagram-list.txt" 2>&1)
done
