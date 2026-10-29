#!/usr/bin/env bash
# Runs P1 and P3 against a Kronikol source tree in place of the released package: the tree r1-walk.patch was applied to
# (the prototype of section 5.1, which is research, not R1). usage: run.sh <Kronikol tree> <scratch dir>
set -eu
tree=$(cd "$1" && (pwd -W 2>/dev/null || pwd))
scratch=$2
here=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$scratch"
for probe in ../p1-forwarders/forwarders.cs ../p3-own-spans/own-spans.cs; do
  name=$(basename "$probe")
  sed -e "s|^#:package Kronikol@.*|#:project $tree/src/Kronikol/Kronikol.csproj|" "$here/$probe" > "$scratch/$name"
done
(cd "$scratch" && dotnet run --file forwarders.cs -- "$scratch/reports")
echo
echo "\$ query parties"
(cd "$scratch/reports" && dotnet run --file query.cs -- parties "$scratch/reports")
echo
(cd "$scratch" && dotnet run --file own-spans.cs)
