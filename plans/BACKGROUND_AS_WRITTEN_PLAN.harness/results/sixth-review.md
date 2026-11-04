# The sixth independent review of R1 (2026-11-04)

The search's fixes named below were replaced by a simpler design after the eighth review (plan §5.6, `eighth-review.md`). The shapes stand, and what each now does is in `sixth-review-on-the-final-cut.txt`.

A fresh reviewer, given the code and not the plan, on the pieces the fifth review changed: pairing a nested step by what opened it, the toggle with no change to the public record, what the row script keeps, the search's reveal, and whether the facts fail when the code is wrong. Its inputs are in `probes/review-6/`. `sixth-review-that-cut-*.txt` are what it read on that cut, and `sixth-review-on-the-final-cut.txt` is `probes/review-6/run.sh` on the final cut. Every fault below was reproduced by running it.

It found no fault in the toggle, and the row script and the pairing did the right thing in every flow it ran.

## A red suite

Three facts passed on 5.28.2 and failed on that cut. Two were in the core suite, which the same night's run also showed; the third lives in `Kronikol.Tests.SearchEngine`, which no measurement of this plan had run.

| Fact | Why | Where it went |
|---|---|---|
| `VerdictFencesTests.cs:71` | It pins the hash of `report-search-function.js` | The pin moves with the file: in R1's edit sites |
| `ParameterizedGroupRenderTests.cs:1274` | It looks for the text `querySelectorAll('details')`, which the row script had stopped holding | The script holds it again |
| `ShippedSearchFunctionTests`, 8 of 787 | Its stand-in for a scenario has `querySelector` and no `querySelectorAll` | The search uses `querySelector`, and `Kronikol.Tests.SearchEngine` is now in the measurements |

## Faults

| # | Fault | On that cut | On 5.28.2 | Where it went |
|---|---|---|---|---|
| 1 | A single-match search opened every row's sections and lost what the reader closed. The fifth review's answer caused this | Close Steps on row c, search a word only row b holds, empty it: row c and row a come back with Steps and Background open | Row c comes back as left | Fixed: the search opens the sections of the row it chose and no other, and records them, so emptying closes them, which also ends a leak 5.28.2 has |
| 2 | A woven step in a `[BeforeScenario]` hook now shifts a composite's sub-steps to the next row (`R11`) | The composite shows none, the `Then` shows the composite's | Each shows its own, as the flat nested row happened to absorb the hook's frame | Accepted, plan section 9. Plain steps shift on both builds (`R19`). It is #223's fault, and that plan's pairing ends it. Pairing rows by what opened them would end it here too, and would lose a hook frame's attachments, which no row would then hold |
| 3 | A nested step a suite's `[AfterStep]` hook threw on loses its table, doc string and inline values, and leaves the public list (`R02`) | Drawn failed with nothing else | Not drawn at all | Fixed: a frame left open is described the same way at either place |
| 4 | The public `StepStopwatchKey` reads 0 in a composite's hook after a nested step a hook stopped (`R07`) | 0 ms | 320 ms | Fixed: only a step opening in earnest sets it |

After a definition catches a hook's failure on its nested step and carries on (`R13`), later calls hang under that nested step until the composite closes. On 5.28.2 the nested step is not drawn at all. Rare, and left.

## What no fact caught

Each break below has a fact now, with its mutation in `prototype/mutations.py`:

- One store of what the reader left for every outline.
- A click on the row already chosen putting a composite's old state back.
- A composite holding only for its own failure and not for a failed step inside it.
- Composites going by kind as sections do.
- A frame left open not described under its parent.
- The public stopwatch key not set.
- The Specifications page told the report's own value.

Three were simplified away. A kind of `<details>` appears once in a panel, so its place among that kind guarded nothing. A frame the hooks open has no parameters, so `?? subStep.Parameters` guarded nothing. And the search's first reveal did nothing the second did not.

One stays unpinned: dropping `InnermostOpenStep is not null` from `AfterStep`'s check would give a top-level step a suite's hook stopped a frame of its own, and so a bar on the diagram. `probes/edge-shapes/`'s `E34` and `E36` read it, and no fact does.

The search fact claimed more than it checked: its word sat in a sub-step whose composite stays closed. Its word is now on a plain step, and the fact asserts the step shows.

## Found fine

Pairing: a stopped step under a woven step, hooks running steps before, after and ahead of ours, a stop on one row, `And`, a top-level stop, pending and ignored from a hook, block hooks, the same text twice with the second stopped, with `stopAtFirstError`, and on Reqnroll 3.0.1. The phase does not leak. The token appears in no written output, and the public surface does not differ on either assembly. `Kronikol.Tests.StepTracking` and `Kronikol.Tests.LightBDD.xUnit3` pass.

The toggle: the options route holds as designed, merge and ingest draw no separate section, Specifications is blank on a failure, and `ResolvedToggleDefaults` is unchanged.

The row script: lazy drawing, two `Examples:` blocks, deep links and inline mode, with no page errors. Every difference from 5.28.2 is an intended one.

Cost: on 1,500 rows the search's synchronous part goes from about 33 ms to 41 on that cut.

## Also on 5.28.2, so not R1's

- A woven step in a `[BeforeScenario]` hook shifts plain steps' sub-steps (`R19`).
- Back and Forward between two row links change the address and not the row shown (`sixth-review-that-cut-nav2.txt`).
- `StepStopwatchKey` read in a composite's hook after a normal nested step gives 0 ms.
- Closing Steps carries onto a row whose failure is in Steps.
- A single-match search does not open a composite holding the word (`sixth-review-that-cut-hidden.txt`).

Plan section 11 lists them.

`BackForwardRestoreTests.R16` on WebKit failed about 3 of the reviewer's 15 runs, on clean and on mutated trees. It reads a long report and no rows. Nobody ran it on pristine main in that batch, so it is not attributed to either build.
