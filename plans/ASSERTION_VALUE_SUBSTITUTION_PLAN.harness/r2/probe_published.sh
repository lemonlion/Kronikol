#!/usr/bin/env bash
# The plan's probe on PUBLISHED packages (nuget.org), Debug and Release, recording the weave's line (count and ms) and
# the woven Probe.dll's size: the baseline for R2's cost (plan section 9). Usage: probe_published.sh <worktree> <version>
set -u
S=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/ea84b5d9-416d-4a24-a87c-1a1b3e46539b/scratchpad
WT=${1:-/c/Code/Kronikol-163}
V=${2:-4.14.8}
P=$S/probe-pub-$V
OUT=$S/probe-pub-$V-out
rm -rf "$P" "$OUT" "$S/nuget-pub-$V"; mkdir -p "$P" "$OUT"
cp "$WT"/plans/ASSERTION_VALUE_SUBSTITUTION_PLAN.harness/probe/*.cs "$WT"/plans/ASSERTION_VALUE_SUBSTITUTION_PLAN.harness/probe/Probe.csproj "$P/"
cat > "$P/NuGet.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML
export NUGET_PACKAGES=$(cygpath -w "$S/nuget-pub-$V")
cd "$P"
for c in Debug Release; do
  rm -rf bin obj
  dotnet build -c $c -p:KronikolVersion=$V -v:n > "$OUT/build-$c.log" 2>&1
  echo "== $c build exit $?" >> "$OUT/summary.txt"
  grep -h "Instrumented" "$OUT/build-$c.log" | sed 's/^ *//' | sort -u >> "$OUT/summary.txt"
  echo "$c Probe.dll bytes: $(stat -c %s bin/$c/net8.0/Probe.dll)" >> "$OUT/summary.txt"
  dotnet run --no-build -c $c -p:KronikolVersion=$V > "$OUT/run-$c.txt" 2>&1
  echo "== $c run exit $?" >> "$OUT/summary.txt"
done
rm -rf bin obj
echo DONE >> "$OUT/summary.txt"
