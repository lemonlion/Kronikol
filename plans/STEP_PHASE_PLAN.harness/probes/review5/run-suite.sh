#!/usr/bin/env bash
# Usage: run-suite.sh <folder> <version: 5.28.2 | 5.28.3-rev5...> [out label]. Clean build, one test run, dump under ../out/<label or version>.
HERE="$(cd "$(dirname "$0")" && pwd)"
FOLDER="$1"; V="$2"; LABEL="${3:-$V}"
OUT="$HERE/../out/$LABEL"; mkdir -p "$OUT"
cd "$HERE/$FOLDER" && rm -rf bin obj
PHASE_PROBE_OUT="$OUT" dotnet test -p:KronikolVersion="$V" --nologo -v q 2>&1 | grep -E "^(Passed|Failed)!|error |  Failed |\[FAIL\]|Error Message|crash|Unhandled" | cut -c1-220 | head -${MAXLINES:-30}
cd "$HERE/$FOLDER" && rm -rf bin obj
