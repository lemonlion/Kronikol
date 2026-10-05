# History and diagnostics move plan

**Date:** 2026-10-05 · **Repo version:** 4.5.0 (`main` at 425d9bad; line numbers are for that commit) · **Status:**
green-lit 2026-10-05 ("implement the plan in full"), being executed. Revised the same day after the owner's
clarification (below).

Every claim below was READ in the code, the tests, the wiki or the plans at that commit, unless it is marked RUN
(checked with a command on 2026-10-05) or ESTIMATED (to be measured in S0). No test and no report generation was run.

The owner's ask (2026-10-05): "The history stuff and the report diagnostics sections need to be completely stripped
out of the TestRunReport.html and put in a separate doc as they're not ready for prime time."

The owner's clarification, the same day: "To be clear, by default i dont want the TestRunReport.html reports to have
any history or report diagnostics in them anymore for the time being. It's adding a lot of ugly noise and i need to
consider how best to readd it in the future." Asked two follow-up questions, the owner chose that the current views
stay available inside the report as opt-ins, off by default, with nothing deprecated, and that the separate page is
written by default whenever it has something to show.

## TL;DR

- By default, `TestRunReport.html` carries no history and no diagnostics at all. That means no History section, no
  sparkline or pill in the scenario headers, no `data-history-verdicts` attributes and no Report diagnostics section,
  and also none of the CSS or `$verdict` search code behind them. Today the sparklines and pills are on by default for
  any consumer whose tests run inside a git repository (the ledger is found by walking up to the repository root), and
  the CSS and search code ship in every report.
- Nothing is thrown away while the owner works out how to re-add them (section 13). Every current view stays available
  in the report as an opt-in, off by default. `ShowHistorySection` and `ShowReportDiagnosticsSection` keep doing what
  they do (both have been off by default since 3.21.0). A new `ShowScenarioHistory` (default false) brings back the
  sparkline, the pill and the `$verdict` search. Nothing is deprecated.
- A new page beside the report, `TestRunReport.labs.html`, is written by default whenever the run read history or
  recorded a diagnostic. It holds the History section, every scenario's sparkline and verdict, and the diagnostics
  list. It has no script, and its scenario names link back to the report by `#sid-<id>`. A new `GenerateLabsReport`
  (default true) turns it off.
- Everything else that carries history or diagnostics stays as it is: the `diagnostics` array of
  `TestRunReport.json`, `Failures.md` and `Failures.jsonl`, CTRF, `History.run.json`, the ledger, `kronikol query
  history`, the console and CI lines.
- A default report gets smaller: 6.1 KB of stylesheet rules and 1.3 KB of script in every report, plus the history
  markup, measured by S0 (harness `s0/`) at 7,124 bytes on an 8-scenario example suite with ten earlier runs: about
  900 bytes per scenario raw, most of it sparkline tooltips that gain an 83-byte line a run up to the 10-run cap, and
  about 60 bytes per scenario gzipped. On CI each tooltip line also carries a commit, so real suites cost more.
- Two releases. 4.5.1 (patch) fixes ten defects this research found (section 9), among them three assertions that pass
  today only because the stylesheet happens to contain their text, and two tail writes that can cost a run its
  `Run.json`. 4.6.0 (minor) is the move: two new options, each defaulting to the new behaviour, as 3.21.0 did. No
  existing option's default changes, and nothing is deprecated or removed.
- Turning any of these views back on by default later flips a default, which CLAUDE.md and ROADMAP rule 7 count as a
  major. Section 13 lists what the re-add design has to settle.
- Decided on 2026-10-05: the page by default, the opt-ins kept, a minor now, and the two names
  (`TestRunReport.labs.html`, `ShowScenarioHistory`). The other ten questions are taken as recommended (section 11).

## 1. Scope

### 1.1 What leaves the default report

| What | Drawn by | 4.5.0 | From 4.6.0 |
|---|---|---|---|
| History section, `<details id="history-section">`: summary line, stream and run, cold-start note, compare line, pass-rate and duration charts, ten lists (new failures, failing since, flaky, newly fixed, slower, behaviour changed, new scenarios, quarantined, absent, new dependencies) | `HistoryHtml.Section` (src/Kronikol/History/HistoryHtml.cs:100-160), called at ReportGenerator.cs:1839-1841 | in the report when a ledger was read and `ShowHistorySection` is on (default off since 3.21.0); `kronikol merge --history` turns it on | on the page whenever history was read; in the report only with `ShowHistorySection`, as before |
| Sparkline `span.history-sparkline` and pill `span.history-verdict` in each scenario header | `HistoryHtml.Sparkline` / `Pill`, ReportGenerator.cs:2075-2081 | in the report whenever a ledger was read (`EmbedHistoryInReport`, default on) | on the page; in the report only with the new `ShowScenarioHistory` |
| Group pill and union attribute on a parameterised group | `HistoryHtml.GroupPill` / `VerdictAttribute`, ReportGenerator.cs:2762-2779, 2860-2861 | as above | as above |
| `data-history-verdicts` on outline rows | ReportGenerator.cs:2947 (flat), 3054 (grouped) | as above | in the report only with `ShowScenarioHistory` |
| `$flaky`, `$broke`, `$new` and the other verdict operators in the search box | advanced-search.js:249-294, report-scenario-feature-map-helper.js:14-16, report-search-function.js:62, report-search-index.js:251, 253, 358, 481 (the deep-search worker's item payload) | the code ships in every report; it matches only where the attribute exists | the code ships only in a report drawn with `ShowScenarioHistory` |
| History CSS | stylesheets.css:1316-1451 (4,226 B) and 1995-1997 (75 B, the 480 px copy) | in every report, Specifications.html included | only in a report that draws history markup, and on the page |
| Report diagnostics section, `<details class="report-diagnostics">` | `ReportGenerator.RenderReportDiagnostics` (4111-4144), emitted at 1827-1828 | in the report when `ShowReportDiagnosticsSection` is on (default off since 3.21.0) and the run recorded an entry; `kronikol ingest --diagnostics-section` turns it on | on the page whenever the run recorded an entry; in the report only with `ShowReportDiagnosticsSection`, as before |
| Diagnostics CSS | stylesheets.css:1261-1314 (1,784 B) | in every report | only in a report that draws the section, and on the page |

So of what history and diagnostics put into a default 4.5.0 report, the visible part was the sparkline and the pill,
on every scenario once a ledger held a run. The rest was CSS and script. The sections were already opt-in.

Nothing in the title area, the statistics table, the CI block, the filter box or the toolbar shows history or
diagnostics (ReportGenerator.cs:1455-1732). The search help documents `$failed`, `$passed` and `$skipped` but never
the verdict operators, which are documented only in README.md:16, nuget-readme.md:65 and the wiki.

### 1.2 What stays, and why

- **The Background calls section** (ReportGenerator.cs:1830-1831): evidence about calls that belong to no scenario,
  not history and not report health. The ask names the diagnostics sections. Q6 asks whether the owner counts it
  among them. Each background group also records a `BackgroundCalls` diagnostic entry (ReportGenerator.cs:243-248),
  so the page's diagnostics list mentions them either way.
- **Failure clusters, the timeline, the component diagram panel**: unrelated.
- **`DiagnosticReport.html`** (`DiagnosticMode`, off by default): a different page. It is a setup troubleshooting page
  built from process-global registries that only the test process has, it holds none of the diagnostic entries, and
  Kronikol4J ports it byte for byte. Section 3.1 says why it is not the new home. Q5 asks.
- **The data outputs**, so nothing a tool reads changes:

| Output | History | Diagnostics |
|---|---|---|
| TestRunReport.json / .xml / .yml | none (no verdict field in any writer or the schema) | the `diagnostics` array, the ReportGenerator.cs:464 snapshot |
| Failures.md / Failures.jsonl | header line, and each failure's verdict, evidence and series | `ResultDefaulted` only |
| ctrf-report.json (off by default) | every scenario: `extra.kronikolHistory`, and the ledger's flaky flag | none |
| History.run.json, the ledger, `kronikol query history` | everything, recomputed from the data file, the fragment and the ledger | `kronikol query summary` prints the array |
| Console pointer, CI "Debug this run" | one line | none (the console gets the separate `ReportDiagnostics.Analyse` strings) |

### 1.3 What a reader of a default report loses, and where it is after

- Verdicts beside scenarios that did not fail (fixed, slower, behaviour-changed, new, flaky but passed, quarantined):
  the page's lists and table, `kronikol query history`, or the report with `ShowScenarioHistory`.
- The trend charts and the sparkline tooltips (per-run commit, duration, attempt, error): the page, or the report with
  the opt-ins.
- `$flaky` and the other operators: only in a report drawn with `ShowScenarioHistory`. In a default report `$flaky`
  behaves as it does today in any report without a ledger: a status filter that matches nothing, so the list is empty.
  A bookmarked `#q=%24flaky` opens an empty list. The changelog says so.
- Export Filtered HTML of a default report carries no history. An opt-in report's export keeps the sparklines and
  pills, as today; neither export carries either section (HistoryHtml.cs:17-19).

## 2. What the code does today

Findings that shape the design. Each was read in the code at 425d9bad.

1. **History is on by default.** `HistoryPathResolver` takes the ledger from `HistoryFilePath`, then
   `KRONIKOL_HISTORY`, then the nearest repository above the run (`<root>/.kronikol/history.jsonl`), then the nearest
   `.kronikol` directory (src/Kronikol/History/HistoryPathResolver.cs:3-46). `KRONIKOL_HISTORY=off` is the only way
   out, which is why every `test.runsettings` in this repository sets it. The window is 50 runs
   (ReportConfigurationOptions.cs:606) and the sparkline shows the last 10 (HistoryAnalyzer.cs:641). An ingest reads
   history the same way, so an ingested report carries sparklines too.
2. **All history markup comes from one internal class**, `HistoryHtml`, through four call sites in
   `GenerateHtmlReportCore` (section 1.1). `historySlots` (ReportGenerator.cs:1839) gives two scenarios sharing a stable
   id a line each. The section's links are `#sid-<id>` driven by `reveal_url_anchor` (HistoryHtml.cs:175).
3. **One diagnostics snapshot feeds every file.** `reportDiagnostics` (ReportGenerator.cs:464) is taken before the
   outputs run in parallel and is shared by the HTML, the data files and the digest. Entries recorded later (an
   `OutputFailure`, the append's `HistoryUnavailable`) reach only the collector, the console and
   `IngestResult.Diagnostics`.
4. **The data enters through a public method.** `ReportGenerator.GenerateHtmlReport` (ReportGenerator.cs:1026-1069,
   no XML doc) takes `diagnostics`, `history`, `showHistorySection` and `showReportDiagnostics`, and draws the
   per-scenario history whenever it is handed verdicts. Two producers call it: the run (through the internal
   `GenerateHtmlReportCore`, 488, which passes `options.EmbedHistoryInReport ? history?.Verdicts : null`) and
   `MergeableReportRenderer.Render` (57-95). `Render` passes the section switches but never
   `diagnostics: report.Diagnostics`, so a merged report can never show the diagnostics section (F3).
5. **The client side is small and separable.** The verdict operators touch the lines in section 1.1 and nothing else.
   Reverting `advanced-search.js` and `report-scenario-feature-map-helper.js` to their bytes before 3.11.0 makes them
   byte-identical to Kronikol4J's copies (RUN: one commit, c9c3b068, has touched each since v3.10.0, and the v3.10.0
   files hash the same as Kronikol4J's with line endings normalised). Scripts reach the page through
   `LoadResource` and its cache (ReportGenerator.cs:84-95; `advanced-search.js` through its own loader at 70-80), and
   some are already rewritten on the way (`__DEP_MODE_DEFAULT__` and ten other placeholders), so a variant per report
   is a known pattern. No client code knows `#history-section`. The report's hash handler resolves `#sid-<id>` on load
   (report-url-hash-function.js:3-8, 26; StableIdDeepLinkTests), so a page elsewhere can link straight to a scenario.
   `DiagnosticsOpen` is server-side only: it becomes an `open` attribute and no script reads it.
6. **A new top-level file written through the output list is mostly automatic.** `Add(label, action)` plus
   `WriteFile` (ReportGenerator.cs:474-604, 5855-5894) records the file with `RunFileCollector`, so it is listed in
   `Run.json`, moved into `runs/<run>/` by the rotation and uploaded on Azure DevOps (`.html` is a published extension,
   CiArtifactPublisher.cs:17). Four things are not automatic:
   - `PlannedFiles` (ReportGenerator.cs:736-772), which the rotation reads for a directory with no `Run.json`;
   - the console pointer's candidates (630-635): its `HtmlFileName` is the first `.html` it lists
     (RunSummaryConsoleWriter.cs:333-334), so the page must be listed after the report;
   - the tails of `kronikol merge` (MergedRunOutputs.cs:37-182) and `kronikol ingest` (IngestCommand.cs:448-459);
   - the label: `RunOutputs` returns labels, and the pointer names a file only when its label is its file name.

   Two traps: a top-level `.html` missing from `Run.json` turns attachment moves into copies
   (`AnotherReportIsPresent`, RunRotation.cs:416-418), and a file in a subfolder is never recorded, rotated or
   uploaded (RunFileCollector.cs:49-72).
7. **Fixed names collide.** Two reports sharing a folder (`Checkout.*` and `Payments.*`) already share `Run.json` and
   `Failures.md` (RunRotation.cs:206-210). A name derived from `HtmlTestRunReportFileName` does not collide, and
   follows `{report}.schema.json`.
8. **Kronikol4J renders none of this.** It has no cross-run history and never drew the diagnostics block. Its
   divergence ledger (C:/Code/Kronikol4J/docs/REMAINING_PARITY.md, from line 1877) already records both as .NET-only,
   and none of its 59 parity goldens contain the rules. The move takes .NET's default report toward Java's.

## 3. Design

### 3.1 One page, and not DiagnosticReport.html

One page holds both, because the ask says one ("a separate doc") and because both share the reason they are leaving
the default report: neither is ready. `DiagnosticReport.html` was the obvious alternative for the diagnostics, and it
is the wrong home:

- it is opt-in (`DiagnosticMode`, ReportConfigurationOptions.cs:417-418), and its content is a troubleshooting dump
  read from process-global registries (`TrackingComponentRegistry`, `InternalFlowSpanStore`, `Track.DiagnosticLog`),
  which `kronikol merge` and `kronikol ingest` do not have;
- it is written outside the isolated output list with its own directory rule (DiagnosticReportGenerator.cs:30-36;
  F4, F5);
- Kronikol4J ports it byte for byte, so every change to it is a divergence.

The new page is built only from data all three producers (a run, `merge`, `ingest`) have: `HistoryVerdicts` and the
`DiagnosticEntry` list.

### 3.2 Name and place

`{HtmlTestRunReportFileName}.labs.html`, `TestRunReport.labs.html` by default, at the top level of the reports
directory, beside the report. Derived from the report's name so that two reports in one folder do not collide, and
lower case after the dot like `{report}.schema.json`. At the top level because subfolders are invisible to the
manifest, the rotation and the upload. "Labs" says what the owner said: these views are not finished. Q2 offers other
names.

### 3.3 When it is written

When `GenerateLabsReport` is on (default true, decided) and the run has something for it: history was read and
`EmbedHistoryInReport` is on, or the diagnostics snapshot holds at least one entry. With nothing to show, no page is
written, so a first run with no diagnostics leaves the reports directory exactly as 4.5.0 does.

It does not depend on `GenerateTestRunReport` or on the report's opt-ins. With the report off, the page is still
written, and its scenario names are plain text instead of links. As with the digest's deep links
(ReportGenerator.cs:539-543), that is decided from the options, never by checking whether a sibling output has reached
disk.

### 3.4 What it holds

1. A header: the report's title (`GetTestRunReportTitle`, ReportGenerator.cs:913) followed by "Labs", the suite, the
   run id and when it ended, a link back to the report (when the report is written), and one line saying what the
   page is (Q4 settles the wording).
2. **History**, when history was read and `EmbedHistoryInReport` is on: `HistoryHtml.Section` as it is today, always
   open, whatever `ShowHistorySection` says (that switch is about the report). Its links become plain
   `href="<report>.html#sid-<id>"`, with no inline script.
3. **Every scenario** (Q7): the sparkline, pill and evidence that sat in each scenario header, one row per scenario in
   report order (feature, then scenario), each outline row on its own line under its scenario's name. The two-slot rule
   (`HistoryHtml.Entry`) carries over. It is a collapsed `<details>` after the section, because most rows read
   "stable".
4. **Report diagnostics**, when the snapshot holds an entry: what `RenderReportDiagnostics` draws today, open,
   whatever `ShowReportDiagnosticsSection` says.
5. Styles: the shared report stylesheet (`Stylesheets.HtmlReportStyleSheet`, public, which Specifications.html already
   shares), the user's `CustomCss`, favicon and logo, the two feature sheets of section 3.5 (`history-styles.css`,
   `report-diagnostics-styles.css`), and `labs-styles.css` for the page's own header and table. No script, so no CSP
   or `file://` question arises.

The page is written by a new internal class, `LabsReportGenerator` (src/Kronikol/Reports/LabsReportGenerator.cs),
which takes `HistoryVerdicts?`, the diagnostic entries, the report's file name (or null) and the options. No new
public type. The report's opt-ins and the page draw through the same renderers (`HistoryHtml`,
`RenderReportDiagnostics`), so the two cannot drift.

### 3.5 The default report, and the opt-ins that bring each view back

- **Gating.** `GenerateHtmlReportCore` gains an internal `showScenarioHistory` parameter beside `showHistorySection`
  and `showReportDiagnostics`. The run hands it the verdicts when `EmbedHistoryInReport` is on and either history
  opt-in is, and each opt-in governs its own pieces: `ShowScenarioHistory` the attribute, sparkline and pill
  (ReportGenerator.cs:2075-2081, 2756-2779, 2860-2861, 2947, 3054), `ShowHistorySection` the section (1839-1841),
  `ShowReportDiagnosticsSection` the diagnostics section (1827-1828). The public `GenerateHtmlReport` keeps its
  signature and passes `showScenarioHistory: history is not null`, so a caller that hands it verdicts gets what it got
  in 4.5.0.
- **CSS.** The history rules (stylesheets.css:1316-1451 and 1995-1997) move to an embedded `history-styles.css`, and
  the diagnostics rules (1261-1314) to `report-diagnostics-styles.css`. A report appends each only when it draws that
  markup; the page always carries both. Specifications.html never carries them. `Stylesheets.HtmlReportStyleSheet`
  (public) loses those rules, which moves it toward Kronikol4J's copy.
- **Scripts.** The verdict lines in the four scripts are fenced with comment lines (`/* kron:verdicts */` and
  `/* kron:/verdicts */`). The generator strips the fenced lines, markers included, unless the report draws verdict
  attributes; with them, it strips only the marker lines. A default report's `advanced-search.js` and
  `report-scenario-feature-map-helper.js` are then byte-identical to 3.10.0's, which are Kronikol4J's (section 2,
  item 5), and an opt-in report's four scripts are byte-identical to 4.5.0's. The deep-search worker is assembled in the
  page from the functions the page loaded, so it sees the same variant. The resource cache keys each variant
  separately. One source per script, no copies.
- **The guards** (section 7.3). With default options, the report for a run with a ledger and diagnostics is
  byte-identical to the report for the same run with neither, and contains none of `history-`,
  `data-history-verdicts` or `report-diagnostic`. Each opt-in brings back its own pieces and no others. With all three
  on, the report is what 4.5.0 drew with both sections asked for.

### 3.6 Options and public surface

| Member | 4.5.0 | From 4.6.0 |
|---|---|---|
| `GenerateLabsReport` (new, `bool`, default `true`) | none | writes the page (section 3.3) |
| `ShowScenarioHistory` (new, `bool`, default `false`; name open, Q14) | none: the sparkline, pill and attribute came with any ledger | puts them back in the report, and with them the `$verdict` operators and the history CSS |
| `EmbedHistoryInReport` (default `true`) | the per-scenario history in the report, and the section with `ShowHistorySection` | the master switch for history in the run's HTML: the page's history and both report opt-ins. Default kept, doc rewritten |
| `ShowHistorySection` (default `false`) | the section in the report | unchanged; its doc names the page |
| `ShowReportDiagnosticsSection` (default `false`) | the diagnostics section in the report | unchanged; its doc names the page |
| `ReportToggleDefaults.DiagnosticsOpen` | the report section's start state | unchanged; the page's list is always open |
| `GenerateHtmlReport(...)` (public) | draws what it is handed | unchanged: same signature, and a caller that hands it verdicts still gets the per-scenario history. It gains the XML doc it never had |
| `MergeableReportRenderer.Render(..., options, history)` | the per-scenario history whenever verdicts are handed; the section on request | follows the options as a run does (per-scenario history and the section only on request), writes the page, and passes `report.Diagnostics` (F3) |
| `kronikol merge --history <ledger>` | the section, sparklines and pills in the merged report | the ledger is read for the page, `Failures.md` and the pointer; the merged report stays clean. The merge has no flag for the opt-ins (Q15) |
| `kronikol ingest --diagnostics-section` | the section in the ingested report | unchanged; the page lists the diagnostics by default |

Nothing is deprecated, renamed or removed, and no CLI flag is added. A host that wants the page's path, as
`IngestResult.TestRunReportHtml` (IngestPipeline.cs:284) gives it the report's, gets nothing new in this plan. That is
Q9.

### 3.7 Bookkeeping, pointers and links

- Output list: `Add("<report>.labs.html", ...)` inside `RunOutputs` (never the unguarded tail, F4), through `WriteFile`
  with `partOfTheRun: true`, so it reaches `Run.json`, the rotation and the upload.
- `PlannedFiles` gains the name, under the same conditions as section 3.3.
- The console pointer lists it after the report and the data file, so `HtmlFileName` still picks the report. The CI
  "Debug this run" text does not change.
- `kronikol merge` writes it from `MergedRunOutputs.Write` beside the `-o` report, and its pointer lists it.
  `RefuseAnOutputThatIsAnInput` (MergeCommand.cs:233-251) needs nothing, since the page is never a `.json`.
- `kronikol ingest`'s pointer, which names files by existence from a hard-coded list (IngestCommand.cs:451), names the
  page only when this ingest wrote it (F6).
- Failures.md, Failures.jsonl, the agent instructions (CLAUDE.md, AGENTS.md and their seven copies) and the PR-link
  action do not change. The digest keeps pointing at `kronikol query history .`, and agents keep using the query tool.
  Q10 asks whether the agent text should name the page.
- A default report gets no link to the page (Q11): the owner asked for no history in it, and a link to history is
  history.

### 3.8 Kronikol4J

One ledger line, after publication: a default .NET report no longer carries history or diagnostics markup, CSS or
verdict code, so two of its scripts are back to the bytes Java ships and its stylesheet lost rules Java never had. The
opt-ins and the page are .NET-only, like the history they show. The stylesheet still differs in other rules (Java's
copy dates from 2026-06-21).

## 4. Versioning

R2 is a minor, 4.6.0, by CLAUDE.md's rules:

- `GenerateLabsReport` and `ShowScenarioHistory` are new options, and a new option is a minor even when its default
  changes what an untouched configuration produces. 3.21.0 took both sections out of the default report the same way,
  with two new options defaulting off, as a minor.
- No existing option's default changes, nothing is deprecated, renamed or removed, and the public
  `GenerateHtmlReport` draws what it drew for its callers.
- The default report losing its sparklines, pills, CSS and verdict code is a report output change, which "is not on
  its own a major bump". It is recorded in the Kronikol4J ledger and called out in the changelog.

The owner's "for the time being" settles when: now, not with v5.

One consequence for later: turning any of these views on by default again flips `ShowScenarioHistory`,
`ShowHistorySection` or `ShowReportDiagnosticsSection` to true, and CLAUDE.md and ROADMAP rule 7 count a flipped
default as a major. Section 13 says what else the re-add has to settle.

R1 is a patch, 4.5.1: defect fixes and doc comments only. Nothing new to call.

## 5. Releases

- **R1, 4.5.1 (patch):** the defects of section 9 that do not depend on the move (F1, F2 and F4 to F11). It goes
  first so that R2 never leans on a test that passes for the wrong reason.
- **R2, 4.6.0 (minor):** the clean default report, the opt-in, the page and its bookkeeping (S2 to S8), with F3.

R1 does not need R2. R2 starts from R1's tag, since both edit ReportGenerator.cs, ReportConfigurationOptions.cs,
IngestCommand.cs and HistoryRunContext.cs.

## 6. Steps

Every step is red, then green, then refactor (CLAUDE.md). "Red on the previous tag" means the new facts are copied into
a worktree at the tag before the change and fail there for the reason they exist.

- **S0, baseline (no release).** Measure, on a real suite with a ledger of at least ten runs (BreakfastProvider on local
  4.5.0 packages, or this repository's dogfood with `KRONIKOL_HISTORY` pointed at a scratch ledger), the size of
  `TestRunReport.html` with and without history, raw and gzip, and per scenario. Count the scenarios whose verdict is
  not `stable`, which section 13 needs. Replace the TL;DR's estimate with the numbers. The harness goes in
  `plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.harness/`. Measure with scripts, never by opening the report.
- **S1, R1 defects.** Section 9, one red fact per defect first.
- **S2, the page.** `LabsReportGenerator`, `labs-styles.css`, and the split of the history and diagnostics rules into
  their two sheets, which the page needs. Unit facts first (section 7.1). Nothing calls the page yet.
- **S3, wiring.** The output list, `PlannedFiles`, the pointer, `GenerateLabsReport`, `ShowScenarioHistory` and the
  docs of section 8.3. Facts on `Run.json`, the rotation and the pointer (section 7.2).
- **S4, the clean default.** The gating, the per-report CSS and the fenced scripts of section 3.5, with the
  byte-identity guard written first. The facts that asserted history in a report through the options either ask for
  the opt-in or are turned round (section 7.3).
- **S5, merge and ingest.** The merged page with `report.Diagnostics` (F3), the merged report clean by default, the
  usage texts.
- **S6, end to end.** Playwright facts on the page and on a default report (section 7.3). The existing History classes
  keep covering the opt-in report.
- **S7, documentation.** Section 8, the changelog, this plan's log, the PLANS_STATUS.md row, and ROADMAP Appendix C for
  the leftovers and the re-add (section 13).
- **S8, audit.** The plan execution checklist: doc comments made false (section 8.3), every default tested (no option
  set at all, as well as each switch), the CLI paths, the export, a real consumer run on local packages before
  tagging, and `release.slnf` built in Release for every target framework.

## 7. Tests

The inventory below was read at 425d9bad. No test in the repository reads the wiki, and there is no golden HTML and
no stylesheet, script or report-size budget, so nothing outside these lists pins today's placement.

### 7.1 The page (S2), in a new `tests/Kronikol.Tests/Reports/LabsReportTests.cs`

1. A run with a ledger and diagnostics writes `TestRunReport.labs.html` holding `<details id="history-section"` open,
   a `class="history-sparkline"` per scenario, the broke scenario's pill, and `<details class="report-diagnostics"`
   open. With no option set.
2. The section's links read `href="TestRunReport.html#sid-<id>"` and carry no `onclick`. With
   `HtmlTestRunReportFileName = "Checkout"` they read `Checkout.html#sid-`, and the page is `Checkout.labs.html`. With
   `GenerateTestRunReport = false` the names are text, with no `<a`.
3. No page when there is nothing for it (no ledger, no entries); none with `GenerateLabsReport = false` and both
   present; history only gives no diagnostics block; diagnostics only (or `EmbedHistoryInReport = false`) gives no
   history.
4. The scenario table has one row per scenario in report order, each outline row under its scenario with its own
   stable id, and two scenarios sharing a stable id each read their own entry (the `HistoryHtml.Entry` slot rule).
5. Feature and scenario names, evidence, diagnostic messages and scenario ids holding `<`, `&` and `"` are encoded
   (the cases of HistoryHtmlTests and IngestHostDiagnosticsTests.cs:85-86).
6. The page has no `<script`. It carries the shared stylesheet, both feature sheets, `labs-styles.css` and the user's
   `CustomCss`.
7. The compare line (`class="history-compare"`) appears when `HistoryCompareBranch` is set
   (HistoryOutputsTests.cs:388-390 asserts it in the report today).
8. A merged page lists the shards' diagnostics (F3). Red on 4.5.0, where `Render` drops them.

HistoryHtmlTests and the renderer fact in IngestHostDiagnosticsTests stay where they are: the report's opt-ins draw
through the same renderers.

### 7.2 Bookkeeping and options (S3)

- RunRotationTests.cs:695-733 (`The_manifest_lists_every_file_the_run_wrote_and_nothing_it_did_not`): the named list
  at 728 gains the page. RunRotationTests.cs:149-186: the exact top-level set at 183-185 gains it, since the run has
  history on.
- New: a directory with no `Run.json` rotates the page (`PlannedFiles`); attachments still move, not copy, when the
  page is present (`AnotherReportIsPresent`); two reports in one folder write and rotate a page each.
- New: the pointer lists the page after the report and still names the report in its "never open" line.
- The defaults: `GenerateLabsReport` true and `ShowScenarioHistory` false, the three existing switches unchanged
  (ReportConfigurationOptionsDefaultsTests).

### 7.3 The guards, and the facts that change (S4)

- **Default byte identity.** The same features rendered (a) with no ledger, no diagnostics and default options, and
  (b) with a ledger and two diagnostic entries and default options, give identical `TestRunReport.html` and identical
  `Specifications.html`. Red on 4.5.0.
- **Default whole-file absence.** In a default report `history-`, `data-history-verdicts` and `report-diagnostic` do
  not occur anywhere, scripts and styles included. The absence facts at HistoryHtmlTests.cs:199-200,
  HistoryOutputsTests.cs:301 and 318 and IngestHostDiagnosticsTests.cs:142 could not assert that, and said so, because
  the stylesheet and the scripts named the classes. `history.replaceState` stays in the scripts, so the pattern is
  `history-`, never `history`.
- **Each opt-in brings back its own pieces.** `ShowScenarioHistory` alone: the attribute, sparkline, pill, verdict
  code and history CSS, and no section. `ShowHistorySection` alone: the section and the history CSS, with no attribute
  and no verdict code. `ShowReportDiagnosticsSection` alone: the section and its CSS. `EmbedHistoryInReport = false`
  with both history opt-ins on: no history in the report or on the page.
- **The fences.** A default report's `advanced-search.js` and feature-map helper equal their v3.10.0 bytes, and the
  other two scripts hold no fenced line; an opt-in report's four scripts equal 4.5.0's; an unbalanced fence fails a
  fact, so a careless edit cannot ship half a block. VerdictSearchTests and VerdictDeepSearchTests load the raw
  resource, whose fences are comments, so they keep testing the opt-in code; one new Jint fact runs `$flaky` against
  the default variant (false, never an error).

| File | Facts | After |
|---|---|---|
| tests/Kronikol.Tests/History/HistoryOutputsTests.cs | 4 of 19 (288-308, 311-329, 357-376 at 372, 379-391 at 388-390) | 288-308: the report's assertions ask for `ShowScenarioHistory` and the section, the page's are added, and `EmbedHistoryInReport = false` still keeps everything away. 311-329: by default neither the section nor the attribute and sparkline (323-324 turned round), and each opt-in brings its own. 372: on the page. 388-390: kept with `ShowHistorySection`, and on the page. The other 15 are unaffected |
| tests/Kronikol.Tests/Reports/Merge/MergeCommandTests.cs | 1 (184-248) | 220-222 assert the merged page, and that the merged report carries none; 229-231 (no section without `--history`) stays; 222 is F1 |
| tests/Kronikol.Tests/Ingestion/IngestHostDiagnosticsTests.cs | 5 | the helper asks for the section, so the report facts stand; the page's list is asserted beside them; 81-82 are F1 |
| tests/Kronikol.Tests/Tool/IngestCommandTests.cs | 1 (424-477) | unchanged (the flag still works), plus the page |
| tests/Kronikol.Tests/Reports/ReportGeneratorDiagnosticsScopeTests.cs | 1 (61) | F1: anchored absence from a default report, presence on the page |
| HistoryHtmlTests, ToggleDefaultsMarkupTests, ToggleDefaultsBaselineTests, ReportToggleDefaultsResolverTests | | unchanged: they draw through the public method, or set the switches they test |
| tests/Kronikol.Tests.EndToEnd: HistorySectionTests (4), HistorySparklineTests (4), HistoryFilterTests (3), HistoryExportTests (1) | 12 | unchanged: `HistoryReportHelper.Generate` (HistoryReportHelper.cs:51-105) draws through the public method with the section asked for, so these now cover the opt-in report |
| ReportSectionWidthTests, ViewportSweepTests | | unchanged: their fixture (`ReportTestHelper.GenerateReportWithEverySection`, ReportTestHelper.cs:2798-2887) asks for every section, so the report's opt-ins stay swept at every width |
| tests/Kronikol.Tests/Reports/StylesheetRulesTests.cs | AllSheets (22-30) | gains the three new sheets, so the selector and breakpoint facts cover them |

New Playwright classes: the page (it opens, the section is open, the sparkline paints its stops as computed
`background-image`, a link opens the scenario it names in the report; StableIdDeepLinkTests already holds the
receiving side) and a default report (no sparkline or pill painted in any header, `$flaky` empties the list without a
console error, Export Filtered HTML carries no history).

**The page's width.** `ViewportSweepTests.Sweep` is shaped for the run report: it requires `.filtering-box` and a
visible `.diagram-toggle` (ViewportSweepTests.cs:203-215). A page mode skips those, opens every `details`, and checks
the same conditions (no horizontal page scroll from 320 px, long tokens break inside their box, also under WCAG text
spacing), with a long scenario name in a history link, a long branch in `.history-meta code`, and a diagnostic message
holding a long path.

**Lanes.** New E2E classes land in E2E (Remainder) by default, as the four History classes do today
(.github/workflows/ci.yml:133-136; HistoryFilterTests is not in the Search & Filters lane, whose filter at 102 does
not name it). ReportSectionWidthTests and ViewportSweepTests run in E2E (Toolbar & Reports) (104-107).

### 7.4 Proofs before the tag

- Every new fact copied into a worktree at v4.5.0 and run there red, reading each failure's message (a fact can be red
  for the wrong reason).
- One mutation per behaviour, each turning a fact red: the sparkline drawn without the opt-in; the history CSS
  appended to a report that draws no history; the fences kept in a default report; the fences stripped from an opt-in
  report; `ShowScenarioHistory` ignored; `EmbedHistoryInReport = false` ignored by the page; the page dropped from
  `PlannedFiles`; the link built from the literal `TestRunReport` instead of the option; `GenerateLabsReport` ignored;
  an empty page written when there is nothing to show; `report.Diagnostics` dropped from the merged page; the page
  listed before the report in the pointer.
- The full Playwright suite once, since the fixtures are shared through base classes (the 4.0.0 default flips turned
  77 facts red across 20 classes).
- `release.slnf` built in Release for every target framework, and a real consumer (BreakfastProvider on local 4.6.0
  packages, nothing configured) run end to end: its reports directory, its page, and its `TestRunReport.html` with no
  history in it.

### 7.5 Unaffected

The analysis and ledger suites (HistoryAnalyzerTests 55, HistoryLedgerTests 39, HistoryAsOfTests 19, DegradedRunTests
18, HistoryShapesTests 15, PartialRunBaselineTests 14, and the rest of tests/Kronikol.Tests/History), the
`kronikol query history` and `kronikol history` suites, the history action template tests,
DiagnosticReportGeneratorTests (16), SkillDriftTests, PluginManifestTests and AgentInstructionsGeneratorTests. Kronikol4J:
none of its 59 parity goldens contains either block, so nothing regenerates.

## 8. Documentation (S7)

### 8.1 Wiki

Edited in a wiki worktree of its own (the checkout is CRLF) and pushed when R2 is published. The 3.21.0 wiki commit
(`b30e98b`) touched six of these pages and the 3.11.0 one (`4db2e51`) twelve, which is the scale to expect.
`tools/wiki-links` checks `[[...]]` links only; the `[text](Page#fragment)` links below are checked by hand.

| Page | Lines | Change |
|---|---|---|
| Cross-Run-History.md | 13-19 | the status paragraph gains 4.6.0 |
| | 551-586 `## In the HTML report (3.11.0)` | rewritten as "In the HTML (4.6.0)": the page by default and what it shows, `GenerateLabsReport`, and the report's opt-ins (`ShowScenarioHistory`, `ShowHistorySection`) for anyone who wants the views in the report. Generated-Reports.md:498 links its anchor and is edited in the same pass |
| | 588-603 `### Which runs a run is read against` | re-parented under a heading of its own, since it is about the as-of window, not the HTML; the slug stays, because Querying-Reports.md:959 links it |
| | 634-640, 110, 798-826 | merged reports (`--history` writes the page); the action table's `read` row; the options table gains `ShowScenarioHistory`, `GenerateLabsReport` and the `ShowHistorySection` row it never had (W3), and `EmbedHistoryInReport` is rewritten |
| Search-Syntax.md | 123-143, 165-166 | `### History verdicts` stays, opening with when it applies: in a report drawn with `ShowScenarioHistory` (4.6.0); in a default report the operators match nothing. Cross-Run-History.md:574 links `#history-verdicts`, so the slug stays |
| Report-Configuration.md | 178, 205, 209, 210, 252 | `ShowReportDiagnosticsSection`, `ShowHistorySection` and `DiagnosticsOpen` name the page; `EmbedHistoryInReport` and `HistoryCompareBranch` rewritten; rows for `ShowScenarioHistory` and `GenerateLabsReport` |
| Generated-Reports.md | 3-24, 79-85, 486, 498, 886-918 | an Output Files row and a short section for the page; the report's contents list points at it; the width and export bullets; the `GenerateHtmlReport` signature (W8) |
| Diagnostics-and-Debugging.md | 66-80, 127-158 | the history kinds; where host diagnostics land: the JSON, the page, `IngestResult`, and the report when `ShowReportDiagnosticsSection` asks |
| Ingesting-External-Captures.md | 640-657, 255-273 | the page by default; a `--diagnostics-section` row (W5): it still adds the section to the ingested report |
| Merging-Parallel-Reports.md | 64, 274-279 | `--history` writes the page and leaves the merged report clean; merged diagnostics are listed there (F3) |
| FAQ.md, Home.md | 106-108; 62 | one line each |
| Capture-Time-Redaction.md | 10-15 | the page carries diagnostic messages and scenario names, so it joins the derived-files list (W9) |

About twenty pages mention only `DiagnosticMode` or `DiagnosticReport.html`. They do not change, because that page does
not. No screenshot shows either feature, so none is retaken.

### 8.2 Repository

- README.md:16 and nuget-readme.md:65 (packed into every package): "a sparkline and verdict in the report ... `$flaky`
  in the search box" becomes the labs page, with `ShowScenarioHistory` for the report.
- templates/github-actions/kronikol-history/README.md 3-4 and 152-153, and read/action.yml:3-4: "the run's own report"
  becomes the run's labs page.
- CHANGELOG.md: a 4.5.1 section and a 4.6.0 section, each saying which part of the version moved and why. 4.6.0 says
  what a reader of a default report loses (section 1.3) and the switch that brings each piece back, that `$flaky` in
  a default report now matches nothing, that `kronikol merge --history` writes the page instead of putting history in
  the merged report, and that the Kronikol4J ledger records the change.
- Unchanged: agent-instructions.md and its seven copies, the skills, templates/agents/CLAUDE.md, the PR-link action.

### 8.3 Text that ships in the packages

`GenerateDocumentationFile` is on, so these are part of R2, not a later docs commit:

- ReportConfigurationOptions.cs: `ShowReportDiagnosticsSection` (420-429), `EmbedHistoryInReport` (760-765),
  `ShowHistorySection` (767-776), `HistoryCompareBranch` (737-742, "the report's History section"), `HistoryMinRuns`
  (608-613, "the report says how many runs are recorded"), and the two new options.
- ReportToggleDefaults.cs:86-89; `ResolvedToggleDefaults.DiagnosticsOpen` (ReportToggleDefaultsResolver.cs:37), which
  has no doc.
- MergeableReportRenderer.cs:6-11 and 21-23; IngestPipeline.cs:251-257; HistoryRunContext.cs:6-10 ("the HTML");
  HistoryHtml.cs:7-20; ReportGenerator.cs comments at 1824-1826 and 1833-1838 and the summary at 4112.
- `GenerateHtmlReport` (ReportGenerator.cs:1026) gains an XML doc.
- CLI text: MergeCommand.cs comment 111-112 and usage 357, 382-385; IngestCommand.cs usage 686-692 (`--diagnostic`
  lands on the page, and in the report with `--diagnostics-section`); the merge blurb in Commands.cs:26-27 ("into one
  TestRunReport.html plus the merged data file").

### 8.4 Outside this repository

- **Kronikol4J**, after publication, one bullet appended to the divergence ledger (C:/Code/Kronikol4J/docs/
  REMAINING_PARITY.md, after the 4.5.0 entry at 2406), committed as `Ledger: .NET 4.6.0 ...`. Draft:

  > **History and diagnostics left the default run report (.NET 4.6.0, <date>).** Not mirrored, a ledger entry only.
  > A default .NET `TestRunReport.html` no longer draws the sparkline and verdict pill or `data-history-verdicts`, and
  > carries no history or diagnostics CSS and no verdict search code; the History section and the Report diagnostics
  > block were already opt-in (3.21.0). `ShowScenarioHistory`, `ShowHistorySection` and
  > `ShowReportDiagnosticsSection` bring each back, and a new page beside the report, `TestRunReport.labs.html`,
  > carries all of it by default. A default report's `advanced-search.js` and `report-scenario-feature-map-helper.js`
  > are back to the bytes this port ships, and its stylesheet lost the `.history-*` and `.report-diagnostic*` rules,
  > which the port never had. A ledger no longer changes a default .NET report, so the 3.9.0-3.11.0 entry's "byte
  > parity holds whenever there is no ledger" stops being a condition for default options. When the port takes
  > cross-run history, the page is what it ports first.

- **BreakfastProvider**, the owner's consumer: `.github/pages/index.html:101` and `README.md:945-947` advertise "a
  History section beside the timeline", which its reports lost in 3.21.0 (it sets none of the options). After R2 the
  sparkline and `$flaky` claims are false too (W10), unless it opts in. One consumer commit when it moves to 4.6.0,
  either rewording the paragraph or setting the opt-ins. DOORSTEP_PLAN.md F18 already calls that paragraph insider
  language, and ROADMAP 13.1 refreshes the demo before the launch. `doorstep.yml` is unaffected: it checks
  `data-stable-id`, which stays.
- **Plans**: this plan's PLANS_STATUS.md row, and a ROADMAP Appendix C row for the re-add (section 13). No V5_PLAN.md
  row: nothing is deprecated.

## 9. Defects found while researching

CLAUDE.md asks for every defect found on the way to be fixed. Each fix starts with a fact that is red on the code
before it.

| | Defect | Evidence | Fix | Release |
|---|---|---|---|---|
| F1 | Three assertions pass on text the stylesheet holds, not on markup | ReportGeneratorDiagnosticsScopeTests.cs:61 asserts `"Report diagnostics"` while the section is off, matched by the CSS comment at stylesheets.css:1261; IngestHostDiagnosticsTests.cs:81-82, the same comment and the selector at 1301; MergeCommandTests.cs:222, `"history-sparkline"`, matched by the rule at 1317 | anchor each on emitted markup (`<details class="report-diagnostics"`, `<span class="history-sparkline"`) and prove it fails with its markup removed | R1 (test-only) |
| F2 | Export Filtered CSV's Scenario column holds more than the name | report-export-function.js:141 takes `summary.h3`'s `textContent`, so a row reads like `Pay by card 120ms broke📋🔗` (duration badge, labels, the pill, the copy and link glyphs); Generated-Reports.md:498 promises "feature, scenario, status, duration"; only tests/Kronikol.Tests/Reports/ExportFilteredViewReportTests.cs:54-58 touches it, checking that the function's name is in the page | read the name from an element that holds only the name, with a Playwright fact on the downloaded file | R1 |
| F3 | A merged report never shows its diagnostics | MergeableReportRenderer.Render passes `showReportDiagnostics` (95) but never `diagnostics: report.Diagnostics` (MergeableReport.cs:92); MergeCommand.cs:114 sets only `ShowHistorySection` | the merged page lists `report.Diagnostics`, and so does the merged report when the section is asked for | R2 |
| F4 | Two tail writes can lose `Run.json` | DiagnosticReport.html (ReportGenerator.cs:620-621, and 215-216 on the zero-scenario path) and CiSummary.md (665-668) are written after `RunOutputs` with no catch; one exception there ends the generation before `Run.json`, the artifact publish, the CI debug section and the pointer | the isolation `RunOutputs` gives (an `OutputFailure` diagnostic, the rest still written) | R1 |
| F5 | DiagnosticReport.html can land outside the run | DiagnosticReportGenerator.cs:30 writes to `Path.Combine(BaseDirectory, ReportsFolderPath)`, not the run's directory: with a blank `ReportsFolderPath` it lands one level up, outside `Run.json`, the rotation and the upload; with null, `Path.Combine` throws (F4) | write through the run's directory | R1 |
| F6 | `kronikol ingest`'s pointer names files by existence | IngestCommand.cs:451 lists `TestRunReport.html`, `.json` and `Failures.md` when they exist, so a failed write names the previous ingest's file under this run (the run's rule is ReportGenerator.cs:623-626) | name what this ingest wrote | R1 |
| F7 | Two doc comments sit on the wrong member | HistoryRunContext.cs:189-199 (public: `Append`'s summary sits on the `AppendedHere` field, so `Append` has none); IngestCommand.cs:574-578 (`LooksLikeInput`'s summary on `HoldsSpans`) | move them | R1 |
| F8 | `DiagnosticMode`'s doc says too little | ReportConfigurationOptions.cs:417 says "enables diagnostic logging", never names DiagnosticReport.html, and does not say that the page's assertion section fills only with `Track.DiagnosticMode` (Track.cs:26, 401) | say both | R1 |
| F9 | ComponentDiagram.html renders in quirks mode | ComponentDiagramReportGenerator.cs:183-237 writes `<html>` (216) with no DOCTYPE and no `<title>` (RUN: grep) | add both, with a Playwright paint check that the diagram draws as before; a Kronikol4J ledger line if Java's copy matches the old bytes | R1 (confirm first) |
| F10 | `ComponentDiagramOptions` documents five options that nothing reads | `ShowRelationshipFlows`, `RelationshipFlowStyle`, `ShowSystemFlameChart`, `LowCoverageThreshold`, `MaxFlameChartTests` (ComponentDiagramOptions.cs:35-53; RUN: no use anywhere else in src) | their docs say they have no effect; removing them is a v5 row | R1 |
| F11 | A zero-scenario DiagnosticMode pass is archived under the previous run | ReportGenerator.cs:215-216 overwrites DiagnosticReport.html in a directory whose `Run.json` still lists the previous run's copy, so the next rotation files the discovery pass's page under that run | confirm red first, then skip the write or record it as its own run's | R1 (confirm first) |

Documentation defects, no bump, fixed in S7 or in a docs commit at any time: W1 `_Sidebar.md:133-138` renders the four
Querying Reports links under Cross-Run History (since 3.9.0); W2 a blank line at Generated-Reports.md:21 cuts the
`Run.json` and `runs/<run>/` rows out of the Output Files table, which also has no DiagnosticReport.html row; W3 the
options table of Cross-Run-History.md has no `ShowHistorySection` row; W4 Report-Configuration.md has no rows for
`HistorySlowerMinMs`, `HistoryAlternatingRuns`, `HistoryCountRuns`, `HistoryShapeTemplates` and `HistoryDegradedBy`;
W5 the ingest flags table has no `--diagnostics-section` row; W6 Diagnostics-and-Debugging.md:69 names a panel that has
been off by default since 3.21.0 and three history kinds where there are four; W7 Diagnostics-and-Debugging.md:86 and
API-Reference.md:53 say the `ReportDiagnostics.Analyse` strings go to the report's footer (they go to the console);
W8 Generated-Reports.md:886-918 shows a `GenerateHtmlReport` signature about twenty parameters short; W9
Capture-Time-Redaction.md:10-15 omits TestRunReport.html and DiagnosticReport.html; W10 BreakfastProvider (8.4).

## 10. Risks and traps

1. **Bare-substring assertions.** The report is one file holding markup, scripts and the stylesheet, so a class name in
   a test can match CSS instead of markup (F1 is three live cases). Anchor every presence fact on emitted attributes.
   The default whole-file absence fact is safe only because a default report no longer carries the CSS and the verdict
   code, and only for `history-`, never `history`.
2. **Three producers.** A run, `kronikol merge` and `kronikol ingest` each write a report, and each must leave it clean
   by default and write the page. The merge builds a fixed options object (MergeCommand.cs:114); it loses
   `ShowHistorySection = history is not null`, so a merge with `--history` writes the page and a clean report. The
   changelog says so, since a merge has no way to ask for the opt-ins (Q15).
3. **Defaults.** Facts that only set the switches prove nothing about a consumer who sets none. Each behaviour is also
   asserted with no option set.
4. **The CI process is on a branch.** History facts that seed a stream named `main` read differently on CI. Pin
   `HistoryBranch = ""` in helpers, and run the history facts plain, as `GITHUB_ACTIONS=true GITHUB_REF_NAME=main`, and
   as a pull request (`GITHUB_REF_NAME=42/merge GITHUB_BASE_REF=main`), as the cross-run history plan did.
5. **Two homes for one view.** Until the re-add, the report's opt-ins and the page draw the same pieces. One renderer
   per view (`HistoryHtml`, `RenderReportDiagnostics`) and one stylesheet per feature keep them from drifting; the E2E
   History classes guard the report's path and the page's facts guard the page.
6. **The fences.** A verdict line edited outside its fence ships in every default report, and a fence left open strips
   the rest of its script. The fence facts of section 7.3 catch both.
7. **Pointer heuristics.** `HtmlFileName` is the first `.html` the pointer lists and `IsDataFile` takes any `.json`, so
   the page is listed after the report and never gets a `.json` companion.
8. **Attachments.** A top-level `.html` missing from `Run.json` turns attachment moves into copies. The page is
   written through `WriteFile` with `partOfTheRun: true`, and a fact holds it.
9. **Size.** The scenario table carries the tooltips the report carried, so a large suite's page is large (the
   ESTIMATED 1.2 KB per scenario). S0 measures it; if it matters, Q7 decides.
10. **Other claimants of the same files.** ROADMAP rule 5 names "the history renderer" and `stylesheets.css` among
    files with several claimants. Check `git log` and the live sessions before S4 starts, and run it after any toolbar
    or report-rendering work in flight.
11. **Releasing.** Push a release only after the previous one lists on nuget.org (the template pins), date the
    changelog by the local date, keep "fixes #N" out of commit messages, and never quote the CI skip marker.

## 11. Open questions for the owner

**Decided by the owner, 2026-10-05:**

- **Q1.** The page is written by default whenever it has something to show.
- **Q3.** Now, not with v5 ("for the time being"). By section 4 it is a minor.
- **Q13.** The current views stay available in the report as opt-ins, off by default, and nothing is deprecated.
- **Q2.** The page is `{report}.labs.html` (`TestRunReport.labs.html` by default).
- **Q14.** The new opt-in is `ShowScenarioHistory`.

The owner green-lit the plan the same day ("implement the plan in full"), so the questions below are taken as
recommended. As asked when written, each with its recommendation:

| | Question | Recommendation |
|---|---|---|
| Q2 | The page's name | **`{report}.labs.html`**: it says the views are not finished, and it leaves room for the next unfinished view. Alternatives: `{report}.history.html` (names one of its two contents), a fixed `Labs.html` (collides between two reports in one folder), or two pages (`{report}.history.html` and `{report}.diagnostics.html`, against the ask's "a separate doc"). |
| Q4 | What the page says about itself | One line under the title: "Labs: views still being designed. Their layout may change in any release." It tells a reader why these views are apart, and it frees the page from the care the report gets (Kronikol4J parity, byte pins). |
| Q5 | Does DiagnosticReport.html stay separate? | **Yes** (section 3.1). Folding it in later is possible once the page has the run's directory rule. |
| Q6 | Does the Background calls section stay in the report? | **Yes.** It is evidence about calls, not report health, and the ask names the diagnostics sections. |
| Q7 | Does the page list every scenario, or only the History section's lists? | **Every scenario, collapsed**: it is the per-scenario history the report showed, and dropping it would lose the verdicts of scenarios the lists leave out (stable ones, and anything past a list's 50). |
| Q8 | R1 as its own patch first, or folded into 4.6.0? | **Its own patch first**, so R2 starts with honest tests. |
| Q9 | Should `IngestResult` gain the page's path, as it has `TestRunReportHtml` (IngestPipeline.cs:284)? | **Not now**: new public surface nobody has asked for. |
| Q10 | Should the agent instructions (Reports/CLAUDE.md, AGENTS.md and their copies) name the page? | **Not now**: agents read history through `kronikol query history`, which has everything the page has except the charts. |
| Q11 | Should a default report link to the page? | **No**: a link to history is history, and the console pointer names the page. |
| Q12 | Should the page get a verdict filter in place of `$flaky`? | **No**: its lists are already grouped by verdict, and the opt-in report keeps `$flaky`. CROSS_RUN_HISTORY_PLAN.md §8.1 deferred a verdict control to the toolbar redesign, which never planned it. |
| Q14 | The new opt-in's name | **`ShowScenarioHistory`**, beside `ShowHistorySection` and `ShowReportDiagnosticsSection`. Alternatives: `ShowHistoryInScenarioHeaders` (says where), `ShowHistoryBadges`. |
| Q15 | Should `kronikol merge` and `kronikol ingest` get flags for the report's opt-ins (`ingest` already has `--diagnostics-section`)? | **Not now**: nobody has asked, and the re-add design (section 13) may change what they would switch. |

**Placement.** The move touches both roadmap tracks: A (`History/HistoryHtml.cs`) and B (`ReportGenerator.cs`,
`stylesheets.css`, the search scripts). It serves the launch bar's "how it looks" row by taking unfinished views out
of the report a visitor opens first. If the owner places it, the roadmap's amendment line takes the next decision
number, D33.

## 12. Not in this plan

- Deprecating or removing any switch: nothing is, while section 13 is open.
- The re-add design itself (section 13).
- A verdict filter on the page, a link from the report, the page's path on `IngestResult`, agent text naming the
  page, and CLI flags for the opt-ins (Q9 to Q12, Q15).
- Folding DiagnosticReport.html into the page (Q5), and moving the Background calls section (Q6).
- The dashboard (DASHBOARD_PLAN.md, stage 9, waiting on D9), which is cross-run where this page is per run.
- A message when a `$` query matches nothing: no channel for one exists today (section 1.3).
- Dark mode for the page: the report has none either.
- Removing or implementing the five unread `ComponentDiagramOptions` (F10 corrects their docs only).
- `kronikol ctrf` writing no history where the run's own ctrf-report.json does: the data file has no verdicts to
  carry, so the two differ by design.

## 13. Re-adding later

The owner wants to consider how best to bring these views back. This plan decides none of that, and keeps the ground
ready for it.

**What stays ready:**

- Every current view, one switch away: `ShowScenarioHistory`, `ShowHistorySection`, `ShowReportDiagnosticsSection`. A
  design can be tried on the owner's own runs (the dogfood in `ci-summary-preview.yml`, BreakfastProvider) against the
  current form, with no code change.
- One renderer per view, shared by the report and the page, one stylesheet per feature, and the verdict search in
  fenced blocks. A redesign changes one place.
- The tests: HistoryHtmlTests and the four E2E History classes keep covering the current form in the report.
- The page, which shows everything by default, so the data stays in view while the report is clean.

**What the re-add design has to settle:**

- **Which pieces earn a place in the report.** Today a sparkline sits on every scenario once a ledger holds a run,
  stable or not. Candidates: a pill only where the verdict is not `stable`; a sparkline only where the series is not
  all passes; the section only when something changed (it already opens only then); diagnostics only of kinds a reader
  can act on (`CaptureDegraded`, `RenderFailure`, `HistoryUnavailable`) rather than informational ones
  (`StepsNotStartingWithCapital`, `OptionNotApplied`).
- **Where.** In the scenario headers, a toolbar control (CROSS_RUN_HISTORY_PLAN.md §8.1 deferred a verdict control to
  TOOLBAR_REDESIGN_PLAN.md's vocabulary, which never planned one), a tab, or this page linked from the report.
- **How it looks against the launch bar.** TOOLBAR_REDESIGN_PLAN.md's Option C and the `--kron-*` tokens are v5 work,
  and DASHBOARD_PLAN.md's scenario panel plans to reuse the sparkline colours through a shared palette.
- **Evidence on noise.** The history noise audit of 2026-09-15 measured 77% of count-only verdicts as one-offs. S0
  counts the share of scenarios whose verdict is not `stable` on a real ledger; repeat it on the dogfood and
  BreakfastProvider ledgers before choosing what to show by default.
- **Versioning.** Turning a switch on by default is a flipped default, so a major (CLAUDE.md, ROADMAP rule 7); v5 is
  stages 11 and 12. A redesign behind new options is a minor, but leaves today's switches beside it.

## Appendix A. Edit sites

| File | R | Change |
|---|---|---|
| src/Kronikol/Reports/LabsReportGenerator.cs (new) | R2 | the page |
| src/Kronikol/Reports/history-styles.css, report-diagnostics-styles.css, labs-styles.css (new), Kronikol.csproj | R2 | the moved rules and the page's own, embedded |
| src/Kronikol/Reports/stylesheets.css | R2 | 1261-1314, 1316-1451, 1995-1997 leave |
| src/Kronikol/Reports/advanced-search.js, report-scenario-feature-map-helper.js, report-search-function.js, report-search-index.js | R2 | the verdict lines fenced (section 1.1 lines) |
| src/Kronikol/Reports/ReportGenerator.cs | R1, R2 | R2: the gating at the call sites (1824-1841, 1999-2000, 2075-2081, 2756-2779, 2860-2861, 2947, 3054), the per-report CSS (1306) and script variants (70-95), the output list and `PlannedFiles` (474-604, 736-772), the pointer's candidates (630-635), the docs in 8.3. R1: the tail writes (215-216, 620-621, 655-672) |
| src/Kronikol/Constants/Stylesheets.cs | R2 | the two feature sheets beside `HtmlReportStyleSheet` |
| src/Kronikol/History/HistoryHtml.cs | R2 | a link target for the page (`<report>.html#sid-<id>`); the class doc |
| src/Kronikol/ReportConfigurationOptions.cs | R1, R2 | R2: `GenerateLabsReport`, `ShowScenarioHistory` and the docs in 8.3. R1: `DiagnosticMode`'s doc (F8) |
| src/Kronikol/Reports/ReportToggleDefaults.cs, ReportToggleDefaultsResolver.cs | R2 | docs only |
| src/Kronikol/Reports/Merge/MergeableReportRenderer.cs, MergedRunOutputs.cs | R2 | the options-driven gating, the merged page, `report.Diagnostics` (F3), the pointer |
| src/Kronikol.Tool/MergeCommand.cs, IngestCommand.cs, Commands.cs | R1, R2 | R2: the merge's options, both usage texts, the blurb. R1: the ingest pointer (F6), the misplaced summary (F7) |
| src/Kronikol/Ingestion/IngestPipeline.cs | R2 | `HostDiagnostics` doc |
| src/Kronikol/History/HistoryRunContext.cs | R1, R2 | R1: `Append`'s summary (F7). R2: the class doc |
| src/Kronikol/Reports/DiagnosticReportGenerator.cs | R1 | the directory (F5) |
| src/Kronikol/Reports/report-export-function.js | R1 | the CSV's scenario name (F2) |
| src/Kronikol/ComponentDiagram/ComponentDiagramReportGenerator.cs, ComponentDiagramOptions.cs | R1 | F9, F10 |
| tests (section 7), plans/PLANS_STATUS.md, plans/ROADMAP.md (Appendix C), CHANGELOG.md, README.md, nuget-readme.md, templates/github-actions/kronikol-history/ | R1, R2 | sections 7 and 8 |
| Directory.Build.props, the plugin manifests, the template pins | R1, R2 | the version, per CLAUDE.md |

## Log

- 2026-10-05: drafted; revised the same day after the owner's clarification (Q1, Q3 and Q13 decided). Nothing
  executed yet.
- 2026-10-05: green-lit ("implement the plan in full"); the owner chose both recommended names (Q2
  `{report}.labs.html`, Q14 `ShowScenarioHistory`). The other open questions are taken as recommended. Executed in
  a worktree of its own (`C:/Code/Kronikol-hdm`).
- 2026-10-05, R1 = 4.5.1 (patch), executed. Every fix started from a fact that failed on 4.5.0 for its own reason
  (harness `r1/red-4.5.0-unit.txt`: 23 unit facts; `r1/red-4.5.0-e2e.txt`: 5 Playwright facts). What went beyond
  section 9, each found while executing it:
  - F1 covered five assertions, not three: `FailureClusterReportTests` asserted `failure-clusters` twice, which the
    stylesheet's `.failure-clusters` selector satisfies. The proof is by mutation (`mutations/mutate.py`, the `f1-*`
    entries): with each piece of markup broken, all 17 facts of the old assertions still passed and each new one failed.
    A new fact asks for the section and reads the run's render failure in it.
  - F2: the Feature column had the same defect as the Scenario column (`"Orders /api/orders smoke"`).
  - F12, new: feature and scenario names and the report's title were written into the HTML unencoded. Measured on 4.5.0,
    a scenario named `Refund <img src=x onerror=...>` drew the image and ran its script, and `kronikol ingest` takes
    names from files other tools wrote. A fact renders a marker tag in every field a run hands the report: exactly
    those three leaked. ComponentDiagram.html's heading and `alt` text, the same. Kronikol4J writes them raw too.
  - F4 isolated the CI tail after `Run.json` as well (job summary, artifact list, debug section, pointer), wrote the
    job summary even when `CiSummary.md` cannot be written, and made those writes read the run's environment, as the
    merge's already did. The test that used `CiSummary.md` as a stand-in for a killed run now uses an internal seam,
    `ReportGenerator.AfterOutputsForTests`.
  - F7 found eleven orphaned summaries, not two; `DocCommentPlacementTests` reads the shipped XML docs and fails on any
    member (not type: partial types join one summary per file) with two.
  - F10 also corrected `ArrowColorMode.Performance`: no generated report has passed relationship stats since
    2.0.92-beta, so that mode draws uncoloured arrows. Their removal is `V5_PLAN.md` open question 8, and
    `ROADMAP.md`'s Appendix C stats row mentions the docs.
  - F11 keeps the previous run's `DiagnosticReport.html` when that run's `Run.json` lists it, rather than skipping the
    write always: the page is most useful in a run whose contexts were never enqueued, which is such a pass.
  The full core suite and the full Playwright suite (972 passed, 28 skipped) passed before the tag. The wiki edits wait
  in `wiki/r1_wiki.py` until 4.5.1 is published.
- 2026-10-05: 4.5.1 published. Release run 37346008134 passed, with CI 37346005336, CodeQL 37346005294 and CI
  Summary Preview 37346005255 on the same commit (4a1aad8b); nuget.org lists all 62 ids at 4.5.1. The wiki has R1's
  edits (8f491c5) and Kronikol4J's ledger its line (f723514).
- 2026-10-05, R2 = 4.6.0 (minor), executed. S0 ran first (harness `s0/`): on the ReqNRoll xUnit v3 example (8
  scenarios, ten earlier runs) the history markup was 7,124 bytes, 897 per scenario, and no verdict was other than
  `stable`; the first run against a ledger file that did not exist yet already drew a one-run sparkline on every
  scenario, which the `EmbedHistoryInReport` doc denied (fixed in that doc). Departures from the plan, each decided while
  executing it:
  - The page is written whenever the run's verdicts are not null, which includes that first run (an empty ledger is
    read, and the page says so). Section 3.3's "a first run with no diagnostics leaves the reports directory exactly as
    4.5.0 does" holds only for a run with history off or no ledger found.
  - `PlannedFiles` lists the page whenever `GenerateLabsReport` is on, not only when it will have content: content is
    known only after the analysis, and a page an older run left is that run's to rotate either way.
  - The fences are line comments with an else branch (`// kron:verdicts`, `// kron:else` with each stand-in line written
    `//|`, `// kron:/verdicts`), not the `/* */` pair section 3.5 sketched: the default variant must restore lines the
    verdict code replaced (four functions take a fifth argument), not only drop added ones. `VerdictFences` refuses an
    unbalanced fence, and a fact holds every embedded script to that. The default variants of three scripts are the
    3.10.0 bytes; `report-search-index.js`'s is 4.5.0's with c9c3b068 taken out (512bc85a changed it since).
  - `MergeableReportRenderer.Render` writes the merged page itself (so a library caller who hands it history still gets
    it somewhere), and `MergedRunOutputs` names it in the pointer by the same rule.
  - The CI "Debug this run" text took the first `.html` the run listed for its "do not open" line, which with the report
    switched off would have been the page: it now skips the page.
  - The Playwright fixture for the page and the default report is a real run with default options against a seeded
    ledger (`HistoryReportHelper.GenerateWithLabsPage`), so the default-report facts fail on 4.5.1 for their own reason.
  - The labs page's table scrolls inside its wrap. At 100% text it fits at 320 px, so the wrap mutation survived the
    sweep; a third sweep with text at 200% (WCAG 1.4.4, text-only zoom) is the fact that needs it.
  - The wiki pass also fixed W1, W2, W4 and W7 of section 9's list, the merge workflow's upload example (a `#` after a
    path in a `path: |` block is part of the path, not a comment), and listed the page on CI-Artifact-Upload.
  Proofs: the behaviour facts failed at v4.5.1 for their own reasons (harness `r2/red-4.5.1-unit.txt`: 30 facts;
  `r2/red-4.5.1-e2e.txt`: 7), with the two new options stubbed as properties that do nothing; each of the 16 `r2-*`
  mutations turned at least one fact red (`mutations/r2-results.txt`); the cascade check found no computed style
  changed by moving the rules, over 92 elements at 1280 and 375 px (`r2/cascade.txt`). The core suite passed 6,499
  with 2 skipped, the history facts also under a push to main and a pull request's environment, the search-engine
  suite 214, and the full Playwright suite 981 with 28 skipped. `release.slnf` packed in Release for every target
  (62 packages), and BreakfastProvider's xUnit in-memory lane ran on those packages with nothing configured: 212 of
  212 passed, twice; its `TestRunReport.html` holds no `history-`, `data-history-verdicts` or `report-diagnostic`,
  and `TestRunReport.labs.html` (199 KB, 21 KB gzipped; 252 KB on the second run) holds the History section and 212
  rows, all `stable`, linking into the report, and moved under `runs/` with its run.
  While this ran, the owner saw the Report diagnostics section in a report a Node application's `kronikol ingest`
  wrote with 4.5.1: its command line passed `--diagnostics-section`, the opt-in, which 4.6.0 keeps.
- 2026-10-05: 4.6.0's CI failed one Playwright fact on Linux, `LabsPageTests.Each_sparkline_paints_a_stop_per_run`
  (one stop where seven were seeded): `HistoryReportHelper.GenerateWithLabsPage` seeded the ledger with no CI context
  and then ran the generator in the process's environment, where `GITHUB_*` put the run on its branch's stream with
  no earlier runs. The product is right; the helper now runs the generator in an empty environment, as the rotation
  facts do, and the labs, default-report, sweep and History classes pass under no CI, a push to main and a pull
  request (22 of 22 each). The environment variants of S8 had covered the unit facts only. Test-only, no bump.
