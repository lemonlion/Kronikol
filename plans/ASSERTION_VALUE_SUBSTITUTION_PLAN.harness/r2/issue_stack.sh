#!/usr/bin/env bash
# The issue's stack (#163) on R2's local packages: a copy of R1's scratch project (xUnit 2.9.3, Kronikol.xUnit2,
# Shouldly 4.3.0, FluentAssertions 6.12.1, net8.0) moved to 4.16.0-local.163, run, and read through query.cs.
set -u
S=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/ea84b5d9-416d-4a24-a87c-1a1b3e46539b/scratchpad
WT=${1:-/c/Code/Kronikol-163}
V=${2:-4.16.0-local.163}
FEED=$S/feed-r2
OUT=$S/issue-stack-r2-out
rm -rf "$OUT"; mkdir -p "$OUT"
(cd "$WT" && dotnet build src/Kronikol.xUnit2/Kronikol.xUnit2.csproj -c Release -p:Version=$V -v:q -nologo > "$OUT/build-xunit2.log" 2>&1 \
  && dotnet pack src/Kronikol.xUnit2/Kronikol.xUnit2.csproj --no-build -c Release -p:Version=$V -o "$(cygpath -w "$FEED")" -v:q -nologo > "$OUT/pack-xunit2.log" 2>&1
  echo "pack xunit2 exit $?" >> "$OUT/summary.txt")
ls "$FEED" >> "$OUT/summary.txt"
P=$S/issue-stack-r2
rm -rf "$P" "$S/nuget-issue-r2"; cp -r "$S/issue-stack" "$P"; rm -rf "$P/bin" "$P/obj"
sed -i "s/4\.14\.8-local\.163/$V/g" "$P/KronikolComponentTests.csproj"
sed -i "s#feed-r1#feed-r2#" "$P/NuGet.config"
grep -n "local.163" "$P/KronikolComponentTests.csproj" >> "$OUT/summary.txt"
export NUGET_PACKAGES=$(cygpath -w "$S/nuget-issue-r2")
cd "$P"
dotnet test > "$OUT/test.log" 2>&1; echo "test exit $?" >> "$OUT/summary.txt"
R=$(dirname "$(find "$P/bin" -name TestRunReport.json | head -1)")
echo "reports $R" >> "$OUT/summary.txt"
dotnet run --file "$R/query.cs" -- assertions "$R" > "$OUT/query.txt" 2>&1; echo "query exit $?" >> "$OUT/summary.txt"
echo DONE >> "$OUT/summary.txt"
