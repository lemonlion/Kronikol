#!/bin/sh
# Decodes a local lane pair (run1, run2), resolves its parties with R1, builds the four card readings and diffs them.
# Usage: sh local-pair.sh <audit/B dir> <suite>
B=$1; SUITE=$2; P=C:/Code/Kronikol-191-dive/plans/SERVICE_CARD_PLAN.harness/probes
K="dotnet C:/Code/Kronikol-191-dive/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll"
mkdir -p "$B/decoded/local"
for n in 1 2; do
  d="$B/local/out/$SUITE-run$n"; tag="L$n-$SUITE"
  $K decode "$d" -o "$B/decoded/local/$SUITE-run$n.json" > /dev/null 2>&1 || echo "decode failed $tag"
  $K query parties "$d" --json --max-bytes 0 > "$B/out/parties/$tag.json" 2> "$B/out/parties/$tag.err"
done
cd "$P" || exit 1
for v in "cards:" "cards-attr:--cause-attribution" "cards-nobg:--scenario-ops-no-background" "cards-attr-nobg:--cause-attribution --scenario-ops-no-background"; do
  dir=${v%%:*}; flags=${v#*:}; mkdir -p "$B/out/$dir"
  for n in 1 2; do
    dotnet run --no-build --file card-behaviour.cs -- "$B/decoded/local/$SUITE-run$n.json" "$B/out/parties/L$n-$SUITE.json" "$B/out/$dir/L$n-$SUITE" $flags > "$B/out/$dir/L$n-$SUITE.txt" 2>&1
  done
  python -I -X utf8 "$B/scripts/behaviour_diff.py" "$B/out/$dir/L1-$SUITE" "$B/out/$dir/L2-$SUITE" "$dir local $SUITE" > "$B/out/diff-local-$SUITE-$dir.txt" 2>&1
done
python -I -X utf8 "$B/scripts/parties_summary.py" "$B/out/parties/L1-$SUITE.json" "$B/out/parties/L2-$SUITE.json"
for dir in cards cards-attr cards-nobg cards-attr-nobg; do grep -h "^SUMMARY" "$B/out/diff-local-$SUITE-$dir.txt"; done
