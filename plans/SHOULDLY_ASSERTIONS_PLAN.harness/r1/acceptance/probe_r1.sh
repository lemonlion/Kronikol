#!/usr/bin/env bash
# R1 acceptance: the plan's probe on local packages (an isolated NuGet cache, so nothing reaches the shared one).
set -u
S=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/19eb1bc1-2d7b-43ae-8364-4eb3eecabfcc/scratchpad
V=${1:-4.11.2-local.141}
FEED=${2:-$S/nupkgs-r1}
P=$S/probe-r1
OUT=$S/probe-r1-out
rm -rf "$P" "$OUT"; mkdir -p "$P" "$OUT"
cp /c/Code/Kronikol-shouldly-impl/plans/SHOULDLY_ASSERTIONS_PLAN.harness/probe/Probe.csproj \
   /c/Code/Kronikol-shouldly-impl/plans/SHOULDLY_ASSERTIONS_PLAN.harness/probe/Program.cs "$P/"
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
echo '{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }' > "$P/global.json"
export NUGET_PACKAGES=$(cygpath -w "$S/nuget-r1")
cd "$P"
for cfg in Debug Release; do
  dotnet build -c $cfg -p:KronikolVersion=$V -p:Control=true -v:n > "$OUT/control-$cfg-build.log" 2>&1
  echo "build $cfg exit $?" >> "$OUT/summary.txt"
  grep -h "Kronikol.AssertionTracking" "$OUT/control-$cfg-build.log" | sed 's/^ *//' | sort -u >> "$OUT/summary.txt"
  dotnet run --no-build -c $cfg -p:KronikolVersion=$V -p:Control=true > "$OUT/control-$cfg-run.txt" 2>&1
  echo "run $cfg exit $?" >> "$OUT/summary.txt"
done
rm -rf bin obj
dotnet build -c Debug -p:KronikolVersion=$V -p:Control=true -p:DebugType=embedded -v:n > "$OUT/embedded-build.log" 2>&1
echo "embedded build exit $?" >> "$OUT/summary.txt"
grep -h "Kronikol.AssertionTracking" "$OUT/embedded-build.log" | sed 's/^ *//' | sort -u >> "$OUT/summary.txt"
ls bin/Debug/net8.0/Probe.pdb >> "$OUT/summary.txt" 2>&1
dotnet run --no-build -c Debug -p:KronikolVersion=$V -p:Control=true -p:DebugType=embedded > "$OUT/embedded-run.txt" 2>&1
echo "embedded run exit $?" >> "$OUT/summary.txt"
echo DONE >> "$OUT/summary.txt"
