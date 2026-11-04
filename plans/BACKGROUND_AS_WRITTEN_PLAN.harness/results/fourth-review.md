# The fourth independent review of R1 (2026-11-03)

A fresh reviewer, given the patch and the code and not the plan, on the cut before the plan's last log entry. Its inputs are in `probes/review-4/`: `features.txt` cut at each `#file` line by `split.py`, `suite/Steps/ReviewSteps.cs` over a copy of the shape suite, `api/Program.cs`, `ingest/make.py`, and the browser probes `rowswitch.js`, `readerclose.js` and `composite.js`. `run.sh` and `steps.sh` run a suite and read its steps through `kronikol query`. Row names below are that feature file's. Every fault here was reproduced by running it.

## Faults

| # | Fault | Where it went |
|---|---|---|
| 1 | A hook-run step ahead of the Background loses the marks: `R20` from a `[BeforeScenario(Order = 1)]`, `R53` from a `[BeforeStep(Order = int.MinValue)]`. `R70` half-counts when the hook's text equals the first Background step. 5.28.2 marks the hook's step `b0`. | Accepted, plan section 9. |
| 2 | With `SeparateBackgroundSteps`, selecting a failed outline row closed its Failure Result (`R06`, a `[BeforeScenario]` throwing for row b), as states were copied by position. 5.28.2 has it where it guesses (`R23`). | Fixed: states go by a `<details>`' kind and place (section 5.6). |
| 3 | Messages out of reach (`R30`, the `.ndjson` taken out of the embedded resources): no mark, and the guess is gone too. | Accepted, sections 5.1 and 5.9. |
| 4 | Public default. Left unset, a section opens with a failure in it. A hand-built `ResolvedToggleDefaults { BackgroundStepsOpen = false }` opened, and `Resolve(unset) != Resolve(false)`. | The hand-built record is fixed. The first stays as section 7 reads it and Q1 asks. The inequality is noted in section 5.6. |
| 5 | `StepMarkerMatches` in the form taken from #225's R2 refused a raw marker (an ingest's `kind: marker`, unescaped). | Gone: the matcher went back to published, with #223's fifth review (below). |
| 6 | Row switch: the forced open leaked back to the row left (`R05`), undid a reader's close, and a failed composite hides through `selectRowSyncSteps` (`R63`). | The first two fixed (section 5.6). The third left in section 11, as on published. |
| 7 | `DescribeNested` paired a nested step with a woven `[Step]` of the same text (`R02`). | Fixed: only a step with a keyword is paired (section 5.3). |
| 8 | `HasAnyFailed` changes every lane, and sub-steps are no longer held back. | In section 5.6, the changelog line and the ledger line. |
| 9 | A failing nested run writes `## Debug this run` to CI's job summary. | Fixed: the fact clears `GITHUB_STEP_SUMMARY` (section 6.3). |
| 10 | The row script's JavaScript had no Playwright fact. | Fixed: `BackgroundSectionRowSwitchTests`. |

Also on 5.28.2, so not R1's:

- A woven step in a hook shifts sub-steps (`R60`), and with nesting draws a phantom `b0.0` (`R03`). Plan section 9.
- An ingest whose steps carry `background: true` loses every call's step. Plan section 11.

Read and not run: no title check on the pickle (the text check stands). A step left open could list Passed on a framework timeout. A `Kronikol.OpenStep` entry beside a suite that serialises `ScenarioContext`, where a Stopwatch and the hooks already sit.

Found fine: the floor rows, double discovery, the formatter on, xRetry, parallel runs, hook-run steps after ours, deep nesting, attachments, Backgrounds in a rule, merge, the query verbs, the output files, `GetAll()` (only a composite's `Status` changes), a hand-built info, and collect-on-finish on both bases. The core suite, Integration's Reqnroll facts (70 of 70), TUnit (11 of 11), 73 Playwright facts and 14 pipeline facts passed on that cut.

## #223's fifth review, on the same matcher

From that plan's session, on the form R1 had taken from #225's R2. It refuses a bar that is not escaped, which published reads: a store from before v3.31.8, another producer's `kind: marker` lines, `InsertPlantUml(..., DiagramMarkerKind.Step)`. There a published read of `/read`, `/send`, `/confirm` at 0, 1, 2 becomes `-`, `-`, 2, with `StepAttributionMismatch: 2`. It also makes false matches from reading the first line only, the 400 cap, folded spaces and `Contains`: `a new order` with `anew order`, two steps sharing a first line, and a Background step `a cart` under a bar `When a car takes the order`, which lands the call on `b0`.

So R1 leaves the matcher as published and #128 stays with #225's R2 (plan section 5.11). A form taken from a sibling plan still needs its own review.
