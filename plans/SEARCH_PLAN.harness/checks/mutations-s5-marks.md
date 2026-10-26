# S5 marks: mutations (SEARCH_PLAN 6.4)

Run 2026-10-26 on `feat/github-search-marks` (worktree `C:/Code/Kronikol-search-marks`, the marks over `5755f5dc`) by
`mutate-s5-marks.js` beside this file (`node mutate-s5-marks.js <worktree> [id,id,...]`). Each mutation replaces one
exact line or expression of a shipped script (`report-search-marks-script.js`, and the strips in `context-menu-script.js`
and `report-export-function.js`), builds `tests/Kronikol.Tests.EndToEnd`, whose report carries the scripts from the
assembly's resources, and runs the `SearchHitTests` facts it should break in Playwright's bundled Chromium; the script is
then written back from the bytes it was read from and its SHA-256 compared. A mutation counts only when its facts pass
unmutated first. 24 rows ran in one sweep; `frame` and `box-refind`, and `whole` again, ran after the scroll box given
by its frame came in (the frame read as its box, the box found again by its note, its outline rules), on the code as
committed. Every `from` of the 27 is found once in the committed scripts.

| Mutation | Script | Change | Facts | Unmutated | Mutated | Restored |
|---|---|---|---|---|---|---|
| pair: a pair of zero-width spaces ending a row is read as one: nothing between the rows | `report-search-marks-script.js` | `rowsOf`: the pair rule `end.slice(-2) === ZW + ZW ? ' ' :` taken out | `A_pair_of_zero_width_spaces_ending_a_row_is_the_space_the_wrap_took` | 1 pass | **1 fail** | yes |
| single: one zero-width space ending a row is a gap, a term's space or nothing | `report-search-marks-script.js` | `rowsOf`: a row ending in one zero-width space joins with `GAP`, not nothing | `A_match_runs_on_to_the_next_row_of_a_wrapped_note` | 1 pass | **1 fail** | yes |
| note-join: a note's rows join as a label's do: the engine's wrap a space or nothing | `report-search-marks-script.js` | `noteOf`: the note's `rowJoin: ' '` becomes `GAP` | `A_match_runs_on_to_the_next_row_of_a_wrapped_note` | 1 pass | **1 fail** | yes |
| gap: texts on one y with a gap between them are one row: two participants side by side read as one word | `report-search-marks-script.js` | `rowsOf`: the split at a gap wider than half the font taken out | `A_diagram_given_whole_is_outlined_and_the_words_in_its_picture_marked` | 1 pass | **1 fail** | yes |
| number: an arrow's number is read as part of its label | `report-search-marks-script.js` | `wordsOf`: `number = null` | `A_match_runs_on_across_the_rows_of_an_arrow_s_label_as_the_search_reads_them`, `A_marked_label_comes_back_after_a_redraw_that_numbers_its_arrow_anew` | 2 pass | **2 fail** | yes |
| refind: an arrow's label is found again by its number as well as its words | `report-search-marks-script.js` | `finderOf`: `keyOf` reads `textsOf(c)` for `wordsOf(c)` | `A_marked_label_comes_back_after_a_redraw_that_numbers_its_arrow_anew` | 1 pass | **1 fail** | yes |
| whole: the pictures an HTML place holds are not marked as pictures (a diagram given whole) | `report-search-marks-script.js` | `resolve`: the pictures an HTML place holds left out of its groups | `A_diagram_given_whole_is_outlined_and_the_words_in_its_picture_marked` | 1 pass | **1 fail** | yes |
| svg-text: an HTML place's text takes in the SVG text of the pictures it holds (::highlight on SVG text) | `report-search-marks-script.js` | `htmlStream`: `el.namespaceURI !== SVGNS &&` taken out | `A_diagram_given_whole_is_outlined_and_the_words_in_its_picture_marked` | 1 pass | **1 fail** | yes |
| reading: a whole picture's occurrences in the order of its groups, not in reading order | `report-search-marks-script.js` | `draw`: `svgOcc.sort(readingOrder)` taken out | `A_diagram_given_whole_is_outlined_and_the_words_in_its_picture_marked` | 1 pass | **1 fail** | yes |
| frame: a scroll box's frame is taken for the picture's element it is, not for the box | `report-search-marks-script.js` | `resolve`: `if (inFrame) place = inFrame;` taken out | `A_scroll_box_given_by_its_frame_is_outlined_inside_it_and_found_again` | 1 pass | **1 fail** | yes |
| box-refind: a scroll box is not found again in a picture drawn anew | `report-search-marks-script.js` | `resolve`: an HTML place's `refind` by the frame's `data-note` becomes `null` | `A_scroll_box_given_by_its_frame_is_outlined_inside_it_and_found_again` | 1 pass | **1 fail** | yes |
| picture-added: a picture drawn in an HTML place marked before it was is not marked | `report-search-marks-script.js` | `stale`: a picture added under an HTML place no change | `A_diagram_marked_before_its_picture_is_drawn_is_marked_once_it_is` | 1 pass | **1 fail** | yes |
| order: a mark is inserted after the text it marks, over its words | `report-search-marks-script.js` | `draw`: the ground inserted after its `<text>` (`m.anchor.nextSibling`) | `A_mark_leaves_every_text_of_the_diagram_whole` | 1 pass | **1 fail** | yes |
| current: the occurrence named is underlined as the others are | `report-search-marks-script.js` | `draw`: `named = false` | `The_named_occurrence_is_outlined_and_the_others_underlined` | 1 pass | **1 fail** | yes |
| redraw: a redraw does not draw the marks again | `report-search-marks-script.js` | `watch`: the observer's `stale(...) ? redraw` becomes `void records` | `H16_The_marks_come_back_after_a_redraw_and_go_on_Escape_a_click_outside_or_another_search`, `A_match_in_a_long_note_s_scroll_box_is_marked_in_the_box_and_the_box_scrolled_to_it` | 2 pass | **2 fail** | yes |
| arrive: a redraw pulses the place again, as an arrival | `report-search-marks-script.js` | `redraw`: `draw(rec, true)` | `H16_The_marks_come_back_after_a_redraw_and_go_on_Escape_a_click_outside_or_another_search` | 1 pass | **1 fail** | yes |
| escape: Escape takes the marks away while a layer is open | `report-search-marks-script.js` | keydown: `!layerOpen()` taken out | `H16_The_marks_come_back_after_a_redraw_and_go_on_Escape_a_click_outside_or_another_search` | 1 pass | **1 fail** | yes |
| inside: a click inside the scenario takes the marks away | `report-search-marks-script.js` | `pressed`: `rec.scope.contains(t)` taken out | `H16_The_marks_come_back_after_a_redraw_and_go_on_Escape_a_click_outside_or_another_search` | 1 pass | **1 fail** | yes |
| search: a search run with another query leaves the marks (the search function not wrapped) | `report-search-marks-script.js` | `watchSearch` returns at once | `H16_The_marks_come_back_after_a_redraw_and_go_on_Escape_a_click_outside_or_another_search` | 1 pass | **1 fail** | yes |
| close: closing the scenario leaves the marks | `report-search-marks-script.js` | the toggle listener's `clear()` becomes `void at` | `The_marks_go_when_the_scenario_is_closed` | 1 pass | **1 fail** | yes |
| back: Back that leaves the scenario leaves the marks | `report-search-marks-script.js` | `moved`: `clear()` becomes `void rec` | `The_marks_go_when_Back_leaves_the_scenario` | 1 pass | **1 fail** | yes |
| box-scroll: the scroll box is not brought to the words | `report-search-marks-script.js` | `draw`: `scrollBoxTo(inBox, target)` becomes `void inBox` | `A_match_in_a_long_note_s_scroll_box_is_marked_in_the_box_and_the_box_scrolled_to_it` | 1 pass | **1 fail** | yes |
| under-box: the rows a scroll box lies over, hidden by the sheet, are read | `report-search-marks-script.js` | `shownText`: `cs.visibility !== 'hidden' &&` taken out | `A_match_in_a_long_note_s_scroll_box_is_marked_in_the_box_and_the_box_scrolled_to_it` | 1 pass | **1 fail** | yes |
| sized: a picture not laid out is marked at once, on nothing | `report-search-marks-script.js` | `draw`: `sized = svgs` | `A_place_not_laid_out_is_marked_once_it_is` | 1 pass | **1 fail** | yes |
| wait: a picture not laid out is not waited for | `report-search-marks-script.js` | `draw`: the `waitForSize` line taken out | `A_place_not_laid_out_is_marked_once_it_is` | 1 pass | **1 fail** | yes |
| copy: a copy keeps the marks (lookClone's strip leaves them) | `context-menu-script.js` | `withoutScrollBoxes`: `.kron-mark-svg` taken out of the selector | `H32_Copies_of_a_marked_diagram_carry_no_marks` | 1 pass | **1 fail** | yes |
| export: an export keeps the marks | `report-export-function.js` | `export_undrawn`: the two strips taken out | `An_export_taken_while_marks_are_on_carries_none` | 1 pass | **1 fail** | yes |

All 27 killed, and every script put back. An earlier sweep, on the code before the whole diagram and the label found
again by its words came in (18 mutations), killed 17. The one it did not, the rows a scroll box lies over read
(`shownText`'s `note-scroll-under` test taken out), changed nothing: the sheet hides those rows
(`collapsible-notes-styles.css:200`), and the computed visibility test after it left them out anyway. The class test
went; the row above now takes out the visibility test, which nothing else backs, and the box's fact catches it.
