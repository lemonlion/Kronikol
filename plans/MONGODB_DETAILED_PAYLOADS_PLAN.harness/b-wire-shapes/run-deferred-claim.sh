#!/bin/sh
# Runs deferred-claim.cs against driver 3.12.0 and 2.30.0 (Kronikol 4.18.1 packages); needs start-mongo.sh first.
# Usage: sh run-deferred-claim.sh   (writes results/deferred-claim-driver-<v>.txt)
HERE=$(cd "$(dirname "$0")" && pwd)
WORK=${WORK:-${TMPDIR:-/tmp}/kronikol168-wire-shapes}
mkdir -p "$HERE/results"
for v in 3.12.0 2.30.0; do
  dir="$WORK/deferred-$v"
  rm -rf "$dir"; mkdir -p "$dir"
  case "$v" in
    2.*) {
           echo "#:package MongoDB.Driver@$v"
           echo "#:package Kronikol.Extensions.MongoDB.V2@4.18.1"
           echo '#:property DefineConstants=$(DefineConstants);DRIVER2'
           echo '#:property NoWarn=$(NoWarn);NU1902;NU1903;CS0618'
           echo '#:property Nullable=enable'
         } > "$dir/deferred-claim.cs"
         tail -n +6 "$HERE/deferred-claim.cs" >> "$dir/deferred-claim.cs" ;;
    *)   sed "1s/.*/#:package MongoDB.Driver@$v/" "$HERE/deferred-claim.cs" > "$dir/deferred-claim.cs" ;;
  esac
  echo "== MongoDB.Driver $v"
  (cd "$dir" && dotnet run deferred-claim.cs) > "$HERE/results/deferred-claim-driver-$v.txt" 2>&1
  echo "   exit $? -> results/deferred-claim-driver-$v.txt"
done
