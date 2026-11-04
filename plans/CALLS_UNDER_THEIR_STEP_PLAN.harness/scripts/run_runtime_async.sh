#!/bin/bash
# probes-runtime-async on a prototype with #226's weave: state machines, then runtime async.
# Usage: run_runtime_async.sh <prototype worktree, with #226's patch on it> <work dir> <version> <tool command...>
set -u
HERE=$(cd "$(dirname "$0")/.." && pwd)
PROTO=$1; WORK=$2; VER=$3; shift 3
FEED="$WORK/feed"
mkdir -p "$FEED"
cd "$PROTO" || exit 1
for id in Kronikol Kronikol.StepTracking Kronikol.AssertionTracking Kronikol.xUnit3; do
  dotnet build "src/$id/$id.csproj" -c Release -p:Version="$VER" -p:WarningLevel=0 -v q -nologo 2>&1 | grep -E " error " | sort -u | head -3 | cut -c1-200
  dotnet pack "src/$id/$id.csproj" -c Release --no-build -p:Version="$VER" -o "$FEED" -v q -nologo 2>&1 | grep -E " error " | head -3 | cut -c1-200
done
for mode in state-machines runtime-async; do
  D="$WORK/$mode"
  rm -rf "$D"; mkdir -p "$D"
  cp "$HERE"/probes-runtime-async/*.cs "$HERE"/probes-runtime-async/*.csproj "$D"/
  cat > "$D/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear />
<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
<add key="local" value="$(cygpath -w "$FEED")" />
</packageSources></configuration>
EOF
  cd "$D" || exit 1
  echo "=== $mode"
  if [ "$mode" = runtime-async ]; then
    dotnet build -c Release -p:KronikolVersion="$VER" -p:RuntimeAsyncOn=true -p:WarningLevel=0 -v q -nologo 2>&1 | grep -E " error |rror\(s\)|warning KRONIKOL" | sort -u | head -5 | cut -c1-200
    DOTNET_RuntimeAsync=1 dotnet test -c Release --no-build -p:KronikolVersion="$VER" -p:RuntimeAsyncOn=true 2>&1 | grep -E "^(Passed!|Failed!)|InvalidProgram|Fatal" | sort -u | head -6 | cut -c1-220
  else
    dotnet test -c Release -p:KronikolVersion="$VER" -p:WarningLevel=0 2>&1 | grep -E " error |^(Passed!|Failed!)" | sort -u | head -6 | cut -c1-220
  fi
  python "$HERE/scripts/read_calls.py" "$D/bin/Release/net10.0/Reports" "$@"
done
