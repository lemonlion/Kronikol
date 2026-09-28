# CI flake sweep, 2026-09-28

**Asked:** "Flaky tests should definitely be fixed" (the owner, 2026-09-28), after the session that deepened
`PR_REPORT_LINK_PLAN.md` found `main`'s CI red on a flaky fact. **Done:** 3.32.1 (`45d0f1f0`, on `main` at `f1647509`
with this record's commit after it). **Owed to the owner**, because this environment cannot push either: the wiki patch
beside this file (`CI_FLAKE_SWEEP_2026-09-28.wiki.patch`, one paragraph of `Integration-TcpTap-Extension.md`, made on
the wiki at `5bc9589`; `git am` it in the wiki checkout), and the tag `v3.32.1` once CI is green on the release.
Both done on 2026-09-28 by a local session at the owner's word: the wiki patch as `7da25b1`, and the tag `v3.32.1` on
`1109e7b`, a head carrying the audited 3.32.0 too (`QUERY_FALLBACK_PLAN.md` §11.7), whose Release run `36454930897`
pushed all 62 packages, all listed on nuget.org.

## Method

- Every run of `.github/workflows/ci.yml` on `main` from 2026-09-12 18:51Z to 2026-09-28 12:11Z: 135 runs, 22 red
  (READ, the Actions API). Also the only two runs in the repository that failed on their first attempt and passed on a
  re-run of the same commit, the surest sign of a flake. The failing tests were read out of each failed job's log.
- A **regression** failed on consecutive commits, or a later commit changed the code or test it names; a **flake**
  failed once on a commit that did not touch it, or passed on a re-run of the same commit.
- Six local runs of all of `Kronikol.Tests` on 3.32.0 (RUN): 6,040 tests each, the same 6,032 passed, 5 skipped and 3
  failed every time. The three expect a write to a read-only file to be refused, and this machine runs as root.

## What failed, and what fixed it

| Date | Run(s) | Failing | Kind | Fixed by |
|---|---|---|---|---|
| 09-12 | 34712464439, 34715835354 | `RunEndPointerTests.Agents_line_points_at_the_instruction_file_and_forbids_the_json` (Core) | regression, two runs | a later commit (INFERRED: green since) |
| 09-12 | the same and 34720191355, 34721367653 | `ComponentDiagramLabelWidthTests.A_wrapped_participant_name_stays_bold_and_leaks_no_creole_markers` (IKVM) | regression, four runs | a later commit (INFERRED) |
| 09-12 | 34720191355, 34721367653 | seven `StableIdDeepLinkTests` (E2E Remainder) | regression, two runs | a later commit (INFERRED) |
| 09-13 | 34754582939 | `TcpTapTests.AnIdleConnectionWithNothingUnansweredIsNeverReaped` | **flake**: 3.4.1 (`58a3862f`) put it down to a dropped segment; the reply-overtakes-command race below fits it better (INFERRED) | 3.32.1 |
| 09-13 | 34754582939 | `ReqNRollDuplicateStepsTests.Outline_example_records_exactly_the_feature_file_steps(ReqNRoll.xUnit3)` (Integration) | regression: `Specifications.yml` sub-steps | `58a3862f` (3.4.1) |
| 09-13 | 34756205141 | Template Scaffolds: NU1102, `Kronikol.BDDfy.xUnit3 >= 3.4.0` not yet on nuget.org | release ordering, not a test | none needed |
| 09-13 | 34761167629 | `PagerContractTests.An_out_path_that_is_not_a_path_is_a_sentence_not_a_stack_trace("   ")` | regression, by operating system | `c26f935f` (3.5.1) |
| 09-14 | 34820360412 | `AssertionToggleTests.Assertions_survive_details_state_change` (E2E Remainder) | one failure, not seen again in two weeks; not diagnosed (E2E cannot run in this environment) | none |
| 09-14 | 34820360412 | `StepBarPlantUmlTests.StartStep_with_an_explicit_table_draws_it_in_the_bar` | **flake**: the request log emptied under it | an explicit clear removed in `b5cff009`; the ingests' clears in 3.32.1 |
| 09-14 | 34824757416, 34824973234 | `ReproTests.Failures_prints_where_it_was_thrown_and_how_to_rerun_it` (a Windows path read on Linux) | regression, two runs | a later commit (INFERRED) |
| 09-14 | 34857760928 | `OtlpExporterTests.An_unreachable_endpoint_is_counted_not_thrown` | **flake**: a port reused | `83e6d73e` |
| 09-14 | 34876770792 | two `HistoryOutputsTests` facts | regression (CI's own run stream) | 3.21.0 to 3.25.1 (INFERRED) |
| 09-14 | 34899211911, attempt 1 | `StaleOutputTests.The_pointer_does_not_name_the_previous_runs_file_when_this_run_could_not_replace_it` | **flake**: another run's pointer in the console capture | `d9eff2d4` (`ConsoleLines`) |
| 09-15 | 34946693240 | `IngestPipelineTests.Responses_follow_their_requests_by_default_so_concurrent_calls_stay_paired` | one failure (extra marker records); the test was rewritten since | 3.27.4 to 3.29.1 rewrote it |
| 09-15 | 34948756783 | `TcpTapTests.TheNdjsonSinkWritesReplayableRecords` | **flake**: the race below | 3.32.1 |
| 09-22 | 35704781520, attempt 1 | `MongoDbTrackingSubscriberTests.The_flow_that_wrote_a_scenarios_document_keeps_the_scenario_until_it_touches_another` | **flake**: the correlation store cleared by `ChangeStreamCorrelationTests` | `b5cff009` |
| 09-25 | 36127301528 | Release Build: NU1605, a package downgrade in `Kronikol.Extensions.ServiceBus` | build break | the next commit |
| 09-27 | 36315955586 | Release Build: CS0117, `Convert.ToHexStringLower` | build break | the next commit |
| 09-27 | 36323047132 to 36342769330 | `ArrowLinkOpensPopupTests` (2, 1) (E2E Popups & Flows) | regression, four runs | `571a98dc` (3.31.10) |
| 09-27 | 36344296790 | `ProcessGlobalStoreTests` rejecting a fix that cleared the log | regression | `571a98dc` |
| 09-27 | 36348913904 | `NodeJsPlantUmlRendererTests.Code_cache_is_created_on_first_run_reused_afterwards_and_regenerated_when_v8_rejects_it` | **flake**: the machine-wide code cache rewritten under it | `28e44604` |

## The two live flakes, and what 3.32.1 did

**1. TcpTap mis-paired replies on a busy connection (a product bug).** Both directions of a connection feed one decode
queue, and each pump forwarded a read before queueing its copy, so a server answering at once could have its reply
queued ahead of its command. The Redis decoder matches replies in order, so from there on every command on the
connection was recorded with the next one's reply, and the last was left unanswered for the reaper. Measured through a
`RedisTap`, 16 connections of 1,500 GETs against a stub echoing each key, on 3.32.0 against 3.32.1: 6,785 of 23,991
recorded exchanges carried another command's reply and the last exchange was lost on 9 of 16 connections, against none
and none (RUN). Under CPU load, four runs on 3.32.0 each lost the last exchange on 16 of 16 connections, and four on
3.32.1 lost none. The
fix queues each read before forwarding it. `PumpOrderTests` pins the order (red on 3.32.0, deterministic) and
`BusyConnectionTests` the outcome (8 x 1,000 GETs, red on 3.32.0 in 2 of 2 runs).

**2. Readers of the request log outside the collection that clears it (tests only).** `IngestPipeline.Run` clears the
process-global request log by default, and all 17 test classes that run it, directly or through `IngestCommand`, are in
the DiagramsFetcher collection. `StepBarPlantUmlTests`, `TestDelimiterTests` and `CapturedTextEscapeTests` read the log
back from outside it, so an ingest could empty it between their write and their read, the shape of
`StepBarPlantUmlTests`' 2026-09-14 failure. The first two joined the collection; `CapturedTextEscapeTests` reads the
delimiter's statement from a new internal function instead of the log, because moving its 9 s into the collection would
have lengthened the suite's critical path (the collection holds 73 classes and 56 s of its 66 s). A second fact in
`ProcessGlobalStoreTests` keeps every reader and every ingest in the collection; it was red before, naming the three.

## Left as it is

- The three tests that fail only as root (`InitAgentsCommandTests`, `RunRotationTests`, `StaleOutputTests`): not flaky,
  and CI never runs as root. Skipping them under root, as the Windows-only tests skip elsewhere, waits on the owner.
- `AssertionToggleTests.Assertions_survive_details_state_change`: one failure in two weeks of E2E runs. Undiagnosed;
  the E2E tests that draw a diagram cannot pass in this environment, whose browser cannot reach jsDelivr (as the 3.31.10
  release commit records).
