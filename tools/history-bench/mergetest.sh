#!/usr/bin/env bash
# CROSS_RUN_HISTORY_PLAN §5.10 / §3.1 — what git actually does to an append-only JSONL ledger.
# Five scenarios, each in a throwaway repo. Usage: bash mergetest.sh [workdir]
set -e
W="${1:-$(mktemp -d)}/kron-mergetest"; rm -rf "$W"; mkdir -p "$W"

new_repo() {           # $1 = dir, $2 = .gitattributes content ("" for none), $3 = autocrlf
  rm -rf "$1"; mkdir -p "$1"; cd "$1"
  git init -q .; git config user.email t@t; git config user.name t
  git config commit.gpgsign false; git config core.autocrlf "$3"
  [ -n "$2" ] && printf '%s\n' "$2" > .gitattributes
  printf '{"t":"header"}\n{"t":"run","id":"base"}\n' > KronikolHistory.jsonl
  git add -A; git commit -qm base >/dev/null 2>&1
  BASE=$(git branch --show-current)
}
branch_append() {      # $1 = branch, $2 = line
  git checkout -q "$BASE"; git checkout -qb "$1"
  printf '%s\n' "$2" >> KronikolHistory.jsonl
  git commit -qam "$1" >/dev/null 2>&1
}

echo "### 1. no .gitattributes — two branches append"
new_repo "$W/1" "" false
branch_append a '{"t":"run","id":"A1"}'; branch_append b '{"t":"run","id":"B1"}'
git checkout -q a; git merge b 2>&1 | grep -i conflict || echo "(no conflict)"; cat KronikolHistory.jsonl

echo; echo "### 2. merge=union — two branches append"
new_repo "$W/2" 'KronikolHistory.jsonl merge=union text eol=lf' false
branch_append a '{"t":"run","id":"A1"}'; branch_append b '{"t":"run","id":"B1"}'
git checkout -q a; git merge b >/dev/null 2>&1 && echo "clean"; cat KronikolHistory.jsonl

echo; echo "### 3. union — IDENTICAL line on both sides (dedup?)"
new_repo "$W/3" 'KronikolHistory.jsonl merge=union text eol=lf' false
branch_append a '{"t":"run","id":"SAME"}'; branch_append b '{"t":"run","id":"SAME"}'
git checkout -q a; git merge b >/dev/null 2>&1 && echo "clean"; cat KronikolHistory.jsonl

echo; echo "### 4. union + COUNTER-KEYED ROSTER — the corruption case"
new_repo "$W/4" 'KronikolHistory.jsonl merge=union text eol=lf' false
branch_append a '{"t":"roster","hash":"r2","ids":["aaa","bbb","ccc"]}'
branch_append b '{"t":"roster","hash":"r2","ids":["aaa","bbb","zzz"]}'
git checkout -q a; git merge b >/dev/null 2>&1 && echo "clean (BAD — two rosters share a key):"
grep -c '"hash":"r2"' KronikolHistory.jsonl | sed 's/^/  rosters named r2: /'

echo; echo "### 5. union under core.autocrlf=true, and a rebase instead of a merge"
new_repo "$W/5" 'KronikolHistory.jsonl merge=union text eol=lf' true
branch_append a '{"t":"run","id":"A1"}'; branch_append b '{"t":"run","id":"B1"}'
git checkout -q a; git merge b >/dev/null 2>&1
echo "worktree bytes:"; cat -A KronikolHistory.jsonl | tail -2
echo "object-store bytes:"; git show HEAD:KronikolHistory.jsonl | cat -A | tail -2
branch_append r '{"t":"run","id":"R1"}'; git rebase a 2>&1 | tail -1; cat KronikolHistory.jsonl

# ---------------------------------------------------------------------------
# 6-9. WHERE DOES GIT READ merge=union FROM? (plan §5.10)
# The answer decides whether a server-side merge (GitHub's merge button) honours it.
# ---------------------------------------------------------------------------
echo; echo "### 6. merge-tree in a normal clone, attribute checked out"
new_repo "$W/6" 'KronikolHistory.jsonl merge=union text eol=lf' false
branch_append a '{"t":"run","id":"A1"}'; branch_append b '{"t":"run","id":"B1"}'
git merge-tree --write-tree a b >/dev/null 2>&1 && echo "  CLEAN" || echo "  CONFLICT"

echo; echo "### 7. same repo, .gitattributes DELETED from the worktree (still committed)"
rm -f .gitattributes
git merge-tree --write-tree a b >/dev/null 2>&1 && echo "  CLEAN" || echo "  CONFLICT  <-- attribute comes from the WORKTREE, not the commit"

echo; echo "### 8. BARE repository (what a server-side merge sees)"
git checkout -q -- .gitattributes 2>/dev/null || true
rm -rf "$W/bare.git"; git clone -q --bare . "$W/bare.git"; cd "$W/bare.git"
git merge-tree --write-tree a b >/dev/null 2>&1 && echo "  CLEAN" || echo "  CONFLICT  <-- union does NOT apply server-side"

echo; echo "### 9. bare repository with -c attr.tree=HEAD (git >= 2.40, OFF by default)"
git -c attr.tree=HEAD merge-tree --write-tree a b >/dev/null 2>&1 && echo "  CLEAN  <-- the opt-in that would make it work" || echo "  CONFLICT"
