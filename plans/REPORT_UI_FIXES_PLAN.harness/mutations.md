# Mutations for REPORT_UI_FIXES_PLAN

Each line breaks one rule of section 4 by one edit; at least one fact of section 6 must fail on it, and the failing
fact's message is read (a fact that fails for another reason does not count). A mutation that survives means a fact
never reaches its case: fix the fixture and add a premise that fails when the case is absent, or record why the
mutation is equivalent. Run them in a snapshot worktree, never on the release's own tree.

## I1 (chips)

- C2: `cycle_chip` ignores the row's ways (always three-way).
- C3: a pressed switch gives two-way.
- C4a: `apply_url_hash` sets `tagx` chips red without turning the row three-way.
- C4b: `hpx=1` does not turn the Tags row three-way.
- C4c: Happy paths' Hide leaves the row two-way.
- C5: turning three-way off leaves red chips red.
- C5b: turning three-way off does not rewrite the link.
- C6: the right-click handler acts on a two-way row.
- C6b: Alt+click leaves a chip out on a two-way row.
- C7a: `tagstates=3` written while two-way (the default written).
- C7c: `tagstates=3` written for a red chip's three-way (Happy paths' Hide then Show become two Back entries).
- C7b: `tagstates` not read back.
- C8a: Clear All leaves a row three-way.
- C8b: a row's Clear resets the row's ways.
- C9a: a flip leaves the chips' titles as they were.
- C9b: a flip drops the count from a chip's title.
- C9c: the switch carries `aria-pressed`, or an `aria-label` that lacks its visible text.
- C12: the switch written only when `includeTestRunData` (Specifications and a merged page lose it).
- C1: one switch flips both rows.
- C11/E1: the 36 px list without `.tag-mode-toggle`.
- E2: Status buttons without `aria-pressed`.
- E3: SkippedAfterFailure back under Failed.
- E19: the touch rules left in the 480 px block.
- C5c: turning three-way off leaves Happy paths on Hide.

## I2 (colours)

- F1: no `tagShown` in the note path's parts swap.
- F1b: `tagShown` after `makeNotesCollapsible` (the buttons read untagged geometry).
- F2: no `tagShown` in `onQueueItemDone`.
- F3: the waiting list back on the IntersectionObserver.
- F3b: the ResizeObserver tags only pictures inside the window (a far scenario's parts, laid out ahead of it, reach the window untagged).
- F5: the F3 facts sample in `requestAnimationFrame` (they must then report a raw frame that is never painted, and so cannot go green: proves the instrument matters).
- E15: the aside outline drawings marked as sequence hosts again.

## I3 (blocks)

- L1a: `dropEmptiedBlocks` drops a block that was empty before the filters.
- L1b: the pass runs when no filter changed the source.
- L2a: openers tested before arrows (a participant called `group`).
- L2b: note bodies not skipped (a body line `end`).
- L2c: `<style>` blocks not skipped (a `group {` style line).
- L2d: openers read in lower case only (`Loop`, `GROUP` kept empty).
- L2e: `end group` not read as a closer.
- L2f: `end box` read as a group's closer.
- L3: a comment counted as content (an assertion's location comment keeps an empty block).
- L4a: no stack (an emptied loop in the Setup box leaves the Setup box).
- L4b: `else` counted as content.
- L4c: an `alt` with one live branch dropped.
- L4d: a `box` emptied of its declarations kept.
- L3b: `destroy` or `return` not counted as content (their block dropped).
- E5: the splitter's opener test before its arrow test.
- E6: the C# split re-opens the Setup box eagerly.
- E7: the participant-row width not reset at a split.
- E9: `stripParticipantCalls` reads note bodies.
- E13: `scanOpenBlocks` counts a style line.
- E14: tooltips read the original source.

## I4 (names)

- N1: the ReqNRoll lane titleizes the feature again.
- N2: the word pattern without the apostrophe.
- N2b: U+2019 not taken as an apostrophe.
- N3: `ScenarioStableId.For` hashes the heading (if Q5 keeps ids).
- N3b: `merge` hashes the heading while the report hashes the key.
- N3c: the key computed with the current `Titleize`, not the frozen 5.9.0 copy (the `Month's` feature gets new ids).
- N3d: the BDDfy, LightBDD or NUnit4 lane records no key.
- N5b: `nameAsWritten` not written to the data file (`merge` groups the bracketed scenario again).
- N4: the outline heading through `FormatScenarioDisplayName` again.
- N5: `ParameterParser` grouping a Gherkin name.
- E16: `HTTP,` lower-cased.

## I5 (slider)

- S1: `placeControls` ignores the open settings.
- S3: GAP back to 8.
- S4a: opening the settings does not re-place a shown slider.
- S4b: a focused slider not placed.
- S2: the focus script keeps its own copy of `freeTop` (the shared one changed, the copy not).

## I6 (step bars)

- B1: no `HorizontalAlignment center`.
- B2: the doc string drawn as lines.
- B2b: the doc string's lines joined before escaping (the `\\n` breaks turn into text).
- B2c: the doc string's cell cut by `TableCellBudget`.
- B3a: one space where `<space:8>` belongs.
- B3b: `TruncateLabel` cuts inside `<space:`.
- B3c: the normaliser keeps `<space:8>`.
- B3d: `TruncateLabel` cuts inside `<sup>`.
- B3e: the normaliser keeps `<sup></sup>`.
- B4: the header row not centred by the role pass.
- B4b: the header told by the rules alone (a doc string's box is shaded and its lines centred).
- B5: body cells keep `--dg-step-ink`.
- B6a: one ground per bar again.
- B6b: a header-only table unshaded.

## I7 (phone)

- P1a: `.features-tools .toolbar-right` left out of the phone rule.
- P1b: the page without tabs not stretched.
- P2: `.tl-seg-btn` without `flex: 1 1 0`.
- P3: the sweep without the `.tools-gear` click.

## I8 (history)

- H2: the rings without their scroller.
- H3a: the newest-first scroller right to left (opens on the oldest).
- H3b: one list per card shown for both radios.
- H3c: a list's `aria-label` naming the other order.
- H4: the oldest-first radio checked as written.
- H4b: the radios without `autocomplete="off"` (Firefox keeps Oldest first across a reload).
- H3d: the hiding rule inside `:has()` (an engine without it shows both lists).
- H5: `HistoryVerdicts.Runs` reversed in the model.

## Runs

**R1 (5.9.1, 2026-10-26).** Each run edited the release's own scripts in its worktree and put them back from a copy
(not a snapshot worktree: the tree had nothing else in flight).

- I2: F1 failed the note's own redraw (light and dark) and the Wide width; F2 failed the first drawing whose later part
  lands first; F3 failed the view redrawn behind another and the far scenario; E15 failed the aside drawings. Each of
  the seven `DiagramColourFrameTests` facts that hold a fix failed only on its own mutation, and all seven failed on
  5.9.0's scripts for the reason section 6.2 gives; the report controls' fact, the control, passed throughout. F1b,
  F3b and F5 were not run.
- I3: nine mutations of L1 to L5 and E5 to E14, each failed a `FilteredBlocksTests` fact or a C# fact.
- I6: B4b (the header told by the rules alone) failed the doc string's box fact.
- E2 and E3: with the change taken out, the E2 fact failed on the first button's missing `aria-pressed` and the E3 fact
  on the scenario skipped after a failure missing under Skipped.
