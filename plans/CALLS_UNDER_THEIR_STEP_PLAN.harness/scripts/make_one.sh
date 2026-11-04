#!/bin/bash
# The two prototypes built as one, on current main, outside Temp (Storage Sense takes DLLs from Temp).
set -u
H=/c/Code/Kronikol-225/plans/CALLS_UNDER_THEIR_STEP_PLAN.harness
W=${1:-/c/Code/Kronikol-225x}
REPO=/c/Code/Kronikol-225
BASE=$(git -C "$REPO" rev-parse origin/main)
echo "base $BASE"
if [ ! -d "$W" ]; then
  git -C "$REPO" worktree add --detach "$W" "$BASE" 2>&1 | tail -1
fi
cd "$W" || exit 1
git diff --quiet HEAD -- src || { echo "already patched"; exit 0; }
git -C "$REPO" diff 27cc0e46 abf7b0a3 -- src tests > /tmp/k225-223.patch 2>/dev/null || true
P=$(mktemp -d)
git -C "$REPO" diff 27cc0e46 abf7b0a3 -- src tests > "$P/223.patch"
wc -l "$P/223.patch"
git apply --3way --whitespace=nowarn "$P/223.patch" 2>&1 | grep -v -E "^(Falling back|Applied patch|Checking patch|U )" | head -30
echo "--- after #223's prototype:"; git status --short | grep -E '^(UU|AA|DU|UD)' | cut -c1-100
git apply --3way --whitespace=nowarn "$H/prototype/on-223-abf7b0a3.patch" 2>&1 | grep -v -E "^(Falling back|Applied patch|Checking patch|U )" | head -30
echo "--- after the as-one patch:"; git status --short | grep -E '^(UU|AA|DU|UD)' | cut -c1-100
git status --short | wc -l
