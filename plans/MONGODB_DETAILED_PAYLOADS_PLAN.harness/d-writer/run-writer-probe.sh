#!/bin/sh
# Usage: sh run-writer-probe.sh [versions]   (default 2.30.0 3.12.0). Copies the probe outside the repo first.
HERE=$(cd "$(dirname "$0")" && pwd)
WORK=${WORK:-${TMPDIR:-/tmp}/kronikol168-writer}
for V in ${*:-2.30.0 3.12.0}; do
  mkdir -p "$WORK/$V" && sed "s/^#:package MongoDB.Bson@.*/#:package MongoDB.Bson@$V/" "$HERE/writer-probe.cs" > "$WORK/$V/writer-probe.cs"
  (cd "$WORK/$V" && dotnet run writer-probe.cs) 2>&1 | tr -d '\r' > "$HERE/results/writer-probe-bson-$V.txt"
  echo "bson $V -> results/writer-probe-bson-$V.txt"
done
