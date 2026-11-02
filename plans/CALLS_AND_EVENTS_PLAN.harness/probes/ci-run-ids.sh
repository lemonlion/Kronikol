#!/bin/bash
# args: <name> <env flavour: gh|ado|gitlab|local> <last verdict pass|fail> <prev verdict pass|fail> [attempt-of-last]
SW=${SW:?a scratch folder}
K=${K:?the published kronikol}
name=$1; flavour=$2; last=$3; prev=$4; attempt=${5:-1}
root=$SW/q2/$name; rm -rf $root; mkdir -p $root
ledger=$root/history.jsonl
"$K" history init --history "$ledger" >/dev/null 2>&1
envs() { # run number, attempt
  case $flavour in
    gh) echo "GITHUB_ACTIONS=true CI=true GITHUB_RUN_ID=$((9000+$1)) GITHUB_RUN_ATTEMPT=$2 GITHUB_RUN_NUMBER=$1 GITHUB_REF_NAME=main GITHUB_REF=refs/heads/main GITHUB_SHA=abc$1 GITHUB_REPOSITORY=o/r GITHUB_SERVER_URL=https://github.com GITHUB_EVENT_NAME=push" ;;
    ado) echo "TF_BUILD=True BUILD_BUILDID=$((9000+$1)) BUILD_BUILDNUMBER=20261102.$1 BUILD_SOURCEBRANCH=refs/heads/main BUILD_SOURCEVERSION=abc$1 BUILD_REPOSITORY_NAME=r SYSTEM_TEAMFOUNDATIONSERVERURI=https://dev.azure.com/o/ SYSTEM_TEAMPROJECT=p" ;;
    gitlab) echo "GITLAB_CI=true CI=true" ;;
    local) echo "" ;;
  esac
}
custom() { [ "$flavour" = gitlab ] && echo "gitlab:$((9000+$1)):1"; }
for i in 1 2 3 4 5 6 7; do
  cp "$ledger" "$root/read$i.jsonl"
  v=pass; [ $i = 6 ] && v=$prev; [ $i = 7 ] && v=$last
  a=1; [ $i = 7 ] && a=$attempt
  env $(envs $i $a) KRONIKOL_HISTORY="$root/read$i.jsonl" dotnet run --file "$(dirname "$0")/history-ci.cs" -- "$root/run$i" $v $(custom $i) >/dev/null 2>&1
  if [ $i -lt 7 ]; then env $(envs $i $a) "$K" history record "$root/run$i" --history "$ledger" 2>&1 | grep -E '^(recorded|duplicate|skipped)' | cut -c1-90 | sed "s/^/  /" ; fi
done
echo "-- ledger run ids: $(grep -o '"t":"run","id":"[^"]*"' "$ledger" | cut -d'"' -f8 | tr '\n' ' ')"
echo "-- shard's own line:"
grep -E '^history:|^  history:' "$root/run7/CLAUDE.md" | head -2 | cut -c1-120
grep -m1 '^\*\*History:\*\*' "$root/run7/Failures.md" | cut -c1-110
grep -m1 '^History:' "$root/run7/Failures.md" | cut -c1-170
python -I - "$root/run7/History.run.json" <<'PY'
import json,sys
d=json.load(open(sys.argv[1],encoding='utf8'))
r=d.get('run',d)
print('   fragment id:',r.get('id'),'| branch:',r.get('branch'),'| provider:',r.get('provider'))
PY
"$K" query summary "$root/run7" 2>/dev/null | grep -iE 'ci |provider|branch|commit' | head -3 | cut -c1-140
echo "-- order 1, CI's: merge BEFORE the run is recorded"
mkdir -p "$root/m1"; env $(envs 7 $a) "$K" merge "$root/run7" -o "$root/m1/x.html" --history "$ledger" 2>&1 | grep -E 'history:|rror' | cut -c1-140
grep -m1 '^History:' "$root/m1/Failures.md" | cut -c1-190
echo "-- then record"
env $(envs 7 $a) "$K" history record "$root/run7" --history "$ledger" 2>&1 | grep -E '^(recorded|duplicate|skipped)' | cut -c1-90 | sed "s/^/  /"
echo "-- order 2, Merging-Parallel-Reports.md's: merge AFTER the run is recorded"
mkdir -p "$root/m2"; before=$(wc -c < "$ledger"); env $(envs 7 $a) "$K" merge "$root/run7" -o "$root/m2/x.html" --history "$ledger" 2>&1 | grep -E 'history:|rror' | cut -c1-140
grep -m1 '^History:' "$root/m2/Failures.md" | cut -c1-190
echo "-- the merge's id, from the merged file's ci block"
"$K" query summary "$root/m2/x.krkl" 2>/dev/null | grep -iE 'attempt|run id|gh:|ado:' | head -3 | cut -c1-120
