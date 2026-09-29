basic=$(printf 'x-access-token:%s' "$TOKEN" | base64 | tr -d '\r\n')
echo "::add-mask::$basic"
export GIT_TERMINAL_PROMPT=0 GCM_INTERACTIVE=Never GIT_CONFIG_COUNT=3 \
  GIT_CONFIG_KEY_0=credential.helper GIT_CONFIG_VALUE_0= \
  GIT_CONFIG_KEY_1="http.$SERVER/.extraheader" GIT_CONFIG_VALUE_1= \
  GIT_CONFIG_KEY_2="http.$SERVER/.extraheader" GIT_CONFIG_VALUE_2="AUTHORIZATION: basic $basic"
repo="$RUNNER_TEMP/kronikol-history-read" out="$RUNNER_TEMP/kronikol-history"
rm -rf "$repo" "$out"; git init -q "$repo"; git -C "$repo" remote add origin "$REMOTE"
status=0; git -C "$repo" ls-remote --exit-code origin refs/heads/kronikol-history > /dev/null || status=$?
case $status in
  0) ;;
  2) echo "No kronikol-history branch yet: the first record starts it"; exit 0 ;;
  *) echo "::warning::could not read kronikol-history (git ls-remote exit $status): the tests run without history"; exit 0 ;;
esac
git -C "$repo" fetch -q --depth=1 --filter=blob:none origin refs/heads/kronikol-history
mkdir -p "$out"
for f in history.jsonl quarantine.json aliases.json; do
  if [ -n "$(git -C "$repo" ls-tree --name-only FETCH_HEAD -- "$f")" ]; then git -C "$repo" cat-file blob "FETCH_HEAD:$f" > "$out/$f"; fi
done
if [ -f "$out/history.jsonl" ]; then echo "KRONIKOL_HISTORY=$out/history.jsonl" >> "$GITHUB_ENV"; fi
