#!/usr/bin/env bash
# Plan §2.5: what a fold costs on the consumer's real ledger (7.1 MB, 664 lines, 18 suites, 2026-09-27):
# the record of one run of eighteen lanes, and what each recorded run adds to the branch's packed history.
source "$(dirname "$0")/common.sh"
T=${1:-/tmp/kh-s4}
new_world "$T/w"
REPO="$W/branch"; git init -q -b kronikol-history "$REPO"
cp "$H/bp-history.jsonl" "$REPO/history.jsonl"; echo "history.jsonl merge=union text eol=lf" > "$REPO/.gitattributes"
git -C "$REPO" add -A; git -C "$REPO" -c user.name=b -c user.email=b@example.com commit -q -m base
git -C "$REPO" gc -q --aggressive 2>/dev/null
base=$(du -sk "$REPO/.git/objects" | cut -f1)
for r in 1 2 3 4 5; do
  rm -rf "$W/frag"; python3 "$H/make_fragments.py" "$H/bp-history.jsonl" "$W/frag" $((900 + r)) --suites 18 > /dev/null
  start=$(date +%s.%N)
  kronikol history record "$W/frag" --history "$REPO/history.jsonl" > /dev/null
  end=$(date +%s.%N)
  printf 'record of 18 fragments into the %s-byte ledger: %.2f s\n' "$(stat -c %s "$REPO/history.jsonl")" "$(echo "$end - $start" | bc)"
  git -C "$REPO" add -A; git -C "$REPO" -c user.name=b -c user.email=b@example.com commit -q -m "run $r"
done
git -C "$REPO" gc -q 2>/dev/null
after=$(du -sk "$REPO/.git/objects" | cut -f1)
echo "packed objects: ${base} KB before, ${after} KB after five recorded runs: $(( (after - base) / 5 )) KB per run of eighteen lanes"
echo "tip blob: $(stat -c %s "$REPO/history.jsonl") bytes raw, $(gzip -9 -c "$REPO/history.jsonl" | wc -c) gzipped"
start=$(date +%s.%N); kronikol history show --history "$REPO/history.jsonl" > /dev/null; end=$(date +%s.%N)
printf 'history show on it: %.2f s\n' "$(echo "$end - $start" | bc)"
