# Research B: test inventory for moving History UI and Report diagnostics out of TestRunReport.html

Read-only survey of C:/Code/Kronikol at main 425d9bad (2026-10-05). No test was run, no report opened.
Line numbers are 1-based against that commit.

Legend used in every table:
- **(a)** moves with the feature to the new document (re-point the fact at the new file/renderer)
- **(b)** must be turned round to assert ABSENCE from TestRunReport.html
- **(c)** unaffected (asserts JSON, Failures.md/jsonl, CTRF, ledger, console, query output)
- "mixed" = parts of one fact fall in different classes; line ranges are given per part

Production anchors the tests hit:
- `ReportGenerator.GenerateHtmlReport` is PUBLIC and takes `diagnostics` (ReportGenerator.cs:1055), `history` (1067),
  `showHistorySection` (1068), `showReportDiagnostics` (1069). Inside the HTML generator `diagnostics` is used only
  for the section (1827-1828). History section 1839-1841; scenario attr/sparkline/pill 2075-2081; parameterised
  group + rows 2762-2779, 2860-2861, 2947, 3054. Main path passes `options.EmbedHistoryInReport ? history : null`,
  `options.ShowHistorySection`, `options.ShowReportDiagnosticsSection` (488).
- `HistoryHtml` (internal): VerdictAttribute 36-47, Sparkline 54-71, Pill 75, GroupPill 86, Section 100+.
- `ReportGenerator.RenderReportDiagnostics` (internal, 4118-4142).
- Options: DiagnosticMode 418, ShowReportDiagnosticsSection 429, EmbedHistoryInReport 765, ShowHistorySection 776
  (ReportConfigurationOptions.cs); ReportToggleDefaults.DiagnosticsOpen (ReportToggleDefaults.cs:89);
  ResolvedToggleDefaults.DiagnosticsOpen (ReportToggleDefaultsResolver.cs:37, 88).
- CLI: MergeCommand.cs:56-58 (--history), 111-115 (ShowHistorySection = history is not null), usage 357, 382-384;
  IngestCommand.cs:392 (ShowReportDiagnosticsSection = diagnosticsSection), usage 686-692.
- CSS (embedded VERBATIM, comments included, via src/Kronikol/Constants/Stylesheets.cs): diagnostics block
  stylesheets.css:1261-1314 (line 1261 is the comment `/* Report diagnostics (capture health, skipped lines, render
  failures) */`), history block 1316-1451, phone override `.history-sparkline` 1995-1997.
- JS: advanced-search.js:249-279 (5th `verdicts` arg; status branch 262-271), report-search-function.js:62,
  report-search-index.js:251-253, 358, 481, report-scenario-feature-map-helper.js:14-16 (reads data-history-verdicts).

---------------------------------------------------------------------------------------------------------------

## 0. Headline findings

1. **Affected facts.** Kronikol.Tests: about 20 facts in 10 files (17 definite, 3 fixture/conditional).
   Kronikol.Tests.SearchEngine: 8 facts (VerdictSearchTests + VerdictDeepSearchTests). E2E: 12 facts in the four
   History* classes (all in lane **E2E (Remainder)**), 2 theory rows + fixture in ReportSectionWidthTests and 2
   facts via fixture in ViewportSweepTests (both in lane **E2E (Toolbar & Reports)**).
2. **No E2E test targets the diagnostics section, and none covers DiagnosticsOpen.** The only E2E pages carrying
   the diagnostics section are the `GenerateReportWithEverySection` pages (ViewportSweepTests x2,
   ReportSectionWidthTests x7 cases). DiagnosticsOpen is covered by one unit fact only.
3. **Three assertions are vacuous today** (bug class of 3.0.82: a bare substring the embedded stylesheet
   satisfies). They go red the moment the CSS leaves and should be fixed regardless:
   - ReportGeneratorDiagnosticsScopeTests.cs:61 `Assert.Contains("Report diagnostics", TestRunReport.html)`: the run
     never asks for the section (Options() 30-40; default false), so only the CSS comment at stylesheets.css:1261
     matches.
   - IngestHostDiagnosticsTests.cs:81 `"Report diagnostics ("` (same CSS comment) and :82
     `"report-diagnostic-kind-capturedegraded"` (selector at stylesheets.css:1301). Lines 80 and 83 do hold the fact
     to real markup.
   - MergeCommandTests.cs:222 `Assert.Contains("history-sparkline", html)` (matches `.history-sparkline {` at 1317).
4. **No golden HTML, no CSS/JS byte budget and no report-size budget exist in this repo.** The only byte-identity
   pin (ToggleDefaultsBaselineTests) is self-relative and survives; only its fixture arguments change.
   RunRotationTests pins exact file sets that move only if a new document is written by default.
5. **Kronikol4J:** 0 of 59 parity goldens and its stylesheets.css copy contain `.history-*` or
   `.report-diagnostic*`; REMAINING_PARITY.md already records both as .NET-only. One ledger line, no golden regen.
6. **Drift tests do not pin these flags/options.** SkillDriftTests covers only `kronikol query`.
   `--diagnostics-section` is pinned only by IngestCommandTests.cs:476 (usage text) and exercised at :466.
   `merge --history`'s usage text is pinned by nothing. No test reads the wiki.
7. **Public surface.** Four test helpers pass the public GenerateHtmlReport parameters `diagnostics` / `history` /
   `showHistorySection` / `showReportDiagnostics`; removing them is a source and binary break (as is removing the
   options, ReportToggleDefaults.DiagnosticsOpen, ResolvedToggleDefaults.DiagnosticsOpen, or the CLI flag).
   Precedent for a removal pin: tests/Kronikol.Tests/Reports/MonospaceNoteControlRemovedTests.cs (members marked
   [Obsolete] in 3.36.0/R7, removed in 4.0.0/R8, reflection facts that they are gone).

---------------------------------------------------------------------------------------------------------------

## 1. Unit tests that assert history markup in the HTML

### 1.1 tests/Kronikol.Tests/History/HistoryHtmlTests.cs - class `HistoryHtmlTests` (7 facts, all affected)

Helpers: `Verdicts`/`VerdictsInto` (50-73) seed `<temp>/.kronikol/history.jsonl` (or a named path) with
HistoryLedgerWriter.Append and analyse with HistoryAnalyzer (MinRuns 2); `Html` (75-82) calls
ReportGenerator.GenerateHtmlReport(..., suite: "HtmlSuite", history, showHistorySection: true) (line 80);
`ScenarioHead` (85-90) regex-extracts one scenario's opening tag + summary by data-stable-id.

| Fact (line) | What it asserts | Class |
|---|---|---|
| A_regression_carries_its_verdict_a_sparkline_and_a_pill_beside_its_duration (93-114) | data-history-verdicts="broke", class="history-sparkline", gradient stops, tooltip PPPF and "broke: passed in test:3:1", pill history-verdict-broke / >broke</span>, sparkline after duration-badge (108); refund: "stable", sparkline, no pill | mixed: a (attr/sparkline/pill wherever they now live) + b (scenario head in TestRunReport.html carries none) |
| A_position_that_was_not_a_test_is_named_in_the_sparkline_tooltip (117-129) | "not a test", #dcdcdc, no "bypassed" | a |
| A_degraded_run_is_named_in_the_run_tooltips_and_once_above_them (132-155) | calls HistoryHtml.Section directly (149): degraded line, 2 chart titles | a (pure section renderer) |
| The_history_section_lists_the_regression_and_links_it_by_stable_id (158-176) | <details id="history-section" open, "1 broke (against 3 earlier runs on local)", "New failures", href="#sid-<id>", "Checkout &rsaquo; Pay by card", class="history-chart", "Pass rate", 4 rects; section before <div id="report-content"> (175) | a; the href must become a cross-document link (TestRunReport.html#sid-<id>) if the section lives in its own file; line 175 is a TestRunReport placement check (drop) |
| A_fixed_scenario_is_listed_as_newly_fixed_and_a_stable_run_leaves_the_section_closed (179-192) | data-history-verdicts="fixed", "Newly fixed", section closed, "nothing changed (against 3 earlier runs on local)" | a |
| Without_history_nothing_about_history_reaches_the_html (195-205) | with history = null: no class="history-sparkline", <details id="history-section", data-history-verdicts=", class="history-verdict | b: generalise to WITH history; once CSS/JS leave, bare names can be asserted absent from the whole file (comment 199-200 says why not until now). `history.replaceState` stays in scripts, so assert "history-" / "data-history-verdicts", not "history" |
| A_parameterised_group_carries_the_union_of_its_rows_verdicts_and_each_row_its_own (208-235) | group data-history-verdicts="broke,stable" + broke pill; each visa <tr> carries "broke" | mixed: a (if the new doc keeps per-row verdicts) + b (no attr on group/rows) |

Class summary (8-13) claims "a report rendered with no history is byte for byte what it was before"; no fact
checks bytes.

### 1.2 tests/Kronikol.Tests/History/HistoryOutputsTests.cs - class `HistoryOutputsTests` (19 facts; 4 touch HTML)

Helpers: `Options(run, runId)` (30-48): GenerateTestRunReport = **false**, HistoryFilePath =
<temp>/ledger/history.jsonl, HistoryRunId, HistoryMinRuns 2, HistoryBranch ""; `Run(...)` (76-81) =
ReportGenerator.CreateStandardReportsWithDiagrams with an optional `configure`; `Seed`/`SeedTrunk` (336-354).

| Fact (line) | What it asserts | Class |
|---|---|---|
| The_html_report_embeds_history_unless_told_not_to (288-308) | h2 (section asked for): section, data-history-verdicts="broke", <span class="history-sparkline" in TestRunReport.html (295-298); h3 EmbedHistoryInReport=false: all absent (302-305); ledger still recorded (307) | mixed: 295-298 a (assert in the new document) + b (absent from TestRunReport.html); 302-305 becomes the switch test for the new document or goes with the option; 307 c |
| The_history_section_is_left_out_of_the_report_unless_it_is_asked_for (311-329) | default: no section (320-321) but attr + sparkline present (323-324); ShowHistorySection on: section present (327-328) | 320-321 b (keep, unconditional); 323-324 turn round (b); 327-328 a or delete with the option |
| A_run_reads_against_the_stream_it_is_told_to (357-376) | Failures.md broke vs not (366-371); TestRunReport.html data-history-verdicts="broke" (372); ledger branch (374-375) | 372 a/b; rest c |
| A_compare_branch_is_read_out_beside_the_run_s_own_stream (379-391) | ShowHistorySection + HistoryCompareBranch (384); digest "on trunk: 1 broke ..." (386-387); class="history-compare" and "on <code>trunk</code>: 1 broke" in TestRunReport.html (388-390) | 388-390 a; 386-387 c |

(c) the other 15: A_run_writes_its_fragment_and_appends_its_line_to_the_named_ledger (84),
The_second_run_reads_the_first_and_every_surface_says_what_broke (104; Failures.md/jsonl/CTRF),
A_flaky_scenario_is_flaky_on_the_ctrf_document_even_when_it_passed_first_time (130),
Failures_are_worked_through_regressions_first (145), The_fragment_and_the_append_can_each_be_switched_off (162),
The_same_run_identity_is_not_appended_twice (172),
A_later_attempt_of_the_same_run_from_another_process_is_overlaid_on_its_line (187),
A_ledger_that_cannot_be_read_is_a_diagnostic_not_a_failed_run (204),
A_consumers_templating_rules_reach_the_run_and_a_bad_one_is_a_diagnostic_not_a_failed_run (221),
A_filtered_run_is_recorded_as_partial_with_a_diagnostic (243), The_pointer_carries_the_history_line_on_both_channels (259),
Nothing_about_history_reaches_the_outputs_when_it_is_off (269; GenerateTestRunReport false; Failures.md/jsonl/CTRF),
A_pull_request_reads_its_target_however_many_pull_request_runs_came_since (394),
A_window_of_zero_is_not_an_invitation_to_read_the_whole_ledger_back (414),
A_pull_request_reads_against_the_branch_it_targets_without_being_told (429).

### 1.3 tests/Kronikol.Tests/Reports/Merge/MergeCommandTests.cs - class `MergeCommandTests` (merge --history)

| Fact (line) | What it asserts | Class |
|---|---|---|
| Merge_with_a_ledger_renders_history_and_never_writes_to_it (184-248) | seeds ledger inline (200-209) at <temp>/.kronikol/history.jsonl; with `--history`: Combined.html has <details id="history-section" (220), data-history-verdicts="broke" (221), "history-sparkline" (222, VACUOUS bare substring); Failures.md **History:**/**broke** (223-225); stdout "history: 1 broke" (226); ledger length unchanged (227); Plain.html without --history has no section (229-231); PR env cold start (235-238); missing ledger exit 2 (240-242) | mixed: 220-222 a (the new document merge writes) + b (Combined.html carries none); 229-231 b (keep); 223-227, 235-242 c |
| Merge_of_a_reports_directory_skips_the_files_a_run_writes_beside_its_report_and_says_so (141-160) | "skipped 2 files Kronikol writes beside a report (History.run.json, ctrf-report.json)" (154) | c, UNLESS the new document is a .json file merge would discover; then the skip list (MergeCommand.cs:307-347) and this pin move |
| Merge_still_refuses_such_a_file_when_it_is_named_rather_than_found (163-181) | named History.run.json refused | c |

### 1.4 tests/Kronikol.Tests.SearchEngine/VerdictSearchTests.cs - the `$verdict` sigil (Jint)

Class `VerdictSearchTests : JintTestBase` (6 facts) and class `VerdictDeepSearchTests` (2 facts). The class depends
on a plan choice: does the new document reuse advanced-search.js with a verdict set?

| Fact (line) | If verdict search moves to the new doc | If `$verdict` is simply dropped |
|---|---|---|
| A_status_query_still_matches_the_execution_result (15) | c | c |
| A_verdict_query_matches_the_scenarios_that_carry_it (23) | a | b (a verdict word is no longer special) or delete |
| Without_a_verdict_set_a_verdict_query_is_false_and_never_an_error (32) | c | c: becomes THE rule for TestRunReport.html (`$flaky` false, never an error, never the legacy text search) |
| Verdicts_and_statuses_combine_with_the_operators (41) | a | delete |
| An_execution_result_name_is_never_looked_up_in_the_verdict_set (51) | a | delete |
| The_evaluator_takes_the_verdict_set_on_the_parsed_tree_as_well (60) | a | delete |
| VerdictDeepSearchTests.The_deep_matcher_reads_the_verdicts_it_is_handed (95) | a | delete |
| VerdictDeepSearchTests.The_worker_source_carries_the_verdict_aware_matcher (107) | a | b: pins "verdicts" in kronDeepMatchesItem, "item.verdicts", "verdicts: c.items[i].verdicts" in the shipped report-search-index.js (116-118); red when the plumbing leaves |

Helper plumbing: tests/Kronikol.Tests.SearchEngine/JintTestBase.cs:69-111. CallMatch/CallEvaluate take
`string[]? verdicts` and always pass `__verdictSet` as the 5th JS argument (76, 91-96); SetVerdicts (101-111).
Dead if the 5th argument goes (JS ignores extra arguments, so nothing breaks).
Other SearchEngine classes are (c): BackwardCompatibilityTests 5, EdgeCaseTests 22, EvaluatorTests 25,
IntegrationTests 19, ParserTests 30, SearchIndexJintTests 18 (calls kronDeepMatchesItem with 5 args at 171),
TokeniserTests 42. tests/shared-vectors/search-index-vectors.json has no verdict case.

---------------------------------------------------------------------------------------------------------------

## 2. Report diagnostics section unit tests

### 2.1 tests/Kronikol.Tests/Ingestion/IngestHostDiagnosticsTests.cs - class `IngestHostDiagnosticsTests` (5 facts)

Helper `Request(folder, hostDiagnostics, withTraffic = true, showSection = true)` (39-64) sets
`options.ShowReportDiagnosticsSection = showSection` (46): every fact asks for the section unless told not to.

| Fact (line) | What it asserts | Class |
|---|---|---|
| Host_diagnostics_lead_the_result_and_reach_the_html_and_json_report (70-105) | result order (74-77) c; HTML (79-87): class="report-diagnostics" (80), "Report diagnostics (" (81, VACUOUS), "report-diagnostic-kind-capturedegraded" (82, VACUOUS), message (83), encoding (85-86), [TestId] (87); JSON + schema (89-104) c | 79-87 a (new document) + b (absent from TestRunReport.html) |
| Without_host_diagnostics_nothing_changes_and_the_data_file_mirrors_the_result (108-126) | result/JSON (112-119) c; HTML: section present iff the JSON lists entries (121-125) | 121-125 a/b (TestRunReport.html: always absent) |
| Host_diagnostics_survive_an_ingest_that_generates_nothing (129-135) | result only | c |
| The_html_section_is_left_out_unless_it_is_asked_for_and_the_data_file_carries_it_either_way (138-157) | off: no <details class="report-diagnostics", no <summary>Report diagnostics ( (143-145); JSON (147-153); on: section present (155-156) | 143-145 b (keep, unconditional); 147-153 c; 155-156 a or delete with the option |
| The_html_block_is_collapsed_summarises_kinds_and_encodes_every_field (160-176) | direct ReportGenerator.RenderReportDiagnostics (162, 175): starts <details class="report-diagnostics"><summary>, not open, "Report diagnostics (3: CaptureDegraded x2, MalformedLine)" (the real text uses the multiplication sign), encoding, empty input renders "" | a (pure renderer moves) |

### 2.2 tests/Kronikol.Tests/Tool/IngestCommandTests.cs - class `IngestCommandTests` (`kronikol ingest --diagnostics-section`)

| Fact (line) | What it asserts | Class |
|---|---|---|
| Ingest_command_carries_host_diagnostics_into_the_report (424-477) | stdout lines (450-453) c; default TestRunReport.html: no <summary>Report diagnostics (, no message (455-458) b (keep); JSON (459-461) c; `--diagnostics-section` (466): section + message in TestRunReport.html (469-471) a (or delete with the flag); usage names --diagnostic and --diagnostics-section (473-476) moves with the flag | mixed |
| An_ingest_does_not_warn_that_activity_diagrams_will_be_empty (585-614) | console "Report diagnostics:" (613), the ReportDiagnostics.Analyse warning line, not the section | c |

Also (c): Ingest_command_rejects_a_diagnostic_without_a_message (480), Diagnostic_values_parse_as_kind_colon_message
(theory, 498), Diagnostic_values_without_a_message_are_refused (theory, 511): the `--diagnostic` flag stays.

### 2.3 tests/Kronikol.Tests/Reports/ReportGeneratorDiagnosticsScopeTests.cs - class `ReportGeneratorDiagnosticsScopeTests`

| Fact (line) | What it asserts | Class |
|---|---|---|
| A_normal_run_records_its_diagnostics_in_the_json (52-62) | JSON RenderFailure (59-60) c; line 61 `Assert.Contains("Report diagnostics", TestRunReport.html)` | 61 is VACUOUS (only the CSS comment matches); rewrite as b (anchored absence of <details class="report-diagnostics") or point it at the new document (a) |
| A_scope_opened_by_the_host_is_reused_not_replaced (65), Two_generations_do_not_share_a_collector (84) | JSON only | c |

### 2.4 Toggle defaults (DiagnosticsOpen)

- tests/Kronikol.Tests/Reports/ToggleDefaultsMarkupTests.cs: `Diagnostics_open_default_opens_the_disclosure`
  (463-468) asserts <details class="report-diagnostics" open> through `GenerateRich` (387-400). a (if the new
  document honours a toggle) or delete with DiagnosticsOpen.
- tests/Kronikol.Tests/Reports/ReportToggleDefaultsResolverTests.cs: `Built_in_defaults_are_todays_hard_coded_literals`
  (22-54), line 49 `Assert.False(b.DiagnosticsOpen)`. Delete the line if the property goes (compile error otherwise).
- tests/Kronikol.Tests/Reports/ToggleDefaultsBaselineTests.cs: fixture `GenerateTestRunShape` (245-261) passes
  `diagnostics:` (256) and `showReportDiagnostics: true` (257-258); fact
  `Unset_config_produces_byte_identical_output_across_null_builtin_and_resolved` (263-284) is self-relative (A==B==C),
  so it survives; the fixture loses the two arguments if GenerateHtmlReport does. Class summary (8-17) names
  "diagnostics" (stale after the move). `BuildRichFixture`/`GenerateTestRunShape` are also used by
  ToggleDefaultsMarkupTests (389, 397, 542, 561).
- tests/Kronikol.Tests/ReportConfigurationOptionsDefaultsTests.cs: `Toggle_default_groups_are_non_null_with_every_property_unset`
  (35-45) reflects over ReportToggleDefaults: c.
- E2E tests/Kronikol.Tests.EndToEnd/ToggleDefaultsTests.cs (20 facts) has no diagnostics fact: c.

### 2.5 DiagnosticReport.html / DiagnosticMode

- tests/Kronikol.Tests/Reports/DiagnosticReportGeneratorTests.cs - class `DiagnosticReportGeneratorTests`, 16 facts
  (20-295) on DiagnosticReportGenerator.BuildHtml (configuration dump, tracking components table, unknown-entry
  breakdown, unmatched client names). c today. DiagnosticReport.html is already a separate, self-styled document,
  written only when ReportConfigurationOptions.DiagnosticMode is on (ReportGenerator.cs:620-621; planned 767-768), so
  it is a candidate home for the moved list; if chosen, the moved facts land in this class.
- tests/Kronikol.Tests/Reports/RunRotationTests.cs: `The_manifest_lists_every_file_the_run_wrote_and_nothing_it_did_not`
  (695-731) sets DiagnosticMode = true (715) and names DiagnosticReport.html among expected files (728). c; extend the
  named list for any new document.
- tests/Kronikol.Tests/Tracking/TrackThatTests.cs: That_DiagnosticMode_logs_fallback_reason (235),
  That_DiagnosticMode_off_does_not_log_fallback (256) are about **Track.DiagnosticMode** (assertion fallback logging),
  a different switch: c.
- Console/data, all c: ReportDiagnosticsTests.cs:82 (console "Report diagnostics: 2 log entries across 1 test(s)."),
  DataFormatParityTests (XML/YAML diagnostics arrays), MergeDoesNotDoubleTests / MergeSaysWhatItLostTests (JSON
  diagnostics), TestRunReportSchemaContractTests, DiagramFailureIsolationTests:86-93 (per-diagram placeholder text,
  not the section).

---------------------------------------------------------------------------------------------------------------

## 3. Playwright E2E (tests/Kronikol.Tests.EndToEnd)

### 3.1 Fixtures that build pages with history / diagnostics

- **HistoryReportHelper.Generate(tempDir, outputDir, fileName, tweak = null, wide = false)**
  (HistoryReportHelper.cs:51-105). Four scenarios (Checkout: Pay by card FAILED, Refund an order, Retry a payment;
  Search: Find a product). Seeds a ledger at `<tempDir>/ledger-<8 hex>/history.jsonl` (65):
  HistoryRunBuilder.Build(features, [], "E2EHistory", null, 2026-09-14T12:00Z, new HistoryBuildOptions(), "e2e:99:1"),
  then six earlier runs e2e:1:1..e2e:6:1 appended with HistoryLedgerWriter.Append, results alternating "PPFP"/"PPPP"
  (Retry flips, so flaky; Pay always passed, so its current failure is broke) (70-86). `tweak(i, run)` edits a seeded
  run (used for a degraded run, a "not a test" N, a partial run); `wide` adds a "Wishlist" feature of 4 passing
  scenarios (needed for the degraded pace). Reads back (HistoryLedgerReader.Read, 50) and analyses
  (HistoryAnalyzer.Analyse, MinRuns 3) (88-89), then ReportGenerator.GenerateHtmlReport(..., PlantUml, BrowserJs,
  suite, history: verdicts, showHistorySection: true) into tempDir (92-101), copies to outputDir (PlaywrightOutput)
  and returns the tempDir file URL. Exposes PayId/RetryId/RefundId stable ids; HistoryFilterTests computes FindId (38).
  It bypasses ReportConfigurationOptions (EmbedHistoryInReport, ledger resolution) entirely.
- **ReportTestHelper.GenerateReportWithEverySection(tempDir, outputDir, fileName)** (ReportTestHelper.cs:2798-2887;
  doc 2788-2797; token constants 2779-2786). Same seeding on a Dependabot-branch CiMetadata (2837-2857: six runs
  "PPPFP"/"PPPPP", MinRuns 3), two DiagnosticEntry (RenderFailure scoped to es1, MalformedLine with a long path)
  (2859-2863), background calls (2864-2868), and GenerateHtmlReport(..., diagnostics, background, suite, history,
  showHistorySection: true, showReportDiagnostics: true) (2870-2883). So **yes: the sweep/section-width fixture
  carries BOTH the History section and the Report diagnostics section** (plus per-scenario sparklines and pills).
- PlaywrightTestBase: TempDir = %TEMP%/kronikol-pw-<8 hex>, OutputDir = <bin>/PlaywrightOutput; no history or
  diagnostics parameters. No other ReportTestHelper generator takes or hard-codes history/diagnostics.

### 3.2 Per class

**HistorySectionTests** - [Collection(Reports)], lane E2E (Remainder), 4 facts, all (a):

| Fact (line) | Asserts |
|---|---|
| The_section_summarises_the_run_and_opens_when_there_is_something_to_say (21-40) | #history-section visible + open; summary "1 broke", "1 flaky", "against 6 earlier runs"; h4 "New failures"/"Flaky"; 2 svg.history-chart-svg; 7 rects |
| A_link_in_the_section_opens_the_scenario_it_names (42-58) | clicks a.history-link[href='#sid-PayId'], scenario opens, URL ends #sid-...; in another document this becomes a cross-file link; the receiving side (TestRunReport.html#sid-...) is already pinned by StableIdDeepLinkTests (c, becomes load-bearing) |
| A_degraded_run_says_so_on_its_bars_and_a_healthy_one_does_not (60-76) | tweak i==3 durations x3, wide; rect titles "e2e:4:1 ... degraded 3.0x"; .history-meta has no "degraded" |
| The_flaky_entry_carries_its_evidence_and_series (78-88) | list entry .history-verdict "flaky", .history-evidence "flips", .history-series "FPFPFPP" |

**HistorySparklineTests** - [Collection(Scenarios)], lane E2E (Remainder), 4 facts:

| Fact (line) | Asserts | Class |
|---|---|---|
| A_regression_shows_its_sparkline_and_a_broke_pill_in_its_header (23-41) | data-history-verdicts="broke"; summary .history-sparkline visible, title "PPPPPPF", "broke: passed in e2e:6:1"; pill "broke" with class history-verdict-broke | a (if the new doc keeps sparklines) + b (TestRunReport.html header has none) |
| A_stable_scenario_has_a_sparkline_and_no_pill (43-52) | stable attr, sparkline, 0 pills | a |
| The_sparkline_paints_one_colour_stop_per_run_as_a_single_element (54-77) | 0 child nodes; computed background-image counts rgb(191, 0, 0) x2, rgb(34, 139, 34) x12; flaky x6 | a (depends on the .history-sparkline CSS travelling with it) |
| A_run_that_was_partial_and_a_position_that_was_not_a_test_are_painted_and_named (79-108) | tweak: N and partial run; title lines; grey stop pair; attr "stable" | a |

**HistoryFilterTests** - [Collection(Search)], lane E2E (Remainder). NOT "E2E (Search & Filters)": that lane's
filter (ci.yml:102) names UrlHashFilterHighlightTests|FilterPerformanceTests|AdvancedSearchTests|DeepSearchTests|
DependencyColoringTests, and the substring match does not hit HistoryFilterTests. 3 facts:

| Fact (line) | Asserts | Class |
|---|---|---|
| Dollar_flaky_shows_only_the_flaky_scenario (40-46) | `$flaky` leaves only Retry visible | a if the new doc filters by verdict; else b (in TestRunReport.html `$flaky` matches nothing and does not error) |
| Verdicts_combine_with_statuses_and_each_other (48-59) | `$failed && $broke`, `$passed && $flaky`, `$flaky || $broke`, `$stable`, `$failed && !!$flaky`, "" | a / delete |
| A_status_name_still_means_the_status (61-68) | `$failed`, `$passed` | c in substance, already covered by AdvancedSearchTests (`$failed` 104, `$passed && order` 120, `@api && $passed` 162); delete or re-home on a plain fixture |

SearchAndWaitFor (25-36) uses FillSearchBar + WaitForFunctionAsync with PollingInterval 200.

**HistoryExportTests** - [Collection(Reports)], lane E2E (Remainder), 1 fact:

| Fact (line) | Asserts | Class |
|---|---|---|
| The_filtered_export_keeps_the_sparklines_and_leaves_the_section_behind (15-42) | Export Filtered HTML keeps class="history-sparkline", data-history-verdicts="broke", >broke</span> (30-32), drops <details id="history-section" (33); reopened export shows sparkline + pill (36-41) | b: its premise disappears; turn round to "the export carries no history markup" or delete. The only "export filtered HTML with history" test (ExportFilteredHtmlRenderingTests has no history) |

**ReportSectionWidthTests** - [Collection(Reports)], lane E2E (Toolbar & Reports); every case opens
GenerateReportWithEverySection (Open 25-33):

| Fact (line) | Class |
|---|---|
| A_long_token_outside_the_features_breaks_inside_its_box (theory 38-64; 5 rows 39-43) | rows (320, "a.history-link", "OrderReconciliationTests") (41) and (320, ".history-meta code", "dependabot/nuget") (42) are (a): move to the new document's own width test; rows 39, 40, 43 (c); the closest('.failure-clusters, .history-section, .filtering-box') selector (53) loses .history-section |
| The_search_help_table_scrolls_inside_its_panel_when_the_filtering_box_is_narrower (68-80) | c (fixture only) |
| An_export_button_wider_than_the_filtering_box_wraps_its_label_inside_it (84-104) | c (fixture only) |

Class summary (5-14) names the History section (stale after the move).

**ViewportSweepTests** - [Collection(Mobile)] + IClassFixture<ClassicScrollbarBrowser>, lane E2E (Toolbar & Reports), 7 facts:

| Fact (line) | Class |
|---|---|
| Run_report_with_every_section_fits_every_width (104-106) | fixture change: the page no longer holds History/diagnostics, so their at-every-width coverage must be re-established on the new document(s) |
| Run_report_with_every_section_fits_every_width_under_wcag_text_spacing (127-129) | same |
| Run_report_with_internal_flow_tracking_fits_every_width (80-82), Violet_specifications_with_internal_flow_tracking_fit_every_width (84-86), Run_report_without_internal_flow_tracking_fits_every_width (89-91), Run_report_with_wide_content_fits_every_width (96-98), Run_report_with_wide_content_fits_every_width_under_wcag_text_spacing (115-117), Run_report_with_internal_flow_tracking_fits_every_width_under_wcag_text_spacing (123-125) | c |

`Sweep` (131-177) is run-report-shaped: waits for details.feature (148), clicks .search-help-toggle (160) and the
phone toggles (156-157); `Measure` (179-288) reports a problem when .filtering-box/.filtering-box-export are missing
(203-205) or no .diagram-toggle is visible (214-215). It cannot sweep a standalone History or diagnostics document
without a new mode. Summary (100-103) names History and report diagnostics (stale after the move).

No other E2E file references history markup, verdicts, `$flaky`, report-diagnostics or DiagnosticsOpen
(ScenarioContentWidthTests:107 only has "history" inside a URL; WikiGifTests Feature13 "Failure Diagnostics" is
failure clusters and is skipped). StableIdDeepLinkTests ([Collection(Scenarios)], Remainder) is c.
tests/TestTrackingDiagrams.Tests.EndToEnd/ (tracked residue: ReportTestHelper.cs, DiagramNotePlaywrightBase.cs, no
csproj) references neither feature.

---------------------------------------------------------------------------------------------------------------

## 4. Tool tests and drift tests

- **merge --history**: MergeCommandTests.Merge_with_a_ledger_renders_history_and_never_writes_to_it (section 1.3).
  The merge usage text for --history (MergeCommand.cs:357, 382-384: "the History section, sparklines and verdict
  pills in the HTML") is pinned by NO test: MergedJsonOutputTests.The_usage_text_names_the_second_file_and_the_flag
  (186-193) pins only "--no-json"; CommandTableTests pins help routing only.
- **ingest --diagnostics-section**: IngestCommandTests.cs:466 (exercised) and :476 (usage pin). Nothing else.
- **query history** and the ledger verbs, all (c): they read the ledger/JSON, never the HTML.
  QueryHistoryTests 29, QueryHistoryLedgerOnlyTests 17, QueryHistoryWholeTextTests 10, QueryHistoryHintsTests 13,
  QueryHistoryShortfallReplayTests 2, HistoryCommandTests 23, HistoryGateTests 16, HistoryMaintenanceTests 12,
  CountFlagTests 8 (its ("history", ["--history", "{ledger}"]) row at 46 is query history's flag), RetainedRunsTests
  11 (prints TestRunReport.html#sid- links, 106).
- **SkillDriftTests** (tests/Kronikol.Tests/Tool/SkillDriftTests.cs): drives only QueryCommand.PrintUsage/KnownFlags
  against templates/skills/kronikol-test-debugging/references/commands.md, the emitted agent instructions and the
  two skill copies. Neither merge/ingest flag nor any option is in scope; the skill text mentions none of them: c.
- **QueryScriptAdviceTests**:142 pins a FailuresDigest signature containing HistoryVerdicts: c.
- **PluginManifestTests** (tests/Kronikol.Tests/Packaging/PluginManifestTests.cs): version == Directory.Build.props,
  template pins behind it, manifest fields/keywords: c (moves only with the release version).
- **AgentInstructionsGeneratorTests**:55 pins "TestRunReport.html#sid-<stableId>": c. If the plan adds the new
  document(s) to the per-run CLAUDE.md/AGENTS.md (agent-instructions.md:7 "Never open ...html"), add a fact there.
- **Templates/HistoryAction* tests** (Credential 4, Gate 8, Read 7, Record 24, Save 8, Static 17, Tool 4): no
  TestRunReport.html content assertion: c.
- **Wiki / README**: no test reads ../Kronikol.wiki; DemoLinkTests reads README.md / nuget-readme.md only for the
  demo link. tools/wiki-links checks links (manual). All doc drift for this change is unguarded.

---------------------------------------------------------------------------------------------------------------

## 5. Golden / byte-identity / budget pins

| Pin | Moves? |
|---|---|
| Golden TestRunReport.html | none exists in this repo |
| ToggleDefaultsBaselineTests byte identity (263-284) | survives (self-relative); fixture args 256-258 change if the parameters go |
| Stylesheet / script byte budgets | none |
| Report size budgets | none (only Failures.* caps: DigestJsonlCapTests:47, DigestSuppressionCapTests:74, c) |
| Render-perf budgets (ContentionScale, ContentionScaleTests, FilterPerformanceTests, BrowserRenderWorkerTests, PlaywrightTestBase helpers) | time-based on pages without history: c |
| StylesheetRulesTests | no fact names a .history-* or .report-diagnostic* selector; generic facts iterate AllSheets (22-30). If the new document gets its own sheet, add it to AllSheets so Every_selector_in_the_built_in_sheets_is_well_formed and No_min_width_bound_leaves_a_gap_or_an_overlap_at_the_phone_breakpoint cover it. Doc comment 231-235 names the History section (stale) |
| RunRotationTests.A_failing_run_is_still_on_disk_after_the_green_run_that_followed_it (149-186) | exact top-level set with history ON (183-185): AGENTS.md, CLAUDE.md, Failures.jsonl, Failures.md, History.run.json, Run.json, TestRunReport.html, TestRunReport.json, TestRunReport.schema.json, query.cs. A history document written by default when history is on adds a name here (and to `kept` at 160 if it must rotate byte for byte) |
| RunRotationTests.The_manifest_lists_every_file_the_run_wrote_and_nothing_it_did_not (695-731) | disk == manifest is self-consistent if the new file goes through RunFileCollector; the named list (728) should gain it |
| tests/shared-vectors/search-index-vectors.json + SearchIndexVectorTests | no verdict vectors: c |
| HistoryHtmlTests class summary "byte for byte" | prose only; no byte check |

---------------------------------------------------------------------------------------------------------------

## 6. Kronikol4J parity / cross-language

- Inside this repo: only tests/shared-vectors/search-index-vectors.json (the deep-search porting spec,
  SearchIndexVectorTests.cs:11), with no verdicts; DataFormatParityTests is .NET XML/YAML parity of the diagnostics
  array (c). No in-repo test covers history/diagnostics HTML across languages.
- C:/Code/Kronikol4J (read-only grep): 0 of 59 goldens in kronikol4j-report/src/test/resources/parity contain
  history-sparkline or report-diagnostics; kronikol4j-report/src/main/resources/io/kronikol/report/assets/stylesheets.css
  has neither block (its copy predates 3.11.0). docs/REMAINING_PARITY.md already records cross-run history HTML as
  .NET-only (3.9.0-3.11.0 entry, around lines 1987-2005) and the 3.21.0 opt-in of both sections (around 1906-1916).
  Its ReportDiagnosticsTest / report-diagnostics.txt fixture (parity-harness/dotnet-capture/Program.cs:1391) is the
  console warning list, not the HTML section. Net: no K4J golden regenerates; add one ledger entry for the removal.

---------------------------------------------------------------------------------------------------------------

## 7. CI lanes (.github/workflows/ci.yml)

Matrix filters are substring matches: a `filter` becomes FullyQualifiedName~X|DisplayName~X per part (245-257);
E2E (Remainder) uses `filter-exclude`, FullyQualifiedName!~X per part (258-267).

| Test class | Project | Lane |
|---|---|---|
| HistorySectionTests, HistorySparklineTests, HistoryFilterTests, HistoryExportTests | Kronikol.Tests.EndToEnd | **E2E (Remainder)** (ci.yml:133-136; no History* name in its exclude list) |
| ReportSectionWidthTests, ViewportSweepTests | Kronikol.Tests.EndToEnd | **E2E (Toolbar & Reports)** (ci.yml:104-107) |
| AdvancedSearchTests (status-search coverage relied on above) | Kronikol.Tests.EndToEnd | E2E (Search & Filters) (100-103) |
| StableIdDeepLinkTests, ToggleDefaultsTests, ExportFilteredHtmlRenderingTests | Kronikol.Tests.EndToEnd | E2E (Remainder) |
| HistoryHtmlTests, HistoryOutputsTests, MergeCommandTests, IngestHostDiagnosticsTests, IngestCommandTests, ToggleDefaults*Tests, ReportToggleDefaultsResolverTests, ReportGeneratorDiagnosticsScopeTests, RunRotationTests, DiagnosticReportGeneratorTests, StylesheetRulesTests | Kronikol.Tests | **Core Tests** (65-66), **Core Tests as root (Linux container)** (46-55), and release.yml:37 (`dotnet test tests/Kronikol.Tests`) |
| VerdictSearchTests, VerdictDeepSearchTests | Kronikol.Tests.SearchEngine | **Remaining Unit Tests (auto-discovered)** (79-80; step 277-291, SKIP list at 283) |
| Templates/* | Kronikol.Tests | also Template Actions (Windows/macOS) (139-146, filter Kronikol.Tests.Templates): unaffected |

A new E2E class for the new document lands in E2E (Remainder) automatically. Moving it to a named lane means
editing that lane's `filter` AND Remainder's `filter-exclude` together.

---------------------------------------------------------------------------------------------------------------

## 8. Test helpers with history or diagnostics parameters

| Helper | Where | History/diagnostics hook |
|---|---|---|
| HistoryReportHelper.Generate | tests/Kronikol.Tests.EndToEnd/HistoryReportHelper.cs:51-105 | seeds ledger in tempDir; `history:` + `showHistorySection: true` (99-101); `tweak`, `wide` |
| ReportTestHelper.GenerateReportWithEverySection | tests/Kronikol.Tests.EndToEnd/ReportTestHelper.cs:2798-2887 | seeds ledger; `diagnostics:` (2878), `history:` (2881), `showHistorySection: true` (2882), `showReportDiagnostics: true` (2883) |
| HistoryHtmlTests.Verdicts / VerdictsInto / Html / ScenarioHead | tests/Kronikol.Tests/History/HistoryHtmlTests.cs:50-90 | `showHistorySection: true` (80) |
| HistoryOutputsTests.Options / Run / Seed / SeedTrunk | tests/Kronikol.Tests/History/HistoryOutputsTests.cs:30-81, 336-354 | HistoryFilePath etc.; facts flip ShowHistorySection / EmbedHistoryInReport / HistoryCompareBranch through `configure` |
| IngestHostDiagnosticsTests.Request | tests/Kronikol.Tests/Ingestion/IngestHostDiagnosticsTests.cs:39-64 | `showSection = true`, sets ShowReportDiagnosticsSection (46) |
| ToggleDefaultsBaselineTests.GenerateTestRunShape (+ BuildRichFixture) | tests/Kronikol.Tests/Reports/ToggleDefaultsBaselineTests.cs:245-261 | `diagnostics:` (256), `showReportDiagnostics: true` (258); reused by ToggleDefaultsMarkupTests.GenerateRich (387-400) |
| JintTestBase.CallMatch / CallEvaluate / SetVerdicts | tests/Kronikol.Tests.SearchEngine/JintTestBase.cs:69-111 | `string[]? verdicts` |
| MergeCommandTests (inline) | tests/Kronikol.Tests/Reports/Merge/MergeCommandTests.cs:200-209 | inline ledger seeding for `--history` |

Compile impact if GenerateHtmlReport loses `diagnostics`/`history`/`showHistorySection`/`showReportDiagnostics`:
HistoryHtmlTests, HistoryReportHelper, ReportTestHelper.GenerateReportWithEverySection, ToggleDefaultsBaselineTests.
Direct calls into internals: HistoryHtml.Section (HistoryHtmlTests:149), ReportGenerator.RenderReportDiagnostics
(IngestHostDiagnosticsTests:162, 175). If DiagnosticsOpen goes: ReportToggleDefaultsResolverTests:49 and
ToggleDefaultsMarkupTests:466 stop compiling.

---------------------------------------------------------------------------------------------------------------

## 9. Unaffected history tests (c), for the record

tests/Kronikol.Tests/History: DegradedRunTests 18, HistoryAnalyzerTests 55 (incl. HistoryVerdictNames and
HistorySummary.Line pins at 643-646 and 827-832, shared with Failures.md/CTRF/query), HistoryAsOfTests 19,
HistoryLedgerTests 39, HistoryPathResolutionTests 11, HistoryRunBuilderTests 11, HistoryShapesTests 15,
InteractionShapeTests 22, PartialRunBaselineTests 14, ShapeTemplateRulesTests 12; HistoryOutputsTests 15 of 19.
Ingestion: IngestAttemptTests.History_and_CTRF_read_the_retry_from_the_ingested_scenario (61), MergeBeforeDropTests.
Tool and Templates suites as listed in section 4. test.runsettings files set KRONIKOL_HISTORY=off (unchanged).

---------------------------------------------------------------------------------------------------------------

## 10. Outside tests (not pinned by any test, but the plan touches them)

- Wiki: Cross-Run-History.md 16-19, 553-593, 634-635, 825; Report-Configuration.md 178, 209-210, 252;
  Search-Syntax.md 129-166; Merging-Parallel-Reports.md 64, 276; Ingesting-External-Captures.md 644-645;
  Diagnostics-and-Debugging.md 147-153; FAQ.md 107-108. README.md:16 and nuget-readme.md:65 advertise the sparkline,
  verdict and `$flaky` in the report.
- Research scripts (not CI): tools/history-bench/jsd/export.js (stub <section id="history-section"> for the export
  question), tools/history-bench/dsl.js (evaluates advanced-search.js verdict claims).
- Pre-existing absence facts avoided bare names because the stylesheet and scripts carried them
  (HistoryHtmlTests.cs:199-200, HistoryOutputsTests.cs:301 and 318, IngestHostDiagnosticsTests.cs:142). With the CSS
  and JS gone, one whole-file absence fact can assert that "history-", "data-history-verdicts" and
  "report-diagnostic" do not occur in TestRunReport.html (and Specifications.html, which embeds the same stylesheet)
  even with a ledger and diagnostics present. Keep `history.replaceState` out of the pattern.
- Semver note from CLAUDE.md: removing public options/members/CLI flags is MAJOR (reserved for v5); the repo's
  precedent is mark [Obsolete] in a minor, remove in the major, pin the removal with a reflection test
  (MonospaceNoteControlRemovedTests).
