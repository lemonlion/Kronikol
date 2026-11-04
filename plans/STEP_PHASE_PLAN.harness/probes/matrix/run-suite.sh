#!/usr/bin/env bash
# Usage: run-suite.sh <suite folder> <results label> <Kronikol version>
# Builds a suite clean against one version of the packages and runs it, with its dump under results/<label>/.
# A suite's results/<label>/<suite>.log keeps what the runner and the build printed, and <suite>.reports what its Reports folder held, as a path.
set -u
SUITE="$1"
LABEL="$2"
VERSION="$3"
HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$HERE/../results/$LABEL"
mkdir -p "$OUT"
LOG="$OUT/$SUITE.log"
: > "$LOG"
cd "$HERE/$SUITE" || exit 2
rm -rf bin obj
export PHASE_PROBE_OUT="$OUT"
case "$SUITE" in
  *tunit*)
    # TUnit runs on the Testing Platform: the executable, not vstest. NotInParallel by run argument, as X20 sets a process-wide option.
    dotnet build -p:KronikolVersion="$VERSION" --nologo -v q > "$LOG" 2>&1
    grep -qE "Build succeeded" "$LOG" || { echo "$SUITE: BUILD FAILED"; grep -E " error " "$LOG" | cut -c1-220 | sort -u | head -8; exit 1; }
    PROJ="$(ls *.csproj | head -1)"
    "./bin/Debug/net10.0/${PROJ%.csproj}.exe" --maximum-parallel-tests "${TUNIT_PARALLEL:-1}" --no-progress --disable-logo >> "$LOG" 2>&1
    ;;
  *)
    dotnet test -p:KronikolVersion="$VERSION" --nologo -v q >> "$LOG" 2>&1
    ;;
esac
echo "$SUITE [$LABEL]: $(grep -E '^(Passed|Failed)!|^Test run summary|total: |failed: |succeeded: | error ' "$LOG" | cut -c1-160 | head -6 | tr '\n' ' ')"
find "$HERE/$SUITE/bin" -maxdepth 4 -type d -name Reports 2>/dev/null | head -1 > "$OUT/$SUITE.reports"
