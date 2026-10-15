#!/bin/sh
# The #165 audit on Linux (podman WSL distro, dotnet/sdk:10.0 container): the weaver suite on the audit's tree (suite),
# and the decoding facts with the C1 mutant, 4.22.1's rule (Windows-1252 for every file that is not UTF-8), which only
# Linux's compiler disagrees with (c1).
# Usage (from Windows): MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- sh <this dir>/linux-audit.sh <mode>
set -u
HERE=/mnt/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/1a468123-a7e8-45cc-b875-791076e4d49a/scratchpad/audit
MODE=${1:-suite}
mkdir -p /root/nuget
rm -rf /root/audit165 && mkdir -p /root/audit165
tar -xzmf "$HERE/audit165.tgz" -C /root/audit165
FILTER=""
if [ "$MODE" = "c1" ]; then
  sed -i 's/return onWindows ? Windows1252(bytes) : ReplacingUtf8.GetString(bytes);/return Windows1252(bytes);/' \
    /root/audit165/src/Kronikol.AssertionTracking/SourceDocuments.cs
  echo "mutant lines: $(grep -c '            return Windows1252(bytes);' /root/audit165/src/Kronikol.AssertionTracking/SourceDocuments.cs)"
  FILTER="--filter 'FullyQualifiedName~A_bom_less_windows_1252_file_is_read_as_the_compiler_reads_it|FullyQualifiedName~A_file_that_is_not_utf8'"
fi
podman run --rm --name "k165-$MODE" -v /root/audit165:/repo -v /root/nuget:/root/.nuget/packages -w /repo mcr.microsoft.com/dotnet/sdk:10.0 \
  sh -c "dotnet test tests/Kronikol.Tests.AssertionTracking/Kronikol.Tests.AssertionTracking.csproj -f net10.0 $FILTER 2>&1" \
  > "$HERE/linux-$MODE.txt" 2>&1
echo "exit $?" >> "$HERE/linux-$MODE.txt"
tail -5 "$HERE/linux-$MODE.txt"
