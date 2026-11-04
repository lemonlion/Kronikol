# The eighth independent review, narrow

Given the code of the two parts that changed last and had no review, the hook that closes a step left open and the search's reveal, and not the plan. Seven faults, five in the search and two in the hooks, all small. Its probes are `probes/review-8/`, its runs `results/eighth-review-*.txt`. In the page results `*r1[bg+ st-]` means row 1 is shown, Background open, Steps closed, and `kept` is what `selectRow` remembers. Line numbers are in the patched files of that cut.

## Faults in the search

| # | Shape | Seen | Should be | 5.28.2 |
|---|---|---|---|---|
| 1 | A row switched by hand keeps what the search opened as the reader's choice. Word in row b, click row c, empty the search (`F1a`). `alphawor`, click c, `alphaword`, empty (`F1b`). The reader closes Steps on a, the match is on a, click b, empty, back to a (`F1c`). With Steps closed by option, Steps leak the same way (`stepsclosed`, `A1`). | c's Background stays open with `kept` true, so a opens too when the reader returns to it. Row a, where the reader started, ends with Background open. The Steps the reader closed on a reopen. | Closed as it started, the reader's closed Steps staying closed. Switching back to the row before emptying does come back right (`A5`). | The same end state: not a regression, but §5.6's "the reader's rows come back as they left them" does not hold here. |
| 2 | A section the reader closes and reopens by hand shuts when the search is emptied. Match, reader closes Background, reader opens it, empty (`F2a`, `F2b`). The diagrams section does the same (`G4`). | Closed. | Left open. | Left open: new in the patch. |
| 3 | One outline's row choice acts on another outline's section. Two outlines share a word. One match on PairA, then widen so PairB chooses a row. | `F3a`: PairA's Background shuts although PairA's row did not move. `F3b`: the reader closes it, widens, narrows, and it reopens. Plain scenarios, the controls in the same file, keep both states. | PairA's sections as they were. | `F3a` stays open and `F3b` stays closed: new in the patch. |
| 4 | "Once for that row" lasts only until the match moves. The reader closes on b, the match moves to c, moves back to b (`F4a`), and the same on a plain scenario (`F4b`). | Reopens on b. | Stays closed. | `F4a` stays closed, by the accident of opening only the first row's sections, and `F4b` reopens. |
| 5 | The diagrams section reopens on the next keystroke. Diagrams closed by option, match, the reader closes the section, one more key (`G2`, `G3`). `G6` shows both in one run: Background stays shut and diagrams reopen. | Reopens. | Stays closed, as the row's sections now do. | The same. The scenario itself also reopens (`B10`), on both builds, which may be intended. |

Runs: `results/eighth-review-faults-search.txt` (faults 1 to 4, second run), `results/eighth-review-faults-diagrams.txt` (fault 5, second run), `results/eighth-review-search-*.txt` (the wider page runs). Driver: `probes/review-8/drive8.js`, `seq8-faults.js`, `seq8-diagrams.js`, `gen/`.

## Faults in the hooks

| # | Shape | Seen | Should be | 5.28.2 |
|---|---|---|---|---|
| 6 | `CloseStepsLeftOpen` is not ahead of a suite's own hook at exactly `int.MinValue`. | The suite's hook comes first, as the test assembly is scanned first and the sort is stable. At that hook `H02` reads `[one:OK] \| active=True`, so the failed step is not in the list, and `H04` draws the suite's woven step under the failed step as `0.0`. At `int.MinValue + 1` the step is listed and the frame closed. The same on all three adapters, both Reqnroll versions and both binding orders. | The comment said "Ahead of a suite's own". | No row at all and the woven step dropped, so this is not worse. |
| 7 | A step run by a suite's `[AfterScenario]` and left open is never closed. `H23`, a hook at order 0 runs a step whose `AfterStep` throws, and catches the exception. | No row for that step, its frame stays open, the report says Failed with no failed step, and the test passes. | The step listed, as failed. | The same. Very rare. |

Runs: `results/eighth-review-hooks-*.txt`. Suite: `probes/review-8/suite/`, run by `run.sh`, with `run-lane.sh` for the other adapters.

## Found again, already recorded

`H24`, `H25`: the shift from a `[Step]` in `[BeforeScenario]`, which is #223. `H16`: a woven wrapper that returned reads failed (`R721`). `H07` to `H09`: F16. `I01`: a hook's run-time ignore loses the scenario on 3.0.1, the same Reqnroll behaviour as `E27` on the floor, and a public hook would not run there either. `H1`: the search moves a reader off a matching row, §11. `E1` to `E5`: Clear All and Back undo nothing, §11.

## What held

- The internal hook ran on xUnit2, xUnit3 and TUnit, on 3.3.4 and 3.0.1, adapter only and with both classes listed in either order, and with the message formatter on. Reqnroll has no visibility check at discovery and calls the hook through a compiled expression (read, and consistent with the runs). The one difference it could see: a suite that calls the public hook methods from its own binding class cannot call this one, so it stays as today (read, not run).
- Hook shapes, each giving the right tree and statuses: a throwing `AfterStep` on plain steps, nested steps at four levels, Background steps and outline rows; definition and hook both throwing, a pending throw from the hook, `BeforeStep` ahead throwing; a step run by `BeforeScenario` and left open; woven steps in the definition, a wrapper and the hook; `AfterScenario` at `int.MinValue + 1`, 0 and `int.MaxValue`, including a suite clearing the list; attachments in each place (the three on a left-open scenario were lost on 5.28.2 and are kept now); `stopAtFirstError` on both versions.
- Page shapes: typing, narrowing, widening and emptying, and a section closed then typed on, same row; rows that differ, with held failures and composites; the reader's own opening before a search; Back and Forward; pages with Steps closed by option, Background open or false by option, inline steps, and the Specifications page of a green run. No page errors, and a second run of each fault's shape gave the same results.
- The patch's own tests: `BackgroundAsWritten` 17 and `BackgroundSectionRowSwitch` 21 pass in a clean worktree built from the patch.

## Not settled

- Fault 1 is partly a judgement: a reader could be said to have accepted the open section by moving on with it. The reviewer calls it a fault because they never touched it and `F1c` undoes an explicit close, and left it as the owner's call.
- The dependency injection plugin was not run. The code it read treats public and internal hooks alike, but that is not proven with the plugin.
- `OpenStepKey` left set after the close: it could reach no effect beyond fault 7, as a left-open step always leaves the scenario failed, so no further step opens. A reading backed by `H22`, not a proof.
- Search typing: values went through the field's `input` event, not real key presses, though it is the same handler.
