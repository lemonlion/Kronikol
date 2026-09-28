#!/usr/bin/env bash
# Kronikol: read the ledger on the data branch into a file before the tests, so the reports they write
# carry the history's verdicts. A prototype of 2.3's read (plans/HISTORY_ACTION_PLAN.md §4.3), the sibling
# of kronikol-history-record.sh, written the same way; it is what record_script.sh tests as the read. It
# reads KRONIKOL_REMOTE, KRONIKOL_GIT_AUTH, KRONIKOL_TEMP and KRONIKOL_BRANCH as the record script does, in
# a repository of its own, so no checkout (a persisted credential, a multi-repository checkout whose
# working folder is not a repository) changes what it reads. It never fails the tests:
#
#   exit 0, the ledger at $KRONIKOL_TEMP/kronikol-history.jsonl   the branch has one
#   exit 0, no file                                                no branch yet: the first record starts it
#   exit 3, no file                                                the remote could not be read; the
#                                                                  wrapper warns and the tests run without
set -uo pipefail
export GIT_TERMINAL_PROMPT=0
: "${KRONIKOL_REMOTE:?the repository URL}" "${KRONIKOL_TEMP:?a folder the job owns}"

branch="${KRONIKOL_BRANCH:-kronikol-history}"
repo="$KRONIKOL_TEMP/kronikol-ledger-read"
file="$KRONIKOL_TEMP/kronikol-history.jsonl"
# The header goes under the remote's own URL, set empty first. Git collects extraheader values from every
# level of its configuration: a header a machine persisted for this server would be sent beside this one,
# and one given for no URL is not sent at all when the server has one of its own (record_script.sh G2 to
# G4, G11). The empty value clears what came before it, so this is the one header sent.
auth=()
if [ -n "${KRONIKOL_GIT_AUTH:-}" ]; then
  auth=(-c "http.$KRONIKOL_REMOTE.extraheader=" -c "http.$KRONIKOL_REMOTE.extraheader=$KRONIKOL_GIT_AUTH")
fi

rm -rf "$repo" "$file"
git init -q --bare "$repo" || exit 3
git -C "$repo" ${auth[@]+"${auth[@]}"} ls-remote --exit-code "$KRONIKOL_REMOTE" "refs/heads/$branch" > /dev/null
found=$?
if [ "$found" -eq 2 ]; then
  echo "no $branch branch yet: this run has no history, and its record starts the branch"
  exit 0
fi
if [ "$found" -ne 0 ] \
   || ! git -C "$repo" ${auth[@]+"${auth[@]}"} fetch -q --depth=1 "$KRONIKOL_REMOTE" "refs/heads/$branch" \
   || ! git -C "$repo" show FETCH_HEAD:history.jsonl > "$file"; then
  rm -f "$file"
  echo "could not read $branch from the remote: the tests run without history"
  exit 3
fi
echo "ledger: $(wc -l < "$file" | tr -d ' ') lines from $branch"
