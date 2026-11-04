#!/usr/bin/env bash
# Usage: pack-variant.sh <worktree> <version>, e.g. pack-variant.sh ../wt 5.28.3-shapes3
# Packs every adapter the probes use from the worktree into localfeed, and drops the NuGet cache's copies of that version
# (a rebuilt package is cached by version, and a probe would silently read the old one).
set -u
WORKTREE="$1"
VERSION="$2"
HERE="$(cd "$(dirname "$0")" && pwd)"
FEED="$HERE/localfeed"
mkdir -p "$FEED"
for project in Kronikol.StepTracking Kronikol Kronikol.xUnit2 Kronikol.xUnit3 Kronikol.NUnit4 Kronikol.MSTest Kronikol.TUnit Kronikol.ReqNRoll.Core Kronikol.ReqNRoll.xUnit2 Kronikol.ReqNRoll.xUnit3 Kronikol.ReqNRoll.TUnit Kronikol.LightBDD.Core Kronikol.LightBDD.xUnit2 Kronikol.BDDfy.xUnit3 Kronikol.AssertionTracking; do
    if [ -d "$WORKTREE/src/$project" ]; then
        echo "== $project"
        (cd "$WORKTREE/src/$project" && dotnet build -c Release -p:Version="$VERSION" -p:PackageVersion="$VERSION" -p:GeneratePackageOnBuild=true -p:PackageOutputPath="$FEED" --nologo -v q 2>&1 | grep -E " error |Build FAILED" | cut -c1-240 | head -5)
    fi
done
ls "$FEED" | grep -c "$VERSION" | sed "s/^/packages for $VERSION: /"
ls "$FEED" | grep "$VERSION"
for cached in "$USERPROFILE"/.nuget/packages/kronikol*/"$VERSION"; do rm -rf "$cached" 2>/dev/null; done
