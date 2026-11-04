#!/bin/bash
# The ingest label shapes through each settlement of StepMarkerMatches on a worktree holding the conflict of on-223-10bbbe81.patch on c7252f6c.
# Usage: run_matchers.sh <worktree, with the conflict in ReportGenerator.cs> <work dir> <out file> [variant...]
set -u
export MSBUILDDISABLENODEREUSE=1
W=$1; WORK=$2; OUT=$3; shift 3
HERE=$(cd "$(dirname "$0")/.." && pwd)
RG="$W/src/Kronikol/Reports/ReportGenerator.cs"
mkdir -p "$WORK"
cp "$RG" "$WORK/ReportGenerator.conflict.cs"
python "$HERE/probes-ingest/gen.py" > /dev/null
: > "$OUT"
for variant in "${@:-theirs223 r2}"; do
  for v in $variant; do
    cp "$WORK/ReportGenerator.conflict.cs" "$RG"
    python "$HERE/prototype/resolve_on_223_r11.py" "$W" "$v" > /dev/null || exit 1
    (cd "$W" && dotnet build src/Kronikol.Tool/Kronikol.Tool.csproj -c Release -p:WarningLevel=0 -v q -nologo 2>&1 | grep -E " error " | sort -u | head -3 | cut -c1-200)
    TOOL=$(cygpath -m "$W/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll")
    R="$WORK/out-$v"
    rm -rf "${R:?}"
    dotnet "$TOOL" ingest "$(cygpath -w "$HERE/probes-ingest/interactions.ndjson")" --tests "$(cygpath -w "$HERE/probes-ingest/tests.ndjson")" -o "$(cygpath -w "$R")" > "$WORK/ingest-$v.log" 2>&1
    {
      echo "=== $v"
      python "$HERE/probes-ingest/paths.py" "$R" dotnet "$TOOL" | cut -c1-110
      echo
    } >> "$OUT"
  done
done
cp "$WORK/ReportGenerator.conflict.cs" "$RG"
echo "== done $(date +%H:%M:%S)"
