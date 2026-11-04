#!/bin/bash
# probes-matcher/consumer-shapes: scenarios a consumer builds by hand, so the walk reads them, on published and on a worktree.
# Usage: run_consumer_shapes.sh <worktree> <work dir> <Kronikol.Tool.dll>
set -u
WT=$1; WORK=$2; TOOL=$3
HERE=$(cd "$(dirname "$0")/.." && pwd)
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off MSBUILDDISABLENODEREUSE=1
SHAPES="plain-control mix-new-row mix-background hand-first-line hand-400 hand-fold manual-bar with-copy prepend-hand-row hand-manual-raw issue-128 hand-tail-plain hand-tail-escaped hand-long-no-keyword null-test-id null-scenario-id"
for side in pub r2; do
  rm -rf "${WORK:?}/$side"
  mkdir -p "$WORK/$side"
  cp "$HERE/probes-matcher/consumer-shapes/Program.cs" "$HERE/probes-matcher/consumer-shapes/ConsumerShapes.csproj" "$WORK/$side/"
  prop=""; [ "$side" = r2 ] && prop="-p:KronikolProto=$(cygpath -m "$WT")"
  (cd "$WORK/$side" && dotnet build -c Release $prop -v q -nologo -p:WarningLevel=0 2>&1 | grep -E " error |rror\(s\)" | sort -u | head -3)
done
for shape in $SHAPES; do
  echo "== $shape"
  for side in pub r2; do
    out=$(cygpath -m "$WORK/$side/R-$shape")
    (cd "$WORK/$side" && dotnet bin/Release/net10.0/ConsumerShapes.dll "$shape" "$out" > "$WORK/$side/$shape.log" 2>&1)
    if [ -f "$out/Failures.md" ] || ls "$out"/TestRunReport.* >/dev/null 2>&1; then
      printf '  %-7s %s\n' "$side" "$(python -I "$HERE/scripts/read_shape.py" "$out" dotnet "$TOOL" 2>&1 | cut -c1-150)"
    else
      printf '  %-7s %s\n' "$side" "no report: $(tail -1 "$WORK/$side/$shape.log" | cut -c1-120)"
    fi
  done
done
