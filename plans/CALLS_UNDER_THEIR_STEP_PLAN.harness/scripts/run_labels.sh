#!/bin/bash
# The ingest labels (probes-ingest/) through one build: each call's step, and how many mismatches the run reported.
# Usage: run_labels.sh <label> <work dir> <tool that reads, a Kronikol.Tool.dll> <ingesting command...>
set -u
LABEL=$1; WORK=$2; READ=$3; shift 3
HERE=$(cd "$(dirname "$0")/.." && pwd)
mkdir -p "$WORK"
R="$WORK/out-$LABEL"
rm -rf "${R:?}"
"$@" ingest "$(cygpath -w "$HERE/probes-ingest/interactions.ndjson")" --tests "$(cygpath -w "$HERE/probes-ingest/tests.ndjson")" -o "$(cygpath -w "$R")" > "$WORK/ingest-$LABEL.log" 2>&1
echo "=== $LABEL: StepAttributionMismatch in its diagnostics: $(grep -c StepAttributionMismatch "$WORK/ingest-$LABEL.log")"
python "$HERE/probes-ingest/paths.py" "$R" dotnet "$READ" | sed -E 's/ {2,}/  /g' | cut -c1-80
echo
