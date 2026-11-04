#!/usr/bin/env bash
# Usage: run-suite.sh <folder> <variant: 5.28.2 | 5.28.3-rev4> [extra dotnet args]
HERE="$(cd "$(dirname "$0")" && pwd)"
FOLDER="$1"; V="$2"; shift 2
OUT="$HERE/../out/$V"; mkdir -p "$OUT"
cd "$HERE/$FOLDER" && rm -rf bin obj && PHASE_PROBE_OUT="$OUT" dotnet test -p:KronikolVersion="$V" --nologo -v q "$@" 2>&1 | grep -E "^(Passed|Failed)!|error |  Failed |\[FAIL\]|Error Message" | cut -c1-240
