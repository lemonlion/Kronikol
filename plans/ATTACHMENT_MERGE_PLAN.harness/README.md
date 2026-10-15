# ATTACHMENT_MERGE_PLAN harness

The probes behind every measured line of [`../ATTACHMENT_MERGE_PLAN.md`](../ATTACHMENT_MERGE_PLAN.md), with their
output. Written 2026-10-14 at 4.20.2 (`3240c983`) on Windows 11, .NET SDK 10.0.300, node 25.9.0.

## The probe: a real playwright-bdd run with the wiki's reporter

`probe/` is playwright-bdd 9.2.0 on @playwright/test 1.62.1, as published, in a browser, with Playwright's own
artefacts on (`screenshot: 'only-on-failure'`, `video` and `trace: 'retain-on-failure'`) and `retries: 1`.

| File | What it is |
|---|---|
| `probe/kronikol-reporter.js` | The reporter recipe from the wiki's `Integration-Playwright.md` ("Screenshots and artefacts"), cut to `start`, `attachment` and `end` records, plus `attempt` |
| `probe/steps/fixtures.js` | The wiki's start/end screenshot auto fixture (`screenshot-start.png`, `screenshot-end.png`) |
| `probe/steps/steps.js` | A `Before` hook attaching `kronikol-test-id`, the wiki's `AfterStep` per-step screenshot (`after-step.png` on every step), a step attaching `step-shot.png`, two `note` JSON bodies with one name, a `kronikol-bypass` step, and the `@big` step that attaches a 58 MB `trace` and a 7.5 MB `video` by path |
| `probe/features/attachments.feature` | Four scenarios (a failing UI scenario, a passing one, a flaky one that passes on its retry, a bypass) and the `@big` one |
| `probe/features/background.feature` | Added in the sweep (2026-10-15): one failing scenario under a `Background:` step, run with `PROBE_OUT=out-bg npx playwright test --grep @background`, and ingested with and without `--include-hooks` |

Run it (the config points `executablePath` at the Chromium headless shell 1228 already on this machine; delete
`launchOptions` and run `npx playwright install chromium-headless-shell` anywhere else):

```bash
cd probe && npm install && npx bddgen
PROBE_OUT=out npx playwright test --grep-invert @big    # out/messages.ndjson and out/tests.jsonl
PROBE_OUT=out-big npx playwright test --grep @big       # the issue's sizes, a 186 MB messages file
```

Playwright empties `test-results/` at the start of every run, so the `@big` run deletes the files the first run's
`tests.jsonl` names: run the first one again before measuring it (this is also how a real user meets the
`gone` case below).

## Scripts

| File | What it is |
|---|---|
| `summarise_attachments.py <messages> <tests>` | Each side's view of every attachment: name, media type, encoding, size, and the step or hook the messages pin it to. `results/summary-probe.txt` is its output for the probe |
| `extract_attachments.py <reports dir>` | Every attachment a report lists and where (scenario or step), and the files in `attachments/`, read from `TestRunReport.json` without opening it |
| `variants.py` | From `out/tests.jsonl`, writes `tests-gone.jsonl` (every path in a folder that does not exist: CI kept only the two NDJSON files, or Playwright ran again), `tests-noattempt.jsonl` (no `attempt` on attachment records, as the wiki recipe writes them) and `tests-noname.jsonl` (no `name`, so the tests side falls back to the file's name) |
| `measure.sh <control dll> <prototype dll>` | The matrix: four rules against the four variants. `results/matrix.txt` is its output |

The four rules in the matrix: **control** is 4.20.2; **issue-1** is the issue's first suggestion (names compared with
the media type's extension on both sides, the scenario's own list only, the reporter's copy always kept), emulated in
the prototype by `KRONIKOL_175_ISSUE1=1`; **name-only** is the prototype without the byte check
(`KRONIKOL_175_VERIFY=0`); **prototype** is the plan's rule. `KRONIKOL_175_PASS2=0` turns off the bytes-only second
pass, which changed nothing anywhere (results, section 7).

## The prototype

`prototype.patch` (against `3240c983`, `src/` only) is the plan's rule as measured: the origins side table in
`CucumberFeatureSynthesizer`, `MergeAttachments` over the whole scenario tree in `CucumberFeatureMerger`, and the
pipeline's own staging folder deleted after the report is written. It carries the three `KRONIKOL_175_*` switches for
the matrix and the bytes-only second pass, none of which belong in the release (plan section 4). To use it:
`git apply plans/ATTACHMENT_MERGE_PLAN.harness/prototype.patch`, build `src/Kronikol.Tool -f net10.0`.

## Results

`results/matrix-4.22.1.txt`: the matrix re-run in the sweep on 4.22.1 (`85f849f7`), with a control and the prototype both built there.

`results/measurements-2026-10-14.txt`: the staging folder census, the matrix's key rows, the issue-scale timing,
memory and placement (with and without the byte check), `query failures` before and after, the test suite's own
staging folders, and the two defects the prototype's first cuts had; sections 9 to 12 were added by the sweep on 2026-10-15 (4.22.1, the
background and hook runs, the digest's attachment lists behind F9, playwright-bdd 8.5.1 and the protocol's fields).
