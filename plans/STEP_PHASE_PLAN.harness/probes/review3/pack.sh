#!/usr/bin/env bash
# Usage: pack.sh  -- packs R/wt as $VERSION into R/probes/localfeed
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
WORKTREE="${WORKTREE:-$HERE/../wt}"
VERSION="${VERSION:-5.28.3-rev3}"
FEED="$HERE/localfeed"
mkdir -p "$FEED"
rm -f "$FEED"/*"$VERSION"*.nupkg
for project in Kronikol Kronikol.StepTracking Kronikol.ReqNRoll.Core Kronikol.ReqNRoll.xUnit2 Kronikol.LightBDD.Core Kronikol.LightBDD.xUnit2 Kronikol.xUnit2 Kronikol.xUnit3 Kronikol.BDDfy.xUnit3 Kronikol.AssertionTracking; do
    if [ -d "$WORKTREE/src/$project" ]; then
        echo "== $project"
        (cd "$WORKTREE/src/$project" && dotnet build -c Release -p:Version="$VERSION" -p:PackageVersion="$VERSION" -p:GeneratePackageOnBuild=true -p:PackageOutputPath="$FEED" --nologo -v q 2>&1 | grep -E " error |Build FAILED" | cut -c1-220 | head -5)
    fi
done
ls "$FEED" | grep -c "$VERSION" | sed "s/^/packages for $VERSION: /"
for cached in "$USERPROFILE"/.nuget/packages/kronikol*/"$VERSION"; do rm -rf "$cached" 2>/dev/null; done
echo PACK_DONE
