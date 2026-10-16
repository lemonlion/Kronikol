#!/bin/sh
# The #173 audit on Linux: the TcpTap, ProxyTap and Otlp suites on one tree.
# $1 = the tree's tgz under /mnt/c; $2 = a label (fix, red, ...). SUITES picks fewer; FILTER passes a --filter.
set -u
TGZ="$1"
LABEL="$2"
BASE=/root/k173a/$LABEL
mkdir -p "$BASE" /root/nuget
rm -rf "$BASE/tree"
mkdir -p "$BASE/tree"
tar -xzmf "$TGZ" -C "$BASE/tree"

for SUITE in ${SUITES:-TcpTap ProxyTap Otlp}; do
  if [ -n "${FILTER:-}" ]; then
    podman run --rm --name "k173a-$LABEL-$SUITE" -v "$BASE/tree":/repo -v /root/nuget:/root/.nuget/packages -w /repo \
      mcr.microsoft.com/dotnet/sdk:10.0 dotnet test "tests/Kronikol.Tests.$SUITE" --blame-hang-timeout 10m \
      --filter "$FILTER" --logger "console;verbosity=normal" > "$BASE/$SUITE.log" 2>&1
  else
    podman run --rm --name "k173a-$LABEL-$SUITE" -v "$BASE/tree":/repo -v /root/nuget:/root/.nuget/packages -w /repo \
      mcr.microsoft.com/dotnet/sdk:10.0 dotnet test "tests/Kronikol.Tests.$SUITE" --blame-hang-timeout 10m > "$BASE/$SUITE.log" 2>&1
  fi
  echo "== $SUITE exit $?"
  grep -E "Passed!|Failed!|\[FAIL\]|^\s+Failed |error CS" "$BASE/$SUITE.log" | head -30
done
