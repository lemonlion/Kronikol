#!/bin/bash
# Each scenario's diagram source, published against a prototype's run of the probe suite. The sources go to files and only compare_diagrams.py reads them.
# Usage: run_diagrams.sh <work dir, holding repro-<label>> <prototype label> <results file> <Kronikol.Tool.dll>
set -u
WORK=$1; LABEL=$2; OUT=$3; TOOL=$4
HERE=$(cd "$(dirname "$0")/.." && pwd)
export MSBUILDDISABLENODEREUSE=1
if [ ! -d "$WORK/repro-published/bin/Release/net10.0/Reports" ]; then
  bash "$HERE/scripts/run_repro.sh" "$WORK" published 5.28.2 - dotnet "$TOOL" > "$WORK/repro-published.txt" 2>&1
fi
for side in published "$LABEL"; do
  R="$WORK/repro-$side/bin/Release/net10.0/Reports"
  D="$WORK/diagrams-$side"
  rm -rf "${D:?}"; mkdir -p "$D"
  n=$(dotnet "$TOOL" query scenarios "$R" --max-bytes 0 2>/dev/null | grep -cE "^s[0-9]+ ")
  i=0
  while [ "$i" -lt "$n" ]; do
    dotnet "$TOOL" query diagram "$R" "s$i" --out "$(cygpath -w "$D")" > /dev/null 2>&1
    i=$((i+1))
  done
done
# An empty file would compare as the same: this comparison once passed on 25 empty files.
find "$WORK/diagrams-published" "$WORK/diagrams-$LABEL" -name '*.puml' -size +0 | wc -l | sed 's/^/diagram files with content: /'
python "$HERE/scripts/compare_diagrams.py" "$WORK/diagrams-published" "$WORK/diagrams-$LABEL" > "$OUT" 2>&1
tail -3 "$OUT" | cut -c1-120
