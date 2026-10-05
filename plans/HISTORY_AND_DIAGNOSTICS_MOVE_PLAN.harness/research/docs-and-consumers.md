# Research C: documentation and external-consumer inventory

Scope: moving all cross-run history UI out of `TestRunReport.html` (the History section
`<details id="history-section">`, per-scenario sparklines, verdict pills, `data-history-verdicts`, the
`$verdict` search sigil; options `EmbedHistoryInReport` (default true) and `ShowHistorySection`
(default false since 3.21.0); `kronikol merge --history`) and the "Report diagnostics" section
(`ShowReportDiagnosticsSection`, `ReportToggleDefaults.DiagnosticsOpen`, `kronikol ingest
--diagnostics-section`) into a separate document.

Snapshot taken 2026-10-05, read only, nothing modified anywhere:

- Kronikol `C:/Code/Kronikol`: `main` at `425d9bad` (4.5.0 released), tree clean.
- Wiki `C:/Code/Kronikol.wiki`: `e164dbc` (4.5.0); 101 pages + `_Sidebar.md` + `images/`.
- Kronikol4J `C:/Code/Kronikol4J`: `84132db` (one untracked file, `docs/OTLP_TAP_PLAN.md`). Its wiki:
  `C:/Code/Kronikol4J.wiki`.
- BreakfastProvider `C:/Code/BreakfastProvider`: the LOCAL checkout is stale (HEAD `0d29537` = Kronikol
  3.29.0; the local `origin/main` ref is `fbec5ab` = 3.31.9). The live repository was read with `gh api`
  and `gh search code` (read only): `main` = `106702b` (2026-09-30), on Kronikol 4.0.2. Section 6 is
  from live `main` unless marked "local".

---

## 0. What is being moved, by name (for grep and for the plan's file list)

| Thing | Where it is emitted / read |
|---|---|
| History section | `src/Kronikol/History/HistoryHtml.cs:105` `<details id="history-section" class="history-section"[ open]>`, summary `<summary class="h2">History <span class="history-summary-line">` (106). `HistoryHtml` is `internal static class` (21). It opens itself when `history.HasAnything` (104); there is no toggle default for it. Placed by `ReportGenerator.cs:1836-1840` only when `history is not null && showHistorySection`. Its lists link scenarios as `a.history-link[href='#sid-...']` |
| Sparkline | `HistoryHtml.cs:71` `<span class="history-sparkline" role="img" aria-label=... title=... style="background:linear-gradient(90deg,...)">` (one node per scenario) |
| Pill | `.history-verdict-*` classes, `stylesheets.css:1316-1373` (History section rules at 1366, 1373; sparkline at 1317, 1995) |
| `data-history-verdicts` | `HistoryHtml.cs:36`, `:47` (scenario `<details>` and outline rows) |
| `$verdict` search | `advanced-search.js:249-294` (the 5th `verdicts` argument of `advancedSearchEvaluate` / `advancedSearchMatch`), `report-scenario-feature-map-helper.js:14-16` (reads the attribute), `report-search-function.js:62`, `report-search-index.js:251-253`, `:358`, `:481` (worker item payload) |
| Report diagnostics block | `ReportGenerator.cs:4112-4136`: `<details class="report-diagnostics"[ open]>` (**no id**), `<summary>Report diagnostics (N: Kind xk, ...)</summary>`, `<ul class="report-diagnostics-list">` with `report-diagnostic-kind-<kind>` badges; gated at `ReportGenerator.cs:1824-1828` (`includeTestRunData && showReportDiagnostics && diagnostics.Count > 0`), open state from `toggles.DiagnosticsOpen`; CSS `stylesheets.css:1262-1287` |
| Public API | `ReportGenerator.GenerateHtmlReport(..., IReadOnlyList<DiagnosticEntry>? diagnostics, ..., HistoryVerdicts? history = null, bool showHistorySection = false, bool showReportDiagnostics = false)` at `ReportGenerator.cs:1026-1069`; `MergeableReportRenderer.Render(..., History.HistoryVerdicts? history)` `MergeableReportRenderer.cs:24`, options passed at 94-95; `MergedRunOutputs.Write(..., history)` `MergedRunOutputs.cs:39` |
| Options | `ReportConfigurationOptions.cs:421-429` (`ShowReportDiagnosticsSection`), `:760-765` (`EmbedHistoryInReport = true`), `:766-776` (`ShowHistorySection`); `ReportToggleDefaults.cs:87-89` (`DiagnosticsOpen`), resolver `ReportToggleDefaultsResolver.cs:37`, `:88` |
| CLI | `MergeCommand.cs:56-58` (`--history`), `:111-114` (`options: new ReportConfigurationOptions { ShowHistorySection = history is not null }`, so `kronikol merge` can never render the diagnostics block, and no flag exists for it), help text `:357`, `:382-385`. `IngestCommand.cs:167` (`--diagnostics-section`), `:392`, help `:686-692` |

**Three different things share the word "diagnostics", and the docs mix them:**

1. Console lines from `ReportDiagnostics.Analyse()`, printed by `ReportGenerator.cs:612-617` to the
   console only. The first one is literally `Report diagnostics: N log entries across M test(s).`
   (`ReportDiagnostics.cs:39`), the same name as the HTML block.
2. Structured `DiagnosticEntry` records: `IngestResult.Diagnostics`, the `diagnostics` array of
   `TestRunReport.json` (schema `$defs/diagnostic`), `kronikol query summary`'s Diagnostics section and
   the provenance lines above query answers, and the HTML "Report diagnostics" block (the thing being
   moved).
3. `DiagnosticMode = true`: a separate `DiagnosticReport.html` (`ReportGenerator.cs:619-620`,
   `DiagnosticReportGenerator`). Already its own document: a candidate home, or sibling, for the moved
   block.

---

## 1. Wiki (`C:/Code/Kronikol.wiki`)

Legend: **REWRITE** = section-level rewrite or move; **LINE** = a sentence or table row;
**COND** = only if `DiagnosticMode` / `DiagnosticReport.html` is merged, renamed or re-homed;
**NONE** = mentions history or diagnostics but not the HTML surfaces.

### 1.1 Pages that must change

**`Cross-Run-History.md`** (876 lines): the main page, **REWRITE**.

- 13-19, "Status by release": "3.11.0 renders history in the HTML report (a sparkline and verdict
  beside each scenario, a History section beside the timeline, `$flaky` in the search box), adds `merge
  --history` ... 3.21.0 leaves the History section out of the report unless `ShowHistorySection` asks
  for it". LINE (paragraph).
- 551-586, `## In the HTML report (3.11.0)`: the sparkline (colour stops, tooltip), the pill vocabulary
  (`broke`, `failing`, `always-failing`, `fixed`, `flaky`, `new`, `slower`, `behaviour-changed`,
  `quarantined`), parameterised groups, the History section's contents (summary line, pass-rate and
  duration trends, the lists linked by `#sid-`, the cold-start line), the search box's `$` sigil,
  Export Filtered HTML keeping sparklines and pills and dropping the section, the 3.21.0 opt-in,
  `merge --history` turning the section on, `EmbedHistoryInReport = false`, `Specifications.html` gets
  none. **REWRITE / move.** Its anchor `#in-the-html-report-3110` is linked from
  `Generated-Reports.md:498`.
- 588-603, `### Which runs a run is read against`: nested UNDER the HTML heading, but it is about the
  as-of window (3.25.1, #95) that `query history`, `history gate` and `merge --history` use. Its anchor
  `#which-runs-a-run-is-read-against` is linked from `Querying-Reports.md:959`. Re-parent it and keep
  the slug.
- 634-640, "Merged reports": "`kronikol merge <shards> --history <ledger>` (3.11.0) renders the merged
  report against the ledger: the History section, sparklines and pills in the HTML, the history lines
  in `Failures.md`, the summary in the pointer". LINE / REWRITE.
- 367: "`kronikol history gate`, `kronikol query history` and `kronikol merge --history` read the same
  way on a pull request build": NONE.
- 110 (the action table's `read` row): "so the run's own report, `Failures.md` and job
  summary carry the verdicts". LINE at most.
- 798-826, `## Options`: row 825 `EmbedHistoryInReport | true | render history in TestRunReport.html
  (3.11.0)`. LINE. **`ShowHistorySection` has no row in this table** (pre-existing gap: it appears only
  in prose at 19, 563, 580).
- 399-550, `## The verdicts` (anchor `#the-verdicts`, linked from `Search-Syntax.md:138` and
  `Querying-Reports.md:1085`): the vocabulary, NONE, but it is what the moved UI renders.
- 867-876, `## Diagnostics` (`HistoryUnavailable`, `HistoryPartialRun`, `HistoryShapeTemplate`,
  `HistoryLedgerDamaged`): no HTML mention, NONE; these entries reach a reader through the surfaces in
  section 0, point 2.

**`Report-Configuration.md`**: five rows.

- 178, `ShowReportDiagnosticsSection` row ("Render the 'Report diagnostics' section in
  `TestRunReport.html` ... Off by default since 3.21.0 ... `ReportToggleDefaults.DiagnosticsOpen`
  decides whether the section starts open and is inert while this is `false`"). REWRITE row.
- 205, `HistoryCompareBranch`: "reported beside the run's own reading on the digest, the pointer and
  the report's History section (`on main: 1 broke (against 12 earlier runs on main)`)". LINE.
- 209, `EmbedHistoryInReport` row ("Render the history the run read in `TestRunReport.html` (3.11.0): a
  sparkline and verdict pill beside each scenario, and the History section when `ShowHistorySection`
  asks for it. With no ledger the report is byte-for-byte what it was. Since 3.9.0."). REWRITE row.
- 210, `ShowHistorySection` row ("beside the timeline ... Off by default since 3.21.0 ... `kronikol
  merge --history` turns it on"). REWRITE row.
- 252, Toggle Default Start States table: `DiagnosticsOpen | bool? | false | The report-diagnostics
  disclosure (test run report only). Inert unless ShowReportDiagnosticsSection renders the section`.
  REWRITE row. (No toggle exists for the History section.)
- COND: 59-60 (code sample: `ActivitySourceDiscovery`, `DiagnosticMode`), 176 (`ActivitySourceDiscovery`;
  "report diagnostics" here means the console lines), 177 (`DiagnosticMode` row, `DiagnosticReport.html`).
- **Pre-existing gap:** five history options exist in code and in Cross-Run-History's table but have no
  row here: `HistorySlowerMinMs` (`ReportConfigurationOptions.cs:653`, 3.16.0), `HistoryAlternatingRuns`
  (`:662`, 3.18.0), `HistoryCountRuns` (`:673`, 3.20.0), `HistoryShapeTemplates` (`:691`, 3.25.0),
  `HistoryDegradedBy` (`:703`, 3.24.0). `CROSS_RUN_HISTORY_PLAN.md` §15.1: "An option not on this page
  does not exist".

**`Search-Syntax.md`** (the search help page)

- 123-143, `### History verdicts` (under `## Status Filtering ($status)` at 103): `$flaky`, `$broke`,
  `$failed && !!$flaky`, `$passed && $flaky`, `$stable`, the 13-word vocabulary, the collision rule, "A
  report generated with no ledger carries no verdicts". REWRITE or move. Its anchor `#history-verdicts`
  is linked from `Cross-Run-History.md:574`.
- 165-166, Examples table rows `$flaky` and `$failed && !!$flaky`. LINE (remove or move).
- The in-report search help panel (`ReportGenerator.cs:1614-1626`) lists only `$status` with
  `$failed`, `$passed`, `$skipped`: it never documented verdicts, so nothing to remove there.

**`Generated-Reports.md`**

- 486, "Layout by window width" bullet: "...a test reported by its method's full name in the failure
  clusters and the History section, the History section's branch, ..." (3.29.4). LINE.
- 498, Export Filtered HTML bullet: "The cross-run history sparklines and verdict pills live in the
  scenario headers and are carried; the aggregate History section sits beside the timeline and is left
  behind with it (3.11.0)", linking `Cross-Run-History#in-the-html-report-3110`. LINE.
- 500, toggle defaults bullet ("the disclosure sections", generic). NONE or LINE.
- 79-85, `## TestRunReport.html` contents list: mentions neither section; add a pointer to the new
  document. LINE.
- 3-24, `## Output Files Summary`: needs a row for the new document. `DiagnosticReport.html` has no row
  today (gap). **Pre-existing bug:** line 21 is a blank line inside the table (wiki `b20ea14`, 3.27.0),
  so the `Run.json` and `runs/<run>/` rows (22-23) do not render as table rows.
- 886-918, `### GenerateHtmlReport`: the signature shown is stale. It stops at `titleizeParameterNames`;
  the real one (`ReportGenerator.cs:1026-1069`) has about 20 more parameters, including `diagnostics`,
  `history`, `showHistorySection` and `showReportDiagnostics`. Rewrite it if those public parameters
  change.

**`Diagnostics-and-Debugging.md`**

- 66-80, `### Cross-run history diagnostics (3.9.0)`: line 69 "one of three diagnostic kinds in the
  report's diagnostics panel and `TestRunReport.json`". The panel has been off by default since 3.21.0,
  and the table lists three kinds where Cross-Run-History lists four (`HistoryShapeTemplate`, 3.25.0).
  LINE.
- 127-158, `### Host diagnostics`: 146-148 "land in three places"; item 2 (147) is the HTML "Report
  diagnostics" block (under the summary after the failure clusters, the summary format, amber and red
  kind badges, "From 3.21.0 the block is off unless `ShowReportDiagnosticsSection = true` asks for it
  (`kronikol ingest --diagnostics-section` on the CLI)"); 150-156 the CLI form with a
  `--diagnostics-section` example; 157 "no section in the HTML". REWRITE.
- 86: "`ReportDiagnostics.Analyse()` produces human-readable strings for the footer". They are console
  lines, as line 46 of the same page says ("They are not written into the report"). LINE (pre-existing
  ambiguity).
- 44-64, `## Report Diagnostics` (the console lines): NONE, but the heading collides with the HTML
  block's name.
- COND: 1-15 (intro table), 17-40 (`## Diagnostic Mode`, the `DiagnosticReport.html` sections table),
  259-300 (TrackingComponentRegistry, `### Diagnostic Report Details`), 451.

**`Ingesting-External-Captures.md`**

- 640-657, `### Host diagnostics`: "appear in the collapsed 'Report diagnostics' block of
  `TestRunReport.html` when `ShowReportDiagnosticsSection` (or `kronikol ingest --diagnostics-section`)
  asks for it, off by default from 3.21.0"; 655-657 "With an empty list nothing changes: no HTML block".
  LINE / paragraph.
- 255-273, the CLI flags table: has `--diagnostic <kind>:<message>` (273) and **no
  `--diagnostics-section` row** (pre-existing gap: the flag shipped in 3.21.0).
- 669-676, `## History for ingested runs (3.9.0)`: `Failures.md` and the ledger only. NONE.

**`Merging-Parallel-Reports.md`**

- 64, the `--history <ledger>` row: "the History section, sparklines and verdict pills in the HTML
  (asking for history is what turns the section on, which a report is otherwise written without from
  3.21.0), the history lines in `Failures.md`, the summary in the pointer". REWRITE row.
- 274-279, Limitations: "Without `--history` the merged report says nothing about earlier runs". LINE,
  maybe.
- Nothing says that a merge cannot render the diagnostics block (`MergeCommand.cs:114`).

**`FAQ.md`**

- 101-110, "Does Kronikol detect flaky tests?": 106-108 "It shows in `Failures.md`, the CTRF document,
  the run-end pointer, `kronikol query history`, and in the report as a sparkline and verdict beside
  each scenario with `$flaky` in the search box." LINE.
- COND: 33, 88-95 (`DiagnosticMode`, "generates a `DiagnosticReport.html` alongside your test report").

**`Home.md`**

- 62, the Cross-Run History bullet: "...a sparkline and verdict beside every scenario, a History
  section (off by default from 3.21.0, `ShowHistorySection` brings it back) and `$flaky` in the report,
  and `merge --history` (3.11.0)". LINE. (Line 5 links Migrating to v4.)

**`_Sidebar.md`**

- 134 `* [[Cross-Run History]]` (under **Features**), 105 `[[Search Syntax]]`, 139 `[[Diagnostics and
  Debugging]]`. Add a link if the new document gets a page of its own.
- **Pre-existing bug** (since 3.9.0, wiki `5f98f7f`): `[[Cross-Run History]]` was inserted between
  `[[Querying Reports]]` (133) and its four children (135-138: Aggregation (values), Using It from an AI
  Agent, Installing the skill, `kronikol ctrf`, all `Querying-Reports#...` links), so they render
  nested under Cross-Run History.

**`Capture-Time-Redaction.md`**

- 10-15 lists "any file derived from" captured data (`TestRunReport.json` and siblings, the mergeable
  JSON, any NDJSON sink, `CiSummary.md`, `Failures.md`/`.jsonl`, `ctrf-report.json`). A new document
  that carries failure keys or diagnostic messages belongs on it. (`TestRunReport.html` and
  `DiagnosticReport.html` are not on it today.) 71-80 `## The history ledger (3.9.0)`. LINE.

**`Integration-TcpTap-Extension.md`**: 284 ("so the fact reaches the report, not just a log file"), 307
("worded for the report"). Generic, LINE at most.

### 1.2 Pages that mention only `DiagnosticMode` / `DiagnosticReport.html` (COND)

`API-Reference.md:53` (`ReportDiagnostics` "returns diagnostic warning strings for the report footer":
stale wording, they are console lines) and `:54` (`DiagnosticReportGenerator`);
`AI-Integration-Prompt.md:10`; `Background-Thread-Correlation.md:494`;
`Event-Driven-Architecture-Testing.md:225`; `HTTP-Tracking-Setup.md:91`;
`Integration-BigQuery-Extension.md:302`; `Integration-BlobStorage-Extension.md:226`;
`Integration-CosmosDB-Extension.md:473`; `Integration-Dapper-Extension.md:270`;
`Integration-DispatchProxy-Extension.md:204`; `Integration-EF-Core-Relational-Extension.md:455, 460,
613-627`; `Integration-Elasticsearch-Extension.md:172`; `Integration-Kafka-Extension.md:544`;
`Integration-MassTransit-Extension.md:204`; `Integration-Redis-Extension.md:314`;
`Integration-S3-Extension.md:245`; `Integration-ServiceBus-Extension.md:211`;
`Integration-StorageQueues-Extension.md:225`; `FAQ.md:33, 92-95`; `Diagnostics-and-Debugging.md:10-40,
269-285, 451`. (The integration pages all say "surfaced as console warnings and in the diagnostic
report (when `DiagnosticMode=true`)".)

### 1.3 Pages that mention history but not the HTML surfaces (NONE)

`Querying-Reports.md` 949-1185 (`kronikol query history`; links the two Cross-Run-History anchors above
at 959 and 1085; 1163-1182 "The run's own diagnostics lead the answer" and `summary`'s Diagnostics
section, a surface for diagnostics that survives the move); `CI-Summary-Integration.md` 62, 313 (the
`history:` line of "Debug this run"); `CI-Artifact-Upload.md` 88; `Project-Templates.md` 154-158;
`Ingesting-From-Jest.md` 30; `Exporting-to-OpenTelemetry.md` 36-58, 138 ("verdict" there is the test
result); `Generated-Reports.md` 20 (the `History.run.json` row).

`Migrating-to-v4.md` (52 lines): nothing about either section (34 is the history action's `VERSION`).
It is the template for the "Migrating to v5" page `ROADMAP.md` 12.1 asks for, if any of this lands as a
major.

### 1.4 Images and screenshots

None to retake. `images/` holds 18 files (`whats-new-*.gif` / `.png`), all committed 2026-04-17 to
2026-04-26 for "What's New in 2.0" (v2.x). No current page references them, and none can show history
(3.11.0, 2026-09-14) or the diagnostics block (3.0.45, 2026-08-22). The only image references in the
wiki are `Integration-PlantUML-IKVM.md:73-74` (`images/diagram_1.png` / `.svg`, which are not in the
folder). `README.md:37`'s example image (a user-attachments asset) was last changed 2026-03-23
(`e7c23a39`), before history existed.

### 1.5 Precedent, and the link checker

- The 3.21.0 wiki commit `b30e98b` ("Report sections off by default") touched six pages:
  Cross-Run-History, Diagnostics-and-Debugging, Home, Ingesting-External-Captures,
  Merging-Parallel-Reports, Report-Configuration. The 3.11.0 commit `4db2e51` touched twelve (also
  CI-Artifact-Upload, CI-Summary-Integration, Capture-Time-Redaction, FAQ, Generated-Reports,
  Project-Templates, Search-Syntax).
- `tools/wiki-links/wikilinks.py check` (run with `PYTHONUTF8=1`) checks `[[...]]` links only. The
  `[text](Page#frag)` and `[text](#frag)` links (most of the anchors above) are not checked.

---

## 2. Repo docs

### 2.1 README, NuGet readme, package readmes

- `README.md:16` (the opening paragraph): "A cross-run history ledger committed in your repository says,
  on every scenario, whether a failure is a regression, has been failing since a particular run, or
  flips - as a sparkline and verdict in the report, a line in `Failures.md`, and `$flaky` in the search
  box." LINE.
- `README.md:175-200` ("Debugging a run"): `kronikol query history`, `kronikol history gate`, the
  `kronikol-history` action. CLI only, NONE.
- `nuget-readme.md:65` (packed into every package by `Directory.Build.props:17`, `:31`): "**Cross-run
  history** - ... in `Failures.md`, the CTRF document, the report (a sparkline and verdict per scenario,
  `$flaky` in the search box) and `kronikol history gate`". LINE.
- `templates/README.md` (the `Kronikol.Templates` package readme, `templates/Kronikol.Templates.csproj:11`):
  77 "`read` fetches it before the tests, so each report says what is new". Generic, LINE at most.
- No other package has its own readme (every other csproj packs `nuget-readme.md`). There is **no
  `docs/` folder** in the repository.
- `tests/Kronikol.Tests/Packaging/DemoLinkTests.cs` and `PackageReadmeLinkTests.cs` read the readmes
  (demo link, link shapes); nothing found that pins the sparkline wording.

### 2.2 CHANGELOG.md: the version history of these features

| Version (date) | What happened |
|---|---|
| 2.x, by 2.3.0 (2026-04-21) | `DiagnosticMode` -> `DiagnosticReport.html` already existed. 2.3.0: a "Tracking Components" section and unused-component warnings (CHANGELOG 8024-8025); 2.3.1 passive warnings (8015); 2.28.11 "Unknown Entries Breakdown" (7081); 2.28.12 "Unmatched HTTP Client Names", HttpContextAccessor column, smart warnings (7064-7067); 2.28.13 the diagnostic report written even with no features (7038); 2.28.37 `Track.DiagnosticMode` value-resolution fallbacks in `DiagnosticReport.html` (6856) |
| 3.0.45 (2026-08-22) | Structured diagnostics: `IngestResult.Diagnostics`, `DiagnosticEntry`/`DiagnosticKind` (5339); `IngestRequest.HostDiagnostics`, `kronikol ingest --diagnostic "<kind>:<message>"`, and the **new collapsed "Report diagnostics" block in `TestRunReport.html`** (under the summary, kind badges, absent when empty) plus the `diagnostics` array in the JSON and schema (5297). Also the unpaired-request warning fix (5289) |
| 3.0.80 (2026-09-04) | Toggle default start states, including `DiagnosticsOpen` (git `7e072315`) |
| 3.1.0 | Adapter-driven runs record diagnostics: before, the HTML block never appeared on any framework-adapter path (4754) |
| 3.9.0 (2026-09-14) | The ledger and fourteen options, `EmbedHistoryInReport` among them (no HTML yet; "the HTML rendering to follow in 3.10.0 and 3.11.0") |
| **3.11.0** (2026-09-14) | **History in the HTML** (3902-3950): the sparkline, the pill for every verdict but stable, the History section beside the timeline (summary, trends, lists linked by `#sid-`), `$flaky`/`$broke`/`$new`/... in the search box (the deep-search worker reads the same verdicts), `kronikol merge --history`, the filtered export carrying sparklines and pills but not the section, and the dogfood in `ci-summary-preview.yml`. Two design changes are recorded: history rendered on the generation side rather than as a payload script, and "the verdict filter is the search box's `$` sigil rather than a new toolbar control, which waits for the toolbar redesign". Minor |
| 3.12.0 | `HistoryCompareBranch`, read out in the report's History section among other places (3898) |
| 3.17.0 | `BackgroundCalls` diagnostic entries, and the HTML's separate "Background calls" section (3728; adjacent, not in scope) |
| 3.18.0 | The `alternating` verdict: "the pill and `$alternating` in the search box" (3678) |
| **3.21.0** (2026-09-16) | **"Minor - the History and Report diagnostics sections leave the HTML report unless it asks for them"** (3473-3510): `ShowHistorySection` and `ShowReportDiagnosticsSection` (both default `false`), `kronikol ingest --diagnostics-section`; `merge --history` sets `ShowHistorySection`; the sparkline and pill stay under `EmbedHistoryInReport`; `DiagnosticsOpen` inert while the section is off; the two new `showHistorySection`/`showReportDiagnostics` parameters of `GenerateHtmlReport` and `MergeableReportRenderer.Render` default off |
| 3.23.0 | The sparkline names and colours `N` (not a test); a partial run's tooltip row (3262-3278) |
| 3.24.0 | A degraded run said once above the History section's charts and on the run's bar, and in a sparkline tooltip (3185-3186) |
| 3.27.0 | "passed on attempt 2 ... an earlier attempt failed: ..." in the history tooltip (2814) |
| 3.29.4 | Long tokens wrap in the History section and the report diagnostics block; `ViewportSweepTests` gains a page holding every section (2249, 2283) |

The changelog has no `## [Unreleased]` section today (it opens at `## [4.5.0] - 2026-10-01`).

### 2.3 Agent-facing docs and skills

- `src/Kronikol/Reports/agent-instructions.md` (148 lines, emitted as `Reports/CLAUDE.md`/`AGENTS.md`):
  no HTML history or diagnostics mention. 39-41 and 111 are `kronikol query history`; 88 is the
  `__REPORT__.html#sid-<stableId>` deep link; 134 the `!` diagnostic provenance lines. NONE.
- `.claude/skills/kronikol-test-debugging/` and `templates/skills/kronikol-test-debugging/` (`SKILL.md`,
  `references/commands.md`, `scripts/query.py`): byte-identical copies (held by `SkillDriftTests`), CLI
  only. `query.py:261` prints `open: TestRunReport.html#sid-...`. NONE.
- `templates/agents/CLAUDE.md`: 29 `kronikol query history`. NONE.
- `.claude-plugin/plugin.json`, `.claude-plugin/marketplace.json`: no mention; only the version
  (4.5.0, held to `Directory.Build.props` by `PluginManifestTests`) moves with a release.
- `.github/copilot-instructions.md`, `CONTRIBUTING.md`, `AGENTS.md`, the issue and PR templates: no mention.

### 2.4 In-code documentation that ships (XML docs, CLI help, comments)

- `ReportConfigurationOptions.cs:421-429` (`ShowReportDiagnosticsSection`), `:760-776`
  (`EmbedHistoryInReport`, `ShowHistorySection`); `ReportToggleDefaults.cs:87-89`;
  `IngestPipeline.cs:253-256` (HostDiagnostics remarks naming the section and the CLI form);
  `MergeableReportRenderer.cs:22`; `ReportGenerator.cs:4112` (block summary), `:1824-1840` (comments).
- CLI help: `IngestCommand.cs:686-692` (`--diagnostic` lands "in the HTML report's 'Report diagnostics'
  section when --diagnostics-section asks for it"; `--diagnostics-section`), `MergeCommand.cs:357`
  (usage line) and `:382-385` ("render what the last runs said: the History section, sparklines and
  verdict pills in the HTML, and the history lines in Failures.md").

---

## 3. GitHub automation and tools in this repository

Summary: **nothing in `.github/` or `templates/github-actions/` links to `#history-section`, reads
history from HTML, sets `ShowHistorySection`, `EmbedHistoryInReport` or `ShowReportDiagnosticsSection`,
or passes `merge --history`.** Every `--history` in automation is the ledger path of a `kronikol
history ...` verb.

- `.github/workflows/ci-summary-preview.yml` (the dogfood): four `examples/Example.Api/tests/...CiPreview.*`
  projects, `kronikol-history/read` before `dotnet test`, `gate` and `save` after, a `history` job runs
  `record`. Comment 40-44: "read before the run so the reports these jobs write carry the verdicts".
  The example projects set none of the options (grep of `examples/`), so their HTML carries sparklines,
  pills and `$flaky` but no History section and no diagnostics block. Comment-level LINE at most.
- `.github/workflows/history-action.yml`: the action's live lane (read, gate, save, record, a racer);
  line 212 `kronikol history verify --history`. No HTML.
- `.github/workflows/doorstep.yml` (weekly): fetches BreakfastProvider's landing page, follows every
  `reports/.../TestRunReport.html` card to its `Failures.md` (must read `# No failures`), and every
  `reports/.../TestRunReport.html#sid-<hex>` link (52-60), grepping the report for
  `data-stable-id="$sid"`; it fails when there is none. Untouched by the move as long as scenario
  elements keep `data-stable-id`; it never looks at history markup.
- `.github/workflows/pr-report-link.yml` and `templates/github-actions/kronikol-pr-report-link/`: the
  comment says to open `TestRunReport.html` inside the zip (action.yml 24, 144). No history.
- `.github/workflows/ci.yml` 137, 325: comments about the history action's facts. `release.yml`,
  `codeql.yml`: nothing.
- `templates/github-actions/kronikol-history/` (README, four `action.yml`, `scripts/*.sh`): reads the
  ledger and the reports' JSON through `kronikol history gate`/`record`; never the HTML. Wording that
  says the report carries the verdicts: README 3-4 ("so a report, its `Failures.md` and the job summary
  say what is new"), 152-153 ("the verdicts reach its own report, `Failures.md` and job summary"),
  `read/action.yml:3-4` ("so the run's own report, Failures.md and job summary carry the verdicts").
  LINE at most; true of `Failures.md` either way.
- `tools/`:
  - `tools/history-bench/` (the measurements behind `CROSS_RUN_HISTORY_PLAN.md` §2, §6.5 and §8.1):
    `dsl.js` (runs `advanced-search.js` with `$flaky`, `$new`, `$broke`), `prune.js` (deep-search
    pruning for `$flaky` queries), `jsd/export.js` + `jsd/README.md` (export behaviour with a synthetic
    `<section id="history-section">` and `#history-data`), README 85-120, 170. Historical harnesses;
    they would go stale, nothing runs them in CI.
  - `tools/history-replay/` (verdict acceptance harness): no HTML.
  - `tools/wiki-links/`: the `[[...]]` checker (section 1.5).
  - `plans/TOOLBAR_AT_EVERY_WIDTH_PLAN.harness/sections.cs` builds a page holding the History and
    diagnostics sections for width sweeps (harness).
- Tests that pin the moved markup (for the plan's TDD list): `tests/Kronikol.Tests.EndToEnd/`
  `HistoryExportTests.cs`, `HistorySectionTests.cs`, `HistorySparklineTests.cs`,
  `ReportSectionWidthTests.cs`, helpers `HistoryReportHelper.cs` and `ReportTestHelper.cs` (turn the
  sections on); `tests/Kronikol.Tests/` `History/HistoryHtmlTests.cs`, `History/HistoryOutputsTests.cs`,
  `Ingestion/IngestHostDiagnosticsTests.cs`, `Reports/Merge/MergeCommandTests.cs` (220-231),
  `Reports/ReportToggleDefaultsResolverTests.cs`, `Reports/ToggleDefaultsMarkupTests.cs`,
  `Reports/ToggleDefaultsBaselineTests.cs`, `Tool/IngestCommandTests.cs`.

---

## 4. Plans

### 4.1 `plans/README.md` (conventions)

Design records; each `*_PLAN.md` is the working document behind a piece of Kronikol, kept after the
work ships. "A plan is **not** a promise." `PLANS_STATUS.md` is the index, one row per plan with its
status and the release it shipped in. Nothing in `plans/` ships, and nothing there is authoritative
about current behaviour (the wiki, README and changelog are). `CLAUDE.md`: new `*_PLAN.md` go in
`plans/`, never the root, and get a `PLANS_STATUS.md` row when created, advanced or completed.

House style seen in recent plans (e.g. `HISTORY_ACTION_PLAN.md` 1-25): a title naming the roadmap row,
a `**Date:** ... · **Repo version:** ... · **Status:** ...` line, claims marked RUN / READ / DOC /
INFERRED, findings F1..., open questions Q1..., slices S0... or releases R1..., a log section once
executed, and a `<NAME>_PLAN.harness/` folder with its own README for scripts and output.

### 4.2 `plans/PLANS_STATUS.md`

Column headers (lines 18-19):

```
| Plan | Status | Shipped in |
|---|---|---|
```

New plans are added at the **top** of the table (line 20 is the newest, `CLICKHOUSE_DRIVER_CLIENT_PLAN.md`
2026-10-01), named `` `NAME_PLAN.md` *(new, YYYY-MM-DD)* ``. The header (line 3) still reads "Date:
2026-08-30 ... Repo version: 3.27.2" (stale; rows are newer). Two rows verbatim:

```
| `SPAN_ATTRIBUTION_PLAN.md` *(new, 2026-09-29)* | ✅ Executed as 3.35.1 the day it was written, as `V4_PLAN.md` R3 requires (#87). A call's popup takes the spans of its trace; where tests' calls share a trace, its own span and what descends from it; a call with no trace id, what its test's traced calls may take, or spans no call claims; then its time. A span calls of two tests would both take goes to neither, and the popup says how many it left out. Proved at the builder, through the real handler, and on the page. Found: database trackers record no trace id, the note was lost under the default flame chart, `ShowMessage` popups showed their markup as text, and a parallel span-store clear failed a report fact one run in three | 3.35.1 |
| `DASHBOARD_PLAN.md` *(new, 2026-09-14)* | ❌ Plan written, nothing implemented, **NOT green-lit**. A static dashboard over the history ledgers: each repository's `kronikol-history` branch gains a derived `history.view.json` (verdicts computed by `HistoryAnalyzer`, no fingerprints; measured 116 KB gzipped for BreakfastProvider's 108 runs against 226 KB for the ledger), and `kronikol dashboard build` emits one self-contained page that renders the baked snapshot and refreshes live from public branches (raw serves `Access-Control-Allow-Origin: *`, a 5-minute cache and `304` on `If-None-Match`; all measured). Leads with what no competitor has (behaviour drift, calls per scenario, cluster ageing); private repositories are snapshot-only and a private Pages site needs Enterprise Cloud. Three milestones, one minor each, plus an optional ledger v2 for per-scenario dependencies; proposes one sentence for `CROSS_RUN_HISTORY_PLAN.md` §14. | — |
```

Status cells in use: `✅ **Executed ...**`, `❌ Plan written, nothing implemented, **NOT green-lit**`,
`🟡 Partially done`, `**Deleted YYYY-MM-DD.** ✅ Done ...`. The `CROSS_RUN_HISTORY_PLAN.md` row (73)
records 3.11.0 as "M5 (history in `TestRunReport.html`: sparkline + verdict pill per scenario, a
History section beside the timeline, `$flaky` in the search box, `merge --history`)".

### 4.3 `plans/ROADMAP.md`

- Header (line 3): amendments are logged as "**Amended YYYY-MM-DD:** X placed as N, with decision Dnn
  and `X_PLAN.md`; nothing else moved." The decision queue (section 3, 137-176) runs D1 to **D32**; the
  next free number is **D33**.
- The bar (59-68, decision D19): "How much it covers" includes "**Trends across runs on a page**"
  (stage 9.0 to 9.4). Nothing in the bar names the History section or the diagnostics block.
- Rule 5 (85): "Work that edits the same files runs one after the other. **The history renderer**,
  `VerbTable`, `stylesheets.css` and the toolbar emit sites each have several claimants."
- Rule 7 (90): "A new option is a minor, **flipping its default is a major**. Everything a major needs
  is built behind options first, so the major itself is flips and deletions." Majors: v4 shipped
  (4.0.0), v5 is stages 11-12 (D30).
- Tracks (535-541): A "History and the tool" (`Kronikol.Tool/`, `History/`, `VerbTable`, skill copies;
  stages 1.1, 1.2, 1.13, 1b, 4, 7, 9, 10) and B "Report rendering" (`ReportGenerator.cs`,
  `stylesheets.css`, the report scripts; 1.5, 1.6, 1.8, 1.9, 1c, 5, 6, 8, 11, 12). This move touches
  both (`History/HistoryHtml.cs` is A; `ReportGenerator.cs`, `stylesheets.css`, the search scripts are B).
- **Stage 9, "History over time"** (415-437), needs **D9** (153: "Dashboard plan §9: enable Pages on
  `lemonlion/Kronikol`, the three names, the default window of 200, and where the P1 addendum lives";
  still open). 9.0 refresh `DASHBOARD_PLAN.md` (429), 9.1 M0 the view (430), 9.2 #96 (431), **9.3 M1,
  the page** (432: "Snapshot mode, one repository, seven panels, opened from `file://` in Playwright ...
  Uses the `--kron-*` token names from the first byte ... built to the bar's look from the start"), 9.4
  M2 plus P1 (433), 9.5 P2 durable reports (434), 9.6 alerting (435). The stage quotes the direction
  report's line: "Do not build a hosted dashboard".
- Stage 12.1 (466): v5 scope ("Wiki 'Migrating to v5'"); the History section and diagnostics block are
  not in it. Stage 13.0 (492): "The wiki and README re-read for anything 4.0.0 or 5.0.0 made false."
- Section 6 (604-624), deliberately not scheduled: store plan P3 and ledger format v2. Nothing about the
  HTML sections.
- Appendix B.3 (837-906): the store plan by subject. Appendix C (908-938): `CROSS_RUN_HISTORY_PLAN`
  (924): "Q15, whether `diff --baseline` should read the ledger. `merge --history` has never been timed
  (mergeable shards cannot be made from outside the test project) | Open"; `HISTORY_ACTION_PLAN` (936):
  S5's week of BreakfastProvider runs to 2026-10-06, Q6 open. Appendix B.2 (809): the
  `CROSS_RUN_HISTORY_PLAN` register row.
- Nothing in the roadmap plans a separate per-run history or diagnostics document.

### 4.4 Plans that overlap with a separate history / trends document

- **`DASHBOARD_PLAN.md`** (657 lines, NOT green-lit, roadmap 9.0-9.4, D9): a separate self-contained
  page from `kronikol dashboard build` over a derived `history.view.json`. Overlaps directly:
  - §4.3 panel 5 (257): "**Scenarios.** The roster with the report's sparkline and colours
    (`HistoryHtml.Colour` becomes a shared palette the page and the report both read), the verdict
    pill, p95 and last result; filters by verdict, suite, stream and text". M1 (510-543) names
    `HistoryPalette` "read by `HistoryHtml` and by the build". 293: "the sparklines in the scenario
    table, which the report already draws as one node". 305: a recorded idea, "a duration chart per
    run, or a chart in the History section" with PlantUML's `@startchart`, "not part of this one".
  - Principles that would bind a moved History document: "The page renders, it never judges" (46;
    every verdict from `HistoryAnalyzer`, none in JavaScript), one self-contained file with a hash
    CSP and no CDN (§4.1, §4.7), Observable Plot on vendored D3 (§4.3), light and dark with the
    `--kron-*` names (§4.3), and §6 (593) "No report content beyond the ledger ... the run URL is the
    hand-off to the report artifact".
  - §9 open questions (632-652): Pages on Kronikol, the default window of 200, the names
    (`history.view.json`, `kronikol history view`, `kronikol dashboard build`), whether `history
    record` writes the view by default, a `dashboard:` pointer line.
  - Difference from the proposal: the dashboard is cross-run and cross-repository from the ledger; it
    is not a per-run companion of one report.
- **`HISTORY_DASHBOARD_STORE_PLAN.md`** (2,151 lines, NOT green-lit): §8.0 (1189-1223, the owner's
  order of 2026-09-21): **P1** years of trends at verdict level on the dashboard page (1198), **P2**
  "Any past run's evidence": the report CI already emits, in the team's bucket, "its address on the
  run line so the page and `query history` can link it" (1199), P3 the facts layer. M4 (1292-1302), the
  dashboard reads the store. §8.6 (1365-1372): docs obligations ("The README's history section and the
  agent skill's `commands.md` copies move with any new verb").
- **`CROSS_RUN_HISTORY_PLAN.md`** (executed), §8.1 (1450-1584): the original HTML design. It specified
  a gzip+base64 `#history-data` payload script (direct child of `<body>`, so export carries it) that
  3.11.0 replaced with generation-side rendering; the single-node sparkline (performance budgets
  `WorstTaskMs`, `ToggleWorstTaskMs`, `FilterPerformanceTests` 200 scenarios under 1,000 ms); the
  History section by the timeline (1505); "A **toolbar filter** over the verdicts, **designed inside
  `TOOLBAR_REDESIGN_PLAN`'s vocabulary, not beside it**" (1507), never built; the `$verdict` search
  design with its seven touchpoints; the export interplay. §8.7 (1773) the merged report. §14
  (2090-2114) what it does not build ("No server, database, hosted dashboard or account"). §15
  (2116-2182) the documentation list a history change touched: Report-Configuration (every option a
  row), Search-Syntax, Generated-Reports, Querying-Reports, Merging-Parallel-Reports,
  CI-Summary-Integration, CI-Artifact-Upload, Ingesting-External-Captures, Capture-Time-Redaction,
  Project-Templates, FAQ, Diagnostics-and-Debugging, Home, `_Sidebar`; README, nuget-readme, the skills,
  agent-instructions, CHANGELOG, PLANS_STATUS, XML docs; §15.3 a Kronikol4J ledger entry ("record, do
  not port").
- **`TOOLBAR_REDESIGN_PLAN.md`** (353 lines, DRAFT, NOT green-lit): no history, verdict or diagnostics
  content (its "pills" are status/duration filter pills). The verdict toolbar control 3.11.0 deferred to
  it is not planned there.
- **`V5_PLAN.md`** (233 lines): Mermaid CI summaries and removing server-side rendering only; no
  mention. **`V4_PLAN.md`**: no mention.
- Smaller overlaps: `FLEET_EVIDENCE_PLAN.md` S4 service card item "7. **History.**" (253-254, in
  `Service.md`, not HTML); `HISTORY_ACTION_PLAN.md` 1029 (`record` gains `view: true` once `record
  --view` exists, for 9.4); `AZURE_DEVOPS_PARITY_PLAN.md` 151, 747 (the Tier 1 dashboard on Azure
  DevOps); `DIAGRAM_COLOURS_PLAN.md` 669-671 (`OptionNotApplied` "lands in ... the HTML block when
  `ShowReportDiagnosticsSection` is on"); `INTERNAL_FLOW_BLOB_PLAN.md` 869, 893 ("the report
  diagnostics section counts them as 3 orphaned test ids", which is the console line, point 1 of
  section 0); `DOORSTEP_PLAN.md` 166 (F18: the landing page's first paragraph is "insider language (the
  history ledger, a sparkline, `$flaky`, ...)").

---

## 5. Kronikol4J (`C:/Code/Kronikol4J`)

- **It renders none of the moved UI.** No cross-run history at all: its `advanced-search.js` has no
  `verdict` (0 matches), its `stylesheets.css` no `history` rule, no `report-search-index.js`, nothing
  emits `history-section`, `history-sparkline`, `data-history-verdicts` or `report-diagnostics`. Its
  README (status paragraph, about lines 7-12) says: "no cross-run history, no `kronikol query`, no kept
  runs or `Failures.md`, no ingest of external captures and no out-of-process taps". Its rendering is a
  byte-proven port of .NET 3.0.43.
- It **does** port `DiagnosticReport.html`: `DiagnosticReportGenerator.java`
  (`kronikol4j-report/src/main/java/io/kronikol/report/diagnostics/`), `ReportOptions.withDiagnosticMode`
  (`ReportOptions.java:327-331`), the system property `kronikol.report.diagnosticMode` (`:91`), the
  Gradle DSL `diagnosticMode` (`KronikolExtension.java:122-123`, `KronikolPlugin.java:112`),
  `Track.diagnosticMode` (`Track.java:151-176`). Its wiki documents it at
  `C:/Code/Kronikol4J.wiki/Rendering-and-Offline-Reports.md:62-63`. So a plan that merges the diagnostics
  block into `DiagnosticReport.html` changes a document the port has.
- **The divergence ledger** is `C:/Code/Kronikol4J/docs/REMAINING_PARITY.md`, section `## .NET-side
  features shipped after this audit (divergence ledger)` at line 1877. Entries are bullets. A block from
  3.22.3 back to 3.0.62 sits at the top, newest first (1879-2050); from 3.27.0 on, entries are appended
  in order at the end, the last at line 2406 (4.5.0), before the `---` that closes the section. Each
  release's line is its own commit, message `Ledger: .NET X.Y.Z ...` (e.g. `84132db`, `a04e6b0`).
- Existing entries on exactly this subject:
  - 1905-1915, "**Two report sections left the HTML by default (.NET 3.21.0, 2026-09-16).**" ... "Java
    has neither section (cross-run history is unported, and the diagnostics block was never rendered),
    so byte parity is unaffected either way; what Java needs when it ports them is the two option
    defaults, and the rule that a section's absence is the default rather than a sign of an empty run."
  - 1987-2010, "**Cross-run history (.NET 3.9.0-3.11.0, 2026-09-14), .NET-only**": lists
    `data-history-verdicts`, `<span class="history-sparkline">`, the pill, `<details
    id="history-section">`, the `.history-*` rules, the fifth `verdicts` argument, and "Byte parity
    holds whenever there is no ledger" (the parity harness runs with `KRONIKOL_HISTORY=off`).
  - 1951-1962, 3.18.0: the `alternating` verdict in `data-history-verdicts`, the pill, `$alternating`.
- Example ledger entry, verbatim (line 2406):

```
- **ClickHouse.Driver's `IClickHouseClient` is tracked (.NET 4.5.0, 2026-10-01).** Not mirrored, a ledger entry only: a .NET-only capture surface. .NET now records the calls of the official .NET ClickHouse client's non-ADO.NET API, `InsertBinaryAsync` drawn as one `INSERT INTO <table>` whose request carries the rows it sent, formatted by the rule a reader's rows follow. Kronikol4J's ClickHouse module wraps JDBC only (`ClickHouseTracking.wrap`, through `TrackingDataSource`), so the Java client-v2 API is not tracked there; output for inputs both ports capture is unchanged. Also .NET-only: `DecorateAll` decorates a keyed registration under its key (the port has no counterpart of `DecorateAll`).
```

- Consequence for the plan: a ledger entry only ("not mirrored"). With no ledger the default .NET report
  carries no history markup and (since 3.21.0) no diagnostics block, so default-case byte parity is
  unaffected; the shared scripts the port copies (`advanced-search.js`, the feature-map helper) already
  differ from .NET's since 3.11.0.

---

## 6. BreakfastProvider (the consumer and live demo)

- Live `main` `106702b` (2026-09-30): Kronikol packages **4.0.2**
  (`tests/BreakfastProvider.Tests.Component.Shared/BreakfastProvider.Tests.Component.Shared.csproj:31-41`),
  the history action at `@v4.0.2` (`.github/workflows/ci-main.yml:587`; `_tests.yml:272, 299, 309`;
  `_tests-tunit.yml:219, 241`).
- **Sets none of `EmbedHistoryInReport`, `ShowHistorySection`, `ShowReportDiagnosticsSection`,
  `DiagnosticsOpen` or `DiagnosticMode`** (GitHub code search on the default branch returned nothing for
  each; the local `origin/main` agrees). `KronikolHistory.WithCrossRunHistory`
  (`tests/BreakfastProvider.Tests.Component.Shared/KronikolHistory.cs`) sets only `SuiteName`,
  `HistoryMinRuns = 3` and `GenerateCtrfReport = true`; the xUnit `GlobalTestSetup.cs:66-71` sets the
  titles, `WriteCiSummary` and `ExcludedHeaders`. The `read` action names the ledger in
  `KRONIKOL_HISTORY`. **So its published reports carry sparklines, pills and `$flaky`, and have carried
  no History section and no diagnostics block since it moved past 3.21.0.**
- **Landing page** `.github/pages/index.html` (198 lines; built into the Pages site by
  `.github/scripts/build-api-pages.sh`, `ci-main.yml:748-749`; before, an inline heredoc in
  `ci-main.yml`):
  - 99: "...each scenario's HTTP calls, database queries and messages as a sequence diagram. `<a
    href="reports/xunit/TestRunReport.html#sid-fb8c79cbd78d82b0">See one</a>`" (the one deep link;
    `doorstep.yml` checks it).
  - **101: "Every report reads the last runs of its lane from the cross-run history ledger: a sparkline
    and verdict beside each scenario, a History section beside the timeline, and `$flaky` in the search
    box. Each report folder also carries `Failures.md` and `ctrf-report.json`."** "A History section
    beside the timeline" is **already false** (the section is off and not asked for); after the move the
    sparkline and `$flaky` claims would be too.
  - 103-128: cards to `reports/{reqnroll,lightbdd,bddfy,xunit,tunit,nunit}/TestRunReport.html`. No
    link to `#history-section` anywhere.
- `README.md` 941-959, `## Cross-run test history`: 945-947 "...and the published `TestRunReport.html`
  (a sparkline and verdict beside each scenario, a History section beside the timeline, `$flaky` in the
  search box)". The same stale claim.
- Workflow comments: `_tests.yml:265-269` and `_tests-tunit.yml:213-217` ("so this run's Failures.md,
  CTRF document and TestRunReport.html already say whether a failure is a regression, has been failing
  since a particular run, or flips"); `ci-main.yml:10-11` ("the published reports saying what the last
  runs said").
- Nothing in its workflows reads history from the HTML; the gate reads the JSON and the ledger. The
  memory note says Pages serves gzip only; doorstep fetches with `--compressed`.
- Kronikol's own `plans/DOORSTEP_PLAN.md` F18 (166) quotes that paragraph as "insider language", and
  ROADMAP 13.1 plans to refresh the demo before the launch.

---

## 7. Pre-existing documentation defects found on the way (not caused by the move)

1. `_Sidebar.md:133-138`: the four Querying Reports sub-links render under Cross-Run History (since
   3.9.0, `5f98f7f`).
2. `Generated-Reports.md:21`: a blank line inside the Output Files Summary table cuts off the `Run.json`
   and `runs/<run>/` rows (since 3.27.0, `b20ea14`). The same table has no `DiagnosticReport.html` row.
3. `Cross-Run-History.md:798-826`: the options table has no `ShowHistorySection` row.
4. `Report-Configuration.md`: no rows for `HistorySlowerMinMs`, `HistoryAlternatingRuns`,
   `HistoryCountRuns`, `HistoryShapeTemplates`, `HistoryDegradedBy`.
5. `Ingesting-External-Captures.md:255-273`: no `--diagnostics-section` row in the ingest flags table.
6. `Diagnostics-and-Debugging.md:69` says the history kinds appear "in the report's diagnostics panel",
   which is off by default since 3.21.0, and lists three kinds where there are four.
7. `Diagnostics-and-Debugging.md:86` and `API-Reference.md:53` say `ReportDiagnostics.Analyse()` strings
   go to "the footer" / "the report footer"; they are printed to the console only.
8. `Generated-Reports.md:886-918`: the `GenerateHtmlReport` signature lacks about 20 parameters.
9. `Capture-Time-Redaction.md:10-15`: the derived-files list omits `TestRunReport.html` and
   `DiagnosticReport.html`.
10. BreakfastProvider `.github/pages/index.html:101` and `README.md:945-947` advertise a History section
    its reports have not had since 3.21.0.
11. `kronikol merge` can never render the diagnostics block (`MergeCommand.cs:114` builds fresh options
    with only `ShowHistorySection`), and nothing documents that.

---

## 8. Versioning notes the plan will need

- `CLAUDE.md`: MAJOR includes "changing a default so that existing code behaves differently without
  being touched"; but "a change to generated report output (HTML, ...) is not on its own a major
  bump" (record it in the Kronikol4J ledger and bump by the nature of the change); a new configuration
  option is a minor even when its default preserves today's behaviour.
- `ROADMAP.md` rule 7 (90): "A new option is a minor, flipping its default is a major", with majors
  prepared behind options in the line before and spent once (v5 = stages 11-12).
- Precedent: 3.21.0 took both sections out of the default HTML with two new options defaulting to the
  new behaviour, and shipped as a **minor** (CHANGELOG 3475: "Two new options and a new `ingest` flag,
  each defaulting to the new behaviour, so `TestRunReport.html` loses two blocks it used to open with").
- Removing or renaming `EmbedHistoryInReport`, `ShowHistorySection`, `ShowReportDiagnosticsSection`,
  `DiagnosticsOpen`, `--diagnostics-section` or the public `GenerateHtmlReport` /
  `MergeableReportRenderer.Render` parameters is removal of public surface: MAJOR (v5), with
  `[Obsolete]` in a 4.x minor first (`ROADMAP.md` 11.1 makes the same point for v5's own removals).
