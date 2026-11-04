#!/usr/bin/env bash
# Usage: run-weave.sh <version>. rev5-weave in Debug and Release.
HERE="$(cd "$(dirname "$0")" && pwd)"
V="$1"; OUT="$HERE/../out/$V"; mkdir -p "$OUT"
cd "$HERE/rev5-weave"
for C in Debug Release; do
  rm -rf bin obj
  PROBE_CONFIG="-$C" PHASE_PROBE_OUT="$OUT" dotnet test -c $C -p:KronikolVersion="$V" --nologo -v q 2>&1 | grep -E "^(Passed|Failed)!| error |  Failed |\[FAIL\]|crash|Unhandled|InvalidProgram" | cut -c1-170 | head -24
done
rm -rf bin obj
