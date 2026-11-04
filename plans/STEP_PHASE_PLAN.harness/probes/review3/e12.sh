#!/usr/bin/env bash
HERE="$(cd "$(dirname "$0")" && pwd)"
for V in 5.28.2 5.28.3-rev8; do
  OUT="$HERE/../out/e12/$V"; mkdir -p "$OUT"
  cd "$HERE/rev3-plain" && rm -rf bin obj && PHASE_PROBE_OUT="$OUT" dotnet test -p:KronikolVersion="$V" --nologo -v q 2>&1 | grep -E "^(Passed|Failed)!|error " | cut -c1-140 | head -4
done
echo E12_DONE
