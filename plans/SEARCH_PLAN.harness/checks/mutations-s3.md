# S3 mutations (SEARCH_PLAN 6.4)

Run 2026-10-26 on `feat/github-search-lang` by `mutate-s3.js` beside this file (`node mutate-s3.js <worktree>`). Each
mutation replaces one exact line or expression of a shipped script, builds `tests/Kronikol.Tests.SearchEngine` (whose
Jint facts load the scripts from the assembly's resources) and runs the facts it should break; the script is then
written back from the bytes it was read from and its SHA-256 compared. A mutation counts only when its facts pass
unmutated first. The three 6.4 names for S3 are L4, L6 and L2; L4 is run on both the scripts that read a `-`, and B4
and L3 are run beside them.

| Mutation | Script | Change | Facts | Unmutated | Mutated | Restored |
|---|---|---|---|---|---|---|
| L4-as: a `-` before `tag:` or `status:` does not negate | `advanced-search.js` | the tokeniser's `if (form[1]) tokens.push({ type: 'not', minus: true, ... })` removed | `SearchQueryFormsTests.Minus_before_a_qualifier_excludes_it` | 12 pass | **8 fail**, 4 pass | yes |
| L4-rsf: a `-` before `dep:`, `feature:`, `is:` or `duration:` does not negate | `report-search-function.js` | `SEARCH_QUALIFIER`'s negation group `(!!\s*\|not\s+\|-)?` back to `(!!\s*\|not\s+)?` | every `Minus_before_a_qualifier*` fact (`SearchQueryFormsTests`, `SearchQualifierPlacementTests`) | 24 pass | **14 fail**, 10 pass | yes |
| L6: `feature:` ignored | `report-search-function.js` | the feature test `return parts.some(...)` becomes `return true;` (taken out of the search, keeping every scenario) | `Feature_colon_keeps_the_scenarios_whose_feature_name_holds_it` | 16 pass | **15 fail**, 1 pass | yes |
| L2: `status:` not a token | `advanced-search.js` | the tokeniser's form `/^(-?)(tag\|status):/i` becomes `/^(-?)(tag):/i` | `Status_colon_is_dollar_status_for_results_and_verdicts` | 13 pass | **12 fail**, 1 pass | yes |
| B4: a scenario skipped after a failure is not failed | `advanced-search.js` | the evaluator's `if (ast.value === 'failed' && status.toLowerCase() === 'skippedafterfailure') return true;` removed | `Status_failed_finds_a_scenario_skipped_after_a_failure` | 6 pass | **4 fail**, 2 pass | yes |
| L3: every `status:` name is read | `advanced-search.js` | `searchTokenQualifiers`' `readable: t.type === 'tag' \|\| !names \|\| ...` becomes `readable: true \|\| ...` | `An_unknown_status_name_is_text_and_marks_the_input`, `A_qualifier_is_unreadable_where_the_search_marks_the_field_for_it` | 25 pass | **8 fail**, 17 pass | yes |

All six killed. The rows a mutation leaves passing are the ones it cannot reach: L4-as leaves the four rows on the
search function's qualifiers (`-dep:`, `-is:`, `-duration:`, `-feature:`), which L4-rsf kills, and L4-rsf leaves the
`tag:` and `status:` rows and the guards (`- dep:payment`, `a-dep:payment`); L6 leaves `feature:payment dep:audit`,
where `dep:audit` alone keeps the one scenario the row expects; L2 leaves `status:broke && status:passed`, which keeps
nothing either way; B4 leaves the guards `$skippedafterfailure` and `$skipped`; L3 leaves `tag:""` (a key with no name,
which this mutation does not touch) and the agreement rows, where the field and the spans go through the one reading
(`searchTokenQualifiers`) and so still agree with each other.

**B4, since 5.9.1.** The B4 row above was run on 5.9.0. 5.9.1's E3 (plans/REPORT_UI_FIXES_PLAN.md) made the Status
buttons count a scenario skipped after a failure as skipped, so B4's rule, that the search agrees with the Status buttons,
now reads `status:skipped` and `$skipped`, and the evaluator line this mutation removed is gone from the code. The fact is
now `Status_skipped_finds_a_scenario_skipped_after_a_failure`, and the S7 run (`mutate-s7.js`, mutation E3) puts back the
5.9.0 reading of `status:skipped` to show it fails.
