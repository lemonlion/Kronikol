#!/usr/bin/env bash
# The record step this plan proposes (plan §4.5), as a script. It needs no checkout: each attempt builds a
# fresh one-branch repository in $RUNNER_TEMP from origin's tip. On a lost race it does not rebase: it
# takes the branch as it now is and records again, which `kronikol history record` makes safe (a run
# already in the ledger is a duplicate) and which recomputes what record derives from the ledger (the
# partial heuristic, the rename suggestions, later the view) against the ledger it is appended to.
# A push that failed while the branch did not move, checked after the pause, is not a race (a ruleset,
# a hook, a read-only token), and is not retried.
# Usage: prototype_record.sh <fragments-dir> <origin-url>   (KRONIKOL_TOKEN: the token, for an http(s) origin)
set -euo pipefail
FRAGMENTS=$(cd "$1" && pwd) ORIGIN=$2
BRANCH=${KRONIKOL_BRANCH:-kronikol-history}
ATTEMPTS=${KRONIKOL_PUSH_ATTEMPTS:-8}
REPO="$RUNNER_TEMP/kronikol-history-record"

# Git settings for this script's own git calls, passed in the environment so they reach no file and no
# command line: never ask for anything (no terminal prompt, no credential manager window), never hand the
# machine's credential helpers a failed token (git would erase what they store), no line-ending conversion,
# and no hook of the machine's (a policy hook meant for people's commits). The rest of the machine's
# configuration stands: proxies, CA bundles and signing keep working. The token goes in as checkout's
# header, scoped to the origin's server, and is masked before its first use.
export GIT_TERMINAL_PROMPT=0 GCM_INTERACTIVE=Never
cfg=(credential.helper "" core.autocrlf false core.hooksPath "$RUNNER_TEMP/kronikol-history-no-hooks")
if [ -n "${KRONIKOL_TOKEN:-}" ]; then
  basic=$(printf 'x-access-token:%s' "$KRONIKOL_TOKEN" | base64 | tr -d '\n')
  echo "::add-mask::$basic"
  server=$(printf '%s\n' "$ORIGIN" | sed -E 's#^(https?://[^/]+)/.*#\1/#')
  cfg+=("http.$server.extraheader" "AUTHORIZATION: basic $basic")
fi
export GIT_CONFIG_COUNT=$(( ${#cfg[@]} / 2 ))
for ((i = 0; i < ${#cfg[@]} / 2; i++)); do
  export "GIT_CONFIG_KEY_$i=${cfg[2 * i]}" "GIT_CONFIG_VALUE_$i=${cfg[2 * i + 1]}"
done

if [ -z "$(find "$FRAGMENTS" -name History.run.json -not -path '*/runs/.incoming-*' -print -quit)" ]; then
  echo "no History.run.json under $FRAGMENTS: nothing to record"
  exit 0
fi

# The branch's tip on origin: a commit id, "absent", or a failure. Only "absent" ever makes a new branch.
tip() {
  local out status=0
  out=$(git ls-remote --exit-code "$ORIGIN" "refs/heads/$BRANCH") || status=$?
  case $status in
    0) echo "${out%%[[:space:]]*}" ;;
    2) echo absent ;;
    *) echo "::error::could not read $BRANCH from origin (git ls-remote exit $status)" >&2; return 1 ;;
  esac
}

for attempt in $(seq 1 "$ATTEMPTS"); do
  base=$(tip)
  rm -rf "$REPO"
  git init -q "$REPO"
  git -C "$REPO" config user.name "github-actions[bot]"
  git -C "$REPO" config user.email "41898282+github-actions[bot]@users.noreply.github.com"
  if [ "$base" = absent ]; then
    git -C "$REPO" checkout -q --orphan "$BRANCH"
    kronikol history init --history "$REPO/history.jsonl" > /dev/null
    printf '%s\n' "# Kronikol history ledger" "" \
      "Written by the kronikol-history action. An orphan data branch: never merge it into another branch." \
      "Read it: git fetch origin $BRANCH && git show FETCH_HEAD:history.jsonl > history.jsonl" > "$REPO/README.md"
  else
    git -C "$REPO" fetch -q --depth=1 "$ORIGIN" "$base"
    git -C "$REPO" checkout -q -b "$BRANCH" FETCH_HEAD
  fi

  kronikol history record "$FRAGMENTS" --history "$REPO/history.jsonl"
  git -C "$REPO" add -A
  if git -C "$REPO" diff --cached --quiet; then
    echo "nothing new to record"
    exit 0
  fi
  git -C "$REPO" commit -q --no-verify -m "Record run ${GITHUB_RUN_ID}:${GITHUB_RUN_ATTEMPT} (${GITHUB_SHA::7})"
  if git -C "$REPO" push -q --no-verify "$ORIGIN" "HEAD:refs/heads/$BRANCH" 2> "$RUNNER_TEMP/push.err"; then
    echo "pushed on attempt $attempt"
    exit 0
  fi
  # The pause comes first, so a competing push that still held the ref's lock has landed before the tip
  # is compared: only a branch that has not moved after it means the refusal was not a race.
  sleep "0.$(( (RANDOM % 9) + 1 ))"
  if [ "$(tip)" = "$base" ]; then
    echo "::error::origin refused the push and $BRANCH did not move, so this was not a race: $(grep -m1 -v '^To ' "$RUNNER_TEMP/push.err")"
    exit 1
  fi
  echo "push $attempt lost the race; recording again on the branch as it is now"
done
echo "::error::could not push the ledger after $ATTEMPTS attempts"
exit 1
