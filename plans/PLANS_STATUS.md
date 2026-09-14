# Plan File Status

**Date:** 2026-08-30 (updated for 3.0.69) · **Repo version:** 3.0.69

Living status of every `*_PLAN.md` in `plans/`, verified item-by-item against
code, tests, changelog, git history, the wiki (`../Kronikol.wiki`), and the Java port
(`../Kronikol4J`). This supersedes the 2026-08-29 audit (in git history at `2270445`
if the full item-by-item record of that snapshot is needed); everything below was
re-verified fresh on 2026-08-30.

| Plan | Status | Shipped in |
|---|---|---|
| `BACKGROUND_STEPS_INLINE_PLAN.md` | ✅ Done, **deleted 2026-08-30** | 3.0.48 |
| `LONG_LINE_SYNTAX_ERROR_PLAN.md` | 🟡 ~95% (Java mirror open) | 3.0.48 |
| `REPORT_QUERY_PLAN.md` | 🟡 ~99% | 3.0.47, tail in 3.1.0 |
| `QUERY_V2_PLAN.md` | ✅ Done, **deleted 2026-08-30** | 3.0.51–3.0.58 |
| `BROWSER_RENDER_WORKER_PLAN.md` | ✅ Done, **deleted 2026-08-30** | 3.0.45 / 3.0.50 |
| `NOTE_YAML_TOGGLE_PLAN.md` | ✅ Done, **deleted 2026-08-30** | 3.0.59, 3.0.61–63, 3.0.66 (+fixes 3.0.67/68) |
| `OTLP_EXPORT_PLAN.md` | ✅ Done, **deleted 2026-08-30** | 3.0.60 |
| `EXAMPLES_BLOCKS_PLAN.md` | ✅ Done (1 ledger nit), **deleted 2026-08-30** | 3.0.64 |
| `REQNROLL_DUPLICATE_STEPS_PLAN.md` | ✅ Done (#71), **deleted 2026-08-30** | 3.0.64 |
| `TEOZ_PERF_PLAN.md` *(new, untracked)* | 🟡 In-house work done; upstream-gated | 6 PRs + 1 issue open upstream |
| `PERF_CI_PLAN.md` *(new, untracked)* | 🟡 ~95%; R5 blocked on upstream merge | PR plantuml#2840 open |
| `QUERY_PERF_PLAN.md` | ✅ Done (§3.1–§3.4 all landed) | 3.0.69 |
| `SEARCH_INDEX_PLAN.md` | ✅ Done (executed in full, §15 order; Phase 2 §10 deferred by design; post-release audit fixes in 3.0.71; user-requested scope extension in 3.0.72: descriptions/endpoints instant, stack traces deep-only) | 3.0.70–3.0.72 |
| `NOTE_YAML_TRAILING_WS_PLAN.md` | ✅ Done (executed in full; committed with the release as a design record) | 3.0.79 |
| `TOGGLE_DEFAULTS_PLAN.md` | ✅ Done (M1–M8 in full, §8 recommendations adopted; committed with the release as a design record) | 3.0.80 |
| `LLM_FRIENDLY_PLAN.md` *(new, 2026-09-10)* | 🟢 **Executed — everything but M3.1, which is deferred on purpose.** Green-lit 2026-09-12, §9 recommendations adopted (Q9 closed in M3.3: `Specifications.md` is not blank on a failed run). M0 (six ride-along fixes), M1 (run-end pointer, `Failures.md`/`.jsonl`, `CLAUDE.md`/`AGENTS.md`, `ResultDefaulted`), all of M2 (deep links, parameter hint, run identity, merge JSON + `diff --baseline`, schema contract, source locations, retries, skill distribution + `kronikol init-agents` + drift guards, `query --json` + `--out` everywhere + the streaming test, OTLP `kronikol.test.result` + `export --tests`), and M3.2 CTRF (`ctrf-report.json` + a `kronikol ctrf` verb) and M3.3 `Specifications.md`. **M3.1 MCP not built and not in 3.1.0**: the audit's "SDK unrestorable" finding is wrong (`ModelContextProtocol` 2.2.0 restores in seconds), and the question has moved to the new standalone `MCP_PLAN.md`, which recommends a local stdio server shipped as 3.2.0 *after* the 3.1.0 tag — same sequencing, reached from the other direction. §11 is the running log. **Released as 3.1.0** (tag `v3.1.0`) | 3.1.0 |
| `LLM_FIRST_PLAN.md` *(new, 2026-09-12)* | ✅ **Complete — eight milestones, one release each, 3.1.0 → 3.8.0 (2026-09-14).** Successor audit to `LLM_FRIENDLY_PLAN`: 18 read-only lanes, 144 findings, 108 adversarially verified — **47 did not survive as written**, so every row is a claim to re-verify. **M0 and M1 shipped in 3.1.0; M2 (`Failures.md` tells the truth) in 3.2.0; M3 (you find out from the log that the run failed) in 3.3.0; M4 (the schema tells the truth about the file beside it) in 3.4.0** — A1–A9, Q1 and Q2 in full, including the two defects that only appeared when the digest was run against a real report (the `Thrown at` frame landed on xUnit v3's own source-linked assertion PDBs, and a `MessageQueue` send printed its payload in the Call column). **M5 (every address the tool prints, the tool accepts) in 3.5.0** — G1, G2, G4, G5, G6, G7, G8, G9, G10 and §3.1 in full. It found four things the rows did not say: the provenance banner corrupted `--count`; twelve of the thirteen `DiagnosticKind`s never reached it at all; six writes of a caller-supplied path existed and only two were guarded; and `CiSummary.md` was a live markdown breakout on captured text, on a surface rendered as HTML. The plan's "G2/G5/G6 are verify-don't-rebuild" was wrong on all three. **M6 (a merged report is a real run) in 3.6.0** — F1–F5 and B2 in full. The three worst defects were in none of the rows: `merge -o <a-shard>.json` destroyed the shard while printing that it had protected it; `merge` crashed on any shard that captured a database call; and two shards running different NUnit tests merged into one scenario, so a failing shard merged green (the survey's proposed fix would have deleted distinct scenarios run-wide — the verifier caught it). `kronikol merge` now writes the whole run tail (`Failures.md`, `CLAUDE.md`, the schema, the pointer, the CI debug section) through a new `MergedRunOutputs` rather than a `FinishRun` extraction, and `diff` tracks calls over matched scenarios and refuses one-sided or cross-suite `stableId`s with exit 2. **M7 (one question, one command) in 3.7.0** — I2, I3, I4 and A9's query half. `--describe` and `--version` come off one `VerbTable` that the CLI dispatches, validates and prints its help from (the four hand-kept verb lists are gone, and the drift guard compares against the table instead of regexing prose); `repro` reads the `dotnet test --filter` line out of the frame `Thrown at` picks, and every failure surface (`failures`, `Failures.md`, `Failures.jsonl`) carries it; I3 closed by round-trip tests rather than by changing addresses. **M8 (an agent that never heard of Kronikol finds it) in 3.8.0** — H1–H5, in-repo only: a Claude Code plugin manifest and marketplace held by an offline validation test (version pinned to `Directory.Build.props`), the discovery terms and agent tags on `Kronikol.Tool`, the old identity named where search reads (and the core package's NuGet title, still "Test Tracking Diagrams", fixed), and `dnx Kronikol.Tool` documented with its four measured costs. The outward steps — marketplace submission, `claude plugin validate`/`eval`, old-package deprecation, reserved prefix — are people-work, listed in §14.3. M4 found that `TestRunReport.yml` did not parse at all for any failing run, which the row it was working from did not say, and left one item explicitly undone: `TestRunReportFullStepDetail` is JSON-only. §16 is the implementation log | 3.1.0, 3.2.0, 3.3.0, 3.4.0, 3.5.0, 3.6.0, 3.7.0, 3.8.0 |
| `CROSS_RUN_HISTORY_PLAN.md` | ✅ **Done (2026-09-14, 3.9.0 → 3.11.0).** 3.9.0 shipped M0, M1, M2, M3 and M6: the ledger (`src/Kronikol/History/`), the verdicts, the digest/CTRF/pointer surfaces, `kronikol query history` and `kronikol history record|init|show|verify|prune|compact`; 3.10.0 (2026-09-14) shipped M4 and M7: `kronikol history gate` (trips on what the ledger says is new; flaky, duration and behaviour advisory below the minimum runs), `quarantine`, `rename` + `record --accept-renames`, `doctor` and `import` (Kronikol reports, `--from-ctrf`, `--from-allure`); 3.11.0 (2026-09-14) shipped M5 (history in `TestRunReport.html`: sparkline + verdict pill per scenario, a History section beside the timeline, `$flaky` in the search box, `merge --history`) and M8 (the dogfood on the orphan branch `kronikol-history` via `ci-summary-preview.yml`, the wiki, the Kronikol4J divergence entry). Three plan decisions moved under implementation and are recorded in the code and changelog: the roster key includes the suite; flaky needs two failing episodes, not two flips; a first failure with nothing to compare against is `unknown`, not `broke`. **Read §0.2 first:** `ctrf-io/github-test-reporter` already ships free artifact-relay cross-run flakiness, fail-rate and duration trends with PR comments, and a .NET CTRF emitter exists — so the *status* half of this plan is commodity and the plan now offers three options (recommendation: build the ledger but promote behaviour history ahead of the status surfaces, since the interaction layer, the history-aware gate with quarantine, and the repo-committed ledger are the only parts nobody gives away). **Measured, not assumed:** ledger 228 KB/50 runs, 16.0%% of stableIds collide across test projects, templater ladder 66.9%%→96.6%% over six runs whose ids are 98%% freshly minted, three live consecutive runs 100%% stable, analyzer 65 ms at 5000×50, and a concurrent append **silently loses 34-67%% of run lines** on Windows and Linux unless locked. **Fifteen of the plan’s own decisions were reversed by checking them — two of them on the day they were written — and they are all the same mistake** — an existence check allowed to stand in for a behaviour check; §17.0 names the class, and §17.1 is now split by *how far each check went* (RUN / READ / DOC) rather than by confidence. Latest round: `merge=union` in `.gitattributes` removes the committed ledger’s merge conflicts entirely **but silently corrupts a counter-keyed roster**, and `$flaky` would have evaluated false for exactly the flaky scenarios. **§0.2’s headline moved**: running `github-test-reporter`’s own arithmetic shows `flakyRate` measures **retry volume, not flakiness** — a test failing a third of the time scores 0 while a test that never fails but retries scores 0.67 — so the free chain reports zero flakiness forever for xUnit/ReqNRoll suites, and Kronikol’s honest claim is “flakiness detection at all for non-retrying suites”, not “better flakiness detection”. **Time-sensitive: add `RunAttempt` to `CiMetadata` before 3.1.0 is tagged** (audited: 8 edits, 5 files). **§2.3's finding SHIPPED at source (2026-09-12/13)**: `ScenarioStableId.Compute` now takes `suite` first, `RunSuite` resolves it, `ReportConfigurationOptions.SuiteName` overrides — so §3.2 no longer defines suite identity and the plan's `HistorySuiteName` was **deleted as a duplicate option**; new hazard recorded (a change in suite resolution re-keys every id at once and detaches history silently). **Re-synced 2026-09-12 pm with the in-flight LLM plan**: M2.4’s `--baseline` + `KRONIKOL_BASELINE` landed (§6.2 now follows that precedent, and new Q15 asks whether `--baseline` should read the ledger), and M2.8 added `Commands.cs` (one command table, `CommandTableTests`) plus `init-agents` writing a managed CLAUDE.md/AGENTS.md block from one canonical copy — so §8.3 now splits `query history` (read) from a top-level `history` command (writes) | — |
| `NOTE_COPY_FIDELITY_PLAN.md` *(2026-09-11)* | Done - executed in full, M1-M6 including the optional M5 that section 11 Q2 had recommended deferring. Every break the width budget writes into a note body is marked with an invisible `<U+200B>` and one shared rejoin undoes exactly those, so Copy box text, Open box text in new tab and Copy all caller request payloads hand back the payload whole; search rule 5b (a flush-left guess that welded flush-left payload lines together) is replaced by an exact pass 1b. Committed as a design record | 3.0.86 |
| `NOTE_WRAP_AND_WIDTH_PLAN.md` *(2026-09-11)* | ✅ Done — executed in full. Part A (non-JSON request bodies chunked at 80 chars, user-reported) shipped alone; Part B M2–M7 followed with the §2.9 recommendation adopted (monospace first, width second) and every §2.10 measurement folded in. §2.11's HTML-panel alternative stays deliberately not taken. Committed as a design record | 3.0.84, 3.0.85 |
| `DIAGRAM_WIDTH_PLAN.md` *(2026-09-04)* | ✅ Done — all nine ranked axes closed except #9 (`InsertPlantUml`, user-authored PlantUML, documented rather than fixed by design). The shared width-budget wrapper, the activity-diagram `wrapWidth`, the participant guard and the two formatter-bypassing note bodies all landed; the 4096 limit was re-measured as **raster-only** and the plan corrected. Activity-diagram **height** is recorded as the axis this work did not close | 3.0.83, 3.0.85 |
| `JAVA_PORT_PLAN.md` | 🟡 Partially done | Kronikol4J v0.1.24 |
| `NODE_PORT_PLAN.md` | ❌ Not started (design record) | — |
| `MONOREPO_MIGRATION_PLAN.md` | ❌ Not started (design record) | — |
| `QUERY_FALLBACK_PLAN.md` *(new, 2026-09-13, untracked)* | ❌ Investigation complete, nothing implemented, **NOT green-lit**. Replace the skill's `scripts/query.py` fallback with a .NET 10 file-based app (`dotnet run query.cs`) emitted beside the report, calling the real `QueryCommand.Run` — parity by construction rather than by maintenance. **Measured:** `#:package` resolves from the NuGet cache with every source cleared (offline), cold 2.9 s / warm 0.25 s, works inside `bin/Debug/<tfm>/Reports/`, and the cache holds every TFM asset so a **net8.0 test project still gets a working net10.0 fallback**. Needs SDK 10 (verified: fails under a `global.json` pin to 9). **Found a real bug on the way:** the `templates/` and `.claude/` copies of `query.py` have drifted — the 3.2.0 `failureCause` line is in one and not the other, and no test compares them (M0 fixes it independently). A9/A10 unverified: the engine compiling inside `Kronikol` under `#if NET10_0_OR_GREATER`, and byte-identical output (stdout encoding is the likely divergence) | — |
| `QUERY_PORTABILITY_PLAN.md` *(new, 2026-09-13, untracked)* | ❌ Investigation complete, nothing implemented, **NOT green-lit**. Ship `kronikol query` to Java and Node as a NativeAOT binary behind thin wrappers (esbuild pattern for npm, protoc-jar pattern for Maven behind the existing jbang alias) rather than reimplementing it. **TeaVM does not help** — it consumes JVM bytecode, not .NET IL; §5 costs and rejects the Java-canonical inversion despite this repo already running IKVM and consuming TeaVM output. **Measured:** the AOT/trim analyzers pass with **17 warning sites under exactly two codes**, all `JsonSerializer` without source-gen — no reflection anywhere in 6,568 lines. **A2 is the gate and is unverified**: no `PublishAot` was attempted (no MSVC linker on the investigating machine). §3.4 amended by `KRONIKOL4J_PORTABILITY_PLAN.md` | — |
| `KRONIKOL4J_PORTABILITY_PLAN.md` *(new, 2026-09-13, untracked)* | ❌ Investigation complete, nothing implemented, **NOT green-lit**. Whether a shared wasm renderer built from .NET can retire the Java port's rendering half. **Measured:** Kronikol4J is 32,100 lines splitting **45% rendering / 38% capture tail / 17% irreducible capture core**; of 13 divergence-ledger entries **7 created re-port work and 5 of those 7 are rendering detail**; the host side is healthier than the producer side (`node:wasi` needs **no flag** on Node 25.9.0, `wasmtime-py` works, `jco new --wasi-command` adapts P1→P2 so **mixing hosts is a packaging step, not a fork**). **Recommendation REVISED once Kronikol4J was understood as pre-1.0**: run E1 *before* 1.0 and let it choose the release architecture — adopting the shared renderer now *deletes* 14,305 lines, after 1.0 it *replaces a shipped renderer*. Two earlier claims withdrawn in place: the frozen-P1 warning (§4.2) and "the 55% is irreducibly Java" (header, §1.1–§1.2). **A9 is the gate: no WASI build was attempted.** §8.1 records the one design constraint — keep the module's syscall surface at P1's floor | — |
| `PLATFORM_FOUNDATIONS_PLAN.md` *(new, 2026-09-14, untracked)* | ❌ Investigation complete, nothing implemented, **NOT green-lit**. **The definitive plan for four platforms in one repo; where it contradicts another plan, it wins (§5 lists every supersession).** Architecture in one sentence: every platform is a capturer writing two NDJSON streams + an options file; one shared renderer built from .NET turns them into every report; one shared query engine reads it. Foundations F0–F9: F0 monorepo (adopts `MONOREPO_MIGRATION_PLAN` whole), **F1 .NET dogfoods its own boundary** — the missing `NdjsonTestRunWriter`, a `CaptureMode` option, and a byte-for-byte in-process-vs-ingest round-trip test that is the gate for everything, F2 the versioned contract + `kronikol validate`, **F3 raw facts in / interpretation inside** (moves the six pure-function classes Java duplicates — classifier, naming, casing, serialiser — renderer-side), F4 the options file (95 options vs 27 flags), F5 close what F1 finds (internal-flow spans are known: `IngestPipeline.cs:284`), F6 the shared renderer (E1/E2 against the post-F5 ingest pipeline; NativeAOT fallback), F7 query as a second entry point, F8 the corpus reshaped so the 94 render fixtures become .NET-internal and the 15 capture fixtures become the cross-language contract, F9 the platform template + `kronikol new-platform`. **Eleven measured gaps (G1–G11)**; **P10 (WASI can run ingest) is the gate and is unverified**; P8 (how many gaps F1 finds) is the honest unknown. Java 1.0 = subtraction, gated on F6 | — |
| `NEXT_LANGUAGE_PLAN.md` *(new, 2026-09-13, untracked)* | ❌ Investigation complete, nothing implemented, **NOT green-lit**. **Read §0 first:** written without reading `JAVA_PORT_PLAN`/`NODE_PORT_PLAN`/`MONOREPO_MIGRATION_PLAN`, and its §1 is a **rediscovery** of `NODE_PORT_PLAN` §3.11–§3.12, which states it better. Genuinely new: **the audience ranking** (§3 — Python best *after* Node, on pytest's near-monopoly: one adapter where Node needs six and .NET needed fourteen; Go deferred because it cannot be instrumented at runtime and eBPF shows *"generic DB operations rather than detailed statements"*; C/C++ and Rust excluded, §3.2) and the three-way split that corrected the Kronikol4J plan's header. **The correction that matters:** OTel **cannot** carry request/response bodies — semconv captures *sizes*, spec issues #857/#1219 open for years — and bodies are ~90% of a report, so OTel is a **shallow long-tail catch-all beside deep native adapters**, never a replacement for them. Open disagreement with a locked decision: whether *"full feature parity"* should stay the end-state commitment for the ports. **B7 (HTTP+SQL is the 80/20) has no evidence — falsifier named in §5**. §2.1 costs four platforms and corrects the obvious arithmetic: "17%" is a fraction of the wrong denominator (a scoped port is **~5,600 lines**, and `core` does not shrink when the renderer is shared), Node is materially above that (`NODE_PORT_PLAN`: no JDBC equivalent, no universal HTTP seam, Prisma exception, six adapters), and **a shared renderer takes *rendering* maintenance from N to 1 while *capture* maintenance stays at N** | — |
| `SMETANA_PARITY_PLAN.md` *(new, 2026-09-14, untracked)* | ❌ Plan complete, nothing implemented, **NOT green-lit**, nothing posted upstream. Upstream-facing (plantuml/plantuml): one umbrella issue plus nine PRs to bring Smetana level with the Graphviz path for everything PlantUML emits. `skinparam linetype ortho` needs a ~4,800-line hand port of Graphviz 2.38 `lib/ortho` (section 6, design study in the scratchpad); everything else is maker-side omission (nodesep/ranksep alone explain the #1703 density complaint, canvas ratio 0.79). 49 attribute + 31 port findings, 52-probe corpus with 2x2 montages, issue and PR drafts in Appendices A/B, seven open questions in section 12. | — |

---

## Fully done — DELETED 2026-08-30

The seven plans below were removed from the repo once verified complete; the full
plan texts live in git history (last present at commit `159aef5`,
`git show 159aef5:<name>` retrieves any of them). Their verification records are kept
here. Note that a few living documents still reference the deleted files by name
(QUERY_PERF_PLAN "builds on" QUERY_V2_PLAN; MONOREPO_MIGRATION_PLAN's `git mv` list;
Kronikol4J's parity ledger and CHANGELOG entries) — those references now resolve via
git history.

### BACKGROUND_STEPS_INLINE_PLAN.md (3.0.48, `af4cfde`)
Inline background steps, `SeparateBackgroundSteps` / `CollapseRepeatedStepKeywords`,
`InlineBackgroundSteps` deprecated, all five ride-along bug fixes, full unit + E2E
matrix, wiki + changelog. Verified complete 08-29; no movement needed since.

### LONG_LINE_SYNTAX_ERROR_PLAN.md → moved to Partially done below.

### REPORT_QUERY_PLAN.md → moved to Partially done below.

### QUERY_V2_PLAN.md (3.0.51–3.0.58)
All eight milestones (PathEngine, `values --stats`, `--where`, `diff`, `--group-by`,
`grep --number`, `trace`, `select` no-go) plus both ride-along fixes. The former
cosmetic gap is closed: `README.md:182/184/186/187` now show `trace`, `grep --number`,
`--group-by`, and `diff` (docs sweep `50d53a1`), and the matching `trace` line landed
in `nuget-readme.md` with 3.0.69.
**Part 7 addendum (per QUERY_PERF_PLAN §5, recorded here since the plan file is
deleted):** the Part 7 perf row was calibrated on a corpus 250× smaller than real
large reports (562 interactions / 0.53 MB of bodies vs the ~25% of users above 50 MB);
QUERY_PERF_PLAN re-measured on a ~140 MB corpus and fixed what it found (3.0.69). The
SQL query surface was considered and declined 2026-08-26 alongside the M8 `select`
no-go (bodies are stringified JSON in `content`; no budget/elision contract in SQL
results; worse errors; `grep --number` inexpressible). Sidecar cache (persisted
index) stays gated on evidence of routine 250 MB+ reports.

### BROWSER_RENDER_WORKER_PLAN.md (3.0.45 / 3.0.50)
Phases 0–7 complete; remaining items are all plan-labelled optional (`_svgCache`
retirement, stable fragment boundaries, `requestIdleCallback` chunking). The perf
guard was deflaked post-release twice: `087be03` (3-attempt retry, budgets unchanged;
no changelog entry) was itself outrun by a saturated Remainder runner (510–667 ms
WorstTaskMs on all 3 attempts, run 33305698464), so 3.0.69 made the absolute budgets
contention-scaled: a probe worker measures how stretched CPU time actually is during
each attempt and the budgets scale by that factor (`ContentionScale`, floor 1, cap 5;
arithmetic unit-pinned, probe liveness E2E-pinned).

### NOTE_YAML_TOGGLE_PLAN.md (3.0.59 + 3.0.61–63 + 3.0.66; fixes in 3.0.67/3.0.68)
**All five open items from the 08-29 audit are now closed and test-pinned:**
- Bulk JSON⇄YAML dropdowns at report + scenario level (`_setNoteFormat` /
  `_setScenarioNoteFormat`, `setAllNoteFormats`, lazy-container seeding via
  `_noteFormatPreference`/`_noteFormatDefault`), 10-fact `NoteFormatDropdownTests` E2E.
- `NotePayloadFormat` option + `kronikol ingest --note-format` (token-injected default).
- Copy-text fixed: YAML notes copy the displayed YAML; creole `~` escapes no longer
  reach the clipboard in either view (`YamlNoteCopyTextTests`, 5 facts).
- Assertion + database filter-survival tests exist (`NoteYamlToggleTests.cs:219/:244`).
- 3.0.67 fixed split-note button/toggle indexing across fragments
  (`computeFragmentNoteIndexing`); 3.0.68 fixed Firefox reload desync
  (`autocomplete="off"` on all report controls, real-Firefox E2E, wiki refresh
  contract documented).
Kronikol4J script sync remains **deferred by design** (plan permits it; divergence
documented in `Kronikol4J/README.md:25-49` and, for 3.0.66, ledger entry `fc2e4fc`).
Residual nits: no `IngestCommandTests` case for `--note-format` parsing; the plan's
own header stops at 3.0.66 (does not mention the 3.0.67/68 fixes).

### OTLP_EXPORT_PLAN.md (3.0.60, `cc0f4ea`)
M1–M7 complete. Deferred-by-design items unchanged and still absent (protobuf encoder,
header allow-list, `parentSpanId` inference) — `src/Kronikol.Extensions.Otlp/` has had
zero commits since the audit.

### EXAMPLES_BLOCKS_PLAN.md (3.0.64, `ef9b370` + `0f5bb0e`)
Implemented in full by a parallel session, verified item-by-item on 08-30:
- `Scenario` block fields; `stableId` inputs deliberately unchanged.
- Band rendering in flat + grouped tables (`BuildExamplesBlockBands` /
  `AppendExamplesBlockBand`), correct activation rule with a byte-equality test
  against the nulled baseline, inert band rows, counts vocabulary, HTML encoding,
  continuous row numbering. CSS shipped light-only (accurate deviation: the stylesheet
  has no dark theme to extend).
- The ordering interleave bug fixed in BOTH sites (`ParameterGrouper.cs:93-94` and
  `ScenarioInfoEnumerableExtensions.cs:36`) with regression tests.
- All four capture sources: Cucumber messages (`ExampleRow` block fields,
  `CucumberExamples.Description`), ReqNRoll live capture via guarded reflection
  (`ExamplesBlockResolver` with pickle-route + value-match fallbacks, drift test for
  the internal Reqnroll members), generic NDJSON `start` fields, merge round-trip.
- 11-bullet unit test file, 6-fact Playwright E2E (runs in the CI E2E Remainder job),
  multi-block `Muffins.feature`, wiki (3 pages), changelog, tag. Plan header updated
  with an accurate deviations list.
**One missing plan deliverable (M6.3):** no Kronikol4J parity-ledger entry for the
3.0.64 band rendering — see the ledger-gap item under Cross-cutting below.
Also noted (not a deliverable): the vacuous legacy `Assert.Contains("examples-table")`
assertions the plan flagged were left as-is.

### REQNROLL_DUPLICATE_STEPS_PLAN.md (3.0.64, issue #71)
Implemented in full by a parallel session, verified on 08-30: `OwnerHooksKey` +
`IsOwner` (`ReferenceEquals`) guard on `BeforeStep`/`AfterStep`/`AfterScenario`,
ownership recorded in `BeforeScenario`, `DistinctBy` kept as defense-in-depth;
`Kronikol.ReqNRoll.Core` removed from all three templates' `reqnroll.json`; stale
package pins gone (templates now pin 3.0.67); the example projects deliberately keep
the double-scan config as a permanent regression harness, documented in the doc-comment
of the new `ReqNRollDuplicateStepsTests` (adjacent-duplicate scan, plain + Background +
Scenario Outline coverage, new `CakeQuality.feature`); changelog references #71; wiki
troubleshooting entries in all three ReqNRoll guides. The plan's optional unit test was
skipped per the plan's own gate (Reqnroll 3.3.4's `ScenarioContext` ctor is internal).

---

## In progress / partially done

### TEOZ_PERF_PLAN.md (new; upstream-facing)
PlantUML 1.2026.7 dropped the fast Puma sequence engine leaving only Teoz; this plan
packages fixes for the upstream maintainer rather than shipping in Kronikol. Status:
**every in-house workstream is executed and delivered upstream; the remainder is gated
on the maintainer.**
- Done: corpus + shape generators, inclusive-time and allocation profilers,
  Puma-vs-Teoz ratio matrix, five patches with SVG-SHA-256 identity proofs —
  upstream PRs **plantuml/plantuml #2835–#2839** (LiveBoxes O(1) lookup, tile memos,
  SVG number formatting, getThickness cache), consolidated evidence post on **#2834**,
  TeaVM issue **konsoletyper/teavm#1247** (`getEnumConstants` rebuild),
  `OptimizationLevel.AGGRESSIVE` trial (negative result recorded).
- Open items in-plan: W0.4 Real-solver convergence probe (no evidence produced);
  speedscope export (optional).
- Upstream outcome: **all five patch PRs #2835–#2839 MERGED 2026-08-30/31** (silently,
  as usual — no comments).
- Kronikol-side follow-ups **SHIPPED 2026-09-04 (3.0.76)** without waiting for npm
  (latest there is still slow 1.2026.7): `TrackingDefaults.PlantUmlJsCdnBase` now pins
  fork tag `@v1.2026.8beta1-0e4f452` — a **stock** `npmPackage` build of master
  `0e4f452e` (all merged perf work incl. #2858–#2860, themes, background, Smetana
  fallback) — and every ES-module render call site passes `{ maxSvgSize: 98304 }`, so
  the 98304 patch is retired. Measured on Kronikol shapes with the teoz pragma: 4–8×
  faster warm (puml-19 789→181 ms, gen-500 5.8→0.8 s). viz-global.js kept (user
  decision); statement-limit constants re-measured, all unchanged. A move to npm
  `@plantuml/core` can follow when 1.2026.8 final publishes.
- Working artifacts are the untracked `tools/render-bench/core-head-*.js`,
  `core-bisect*.js`, `alloc-real.js`, `patches/`, `results/` files.

### PERF_CI_PLAN.md (new; upstream-facing, companion to TEOZ_PERF)
A non-blocking perf workflow contributed to plantuml/plantuml so Teoz performance
cannot drift unnoticed. **R1–R4 done, R5 blocked:**
- Done: `perf-bench/` harness in the `lemonlion/plantuml` fork (bench.js, generators,
  13 checked-in corpus fixtures, runner-calibrated `expected-bands.json` from 4 A/A
  runs, pinned reference via git commit because npm ≤1.2026.7 throws on large
  diagrams), workflow with dispatch inputs + three compare modes + step summary +
  1-day artifacts, push-to-master trigger, 7 successful validation runs including a
  different-day recheck, PR **plantuml/plantuml#2840** + announcement on #2834.
- Minor deviation: artifact name is static `perf-bench-results`, not `perf-bench-<sha>`.
- Blocked: R5 (post-merge seeding dispatches) until #2840 merges.
- By design nothing lands in this repo; commit `087be03` (Kronikol's own perf-guard
  retry) is independent of this plan.

### QUERY_PERF_PLAN.md (3.0.69) — ✅ Done
Executed in full 2026-08-30 (the §3.4 gate resolved by explicit request for the whole
plan). All four fixes landed TDD-first with the plan's deterministic observables:
- §3.1 `TieredCompilationQuickJitForLoops=false` in `Kronikol.Tool.csproj`, guarded by
  `Tool_runtimeconfig_pins_optimized_jit_for_loops`.
- §3.2 `BodyCache` owns one `FileStream` (opened via `PayloadReader.Open`, closed on
  dispose); `PayloadReader.Read` stream overload; `ReportIndex.PayloadOpens` counter.
- §3.3 `Grep`/`NumberGrep` route bodies through `cache.Raw`/`cache.Json` and diagrams
  through `cache.ReadSlice` — opens-per-command pinned to 1 by three red tests.
- §3.4 walker diet: interned property names (span AlternateLookup against the closed
  name set), fixed-arity `At`, null path segments for array indices; entire suite as
  the byte-identical pin.
- Measured same-session on the 142.9 MB corpus (record in `tools/query-bench/README.md`):
  `summary` 1.39→1.19 s, `values` 2.89→1.95 s, `grep --number` 4.53→2.64 s; scan at
  its re-tokenization floor after §3.4.
- The audit's corpus hazard is closed: `tools/query-bench/TestRunReport*.json` is
  gitignored; harness + README/protocol committed.

### LONG_LINE_SYNTAX_ERROR_PLAN.md (~95%, shipped 3.0.48)
Unchanged since the audit. Both fix layers, all tests, docs, and the IKVM verification
are done. Open: the **Kronikol4J mirror** — `PlantUmlCreator.java` still has zero
statement caps, so long-URL traces lose the whole diagram on the Java side while .NET
truncates and renders. The divergence is now ledgered
(`Kronikol4J/docs/REMAINING_PARITY.md:1879`, commit `e740422`) but not implemented.
Also open: the §6 Q2 component-diagram limit probe (a defensive ceiling was applied
instead; the component parser's limits remain unmeasured).

### REPORT_QUERY_PLAN.md (~99%, shipped 3.0.47; the tail closed in 3.1.0)
Three of the five open items closed by LLM_FRIENDLY_PLAN M0 and M2.9:
- ~~`--json` machine-readable output (§3.1 principle 5)~~ — shipped 3.1.0 on the seven
  listing verbs, with the two-notes-per-token cost the principle warns about stated in
  the docs and the skill.
- ~~The dead `--raw` flag~~ — removed in 3.1.0 (M0).
- ~~The >100 MB streaming-path test (§3.6)~~ — `QueryStreamingTests`, 3.1.0. Written as a
  test of allocation rather than of scale, and it immediately found the scanner
  materialising a UTF-16 copy of every payload: 324 MB for a 142 MB file, now 32 MB.
- Note-divergence detection (§3.4) — still a blanket caveat footer, no reconciliation.
  **Stays deferred**: no agent-reported need (LLM_FRIENDLY_PLAN §2.4).
- Golden output tests remain assertion-based, not snapshots.

### JAVA_PORT_PLAN.md
No code movement — Kronikol4J's last Java-source commit is 2026-08-23; its six most
recent commits are all divergence-ledger docs. Still outstanding: Appendix C in its
entirety (0%; `OTLP_TAP_PLAN.md` exists there but is untracked and unstarted),
cross-runtime parity CI, the wiki at 17 pages, `Clock` seam, context modules, GraalJS
search-test reuse, Playwright suite port, Java BreakfastProvider demo, .NET-side
parity-hardening items. The asset divergence keeps widening (now through 3.0.68).

---

## Not started (kept as design records — do not delete casually)

### NODE_PORT_PLAN.md
Unchanged: "Design phase complete; no Kronikol.js code written yet" remains accurate.
No `js/`, no `package.json`, no npm packages.

### MONOREPO_MIGRATION_PLAN.md
Unchanged: no phase executed (no `dotnet/`/`java/`/`parity/` dirs, tags unprefixed,
the 12 cross-repo ProjectReferences intact, both wikis separate). Its motivating
problem is still live and growing — see the ledger-gap item below.

---

## Cross-cutting follow-ups (actionable)

1. **CHANGELOG has no `[3.0.66]` section.** The string does not appear in the file at
   all; the 3.0.66 content (bulk YAML dropdowns, `NotePayloadFormat`) sits under
   `[3.0.67]`. The tag `v3.0.66` exists. Release owner should decide whether to split
   the entry or leave a note.
2. **Kronikol4J divergence-ledger gaps.** Entries exist for 3.0.48/3.0.60/3.0.62/3.0.66
   only. Missing: **3.0.63** (leading-newline block scalars), **3.0.64
   examples-blocks** (an explicit plan deliverable, M6.3), **3.0.67** (split-note
   indexing — directly affects the toggle scripts the port must eventually take), and
   **3.0.68** (autocomplete refresh contract).
3. **Commit `087be03`** (perf-guard 3-attempt retry) has no changelog entry.
4. **`nuget-readme.md`** — the `trace` line is sitting uncommitted in the working tree.
5. **`--note-format`** has no CLI parse/validation test in `IngestCommandTests`.
6. **`--raw`** decision still pending (delete vs implement; deleting wants a test).
7. **query-bench corpus gitignore** — add `tools/query-bench/TestRunReport.query-bench.json`
   (or `tools/query-bench/*.json`) to `.gitignore` before the harness is committed.
8. **Wiki anchor normalisation** — 93 space-form `[[Page#Heading With Spaces]]` anchors
   across 41 pages remain (vs 40 slug-form); verify GitHub resolves them before or
   instead of a bulk sweep.
9. **Vacuous legacy assertions** — `ExamplesTableReportTests.cs` still asserts
   `Contains("examples-table")` / `("examples-detail-row")`, strings the generator
   never emits (satisfied by inlined CSS/JS resources).

## Documentation audit (2026-08-29) — closing summary

The full docs audit and its fix pass are recorded in git history (`2270445` and the
docs-sweep commit `50d53a1` + wiki `5a5d770`). All identified gaps were fixed on
08-29 across ~20 wiki pages, README, nuget-readme, and three doc-bearing code strings;
the wiki has since gained 3.0.67/3.0.68 coverage (refresh contract, dropdown docs) via
the feature sessions. Still open from that audit: items 6 and 8 above, plus the
Kronikol4J wiki coverage gap (17 pages, unchanged, tracked under `JAVA_PORT_PLAN.md`).
