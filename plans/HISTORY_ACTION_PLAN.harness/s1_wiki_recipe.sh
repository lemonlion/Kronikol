#!/usr/bin/env bash
# Plan F1: the wiki's recipe, as written, on the runs a new user meets first.
source "$(dirname "$0")/common.sh"
T=${1:-/tmp/kh-s1}
fold_job() { # $1 world-relative job dir, $2 run number, $3 the script, [$4 extra setup]
  local job="$W/$1"
  checkout "$job/ws"
  job_env "$job" "$2"
  python3 "$H/make_fragments.py" "$H/bp-history.jsonl" "$job/ws/fragments" "$2" --suites 3 > /dev/null
  ( cd "$job/ws" && eval "${4:-true}" && bash -e "$3" ) > "$job/out.txt" 2>&1
  echo "exit $?"
}

echo "== W1: the first run, no kronikol-history branch yet"
new_world "$T/w1"
echo "fold job: $(fold_job job1 101 "$H/recipe_wiki.sh")"; grep -m1 -E "fatal|error" "$W/job1/out.txt"
branch_report "W1"

echo; echo "== W2: the branch exists (made by the dogfood recipe), the next run's fold job"
new_world "$T/w2"
echo "seed (dogfood recipe): $(fold_job seed 100 "$H/recipe_dogfood.sh")"
echo "fold job: $(fold_job job1 101 "$H/recipe_wiki.sh")"; grep -m1 -E "fatal|error" "$W/job1/out.txt"
branch_report "W2"

echo; echo "== W3: as W2, with the fetch the fold job lacks added before the recipe"
new_world "$T/w3"
echo "seed (dogfood recipe): $(fold_job seed 100 "$H/recipe_dogfood.sh")"
echo "fold job: $(fold_job job1 101 "$H/recipe_wiki.sh" 'git fetch -q origin kronikol-history')"; grep -m2 -E "fatal|error|identity|tell me" "$W/job1/out.txt"
branch_report "W3"

echo; echo "== W4: as W3, with a committer identity too: one writer"
new_world "$T/w4"
echo "seed (dogfood recipe): $(fold_job seed 100 "$H/recipe_dogfood.sh")"
IDENT='git config user.name bot && git config user.email bot@example.com && git fetch -q origin kronikol-history'
echo "fold job: $(fold_job job1 101 "$H/recipe_wiki.sh" "$IDENT")"
branch_report "W4"
