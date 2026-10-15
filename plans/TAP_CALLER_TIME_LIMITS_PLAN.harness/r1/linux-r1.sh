#!/bin/sh
# R1 on Linux: the whole ProxyTap suite on the release's tree, then each managed-listener-only mutant over a copy of it,
# with R1's facts. $1 = the tree's tgz under /mnt/c, $2 = the mutants' tgz (a directory per mutation, overlay files).
set -u
TGZ="$1"; MUTANTS="$2"
BASE=/root/k173/r1
mkdir -p "$BASE" /root/nuget
rm -rf "$BASE/tree" "$BASE/mutants"
mkdir -p "$BASE/tree" "$BASE/mutants"
tar -xzmf "$TGZ" -C "$BASE/tree"
tar -xzmf "$MUTANTS" -C "$BASE/mutants"

run() {
  # $1 = tree, $2 = label, $3 = filter or empty
  if [ -n "$3" ]; then
    podman run --rm --name "k173-r1-$2" -v "$1":/repo -v /root/nuget:/root/.nuget/packages -w /repo \
      mcr.microsoft.com/dotnet/sdk:10.0 dotnet test tests/Kronikol.Tests.ProxyTap --blame-hang-timeout 10m --filter "$3" > "$BASE/$2.log" 2>&1
  else
    podman run --rm --name "k173-r1-$2" -v "$1":/repo -v /root/nuget:/root/.nuget/packages -w /repo \
      mcr.microsoft.com/dotnet/sdk:10.0 dotnet test tests/Kronikol.Tests.ProxyTap --blame-hang-timeout 10m > "$BASE/$2.log" 2>&1
  fi
  echo "== $2 exit $?"
  grep -E "Passed!|Failed!|^\s+Failed |error" "$BASE/$2.log" | head -30
}

run "$BASE/tree" suite ""
for MUTANT in "$BASE"/mutants/*; do
  KEY=$(basename "$MUTANT")
  rm -rf "$BASE/mutated"
  cp -a "$BASE/tree" "$BASE/mutated"
  cp -a "$MUTANT"/. "$BASE/mutated/"
  run "$BASE/mutated" "mutant-$KEY" "FullyQualifiedName~ProxyTapStallTests"
done
rm -rf "$BASE/mutated"
