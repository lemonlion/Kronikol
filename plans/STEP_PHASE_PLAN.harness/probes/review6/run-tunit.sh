#!/usr/bin/env bash
# Usage: run-tunit.sh <folder> <version> [out label]. TUnit runs on the Testing Platform: build, then the executable.
HERE="$(cd "$(dirname "$0")" && pwd)"
FOLDER="$1"; V="$2"; LABEL="${3:-$V}"
OUT="$HERE/../out/$LABEL"; mkdir -p "$OUT"
cd "$HERE/$FOLDER" && rm -rf bin obj
LOG="$OUT/$FOLDER.log"
dotnet build -p:KronikolVersion="$V" --nologo -v q > "$LOG" 2>&1
grep -qE "Build succeeded" "$LOG" || { echo "$FOLDER: BUILD FAILED"; grep -E " error " "$LOG" | cut -c1-220 | sort -u | head -8; exit 1; }
PROJ="$(ls *.csproj | head -1)"
PHASE_PROBE_OUT="$OUT" "./bin/Debug/net10.0/${PROJ%.csproj}.exe" --maximum-parallel-tests "${TUNIT_PARALLEL:-1}" --no-progress --disable-logo >> "$LOG" 2>&1
echo "$FOLDER [$LABEL]: $(grep -E '^(Test run summary|  total|total:|  failed|failed:|  succeeded|succeeded:)' "$LOG" | tr -s ' ' | tr '\n' ' ' | cut -c1-140)"
cd "$HERE/$FOLDER" && rm -rf bin obj
