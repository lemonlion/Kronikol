#!/usr/bin/env bash
# Packs the worktree as 5.28.3-<suffix> into localfeed. Usage: pack-rev4.sh <worktree> <suffix> [extra projects...]
set -u
WORKTREE="$1"; SUFFIX="$2"; shift 2
HERE="$(cd "$(dirname "$0")" && pwd)"
VERSION="5.28.3-$SUFFIX"
FEED="$HERE/localfeed"
mkdir -p "$FEED"
for project in Kronikol Kronikol.StepTracking Kronikol.ReqNRoll.Core Kronikol.ReqNRoll.xUnit2 Kronikol.LightBDD.Core Kronikol.LightBDD.xUnit2 Kronikol.xUnit2 Kronikol.xUnit3 Kronikol.BDDfy.xUnit3 Kronikol.AssertionTracking "$@"; do
    if [ -d "$WORKTREE/src/$project" ]; then
        (cd "$WORKTREE/src/$project" && dotnet build -c Release -p:Version="$VERSION" -p:PackageVersion="$VERSION" -p:GeneratePackageOnBuild=true -p:PackageOutputPath="$FEED" --nologo -v q 2>&1 | grep -E " error |Build FAILED" | cut -c1-220 | head -5)
    fi
done
ls "$FEED" | grep -c "$VERSION" | sed "s/^/packages for $VERSION: /"
for cached in "$USERPROFILE"/.nuget/packages/kronikol*/"$VERSION"; do rm -rf "$cached" 2>/dev/null; done
