#!/usr/bin/env bash
# WEAVER_SOURCE_PATHS_PLAN section 5.4: BreakfastProvider at origin/main, its 27 Kronikol references moved to a version,
# its seven tracked test projects built without and with ContinuousIntegrationBuild, and what the weave wrote into each
# compared (wovenlabels.cs). Usage: breakfast.sh <label> <kronikol version>
set -u
export MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
A=<TEMP>/claude/c--Code-Kronikol/1a468123-a7e8-45cc-b875-791076e4d49a/scratchpad/accept165
LABEL=$1; VERSION=$2
BP=$A/bp-$LABEL
OUT=$A/breakfast-$LABEL
rm -rf "$BP" "$OUT"; mkdir -p "$OUT"
git -c core.longpaths=true clone -q --no-checkout C:/Code/BreakfastProvider "$BP"
git -C "$BP" fetch -q C:/Code/BreakfastProvider refs/remotes/origin/main
git -C "$BP" -c core.autocrlf=false -c core.longpaths=true checkout -q FETCH_HEAD
echo "BreakfastProvider $(git -C "$BP" rev-parse --short HEAD), Kronikol $VERSION" > "$OUT/summary.txt"
grep -rl 'Include="Kronikol[^"]*" Version="4.9.1"' --include=*.csproj "$BP" | while read -r f; do
  sed -i -E "s/(Include=\"Kronikol[^\"]*\" Version=\")4\.9\.1\"/\1$VERSION\"/g" "$f"
done
echo "references moved: $(grep -rho "Include=\"Kronikol[^\"]*\" Version=\"$VERSION\"" --include=*.csproj "$BP" | wc -l)" >> "$OUT/summary.txt"
cat > "$BP/NuGet.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="accept165" value="$A/feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
PROJECTS="BDDfy LightBDD NUnit ReqNRoll Shared TUnit xUnit"
for mode in local ci; do
  args=""; [ $mode = ci ] && args="-p:ContinuousIntegrationBuild=true"
  find "$BP" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null
  for p in $PROJECTS; do
    proj=$BP/tests/BreakfastProvider.Tests.Component.$p/BreakfastProvider.Tests.Component.$p.csproj
    log=$OUT/$mode-$p.log
    dotnet build "$proj" -c Debug -v:n $args > "$log" 2>&1
    rc=$?
    dll=$(find "$BP/tests/BreakfastProvider.Tests.Component.$p/bin/Debug" -name "BreakfastProvider.Tests.Component.$p.dll" | head -1)
    echo "$mode $p: build exit $rc; $(grep -hoE 'Instrumented [0-9]+ assertion\(s\) in [0-9]+ method\(s\)|warning KRONIKOL[0-9]+|error CS[0-9]+' "$log" | sort | uniq -c | tr -s ' ' | paste -sd';' -)" >> "$OUT/summary.txt"
    if [ -n "$dll" ]; then
      (cd "$A" && dotnet run --file wovenlabels.cs -- "$dll" > "$OUT/$mode-$p.labels.txt" 2>&1)
    fi
  done
done
for p in $PROJECTS; do
  if cmp -s "$OUT/local-$p.labels.txt" "$OUT/ci-$p.labels.txt"; then same=same; else same=DIFFERENT; fi
  echo "$p: local [$(head -1 "$OUT/local-$p.labels.txt")] ci [$(head -1 "$OUT/ci-$p.labels.txt")] -> $same" >> "$OUT/summary.txt"
done
cat "$OUT/summary.txt"
