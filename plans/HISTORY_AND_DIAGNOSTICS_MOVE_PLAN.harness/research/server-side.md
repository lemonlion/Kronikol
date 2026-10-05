# Server-side inventory: cross-run history UI and "Report diagnostics" in TestRunReport.html

Researched 2026-10-05, read only, on main at 425d9bad. Line numbers are for that commit. Paths are relative to
C:/Code/Kronikol unless absolute. "RG" = src/Kronikol/Reports/ReportGenerator.cs.

## TL;DR

- Every piece of history markup in the HTML comes from one internal class, `src/Kronikol/History/HistoryHtml.cs`,
  called from RG in four places: the History section (RG 1839-1841), the per-scenario attribute + sparkline + pill
  (RG 2075-2081), the parameterised group (RG 2762-2779, 2860-2861) and the outline rows (RG 2947 flat, RG 3054
  grouped). The "Report diagnostics" block is `ReportGenerator.RenderReportDiagnostics` (RG 4111-4144), emitted at
  RG 1827-1828. Nothing in the title area, stats table, CI block, filter box or toolbar shows history or diagnostics.
- The data enters through the PUBLIC `ReportGenerator.GenerateHtmlReport` (RG 1026-1069: `diagnostics` 1055,
  `history` 1067, `showHistorySection` 1068, `showReportDiagnostics` 1069; the method has no XML doc at all), from
  exactly two producers: the run (RG 486-489, via internal GenerateHtmlReportCore) and `MergeableReportRenderer.Render`
  (src/Kronikol/Reports/Merge/MergeableReportRenderer.cs:57-95).
- TestRunReport.json/.xml/.yml carry NO history verdicts (they do carry `diagnostics` and `background`). After an
  HTML strip, verdicts survive in Failures.md and Failures.jsonl (failing scenarios only), ctrf-report.json (opt-in,
  every scenario), the one-line console pointer / CI "Debug this run", the ledger, and `kronikol query history`
  (recomputed from the data file + History.run.json + ledger). HTML-only today: the run-trend charts, a browsable
  view of passing scenarios' verdicts (fixed, slower, behaviour-changed, new, flaky-but-passed, quarantined), the
  Absent / New-dependencies lists as a page, sparkline tooltips with per-run detail, and `$flaky`/`$broke` search.
- A new top-level file written through `WriteFile` from the `Add(...)` list is mostly automatic: RunFileCollector ->
  Run.json -> rotation into runs/<run>/ -> Azure DevOps upload (.html is a published extension) -> the manifest
  guard test. NOT automatic: PlannedFiles (pre-manifest rotation), the console pointer / CI summary, CLAUDE.md /
  AGENTS.md and their copies, the merge and ingest tails, the PR-link action, docs, the Java ledger.
- DiagnosticReport.html is a different thing: an opt-in (DiagnosticMode) tracking-setup troubleshooting page that
  reads process-global registries, written OUTSIDE the isolated output list with its own directory rule, ported
  byte-for-byte by Kronikol4J. It holds none of the DiagnosticEntry list. It has a ready slot in PlannedFiles,
  Run.json, rotation and the ADO upload, but its gating, data source and plumbing make it a poor home as-is.
- Bugs found on the way (section 10): B1 DiagnosticReport.html and CiSummary.md are written unguarded after the
  isolated outputs, so one exception there loses Run.json, the artifact publish and the pointer; B2 the
  DiagnosticReport directory rule diverges from ResolveReportsDirectory for a blank ReportsFolderPath; B3 a merged
  report can never show Report diagnostics (Render never passes `report.Diagnostics`); B4
  ReportGeneratorDiagnosticsScopeTests.cs:61 is vacuous (satisfied by a CSS comment); plus doc-comment and smaller defects.

---------------------------------------------------------------------------------------------------------------

## 1. ReportGenerator: outputs, bookkeeping, and every consumer of "what was written"

### 1.1 Order of events in CreateStandardReportsWithDiagramsCore

- RG 173-196 `CreateStandardReportsWithDiagramsInEnvironment` (internal; public wrapper `CreateStandardReportsWithDiagrams`
  RG 150-151): sets the AsyncLocal `ActiveReportsDirectory = ResolveReportsDirectory(options)` (RG 176), opens its own
  `ReportDiagnosticsCollector` unless the host scoped one (RG 182), `RunFileCollector.Begin` (RG 185).
- RG 206-220 zero-scenario guard; with logs present and DiagnosticMode on it still writes DiagnosticReport.html (RG 215-216).
- RG 243-249 background attribution, one `DiagnosticKind.BackgroundCalls` entry per expired group, RecordOptionDiagnostics.
- RG 253-259 query.cs decision (OptionNotApplied diagnostic).
- RG 345 CI metadata; RG 354-360 `history = HistoryRunContext.Create(...)` (null only when history is switched off).
- RG 365-369 run id; RG 377-384 `RunRotation.Prepare(... PlannedFiles(...))`; RG 389 `beforeFirstWrite` hook.
- RG 445-452 reports dir created, `queryScriptPlanned`, attachments copied; RG 456-458 component diagram drawn for non-BrowserJs.
- RG 464 `reportDiagnostics = ReportDiagnosticsScope.Current?.Entries ?? []` -- THE ONE SNAPSHOT shared by the HTML,
  the data files and the digest. Anything recorded later (OutputFailure from RunOutputs, HistoryUnavailable from
  Append, a DiagnosticReport failure) is only in the collector (and in IngestResult.Diagnostics for an ingest).
- RG 474-604 the output list; RG 606 `written = RunOutputs(actions)`.
- RG 610 `history?.Append()` (after every output, so a failed report still leaves its fragment).
- RG 612-618 `ReportDiagnostics.Analyse` console strings (a different thing from DiagnosticEntry; one of them starts
  "Report diagnostics: N log entries ..." -- src/Kronikol/Reports/ReportDiagnostics.cs:39).
- RG 620-621 DiagnosticReport.html (DiagnosticMode), unguarded.
- RG 627-653 RunSummary for the pointer and CI; RG 655-672 CiSummary.md (WriteCiSummary), unguarded write at 667.
- RG 678-687 Run.json (WriteRunManifest RG 779-791); RG 689-699 CI artifact publish; RG 709-718 CI debug section;
  RG 721-722 console pointer, last.

### 1.2 The output list and how an output is added (RG 474-604)

| Label given to Add | File | RG lines | Gate | Writer |
|---|---|---|---|---|
| `{HtmlSpecificationsFileName}.html` | Specifications.html | 481-484 | GenerateSpecificationsReport (default true) | GenerateHtmlReport -> WriteFile |
| `{HtmlTestRunReportFileName}.html` | TestRunReport.html | 486-489 | GenerateTestRunReport (default true) | GenerateHtmlReportCore -> WriteFile |
| `{YamlSpecificationsFileName}.{ext}` | Specifications.yml | 491-494 | GenerateSpecificationsData | WriteFile |
| `{HtmlTestRunReportFileName}.{ext}` | TestRunReport.json (mergeable 498-506, standard 507-510) | 496-511 | GenerateTestRunReportData | WriteFile |
| "TestRunReport schema" (a label, not a file name) | TestRunReport.schema.json | 513-516 | GenerateTestRunReportSchema | WriteFile |
| "ComponentDiagram.html" (a label even when ComponentDiagramOptions.FileName differs) | {FileName}.html (+ .png/.svg under Local) | 518-525 | GenerateComponentDiagram (default true) | own writer, File.WriteAllText + Record |
| Failures.md, Failures.jsonl (two actions, one Lazy) | same | 535-560 | GenerateFailuresDigest | WriteFile |
| `{YamlSpecificationsFileName}.md` | Specifications.md | 562-568 | GenerateSpecificationsMarkdown | WriteFile |
| ctrf-report.json | same | 572-577 | GenerateCtrfReport (default false) | WriteFile |
| History.run.json | same | 582-585 | GenerateHistoryFragment && history != null | WriteFile |
| CLAUDE.md (AGENTS.md in the same action) | both | 587-595 | WriteAgentInstructions | WriteAgentInstructionsFile -> WriteFile(partOfTheRun: false) |
| query.cs | same | 600-604 | WritesQueryScript (RG 861-862) && not blocked | WriteFile(partOfTheRun: false) |

Outside the list (the "tail", NOT isolated): DiagnosticReport.html (RG 620-621), CiSummary.md (RG 655-672), Run.json (RG 678-687).

Mechanics:
- `void Add(string name, Action run)` (RG 474-475) appends to a list. `RunOutputs` (RG 814-834) runs the whole list in
  `Parallel.Invoke`, each action in try/catch: an exception becomes `DiagnosticKind.OutputFailure` "Could not write
  {name}" plus a console warning, and every other output still runs. It returns the LABELS that completed; that set
  (`written`) is what the pointer names (RG 630-635, 652) -- never File.Exists, because a previous run's file exists
  (tests/Kronikol.Tests/Reports/StaleOutputTests.cs:90-130). So a new output's label must be its file name if the
  pointer is ever to name it.
- `WriteFile(text, fileName, partOfTheRun = true)` (RG 5855-5894): writes into `CurrentReportsDirectory` (RG 116-117,
  an AsyncLocal that flows into the Parallel.Invoke workers); records the full path with `RunFileCollector.Record`
  when partOfTheRun; on IOException writes a salvage `<name>2<ext>` (recorded too), prints which copy is whose, and
  rethrows so the output is marked failed.
- `partOfTheRun: false` = describes the directory, not the run: never in Run.json, never rotated (query.cs RG 603;
  CLAUDE.md/AGENTS.md RG 5835-5847). A history page describes THIS run, so it belongs with partOfTheRun true.
- Writers that resolve their own directory record themselves: ComponentDiagramReportGenerator.cs:62 and :170,
  DiagnosticReportGenerator.cs:36, RG 668 (CiSummary.md).
- Cross-file links are decided from the options, not raced: the digest links `TestRunReport.html#sid-` only when
  GenerateTestRunReport is on (RG 539-543, "a decision, not a race: the HTML is written by a sibling action");
  `queryScriptPlanned` (RG 451) the same. A link from the report or the digest to a new page, or from the page back
  to the report, needs the same treatment.
- `ScopeReportsDirectory` (RG 128-133) is how `kronikol merge` reaches WriteFile without a run.

### 1.3 PlannedFiles (RG 725-772)

Lists: data file, TestRunReport.html, schema, Specifications.html, specs data, specs .md, Failures.md +
Failures.jsonl, ctrf-report.json, History.run.json (only when history != null), {ComponentDiagram FileName}.html /
.png / .svg, DiagnosticReport.html (DiagnosticMode), CiSummary.md (WriteCiSummary). Never CLAUDE.md, AGENTS.md,
query.cs, Run.json. Used ONLY by the rotation for a directory with no Run.json (src/Kronikol/Reports/RunRotation.cs:199-203):
the top-level files this run would overwrite are what moves. Deliberately not what the manifest is built from (doc
RG 732-735). A new file must be added here or a manifest-less directory's copy is overwritten instead of kept.

### 1.4 RunRotation (src/Kronikol/Reports/RunRotation.cs, internal static)

- Prepare (108-143): at most once per directory per process (Claimed 57-58); keep count from KeepRuns, then
  KRONIKOL_KEEP_RUNS, then 3 off CI / 0 on CI (ResolveKeepRuns 82-96).
- What moves (Rotate 174-309): the previous Run.json's `Files` (bare top-level names; IsTopLevelName 425-431 drops
  Run.json, CLAUDE.md, AGENTS.md and anything with a separator) and `Attachments` (attachments/<name>); with no
  manifest, the PlannedFiles that exist (199-203) and no attachments. Only when those include THIS report's data
  file or HTML (IsThisReport 412-414; check 211-212), so a folder shared by Checkout.* and Payments.* never rotates
  the other report. Data file first (224-227), manifest last (270-274); staged in runs/.incoming-<name>/ then one
  rename (295-306); failures are ReportRotationFailed diagnostics, never a failed run.
- Attachments are COPIED instead of moved when "another report is present" = any top-level *.html not in the
  manifest (AnotherReportIsPresent 416-418). A new .html that is not in the manifest (partOfTheRun false, or a
  stale copy from an earlier run whose write failed) flips attachments from move to copy.
- Reconstruct (454-534) names a manifest-less run from History.run.json (472-491) and the Failures.jsonl header line
  (493-515).
- Prune (551-576) keeps KeepRuns plus the newest failing run (and on CI every attempt of this run id).
- Fixed names already collide between two reports in one folder (Run.json, Failures.md: comment 206-210). A new page
  with a fixed name (History.html) adds a collision; one derived from HtmlTestRunReportFileName does not, but then the
  digest, the agent text and IsThisReport derive the name the way they derive the data file's.

### 1.5 Run.json (src/Kronikol/Reports/RunManifest.cs, public sealed record)

- FileName "Run.json" (30); members runManifestVersion, run, at, suite, scenarios, failed, partial, kronikolVersion,
  files, attachments (ToJson 79-101). Written last (RG 678-687 -> WriteRunManifest RG 779-791) from
  `RunFileCollector.Current.Snapshot()` = recorded AND still on disk (RunFileCollector.cs:81-89). Absent = run did not finish.
- `RunFileCollector.Record` (src/Kronikol/Reports/RunFileCollector.cs:49-72) keeps only top-level names and
  attachments/<name>. A page in any other subfolder (history/index.html) is silently not the manifest's: never
  listed, rotated or ADO-published.
- Merge never writes one (no collector scoped; RunFileCollector.cs:17-19). Ingest does (it goes through the run path).
- Guard: tests/Kronikol.Tests/Reports/RunRotationTests.cs:695-733 asserts manifest == every file on disk except
  CLAUDE.md, AGENTS.md, Run.json and query.cs, with DiagnosticMode (715) and every other output on, and names each
  expected file (728). A new page written without Record fails it; its name should join the list.

### 1.6 CiArtifactPublisher (src/Kronikol/Reports/CiArtifactPublisher.cs)

- `PublishedExtensions = [.html .yml .md .json .jsonl .xml]` (17). `ReportFiles` (25-30, internal): top-level files
  with those extensions plus query.cs. `RetainedFiles` (37-42): every file under runs/<run>/.
- Azure DevOps: one `##vso[artifact.upload]` per file (57-72). GitHub Actions: writes only `reports-path` and
  `reports-retention-days` to GITHUB_OUTPUT (74-84); the workflow uploads the folder.
- A new .html is uploaded on ADO automatically; a .css/.js/.png/.svg companion would not be (attachments/ and the
  component .png/.svg are already missing on ADO: plans/AZURE_DEVOPS_PARITY_PLAN.md F4, line 88, not green-lit).
- Callers: RG 689-699 (after Run.json, with retained runs); MergedRunOutputs.cs:171-175 (no retained runs).

### 1.7 RunSummaryConsoleWriter (src/Kronikol/Reports/RunSummaryConsoleWriter.cs, public)

- Candidates: run = `{report}.html`, `{report}.{ext}`, Failures.md filtered by `written` (RG 630-635); merge = html,
  data file, Failures.md if written (MergedRunOutputs.cs:124-128); ingest CLI = hard-coded ["TestRunReport.html",
  "TestRunReport.json", "Failures.md"] by existence (src/Kronikol.Tool/IngestCommand.cs:451).
- Summarise (99-141) drops names absent from disk. Build (144-193): "Kronikol: reports written to <dir> (files)" with
  only the data file sized (Describe 341-342); the history line (156-157); previous run kept (162-163); failures,
  "agents: read CLAUDE.md first; never open <data file>" (182-184); query.cs line (188-189). GitHub ::notice (199-208)
  names Failures.md only.
- History line: run -> `history.Summary()` = "history: " + HistorySummary.Line + CompareTail
  (src/Kronikol/History/HistoryRunContext.cs:243-253), only when verdicts say anything or something failed (RG 641-643);
  merge -> "history: " + Line WITHOUT CompareTail (MergedRunOutputs.cs:137-139); ingest -> none (IngestCommand.cs:448-459).
- Heuristics a new file can break: HtmlFileName = the FIRST .html in Files (333-334), used in "Do not open X.json
  or X.html" (257); DataFileName (330-331) = the first IsDataFile, and IsDataFile (344-349) treats ANY .json/.xml/.yml
  that is not *.schema.* as the data file. Put a new .html after the report (or exclude it from HtmlFileName) and
  never list a .json companion.
- `RunSummary` is a public positional record (34-41) with `History` last; extra facts are init properties
  (PreviousRun 48, QueryScriptWritten 56) so the constructor does not change -- the pattern for a new "page written" fact.

### 1.8 CiSummaryGenerator and BuildCiSummarySection

- `CiSummaryGenerator.GenerateMarkdown` (src/Kronikol/Reports/CiSummaryGenerator.cs:14): no history, no diagnostics,
  no .html reference anywhere in the file.
- `RunSummaryConsoleWriter.BuildCiSummarySection` (250-281) "## Debug this run": do-not-open line naming the data
  file and HtmlFileName (257); query commands; query.cs (265-267); the history line (269-270); previous run
  (274-275); "The artifact also carries Failures.md" (277-278). Appended to CiSummary.md (RG 663) or written alone on
  a failing CI run with WriteCiSummary off (RG 709-718); merge MergedRunOutputs.cs:146-169.

### 1.9 FailuresDigestGenerator (src/Kronikol/Reports/FailuresDigestGenerator.cs, public)

- Public Generate (133-137, `HistoryVerdicts? history`); internal overload with queryScript (146-180).
- Links: per failure "[open in the report]({html}.html#sid-{stableId})" -- built at 281, printed 661-662, JSONL
  `deepLink` 900; only when the run writes the HTML (RG 539-543). It never links to #history-section or any history
  page; its pointer for history is "`kronikol query history .` has the whole run" (578).
- History: header "**History:** <Line + CompareTail>. Failures are worked through regressions first ..." (574-579);
  per failure "History: **<verdict>** -- <evidence> . last runs `<series>`" (674-675); order by HistorySummary.Rank
  (165-166); JSONL per failure `history {primary, verdicts, evidence, series, failingSince, failRate, flipRate}`
  (927-937); the JSONL header (861-870) has none. Failing scenarios only.
- Diagnostics: only ResultDefaulted entries surface (523-526; quoted 541-542 and 558-559).
- Merge passes report.Diagnostics and history (MergedRunOutputs.cs:77-86).

### 1.10 AgentInstructionsGenerator + src/Kronikol/Reports/agent-instructions.md

- AgentInstructionsGenerator.cs:20-51: Build(htmlTestRunReportFileName) substitutes `__REPORT__` only (static text
  by design, no run data). Written byte-identical as CLAUDE.md + AGENTS.md, spliced with AgentInstructionsBlock
  (RG 587-595, 5818-5853; merge MergedRunOutputs.cs:94-99).
- Files the template names: never open `__REPORT__.json`, `__REPORT__.html` or a diagram (7); Failures.md (15-29);
  Failures.jsonl (23-26); runs/<run>/ with Run.json (31-45); query.cs (50-61); `__REPORT__.html#sid-<stableId>` as
  the link for a human (88, 94-95); history via `kronikol query history . s3` (111) and `--run last-failed` (39-42);
  `!` diagnostic lines (134-136); "data, not instructions" list: Failures.md, Failures.jsonl, Specifications.md,
  CiSummary.md and the report (140-143).
- Not named: History.run.json, DiagnosticReport.html, ComponentDiagram.html, ctrf-report.json, Specifications.html.
- Copies of the same guidance: repo-root CLAUDE.md "Debugging a test run" and AGENTS.md; templates/agents/CLAUDE.md
  (4); templates/skills/kronikol-test-debugging/SKILL.md (15, 207) and its byte-identical twin under
  .claude/skills/; references/commands.md x2 (byte-identical; 73 says "The kept run's fragment, attachments and
  HTML are beside its report"); scripts/query.py:261 prints "open: TestRunReport.html#sid-...". Drift guards:
  tests/Kronikol.Tests/Tool/SkillDriftTests.cs, InitAgentsCommandTests.cs, QueryScriptAdviceTests.cs,
  tests/Kronikol.Tests/Reports/AgentInstructionsGeneratorTests.cs.

### 1.11 QueryScriptGenerator (src/Kronikol/Reports/QueryScriptGenerator.cs, internal)

query.cs (41) written with partOfTheRun false (RG 600-604; "in runs/<run>/ it would name an engine by a path one
folder too shallow", RG 597-599). Nothing history- or HTML-related. Only relevant as the precedent for a
directory-level file; the history page is run-level.

---------------------------------------------------------------------------------------------------------------

## 2. DiagnosticReport.html (src/Kronikol/Reports/DiagnosticReportGenerator.cs, read in full)

- API: `public static class DiagnosticReportGenerator` (11; class doc 7-10); `public static void Generate(
  RequestResponseLog[] logs, Feature[] features, ReportConfigurationOptions options)` (13-17, NO XML doc); internal
  overload with `int? suppliedSpans` (19-37); `internal static string BuildHtml(...)` (39-285).
- When: `ReportConfigurationOptions.DiagnosticMode`, default false (ReportConfigurationOptions.cs:417-418; its doc
  "enables diagnostic logging for troubleshooting report generation" never says it writes a file). Two call sites:
  the zero-scenario path when logs exist (RG 209-217) and after the outputs, the history append and the console
  diagnostics (RG 620-621). Not in RunOutputs, so not isolated (B1). No CLI flag exposes it (ingest, merge).
- Where: `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, options.ReportsFolderPath)` (30), hard-coded name
  "DiagnosticReport.html" (32), `File.WriteAllText` (33) -- not WriteFile (no salvage), not CurrentReportsDirectory and
  not ResolveReportsDirectory. Same directory as the run for an absolute or ordinary relative ReportsFolderPath
  (ingest sets it absolute, src/Kronikol.Tool/IngestCommand.cs:374); diverges for a blank/whitespace value and throws
  for null (B2). Then `RunFileCollector.Record(path)` (36), a no-op for a path outside the run's directory.
- Bookkeeping: in PlannedFiles (RG 767-768); in Run.json via Record; rotated with its run; ADO-published (.html);
  NOT named by the pointer, the CI summary, the agent instructions or Failures.md. Mentioned by the console hint
  src/Kronikol/Reports/ReportDiagnostics.cs:114 and by the wiki (Diagnostics-and-Debugging.md and ~13 integration pages).
- Content (BuildHtml): configuration table of 8 options (53-62); log summary (65-67); entries per service (70-77);
  per test, top 50 (80-90); "unknown" test-id breakdown (93-113); unpaired requests (116-125); orphaned test ids and
  scenarios with no log entries (128-154); span count, supplied or InternalFlowSpanStore (157-160); discovered
  activity sources (162-175); tracking components and never-invoked hints (178-252); unmatched HTTP client names
  (255-265); assertion value resolution from Track.DiagnosticLog (268-281). DOCTYPE and its own <title> (46-47),
  inline 6-rule CSS (48), no favicon, no shared stylesheet, no CustomCss. It contains NONE of the DiagnosticEntry list.
- Tests: tests/Kronikol.Tests/Reports/DiagnosticReportGeneratorTests.cs (16 facts, all through BuildHtml; nothing
  covers Generate's directory, the zero-scenario path or a failing write); RunRotationTests.cs:695-733 (in the
  manifest). Kronikol4J ports it with goldens (C:/Code/Kronikol4J/kronikol4j-report/src/main/java/io/kronikol/report/
  diagnostics/DiagnosticReportGenerator.java; C:/Code/Kronikol4J/docs/REMAINING_PARITY.md around 1616-1620 and
  1702-1706), so any change to its bytes is a divergence-ledger line.
- Natural home for the DiagnosticEntry list? Mostly a different thing:
  - Gating/audience: an opt-in setup-troubleshooting page; the DiagnosticEntry list is per-run fact a reader needs
    without opting in (CaptureDegraded, RenderFailure, HistoryUnavailable, ReportRotationFailed, OptionNotApplied...).
  - Data: it reads process-global registries (TrackingComponentRegistry, UnmatchedClientNameRegistry,
    ActivitySourceDiscovery, InternalFlowSpanStore, Track.DiagnosticLog) that only the test process has. `kronikol
    merge` has none of them but HAS the DiagnosticEntry list (MergeableReport.Diagnostics,
    src/Kronikol/Reports/Merge/MergeableReport.cs:92); `kronikol ingest` has the list (IngestResult.Diagnostics) and
    empty registries.
  - Plumbing: wrong directory rule, unguarded, written after the diagnostics snapshot.
  - For it: an existing slot in PlannedFiles / Run.json / rotation / ADO upload, the name, Java already knows the file.
  - If the plan uses it anyway: move it into the isolated list, resolve the directory through CurrentReportsDirectory,
    decide whether a DiagnosticEntry section writes the file regardless of DiagnosticMode (new behaviour for an
    existing option), make merge able to write it, and ledger the Java divergence.
  - Otherwise one new page holding both the History view and the diagnostics list, built only from data all three
    producers have (HistoryVerdicts + DiagnosticEntry list). Timing: inside the Add list it sees the same snapshot
    as the JSON (RG 464); written after RunOutputs/Append it could also show OutputFailure and the append's
    HistoryUnavailable, which today reach only the console -- but then it must be guarded and recorded by hand, and
    it would disagree with the JSON's `diagnostics` array.

---------------------------------------------------------------------------------------------------------------

## 3. ComponentDiagram.html as a precedent (and Specifications.html as the better one)

- Options (src/Kronikol/ComponentDiagram/ComponentDiagramOptions.cs, public record): FileName "ComponentDiagram" (11),
  EmbedInTestRunReport true (14), Title (17), PlantUmlTheme (26), ParticipantFilter (29), RelationshipLabelFormatter
  (32), ArrowColorMode (47), DependencyColors (50). Five documented options that nothing in src reads:
  ShowRelationshipFlows (35), RelationshipFlowStyle (38), ShowSystemFlameChart (41), LowCoverageThreshold (44),
  MaxFlameChartTests (53) (B6). Gate: ReportConfigurationOptions.GenerateComponentDiagram (default true, :221).
  Panel toggle: ReportToggleDefaults.ComponentDiagramVisible (ReportToggleDefaults.cs:65-66).
- Writer: `public static ComponentDiagramResult GenerateComponentDiagramReport(logs, reportOptions,
  perBoundarySegments, wholeTestSegments)` (src/Kronikol/ComponentDiagram/ComponentDiagramReportGenerator.cs:17-65;
  the two segment parameters are unused). Directory via `ReportGenerator.ResolveReportsDirectory(reportOptions)` (50)
  -- equal to CurrentReportsDirectory in a run, not ScopeReportsDirectory-aware. File.WriteAllText + Record (58-62),
  and the image beside it (168-170). Added under the fixed label "ComponentDiagram.html" (RG 518-525); PlannedFiles
  lists {FileName}.html/.png/.svg (RG 760-766).
- Shares nothing with the main report: GenerateHtml (183-237) has no DOCTYPE (quirks mode), no <title>, four inline
  CSS rules, the default favicon (ignores CustomFaviconBase64), no CustomCss/CustomLogoHtml; under BrowserJs it adds
  the context-menu styles and the PlantUML browser render script (206-213). It does not use
  Stylesheets.HtmlReportStyleSheet.
- The main report does NOT link to ComponentDiagram.html (no href anywhere in RG). It embeds the same PlantUML in a
  hidden panel `#component-diagram` (RG 1878-1893), toggled by the "Component Diagram" toolbar button
  (RG 1714-1715; script RG 1256-1275), gated by EmbedInTestRunReport (RG 66-67, 488).
- Better precedent for "same look and feel": Specifications.html, a second document from the same GenerateHtmlReport
  with includeTestRunData false (RG 481-484). It shares Stylesheets.HtmlReportStyleSheet (RG 1306; public at
  src/Kronikol/Constants/Stylesheets.cs:23), CustomCss, favicon, logo and scripts, has its own toggle defaults
  (SpecificationsToggleDefaults) and a theme sheet (UserStylesheets RG 59-64). The report stylesheet has no
  dark-mode rules (no prefers-color-scheme in src/Kronikol/Reports/stylesheets.css).

---------------------------------------------------------------------------------------------------------------

## 4. History data available at render time, and what stays after the strip

### 4.1 HistoryRunContext (public sealed class, src/Kronikol/History/HistoryRunContext.cs)

- Created once per run before any output (RG 354-360); never throws; null only when history is switched off
  (KRONIKOL_HISTORY=off -> Disabled) -- note the class doc (6-10) says it is handed to "the digest, the CTRF
  document, the console pointer, the HTML".
- Members: Location (29), Roster (32), Run (35; Partial resolved), Shapes (38), Verdicts (41; null when no ledger
  could be read), Ledger (44), WriteLedger (47), Generator (50), Quarantine (53), Aliases (56). Fragment() (59) =
  the History.run.json text. Create(...) (66-174): resolves the ledger (HistoryPathResolver), builds roster/run/
  shapes, reads the window, runs HistoryAnalyzer.Analyse with every History* option plus Branch/CompareBranch
  (123-144), records HistoryShapeTemplate / HistoryUnavailable / HistoryLedgerDamaged / HistoryPartialRun
  diagnostics. Append() (205-237) is called after the outputs (RG 610). Summary() (243-253) = the pointer/CI line,
  including "history: no ledger found ..." when Verdicts is null.
- HistorySummary (public static, 257-326): Line (260-287), Degraded (294-295), Times (298), CompareTail (304-308),
  Rank (315-325).
- "Compare": there is no Compare method on the context; the second reading is `HistoryVerdicts.Compare`
  (HistoryVerdicts.cs:415), set when HistoryCompareBranch is, printed by CompareTail and by HistoryHtml.Section (118-119).

### 4.2 The model (src/Kronikol/History/HistoryVerdicts.cs and HistoryModel.cs, all public)

- HistoryVerdicts (HistoryVerdicts.cs:361-436): Suite, Stream, RunId, RunsRecorded, MinRuns, ColdStart,
  ColdStartMessage, Partial, PreviousFullCount, FailingOutside, Scenarios (one ScenarioHistory per roster position),
  Absent, Counts, Runs (RunPoint trend, oldest first, current last), NewDependencies, FirstSeen, Compare; Find (418),
  At (426), Count (432), HasAnything (435).
- ScenarioHistory (206-325): StableId, Slot, Name, Feature, Current, Primary, Verdicts, Evidence, RunsSeen,
  RealVerdicts, Failures, FailRate, Flips, FlipRate, FailingEpisodes, FlakyShortfall, RunsSinceLastFlip,
  LastFailedRunsAgo, FailingSince, Series, Points (HistoryPoint per prior run), DurationMs, DurationP95 (+IsRaw),
  PreviousShapeSet, ShapeSet, PreviousCalls, Calls, NewCalls, GoneCalls, Quarantine, FailuresInDegradedRuns;
  Has(), VerdictNames.
- RunPoint (358), HistoryPoint (195-196), FailingSince (203), AbsentScenario (332), FailingOutside (341),
  HistoryVerdictKind (7-60), HistoryVerdictNames (63-99: the kebab names used in data-history-verdicts and the pills),
  HistoryAnalysisOptions (102-143).
- HistoryModel.cs: HistoryRosterEntry (11), HistoryRoster (27-119), HistoryShapes (129-157), HistoryRun (165-291),
  HistoryFragment (318-336, Write/Parse). HistoryFormat.FragmentFileName = "History.run.json" (HistoryFormat.cs:37).

### 4.3 What HistoryHtml renders from it (src/Kronikol/History/HistoryHtml.cs, internal static)

- Entry (29-34) slot-aware lookup; VerdictAttribute (37-48) -> ` data-history-verdicts="broke,stable"`; Sparkline
  (54-72) one span with a hard gradient stop per run and a tooltip from Points (241-268); Pill (75-83); GroupPill
  (86-94); Section (100-160): summary line, meta (stream, run, earlier runs, partial, degraded pace), cold-start
  note, compare line, two SVG bar charts from Runs (121-128, 189-231), lists New failures / Failing since / Flaky /
  Newly fixed / Slower / Behaviour changed / New scenarios / Quarantined (131-138, 50 each), Absent (140-147),
  New dependencies (149-156). Open when HasAnything (104); there is no toggle default for it.
- Section links are `#sid-<id>` driven by `reveal_url_anchor` (175). On a separate page they become
  `{report}.html#sid-<id>` -- the same shape as Failures.md's deep link, and the report's hash handler already
  resolves `#sid-` on load (src/Kronikol/Reports/report-url-hash-function.js:3-8, 26).

### 4.4 What each other output carries (so: what survives an HTML strip)

| Output | History | Diagnostics |
|---|---|---|
| TestRunReport.json / .xml / .yml | nothing (no verdict field in any writer or the schema) | `diagnostics` [{kind, message, scenarioId}] (MapDiagnosticsJson RG 4259-4261; JSON RG 4510; XML RG 5015/5041; YAML RG 5378-5385; schema ~RG 6100) = the RG 464 snapshot; plus the `background` object (RG 4246-4257) |
| Mergeable TestRunReport.json | nothing | `diagnostics` (RG 4746) |
| Failures.md | header line + per-failure verdict/evidence/series, regressions first; failing scenarios only | ResultDefaulted only |
| Failures.jsonl | per-failure `history` object (FailuresDigestGenerator.cs:927-937) | none |
| ctrf-report.json (GenerateCtrfReport, default false) | every scenario: `flaky` = ledger flaky OR passed on retry, and `extra.kronikolHistory` {primary, verdicts, evidence, series} (src/Kronikol/Reports/CtrfReportGenerator.cs:181-202, 288-295) | none |
| History.run.json | the run's own line, roster and shapes; no verdicts, no prior runs (HistoryRunContext.cs:59) | none |
| Console pointer / CI "Debug this run" | one line (Summary) | none (the console gets the separate ReportDiagnostics.Analyse strings) |
| Ledger + `kronikol query history` | everything, recomputed from TestRunReport.json + History.run.json beside it + the ledger (src/Kronikol/Query/QueryCommand.History.cs:508-537): summary, cold start, degraded, verdict lists, absent (333-341), new dependencies (344-347), per-scenario stats and per-run points, `--json` rows with runs[] (801) | `kronikol query summary` prints the diagnostics array (QueryCommand.Overview.cs:133-140); every verb prints `!` lines |
| IngestResult.Diagnostics (public) | -- | the whole collector, including entries recorded after the snapshot |

HTML-only today: the pass-rate and duration trend charts (Runs); a browsable view of verdicts of scenarios that did
not fail (fixed, slower, behaviour-changed, new, flaky-but-passed, quarantined); Absent and New dependencies as a
page; sparkline tooltips with per-run commit/duration/attempt/error; and the `$flaky`/`$broke`/`$new` search
operators. Those are client code reading `data-history-verdicts`: advanced-search.js:249-294,
report-scenario-feature-map-helper.js:14-16, report-search-index.js:251-253, 358, 481 (deep-search worker). The C#
search index (src/Kronikol/Reports/SearchIndex/) has no verdicts. `kronikol query history` covers all but the charts as text.

---------------------------------------------------------------------------------------------------------------

## 5. CLI and tool paths

### 5.1 kronikol merge (src/Kronikol.Tool/MergeCommand.cs, internal)

- `--history <ledger>`: parsed 56-59. ReadHistory (174-212): the file must exist (exit 2 otherwise); window
  hard-coded 50 (184); HistoryBuildOptions defaults, no shapes; only Branch (the PR target) in HistoryAnalysisOptions
  (208-211); quarantine/aliases read beside the ledger. Rendered with
  `new ReportConfigurationOptions { ShowHistorySection = history is not null }` (111-115) -- the one place a CLI turns
  the History section on -- and passed to MergedRunOutputs (155) for the digest and pointer. Usage: synopsis 357,
  text 382-385 ("the History section, sparklines and verdict pills in the HTML, and the history lines in
  Failures.md"). Tests: tests/Kronikol.Tests/Reports/Merge/MergeCommandTests.cs:216, 237, 241.
- What merge writes: the -o HTML (MergeableReportRenderer.Render); `<-o>.json` (WriteMergedData 263-272) unless
  --no-json; then MergedRunOutputs.Write (src/Kronikol/Reports/Merge/MergedRunOutputs.cs:37-182): Failures.md and
  Failures.jsonl (72-92), CLAUDE.md and AGENTS.md (94-99), query.cs (103-110), `<base>.schema.json` (112-120),
  CiSummary.md with --ci-summary (146-160) else the debug section on a failing CI merge (161-169), the artifact
  publish with --publish-artifacts (171-175, no retained runs), the pointer (178-179). No Run.json, no rotation, no
  History.run.json, no ctrf, no ComponentDiagram.html, no DiagnosticReport.html, no Specifications. Failures.md,
  CLAUDE.md etc. take fixed names in the -o directory; the schema takes the -o base name.
- Directory sweeps skip Kronikol's own JSON by name: `WrittenBesideAReport = [History.run.json, ctrf-report.json,
  Run.json]` (353, used 312-317). Only matters if the new page has a .json companion; HTML is never swept.
- RefuseAnOutputThatIsAnInput (233-251) checks the -o .html and .json against the inputs; a new output named after
  -o would need the same check only if it could be a *.json.
- MergeableReportRenderer (public static, src/Kronikol/Reports/Merge/MergeableReportRenderer.cs): Render(report,
  outputPath, title, options, history) (24); scopes the directory (52-55); the GenerateHtmlReport call (57-95)
  passes `history`, `options.ShowHistorySection`, `options.ShowReportDiagnosticsSection` but NOT
  `diagnostics: report.Diagnostics` and NOT `background` (B3). MergeFilesToHtml (201-202) takes no history.
  EmbedHistoryInReport is not consulted on this path (the caller passes history or not).

### 5.2 kronikol ingest (src/Kronikol.Tool/IngestCommand.cs, internal)

- `--diagnostics-section` (39, 167-169) -> `options.ShowReportDiagnosticsSection` (392); usage 691-692.
- `--diagnostic <kind>:<msg>` (repeatable; 245-253; TryParseDiagnostic 552-572) -> IngestRequest.HostDiagnostics
  (434); usage 686-690 says it reaches "the HTML report's Report diagnostics section when --diagnostics-section asks
  for it". Malformed --spans lines also become MalformedLine host diagnostics (366-367).
- No history flag. An ingest still reads history implicitly through HistoryRunContext.Create (KRONIKOL_HISTORY, or a
  .kronikol/.git above the reports directory or the tool's base directory), so an ingested report can carry
  sparklines and write History.run.json. The CLI pointer gets no history line (448-459); IngestResult has no verdicts.
- No DiagnosticMode flag. `options.WriteRunSummaryToConsole = false` (378); the CLI prints its own pointer with
  hard-coded candidates (451).
- Tests: tests/Kronikol.Tests/Tool/IngestCommandTests.cs:457, 470 (anchored on `<summary>Report diagnostics (`),
  613 (the console line, not HTML); tests/Kronikol.Tests/Ingestion/IngestHostDiagnosticsTests.cs:70-171.

### 5.3 IngestPipeline (public, src/Kronikol/Ingestion/IngestPipeline.cs)

- IngestRequest.HostDiagnostics (247-258, public; its doc names the HTML section and the CLI form), added first to
  the collector (364); the collector is scoped around generation (480-487), so IngestResult.Diagnostics (266-296) is
  the full list including late entries. IngestResult.TestRunReportHtml (284) hard-codes "TestRunReport.html" (a
  sibling property is the natural way to hand a host the new page's path). DefaultOptions (320-327): nothing
  history- or diagnostics-related. ReportsDirectory resolved with ResolveReportsDirectory (422).

### 5.4 kronikol history (src/Kronikol.Tool/HistoryCommand.cs + HistoryCommand.Maintenance.cs, internal)

- record / init / show / verify / prune / compact / gate / quarantine / rename / doctor / import (dispatch 166-180,
  usage 704-727). Text only; no verb writes or reads HTML. gate prints new-failures / already-failing sections
  (Maintenance 107-146); doctor reads the Run.json of runs under runs/ (Maintenance ~410-420).

### 5.5 kronikol query (src/Kronikol/Query; also what query.cs runs)

- No verb reads the HTML. The only HTML awareness is DeepLinkPrefix (QueryCommand.Narrative.cs:753-762):
  `<data>.html#sid-` when that file exists beside the data file (used at 47 and 281).
- `history` verb (VerbTable.cs:260-…; flags --history, --flaky, --new, --failing, --regressed, --changed, --branch,
  --compare-branch, --min-runs, --alternating-runs, --count-runs, --degraded-by, --calls, --suite, --run, --sid,
  --window, --count, --offset, --limit, --json) reads the ledger and the History.run.json beside the report
  (QueryCommand.History.cs:74, 364, 508-537). `summary` prints the diagnostics (VerbTable.cs:125;
  QueryCommand.Overview.cs:133-140).

### 5.6 Where a flag has to be registered

- kronikol query (four code places): VerbTable.Flags FlagSpec rows (src/Kronikol/Query/VerbTable.cs:55-110); the
  verb's VerbSpec.Flags array and Usage lines (e.g. `history` at 260); QueryOptions.KnownFlags
  (src/Kronikol/Query/QueryOptions.cs:171-180); a case in QueryOptions.Parse (e.g. 321, 368). Legality is derived
  (QueryCommand.cs:309-325). Docs: references/commands.md x2 (byte-identical: .claude/skills/kronikol-test-debugging/
  and templates/skills/kronikol-test-debugging/), SKILL.md x2, wiki. Drift guards: tests/Kronikol.Tests/Tool/
  DescribeTests.cs:101, 111; QueryCommandTests.cs:1887, 1902, 1915; SkillDriftTests.cs:142, 161, 206.
- kronikol merge / ingest / history: a hand-written switch and PrintUsage in each command file, plus the verb blurb
  in src/Kronikol.Tool/Commands.cs:24-47 (merge 26-27: "into one TestRunReport.html plus the merged data file").
  No table and no drift guard beyond tests that read PrintUsage (IngestCommandTests.cs:132-134, 183-184).
- MCP server: none exists; only comments anticipating one (QueryCommand.Describe.cs:17, QueryOptions.cs:23,
  VerbTable.cs:38-41).
- Environment variables involved: KRONIKOL_HISTORY (path or `off`; HistoryFormat.cs:46-49), KRONIKOL_KEEP_RUNS
  (RunRotation.cs:33-36), KRONIKOL_BASELINE (diff only).

---------------------------------------------------------------------------------------------------------------

## 6. Public API surface that exposes these

Public, and touched by a move:
- `ReportGenerator.GenerateHtmlReport` (RG 1026-1069): parameters `diagnostics` (1055), `background` (1056),
  `history` (HistoryVerdicts, 1067), `showHistorySection` (1068), `showReportDiagnostics` (1069). No XML doc on the
  method. Callers: the run (through internal GenerateHtmlReportCore RG 1121-1165, which adds componentDiagramDrawn),
  MergeableReportRenderer.Render, and tests directly.
- `ReportGenerator.CreateStandardReportsWithDiagrams` (RG 150-151), `ResolveReportsDirectory` (RG 107-113),
  `GenerateTestRunReportData` (RG 4270-4271, diagnostics parameter), `GetTestRunReportTitle` (RG 913).
- `MergeableReportRenderer.Render(..., history)` (24): param doc 21-23 ("renders the sparklines and the verdict pills,
  and the History section when ShowHistorySection asks for it") and class doc 6-11 would become false.
- `MergedRunOutputs.Write(..., history)` (37-39): doc 36 (digest + pointer) stays true; would gain the page if merge
  writes one.
- ReportConfigurationOptions (src/Kronikol/ReportConfigurationOptions.cs):
  - DiagnosticMode (417-418) -- doc already thin ("diagnostic logging"); says nothing about DiagnosticReport.html.
  - ShowReportDiagnosticsSection (420-429) -- doc false after a move.
  - EmbedHistoryInReport (760-765, default true) -- doc false ("embeds the history it read -- a sparkline and verdict
    beside each scenario, and the History section").
  - ShowHistorySection (767-776, default false) -- doc false, and it names `kronikol merge --history`.
  - HistoryCompareBranch (737-742) -- "on the failures digest, the run-end pointer and the report's History section".
  - HistoryMinRuns (608-613) -- "the report says how many runs are recorded".
  - TestRunReportToggleDefaults (523-530) -- lists "disclosure sections"; fine.
  - GenerateHistoryFragment (744-749), WriteHistoryLedger (751-758) -- unaffected.
  - Analysis options, unaffected by rendering: HistoryFilePath 588, HistoryRunId 598, HistoryWindow 606,
    HistoryDurations 616, HistoryShapes 623, HistoryErrorKeys 630, HistoryFlakyRate 638, HistorySlowerBy 646,
    HistorySlowerMinMs 653, HistoryAlternatingRuns 662, HistoryCountRuns 673, HistoryShapeTemplates 691,
    HistoryDegradedBy 703, HistoryPartialThreshold 711, HistoryPartialRun 717, HistoryReordered 723,
    HistoryBranch 735; KeepRuns 796.
- `ReportToggleDefaults.DiagnosticsOpen` (src/Kronikol/Reports/ReportToggleDefaults.cs:86-89; doc ties it to
  ShowReportDiagnosticsSection; settable on SpecificationsToggleDefaults too, where it is always inert);
  `ResolvedToggleDefaults.DiagnosticsOpen` (src/Kronikol/Reports/ReportToggleDefaultsResolver.cs:37, no doc;
  resolved at 88); `ReportToggleDefaultsResolver.Resolve` (55). No toggle exists for the History section.
- `DiagnosticReportGenerator.Generate` (DiagnosticReportGenerator.cs:13-17, no doc).
- `IngestRequest.HostDiagnostics` (IngestPipeline.cs:247-258; doc names the section) and `IngestResult.TestRunReportHtml` (284).
- History types: HistoryRunContext (class doc 6-10 names "the HTML"), HistorySummary, HistoryVerdicts and the model
  records, HistoryVerdictNames, HistoryFormat.
- Other surfaces with a history parameter: FailuresDigestGenerator.Generate, CtrfReportGenerator.Generate
  (CtrfReportGenerator.cs:164-165), RunSummary / RunSummaryConsoleWriter.Summarise. Also public and relevant:
  RunManifest, Stylesheets.HtmlReportStyleSheet, ComponentDiagramReportGenerator.GenerateComponentDiagramReport /
  ComponentDiagramResult, BackgroundCalls (BackgroundAttribution.cs:23).

Internal: HistoryHtml (everything), GenerateHtmlReportCore, RenderReportDiagnostics (RG 4118), RenderBackgroundCalls
(RG 4152), RunRotation, RunFileCollector, CiArtifactPublisher.ReportFiles/RetainedFiles, QueryScriptGenerator,
DiagnosticReportGenerator.BuildHtml, all CLI command classes. There is no PublicAPI.*.txt or API-compat test in the repo.

Other text that becomes false: README.md:16 and nuget-readme.md:65 ("a sparkline and verdict in the report ...
`$flaky` in the search box"); MergeCommand.cs comment 111-112 and usage 382-385; IngestCommand.cs usage 686-692;
RG comments 1824-1826 and 1833-1838; HistoryHtml.cs class doc 7-20; IngestHostDiagnosticsTests.cs class doc 11;
templates/github-actions/kronikol-history/read/action.yml:3-5 ("the run's own report, Failures.md and job summary
carry the verdicts"); wiki (C:/Code/Kronikol.wiki): Cross-Run-History.md (14 hits), Diagnostics-and-Debugging.md
(13), Report-Configuration.md (7), FAQ.md (3), Generated-Reports.md (2), Ingesting-External-Captures.md (2),
Merging-Parallel-Reports.md, API-Reference.md, Home.md, AI-Integration-Prompt.md, Background-Thread-Correlation.md,
HTTP-Tracking-Setup.md and ~13 integration pages that mention DiagnosticMode. CHANGELOG 3.21.0 (where both sections
went opt-in) is history and stays.

---------------------------------------------------------------------------------------------------------------

## 7. Specifications.html and ComponentDiagram.html

- Specifications.html (RG 481-484): `history`, `showHistorySection`, `diagnostics`, `background` and
  `showReportDiagnostics` are not passed, and includeTestRunData false gates the diagnostics and background blocks
  (RG 1827, 1830); the History section gate (RG 1840) is `history is not null && showHistorySection`, so it is off.
  No history or diagnostics MARKUP. It does carry the shared CSS for them (stylesheets.css 1228-1259 background calls,
  1261-1313 report diagnostics, 1316-1451 and 1995 history) and the client verdict-search code -- dead weight in both
  reports once the markup leaves.
- ComponentDiagram.html: no history or diagnostics markup (ComponentDiagramReportGenerator.cs:183-237).

---------------------------------------------------------------------------------------------------------------

## 8. Header and toolbar

Nothing in the header shows history or a diagnostics badge:
- Logo and title (RG 1455-1457); Features Summary (RG 1472-1539); Test Execution Summary table, with a hidden Kronikol
  Version row (RG 1541-1555); CI metadata block -- provider, build #, branch, commit, pipeline link, repository
  (RG 1557-1576); pie chart (RG 1578-1582).
- Filtering box (RG 1608-1695): search with a help panel (RG 1614-1627) that documents `$failed/$passed/$skipped`
  but not the verdict operators; status, happy-path, duration percentile, dependency and category filters.
- Toolbar row (RG 1699-1732): Expand/Collapse All Features and Scenarios, Scenario Timeline, Component Diagram; under
  BrowserJs the Details radio, Headers/Assertions/Steps/Databases toggles, note format and note width selects.
- No History button or link, no verdict count, no diagnostics count. The only header-side history affordance is that
  the search box accepts `$flaky`, `$broke`, ... (undocumented in its help), which works only because of the
  data-history-verdicts attributes on scenario elements.

---------------------------------------------------------------------------------------------------------------

## 9. Body order around the sections (GenerateHtmlReportCore)

1. Failure Clusters `<details class="failure-clusters">` (RG 1795-1822; FailureClustersOpen) -- stays.
2. Report diagnostics (RG 1824-1828; includeTestRunData && showReportDiagnostics && Count > 0; DiagnosticsOpen) -- moves.
3. Background calls `<details class="background-calls">` (RG 1830-1831; includeTestRunData && Calls > 0;
   RenderBackgroundCalls RG 4146-4201) -- not history or report health but evidence (calls that belong to no
   scenario); stays unless the plan widens its scope. Note the run also records one BackgroundCalls DiagnosticEntry
   per expired group (RG 243-248), so they show in a diagnostics list anyway.
4. History section (RG 1833-1841) -- moves. `historySlots` (RG 1839) is created here and threaded into the scenario
   loop and the group renderer (RG 1999-2000).
5. Scenario timeline panel (RG 1843-1876; hidden unless ScenarioTimelineVisible) -- stays.
6. Embedded component diagram panel (RG 1878-1893) -- stays.
7. `<div id="report-content">` features and scenarios (RG 1895-...) -- stay, minus the per-scenario history: the
   attribute, sparkline and pill on each scenario (RG 2075-2081), the group union attribute and group pill
   (RG 2762-2779, 2860-2861), and the row attributes on flat (RG 2947) and grouped (RG 3054) outline rows.

---------------------------------------------------------------------------------------------------------------

## 10. Bugs and defects found on the way (none fixed: this was read-only)

- B1. Unguarded tail writes lose Run.json. DiagnosticReport.html (RG 620-621, and RG 215-216) and CiSummary.md
  (RG 665-668, then CiSummaryWriter at 671) run outside RunOutputs with no try/catch. An exception there (a directory
  named like the file, a read-only file, ArgumentNullException from a null ReportsFolderPath, see B2) leaves
  CreateStandardReportsWithDiagramsCore: no Run.json (the directory then reads as an unfinished run and the next
  rotation treats it as manifest-less), no artifact publish, no CI debug section, no console pointer. The isolation
  the output list exists for (RG 527-534, RunOutputs doc RG 798-813) stops short of them. A new page must not be
  added to that tail.
- B2. DiagnosticReportGenerator resolves its own directory (DiagnosticReportGenerator.cs:30) as
  Path.Combine(BaseDirectory, ReportsFolderPath), not ResolveReportsDirectory / CurrentReportsDirectory. With a blank
  or whitespace ReportsFolderPath the run writes to <base>/Reports but DiagnosticReport.html lands in <base>: outside
  the manifest (RunFileCollector ignores a ".." path), never published, never rotated. With null, Path.Combine
  throws and B1 applies. Untested (DiagnosticReportGeneratorTests only calls BuildHtml).
- B3. A merged report can never show Report diagnostics: MergeableReportRenderer.Render passes
  `showReportDiagnostics` (95) but never `diagnostics: report.Diagnostics` (MergeableReport.cs:92 has them), so the
  section is inert for every merge and every programmatic Render caller. Background calls are never passed either.
- B4. Vacuous test assertion: tests/Kronikol.Tests/Reports/ReportGeneratorDiagnosticsScopeTests.cs:61 asserts
  "Report diagnostics" is in TestRunReport.html with ShowReportDiagnosticsSection at its default (false); it passes
  only because src/Kronikol/Reports/stylesheets.css:1261 has the comment
  `/* Report diagnostics (capture health, skipped lines, render failures) */`, embedded in every report. Two more
  in tests/Kronikol.Tests/Ingestion/IngestHostDiagnosticsTests.cs:81-82 ("Report diagnostics (" matches the same
  comment; "report-diagnostic-kind-capturedegraded" matches the selector at stylesheets.css:1301), though that test
  also has anchored assertions (80, 83-86). The repo rule is to anchor on emitted attributes.
- B5. Misplaced doc comments: HistoryRunContext.cs:189-199 -- Append's `<summary>` sits above the AppendedHere field
  with that field's own summary, so Append (205) has none and the field has two. IngestCommand.cs:574-578 -- the
  "Whether an argument looks like a capture input" summary is stacked on HoldsSpans instead of LooksLikeInput (601).
- B6. ComponentDiagramOptions documents five options nothing reads (ShowRelationshipFlows 35, RelationshipFlowStyle
  38, ShowSystemFlameChart 41, LowCoverageThreshold 44, MaxFlameChartTests 53); GenerateComponentDiagramReport's
  perBoundarySegments / wholeTestSegments are unused (20-21); ComponentDiagram.html has no DOCTYPE (quirks mode) and
  no <title>, and ignores CustomFaviconBase64 / CustomCss.
- B7. `kronikol ingest`'s pointer (IngestCommand.cs:448-459) names files by existence from a hard-coded list instead
  of what this ingest wrote (the run's rule, RG 623-626), so a failed HTML write leaves the previous ingest's
  TestRunReport.html named under this run; and it carries no history line although the ingest computed verdicts
  (IngestResult exposes none).
- B8. Zero-scenario DiagnosticMode write (RG 215-216) overwrites DiagnosticReport.html in a directory whose Run.json
  still lists the previous run's copy; the next rotation archives the discovery pass's page under the previous run.
  Edge case.
- B9. DiagnosticMode's doc (ReportConfigurationOptions.cs:417) says "enables diagnostic logging" and never names
  DiagnosticReport.html; the page's Assertion Value Resolution section only fills when the separate
  Track.DiagnosticMode (src/Kronikol/Tracking/Track.cs:26, 401) is on, which the option does not set.
- B10. `kronikol ctrf` (TestRunReport.json -> CTRF) can never carry extra.kronikolHistory or the ledger flaky flag
  because the data file has no verdicts, while the run's own ctrf-report.json does. Inconsistent, not wrong.
- Known and already planned: Azure DevOps uploads top-level files of six extensions only, so attachments/ and the
  component .png/.svg are missing there (plans/AZURE_DEVOPS_PARITY_PLAN.md F4, not green-lit).

---------------------------------------------------------------------------------------------------------------

## 11. Checklist: everything that must learn about a new file in the reports directory

Automatic when written through WriteFile from the Add list, at the top level, with a .html name:
- RunFileCollector.Record -> Run.json `files` -> rotation into runs/<run>/ (and RetainedFiles on ADO) -> ADO upload
  via ReportFiles -> RunRotationTests.cs:695-733 holds manifest == disk.

By hand:
1. The output list entry (RG 474-604): label = file name, partOfTheRun true, inside RunOutputs (never the tail, B1).
2. PlannedFiles (RG 736-772) for manifest-less rotation.
3. The pointer's candidates (RG 630-635) if it should be named -- AFTER the report so HtmlFileName
   (RunSummaryConsoleWriter.cs:333-334) still picks the report; never a .json companion (IsDataFile 344-349).
   Same for the CI "Debug this run" text (250-281) if the job summary should mention it.
4. Merge: MergedRunOutputs.Write (and a MergeCommand flag, usage 355-390, and RefuseAnOutputThatIsAnInput 233-251 if
   the name derives from -o); `--history` today maps to ShowHistorySection (MergeCommand.cs:111-115);
   WrittenBesideAReport (353) only for a .json companion.
5. Ingest: IngestResult (a path property beside TestRunReportHtml, IngestPipeline.cs:284), the CLI's hard-coded
   candidates (IngestCommand.cs:451) and usage (686-692; `--diagnostics-section`).
6. Links: Failures.md (deep link FailuresDigestGenerator.cs:281/661-662; the History header 574-579 points at
   `kronikol query history .`); TestRunReport.html linking to the page would be its first cross-document link --
   decide existence from the options, as the digest does (RG 539-543); the page linking back via
   `{report}.html#sid-<id>` (the report already resolves `#sid-` on load).
7. RunRotation: nothing if it is in the manifest; but a top-level .html NOT in the manifest flips attachments from
   move to copy (AnotherReportIsPresent RunRotation.cs:416-418); a name derived from HtmlTestRunReportFileName may
   want IsThisReport (412-414) to know it.
8. Agent text: agent-instructions.md (whether an agent may open the page -- it is small, unlike the report), repo
   CLAUDE.md / AGENTS.md section, templates/agents/CLAUDE.md, SKILL.md x2, commands.md x2, query.py; drift tests
   (SkillDriftTests, AgentInstructionsGeneratorTests, InitAgentsCommandTests).
9. CI templates: templates/github-actions/kronikol-pr-report-link/action.yml:23-26 (`report-file` input names the one
   file a PR comment tells the reader to open); kronikol-history read/action.yml:3-5 description.
10. Docs: README.md:16, nuget-readme.md:65, the wiki pages in section 6, CHANGELOG, and the Kronikol4J divergence
    ledger (C:/Code/Kronikol4J/docs/REMAINING_PARITY.md, section starting ~1877). Java has no cross-run history and
    never rendered the diagnostics block (ledger lines ~30-37 after 1877), so stripping them moves .NET's
    TestRunReport.html toward Java; changing DiagnosticReport.html moves away from Java's golden.
11. Name collisions: a fixed name collides between two reports sharing a folder (as Run.json and Failures.md already
    do); a name derived from HtmlTestRunReportFileName does not.
12. Subfolders are invisible to RunFileCollector (top level and attachments/ only), CiArtifactPublisher.ReportFiles
    (top level only) and the rotation.
13. Styling: Stylesheets.HtmlReportStyleSheet is public and reusable; the history/diagnostics CSS (stylesheets.css
    1261-1313, 1316-1451, 1995) and the client verdict-search code would move with the markup or become dead.

---------------------------------------------------------------------------------------------------------------

## 12. Versioning notes (CLAUDE.md rules applied to what is public)

- Removing or renaming EmbedHistoryInReport, ShowHistorySection, ShowReportDiagnosticsSection,
  ReportToggleDefaults.DiagnosticsOpen, ResolvedToggleDefaults.DiagnosticsOpen, the public GenerateHtmlReport
  parameters (diagnostics, history, showHistorySection, showReportDiagnostics), MergeableReportRenderer.Render's
  `history`, or the CLI flags `merge --history` / `ingest --diagnostics-section` is MAJOR (v5; ask first).
- Keeping them and making them inert or repointing them (EmbedHistoryInReport now meaning "write the history page";
  merge --history rendering the page) compiles, but changes what an untouched configuration produces. The repo rule
  treats an output change as a judgement call recorded in the Java ledger, and a changed default as MAJOR; the
  default of EmbedHistoryInReport (true) currently puts sparklines in every report that found a ledger.
- A new option (a file name for the page, a switch to write it) or a new IngestResult property is MINOR.

---------------------------------------------------------------------------------------------------------------

## 13. Tests that pin today's placement (all would change)

- Unit: tests/Kronikol.Tests/History/HistoryHtmlTests.cs (93-234); HistoryOutputsTests.cs (259 pointer line, 269
  history off, 288-306 EmbedHistoryInReport master switch, 311-331 section opt-in; assertions anchored on emitted
  markup); Ingestion/IngestHostDiagnosticsTests.cs (70-171); Tool/IngestCommandTests.cs (457, 470);
  Reports/Merge/MergeCommandTests.cs (216-241); Reports/ToggleDefaultsMarkupTests.cs (466, DiagnosticsOpen);
  Reports/ReportToggleDefaultsResolverTests.cs (49); Reports/ReportGeneratorDiagnosticsScopeTests.cs (61, vacuous);
  Reports/RunRotationTests.cs (695-733); Reports/StaleOutputTests.cs (90-130); Reports/DiagnosticReportGeneratorTests.cs.
- Playwright: tests/Kronikol.Tests.EndToEnd/HistorySectionTests.cs (22-79), HistorySparklineTests.cs (24-80),
  HistoryExportTests.cs (16: the filtered export keeps sparklines and leaves the section behind), HistoryFilterTests.cs
  (41-62: `$flaky` search), ReportSectionWidthTests.cs:53 (selects `.history-section`).
- JS search engine: tests/Kronikol.Tests.SearchEngine/VerdictSearchTests.cs.
