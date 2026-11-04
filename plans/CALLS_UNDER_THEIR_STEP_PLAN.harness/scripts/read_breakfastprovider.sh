#!/bin/bash
# Reads a finished run of run_breakfastprovider.sh: each lane's published report against its prototype report, then the walk against the final path in the prototype's own run.
# Usage: read_breakfastprovider.sh <work dir> <results dir> <Kronikol.Tool.dll> <log> <label of the build>
set -u
WORK=$1; RESULTS=$2; TOOL=$3; LOG=$4; LABEL=$5
HERE=$(cd "$(dirname "$0")/.." && pwd)
echo "# BreakfastProvider's in-memory lanes: published 5.28.2 against $LABEL. scripts/run_breakfastprovider.sh, scripts/read_breakfastprovider.sh."
grep -E "^== proto|^ *failed:|^ *succeeded:" "$LOG" | cut -c1-60
for lane in ReqNRoll LightBDD BDDfy NUnit; do
  echo
  echo "=== $lane: published against the prototype, scenario by scenario"
  python "$HERE/scripts/compare_reports.py" "$WORK/out/published/$lane" "$WORK/out/proto/$lane" dotnet "$TOOL" decode 2>&1 | cut -c1-190
  echo
  echo "=== $lane: in one run, published's walk against the path a record ends with"
  python "$HERE/scripts/differential_final.py" "$RESULTS/bp-$lane.tsv" 2>&1 | cut -c1-190
done
