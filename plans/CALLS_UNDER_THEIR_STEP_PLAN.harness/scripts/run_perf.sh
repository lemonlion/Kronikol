#!/bin/bash
# What logging a record costs, published against a prototype's packages, two rounds of each, alternating.
# Usage: run_perf.sh <feed> <version> <work dir> <out file>
set -u
export MSBUILDDISABLENODEREUSE=1
FEED=$1; VER=$2; WORK=$3; OUT=$4
HERE=$(cd "$(dirname "$0")/.." && pwd)
mkdir -p "$WORK"
cp "$HERE"/perf/perf.cs "$HERE"/perf/perf.csproj "$WORK"/
cat > "$WORK/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear />
<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
<add key="local" value="$(cygpath -w "$FEED")" />
</packageSources></configuration>
EOF
cd "$WORK" || exit 1
echo "On the final cut. Published 5.28.2 first, each round; best of 5." > "$OUT"
for round in 1 2; do
  for v in 5.28.2 "$VER"; do
    echo "--- round $round, $v" >> "$OUT"
    rm -rf bin obj
    dotnet run -c Release -p:KronikolVersion="$v" -p:WarningLevel=0 2>&1 | grep -E "per record| error " | cut -c1-90 >> "$OUT"
  done
done
cat "$OUT"
