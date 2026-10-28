#!/usr/bin/env bash
# The plan's probe (harness/probe) on local packages of a worktree's code, Debug and Release, under a version label
# nuget.org never has and an isolated NuGet cache, as ASSERTION_VALUE_SUBSTITUTION_PLAN.harness/r2/probe_local.sh.
# Usage: probe_local.sh <worktree> <version-label> <out-dir> [<probe-dir>]
# <probe-dir> defaults to the worktree's own harness/probe (a worktree holding the prototype need not hold the harness).
set -u
WT=${1:-/c/Code/Kronikol-210}
V=${2:-5.12.1-local.210}
OUT=${3:-$WT/plans/ASSERTION_LABELS_PLAN.harness/s1}
PROBE=${4:-$WT/plans/ASSERTION_LABELS_PLAN.harness/probe}
S=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/8bfcad7e-3186-4dfa-a174-0bb2327bb297/scratchpad
FEED=$S/feed-210
P=$S/probe-210-local
LOGS=$S/probe-210-logs
rm -rf "$FEED" "$P" "$LOGS" "$S/nuget-210"; mkdir -p "$FEED" "$P" "$LOGS" "$OUT"
(cd "$WT" && dotnet build src/Kronikol/Kronikol.csproj -c Release -p:Version=$V -v:q -nologo > "$LOGS/build-kronikol.log" 2>&1 \
  && dotnet pack src/Kronikol/Kronikol.csproj --no-build -c Release -p:Version=$V -o "$(cygpath -w "$FEED")" -v:q -nologo > "$LOGS/pack-kronikol.log" 2>&1
  echo "pack Kronikol exit $?" >> "$LOGS/summary.txt")
(cd "$WT" && dotnet build src/Kronikol.AssertionTracking/Kronikol.AssertionTracking.csproj -c Release -p:Version=$V \
   -p:PackageOutputPath="$(cygpath -w "$FEED")" -v:q -nologo > "$LOGS/pack-weaver.log" 2>&1
  echo "pack weaver exit $?" >> "$LOGS/summary.txt")
ls "$FEED" >> "$LOGS/summary.txt"
cp "$PROBE"/*.cs "$PROBE"/Probe.csproj "$P/"
cat > "$P/NuGet.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$(cygpath -w "$FEED")" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
export NUGET_PACKAGES=$(cygpath -w "$S/nuget-210")
cd "$P"
dotnet build -p:KronikolVersion=$V -v:n -nologo 2>&1 | grep -E "Instrumented| error [A-Z]+[0-9]+" | sed 's/^ *//' > "$OUT/build-local-debug.txt"
dotnet run --no-build > "$OUT/run-local-debug.txt" 2>&1
dotnet build -c Release -p:KronikolVersion=$V -v:n -nologo 2>&1 | grep -E "Instrumented| error [A-Z]+[0-9]+" | sed 's/^ *//' > "$OUT/build-local-release.txt"
dotnet run -c Release --no-build > "$OUT/run-local-release.txt" 2>&1
ls -l bin/Debug/net10.0/Probe.dll bin/Release/net10.0/Probe.dll | awk '{print $5, $9}' > "$OUT/woven-size-local.txt"
cat "$LOGS/summary.txt"
