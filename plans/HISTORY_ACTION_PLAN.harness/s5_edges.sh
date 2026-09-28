#!/usr/bin/env bash
# Plan F1 (the hand-made branch), §4.5 (a rejected push, and what ls-remote answers).
source "$(dirname "$0")/common.sh"
T=${1:-/tmp/kh-s5}
IDENT='git config user.name bot && git config user.email bot@example.com && git fetch -q origin kronikol-history'
job() { # $1 dir, $2 run, $3 command
  checkout "$W/$1/ws"
  python3 "$H/make_fragments.py" "$H/bp-history.jsonl" "$W/$1/ws/fragments" "$2" --suites 3 > /dev/null
  ( job_env "$W/$1" "$2"; cd "$W/$1/ws"; eval "$3" ) > "$W/$1/out.txt" 2>&1; echo $? > "$W/$1/exit"
}

echo "== E1: the branch made by hand as the wiki's comment says (--orphan, no .gitattributes), two writers at once"
new_world "$T/e1"
git clone -q "file://$W/origin.git" "$W/person"
git -C "$W/person" checkout -q --orphan kronikol-history && git -C "$W/person" rm -rfq .
kronikol history init --history "$W/person/history.jsonl" > /dev/null && rm "$W/person/.gitattributes"
git -C "$W/person" add history.jsonl && git -C "$W/person" -c user.name=p -c user.email=p@example.com commit -q -m start && git -C "$W/person" push -q origin kronikol-history
for i in 1 2; do job "j$i" $((300 + i)) "$IDENT && bash -e '$H/recipe_wiki.sh'" & done; wait
echo "exit codes: $(cat "$W/j1/exit") $(cat "$W/j2/exit")"; grep -h -m1 -E "CONFLICT|could not apply" "$W"/j*/out.txt
branch_report "E1"

echo; echo "== E2: the server refuses every push to the branch (a ruleset, a hook): the prototype"
new_world "$T/e2"
mkdir -p "$W/origin.git/hooks"
printf '#!/bin/sh\nwhile read old new ref; do case "$ref" in refs/heads/kronikol-history) echo "refusing $ref: protected by a ruleset" >&2; exit 1;; esac; done\n' > "$W/origin.git/hooks/pre-receive"
chmod +x "$W/origin.git/hooks/pre-receive"
job j1 401 "KRONIKOL_PUSH_ATTEMPTS=3 bash '$H/prototype_record.sh' fragments 'file://$W/origin.git'"
echo "exit $(cat "$W/j1/exit")"; grep -E "lost the race|error" "$W/j1/out.txt"
branch_report "E2"

echo; echo "== E3: what ls-remote --exit-code answers"
new_world "$T/e3"
git ls-remote --exit-code "file://$W/origin.git" refs/heads/kronikol-history > /dev/null 2>&1; echo "branch absent: $?"
git ls-remote --exit-code "file://$W/origin.git" refs/heads/main > /dev/null 2>&1; echo "branch present: $?"
git ls-remote --exit-code "file://$W/no-such-origin.git" refs/heads/main > /dev/null 2>&1; echo "origin unreachable: $?"
