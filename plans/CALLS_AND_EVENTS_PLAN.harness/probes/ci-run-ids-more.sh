#!/bin/bash
# Three more shapes on Actions, each seven identical runs with the last newly failing, merged after the record.
SW=${SW:?a scratch folder}
K=${K:?the published kronikol}
gh() { echo "GITHUB_ACTIONS=true CI=true GITHUB_RUN_ID=$((9000+$1)) GITHUB_RUN_ATTEMPT=1 GITHUB_RUN_NUMBER=$1 GITHUB_REF_NAME=main GITHUB_REF=refs/heads/main GITHUB_SHA=abc$1 GITHUB_REPOSITORY=o/r GITHUB_SERVER_URL=https://github.com GITHUB_EVENT_NAME=push"; }
pr() { echo "GITHUB_ACTIONS=true CI=true GITHUB_RUN_ID=$((9000+$1)) GITHUB_RUN_ATTEMPT=1 GITHUB_RUN_NUMBER=$1 GITHUB_REF_NAME=12/merge GITHUB_REF=refs/pull/12/merge GITHUB_BASE_REF=main GITHUB_HEAD_REF=feature GITHUB_SHA=abc$1 GITHUB_REPOSITORY=o/r GITHUB_SERVER_URL=https://github.com GITHUB_EVENT_NAME=pull_request"; }

build() { # name, custom-id-prefix
  root=$SW/q2/$1; rm -rf $root; mkdir -p $root; ledger=$root/history.jsonl
  "$K" history init --history "$ledger" >/dev/null 2>&1
  for i in 1 2 3 4 5 6; do
    cp "$ledger" "$root/read$i.jsonl"
    env $(gh $i) KRONIKOL_HISTORY="$root/read$i.jsonl" dotnet run --file "$(dirname "$0")/history-ci.cs" -- "$root/run$i" pass ${2:+$2$i:1} >/dev/null 2>&1
    env $(gh $i) "$K" history record "$root/run$i" --history "$ledger" >/dev/null 2>&1
  done
}

echo "##### Actions, HistoryRunId set by the consumer: the shard is custom:7:1, the merge mints its own"
build gh-custom custom:
root=$SW/q2/gh-custom; ledger=$root/history.jsonl
cp "$ledger" "$root/read7.jsonl"
env $(gh 7) KRONIKOL_HISTORY="$root/read7.jsonl" dotnet run --file "$(dirname "$0")/history-ci.cs" -- "$root/run7" fail custom:7:1 >/dev/null 2>&1
grep -m1 '^History:' "$root/run7/Failures.md" | cut -c1-150
mkdir -p "$root/m1" "$root/m2"
echo "-- before record"; env $(gh 7) "$K" merge "$root/run7" -o "$root/m1/x.html" --history "$ledger" 2>&1 | grep -E '^  history:' | cut -c1-120; grep -m1 '^History:' "$root/m1/Failures.md" | cut -c1-170
env $(gh 7) "$K" history record "$root/run7" --history "$ledger" 2>&1 | grep -E '^(recorded|duplicate)' | cut -c1-70
echo "-- after record"; env $(gh 7) "$K" merge "$root/run7" -o "$root/m2/x.html" --history "$ledger" 2>&1 | grep -E '^  history:' | cut -c1-120; grep -m1 '^History:' "$root/m2/Failures.md" | cut -c1-170

echo; echo "##### Actions shard, merge with no Actions variables at all (a developer who downloaded the artifact)"
build gh-noenv
root=$SW/q2/gh-noenv; ledger=$root/history.jsonl
cp "$ledger" "$root/read7.jsonl"
env $(gh 7) KRONIKOL_HISTORY="$root/read7.jsonl" dotnet run --file "$(dirname "$0")/history-ci.cs" -- "$root/run7" fail >/dev/null 2>&1
env $(gh 7) "$K" history record "$root/run7" --history "$ledger" 2>&1 | grep -E '^(recorded|duplicate)' | cut -c1-70
mkdir -p "$root/m"; env -u GITHUB_ACTIONS -u CI "$K" merge "$root/run7" -o "$root/m/x.html" --history "$ledger" 2>&1 | grep -E '^  history:' | cut -c1-120; grep -m1 '^History:' "$root/m/Failures.md" | cut -c1-170

echo; echo "##### Actions pull request: six runs on main, then the shard on 12/merge, with GITHUB_BASE_REF set"
build gh-pr
root=$SW/q2/gh-pr; ledger=$root/history.jsonl
cp "$ledger" "$root/read7.jsonl"
env $(pr 7) KRONIKOL_HISTORY="$root/read7.jsonl" dotnet run --file "$(dirname "$0")/history-ci.cs" -- "$root/run7" fail >/dev/null 2>&1
grep -m1 '^\*\*History:\*\*' "$root/run7/Failures.md" | cut -c1-90
mkdir -p "$root/m1" "$root/m2" "$root/m3"
echo "-- before record, PR env"; env $(pr 7) "$K" merge "$root/run7" -o "$root/m1/x.html" --history "$ledger" 2>&1 | grep -E '^  history:' | cut -c1-120
env $(pr 7) "$K" history record "$root/run7" --history "$ledger" 2>&1 | grep -E '^(recorded|duplicate)' | cut -c1-70
echo "-- after record, PR env"; env $(pr 7) "$K" merge "$root/run7" -o "$root/m2/x.html" --history "$ledger" 2>&1 | grep -E '^  history:' | cut -c1-120
echo "-- after record, no env"; env -u GITHUB_ACTIONS -u CI "$K" merge "$root/run7" -o "$root/m3/x.html" --history "$ledger" 2>&1 | grep -E '^  history:|^history:' | cut -c1-120
grep -m1 '^\*\*History:\*\*' "$root/m3/Failures.md" | cut -c1-120
