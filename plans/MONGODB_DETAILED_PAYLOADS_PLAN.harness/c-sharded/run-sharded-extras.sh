#!/bin/sh
# Runs sharded-extras.cs through mongos with each driver version; needs start-sharded.sh first.
# Usage (Git Bash, .NET 10 SDK): sh run-sharded-extras.sh [versions...]   (default 3.12.0 2.30.0)
# Writes results/sharded-extras-driver-<v>-mongos8.txt, converted to LF. MONGO_URL overrides the mongos URL.
# The copies are written outside the repo (WORK) so the repo's Directory.Build.props is not imported; for 2.x the
# copy's first line names the version and its second defines DRIVER2 in place of DRIVER3.
HERE=$(cd "$(dirname "$0")" && pwd)
WORK=${WORK:-${TMPDIR:-/tmp}/kronikol168-sharded}
VERSIONS=${*:-"3.12.0 2.30.0"}
mkdir -p "$HERE/results"
for v in $VERSIONS; do
  dir="$WORK/sharded-extras-$v"
  rm -rf "$dir"; mkdir -p "$dir"
  case "$v" in
    2.*) sed -e "1s/.*/#:package MongoDB.Driver@$v/" -e '2s/DRIVER3/DRIVER2/' "$HERE/sharded-extras.cs" > "$dir/sharded-extras.cs" ;;
    *)   sed "1s/.*/#:package MongoDB.Driver@$v/" "$HERE/sharded-extras.cs" > "$dir/sharded-extras.cs" ;;
  esac
  out="$HERE/results/sharded-extras-driver-$v-mongos8.txt"
  echo "== MongoDB.Driver $v"
  (cd "$dir" && dotnet run sharded-extras.cs) > "$out" 2>&1
  rc=$?
  sed -i 's/\r//g' "$out"   # every CR, as in ../b-wire-shapes/results
  echo "   exit $rc -> results/$(basename "$out") ($(wc -l < "$out") lines)"
done
