#!/usr/bin/env bash
# The plan's section 6.6 on a release build: run.sh <tree> <scratch> [stanza...]. It holds the example projects' fixed ports and Playwright for hours, so say so to peers
# first. With stanzas named, only those run: build surface suites examples matrix lanes shapes across upgrade bp playwright.
set -u
T=$1; W=$2; shift 2
want=" $* "
on() { [ "$want" = "  " ] || [[ "$want" == *" $1 "* ]]; }
CONTROL=${CONTROL:-5.28.2}
H=$T/plans/BACKGROUND_AS_WRITTEN_PLAN.harness; R=$H/results; O=$R/release-proof.txt
K=$W/tool/Kronikol.Tool.exe; D=$W/dlls
mkdir -p "$W"
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off CONTROL
stamp() { echo; echo "##### $1  $(date +%H:%M)"; }
sumline() { grep -E "^\s+Failed |^\s*(Passed|Failed)!| error " | cut -c1-170; }
cd "$T" || exit 1

if on build; then
  stamp "release.slnf in Release, every target framework"
  git log -1 --format='%h %s' | cut -c1-100
  dotnet build release.slnf -c Release -v q -nologo 2>&1 | grep -E " error |Build succeeded|Warn|Error\(s\)" | sort -u | cut -c1-170 | tail -6
  dotnet build src/Kronikol.Tool/Kronikol.Tool.csproj -c Release -p:GeneratePackageOnBuild=false -v q -nologo 2>&1 | grep -E " error |Build succeeded" | head -2 | cut -c1-170
  dotnet build src/Kronikol.ReqNRoll.Core/Kronikol.ReqNRoll.Core.csproj -c Release -p:GeneratePackageOnBuild=false -v q -nologo 2>&1 | grep -E " error |Build succeeded" | head -2 | cut -c1-170
  rm -rf "$D" "$W/tool"; mkdir -p "$D"
  cp src/Kronikol.ReqNRoll.Core/bin/Release/net10.0/Kronikol.dll src/Kronikol.ReqNRoll.Core/bin/Release/net10.0/Kronikol.ReqNRoll.Core.dll "$D/"
  cp -r src/Kronikol.Tool/bin/Release/net10.0 "$W/tool"
fi

if on surface; then
  stamp "the public surface against the published $CONTROL"
  ( cd "$T/plans/BACKGROUND_AS_WRITTEN_PLAN.harness/probes/shapes-suite" && dotnet restore -p:KronikolVersion="$CONTROL" --packages "$HOME/.nuget/packages" -v q >/dev/null 2>&1; rm -rf bin obj )
  mkdir -p "$W/api"; cp "$H/probes/public-api/PublicApi.cs" "$W/api/"
  ( cd "$W/api" || exit 1
    N=$(cygpath -m "$HOME/.nuget/packages")
    dotnet run --file PublicApi.cs -- "$N/kronikol/$CONTROL/lib/net10.0/Kronikol.dll" "$N/kronikol.reqnroll.core/$CONTROL/lib/net10.0/Kronikol.ReqNRoll.Core.dll" > published.txt 2>&1
    dotnet run --file PublicApi.cs -- "$(cygpath -m "$D/Kronikol.dll")" "$(cygpath -m "$D/Kronikol.ReqNRoll.Core.dll")" > release.txt 2>&1
    echo "$(wc -l < published.txt) and $(wc -l < release.txt) lines"; diff published.txt release.txt | cut -c1-170 | head -30 && echo "diff ended" )
fi

if on suites; then
  stamp "the core suite and the suites that load what changed"
  dotnet test tests/Kronikol.Tests/Kronikol.Tests.csproj -c Release -p:GeneratePackageOnBuild=false 2>&1 | sumline
  for project in Kronikol.Tests.SearchEngine Kronikol.Tests.StepTracking Kronikol.Tests.LightBDD.xUnit3 Kronikol.Tests.LightBDD.xUnit2; do
    echo "--- $project"; dotnet test "tests/$project" -c Release -p:GeneratePackageOnBuild=false 2>&1 | sumline
  done
fi

if on examples; then
  stamp "the example projects on their own, then every Integration fact"
  for p in xUnit2 xUnit3; do echo "--- $p"; dotnet test "examples/Example.Api/tests/Example.Api.Tests.Component.ReqNRoll.$p" -c Release -p:GeneratePackageOnBuild=false 2>&1 | sumline; done
  echo "--- TUnit"; dotnet run --project examples/Example.Api/tests/Example.Api.Tests.Component.ReqNRoll.TUnit -c Release -p:GeneratePackageOnBuild=false 2>&1 | grep -E "summary|total:|failed:|succeeded:| error " | cut -c1-120
  # its facts run the example projects, and a fresh worktree has not restored them: NETSDK1004 on 158 of 265 facts otherwise
  for project in examples/Example.Api/tests/*/*.csproj; do dotnet restore "$project" -v q >/dev/null 2>&1; done
  echo "--- Integration"; dotnet test examples/Example.Api/tests/Example.Api.Tests.Integration -c Release -p:GeneratePackageOnBuild=false 2>&1 | sumline
fi

count_row() { for mode in pass fail-and fail-when fail-bg fail-bg2; do f=$R/shapes-$1-$mode.txt; printf '%s: ' "$mode"; for v in MISSED INVENTED BORROWED; do printf '%s=%s ' "$v" "$(grep -c "$v" "$f")"; done; echo; done; }

if on matrix; then
  stamp "the shape matrix: xUnit v2, on the release's assemblies"
  bash "$H/probes/run-matrix.sh" release "$W" "$K" "$D" 2>&1 | tail -3 | cut -c1-100
  count_row release
  stamp "the floor: Reqnroll 3.0.1"
  bash "$H/probes/run-matrix.sh" release-reqnroll-3.0.1 "$W" "$K" "$D" -p:ReqnrollVersion=3.0.1 2>&1 | tail -2 | cut -c1-100
  count_row release-reqnroll-3.0.1
  stamp "the control: published $CONTROL"
  bash "$H/probes/run-matrix.sh" control-release "$W" "$K" "" -p:KronikolVersion=$CONTROL 2>&1 | tail -2 | cut -c1-100
fi

if on lanes; then
  stamp "the other adapters and row tests off"
  for lane in xunit3 tunit norowtests; do
    echo "--- $lane"; bash "$H/probes/run-lane.sh" "$lane" release "$W" "$K" "$D" 2>&1 | tail -1 | cut -c1-100
    count_row "release-lane-$lane"
  done
fi

if on shapes; then
  stamp "the edge and nested shapes"
  bash "$H/probes/edge-shapes/run.sh" release "$W" "$K" "$D" > "$R/edge-shapes-release.txt" 2>&1
  grep -E "^E(35|36|39|41|46|47) |calls belong to no step" "$R/edge-shapes-release.txt" | cut -c1-110 | head -12
  bash "$H/probes/nested-shapes/run.sh" release "$W" "$K" "$D" > "$R/nested-shapes-release.txt" 2>&1
  wc -l "$R/nested-shapes-release.txt"
  grep -E "^(21|22|23) " "$R/nested-shapes-release.txt" | cut -c1-110 | head -8
fi

if on across; then
  stamp "published $CONTROL against the release, scenario by scenario"
  for mode in pass fail-and fail-when fail-bg fail-bg2; do
    python -X utf8 "$H/probes/compare-across-the-fix.py" "$K" "$W/control-release-$mode" "$W/release-$mode" > "$R/across-the-fix-release-$mode.txt" 2>&1
    echo "$mode: $(tail -1 "$R/across-the-fix-release-$mode.txt" | cut -c1-140)"
  done
fi

if on bp; then
  stamp "BreakfastProvider across the fix"
  B=$W/bp
  PROJECT=./tests/BreakfastProvider.Tests.Component.ReqNRoll/BreakfastProvider.Tests.Component.ReqNRoll.csproj; OUT=$B/tests/BreakfastProvider.Tests.Component.ReqNRoll/bin/Debug/net10.0
  rm -rf "$B"; git clone -q --depth 1 https://github.com/lemonlion/BreakfastProvider "$B" || exit 1
  ( cd "$B" || exit 1
    export KRONIKOL_SUITE=reqnroll-in-memory
    python -I - <<'PY'
import pathlib, re
n = 0
for path in pathlib.Path(".").rglob("*"):
    if path.suffix in (".csproj", ".props", ".targets") and path.is_file():
        text = path.read_text(encoding="utf-8")
        new, k = re.subn(r'(Include="Kronikol[^"]*"\s+Version=")[^"]+', r"\g<1>" + __import__("os").environ["CONTROL"], text)
        if k:
            path.write_text(new, encoding="utf-8"); n += k
print("pins moved:", n)
PY
    git log -1 --format='BreakfastProvider at %h' | cut -c1-60
    dotnet build $PROJECT -c Debug -p:WarningLevel=0 2>&1 | grep -E " error |Build succeeded" | sort -u | head -3 | cut -c1-170
    run() { dotnet test $PROJECT -p:WarningLevel=0 --no-build 2>&1 | tail -1 | cut -c1-110; rm -rf "$W/$1"; cp -r "$OUT/Reports" "$W/$1"; }
    run bp-control
    for name in Kronikol.dll Kronikol.ReqNRoll.Core.dll; do cp "$D/$name" "$OUT/"; done
    run bp-fix
    for name in bp-control bp-fix; do echo "$name: BackgroundSteps blocks in Specifications.yml: $(grep -c 'BackgroundSteps:' "$W/$name/Specifications.yml")"; done
    "$K" query diff "$W/bp-control" "$W/bp-fix" 2>&1 | cut -c1-110 | head -6
    python -X utf8 "$H/probes/addresses-across-the-fix.py" "$K" "$W/bp-control" "$W/bp-fix" 2>&1 | tail -3 | cut -c1-120 )
  rm -rf "$B"
fi

if on upgrade; then
  stamp "history, query diff and a merge: seven runs on the published $CONTROL, then one on the release"
  # it wants the ledger, which this script's own setting turns off
  env -u KRONIKOL_HISTORY KronikolVersion="$CONTROL" bash "$H/probes/upgrade-across-the-fix.sh" "$W" "$K" "$D" 2>&1 | cut -c1-170
fi

if on playwright; then
  stamp "the whole Playwright suite"
  dotnet test tests/Kronikol.Tests.EndToEnd -c Release -p:GeneratePackageOnBuild=false 2>&1 | sumline
fi
stamp "finished"
