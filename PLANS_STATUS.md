# Plan File Status

**Date:** 2026-08-30 · **Repo version:** 3.0.68 (HEAD `2975e8b`)

Living status of every `*_PLAN.md` in the repo root, verified item-by-item against
code, tests, changelog, git history, the wiki (`../Kronikol.wiki`), and the Java port
(`../Kronikol4J`). This supersedes the 2026-08-29 audit (in git history at `2270445`
if the full item-by-item record of that snapshot is needed); everything below was
re-verified fresh on 2026-08-30.

| Plan | Status | Shipped in |
|---|---|---|
| `BACKGROUND_STEPS_INLINE_PLAN.md` | ✅ Fully done | 3.0.48 |
| `LONG_LINE_SYNTAX_ERROR_PLAN.md` | 🟡 ~95% (Java mirror open) | 3.0.48 |
| `REPORT_QUERY_PLAN.md` | 🟡 ~95% | 3.0.47 |
| `QUERY_V2_PLAN.md` | ✅ Fully done | 3.0.51–3.0.58 |
| `BROWSER_RENDER_WORKER_PLAN.md` | ✅ Fully done | 3.0.45 / 3.0.50 |
| `NOTE_YAML_TOGGLE_PLAN.md` | ✅ Fully done incl. follow-ups | 3.0.59, 3.0.61–63, 3.0.66 (+fixes 3.0.67/68) |
| `OTLP_EXPORT_PLAN.md` | ✅ Fully done | 3.0.60 |
| `EXAMPLES_BLOCKS_PLAN.md` | ✅ Fully done (1 ledger nit) | 3.0.64 |
| `REQNROLL_DUPLICATE_STEPS_PLAN.md` | ✅ Fully done (#71) | 3.0.64 |
| `TEOZ_PERF_PLAN.md` *(new, untracked)* | 🟡 In-house work done; upstream-gated | 6 PRs + 1 issue open upstream |
| `PERF_CI_PLAN.md` *(new, untracked)* | 🟡 ~95%; R5 blocked on upstream merge | PR plantuml#2840 open |
| `QUERY_PERF_PLAN.md` *(untracked)* | 🟡 In progress (harness only; 0% production code) | — |
| `JAVA_PORT_PLAN.md` | 🟡 Partially done | Kronikol4J v0.1.24 |
| `NODE_PORT_PLAN.md` | ❌ Not started (design record) | — |
| `MONOREPO_MIGRATION_PLAN.md` | ❌ Not started (design record) | — |

---

## Fully done — ready for deletion (or keep as design records)

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
`--group-by`, and `diff` (docs sweep `50d53a1`). One straggler: the matching `trace`
line in `nuget-readme.md` exists only as an uncommitted working-tree edit.

### BROWSER_RENDER_WORKER_PLAN.md (3.0.45 / 3.0.50)
Phases 0–7 complete; remaining items are all plan-labelled optional (`_svgCache`
retirement, stable fragment boundaries, `requestIdleCallback` chunking). The perf
guard was deflaked post-release (`087be03`: 3-attempt retry, budgets unchanged) — note
that commit has no changelog entry.

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
- Blocked externally: retest checklist (all six PRs open, zero comments as of
  08-30); Kronikol-side follow-ups (npm upgrade to 1.2026.8, drop the fork, pass
  `maxSvgSize`) blocked because npm latest is still 1.2026.7. **Nothing shipped in
  Kronikol** — `TrackingDefaults.PlantUmlJsCdnBase` is still `@v1.2026.6-patched`.
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

### QUERY_PERF_PLAN.md
**Actively in flight (another session, as of 08-30 late morning), but 0% of the
production code has landed.** Status line still reads "planned. Not started."
- Built so far (all untracked under `tools/query-bench/`): the §4.3 report generator
  (200 scenarios × 60 pairs, ~130 MB corpus — the generated
  `TestRunReport.query-bench.json` weighs ~105–150 MB and was regenerated during this
  audit), the BCL re-tokenization benchmark, an internals harness (references the
  not-yet-existing `ReportIndex.PayloadOpens`, so it cannot compile yet), and an
  unplanned `bench.ps1` wall-clock harness. No README/protocol doc yet.
- One of five red tests written (uncommitted, currently red as intended):
  `Tool_runtimeconfig_pins_optimized_jit_for_loops` in `QueryCommandTests.cs`.
- Still missing: the JIT tiering property, `BodyCache` single-stream ownership,
  `PayloadReader` stream overload, `PayloadOpens` counter, grep/number-grep routing
  through `BodyCache`, the other four red tests, changelog/version.
- ⚠️ Hazard: the ~105 MB generated corpus is NOT gitignored (`[Bb]in/` covers only the
  build dirs) — a naive `git add tools/query-bench` would commit it. It is a
  regenerate-in-one-command artifact per `bench.ps1`'s own error message.

### LONG_LINE_SYNTAX_ERROR_PLAN.md (~95%, shipped 3.0.48)
Unchanged since the audit. Both fix layers, all tests, docs, and the IKVM verification
are done. Open: the **Kronikol4J mirror** — `PlantUmlCreator.java` still has zero
statement caps, so long-URL traces lose the whole diagram on the Java side while .NET
truncates and renders. The divergence is now ledgered
(`Kronikol4J/docs/REMAINING_PARITY.md:1879`, commit `e740422`) but not implemented.
Also open: the §6 Q2 component-diagram limit probe (a defensive ceiling was applied
instead; the component parser's limits remain unmeasured).

### REPORT_QUERY_PLAN.md (~95%, shipped 3.0.47)
Unchanged since the audit. Open items:
- `--json` machine-readable output (§3.1 principle 5) — still absent.
- The dead `--raw` flag — still parsed (`QueryOptions.cs:48/:156`) and read by no code
  path; ledgered in the 3.0.64 changelog as "pending a decision to remove".
- Note-divergence detection (§3.4) — still a blanket caveat footer, no reconciliation.
- The >100 MB streaming-path test (§3.6) — still missing as a *test*, but the
  QUERY_PERF harness now being built generates exactly the needed corpus; wiring one
  test to it would close this.
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
