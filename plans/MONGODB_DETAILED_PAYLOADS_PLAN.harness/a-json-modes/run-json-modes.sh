#!/bin/sh
# Runs json-modes.cs once per MongoDB.Bson version and saves each output under results/.
# Usage (Git Bash, .NET 10 SDK): sh run-json-modes.sh [versions...]
# The copies are written outside the repo (WORK, default $TMPDIR/kronikol168-json-modes) so the repo's
# Directory.Build.props is not imported into the file-based apps.
HERE=$(cd "$(dirname "$0")" && pwd)
WORK=${WORK:-${TMPDIR:-/tmp}/kronikol168-json-modes}
VERSIONS=${*:-"2.10.0 2.19.0 2.30.0 3.12.0"}
mkdir -p "$HERE/results" "$WORK"
for v in $VERSIONS; do
  dir="$WORK/bson-$v"
  mkdir -p "$dir"
  sed "1s/.*/#:package MongoDB.Bson@$v/" "$HERE/json-modes.cs" > "$dir/json-modes-bson-$v.cs"
  echo "== MongoDB.Bson $v"
  (cd "$dir" && dotnet run "json-modes-bson-$v.cs") > "$HERE/results/json-modes-bson-$v.txt" 2>&1
  echo "   exit $? -> results/json-modes-bson-$v.txt ($(wc -l < "$HERE/results/json-modes-bson-$v.txt") lines)"
done
