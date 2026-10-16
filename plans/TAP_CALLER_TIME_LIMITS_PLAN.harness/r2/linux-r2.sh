#!/bin/sh
# R2 on Linux: the whole Otlp suite on the release's tree. $1 = the tree's tgz under /mnt/c.
set -u
TGZ="$1"
BASE=/root/k173/r2
mkdir -p "$BASE" /root/nuget
rm -rf "$BASE/tree"
mkdir -p "$BASE/tree"
tar -xzmf "$TGZ" -C "$BASE/tree"

podman run --rm --name k173-r2-suite -v "$BASE/tree":/repo -v /root/nuget:/root/.nuget/packages -w /repo \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet test tests/Kronikol.Tests.Otlp --blame-hang-timeout 10m > "$BASE/suite.log" 2>&1
echo "== suite exit $?"
grep -E "Passed!|Failed!|^\s+Failed |error" "$BASE/suite.log" | head -40
