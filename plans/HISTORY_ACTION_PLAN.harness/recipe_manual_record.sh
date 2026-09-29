basic=$(printf 'x-access-token:%s' "$TOKEN" | base64 | tr -d '\r\n')
echo "::add-mask::$basic"
export GIT_TERMINAL_PROMPT=0 GCM_INTERACTIVE=Never GIT_CONFIG_COUNT=5 \
  GIT_CONFIG_KEY_0=credential.helper GIT_CONFIG_VALUE_0= \
  GIT_CONFIG_KEY_1=core.autocrlf GIT_CONFIG_VALUE_1=false \
  GIT_CONFIG_KEY_2=core.hooksPath GIT_CONFIG_VALUE_2="$RUNNER_TEMP/kronikol-history-no-hooks" \
  GIT_CONFIG_KEY_3="http.$SERVER/.extraheader" GIT_CONFIG_VALUE_3= \
  GIT_CONFIG_KEY_4="http.$SERVER/.extraheader" GIT_CONFIG_VALUE_4="AUTHORIZATION: basic $basic"
if [ -z "$(find "$FRAGMENTS" -name History.run.json 2> /dev/null)" ]; then echo "No History.run.json to record"; exit 0; fi
repo="$RUNNER_TEMP/kronikol-history-record"
for attempt in 1 2 3 4 5 6 7 8; do
  status=0; tip=$(git ls-remote --exit-code "$REMOTE" refs/heads/kronikol-history) || status=$?
  if [ $status != 0 ] && [ $status != 2 ]; then echo "::error::could not read kronikol-history (git ls-remote exit $status)"; exit 1; fi
  rm -rf "$repo"; git init -q -b kronikol-history "$repo"
  git -C "$repo" config user.name "github-actions[bot]"
  git -C "$repo" config user.email "41898282+github-actions[bot]@users.noreply.github.com"
  if [ $status = 2 ]; then
    kronikol history init --history "$repo/history.jsonl"   # the first run: the header and merge=union
  else
    git -C "$repo" fetch -q --depth=1 "$REMOTE" refs/heads/kronikol-history
    git -C "$repo" reset -q --hard FETCH_HEAD
    tip=$(git -C "$repo" rev-parse HEAD)
  fi
  kronikol history record "$FRAGMENTS" --history "$repo/history.jsonl"
  git -C "$repo" add -A
  if git -C "$repo" diff --cached --quiet; then exit 0; fi   # every run here is on the branch already
  git -C "$repo" commit -q --no-verify -m "Record run $GITHUB_RUN_ID:$GITHUB_RUN_ATTEMPT (${GITHUB_SHA::7}) [skip ci]"
  if git -C "$repo" push -q --no-verify "$REMOTE" HEAD:refs/heads/kronikol-history; then exit 0; fi
  sleep "0.$((RANDOM % 9 + 1))"   # a competing push lands; a branch that has not moved means no race
  now=$(git ls-remote "$REMOTE" refs/heads/kronikol-history)
  if [ "${now%%[[:space:]]*}" = "${tip%%[[:space:]]*}" ]; then
    echo "::error::the push was refused and kronikol-history did not move, so this was not a lost race"; exit 1
  fi
  echo "Push $attempt lost the race to another run; recording again on the branch as it is now"
done
echo "::error::every push lost a race to another run; re-run this job, which is safe"; exit 1
