# S5 worker side: red run on the base (SEARCH_PLAN 6.4)

Stage S5's worker side (4.6.1 to 4.6.5; fact rows W3 to W6, W9, H26 and H27), on branch `feat/github-search-worker`
from `5755f5dc`. Every new fact was written first and run red against that commit's scripts before either changed, in two
rounds as the work went (the plan's rows first, the edge facts after them). The table is the last run, made when the work
was done: the two scripts it changes (`report-search-index.js`, `collapsible-notes-script.js`) were put back as
`git show 5755f5dc:` has them under the finished test files, both test projects built, the touched classes run, and the
scripts restored (SHA-256 checked before and after: `0c38d68c…` and `18e7e479…`; the base's are `753408e4…` and
`5a9c9334…`). Jint `SearchSnippetTests`, `SearchIndexJintTests` and `VerdictDeepSearchTests`: 99 facts, 35 failing. Unit
`NoteCopyFidelityScriptTests`, `VerdictFencesTests` and `NoteAppearanceScriptTests`: 39 facts, 5 failing. Every failure
is a new or changed fact and every other fact passes. "Line" is the line of the test file that failed.

`SearchSnippetTests` (new) runs the worker as the page builds it: the functions of `buildWorker`'s roster joined into one
script, read out of the shipped file, with a stub `self`, `atob`, streams and `Response`, a KSI1 index whose one row
holds every scenario (so every scenario is a candidate and the corpus decides), and items as `collectItemMeta` posts them.
Its page check runs the main-thread half over a stub page and a stub `Worker` whose inbox can be held.

## Red, as each fact says

| Fact | Row | Class | Red rows | Line | Why it fails on the base |
|---|---|---|---|---|---|
| `Display_text_folds_to_the_search_text_for_every_vector` | W3 | Jint `SearchIndexJintTests` | 1 of 1 | `SearchIndexJintTests.cs:67` | no `kronNormalizeForDisplay` ("Can only invoke functions") |
| `Display_text_keeps_the_case_the_search_text_folds` | W3 | Jint `SearchIndexJintTests` | 6 of 6 | `SearchIndexJintTests.cs:81` | no function |
| `A_query_s_display_text_keeps_its_tildes_as_its_search_text_does` | W3 | Jint `SearchIndexJintTests` | 1 of 1 | `SearchIndexJintTests.cs:87` | no function |
| `Display_text_and_the_map_read_any_text_as_the_reference_does` | W3, H26 | Jint `SearchIndexJintTests` | 1 of 1 | `SearchIndexJintTests.cs:173` | `kronNormalizeForDisplay is not defined` |
| `kronNormalizeWithMap_is_the_search_text_and_maps_each_character_back` | H26 | Jint `SearchIndexJintTests` | 1 of 1 | `SearchIndexJintTests.cs:247` | `kronNormalizeWithMap is not defined` |
| `A_character_a_pass_keeps_maps_to_itself_and_the_end_of_the_text_to_the_end_of_the_input` | H26 | Jint `SearchIndexJintTests` | 1 of 1 | `SearchIndexJintTests.cs:255` | `kronMapBack is not defined` |
| `A_place_inside_what_a_pass_takes_out_moves_to_where_it_was_taken_out` | H26 | Jint `SearchIndexJintTests` | 1 of 1 | `SearchIndexJintTests.cs:140` | `kronCorpusPiece is not defined` |
| `A_piece_s_lines_and_letters_are_where_the_passes_put_them` | H26, H27 | Jint `SearchIndexJintTests` | 1 of 1 | `SearchIndexJintTests.cs:196` | `kronCorpusPiece is not defined` |
| `A_line_is_the_piece_s_line_cut_to_160_around_the_hit` | W4 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:488` | `kronLineAround is not defined` |
| `A_hit_at_either_end_of_a_long_line_is_cut_on_the_other_side_only` | W4 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:488` | no function |
| `A_line_with_no_spaces_is_cut_inside_its_word_and_its_own_spaces_are_left_out` | W4 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:488` | no function |
| `A_hit_longer_than_the_line_s_room_shows_its_start` | W4 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:488` | no function |
| `The_worker_reports_the_kinds_a_match_hit` | W5 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:124` | the result carries indices only: terms `[]` where `["flour"]` was asked |
| `Every_matches_batch_carries_the_kinds_of_its_matches` | W5 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:154` | a batch has no `kinds` |
| `The_kinds_of_the_data_search_follow_its_text_through_the_normaliser` | W5 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:309` | no kinds to read |
| `A_scenario_written_without_data_search_kinds_reads_as_its_name` | W5 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:319` | no kinds to read |
| `A_batch_goes_once_however_many_candidates_follow_it` | W5 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:298` | batches of `[100, 100, 150]`: the base posts the hundredth match's batch again for the candidate after it that does not match (a bug, below) |
| `The_worker_answers_snippets_for_the_items_asked_and_drops_stale_ones` | W6 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:578` | no `snippets` answer ("Sequence contains no matching element"): the base worker reads no such request |
| `A_scenario_whose_text_holds_no_hit_answers_no_lines` | W6 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:578` | no request |
| `Every_hit_on_a_line_is_marked_and_the_line_counts_once` | W6 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:578` | no request |
| `A_line_names_which_hit_of_its_place_it_shows_first` | W6 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:578` | no request |
| `A_long_line_s_marks_follow_its_cut` | W6 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:578` | no request |
| `The_page_asks_for_snippets_and_gets_them_with_their_elements` | W6 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:385` | `Property '_kronDeepKinds' of object is not a function` |
| `A_match_s_offset_names_its_note_hnote_arrow_or_partition` | H27 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:578` | no request |
| `A_note_s_place_counts_its_hits_and_a_line_outside_every_statement_names_none` | H27 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:578` | no request |
| `Each_diagram_of_a_scenario_is_named_by_its_element` | H27 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:578` | no request |
| `A_Raw_PlantUML_section_s_line_names_the_section` | H27 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:578` | no request |
| `The_statements_of_a_source_are_its_notes_by_the_page_s_rule_and_its_one_line_statements` | H27 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:268` | `kronDiagramStatements is not defined` |
| `The_note_blocks_are_read_by_one_rule_the_page_and_the_worker_share` | H27 | Jint `SearchSnippetTests` | 1 of 1 | `SearchSnippetTests.cs:280` | `kronParseNoteBlocks is not defined` |
| `The_worker_built_from_its_roster_reads_the_new_forms` (one line added) | W9 | Jint `VerdictDeepSearchTests` | 1 of 1 | `VerdictSearchTests.cs:154` | the roster holds no `kronPositiveTerms` |
| `The_worker_roster_holds_the_new_functions` | W9 | unit `NoteCopyFidelityScriptTests` | 1 of 1 | `NoteCopyFidelityScriptTests.cs:105` | the roster holds no `kronNormalizerPasses`, the first of the twelve |
| `The_search_worker_roster_carries_the_rejoin_helpers` (changed) | W9 | unit `NoteCopyFidelityScriptTests` | 1 of 1 | `NoteCopyFidelityScriptTests.cs:86` | the roster holds `kronEndsWithOwnMarker` and `kronRejoinMarkedBreaks`, no `kronNormalizerPasses`; rule 1b is now that list's pass |
| `The_page_and_the_worker_read_note_blocks_by_one_rule` | H27 | unit `NoteCopyFidelityScriptTests` | 1 of 1 | `NoteCopyFidelityScriptTests.cs:114` | no `function kronParseNoteBlocks(source, lines) {` in the search index script |
| `Every_note_header_matcher_allows_several_stereotypes` (changed) | H27 | unit `NoteAppearanceScriptTests` | 1 of 1 | `NoteAppearanceScriptTests.cs:41` | 4 note header matchers in the notes script where 3 are asked: the fourth moved to `kronParseNoteBlocks` |
| `Each_variant_is_the_bytes_it_was_before` (`report-search-index.js`) | pin | unit `VerdictFencesTests` | 1 of 4 | `VerdictFencesTests.cs:67` | the pin moved: the base's default variant is `19b3039e…` |

## Guards, green on the base

| Fact | Class | Rows |
|---|---|---|
| `The_shipped_normaliser_reads_any_text_as_the_reference_does` (new: 2000 seeded texts of the characters every pass acts on, through the shipped `kronNormalizeForSearch` and `tools/search-bench/normalize.js`) | Jint `SearchIndexJintTests` | 1 of 1 |
| `Shipped_normalization_matches_every_vector` and every other fact the touched Jint classes held before | Jint | 63 of 63 |
| every other fact the touched unit classes held before | unit | 34 of 34 |

The new guard is what lets the passes be rewritten as one list: rule 1b, a line-by-line function before
(`kronRejoinMarkedBreaks`), is now a regular expression in that list, and the guard reads the same texts through it and
through the reference, which keeps the line-by-line form. Passes 4 and 5a gained the `i` flag (they run after the fold,
so it changes nothing they match there) and pass 6 matches only the runs it changes (`[ \t]{2,}|\t`, where `[ \t]+`
also matched a lone space it wrote back as it was); the guard and the vectors hold all three.

## Green

The same classes on the finished scripts: Jint 99 of 99 (107 of 107 with the rest of `VerdictSearchTests.cs`), unit 39 of
39. Every unit class that reads either script (`DiagramContextMenuTests`, `EventsAndSetupToggleReportTests`,
`NoteFormatToggleScriptTests`, `PayloadScrollLinesTests`, `ScenarioTruncateAndCollapsibleNotesTests`,
`ToggleDefaultsMarkupTests`, `StepBarPlantUmlTests`, `CaptureFootnoteTests` and the three above): 306 of 306, once
`NoteAppearanceScriptTests` counted the note header matcher where it moved; it was the one fact of theirs the move broke.
No other test project reads either script outside a browser.

E2E (bundled Chromium), the classes that read note blocks through `window._parseNoteBlocks` or the note menu
(`NoteButtonIndexTests`, `NoteContextMenuIndexTests`, `DiagramNoteBasicTests`, `DiagramNoteSplitTests`,
`ContinuationNoteInteractivityTests`, `DiagramNoteMixedParticipantTests`, `ThreeFragmentContinuationTests`,
`NoteCopyFidelityTests`), the deep search and the Search tab (`DeepSearchTests`, `CompressedReportTests`,
`SearchTabTests`, `SearchResultsTests`) and `BrowserRenderWorkerTests.Large_report_renders_off_the_main_thread_within_budget`:
156 of 160. The four that fail fail the same way with the base's two scripts put back (run the same way, scripts restored
and checked), so they are the branch's, not this work's:

| Fact | On this work and on the base |
|---|---|
| `DeepSearchTests.Deep_results_join_after_the_instant_ones_of_their_strength_while_the_bar_runs` (R22) | first result "Create order successfully" where "List orders quietly" is asked |
| `SearchResultsTests.The_kebab_exports_exactly_the_results` (R21) | the HTML export holds 6 scenarios where 2 are asked (its CSV export holds the 2) |
| `CompressedReportTests.The_export_of_a_compressed_report_is_compressed_and_opens_with_its_diagrams_and_held_steps` | no download within 30 s |
| `CompressedReportTests.Where_the_browser_cannot_compress_the_export_stores_its_page_frame_and_still_opens` | no download within 30 s |

## A bug found on the way

The worker posted a batch as each hundredth match joined, from a check that ran for every candidate after the match too:
the hundredth match's batch went again for each candidate after it that did not match (the base posts `[100, 100, 150]`
for 150 matches among 300 scenarios). The page appends what a batch brings, so the deep results it shows and counts held
those matches twice until the run ended. `A_batch_goes_once_however_many_candidates_follow_it` holds it.

## Mutations

Each mutation replaced one exact text of the finished `report-search-index.js` once, the Jint classes were built and run,
and the script was restored (its SHA-256 checked at the end). 20 of 22 were killed.

| # | Mutation | Result | Facts failing (the first four) |
|---|---|---|---|
| 1 | a place inside what a pass takes out keeps its number | killed | `A_place_inside_what_a_pass_takes_out_moves_to_where_it_was_taken_out` |
| 2 | the display text is the folded text | killed | `A_piece_s_lines_and_letters_are_where_the_passes_put_them`, `A_query_s_display_text_keeps_its_tildes_as_its_search_text_does`, `Display_text_keeps_the_case_the_search_text_folds` |
| 3 | what a match writes back maps as a whole, the part it keeps included | killed | `A_character_a_pass_keeps_maps_to_itself_and_the_end_of_the_text_to_the_end_of_the_input`, `kronNormalizeWithMap_is_the_search_text_and_maps_each_character_back` |
| 4 | the kinds of `data-search` are not read | killed | `A_scenario_written_without_data_search_kinds_reads_as_its_name`, `Every_matches_batch_carries_the_kinds_of_its_matches`, `The_kinds_of_the_data_search_follow_its_text_through_the_normaliser`, `The_page_asks_for_snippets_and_gets_them_with_their_elements` |
| 5 | a cut ignores word boundaries | killed | `A_line_is_the_piece_s_line_cut_to_160_around_the_hit`, `A_long_line_s_marks_follow_its_cut` |
| 6 | a `snippets` request is answered when overtaken | killed | `The_worker_answers_snippets_for_the_items_asked_and_drops_stale_ones` |
| 7 | a batch goes again for each candidate after it (the base's code) | killed | `A_batch_goes_once_however_many_candidates_follow_it` |
| 8 | an hnote never opens a block | killed | `A_match_s_offset_names_its_note_hnote_arrow_or_partition`, `The_statements_of_a_source_are_its_notes_by_the_page_s_rule_and_its_one_line_statements` |
| 9 | a term under `!!` counts | killed | `The_worker_built_from_its_roster_reads_the_new_forms`, `The_worker_reports_the_kinds_a_match_hit` |
| 10 | rule 1b rejoins a marker after a tilde | killed | `A_piece_s_lines_and_letters_are_where_the_passes_put_them`, `Display_text_and_the_map_read_any_text_as_the_reference_does`, `Display_text_folds_to_the_search_text_for_every_vector`, `Shipped_normalization_matches_every_vector` |
| 11 | the kinds of `data-search` keep their offsets in the attribute | killed | `The_kinds_of_the_data_search_follow_its_text_through_the_normaliser` |
| 12 | no mask of lowered letters is kept | killed | `A_Raw_PlantUML_section_s_line_names_the_section`, `A_long_line_s_marks_follow_its_cut`, `A_match_s_offset_names_its_note_hnote_arrow_or_partition`, `A_note_s_place_counts_its_hits_and_a_line_outside_every_statement_names_none` |
| 13 | a note is named by its line, not the page's note index | killed | `A_line_names_which_hit_of_its_place_it_shows_first`, `A_match_s_offset_names_its_note_hnote_arrow_or_partition`, `A_note_s_place_counts_its_hits_and_a_line_outside_every_statement_names_none`, `Each_diagram_of_a_scenario_is_named_by_its_element` |
| 14 | a line's hit number counts every hit of the piece before it | killed | `A_note_s_place_counts_its_hits_and_a_line_outside_every_statement_names_none` |
| 15 | the page finds a line's element by its index only, never its id | survived | |
| 16 | an ask overtaken by a new query is left pending | killed | `The_page_asks_for_snippets_and_gets_them_with_their_elements` |
| 17 | the last line's start is not found after its line break | killed | `A_piece_s_lines_and_letters_are_where_the_passes_put_them`, `A_place_inside_what_a_pass_takes_out_moves_to_where_it_was_taken_out` |
| 18 | a pass that moves line breaks is not noticed | killed | `A_piece_s_lines_and_letters_are_where_the_passes_put_them`, `A_place_inside_what_a_pass_takes_out_moves_to_where_it_was_taken_out` |
| 19 | the markup tags pass matches lower case only | killed | `A_piece_s_lines_and_letters_are_where_the_passes_put_them`, `Display_text_and_the_map_read_any_text_as_the_reference_does`, `Display_text_keeps_the_case_the_search_text_folds` |
| 20 | the literal `\n` pass matches lower case only | killed | `A_piece_s_lines_and_letters_are_where_the_passes_put_them`, `Display_text_and_the_map_read_any_text_as_the_reference_does`, `Display_text_keeps_the_case_the_search_text_folds` |
| 21 | the line break the last pass adds is not counted | survived | |
| 22 | a note's end is any line that holds the words | killed | `The_note_blocks_are_read_by_one_rule_the_page_and_the_worker_share` |

Mutations 1 and 16 survived a first run; `A_place_inside_what_a_pass_takes_out_moves_to_where_it_was_taken_out` and the
held inbox of the page check were written for them, red on the base as above, and the second note source of
`The_note_blocks_are_read_by_one_rule_the_page_and_the_worker_share` for 22. Two are equivalent: with 15, every page the
facts build has an element with an id at its index among those of its kind, so either way finds it; with 21, the last
pass reads as one that moves line breaks, so the lines are followed through it, to the same places, more slowly.

## Cost

4.12 asks `DeepQueryMs` unchanged. The first build of a piece followed every pass with edits whenever rows or letters
were asked for, which doubled the cold deep query on the bench page, so it was rebuilt: the fold is made last (the passes
after it match any case and write no letter, so it changes nothing they do) and its callback records the letters it
lowers; a pass is a `replace()` while the text keeps every line break, each line's start then being the place after the
line break before it, and only from a pass that moves line breaks (a marked break, a tag over two lines, a decoded line
break) are the lines followed through the passes; the note scan splits a source once and trims and reads only the lines
that hold `note`. `A_piece_s_lines_and_letters_are_where_the_passes_put_them` holds the two ways to each other on 2000
texts that take both.

Node 25 (the least of 20 rounds, each measure in turn in every round):

| Text | Base `kronNormalizeForSearch` | Search text now | `kronCorpusPiece` | `kronDiagramStatements` | First build: piece, statements |
|---|---|---|---|---|---|
| The bench page's six diagrams (`LargeReportFixture.BuildDiagram`, 2,516,997 characters, 134,295 lines of pretty-printed JSON notes) | 18.3 ms | 13.9 ms | 21.3 ms (1.17x) | 5.6 ms | 38.2 ms (2.09x), 8.0 ms |
| A synthetic 1,433,990-character source with marked breaks (21,905 lines, followed from rule 1b on) | 20.9 ms | 17.2 ms | 25.4 ms (1.22x) | 1.7 ms | 23.5 ms (1.13x), 1.7 ms |

`DeepQueryMs` of `BrowserRenderWorkerTests.Large_report_renders_off_the_main_thread_within_budget` (a cold `order` over
the compressed bench page; seven runs each at a contention stretch of 1.00 to 1.05, budget 2000 ms times the stretch):
base 31 to 41 ms (median 35), the first build 66 and 85 ms at a stretch near 1 (66 to 198 ms in all), the final build 39
to 49 ms (median 44). The 9 ms left on this page, a quarter of it, are mostly the note scan, which still splits each
source into lines; finding the notes by where `note` occurs would take most of them.

Memory: a cache entry also holds each piece's rows (4 bytes a line), the mask (a bit a character) and each diagram's
statements (counted at 48 bytes each), about a fifth more than its text on the bench page (5.0 MB of text, 0.5 MB of
rows, 0.3 MB of mask), so the 64 MiB cache holds that much fewer scenarios.
