#!/usr/bin/env bash
# Usage: run-suite.sh <folder> <variant: 5.28.2 | 5.28.3-rev3> [out-subdir]
HERE="$(cd "$(dirname "$0")" && pwd)"
FOLDER="$1"; V="$2"; OUTDIR="${3:-$V}"
OUT="$HERE/../out/$OUTDIR"; mkdir -p "$OUT"
cd "$HERE/$FOLDER" && rm -rf bin obj && PHASE_PROBE_OUT="$OUT" dotnet test -p:KronikolVersion="$V" --nologo -v q 2>&1 | grep -E "^(Passed|Failed)!|error |  Failed |\[FAIL\]|Error Message" | cut -c1-240 | head -30
mkdir -p "$OUT/reports-$FOLDER" && rm -rf "$OUT/reports-$FOLDER"/*
cp -r "$HERE/$FOLDER/bin/Debug/net10.0/Reports/." "$OUT/reports-$FOLDER/" 2>/dev/null
true
