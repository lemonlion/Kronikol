#!/usr/bin/env bash
# Sourced by every scenario. A world is a bare "origin" holding one commit on main, a `kronikol` on PATH
# (the tool built from this repository), a main with two files, and git with no global or system configuration: a fresh
# GitHub-hosted runner has no committer identity, and actions/checkout does not set one.
set -uo pipefail
H=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
: "${KRONIKOL_TOOL_DIR:?the directory holding the built Kronikol.Tool.dll}"
: "${DOTNET_ROOT:?the .NET 10 root}"
export GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_GLOBAL=/dev/null GIT_TERMINAL_PROMPT=0
unset KRONIKOL_HISTORY KRONIKOL_KEEP_RUNS

new_world() {
  W=$1
  rm -rf "$W"
  mkdir -p "$W/bin"
  printf '#!/usr/bin/env bash\nexec "%s/dotnet" "%s/Kronikol.Tool.dll" "$@"\n' "$DOTNET_ROOT" "$KRONIKOL_TOOL_DIR" > "$W/bin/kronikol"
  chmod +x "$W/bin/kronikol"
  git init -q --bare -b main "$W/origin.git"
  git init -q -b main "$W/seed"
  mkdir -p "$W/seed/src"
  echo "# app" > "$W/seed/README.md"; echo "class App {}" > "$W/seed/src/App.cs"
  git -C "$W/seed" add -A
  git -C "$W/seed" -c user.name=seed -c user.email=seed@example.com commit -q -m init
  git -C "$W/seed" push -q "file://$W/origin.git" main
  export PATH="$W/bin:$PATH"
}

# What actions/checkout@v5 does by default: init, add the remote (which configures the default
# +refs/heads/*:refs/remotes/origin/* refspec), fetch the one commit at depth 1, check it out.
checkout() {
  local dir=$1
  git init -q "$dir"
  git -C "$dir" remote add origin "file://$W/origin.git"
  git -C "$dir" -c protocol.version=2 fetch -q --no-tags --prune --depth=1 origin +refs/heads/main:refs/remotes/origin/main
  git -C "$dir" checkout -q --force -B main refs/remotes/origin/main
}

# The runner's per-job environment.
job_env() {
  local root=$1 run=$2 attempt=${3:-1}
  export RUNNER_TEMP="$root/runner-temp" GITHUB_RUN_ID=$run GITHUB_RUN_ATTEMPT=$attempt GITHUB_SHA=$(git -C "$W/seed" rev-parse HEAD)
  mkdir -p "$RUNNER_TEMP"
  export GITHUB_ENV="$RUNNER_TEMP/env" GITHUB_OUTPUT="$RUNNER_TEMP/output" GITHUB_STEP_SUMMARY="$RUNNER_TEMP/summary"
  : > "$GITHUB_ENV"; : > "$GITHUB_OUTPUT"; : > "$GITHUB_STEP_SUMMARY"
}

branch_report() {
  local label=$1
  local tmp; tmp=$(mktemp -d)
  if ! git -C "$W/seed" fetch -q "file://$W/origin.git" kronikol-history 2>/dev/null; then
    echo "$label: no kronikol-history branch on origin"
    return
  fi
  git -C "$W/seed" show FETCH_HEAD:history.jsonl > "$tmp/history.jsonl"
  python3 - "$tmp/history.jsonl" "$label" <<'PY'
import json, sys, collections
path, label = sys.argv[1], sys.argv[2]
kinds = collections.Counter(); ids = collections.Counter(); rosters = collections.Counter()
for line in open(path, encoding='utf-8'):
    o = json.loads(line)
    kinds[o['t']] += 1
    if o['t'] == 'run': ids[o['id']] += 1
    if o['t'] == 'roster': rosters[o['hash']] += 1
print(f"{label}: lines {dict(kinds)}; runs by id {dict(sorted(ids.items()))}; duplicate roster lines {sum(v - 1 for v in rosters.values())}")
PY
  kronikol history verify --history "$tmp/history.jsonl" > "$tmp/verify.txt" 2>&1; echo "$label: history verify exit $? ($(tail -1 "$tmp/verify.txt"))"
  echo "$label: commits on the branch: $(git -C "$W/seed" rev-list --count FETCH_HEAD)"
  rm -rf "$tmp"
}
