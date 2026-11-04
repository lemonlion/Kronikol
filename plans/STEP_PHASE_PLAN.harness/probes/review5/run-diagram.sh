#!/usr/bin/env bash
HERE="$(cd "$(dirname "$0")" && pwd)"
V="$1"; OUT="$HERE/../out/$V"; mkdir -p "$OUT"
cd "$HERE/rev5-diagram" && rm -rf bin obj
PHASE_PROBE_OUT="$OUT" dotnet run -p:KronikolVersion="$V" 2>&1 | tail -3 | cut -c1-200
rm -rf bin obj
