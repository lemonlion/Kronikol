#!/bin/sh
# Runs ../b-wire-shapes/wire-shapes.cs against the c-sharded cluster, with MONGO_URL as the only change: the copy's
# #:package header is rewritten per driver version exactly as ../b-wire-shapes/run-wire-shapes.sh does it. Writes
# results/wire-shapes-driver-<v>-<tag>.txt here, converted to LF. Needs start-sharded.sh first.
# Usage (Git Bash, .NET 10 SDK): sh run-wire-shapes-sharded.sh <tag> <url> [versions...]
#   sh run-wire-shapes-sharded.sh mongos8 'mongodb://127.0.0.1:27230/?directConnection=false' 3.12.0 2.30.0
#   sh run-wire-shapes-sharded.sh shard8  'mongodb://127.0.0.1:27232/?directConnection=true'  3.12.0 2.30.0
# The copies are written outside the repo (WORK) so the repo's Directory.Build.props is not imported.
HERE=$(cd "$(dirname "$0")" && pwd)
SRC="$HERE/../b-wire-shapes/wire-shapes.cs"
WORK=${WORK:-${TMPDIR:-/tmp}/kronikol168-sharded}
TAG=${1:?tag}
URL=${2:?url}
shift 2
VERSIONS=${*:-"3.12.0 2.30.0"}
mkdir -p "$HERE/results"
for v in $VERSIONS; do
  dir="$WORK/wire-shapes-$TAG-$v"
  rm -rf "$dir"; mkdir -p "$dir"
  case "$v" in
    2.*) {
           echo "#:package MongoDB.Driver@$v"
           echo "#:package Kronikol.Extensions.MongoDB.V2@4.18.1"
           echo '#:property DefineConstants=$(DefineConstants);DRIVER2'
           echo '#:property NoWarn=$(NoWarn);NU1902;NU1903;CS0618'
           echo "#:package MongoDB.Driver.GridFS@$v"
         } > "$dir/wire-shapes.cs"
         tail -n +6 "$SRC" >> "$dir/wire-shapes.cs" ;;
    *)   sed "1s/.*/#:package MongoDB.Driver@$v/" "$SRC" > "$dir/wire-shapes.cs" ;;
  esac
  out="$HERE/results/wire-shapes-driver-$v-$TAG.txt"
  echo "== MongoDB.Driver $v ($TAG, $URL)"
  (cd "$dir" && MONGO_URL="$URL" dotnet run wire-shapes.cs) > "$out" 2>&1
  rc=$?
  sed -i 's/\r//g' "$out"   # every CR: an exception message's CRLF survives the probe's Replace("\n", " ") as "\r "
  echo "   exit $rc -> results/$(basename "$out") ($(wc -l < "$out") lines)"
done
