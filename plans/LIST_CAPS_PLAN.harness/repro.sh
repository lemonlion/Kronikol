#!/usr/bin/env bash
# Reproduces #161 on a built or published Kronikol.Tool, on a copy of the real Playwright 1.62.1 capture the repo
# already holds (tests/Kronikol.Tests/TestData/Cucumber/playwright-bdd-9.2-attachments, from ATTACHMENT_MERGE_PLAN).
#
# Usage: KRONIKOL=<kronikol executable> REPO=<a Kronikol checkout> OUT=<an empty scratch folder> bash repro.sh
#
# Three lanes, each ingested into its own report:
#   recipe   --tests alone: the wiki's Playwright reporter recipe writes no `step` index, so every artefact is the
#            scenario's own (Integration-Playwright.md, "The recipe above writes no step index").
#   messages --tests plus --cucumber-messages: playwright-bdd's messages put step screenshots on their steps.
#   hooks    as messages, plus --include-hooks: Playwright's own artefacts sit on the after-hook step.
# Nothing is written outside OUT. The fixture is copied, never used in place.
set -euo pipefail
: "${KRONIKOL:?set KRONIKOL to the kronikol executable}"
: "${REPO:?set REPO to a Kronikol checkout}"
: "${OUT:?set OUT to a scratch folder}"

fixture="$REPO/tests/Kronikol.Tests/TestData/Cucumber/playwright-bdd-9.2-attachments"
rm -rf "$OUT/in"
mkdir -p "$OUT/in/empty"
cp -r "$fixture/." "$OUT/in/"
: > "$OUT/in/empty/captures.ndjson"
"$KRONIKOL" --version > "$OUT/version.txt"

run_lane() {
  local lane="$1"; shift
  local report="$OUT/$lane/report"
  rm -rf "$OUT/$lane"
  mkdir -p "$OUT/$lane"
  (cd "$OUT/in" && "$KRONIKOL" ingest empty/captures.ndjson --tests tests.ndjson -o "$report" \
      --allow-empty --no-component-diagram "$@" > "$OUT/$lane/ingest.txt" 2>&1)
  "$KRONIKOL" query failures "$report" > "$OUT/$lane/failures.txt" 2>&1 || true
  "$KRONIKOL" query failures "$report" --json > "$OUT/$lane/failures.json" 2>&1 || true
  "$KRONIKOL" query failures "$report" --json --out "$OUT/$lane/failures.out.json" > /dev/null 2>&1 || true
  for s in s0 s4; do
    "$KRONIKOL" query steps "$report" "$s" > "$OUT/$lane/steps-$s.txt" 2>&1 || true
    # The full view, from 4.28.0 (R1); an older tool answers that it has no such verb.
    "$KRONIKOL" query attachments "$report" "$s" --max-bytes 0 > "$OUT/$lane/attachments-$s.txt" 2>&1 || true
  done
  cp "$report/Failures.md" "$OUT/$lane/Failures.md"
  cp "$report/Failures.jsonl" "$OUT/$lane/Failures.jsonl"
}

run_lane recipe
run_lane messages --cucumber-messages messages.ndjson
run_lane hooks --cucumber-messages messages.ndjson --include-hooks
echo "done: $(cat "$OUT/version.txt")"
