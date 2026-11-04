#!/bin/bash
# probes-service on published, on a prototype's packages, and what each reads.
# Usage: run_service_probes.sh <work dir> <label> <Kronikol version> <feed dir or -> <tool command...>
set -u
HERE=$(cd "$(dirname "$0")/.." && pwd)
WORK=$1; LABEL=$2; VER=$3; FEED=$4; shift 4
D="$WORK/service-$LABEL"
rm -rf "$D"; mkdir -p "$D"
cp "$HERE"/probes-service/*.cs "$HERE"/probes-service/*.csproj "$D"/
{
  echo '<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear />'
  echo '<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'
  [ "$FEED" != "-" ] && echo "<add key=\"local\" value=\"$(cygpath -w "$FEED")\" />"
  echo '</packageSources></configuration>'
} > "$D/nuget.config"
cd "$D" || exit 1
dotnet test -c Release -p:KronikolVersion="$VER" -p:WarningLevel=0 2>&1 | grep -E " error |^(Passed!|Failed!)|Test Run|failed:|succeeded:" | sort -u | head -8 | cut -c1-200
python "$HERE/scripts/read_calls.py" "$D/bin/Release/net10.0/Reports" "$@"
