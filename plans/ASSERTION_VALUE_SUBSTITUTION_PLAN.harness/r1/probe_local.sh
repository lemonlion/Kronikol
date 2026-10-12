#!/usr/bin/env bash
# The plan's probe (harness/probe) on local packages of the worktree's code, Debug and Release, under a version label
# nuget.org never has and an isolated NuGet cache (plan section 4.4).
# Usage: probe_local.sh <worktree> <version-label> <out-name>
set -u
S=${SCRATCH:-/tmp/probe-163}
WT=${1:-/c/Code/Kronikol-163}
V=${2:-4.14.8-local.163}
NAME=${3:-r1}
FEED=$S/feed-$NAME
P=$S/probe-$NAME
OUT=$S/probe-$NAME-out
rm -rf "$FEED" "$P" "$OUT" "$S/nuget-$NAME"; mkdir -p "$FEED" "$P" "$OUT"
(cd "$WT" && dotnet build src/Kronikol/Kronikol.csproj -c Release -p:Version=$V -v:q -nologo > "$OUT/build-kronikol.log" 2>&1 && dotnet pack src/Kronikol/Kronikol.csproj --no-build -c Release -p:Version=$V -o "$(cygpath -w "$FEED")" -v:q -nologo > "$OUT/pack-kronikol.log" 2>&1; echo "pack Kronikol exit $?" >> "$OUT/summary.txt")
(cd "$WT" && dotnet build src/Kronikol.AssertionTracking/Kronikol.AssertionTracking.csproj -c Release -p:Version=$V \
   -p:PackageOutputPath="$(cygpath -w "$FEED")" -v:q -nologo > "$OUT/pack-weaver.log" 2>&1; echo "pack weaver exit $?" >> "$OUT/summary.txt")
ls "$FEED" >> "$OUT/summary.txt"
cp "$WT"/plans/ASSERTION_VALUE_SUBSTITUTION_PLAN.harness/probe/*.cs "$WT"/plans/ASSERTION_VALUE_SUBSTITUTION_PLAN.harness/probe/Probe.csproj "$P/"
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
export NUGET_PACKAGES=$(cygpath -w "$S/nuget-$NAME")
cd "$P"
for c in Debug Release; do
  rm -rf bin obj
  dotnet build -c $c -p:KronikolVersion=$V -v:n > "$OUT/build-$c.log" 2>&1
  echo "== $c build exit $?" >> "$OUT/summary.txt"
  grep -h "Instrumented" "$OUT/build-$c.log" | sed 's/^ *//' | sort -u >> "$OUT/summary.txt"
  dotnet run --no-build -c $c -p:KronikolVersion=$V > "$OUT/run-$c.txt" 2>&1
  echo "== $c run exit $?" >> "$OUT/summary.txt"
done
diff --strip-trailing-cr "$OUT/run-Debug.txt" "$OUT/run-Release.txt" > "$OUT/debug-vs-release.diff"; echo "debug vs release diff lines: $(wc -l < "$OUT/debug-vs-release.diff")" >> "$OUT/summary.txt"
diff --strip-trailing-cr "$WT/plans/ASSERTION_VALUE_SUBSTITUTION_PLAN.harness/s0/run-4.14.6-debug.txt" "$OUT/run-Debug.txt" > "$OUT/s0-vs-$NAME.diff"
echo "s0 vs $NAME diff lines: $(wc -l < "$OUT/s0-vs-$NAME.diff")" >> "$OUT/summary.txt"
rm -rf bin obj
echo DONE >> "$OUT/summary.txt"
