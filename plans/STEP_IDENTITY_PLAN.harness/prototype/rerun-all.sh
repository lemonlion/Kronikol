#!/usr/bin/env bash
# Runs every probe against the prototype worktree, then the suites. usage: rerun-all.sh <harness dir> <scratch dir> <prototype worktree>
# Each probe runs from a copy outside the repo, as a checkout-built adapter beside the published build's bin would be compiled as source.
set -u
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
H="$1"; S="$2"; W="$3"
PROPS="-p:KronikolProto=$W -p:GeneratePackageOnBuild=false"
R_="$H/results"

copy_and_run() {  # <project folder> <copy name>
  local copy="$S/$2"
  mkdir -p "$copy"
  (cd "$H/$1" && tar --exclude=bin --exclude=obj --exclude=.kronikol -cf - .) | (cd "$copy" && tar xf -)
  (cd "$copy" && dotnet build -v q --nologo $PROPS 2>&1 | grep -E " error |Error\(s\)" | cut -c1-220 | sort -u)
  (cd "$copy" && dotnet test --no-build --nologo -v q 2>&1 | grep -E "^\s*(Passed|Failed)!" | cut -c1-120)
}

echo "=== reqnroll"
copy_and_run repro repro-proto
R="$S/repro-proto/bin/Debug/net10.0/Reports"
dotnet run --file "$R/query.cs" -- summary "$R" 2>&1 | sed -n 3p | cut -c1-90
bash "$H/read-report.sh" "$R" "$R_/triggers-prototype-final.txt"
awk -f "$R_/compact.awk" "$R_/triggers-prototype-final.txt" > "$R_/triggers-prototype-final.compact.txt"
dotnet run --file "$R/query.cs" -- failures "$R" > "$R_/failures-prototype-final.txt" 2>&1

echo "=== paint"
sid() { sed -n "/^### s$1 steps/,/^### s$1 calls/p" "$R_/triggers-prototype-final.txt" | grep -a -m1 -E '^stableId' | cut -d' ' -f2; }
(cd "$H" && node paint-probe.js "$R/TestRunReport.html" prototype results/paint "$(sid 1)" "$(sid 5)" "$(sid 8)" "$(sid 11)" 2>&1 | cut -c1-118 > "$R_/paint-prototype.txt")

echo "=== no bars"
(cd "$S/repro-proto" && HOOKSTEPS_NO_BARS=1 dotnet test --no-build --nologo -v q 2>&1 | grep -E "^\s*(Passed|Failed)!" | cut -c1-110)
{
  echo "### prototype, ShowStepDelimiters=false: T00 control"
  dotnet run --file "$R/query.cs" -- steps "$R" s0 2>&1 | grep -v -E "^stableId|^at Features|^open:|^$"
  dotnet run --file "$R/query.cs" -- interactions "$R" s0 --group-by step,method,path 2>&1
} > "$R_/no-bars-prototype.txt"
(cd "$S/repro-proto" && dotnet test --no-build --nologo -v q >/dev/null 2>&1)

echo "=== bddfy"
copy_and_run repro-bddfy repro-bddfy-proto
bash "$H/read-lane.sh" "$S/repro-bddfy-proto/bin/Debug/net10.0/Reports" "$R_/repro-bddfy-prototype-final.txt"

echo "=== lightbdd"
copy_and_run repro-lightbdd repro-lightbdd-proto
bash "$H/read-lane.sh" "$S/repro-lightbdd-proto/bin/Debug/net10.0/Reports" "$R_/repro-lightbdd-prototype-final.txt"

echo "=== xunit3"
copy_and_run repro-xunit3 repro-xunit3-proto
bash "$H/read-lane.sh" "$S/repro-xunit3-proto/bin/Debug/net10.0/Reports" "$R_/repro-xunit3-prototype-final.txt"

echo "=== suites"
OUT="$R_/core-suite-prototype.txt"
: > "$OUT"
cd "$W"
for t in Kronikol.Tests Kronikol.Tests.LightBDD.xUnit3 Kronikol.Tests.LightBDD.xUnit2 Kronikol.Tests.LightBDD.TUnit Kronikol.Tests.StepTracking; do
  echo "--- $t" >> "$OUT"
  # -v q, as a console logger at normal verbosity prints "Total tests:" and not the "Passed!" line this keeps
  dotnet test "tests/$t/$t.csproj" -p:GeneratePackageOnBuild=false --nologo -v q 2>&1 \
    | grep -a -E "^\s*(Passed|Failed)!|^\s+Failed |error CS|Total: [0-9]+" | cut -c1-200 | head -40 >> "$OUT"
done
cat "$OUT"

echo "=== the review's probes"
for pr in xu rq lb; do bash "$H/review-probes/run.sh" "$H" "$S" "$pr" proto "$W"; done

echo "=== every scenario"
bash "$H/repro-every/run-every.sh" "$H" "$S" prototype "$W" > /dev/null 2>&1
grep -a -E "^===|^(Passed|Failed)!|NO REPORT" "$R_/every-prototype.txt" | cut -c1-110

echo "=== the other adapter suites and the examples"
EX="$R_/examples-prototype.txt"
: > "$EX"
guard() { local free; free=$(df -k /c | tail -1 | awk '{print $4}'); [ "$free" -gt 8000000 ] || { echo "stopped: under 8 GB free before $1" >> "$EX"; return 1; }; }
vstest() {  # <project path> [filter]
  guard "$1" || return
  echo "--- $1" >> "$EX"
  dotnet test "$1" -p:GeneratePackageOnBuild=false --nologo -v q ${2:+--filter "$2"} 2>&1 \
    | grep -a -E "^\s*(Passed|Failed)!|^\s+Failed |error CS" | cut -c1-160 | head -20 >> "$EX"
}
mtp() {  # <project path>: Microsoft.Testing.Platform, which this SDK's dotnet test refuses without a global.json setting; ci.yml runs it this way too
  guard "$1" || return
  echo "--- $1" >> "$EX"
  dotnet run --project "$1" -p:GeneratePackageOnBuild=false 2>&1 | grep -a -E "Test run summary|^\s+(total|failed|succeeded|skipped):|error CS" | sed -E 's/ - [A-Z]:.*//' | cut -c1-120 >> "$EX"
}
for t in Kronikol.Tests.xUnit2 Kronikol.Tests.xUnit3 Kronikol.Tests.NUnit4 Kronikol.Tests.MSTest; do vstest "tests/$t"; done
mtp tests/Kronikol.Tests.TUnit
E=examples/Example.Api/tests/Example.Api.Tests
for lane in Component.ReqNRoll.xUnit3 Component.ReqNRoll.xUnit2 Component.BDDfy.xUnit3 Component.LightBDD.xUnit3 Component.LightBDD.xUnit2 Component.xUnit3 Component.xUnit2 Component.NUnit4; do vstest "$E.$lane"; done
for lane in Component.ReqNRoll.TUnit Component.LightBDD.TUnit Component.TUnit; do mtp "$E.$lane"; done
vstest "$E.Integration" "FullyQualifiedName~ReqNRollDuplicateStepsTests"
cat "$EX"
df -h /c | tail -1
echo "=== done"
