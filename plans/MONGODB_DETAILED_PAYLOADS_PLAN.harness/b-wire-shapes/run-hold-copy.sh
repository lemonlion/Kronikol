#!/bin/sh
# Runs hold-copy.cs with driver 3.12.0 and 2.30.0; needs start-mongo.sh first.
# Usage: sh run-hold-copy.sh   (writes results/hold-copy-driver-<v>.txt)
HERE=$(cd "$(dirname "$0")" && pwd)
WORK=${WORK:-${TMPDIR:-/tmp}/kronikol168-wire-shapes}
mkdir -p "$HERE/results"
for v in 3.12.0 2.30.0; do
  dir="$WORK/hold-$v"
  rm -rf "$dir"; mkdir -p "$dir"
  sed "1s/.*/#:package MongoDB.Driver@$v/" "$HERE/hold-copy.cs" > "$dir/hold-copy.cs"
  echo "== MongoDB.Driver $v"
  (cd "$dir" && dotnet run hold-copy.cs) > "$HERE/results/hold-copy-driver-$v.txt" 2>&1
  echo "   exit $? -> results/hold-copy-driver-$v.txt"
done
