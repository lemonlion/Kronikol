#!/usr/bin/env bash
# The probe's control cases in Release on a PUBLISHED version, from nuget.org only, in an isolated NuGet cache.
set -u
S=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/19eb1bc1-2d7b-43ae-8364-4eb3eecabfcc/scratchpad
V=${1:-4.11.0}
P=$S/probe-pub-$V
rm -rf "$P"; mkdir -p "$P"
cp /c/Code/Kronikol-shouldly-impl/plans/SHOULDLY_ASSERTIONS_PLAN.harness/probe/Probe.csproj \
   /c/Code/Kronikol-shouldly-impl/plans/SHOULDLY_ASSERTIONS_PLAN.harness/probe/Program.cs "$P/"
cat > "$P/NuGet.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
echo '{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }' > "$P/global.json"
export NUGET_PACKAGES=$(cygpath -w "$S/nuget-pub")
cd "$P"
dotnet build -c Release -p:KronikolVersion=$V -p:Control=true -v:n > build.log 2>&1
echo "build exit $?"
grep -h "Kronikol.AssertionTracking" build.log | sed 's/^ *//' | sort -u
dotnet run --no-build -c Release -p:KronikolVersion=$V -p:Control=true > run.txt 2>&1
echo "run exit $?"
sed -n '1p' run.txt
sed -n '/== Control_Should_Be_fails/,/== Control_label/p' run.txt
