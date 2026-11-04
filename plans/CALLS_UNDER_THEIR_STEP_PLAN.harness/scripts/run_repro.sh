#!/bin/bash
# The probe suite (repro/) on a given Kronikol version, printing every scenario's rows and each call's stepPath.
# Usage: run_repro.sh <work dir> <label> <version> <feed dir or -> <tool command...>
# Works in a copy outside this repo, so no Directory.Build.props is picked up. PROBE_NO_BARS=1 in the environment reads the same suite with bars off.
set -u
HERE=$(cd "$(dirname "$0")/.." && pwd)
WORK=$1; LABEL=$2; VER=$3; FEED=$4; shift 4
D="$WORK/repro-$LABEL"
rm -rf "$D"; mkdir -p "$D"
cp -r "$HERE"/repro/. "$D"/
rm -rf "$D/bin" "$D/obj"
sed -i -E "s/(Include=\"Kronikol[^\"]*\" Version=\")[^\"]*\"/\1$VER\"/" "$D/Spike.Substeps.csproj"
{
  echo '<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear />'
  echo '<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'
  [ "$FEED" != "-" ] && echo "<add key=\"local\" value=\"$(cygpath -w "$FEED")\" />"
  echo '</packageSources></configuration>'
} > "$D/nuget.config"
cd "$D" || exit 1
dotnet test -c Release -p:WarningLevel=0 2>&1 | grep -E " error |^(Passed!|Failed!)" | sort -u | head -6 | cut -c1-200
python "$HERE/scripts/read_calls.py" "$D/bin/Release/net10.0/Reports" "$@"
