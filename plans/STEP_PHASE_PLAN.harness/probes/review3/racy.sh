#!/usr/bin/env bash
# Usage: racy.sh <runs> <suite> <version> [<suite> <version> ...]
# Builds a suite once per version, then runs it <runs> times, one dump per run, under out/racy/<version>/<n>.
# The shapes that race differ from run to run, so one dump says nothing about how often an outcome happens.
HERE="$(cd "$(dirname "$0")" && pwd)"
N="$1"; shift
while [ "$#" -gt 1 ]; do
  FOLDER="$1"; V="$2"; shift 2
  cd "$HERE/$FOLDER" || exit 1
  rm -rf bin obj
  dotnet build -p:KronikolVersion="$V" --nologo -v q 2>&1 | grep -E "error |Build succeeded|Elapsed" | cut -c1-160 | head -3
  for i in $(seq 1 "$N"); do
    OUT="$HERE/../out/racy/$V/$i"; mkdir -p "$OUT"
    PHASE_PROBE_OUT="$OUT" dotnet test --no-build -p:KronikolVersion="$V" --nologo -v q 2>&1 | grep -E "^(Passed|Failed)!" | cut -c1-90
  done
  echo "RACY_SUITE_DONE $FOLDER $V"
done
echo RACY_ALL_DONE
