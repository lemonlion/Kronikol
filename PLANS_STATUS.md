# Plan File Status Audit

**Date:** 2026-08-29 · **Repo version at audit:** 3.0.63 (bumped to 3.0.64 during the session)

> **UPDATE (2026-08-29, later the same day):** two "Not done" verdicts below are now
> stale. Parallel sessions implemented `EXAMPLES_BLOCKS_PLAN.md` and
> `REQNROLL_DUPLICATE_STEPS_PLAN.md` (issue #71) after this audit ran; both shipped in
> the 3.0.64 release commit alongside the CI-coverage overhaul, with the full suite
> reported green. The audit text below describes the state at audit time and is kept
> as the record; the examples-blocks Java divergence is logged in Kronikol4J's parity
> backlog.

Item-by-item verification of all 13 `*_PLAN.md` files in the repo root against the
actual code, tests, changelog, git history, and the wiki (`../Kronikol.wiki`,
`../Kronikol4J` where relevant).

| Plan | Status | Shipped in |
|---|---|---|
| `BACKGROUND_STEPS_INLINE_PLAN.md` | ✅ Fully done | 3.0.48 |
| `OTLP_EXPORT_PLAN.md` | ✅ Fully done | 3.0.60 |
| `QUERY_V2_PLAN.md` | ✅ Fully done (1 cosmetic gap) | 3.0.51–3.0.58 |
| `BROWSER_RENDER_WORKER_PLAN.md` | ✅ Fully done | 3.0.45 / 3.0.50 |
| `LONG_LINE_SYNTAX_ERROR_PLAN.md` | 🟡 Partially done (~95%) | 3.0.48 |
| `NOTE_YAML_TOGGLE_PLAN.md` | ✅ Fully done (follow-ups shipped) | 3.0.59 + 3.0.61–63 + 3.0.66 |
| `REPORT_QUERY_PLAN.md` | 🟡 Partially done (~95%) | 3.0.47 |
| `JAVA_PORT_PLAN.md` | 🟡 Partially done | Kronikol4J v0.1.24 |
| `EXAMPLES_BLOCKS_PLAN.md` | ❌ Not done | — |
| `QUERY_PERF_PLAN.md` | ❌ Not done | — |
| `REQNROLL_DUPLICATE_STEPS_PLAN.md` | ❌ Not done | — |
| `NODE_PORT_PLAN.md` | ❌ Not done (design-only) | — |
| `MONOREPO_MIGRATION_PLAN.md` | ❌ Not done | — |

---

## Fully done — ready for deletion

### BACKGROUND_STEPS_INLINE_PLAN.md

Shipped completely in **v3.0.48** (commit `af4cfde`, tag `v3.0.48`). Every step verified:

- `StepKeywordCollapser` (`src/Kronikol/Reports/StepKeywordCollapser.cs`) with the full
  planned test file, including casing, ButWhen, unrecognised-keyword-reset, and
  input-not-mutated cases.
- Shared step-section renderer (`ReportGenerator.RenderScenarioStepSections`) used by both
  the normal-scenario and parameterized-detail-panel call sites.
- Both new options — `SeparateBackgroundSteps` and `CollapseRepeatedStepKeywords`
  (default true) — plus `InlineBackgroundSteps` marked `[Obsolete]` with the exact
  planned message.
- All five ride-along bug fixes: background text in the search index, continuous step
  numbering, `hasAnyDetail` for all-background scenarios, Features Summary step counts,
  and `BackgroundSteps` in the Specifications YAML/JSON/XML writers.
- CSS (`.step-background` variants, old `.scenario-background` rules retained for
  separated mode).
- Full unit + E2E matrix, including the §1.2 shared-array render-race regression test
  (`Rendering_the_html_does_not_leak_a_collapsed_keyword_into_the_model`), the new
  `bg4` Given/Given-seam E2E fixture, `BackgroundRenderingTests` +
  `SeparatedBackgroundRenderingTests`, all following the Playwright house rules.
- Wiki (`Report-Configuration.md`, `Step-Tracking.md`, `Generated-Reports.md`,
  `API-Reference.md`) and `CHANGELOG.md:104-124`.
- Correctly excluded per the plan: Phase B localized conjunctions; Kronikol4J follow-up.

### OTLP_EXPORT_PLAN.md

All committed scope (M1–M7) shipped in **v3.0.60** (single commit `cc0f4ea`):

- **M1 mapper** — `OtlpSpanMapper` with D4 identity (real W3C ids win), `PerTest`
  trace-grouping default, null-timestamp borrowing + `kronikol.times.synthetic`, echo
  suppression (exceeds plan: also suppresses `MergedSource`), full attribute mapping
  table, error statuses, the 15-category → `db.system.name` reverse map, opt-in bodies
  with cap + truncation marker, `OtlpExportOptions` with `Validate()`.
- **M2 encoder** — hand-written dependency-free `OtlpJsonEncoder`, decode-back oracle
  tests via `OtlpTraceReader`, byte-stable goldens.
- **M3 batch exporter** — paging, gzip, header auth, one-retry-then-count, full-circle
  test against a live `OtlpTap`.
- **M4 CLI** — `kronikol export` with every planned flag; the plan's flagged
  unredacted-CLI-path gap closed (`CaptureRedaction` applied, default on).
- **M5 streaming sink** — `OtlpExportSink` with bounded channel, TTL orphaning,
  `Diagnostics()`, D3 never-block tests.
- **M6/M7 docs + release** — new wiki page `Exporting-to-OpenTelemetry.md` with all
  required sections, sidebar/README/nuget-readme, D3/D4 definitions written down,
  Kronikol4J divergence ledgered in its `REMAINING_PARITY.md`.

Not built, **by the plan's own design** (demand-driven, not deliverables): M8 protobuf
encoder, header export behind an allow-list, `parentSpanId` inference, Java-side export.

Quirk: this is the only plan in the repo with **no Status header** — the file alone gives
no signal that it shipped.

### QUERY_V2_PLAN.md

All eight milestones landed across **v3.0.51–3.0.58** (per-milestone commits + tags):

- M1 shared infra: `PathEngine` (`[*]`, `['a.b']`, `.length()`, Levenshtein near-miss),
  `AllInteractions` + `BodyCache`, exact `requestResponseId` pairing (red-first), single
  `IsError` classifier, request/response targeting convention, all flags in
  `RerunPrefix()`.
- M2 `values --stats` · M3 `--where` + run-scoped `interactions` · M4 body `diff` +
  cross-run `--body` + compare pointer (with the ≥60% array-shift collapse guard) ·
  M5 `--group-by` (exactly the nine planned dimensions) · M6 `grep --number --tolerance`
  (incl. European decimal comma) · M7 `trace` (file-order fallback, leakage warning) ·
  M8 `select` resolved as a **documented no-go** (recorded in the plan and 3.0.58
  changelog), which the plan's gate permits.
- Every planned test name present verbatim; wide-run perf guards; skill
  (`kronikol-test-debugging`) and all 13 wiki pages verified; per-milestone changelog
  entries each stating "Kronikol4J: explicitly none".

**One cosmetic gap:** the `trace` verb never reached the README query snippet
(`README.md:167-175`) or `nuget-readme.md` — `values` did. One-line fix.

### BROWSER_RENDER_WORKER_PLAN.md

Phases 0–7 all landed (**v3.0.45** workers, **v3.0.50** engine re-base):

- Worker host (`plantuml-worker-host.js`: mock DOM + SVG serializer) with the full unit
  suite; main-thread shim (`plantumlLoad` no-op, `window.plantuml.render`/`prefetch`,
  telemetry, main-thread fallback); Blob-inlined engine for `file://`.
- Parallel dispatch with staggered worker start; byte-bounded LRU SVG cache; prefetch
  hooks in both note-toggle loops.
- Three `ReportConfigurationOptions` (`BrowserRenderWorkers`,
  `BrowserRenderCacheMegabytes`, `BrowserFragmentMaxHeight`) + `kronikol ingest` flags,
  plumbed to all three generators, with value-baking tests.
- Phase 6: Node renderer `--batch` NDJSON mode with per-line error isolation + V8 code
  cache + versioned engine cache dir; used by `DefaultDiagramsFetcher`.
- Phase 7: CDN re-based to `@v1.2026.6-patched`, ESM tail rewrite in all three consumers.
- Wiki (`PlantUML-Browser-Rendering.md` "How Rendering Runs" + results table,
  `Report-Configuration.md`), changelog, tags.

Deliberate divergences (documented in-code, not gaps): perf budgets shipped looser than
the plan's numbers (ReadyMs < 4500 vs 300 ms; WorstTaskMs < 500 vs 200 ms) due to CI
contention. Remaining items are all plan-labelled optional: dropping `_svgCache` in
favour of the shim cache, stable fragment boundaries on toggle, `requestIdleCallback`
chunking. Kronikol4J divergence documented in its README, as the plan allowed.

---

## Partially done

### LONG_LINE_SYNTAX_ERROR_PLAN.md (~95%, shipped v3.0.48)

**Done:** both fix layers (`PlantUmlStatementLimits` label caps counting the real
prefix/wrapper/suffix + full path moved into the request note; `PlantUmlStatementGuard`
backstop classifying messages/blocks/notes/comments), all §4.3 emitters (user action,
response, loop), the §4.4 browser failure diagnosis (`findOverLongStatement`), all seven
§5.1 unit tests, §5.2 real-engine boundary pins, §5.3 corpus invariant, §5.4 Playwright
E2E (CI-wired), the IKVM cross-engine answer to Q1 (2000-char limit is PlantUML's own;
block/coloured-bar limits are TeaVM artifacts), changelog, and wiki. Plus a
beyond-plan discovery: coloured `hnote across` bars past ~1458 chars crash the JS engine
(`RangeError`), fixed with a fourth `ColouredNoteBar` kind capped at 1400.

**Missing:**

1. **Kronikol4J mirror (§7).** No statement-limits equivalent exists in
   `kronikol4j-diagram`, and — unlike the 3.0.60/3.0.62 convention — the divergence was
   **never logged** in Kronikol4J's `docs/REMAINING_PARITY.md` ledger. The two
   implementations silently diverge on long-URL traces today.
2. **Component-diagram limit probe (§6 Q2).** Never measured; a defensive ceiling at the
   sequence-diagram limit was applied instead (`ComponentDiagramGenerator.cs:208`), and
   the changelog states the component parser's limits remain unmeasured.

(The configurable cap was "only if a user asks" — correctly not built.)

### NOTE_YAML_TOGGLE_PLAN.md (v1 shipped 3.0.59; addenda 3.0.61/62/63)

**Done:** the full v1 design — reconstructor (gray-header drop, focus-markup strip,
creole reversal, wrap-break rejoin, `JSON.parse` gate, lazy + cached), token-level YAML
emitter (int64/duplicate-key/key-order fidelity, block scalars with first-non-empty-line
indicator anchoring, quoted fallback, `\uXXXX` decode), conservative re-escape that never
wraps block-scalar content, `_noteFormats` state swapped in before collapse/truncate,
hover button (hidden while collapsed, `Y`⇄`J`), `window._noteFormatInternals` test
fixture, the large internals + E2E test matrix, wiki section, README mention, changelog
entries and tags for all four versions. Three addenda also done: uniform-CRLF block
scalars (3.0.61), backslash-doubling removal end-to-end (3.0.62), leading-newline
unfolding (3.0.63).

**UPDATE (2026-08-29, 3.0.66): items 1, 2 and 4 below are closed.** Item 1 was resolved
as JSON/YAML toolbar `<select>` dropdowns at report + scenario level instead of a
context-menu entry (user decision: the per-note button + dropdowns cover its use cases);
item 2 shipped as `ReportConfigurationOptions.NotePayloadFormat` + `kronikol ingest
--note-format`, honoured by lazy containers via `_noteFormatPreference` /
`window._noteFormatDefault` in `_preProcessSource`; item 4's assertion/database filter
survival tests were added (green — the shared pipeline already preserved the state) and
the copy-text path exercised on YAML notes exposed **two real bugs, both fixed in
3.0.66**: "Copy box text" returned the original JSON (user-reported), and creole `~`
escapes leaked from the truncated splice — plus the same `~` leak in the plain JSON view
(`YamlNoteCopyTextTests`, `unescapeNoteDisplayLine`). Item 3 (Kronikol4J sync) remains
deferred by design; `collapsible-notes-script.js` and `context-menu-script.js` are now
further ahead of the port's pinned assets.

**Known-uncovered (deliberately deferred, so nothing implies full coverage):** YAML-state
survival is not separately tested against the search filter (`FillSearchBar`), the
report-level Details radio (expand/collapse/truncate), or the report-level filter
toggles — all route through the same `applyNoteFormats` pipeline the covered paths pin,
and the dropdown tests partially cover report-level paths; there is also no multi-note ×
multi-scenario format-independence matrix.

**Missing (original audit, kept as record):**

1. **Bulk per-diagram "Show payloads as YAML" context-menu entry** — the plan's own
   status header says it remains open; nothing YAML-related exists in
   `context-menu-script.js` / `DiagramContextMenu.cs`.
2. **Report-level default-format option** ("start notes in YAML") — no such member on
   `ReportConfigurationOptions`.
3. **Kronikol4J script sync** — deferred by design; the port ships pre-3.0.45 assets and
   documents the divergence in its README (the fallback the plan permits).
4. **Test coverage gaps:** no test that the toggle survives the **assertion** and
   **database** filters (only *steps* is tested, though the wiki claims all three), and
   the **SVG copy-text path is never exercised on a YAML note** (only a proxy assertion
   that block lines are separate `<text>` elements).

### REPORT_QUERY_PLAN.md (~95%, shipped v3.0.47)

**Done:** all of work item A (new interaction fields across JSON/XML/YAML/XSD/schema,
failure detail on steps/assertions, full step detail default-on, `annotations` +
`DiagramMarkerKind`, `stepPath` attribution with the mismatch-diagnostic safety valve,
both audit defects — `stableId` collision and dropped step error — plus `DurationMs`
round-trip); all 16 query verbs with stable addressing, byte budgets, `--count`/`--out`,
streaming `ReportScanner`, unenriched-report header line; the
`kronikol-test-debugging` skill in its planned layout; the 13-page wiki sweep + new
`Querying-Reports.md`; Kronikol4J data-format parity; single-release 3.0.47 with tag.

**Missing:**

1. **`--json` machine-readable output (§3.1 principle 5)** — the query tool is text-only
   everywhere; no such flag in `QueryOptions`.
2. **§2.6 run-level extras** — `ciMetadata` still only reaches the HTML path and the
   mergeable JSON, not `GenerateTestRunReportJson`; no internal-flow summary.
   (Plan-labelled optional/Phase 6.)
3. **Note-divergence detection (§3.4)** — replaced by an unconditional caveat footer
   instead of reconciling note payloads against captured content. Similarly, the
   unenriched-report fallback *declares* the data loss rather than reconstructing step
   boundaries from the diagram as the plan promised (changelog is honest about this).
4. **>100 MB streaming-path test (§3.6)** — largest query fixture is ~400 KB /
   300 interactions, three orders of magnitude short. This is exactly the gap
   `QUERY_PERF_PLAN.md` was later written to close.
5. **Golden output tests** are assertion-based (`Assert.Contains` on phrases), not
   snapshots — silent drift in unasserted output parts would go unnoticed.

### JAVA_PORT_PLAN.md (main body largely done; Appendix C at 0%)

**Done:** Kronikol4J exists (36 Gradle modules, ~330 main / ~180 test files,
`build-logic` conventions, JDK 17/21/25 CI, Maven Central releases through `v0.1.24`).
Phases 1–4 core/diagram/report modules are golden-proven byte-identical on the output
surface; Phase 5+ breadth is broad (Spring starter/servlet, TestNG/Cucumber/Spock/JUnit5
adapters, messaging, Redis, AWS/Azure/GCP, NoSQL, gRPC, OTel, Hibernate, Gradle+Maven
plugins, CLI merge + distribution, and Assertion Tier 2 agents which the plan gated on
demand). 109 golden fixtures; `Automatic-Module-Name` on every module; templates,
CLAUDE.md, CHANGELOG, tag-driven release automation.

**Missing / outstanding:**

1. **Appendix C in its entirety (0%)** — every symbol greps to zero Java files: proxy tap,
   TCP tap (RESP/OP_MSG), NDJSON ingest + `InteractionRecord` + `FeatureSynthesizer`,
   Playwright adapter (`TestTrackingIdentity`), capture-time redaction, OTLP tap
   (`OtlpTap` — only an untracked `docs/OTLP_TAP_PLAN.md` exists), wire/span
   `InteractionMerger`, the C.5 core options (`CollapseConsecutiveIdenticalCalls`,
   `MaxArrowsPerDiagram`, `DependencyCategories.AI`, …), and the 3.0.45 ingest events.
2. **Cross-runtime parity CI (§10/§15)** — Java CI runs only `./gradlew build`; fixtures
   are hand-committed and drift-prone. Two divergences are already ledgered (OTLP export
   3.0.60, backslash escaping 3.0.62) and one is not ledgered at all (statement limits
   3.0.48).
3. **Wiki at ~17%** — 17 pages vs the 99-page .NET wiki; the "one page lands with each
   extension module" definition of done is not honoured (36 modules).
4. Smaller gaps: Java `Clock` determinism seam; `kronikol4j-context-java21` /
   `kronikol4j-context-agent` modules (propagation tiers 3–5 absent entirely); GraalJS
   reuse of the ~143 search-engine tests (no Graal dependency, no search tests);
   Playwright suite port (one smoke spec vs the planned ~516 methods); Java
   BreakfastProvider demo; explicit `maxParallelForks > 1` fork-aggregation test;
   .NET-side prep items — `IdGenerator`/`Clock` seam in .NET (worked around via fixed
   GUIDs in the capture harness) and three parity-hardening items
   (`Environment.NewLine` ×7 in `PlantUmlCreator.cs`, culture-sensitive `Titleize`,
   culture-sensitive header sort at `PlantUmlCreator.cs:829`).
5. Deviation (documented, accepted): Maven group is `io.github.lemonlion`, not the plan's
   `io.kronikol` (Java packages remain `io.kronikol.*`).

---

## Not done

### EXAMPLES_BLOCKS_PLAN.md

Zero implementation. Named Gherkin `Examples:` block capture/rendering: no
`ExamplesBlock*` symbol anywhere in `src/` or `tests/`, `Scenario` model unchanged, no
band-row markup/CSS, no ingest changes (Cucumber messages, ReqNRoll reflection, generic
JSON, merge), no E2E, no changelog/wiki entries. The alphabetical-ordering interleave bug
the plan flags as mandatory (`ScenarioInfoEnumerableExtensions.cs:33-35`) is still live,
and the weak legacy assertions it wanted strengthened are unchanged. File is untracked.

### QUERY_PERF_PLAN.md

Zero implementation; its own status line ("planned. Not started.") is accurate.
`PayloadReader.Read` still does `File.OpenRead` per body; no `BodyCache` stream
ownership, no `ReportIndex.PayloadOpens` counter, grep/number-grep loops still bypass
`BodyCache` at the exact lines the plan cites, no
`TieredCompilationQuickJitForLoops` property, none of the five red tests, no
`tools/query-bench/` harness, no changelog/release. File is untracked.

### REQNROLL_DUPLICATE_STEPS_PLAN.md

Zero implementation; header still reads "Status: NOT STARTED". The owner-instance guard
(`OwnerHooksKey` / `IsOwner`) does not exist; `BeforeStep`/`AfterStep`/`AfterScenario` in
`ReqNRollTrackingHooks.cs` still double-execute under double binding-assembly scan; all
three ReqNRoll templates still list `Kronikol.ReqNRoll.Core` in `reqnroll.json`; the
stale `2.29.17-beta` pins remain in twelve templates; no
`ReqNRollDuplicateStepsTests.cs`, no `ExtractScenarioStepsAsync` helper, no
changelog/wiki troubleshooting entries. File is untracked.

Stale details in the plan itself: it assumed shipping as 3.0.61 (3.0.61–63 have since
shipped for other work), and the TUnit example project is now in the CI matrix via the
uncommitted `ci.yml` change, partially addressing its coverage-gap note. Also, the TUnit
example's `reqnroll.json` lists only one assembly, contrary to the plan's
"keep both in all three" regression-harness intent.

### NODE_PORT_PLAN.md

Design-only, as its own §0 states ("no repo, nothing built"). No `js/` subtree, no
`package.json` anywhere in the repo, no pnpm/Turborepo/changesets/tsup, no `@kronikol/*`
packages, no `js-ci.yml`, no `js-v*` tags, no `parity/` dir, no Kronikol.js wiki section.
The only completed .NET prep item (asset externalization) was delivered for the Java
port; the determinism seam and most parity-hardening items are still missing on the .NET
side. Only trace of activity: a doc-edit commit re-targeting the plan at the `js/`
subtree.

**Keep-note:** 1,251 lines of locked design decisions — deleting loses the design record,
not just a todo list.

### MONOREPO_MIGRATION_PLAN.md

Not a single phase executed. No `dotnet/`/`java/`/`parity/`/`docs/` dirs; both repos
still tag unprefixed `v*`; the 12 cross-repo `ProjectReference`s in
`Kronikol4J/parity-harness/dotnet-capture/Capture.csproj` — the plan's stated raison
d'être — are unchanged; no `parity.yml` (Java CI still can't regenerate goldens, the
exact failure mode the plan targets); both wikis separate; `setup-action` branches still
exist locally and on origin; CLAUDE.md still carries the single-version rule the plan
wanted rewritten; even the quiet-working-tree precondition isn't met.

**Keep-note:** like the Node plan, this is a design record whose problem (golden-fixture
drift between repos) is still live.

---

## Cross-cutting observations

1. **The three not-done infrastructure plans are interlocked and the missing link is
   load-bearing.** `JAVA_PORT_PLAN` §10's cross-runtime parity CI cannot be built while
   the repos are split; `MONOREPO_MIGRATION_PLAN` exists precisely to fix that; and
   `NODE_PORT_PLAN`'s locked repository-placement decisions depend on the monorepo
   layout. Meanwhile .NET has moved to 3.0.63 with at least three known divergences
   (OTLP export, backslash escaping, statement limits — the last un-ledgered) that no
   automated check would catch.
2. **Deletion candidates:** the four fully-done plans are safe to delete (optionally
   after adding the missing `trace` line to README/nuget-readme). The three ~95% partials
   could be trimmed to just their outstanding items rather than deleted. `NODE_PORT` and
   `MONOREPO` are unimplemented but are the only record of those designs.
3. **Convention gap:** every plan except `OTLP_EXPORT_PLAN.md` carries a Status header.
   Adding one there (and keeping headers current, as `REPORT_QUERY_PLAN.md`'s
   "implemented in full in 3.0.47" flip demonstrates) makes future audits trivial.

---

# Documentation Audit (follow-up)

**Question:** are all features/changes from the fully-done and partially-done plans fully
documented in the wiki (including discoverability and cross-references), and is the
README fully up to date?

**Verdict: NO — core semantics are documented well, but there are 2 factual errors in
the wiki, several stale pre-worker/pre-3.0.48 passages, the 3.0.63 changes never
reached the wiki, meaningful discoverability holes (Home.md, one-way cross-links,
missing CLI-flag rows), and the README/nuget-readme under-represent or omit several
shipped features.** Detail below, by feature area. (Repo version at audit time: 3.0.64;
wiki HEAD corresponds to 3.0.62 content.)

> **UPDATE (2026-08-29, same day):** every gap below has been FIXED except the three
> items in "Still open" at the end of this section. Fixes landed across ~20 wiki pages,
> README.md, nuget-readme.md, the `kronikol-test-debugging` skill, three doc-bearing
> code strings (`stableId` schema description, `query --help` note line, the OTLP
> package's NuGet `<Title>`/`<PackageTags>`), the CHANGELOG (under 3.0.64), and
> Kronikol4J's parity ledger (the missing 3.0.48 statement-limits entry). Affected test
> classes pass (179/179); nothing committed yet. The lists below are retained as the
> audit record.
>
> **Still open (deliberate):**
> 1. `--raw` query flag: parsed at `QueryOptions.cs` but read by no code path. Removing
>    it is a behavior change wanting a test, so it awaits a decision (delete vs
>    implement) rather than documentation.
> 2. Kronikol4J wiki coverage (~17 of the planned page set): a separate-repo writing
>    effort, tracked under `JAVA_PORT_PLAN.md` §12.1, not a fix-pass item.
> 3. Wiki-wide anchor-style normalisation: the two audit-flagged space-form anchors were
>    converted, but many other pre-existing `[[Page#Heading With Spaces]]` anchors
>    remain across the wiki; verify they resolve on GitHub before a bulk sweep.

| Area | Wiki coverage | Discoverability | README/NuGet |
|---|---|---|---|
| Background steps inline (3.0.48) | 🟡 good, 2 factual errors + gaps | 🟡 mostly wired | ✅ nothing stale |
| Statement limits (3.0.48) | 🟡 core section accurate, emitters under-enumerated | 🔴 key one-way link hole | ✅ nothing needed |
| OTLP export (3.0.60) | 🟡 semantics right, public API under-documented | 🔴 Home + CLI listing holes | 🟡 table right, no narrative; stale NuGet Title |
| Query family (3.0.47–58) | 🟢 verbs/flags complete, few gaps | 🟡 many zero-mention pages | 🔴 README partial, NuGet omits entirely |
| Render workers (3.0.45/50) | 🟡 main section right, stale passages | 🟡 mostly one-way links | 🔴 invisible in README |
| Note YAML toggle (3.0.59–63) | 🟡 accurate through 3.0.62; 3.0.63 missing | 🟡 not findable by searching "YAML" | 🟡 one accurate clause; absent from NuGet |
| Java port (Kronikol4J) | 🔴 own wiki at ~17% (17 vs 99 pages) | — | — (prior audit finding, carried forward) |

## Factual errors in the wiki (fix first)

1. **`Step-Tracking.md:296`** claims each section "numbers and collapses keywords
   independently" in the separated layout — numbering is continuous in *both* layouts
   (`ReportGenerator.cs:2279` passes `numberOffset: background.Length`); restarting
   numbering is a bug 3.0.48 *fixed*. The page re-documents the fixed bug.
2. **`Generated-Reports.md:25`** says `BackgroundSteps` is "omitted entirely when a
   scenario has none" — true for Specifications YAML/XML, but the JSON writer emits an
   empty array unconditionally (`ReportGenerator.cs:3881`; asserted by
   `SpecificationsDataTests.cs:267-269`).

## Stale content

- **`PlantUML-Browser-Rendering.md:40-43` and `:139`** describe per-diagram
  `data-plantuml` attributes and a "Loading diagram…" placeholder — generation now emits
  empty divs and a single gzip+base64 `puml-data` blob decompressed lazily
  (`ReportGenerator.cs:1334`, `:1414-1418`). The gzip transport (and why
  `DecompressionStream` is required) is documented nowhere.
- **`PlantUML-Browser-Rendering.md:85-94`** ("Lazy Rendering") still frames main-thread
  blocking as the current mechanism, contradicting the worker-mode table on the same page.
- **`Inline-SVG-Rendering.md:41`** — pre-worker: says the engine renders SVG "directly
  into the DOM"; workers return an SVG string written via `innerHTML`. Whole page
  predates 3.0.45 and never links to (or from) PlantUML-Browser-Rendering.
- **`FAQ.md:46`** — "self-hosted PlantUML JS library"; it is CDN-fetched from jsDelivr.
- **`Step-Tracking.md:318`** — "extracts them into the Background section" contradicts
  the inline default stated at `:272`; **`:530`** lists `SeparateBackgroundSteps` in the
  wrong options table (`StepTrackingOptions`) and omits `CollapseRepeatedStepKeywords`.
- **Generated `TestRunReport.schema.json` description of `stableId`**
  (`ReportGenerator.cs:4234`) predates the 3.0.47 example-values fold-in — and
  `AI-Integration-Prompt.md` explicitly sends agents to that schema.
- **`kronikol query --help`** (`QueryCommand.cs:193`) prints a `note` usage form
  (`s3/d0 [n12]`) the parser does not accept (wiki and error message are correct).
- **NuGet `<Title>` of `Kronikol.Extensions.Otlp`** still reads "Kronikol OTLP-Tap
  Extension" though the package now ships export too; `<PackageTags>` has no
  export/exporter tags.

## Missing documentation

- **3.0.63 never reached the wiki**: leading-newline block-scalar unfolding
  (indicator anchoring, `|2`, all-newline fallback) is only in the CHANGELOG — absent
  from `PlantUML-Browser-Rendering.md:121-125`. Also undocumented: `|` vs `|-` header
  choice; the >120-char-run fallback is measured after creole escaping, not on the raw
  string as the wiki implies.
- **3.0.62 backslash change invisible on `Content-Formatting.md`** — the page that owns
  note formatting never mentions creole/backslash handling.
- **3.0.48 ride-along fixes undocumented**: background text in the search index
  (`Search-Syntax.md` "What Gets Searched" omits background steps), Features Summary
  counts including background steps, all-background detail panels. The TestRunReport
  data-contract section never documents the `backgroundSteps` field despite both schemas
  declaring it. No "3.0.48+" version marker on the new option rows.
- **Statement-limits emitters under-enumerated**: `Large-Response-and-Diagram-Handling`
  covers only the request label; user-action, response, and loop/partition caps (and the
  fact that no `[Full path]` equivalent exists for a truncated Playwright locator) are
  undocumented, as is the component-diagram edge-label ceiling relevant to
  `RelationshipLabelFormatter` authors.
- **OTLP public API surface**: `OtlpExportOptions.Log`, `Name` default, `Validate()`
  rules (+ ctor throws), all seven public sink counters, `FlushAsync(timeout)` semantics,
  `OtlpExportResult.SpansFailed`/`BatchesSent`, `OtlpExporter.ExportSpansAsync`,
  `OtlpSpanMapper`/`OtlpJsonEncoder` never named, full 15-category `db.system.name`
  table (docs list 3), HTTP 30 s timeout, "CALL" name fallback, truncation marker,
  `--dry-run` without `--otlp`, directory/glob/`*.jsonl` input resolution, malformed-line
  reporting, `-h`. Imprecisions: PerPair's empty-TraceId fallback; `BodyAttributeCapBytes`
  is applied as characters, not bytes (docs and help both say bytes); orphans arise
  immediately in the batch path, not only via TTL.
- **Query**: `--raw` is parsed but read by no code path — dead or unimplemented, and
  undocumented everywhere; per-verb silent row caps (25/120/200) contradict the
  "every truncation announces itself" promise; wiki lacks the `--group-by step` collision
  warning and the non-numeric `--number` exit-2 rule (skill has both); `SKILL.md` omits
  `--group-by`, `--stats`, `assertions`, `--tolerance`, `--in`, `--limit`;
  `scripts/query.py`'s 6-verb fallback set is never named; `Generated-Reports.md:181`
  omits that `annotations` is gated like `diagrams`/`httpInteractions`.
- **Render workers**: `--browser-render-workers` missing from the ingest CLI table in
  `Ingesting-External-Captures.md:172-198` (the canonical flag list); nothing states the
  other two browser options have no CLI equivalent; telemetry field list incomplete;
  `window.plantuml.cacheStats()` undocumented; no diagnostics entry for
  "is it in worker mode / fallbackReason".

## Discoverability / cross-reference holes

- **`Home.md`** never mentions `kronikol export` or either OTLP page; PlantUML Browser
  Rendering is a bare link with no description; neither workers nor the YAML toggle is
  named anywhere on Home. A wiki search for "YAML" finds only the data-file format, not
  the toggle.
- **No page lists the four CLI verbs together** (merge/ingest/query/export);
  `Querying-Reports.md`'s See-also omits `kronikol export`.
- **One-way links**: `Capture-Time-Redaction.md` "Where else it applies" omits
  `kronikol export`; `Diagnostics-and-Debugging.md` omits `OtlpExportSink.Diagnostics()`
  from its list of diagnostic surfaces; `Integration-OpenTelemetry-Extension.md`'s
  disambiguation never mentions the outbound direction; ProxyTap/TcpTap pages never
  offer streaming-to-collector as a sink topology (TcpTap has zero "export" hits);
  `Large-Response-and-Diagram-Handling` ↔ `PlantUML-Browser-Rendering` and
  `Generated-Reports` ↔ YAML-toggle links only go one way.
- **The biggest single hole**: `PlantUML-Browser-Rendering.md:200` ("When the Engine
  Cannot Draw a Diagram") — the page a user lands on for `Syntax Error?` — never
  mentions the 3.0.48 over-long-statement diagnosis or the `RangeError` failure mode and
  never links to the statement-limits section.
- **Zero `kronikol query` mentions** on pages that should route to it:
  `Ingesting-External-Captures` (verifying an ingest landed), `Report-Configuration`
  (`TestRunReportFullStepDetail`'s raison d'être), `How-To-Guides`, `FAQ`
  ("how do I see what's being tracked" answers with DiagnosticMode only),
  `Step-Tracking`, `Search-Syntax`, `Capture-Time-Redaction` (verification via grep),
  ProxyTap/TcpTap pages, `Background-Thread-Correlation` (the page `stepPath: null`
  sends you to).
- **Sidebar placement**: Querying-Reports and PlantUML-Browser-Rendering sit deep in the
  Features block with no sub-anchors, unlike comparable neighbours; neither appears in
  Getting Started/Common Tasks. `FAQ.md`/`How-To-Guides.md` have no entries for
  statement limits ("Why is my diagram empty?" routes elsewhere) or query.
- **Anchor-style inconsistency**: `[[Page#How Rendering Runs (3.0.45+)]]`-style anchors
  (spaces/parens) vs normalised slugs — worth verifying the former resolve on GitHub.

## README / nuget-readme

- **README query block lists 7 of 18 verbs**; `trace` (the 3.0.57 headline), `diff`,
  `compare`, and `grep --number` appear nowhere; the v2 flag surface (`--where`,
  `--group-by`, `--stats`, `--tolerance`) is entirely absent.
- **No narrative section or runnable example for `kronikol export`** (merge, ingest,
  query all have one); the polyglot-backends section never says NDJSON can also be
  pushed to a collector. The tables/rows/links that do exist are accurate.
- **Browser render workers are invisible in the README** — no mention of workers, cache,
  engine, or CDN; `README.md:102` leads with server-side rendering though BrowserJs is
  the default, and the docs list links the IKVM page but not the browser-rendering page.
- **README:14 YAML-flip clause is accurate** but doesn't say it's hover-per-note on
  diagram notes and BrowserJs-only.
- **nuget-readme.md** (shown on every NuGet package page) has no query/debugging section
  (Querying-Reports absent from its Key-pages list), no YAML-toggle mention, no
  browser-rendering mention — the query family and viewer-experience work are invisible
  to NuGet arrivals.
- Background-steps/statement-limits: nothing stale in README/nuget-readme; at most a
  one-clause README mention of inline background steps by the precedent of the
  YAML-flip clause.

## Carried forward from the plan audit

- The **Kronikol4J wiki** remains at ~17 pages vs the .NET wiki's 99, violating
  `JAVA_PORT_PLAN` §12.1's page-per-module rule; its README documents the asset
  divergence (correct fallback), but the 3.0.48 statement-limits divergence is not
  ledgered in `REMAINING_PARITY.md`.

