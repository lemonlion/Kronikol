#!/bin/sh
# Runs wire-shapes.cs against driver 3.12.0 (with Kronikol.Extensions.MongoDB 4.18.1) and driver 2.30.0 (with
# Kronikol.Extensions.MongoDB.V2 4.18.1), each writing results/wire-shapes-driver-<v>[-<tag>].txt.
# Needs a server: start-mongo.sh (mongo:7.0 replica set on 127.0.0.1:27217) first; MONGO_URL overrides the URL.
# Usage: sh run-wire-shapes.sh [tag] [versions...]     e.g. sh run-wire-shapes.sh mongo7 3.12.0 2.30.0
# The copies are written outside the repo (WORK) so the repo's Directory.Build.props is not imported.
HERE=$(cd "$(dirname "$0")" && pwd)
WORK=${WORK:-${TMPDIR:-/tmp}/kronikol168-wire-shapes}
TAG=${1:-mongo7}
shift 2>/dev/null
VERSIONS=${*:-"3.12.0 2.30.0"}
mkdir -p "$HERE/results"
for v in $VERSIONS; do
  dir="$WORK/driver-$v"
  rm -rf "$dir"; mkdir -p "$dir"
  case "$v" in
    2.*) {
           echo "#:package MongoDB.Driver@$v"
           echo "#:package Kronikol.Extensions.MongoDB.V2@4.18.1"
           echo '#:property DefineConstants=$(DefineConstants);DRIVER2'
           echo '#:property NoWarn=$(NoWarn);NU1902;NU1903;CS0618'
           echo "#:package MongoDB.Driver.GridFS@$v"
         } > "$dir/wire-shapes.cs"
         tail -n +6 "$HERE/wire-shapes.cs" >> "$dir/wire-shapes.cs" ;;
    *)   sed "1s/.*/#:package MongoDB.Driver@$v/" "$HERE/wire-shapes.cs" > "$dir/wire-shapes.cs" ;;
  esac
  out="$HERE/results/wire-shapes-driver-$v-$TAG.txt"
  echo "== MongoDB.Driver $v ($TAG)"
  (cd "$dir" && dotnet run wire-shapes.cs) > "$out" 2>&1
  echo "   exit $? -> results/$(basename "$out") ($(wc -l < "$out") lines)"
done
