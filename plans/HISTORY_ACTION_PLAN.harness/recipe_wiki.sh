#!/usr/bin/env bash -e
# The fold job of Cross-Run-History.md, "The recommended shape", verbatim (wiki at 2026-09-27), run the way a
# `run:` block with no `shell:` runs on ubuntu: bash -e. Its checkout step and the download of the fragments
# into ./fragments are what the job does before this block. Run from the checkout.
git worktree add "$RUNNER_TEMP/history-branch" origin/kronikol-history   # or --orphan on the first run
kronikol history record fragments --history "$RUNNER_TEMP/history-branch/history.jsonl"
cd "$RUNNER_TEMP/history-branch" && git add history.jsonl && git commit -m "Record run $GITHUB_RUN_ID" \
  && (git push origin HEAD:kronikol-history || (git fetch origin kronikol-history && git rebase origin/kronikol-history && git push origin HEAD:kronikol-history))
