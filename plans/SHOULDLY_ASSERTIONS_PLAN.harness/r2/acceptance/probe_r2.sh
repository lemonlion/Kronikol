#!/usr/bin/env bash
# R2 acceptance: the plan's probe on local packages (an isolated NuGet cache, under a version label nuget.org never has).
# plain   = Shouldly only, the #141 repro (4.9.0 to 4.13.x skip it whole)
# control = with AwesomeAssertions and the CONTROL cases (F21's shape among them)
# embedded, and DebugType=none for #144's KRONIKOL001
set -u
S=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/19eb1bc1-2d7b-43ae-8364-4eb3eecabfcc/scratchpad
V=${1:-4.14.0-local.141}
FEED=${2:-$S/nupkgs-r2}
P=$S/probe-r2
OUT=$S/probe-r2-out
rm -rf "$P" "$OUT"; mkdir -p "$P" "$OUT"
cp /c/Code/Kronikol-shouldly-r2/plans/SHOULDLY_ASSERTIONS_PLAN.harness/probe/Probe.csproj \
   /c/Code/Kronikol-shouldly-r2/plans/SHOULDLY_ASSERTIONS_PLAN.harness/probe/Program.cs "$P/"
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
export NUGET_PACKAGES=$(cygpath -w "$S/nuget-r2")
cd "$P"
run_case() { # name, extra build args...
  local name=$1; shift
  rm -rf bin obj
  dotnet build -p:KronikolVersion=$V "$@" -v:n > "$OUT/$name-build.log" 2>&1
  echo "== $name: build exit $?" >> "$OUT/summary.txt"
  grep -h "Kronikol.AssertionTracking\|KRONIKOL001" "$OUT/$name-build.log" | grep -v "Installed\|Restored" | sed 's/^ *//' | sort -u >> "$OUT/summary.txt"
  dotnet run --no-build -p:KronikolVersion=$V "$@" > "$OUT/$name-run.txt" 2>&1
  echo "== $name: run exit $?" >> "$OUT/summary.txt"
}
run_case plain-Debug -c Debug
run_case plain-Release -c Release
run_case control-Debug -c Debug -p:Control=true
run_case control-Release -c Release -p:Control=true
run_case embedded -c Debug -p:Control=true -p:DebugType=embedded
rm -rf bin obj
dotnet build -c Debug -p:KronikolVersion=$V -p:DebugType=none -v:n > "$OUT/nosymbols-build.log" 2>&1
echo "== nosymbols: build exit $?" >> "$OUT/summary.txt"
grep -h "KRONIKOL001" "$OUT/nosymbols-build.log" | sed 's/^ *//' | sort -u >> "$OUT/summary.txt"
rm -rf bin obj
dotnet build -c Debug -p:KronikolVersion=$V -p:DebugType=none -warnaserror -v:q > "$OUT/nosymbols-warnaserror-build.log" 2>&1
echo "== nosymbols -warnaserror: build exit $?" >> "$OUT/summary.txt"
rm -rf bin obj
echo DONE >> "$OUT/summary.txt"
