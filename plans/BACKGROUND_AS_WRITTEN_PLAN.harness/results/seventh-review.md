# The seventh independent review of R1 (2026-11-04)

The search's fixes named below were replaced by a simpler design after the eighth review (plan §5.6, `eighth-review.md`). The shapes stand, and what each now does is in `eighth-review-on-the-final-cut.txt`.

A narrow review, given the code and not the plan, of three pieces the sixth review's fixes changed: the filter search's reveal, `CloseLeftOpen`, and `StepStopwatchKey`. Its inputs are in `probes/review-7/`. `seventh-review-that-cut-*.txt` are what it read on that cut. Every fault below was reproduced by running it. It ran on Chromium, xUnit v2 and Reqnroll 3.3.4.

`StepStopwatchKey` is fine: of 107 reads from suite hooks, one differs, where 5.28.2 reads a stopped stopwatch and this reads a running one, which is right.

## Faults

| # | Fault | On that cut | On 5.28.2 | Where it went |
|---|---|---|---|---|
| 1 | A step a suite's `AfterStep` left open takes in the time and work of the `[AfterScenario]` hooks, as it closed after them | `R715`: a 0.3 s step reads 1.54 s, with a woven step from the hook under it | No row at all | Fixed: such a step closes as the scenario's AfterScenario hooks begin, in a hook of its own at `Order = int.MinValue`. A suite's own hook then finds it in the list, which it did not on either build |
| 2 | Emptying the search does not undo what it opened once the chosen row moves, as `selectRow` kept the search's open as the reader's choice | `sku1`, `sku12`, `sku123`, then emptied: all three rows open, and the reader's closed Steps on the first reopen | The same | Fixed: what it opened on a row goes back before it chooses another |
| 3 | A section the reader closes reopens on the next keystroke | On a chosen row that is not the first | Stays closed there; reopens on the first row and on a plain scenario | Fixed: it opens a row's sections once, which ends this on every row |
| 4 | Where a suite catches what a nested step's `AfterStep` threw, the left-open frame takes what runs next | `R723`: a woven step that ran after sits under the nested step. `R721`: a woven step that returned reads failed. `R724`: a woven step's failure text sits under the nested step | Neither is drawn at all | Accepted, plan section 9. Nothing tells the adapter the frame is finished before the next hook |
| 5 | A `[BeforeStep]` ahead of ours that runs a step whose `AfterStep` throws demotes the written step | `R750`, `R754`: the hook's step is the row, with the written step as its sub-step | One row, the written step, and no hook's step | Accepted, plan section 9. Three suite hooks have to line up |

## The public list

Order is as on 5.28.2: a child just before its parent. Contents grow where a hook kept a step from closing. A parent's status changes: `R701` holds four `TestError` entries where 5.28.2 holds one and two `OK`, as plan section 7's table says, and the changelog must.

## Found fine

The selector finds the right panel on first load, after a click, back on the first row, loading with a search in the address, on a reload and across `Examples:` blocks, with no page errors. A match on one row, emptied, comes back as it started under each toggle. A search moving between scenarios, one match becoming several and back, a plain scenario, emptying on the search's row or a reader's. A held section is not tracked. Back and Forward between two searches. Four levels of nesting with hooks throwing at every level or alternate levels, catches, Background steps and outline rows all give the right tree and statuses.

## Also on 5.28.2, so not R1's

- Back to an address with no search, and Clear All, undo nothing: the row, its mark and the opened sections stay.
- The search moves a reader off a matching row to the first matching one.
- A step whose `BeforeStep` and `AfterStep` hooks both throw is listed on neither build (`R710`).
- A call in `[AfterScenario]` counts under "Calls in the failing step" (`R753`).

Plan section 11 lists them.

## Not reached

Back after Enter is only partly covered: its driver crashed on the second such sequence. The `Examples:` blocks sequence ran on the default page only.

The fixes for faults 1 to 3 had no review after this one. Their facts and mutations are in `prototype/apply_r1m_review7.py` and `prototype/mutations.py`.
