# S3 red run on the base (SEARCH_PLAN 6.4)

Stage S3, the query language (4.4) and B4. Every new fact was written first and run against the code the stage starts
from, `c10ed743` (5.9.0's code; its commits after `37428293` touch plans only), on branch `feat/github-search-lang`,
before any script changed: `dotnet test tests/Kronikol.Tests.SearchEngine` (all 658 facts, 300 failing) and the E2E class
`AdvancedSearchTests` alone (19 facts, 4 failing; bundled Chromium). Every failure is in a new fact and every guard
passes, the facts that were there before included. "Line" is the line of the test file at that commit that failed.

The Jint facts run the shipped scripts. The page's own search is run through `SearchPage` (new): `advanced-search.js`,
`report-scenario-feature-map-helper.js` and `report-search-function.js` over stub elements carrying the attributes the
generator writes, so a query goes through `run_search_scenarios`, the real `fc()` and `applyVisibility` as in a browser;
`SearchFixture` is its report (three features, eleven scenarios S1 to S11, every status, tags, dependencies in three
alphabets, own times, verdicts, and text holding `status:400` and `milk:`).

## Red, as each fact says

| Fact | Class | Red rows | Line | Why it fails on the base |
|---|---|---|---|---|
| L1 `Tag_colon_is_at_tag` | Jint `SearchQueryFormsTests` | 10 of 10 | `SearchQueryFormsTests.cs:20` | `tag:smoke` kept nothing (`tag:smoke` is text); `@smoke`, checked first in each row, passes |
| L2 `Status_colon_is_dollar_status_for_results_and_verdicts` | Jint `SearchQueryFormsTests` | 12 of 13 | `SearchQueryFormsTests.cs:20` | `status:always-failing` and the rest kept nothing; the row that passes is `status:broke && status:passed`, which keeps nothing either way |
| L3 `An_unknown_status_name_is_text_and_marks_the_input` | Jint `SearchQueryFormsTests` | 9 of 9 | `SearchQueryFormsTests.cs:82` | the right scenarios were kept, the name being text already, but the field was left unmarked ("status:400 left the field unmarked") |
| L3 `A_verdict_in_a_report_drawn_without_verdicts_is_text_and_marks_the_input` | Jint `SearchQueryFormsTests` | 1 of 1 | `SearchQueryFormsTests.cs:91` | `status:flaky` in a default report left the field unmarked |
| L3 `An_unknown_status_name_is_text_and_marks_the_input` | E2E `AdvancedSearchTests` | 1 of 1 | `AdvancedSearchTests.cs:297` | `#searchbar` never took `kron-q-bad` after `status:nonsense` |
| L4 `Minus_before_a_qualifier_excludes_it` | Jint `SearchQueryFormsTests` | 12 of 12 | `SearchQueryFormsTests.cs:20` | `-is:happy`, `-tag:slow`, `-feature:payment` and the rest kept nothing (`-` is text); `!!feature:payment` kept everything |
| L4 `Minus_before_a_qualifier_is_part_of_it` | Jint `SearchQualifierPlacementTests` | 7 of 9 | `SearchQualifierPlacementTests.cs:109` | `-dep:payment` and the others were no qualifier (0 read, left in the rest); the guards `- dep:payment` and `a-dep:payment` pass |
| L4 `Minus_before_a_qualifier_excludes_it` | Jint `SearchQualifierPlacementTests` | 2 of 3 | `SearchQualifierPlacementTests.cs:118` | no qualifier was read, so the fact's first one was no function ("Property '0' of object is not a function"); the guard `- dep:payment` passes |
| L4 `A_minus_qualifier_inside_a_negated_group_stays_text_and_marks_the_search` | Jint `SearchQualifierPlacementTests` | 1 of 1 | `SearchQualifierPlacementTests.cs:124` | `!!(-dep:payment)` was not named as a qualifier it cannot read |
| L1, L4 `Tag_colon_and_a_minus_before_it_read_as_at_tag_and_its_negation` | E2E `AdvancedSearchTests` | 1 of 1 | `AdvancedSearchTests.cs:339` | `tag:smoke` never left Create order alone showing (5 s wait) |
| L6 `Feature_colon_keeps_the_scenarios_whose_feature_name_holds_it` | Jint `SearchQualifierPlacementTests` | 13 of 16 | `SearchQualifierPlacementTests.cs:151` | `feature:pancake` and the rest kept nothing (`feature:` is text); the rows that pass keep nothing either way (`feature:api`, `feature:orders`, `feature:kitchen`: the endpoint and the tags are no part of the name) |
| L6 `A_feature_qualifier_with_no_name_cannot_be_read` | Jint `SearchQualifierPlacementTests` | 3 of 3 | `SearchQualifierPlacementTests.cs:161` | `feature:""` was not named as a qualifier it cannot read |
| L6 `Feature_colon_keeps_the_scenarios_whose_feature_name_holds_it` | E2E `AdvancedSearchTests` | 1 of 1 | `AdvancedSearchTests.cs:325` | `feature:payment` never left Payment Feature's two scenarios showing (5 s wait) |
| L9 `searchQuerySpans_cuts_keys_values_operators_and_unreadable_qualifiers` | Jint `SearchQuerySpansTests` | 64 of 64 | `SearchQuerySpansTests.cs:61` | "searchQuerySpans is not defined" |
| L9 `The_spans_touch_from_the_start_of_the_query_to_its_end` | Jint `SearchQuerySpansTests` | 127 of 127 | `SearchQuerySpansTests.cs:163` | "searchQuerySpans is not defined" |
| L9 `A_qualifier_is_unreadable_where_the_search_marks_the_field_for_it` | Jint `SearchQuerySpansTests` | 16 of 16 | `SearchQuerySpansTests.cs:188` | "searchQuerySpans is not defined" |
| L10 `The_worker_reads_the_new_forms_as_the_page_does` | Jint `VerdictDeepSearchTests` | 9 of 11 | `VerdictSearchTests.cs:125` | the page kept nothing for `tag:smoke` and the rest; the two rows that pass are `status:400` and `api status:400`, text on both sides then and now |
| L10 `The_deep_search_plans_the_new_forms_as_it_plans_their_old_ones` | Jint `VerdictDeepSearchTests` | 4 of 5 | `VerdictSearchTests.cs:153` | `kronIsDeepEligible(tag:smoke)` was true (a text term), `kronQueryCanRemove(-tag:slow && order)` false; `tag:"two words" \|\| pancake` passes, its `\|\|` making both forms advanced and eligible |
| L10 `Shipped_candidates_never_prune_by_a_tag_or_status_qualifier` | Jint `SearchIndexJintTests` | 6 of 6 | `SearchIndexJintTests.cs:137` | `tag:sometag` and the rest pruned the candidates as text (none, or only the text's) |
| L10 `The_worker_built_from_its_roster_reads_the_new_forms` (written after the run above, and run red on its own: the two scripts put back to `c10ed743`'s bytes for the run, then restored and their SHA-256 checked) | Jint `VerdictDeepSearchTests` | 1 of 1 | `VerdictSearchTests.cs:164` | the worker built from `buildWorker`'s roster alone read `tag:smoke` as text ("Assert.True() Failure") |
| B4 `Status_failed_finds_a_scenario_skipped_after_a_failure` | Jint `SearchQueryFormsTests` | 4 of 6 | `SearchQueryFormsTests.cs:20` | `$failed` kept S2, S7 and S11 but not S4, skipped after a failure, in both variants; the guards `$skippedafterfailure` (S4) and `$skipped` (S6, not S4) pass |
| B4 `Status_failed_finds_a_scenario_skipped_after_a_failure` | E2E `AdvancedSearchTests` | 1 of 1 | `AdvancedSearchTests.cs:312` | `$failed` never showed Order history after the failure (5 s wait) |

## Guards, green on the base

| Fact | Class | Rows |
|---|---|---|
| L5 `Minus_before_a_word_is_text` | Jint `SearchQueryFormsTests` | 10 of 10 |
| L7 `New_forms_inside_quotes_are_text` | Jint `SearchQueryFormsTests` | 8 of 8 |
| L8 `Every_documented_query_finds_what_it_found_before` | Jint `SearchCompatibilityTests` | 74 of 74, each in a report drawn with verdicts and in a default one |
| the 15 facts `AdvancedSearchTests` held before | E2E `AdvancedSearchTests` | 15 of 15 |
| every fact the Jint project held before | Jint | 254 of 254 |

L8's table was captured on this base by running every query of the wiki's `Search-Syntax.md` (at `1f7944e`) through
`SearchPage` in both variants, then checked by hand against the fixture (for example `order || dep:payment` keeps S4
alone, the qualifier narrowing by AND; `!!(dep:payment)` keeps every scenario and marks the field). A query that names
`$failed` is compared without S4, B4 being the one change of meaning the stage makes on purpose.
