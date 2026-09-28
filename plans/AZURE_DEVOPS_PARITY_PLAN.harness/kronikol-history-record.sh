#!/usr/bin/env bash
# Kronikol: fold this run's History.run.json fragments into the ledger on the data branch, and push it.
#
# A prototype of the record script roadmap 2.3 (GitHub) and 2.4 (Azure Pipelines) will share.
# plans/HISTORY_ACTION_PLAN.md designs the one that ships (its §4.5 and §4.9), and
# plans/AZURE_DEVOPS_PARITY_PLAN.md §4.5 wraps it for Azure Pipelines. This copy was written independently
# to the same design, with the Azure DevOps inputs and the header reset that plan asks of 2.3's; it is what
# record_script.sh tests as v2. Bash 3.2 or later, git and the Kronikol tool (`kronikol`) on the path.
# Nothing in it names a provider; the wrapper sets:
#
#   KRONIKOL_REMOTE      the repository's URL: the ledger is fetched from it and pushed to it
#   KRONIKOL_GIT_AUTH    an http.extraheader value ("AUTHORIZATION: bearer <token>"), or empty to use git's
#                        own credentials; passed to each command that talks to the remote, never written down
#   KRONIKOL_FRAGMENTS   the folder the fragments were downloaded into
#   KRONIKOL_TEMP        a folder the job owns; the ledger is checked out under it
#   KRONIKOL_RUN_LABEL   how the commit message names this run
#   KRONIKOL_BRANCH      the data branch; kronikol-history when empty
#
# The ledger gets a repository of its own under KRONIKOL_TEMP, not a worktree of the job's checkout, so
# nothing that checkout holds can reach it: not a credential it persisted (which git would prefer to the
# one passed here), not a worktree an earlier job registered on a reused agent. The job needs no checkout.
# A push that loses a race starts again from the branch as it is now and folds again: a run already on the
# branch is a duplicate, so the fold is the merge, and no rebase or merge driver is involved. The fetch is
# one commit deep however long the branch's history grows.
set -euo pipefail
export GIT_TERMINAL_PROMPT=0
: "${KRONIKOL_REMOTE:?the repository URL}" "${KRONIKOL_FRAGMENTS:?the fragments folder}"
: "${KRONIKOL_TEMP:?a folder the job owns}" "${KRONIKOL_RUN_LABEL:?the run, for the commit message}"

branch="${KRONIKOL_BRANCH:-kronikol-history}"
ledger="$KRONIKOL_TEMP/kronikol-ledger"
# The header goes under the remote's own URL, set empty first. Git collects extraheader values from every
# level of its configuration: a header a machine persisted for this server would be sent beside this one,
# and one given for no URL is not sent at all when the server has one of its own (record_script.sh G2 to
# G4, G11). The empty value clears what came before it, so this is the one header sent.
auth=()
if [ -n "${KRONIKOL_GIT_AUTH:-}" ]; then
  auth=(-c "http.$KRONIKOL_REMOTE.extraheader=" -c "http.$KRONIKOL_REMOTE.extraheader=$KRONIKOL_GIT_AUTH")
fi

# Bash 3.2, which macOS ships, calls an empty array unbound under set -u; this form expands to nothing.
remote() { git -C "$ledger" ${auth[@]+"${auth[@]}"} "$@"; }

if [ -z "$(find "$KRONIKOL_FRAGMENTS" -name History.run.json 2>/dev/null | head -n 1)" ]; then
  echo "no History.run.json under $KRONIKOL_FRAGMENTS: nothing to record"
  exit 0
fi

# The ledger folder, set to the branch as the remote holds it now, or to a new orphan branch when the
# remote has none. ls-remote tells those apart (2 is "no such ref"); any other failure stops the step, so
# a remote that cannot be read is never taken for an empty one.
checkout_tip() {
  rm -rf "$ledger"
  git init -q "$ledger"
  git -C "$ledger" symbolic-ref HEAD "refs/heads/$branch"
  git -C "$ledger" remote add origin "$KRONIKOL_REMOTE"
  git -C "$ledger" config user.name "Kronikol history"
  git -C "$ledger" config user.email "kronikol-history@noreply.invalid"
  set +e
  remote ls-remote --exit-code origin "refs/heads/$branch" > /dev/null
  found=$?
  set -e
  if [ "$found" -eq 0 ]; then
    remote fetch -q --depth=1 origin "+refs/heads/$branch:refs/remotes/origin/$branch"
    git -C "$ledger" reset -q --hard "refs/remotes/origin/$branch"
  elif [ "$found" -eq 2 ]; then
    printf '%s\n' "history.jsonl merge=union text eol=lf" > "$ledger/.gitattributes"
    printf '%s\n' "# Kronikol history ledger" "" \
      "Kronikol's cross-run ledger: an orphan data branch its CI appends to, never merged." "" \
      "Read it with: git fetch origin $branch && git show FETCH_HEAD:history.jsonl" > "$ledger/README.md"
  else
    echo "could not read $branch from the remote (git ls-remote exit $found); nothing recorded"
    exit 1
  fi
}

for attempt in 1 2 3 4 5; do
  checkout_tip
  kronikol history record "$KRONIKOL_FRAGMENTS" --history "$ledger/history.jsonl"
  git -C "$ledger" add -A
  if git -C "$ledger" diff --cached --quiet; then
    echo "nothing new to record"
    exit 0
  fi
  git -C "$ledger" commit -q -m "Record $KRONIKOL_RUN_LABEL [skip ci]"
  if remote push -q origin "HEAD:refs/heads/$branch"; then
    exit 0
  fi
  echo "push $attempt lost the race; folding again onto $branch as it is now"
done
echo "could not push the ledger after 5 attempts"
exit 1
