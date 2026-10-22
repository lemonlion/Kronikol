# Who reads a scenario's name: research for OUTLINE_ROW_NAMES_PLAN (#101)

Worktree `C:/Code/Kronikol-101` at release 5.4.0 (`eabec2bd`), read on 2026-10-22. Method: reading and grep only;
nothing was built or run. Paths are relative to the worktree. **KEY** = the name (or the stable id hashed from it)
is used as an identity, a join, a filter or an ordering. **DISPLAY** = shown only. **INFERRED** = deduced from the
code, not read as a fact or run.

## 0. What the Reqnroll adapter does today

- `src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs:198`: `ScenarioTitle = ScenarioInfo.Title` (raw title).
- `ReqNRollTrackingHooks.cs:211`: `OutlineId = exampleValues is not null ? ScenarioInfo.Title : null`. So for every
  outline row today, DisplayName == OutlineId == the raw title.
- `ScenarioInfoEnumerableExtensions.cs:47`: `DisplayName = x.ScenarioTitle`; `:62` OutlineId copied; `:46`
  `Id = x.ScenarioId`, a fresh Guid per run (`ReqNRollTrackingHooks.cs:51-52`).
- `ReqNRollTrackingHooks.cs:55`: the test identity is `(ScenarioInfo.Title, guid)`, so calls carry the raw title as
  their `TestName`.
- Step texts and step tables are already substituted by Reqnroll (`ReqNRollTrackingHooks.cs:94-100` comment), so
  only the scenario name keeps `<placeholders>`.
- Where a filled name is available:
  - The pickle. `ExamplesBlockResolver.cs:116-135` (`FindPickle`) already finds it (by `PickleId`, else
    `PickleIdIndex`). The pickles come from Reqnroll's embedded Cucumber messages (`:178`), and a pickle's name is
    the one Gherkin interpolated (INFERRED: the Gherkin pickle compiler interpolates outline names; the fixtures in
    `tests/.../ExamplesBlockResolverTests.cs:35-36,64-72` give pickles the raw outline name). Only the Examples-block
    lookup cross-checks the pickle against the argument values (`:95-100`); a name taken from a mis-indexed pickle
    would not be checked.
  - The flat Examples values `flatValues` (`ReqNRollTrackingHooks.cs:175-177`, header to cell).
  - Not `ExampleValues`: `ExampleValueGrouper` re-keys flat columns into grouped objects
    (`ExampleValueGrouper.cs:5-9,23-80`), so the keys can differ from the headers. Not `Scenario.ExampleFlatValues`
    either: it is kept only when the grouping changed the key set (`ReqNRollTrackingHooks.cs:185-186`).
- The Cucumber importer already names rows this way: `CucumberFeatureSynthesizer.cs:489` takes
  `DisplayName = pickle.Name`, and `:743` takes `OutlineId = node.Name` (raw). `ScenarioStableId.cs:27` already
  documents `scenarioDisplayName` as "for an outline row, its expanded name".
- Public surface:
  - `ReqNRollScenarioInfo` is a public record (`ReqNRollScenarioInfo.cs:8-39`), taken by the public
    `ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(IEnumerable<ReqNRollScenarioInfo>, ...)`
    (`ReqNRollReportGenerator.cs:18-22`).
  - `CurrentTestInfo.Fetcher` (`CurrentTestInfo.cs:12-14`) and `ReqNRollTestContext.CurrentTestInfo`
    (`ReqNRollTestContext.cs:11-15`) are public.
  - A new property is new surface. Changing what `ScenarioTitle` or the identity's Name holds is a behaviour change.

## A. Identity and keys

### A1. ScenarioStableId

`src/Kronikol/Reports/ScenarioStableId.cs:33-53`:
`Compute(string? suite, string featureName, string scenarioDisplayName, string? outlineId = null, IReadOnlyDictionary<string,string>? exampleValues = null)`.

- Input string: `[suite::]feature::[outlineId::]displayName[::k=v|k=v ordinal by key]`.
- Hash: SHA-256, the first 16 hex characters, lower case.

The id is computed centrally from the Scenario record. No adapter calls it: every caller in `src/` is in core or in
the Tool. These 17 call sites recompute it:

| # | Caller | What it passes |
|---|---|---|
| 1 | `History/HistoryRunBuilder.cs:58` (ledger roster) | feature.DisplayName, scenario.DisplayName, OutlineId, ExampleValues |
| 2 | `Reports/CtrfReportGenerator.cs:179` | same |
| 3 | `Reports/FailuresDigestGenerator.cs:267` (Failures.md / .jsonl) | same |
| 4 | `Reports/LabsReportGenerator.cs:113` | same |
| 5 | `Reports/Merge/MergeableReportMerger.cs:372` (`WhatItIs`, merge dedupe) | same, `DisplayName ?? ""`, under the shard's suite |
| 6 | `Reports/ReportGenerator.cs:2258` | same (timeline `data-sid`) |
| 7 | `ReportGenerator.cs:2495` | same (scenario `data-stable-id`, history lookup) |
| 8 | `ReportGenerator.cs:2973` | same (Components tab usage) |
| 9 | `ReportGenerator.cs:3493` | same (per-row history in a group) |
| 10 | `ReportGenerator.cs:3669` | same (flat-table row `data-stable-id`) |
| 11 | `ReportGenerator.cs:3776` | same (grouped-table row `data-stable-id`) |
| 12 | `ReportGenerator.cs:5355` | same (data file `stableId`: .json, .krkl, mergeable) |
| 13 | `ReportGenerator.cs:5943` | same (XML `StableId`) |
| 14 | `ReportGenerator.cs:6235` | same (YAML `StableId`) |
| 15 | `Reports/RunSummaryConsoleWriter.cs:148` | same (console summary) |
| 16 | `Kronikol.Tool/HistoryCommand.Maintenance.cs:547` (history import, CTRF) | `(args.Suite, ctrf suite, ctrf name)`, only when the test has no `extra.stableId` (`:545-546`) |
| 17 | `HistoryCommand.Maintenance.cs:613` (history import, Allure) | `(args.Suite, feature label, name)` |

These readers take the id the file carries and do not recompute it:

- query index: `Query/ReportScanner.cs:933`. It does not read `outlineId`; its interned names are at `:324-335`.
- `kronikol ctrf`: `Kronikol.Tool/CtrfCommand.cs:157`.
- history import of a Kronikol report: `HistoryCommand.Maintenance.cs:493-511` calls
  `Query/QueryCommand.History.cs:535-568`, which uses the file's ids.
- the skill's `query.py:273-279` (both copies are identical: `templates/skills/...` and `.claude/skills/...`).
- page scripts (section D).

What changes:

- Today, row id = `Compute(suite, F, T, T, values)`, with T the raw title. After the change it is
  `Compute(suite, F, filled, T, values)`.
- The id is identical when the title has no placeholder.
- The old id can be derived from the new record as `Compute(suite, F, OutlineId, OutlineId, ExampleValues)`. This
  works because DisplayName and OutlineId were both the raw title, and `CapitaliseTitles` capitalises both alike
  (`StepText.cs:167,169`, run at `ReportGenerator.cs:254-255` before any id is computed; see
  `ReportConfigurationOptions.cs:674`).

### A2. History ledger (KEY)

- **Roster.** `HistoryRunBuilder.cs:56-62` builds one entry per scenario in file order: id, name, feature, source.
  The roster hash is computed over the suite and the ids (`HistoryModel.cs:54-99`), so it changes. Positions are
  matched by `(id, slot)` (`HistoryModel.cs:105-111`; `HistoryAnalyzer.cs:89-94,227-260`).
- **First run after the change.** Every re-keyed row reads **New** (`HistoryAnalyzer.cs:368-371`). Its old id reads
  **absent** (`:98-109`), listed under the old raw name: N entries with one name.
  - Flaky, broke, slower and behaviour verdicts restart, because the fingerprints are stored per position
    (`HistoryRunBuilder.cs:88-97`). The fingerprints and the templater (`History/InteractionShape.cs:206-266`,
    `HistoryShapeRules.cs`) carry no scenario name.
- **Partial heuristic.** A run is partial when more than 10% of the previous full roster's distinct ids are missing
  (`HistoryAnalyzer.cs:209-217`; threshold `HistoryPartialThreshold` 0.10 at `ReportConfigurationOptions.cs:829`).
  - In process, a partial verdict is written as `Partial = true` with a HistoryPartialRun diagnostic
    (`HistoryRunContext.cs:146-152`). `kronikol history record` and `import` do the same
    (`HistoryCommand.cs:345-349`; `HistoryCommand.Maintenance.cs:477`).
  - "Previous full" means the last run that is not partial (`HistoryAnalyzer.cs:196-202`;
    `HistoryCommand.cs:490-500`). So the run after a partial one is again compared with the roster from before the
    change, and is again partial.
  - **Would break:** INFERRED, if re-keyed rows are more than 10% of a suite's ids, every run stays partial until
    the last full run before the change leaves the 50-run window (`HistoryVerdicts.cs:105`; `HistoryCommand.cs:23`;
    `ReportConfigurationOptions.cs:721`), or until `HistoryPartialRun = false` or aliases are given. While partial,
    nothing is reported absent.
  - This hazard already exists for any large re-key, such as a SuiteName change. #101 would trigger it.
- **Rename suggestion.** It pairs a gone id and an arrived id only when their (feature, name) match and are unique
  (`HistoryCommand.Maintenance.cs:301-311`, used by `HistoryCommand.cs:310-343`). The name changes here too, and
  the old rows all shared one name, so nothing is suggested, and `--accept-renames` (also passed by
  `templates/github-actions/kronikol-history/scripts/record.sh:136`) writes nothing.
- **Aliases.** Aliases (`History/HistoryQuarantine.cs:107-152`, applied at `HistoryAnalyzer.cs:105,130,233`) are the
  only bridge between the old and the new id. Today that means one `kronikol history rename <old> <new>` per row
  (`HistoryCommand.Maintenance.cs:269-294`).
- **Quarantine.** Entries are keyed by stable id (`HistoryQuarantine.cs:13,42-46`), and the lookup ignores aliases
  (`HistoryAnalyzer.cs:625`, against `:233`). **Would break:** a quarantined placeholder row stops being quarantined,
  even when aliased. Not following aliases may be a pre-existing gap.
- **Gate.** `kronikol history gate` counts a failing row that is New and neither quarantined nor flaky as a new
  failure (`HistoryCommand.Maintenance.cs:107-109`). The default is `new-failures` with `--max-new-failures 0`
  (`:62,147-148`). **Would break once:** a known-flaky or quarantined placeholder row that fails on the first run
  after the upgrade trips the gate.
- **Imports.**
  - A Kronikol report keeps the ids it was written with: old reports keep old ids.
  - CTRF written by Kronikol carries `extra.stableId` (`CtrfReportGenerator.cs:284`), so an import keeps the old ids.
  - Foreign CTRF and Allure are not the Reqnroll lane.
- **kronikol merge.** It rebuilds the history from the merged model (`Kronikol.Tool/MergeCommand.cs:217`), with ids
  recomputed from the shards' names.

### A3. Kept runs, baseline, cross-run pairing (KEY)

- No name keys:
  - `Run.json` holds counts only (`Reports/RunManifest.cs:40-76`).
  - Retained runs are found by run id or folder name (`Query/RetainedRunResolver.cs:67-76`).
- `kronikol query diff`, `--baseline` and `--baseline-run` (`Query/VerbTable.cs:91-92`) match by stableId
  (`QueryCommand.Search.cs:309-319,700-807`). A report from before the change against one after it lists each
  placeholder row as **new** and its old id as **gone**.
- `RefuseUnmatchable` compares names only when no id at all is shared (`QueryCommand.Search.cs:447-481`, names at
  `:470`). So no refusal, unless a report holds nothing but placeholder rows. INFERRED.
- `diff --body` across runs matches the scenario by stableId and refuses when none matches (`QueryCommand.Diff.cs:123-158`).
- `sid:` addresses (`Query/Address.cs:52`; `QueryCommand.History.cs:91,207`) and `#sid-` links
  (`report-url-hash-function.js:17-31,83-84`): a sid saved before the change no longer resolves in a new report.
- `query scenarios` marks `×N` for shared ids (`QueryCommand.Overview.cs:253-268,293`). No change: rows already
  differ by their values.

### A4. Merge, ingest, Cucumber

- **Merge dedupe and collisions.** Merge uses `Identity` = `WhatItIs` (the stable id) + attempt + result + duration
  + message (`MergeableReportMerger.cs:182-275` at `:204,214`; `:296-333` at `:311`; `:371-385`). It recomputes from
  the shard's `name` (`MergeableReportReader.cs:239`), so results are consistent within one version.
  - INFERRED: one row from a shard written before the change and one written after are two scenarios.
- **Merged features.** Merge keys features by feature name only (`MergeableReportMerger.cs:123-143`).
- **Ingest `FeatureSynthesizer`.** Tests are keyed by test id (`Ingestion/FeatureSynthesizer.cs:81`) and features by
  feature name (`:169-185`). The name comes from the tests file, or failing that from the calls' `TestName`
  (`:111,160`). INFERRED: Reqnroll traffic tapped and ingested without a tests file keeps the identity's raw title.
- **Cucumber importer.** It keys by AST ids and test ids (`Ingestion/Cucumber/GherkinIndex.cs:132-136`;
  `CucumberFeatureMerger.cs:58,90-115`) and features by name (`CucumberFeatureMerger.cs:442-445`). Its ids come
  from Compute in the writers. It is not changed by #101.
- **Changed-scenarios code.** It is not in this repo: there is no plan and no code. `--changed` is only a history
  filter (`VerbTable.cs:99`).

### A5. Ordering keys (KEY: order defines sN and roster positions)

- **Adapter order.** `ScenarioInfoEnumerableExtensions.cs:29-37` sorts by IsHappyPath, then ScenarioTitle, then
  ExamplesBlockIndex, then the joined values. The comment at `:31-33` states the invariant "rows must group by
  their Examples: block".
  - This file order defines `sN` (`FailuresDigestGenerator.cs:226-242`; `CtrfReportGenerator.cs:162-165`) and the
    roster order (`HistoryRunBuilder.cs:52-56`).
  - **Would break:** if the filled name goes into `ScenarioTitle`, rows sort by filled name before the block index,
    and blocks interleave in the data file.
  - The existing tests (`tests/Kronikol.Tests/ReqNRoll/ReqNRollScenarioInfoExtensionsTests.cs:604-633`) use the
    title "Movement", which has no placeholder, so they cannot catch this.
- **Page order.**
  - `ReportGenerator.cs:2329` orders by DisplayName. The group is drawn where its first member sorts
    (`:2356-2367`).
  - `ParameterGrouper.cs:90-94` keeps the blocks together but leaves the rows within a block in name order, so the
    row numbers and the first-shown row change.
- **Specifications order.**
  - The YAML, JSON and XML writers order by DisplayName (`ReportGenerator.cs:6560,6608,6642`), and so does the
    Markdown writer (`SpecificationsMarkdownGenerator.cs:103-108`).
  - None of them regroups by block, so rows from different blocks interleave by filled name.

### A6. Test identity scopes (name is a label, not a join)

- **Identity.** It is `(Title, guid)` (`ReqNRollTrackingHooks.cs:55`), handed out by `CurrentTestInfo.Fetcher`
  (`ReqNRollTestTrackingMessageHandlerOptions.cs:15-19`). `Track.TestIdResolver` uses the id only
  (`ReqNRollTestRunHooks.cs:23`).
- **Where the name travels.**
  - The `test-tracking-current-test-name` header: `Tracking/TestTrackingMessageHandler.cs:235`;
    `Kronikol.Extensions.Grpc/GrpcTrackingInterceptor.cs:693`; `Kronikol.Extensions.ProxyTap/ProxyTap.cs:919,944-945`.
  - On the server side the name only has to be present and non-empty (`Tracking/TestTrackingServerBridge.cs:25-35`;
    `TestInfoResolver.cs:171-177`).
- **Joins use TestId:** `DefaultDiagramsFetcher.cs:164`; `PlantUml/PlantUmlCreator.cs:114`;
  `HistoryRunBuilder.cs:75-77,90`; `FailuresDigestGenerator.cs:248-259`.
- **The call's TestName as a label** (DISPLAY):
  - component diagram popups and outliers (`ComponentDiagram/ComponentFlowSegmentBuilder.cs:84-87,332,417`, drawn
    at `InternalFlow/InternalFlowHtmlGenerator.cs:553-559`)
  - the diagnostic report (`Reports/DiagnosticReportGenerator.cs:91,128,143`)
- **OTLP.** The span name is the HTTP method (`Kronikol.Extensions.Otlp/OtlpSpanMapper.cs:250`). The trace id comes
  from the TestId (`:280-291`). The test name goes only into the `kronikol.test.name` attribute, beside
  `kronikol.test.id` (`:219-221`), and `kronikol export` takes the same path (`Kronikol.Tool/ExportCommand.cs:201`).
- **What this means.** If only `Scenario.DisplayName` changes, all of these keep the raw title, which is
  inconsistent but breaks nothing. Changing the identity name too is visible through the public `CurrentTestInfo`.

### A7. Other name-derived keys

- **Anchor slugs** (in-page ids, bookmarks).
  - Every scenario's slug is precomputed from DisplayName with `-2`, `-3` for repeats
    (`ReportGenerator.cs:2133-2153`, slug `:4888-4893`).
  - Rows use it as their `id` and `data-scenario-id` (`:3773,3777`). The group gets
    `UniqueAnchorId(slug(GroupDisplayName))` (`:3432-3440,3577`).
  - Today the rows take `...-case`, `-2`, `-3`, and the group, whose slug is the same, takes `-4`. After the change
    the rows take filled-name slugs and the group takes the plain slug. INFERRED from the code.
  - `#scenario-` links are documented as unstable (`report-url-hash-function.js:1-5`). The comment at
    `ReportGenerator.cs:3774-3775` ("every row of an outline shares one display name") would be stale.
- **Search and filter** (FILTER).
  - The page puts the name into `data-search` and `data-row-search` (`ReportGenerator.cs:2456,3518-3548,3641-3660,3744-3763`).
  - The query filters on it in `scenarios --grep` (`QueryCommand.Overview.cs:327`) and in `search` names
    (`QueryCommand.Search.cs:91-94`).
  - After the change the filled text matches the row. `<case>` still matches the group, through its
    GroupDisplayName.
- **Capitalisation** (`ReportGenerator.cs:253-258`; `StepText.cs:63-90,158-207`).
  - The filled name is capitalised before the ids are computed. A row whose name now starts with a lower-case value
    is capitalised: `<fruit> is weighed` becomes `Apple is weighed`, while `<` was left alone before.
  - With `CapitaliseTitles` off, the lowercase-titles diagnostic can newly count these rows.
- **CTRF `name`** (`CtrfReportGenerator.cs:186`, written at `:267`; suite `:272`).
  - It is DISPLAY for Kronikol: Kronikol's own import uses `extra.stableId`.
  - INFERRED: an external CTRF tool that tracks tests by name sees the rows as new tests, where today it sees N
    tests with one name.

## B. Matching against things outside the report

### B1. Source location (`written at Features/Baskets.feature:3`)

- It is resolved from Reqnroll's embedded Cucumber messages, read by reflection
  (`FeatureInfo.FeatureCucumberMessages`, `ExamplesBlockResolver.cs:38`):
  - The file is `GherkinDocument.Uri` (`:179`).
  - The line is a **title lookup**: `LinesByScenarioName[ScenarioInfo.Title]` (`:74`). The table is built from each
    Gherkin scenario's `Name` to its `Location.Line` (`:181-185`; doc `:159-164`).
- It is called with the live `ScenarioInfo` (`ReqNRollTrackingHooks.cs:193`). It is a **JOIN on the raw title** and
  must keep `ScenarioInfo.Title`; the filled name would give a null line.
- The Examples-block fallback also compares `RowEntry.ScenarioName` (the raw Gherkin name, `:226`) with
  `ScenarioInfo.Title` (`:105-110`). Same rule.
- Readers of the source:
  - Failures.md (`FailuresDigestGenerator.cs:720-721`)
  - the preferred file for ThrownAt (`FailuresDigestGenerator.cs:304`; `QueryCommand.Narrative.cs:117,330`)
  - the CTRF `filePath` and `line` (`CtrfReportGenerator.cs:192-193`)
  - the roster source (`HistoryRunBuilder.cs:155-158`)
  - `query failures` (`QueryCommand.Narrative.cs:113-114`)
- Pre-existing, INFERRED: two scenarios with one title in a file get the last one's line (`:184` overwrites). The
  pickle's AST id would give the exact line.

### B2. Pairing framework results with Reqnroll scenarios

None exists. `Kronikol.ReqNRoll.TUnit`, `.xUnit2` and `.xUnit3` each hold a `[Binding]` subclass
(`BindingHooks.cs:11-26`) and type forwarders, and reference only Core (`Kronikol.ReqNRoll.*.csproj:12`). Core
references Reqnroll 3.3.4 and Kronikol (`Kronikol.ReqNRoll.Core.csproj:12,16`). There is no Reqnroll NUnit or MSTest
package in `src/`. The result comes from the ScenarioContext (`ReqNRollTrackingHooks.cs:205-207`), the id is a new
Guid, and nothing reads the framework's test name or id.

### B3. Re-run and `--filter` lines

- Every one is built from the stack frame, never from the name:
  - the builders: `FailureText.cs:151-190` (`TestFilter`, `RerunCommand`) and `:214-238` (`ThrownAt`, which prefers
    a frame in the scenario's file)
  - the callers: `FailuresDigestGenerator.cs:739-746,976-977`; `QueryCommand.Narrative.cs:117-121,222-223,330-335,358-359`
- No CI-summary caller was found by grep. So a filled name cannot change any printed filter.
- What it prints for a Reqnroll row (INFERRED, unverified; no test pins a Reqnroll re-run line):
  `dotnet test --filter "FullyQualifiedName~<Ns.Class.Method>"` of the chosen frame. That frame is either:
  - a step-definition method, which selects no test, or
  - when the trace holds a frame in the `.feature` file (generated code's `#line`), the generated test method,
    which `~` matches for every row (the tool's own note, `QueryCommand.Narrative.cs:342`).

  Which one depends on what `TestError.StackTrace` holds when AfterScenario reads it (`ReqNRollTrackingHooks.cs:205`).

## C. Outline grouping and per-row naming

### ParameterGrouper (`src/Kronikol/Reports/ParameterGrouper.cs`)

- Rows are always grouped by OutlineId (`:38-56`), even a single row, since rows with values have parameters
  (`:47,113-114`).
- The group header is `FormatScenarioDisplayName(OutlineId)` (`:53`).
- Grouping by name prefix (`ExtractBaseName(DisplayName)`, `:58-79`) applies only to scenarios without OutlineId.
  Reqnroll rows never reach it.
- Display rule:
  - any `ExampleDisplayName` gives Fallback (`:119-121`)
  - ExampleValues keys give ScalarColumns, a flattened object or Fallback (`:123-144`); Fallback comes past
    `MaxParameterColumns` 10 (`ReportConfigurationOptions.cs:654`)
  - names are parsed only when some row lacks ExampleValues (`:146-157`)
- `BuildGroup` stable-sorts by block index (`:90-94`).

### What a row shows

- With ScalarColumns or a flattened object: value cells only (`ReportGenerator.cs:3780-3818`, flat table
  `:3595-3684`).
- With Fallback (R0): a "Test Case" cell = `ExampleDisplayName ?? DisplayName` (`:3707-3711,3819-3824`). Today
  every row of a wide (more than 10 keys) Reqnroll outline shows the same raw title there; after the change it shows
  the filled name.

### Group header (pre-existing defect, INFERRED by reading `ScenarioTitleResolver.cs:70-97,166-175`)

- `Humanize` lower-cases everything after the first letter, and a trailing `(...)` becomes ` [...]`. So
  `A basket is weighed on the UK scale (<case>)` is shown as `A basket is weighed on the uk scale [<case>]`.
- The header is also what the group's Copy scenario name copies (`ReportGenerator.cs:3584`) and what CSV export
  writes (`report-export-function.js:156-159`).
- The tests only pin titles that are already lower case (`tests/Kronikol.Tests/Reports/ParameterGrouperTests.cs:16-58`;
  `CucumberIngestPipelineTests.cs:178`).
- #101 does not change the header, since OutlineId stays raw. Next to filled row names it reads inconsistently.

### Scenario.ExampleDisplayName

- No adapter in `src/` sets it. It round-trips through the data file: written at `ReportGenerator.cs:5378` (schema
  `:7394`) and read back at `MergeableReportReader.cs:267`.
- It is read at `ParameterGrouper.cs:119-121` (it forces Fallback) and `ReportGenerator.cs:3822` (the R0 cell).
  Capitalisation leaves it alone (`StepText.cs:155`).
- Using it for Reqnroll rows would drop every group's value columns (`ParameterGrouper.cs:120`).

### ExampleValueGrouper (`src/Kronikol.ReqNRoll.Core/ExampleValueGrouper.cs:23-80`)

- It builds ExampleValues and ExampleRawValues, which feed the stable id and the columns. They must stay as they
  are.
- The placeholders must be filled from the flat values or from the pickle (section 0).

### Specifications

- The HTML page uses the same group rendering (`ReportGenerator.cs:529-531`).
- `Specifications.yml`, `.json` and `.xml` list every row as its own scenario by DisplayName, with no example values
  (`:6560-6563,6608-6610,6642-6644`). Today that is the raw title repeated once per row; after the change, distinct
  names.
- `Specifications.md` gives each row a heading (`SpecificationsMarkdownGenerator.cs:69`) and an `Example:` line
  (`:137-144`).

## D. Display-only readers (brief)

- **HTML.**
  - timeline label and title (`ReportGenerator.cs:2261`)
  - failure-cluster links (`:2192`; clusters are keyed by message, `FailureClusterer.cs:18-32`)
  - a scenario's summary and Copy scenario name (`:2477,2502`); for an outline group, the copy is the header from
    OutlineId (`:3578,3584`)
  - the History tab (`History/HistoryHtml.cs:136,155,200`, ordered by name `:224`)
  - the labs page (`LabsReportGenerator.cs:109-115`)
- **Data files.** `name` in the JSON (`ReportGenerator.cs:5356`), XML (`:5944`) and YAML (`:6234`).
- **Failures.md.** The heading (`FailuresDigestGenerator.cs:710`), the further-failures table (`:699`) and the
  `Example row:` line (`:726-727`). Ranked by history (`:174`): rows that read New on the upgrade run may move.
- **Failures.jsonl.** `scenario` (`:963`). `testName` and `rerun` come from the frame (`:976-977`).
- **CI summary.** `CiSummaryGenerator.cs:92,158`.
- **Console summary.** `RunSummaryConsoleWriter.cs:147-150,192`.
- **Run.json.** No names.
- **CTRF.** `name` (`CtrfReportGenerator.cs:186`) and `kronikol ctrf` (`CtrfCommand.cs:143`).
- **kronikol query.**
  - `summary` (`QueryCommand.Overview.cs:122,135,154`)
  - `scenarios` (`:276,284`)
  - `failures` (`QueryCommand.Narrative.cs:110,214`)
  - `repro` (`:331`)
  - `history` (`QueryCommand.History.cs:317,338,380,788`)
  - `diff` sections (`QueryCommand.Search.cs:774-807`)
- **query.py.** `:206,328,333,350,383` (display); sid lookup `:273`.
- **Page scripts.**
  - The name is read only by Copy (`report-copy-scenario-name-function.js:4`) and CSV export
    (`report-export-function.js:140-161`).
  - Search reads `data-search` (`report-scenario-feature-map-helper.js:25`) and `data-row-search`
    (`report-search-function.js:234-239,315-320`).
  - Scripts pair elements by stable id, never by name: timeline (`report-timeline-script.js:6,47-58`), components
    (`report-components-script.js:44-52`), next failure (`report-jump-to-failure-function.js:8`), navigation
    (`report-navigation-script.js:95-97`) and anchors (`report-url-hash-function.js:17-31,81-92`).
  - No script sorts or groups scenarios by name. The features sort pairs features "by position, never by name"
    (`report-features-tools-script.js:41-73`). There is no name qualifier in advanced search
    (`advanced-search.js:272-307`). Nothing kept in storage is keyed by scenario (only `kronikol.theme`).

## E. Tests that pin a Reqnroll outline row's name or stable id

- **None of the tests in `tests/` pins the name or stable id of a Reqnroll adapter row with a placeholder title.** No
  test drives `ReqNRollTrackingHooks.AfterScenario`: `ReqNRollBindingDiscoveryTests.cs:11-53` only checks binding
  discovery.
- `ReqNRollScenarioInfoExtensionsTests.cs:307-324`: `ScenarioTitle = "Test with <amount>"`, no OutlineId. It asserts
  only ExampleValues, so it would not move.
- `ReqNRollScenarioInfoExtensionsTests.cs:572-633` (order pins at `:618,632`): title "Movement" without a
  placeholder. It passes either way and cannot catch the A5 sort hazard.
- `ExamplesBlockResolverTests.cs`:
  - outline "Market share movement is reported" without a placeholder (`:17`), and pickles that carry the raw name
    (`:35-36,64-72`)
  - the source lookups by raw title (`:228-245,260-268`)

  They must keep passing, and they pin the raw-title join of B1.
- **E2E.** `FullPipelineParameterizedE2ETests.cs:415-640` and `WikiGifTests.cs:1620-1636` run the example outline
  "Different muffin recipes should produce the expected batch"
  (`examples/.../ReqNRoll.xUnit3/Features/Muffins.feature:15`), which has no placeholder, so they do not move. The
  other example and template features have no outline
  (`templates/kronikol-reqnroll-*/Features/Sample.feature`).
- **Core fixtures.** These are hand-built models, not adapter output, so they move only if core changes:
  - `ParameterizedRenderingIntegrationTests.cs:630-653` already models filled names with a raw OutlineId
    ("Purchase with card type Visa" / `<cardType>`).
  - `StableIdDeepLinkTests.cs` (unit `:61-77`, E2E `:65-80`) models rows that share one name.
  - `TestRunReportSchemaContractTests.cs:201-216` and `TestData/Reports/TestRunReport.pin.json:30-58` hold the
    byte pin: stableId `9d65b55780cab0ec`, name and outlineId `Place an order for <item>`, exampleDisplayName
    `Place an order for Widget`.
  - `CultureInvariantPipelineTests.cs:165-173`.
- **Cucumber.** `CucumberFeatureSynthesizerTests.cs:346-358` and `CucumberIngestPipelineTests.cs:175-178` use "An
  outline over pages", which has no placeholder, so pickle-name filling is untested on that lane too.
- **Outside the repo** (read only):
  - BreakfastProvider has 13 outlines and no placeholder in any scenario title (grep of 67 `.feature` files), so it
    keeps its ids.
  - Kronikol4J has no Reqnroll lane (grep of `*.java` and `*.kt` for "reqnroll" finds nothing).

## Summary table

| Reader | file:line | Identity/key or display | What changes when the row name is filled |
|---|---|---|---|
| ScenarioStableId.Compute (17 callers) | `Reports/ScenarioStableId.cs:33-53` | KEY | New id for each placeholder row; unchanged without a placeholder; old id derivable as `Compute(s,F,OutlineId,OutlineId,values)` |
| History roster and verdicts | `History/HistoryRunBuilder.cs:56-62`; `HistoryAnalyzer.cs:98-109,368-371` | KEY | New roster hash; rows read New, old ids absent; flaky and behaviour history restart |
| Partial heuristic | `HistoryAnalyzer.cs:196-217`; `HistoryRunContext.cs:146-152`; `HistoryCommand.cs:345-349` | KEY | If more than 10% re-key, runs are recorded partial, sticky up to the 50-run window (INFERRED) |
| Rename suggestion | `HistoryCommand.Maintenance.cs:301-311` | KEY (feature+name) | Suggests nothing; `--accept-renames` writes nothing |
| Aliases | `HistoryQuarantine.cs:107-152`; `HistoryAnalyzer.cs:105,130,233` | KEY | The only bridge; manual today |
| Quarantine | `HistoryQuarantine.cs:42-46`; `HistoryAnalyzer.cs:625` | KEY (no aliases) | Entries for placeholder rows stop applying |
| History gate | `HistoryCommand.Maintenance.cs:107-109,147-148` | KEY (via verdicts) | A flaky or quarantined row failing on the upgrade run trips `new-failures` |
| query diff, --baseline, --baseline-run | `QueryCommand.Search.cs:309-319,700-807` | KEY | Rows listed new + gone across the upgrade |
| diff --body cross-run | `QueryCommand.Diff.cs:123-158` | KEY | Refuses for re-keyed rows across the upgrade |
| RefuseUnmatchable names | `QueryCommand.Search.cs:468-478` | KEY (names, fallback) | Nothing unless a report is only placeholder rows |
| sid: addresses, #sid- links | `Query/Address.cs:52`; `QueryCommand.History.cs:91,207`; `report-url-hash-function.js:17-31` | KEY | Saved sids for these rows stop resolving in new reports |
| Merge dedupe | `MergeableReportMerger.cs:204,214,311,371-385` | KEY | Consistent per version; pre- and post-change shards of one row kept twice (INFERRED) |
| History import (CTRF/Allure) | `HistoryCommand.Maintenance.cs:545-548,613` | KEY | Kronikol CTRF uses `extra.stableId`: unaffected |
| Adapter sort | `ScenarioInfoEnumerableExtensions.cs:29-37` | KEY (sN, roster order) | If ScenarioTitle holds the filled name, blocks interleave |
| Page and Specifications order | `ReportGenerator.cs:2329,6560,6608,6642`; `SpecificationsMarkdownGenerator.cs:103-108` | ORDER | Rows reorder by filled name; Specifications interleave blocks; group may move |
| Source line lookup | `ExamplesBlockResolver.cs:74,181-185` | JOIN (raw title) | Must keep `ScenarioInfo.Title`, or the line is lost |
| Examples-block value match | `ExamplesBlockResolver.cs:105-110` | JOIN (raw title) | Must keep `ScenarioInfo.Title` |
| Pickle route | `ExamplesBlockResolver.cs:95-100,116-135` | JOIN (pickle id/index) | Source of the filled name; unaffected |
| Group by OutlineId | `ParameterGrouper.cs:39-56` | KEY | Unchanged (OutlineId raw) |
| Name-prefix grouping | `ParameterGrouper.cs:58-79,113-114,146-157` | KEY (name) | Not reached by Reqnroll rows |
| Anchor slugs | `ReportGenerator.cs:2140-2153,3577,3773,4888-4893` | KEY (in-page ids) | Rows get filled-name slugs; group id loses `-N`; old `#scenario-` bookmarks move |
| Identity name (headers, OTLP attribute, popups, ingest fallback) | `ReqNRollTrackingHooks.cs:55`; `OtlpSpanMapper.cs:219-221`; `ComponentFlowSegmentBuilder.cs:84-87`; `FeatureSynthesizer.cs:160` | LABEL (joins use TestId) | Keeps the raw title unless the identity changes too |
| Test-id joins | `DefaultDiagramsFetcher.cs:164`; `PlantUmlCreator.cs:114`; `HistoryRunBuilder.cs:76` | KEY (id) | Unaffected |
| Search and filters | `ReportGenerator.cs:2456,3518-3548,3641-3660,3744-3763`; `QueryCommand.Overview.cs:327`; `QueryCommand.Search.cs:91-94` | FILTER | Filled text matches the row; `<case>` still matches the group |
| Capitalisation and lowercase-titles diagnostic | `ReportGenerator.cs:253-258`; `StepText.cs:63-90,158-207` | transform before the id | A filled name starting with a value is capitalised; diagnostic may count rows when off |
| CTRF name | `CtrfReportGenerator.cs:186,267` | DISPLAY (external key, INFERRED) | External tools see new test names |
| R0 Test Case cell | `ReportGenerator.cs:3822` | DISPLAY | Raw title becomes filled name (outlines with more than 10 columns) |
| Group header and Copy name | `ParameterGrouper.cs:53`; `ReportGenerator.cs:3578,3584` | DISPLAY | Unchanged (still the lower-cased raw title) |
| Timeline, clusters, History tab, labs | `ReportGenerator.cs:2192,2261`; `HistoryHtml.cs:136,155,200`; `LabsReportGenerator.cs:115` | DISPLAY | Rows distinguishable |
| Data files' name | `ReportGenerator.cs:5356,5944,6234` | DISPLAY | Filled name |
| Specifications YAML, JSON, XML, MD | `ReportGenerator.cs:6563,6610,6644`; `SpecificationsMarkdownGenerator.cs:69` | DISPLAY | Distinct row names |
| Failures.md and .jsonl | `FailuresDigestGenerator.cs:699,710,963` | DISPLAY | Filled name; re-run line unchanged (from the frame) |
| Re-run filter | `FailureText.cs:151-190,214-238` | from the stack frame | Unchanged; may select no test or every row (INFERRED) |
| CI and console summaries | `CiSummaryGenerator.cs:92,158`; `RunSummaryConsoleWriter.cs:147-150,192` | DISPLAY | Filled name and new sid |
| kronikol query verbs, query.py | `QueryCommand.*` (D); `query.py:206,328-386` | DISPLAY | Filled name |
| Page scripts | `report-copy-scenario-name-function.js:4`; `report-export-function.js:156-159` | DISPLAY | None for groups; pairing is by stable id |
