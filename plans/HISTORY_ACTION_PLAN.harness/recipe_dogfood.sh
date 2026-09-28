#!/usr/bin/env bash
# The fold step of .github/workflows/ci-summary-preview.yml at e4c9e36, verbatim but for the tool: the
# workflow runs it from source with `dotnet run --project src/Kronikol.Tool/...`, the consumer's copy
# (BreakfastProvider ci-main.yml) runs the installed `kronikol`, as here. Run from the checkout.
set -euo pipefail
git config user.name "github-actions[bot]"
git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
WT="$RUNNER_TEMP/history-branch"
# FETCH_HEAD is per-worktree, so it cannot be named from inside the new one; the fetch above
# also updated origin/kronikol-history, which can.
if git fetch origin kronikol-history; then
  git worktree add -B kronikol-history "$WT" origin/kronikol-history
else
  git worktree add --detach "$WT"
  git -C "$WT" checkout -q --orphan kronikol-history
  git -C "$WT" rm -rfq .
  printf '%s\n' "# Kronikol history ledger" "" \
    "The cross-run ledger of this repository's own CI Summary Preview runs (plans/CROSS_RUN_HISTORY_PLAN.md, section 11):" \
    "an orphan data branch, appended to by the history job of .github/workflows/ci-summary-preview.yml and never merged into main." \
    "" "Read it with: git fetch origin kronikol-history && git show FETCH_HEAD:history.jsonl" \
    > "$WT/README.md"
  printf '%s\n' "history.jsonl merge=union text eol=lf" > "$WT/.gitattributes"
fi
if [ ! -d fragments ] || [ -z "$(find fragments -name History.run.json -print -quit)" ]; then
  echo "no fragments to record"
  exit 0
fi
kronikol history record fragments --history "$WT/history.jsonl"
cd "$WT"
git add history.jsonl README.md .gitattributes
if git diff --cached --quiet; then
  echo "nothing new to record"
  exit 0
fi
git commit -q -m "Record run ${GITHUB_RUN_ID}:${GITHUB_RUN_ATTEMPT} (${GITHUB_SHA::7})"
for attempt in 1 2 3 4 5; do
  if git push origin HEAD:kronikol-history; then
    exit 0
  fi
  echo "push $attempt lost the race; rebasing onto the branch as it is now"
  git fetch origin kronikol-history
  git rebase origin/kronikol-history
done
echo "could not push the ledger after 5 attempts"
exit 1
