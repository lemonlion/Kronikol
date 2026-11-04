#!/usr/bin/env bash
HERE="$(cd "$(dirname "$0")" && pwd)"
V="$1"
OUT="$HERE/../out/$V"; mkdir -p "$OUT"
cd "$HERE/rev5-readers2" && rm -rf bin obj
PHASE_PROBE_OUT="$OUT" dotnet test -p:KronikolVersion="$V" --nologo -v q 2>&1 | grep -E "^(Passed|Failed)!| error " | cut -c1-160 | head -5
R="$OUT/reports-rev5-readers2"; rm -rf "$R"; mkdir -p "$R"
cp -r bin/Debug/net10.0/Reports/. "$R/" 2>/dev/null
cd "$HERE/rev5-readers2" && rm -rf bin obj
