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

echo; echo "== W5: the manual recipe of the new text (Without the action), its two run: blocks extracted from the page"
# recipe_manual_read.sh and recipe_manual_record.sh are extract_recipe.py's output for the page's two steps, run the
# way a step with shell: bash runs: bash --noprofile --norc -eo pipefail. Each job has its own RUNNER_TEMP; SERVER and REMOTE are what
# github.server_url gives, here a file:// origin (no token: the header is for HTTP origins, s6 H3 and H4).
step() { # $1 job, $2 run, $3 read or record
  local job="$W/$1"
  [ -d "$job/ws" ] || checkout "$job/ws"
  job_env "$job" "$2"
  [ -d "$job/fragments" ] || python3 "$H/make_fragments.py" "$H/bp-history.jsonl" "$job/fragments" "$2" --suites 3 > /dev/null
  ( cd "$job/ws" && TOKEN= SERVER=file:// REMOTE="file://$W/origin.git" FRAGMENTS="$job/fragments" bash --noprofile --norc -eo pipefail "$H/recipe_manual_$3.sh" ) > "$job/$3.txt" 2>&1
  echo "exit $?"
}
named() { grep -c '^KRONIKOL_HISTORY=' "$W/$1/runner-temp/env"; }
read_files() { ls "$W/$1/runner-temp/kronikol-history" 2> /dev/null | tr '\n' ' '; }
new_world "$T/w5"
echo "a. the first run, no branch yet. read: $(step job1 101 read) ($(grep -v '::add-mask::' "$W/job1/read.txt" | head -1)); KRONIKOL_HISTORY named: $(named job1)"
echo "a. record: $(step job1 101 record)"
branch_report "W5a"
echo "W5a: the branch holds: $(git -C "$W/seed" ls-tree --name-only FETCH_HEAD | tr '\n' ' '); its .gitattributes: $(git -C "$W/seed" show FETCH_HEAD:.gitattributes | tr '\n' ' ')"
echo "W5a: the commit: $(git -C "$W/seed" log -1 --format='%an <%ae>: %s' FETCH_HEAD)"
echo "b. a later run. read: $(step job2 102 read); KRONIKOL_HISTORY named: $(named job2); read: $(read_files job2)"
echo "b. record: $(step job2 102 record)"
branch_report "W5b"
echo "c. run 102's record step again, as a re-run of the record job: $(step job2 102 record) ($(grep -F 'run(s) recorded' "$W/job2/record.txt"))"
branch_report "W5c"
git clone -q --branch kronikol-history "file://$W/origin.git" "$W/person"
SID=$(python3 -c "import json;[print(json.loads(l)['ids'][0]) or exit() for l in open('$W/person/history.jsonl') if json.loads(l)['t']=='roster']")
kronikol history quarantine "$SID" --reason "flaky since the DB upgrade" --by person --until 2026-12-31 --history "$W/person/history.jsonl" > /dev/null
git -C "$W/person" add quarantine.json && git -C "$W/person" -c user.name=p -c user.email=p@example.com commit -q -m "Quarantine $SID" && git -C "$W/person" push -q origin kronikol-history
echo "d. a person quarantines a scenario on the branch; run 103's read: $(step job3 103 read); read: $(read_files job3)"
echo "d. what run 103 reads as quarantined: $(KRONIKOL_HISTORY="$W/job3/runner-temp/kronikol-history/history.jsonl" kronikol history quarantine --list 2>&1 | head -1)"
echo "d. record: $(step job3 103 record)"
branch_report "W5d"
echo "W5d: the branch holds: $(git -C "$W/seed" ls-tree --name-only FETCH_HEAD | tr '\n' ' ')"
mkdir -p "$W/job4/fragments"
echo "e. a record job with nothing downloaded (every test job stopped before its report): $(step job4 104 record) ($(tail -1 "$W/job4/record.txt"))"
branch_report "W5e"
