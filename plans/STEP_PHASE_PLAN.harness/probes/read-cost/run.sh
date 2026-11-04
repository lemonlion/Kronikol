#!/usr/bin/env bash
cd "$(dirname "$0")"
for V in 5.28.2 5.28.3-r3; do
  rm -rf bin obj
  dotnet build -c Release -p:KronikolVersion=$V --nologo -v q 2>&1 | grep -E " error |Build succeeded" | cut -c1-200 | head -4
  dotnet bin/Release/net10.0/Perf.dll 2>&1 | cut -c1-120
done
rm -rf bin obj
