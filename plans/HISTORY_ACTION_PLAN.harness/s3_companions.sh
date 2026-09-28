#!/usr/bin/env bash
# Plan F2: quarantine.json and aliases.json are read beside the ledger (HistoryQuarantine.PathBeside,
# HistoryAliases.PathBeside). The recipe's read step copies history.jsonl alone into $RUNNER_TEMP, so a
# quarantine kept on the data branch never reaches the run or the gate. BreakfastProvider's copy of the
# step also fetches the two companions.
source "$(dirname "$0")/common.sh"
T=${1:-/tmp/kh-s3}
new_world "$T/w"
checkout "$W/seed-job/ws"; job_env "$W/seed-job" 100
python3 "$H/make_fragments.py" "$H/bp-history.jsonl" "$W/seed-job/ws/fragments" 100 --suites 1 > /dev/null
( cd "$W/seed-job/ws" && bash "$H/recipe_dogfood.sh" > /dev/null 2>&1 )
# A person quarantines a scenario on the data branch, the only place the companion can live beside the ledger.
git clone -q --branch kronikol-history "file://$W/origin.git" "$W/person"
SID=$(python3 -c "import json;[print(json.loads(l)['ids'][0]) or exit() for l in open('$W/person/history.jsonl') if json.loads(l)['t']=='roster']")
kronikol history quarantine "$SID" --reason "flaky since the DB upgrade" --by person --until 2026-12-31 --history "$W/person/history.jsonl"
git -C "$W/person" add quarantine.json && git -C "$W/person" -c user.name=p -c user.email=p@example.com commit -q -m "Quarantine $SID" && git -C "$W/person" push -q origin kronikol-history

checkout "$W/test-job/ws"; job_env "$W/test-job" 101
cd "$W/test-job/ws"
echo "== the read step as the wiki and ci-summary-preview.yml write it"
if git fetch --depth=1 origin kronikol-history 2>/dev/null; then
  git show FETCH_HEAD:history.jsonl > "$RUNNER_TEMP/history.jsonl"
fi
KRONIKOL_HISTORY="$RUNNER_TEMP/history.jsonl" kronikol history quarantine --list
echo "== the read step with the companions (BreakfastProvider's copy, and the action)"
mkdir -p "$RUNNER_TEMP/with"
for f in history.jsonl quarantine.json aliases.json; do
  git show "FETCH_HEAD:$f" > "$RUNNER_TEMP/with/$f" 2>/dev/null || rm -f "$RUNNER_TEMP/with/$f"
done
KRONIKOL_HISTORY="$RUNNER_TEMP/with/history.jsonl" kronikol history quarantine --list
