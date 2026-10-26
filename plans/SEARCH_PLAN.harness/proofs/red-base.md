# Red on the base (SEARCH_PLAN 6.4)

Every new fact run before its code, on `feat/github-search` at `c10ed743` (5.9.0's code: only plan commits follow
`37428293`), unless a line says otherwise. One line per red fact: the fact, then the line that failed. Guards are listed
as passing. The facts the parallel branches wrote are in `red-base-b8-b17.md` and `red-base-s3.md`.

## S1

- B14 `OptionDocsTests.NotePayloadFormat_calls_the_option_s_default_the_default`: `Assert.Contains() Failure: Sub-string
  not found` (OptionDocsTests.cs:51; the Yaml member's summary does not call itself the default).
- B14 `OptionDocsTests.A_member_that_calls_itself_the_default_is_the_option_s_default(NotePayloadFormat, Json, Yaml)`:
  "NotePayloadFormat: Json calls itself the default, but the option starts at Yaml" (OptionDocsTests.cs:60).
- B11 `SearchDataAttributeTests.A_linked_call_s_path_is_in_data_search`: `Assert.Contains() Failure: Item not found in
  set` (the linked label `[[#iflow-ID GET: /api/orders/search]]` gave no path).
- B11 `SearchDataAttributeTests.A_chunked_path_is_read_whole(False)` and `(True)`: `Item not found in set` (unlinked, the
  path read to its first chunk with the break on its end; linked, no path at all). The URL is 130 characters, so it is
  chunked at 100 and stays under the linked label's 350-character cap.
- B5 `SearchResultsTests.Emptying_the_search_unmarks_its_outline_rows`: the wait for no `tr.row-search-match` timed out
  (SearchResultsTests.cs:72).
- B5 `SearchResultsTests.Emptying_the_search_puts_back_the_row_it_chose`: the wait for row 0 to be active again timed out
  (:88; row 2, which the search chose, stayed).
- B5 `SearchResultsTests.Emptying_the_search_keeps_a_row_the_reader_chose`: the wait for no marks timed out (:105); its
  last line, the reader's row kept, is a guard of the fix.

## S6 (the filters), E2E `FilterBoxTests`, red on the base

- F-a `The_box_is_hidden_when_the_page_opens`: `Assert.False(Shows(#filtering-box))` failed (:86): the box shows.
- F-b `Filters_shows_at_rest_when_pinned_and_on_a_phone`: `Expect(Toggle).ToBeVisibleAsync()` failed at rest (:96).
- F-c `Filters_opens_the_box_at_the_bar_s_top_with_every_row_showing(1400)` and `(390)`: Filters not clickable at rest /
  on a phone (:115, 30 s).
- F-e `Filters_counts_the_filters_on_and_says_so_in_its_name`: no `aria-label="Filters"` (:133).
- F-f `Escape_and_the_close_button_close_the_box_and_focus_Filters`: Filters not clickable at rest (:151).
- F-g `The_box_stays_open_when_the_bar_comes_to_rest`: the box is put back (:179).
- F-h `Opening_and_closing_keep_what_was_being_read_in_place(False)`: Filters not clickable at rest (:193).
  `(True)`, pinned: passes, the guard the plan names.
- F-i `A_pinned_box_takes_at_most_60_percent_of_the_window_and_scrolls_inside`: red, not the guard the plan expected: on
  the base a box taken into the bar keeps its rows folded, so it is never tall enough to scroll inside. The cap's code is
  kept, with its height made the box's whole height (it set the content box's, so the bar ran past 60% by the box's
  padding, found by this fact: 341 px in a 480 px window, the cap's 180 px floor aside).
- F-j `A_page_with_only_its_features_has_the_bar_with_Features_and_Filters`: no tabs (:221).
- F-k `The_filters_are_announced_with_the_box_closed_open_and_the_bar_short`: no `.nav-status[role=status]` (:233).
- F-l `A_link_with_filters_opens_with_the_box_closed_and_Filters_counting_them`: the box shows (`Assert.False`).
- F-m `The_rows_lay_out_by_measure_the_first_time_the_box_opens`: Filters not clickable at rest (:272).
- F-o `The_box_s_exports_export_what_they_did`: the status button is folded away (:284); the export itself is the guard.
- F-p `The_box_shows_the_applied_search_and_its_clear_clears_only_the_search`: no `.box-search` (:300).

## S2 (the button and the builder), run red in a base worktree (`C:/Code/Kronikol-search-base`, detached at `c10ed743`)
with the new test files copied in, the code having been written first in the feature worktree

- Unit `SearchButtonMarkupTests`, 8 of 8 red: T1 `The_title_line_holds_the_title_then_the_search_then_the_theme_switch`
  (True, False: "Collections differ at index 1", the line is `h1 + button.theme-toggle`); T5
  `The_search_button_names_its_key_and_says_it_opens_a_dialog` (no `#search-button`);
  `The_applied_query_is_a_hidden_field_and_the_box_has_no_search_line` (`#searchbar` has no type); `The_builder_is_a_
  labelled_dialog_with_a_combobox_over_a_listbox` (no dialog); B3 `The_syntax_tips_say_what_the_instant_search_reads`
  (no tips in a dialog); `The_search_script_is_in_the_page_after_the_navigation_script`; `The_search_markup_is_written_once`
  ("The collection was empty").

## S2: the search button and the builder (run on c10ed743 with the new test files copied in, 2026-10-26)

Unit `SearchButtonMarkupTests`: 8 of 8 failed on the base.

- `SearchButtonMarkupTests.The_search_button_names_its_key_and_says_it_opens_a_dialog`: Failed Kronikol.Tests.Reports.SearchButtonMarkupTests.The_applied_query_is_a_hidden_field_and_the_box_has_no_search_line [425 ms]
- `SearchButtonMarkupTests.The_applied_query_is_a_hidden_field_and_the_box_has_no_search_line`: Assert.Equal() Failure: Strings differ
- `SearchButtonMarkupTests.The_title_line_holds_the_title_then_the_search_then_the_theme_switch(includeTestRunData:`: Assert.Equal() Failure: Collections differ at index 1
- `SearchButtonMarkupTests.The_title_line_holds_the_title_then_the_search_then_the_theme_switch(includeTestRunData:`: Assert.Equal() Failure: Collections differ at index 1
- `SearchButtonMarkupTests.The_builder_is_a_labelled_dialog_with_a_combobox_over_a_listbox`: Failed Kronikol.Tests.Reports.SearchButtonMarkupTests.The_syntax_tips_say_what_the_instant_search_reads [383 ms]
- `SearchButtonMarkupTests.The_syntax_tips_say_what_the_instant_search_reads`: Failed Kronikol.Tests.Reports.SearchButtonMarkupTests.The_search_script_is_in_the_page_after_the_navigation_script [369 ms]
- `SearchButtonMarkupTests.The_search_script_is_in_the_page_after_the_navigation_script`: Failed Kronikol.Tests.Reports.SearchButtonMarkupTests.The_search_markup_is_written_once [419 ms]
- `SearchButtonMarkupTests.The_search_markup_is_written_once`: Assert.Single() Failure: The collection was empty

E2E `SearchButtonTests` and `SearchBuilderTests`: 43 of 44 failed on the base. The one that passed,
`SearchBuilderTests.Enter_runs_one_search`, is a guard written to run on both: it types into the box field where the page
has no search button, and pins that Enter runs the search once on the base as on the builder.

- `SearchButtonTests.The_search_box_is_192_to_384_px_wide_from_768_02_px_and_the_switch_s_square_below(width:`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchButtonTests.The_search_box_is_192_to_384_px_wide_from_768_02_px_and_the_switch_s_square_below(width:`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchButtonTests.The_search_box_is_192_to_384_px_wide_from_768_02_px_and_the_switch_s_square_below(width:`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchButtonTests.The_search_box_is_192_to_384_px_wide_from_768_02_px_and_the_switch_s_square_below(width:`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchButtonTests.The_search_box_is_192_to_384_px_wide_from_768_02_px_and_the_switch_s_square_below(width:`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchButtonTests.The_search_box_is_192_to_384_px_wide_from_768_02_px_and_the_switch_s_square_below(width:`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchButtonTests.The_search_box_has_the_switch_s_height_edge_corners_ground_and_shadow(theme:`: Microsoft.Playwright.PlaywrightException : TypeError: Failed to execute 'getComputedStyle' on 'Window': parameter 1 is not of type 'Element'.
- `SearchButtonTests.The_search_box_has_the_switch_s_height_edge_corners_ground_and_shadow(theme:`: Microsoft.Playwright.PlaywrightException : TypeError: Failed to execute 'getComputedStyle' on 'Window': parameter 1 is not of type 'Element'.
- `SearchButtonTests.The_search_box_and_the_switch_share_their_top_and_bottom_at_every_width`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchButtonTests.An_applied_query_shows_in_the_search_box_with_its_values_coloured`: Microsoft.Playwright.PlaywrightException : Locator expected to be visible
- `SearchButtonTests.Clear_empties_the_query_and_nothing_else_in_one_entry`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.A_query_put_in_without_a_keyup_is_read_without_leaving_the_field`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.Enter_with_no_active_row_runs_the_search_and_opens_the_Search_tab_in_one_entry`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.The_builder_s_input_lands_on_the_search_box_and_is_a_top_sheet_on_a_phone(width:`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.The_builder_s_input_lands_on_the_search_box_and_is_a_top_sheet_on_a_phone(width:`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.The_builder_s_input_lands_on_the_search_box_and_is_a_top_sheet_on_a_phone(width:`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.The_builder_s_input_lands_on_the_search_box_and_is_a_top_sheet_on_a_phone(width:`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.The_builder_s_input_lands_on_the_search_box_and_is_a_top_sheet_on_a_phone(width:`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.The_builder_s_input_lands_on_the_search_box_and_is_a_top_sheet_on_a_phone(width:`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.The_live_region_counts_the_suggestions`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.Clicking_the_search_box_opens_the_builder_with_the_focus_in_its_input`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.After_status_colon_the_statuses_show_with_their_counts`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.The_builder_opens_holding_the_applied_query_selected`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.Under_reduced_motion_the_builder_does_not_move`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.The_page_behind_the_builder_is_inert_and_a_click_outside_closes_it`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.Keys_in_the_builder_never_reach_the_page`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.A_draft_never_reaches_the_address`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.Slash_opens_the_builder_from_a_tab_and_a_scenario_but_not_from_a_field`: Microsoft.Playwright.PlaywrightException : TypeError: Cannot read properties of null (reading 'open')
- `SearchBuilderTests.Jump_to_a_scenario_only_the_query_hides_clears_the_query_and_says_so`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.Slash_does_nothing_under_a_lightbox`: Assert.False() Failure
- `SearchBuilderTests.The_builder_is_a_layer_Back_closes_it_and_a_search_takes_its_entry`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.The_filter_row_applies_the_query_and_stays_on_the_tab(tab:`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.The_filter_row_applies_the_query_and_stays_on_the_tab(tab:`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.Composed_text_stays_visible_while_an_input_method_composes`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.Jumping_opens_the_scenario_and_leaves_the_applied_query`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.Recent_lists_this_page_s_searches_and_nothing_is_stored`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.A_partial_key_offers_the_key_and_Tab_completes_it`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.Down_activates_the_first_row_and_the_focus_stays_in_the_input`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.After_duration_colon_this_report_s_percentiles_show_with_their_times`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.The_syntax_tips_open_in_the_builder_and_typing_brings_the_suggestions_back`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.Escape_closes_the_builder_drops_the_draft_and_focuses_the_search_box`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.Typing_lists_scenarios_and_features_to_jump_to`: System.TimeoutException : Timeout 30000ms exceeded.
- `SearchBuilderTests.After_tag_colon_the_tags_show_with_their_chips_counts`: System.TimeoutException : Timeout 30000ms exceeded.

## W1 and W2: the parts of a scenario's search text (2026-10-26)

W1 pins every `data-search` and `data-row-search` of the rich fixture (`ToggleDefaultsBaselineTests.BuildRichFixture`,
both page shapes, 30 values in page order). Run on c10ed743 (5.9.0's code) and on the branch after B11 and W2, the 30
values were the same line for line, so the pin, `69078e15…f3c673`, is 5.9.0's: B11's linked-call paths do not occur in
the fixture, and W2 adds `data-search-kinds` beside `data-search` without moving it.

W2 on c10ed743, with `SearchKindsTests.cs` copied in:

- `SearchKindsTests.data_search_kinds_says_where_each_part_starts`: failed (no `data-search-kinds` attribute)
- `SearchKindsTests.An_outline_group_s_kinds_say_where_each_part_starts`: failed (no `data-search-kinds` attribute)

## S4: the Search tab (run on c10ed743 with the new test files copied in, 2026-10-26)

32 of 33 failed on the base. Each Search tab fact of SearchTabTests.cs (R1 to R32 as written there) failed opening its
page, at the wait for window.kronikol_search_results: there is no Search tab, the plan's red reason. The three B5
facts of SearchResultsTests failed in FillSearchBar, as recorded for B5. ReportTabsTests.Slash_on_a_tab_goes_to_the_search
failed (the slash focuses the box's field, there is no builder). R33, Right_from_the_last_shown_tab_skips_the_hidden_Search_tab,
passed: a tab that is not there reads as hidden to Playwright. It now counts the tab first (1), which the base fails.

    ReportTabsTests            Slash_on_a_tab_goes_to_the_search                          ReportTabsTests.cs:99                Microsoft.Playwright.PlaywrightException : Locator expected to be focused
    SearchResultsTests         A_Specifications_page_searches_as_TestRunReport_does       SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         A_matched_in_place_keeps_its_results_and_writes_in         SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         A_reload_of_tab_search_with_q_shows_the_results_once_filte SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         A_result_shows_its_feature_its_time_its_tags_and_up_to_thr SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         A_status_row_presses_the_box_s_status_button_and_the_box_s SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         An_advanced_row_opens_the_builder_at_its_values            SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         An_unreadable_qualifier_is_warned_about_and_the_search_sti SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         Back_from_a_result_returns_to_its_page_type_sort_and_scrol SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         Best_match_puts_a_scenario_named_after_the_word_first      SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         Clearing_on_the_Search_tab_returns_to_the_tab_the_search_w SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         Down_and_Up_on_the_Search_tab_scroll_and_stay_on_it        SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         Emptying_the_search_keeps_a_row_the_reader_chose           PlaywrightTestBase.cs:368            System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         Emptying_the_search_puts_back_the_row_it_chose             PlaywrightTestBase.cs:368            System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         Emptying_the_search_unmarks_its_outline_rows               PlaywrightTestBase.cs:368            System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         Exclude_sets_the_chip_to_exclude                           SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         Features_lists_the_features_holding_a_result_and_writes_ty SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         Longest_first_orders_by_own_time_and_writes_s_and_o        SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         Matched_in_counts_where_each_result_matched                SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         More_tags_shows_the_rest_in_place                          SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         Nothing_found_names_the_type_and_the_types_that_did_match  SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         Show_lists_Scenarios_and_Features_with_their_counts_zeros_ SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         Show_more_lists_up_to_twenty_and_Show_less_folds_them      SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         Ten_results_a_page_and_each_page_writes_p                  SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         The_Search_tab_shows_while_a_query_is_applied_and_goes_wit SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         The_box_s_filters_narrow_the_results_and_the_note_counts_w SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         The_current_type_has_the_accent_bar_and_aria_current       SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         The_focus_goes_to_the_count_after_a_search_a_type_and_a_pa SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         The_header_shows_the_count_and_the_time                    SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         The_kebab_exports_exactly_the_results                      SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         The_sidebar_and_the_box_never_disagree                     SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.
    SearchResultsTests         tab_search_with_no_q_opens_the_Features_tab                SearchTabTests.cs:19                 System.TimeoutException : Timeout 30000ms exceeded.

## S5, the way to a match (HTML places) and the facts written after the 5.9.1 merge (2026-10-26)

Run on the base worktree (c10ed743, 5.9.0) with the new test files copied over (`red-s5-e2e.log` in the session's
scratchpad): **15 of 15 failed**, none passed.

| Fact | Failure on 5.9.0 |
|---|---|
| `SearchHitWayTests` H1 to H5 and R30 (6) | no `window.kronikol_search_results` or `kronikol_hit`: the page never gets past opening (30 s timeout) |
| `BackForwardSearchTests` H19, H20 (2) | the same: no results script, no way to a match |
| `SearchResultsPhoneTests` R24, R29 (2) | no Search tab: the results script never loads |
| `SearchBuilderEngineTests` QB22, Firefox and WebKit (2) | no query builder (`#search-button` missing) |
| `SearchBuilderTests.The_draft_is_coloured_and_an_unreadable_qualifier_turns_the_field_amber` | no builder |
| `SearchBuilderTests.Slash_is_left_to_the_browser_on_an_exported_page` (B7) | `/` is swallowed on an exported page (`Assert.False`: the key's default was prevented) |
| `MobileResponsiveTests.On_a_phone_the_way_to_search_stays_in_reach_down_the_page` (B15) | no floating search button down the page |

`SearchBudgetTests` (SearchOpenMs, the keystroke, SearchResultsMs, HitOpenMs) is a guard: on 5.9.0 there is no builder to
time, so it cannot be red in the usual sense; its first runs on this branch failed on three of its four budgets, which
led to the speed-ups recorded in the changelog draft (the filter controls looked for inside the box, the builder reading
the page once an opening, a tab change measuring itself in the next frame).

## S5, the way to a match in a diagram (2026-10-26, after the agents' merges)

The base for these facts is the branch as the three agents' work was merged (`afa97fea`): the diagram API, the worker's
lines and the marks there, the Search tab showing only the instant search's lines, and the way to a diagram's match
still on the API's planned names. Run with `SearchHitTests.Diagrams.cs` and the extended `DiagramApiReports.cs` copied
over (`red-s5-diag.log` in the session's scratchpad): **20 of the 22 new facts failed**, each on its way never reaching
"Match in" (30 s): no deep line to follow, or the old way stopping short.

| Fact | On `afa97fea` |
|---|---|
| H6, H7 (with H17), H8 (Events, Assertions, Steps, Headers), H9, H10, H11, H13, H14, H18, H21, H22, H23, H29, H30, H33, H34, H35 | red: no DIAGRAM or FLOW line from the deep search, or the way never ends |
| H12 (a word past a long path's cut) | green: the instant search's DIAGRAM line, from the call's path in `data-search` (B11), already reaches the drawn word |
| H31 (a participant's name) | green: the same, from the participant in `data-search`; its mutation (a declaration taken for a message not drawn) is red |

H8's Setup case was added after this run; its mutation (H7, `hiddenBy` finding nothing) is red with the rest of the
theory.
