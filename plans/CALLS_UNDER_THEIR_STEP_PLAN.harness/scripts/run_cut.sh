#!/bin/bash
# Packs a worktree's current state and reads the probe suite and the service probes on it: for a cut of the final build (R1 alone) or a stack (#222's R1 under it).
# Usage: run_cut.sh <worktree> <feed dir> <version> <work dir> <label> <results dir> <Kronikol.Tool.dll, or - for the worktree's own>
set -u
export MSBUILDDISABLENODEREUSE=1
W=$1; FEED=$2; VER=$3; WORK=$4; LABEL=$5; RESULTS=$6; TOOL=$7
HERE=$(cd "$(dirname "$0")/.." && pwd)
mkdir -p "$FEED" "$WORK"
cd "$W" || exit 1
for id in Kronikol Kronikol.StepTracking Kronikol.AssertionTracking Kronikol.ReqNRoll.Core Kronikol.ReqNRoll.xUnit2 Kronikol.xUnit3; do
  dotnet build "src/$id/$id.csproj" -c Release -p:Version="$VER" -p:WarningLevel=0 -v q -nologo 2>&1 | grep -E " error " | sed -E 's/\[[^]]*\]$//' | sort -u | head -3 | cut -c1-200
  dotnet pack "src/$id/$id.csproj" -c Release --no-build -p:Version="$VER" -o "$FEED" -v q -nologo 2>&1 | grep -E " error " | head -3 | cut -c1-200
done
if [ "$TOOL" = "-" ]; then
  dotnet build src/Kronikol.Tool/Kronikol.Tool.csproj -c Release -p:WarningLevel=0 -v q -nologo 2>&1 | grep -E " error " | sort -u | head -3 | cut -c1-200
  TOOL=$(cygpath -m "$W/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll")
fi
rm -rf ~/.nuget/packages/kronikol*/"$VER" 2>/dev/null
bash "$HERE/scripts/run_repro.sh" "$WORK" "$LABEL" "$VER" "$FEED" dotnet "$TOOL" > "$RESULTS/repro-$LABEL.txt" 2>&1
bash "$HERE/scripts/run_service_probes.sh" "$WORK" "$LABEL" "$VER" "$FEED" dotnet "$TOOL" > "$RESULTS/service-$LABEL.txt" 2>&1
echo "== $LABEL done $(date +%H:%M:%S)"
