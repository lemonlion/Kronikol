# Mutations for B8 and B17

Plan 6.4, "one mutation per family": each row breaks one shipped script in `src/Kronikol/Reports`, runs the classes that
should notice, one class per run, and puts the file back byte for byte (`git diff -- src` empty after every row).
`mutate-b8.sh` beside this file runs them again (`bash plans/SEARCH_PLAN.harness/checks/mutate-b8.sh [ids]`).

Run on 2026-10-26 in `C:/Code/Kronikol-search-tests`: the base `c10ed743` (5.9.0's code) with the new classes, before
the port was deleted, Debug build, Playwright's bundled Chromium. Every target passed unmutated: Jint 16 of 16, E2E 13
of 13, FilterPerformanceTests 5 of 5.

The classes:

- **Jint**: `ShippedSearchFunctionTests` (tests/Kronikol.Tests.SearchEngine), 16 with the theories' rows.
- **E2E**: `SearchFunctionPageTests` (tests/Kronikol.Tests.EndToEnd), 13.
- **Perf**: `FilterPerformanceTests` (tests/Kronikol.Tests.EndToEnd), 5.
- **Old**: the deleted `SearchFunctionTests` (tests/Kronikol.Tests), 37, run under W1 and H1 to show B8 itself.

| Id | Family | Where | The change | Aimed at | Result |
|---|---|---|---|---|---|
| W1 | the words | `report-search-function.js` `parseSearchTokensIncludingQuotes` | quotes ignored: both `/"(.*?)"/g` become `/\u0001(.*?)\u0001/g` | Jint `A_search_is_split_into_its_words_and_quoted_phrases`, its three rows with quotes; E2E `A_quoted_phrase_matches_only_where_its_words_stand_together` | Jint 3 of 16 failed, the three rows; E2E 1 of 13 failed, the phrase fact. **Old: 37 of 37 passed** |
| M1 | what a search matches | `run_search_scenarios`, the text test | the first word only: `if (!text.includes(searchTokens[j]))` becomes `if (j === 0 && !text.includes(searchTokens[j]))` | Jint `Every_word_must_be_found_in_the_one_scenario` | Jint 1 of 16 failed, that one; E2E 1 of 13 failed, the phrase fact, whose unquoted search then shows four scenarios |
| M2 | what a search matches | `run_search_scenarios` | the query's case kept: `input = input.toLowerCase().trim();` becomes `input = input.trim();` | Jint `A_word_finds_a_scenario_whatever_the_case_of_either` | Jint 2 of 16 failed, the rows `Chocolate` and `CHOCOLATE` |
| M3 | what a search matches | `run_search_scenarios`, the text test | the scenario's id read for its data-search: `let text = c.items[i].searchText;` becomes `let text = c.items[i].el.getAttribute('id');` | Jint: a word of the steps, of the description, of the diagram, every word | Jint 4 of 16 failed: `A_word_of_its_steps_finds_it`, `A_word_of_its_description_finds_it`, `A_participant_s_name_or_a_call_s_path_in_its_diagram_finds_it`, `Every_word_must_be_found_in_the_one_scenario`. E2E 13 of 13 passed: the deep search reads each scenario's text itself and shows the matches after the instant pass, which is what a reader sees, so the instant search's reading is the Jint family's to hold |
| H1 | hiding | `report-scenario-feature-map-helper.js` `applyVisibility` | the port's way: `d.el.style.display = hidden ? 'none' : '';` becomes `d.el.classList.toggle('search-hidden', hidden);` | every E2E fact; Perf's timings | E2E 13 of 13 failed; Perf 3 of 5 failed, the three timings, nothing hidden; Jint 9 of 16 failed, the matching facts (the Jint page has no `classList.toggle`, so the search throws). **Old: 37 of 37 passed** |
| R1 | one match | `revealSingleMatch` | the scenario left closed: `[el.closest('details.feature'), el.closest('details.rule'), el].forEach(` without `el` | E2E `One_match_is_opened_where_it_is_with_its_steps_and_diagrams_but_not_its_raw_PlantUml` | E2E 1 of 13 failed, that one |
| R2 | one match | `revealSingleMatch` | its steps and background not opened: the sections become `['details.example-diagrams']` | the same | E2E 1 of 13 failed, that one |
| R3 | one match | `revealSingleMatch` | its Raw Plant UML opened too: `'details.example'` added to the sections | the same | E2E 1 of 13 failed, that one |
| R4 | several matches | `run_search_scenarios`, both paths | a match revealed whatever the count: `matchCount === 1` becomes `matchCount >= 1` (2 lines) | E2E `Several_matches_are_left_as_they_were_none_opened_and_none_closed` | E2E 1 of 13 failed, that one |
| C1 | emptying | `run_search_scenarios`, both empty paths | the search's flags kept: `c.items[i].sr = false;` becomes a comment (2 lines) | E2E `Emptying_the_search_shows_every_scenario_and_feature_again`, `A_search_of_spaces_is_no_search`, `A_search_keeps_to_what_Happy_paths_shows_and_emptying_it_leaves_Happy_paths_on`, `Emptying_the_search_shows_every_rule_again` | E2E 4 of 13 failed, those four |
| F1 | rules | `applyVisibility` | a rule never hidden: `rules[i].style.display = '';` | E2E `A_rule_or_a_feature_shows_while_one_of_its_scenarios_matches_and_is_hidden_when_none_does` | E2E 1 of 13 failed, that one |
| F2 | features | `applyVisibility` | a feature never hidden: `f.style.display = '';` | E2E `A_feature_shows_while_one_of_its_scenarios_matches_and_is_hidden_when_none_does`, `A_search_matching_nothing_hides_every_feature`, `A_rule_or_a_feature_shows_while_one_of_its_scenarios_matches_and_is_hidden_when_none_does` | E2E 3 of 13 failed, those three |
| P1 | with the other filters | `run_search_scenarios`, the text test | the search clears the Tags row's flag, Happy paths with it: `c.items[i].tag = false;` added after the search's own flag | E2E `A_search_keeps_to_what_Happy_paths_shows_and_emptying_it_leaves_Happy_paths_on` | E2E 1 of 13 failed, that one |
| D1 | the deep search | `report-search-index.js` `_kronDeepQuery` | the deep search never asked: `return;` first | E2E `A_word_only_in_a_scenario_s_diagram_shows_it_once_the_deep_search_has_read_it` | E2E 1 of 13 failed, that one |
| T1 | B17's timings | `report-dependency-filter-function.js` `filter_dependencies` | the filter hides nothing: `c.items[i].dep = false;` | Perf `DependencyFilter_CompletesWithin1Second_With100Scenarios`, `..._With200Scenarios`, `DependencyFilter_ToggleOffCompletesWithin1Second` | Perf 3 of 5 failed, those three. `The_filter_timings_run_the_shipped_scripts` passed, holding the page to the scripts whatever they are, and so did `DependencyFilter_FeaturesContracted_WhenMoreThan10Matches`, which reads that no feature opens, as a filter hiding nothing also gives |

No survivor among the targets. The old facts passed both mutations they were run under, W1 and H1: that is B8, a
search broken in the page that its facts could not see.

After the classes' last edits (names, comments, and `Emptying_the_search_shows_every_rule_again` comparing each rule's
display exactly), C1, F1, M2 and M3 were run again through `mutate-b8.sh`, with the same results.
