#!/usr/bin/env bash
# The record phase (plans/HISTORY_ACTION_PLAN.md section 4.5). In a job after every test job: this run's
# History.run.json fragments folded into the ledger on the data branch with kronikol history record, and pushed.
#
# Each attempt starts a repository of its own in the job's temporary directory from the branch's tip on the remote;
# the job's checkout is never used, so its version, its persisted credentials and its absence change nothing. Only
# git ls-remote's exit 2 ("no such branch") ever starts the branch, as an orphan with the ledger's header, its
# merge=union attribute and a README; a remote that cannot be read is an error, never an empty ledger. The run's
# lines are appended, committed and pushed, never forced. When the push is refused:
#   - the branch moved: another run's push landed first. Nothing is rebased: the fold is recorded again on the branch
#     as it now is, which kronikol history record makes safe (a run already in the ledger is a duplicate) and which
#     works out what record derives from the ledger against the ledger the line lands in;
#   - the branch did not move, checked after a pause so that a competing push has landed: a ruleset, a hook or a token
#     that cannot write refused it, and that does not go away in a second, so the phase fails at once, naming it.
# With N folds finishing together none loses more than N - 1 times, so eight attempts cover eight at once.
set -euo pipefail
. "$(dirname "$0")/lib.sh"
. "$(dirname "$0")/git.sh"
. "$(dirname "$0")/tool.sh"

branch=${KRONIKOL_HISTORY_BRANCH:?KRONIKOL_HISTORY_BRANCH names the data branch}
attempts=${KRONIKOL_HISTORY_ATTEMPTS:-8}
repo="$KH_TEMP/kronikol-history-record"
fold="$KH_TEMP/kronikol-history-fold"
err="$KH_TEMP/kronikol-history-record.err"
out="$KH_TEMP/kronikol-history-record.out"

outputs() {
  kh_output recorded "$1"
  kh_output duplicates "$2"
  kh_output pushed "$3"
  kh_output commit "$4"
  kh_output ledger-bytes "$5"
}

if [ "${KRONIKOL_HISTORY_PULL_REQUEST:-false}" = true ] && [ "${KRONIKOL_HISTORY_RECORD_PULL_REQUESTS:-false}" != true ]; then
  kh_notice "a pull request's run is not recorded; record-pull-requests: true records it, under a stream of its own"
  outputs 0 0 false "" 0
  exit 0
fi

case $attempts in
  '' | *[!0-9]* | 0)
    kh_error "attempts is '$attempts': a positive number of pushes to try"
    exit 1
    ;;
esac

if ! git check-ref-format --branch "$branch" > /dev/null 2>&1; then
  kh_error "branch '$branch' is not a branch name git takes"
  exit 1
fi

# The fragments to fold: a directory given, or what the download step fetched. A staged rotation (runs/.incoming-*)
# and .git are left out, so the tool is handed only runs that were whole.
source=${KRONIKOL_HISTORY_PATH:-}
if [ -n "$source" ]; then source=$(kh_posix "$source"); else source="$KH_TEMP/kronikol-history-fragments"; fi
rm -rf "$fold"
mkdir -p "$fold"
if [ -d "$source" ]; then
  (cd "$source" && find . \( -name .git -o -path '*/runs/.incoming-*' \) -prune -o -type f -name History.run.json -print) |
    while IFS= read -r fragment; do
      mkdir -p "$fold/$(dirname "$fragment")"
      cp "$source/$fragment" "$fold/$fragment"
    done
fi
if [ -z "$(find "$fold" -type f -name History.run.json -print)" ]; then
  kh_notice "no History.run.json to record under ${KRONIKOL_HISTORY_PATH:-the downloaded artifacts}: nothing to record (a test job that stopped before writing its report leaves none)"
  outputs 0 0 false "" 0
  exit 0
fi

# The ledger never lives on the branch people merge code into.
default=$(git ls-remote --symref "$KRONIKOL_HISTORY_REMOTE" HEAD 2> /dev/null | sed -n 's#^ref: refs/heads/\(.*\)[[:space:]]HEAD$#\1#p' || true)
if [ -n "$default" ] && [ "$default" = "$branch" ]; then
  kh_error "branch '$branch' is the repository's default branch; the ledger lives on a data branch of its own (kronikol-history by default)"
  exit 1
fi

kh_tool_prepare

# The branch's tip on the remote, into TIP: a commit id, or "absent". Anything else stops the phase.
tip() {
  local listed status=0
  listed=$(git ls-remote --exit-code "$KRONIKOL_HISTORY_REMOTE" "refs/heads/$branch" 2> "$err") || status=$?
  case $status in
    0) TIP=${listed%%[[:space:]]*} ;;
    2) TIP=absent ;;
    *)
      kh_error "could not read $branch from $KRONIKOL_HISTORY_REMOTE (git ls-remote exit $status: $(kh_message "$err")); nothing was recorded"
      exit 1
      ;;
  esac
}

readme() {
  printf '%s\n' "# Kronikol history ledger" "" \
    "The cross-run history of this repository's test runs, appended to by the record phase of the kronikol-history" \
    "action: an orphan data branch, sharing no history with the code, never merged into another branch." "" \
    "Read it: \`git fetch origin $branch && git show FETCH_HEAD:history.jsonl > history.jsonl\`, then" \
    "\`kronikol history show --history history.jsonl\`."
}

rename_flag=
[ "${KRONIKOL_HISTORY_ACCEPT_RENAMES:-false}" != true ] || rename_flag=--accept-renames

attempt=0
while [ "$attempt" -lt "$attempts" ]; do
  attempt=$((attempt + 1))
  tip
  base=$TIP
  rm -rf "$repo"
  # HEAD starts unborn on the branch, so a first commit is the orphan root and a fetched tip is reset onto.
  git init -q -b "$branch" "$repo"
  git -C "$repo" remote add origin "$KRONIKOL_HISTORY_REMOTE"
  git -C "$repo" config user.name "github-actions[bot]"
  git -C "$repo" config user.email "41898282+github-actions[bot]@users.noreply.github.com"
  if [ "$base" = absent ]; then
    if ! kh_tool history init --history "$(kh_native "$repo/history.jsonl")" > "$out" 2>&1; then
      kh_verbatim "$out"
      kh_error "kronikol history init could not start the ledger ($(kh_message "$out"))"
      exit 1
    fi
    readme > "$repo/README.md"
  else
    if ! git -C "$repo" fetch -q --depth=1 origin "refs/heads/$branch" 2> "$err"; then
      kh_error "could not fetch $branch from $KRONIKOL_HISTORY_REMOTE ($(kh_message "$err")); nothing was recorded"
      exit 1
    fi
    # The branch may have moved since ls-remote: the base is what was fetched.
    base=$(git -C "$repo" rev-parse FETCH_HEAD)
    git -C "$repo" reset -q --hard FETCH_HEAD
  fi

  status=0
  kh_tool history record "$(kh_native "$fold")" --history "$(kh_native "$repo/history.jsonl")" $rename_flag > "$out" 2>&1 || status=$?
  kh_verbatim "$out"
  if [ "$status" -ne 0 ]; then
    kh_error "kronikol history record exited $status ($(kh_message "$out")); nothing was pushed"
    exit 1
  fi
  counts=$(sed -n -E 's/^([0-9]+) run\(s\) recorded, ([0-9]+) already there.*/\1 \2/p' "$out" | tail -n 1)
  recorded=${counts%% *}
  duplicates=${counts##* }
  bytes=$(wc -c < "$repo/history.jsonl" | tr -d ' ')

  git -C "$repo" add -A
  if git -C "$repo" diff --cached --quiet; then
    echo "Kronikol history: nothing new to record; every run of these fragments is on $branch already"
    outputs "${recorded:-0}" "${duplicates:-0}" false "" "$bytes"
    exit 0
  fi
  if ! git -C "$repo" commit -q --no-verify -m "Record run ${KRONIKOL_HISTORY_RUN:?} (${KRONIKOL_HISTORY_SHA:0:7}) [skip ci]" 2> "$err"; then
    kh_error "git could not commit the ledger: $(kh_message "$err")"
    exit 1
  fi
  if git -C "$repo" push -q --no-verify origin "HEAD:refs/heads/$branch" 2> "$err"; then
    commit=$(git -C "$repo" rev-parse HEAD)
    echo "Kronikol history: pushed $commit to $branch on attempt $attempt"
    outputs "${recorded:-0}" "${duplicates:-0}" true "$commit" "$bytes"
    {
      printf '### Kronikol history: recorded on `%s`\n\n' "$branch"
      printf '%s run(s) recorded, %s already there. The ledger is %s bytes, at commit %s.\n\n' "${recorded:-0}" "${duplicates:-0}" "$bytes" "${commit:0:7}"
      printf '````text\n'
      grep -E '^(recorded|duplicate|not recorded) ' "$out" || true
      printf '````\n\n'
      sed -n -E 's/^recorded   [^ ]+  (.*)  [0-9]+ scenarios.*/\1/p' "$out" | sort -u | while IFS= read -r suite; do
        [ "$suite" != "(no suite)" ] || continue
        printf '#### suite %s\n\n````text\n' "$suite"
        kh_tool history show --history "$(kh_native "$repo/history.jsonl")" --suite "$suite" 2>&1 || true
        printf '````\n\n'
      done
    } | kh_summary
    warn=${KRONIKOL_HISTORY_WARN_LEDGER_MB:-50}
    if awk -v b="$bytes" -v mb="$warn" 'BEGIN { exit !(mb + 0 > 0 && b + 0 >= mb * 1048576) }'; then
      kh_warning "the ledger on $branch is $bytes bytes, past the $warn MB this warns from: every read fetches it whole and every test run reads it. Its retention is plans/HISTORY_ACTION_PLAN.md Q3"
    fi
    exit 0
  fi

  # Refused. A competing push that still held the ref's lock lands during the pause, so a branch that has not moved
  # after it means the refusal was not a race.
  cp "$err" "$err.push"
  sleep "0.$(((RANDOM % 9) + 1))"
  tip
  if [ "$TIP" = "$base" ]; then
    kh_error "$KRONIKOL_HISTORY_REMOTE refused the push and $branch did not move, so this was not a lost race: $(kh_message "$err.push")"
    exit 1
  fi
  echo "Kronikol history: push $attempt lost the race to another run; recording again on $branch as it is now"
done

kh_error "could not push the ledger to $branch after $attempts attempts, each lost to another run's push: record again, which is safe"
exit 1
