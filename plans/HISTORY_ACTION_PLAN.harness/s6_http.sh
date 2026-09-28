#!/usr/bin/env bash
# Plan §4.3, §4.5, F9 over smart HTTP: the origin is `git http-backend` behind basic authentication
# (githttp.py), which takes a token the way github.com takes GITHUB_TOKEN. Everything before this ran
# against file:// origins, which have no authentication and no HTTP status codes.
#   H1  the token, prompts and the machine's credential helpers
#   H2  what ls-remote --exit-code answers over HTTP
#   H3  six writers racing, on a first run and on an existing branch, with the token (TRIALS races each)
#   H4  refusals: a read-only token (403, a fork's pull request) and a pre-receive hook
#   H5  whether the token or its header reached any output or any file
#   H6  the read: a depth-1 fetch against a blobless one, when the tip holds more than the ledger
#   H7  F9: the dogfood's worktree push from checkouts made as actions/checkout v5, v6.0.0 and v6.0.1 make them
source "$(dirname "$0")/common.sh"
T=${1:?root directory}
N=${N:-6} TRIALS=${TRIALS:-3}
rm -rf "$T"; mkdir -p "$T"
TOKEN=harness-token-7f3a9c ROTOKEN=harness-read-only-token-51d2 OTHER=someone:machine-password
PORT=$(python3 -c 'import socket; s = socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1])')
HTTP=http://127.0.0.1:$PORT
GITHTTP_TOKEN=$TOKEN GITHTTP_READ_TOKEN=$ROTOKEN GITHTTP_USER=$OTHER python3 "$H/githttp.py" "$T" "$PORT" "$T/server.log" &
SERVER=$!
trap 'kill $SERVER 2>/dev/null' EXIT
for _ in $(seq 50); do curl -s -o /dev/null "$HTTP/" && break; sleep 0.1; done
basic() { printf 'x-access-token:%s' "$1" | base64 | tr -d '\n'; }
header() { echo "http.$HTTP/.extraheader=AUTHORIZATION: basic $(basic "$1")"; }
served() { tail -n +$(( $1 + 1 )) "$T/server.log" | awk '{print $3, $5, $6}' | sort | uniq -c | tr -s ' ' | tr '\n' ';'; }
mark() { wc -l < "$T/server.log" 2>/dev/null || echo 0; }
world() { # a world under the server's root; URL is its origin over HTTP
  new_world "$T/$1"
  git -C "$W/origin.git" config uploadpack.allowFilter true
  git -C "$W/origin.git" config uploadpack.allowAnySHA1InWant true
  URL=$HTTP/$1/origin.git
}
timed() { local s e; s=$(date +%s.%N); "$@" > "$T/timed.out" 2>&1; local x=$?; e=$(date +%s.%N); printf 'exit %s in %.1f s: %s' "$x" "$(echo "$e - $s" | bc)" "$(grep -m1 -E 'fatal|error' "$T/timed.out" || true)"; }

echo "== H1: the token, prompts and the machine's credential helpers"
world h1
m=$(mark); echo "a. no token:                          $(timed git ls-remote --exit-code "$URL" refs/heads/main)  [$(served "$m")]"
m=$(mark); echo "b. no token, prompts allowed, no tty: $(timed env -u GIT_TERMINAL_PROMPT setsid -w git ls-remote --exit-code "$URL" refs/heads/main)"
m=$(mark); echo "c. the token as checkout's header:   $(timed git -c "$(header "$TOKEN")" ls-remote --exit-code "$URL" refs/heads/main)  [$(served "$m")]"
m=$(mark); echo "d. a wrong token:                     $(timed git -c "$(header wrong-token)" ls-remote --exit-code "$URL" refs/heads/main)  [$(served "$m")]"
# A machine whose git configuration names a credential helper: a self-hosted runner, or the Windows image,
# whose Git for Windows installs the credential manager as the system helper.
store() { printf 'http://%s@127.0.0.1:%s\n' "$OTHER" "$PORT" > "$T/machine-credentials"; }
left() { echo "; the machine's store afterwards: $(grep -c . "$T/machine-credentials") credential(s)"; }
printf '[credential]\n\thelper = store --file %s\n' "$T/machine-credentials" > "$T/machine-store.gitconfig"
printf '[credential]\n\thelper = "!f() { sleep 8; }; f"\n' > "$T/machine-slow.gitconfig"
store; m=$(mark); echo "e. a wrong token, the machine stores another identity:               $(GIT_CONFIG_GLOBAL=$T/machine-store.gitconfig timed git -c "$(header wrong-token)" ls-remote --exit-code "$URL" refs/heads/main)  [$(served "$m")]$(left)"
store; m=$(mark); echo "f. the same, with credential.helper reset as the prototype does:      $(GIT_CONFIG_GLOBAL=$T/machine-store.gitconfig timed git -c credential.helper= -c "$(header wrong-token)" ls-remote --exit-code "$URL" refs/heads/main)  [$(served "$m")]$(left)"
m=$(mark); echo "g. a wrong token, the machine's helper takes 8 s (a credential window): $(GIT_CONFIG_GLOBAL=$T/machine-slow.gitconfig timed git -c "$(header wrong-token)" ls-remote --exit-code "$URL" refs/heads/main)"
m=$(mark); echo "h. the same, with credential.helper reset:                            $(GIT_CONFIG_GLOBAL=$T/machine-slow.gitconfig timed git -c credential.helper= -c "$(header wrong-token)" ls-remote --exit-code "$URL" refs/heads/main)"

echo; echo "== H2: ls-remote --exit-code over HTTP"
world h2
H2() { git -c "$(header "$1")" ls-remote --exit-code "$2" "$3" > /dev/null 2>&1; echo $?; }
echo "branch present: $(H2 "$TOKEN" "$URL" refs/heads/main); branch absent: $(H2 "$TOKEN" "$URL" refs/heads/kronikol-history); wrong token (401): $(H2 wrong "$URL" refs/heads/main); no such repository (404): $(H2 "$TOKEN" "$HTTP/h2/nothing.git" refs/heads/main); read-only token: $(H2 "$ROTOKEN" "$URL" refs/heads/main); server down: $(H2 "$TOKEN" http://127.0.0.1:9/h2/origin.git refs/heads/main)"

job() { # $1 name, $2 run, $3 token
  checkout "$W/$1/ws"
  python3 "$H/make_fragments.py" "$H/bp-history.jsonl" "$W/$1/ws/fragments" "$2" --suites 3 > /dev/null
  ( job_env "$W/$1" "$2"; cd "$W/$1/ws"; KRONIKOL_TOKEN=$3 bash "$H/prototype_record.sh" fragments "$URL" ) > "$W/$1/out.txt" 2>&1
  echo $? > "$W/$1/exit"
}
count() { # the runs the branch holds against the runs that were folded, and duplicate roster lines
  git -C "$W/seed" fetch -q "file://$W/origin.git" kronikol-history 2> /dev/null || { echo "no branch"; return; }
  git -C "$W/seed" show FETCH_HEAD:history.jsonl | python3 -c '
import json, sys, collections
runs, rosters = set(), collections.Counter()
for line in sys.stdin:
    o = json.loads(line)
    if o["t"] == "run": runs.add(o["id"])
    if o["t"] == "roster": rosters[o["hash"]] += 1
print(len(runs), sum(v - 1 for v in rosters.values()))'
}

echo; echo "== H3: $N writers racing over HTTP with the token, $TRIALS races on a first run and $TRIALS on an existing branch"
for kind in first existing; do
  lost=0 dup=0 failed=0 retries=0
  for t in $(seq 1 "$TRIALS"); do
    world "h3-$kind-$t"
    if [ $kind = existing ]; then job seed 500 "$TOKEN"; fi
    pids=(); for i in $(seq 1 "$N"); do job "j$i" $((600 + i)) "$TOKEN" & pids+=($!); done; wait "${pids[@]}"  # not a bare wait: the server is a child too
    read -r have d <<< "$(count)"
    if [ $kind = existing ]; then expect=$(( N + 1 )); else expect=$N; fi
    lost=$(( lost + expect - have )); dup=$(( dup + d ))
    for i in $(seq 1 "$N"); do [ "$(cat "$W/j$i/exit")" = 0 ] || failed=$((failed + 1)); retries=$(( retries + $(grep -c 'lost the race' "$W/j$i/out.txt") )); done
  done
  echo "$kind run: runs lost $lost of $(( N * TRIALS )); duplicate roster lines $dup; jobs failed $failed; lost races recorded again $retries"
done
echo "push refusals the server logged in H3: $(grep -c ' 403 \| 401 ' <(grep 'receive-pack' "$T/server.log") || true) (401 or 403 on receive-pack)"

echo; echo "== H4: refusals over HTTP"
world h4
job seed 700 "$TOKEN" > /dev/null
job ro 701 "$ROTOKEN"
echo "a. a read-only token (a fork's pull request): exit $(cat "$W/ro/exit"), pushes tried $(grep -c 'lost the race' "$W/ro/out.txt" | awk '{print $1 + 1}'): $(grep -m1 '::error::' "$W/ro/out.txt")"
mkdir -p "$W/origin.git/hooks"
printf '#!/bin/sh\nwhile read old new ref; do case "$ref" in refs/heads/kronikol-history) echo "refusing $ref: protected by a ruleset" >&2; exit 1;; esac; done\n' > "$W/origin.git/hooks/pre-receive"
chmod +x "$W/origin.git/hooks/pre-receive"
job hook 702 "$TOKEN"
echo "b. a pre-receive hook refusing the branch:    exit $(cat "$W/hook/exit"), pushes tried $(grep -c 'lost the race' "$W/hook/out.txt" | awk '{print $1 + 1}'): $(grep -m1 '::error::' "$W/hook/out.txt")"
echo "branch after H4: $(count | awk '{print $1 " run(s)"}')"

echo; echo "== H5: the token and its header in outputs and files"
B=$(basic "$TOKEN"); RB=$(basic "$ROTOKEN")
jobs=$(find "$T"/h3-* "$T"/h4 -name out.txt | wc -l)
lines=$(cat $(find "$T"/h3-* "$T"/h4 -name out.txt) | grep -F -e "$TOKEN" -e "$B" -e "$ROTOKEN" -e "$RB" | grep -vc '^::add-mask::' || true)
files=$(grep -rl -F -e "$TOKEN" -e "$B" -e "$ROTOKEN" -e "$RB" $(find "$T"/h3-* "$T"/h4 -type d -name runner-temp) 2>/dev/null | wc -l)
echo "$jobs jobs: lines of their output holding the token or its header, other than the ::add-mask:: line: $lines; files under their RUNNER_TEMP holding either (step env, outputs, summary, the action's repositories and their .git/config): $files"

echo; echo "== H6: the read, when the tip holds more than the ledger (a 25 MB file beside it, as 9.4's views would be)"
world h6
job seed 800 "$TOKEN" > /dev/null
R=$T/h6-person; git clone -q "file://$W/origin.git" -b kronikol-history "$R"
python3 "$H/grow_ledger.py" "$H/bp-history.jsonl" "$R/views.jsonl" 25000000 > /dev/null
git -C "$R" add -A; git -C "$R" -c user.name=p -c user.email=p@e commit -q -m views; git -C "$R" push -q origin kronikol-history
for mode in depth-1 blobless; do
  D=$T/h6-$mode; git init -q "$D"; git -C "$D" remote add origin "$URL"; m=$(mark)
  if [ $mode = depth-1 ]; then
    git -C "$D" -c "$(header "$TOKEN")" fetch -q --depth=1 origin kronikol-history
  else
    git -C "$D" -c "$(header "$TOKEN")" fetch -q --depth=1 --filter=blob:none origin kronikol-history
  fi
  for f in history.jsonl quarantine.json aliases.json; do git -C "$D" -c "$(header "$TOKEN")" show "FETCH_HEAD:$f" > "$D/$f.out" 2> /dev/null || rm -f "$D/$f.out"; done
  echo "$mode: $(tail -n +$(( m + 1 )) "$T/server.log" | awk '{n++; b += $4} END {print n " requests, " b " bytes served"}'); history.jsonl read: $(wc -c < "$D/history.jsonl.out") bytes"
done

echo; echo "== H7 (F9): the dogfood's fold, whose push runs in a worktree of the job's checkout, made as each checkout makes it"
# v5 writes the header into the checkout's .git/config. v6.0.0 writes it into $RUNNER_TEMP/git-credentials-<uuid>.config
# and includes that file with includeIf.gitdir:<workspace>/.git; v6.0.1 adds includeIf.gitdir:<workspace>/.git/worktrees/*.
checkout_as() { # $1 dir, $2 layout
  local dir=$1 layout=$2 file
  git init -q "$dir"; git -C "$dir" remote add origin "$URL"
  local gitdir; gitdir=$(cd "$dir/.git" && pwd)
  case $layout in
    v5) git -C "$dir" config --local "http.$HTTP/.extraheader" "AUTHORIZATION: basic $(basic "$TOKEN")" ;;
    v6.0.0|v6.0.1)
      file="$RUNNER_TEMP/git-credentials-$(python3 -c 'import uuid; print(uuid.uuid4())').config"
      git config --file "$file" "http.$HTTP/.extraheader" "AUTHORIZATION: basic $(basic "$TOKEN")"
      git -C "$dir" config --local "includeIf.gitdir:$gitdir.path" "$file"
      if [ "$layout" = v6.0.1 ]; then git -C "$dir" config --local "includeIf.gitdir:$gitdir/worktrees/*.path" "$file"; fi ;;
  esac
  git -C "$dir" -c protocol.version=2 fetch -q --no-tags --prune --depth=1 origin +refs/heads/main:refs/remotes/origin/main
  git -C "$dir" checkout -q --force -B main refs/remotes/origin/main
}
fold_as() { # $1 layout, $2 run
  local J=$W/$1-$2; mkdir -p "$J"
  ( job_env "$J" "$2"; checkout_as "$J/ws" "$1"; cd "$J/ws"
    python3 "$H/make_fragments.py" "$H/bp-history.jsonl" fragments "$2" --suites 3 > /dev/null
    bash "$H/recipe_dogfood.sh" ) > "$J/out.txt" 2>&1
  local x=$?
  echo "$1, run $2 ($3): exit $x; pushes tried $(( $(grep -c 'lost the race' "$J/out.txt") + 1 ))$( [ $x = 0 ] || grep -E 'fatal:|error:' "$J/out.txt" | tail -1 | sed 's/^/; last error: /')"
}
world h7
for layout in v5 v6.0.0 v6.0.1; do
  world "h7-$layout"
  fold_as "$layout" 901 "first run"
  git ls-remote --exit-code "file://$W/origin.git" refs/heads/kronikol-history > /dev/null || job seed 900 "$TOKEN" > /dev/null
  fold_as "$layout" 902 "the branch exists"
  echo "   branch: $(count | awk '{print $1 " run(s)"}')"
done
