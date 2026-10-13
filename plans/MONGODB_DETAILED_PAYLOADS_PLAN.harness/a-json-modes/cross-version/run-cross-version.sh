#!/bin/sh
# Runs cross-version.cs with the host's MongoDB.Bson at 3.12.0 and at 2.30.0 (the library stays compiled against 2.30.0).
# Copies everything outside the repo first (WORK) so the repo's Directory.Build.props is not imported.
# Usage: sh run-cross-version.sh   (writes ../results/cross-version-host-bson-<v>.txt)
HERE=$(cd "$(dirname "$0")" && pwd)
WORK=${WORK:-${TMPDIR:-/tmp}/kronikol168-json-modes}/cross-version
for v in 3.12.0 2.30.0; do
  dir="$WORK/host-$v"
  rm -rf "$dir"; mkdir -p "$dir"
  cp -r "$HERE/Bson2Writer" "$dir/"
  sed "1s/.*/#:package MongoDB.Bson@$v/" "$HERE/cross-version.cs" > "$dir/cross-version.cs"
  echo "== host MongoDB.Bson $v"
  (cd "$dir" && dotnet run cross-version.cs) > "$HERE/../results/cross-version-host-bson-$v.txt" 2>&1
  echo "   exit $? -> results/cross-version-host-bson-$v.txt"
done
