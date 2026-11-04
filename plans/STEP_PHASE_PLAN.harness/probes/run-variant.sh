#!/usr/bin/env bash
# Usage: run-variant.sh <worktree> <version suffix>, e.g. run-variant.sh .../proto issue
# Packs the worktree's adapters and core as 5.27.14-<suffix> into localfeed, then runs every probe against them and
# writes results/<suffix>/*.txt. Baseline: results/5.27.13, from the packages on nuget.org (no script needed:
# PHASE_PROBE_OUT=results/5.27.13 dotnet test in each probe folder).
set -u
WORKTREE="$1"
SUFFIX="$2"
HERE="$(cd "$(dirname "$0")" && pwd)"
VERSION="5.28.3-$SUFFIX"
FEED="$HERE/localfeed"
OUT="$HERE/../results/$SUFFIX"
mkdir -p "$FEED" "$OUT"

for project in Kronikol Kronikol.StepTracking Kronikol.ReqNRoll.Core Kronikol.ReqNRoll.xUnit2 Kronikol.LightBDD.Core Kronikol.LightBDD.xUnit2 Kronikol.xUnit2 Kronikol.xUnit3 Kronikol.BDDfy.xUnit3 Kronikol.AssertionTracking; do
    if [ -d "$WORKTREE/src/$project" ]; then
        (cd "$WORKTREE/src/$project" && dotnet build -c Release -p:Version="$VERSION" -p:PackageVersion="$VERSION" -p:GeneratePackageOnBuild=true -p:PackageOutputPath="$FEED" --nologo -v q 2>&1 | grep -E " error |Build FAILED" | cut -c1-220 | head -5)
    fi
done
ls "$FEED" | grep -c "$VERSION" | sed "s/^/packages for $VERSION: /"

# A prototype's packages are cached by version: a rebuilt prototype needs its copies gone.
for cached in "$USERPROFILE"/.nuget/packages/kronikol*/"$VERSION"; do rm -rf "$cached" 2>/dev/null; done

for probe in reqnroll-xunit2 xunit3-plain lightbdd-xunit2 bddfy-xunit3; do
    (cd "$HERE/$probe" && rm -rf bin obj && PHASE_PROBE_OUT="$OUT" dotnet test -p:KronikolVersion="$VERSION" --nologo -v q 2>&1 | grep -E "^(Passed|Failed)!| error " | cut -c1-200 | head -5)
done
