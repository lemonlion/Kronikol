#!/usr/bin/env bash
# Plan F6, F7: what a fold, a gate and a show cost as the ledger grows. The consumer's ledger (7.1 MB) and
# the same grown to 25, 50, 100 and 180 MB with more run lines (grow_ledger.py; 180 MB is about a year at
# the consumer's rate). At each size: `history record` of one workflow run of 1, 6 and 18 suites, each
# into a fresh copy, then `history gate` of one suite's report (the reader every test run and every gate
# step uses) and `history show`. Wall time and peak memory (measure.py).
source "$(dirname "$0")/common.sh"
new_world "${1:?world directory}"
BP=$H/bp-history.jsonl
SIZES=${SIZES:-"0 25000000 50000000 100000000 180000000"}

for s in 1 6 18; do python3 "$H/make_fragments.py" "$BP" "$W/frag$s" $((700 + s)) --suites $s > /dev/null; done
python3 "$H/make_fragments.py" "$BP" "$W/gate" 800 --suites 1 --fail-first > /dev/null
REPORT=$(dirname "$(find "$W/gate" -name History.run.json)")
python3 "$H/make_report.py" "$REPORT/History.run.json" > /dev/null

for size in $SIZES; do
  if [ "$size" = 0 ]; then cp "$BP" "$W/base.jsonl"; else python3 "$H/grow_ledger.py" "$BP" "$W/base.jsonl" "$size" > /dev/null; fi
  bytes=$(wc -c < "$W/base.jsonl"); mb=$(( (bytes + 500000) / 1000000 ))
  echo "== ledger $bytes bytes ($mb MB), $(grep -c '"t":"run"' "$W/base.jsonl") run lines, gzip -6 $(gzip -6 -c "$W/base.jsonl" | wc -c) bytes"
  for s in 1 6 18; do
    cp "$W/base.jsonl" "$W/work.jsonl"
    python3 "$H/measure.py" "  record, $s suite(s)" -- kronikol history record "$W/frag$s" --history "$W/work.jsonl"
  done
  python3 "$H/measure.py" "  gate, one suite's report" -- kronikol history gate "$REPORT" --history "$W/base.jsonl"
  python3 "$H/measure.py" "  show" -- kronikol history show --history "$W/base.jsonl"
done
rm -f "$W/base.jsonl" "$W/work.jsonl"
