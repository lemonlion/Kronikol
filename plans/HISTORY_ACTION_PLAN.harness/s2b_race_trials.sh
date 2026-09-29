#!/usr/bin/env bash
# s2_race.sh repeated: TRIALS races of N writers per recipe, counting runs lost and roster lines duplicated.
source "$(dirname "$0")/common.sh"
T=${1:-/tmp/kh-s2b}; N=${N:-6}; TRIALS=${TRIALS:-5}
prepare_job() { checkout "$W/$1/ws"; python3 "$H/make_fragments.py" "$H/bp-history.jsonl" "$W/$1/ws/fragments" "$2" --suites 3 > /dev/null; }
run_job() {
  ( job_env "$W/$1" "$2"; cd "$W/$1/ws"
    case "$3" in
      dogfood) bash "$H/recipe_dogfood.sh" ;;
      dogfood_fair) bash "$H/recipe_dogfood_fair.sh" ;;
      prototype) bash "$H/prototype_record.sh" fragments "file://$W/origin.git" ;;
      action) bash "$H/action_record.sh" fragments "file://$W/origin.git" ;;
      manual) TOKEN= SERVER=file:// REMOTE="file://$W/origin.git" FRAGMENTS="$PWD/fragments" bash --noprofile --norc -eo pipefail "$H/recipe_manual_record.sh" ;;
    esac ) > "$W/$1/out.txt" 2>&1
  echo $? > "$W/$1/exit"
}
count() { # prints: runs-recorded duplicate-roster-lines
  local tmp; tmp=$(mktemp -d)
  git -C "$W/seed" fetch -q "file://$W/origin.git" kronikol-history && git -C "$W/seed" show FETCH_HEAD:history.jsonl > "$tmp/h"
  python3 -c "
import json,collections,sys
ids=set(); r=collections.Counter()
for l in open('$tmp/h'):
    o=json.loads(l)
    if o['t']=='run': ids.add(o['id'])
    if o['t']=='roster': r[o['hash']]+=1
print(len([i for i in ids if not i.startswith('gh:100:')]), sum(v-1 for v in r.values()))"
  rm -rf "$tmp"
}
for recipe in ${RECIPES:-dogfood dogfood_fair prototype}; do
  for first in yes no; do
    lost=0; dup=0; failed=0; retries=0
    for t in $(seq 1 "$TRIALS"); do
      new_world "$T/$recipe-$first-$t"
      if [ "$first" = yes ]; then prepare_job seed 100; run_job seed 100 dogfood; fi
      for i in $(seq 1 "$N"); do prepare_job "job$i" $((200 + i)); done
      for i in $(seq 1 "$N"); do run_job "job$i" $((200 + i)) "$recipe" & done
      wait
      for i in $(seq 1 "$N"); do [ "$(cat "$W/job$i/exit")" = 0 ] || failed=$((failed + 1)); done
      for i in $(seq 1 "$N"); do retries=$((retries + $(grep -c "lost the race" "$W/job$i/out.txt" || true))); done
      read -r recorded d < <(count)
      lost=$((lost + N - recorded)); dup=$((dup + d))
      rm -rf "$W"
    done
    label=$([ "$first" = yes ] && echo "branch exists" || echo "first run    ")
    echo "$recipe ($label): $TRIALS races of $N writers: $failed jobs failed, $lost runs lost, $dup duplicate roster lines, $retries lost races recorded again"
  done
done
