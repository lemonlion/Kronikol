#!/bin/sh
# R3 on Linux: the TcpTap, ProxyTap and Otlp suites (all three compile the watch) on the release's tree.
# $1 = the tree's tgz under /mnt/c.
set -u
TGZ="$1"
BASE=/root/k173/r3
mkdir -p "$BASE" /root/nuget
rm -rf "$BASE/tree"
mkdir -p "$BASE/tree"
tar -xzmf "$TGZ" -C "$BASE/tree"

for SUITE in ${SUITES:-TcpTap ProxyTap Otlp}; do
  podman run --rm --name "k173-r3-$SUITE" -v "$BASE/tree":/repo -v /root/nuget:/root/.nuget/packages -w /repo \
    mcr.microsoft.com/dotnet/sdk:10.0 dotnet test "tests/Kronikol.Tests.$SUITE" --blame-hang-timeout 10m > "$BASE/$SUITE.log" 2>&1
  echo "== $SUITE exit $?"
  grep -E "Passed!|Failed!|^\s+Failed |error" "$BASE/$SUITE.log" | head -20
done
