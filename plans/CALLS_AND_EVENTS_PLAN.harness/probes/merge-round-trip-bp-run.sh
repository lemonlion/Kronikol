#!/bin/bash
# one real lane run: args: <label> <run number> [extra args after --]
M="<scratch>/mrt"
label=$1; number=$2; shift 2
cd "$M/bp" || exit 1
export KRONIKOL_SUITE=xunit-in-memory GITHUB_ACTIONS=true GITHUB_RUN_ID=$((9000+number)) GITHUB_RUN_ATTEMPT=1 GITHUB_REF_NAME=main GITHUB_SHA=abc$number GITHUB_REPOSITORY=x/y GITHUB_SERVER_URL=https://github.example GITHUB_RUN_NUMBER=$number
unset GITHUB_STEP_SUMMARY GITHUB_OUTPUT
R=tests/BreakfastProvider.Tests.Component.xUnit/bin/Debug/net10.0/Reports
rm -rf "$R"
dotnet test tests/BreakfastProvider.Tests.Component.xUnit/BreakfastProvider.Tests.Component.xUnit.csproj -p:WarningLevel=0 "$@" > "$M/bp-$label.log" 2>&1
echo "exit $?" >> "$M/bp-$label.log"
mkdir -p "$M/bp-runs"; rm -rf "$M/bp-runs/$label"; cp -r "$R" "$M/bp-runs/$label"
git checkout -- docs/ 2>/dev/null
echo done "$label"
