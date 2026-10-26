# S5 marks: red runs (SEARCH_PLAN 4.6.5, 6.4)

The marks of stage S5 (H15, H16, H32, H36 and the cases around them). Every new fact was written first and run against
the code the work starts from, `5755f5dc` on branch `feat/github-search-marks`, before any script, sheet or token
changed: the unit class `SearchMarksTests` (`dotnet test tests/Kronikol.Tests --filter SearchMarksTests`, 8 facts, 4
failing) and the E2E class `SearchHitTests` alone (15 facts, 15 failing; bundled Chromium). "Line" is the line of the
test file at that commit that failed. The facts written later, as the work found a case, were each run red against the
code before the change they hold (below, "Red, later").

The E2E facts run on a report the generator writes from captured calls (`SearchHitTests.WriteReport`): a pantry's JSON
response (flour three times on one row; a story of 78 words the engine wraps over five rows, dropping the space at each
wrap; a token of 300 characters with no space the generator breaks and marks with one zero-width space), a ledger of
150 lines drawn 40 tall with its body in a scroll box, a Kafka event, a call to a path of 16 shelves the generator breaks
onto two rows of the arrow's label, a step bar and an assertion the generator breaks at a space and marks with two
zero-width spaces (`before` + U+200B U+200B, then `the` on the next row). Probed in bundled Chromium before the run: the
premises each fact checks first all held.

## Red, as each fact says

| Fact | Class | Red | Line | Why it fails on the base |
|---|---|---|---|---|
| H15 `The_marks_sit_on_the_words_in_both_themes_and_at_every_zoom` (light, dark) | E2E `SearchHitTests` | 2 of 2 | `SearchHitTests.cs:250` | "TypeError: Cannot read properties of undefined (reading 'mark')": no `window.kronikol_marks` |
| H16 `The_marks_come_back_after_a_redraw_and_go_on_Escape_a_click_outside_or_another_search` | E2E `SearchHitTests` | 1 of 1 | `SearchHitTests.cs:266` | the same: no marks |
| H32 `Copies_of_a_marked_diagram_carry_no_marks` | E2E `SearchHitTests` | 1 of 1 | `SearchHitTests.cs:347` | the same: no marks (the copy's own red is below) |
| H36 `The_mark_shows_against_every_note_fill` (light, dark) | E2E `SearchHitTests` | 2 of 2 | `SearchHitTests.cs:389` | the same: no marks |
| `A_match_runs_on_to_the_next_row_of_a_wrapped_note` | E2E `SearchHitTests` | 1 of 1 | `SearchHitTests.cs:450` | the same, after its premise (the engine wraps the story between two of its words) held |
| `A_pair_of_zero_width_spaces_ending_a_row_is_the_space_the_wrap_took` | E2E `SearchHitTests` | 1 of 1 | `SearchHitTests.cs:500` | the same, after its premise (a row ends in the pair) held |
| `A_match_in_a_long_note_s_scroll_box_is_marked_in_the_box_and_the_box_scrolled_to_it` | E2E `SearchHitTests` | 1 of 1 | `SearchHitTests.cs:542` | the same, after the box was built |
| `HTML_places_are_marked_with_the_highlight_API_and_keep_their_text` | E2E `SearchHitTests` | 1 of 1 | `SearchHitTests.cs:574` | the same |
| `Without_the_highlight_API_an_HTML_place_is_outlined_and_its_words_left_alone` | E2E `SearchHitTests` | 1 of 1 | `SearchHitTests.cs:606` | the same |
| `A_mark_leaves_every_text_of_the_diagram_whole` | E2E `SearchHitTests` | 1 of 1 | `SearchHitTests.cs:627` | the same |
| `The_named_occurrence_is_outlined_and_the_others_underlined` | E2E `SearchHitTests` | 1 of 1 | `SearchHitTests.cs:656` | the same |
| `The_place_pulses_twice_on_arrival_and_holds_still_under_reduced_motion` | E2E `SearchHitTests` | 1 of 1 | `SearchHitTests.cs:680` | the same |
| `The_marks_go_when_the_scenario_is_closed` | E2E `SearchHitTests` | 1 of 1 | `SearchHitTests.cs:704` | the same |
| `Text_on_a_mark_reads_at_AA_in_every_theme` (both dark blocks) | unit `SearchMarksTests` | 2 of 3 | `SearchMarksTests.cs:77` | "--kron-accent #7ea6f2 on the mark #4d3d0a is 4.34:1": a link (an arrow's label to an internal flow is drawn as one) marked in the dark theme reads under AA; the light block passes |
| `The_marks_paint_from_the_tokens_and_hold_still_under_reduced_motion` | unit `SearchMarksTests` | 1 of 1 | `SearchMarksTests.cs:102` | `svg .kron-mark-ground` has no rule: the sheet has no marks |
| `The_marks_script_is_in_the_page_after_the_search_scripts` | unit `SearchMarksTests` | 1 of 1 | `SearchMarksTests.cs:90` | "the marks script is in the page": no `kronikol-search-marks` script |

## Guards, green on the base

| Fact | Class | Rows |
|---|---|---|
| H36 `The_mark_edge_shows_against_every_note_fill` | unit `SearchMarksTests` | 3 of 3: the edge at 3:1 or more against `--dg-note`, `--dg-event`, `--dg-step` and `--kron-card` in all three blocks already (light 4.52, 3.88, 4.18, 4.87; dark 5.97, 5.99, 5.59, 6.71) |
| `Text_on_a_mark_reads_at_AA_in_every_theme` (light) | unit `SearchMarksTests` | 1 of 1 |

## Red, later

Each with the marks script in and the change the fact holds not yet made (or, where it says so, taken out again for the
run and put back after).

| Fact | Red | Why it failed |
|---|---|---|
| H32 `Copies_of_a_marked_diagram_carry_no_marks`, its own red | 1 of 1, the other 14 green | Copy as SVG carried the marks: 6 elements of `kron-mark` (the 3 grounds, the 2 underlines and the outline) where the fact wants none, as `lookClone` copied the picture with them |
| the PNG half of H32 (run first in a copy of the fact, the file put back after) | 1 of 1 | the PNG copied with the marks on was not the PNG copied with none: `2460x3012:ad4ed0be` against `2460x3012:1e43841c` |
| `A_match_runs_on_across_the_rows_of_an_arrow_s_label_as_the_search_reads_them` (the script's number rule taken out for the run) | 1 of 1 | 0 occurrences for the two pieces glued: the arrow's number, which the engine draws half way down between the label's two rows, read as a row of its own between them and as part of the label's text, which the source and the search hold without it |
| `An_export_taken_while_marks_are_on_carries_none` | 1 of 1 | "true" for "False" (`SearchHitTests.cs:729` then): Export Filtered HTML carried the failure's `kron-mark-place` class and the rects of a picture drawn when the report was written |
| `A_place_not_laid_out_is_marked_once_it_is` | 1 of 1 | 3 grounds for none (`SearchHitTests.cs:861` then): marked while its tab was hidden, the picture got marks of no size at once, measured on nothing, instead of once it was laid out |
| `A_marked_label_comes_back_after_a_redraw_that_numbers_its_arrow_anew` | 1 of 1 | the marks never came back (15 s wait at `SearchHitTests.cs:816` then) after Events turned off for the scenario took the event's arrow out, its premise holding (the shelf call's arrow numbered anew): a label given as an element was known in the picture drawn anew by its text with its arrow's number in it, and the number had moved |
| `A_diagram_given_whole_is_outlined_and_the_words_in_its_picture_marked` | 1 of 1 | "0 marks for 6 drawn; nothing marks ..." (`SearchHitTests.cs:865` then): a diagram's host given as the place was an HTML place, whose text took in the picture's SVG text, so its 6 occurrences of flour were counted and highlighted with `::highlight` on SVG text, and no rect drawn (the way to a match gives the host for a match in a diagram's participants and calls, `report-search-hit-script.js` on `feat/github-search`) |
| `A_scroll_box_given_by_its_frame_is_outlined_inside_it_and_found_again` | 1 of 1 | `["0", "false", "none", "0px", ...]` for `["1", "True", "solid", "-2px", ...]` (`SearchHitTests.cs:642` then): the frame, an SVG `foreignObject`, was taken for a picture's element, whose texts are the rows hidden under it, so the box's own text went unread and the outline was drawn round the frame; and `svg .kron-mark-place`, meant for the ring, matched the box inside the picture and took its outline off (now `svg rect.kron-mark-place`, and the box's outline inside its edge, over its own `:focus` rule) |

The Kafka event of the fixture goes to a queue (`DependencyCategories.MessageQueue`) since the label fact above: the
report had no Events toggle while it went to a plain participant (the first run of that fact timed out looking for the
toggle, no red of the code).

Three facts were written after the code they check and were not run red on their own:
`The_marks_sit_on_the_words_in_Firefox_and_WebKit` (the H15 and HTML facts' checks in the two other engines),
`The_marks_go_when_Back_leaves_the_scenario` and `A_diagram_marked_before_its_picture_is_drawn_is_marked_once_it_is`;
the mutation sweep (`checks/mutations-s5-marks.md`) breaks the last two with the rule each holds taken out.

## The tokens

The plan's values passed its own check (the edge at 3:1 against every fill) but not the text's: the dark accent, in
which an arrow's label to an internal flow is drawn as a link, read 4.34:1 on the dark mark `#4d3d0a`. The dark mark is
`#4a3800` in both dark blocks; the light block and both edges are the plan's. As the sheet has them now (WCAG 2.2):

| | light | dark (both blocks) |
|---|---|---|
| `--kron-mark` / `--kron-mark-edge` | `#fff1a8` / `#9a6700` | `#4a3800` / `#d29922` |
| the edge on `--dg-note`, `--dg-event`, `--dg-step`, `--kron-card` | 4.52, 3.88, 4.18, 4.87 | 5.97, 5.99, 5.59, 6.71 |
| the edge on the mark (the outline round the one named) | 4.26 | 4.48 (4.19 on `#4d3d0a`) |
| on the mark: `--dg-ink`, `--dg-ink-soft`, `--dg-step-ink` | 12.48, 5.23, 9.90 | 9.27, 5.34, 8.74 (8.66, 4.99, 8.17 on `#4d3d0a`) |
| on the mark: `--kron-ink`, `--kron-ink-soft`, `--kron-accent` | 12.48, 6.81, 5.52 | 9.27, 7.05, 4.64 (8.66, 6.59, 4.34 on `#4d3d0a`) |

## Green

On the code as committed, Debug build, Playwright's bundled engines:

| Run | Result |
|---|---|
| E2E `SearchHitTests` (Chromium; `The_marks_sit_on_the_words_in_Firefox_and_WebKit` in the E2E project's Firefox and WebKit) | 25 of 25 |
| unit `SearchMarksTests` | 8 of 8 |
| unit classes that read the token sheet, the search sheet or the page's head and scripts: `ThemedPagesHtmlTests`, `StylesheetRulesTests`, `ReportThemeTests`, `ReportGeneratorStyleOrderTests`, `DiagramFrameColoursTests`, `BackNavigationReportTests`, `NodeJsPlantUmlRendererTests`, `StepBarPlantUmlTests`, `CopyScenarioNameReportTests`, `FocusScriptTests`, `NoteCopyFidelityScriptTests`, `ReportSheetColoursTests`, `SearchButtonMarkupTests`, `VerdictFencesTests`, `ReportTabsTests`, `RunBandTests`, `ComponentsTabHtmlTests` (with `SearchMarksTests`) | 285 of 285 |
| E2E classes around the copies, the scroll box and the tokens: `DiagramExportLookTests`, `ContextMenuExtendedTests`, `LongPayloadScrollTests`, `HistoryRingsTests` | 69 of 69 |
| the mutation sweep (`checks/mutations-s5-marks.md`) | 27 of 27 killed |

`ExportFilteredHtmlRenderingTests` (9 facts) fails on this branch's base as it does with the marks: each times out
clicking Export Filtered HTML, which the filter box, hidden at rest since S2, hides. With the box opened first
(`OpenFilterRowsAsync()`, in a copy of the class put back after) the 9 pass with the export's strip in. Left as found:
the classes that click a control of the box are the search's to move.

What a mark costs (bundled Chromium, the fixture's report, the median of five calls of `mark`, each taking the last
off first): a note's marks 2.4 to 3.2 ms (3 to 52 occurrences); a whole diagram's 8 ms for 6 occurrences and 15 ms for
567 (119 rects, the rest in the ledger's box).
