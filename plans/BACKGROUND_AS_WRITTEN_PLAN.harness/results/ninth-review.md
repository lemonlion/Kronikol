# The ninth independent review, of the release's first commit

Given the code and not the plan, on `ff9cbcfa` against published 5.28.2, 2026-11-04: most of all what no review had read, `apply_r1o_search_by_row.py`, the accent, and the facts written during execution. Its probes are `probes/review-9/`. It ran no Playwright suite and no example project, so as not to meet another session's run, and drove pages it generated itself through the bundled Chromium.

It found no blocker, and three faults a reader sees that this patch would have brought in, as every review before it had.

## Its faults, and what became of each

| | Fault | New in the patch? | What became of it |
|---|---|---|---|
| 1 | After Clear All, the empty state's Clear or going Back, a search for the same word leaves closed what the reader closed in the last one, so the match is hidden until the field is typed in and emptied by hand. Only the emptied field reset `_sectionsRevealed`. | Yes | `forget_sections_revealed`, called by `reset_filters`, which is the path those three share. `A_search_after_Clear_All_opens_again_what_the_reader_closed`, `A_search_after_going_Back_opens_again_what_the_reader_closed`. |
| 2 | A row the search chose overwrites what the reader left. Close Steps on row a, search a word held by two rows (it picks e, open as it stands), click f, click a: closed on all three on 5.28.2, open on f and on a here, undoing the reader's own close. A Background opened on a ended closed on f and a, and a composite opened on a closed on f. | Yes | Every switch now keeps what the row left held, the search's too, and a row the search chose keeps only what the reader changed there since (`selectRowChosen`, `selectRowKeep`). What the search opened on that row is not counted as the reader's (`selectRowOpenedBySearch`), which also ends the eighth review's fault 1 on such a row. `A_row_the_search_chose_does_not_undo_what_the_reader_closed`, `..._opened` (a section and a composite), `What_the_reader_changes_on_a_row_the_search_chose_carries`, `What_the_search_opened_on_a_row_it_chose_does_not_carry`. |
| 3 | The triangle sat on the accent on a Background step with sub-steps: its box at x 15 to 23, the accent at 19 to 22, in light, dark and an outline's panels. | Yes (the accent is what Q4 added) | The accent now sits left of the triangle: `margin-left: 9px; padding-left: 13px`, the same 25px in all. `The_accent_clears_a_steps_triangle_and_moves_it_out_of_line_with_none`. |
| 4 | `FullPipelineFixture` took a failed build for a run asked to fail, as `dotnet test` exits 1 on both. Read from the code; it did not run the fixture. | Yes (a test) | It now wants `Failed!` in what the run printed, and the message carries the output. |
| 5 | `The_search_changes_no_row_but_the_one_it_chose` read only the reader's row, so it would have passed on 5.28.2. | Yes (a test) | The fixture has a fourth row, and the fact reads that a row nobody has been to is as it started. |
| 6 | Small: the fixture's temp report was never deleted; the new ingest fact split `PhaseRepro` from its doc comment; `A_failed_background_step_shows_the_failed_icon_on_its_muted_text` reads the same on 5.28.2. | Yes | The first two are fixed. The third is a guard and stays, and `red-later-facts-before-the-fix.txt` shows it. |

## The same on 5.28.2

Clear All and Back leave `tr.row-search-match` and the chosen row, now in the plan's §11. A throwing suite `[AfterScenario]` is F16, and R4. A `[BeforeScenario]` hook that runs a step leaves the Background unmarked, in the plan's §9. In the Cucumber lane a Background step that runs a step is listed twice, which is Reqnroll's stream (§6.4, §11).

## What it found fine

- **Scripts:** hand-only switches, sub-steps drawn late, a held Background per row, the Failure Result, two outlines kept apart, the search moving rows and opening once, a load with `#q=`, BrowserJs drawing a row's diagram after the section opens, no page errors.
- **The stylesheet:** with and without sub-steps, step numbers, an outline's panel, dark, 375 and 320 px, a wide Background at 320, and Export Filtered HTML. The status icon's place and the right gap match 5.28.2, and the page does not overflow.
- **Hooks:** real runs on Reqnroll 3.3.4 (xUnit v3, both binding assemblies and derived only) and the floor 3.0.1 (xUnit v2). The internal `CloseStepsLeftOpen` is found, a default-order hook finds the step left open, each stopped step is listed failed under its runner, and an attachment falls to the scenario. Rule, outline, duplicate-text and placeholder Backgrounds read right.
- **Facts:** the accent's values and the section's closed state on 5.28.2 are what the new facts reject; the ingest input crashes 5.28.2 and gives both files here; the two-lanes fact, run again in a scratch program, has 30 of 41 scenarios with a written Background and the exact list of those that differ, so it is not vacuous.
- **CI:** a failing run with `GITHUB_STEP_SUMMARY` set writes to it and a blank one writes nothing; `GITHUB_OUTPUT` is untouched; failing then green, with history and kept runs off as `test.runsettings` sets them, reads `# No failures` with no `runs/`.

## What it did not get to

The Playwright project and the example projects' facts, which it read and enacted but did not run; the fixture itself; the TUnit lane; Firefox and WebKit; the Kronikol4J ledger and the wiki. The release's own round runs the first four.
