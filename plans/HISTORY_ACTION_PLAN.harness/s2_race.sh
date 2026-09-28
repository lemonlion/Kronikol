#!/usr/bin/env bash
# Plan F4 and §4.4: N fold jobs of N workflow runs finishing together, each with its own checkout and its
# own fragments (3 suites, the consumer's real lines), against one origin. Every run should be on the
# branch exactly once, per suite, and the ledger should verify.
source "$(dirname "$0")/common.sh"
T=${1:-/tmp/kh-s2}
N=${N:-6}
IDENT='git config user.name bot && git config user.email bot@example.com && git fetch -q origin kronikol-history'

prepare_job() { # $1 job dir, $2 run number
  checkout "$W/$1/ws"
  python3 "$H/make_fragments.py" "$H/bp-history.jsonl" "$W/$1/ws/fragments" "$2" --suites 3 > /dev/null
}
run_job() { # $1 job dir, $2 run number, $3 recipe
  (
    job_env "$W/$1" "$2"
    cd "$W/$1/ws"
    case "$3" in
      wiki) eval "$IDENT" && bash -e "$H/recipe_wiki.sh" ;;
      dogfood) bash "$H/recipe_dogfood.sh" ;;
      prototype) bash "$H/prototype_record.sh" fragments "file://$W/origin.git" ;;
    esac
  ) > "$W/$1/out.txt" 2>&1
  echo $? > "$W/$1/exit"
}
race() { # $1 label, $2 recipe, $3 seed-first (yes/no)
  new_world "$T/$1"
  if [ "$3" = yes ]; then prepare_job seed 100; run_job seed 100 dogfood; fi
  for i in $(seq 1 "$N"); do prepare_job "job$i" $((200 + i)); done
  for i in $(seq 1 "$N"); do run_job "job$i" $((200 + i)) "$2" & done
  wait
  local codes=""; for i in $(seq 1 "$N"); do codes+="$(cat "$W/job$i/exit") "; done
  echo "$1: $N writers, exit codes: $codes"
  grep -h -o -E "lost the race[^:]*|CONFLICT[^\n]*|could not push[^\n]*|error: [^\n]*|fatal: [^\n]*" "$W"/job*/out.txt | sort | uniq -c | sed "s/^/$1:   /"
  branch_report "$1"
}
race wiki-branch-exists wiki yes
race dogfood-branch-exists dogfood yes
race prototype-branch-exists prototype yes
race dogfood-first-run dogfood no
race prototype-first-run prototype no
