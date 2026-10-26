# S7 mutations (SEARCH_PLAN 6.4)

Run 2026-10-26 on `feat/github-search` by `mutate-s7.js` beside this file (`node mutate-s7.js <worktree> [id ...]`), in
a second worktree of the branch (`C:/Code/Kronikol-search-base`), so that the branch's own worktree stayed free for its
suites. Each mutation replaces one exact line or expression of a shipped script, the stylesheet or `ReportGenerator.cs`,
builds the project holding the facts it should break (`tests/Kronikol.Tests.EndToEnd` in Playwright's bundled Chromium,
`tests/Kronikol.Tests` or `tests/Kronikol.Tests.SearchEngine`) and runs those facts; the file is then written back from
the bytes it was read from and its SHA-256 compared. A mutation counts only when its facts pass unmutated first.

Three runs:

- **Run 1**, on `fca56ac9`, took 43 mutations: 36 failed their facts. Of the seven left, one is equivalent (QB16), one
  did not count, its fact failing unmutated on a loaded machine (H22), and five changed nothing the facts could see,
  which showed a fact too weak (QB20) or a mutation aimed at a line that was not the only one doing the work (F-c, R1,
  H2, H35).
- **Run 2**, on `e716a138`, took those seven again, reworked as set out below, with the three mutations the wiki pass
  added (TIPS, CA, NOT) and seven for the defects found by checking the wiki against the code. Each failed its facts
  but QB16 and H2, whose mutated run froze the page and was stopped (below).
- **Run 3**, on `5a79c178`, took H2 again after the fix that the freeze showed, and TRIED, the mutation for that fix.

A row shows the last run that took its mutation. Of the 54, 53 fail their facts; the one that does not is QB16, which is equivalent.

| Mutation | File | Facts | Unmutated | Mutated | Restored |
|---|---|---|---|---|---|
| B1: Enter runs the search twice, the second 300 ms later (run 1) | `report-search-ui-script.js` | `SearchBuilderTests.Enter_runs_one_search` | 1 pass | **1 fail** | yes |
| B2: no input listener: a query put in without a key waits (run 1) | `report-search-ui-script.js` | `SearchBuilderTests.A_query_put_in_without_a_keyup_is_read_without_leaving_the_field` | 1 pass | **1 fail** | yes |
| QB16: the builder does not stop its keys (run 2) | `report-search-ui-script.js` | `SearchBuilderTests.Keys_in_the_builder_never_reach_the_page` | 1 pass | 1 pass (survived) | yes |
| QB20: the draft is written to #searchbar as it is typed (run 2) | `report-search-ui-script.js` | `SearchBuilderTests.A_draft_never_reaches_the_address` | 1 pass | **1 fail** | yes |
| B7: / prevents its default before it knows the search can open (run 1) | `report-keyboard-navigation-function.js` | `SearchBuilderTests.Slash_is_left_to_the_browser_on_an_exported_page`, `SearchBuilderTests.Slash_does_nothing_under_a_lightbox` | 2 pass | **2 fail** | yes |
| F-a: the box is written without hidden (run 1) | `ReportGenerator.cs` | `FilterBoxTests.The_box_is_hidden_when_the_page_opens` | 1 pass | **1 fail** | yes |
| F-b: Filters is hidden while the bar is at rest (run 1) | `stylesheets.css` | `FilterBoxTests.Filters_shows_at_rest_when_pinned_and_on_a_phone` | 1 pass | **1 fail** | yes |
| F-c: the rows past the first folded away, as More filters left them (rows-folded is gone since S2) (run 2) | `stylesheets.css` | `FilterBoxTests.Filters_opens_the_box_at_the_bar_s_top_with_every_row_showing` | 2 pass | **2 fail** | yes |
| F-e: Filters' count is read from the box's readout, not its controls (run 1) | `report-filter-bar-script.js` | `FilterBoxTests.Filters_counts_the_filters_on_and_says_so_in_its_name` | 1 pass | **1 fail** | yes |
| F-g: the bar shuts the box as it comes to rest (run 1) | `report-filter-bar-script.js` | `FilterBoxTests.The_box_stays_open_when_the_bar_comes_to_rest` | 1 pass | **1 fail** | yes |
| F-i: a pinned box is not capped (run 1) | `report-filter-bar-script.js` | `FilterBoxTests.A_pinned_box_takes_at_most_60_percent_of_the_window_and_scrolls_inside` | 1 pass | **1 fail** | yes |
| F-k: the live region is left in the box's readout (run 1) | `ReportGenerator.cs` | `FilterBoxTests.The_filters_are_announced_with_the_box_closed_open_and_the_bar_short` | 1 pass | **1 fail** | yes |
| R1: the Search tab is left showing after Clear (run 2) | `report-search-results-script.js` | `SearchResultsTests.The_Search_tab_shows_while_a_query_is_applied_and_goes_with_it` | 1 pass | **1 fail** | yes |
| R8: Exclude sets its chip to include (run 1) | `report-search-results-script.js` | `SearchResultsTests.Exclude_sets_the_chip_to_exclude` | 1 pass | **1 fail** | yes |
| R13: Best match ignores names (run 1) | `report-search-results-script.js` | `SearchResultsTests.Best_match_puts_a_scenario_named_after_the_word_first` | 1 pass | **1 fail** | yes |
| R14: p= is not written (run 1) | `report-navigation-script.js` | `SearchResultsTests.Ten_results_a_page_and_each_page_writes_p` | 1 pass | **1 fail** | yes |
| R22: the type counts come from the instant pass only (deep results are merged at once) (run 1) | `report-search-results-script.js` | `DeepSearchTests.Deep_results_join` | 1 pass | **1 fail** | yes |
| R23: the tabs script's arrows do not pass over the Search tab (run 1) | `report-tabs-script.js` | `SearchResultsTests.Down_and_Up_on_the_Search_tab_scroll_and_stay_on_it` | 1 pass | **1 fail** | yes |
| R26: the focus is not moved to the count (run 1) | `report-search-results-script.js` | `SearchResultsTests.The_focus_goes_to_the_count_after_a_search_a_type_and_a_page_and_stays_on_sort_after_a_sort` | 1 pass | **1 fail** | yes |
| B5: the empty path does not unmark the rows the search marked (run 1) | `report-search-function.js` | `SearchResultsTests.Emptying_the_search_unmarks_its_outline_rows` | 1 pass | **1 fail** | yes |
| H2: the way does not open the composite steps on its way (opening one draws its held sub-steps) (run 3) | `report-search-hit-script.js` | `SearchHitWayTests.A_sub_step_line_opens_each_composite_step_and_draws_held_sub_steps_first` | 1 pass | **1 fail** | yes |
| H19-H20: hit= is not read as the match (it falls among the filters) (run 1) | `report-navigation-script.js` | `BackForwardSearchTests` | 2 pass | **2 fail** | yes |
| W2: the kinds offsets one off (run 1) | `ReportGenerator.cs` | `SearchKindsTests` | 3 pass | **2 fail**, 1 pass | yes |
| E3: a scenario skipped after a failure is not skipped for $skipped (5.9.1's rule, which B4 follows) (run 1) | `advanced-search.js` | `SearchQueryFormsTests.Status_skipped_finds_a_scenario_skipped_after_a_failure` | 7 pass | **2 fail**, 5 pass | yes |
| W7: no deep lines asked for the results shown (run 1) | `report-search-results-script.js` | `SearchHitTests.A_body_line_draws_the_diagram_expands_the_note_and_marks_the_words` | 1 pass | **1 fail** | yes |
| R-cut: a line with no spaces is widened to its whole length (run 1) | `report-search-results-script.js` | `SearchHitWayTests.A_line_with_no_spaces_is_cut_to_160_around_the_match` | 1 pass | **1 fail** | yes |
| H7: hiddenBy finds nothing a toggle hides (run 1) | `collapsible-notes-script.js` | `SearchHitTests.A_database_call_line_turns_Databases_on_for_that_scenario_only`, `SearchHitTests.Each_toggle_that_hides_a_match_is_turned_on` | 6 pass | **5 fail**, 1 pass | yes |
| H6: a note without the words is left as it is (run 1) | `report-search-hit-script.js` | `SearchHitTests.A_body_line_draws_the_diagram_expands_the_note_and_marks_the_words` | 1 pass | **1 fail** | yes |
| H8h: a header line never turns Headers on (run 1) | `report-search-hit-script.js` | `SearchHitTests.Each_toggle_that_hides_a_match_is_turned_on` | 5 pass | **1 fail**, 4 pass | yes |
| H10: the note is never said to be shown as JSON (run 1) | `report-search-hit-script.js` | `SearchHitTests.A_JSON_phrase_shows_its_note_as_JSON_and_a_word_leaves_it_in_YAML` | 1 pass | **1 fail** | yes |
| H14: a picture with no text is not taken to its Raw PlantUML (run 1) | `report-search-hit-script.js` | `SearchHitTests.A_server_drawn_diagram_s_line_opens_Raw_PlantUML_and_marks_the_words` | 1 pass | **1 fail** | yes |
| H13: the flow does not choose its view (run 1) | `report-search-hit-script.js` | `SearchHitTests.A_flow_line_opens_the_Flame_view_at_its_bar` | 1 pass | **1 fail** | yes |
| H33: a diagram in a hidden view is not shown (run 1) | `report-search-hit-script.js` | `SearchHitTests.An_Activity_view_line_opens_the_view_and_marks_its_text` | 1 pass | **1 fail** | yes |
| H29: an outline row's diagram is left unchosen (run 1) | `report-search-hit-script.js` | `SearchHitTests.A_payload_line_from_an_outline_row_chooses_that_row_and_its_diagram` | 1 pass | **1 fail** | yes |
| H21: words the diagram does not draw are not said (run 1) | `report-search-hit-script.js` | `SearchHitTests.A_match_the_diagram_does_not_draw_goes_to_the_nearest_place_and_says_why` | 1 pass | **1 fail** | yes |
| H22: the reader's keys and scrolls do not stop the way (run 2) | `report-search-hit-script.js` | `SearchHitTests.A_scroll_or_a_key_from_the_reader_stops_the_way_there` | 1 pass | **1 fail** | yes |
| H35: an arrival (Back) does not stop the way (run 2) | `report-navigation-script.js` | `SearchHitTests.Back_while_the_way_there_runs_stops_it` | 1 pass | **1 fail** | yes |
| H23: words under 10 px are not zoomed (run 1) | `report-search-hit-script.js` | `SearchHitTests.Words_drawn_under_10_px_are_zoomed_to_be_read` | 1 pass | **1 fail** | yes |
| H17: nothing is said under the view tabs (run 1) | `report-search-hit-script.js` | `SearchHitTests.A_database_call_line_turns_Databases_on_for_that_scenario_only`, `SearchHitTests.A_body_line_draws_the_diagram_expands_the_note_and_marks_the_words` | 2 pass | **2 fail** | yes |
| H31: a declaration is taken for a message not drawn (run 1) | `report-search-hit-script.js` | `SearchHitTests.A_participant_or_loop_label_line_marks_it_in_the_diagram` | 1 pass | **1 fail** | yes |
| H-name: the place is not named after its call (run 1) | `report-search-hit-script.js` | `SearchHitTests.A_body_line_draws_the_diagram_expands_the_note_and_marks_the_words`, `SearchHitTests.A_database_call_line_turns_Databases_on_for_that_scenario_only` | 2 pass | **2 fail** | yes |
| M-bar: a click on the bar's own controls takes the marks off (run 1) | `report-search-marks-script.js` | `SearchHitWayTests.Escape_in_the_filter_box_closes_it_and_leaves_the_marks` | 1 pass | **1 fail** | yes |
| M-hide: a filter that hides the scenario leaves the marks (run 1) | `report-search-marks-script.js` | `SearchHitWayTests.A_filter_that_hides_the_marked_scenario_takes_the_marks_off` | 1 pass | **1 fail** | yes |
| TIPS: the tips count a scenario skipped after a failure as failed (run 2) | `ReportGenerator.cs` | `SearchButtonMarkupTests.The_syntax_tips_count_a_scenario_skipped_after_a_failure_as_skipped` | 1 pass | **1 fail** | yes |
| CA: Clear All's title leaves out the new qualifiers (run 2) | `ReportGenerator.cs` | `ControlTitlesTests`, `FilterBarTests`, `FilterRowsTests` | 45 pass | **4 fail**, 41 pass | yes |
| NOT: `not` before a word negates it in the lines (run 2) | `report-search-results-script.js` | `SearchHitWayTests.Not_before_a_word_is_a_word_the_lines_mark` | 1 pass | **1 fail** | yes |
| X-line: the export keeps the line saying what was turned on (run 2) | `report-export-function.js` | `SearchHitTests.An_export_taken_after_a_match_leaves_out_the_line_of_what_was_turned_on` | 1 pass | **1 fail** | yes |
| DESC: a DESCRIPTION line read from every scenario of the feature (run 2) | `report-search-results-script.js` | `SearchHitWayTests.A_description_line_is_the_result_s_own_description` | 1 pass | **1 fail** | yes |
| ENDP: an ENDPOINT line taken from the lower-cased search text (run 2) | `report-search-results-script.js` | `SearchHitWayTests.An_endpoint_line_is_the_endpoint_as_written` | 1 pass | **1 fail** | yes |
| G-cut: an instant diagram line is its whole part of the search text (run 2) | `report-search-results-script.js` | `SearchHitTests.An_instant_diagram_line_is_cut_to_160_around_the_match` | 1 pass | **1 fail** | yes |
| OWN: a hit= naming another scenario's diagram is followed there (run 2) | `report-search-hit-script.js` | `SearchHitTests.A_hit_link_naming_another_scenario_s_diagram_opens_the_scenario_and_says_so` | 1 pass | **1 fail** | yes |
| W-quoted: the warning splits the unreadable qualifiers on spaces (run 2) | `report-search-results-script.js` | `SearchResultsTests.An_unreadable_qualifier_with_a_quoted_space_is_warned_about_whole` | 1 pass | **1 fail** | yes |
| W-group: a negated group's qualifier is given its value's reason (run 2) | `report-search-results-script.js` | `SearchResultsTests.A_qualifier_in_a_negated_group_is_warned_about_for_its_place` | 1 pass | **1 fail** | yes |
| TRIED: a held step that never draws is drawn again, for as long as the page is open (run 3) | `report-search-hit-script.js` | `SearchHitWayTests.A_held_step_that_never_draws_is_tried_once` | 1 pass | **1 fail** | yes |

## Run 1's survivors

- **QB16, equivalent.** Taking out the builder's `stopPropagation` changes nothing a reader can see: each key handler
  of the page returns early for a key typed in a field (the scenario arrows, `/`, Escape), and the builder's input is
  a field. The line stays, as a second guard, and the fact holds the behaviour whichever line gives it. Run 2 shows it
  surviving still.
- **QB20, a fact too weak.** The fact read only the address, and nothing writes the address from `#searchbar` until a
  search runs, so a draft copied into `#searchbar` as it was typed went unseen. The fact now reads `#searchbar` too,
  while the draft is typed.
- **F-c, a mutation that changed nothing.** Run 1 seeded `rows-folded` on the box, a class nothing has read since S2
  took More filters out. Run 2 folds the rows past the first with the stylesheet, as More filters left them.
- **R1, two lines doing one job.** Run 1 took out the results wrapper's hiding of the Search tab, but `paintTab` hid
  it too. The wrapper now only shows the tab, `paintTab` alone hides it, and run 2 mutates `paintTab`.
- **H2, a mutation that changed nothing.** Run 1 made the way's `drawn()` only open a composite step, and opening a
  step draws its held sub-steps by itself. Run 2 makes `drawn()` open nothing.
- **H22, did not count.** Its fact failed unmutated in run 1, on a machine loaded by a suite running beside the
  mutation builds. Run 2 took it again.
- **H35, two lines doing one job.** Run 1 took out the hit script's own `popstate` listener, but the navigation
  script's arrival stops the way too. The listener is gone, so one place stops the way on Back, and run 2 mutates the
  navigation script's line.

## Run 2: H2 froze the page

With `drawn()` opening nothing, the way's search for a step among the held sub-steps (`stepWith`) drew the first held
step, found it still held, and drew it again: a loop of promises that never let the page paint or answer, so the fact
could neither pass nor fail, and the run was stopped, twice (the first time its test host, left running, held the
worktree's assemblies, and the next build failed; `mutate-s7.js` now stops a run's leftover test processes and writes
each result as it is known). The loop was not the mutation's alone: whenever a held step could not be drawn, the way
drew it again every three seconds for as long as the page was open. The way now draws each held step once and, with
none left, says the match was not found (`SearchHitWayTests.A_held_step_that_never_draws_is_tried_once`, which failed
on the code before the fix, the way never ending, and passes after it). Run 3 took H2 again, and TRIED, which puts the
loop back.

## Added in run 2

- **TIPS, CA and NOT**, from the wiki pass: the tips' status row, Clear All's title, and `not` before a word in the
  lines' marks.
- **X-line, DESC, ENDP, G-cut, OWN, W-quoted and W-group**, from checking the wiki against the code: each puts back
  the defect its fix took out (the export kept the line saying what was turned on; a DESCRIPTION line was read from
  every scenario of the feature; an ENDPOINT line came from the lower-cased search text; an instant diagram line was
  its whole part of the search text; a `hit=` naming another scenario's diagram was followed there; the warning split
  the unreadable qualifiers on spaces; a negated group's qualifier was given its value's reason).
