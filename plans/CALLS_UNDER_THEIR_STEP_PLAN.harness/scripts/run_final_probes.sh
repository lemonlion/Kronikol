#!/bin/bash
# Every probe of this harness on the final cut's packages against published 5.28.2, each printing rows and each call's step through the query engine.
# Usage: run_final_probes.sh <probe build worktree> <feed> <version> <work dir> <results dir> <Kronikol.Tool.dll>
set -u
export MSBUILDDISABLENODEREUSE=1
PROTO=$1; FEED=$2; VER=$3; WORK=$4; RESULTS=$5; TOOL=$6
HERE=$(cd "$(dirname "$0")/.." && pwd)
mkdir -p "$WORK"
cd "$PROTO" || exit 1
for id in Kronikol.ReqNRoll.xUnit2 Kronikol.LightBDD.xUnit3 Kronikol.xUnit3; do
  [ -f "$FEED/$id.$VER.nupkg" ] && continue
  dotnet build "src/$id/$id.csproj" -c Release -p:Version=$VER -p:WarningLevel=0 -v q -nologo 2>&1 | grep -E " error " | sort -u | head -3 | cut -c1-200
  dotnet pack "src/$id/$id.csproj" -c Release --no-build -p:Version=$VER -o "$FEED" -v q -nologo 2>&1 | grep -E " error " | head -3 | cut -c1-200
done
rm -rf ~/.nuget/packages/kronikol*/"$VER" 2>/dev/null
bash "$HERE/scripts/run_repro.sh" "$WORK" final "$VER" "$FEED" dotnet "$TOOL" > "$RESULTS/repro-final.txt" 2>&1
PROBE_NO_BARS=1 bash "$HERE/scripts/run_repro.sh" "$WORK" final-nobars "$VER" "$FEED" dotnet "$TOOL" > "$RESULTS/repro-final-nobars.txt" 2>&1
bash "$HERE/scripts/run_service_probes.sh" "$WORK" final "$VER" "$FEED" dotnet "$TOOL" > "$RESULTS/service-final.txt" 2>&1
echo "== done $(date +%H:%M:%S)"
