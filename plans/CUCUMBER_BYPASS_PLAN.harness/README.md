# CUCUMBER_BYPASS_PLAN harness

The probes behind every RUN line of [`../CUCUMBER_BYPASS_PLAN.md`](../CUCUMBER_BYPASS_PLAN.md), with their output.
Written 2026-10-06 at 4.6.0 (`37e93813`).

## The issue's files and the ingest probe

| File | What it is |
|---|---|
| `issue/messages.ndjson`, `issue/tests.jsonl`, `issue/empty.ndjson` | The issue's three files, verbatim from #105's details block |
| `variants.py` | Writes the three variants below from `issue/messages.ndjson`, changing only step results (run from `issue/`; `probe.sh` does) |
| `issue/messages-ffs.ndjson` | `FAILED`, `SKIPPED`, `SKIPPED` (run D) |
| `issue/messages-last.ndjson` | `PASSED`, `PASSED`, `SKIPPED` with the issue's reason as the message (run E) |
| `issue/messages-attach.ndjson` | All `PASSED`, with a `kronikol-bypass` attachment on the middle step (run F) |
| `extract.py <report dir>` | Prints each scenario's result, the run's history letters, and each step's status, `bypassReason` and `comments`, from `TestRunReport.json` and `History.run.json`, so no report is ever opened |
| `probe.sh <Kronikol.Tool.dll> [out dir]` | Runs the eight ingests below and `kronikol query summary` on each. The reports go to the out dir (a temp dir by default), never into this folder |
| `probe-results-4.6.0.txt` | `probe.sh`'s output on a 4.6.0 build (`dotnet build src/Kronikol.Tool -f net10.0` at `37e93813`) |

| Run | Inputs |
|---|---|
| A | `--cucumber-messages issue/messages.ndjson` (the issue's run A) |
| B | `--tests issue/tests.jsonl` (the issue's run B) |
| C | both (the issue's run C, what a playwright-bdd consumer runs) |
| D, E, F | the three variants |
| G | `--cucumber-messages playwright-bdd/messages.ndjson` |
| H | `--cucumber-messages cucumber-js/messages.ndjson` |

To check a fix: build the tool, run `bash probe.sh <dll>`, and compare with `probe-results-4.6.0.txt`. A, B and C should
each read: middle step `Bypassed` with the reason in `bypassReason`, scenario `Bypassed`, history `B`.

## The two real producers

**`playwright-bdd/`**: playwright-bdd 9.2.0 on @playwright/test 1.62.1, as published (no patch). Three scenarios: a step
that attaches `kronikol-bypass` and returns; a step that calls `$test.skip(true, reason)`; a step that throws. A
`Before` hook attaches `kronikol-test-id`. No step uses `page`, so no browser is needed.

- Re-run: `npm install && npx bddgen && npx playwright test`; the messages land in `out/messages.ndjson`.
- `messages.ndjson` is that file from 2026-10-06, with the probe directory's absolute path rewritten to `C:\fixture`, as
  the golden fixture's are (`tests/Kronikol.Tests/Ingestion/Cucumber/CucumberFixtures.cs`).
- `summarise.py <messages file>` prints each test step's status and message, and where each attachment landed;
  `summary.txt` is its output for this file.

**`cucumber-js/`**: cucumber-js 12.9.0. A step that returns `'skipped'` followed by a defined and an undefined step, and
a step that throws, with an `After` hook.

- Re-run: `npm install @cucumber/cucumber@12 && npx cucumber-js --format message:out.ndjson`.
- `messages.ndjson` and `summary.txt` as above.

What they showed is in the plan's §11 log.

## The execution (2026-10-07)

| File | What it is |
|---|---|
| `r1/red-4.6.0.txt` | R1's first facts against the 4.6.0 source: 21 red, each for its own reason, and the guards green |
| `r1/red-4.6.0-all.txt` | R1's facts as committed (the paint facts and the facts added during the work among them) against the 4.6.0 source |
| `r1/probe-results.txt` | `probe.sh` on the R1 build |
| `mutations/mutate.py [r1\|r1-late\|r2]` | Applies each mutation alone to a clean checkout, builds, runs the bypass facts, and says which fact caught it. Run it from the root of a worktree holding the release's commit, never the shared checkout |
| `mutations/r1-results.txt` | Its output for R1: every mutation caught by the fact it names |

The probe now finds `query summary`'s count line by its words: since R1 a run's diagnostics can stand above the header.
