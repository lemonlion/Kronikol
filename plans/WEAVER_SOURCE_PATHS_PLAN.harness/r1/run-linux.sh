#!/bin/sh
# Runs the weaver suite on Linux (podman WSL distro, dotnet/sdk:10.0 container), and then the same facts with
# mutant M20 (the rest of a mapped name keeps its separators), which only Linux can catch.
# Usage (from Windows): wsl -d podman-machine-default -u root -- sh <S>/linux165/run-linux.sh <mode>
#   mode: suite | m20
set -u
HERE=<S>/linux165
MODE=${1:-suite}
mkdir -p /root/nuget
rm -rf /root/k165 && mkdir -p /root/k165
tar -xzmf "$HERE/k165.tgz" -C /root/k165
FILTER=""
if [ "$MODE" = "m20" ]; then
  # The mutant: the rest of a mapped name is joined to the local directory as the name has it.
  sed -i 's/var candidate = entry.Key + WithSeparatorsOf(entry.Key, name.Substring(entry.Value.Length));/var candidate = entry.Key + name.Substring(entry.Value.Length);/' \
    /root/k165/src/Kronikol.AssertionTracking/SourceDocuments.cs
  grep -c "var candidate = entry.Key + name.Substring" /root/k165/src/Kronikol.AssertionTracking/SourceDocuments.cs
  FILTER='--filter FullyQualifiedName~DeterministicSourcePathsTests'
fi
podman run --rm -v /root/k165:/repo -v /root/nuget:/root/.nuget/packages -w /repo mcr.microsoft.com/dotnet/sdk:10.0 \
  sh -c "dotnet test tests/Kronikol.Tests.AssertionTracking/Kronikol.Tests.AssertionTracking.csproj -f net10.0 $FILTER 2>&1" \
  > "$HERE/linux-$MODE.txt" 2>&1
echo "exit $?" >> "$HERE/linux-$MODE.txt"
tail -5 "$HERE/linux-$MODE.txt"
