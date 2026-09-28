#!/usr/bin/env bash
# Plan §4.5, §4.7 and §6: the history-record script, tested before it is written. Three versions of it go
# through the same facts:
#
#   v0  the fold step this repository dogfoods (.github/workflows/ci-summary-preview.yml at e4c9e36), in a
#       checkout made the way actions/checkout makes one (depth 1, the token persisted for the host);
#   v1  the plan's Appendix A draft as first committed (bbbcdb1), in a checkout made the way the Azure
#       Pipelines agent makes one (depth 1, detached, nothing persisted), the token passed per command;
#   v2  kronikol-history-record.sh beside this file, the revision the plan now proposes, with no checkout,
#       given the token as Azure DevOps would (bearer) and as GitHub would (basic, x-access-token).
#
# The remote is githttp.py, git's own http-backend behind a check for the token, so a command that forgets
# the credential fails as it would on either forge; it logs every request's Authorization headers. The ledger
# is judged by `kronikol history verify` and by the run lines on the branch. Every script runs under bash
# 3.2.57 (what macOS agents ship) and under the local bash. Git runs with no global or system config.
#
# Usage: record_script.sh   (from the repository root; the .NET 10 SDK, python3, and gcc and make to build
#        bash 3.2.57 from GNU's tarball unless BASH32 names one)
set -uo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(git -C "$here" rev-parse --show-toplevel)"
work="$(mktemp -d)"
server=""
cleanup() { [ -n "$server" ] && kill "$server" 2>/dev/null; rm -rf "$work"; }
trap cleanup EXIT

# ── Tools: bash 3.2.57, the Kronikol tool, a real fragment ───────────────────────────────────────────────
if [ -z "${BASH32:-}" ]; then
  cache="${XDG_CACHE_HOME:-$HOME/.cache}/kronikol-harness"
  BASH32="$cache/bash-3.2.57/bash"
  if [ ! -x "$BASH32" ]; then
    mkdir -p "$cache"
    curl -sSL -o "$cache/bash-3.2.57.tar.gz" https://ftp.gnu.org/gnu/bash/bash-3.2.57.tar.gz
    # The tarball's hash as downloaded on 2026-09-27.
    echo "3fa9daf85ebf35068f090ce51283ddeeb3c75eb5bc70b1a4a7cb05868bfe06a4  $cache/bash-3.2.57.tar.gz" | sha256sum -c --quiet || exit 1
    tar -xzf "$cache/bash-3.2.57.tar.gz" -C "$cache"
    (cd "$cache/bash-3.2.57" && ./configure --without-bash-malloc CFLAGS="-O1 -std=gnu89 -w -fcommon" > /dev/null && make -j8 > /dev/null) || exit 1
  fi
fi
shells=("$BASH32" "$(command -v bash)")

dotnet build "$root/src/Kronikol.Tool" -c Release > /dev/null || exit 1
mkdir -p "$work/bin"
printf '#!/bin/sh\nexec dotnet "%s" "$@"\n' "$root/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll" > "$work/bin/kronikol"
chmod +x "$work/bin/kronikol"
export PATH="$work/bin:$PATH"

project="$root/examples/Example.Api/tests/Example.Api.Tests.CiPreview.AllPassing"
dotnet build "$project" -c Release > /dev/null || exit 1
rm -rf "$project/bin/Release/net10.0/Reports"
env -u GITHUB_ACTIONS -u KRONIKOL_HISTORY TF_BUILD=True BUILD_BUILDID=6000 BUILD_SOURCEBRANCH=refs/heads/main \
  SYSTEM_TEAMFOUNDATIONSERVERURI=https://dev.azure.com/probe/ SYSTEM_TEAMPROJECT=Probe \
  dotnet test "$project" -c Release --no-build > /dev/null 2>&1
base="$project/bin/Release/net10.0/Reports/History.run.json"
[ -f "$base" ] || { echo "no fragment from the test run"; exit 1; }
# One fragment per run, laid out as a download of one artifact per job leaves it.
for n in 01 02 03 04 05 06 07 08 09 10 11 12; do
  mkdir -p "$work/frag/r$n/history-0"
  python3 - "$base" "$work/frag/r$n/history-0/History.run.json" "ado:60$n:1" "2026-09-27T10:$n:00Z" <<'PY'
import json, sys
fragment = json.load(open(sys.argv[1]))
fragment["run"]["id"], fragment["run"]["at"] = sys.argv[3], sys.argv[4]
json.dump(fragment, open(sys.argv[2], "w"))
PY
done

# ── The three versions ───────────────────────────────────────────────────────────────────────────────────
git -C "$root" show e4c9e36:.github/workflows/ci-summary-preview.yml \
  | awk '/name: Fold the fragments into the kronikol-history branch/ { step = 1 }
         step && /run: \|/ { body = 1; next }
         body && /^      - name:/ { exit }
         body { sub(/^          /, ""); print }' \
  | sed 's|dotnet run --project src/Kronikol.Tool/Kronikol.Tool.csproj --framework net10.0 --configuration Release --|kronikol|' \
  > "$work/v0.sh"
git -C "$root" show bbbcdb1:plans/AZURE_DEVOPS_PARITY_PLAN.md \
  | awk '/^## Appendix A/ { a = 1 } a && /^```bash/ { b = 1; next } b && /^```/ { exit } b { print }' > "$work/v1.sh"
cp "$here/kronikol-history-record.sh" "$work/v2.sh"
for v in v0 v1 v2; do [ -s "$work/$v.sh" ] || { echo "could not extract $v"; exit 1; }; done

# ── Git, hermetic, against the local server ──────────────────────────────────────────────────────────────
export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_NOSYSTEM=1 GIT_TERMINAL_PROMPT=0
unset http_proxy https_proxy HTTP_PROXY HTTPS_PROXY ALL_PROXY all_proxy
who=(-c user.name=harness -c user.email=harness@noreply.invalid)
token=TOKEN-6f2a
basic="$(printf 'x-access-token:%s' "$token" | base64 | tr -d '\n')"
mkdir -p "$work/srv"
stale_global="$work/global-stale"
python3 "$here/githttp.py" "$work/srv" "$work/port" "$work/http.log" "$token" &
server=$!
for _ in $(seq 100); do [ -s "$work/port" ] && break; sleep 0.1; done
port="$(cat "$work/port")"
host="http://127.0.0.1:$port"
printf '[http "%s/"]\n\textraheader = AUTHORIZATION: bearer STALE\n' "$host" > "$stale_global"
git init -q "$work/seed"
echo app > "$work/seed/README.md"
git -C "$work/seed" add README.md
git -C "$work/seed" "${who[@]}" commit -q -m app

# A bare repository with main, and the hook the race and refusal facts arm.
new_origin() {
  git init -q --bare "$work/srv/$1.git"
  git -C "$work/srv/$1.git" symbolic-ref HEAD refs/heads/main
  git -C "$work/seed" push -q "$work/srv/$1.git" HEAD:refs/heads/main
  cat > "$work/srv/$1.git/hooks/pre-receive" <<'SH'
#!/bin/sh
if [ -f compete-once ]; then
  rm -f compete-once
  env -u GIT_QUARANTINE_PATH -u GIT_OBJECT_DIRECTORY -u GIT_ALTERNATE_OBJECT_DIRECTORIES \
    git update-ref refs/heads/kronikol-history "$(git rev-parse refs/competing/tip)"
  echo "another run recorded first" >&2
  exit 1
fi
[ -f refuse-always ] && { echo "refused" >&2; exit 1; }
exit 0
SH
  chmod +x "$work/srv/$1.git/hooks/pre-receive"
}

checkout_github() { # actions/checkout: depth 1, main checked out, the credential persisted for the host
  git init -q "$2"
  git -C "$2" remote add origin "$1"
  git -C "$2" config "http.$host/.extraheader" "AUTHORIZATION: basic $basic"
  git -C "$2" fetch -q --depth=1 origin +refs/heads/main:refs/remotes/origin/main
  git -C "$2" checkout -q -B main refs/remotes/origin/main
}
checkout_ado() { # the agent with persistCredentials off: depth 1, detached, the credential never written down
  git init -q "$2"
  git -C "$2" remote add origin "$1"
  git -C "$2" -c "http.extraheader=AUTHORIZATION: bearer $token" fetch -q --depth=1 origin +refs/heads/main:refs/remotes/origin/main
  git -C "$2" checkout -q --detach refs/remotes/origin/main
}

# run <version> <lane> <shell> <origin> <workspace> <run...>: one job's fold. The workspace (a checkout, for
# v0 and v1) is made when missing and kept when present, as a self-hosted agent keeps its sources folder;
# the job's temp folder is the same path every job and emptied first, as the agent's _temp is. When $arm
# names a file, it is created after the checkout and before the script, to arm a fault for the script alone.
run() {
  local version=$1 lane=$2 shell=$3 origin=$4 ws=$5
  shift 5
  local url="$host/$origin.git" tmp="$ws.tmp" frags="$ws.frags"
  rm -rf "$tmp" "$frags"
  mkdir -p "$tmp" "$frags"
  for r in "$@"; do cp -r "$work/frag/$r" "$frags/$r"; done
  case "$version:$lane" in
    v1:ado|v1:ado-global) [ -d "$ws/.git" ] || checkout_ado "$url" "$ws" ;;
    *:ado-persisted) [ -d "$ws/.git" ] || { checkout_ado "$url" "$ws" && git -C "$ws" config "http.$url.extraheader" "AUTHORIZATION: bearer STALE"; } ;;
    v0:*|v1:*) [ -d "$ws/.git" ] || checkout_github "$url" "$ws" ;;
  esac
  if [ -n "${arm:-}" ]; then touch "$arm"; fi
  case "$version:$lane" in
    v0:github)
      rm -rf "$ws/fragments"; cp -r "$frags" "$ws/fragments"
      (cd "$ws" && RUNNER_TEMP="$tmp" GITHUB_RUN_ID=$RANDOM GITHUB_RUN_ATTEMPT=1 GITHUB_SHA="$(git rev-parse HEAD)" "$shell" "$work/v0.sh") ;;
    v1:ado)
      (cd "$ws" && KRONIKOL_GIT_AUTH="AUTHORIZATION: bearer $token" KRONIKOL_TEMP="$tmp" KRONIKOL_FRAGMENTS="$frags" \
        KRONIKOL_RUN_LABEL="$*" "$shell" "$work/v1.sh") ;;
    v1:ado-persisted)
      (cd "$ws" && KRONIKOL_GIT_AUTH="AUTHORIZATION: bearer $token" KRONIKOL_TEMP="$tmp" KRONIKOL_FRAGMENTS="$frags" \
        KRONIKOL_RUN_LABEL="$*" "$shell" "$work/v1.sh") ;;
    v2:ado-persisted)
      (cd "$ws" && KRONIKOL_REMOTE="$url" KRONIKOL_GIT_AUTH="AUTHORIZATION: bearer $token" KRONIKOL_TEMP="$tmp" \
        KRONIKOL_FRAGMENTS="$frags" KRONIKOL_RUN_LABEL="$*" "$shell" "$work/v2.sh") ;;
    v1:github-empty)
      (cd "$ws" && KRONIKOL_GIT_AUTH="" KRONIKOL_TEMP="$tmp" KRONIKOL_FRAGMENTS="$frags" KRONIKOL_RUN_LABEL="$*" "$shell" "$work/v1.sh") ;;
    v1:github-unset)
      (cd "$ws" && env -u KRONIKOL_GIT_AUTH KRONIKOL_TEMP="$tmp" KRONIKOL_FRAGMENTS="$frags" KRONIKOL_RUN_LABEL="$*" "$shell" "$work/v1.sh") ;;
    v2:ado)
      (cd "$work" && KRONIKOL_REMOTE="$url" KRONIKOL_GIT_AUTH="AUTHORIZATION: bearer $token" KRONIKOL_TEMP="$tmp" \
        KRONIKOL_FRAGMENTS="$frags" KRONIKOL_RUN_LABEL="$*" "$shell" "$work/v2.sh") ;;
    v2:github)
      (cd "$work" && KRONIKOL_REMOTE="$url" KRONIKOL_GIT_AUTH="AUTHORIZATION: basic $basic" KRONIKOL_TEMP="$tmp" \
        KRONIKOL_FRAGMENTS="$frags" KRONIKOL_RUN_LABEL="$*" "$shell" "$work/v2.sh") ;;
    v1:ado-global)
      (cd "$ws" && GIT_CONFIG_GLOBAL="$stale_global" KRONIKOL_GIT_AUTH="AUTHORIZATION: bearer $token" KRONIKOL_TEMP="$tmp" \
        KRONIKOL_FRAGMENTS="$frags" KRONIKOL_RUN_LABEL="$*" "$shell" "$work/v1.sh") ;;
    v2:ado-global)
      (cd "$work" && GIT_CONFIG_GLOBAL="$stale_global" KRONIKOL_REMOTE="$url" KRONIKOL_GIT_AUTH="AUTHORIZATION: bearer $token" \
        KRONIKOL_TEMP="$tmp" KRONIKOL_FRAGMENTS="$frags" KRONIKOL_RUN_LABEL="$*" "$shell" "$work/v2.sh") ;;
  esac
}

# What the branch holds: its run ids (xN when a run is there N times), header lines, verify, commits.
state() {
  local bare="$work/srv/$1.git"
  if ! git -C "$bare" rev-parse -q --verify refs/heads/kronikol-history > /dev/null; then
    echo "no branch"
    return
  fi
  git -C "$bare" show kronikol-history:history.jsonl > "$work/check.jsonl"
  local runs verify
  runs="$(python3 - "$work/check.jsonl" <<'PY'
import collections, json, sys
lines = [json.loads(l) for l in open(sys.argv[1]) if l.strip()]
runs = collections.Counter(l["id"].split(":")[1] for l in lines if l.get("t") == "run")
headers = sum(1 for l in lines if l.get("t") == "header")
print(" ".join(f"{k}x{v}" if v > 1 else k for k, v in sorted(runs.items())) + f"; {headers} header" + ("s" if headers != 1 else ""))
PY
)"
  if kronikol history verify --history "$work/check.jsonl" > "$work/verify.txt" 2>&1; then verify="verify ok"
  else verify="VERIFY FAILS ($(grep -m1 -o 'line [0-9]*: .*' "$work/verify.txt"))"; fi
  echo "runs $runs; $verify; $(git -C "$bare" rev-list --count kronikol-history) commits"
}

# fact <id> <text> <version> <lane> <shell> <body>: a fresh origin, the body's jobs, one result line.
count=0
fact() {
  local id=$1 text=$2 version=$3 lane=$4 shell=$5 body=$6
  count=$((count + 1))
  local origin="o$count" ws="$work/ws$count" out="$work/out$count.txt" status
  new_origin "$origin"
  "$body" "$version" "$lane" "$shell" "$origin" "$ws" > "$out" 2>&1
  status=$?
  local sh
  sh="bash $("$shell" -c 'echo ${BASH_VERSION%%(*}')"
  printf '%-3s %-6s %-14s %-11s exit %-3s %s\n' "$id" "$version" "$lane" "$sh" "$status" "$(state "$origin")"
  grep -E "unbound variable|fatal:|CONFLICT|could not|lost the race|nothing new|nothing to record|already used|rejected|declined|error: [0-9]{3}|returned error|verify" "$out" \
    | grep -v "couldn't find remote ref" | grep -v "^hint:" | sed "s|$work|<work>|g; s|127.0.0.1:[0-9]*|<server>|g" \
    | sort | uniq -c | sed -E 's/^ +1 /      /; s/^ +([0-9]+) /      (x\1) /' | cut -c1-160 | head -5
}

# The bodies. Each returns the exit status of its last job.
first_run()   { run "$1" "$2" "$3" "$4" "$5" r01; }
second_run()  { run "$1" "$2" "$3" "$4" "$5" r01 && rm -rf "$5" && run "$1" "$2" "$3" "$4" "$5" r02; }
rerun_same()  { run "$1" "$2" "$3" "$4" "$5" r01 && rm -rf "$5" && run "$1" "$2" "$3" "$4" "$5" r01; }
# Another run lands between this job's fetch and its push; the hook plays it.
lost_race() {
  run "$1" "$2" "$3" "$4" "$5" r01 || return
  local c="$work/competitor-$4" bare="$work/srv/$4.git"
  git clone -q --branch kronikol-history "$bare" "$c"
  kronikol history record "$work/frag/r03" --history "$c/history.jsonl" > /dev/null
  git -C "$c" add -A && git -C "$c" "${who[@]}" commit -q -m "Record r03" && git -C "$c" push -q origin HEAD:refs/competing/tip
  touch "$bare/compete-once"
  rm -rf "$5"
  run "$1" "$2" "$3" "$4" "$5" r04
}
never_clears() { run "$1" "$2" "$3" "$4" "$5" r01 || return; touch "$work/srv/$4.git/refuse-always"; rm -rf "$5"; run "$1" "$2" "$3" "$4" "$5" r02; }
# The remote fails the job's first read, once (a 500), then works.
transient() { run "$1" "$2" "$3" "$4" "$5" r01 || return; rm -rf "$5"; arm="$work/srv/$4.git/fail-next-upload-pack" run "$1" "$2" "$3" "$4" "$5" r02; }
# The same, on a ledger an earlier release started: its header line names that release.
older_ledger_transient() {
  run "$1" "$2" "$3" "$4" "$5" r01 || return
  local c="$work/older-$4" bare="$work/srv/$4.git"
  git clone -q --branch kronikol-history "$bare" "$c"
  sed -i '1s/"generator":"[^"]*"/"generator":"3.20.0"/' "$c/history.jsonl"
  git -C "$c" "${who[@]}" commit -qam "a ledger an earlier release started" && git -C "$c" push -q origin HEAD:kronikol-history
  rm -rf "$5"
  arm="$bare/fail-next-upload-pack" run "$1" "$2" "$3" "$4" "$5" r02
}
# A self-hosted agent: the next job reuses the sources folder, and its own temp folder is new.
reused_agent() { run "$1" "$2" "$3" "$4" "$5" r01 || return; run "$1" "$2" "$3" "$4" "$5" r02; }
# Four runs of the pipeline fold at once, onto a branch that exists.
parallel() {
  run "$1" "$2" "$3" "$4" "$5" r01 || return
  local pids="" failed=0
  for r in r05 r06 r07 r08; do
    run "$1" "$2" "$3" "$4" "$5-$r" "$r" > "$work/par-$4-$r.txt" 2>&1 &
    pids="$pids $!"
  done
  for p in $pids; do wait "$p" || failed=$((failed + 1)); done
  cat "$work"/par-"$4"-*.txt
  echo "parallel jobs failed: $failed"
  return "$failed"
}

echo "# The history-record script: v0 dogfooded (e4c9e36), v1 the plan's draft (bbbcdb1), v2 the revision"
echo "# $(date -u +%Y-%m-%d), git $(git --version | cut -d' ' -f3), shells: $(for s in "${shells[@]}"; do "$s" -c 'printf "%s " "${BASH_VERSION%%(*}"'; done)"

echo
echo "## G. What git sends: Authorization headers per request, by where the credential is"
new_origin g
url="$host/g.git"
git init -q "$work/g"
git -C "$work/g" remote add origin "$url"
git -C "$work/g" config "http.$host/.extraheader" "AUTHORIZATION: bearer PERSISTED"
probe() { : > "$work/http.log"; "$@" > /dev/null 2>&1; local s=$?; echo "exit $s; sent: $(grep -o 'tokens=[^ ]*' "$work/http.log" | sort -u | tr '\n' ' ')"; }
echo "G1 persisted for the host, nothing passed:                       $(probe git -C "$work/g" ls-remote origin)"
echo "G2 persisted for the host, -c http.extraheader=<token>:          $(probe git -C "$work/g" -c "http.extraheader=AUTHORIZATION: bearer PASSED" ls-remote origin)"
echo "G3 persisted for the host, -c http.extraheader= (empty):         $(probe git -C "$work/g" -c "http.extraheader=" ls-remote origin)"
git -C "$work/g" config --unset "http.$host/.extraheader"
git -C "$work/g" config "http.$url.extraheader" "AUTHORIZATION: bearer PERSISTED"
echo "G3b persisted for the repository URL (the agent's key), -c http.extraheader=<token>: $(probe git -C "$work/g" -c "http.extraheader=AUTHORIZATION: bearer PASSED" ls-remote origin)"
git -C "$work/g" config --unset "http.$url.extraheader"
git -C "$work/g" config "http.$host/.extraheader" "AUTHORIZATION: bearer PERSISTED"
echo "G4 persisted for the host, -c http.<host>/.extraheader=<token>:  $(probe git -C "$work/g" -c "http.$host/.extraheader=AUTHORIZATION: bearer PASSED" ls-remote origin)"
git -C "$work/g" config --unset "http.$host/.extraheader"
echo "G5 nothing persisted, -c http.extraheader=<token>:               $(probe git -C "$work/g" -c "http.extraheader=AUTHORIZATION: bearer $token" ls-remote origin)"
echo "G6 ls-remote --exit-code, a branch that does not exist:         $(probe git -C "$work/g" -c "http.extraheader=AUTHORIZATION: bearer $token" ls-remote --exit-code origin refs/heads/kronikol-history)"
echo "G7 ls-remote --exit-code, the wrong token:                      $(probe git -C "$work/g" -c "http.extraheader=AUTHORIZATION: bearer WRONG" ls-remote --exit-code origin refs/heads/kronikol-history)"
userurl="http://kronikol@127.0.0.1:$port/g.git"
echo "G7b a URL carrying a user name, the header passed:              $(probe git -c "http.extraheader=AUTHORIZATION: bearer $token" ls-remote "$userurl")"
touch "$work/srv/g.git/fail-next-upload-pack"
echo "G8 ls-remote --exit-code, the server fails once (500):          $(probe git -C "$work/g" -c "http.extraheader=AUTHORIZATION: bearer $token" ls-remote --exit-code origin refs/heads/main)"
echo "G9 fetch, a branch that does not exist:                          $(probe git -C "$work/g" -c "http.extraheader=AUTHORIZATION: bearer $token" fetch origin kronikol-history)"
echo "G10 fetch, the wrong token:                                      $(probe git -C "$work/g" -c "http.extraheader=AUTHORIZATION: bearer WRONG" fetch origin kronikol-history)"
echo "G11 the machine's global configuration holds a stale header for the host (a self-hosted machine's can):"
echo "    -c http.extraheader=<token> (no URL):                         $(GIT_CONFIG_GLOBAL="$stale_global" probe git -c "http.extraheader=AUTHORIZATION: bearer $token" ls-remote "$url")"
echo "    -c http.<host>/.extraheader=<token>:                          $(GIT_CONFIG_GLOBAL="$stale_global" probe git -c "http.$host/.extraheader=AUTHORIZATION: bearer $token" ls-remote "$url")"
echo "    the same key set empty, then to <token>:                      $(GIT_CONFIG_GLOBAL="$stale_global" probe git -c "http.$host/.extraheader=" -c "http.$host/.extraheader=AUTHORIZATION: bearer $token" ls-remote "$url")"
echo "    the same two through GIT_CONFIG_COUNT (the environment):      $(GIT_CONFIG_GLOBAL="$stale_global" GIT_CONFIG_COUNT=2 GIT_CONFIG_KEY_0="http.$host/.extraheader" GIT_CONFIG_VALUE_0='' GIT_CONFIG_KEY_1="http.$host/.extraheader" GIT_CONFIG_VALUE_1="AUTHORIZATION: bearer $token" probe git ls-remote "$url")"
echo "    the remote's own URL as the key, set empty, then to <token>:  $(GIT_CONFIG_GLOBAL="$stale_global" probe git -c "http.$url.extraheader=" -c "http.$url.extraheader=AUTHORIZATION: bearer $token" ls-remote "$url")"

echo
echo "## S. The facts of plan §6 (S1 to S5, S8), and the ones it did not list, per version and shell"
echo "   (runs: the run numbers on the branch, x2 when a run is there twice)"
for spec in "S1 first run: creates the branch|first_run" "S2 second run: appends|second_run" \
            "S3 the same fragments again: records nothing|rerun_same" \
            "S4 another run lands between the fetch and the push|lost_race" \
            "S5 a refusal that never clears: gives up, says so|never_clears" \
            "S6 the remote fails the first read once|transient" \
            "S6b the same, on a ledger an earlier release started|older_ledger_transient" \
            "S7 a self-hosted agent: the next job reuses the sources folder|reused_agent" \
            "S8 four pipeline runs fold at once|parallel"; do
  id="${spec%% *}"; rest="${spec#* }"; text="${rest%%|*}"; body="${rest##*|}"
  echo
  echo "### $id $text"
  for shell in "${shells[@]}"; do
    fact "$id" "$text" v0 github "$shell" "$body"
    fact "$id" "$text" v1 ado "$shell" "$body"
    fact "$id" "$text" v2 ado "$shell" "$body"
    fact "$id" "$text" v2 github "$shell" "$body"
  done
done

echo
echo "### S9 the first draft run on GitHub, where actions/checkout persisted the token and the wrapper passes none"
for shell in "${shells[@]}"; do
  fact S9 "" v1 github-empty "$shell" first_run
  fact S9 "" v1 github-unset "$shell" first_run
done

echo
echo "### S11 the checkout persisted a credential the remote no longer accepts; the wrapper passes a good one"
for shell in "${shells[@]}"; do
  fact S11 "" v1 ado-persisted "$shell" first_run
  fact S11 "" v2 ado-persisted "$shell" first_run
done

echo
echo "### S12 the machine's global configuration persisted a stale header for the server; the wrapper passes a good one"
for shell in "${shells[@]}"; do
  fact S12 "" v1 ado-global "$shell" first_run
  fact S12 "" v2 ado-global "$shell" first_run
done

echo
echo "### S10 what an Azure DevOps macro or template expression could touch in each script"
cp "$here/kronikol-history-read.sh" "$work/read.sh"
for v in v0 v1 v2 read; do
  printf '%s: $(identifier) %s; ${{ %s\n' "$v" "$(grep -cE '\$\([A-Za-z_][A-Za-z0-9_.]*\)' "$work/$v.sh")" "$(grep -c '\${{' "$work/$v.sh")"
done

echo
echo "## R. The read step before the tests (kronikol-history-read.sh beside this file), per shell"
# read_job <label> <shell> <origin> <header> <working folder>: one test job's read, and what it left.
read_job() {
  local tmp="$work/read-$1"
  shift
  mkdir -p "$tmp"
  (cd "$4" && KRONIKOL_REMOTE="$host/$2.git" KRONIKOL_GIT_AUTH="$3" KRONIKOL_TEMP="$tmp" "$1" "$work/read.sh") > "$tmp.out" 2>&1
  local status=$? left="no file"
  if [ -f "$tmp/kronikol-history.jsonl" ]; then
    if git -C "$work/srv/$2.git" show kronikol-history:history.jsonl | cmp -s - "$tmp/kronikol-history.jsonl"; then
      left="the branch's ledger, byte for byte"
    else
      left="a file that is not the branch's ledger"
    fi
  fi
  echo "exit $status; $left; said: $(grep -v '^$' "$tmp.out" | tail -1 | sed "s|127.0.0.1:[0-9]*|<server>|g")"
}
for shell in "${shells[@]}"; do
  sh="bash $("$shell" -c 'echo ${BASH_VERSION%%(*}')"
  count=$((count + 1)); empty="r$count"; new_origin "$empty"
  count=$((count + 1)); held="r$count"; new_origin "$held"
  run v2 ado "$shell" "$held" "$work/ws-$held" r01 > /dev/null 2>&1
  echo "R1 $sh no branch yet:                                   $(read_job "R1-$count" "$shell" "$empty" "AUTHORIZATION: bearer $token" "$work")"
  echo "R2 $sh a ledger on the branch:                          $(read_job "R2-$count" "$shell" "$held" "AUTHORIZATION: bearer $token" "$work")"
  touch "$work/srv/$held.git/fail-next-upload-pack"
  echo "R3 $sh the remote fails the read once (500):            $(read_job "R3-$count" "$shell" "$held" "AUTHORIZATION: bearer $token" "$work")"
  echo "R4 $sh the wrong token:                                 $(read_job "R4-$count" "$shell" "$held" "AUTHORIZATION: bearer WRONG" "$work")"
  checkout_ado "$host/$held.git" "$work/stale-$held"
  git -C "$work/stale-$held" config "http.$host/$held.git.extraheader" "AUTHORIZATION: bearer STALE"
  echo "R5 $sh run inside a checkout holding a stale credential: $(read_job "R5-$count" "$shell" "$held" "AUTHORIZATION: bearer $token" "$work/stale-$held")"
  echo "R6 $sh a stale header in the global configuration:         $(GIT_CONFIG_GLOBAL="$stale_global" read_job "R6-$count" "$shell" "$held" "AUTHORIZATION: bearer $token" "$work")"
done

echo
echo "## W. The wiki's printed fold step (Cross-Run-History, \"The recommended shape\", wiki 86a77c1), copied as"
echo "##    printed into a checkout made the way actions/checkout makes one, run with GitHub's default bash -e"
cat > "$work/wiki.sh" <<'WIKI'
git worktree add "$RUNNER_TEMP/history-branch" origin/kronikol-history   # or --orphan on the first run
kronikol history record fragments --history "$RUNNER_TEMP/history-branch/history.jsonl"
cd "$RUNNER_TEMP/history-branch" && git add history.jsonl && git commit -m "Record run $GITHUB_RUN_ID" \
  && (git push origin HEAD:kronikol-history || (git fetch origin kronikol-history && git rebase origin/kronikol-history && git push origin HEAD:kronikol-history))
WIKI
# wiki_job <label> <shell> <origin> <line before the snippet, or empty>
wiki_job() {
  local ws="$work/wiki-$1" tmp="$work/wiki-$1.tmp"
  checkout_github "$host/$3.git" "$ws"
  mkdir -p "$tmp" && cp -r "$work/frag/r02" "$ws/fragments"
  { [ -n "$4" ] && echo "$4"; cat "$work/wiki.sh"; } > "$ws.sh"
  (cd "$ws" && RUNNER_TEMP="$tmp" GITHUB_RUN_ID=77 "$2" -e "$ws.sh") > "$ws.out" 2>&1
  echo "exit $?; $(state "$3"); said: $(grep -m1 -E 'fatal|error|identity|Please tell' "$ws.out" | cut -c1-110)"
}
for shell in "${shells[@]}"; do
  sh="bash $("$shell" -c 'echo ${BASH_VERSION%%(*}')"
  count=$((count + 1)); origin="w$count"; new_origin "$origin"
  run v2 github "$shell" "$origin" "$work/ws-$origin" r01 > /dev/null 2>&1
  echo "W1 $sh as printed, onto a branch that exists:            $(wiki_job "1-$count" "$shell" "$origin" "")"
  echo "W2 $sh with the dogfood's fetch line put before it:        $(wiki_job "2-$count" "$shell" "$origin" "git fetch origin kronikol-history")"
done
