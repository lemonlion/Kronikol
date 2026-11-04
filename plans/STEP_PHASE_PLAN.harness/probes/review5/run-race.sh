#!/usr/bin/env bash
# Usage: run-race.sh <folder> <version> <runs> [dotnet test filter]. One build, N runs, dumps under ../out/race/<version>/<i>/.
HERE="$(cd "$(dirname "$0")" && pwd)"
FOLDER="$1"; V="$2"; N="$3"; FILTER="$4"
cd "$HERE/$FOLDER" && rm -rf bin obj
dotnet build -p:KronikolVersion="$V" --nologo -v q 2>&1 | grep -E " error |Build FAILED" | cut -c1-200 | head -5
for i in $(seq 1 "$N"); do
  OUT="$HERE/../out/race/$V/$i"; mkdir -p "$OUT"
  if [ -n "$FILTER" ]; then
    PHASE_PROBE_OUT="$OUT" dotnet test --no-build -p:KronikolVersion="$V" --nologo -v q --filter "$FILTER" 2>&1 | grep -E "^(Passed|Failed)!" | cut -c1-120
  else
    PHASE_PROBE_OUT="$OUT" dotnet test --no-build -p:KronikolVersion="$V" --nologo -v q 2>&1 | grep -E "^(Passed|Failed)!" | cut -c1-120
  fi
done
rm -rf "$HERE/$FOLDER/bin" "$HERE/$FOLDER/obj"
find "$HERE/$FOLDER" -name "*.feature.cs" -delete
