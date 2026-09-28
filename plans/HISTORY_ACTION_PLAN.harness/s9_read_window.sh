#!/usr/bin/env bash
# Plan F7, Q3: a read window. What every lane fetches and every test run reads is the whole tip; readers
# look back only `--window` runs (50 by default). Would a copy pruned to the window, written beside the
# ledger by record and fetched by read instead, read the same? The consumer's ledger grown to 180 MB (about
# a year, grow_ledger.py): `kronikol history prune --window 50` on a copy (its cost and its size), then one
# suite's failing report gated against the full ledger and against the window, the two outputs compared
# with the ledger path taken out. Then what committing the window beside the ledger on every run costs the
# branch's pack: ten runs recorded, with and without a window file refreshed each time.
source "$(dirname "$0")/common.sh"
new_world "${1:?world directory}"
BP=$H/bp-history.jsonl
SIZE=${SIZE:-180000000}

python3 "$H/grow_ledger.py" "$BP" "$W/full.jsonl" "$SIZE" > /dev/null
cp "$W/full.jsonl" "$W/window.jsonl"
python3 "$H/measure.py" "prune --window 50 of $(wc -c < "$W/full.jsonl") bytes" -- kronikol history prune --window 50 --history "$W/window.jsonl"
echo "window: $(wc -c < "$W/window.jsonl") bytes, $(grep -c '"t":"run"' "$W/window.jsonl") run lines, gzip -6 $(gzip -6 -c "$W/window.jsonl" | wc -c) bytes; full: gzip -6 $(gzip -6 -c "$W/full.jsonl" | wc -c) bytes"

python3 "$H/make_fragments.py" "$BP" "$W/gate" 800 --suites 1 --fail-first > /dev/null
REPORT=$(dirname "$(find "$W/gate" -name History.run.json)")
python3 "$H/make_report.py" "$REPORT/History.run.json" > /dev/null
for f in full window; do
  python3 "$H/measure.py" "gate against $f" -- kronikol history gate "$REPORT" --history "$W/$f.jsonl"
  kronikol history gate "$REPORT" --history "$W/$f.jsonl" | grep -v '^ledger: ' > "$W/gate-$f.txt"
done
if cmp -s "$W/gate-full.txt" "$W/gate-window.txt"; then echo "gate output: identical ($(wc -l < "$W/gate-full.txt") lines)"; else echo "gate output: DIFFERS"; diff "$W/gate-full.txt" "$W/gate-window.txt" | head -20; fi
head -3 "$W/gate-full.txt"
rm -f "$W/full.jsonl" "$W/window.jsonl"

# The pack: ten runs of 18 suites, one commit each, with and without a window file (prune --window 50
# of the new ledger) committed beside it. On the consumer's ledger as it is (every suite still under 50
# runs, so the window is the whole ledger) and grown to 25 MB (about 135 runs a suite, so each run's
# window drops the oldest line of each suite: the steady state).
python3 "$H/grow_ledger.py" "$BP" "$W/grown.jsonl" 25000000 > /dev/null
for base in "$BP" "$W/grown.jsonl"; do
for mode in ledger-only with-window; do
  R=$W/pack-$mode; rm -rf "$R"; git init -q -b data "$R"; cp "$base" "$R/history.jsonl"
  git -C "$R" add -A; git -C "$R" -c user.name=t -c user.email=t@e commit -q -m base
  [ $mode = with-window ] && { cp "$R/history.jsonl" "$R/history.window.jsonl"; kronikol history prune --window 50 --history "$R/history.window.jsonl" > /dev/null; git -C "$R" add -A; git -C "$R" -c user.name=t -c user.email=t@e commit -q -m window; }
  git -C "$R" gc -q --aggressive; before=$(git -C "$R" count-objects -v | awk '/size-pack/ {print $2}')
  for n in $(seq 1 10); do
    python3 "$H/make_fragments.py" "$BP" "$W/run$n" $((900 + n)) --suites 18 > /dev/null
    kronikol history record "$W/run$n" --history "$R/history.jsonl" > /dev/null
    [ $mode = with-window ] && { cp "$R/history.jsonl" "$R/history.window.jsonl"; kronikol history prune --window 50 --history "$R/history.window.jsonl" > /dev/null; }
    git -C "$R" add -A; git -C "$R" -c user.name=t -c user.email=t@e commit -q -m "run $n"
  done
  git -C "$R" gc -q; after=$(git -C "$R" count-objects -v | awk '/size-pack/ {print $2}')
  echo "pack, $(basename "$base"), $mode: $before KB before, $after KB after ten runs ($(( (after - before) / 10 )) KB a run); tip: $(wc -c < "$R/history.jsonl") bytes$( [ $mode = with-window ] && echo ", window $(wc -c < "$R/history.window.jsonl") bytes")"
done
done
rm -f "$W/grown.jsonl"
