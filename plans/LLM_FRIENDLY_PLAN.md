# LLM_FRIENDLY_PLAN — making Kronikol the best tool an agent can debug with

Status: investigation complete (2026-09-06); re-verified line by line 2026-09-10 against 3.0.83.
**Green-lit and in implementation since 2026-09-12** — the §9 recommendations were adopted wholesale
(the repo's convention for its own plans, cf. `TOGGLE_DEFAULTS_PLAN` §8). **M0 and M1 are implemented
and green in the working tree, releasing as 3.1.0**; M2–M3 are in progress. See §11 for the running log and the
measured channel table that M1.0 produced.

Lineage: successor to `REPORT_QUERY_PLAN.md` ("Making Kronikol reports debuggable by an LLM";
its Parts 2–4 shipped in 3.0.47–3.0.58). That plan's still-open items are absorbed in §2.4 so this
is the single live plan for the agent axis. `PLANS_STATUS.md` (dated 3.0.69) does not list this
file yet — add a row when it is next refreshed.

The frame is unchanged: agents read `TestRunReport.json` through `kronikol query`, not the HTML
(a fixture `TestRunReport.html` is ~114K tokens: 83% script, 15% style, ~2% content).

## 0. What the 2026-09-10 revision changed (so the diff against the 09-06 text is legible)

- **Corrected citations.** The isolated `RunOutputs` list is `ReportGenerator.cs:257-307` and its
  runner `:356-370` (not `:257-343`); **`CiSummary.md` is NOT one of its entries** — it is written
  sequentially after the list (`:318-330`) with no failure isolation, so "CiSummary.md precedent"
  meant the *markdown generator*, not the hook. `LogParameters` is `Sql/SqlTrackingOptionsBase.cs:23`;
  `ResultWhenUnknown` is `Ingestion/IngestPipeline.cs:63`.
- **Removed a wrong claim.** CTRF is not "cheap via the existing Export command": `kronikol export`
  is OTLP-only over NDJSON *interaction* captures (`ExportCommand.cs:223`), which do not carry the
  run model CTRF needs. It is a new writer (§4 M3.2).
- **Removed a wrong claim.** "E2E fixture generators already produce JSON alongside" — only
  `WikiGifTests.cs:1175` and the merge helper write a JSON file. A dogfood fixture has to be added.
- **AGENTS.md alone does not reach Claude Code.** It reads `CLAUDE.md` only; nested `CLAUDE.md`
  files load *lazily when a file in that directory is read*, gitignored and `bin/` directories
  included (code.claude.com/docs/en/memory). M1 therefore emits **both** files (§3.2).
- **Added:** ride-along bugs found while re-verifying (§2.3, own first slice M0); twelve new gaps
  (§2.2); the design constraints every milestone has to respect (§3); per-milestone red tests; a
  verification protocol (§6); documentation, Kronikol4J and release sections (§7–§8); open
  questions with recommendations (§9).

## 1. What already exists (verified 2026-09-10 — do not rebuild)

### 1.1 Data, CLI, skill
- **`TestRunReport.json` + `TestRunReport.schema.json` ship BY DEFAULT** next to the HTML
  (`ReportConfigurationOptions.cs:182` / `:185`; full step detail `:204`). The JSON is built inline
  in `GenerateTestRunReportJson` (`ReportGenerator.cs:3579-3591`): top level is exactly
  `kronikolVersion` (`:3584`, from the assembly informational version `:20-23`), `startTime`,
  `endTime`, `features`, `diagnostics`. Per scenario: `stableId` (`:3612`), result, `errorMessage` /
  `errorStackTrace` (`:3618-3619`), example fields (`:3626-3631`), attachments with report-relative
  paths (`:3632`, `FileAttachment.cs:21`), steps with `failureMessage` / `sourceFile` / `sourceLine`
  (`:3838-3839`), raw PlantUML per scenario, `httpInteractions` with positional `stepPath`
  attribution verified against marker text (`AttributeInteractionsToSteps`, `:3435-3526`),
  `annotations` (`:3640-3648`). Schema is hand-built (`:4624-4841`) with real enum reflection.
- **`stableId`** = first 16 hex of SHA-256 over `feature::[outlineId::]scenario[::k=v|…]`
  (`ScenarioStableId.cs:18-35`). Renaming a feature or scenario changes it; example rows are
  distinct; source file, class, tags and `ExamplesBlockIndex` are NOT in the hash (two identical
  rows in two `Examples:` blocks collide — see §2.3 bug 1).
- **`kronikol query` (Kronikol.Tool, `PackageId` Kronikol.Tool, `ToolCommandName` kronikol,
  net10.0 + `RollForward Major`, `Kronikol.Tool.csproj:9-20`)** — 18 verbs exactly as listed
  (`QueryCommand.cs:70-91`): summary/scenarios/services · failures/steps/assertions/flow/annotations
  · values/interactions · http/body/note/diagram · grep/trace/compare/diff. Address grammar
  `sN`, `sN/iN`, `sN/<stepPath>`, `sN/dN[/nN]`, `b:<hash>` (`Query/Address.cs:3-14`). Default
  6000-byte budget latched in `QueryWriter` (`:23-37`) with the footer
  `… output truncated at N bytes · raise with --max-bytes, or filter harder` (`:45-54`) and a
  separate paging footer `next: <flags> --offset N` (`:70-72`). `<report>` may be a directory; several
  candidates are listed, never guessed (`QueryCommand.cs:128-161`). Exit codes 0 / 1 (unreadable,
  invalid JSON, unknown `mergeableFormatVersion` `:61-65`) / 2 (usage). `kronikolVersion` is read
  (`ReportScanner.cs:377`) and printed by `summary` but never validated; the only enrichment signal
  is the heuristic `Enriched` flag → `! report predates step attribution …` banner
  (`QueryCommand.cs:111-121`). 112 facts/theories in `tests/Kronikol.Tests/Tool/QueryCommandTests.cs`.
- **`compare` ≠ `diff`:** `compare s3 s7` = two scenarios in one run (steps/calls positional diff,
  never opens a payload, `QueryCommand.Search.cs:133-200`); `diff` is overloaded three ways
  (`:224-248`) — body diff inside one report, cross-run report diff matched on `stableId`
  (`:256-296`), cross-run single body via `--body`.
- **The Claude Code skill** `templates/skills/kronikol-test-debugging/` — `SKILL.md` (8.4 KB, the
  ladder + recipes + traps), `references/commands.md` (15.1 KB flag reference), `scripts/query.py`
  (14.4 KB Python fallback: summary/failures/steps/services/grep/http only). **None of the three has
  a test** — see §2.2 (i). Pointed to from `README.md:191`, `Querying-Reports.md:491-512`,
  `AI-Integration-Prompt.md:2,4,9`, `Home.md:54`. Not packaged in any NuGet or template.
- **Ingest / merge / NDJSON.** `kronikol ingest` writes the full output set including the JSON
  (`IngestPipeline.cs:385` → the standard generator) and already prints
  `Wrote reports to <dir>` + the HTML path (`IngestCommand.cs:314-317`) — the one existing pointer
  precedent. `kronikol merge` writes **HTML only** (`MergeCommand.cs:64` →
  `MergeableReportRenderer.MergeFilesToHtml`) — see §2.2 (a).
- **`RunOutputs`** — `Add(name, action)` list `ReportGenerator.cs:257-307`, run under
  `Parallel.Invoke` with per-output try/catch (`:356-370`: `DiagnosticKind.OutputFailure` + a
  console warning, every other output still writes). Six entries today: Specifications.html,
  TestRunReport.html, Specifications data, TestRunReport data (mergeable variant when
  `GenerateMergeableData`, `:281-286`), schema, ComponentDiagram.html. **This is the hook for every
  new writer below.**
- `ErrorDiffParser` (public; xUnit / NUnit / FluentAssertions / Shouldly shapes) and
  `FailureClusterer` (same normalised first line, ≥2) are used **only** by the HTML
  (`ReportGenerator.cs:1380-1382`, `:2396-2398`, `:1131`); `query failures`
  (`QueryCommand.Narrative.cs:12-69`) prints raw messages.
- Markdown helpers reusable by a digest writer: `CiSummaryGenerator.EscapeMarkdown` (`:269`),
  `EscapeHtml` (`:271`), `FormatDuration` (`:276`), `AppendFailedScenarios` (`:58`, the structural
  precedent), `FormatDurationBadge` (`ReportGenerator.cs:3342-3350`). `AppendDiagramImages`
  (`:159-181`) is the part a digest must NOT copy.

### 1.2 Documentation already in place (touch, don't duplicate)
- `Querying-Reports.md` — H2s: The idea, Addressing, Budget and truncation, Commands, Older reports
  (:461), Exit codes (:476), **Using it from an AI agent (:486)** (skill install + file tree,
  `query.py` limits, ladder, recipe table incl. `diff old.json new.json`).
- `AI-Integration-Prompt.md` — an *integration* prompt (get an agent to wire Kronikol into a test
  project) with a debugging redirect to the skill at the top. The second agent-facing asset.
- `Diagnostics-and-Debugging.md:9-13` already routes "why did this test do what it did" → `kronikol
  query` and "why is the report wrong" → `DiagnosticMode`.
- `Capture-Time-Redaction.md:7-10` states the boundary: render-time filters touch only the diagram;
  capture-time `RequestResponseLogger.Redaction` keeps a value out of "any file derived from" the
  store. `CiSummary.md` is never named there; neither will `Failures.md` be unless §7 adds it.
- `CI-Summary-Integration.md` — `WriteCiSummary` (default **false**, `ReportConfigurationOptions.cs:207`)
  appends to `$GITHUB_STEP_SUMMARY` or emits `##vso[task.uploadsummary]` (`CiSummaryWriter.cs:9-38`).
- `Generated-Reports.md:614-651` — a "Gold Standard" workflow that copies
  `TestRunReport.baseline.json` and compares with hand-rolled PowerShell, not `kronikol query diff`.
- Wiki-wide zero hits for: `AGENTS.md`, `CLAUDE.md`, `llms.txt`, MCP, CTRF, `Specifications.md`,
  `sid-`. No "AI / Agents" sidebar grouping (`_Sidebar.md:12` vs `:129-131`).

### 1.3 The run-end channel as it is today
- Report generation is **not silent**: `ReportGenerator.cs:309-313` prints every
  `ReportDiagnostics.Analyse` line to stdout on every run; `:102` and `:367` print warnings;
  `DefaultDiagramsFetcher.cs:61/96/162` print render warnings. Whether those lines are *visible*
  under each runner is the empirical question M1.0 answers — they are the probe.
- Who calls the generator: xUnit v2 is the only automatic hook (custom `ITestFramework`,
  `Kronikol.xUnit2/ReportingTestFramework.cs:35-62`; its doc comment `:13-17` records that
  `ProcessExit` is unusable). Every other adapter relies on user code in the framework's own
  run-end hook: xUnit v3 collection fixture `Dispose` (`templates/kronikol-xunit3/Infrastructure/
  TestRun.cs:14-27`), NUnit `[OneTimeTearDown]`, MSTest `[AssemblyCleanup]`, TUnit
  `[After(Assembly)]`, ReqNRoll user `[AfterTestRun]`, BDDfy assembly fixture, LightBDD
  `IReportFormatter` (`StandardPipelineFormatter.cs:35`). No `ProcessExit`, no `ILogger`, no
  `Console` use in any adapter project.
- The structured channel `ReportDiagnosticsScope` / `DiagnosticEntry` (`Reports/DiagnosticEntry.cs`)
  is opened in **exactly one place** — `IngestPipeline.cs:383`. See §2.3 bug 3.

## 2. Verified gaps

### 2.1 The six from 09-06 (still open, citations corrected)
1. **Nothing surfaces the loop entry point.** No adapter writes back into framework output; the
   reports directory is never printed by the library path; an agent watching a failing run has no
   signal that `kronikol query failures <dir>` exists. (Only `kronikol ingest` prints a path.)
2. **SQL parameters default off** (`SqlTrackingOptionsBase.cs:23`, same in Dapper/Spanner): a
   failing INSERT shows text, not the values. Do not flip — see §3.3.
3. **HTML anchors are display-name slugs** (`GenerateScenarioAnchorId`, `ReportGenerator.cs:3352-3357`),
   disambiguated `-2`, `-3` in enumeration order (`:1099-1115`) — collisions can *swap* between runs.
   `stableId` appears nowhere in the DOM. Worse: `update_url_hash()` rewrites the whole fragment to
   filter state (`report-url-hash-function.js:1-25`), so touching any filter destroys a scenario
   deep link.
4. **OTLP spans carry no test outcome** (documented attribute set `Exporting-to-OpenTelemetry.md:19-35`
   has `kronikol.test.id` / `.name`, nothing for result); `IncludeBodies` default `false`
   (`OtlpExportOptions.cs:52-54`) is never stated in words on the wiki.
5. **No prior-run convention** for `diff old.json new.json`. And the built-in artifact upload is
   `CiArtifactRetentionDays = 1` (`ReportConfigurationOptions.cs:219`), GitHub-only, opt-in — fine
   for same-run shards, dead for a last-green baseline.
6. **`ResultWhenUnknown = Passed`** (`IngestPipeline.cs:63`): a crashed worker's scenarios render
   as PASSED on the ingest path. Nothing in the JSON records that a default was applied
   (no options snapshot), so an agent cannot detect it. Documented only at
   `Ingesting-External-Captures.md:151,161`.

### 2.2 New — found during re-verification
- (a) **`kronikol merge` emits no JSON.** The canonical CI shard fan-in produces a run that
  **cannot be queried** and cannot be a baseline. `query` *does* read the per-runner mergeable
  superset (`mergeableFormatVersion: 1`, `ReportGenerator.cs:3742`; banner `QueryCommand.cs:119-120`,
  whose wording "a merge of several runs" is wrong — the flag means "superset format",
  `ReportIndex.cs:36`).
- (b) **The standard JSON carries no run identity.** `CiMetadataDetector.Detect()` runs
  (`ReportGenerator.cs:221`) but commit SHA / branch / run URL reach only the HTML (`:894-909`) and
  the mergeable JSON (`:3765-3774`); the plain writer never receives them. No OS/runtime/framework
  either. `diff`-vs-baseline has nothing to key on but file names.
- (c) **JSON `diagnostics` is always `[]` for adapter-driven runs** — §2.3 bug 3.
- (d) **Schema ≠ writer.** `exampleFlatValues` / `exampleDisplayName` are emitted (`:3630-3631`) but
  undeclared (`:4687` list stops at `exampleValues`). Only 39 of 83 properties carry a
  `description` (47%); the undescribed include `result`, `errorMessage`, all core step fields and
  most of `httpInteraction`. The wiki calls the schema "the field-level contract" — it isn't yet.
- (e) **No source location on features or scenarios** (`Feature.cs:6-12`, `Scenario.cs:7-42`).
  Gherkin `uri` + `line` are parsed (`CucumberMessages.cs:56-70`) and dropped by
  `CucumberFeatureSynthesizer`. Step `sourceFile` is deliberately the bare file name
  (`Track.cs:456-469`) — an agent gets `CakeTests.cs:42` and must glob for it.
- (f) **Retries and duplicate stableIds are unmodelled.** Cucumber ingest keeps the last attempt
  and adds a `retry N` label (`CucumberFeatureSynthesizer.cs:472-474`); merge dedupes by runtime
  `Scenario.Id`, not stableId (`MergeableReportMerger.cs:44-53`); nothing else notices — and
  `diff` crashes on it (§2.3 bug 1).
- (g) **No `--json` on `query`** (REPORT_QUERY_PLAN §3.1 principle 5, never built; `QueryOptions.Parse`
  `:55-178` has no output-mode flag). Text is right for an LLM reading a terminal; scripts, CI gates
  and the MCP wrapper (M3) want a structured envelope. `--raw` is parsed (`:48/:156`) and dead.
- (h) **Specifications data goes blank on failure by design:** `GenerateSpecificationsData` passes
  `generateBlankOnFailedTests: true` (`ReportGenerator.cs:276`, `:4221-4222`) — a 0-byte
  `Specifications.yml` whenever anything failed, and its model is spec-only (no `stableId`, result,
  interactions). Any "read the spec as living docs" export for agents has to decide this (§9 Q9).
- (i) **Skill assets can drift silently.** `commands.md` and `query.py` are hand-maintained against
  an 18-verb CLI with ~40 flags and have zero tests; the only consumer test corpus is the .NET
  tool's own.
- (j) **No write-time size signal.** The JSON writer has no budget, no warning, no post-write size
  check; the only truncation knob is capture-time `MaxContentLength` (default null,
  `RequestResponseLogger.cs:20,40-41`) which shrinks the *diagram* too. The 10 MB / 2.7M-token
  trap is documented only in the consumer.
- (k) **Wiki says the artifact upload covers `.html/.yml/.md`** (`CI-Artifact-Upload.md:63-72`);
  code uploads `.json` and `.xml` too (`ReportGenerator.cs:338-339`). Matters: the JSON *is*
  fetchable from the artifact today.
- (l) **Skill not dogfooded here** — this repo has no `.claude/skills/`, and `CLAUDE.md` never
  mentions `kronikol query`.

### 2.3 Ride-along bugs (fix first, own release — TDD, red test named)
1. **`kronikol query diff <old> <new>` throws** `ArgumentException` on any duplicate `stableId`:
   `left.Scenarios.ToDictionary(s => s.StableId …)` (`QueryCommand.Search.cs:256-257`). Reachable
   by identical example rows across two `Examples:` blocks, a `[Theory]` with repeated data, a
   retried scenario in a mergeable file, or an ingested repeat. `CrossRunBodyDiff` uses
   `FirstOrDefault` (`Diff.cs:97`) and silently picks the first. Fix: group by `stableId`, match
   positionally inside a group, print `! N scenarios share a stableId (repeated rows or retries) —
   matched in order`. Red: `Diff_across_runs_survives_duplicate_stableIds` in `QueryCommandTests`.
2. **Schema drift** (§2.2 d): declare `exampleFlatValues` / `exampleDisplayName` with descriptions.
   Red: `Every_key_the_json_writer_emits_is_declared_in_the_schema` — generate a rich fixture, walk
   every object key, assert ⊆ schema `properties` per `$defs`. This test is the permanent guard for
   every field M2 adds.
3. **Adapter-driven runs never open a diagnostics scope**, so `reportDiagnostics` at
   `ReportGenerator.cs:255` is `[]` and every `Record` (OutputFailure `:366`,
   StepAttributionMismatch `:3477-3480`, render/attachment failures) is a no-op except for the
   console line. Fix: `CreateStandardReportsWithDiagramsCore` opens a scope when
   `ReportDiagnosticsScope.Current` is null (AsyncLocal keeps concurrent generations apart, per the
   type's own remarks `DiagnosticEntry.cs:120-128`). Red: a normal-run generation with a forced
   render failure yields a non-empty `diagnostics` array in the JSON.
4. **`--raw`** — delete (PLANS_STATUS item 6, decision: delete). Red: `Parse` rejects it with the
   usual usage error.
5. **Provenance banner wording** (§2.2 a) → `! mergeable-format report`.
6. **Wiki `CI-Artifact-Upload.md:63`** allowlist → add `.json`, `.xml` (docs-only, same release).

### 2.4 Absorbed from REPORT_QUERY_PLAN (its open ledger, so PLANS_STATUS can retire the row)
`--json` (→ M2.9) · dead `--raw` (→ M0) · note-divergence detection §3.4 (still a blanket footer;
**stays deferred**, no agent-reported need) · the >100 MB streaming-path *test* §3.6 (→ M2.9's
test list; `tools/query-bench` generates the corpus) · golden snapshot tests (stay assertion-based,
decision unchanged) · §2.6 run-level extras (`ciMetadata` unconditionally → M2.3; internal-flow
summary stays deferred) · §6.2 Q7 directory discovery (**done**, `QueryCommand.cs:128-161`).

## 3. Design constraints (every milestone respects these)

### 3.1 The console pointer's delivery channel is unproven — measure before promising
"Reaches CI logs with zero framework surgery" is a hypothesis. The library prints via
`Console.WriteLine` from inside the runner's teardown hook; VSTest-hosted runners (xUnit v2,
NUnit, MSTest under `dotnet test` with VSTest) capture testhost stdout and may drop output written
outside a test, while MTP-hosted runners (xUnit v3, TUnit, MSTest on MTP) are plain console apps.
**M1.0 is a spike:** run every template with `dotnet test` at default verbosity and record whether
the *existing* `ReportDiagnostics` lines (`ReportGenerator.cs:313`) appear; that table is the truth
for the pointer. Belt and braces regardless of the outcome: the same text also goes (1) into
`CiSummary.md` as a "Debug this run" section (reaches `$GITHUB_STEP_SUMMARY` / ADO summary when
`WriteCiSummary`), (2) as a GitHub `::notice title=Kronikol::…` workflow command when
`CiEnvironmentDetector` says GitHub Actions, and (3) into `Reports/CLAUDE.md` + `AGENTS.md`, which
catch every agent that so much as lists the directory. Never depend on one channel.

### 3.2 Instruction files: emit CLAUDE.md AND AGENTS.md, identical, static
Claude Code loads `CLAUDE.md` only; Codex / Cursor / Copilot / Jules / Amp read `AGENTS.md`. A
nested `CLAUDE.md` loads the moment the agent reads *any* file in that directory — `Failures.md` is
the bait that triggers it. Content is a **fixed template with no run-derived text** (see §3.3):
what each file is, the install line (`dotnet tool install -g Kronikol.Tool`, needs the .NET 10
runtime — `Kronikol.Tool.csproj:5`), the cookbook, the traps, `kronikol query --help`. `llms.txt`:
no confirmed consumer, Claude Code has no support — emit nothing.

### 3.3 Security: the digest is data, the pointer carries no content
- Capture-time redaction is **opt-in** (`RequestResponseLogger.Redaction` default null, `:28`;
  ProxyTap and `kronikol export` are the exceptions with defaults on). So `TestRunReport.json` is not
  guaranteed redacted; `Failures.md` derived from it is the *same exposure class* — no new class —
  provided it reuses the stored (post-redaction) values and never re-reads live objects.
- The console pointer and the `::notice` line print **paths, counts, stableIds and scenario names
  only** — CI logs are far more widely readable than artifacts. No SQL, no URIs, no messages.
- `CLAUDE.md` / `AGENTS.md` contain **no run data at all** (scenario names, bodies and error text are
  attacker-influenceable — a third-party response body saying "ignore previous instructions" must
  land in a fenced data file, never in an instruction file). `Failures.md` fences every quoted
  value and says at the top that everything below is captured data.
- The `LogParameters` default stays; the digest *names the knob* when it detects parameter
  placeholders without captured values (M2.2). The `Redaction` default stays too.

### 3.4 Schema surface cost and versioning
Every JSON field lands in **eight places**: JSON + XML + YAML writers + schema, in .NET and again
in Kronikol4J (`kronikol4j-report/.../data/ReportData.java`, `ReportDataFormat.java`,
`ReportDataSchema.java`, byte parity; REPORT_QUERY_PLAN §2.7). **Batch M2's additions into one
release** (M2.3 + M2.6 + M2.7 fields together). `Failures.jsonl` gets its own `formatVersion: 1`
first field, mirroring `mergeableFormatVersion`, and `query` refuses unknown versions loudly.

### 3.5 Byte-identity pins that move
Adding `data-stable-id` to scenario markup changes every `TestRunReport.html`: the substring facts
in `tests/Kronikol.Tests/Reports/*` that anchor on scenario markup need re-pinning
(anchor on emitted attributes, per the 3.0.82 lesson). `ToggleDefaultsBaselineTests` compares two
paths of the *same* build and is unaffected. Kronikol4J's golden HTML is pinned to 3.0.43 assets —
ledger entry only. Runs with no new options set must be byte-identical for every output that this
plan does not deliberately change (a pre/post diff on a process-deterministic fixture per slice).

## 4. Milestones (each strictly TDD: red → green → refactor; each slice its own release)

### M0 — ride-along fixes (§2.3 items 1–6) — no new surface, ships first

### M1 — close the discovery loop (highest ROI)
- **M1.0 Channel spike** (§3.1) — **DONE 2026-09-12, and it changed the design.** Measured on .NET 10
  SDK 10.0.300 / Windows, running the real `Example.Api` component-test projects and grepping the
  captured output for the `ReportDiagnostics.Analyse` lines the library already prints at run end:

  | Runner | Command | Library console output visible? |
  |---|---|---|
  | xUnit v2 (VSTest) | `dotnet test` | **no** |
  | xUnit v3 | `dotnet test` | **no** |
  | NUnit 4 | `dotnet test` | **no** |
  | MSTest | `dotnet test` | **no** |
  | xUnit v3 | `dotnet test -v normal` (what CI runs) | **no** |
  | NUnit 4 | `dotnet test -v normal` | **no** |
  | MSTest | `dotnet test -v normal` | **no** |
  | xUnit v2 | `dotnet test -v detailed` | **yes** |
  | xUnit v3 | `dotnet run --project` (MTP host) | **yes** |
  | TUnit | `dotnet run --project` (MTP host) | **no** — its own runner suppresses it |

  So the console is **not** a channel you can rely on: VSTest forwards testhost stdout only at
  `detailed`, and CI runs `normal`. The consequence for the rest of M1 is that the *files* are the
  primary mechanism and the console is the bonus — the opposite of the 09-06 framing — and that the
  GitHub `::notice` is best-effort for the same reason (it is a stdout workflow command). The CI job
  summary is unaffected: it is written to a file descriptor (`$GITHUB_STEP_SUMMARY`), not to stdout,
  which is why M1.1 also puts a "Debug this run" section there.
- **M1.1 Run-end pointer** — printed where the `ReportDiagnostics` lines already print
  (`ReportGenerator.cs:309-313`), after `RunOutputs`, so it is the *last* thing the run says:
  ```
  Kronikol: reports written to C:\proj\Reports  (TestRunReport.html · TestRunReport.json 48.2 MB · Failures.md)
    3 failed — kronikol query failures C:\proj\Reports
      a1b2c3d4e5f60718  Checkout › Pay with an expired card
      …
    agents: read Reports\CLAUDE.md first; never open TestRunReport.json
  ```
  Caps: 20 failure lines then `… and N more (see Failures.md)`; the JSON size line is the write-time
  size signal (§2.2 j). Zero failures prints the first line only. Also: `kronikol ingest` gets the
  same `kronikol query failures` line after `IngestCommand.cs:316`; a `::notice` on GitHub Actions;
  a "Debug this run" section in `CiSummaryGenerator`. Option: one bool (name §9 Q1), default on.
  Tests: `RunEndPointerTests` (format, caps, zero-failure shape, no content ever — a fixture with a
  secret-bearing URI asserts it is absent), CI-summary section fact, ingest-command fact.
- **M1.2 `Failures.md` + `Failures.jsonl`** — ONE `Add("Failures", …)` action inside the isolated
  list (before `:307`), so a digest failure can never take the HTML down. Per failure ~2–4 KB:
  header (feature › scenario, example values, `errorMessage` first lines), parsed Expected/Actual
  via `ErrorDiffParser`, the last 3 steps before the failing one with durations, the failing step's
  attributed calls with ONE-LINE content (SQL first line ≤120 chars, `METHOD uri → status`; never a
  body), attachment paths, `stableId`, the `query` addresses (`s3`, `s3/i47`) and the deep link
  (`TestRunReport.html#sid-<stableId>` once M2.1 lands; `#scenario-…` until then). Cluster with
  `FailureClusterer` first — one exemplar per cluster, then member `stableId`s in a table. Hard
  budget: 25 exemplars, ≤ ~20K tokens for 20 failures, asserted on a fixture. `Failures.jsonl` = one
  object per failure with the same fields + `formatVersion`, for scripts and the MCP layer. NEVER
  inline SVG or PlantUML (one measured diagram = 663 KB ≈ 166K tokens; `query flow` tells it in
  1–2 KB). Written even when nothing failed (`# No failures` + the pointer to `query summary`), so
  its presence is stable. Kronikol4J: ledger entry (§8).
  Tests: `FailuresDigestGeneratorTests` (clustering, budget, one-line content, redaction pass-through,
  jsonl shape + `formatVersion`), `ReportGeneratorFailuresDigestTests` (registered in the isolated
  list; a throwing digest leaves every other output intact), E2E fact that the deep link in the
  digest opens the scenario.
- **M1.3 `CLAUDE.md` + `AGENTS.md`** (§3.2) — same action, static template resource, byte-identical
  pair, option default on. Test: template contains no run-derived substring (fixture scenario names
  and bodies asserted absent), both files identical, install line present.
- **M1.4 Surface the `ResultWhenUnknown` default** — ingest already has a scope open: record a new
  `DiagnosticKind.ResultDefaulted` entry (count + the configured value) whenever a scenario's result
  was defaulted; `query summary` / `failures` and `Failures.md` print
  `! N scenario(s) had no recorded result and were reported as <Passed>` at the top. Closes gap 6
  by detection instead of by warning agents in prose. Tests: `IngestPipelineTests` red for the entry,
  `QueryCommandTests` banner fact.

### M2 — agent ergonomics on verified gaps (batch the JSON fields — §3.4)
- **M2.1 stableId deep links** — `data-stable-id="<id>"` on the existing anchored elements
  (`<details id=…>` `:1326`, parameterised `<tr>` `:2301-2302`; one `id` per element rules out a
  second `id`); `parse_url_hash` learns `#sid-<id>` → `[data-stable-id]`; **`update_url_hash`
  preserves a `sid-`/`scenario-` fragment when it rewrites filter state** (today it destroys it).
  `query failures` prints the `#sid-` link. TestRunReport.html and the merged HTML (same renderer);
  Specifications.html has no `stableId` in its model — out. E2E (Playwright, per CLAUDE.md):
  navigate to `#sid-…` → scenario opens and scrolls; change a filter → fragment still holds the
  `sid-`; two same-named scenarios resolve to the right one.
- **M2.2 Parameter-capture hint** — heuristic on stored content (placeholders `@p`, `$1`, `:name`,
  `?` with no parameter block) → the digest and `query failures` add one line naming
  `LogParameters` on the tracker. Never flips the default.
- **M2.3 Run identity in the standard JSON** — emit `ciMetadata` unconditionally (null fields when
  not on CI; the detector already runs `:221`) plus `environment { os, runtime }` (no machine name —
  PII-adjacent). Schema descriptions + the drift test cover it. This is what a baseline index keys on.
- **M2.4 Baseline convention + merge emits JSON** — (a) `kronikol merge` writes the merged
  `TestRunReport.json` (mergeable superset — it is what merge consumes and it carries `ciMetadata`)
  next to the HTML, `--no-json` to opt out, so a sharded run is queryable and can *be* the baseline.
  (b) `kronikol query diff --baseline` resolves, in order, `<reports>/baseline/TestRunReport.json`
  → `$KRONIKOL_BASELINE` → an error naming both. (c) Documented recipes: GitHub (download the last
  successful `main` artifact by `run-id` from `gh run list`, longer retention on the copy —
  `CiArtifactRetentionDays` stays 1 for shards), Azure DevOps (`DownloadPipelineArtifact`,
  `runVersion: latestFromBranch`). (d) Rewrite the wiki "Gold Standard" PowerShell onto
  `kronikol query diff`. The remote MCP server (M3.1) is the durable owner of baselines; this is
  the CLI-only floor. Tests: `MergeCommandTests` JSON output + `query summary` over it;
  `--baseline` resolution order; wiki recipe smoke-checked by hand in the protocol (§6).
- **M2.5 Schema as the contract** — 100% property descriptions (39/83 today), `examples` on
  address-bearing fields (`stableId`, `stepPath`, `activityTraceId`), a top-level `$comment` naming
  the size trap and `kronikol query`. Guarded by the M0 drift test plus
  `Every_schema_property_has_a_description`.
- **M2.6 Source locations where they are free** — `feature.sourceFile`, `scenario.sourceFile` /
  `sourceLine` from Gherkin `uri`/`line` on the Cucumber path (parsed today, dropped) and from
  ReqNRoll live capture if `FeatureInfo` exposes the folder path (verify; SpecFlow's did). Unit-test
  adapters: no cheap source, skip (§9 Q5). Steps keep the bare file name — document
  `Glob **/<file>` as the move; an opt-in repo-relative path only if asked for.
- **M2.7 Retries / duplicates** — optional `attempt` (Cucumber has it; today a label) and a `×N`
  marker in `query scenarios` for repeated stableIds. Low priority beyond the M0 crash fix.
- **M2.8 Skill distribution + drift guards** — (a) the packed `kronikol-*` `dotnet new` templates
  ship `.claude/skills/kronikol-test-debugging/` + a `CLAUDE.md` block (`Kronikol.Templates.csproj:16-31`
  packs `**\*` with `NoDefaultExcludes`; the `ttd-*` folders are not packed; verify the template
  engine copies dot-folders).
  (b) `kronikol init-agents [dir]` for existing repos: copies the skill, appends the block to
  `CLAUDE.md` / `AGENTS.md` idempotently. (c) Dogfood: a repo `.claude/skills/` copy + a fact that it
  is byte-identical to `templates/skills/`. (d) `SkillDriftTests`: every verb in `PrintUsage` appears
  in `SKILL.md` and `commands.md` and vice versa; every `--flag` in `commands.md` is accepted by
  `QueryOptions.Parse`; `query.py` smoke (skipped without Python) — `summary`/`failures` counts on
  the CiPreview.Mixed fixture equal the .NET tool's `--count`. (e) The skill's worked examples point
  at `examples/Example.Api/tests/Example.Api.Tests.CiPreview.{Mixed,AllFailing}` output.
- **M2.9 `--json`** on the listing verbs (summary, scenarios, failures, services, interactions,
  assertions, diff): envelope `{ report, kronikolVersion, items[], truncated, next }`, text stays the
  default, budget still applies (announce truncation in the envelope). The ≥100 MB streaming test
  from REPORT_QUERY_PLAN §3.6 rides along (corpus from `tools/query-bench`).
- **M2.10 OTLP** — `kronikol.test.result` on every span of a test; wiki states `IncludeBodies`
  defaults to `false` and names `kronikol.test.id`. Low priority.

### M3 — wider interop (after M1/M2)
- **M3.1 MCP server, thin, remote-first.** Official C# SDK (v2.0, stdio + stateless HTTP). Tools map
  1:1 onto the verbs through the M2.9 envelope — near-zero new logic. The ecosystem shape
  (ReportPortal 37 tools, Sentry + Seer, pytest-mcp) is list / get / filter / payload / history,
  which `query` already is. Honest judgment: agents with a shell run the CLI natively, and a **local
  stdio** wrapper adds little — build last, if ever. The variant that earns its keep is a **remote
  Streamable-HTTP server next to the CI artifact store**: it indexes uploaded reports by
  `ciMetadata.commitSha` (M2.3), owns the last-green baseline (absorbing M2.4's convention), and
  serves the cases the CLI cannot — connector-capable no-shell hosts (Claude Code / desktop connect
  remote HTTP with OAuth; claude.ai, mobile and Slack only through connectors — so "no-shell hosts"
  means the connector-capable ones, not all of them), clean-environment cloud agents where a .NET
  10 tool install is friction or policy, enterprises that allow vetted MCP but not agent shell, and
  above all reports that live in artifact storage rather than on the agent's disk. Side benefits:
  typed parameters delete the PowerShell quoting hazards of `--where "$.success = false"`, and the
  tools list is self-documenting where the CLI needs the skill to be found.
- **M3.2 CTRF** — a `RunOutputs` writer (`ctrf-report.json`, option default off) mapping scenario →
  test `{ name, status, duration, message, trace, filePath?, tags, retries? }` with
  `tool { name: Kronikol, version }`, plus `kronikol export --ctrf <TestRunReport.json>` for
  existing reports. Pin the CTRF schema version. Not a flag on the OTLP exporter (§0).
- **M3.3 `Specifications.md`** — markdown narrative of features / scenarios / steps (no
  interactions) from the same source as `GenerateSpecificationsData`, for agents and humans who
  want the spec without 114K tokens of chrome. Must decide blank-on-failure (§9 Q9). Option default
  off in M3; revisit when the agent-side demand is visible.

## 5. Test matrix (summary)

| Slice | Unit (red first) | E2E / Playwright | Guards |
|---|---|---|---|
| M0 | `QueryCommandTests` dup-stableId diff, `--raw` rejected; schema drift walker; normal-run diagnostics scope | — | drift walker stays forever |
| M1 | `RunEndPointerTests`, `FailuresDigestGeneratorTests`, `ReportGeneratorFailuresDigestTests`, instruction-file template facts, `IngestPipelineTests` ResultDefaulted, CI-summary section | digest deep link opens the scenario | secret-URI-absent fact; budget fact; byte-identity pre/post diff of every unchanged output |
| M2 | anchor/data attribute markup, hash-grammar unit facts, `MergeCommandTests` JSON, `--baseline` order, schema descriptions 100%, source-location mapping, `SkillDriftTests`, `--json` envelope + ≥100 MB streaming | `#sid-` navigation, filter keeps fragment, same-name disambiguation | template packaging fact (dot-folder present in the pack) |
| M3 | MCP tool ↔ verb parity fact, CTRF schema validation, Specifications.md fixture | — | — |

## 6. Verification protocol (pre-release gates, in addition to the full suite)
1. **Channel table** (M1.0): for each of the 12 `kronikol-*` templates, `dotnet test` at default
   verbosity — is the pointer visible? Record runner (VSTest / MTP), verbosity needed, and whether
   `Console.Error` fares better. The table lives in this file.
2. **Tokens-to-answer**: starting from the console output alone on the `CiPreview.Mixed` run,
   count commands and bytes until the failing call's request is on screen. Target ≤ 3 commands,
   ≤ 8 KB. Repeat with `Failures.md` as the entry point (target 1 file read).
3. **Fresh-agent dry run**: `dotnet new kronikol-xunit3`, inject one failing assertion and one
   failing SQL call, open a new Claude Code session with only "the tests are failing". Pass = the
   agent reaches both causes **without opening `TestRunReport.json` or the HTML**; the transcript
   is the evidence. Do once more with an AGENTS.md-reading agent if one is available.
4. **Budget**: `Failures.md` for the `AllFailing` example stays under the §M1.2 cap.
5. **Cross-runtime**: `kronikol query summary` over a Kronikol4J-produced `TestRunReport.json`
   (parity-harness golden) succeeds with the `predates` banner and no error — the JSON contract is
   the parity boundary.

## 7. Documentation (per CLAUDE.md, same release as each slice)
- `Generated-Reports.md`: output table `:5-13` (+ `Failures.md`, `Failures.jsonl`, `CLAUDE.md`,
  `AGENTS.md`, later `ctrf-report.json`, `Specifications.md`), anchors `:336-337` (`#sid-`), Gold
  Standard `:614-651` → `kronikol query diff --baseline`.
- `Report-Configuration.md`: the new option rows; `Merging-Parallel-Reports.md:134`: merge emits JSON.
- `Querying-Reports.md § Using it from an AI agent`: the pointer, `Failures.md` as rung zero,
  `CLAUDE.md`/`AGENTS.md`, `--json`, `--baseline`, `init-agents`, later MCP; `AI-Integration-Prompt.md`:
  mention the generated files.
- `CI-Summary-Integration.md` (Debug section, `::notice`), `CI-Artifact-Upload.md` (allowlist fix,
  baseline recipe, retention note), `Ingesting-External-Captures.md:151` (ResultDefaulted banner),
  `Diagnostics-and-Debugging.md:44-50` (JSON `diagnostics` now populated on every path),
  `Exporting-to-OpenTelemetry.md:34` (`IncludeBodies` default, result attribute),
  `Capture-Time-Redaction.md` (name `Failures.md` / `CiSummary.md` as derived files).
- `_Sidebar.md`: an **AI & Agents** grouping (Querying Reports § agent, AI Integration Prompt,
  Failures digest, MCP when it exists). `README.md:175-191`, `nuget-readme.md:51-70` (skill line is
  missing there), `Home.md:54`, CHANGELOG, `PLANS_STATUS.md` row.

## 8. Kronikol4J, versioning, release mechanics
- The port mirrors the JSON/XML/YAML writers + schema and `CiSummaryGenerator`; its CLI is
  `Main` + `MergeCommand` only (no query); its two templates (`kronikol4j-junit5-{gradle,maven}`)
  can carry the same `CLAUDE.md` block. Every M2 field = the eight-place rule (§3.4): port in the
  same release or ledger it (`Kronikol4J/README.md` divergence ledger + `docs/REMAINING_PARITY.md`).
  M1's pointer / digest / instruction files and M2.4's merge JSON are ledger entries; the query CLI,
  skill and MCP stay .NET-tool-only by design — the Java side's contract is "produce JSON the .NET
  tool reads" (§6 gate 5).
- Each slice: patch bump in **all** packages (same number), CHANGELOG entry that calls out
  observable changes (new files in every consumer's `Reports/`; new console lines; merge writing a
  second file), wiki, commit + tag `v{version}` + push. Template pins track the previous release.

## 9. Open questions (answers needed before green-light; recommendation first)
1. **Defaults for the new outputs.** Recommend ON for the pointer, `Failures.md/.jsonl` and
   `CLAUDE.md`+`AGENTS.md` (the JSON + schema precedent; `WriteCiSummary` is off because it writes
   to CI sinks, a different class). Option names: `WriteRunSummaryToConsole`,
   `GenerateFailuresDigest`, `WriteAgentInstructions`.
2. **File names.** `Failures.md` + `Failures.jsonl` (PascalCase like every sibling) rather than the
   09-06 `failures.jsonl`; `CLAUDE.md` / `AGENTS.md` are fixed by convention.
3. **Merge JSON shape.** Recommend the mergeable superset (what merge consumes; carries `ciMetadata`).
4. **Baseline home.** Recommend `<reports>/baseline/TestRunReport.json` → `$KRONIKOL_BASELINE`; keep
   `CiArtifactRetentionDays = 1`; the remote server owns real history.
5. **Source locations for unit-test adapters.** Recommend skip (no cheap, portable source); Gherkin
   paths only. Repo-relative step paths only on request (privacy of absolute paths).
6. **Skill single source of truth.** Recommend `templates/skills/` stays canonical; repo `.claude/skills/`
   copy + identity fact; templates and `init-agents` copy from it at pack/run time.
7. **`query failures` exit code.** Recommend unchanged (0): the runner already fails the build; a
   read command that fails on data confuses scripting.
8. **`--json` scope.** Recommend the listing verbs only; payload verbs already have `--out`.
9. **`Specifications.md` on a failed run.** Recommend NOT blank (agents want the narrative regardless;
   the living-docs HTML keeps its rule) — but this is a product stance, not a technical one.
   **CLOSED in M3.3: not blank.** It is in the `ExpectedTestCount` guard with its siblings all the same,
   which is a different rule — a partial run must not overwrite a good spec with a shorter one.
10. **`::notice` on GitHub.** Recommend yes (one line, visible in the run summary), only when detected.

## 10. Non-goals
- Making the HTML agent-navigable (markdown/JSON siblings are the industry answer; the data islands
  `#puml-data`, `#kron-search-index`, `data-search` already permit targeted extraction).
- Per-failure files written at failure time (fights the single-flush design and parallel-test FIFO;
  run-end artifacts only).
- Injecting digests into framework failure messages (no adapter rewrites results; the pointer +
  `Failures.md` get there without framework surgery).
- Flipping `LogParameters` or `Redaction` defaults (capture-time redaction is the security boundary).
- Capturing application logs into the report (out of scope; `activityTraceId` is the bridge).
- A persisted query index (QUERY_PERF gate stands: only on evidence of routine 250 MB+ reports).
- `llms.txt`, note-divergence reconciliation, internal-flow summaries in the standard JSON.


## 11. Implementation log

### 3.1.0 — M0 + M1 (2026-09-12, working tree)

**M0, the ride-along fixes (§2.3), all six.** `query diff` groups scenarios by `stableId` and matches the
groups in order, announcing `! N scenarios share a stableId (repeated rows or retries) — matched in order`;
`diff --body` across runs matches the n-th holder rather than `FirstOrDefault`. The schema declares
`exampleFlatValues` and `exampleDisplayName`, every property carries a `description` (39 of 83 did), the
address-bearing fields carry `examples`, and a top-level `$comment` names the size trap and
`kronikol query` — all four guarded permanently by `TestRunReportSchemaContractTests`, whose walker fails
on the first undeclared or undescribed property. `CreateStandardReportsWithDiagrams` opens a
`ReportDiagnosticsScope` when none is scoped, so adapter-driven runs stop discarding every diagnostic
(one limit, now pinned by a test's comment: an `OutputFailure` still cannot reach the data files, because
the diagnostics snapshot is deliberately taken before the outputs run so the HTML and the JSON agree).
`--raw` deleted. Banner reworded. Wiki artifact allowlist corrected.

**M1, the discovery loop.** `RunSummaryConsoleWriter` (pointer + `::notice` + the CI-summary section),
`FailuresDigestGenerator` (`Failures.md` + `Failures.jsonl`), `AgentInstructionsGenerator`
(`CLAUDE.md` + `AGENTS.md` from one embedded template), all three wired into the isolated `RunOutputs`
list, plus `DiagnosticKind.ResultDefaulted` recorded by `IngestPipeline` and surfaced by `query`'s
provenance banner and by the digest. Three options, all default on:
`WriteRunSummaryToConsole`, `GenerateFailuresDigest`, `WriteAgentInstructions` (§9 Q1 recommendation
adopted). `kronikol ingest` prints the pointer's failure lines through its own writer and suppresses the
library's, so the command speaks with one voice. `.jsonl` added to the CI artifact allowlist.

Decisions taken during implementation, beyond the §9 list:
- The pointer prints the **agents** line only when something failed. A pointer that speaks on every green
  run is one people learn to skip.
- The size is printed for the **data file only** — it is the file an agent is tempted to open and the one
  that makes opening impossible.
- The digest's status strings are spelled the way the report spells them (`InternalServerError`, not
  `500`), so a value read in the digest can be grepped in the report. Numeric HTTP status in the data
  file would be a better contract but changes every data file's bytes and the Kronikol4J goldens — out
  of scope here, worth a V4 note.
- `Failures.md` and `Failures.jsonl` are **one** `Add()` action, so the pair is always consistent: if the
  markdown cannot be written the jsonl is not written either.

### M2.1 — stableId deep links (2026-09-12, working tree)

`data-stable-id` on the scenario `<details>` and on **both** copies of every example row (the flat
parameter table and the grouped one — the audit recommended flat rows be left alone, but the flat table
is the one displayed by default, so a link that skipped it selected a row inside `display:none`; the
script resolves whichever copy is displayed instead). Computed with the same four arguments as the JSON
writer, and a contract test asserts set equality between the ids in the HTML and the ids in
`TestRunReport.json`. Emitted for `Specifications.html` too — one renderer builds both, and the ids are
correct there; the plan had it out of scope on the grounds that its *data* files omit `stableId`, which
is still true and unchanged.

Hash grammar: the anchor is the first segment and filter state follows it (`#sid-<id>&status=Failed`).
`parse_url_hash` no longer returns early on an anchor — it applies the filters first and resolves the
anchor last, because a filter pass can collapse what the anchor has to open. Three fixes fall out:
*Clear All* keeps the anchor, a `hashchange` listener makes a link pasted into an already-open report
work (there was none — `parse_url_hash` only ever ran on `DOMContentLoaded`), and `#scenario-<slug>`
aimed at an example row now opens its group (it called `setAttribute('open')` on a `<tr>`, a no-op, and
scrolled to something still hidden).

`kronikol query failures` and `steps` print `open: <report>.html#sid-<id>`, gated on the HTML existing
next to the data file. `CLAUDE.md`/`AGENTS.md` gain the row.

**Attribute order is load-bearing** (from the blast-radius audit): `data-stable-id` sits after
`class`/` open` — `ToggleDefaultsMarkupTests` pins those literal prefixes — and before ` id=`, because
`FailureClusterReportTests` matched `[^>]*\bid="` greedily and `data-stable-id="` ends in a
word-boundary `id="`. Those two regexes are now `\sid="`, which no attribute suffix can reach.

Three existing facts had quietly stopped guarding anything and were re-anchored rather than left green
(`Deep_link_JS_handles_row_inside_parameterized_group`, `Report_expands_scenario_from_hash_on_load`,
`Flat_table_rows_have_no_id_attribute`) — the 3.0.82 substring class. Two of them, plus the new unit
test, sliced the flat table from the first mention of `param-table-flat`, which is in a script.

Verification: 4043 unit + 8 new E2E green; the *Clear All* and `hashchange` facts were re-run red against
the unfixed scripts to prove they bite.

### M2.3 + M2.6 + M2.7 + M2.2 — the JSON field batch (2026-09-12, working tree)

Batched into one pass as §3.4 directs, so the schema, the XSD and the Kronikol4J ledger move once.

**M2.3 — run identity.** `ciMetadata` and `environment {os, runtime}` are emitted **unconditionally** in
all three data writers, between `endTime` and `features` so a streaming reader and a person running
`head` both see which run this is without the megabytes after it. Off CI the same seven keys come back
with `provider: "None"` and nulls beneath — a shape that appears only sometimes is a shape nothing can
key on. `CiMetadataDetector.Detect()` still returns **null** off CI, deliberately: the HTML summary's CI
table is gated on exactly that, and making the detector return a `None` record (the obvious shortcut)
would put a "CI (None)" table on every report generated on a laptop. The all-null object is built inside
the writers instead, and `MergeableReportReader` collapses a `None` provider back to null so merged
reports keep the same gate. New `RunEnvironment` record; `GenerateTestRunReportData` gains one optional
trailing `CiMetadata?` parameter (the 3.0.80 convention — ~60 call sites keep compiling). `query summary`
prints `run: <branch> @<sha>  <repo>  <provider> #<build>` when there was a CI, and nothing when there was
not. XML omits its empty children, which is that writer's own convention; the JSON is the shape to key on.

**M2.6 — source locations.** `feature.sourceFile`, `scenario.sourceFile`, `scenario.sourceLine`.
Deliberately a *different contract* from `ScenarioStep.SourceFile`, which is a bare file name from
`[CallerFilePath]`: a scenario's is the project-relative path the Gherkin document already states. Two
lanes supply it. **Cucumber**: `GherkinScenario` has carried the document uri since it was written and
nothing read it, and `CucumberScenarioNode` never declared `location` at all though every producer emits
one. Paths are normalised to forward slashes — the golden fixture from playwright-bdd on Windows says
`features\kronikol-demo.feature` and cucumber-js on Linux says `features/…`, and a consumer globbing for
the path should not have to know which machine ran the tests. **ReqNRoll**: the plan's premise
(`FeatureInfo.FolderPath`) is a dead end — verified on 3.3.4, it gives the folder `"Features"` and the
file name is unreachable because a feature's `Title` is not its file name (`Muffins.feature` generates
`Feature: Parameterized Diagnostic Feature`). The real seam is `ExamplesBlockResolver`, which already
reflects into the internal `FeatureInfo.FeatureCucumberMessages` and holds the `GherkinDocument`; it now
also exposes `ResolveSource`, with the same silent-degradation contract, and the reflection drift test
covers `GherkinDocument.Uri` / `Scenario.Location.Line` too. An outline reports the **declaration** line,
one line for every row — `exampleValues` is what says which row, and a per-row line would make two
scenarios that are the same code look like different code. Surfaces in `query failures`, `query steps`
and `Failures.md` (markdown + jsonl). The unit-test adapters are still out (§9 Q5) but the audit verified
the seams *do* exist and are one-liners — xUnit v3 `XunitTestCase.SourceFilePath`, TUnit
`TestDetails.TestFilePath`, MSTest `TestMethodAttribute.DeclaringFilePath`; the blocker is that all three
bake the **absolute build-machine path** into the assembly. NUnit 4 and LightBDD have no seam at all.

**M2.7 — retries.** `scenario.attempt`, **1-based** to match the `retry N` label rendered beside it
(Cucumber's wire value is 0-based, and a field disagreeing with the label on the same scenario is worse
than no field). `query scenarios` marks rows sharing a stableId with `×N` and a later attempt with
`attempt N`. **Latent M0 bug fixed here**: the duplicate-stableId warning counted the empty-string group,
so a pre-3.0.47 report — which has no stableIds at all — announced that every scenario in it shared one.
It now says `! this report has no stableIds (written before 3.0.47) — scenarios matched by position`.

**M2.2 — parameter-capture hint.** `ParameterCaptureHint`: a SQL statement carrying placeholders and no
values is the one dead end a report cannot answer its way out of, so `Failures.md` and `query failures`
say so once. The plan's wording would have been wrong: **`LogParameters` alone changes nothing** — the
`-- Parameters:` block is appended only at `Raw` verbosity and the default is `Detailed` — and the EF Core
interceptor captures no parameters at all while stamping the same `SQL` category, so the message names
both settings and says so. Scoped to a closed list of SQL-shaped dependency categories, because a `?` in a
query string, a `:` in `host:8080` and an `@` in an email would each trip a placeholder scan run over
everything. The query side needs a payload read (the index holds only a hash, a length and an offset), so
it is bounded: failing scenarios only, SQL categories only, statements under 8 KB, stopping at the first
hit — a green run opens nothing. Never on the console pointer or the `::notice` (§3.3).

**Ride-along bug, found by the audit and fixed here:** `MergeableReportReader.ReadSteps` never read
`failureMessage`, `sourceFile` or `sourceLine`, though both step mappers have written all three since
3.0.47 — so merging a sharded run silently threw away every step's failure message and every step's
location, which is most of what makes a merged report worth reading. Nothing said so.

Verification: 4068 unit green after M2.3 + M2.6; the rest re-run per slice.

### M2.4 — merge emits JSON, and `diff --baseline` (2026-09-12, working tree)

**(a) The merge writes two files.** `kronikol merge` writes the merged `TestRunReport.json` beside the
HTML, in the mergeable format, `--no-json` to opt out. New public `MergeableReportRenderer.Serialize`
and `MergeFiles`; the CLI does read → merge → render → serialise rather than the old one-shot, and
`MergeFilesToHtml` is now a two-line delegation so the five E2E facts that call it are untouched. The
adapter had to be new public API in `Kronikol.Reports.Merge`, not a call into `ReportGenerator`:
`GenerateMergeableReportJson` is `internal` and **`Kronikol.Tool` is not in the `InternalsVisibleTo`
list**, and the public `GenerateTestRunReportData` writes through `WriteFile` into
`CurrentReportsDirectory` and returns a path, so the CLI would have had to relocate the file the way
`Render` does. A serialiser returning a string avoids all of it. Two guards, both from the audit: the
data file is written **only after** the render succeeded, and never over one of the resolved inputs —
a directory input is swept recursively, so `merge ./artifacts -o ./artifacts/runner1.html` would
overwrite the shard it just read and the next run would merge its own output back in. **The format
version does not move.** Adding `httpInteractions` is additive and the key is the one the *standard*
report has always used, so an old reader ignores it and an old `query` reads it — bumping to 2 would
have made `query` hard-error (`QueryCommand.cs`'s `is { } version and not 1`) on files that are strictly
better than what it accepts today.

**The plan's premise was wrong about what the mergeable format contains.** It calls the format a
superset "— it is what merge consumes and it carries `ciMetadata`", and §1.1 treats a merged file as
queryable. It was not: `GenerateMergeableReportJson` passed `logLookup: null`, so **every mergeable file
ever written had zero `httpInteractions`, zero `annotations` and no `stepPath`**, and because an empty
result is indistinguishable from a run that made no calls, `services`/`interactions`/`http`/`body`/
`values`/`flow`/`trace`/`annotations`/`grep --in bodies` all answered emptily rather than erroring.
`Merging-Parallel-Reports.md` had promised the opposite in print since the feature shipped. Found by the
audit; the user called it in-scope. Shards now write their traffic and the merge carries it. `stepPath`
and the annotations are carried **explicitly** rather than re-derived, because the derivation
(`AttributeInteractionsToSteps`) walks the diagram markers and the markers are dropped at write time by
design — re-deriving from re-read logs would silently blank every attribution. `MergeableReport` gains
`Interactions`, `StepPaths`, `Annotations` and `Diagnostics`; the reader rebuilds `RequestResponseLog`
from `MapLogJson`'s 22 fields (`MetaType` is what tells an event's free-text "method" from an HTTP verb
on the way back); the merger concatenates — no dedup, because a request and its response deliberately
share one `RequestResponseId`.

**Ride-along bug:** the merged HTML labelled **every** scenario "no interactions captured", because
`showNoInteractionsMarker` fell through to `RequestResponseLogger.RequestAndResponseLogs` — the ambient
log of the *merging* process, always empty — and said so directly beneath a sequence diagram showing the
calls. Measured before fixing: every other `trackedLogs` consumer in `GenerateHtmlReport` is
`|| diagrams.Any(…)`, and `Render` supplies precomputed diagrams, so this was the only visible casualty.
`Diagnostics` round-tripping also restores the `ResultDefaulted` banner on a merged report.

**(b) `diff --baseline`.** Resolution `<reports>/baseline/TestRunReport.json` → `$KRONIKOL_BASELINE`
(file or directory) → exit 2 naming both, with `<reports>` taken from `ReportIndex.Directory` — the
*resolved* path, since `ResolveReport` may have descended. Convention beats the variable. The audit's
direction trap is real and is pinned by breaking it: inverting the swap turns 7 of the 11 facts red.
Both files are normally called `TestRunReport.json`, so colliding names fall back to paths relative to
their deepest shared directory. A `baseline` segment is skipped by `ResolveReport`'s recursive fallback
*and* by `MergeCommand.ResolveInputFiles`, so adopting the convention neither creates an ambiguity nor
folds last-green into today's merge. Env reads go through an injected `Func<string,string?>` — the
`CiMetadataDetector.Detect` convention — because the query tests run in the same parallel xunit process
as everything else.

**(c)/(d) beyond the plan:** the Gold Standard rewrite needed something `diff` did not have. Its whole
purpose is catching a *silent interaction decrease*, and `diff` reported only results and timings, so
"rewrite the PowerShell onto `kronikol query diff`" would have been a rewrite onto a command that
cannot answer the question. `diff` gains a **Tracking** section: services that captured fewer calls than
the older run, worst first, `no longer tracked` when one falls to zero. Requests only; silent when the
new run captured more; skipped when either side has no interactions, since absent is not lost. The old
script was also worse than it looked — it `ConvertFrom-Json`'d both whole reports, the exact thing this
tool exists to avoid, and compared one global total, so a service dying while another grew by the same
amount read as no change.

Verification: 4116 unit green (from 4086 at the start of this slice). Wiki: `Merging-Parallel-Reports`
(arguments, the merged-data section, GitHub + Azure baseline recipes, corrected Limitations and the
now-true query claims with a note on what older files do), `Querying-Reports` (`--baseline` section,
exit codes, older-reports caveat, agent recipe row), `Generated-Reports` (outputs table gains M1's four
files; Gold Standard rewritten), `CI-Artifact-Upload` (top-level-only enumeration, `.jsonl`, the M1
files, baseline retention), `Report-Configuration`; plus the skill's `SKILL.md` and `commands.md`,
`README.md` and the tool's NuGet description.

### M2.8 — skill distribution, `init-agents`, and the drift guards (2026-09-12, working tree)

**(a) One copy on disk, four installs.** `templates/skills/` and the new `templates/agents/CLAUDE.md` are
canonical (§9 Q6). The twelve templates pack them by **`PackagePath`** rather than by physical copy — the
audit's other option was twelve folders × three files, and thirty-six duplicates is a drift surface no
test wants to own. One batched MSBuild target over a `KronikolTemplateRoot` item list does it, and
`Every_template_gets_the_skill_and_the_block` compares that list against `Directory.GetDirectories`, so a
thirteenth template cannot ship without them. **`AGENTS.md` exists only as a package path**: `.gitignore`
line 372 is a bare `AGENTS.md`, which git applies at every depth, so a real `templates/*/AGENTS.md` would
build on a developer's machine and be silently absent from CI's checkout and therefore from the pack.
Packing from `templates/agents/CLAUDE.md` to two package paths sidesteps it without touching a policy the
repo set deliberately. (The repo root's own `AGENTS.md` is still ignored — `init-agents` writes one and
git does not track it. Left alone: it is the user's call, and the one-line negation is theirs to make.)

**Both packaging questions were answered empirically, not from documentation.** The audit could not tell
whether a template.json `modifiers.exclude` *adds to* or *replaces* the engine's seven default excludes,
because the two patterns there duplicate two of the defaults. Packing and instantiating settles it: the
scaffold contains `.claude/` and does **not** contain `.template.config/`, so the modifier adds. The pack
carries the dot-directory only because `NoDefaultExcludes` is set, and `dotnet new` copies it only
because the engine's defaults contain no dot-rule. Neither is a property anything in the repo asserted, so
CI now unzips the real package and inspects the real scaffold for all twelve roots, and diffs the
scaffolded `SKILL.md` against the canonical one — the previous job packed, installed, instantiated and
built without ever looking inside, which is exactly how a dropped dot-folder ships green.

**(b) `kronikol init-agents [<dir>]`.** Writes the three skill files and the block into `CLAUDE.md` and
`AGENTS.md`. The block is delimited by `<!-- kronikol:begin -->` / `<!-- kronikol:end -->` and **replaced
in place**, because an installer that appends is correct exactly once: run it again after a tool upgrade
and the file grows a second, stale copy. That makes "re-run it to upgrade" a safe instruction rather than
a hopeful one. A new file gets the shipped bytes verbatim; an existing one gets the block re-punctuated in
whichever line ending it already uses. Unchanged files are reported as `unchanged` and not rewritten, and
a directory that does not exist is an error rather than a new tree somewhere nobody meant. The working
directory is injected the way `getEnv` is in `query`, for the same reason: the tests share one parallel
xunit process.

**Registering the command was a three-place edit with no test coverage, so it became a one-place edit.**
`Program.cs` kept the blurbs, the dispatch switch and the `PrintUsage` sequence in step by hand, inside
top-level statements nothing can reach — a command could route and be absent from both help views with
nothing to notice. All three now read `Commands.Table`, `Program.cs` is two lines, and `CommandTableTests`
asserts the coverage in both directions plus that `Program.cs` names no command at all.

**(c) The repo dogfoods it** by literally running `kronikol init-agents .`: `.claude/skills/` is a real
install, not a hand copy, and `CLAUDE.md` carries the same block verbatim.

**(d) The drift test as specified was already green** — the audit verified the 32 documented flags and the
32 parsed cases were exactly equal, and all 18 verbs had reference headings. The red had to be planned
for, and the strict matcher is where it lives: a verb counts only where it is *demonstrated as a command*
(inside a code span or fence, whole word, not hyphen-prefixed), because `body`, `diagram`, `steps`,
`flow`, `values`, `trace`, `note`, `grep` and `summary` are all ordinary English words here — the 3.0.82
substring bug class exactly. That fails immediately on `body` and `diagram`, which SKILL.md used only as
nouns. **The audit's verb-extraction regex was wrong** and the count assertion caught it: `^  ([a-z-]+)\s{2,}<report>` misses `interactions`, whose twelve characters leave a single space.

**Two silent-typo bugs, found by needing something to pin.** `grep --in` dropped an unknown target without
a word, so `--in bodys` searched nothing and printed nothing — indistinguishable from proof the value is
absent, on the one command whose whole purpose is finding a value. `--sort` did the same at both call
sites via a `_ =>` arm, including for a value valid on the *other* view (`--group-by ... --sort bytes`).
Both now exit 2 and name the valid set, and the arrays they validate against are what the drift test pins
the help and the reference to.

**(e) The content half the specified assertions do not reach**, which the audit called the honest part:
`Failures.md` is now rung zero of the ladder, the two banners M0/M1 added are explained (a green run
printing `recorded no result` is *not* evidence anything passed), `body` and `diagram` have recipes, and
there is a worked three-command investigation using real output from `CiPreview.Mixed` — run, captured,
pasted, not invented. `query.py`'s docstring said "the four commands that matter most" and named five, of
the six it registers; and its `steps` omitted the scenario address the real tool leads with, so an address
the fallback printed could not be pasted back. `FallbackScriptTests` is what found that: it runs the
script under the machine's Python (probing both `python` and `python3` — Windows has the first, the CI
runner the second) and makes the two agree on counts and addresses.

**An adversarial review of this slice found more in the tests than in the code, which is the outcome that
matters.** Three of the new facts were weaker than they read. The banner fact asserted three strings a
person had typed, so the direction it existed for - the tool grows a banner, the skill does not - could
never fail it; and the three it named were only what `WriteProvenance` emits, while six more live in
individual commands. SKILL.md therefore said "Three of them exist", which was **false about the shipped
tool**, and the fact meant to guard that sentence reported green. Worst for `diff`, which
`WriteProvenance` skips entirely: every banner a diff can lead with was one of the six the skill said did
not exist. The coverage facts used bare `Contains`, which five of nine `--group-by` dimensions and four of
six `--in` targets satisfied from unrelated prose - the 3.0.82 substring class, committed inside the test
whose own docstring cites it. And `CommandTableTests` could not see a `query` entry mis-wired to
`MergeCommand`, because merge's usage text contains the words "kronikol query"; that was demonstrated by
applying the mutation, which passed with the whole suite green. All three are now derived from source or
anchored on the emitted line, and the last was re-verified by re-applying the mutation.

The same review found four real defects in code that predates this slice. `RerunPrefix` carried the
filters but not `--sort`, `--in`, `--values`, `--step` or `--slower-than`, and `grep`'s footer did not call
it at all - so a `next:` line handed the agent a command that rebuilt a *different* list, reprinted rows it
had already seen and then announced the listing exhausted. `--in ""` produced zero targets, which the new
unknown-target guard passed by never entering its loop: the emptiest possible input defeating the check
whose entire purpose is that grep never silently searches nothing. `interactions --sort` was accepted and
discarded. And `init-agents` needed hardening the first pass had not thought through: a file that is not
valid UTF-8 was being decoded with the replacement fallback and written back with every non-ASCII byte
destroyed - outside the markers, in text the command promises not to touch - a byte-order mark was
stripped, an unclosed or duplicated block was guessed at, and an unwritable file threw. Each is now a
message and exit 1, changing nothing.

Two lessons worth carrying: the review agents ran with write access and one mutated `Commands.cs` to prove
a fact could catch it, then was killed before restoring - **give a review fleet read-only tools, or expect
to restore the tree afterwards**. And in restoring it I reverted a legitimate pre-compaction change to
`CrossRunBodyDiff` before the CHANGELOG caught me; the changelog was the only record that the behaviour was
intended.

Verification: 4180 unit green (from 4116), plus SearchEngine, TcpTap and IKVM, a clean Release build, and
the pack gate run locally against a real `dotnet pack` — including the `--no-build` form `release.yml`
uses — and real `dotnet new` instantiations, before any of it was written into CI. **E2E is the one gap
left open here**: an early run was 758/0, a later one 757/1 whose failing test name was lost to a
`| tail -4` and which overlapped four other builds, and the clean rerun had its tool DLL replaced
mid-flight by a concurrent build of mine. No E2E test exercises `kronikol query`, so a regression from
this slice is unlikely, but it is unproven. Re-run it alone and capture the whole output before the
release gate is called met.

### Repo state this work sits on (read this first after a break)

The repository moved while M0/M1 were being written: another session released **3.0.84, 3.0.85 and
3.0.86** (note formatting, note copy fidelity, diagram width) and they are committed. `Directory.Build.props`
therefore reads **3.0.86**; this work is uncommitted on top of it and releases as **3.1.0**, whose
`## [3.1.0] - unreleased` CHANGELOG section already holds the M0 and M1 entries. Two consequences:

- **It is 3.1.0, not 3.0.87 — a MINOR bump.** The plan and the CHANGELOG both said 3.0.87 for most of
  the implementation, which was wrong: CLAUDE.md's rule is that *anything new* is a minor and the
  highest-ranking change decides, and this release adds four emitted files, six data-file fields, a
  public record, a `DiagnosticKind` member, a URL-hash form and new output from four query verbs. The
  3.0.x history under-reports its features because everything through 3.0.85 took a patch regardless of
  content; 3.0.86 was genuinely fixes-only, so this is the first release where the rule actually bites.
  The `--raw` removal is the one thing that ranks higher still — removing a public option is nominally
  MAJOR — and it ships here **deliberately** (decided 2026-09-12): the flag had been parsed and
  discarded since 3.0.47, so nothing passing it was getting behaviour worth a whole major cycle to
  preserve. The CHANGELOG entry says so.
- Do **not** re-bump the version until release; then bump `Directory.Build.props` to 3.1.0 in one go and
  move the template pins to 3.0.86.
- The Kronikol4J divergence ledger entry for this work was overwritten by that session's own 3.0.84–3.0.86
  entries and **must be re-added** at release: the schema file changed (every property described,
  `$comment`, `examples`, two newly declared fields) and `ReportDataSchema.java` pins it byte-for-byte
  against a .NET-captured golden.

### M2.9 — `--json`, and what a test of cost found (executed)

Shipped as specified: `{ formatVersion, command, report, kronikolVersion, notes[], items[], total,
truncated, next }` on `summary`, `scenarios`, `failures`, `services`, `interactions`, `assertions` and
`diff`; text the default; the budget still applied and truncation announced inside the envelope. The
audit's central call was right and worth restating — **the format belongs in `QueryWriter`, not in the
verbs**. Every verb already writes through it and it is constructed in exactly one place, so `--json`
cost each verb a projector lambda plus, for four of them, a record to replace a pre-rendered string. No
`if (options.Json)` reached a verb, which is what keeps the two formats from drifting apart.

Decisions taken while implementing, beyond the audit:

- **The eleven other verbs refuse `--json` (exit 2) rather than answering with an empty envelope.** A
  step tree, a payload, a trace and a diagram are shaped for a reader; an object form is either an array
  of rendered strings or a second data model. `QueryWriter` still has the wrap-the-rendered-text fallback
  so an unconverted verb is honest rather than silently empty, and a test asserts it never ships.
- **`--out` was widened to every verb and made to lift the budget**, instead of being left parsed and
  discarded on fourteen of eighteen. Shipping a machine-readable format while `--json --out F` did
  nothing was the trap the audit named, and the honest fix was the general one.
- **`--sort` is now refused wherever it cannot be applied.** 3.1.0 had already fixed `interactions`; the
  same silence remained on `scenarios`, which is the verb an agent hunting a slow test reaches for first.
- `services` and `failures` were converted to the shared pager rather than given parallel `Row` helpers.
  Both had hand-rolled footers, and both footers were wrong: `services` could overflow the budget with
  no resume pointer, `failures` hard-coded 25 and ignored `--limit`.

**The paging bug the audit predicted was real, and worse than described.** `Page` treated "finished" as
`last >= all.Count && offset == 0`, so an offset past the end *and* a first row too big for the budget
both printed `next: --offset {the offset just used}`. In text a reader shrugs; in JSON a script follows
`next` forever. There are three endings now and they are told apart, `next` is `null` in two of them,
and the loop a consumer would actually write is a test.

**What the ≥100 MB test found is the entry worth reading.** It was specified as a scale check and
written instead as a test of *cost*, because every other fact about `query` would pass unchanged if
`ReportScanner` called `File.ReadAllText` — the answers would be identical. On its first run it failed:
**scanning the 142 MB corpus allocated 324 MB**, and the cost tracked payload bytes rather than
interaction count. The scanner streams the file and stores byte offsets, exactly as designed, except on
the one branch that matters most by volume: each `content` token was materialised as a UTF-16 string and
then re-encoded to UTF-8, to compute a character count and a SHA-1, and dropped. Hashing the bytes the
reader already holds took it to **32 MB**, with every `b:` address byte-identical. The lesson generalises:
a design property no test measures is a comment. Three of the four facts run always, at a scale where the
defect is still visible, so the property is guarded on machines without the 143 MB corpus.

One more silent failure surfaced on the way: the body diff's "A body could not be read back" message went
through the `QueryWriter`, which is flushed only on exit 0 — so that path failed with no output at all.

**Amended the same day: `next` is an argument vector, not a string.** `LLM_FIRST_PLAN` §5.2 measured the
break this milestone shipped — a filter value containing a space (`--service "Dessert Provider"`) came
back as one interpolated string, and a consumer splitting it on whitespace got a stray positional and a
bare error on stderr. No quoting convention survives that, because the consumer chooses the split. Two
further defects were only visible once the pointer was read as a *command* rather than a suffix: it
dropped the positional addresses, so `interactions s0 --json` resumed across the whole report, and it
dropped the page size, so page two came back at the default width. `next` is now the whole command
`["query", verb, report, …addresses, …filters, "--limit", n, "--offset", m]`. The text footer keeps its
append-to-what-you-ran meaning and now quotes what it must; both are rendered from one token list, which
is the same lever as the rest of this milestone. `formatVersion` stays 1 — nothing released ever emitted
the string form.

### M2.10 — `kronikol.test.result`, and the layer the fact had to come from (executed)

The audit's blocker was real and structural, and it decided the design. `RequestResponseLog` has no
verdict and never can: every field on it is written at capture time, and the result exists only once a
framework adapter has assembled `Feature[]`/`Scenario[]`. Adding a field would mean forty-odd tracker
packages stamping a value none of them holds. **The join is free, though** — `log.TestId == scenario.Id`,
which `ReportGenerator` already relies on in four places — so the attribute arrives through a new
`OtlpExportOptions.TestResult` lookup. A delegate rather than a dictionary, so a caller with a large run
need not materialise one, and on the options rather than a `Map` parameter, so all three layers pick it
up from one place. The insertion is in `OtlpSpanMapper.Map`, the only site where a span is built.

Decisions taken while implementing:

- **The value is `ExecutionResult.ToString()`**, and the CLI reaches it through the public
  `FeatureSynthesizer.MapStatus` the ingest already uses, so a span attribute and the report the same
  files would produce cannot disagree. That settles the audit's open question about unrecognised status
  words in favour of agreement: an unknown word still maps to `Failed`, because that is what the report
  would say, and **the words are named on stderr** so a producer's typo does not redden spans silently.
  Omitting the attribute instead would have made the two disagree, which is the failure that matters.
- **`kronikol export --tests`** mirrors `IngestCommand` including the `files.Remove(testsFull)` guard — a
  tests file inside an input directory must not be swept up as an interaction capture. The malformed-line
  reporting was extracted so both readers report identically instead of the new one growing its own.
- **The streaming sink's limit is documented, not engineered around.** It POSTs on a flush interval while
  the test is still running, so the lookup has nothing to answer with. Buffering until a verdict existed
  would break D3, which is the rule the class is built on.

**Both tests that should have caught a new attribute were silently weakened** — `Maps_the_core_span_fields`
and the CLI's dry-run fact assert attributes by name and never the count, so a new one passes unnoticed.
Each has an absent-by-default twin now, and the mapper fact pins the attribute's position, since the
encoder emits in list order and the wiki's table documents that order.

### M3.2 + M3.3 — CTRF and `Specifications.md`, and the comparer that was wrong all along (executed)

Both are "one more entry in the isolated `RunOutputs` list", exactly as the audit said, and both are off
by default — so the byte output of a default run is unchanged, which is the cheapest §3.5 gate available
and the easiest to skip on the grounds that no existing writer was touched.

**The audit's `sN` ordering trap was a live bug, not a hazard.** `FailuresDigestGenerator.Enumerate`
ordered features with `StringComparer.Ordinal`; `BuildFeaturesJsonModel` — and therefore
`TestRunReport.json`, and therefore every `sN` the tool hands out — uses the bare culture-sensitive
`OrderBy(f => f.DisplayName)`, as do the XML, the YAML and all three specifications writers. So
`Failures.md` and `kronikol query` numbered the same run differently whenever two feature names differed
by case, punctuation or diacritics, and the digest's whole reason for printing an address is that a
reader can paste it into the tool. Fixed on the digest's side (the outlier, and the side that moves no
golden bytes). Two facts pin it, and the pair of them is the point: the unit fixture uses
`Alpha`/`beta`/`Gamma`, which **a full-pipeline run can never produce**, because `CapitaliseTitles`
upper-cases every initial before the writers see it — so the end-to-end fact uses `Order API` /
`Order api`, which survives capitalisation, and cross-checks the digest's address against the real
`TestRunReport.json` ordering rather than against a hard-coded expectation. The existing cross-check had
one feature, and one feature sorts the same under any comparer.

Decisions taken while implementing:

- **CTRF is a top-level verb, not `export --ctrf` and not a `query` verb.** The plan text said
  `kronikol export --ctrf`; that verb resolves `*.ndjson`/`*.jsonl` inputs and POSTs spans to a
  collector, so every part of its contract is the wrong one. A `query` verb was the other candidate and
  is worse: those answer under a byte budget, and a document that gets truncated is not a document.
  M2.8's one-table dispatch made the new verb a single edit.
- **One writer, two mappers, and a test that reads both.** `CtrfReportGenerator` owns the envelope, the
  summary and every key name; `Generate` maps `Feature[]` and `CtrfCommand.Map` maps what `ReportScanner`
  read back out of a file. `The_converted_document_is_the_one_the_run_would_have_written` asserts the two
  are byte-identical, and was proved load-bearing by mutation — dropping the feature-level `filePath`
  fallback and the retry-label filter from the CLI half turned it red and nothing else did. That fact
  also forced two real decisions: `summary.start`/`stop` are epoch milliseconds **truncated to whole
  seconds**, because that is all `TestRunReport.json`'s own `startTime` carries; and a report written off
  CI still declares `ciMetadata.provider: "None"`, so the CLI half keys on `ReportIndex.OnCi` rather than
  on the block's presence, or it would emit an `environment` naming a build called None.
- **Two of the audit's traps had already been fixed by M2.6 and M2.7 and were stale.** `retries` is
  `Attempt - 1` from a real modelled field rather than a regex over labels, and `filePath` is
  `Scenario.SourceFile ?? Feature.SourceFile` — both project-relative by contract. The `retry N` labels
  are still filtered out of `tags`, because they are Kronikol's own bookkeeping and would grow a new junk
  tag on every retry.
- **The schema pin is a key list, not a vendored schema and not a validator.** The CTRF schema is
  published rather than bundled, and the repo's convention (`TestRunReportSchemaContractTests`) is a
  hand-written walker with no dependency. The walker fails on any key outside the transcribed set, which
  is what stops a field being invented under a name no consumer reads; `extra` is the schema's own escape
  hatch and is where everything Kronikol knows and CTRF does not goes, including `kronikolAddress`.
- **§9 Q9 closed: `Specifications.md` is NOT blank on a failed run.** The other three spec outputs blank
  themselves so a red build cannot publish half-truths; this one is read by somebody trying to understand
  a system while it is broken. It is in the `ExpectedTestCount` guard with its siblings, though — a
  partial run must not overwrite a good spec with a shorter one, and a spec-surface output missing from
  that guard silently narrows it.
- **It carries `Rule` and `Description`, which the data trio drops.** The audit was right that "the same
  source as `GenerateSpecificationsData`" is too thin to read: that model flattens each step to a string
  and drops both. A declared divergence, not an accident. Author prose is a block quote so a `##` inside
  a Gherkin description cannot restructure the document.

**M3.1 was NOT built, it is not in 3.1.0, and the question has moved to a plan of its own.** Two things
changed while M3.2/M3.3 were being written, and both matter more than anything this plan has to say
about MCP.

First, the audit's blocker is not real. `scratchpad/audit/M3.json` records the C# MCP SDK as
unrestorable — 711 packages in the local cache, no match, no fallback folder. That was an offline
finding: `ModelContextProtocol` **2.2.0 restores here in under two seconds**. Feasibility is not the
reason and must not be recorded as one.

Second, a concurrent session has produced repo-root **`MCP_PLAN.md`** (2026-09-12, untracked,
investigation complete, **not green-lit**), which is now the standing analysis and supersedes the
paragraph this plan's §4 M3.1 carries. It argues that both predecessor plans asked only *would an agent
use it* — measured answer, mostly not, because `kronikol query` is already there — and never asked what
the **absence** of an MCP entry says about a .NET test-reporting tool in 2026 to the people choosing one.
It also measures away several of the cost objections: `Kronikol.Tool` has no `PackageReference`s of its
own, but its build output already carries 31 external assemblies pulled transitively, eight of the SDK's
twelve among them, so the "new dependency weight" argument is much smaller than it looks. **Its
recommendation is to build the local stdio server and ship it as 3.2.0, after the 3.1.0 tag** — which is
the same sequencing this milestone lands on from the other direction, and the reason not to hand-roll
something here.

So the decision recorded for **this** milestone is narrow and only about sequencing: M3 ships CTRF and
`Specifications.md`, MCP is not in 3.1.0, and whether it is built at all is `MCP_PLAN.md`'s question to
answer with a green light. The one judgement from this plan that survives untouched is the shape: **not**
the remote Streamable-HTTP server with hand-rolled OAuth — that is a hosted service, and
`CROSS_RUN_HISTORY_PLAN.md` §0 records a standing direction-note constraint against exactly that shape.

### Remaining

**Done:** M2.1 deep links · M2.2 parameter hint · M2.3 run identity · M2.4 merge JSON + `--baseline` ·
M2.5 (in M0) · M2.6 source locations · M2.7 retries · M2.8 skill distribution + `init-agents` + drift
guards · M2.9 `--json` envelope + `--out` everywhere + the streaming test · M2.10 OTLP
`kronikol.test.result` + `export --tests`. **All of M0, M1 and M2.**

**Left:** M3.1 MCP, deliberately — see the M3.2/M3.3 log entry above for the three reasons and for the
correction to the audit's claim that it could not be built at all. Everything else in this plan has
shipped: §7 documentation and §8's Kronikol4J ledger travel with M3, §6's verification protocol has been
run, and the release itself is done: `Directory.Build.props` carries **3.1.0** for every package, the
twelve template pins moved 3.0.85 → 3.0.86 (a scaffolded project restores from nuget.org on the day it
is created, so they track the *previous* release), the CHANGELOG entry is dated, and `v3.1.0` is tagged
and pushed. **This plan is closed** except for M3.1, which now belongs to `MCP_PLAN.md`.

A successor plan now depends on this one finishing. Repo-root `CROSS_RUN_HISTORY_PLAN.md`
(2026-09-12, investigation complete, not green-lit) states that it **assumes LLM_FRIENDLY_PLAN is
complete when it starts**, and names run identity in the JSON, `merge` writing JSON, `--json`
envelopes, `#sid-` deep links, the MCP server, the CTRF writer and the packaged skill with its
drift tests as things it expects to exist — its §1.5 lists the obligations, several of which it
says are mechanical and will break the build if skipped. So M2.4, M2.8, M2.9, M3.1 and M3.2 are
not optional tail-end items.

The read-only blast-radius audit for every remaining slice is saved as JSON per milestone in the
session scratchpad (`scratchpad/audit/M2.4.json`, `M2.8.json`, `M2.9.json`, `M3.json`, and
`M2.10_OTLP_kronikol.test.result_M2.2_par.json`) — sites, tests at risk, traps, docs. Read the
matching file before starting a slice; the M2.1 and M2.6 ones each caught a trap that would have
broken existing pins.
