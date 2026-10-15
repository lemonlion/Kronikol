#!/bin/sh
# R1's managed-listener-only mutants on Linux, over a copy of the tree linux-r1.sh left in /root/k173/r1/tree (built).
# Each overlay file is touched after the copy: it is older than the tree's build output, and MSBuild would otherwise
# call the build up to date and test the unmutated binaries.
set -u
BASE=/root/k173/r1
for MUTANT in "$BASE"/mutants/*; do
  KEY=$(basename "$MUTANT")
  rm -rf "$BASE/mutated"
  cp -a "$BASE/tree" "$BASE/mutated"
  cp -a "$MUTANT"/. "$BASE/mutated/"
  (cd "$MUTANT" && find . -type f) | while read -r FILE; do touch "$BASE/mutated/$FILE"; done
  podman run --rm --name "k173-r1-mutant-$KEY" -v "$BASE/mutated":/repo -v /root/nuget:/root/.nuget/packages -w /repo \
    mcr.microsoft.com/dotnet/sdk:10.0 dotnet test tests/Kronikol.Tests.ProxyTap --blame-hang-timeout 10m \
    --filter "FullyQualifiedName~ProxyTapStallTests" > "$BASE/mutant-$KEY.log" 2>&1
  echo "== mutant-$KEY exit $?"
  grep -E "Passed!|Failed!|^\s+Failed |error" "$BASE/mutant-$KEY.log" | head -20
done
rm -rf "$BASE/mutated"
