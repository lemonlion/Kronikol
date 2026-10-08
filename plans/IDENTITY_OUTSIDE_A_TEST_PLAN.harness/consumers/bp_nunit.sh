#!/bin/bash
# BreakfastProvider's NUnit lane in memory, before (every Kronikol package at $BEFORE from nuget.org) and after (Kronikol
# and Kronikol.NUnit4 packed from this checkout as $LOCAL, the rest at $BEFORE): the change is in Kronikol.NUnit4 only.
#   bp_nunit.sh <BreakfastProvider clone> <checkout> <out dir> [BEFORE=4.11.1] [LOCAL=4.11.2-local133]
set -u
BP="$1"; CHECKOUT="$2"; OUT="$3"; BEFORE="${4:-4.11.1}"; LOCAL="${5:-4.11.2-local133}"
FEED="$OUT/feed"; mkdir -p "$FEED"
for p in Kronikol Kronikol.NUnit4; do
  [ -n "${SKIP_PACK:-}" ] && break
  dotnet build "$CHECKOUT/src/$p/$p.csproj" -c Release -p:Version="$LOCAL" > "$OUT/build-$p.log" 2>&1 || { echo "build $p failed"; exit 1; }
  dotnet pack "$CHECKOUT/src/$p/$p.csproj" -c Release --no-build -p:Version="$LOCAL" -o "$FEED" > "$OUT/pack-$p.log" 2>&1 || { echo "pack $p failed"; exit 1; }
done
cat > "$BP/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local133" value="$FEED" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
LANE="$BP/tests/BreakfastProvider.Tests.Component.NUnit"
pin() {  # pin <version for Kronikol and Kronikol.NUnit4> <version for the rest>
  for f in $(grep -rl 'Include="Kronikol' "$BP" --include=*.csproj); do
    sed -i -E "s#(Include=\"Kronikol[^\"]*\" Version=\")[^\"]+#\1$2#g; s#(Include=\"Kronikol(\.NUnit4)?\" Version=\")[^\"]+#\1$1#g" "$f"
  done
}
for side in before after; do
  if [ "$side" = before ]; then pin "$BEFORE" "$BEFORE"; else pin "$LOCAL" "$BEFORE"; fi
  rm -rf "$LANE/bin/Debug/net10.0/Reports" "$BP/.kronikol"
  # A version published minutes ago can be cached as missing: restore past the HTTP cache.
  (cd "$BP" && dotnet restore "$LANE" --no-http-cache > "$OUT/$side.restore.txt" 2>&1 &&
    dotnet test "$LANE" --no-restore -p:WarningLevel=0 > "$OUT/$side.console.txt" 2>&1)
  echo "$side exit $?"
  rm -rf "$OUT/$side"; cp -r "$LANE/bin/Debug/net10.0/Reports" "$OUT/$side" 2>/dev/null || echo "  no Reports for $side"
  grep -ho 'Include="Kronikol\(\.NUnit4\)\?" Version="[^"]*"' "$LANE"/*.csproj "$BP"/tests/BreakfastProvider.Tests.Component.Shared/*.csproj | sort -u | sed "s/^/  $side pin: /"
done
