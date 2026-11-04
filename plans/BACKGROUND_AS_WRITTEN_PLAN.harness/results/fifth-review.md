# The fifth independent review of R1 (2026-11-03)

A fresh reviewer, given the code and not the plan, on the pieces changed since the fourth: the row script, the toggle record, `HasAnyFailed` and `DescribeNested`, and whether the facts fail when the code is wrong. Its inputs are in `probes/review-5/`: `Features/Q5/`, `Steps/Review5Steps.cs` over a copy of the shape suite, `gen/` for reports built by hand, and the page probes. `fifth-review-that-cut-*.txt` are what it read on that cut, and `fifth-review-published-*.txt` the same on 5.28.2. `gen/Program.cs` here makes its first reports from the options, as a run does, where the reviewer's used the public method: on the fixed cut only the options can tell a section left unset. `fifth-review-on-the-final-cut.txt` is the same again on the final cut. Every fault below was reproduced by running it.

## Faults

| # | Fault | On that cut | On 5.28.2 | Where it went |
|---|---|---|---|---|
| 1 | A nested step paired with a `[GivenStep]` of the same text, as the guard stopped only a keyword-less `[Step]` | `Q02`: a woven step that passed reads `✗ 0.0` and `Failures.jsonl` lists it. `Q15`: one that failed reads passed. `Q16`: a phantom sub-step. `Q09`: a `[WhenStep]` takes a `Given` step's table | Sub-step states right | Fixed: a nested step is paired by what opened it, not by its text (plan 5.3) |
| 2 | A filter search's single match stays shut where the first row holds a failure: the reveal opened the first row's section and relied on the row click's carry, which the hold blocks | `FirstFails`, `Q06`: the row is chosen and the section stays shut | Open | Fixed: the search opens what its match needs again after choosing the row (plan 5.6) |
| 3 | A reader's choice is lost through a row holding a failure or lacking that section | `Pay`: open on a, b, c reads shut, and a again reads shut | Stays open | Fixed: what the reader left lasts as long as the page (plan 5.6) |
| 4 | The public record's contract moved | `new() != BuiltIn`, hash differs, `ToString` equal; `new() { HeadersShown = true }` closed where `BuiltIn with { HeadersShown = true }` opens; a JSON round trip, a reflection copy and a no-op `with` lose the flag | All equal | Fixed: nothing on the record moves; only the options say whether the section was left unset (plan 5.6). Three reviews in a row had found this flag wrong |
| 5 | A failed composite's state crosses rows | `FirstFails`: its open state opens the passing rows' composites. `Q05`: the section open for a failure with the composite shut inside | Both shut | Fixed: a composite holding a failure keeps its own state (plan 5.6) |
| 6 | A nested step a hook stopped is listed after a woven step that ran after it, and the comment said nothing runs after it | `Q13`: `0.0 Woven`, then `✗ 0.1` | No woven step listed, rows misaligned | Fixed: it takes its place among the steps as it closes (plan 5.3) |
| 7 | A `<details class="constructor">` a suite's script adds stops the row switch: `detailStates[d.className].push is not a function` | The highlight moves and the panel stays | Switches | Fixed: maps with no prototype, and a kind is a `<details>`' first class |

## What no fact caught

It broke the patch one line at a time. Facts for each are in `prototype/apply_r1k_facts.py`, and the mutations in `prototype/mutations.py`:

- `A_failing_rows_section_the_reader_closed_stays_closed_when_they_come_back` passed on 5.28.2's script and with no hold at all: it now asserts the section open on arrival first.
- `A_nested_step_is_not_taken_for_a_woven_step_that_reads_the_same` used a keyword-less step only: it is now a theory over both.
- Unpinned: an explicit `false` on outline rows; a section reading only its steps' own status; a composite that failed by itself having its sub-steps held; `HasAnyFailed`'s recursion; the Background and Failure Result carry; dropping a nested step's own nested steps or its inline values; a feature only some of whose scenarios the hooks saw; a row differing from the document only in case.

## Measured, no fault

`HasAnyFailed` reading a step's own status changes only a composite that is Failed with no failed descendant: it starts open and its sub-steps are drawn and not held. The cost, on reports built to be the worst case (`fifth-review-loadtime.txt`, `fifth-review-clicktime.txt`):

| Report | Plain size (MB) | Elements | Load (ms) | Other |
|---|---|---|---|---|
| Real `Q12`, 300 rows of 60 woven steps | n/a | 8,675 to 98,674 | about 570 to 850 | row click median 5 to 28 ms |
| Generated, 500 scenarios of 60 | 6.0 to 11.84 | 27,974 to 147,973 | 922 to 1,332 | |
| Generated, 2,000 of 20 | 14.7 to 22.14 | 110,474 to 270,473 | 1,840 to 2,834 | |
| One composite of 5,000 | 1.68 to 2.72 | 520 to 20,519 | n/a | open-all 97 to 440 ms |

Every row there is a composite that failed by itself. A failed step is what a reader opens first, so the plan keeps it, and says so.

## Read and not run

The wiki's row for `BackgroundStepsOpen` gives its default as `false` with no word of the failure, which plan section 8 changes (`Report-Configuration.md:352` at `c9e0698`; the reviewer read `:273` in a local checkout behind that). `ReqNRollStepInfo`'s `Nested` and `RanByAStep` join its record equality but not `ToString`: a listed step never equalled one built by hand anyway, as its `Duration` is a measured time.

## Also on 5.28.2, so not R1's

- A Search-tab line for a later row's sub-step lands on row 0's line (`fifth-review-published-*.txt`, `probes/review-5/hit.js`).
- Emptying the filter search leaves open the Background section it opened, though the wiki says it closes what it opened: `revealSingleMatch` does not record a section in `_openedBySearch`.

Both are in plan section 11.

## Found fine

Failure Result open on entering a row that never reached its steps. Rules and five outlines on one page, with no crosstalk. Two `Examples:` blocks with step attachments. A file attached to a Background step. Failure-cluster links with Back and Forward, the status filter, Expand and Collapse All. Search with the first row passing, under `StepsSectionOpen = false`, and under `BackgroundStepsOpen = false`. Every Test-run and Specifications overlay combination. A merged report. `query steps` inline and separate. The TUnit lane, which no fact runs: 11 pass, and asked to fail, 7 fail with exactly the failing steps the real run's fact expects. One `ReportGenerationTests` fact first hit a port another job held (`localhost:15034`) and passed alone.
