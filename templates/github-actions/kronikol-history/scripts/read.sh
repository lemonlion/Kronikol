#!/usr/bin/env bash
# The read phase (plans/HISTORY_ACTION_PLAN.md section 4.3). Before the tests: the ledger and its two companions,
# quarantine.json and aliases.json, from the data branch into the job's temporary directory, side by side as the
# library reads them, and the ledger named in KRONIKOL_HISTORY for the job's later steps. The run's own report,
# Failures.md and job summary then carry the verdicts; the run appends its own line to the copy, which nothing reads
# back.
#
# It never fails the job, as a test run never fails over its history: everything that stops it is a warning or a
# notice, and the tests run without history. It uses a repository of its own, never the job's checkout, and fetches
# the branch's tip without its files (a blobless fetch), then only the three it reads: whatever else the branch holds
# is never downloaded.
set -uo pipefail
. "$(dirname "$0")/lib.sh"
. "$(dirname "$0")/git.sh"

branch=${KRONIKOL_HISTORY_BRANCH:?KRONIKOL_HISTORY_BRANCH names the data branch}
repo="$KH_TEMP/kronikol-history-read"
out="$KH_TEMP/kronikol-history"
err="$KH_TEMP/kronikol-history-read.err"

nothing() {
  kh_output found false
  kh_output ledger ""
  kh_output bytes 0
  exit 0
}

rm -rf "$repo" "$out"
mkdir -p "$out"
if ! git init -q -b "$branch" "$repo" 2> "$err" || ! git -C "$repo" remote add origin "$KRONIKOL_HISTORY_REMOTE" 2> "$err"; then
  kh_warning "could not start a repository to read $branch in ($(kh_message "$err")); the tests run without history"
  nothing
fi

status=0
git -C "$repo" ls-remote --exit-code origin "refs/heads/$branch" > /dev/null 2> "$err" || status=$?
case $status in
  0) ;;
  2)
    kh_notice "no $branch branch on $KRONIKOL_HISTORY_REMOTE yet, so this run has no history to be read against; the first record starts one"
    nothing
    ;;
  *)
    kh_warning "could not read $branch from $KRONIKOL_HISTORY_REMOTE (git ls-remote exit $status: $(kh_message "$err")); the tests run without history"
    nothing
    ;;
esac

if ! git -C "$repo" fetch -q --depth=1 --filter=blob:none origin "refs/heads/$branch" 2> "$err"; then
  kh_warning "could not fetch $branch from $KRONIKOL_HISTORY_REMOTE ($(kh_message "$err")); the tests run without history"
  nothing
fi

if [ -z "$(git -C "$repo" ls-tree --name-only FETCH_HEAD -- history.jsonl 2> /dev/null)" ]; then
  kh_notice "$branch holds no history.jsonl yet, so this run has no history to be read against; the first record starts one"
  nothing
fi

for file in history.jsonl quarantine.json aliases.json; do
  if [ -n "$(git -C "$repo" ls-tree --name-only FETCH_HEAD -- "$file" 2> /dev/null)" ] \
    && ! git -C "$repo" cat-file blob "FETCH_HEAD:$file" > "$out/$file" 2> "$err"; then
    rm -rf "$out"
    kh_warning "could not read $file from $branch ($(kh_message "$err")); the tests run without history"
    nothing
  fi
done

ledger=$(kh_native "$out/history.jsonl")
bytes=$(wc -c < "$out/history.jsonl" | tr -d ' ')
kh_export KRONIKOL_HISTORY "$ledger"
kh_output found true
kh_output ledger "$ledger"
kh_output bytes "$bytes"
echo "Kronikol history: $bytes bytes of ledger from $branch, $(ls "$out" | tr '\n' ' ')in $ledger"
