#!/usr/bin/env bash
# Plan F10: a re-run of a failed job reads its own first attempt as history. One suite of the consumer's
# ledger, five green runs, then run 6 fails its first scenario. Attempt 1 is gated before its fold (what
# the lane's own gate step sees), then folded (what attempt 1's record job does, `!cancelled()`), then
# attempt 2, the re-run of the failed job with the same failure, is gated against the ledger as it now is.
# Last, the fix the owner could choose (Q6), approximated by the tool as it is: attempt 2 gated against
# the ledger with attempt 1's line taken out.
source "$(dirname "$0")/common.sh"
new_world "${1:?world directory}"
BP=$H/bp-history.jsonl
L=$W/ledger/history.jsonl
mkdir -p "$W/ledger"
kronikol history init --history "$L" > /dev/null

for n in 1 2 3 4 5; do
  python3 "$H/make_fragments.py" "$BP" "$W/f$n" "$n" --suites 1 > /dev/null
  kronikol history record "$W/f$n" --history "$L" > /dev/null
done
echo "five green runs of one suite recorded: $(grep -c '"t":"run"' "$L") run lines"

gate() {  # label, reports dir
  local out; out=$(kronikol history gate "$2" --history "$L" 2>&1); local exit=$?
  echo "$1: exit $exit; $(echo "$out" | grep -E '^(new-failures|already-failing):' | tr '\n' ' ')$(echo "$out" | grep -E '^gate:' )"
  echo "$out" | grep -E '^  ' | head -2 | sed 's/^/    /'
}

python3 "$H/make_fragments.py" "$BP" "$W/a1" 6 --suites 1 --attempt 1 --fail-first > /dev/null
R1=$(dirname "$(find "$W/a1" -name History.run.json)")
python3 "$H/make_report.py" "$R1/History.run.json" > /dev/null
gate "A. attempt 1 (gh:6:1), gated before its fold" "$R1"

kronikol history record "$W/a1" --history "$L" > /dev/null
echo "attempt 1 folded: $(grep -c '"t":"run"' "$L") run lines, the last $(grep '"t":"run"' "$L" | tail -1 | python3 -c 'import json,sys; o=json.loads(sys.stdin.read()); print(o["id"], o["results"][:1])')"

python3 "$H/make_fragments.py" "$BP" "$W/a2" 6 --suites 1 --attempt 2 --fail-first > /dev/null
R2=$(dirname "$(find "$W/a2" -name History.run.json)")
python3 "$H/make_report.py" "$R2/History.run.json" > /dev/null
gate "B. attempt 2 (gh:6:2), the re-run, the same failure" "$R2"
gate "C. attempt 1's report gated again after its fold" "$R1"

grep -v '"id":"gh:6:1"' "$L" > "$W/ledger/without-attempt-1.jsonl"
L=$W/ledger/without-attempt-1.jsonl
gate "D. attempt 2 against the ledger without gh:6:1's line" "$R2"
