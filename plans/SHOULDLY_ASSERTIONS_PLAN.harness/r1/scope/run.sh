#!/usr/bin/env bash
# The scope fix (F12) on library versions the weaver's facts do not reference, on local packages, isolated NuGet cache.
set -u
S=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/19eb1bc1-2d7b-43ae-8364-4eb3eecabfcc/scratchpad
D=$S/scope-probe
K=${1:-4.11.2-local.141}
FEED=${2:-$S/nupkgs-r1}
export NUGET_PACKAGES=$(cygpath -w "$S/nuget-r1")
OUT=$D/out-$K
rm -rf "$OUT"; mkdir -p "$OUT"
for spec in FluentAssertions:6.12.2 FluentAssertions:7.2.0 FluentAssertions:8.9.0 AwesomeAssertions:8.2.0 AwesomeAssertions:9.6.0; do
  lib=${spec%%:*}; ver=${spec##*:}
  P=$D/p-$lib-$ver
  rm -rf "$P"; mkdir -p "$P"
  cp "$D/Program.cs" "$P/"
  define=""
  if [ "$lib" = AwesomeAssertions ] && [ "${ver%%.*}" -ge 9 ]; then define='<DefineConstants>$(DefineConstants);AWESOME9</DefineConstants>'; fi
  cat > "$P/ScopeProbe.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <NoWarn>\$(NoWarn);NU1603;NU1901;NU1902;NU1903;NU1904</NoWarn>
    $define
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Kronikol" Version="$K" />
    <PackageReference Include="Kronikol.AssertionTracking" Version="$K" PrivateAssets="all" />
    <PackageReference Include="$lib" Version="[$ver]" />
  </ItemGroup>
</Project>
EOF
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
  (cd "$P" && dotnet build -v:n > "$OUT/$lib-$ver-build.log" 2>&1; echo "build exit $?" > "$OUT/$lib-$ver-run.txt"; \
   grep -h "Kronikol.AssertionTracking: " "$OUT/$lib-$ver-build.log" | sed 's/^ *//' | sort -u >> "$OUT/$lib-$ver-run.txt"; \
   dotnet run --no-build >> "$OUT/$lib-$ver-run.txt" 2>&1)
done
echo DONE
