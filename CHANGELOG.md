# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [3.0.68] - 2026-08-30

### Fixed
- **A scenario's JSON/YAML dropdown could come back from a page refresh reading YAML over JSON notes** (user-reported, and initially unreproducible because it needs a prior selection plus a reload). Firefox restores `<select>`/`<input>` state across a plain reload — Chromium does not — so a reader who had once picked **YAML** on a scenario's note-format dropdown got that value written back into the control by the browser, while the freshly-loaded report script (whose `_noteFormatDefault` and per-container preferences start from the report's configured default) rendered the notes as JSON. The control then lied about the report's state and no click could reconcile it: re-picking YAML on an already-YAML `<select>` fires no `change` event. Every report form control — both note-format dropdowns, all truncate-lines dropdowns, the search box and the duration threshold — now carries `autocomplete="off"`, which is what tells the browser not to second-guess state the report owns; filter state that *should* survive a reload continues to travel in the URL hash. Pinned by markup tests over the generated report and by end-to-end tests that drive the real Firefox through the select-reload-assert cycle (they skip where Firefox is not installed, as on CI, which installs chromium only).

## [3.0.67] - 2026-08-30

### Fixed
- **Buttons on a note split across diagram fragments drove the wrong note** (user-reported: a payload large enough to split over 3 diagrams had "non-intuitive" buttons). In browser-rendered reports, note state lives on the owner container keyed by original-source note index, but `makeNotesCollapsible` hard-coded every "..Continued From Previous Diagram.." chunk to original note index **0** — only correct when the split note happens to be the diagram's first. In the reported shape (request-body note first, huge query note second) every control on the continuation chunks — **+**/**−**, the expand/truncate arrows, double-click, and the `Y`/`J` format toggle (which also cached eligibility and YAML derived from the wrong note's payload) — read and wrote note 0's state: the wrong note collapsed or flipped to YAML, the clicked chunk rendered its buttons from the wrong note's state (an expanded chunk could show **+**), and once the wrong note's state matched the click target the click was silently swallowed by the same-step early return. A continuation chunk now maps to the note it continues (`noteIndexOffset - 1`; interior chunks of multiple split notes in one diagram all resolve correctly), via a new shared `computeFragmentNoteIndexing`/`window._computeGlobalNoteIndex` helper.
- **The context menu disagreed with the note buttons on split diagrams.** It computed its global note index by summing raw note-block counts of preceding fragments — no continuation subtraction, no chunk remap — so on any diagram with continuation fragments "Copy box text" consulted a different `_noteSteps`/`_noteFormats` entry than the buttons write (e.g. offering the full/current submenu for a fully expanded chunk because an unrelated note was collapsed). It now uses the same shared indexing helper as the buttons.
- **`forceIsLong` on continuation chunks keyed off the SVG group ordinal** instead of the fragment-local block index; the two diverge when hidden/empty notes make the SVG carry fewer groups than the source has blocks.
- The prior regression test for this area (`Chunked_continuation_note_expand_button_expands_correct_note`) asserted the buggy mapping ("the continuation note is original note 0" — in its own fixture the split note is index 1) and has been rewritten to pin the correct behaviour, alongside new coverage: middle-fragment chunk targeting, context-menu/button index agreement, format-toggle targeting, and in-page unit tests of the shared index arithmetic (including two split notes in one diagram).

### Added
- **Bulk JSON ⇄ YAML note format dropdowns.** Beside the Headers/Assertions/Steps/Databases toggles — at report level and on each scenario's diagram toolbar — browser-rendered reports with JSON note payloads now carry a compact label-free **JSON/YAML** `<select>` (`aria-label`/`title` "Note payload format"). The report-level dropdown converts every eligible note in every scenario, syncs all scenario dropdowns, and becomes the default for not-yet-rendered diagrams (a scenario opened later renders straight into the chosen format); the scenario-level dropdown converts only its own scenario, including its lazy diagrams, and deliberately moves nothing else. Ineligible notes (plain text, GraphQL, truncated JSON, …) are untouched; per-note `Y`/`J` buttons keep working for fine control and — like per-note **+**/**−** vs the Details radio — do not move the dropdowns. Bulk commands ride the shared eligibility/emission caches (`ensureNoteFormatEligible`/`ensureNoteYamlLines`/`setAllNoteFormats`), so no note is ever reconstructed twice, and the dropdowns carry the same pending throb/spinner contract as the other toolbar controls (a no-op command clears instantly). This resolves `NOTE_YAML_TOGGLE_PLAN.md`'s bulk-toggle open question — as toolbar dropdowns rather than the once-contemplated context-menu entry.
- **`ReportConfigurationOptions.NotePayloadFormat` — start reports in YAML.** `NotePayloadFormat.Yaml` renders every eligible JSON note payload in the derived YAML view from first paint (SQL and other embedded multi-line strings display as block scalars with zero clicks), seeds the dropdowns to **YAML**, and leaves every toggle available to go back; default `Json` is byte-identical to before. Injected as a `__NOTE_FORMAT_DEFAULT__` token into `collapsible-notes-script.js` via the new `DiagramContextMenu.GetCollapsibleNotesScript(NotePayloadFormat)` overload, honoured by lazy containers through `_preProcessSource` (which now also counts truncation against the active format's line count and applies formats before building, so a yaml-default long note truncates correctly). Threaded through both HTML reports, the mergeable-report renderer, and the CLI as `kronikol ingest --note-format <json|yaml>`.

### Fixed
- **"Copy box text" on a YAML-toggled note copied the original JSON** (user-reported). The context menu's copy paths (`Copy box text`, `Copy full/current box text`, `Open box text in new tab`, and Copy Highlighted Text's selection normalisation) all read the note source, which still holds the JSON payload after a YAML toggle. They now follow the displayed format: on a YAML note the full text is the gray headers plus the complete YAML view (un-truncated *current view* by design), and the current text is the truncated YAML exactly as displayed.
- **Creole `~` escapes leaked into copied note text.** Two variants, both fixed by reversing the escapes on the way to the clipboard: the *truncated YAML* current view is parsed from the render source, which carries the client splice escapes (`https://` displayed, `https:~/~/` copied) — reversed by a new exact-inverse `unescapeNoteDisplayLine` (exported as `window._noteUnescapeDisplayLine`, roundtrip-pinned across every marker class); and the *plain JSON view* copy paths carried the generation-side escapes from the note source for any payload with `//`, `--`, `**`, `[[` … — reversed with the same transform (and same accepted literal-`~` ambiguity) as the YAML reconstructor. Pinned by the new `YamlNoteCopyTextTests` clipboard suite against a bug-exposing fixture (URL field + 45-line SQL unfold); the shipped gold vector produces zero escapes, which is why the leak went unnoticed.
- **YAML-state filter-survival coverage completed.** The wiki claimed the toggle survives the assertion/step/database filters but only the steps filter was tested; assertion- and database-filter survival tests are now pinned (green — the shared `applyNoteFormats` pipeline already preserved the state). Remaining deliberately-uncovered paths are recorded in `PLANS_STATUS.md`.

## [3.0.65] - 2026-08-29

### Fixed
- **The project templates actually work now — every `kronikol-*` template was broken for users in three independent ways**, exposed by the first-ever run of the 3.0.64 "Template Scaffolds" CI job and reproduced end-to-end via `dotnet new`: (1) all 12 templates shared `"groupIdentity": "Kronikol.Test"` at equal precedence, so the template engine treated them as conflicting variants of one template and **no kronikol template could be instantiated at all**; (2) each template's `sourceName` was the Kronikol package id itself, so instantiation rewrote the `<PackageReference Include="Kronikol.xUnit3">` pin and the `using Kronikol.xUnit3;` API imports into the user's project name — a self-referencing NU1108 cycle plus unresolvable API types in every generated project; (3) the scaffold code had rotted against current packages (missing `using LightBDD.Framework;` for `[FeatureDescription]`, xUnit2/TUnit LightBDD scopes still using the removed `LightBddScope` base instead of `LightBddScopeAttribute`, the xUnit2 `TestRun` calling xUnit3-only APIs instead of the `ReportingTestFramework`/`ReportLifecycle` pattern, TUnit templates declaring a `Main` that collides with TUnit's generated entry point, `Microsoft.AspNetCore.Mvc.Testing` pinned below what Kronikol requires, stale LightBDD/MSTest/NUnit pins, and a duplicate `Content` item under the Web SDK). Scaffolds are renamed to a neutral `sourceName` (`KronikolComponentTests`), TUnit templates host their placeholder API through a Main-less `WebApplicationFactory.CreateWebHostBuilder` override, and all pins are aligned (Kronikol pins track the previous published release, now 3.0.64). All 12 templates verified by pack → `dotnet new install` → instantiate → build.
- **The Template Scaffolds CI job now verifies templates the way users consume them** — pack, install, instantiate under a neutral name, build. The 3.0.64 job built the scaffold folders in place, which both false-failed (a scaffold sharing its project name with the package it references trips NU1108) and could never catch the template.json faults above.
- **E2E (Remainder) CI job fixed**: the `FullPipelineParameterizedE2ETests` fixture ran the example projects with `--no-restore`, which fails on a clean runner (NETSDK1004, no assets file) — restore is now allowed and a fixture timeout throws instead of falling through; `BugReproTests` deleted — debugging scratch that launched a headed browser and read a hard-coded local file (one test even asserted a long-fixed bug was still present in a v3.0.37 artifact), so it could never pass anywhere but its author's machine.

## [3.0.64] - 2026-08-29

### Added
- **Named `Examples:` blocks render as separator bands in parameterized tables.** A Gherkin scenario outline with multiple `Examples:` blocks — each optionally named and described — previously flattened all rows into one table, losing the division and the names. Now a full-width band row (`<tr class="examples-block-row">`) is drawn before each block's rows in **both** the flat and the grouped table variant, carrying the block name (`Examples: the merchant gained share`; an unnamed block among named ones shows the bare keyword), a per-block pass/fail count (`2/2 passed`, `1 failed, 1/2 passed`), and the block's description when present. Bands are inert to row selection, row numbering runs continuously across blocks, and block name + description join the section and per-row search text so searching a block name finds and highlights its rows. **Activation rule:** bands render only when an outline actually has block structure (more than one block, or a named block) — a single unnamed block and all non-Gherkin adapters produce byte-identical output to 3.0.63. Block identity is captured end to end: **ReqNRoll live tracking** resolves it from the feature-level Cucumber messages Reqnroll 3.3+ embeds in generated code (guarded reflection over two internal properties with a value-match fallback; older generated code degrades silently to today's rendering), the **Cucumber Messages ingest** reads it from the `gherkinDocument` envelopes, the **generic NDJSON ingest** accepts optional `examplesBlockName` / `examplesBlockDescription` / `examplesBlockIndex` fields on the `start` event, and the **mergeable report JSON** round-trips all three fields so parallel shards reassemble their blocks. `ScenarioStableId` inputs are unchanged, so historical stable ids are not re-keyed.

### Fixed
- **ReqNRoll steps no longer duplicated when both `Kronikol.ReqNRoll.Core` and the framework assembly are listed in `bindingAssemblies`** (#71). With both assemblies listed — exactly what Kronikol's own ReqNRoll templates shipped — ReqNRoll discovers both the base `ReqNRollTrackingHooks` and its framework subclass and runs every hook on one instance of each per scenario. The v3.0.18 idempotency guard (#59) only covered `BeforeScenario`; `BeforeStep`/`AfterStep`/`AfterScenario` still executed twice, so every step was recorded twice with identical durations (rendered as a duplicate `And` line — both `AfterStep`s read the same surviving stopwatch), `StepCollector.StartStep`/`CompleteStep` ran twice per step, and `AfterScenario` enqueued the scenario twice (masked in reports by `DistinctBy(ScenarioId)`, which stays as defense-in-depth). The `BeforeScenario` that initializes the scenario now records its instance as the owner (`ReqNRollConstants.OwnerHooksKey`), and every per-scenario/per-step hook no-ops on non-owner instances — order-independent via `ReferenceEquals`, correct even when a step text legitimately repeats, per-scenario so nothing leaks across parallel test threads. The three ReqNRoll project templates now list only the framework assembly in `reqnroll.json` (Core became redundant there in 3.0.18; template pins are at 3.0.63, well past it), while the three `Example.Api` ReqNRoll example projects deliberately list **both** assemblies so CI permanently exercises the double-discovery path existing consumers have. Pinned by new `ReqNRollDuplicateStepsTests` integration tests (red before the fix): an adjacent-duplicate scan over every scenario plus exact ordered step lists for plain scenarios, a Scenario Outline example row (doubled steps feed `ExampleValueGrouper`), and the new `Cake Quality` background feature (doubled steps also doubled the `BackgroundStepsDetector`-extracted prefix).
- **Setup partition restored for BDD-framework adapters — step bars no longer suppress it.** With `SeparateSetup` on, every adapter that draws step delimiter bars (ReqNRoll, LightBDD, BDDfy — any path through `StepCollector.StartStep`) lost the `partition Setup` box entirely: step bars travel the override-marker channel, and the first `IsOverrideStart` reaching the diagram builder before the partition opened ran the close-and-suppress logic meant for the boundary-marking `TrackingDiagramOverride.StartOverride`. Since the GIVEN bar precedes the first setup call, the partition never opened; plain xUnit/NUnit/TUnit adapters (no step bars) were unaffected. Markers now branch on `DiagramMarkerKind`: narration markers (`Step`, `Assertion`, `Row`) with real setup traces still to come render **inside** the partition (opening it if needed, so the GIVEN bar sits in the box it introduces), while a narration marker past the last real setup trace — like the WHEN bar emitted just before `StartAction` — closes the partition exactly like the boundary override, whose `Custom`-kind semantics are unchanged and now pinned by test. Surfaced by the revived Integration CI jobs (`SeparateSetupTrue`/`HighlightSetupTrue`/`HighlightSetupFalse` failing for all four BDD example projects); fixed red-first with four new `PlantUmlCreatorTests` cases. Also from that revival, three doses of test rot cured in the long-unrun integration suite: `TUnitParameterizedRenderingTests` matched scenario names by raw method name (`Process_order_in_region`) though reports have humanized them (`Process order in region`) for months; `AssertContainsSequenceArrow` predated colored arrows (`-[#438DD5]->`) and matched nothing; and `Project_plantuml_contains_expected_participants_and_arrows` asserted every diagram shows the API participants, which the deliberately HTTP-free ReqNRoll.xUnit3 Muffins diagnostic scenarios (correctly rendering the `(no interactions)` placeholder) never could — it now asserts on traffic-bearing diagrams only.
- **CI Integration jobs silently ran zero tests.** All five "Integration (…)" matrix entries filter on the project-name theory argument (e.g. `Component.ReqNRoll.xUnit3`), but xunit.v3 exposes theory arguments only in `DisplayName`, never in `FullyQualifiedName` — so every `FullyQualifiedName~` clause matched nothing and `dotnet test` exited 0 without running a single integration test. Filter clauses now match each part against **both** `FullyQualifiedName` and `DisplayName` (class-name filters like the E2E groups are unaffected), and every filtered/grouped `dotnet test` run passes `RunConfiguration.TreatNoTestsAsError=true`, so a filter that matches nothing fails the job instead of passing silently.
- **Outline rows from different `Examples:` blocks no longer interleave.** The ReqNRoll adapter ordered outline members alphabetically by their joined example values, which shuffled rows across blocks; members now stable-sort by block index (in the adapter and centrally in `ParameterGrouper`), preserving source order within a block and today's order for blockless groups. The `Example.Api` ReqNRoll xUnit3 Muffins outline now demonstrates two named blocks (one with a description).
- **CI coverage overhauled — the allowlist rot is closed.** The CI test matrix was a hand-picked allowlist that silently skipped **34 unit-test projects** (TcpTap's 261 tests, Otlp, BigQuery, every messaging/DB extension …), **26 of 58 E2E test classes** (including the NoteYaml suites — three releases of YAML-toggle work shipped without CI ever running their tests), all three TUnit example suites, and the template scaffolds. Now rot-proof by construction: a **"Remaining Unit Tests (auto-discovered)"** job runs every `tests/*` project without a dedicated matrix entry (new projects are covered by default), an **"E2E (Remainder)"** job runs every E2E class not named by a group via a negative `FullyQualifiedName!~` filter chain (new classes covered by default; the three doc-asset generators — WikiGif/WikiScreenshot/ShowcaseReport, which need ImageMagick — are excluded *explicitly* with a comment), the TUnit adapter test projects joined the Adapter group, the three `Example.Api` TUnit suites run via `dotnet run` (Microsoft.Testing.Platform refuses VSTest-mode `dotnet test` on .NET 10), and a **Template Scaffolds Build & Pack** job builds every `kronikol-*` template against its pinned published packages and packs the template package.
- **`Kronikol.Tests.AssertionRewriter` adopted into the solution.** 21 healthy tests were orphaned — in neither `Kronikol.sln` nor CI, so neither local full-suite runs nor CI ever executed them.
- **`Example.Api` TUnit examples fixed: `TUnit` 1.33.0 → 1.53.0.** `Component.TUnit` and `Component.ReqNRoll.TUnit` crashed at test-assembly load (`MissingFieldException: TUnit.Core.Sources.BeforeEveryTestHooks`) — the same generated-hooks-vs-runtime version skew as the 3.0.61 LightBDD.TUnit fix, hidden by the same CI gap. Both suites now run green under the real TUnit engine.
- **Template scaffolds unstuck from `2.29.17-beta`.** Every `kronikol-*` template pinned Kronikol packages at a version from months ago; now pinned to **3.0.63** (the latest *published* release — pins deliberately track the previous release so the CI template-build job can restore them from NuGet before the current tag's packages appear), with `TUnit` aligned to 1.53.0. Follow-up: automating the pin bump in the release flow.
- **Documentation sweep across the wiki, README, and doc-bearing code strings** (findings from a full audit of shipped-plan coverage, recorded in `PLANS_STATUS.md`). Factual fixes: separated background layouts do NOT restart step numbering (numbering is continuous in both layouts since 3.0.48; the wiki re-documented the fixed bug); `Specifications.json` emits an empty `backgroundSteps` array rather than omitting it (only YAML/XML omit); the generated `TestRunReport.schema.json` `stableId` description now mentions the ordered example values folded in since 3.0.47; `kronikol query --help`'s `note` usage line now shows the single-token `s3/d0/n12` form the parser actually accepts. Coverage added: the 3.0.63 leading-newline block-scalar behaviour and the `|` vs `|-` header rule reached the wiki; the 3.0.62 verbatim-backslash change is documented on Content-Formatting; statement-limit docs now enumerate the user-action/response/loop label caps and the component-diagram edge ceiling; the browser "cannot draw" section explains the over-long-statement diagnosis and the coloured-note-bar `RangeError`; stale pre-worker rendering prose (per-diagram `data-plantuml`, "Loading diagram…", main-thread lazy-rendering framing, "self-hosted" engine) rewritten for the 3.0.45+ gzip-source-map + Web Worker reality; the OTLP export page documents the full public surface (`Log`, `Validate()`, sink counters, `FlushAsync(timeout)`, `ExportSpansAsync`, all 15 `db.system.name` mappings, character-count cap, CLI input/dry-run rules); `kronikol query` row ceilings, the `--group-by step` collision warning, and the non-numeric `--number` exit code documented. Discoverability: `--browser-render-workers` added to the ingest option table; export/OTLP pages reach Home, the CLI cross-references, redaction, diagnostics, and both tap pages; FAQ/How-To entries route Syntax-Error and query questions; sidebar sub-anchors for browser rendering and querying; README gains a `kronikol export` section, `trace`/`diff`/`--group-by`/`--number` query examples, worker-rendering visibility, and the browser-rendering wiki link; nuget-readme gains a query section. NuGet metadata: `Kronikol.Extensions.Otlp` `<Title>` now says "OTLP Tap + Export" with export tags. Kronikol4J's parity ledger gains the previously unrecorded 3.0.48 statement-limits divergence. Known remainders (recorded in `PLANS_STATUS.md`): the dead `--raw` query flag (parsed, never read — pending a decision to remove) and the Kronikol4J wiki's coverage gap.

## [3.0.63] - 2026-08-28

### Fixed
- **Note YAML view: strings starting with `\n` now unfold into block scalars.** The emitter's eligibility rule rejected any string whose first character is a newline, so every SQL body written as a C# raw string or indented heredoc (they begin with `\n` — e.g. real BigQuery job bodies) stayed a one-line quoted scalar with visible `\n` escapes in YAML view. A literal block scalar represents the leading newline as empty line(s) opening the block, and YAML anchors block-scalar indentation on the first *non-empty* line — so that line now drives the `|2` explicit-indentation indicator instead of `blockLines[0]`. Strings consisting only of newlines (nothing to anchor indentation on) keep the quoted fallback, as does the indicator-inside-sequence case. Emitted YAML round-trip-verified byte-exact through js-yaml, including the leading-empty-line and `|2-` shapes; pinned by five new Playwright internals tests including the exact reported BigQuery job-body shape.
- **Render-worker perf-guard `ReadyMs` contention budget raised 2500 → 4500 ms.** The absolute budget exists only to catch egregious regressions; the load-independent guarantee is the relative assert (page-ready never waits for the engine), which held while back-to-back full-suite runs measured 3864 ms from CPU contention alone (prior history: 2002 ms noted when the 2500 budget was set).

## [3.0.62] - 2026-08-28

### Fixed
- **Diagram notes now display payload backslash bytes exactly — the blanket backslash doubling is gone.** Since v2.0.83, `EscapeForPlantUmlNote` doubled every backslash in note bodies, so a wire `\n` escape rendered as `\\n` (and a payload literal `\\` as `\\\\`) in every JSON note. Probing both shipped renderers (plantuml.js 1.2026.6 and the IKVM PlantUML jar, with and without the teoz pragma) showed the doubling was never needed: PlantUML block notes render backslash sequences literally — `\n`, `\r`, `\b`, `\f`, `\"`, `\/`, `\\`, `\uXXXX` and trailing `\` all display as written. The single exception is `\t`, which PlantUML always renders as a real tab, and escaping cannot prevent it (the final `\t` pair of any backslash run is consumed — the old doubling displayed a stray `\` before the tab). Notes now carry the wire bytes untouched: the JSON view is byte-exact on screen, and a wire `\t` shows as a clean tab. The YAML toggle's reconstruction drops its backslash-halving step (becoming *more* exact — the halving could not distinguish a payload's own `\\` from the escaper's) and the client-side YAML splice escape stops doubling to match. Pinned by new generation tests (verbatim `\n`/`\\`/`"` bytes in the note source), reconstructor byte-exactness tests, and a new display-fidelity E2E asserting the rendered SVG shows `SELECT o.id,\n` with a single backslash. Kronikol4J still doubles until it re-syncs — recorded in its parity ledger with the rule that the generator and `collapsible-notes-script.js` must move together. Wiki and `NOTE_YAML_TOGGLE_PLAN.md` updated.

- **Flaky EF Core registry test stabilised.** `Constructor_AutoRegistersWithTrackingComponentRegistry` sat in the parallel-running `SqlTrackingInterceptorTests` class while `TrackingComponentRegistry` is a global static guarded by the `"TrackingComponentRegistry"` xunit collection — a parallel class's `Clear()` could race its registration away. Moved into the collection-guarded class.
- **Flaky ProxyTap diagnostics test stabilised.** The tap deliberately answers the caller *before* bumping `RequestsHandled`/`RequestsCaptured` and recording ("respond first, record second" — bookkeeping never delays the forwarded exchange), so `Diagnostics_are_empty_while_healthy_and_name_the_requests_that_were_not_captured` asserting the counters immediately after `SendAsync` returned raced the increments (reproduced 3 failures in 4 runs). The test now waits out that gap with a bounded poll before asserting; the tap's respond-first design is unchanged.

## [3.0.61] - 2026-08-28

### Fixed
- **Note YAML view: CRLF strings now unfold into block scalars** (`NOTE_YAML_TOGGLE_PLAN.md` addendum). The 3.0.59 toggle promised strings with `\n` line breaks would render as literal block scalars, but any string whose breaks were `\r\n` — which is every payload captured from a Windows-built service, since `\r` is a control character the emitter's byte-fidelity rule routes to the quoted fallback — stayed a single soft-wrapped `"...\r\n..."` line in YAML view. `formatYamlString` now normalises the string for display when *every* line break in it is exactly `\r\n` (no lone `\r`, no bare `\n`) and emits the block scalar with the `\r` dropped; the YAML view knowingly trades those bytes for readability while the JSON view stays byte-exact, consistent with the existing documented creole `~` caveat. Mixed or lone `\r`, and CRLF strings that are ineligible for other reasons (e.g. trailing whitespace before a break), still take the exact double-quoted fallback — and that fallback keeps the original `\r` bytes. Covered by new Playwright internals tests (uniform CRLF → `|-`/`|`, mixed breaks and lone `\r` stay quoted, fallback preserves `\r` bytes) and a full-flow toggle test on a note carrying the pipeline's doubled `\\r\\n` escapes; wiki (`PlantUML-Browser-Rendering.md`, `Generated-Reports.md`) and the plan doc updated.
- **LightBDD + TUnit: crash at test-assembly load fixed by aligning the TUnit package line.** `LightBDD.TUnit` 3.12.0 ships hook registrations compiled against the pre-1.x TUnit API; combined with the `TUnit.Core` 1.43.11 that `Kronikol.TUnit` pins, every assembly referencing `Kronikol.LightBDD.TUnit` crashed at module initialization (`MissingFieldException: TUnit.Core.Sources.AfterClassHooks`) — under xunit.v3's process launcher this surfaced as `Catastrophic failure: Test process did not return valid JSON`, so `Kronikol.Tests.LightBDD.TUnit` had silently run **zero** tests (CI never builds that project, hiding it). `Kronikol.LightBDD.TUnit` now references `LightBDD.TUnit` **3.12.1** (the first build against the modern TUnit line, resolving TUnit 1.53.0) with `Microsoft.Testing.Platform.MSBuild` 2.2.3; the test project disables TUnit source generation (`EnableTUnitSourceGeneration=false` — xUnit drives it) and aligns its `TUnit.Assertions` pin to 1.53.0. All 25 tests in `Kronikol.Tests.LightBDD.TUnit` now run and pass, and the `Example.Api` LightBDD-TUnit component suite runs green under the real TUnit engine. The `kronikol-lightbdd-tunit` project template carried the same crash combo (`LightBDD.TUnit` 3.12.0 + `TUnit` 1.43.11) to every user who scaffolded it — bumped to 3.12.1 + 1.53.0. Wiki `Integration-LightBDD-TUnit.md` records the minimum-version requirement.
- **`Example.Api` LightBDD-TUnit example compiles again.** Three files had drifted from their xUnit siblings and were missing `using Kronikol.LightBDD;` (for `HappyPath`, `TrackingDiagramOverride`, `TrackDependenciesForDiagrams`, `CreateTestTrackingClient` and the handler options) — invisible because CI does not build this example.
- **Flaky E2E test `Whole_test_flow_toggle_switches_views` stabilised.** The whole-test-flow main view holds only an empty `plantuml-browser` div until the browser engine renders the SVG into it — a zero-size box Playwright reports as "hidden" — so the test's first visibility assert raced the render and could exceed the 5 s `Expect` default under full-suite CPU load. The test now waits for the rendered `svg` (60 s, `PollingInterval = 200`) before asserting visibility.

## [3.0.60] - 2026-08-27

### Added
- **OTLP export — Kronikol captures → the user's tracing backend** (`OTLP_EXPORT_PLAN.md`, M1–M7). `Kronikol.Extensions.Otlp` gained the outbound direction, the twin of its receiver-tee: captured interactions leave as OpenTelemetry spans over OTLP/HTTP (OTLP/JSON), so the traffic only Kronikol can see — proxy/TCP-tap hops from services that emit no telemetry, handler-captured calls with test attribution — appears in Tempo/Jaeger/any collector next to the app's real traces. Three layers, each useful alone: `OtlpSpanMapper` + `OtlpJsonEncoder` (pure pair→span mapping and byte-stable hand-written encoding, round-trip-tested against the package's own `OtlpTraceReader` as the decode-back oracle, plus a live full-circle test exporter→`OtlpTap`); `OtlpExporter` (batch push, paged by `BatchMaxSpans`, gzip, header auth, one immediate re-attempt then count-never-throw); and `OtlpExportSink : IRequestResponseSink` (streaming for live tap topologies via `CompositeRequestResponseSink` — bounded queue with counted drops, request/response pairing with a `PendingRequestTtl` orphan path, `FlushAsync`/`DisposeAsync` draining under `ShutdownTimeout`, `Diagnostics()` surfacing `CaptureDegraded` entries like the taps). Span identity preserves D4: captured `ActivityTraceId`/`ActivitySpanId` always win so exported spans join the SUT's own distributed traces; otherwise the default `TraceIdStrategy.PerTest` derives the trace id from `TestId` (the `InteractionRecord.ToGuid` recipe — a browser-minted 32-hex test id maps to itself) so **one test renders as one trace** instead of thousands of single-span traces (`PerPair` opts out). Mapping: one pair = one `CLIENT` span (`PRODUCER` for events), semconv attributes (`url.full`, `http.request.method`, numeric `http.response.status_code` with the ≥ 400 → `ERROR` rule, `db.system.name` as the exact reverse of the tap's category map, `peer.service`) plus the `kronikol.*` vocabulary the taps already emit; bodies opt-in (`IncludeBodies`, capped) and headers never exported; span-sourced echoes (`capturedBy: span` / `wire + span`) suppressed by default so the backend never stores its own spans twice; markers and `TrackingIgnore` always skipped; missing timestamps never drop a record (`kronikol.times.synthetic`), unpaired records export as zero-duration `kronikol.orphan` spans, and a lone request carrying `durationMs` (the one-record NDJSON contract) is a complete span. Exported traces are flat (no `parentSpanId`) by documented design. Non-interference: a standalone `HttpClient` POSTing to a URL — never touches the SUT's `TracerProviderBuilder`, processors or `Activity.Recorded`.
- **`kronikol export` CLI verb.** `kronikol export <captures.ndjson>... --otlp <endpoint> [--header k=v]... [--include-bodies] [--body-cap N] [--include-span-sourced] [--per-pair-traces] [--no-redact] [--redact-header h]... [--gzip] [--dry-run [--out file.json]]` — reads the same NDJSON `kronikol ingest` does and POSTs it as spans; `--dry-run` writes the encoded OTLP/JSON (to `--out`, else stdout, counts to stderr) for listener-free testing and debugging; prints spans/traces/skipped/orphans; exit codes follow `ingest` conventions. Redaction is per-path and the NDJSON path is the one where nothing has run yet, so the verb applies `CaptureRedaction` itself — default on, `--no-redact`/`--redact-header` mirroring `ingest` exactly.
- **Docs.** New wiki page `Exporting-to-OpenTelemetry.md` (mapping table, trace-grouping + flat-trace statement, echo suppression, body/header policy, per-path redaction, non-interference, tap composition example, CLI) — including the first written definitions of the **D3** (capture never blocks or degrades the observed system) and **D4** (real W3C ids are preserved end-to-end) invariants the code has long cited; `Integration-Otlp-Extension.md`'s dangling "other direction" cross-reference now points at it (it mis-cited the OpenTelemetry extension page); sidebar, README and nuget-readme rows; package descriptions updated. Kronikol4J: export is .NET-only for now — recorded in its `docs/REMAINING_PARITY.md` divergence ledger (the Java side gains the inbound tap first per `OTLP_TAP_PLAN.md`).

## [3.0.59] - 2026-08-26

### Added
- **Note payload JSON ⇄ YAML hover toggle** (`NOTE_YAML_TOGGLE_PLAN.md`, browser-rendered reports). Hovering a diagram note whose body is JSON shows a **`Y`** button beside **−**; clicking re-renders the payload as YAML, where strings holding `\n` escapes — SQL queries, stack traces, scripts — unfold into literal block scalars (`|-`/`|`/`|2-`) with their original line breaks and indentation instead of one soft-wrapped line; **`J`** restores the exact original JSON (the note lines come back from `_noteOriginalSource`, byte-identical). Entirely client-side in `collapsible-notes-script.js`: the JSON is *reconstructed* from the note text by reversing the generation-time transforms (gray headers dropped, backslash doubling halved, focus markup stripped, creole escapes removed, wrap breaks re-joined — a line ending inside an unterminated JSON string is provably a wrap break) and gated by `JSON.parse`, so ineligible bodies (capture-truncated prefixes, GraphQL, form-urlencoded, plain text, binary placeholder, continuation chunks) simply never show the button — the failure mode is "toggle unavailable", never wrong output. YAML is emitted from the reconstructed *text's tokens*, never a parsed JS value, so int64s beyond 2^53, duplicate keys and integer-like key order survive verbatim; strings a block scalar can't faithfully represent (control chars, trailing-whitespace lines, >120-char unbreakable runs — which could never be wrapped without changing the string's meaning) take a double-quoted fallback no worse than the JSON view. Eligibility is computed lazily on the note's first hover and cached; a toggle is the same rebuild/re-render path as expand/collapse (shared `rerenderWithNoteStates` helper), and the format survives expand/truncate/collapse, header hiding, assertion/step/database filters and client-side fragment splits; truncation and the long-note arrows count the *displayed* (YAML) line count. Known narrow caveat (documented): a payload literal `~` directly before a doubled creole marker reconstructs one character off — the JSON view is always exact. Already-generated reports gain the feature once they use the updated script asset; Kronikol4J ships the same script byte-for-byte.

## [3.0.58] - 2026-08-26

### Changed
- **`QUERY_V2_PLAN.md` closed out.** Milestone 8 (`select`, the full-JSONPath escape hatch) is a recorded **no-go**: milestones 2–7 shipped without any verb sprouting flags that approximate descent or filters — `values` + `--where` + `--group-by` answered the traffic, which was the plan's bet. The section stays as the design record with the reopening trigger. A synthesized wide run (300 interactions, 200 distinct bodies) now pins that `values` and `grep --number` complete at real-report scale, closing the plan's remaining perf risk; the final docs sweep verified every command and flag in `--help` is documented in the skill reference and the wiki, and smoke-ran every verb against a generated report. Kronikol4J: none of the query-v2 work applies — the tool is .NET-only and the report format is untouched throughout 3.0.51–3.0.58.

## [3.0.57] - 2026-08-26

### Added
- **`kronikol query trace` — follow a W3C trace id across the run** (`QUERY_V2_PLAN.md` Milestone 7). 3.0.47 put `activityTraceId`/`activitySpanId` on every interaction; nothing consumed them until now. `trace <report> <id>` (full id, an unambiguous prefix of ≥ 8 hex chars, or an interaction address for that call's trace) lists every call sharing the id, chronologically with offsets from the first — scenario-qualified address, service, summary, status (exactly paired), short span id. When any row's timestamp is absent or unparseable the whole trace falls back to file order with a `!` line, never a silent mix of two orderings. The command's second job is the warning nothing else in the tool can see: `! spans 2 scenarios (s3, s7) — shared state or fixture leakage`, the classic flaky-test smell. The footer states the known limitation honestly — parent span ids are not captured, so this is the chronology of the trace, not its tree. Ambiguous prefixes exit 2 listing candidates; an unknown id says how many distinct trace ids the report holds; untraced calls and unenriched reports are told to re-run on a current Kronikol.

## [3.0.56] - 2026-08-26

### Added
- **`kronikol query grep --number` — numeric-aware search** (`QUERY_V2_PLAN.md` Milestone 6). The number the user quotes is the *formatted* one while the payload holds the raw one, and `grep "4,173.00"` missing `4173` was a real failure of the tool's flagship use case. With `--number` the needle and every candidate token are compared numerically: `,`/`_`/spaces and leading currency symbols are stripped, and each token is read under **both** separator conventions — comma-as-thousands and comma-as-decimal — so `4.173,00` (European) matches `4173` rather than being misread as `4.173`. JSON bodies are walked by value (numbers compared numerically, strings scanned for embedded numeric tokens), so `--number` always emits the JSON path of each hit and notes when the raw text differed (`$.display = "4,173.00" (≈ 4173)`); non-JSON targets (uris, headers, steps, assertions, notes, non-JSON bodies) are token-scanned the same way. `--tolerance 0.5` (absolute) or `--tolerance 1%` (relative) widens the match; the default is exact with a 1e-9 relative epsilon so `4173.0` matches `4173`. A non-numeric needle exits 2 telling you to drop the flag. Everything else about `grep` — targets, dedup per distinct body, address output, paging — is unchanged.

## [3.0.55] - 2026-08-26

### Added
- **`kronikol query interactions --group-by` — generic bucketing** (`QUERY_V2_PLAN.md` Milestone 5). `--group-by service,status` (dimensions, any order: `service`, `method`, `status`, `path` — URI path with the query stripped — `step`, `phase`, `category`, `kind`/metaType, `capturedBy`) prints one row per bucket with calls, errors, median and max duration, and the number of *distinct response bodies* — a bucket with 120 calls and 1 body is one fact. Status and duration come from the exactly-paired response and errors from the shared classifier, so a `Created` is a success here exactly as in `services`. Index-only unless combined with `--where`; composes with every filter; `--sort errors|duration`; `--count` is the bucket count. Unknown dimensions exit 2 listing the valid ones; `--group` (adjacent-identical folding) and `--group-by` refuse to compose; at run scope a `step` dimension warns that step paths collide across scenarios. `services` stays as the curated view and the only answerer of negative questions — `--group-by` is the general form.

## [3.0.54] - 2026-08-26

### Added
- **`kronikol query diff` learned body addresses — structural body diff** (`QUERY_V2_PLAN.md` Milestone 4). The most common debugging move — "this call succeeded in the passing scenario, what was different in mine?" — used to require printing two bodies whole; now `diff <report> s3/i47 s7/i47` (or two `b:` hashes) prints only the differing paths: changed scalars as `$.total: 4173 → 3902`, type changes as `number 3 → string "3"`, an added or removed subtree as one row with a shape summary (`{sku, price, qty}` / `[3 elements]`) and never a dump, array length changes as their own row followed by per-index diffs. Byte-identical bodies answer from the index without reading anything. An array where a single insert shifted every later index collapses to one honest row (`$.items: elements shifted/reordered — 9 vs 10, 8 identical`) instead of a page of misleading per-index rows (LCS alignment deliberately deferred). Non-JSON bodies fall back to a line diff; two scenario addresses are refused with a pointer at `compare`; capture-time truncation markers are surfaced (in `values` too). Cross-run: `diff <old.json> <new.json> --body s3/i47` resolves the address in the old report and matches the scenario into the new run **by `stableId`** — ordinals shift between runs — then diffs that one call's bodies across the files.
- **`kronikol query compare` names the first differing body.** After the existing `bodies: 9 vs 9, 4 byte-identical` line it prints `first differing body: diff s3/i12 s7/i12` — the footer's claim that "the first differing call is usually the answer" is now an address instead of advice.

## [3.0.53] - 2026-08-26

### Added
- **`kronikol query --where` — body-content predicates on `interactions` and `values`** (`QUERY_V2_PLAN.md` Milestone 3). `--where "$.success = false"` filters calls by what their bodies actually say: grammar `[req:]PATH OP LITERAL`, ops `= != < > <= >= ~ !~ exists !exists`, numeric comparison when both sides are numeric and case-insensitive string comparison otherwise, `~` as substring. Wildcard paths use any-semantics (`$.items[*].price < 0` passes when any element satisfies), repeated `--where` composes as AND (OR is deliberately absent — run the command twice), the default target is the response body with a per-expression `req:` prefix for the request, and a call whose targeted body is missing or not JSON fails the predicate and is counted in a footer rather than silently dropped. A malformed expression exits 2 with the grammar in one line. `--where` joins the paging re-run footer, so an `--offset` continuation means the same thing.
- **`kronikol query interactions` is now run-scopable** — without a scenario address it lists matching calls across the whole run (rows already print full `s3/i47` addresses), which is half the value of `--where`: "which calls anywhere returned `success: false`" is one command now.

## [3.0.52] - 2026-08-26

### Added
- **`kronikol query values` — cross-body projection with aggregation** (`QUERY_V2_PLAN.md` Milestone 2). The SQL analog is `SELECT value, COUNT(*) … GROUP BY value` where the column is a JSON path evaluated across every matched body: `kronikol query values <report> --path '$.status'` prints each distinct value with its occurrence count and example addresses; `--stats` summarises a numeric path (count/absent/non-numeric/distinct, min/median/max/sum/mean — min and max carry the address of the extreme, because the outlier is usually the next thing to fetch). Scope is the whole run or one scenario (`s3`), the `interactions` filters (`--service`, `--status`, `--method`, `--step`, `--grep`) all apply, `--request`/`--both` shift the target from the default response body (rows tagged `req`/`resp` under `--both`), and wildcards fan out (`--path '$.items[*].price'` counts every element of every body). Counting is per occurrence — the question is "what did the system see" — while each distinct body is parsed exactly once through the new `BodyCache`. A body the path misses counts as `(absent)`; bodiless calls, calls with no response to evaluate (fire-and-forget events included — though an event with a tracked response participates normally) and non-JSON bodies are footnoted, never silently dropped. Kronikol4J: explicitly none — the tool is .NET-only and the report format is untouched.

## [3.0.51] - 2026-08-26

### Added
- **`kronikol query` path engine — the `--path` grammar grew wildcards, quoted keys and `length()`** (`QUERY_V2_PLAN.md` Milestone 1). `--path '$.items[*].price'` fans out and prints one row per match, each with its *concrete* path (`$.items[2].price = 4173`) so every emitted row is itself a valid `--path` input; `['a.b']` bracket-quotes a property whose key contains dots; `$.items.length()` answers "how many" directly (array → element count, object → property count, string → char count — any other kind explains what the kind was instead of missing silently). A missed property now suggests the nearest key actually present (`$.data.custmers is not in this body — nearest: $.data.customers`), and a path resolving to an array or object bigger than the byte budget *describes itself* — kind, element count, size, and the flags that window it — instead of refusing. All of it lives in one place (`Query/PathEngine.cs`): the resolver, the `--keys` walker and grep's `--values` walker were three copies of the same recursive walk and are now one, so a path printed by any command parses back to the same value everywhere. New shared flags (`--where`, `--group-by`, `--stats`, `--request`/`--both`, `--number`, `--tolerance`, `diff --body ADDR`) are parsed and reserved for the v2 query commands landing next.

### Fixed
- **`kronikol query` paired a request with the wrong response under interleaved parallel calls.** The pairing key has always been in the file — `requestResponseId`, the same identity the diagram pipeline groups on — but the tool's scanner dropped it and paired by proximity ("next response from the same service within four entries"), so two in-flight calls to one service could swap statuses, durations and response body pointers in `interactions`, `flow` and the `--status` filter. The scanner now keeps `requestResponseId` (and `traceId`), pairing is exact wherever the id exists, and the proximity heuristic survives only for entries that genuinely carry none (markers, user actions, unpaired captures).
- **`flow --errors-only` and `services` disagreed about what an error is.** `flow` flagged any non-`OK` text status — including `Created`, `Accepted` and `NoContent`, which `services` correctly counted as successes. One shared classifier now serves both (and everything built later on it): numeric ≥ 400 is an error, text statuses are errors unless they are a known success name, so a `Created` response is never an "error" in one command and fine in another.

## [3.0.50] - 2026-08-25

### Changed
- **PlantUML JS engine re-based on npm [`@plantuml/core`](https://www.npmjs.com/package/@plantuml/core) 1.2026.6 (MIT)** — `TrackingDefaults.PlantUmlJsCdnBase` now points at `…@v1.2026.6-patched`, the new build published with the same two `4096.0 → 98304.0` size-limit patches as before, plus a correction the old build lacked: the *"Diagram too large for browser rendering"* message now says `(max 98304)` instead of the unpatched `(max 4096)` it printed while actually enforcing 98304. The new engine renders faster and emits ~35 % smaller SVG — on the E2E large-report fixture (6 diagrams, 70 fragment renders): first worker ready 0.78 s (was 1.0–2.3 s), full render 3.7 s (was 4.0–11.2 s), note toggle 0.47 s (was 0.54–1.5 s), total worker render time 8.9 s (was 9.9–32 s); geometry and element counts are pinned identical between worker and main-thread output. The build is an ES module (`export { render, renderToString }`, no `plantumlLoad`): the browser shim and worker host already handled that form; `plantuml-render.js` (NodeJs rendering) now rewrites the trailing `export` into an assignment before `vm.Script` compilation, and the browser's main-thread fallback detects the missing `plantumlLoad` after script-tag loading and `import()`s the engine instead. The statement-length limits in `PlantUmlStatementLimits` were re-measured against the new build and are unchanged.

### Fixed
- **A note/assertion/step toggle on a fully cached diagram blocked the main thread for the sum of all its fragment swaps** (~½ s on a large diagram under load): after a prefetch, every fragment is a synchronous cache hit and the two toggle loops chained them in a single task. They now yield between fragments, so the worst main-thread task during a toggle is one fragment's `innerHTML` swap.
- **A CDN engine change never reached machines with a warm cache.** `NodeJsPlantUmlRenderer` cached `plantuml.js` / `viz-global.js` under `%LOCALAPPDATA%/Kronikol/plantuml-js/` keyed by filename alone and skipped the download when the file existed — so after any `PlantUmlJsCdnBase` change, every machine that had ever rendered kept the old engine forever. The cache directory now carries the CDN tag (`…/plantuml-js/v1.2026.6-patched/`), and the V8 code cache moves with it.

## [3.0.49] - 2026-08-24

### Added
- **`IngestRequest.WindowAttribution` — how window attribution resolves overlapping test windows.** `AttributeByTestWindow` has always filed a record inside several windows under the test that *started latest* — right for suites that nest tests, and simply wrong for suites that run tests **concurrently**, where "latest started" is just "whichever parallel worker began most recently" and arrows migrate to a stranger's diagram. The new `WindowAttributionMode.ExclusiveOnly` attributes a record only when **exactly one** window contains its timestamp; a record inside two or more stays unattributed — honestly ambiguous, reaching `FoldUnknownTestsInto` / `DropUnattributed` like any other unowned traffic — and is counted in a diagnostic that is emitted *even at zero*, because the line's presence is what proves the mode ran. With one worker windows never overlap, so `ExclusiveOnly` is behaviour-preserving there; the default remains `InnermostWins`, byte-for-byte the old rule.
- **`IngestRequest.AttributeByClaims` — content-based attribution for parallel workers.** A capturer that sees no test identity on the wire (a Redis tee: StackExchange.Redis multiplexes every command onto one connection) cannot be window-attributed exactly under parallelism — but when concurrent tests touch **disjoint** data, the data itself names its owner. A test may now declare `claims` (literal fragments: customer ids, cache-key parts) on its tests-NDJSON `start` record, or add them mid-run with a new `{"event":"claims", …}` line (unknown to the scenario synthesiser, so it renders as nothing). The new pass runs before window attribution and assigns a still-unowned record to the test whose window contains its timestamp **and** whose claim appears literally in the record's URI or body — when exactly one such test exists; several claimants is counted and left for the window pass. A test that roams over shared data should claim everything it touches, which turns its shared records ambiguous (honest) rather than exclusively someone else's (wrong). `TestRunRecord.Claims` and both passes are additive and default-off; no consumer changes behaviour without opting in.
- **`IngestAttribution.AttributeByWindow` overload with a `WindowAttributionMode`** returning `(Records, Attributed, Ambiguous)`, plus `BuildClaimWindows` / `AttributeByClaims` / `ClaimWindow` for hosts that drive the passes directly. The existing two-tuple overload is untouched and delegates with `InnermostWins`.
- **`TcpTapOptions.ReapStuckConnectionsAfter` — the stuck-connection reaper**, the one intervention that ever touches forwarding, added for a failure mode observed live (2026-08-24): a machine-wide stall wedged StackExchange.Redis 2.6.x *inside the tapped service* — its pipe writer jammed with the socket still open, and from then on every command sat queued forever (no completion, no timeout, no reconnect; a process dump showed 413 requests parked in `RedisRepository.TryGetAsync`), taking every request in the service down until the stack was restarted. Such a client recovers from a **closed socket** — it reconnects and fails its backlog fast — where it never recovers on its own. With the option set, the tap closes any connection on which a **decoded command has sat unanswered** for the configured span. That signature was chosen the hard way: a first version reaped on *silence*, and promptly killed healthy idle **pool** connections (a Mongo pool member between checkouts is silent for minutes; per-connection keep-alives are not universal), disrupting the drivers it was meant to protect. An unanswered command has no such false positive — Redis and Mongo answer in milliseconds, and an idle connection has nothing unanswered. The pending state comes from the protocol decoders via the new `IProtocolDecoder.OldestUnansweredSince` (default-null default interface member — custom decoders are unaffected and simply never reap); when decoding has been disabled for a connection the tap is blind there and never guesses. Reaps are counted (`StuckConnectionsReaped`), logged, reported through `OnCaptureDegraded` (new `CaptureDegradationKind.StuckConnectionReaped`) and summarised in `Diagnostics()`. Default **null (off)**.

## [3.0.48] - 2026-08-23

### Added
- **`ReportConfigurationOptions.SeparateBackgroundSteps`** (default `false`) and **`CollapseRepeatedStepKeywords`** (default `true`) — see below.
- **`PlantUmlStatementLimits` — the engine's measured statement-length limits, enforced at generation time.** PlantUML's parser refuses a message statement longer than 2,000 characters, and it does not say so: the statement matches no rule, the parser abandons the whole diagram, and the fragment renders as `Syntax Error?` with every other call in it gone. Reported against a real report where one Redis `DELETE` of 41 cache keys produced a **5,410-character** arrow — `maxUrlLength` (default 100) decides where a long URL *wraps for display*, not how long the label may get, so 5,300 characters of path became 53 display chunks joined by a literal `\n        ` and one statement nothing bounded. Client-side splitting could not help: it splits *between* lines, so the over-long statement landed intact in whichever fragment held it and only that fragment failed. Measured against the engine Kronikol ships (`plantuml-js …@v1.2026.3beta6-patched`), the limits are **per statement kind, not one line limit**, and they fail in two different ways: a message (**2,000**, exactly, on the whole trimmed statement — a longer participant alias leaves a shorter label, not a longer statement) or a block opener (`loop` 1,476, `alt` 1,477, `group` 1,482, `opt` 1,484) draws `Syntax Error?`, while a **coloured** `hnote across` past **1,458** — the exact form the step-delimiter bar uses — overflows the engine's own JavaScript stack (`RangeError: Maximum call stack size exceeded`) and produces no SVG at all, costing the scenario every diagram it had rather than one statement. Note bodies, one-line `note over` statements and *uncoloured* `hnote across` bars run to ~16,400 and are left alone: a Gherkin step with a doc string is legitimately long. The constants sit under the measurements (2,000 / 1,471 / 1,400 / 16,000) so a small engine drift does not reopen the bug, and the boundaries are pinned by integration tests against the real engine — including the fact that a *silent* failure is possible, because where PlantUML's fallback class-diagram parse happens to succeed it draws the wrong diagram with no banner at all. **The 2,000 message limit is PlantUML's own; the other two are the JS build's.** Measured the other way as well, against real Java PlantUML through `Kronikol.PlantUml.Ikvm`: Java refuses 2,001 exactly as the JS build does, but draws a 2,000-character `loop` label and a 3,000-character coloured `hnote` without complaint. So the message cap is load-bearing for every renderer while the other two are concessions to the default one — and because the same generated source may be rendered either way, all of them are applied where the source is written rather than where a renderer is chosen. `IkvmStatementLimitTests` pins the split. Measuring it is easy to get wrong, and the first version of that test was: when a message statement is too long the parser falls back to reading the source as a *class* diagram, and that fallback often succeeds — echoing the label text and emitting no `Syntax Error?` banner — so "is the label in the SVG?" passes for both outcomes. It reported "Java has no limit", and CI caught it. The signal that separates them is that a sequence diagram draws each participant twice, as a head box and a foot box.

### Changed
- **Background steps render inline with the scenario's own steps.** A scenario's background — a Gherkin `Background:` block, a `background: true` record from an external capture, or the common step prefix `BackgroundStepsDetector` finds — used to sit behind its own collapsed `Background Steps` disclosure above `Steps`. It is now the first entries of the one `Steps` list, marked `step-background` (a muted left accent) so the seam stays visible, numbered continuously with the rest, and open by default. That is the order everything else already used: interaction attribution and the `stepPath` addresses in the data files count background steps first (`b0`, `b1`, then `0`, `1`), so the rendered list and the data now read the same way. It also closes a dead end — `toggle_expand_collapse` is wired to `details.feature` and `details.scenario` only, so **"Expand All Scenarios" never opened the Background section**; every E2E test had to click its summary explicitly, and a misfiring common-prefix heuristic could hide real steps behind a collapsed disclosure. `SeparateBackgroundSteps = true` restores the two-section layout in one line. Both HTML reports go through the same renderer, and the ~35 lines of duplicated step-rendering markup between the plain-scenario surface and the parameterized-group detail panels are now one method, so the two cannot drift apart again.
- **A step repeating the primary keyword in force displays as `And`.** With the two lists combined, a background `Given` followed by a scenario `Given` would otherwise read `Given / Given`. `StepKeywordCollapser` now renders the second as `And`, in the casing of the keyword it replaces, using the same keyword vocabulary `IngestAttribution.PhaseForStep` already established rather than a second table. It applies to the whole rendered list, so `Given / Given` inside a plain scenario collapses too; `CollapseRepeatedStepKeywords = false` turns it off. **This is a render-time projection and never touches the model** — which is not a stylistic preference: `BackgroundStepsDetector` assigns the *same array instance* to every scenario in a Rule group, and `RunOutputs` writes the HTML and the JSON/XML/YAML data files under `Parallel.Invoke` over those same objects, so rewriting a keyword in place would be a data race that leaked `And` into the data files. A regression test generates the HTML and then asserts the model still says `Given`. Localisation is a deliberate non-goal: an unrecognised keyword (`Angenommen`) passes through untouched and switches collapsing off until the next keyword the collapser understands, so a non-English feature file renders exactly as before. ReqNRoll supplies an English keyword enum, so only the Cucumber-messages ingest of a localised feature is affected.
- **Request arrow labels are capped at the engine's limit, and the full path moves to the note.** The label is now bounded against the real statement — the `{caller} -{colour}> {service}: ` prefix, whose length varies with the aliases and the colour token, plus the `[[#iflow-{guid} …]]` wrapper when internal-flow tracking is on, since cutting inside that would leave the link unclosed. A truncated label ends with `…`, and the cut never strands a backslash from the `\n` escape it belongs to. **Nothing is lost:** for a `DELETE` with no body the path *is* the payload, so the untruncated `PathAndQuery` is appended to the request note under a `[Full path]` heading, chunked at `MaxNoteChunkChars` like every other note value — note bodies are uncapped at this scale, so it stays visible, searchable and copyable. User-action arrows (a long Playwright locator is one message statement), response labels and `loop`/`partition` labels are capped the same way, and the step-delimiter bar is capped at both its emitters (`InteractionRecord.StepDelimiterPlantUml` and `StepCollector`).
- **One guard in `DiagramBuilder` makes it an invariant.** Every generated line — `AppendLine` for Kronikol's own statements and `Append` for raw passthrough of `trace.PlantUml` — passes through a stateful classifier that caps only what the engine caps, so a future emitter that forgets cannot reintroduce the failure. It tracks note blocks across lines, because a note body is captured payload that may contain anything resembling an arrow statement, and leaves notes, comments, preprocessor directives and participant declarations exactly as they were. A corpus test asserts no diagram the test suite generates exceeds any limit.
- **Component-diagram edge labels get a defensive ceiling too.** `ComponentDiagramGenerator` builds `caller -[#colour]-> service : "label"` edges whose label grows with the method list and with whatever a `RelationshipLabelFormatter` returns. Component diagrams go through a different parser whose limits are **unmeasured**, so this is a ceiling at the sequence-diagram message limit rather than a measured one — real labels are two orders of magnitude below it, and the cost of being wrong is the whole diagram rather than one edge.
- **A `Syntax Error?` fragment now says which line was too long.** `describeEngineFailure` already attached the raw source in a `<details>` — the only reason this was diagnosable at all. It now also classifies that source and, when a statement is over its limit, states the line number, the kind and the length, alongside the fact that the parser abandons the entire diagram rather than the one statement.

### Fixed
- **Background step text was not searchable.** `CollectStepText` was called on `scenario.Steps` at all four sites that build the report's `data-search` / `data-row-search` attributes, and never on `BackgroundSteps` — so a phrase that appeared only in a background step could not be found by the search box, in either the scenario list or a parameterized group's rows.
- **Step numbering restarted between the two lists.** With `showStepNumbers`, the background list numbered `1..n` and the steps list started again at `1`, so two different steps in one scenario were both "1.". Numbering is now continuous in both layouts — in the separated one the `Steps` section carries on from where the background ended.
- **A scenario whose steps were *all* background rendered no detail panel in a parameterized group.** `hasAnyDetail` consulted `s.Steps` and the failure state, never `s.BackgroundSteps`, so a passing scenario whose entire step list had been extracted as background produced no `.param-detail-panel` and its steps vanished from the report. This is reachable, not theoretical: `BackgroundStepsDetector` deliberately permits the whole list to become background, which a scenario outline whose example rows share identical step prose hits exactly.
- **The Features Summary under-counted steps.** `hasAnySteps` and the per-feature `allSteps` were built from `s.Steps` alone, so the four step columns under-reported by the size of the background — and a feature whose steps had *all* become background lost the columns entirely. Combined rendering made the mismatch plainly visible: the table said 2 where the scenario below listed 4.
- **`Specifications.yml` / `.json` / `.xml` dropped background steps entirely.** The Specifications HTML has always shown them; the machine-readable files beside it — the living-documentation artefact — emitted `Steps` and nothing else, in all four writers including the public `GenerateYamlSpecs`. They now emit a `BackgroundSteps` sibling, shaped like the `TestRunReport` writers that already got this right, and omitted when a scenario has none. Deliberately **not** merged into `Steps`: that would be lossy and would break the `b{i}`/`{i}` path convention the step paths and interaction attribution depend on. `SpecificationsDataTests` had no background case at all, which is why this survived.
- **`ReportConfigurationOptions.InlineBackgroundSteps` is deprecated — it never did anything.** It was declared, documented in the wiki and the changelog, and **never read by any code path**: `GenerateHtmlReport` had no corresponding parameter, and its one test called a helper byte-identical to the plain one, asserting only that both step texts appeared, with a comment at line 839 admitting `// inlineBackgroundSteps option not yet implemented`. It passed vacuously for six months. A new name rather than a flipped default because `InlineBackgroundSteps = false` would now have to mean "separate", which reads backwards, and because a `bool` cannot distinguish "left at default" from "explicitly `false`" — honouring the old property would silently give anyone who copied the wiki's `InlineBackgroundSteps = false` example the *old* behaviour, from a property that never had any.

## [3.0.47] - 2026-08-23

### Added
- **Toolbar buttons show that a re-render is in flight.** Clicking Expand / Collapse / Truncate, the lines selector, or Headers / Assertions / Steps / Databases marks the clicked control and every peer synced with it as pending (`details-pending`, `aria-busy`) until the last diagram of that action is drawn: the label throbs and a small spinner appears in the button — both delayed by 0.2 s, so a fast re-render (a cache hit, one small diagram) shows nothing and a slow one is clearly "loading" from the button itself. A control stays pending until its last overlapping action completes; a click that changes nothing clears at once.
- **The scenario diagram toolbar sits on the title line.** The Details / Headers / Assertions / Steps / Databases buttons (and the Sequence / Activity / Flame tabs when a scenario has several diagram types) now float to the right of the "Sequence Diagrams" / "Diagrams" title, on the same line, when both fit side by side — and stack under the title, full width, as before when the container is too narrow (a phone, a narrow pane, a scenario with many toggles). `diagram-toggle-layout-script.js` measures each toolbar against its title (re-evaluated on resize, on every toggle click and when a scenario opens) and sets `data-layout="inline"`; the CSS lives under `.diagram-toggle[data-layout="inline"]`.
- **`kronikol query` — debug a test run without reading the report.** A real `TestRunReport.json` measured on 2026-08-22 was **10.7 MB ≈ 2.7M tokens**, with a single embedded diagram of **663 KB ≈ 166k tokens** — one diagram larger than most context windows. An AI agent asked to debug a failing test opens the file and spends its entire context before reaching the question, and the wiki's own `AI-Integration-Prompt` told it to. The new subcommand answers questions about a report instead of loading one: `summary`, `scenarios`, `failures`, `steps`, `assertions`, `flow`, `services`, `interactions`, `annotations`, `http`, `body`, `note`, `diagram`, `grep`, `compare`, `diff`. It never loads the file — one `Utf8JsonReader` pass over a sliding window (which grows when a single token will not fit, because a diagram *is* one JSON string) builds an index of the narrative, the topology and, for every payload, a content hash, a length and a byte offset; a payload is seeked to only when it is named. Four rules make the output usable by an agent: **stable addresses** (`s3`, `s3/i47`, `s3/2` — the same value as the report's `stepPath` — `b:4bdea521`, `s3/d0/n12`), every one of them valid input to another command; **elide, never omit** (`body: 2.7 KB · b:4bdea521 (×28 in this report)` says what exists and how to get it, where silence would send the reader back to `cat`); a **byte budget** (`--max-bytes`, default 6000) with truncation always announcing the exact re-run (`calls: 1-24 of 127 · next: --service redis --offset 24`); and **aggregation before pagination** (`--group` folds a run of identical calls into one row with `×N`). `services` is the only view that answers a *negative* question — a service absent from its table was never called — and `grep "<value>" --values` names the JSON path a wrong number came from, which is the question a fully passing suite still leaves open. `diagram` refuses stdout outright and points at `flow`, which tells the same story in 1–2 KB. `--out FILE` costs six tokens instead of sixteen thousand. Reports written before this release are read too: the tool detects the missing attribution and prints one header line saying the answer came from thinner data, rather than quietly returning less. A `mergeableFormatVersion` it does not understand is a hard error, not a best-effort read.
- **A `kronikol-test-debugging` skill, in `templates/skills/`.** Without it agents `cat` by default, so the tool goes unused. The skill carries the rule (with the number attached — a prohibition whose reason is missing gets ignored), the four-layer model of a report so an agent knows what is cheap *before* it asks, the command ladder with an instruction to stop at the first rung that answers the question, recipes keyed to what a user actually says ("the number on screen is wrong", "did it even call X?"), budget discipline, and the traps — notably that a diagram note is a *rendering* of a payload rather than a copy of it, so a value the user quotes and you cannot find is reachable through `query note`. `scripts/query.py` implements `summary`, `failures`, `steps`, `services`, `grep` and `http` against the same addressing, so a machine without the dotnet tool degrades to fewer answers rather than to reading the raw file.
- **Interactions in the data files now carry everything the diagram knows about them.** `MapLogJson` emitted 11 fields and dropped the rest of the record, so the JSON/XML/YAML views were strictly poorer than the picture drawn from the same objects. Added: `metaType` (an event was previously indistinguishable from a request/response exchange outside the diagram), `dependencyCategory` and `callerDependencyCategory` (what drives participant shape and arrow colour — whether `redis` is a cache and `mongo` a database), `phase` (whether a call happened during setup or the action under test), `isUserAction`, `activityTraceId` and `activitySpanId` — **the bridge to OpenTelemetry traces and application logs**, where the long-standing `traceId` is Kronikol's own identifier for the request/response pair and not a W3C id — `capturedBy` (`wire` or `span`, which matters when capture fidelity is itself the bug), and a derived `durationMs` computed from the request/response timestamp pair and repeated on both halves. Mirrored in the XML and YAML writers (which omit what carries nothing, as the rest of those writers do) and in both generated schemas.
- **Scenario `annotations`: which example row was in flight, and any fragment the test author injected.** A `TabularInputs<T>` iteration emits a `Row 3` band into the diagram and `DefaultTrackingDiagramOverride.InsertPlantUml` splices in whatever the author wrote — both as marker records whose payload is the `PlantUml` property, which no export ever emitted, so this content was write-only. `RequestResponseLog.MarkerKind` (`Custom | Step | Assertion | Row | Phase`) is now set by each emitter — classifying at the source is exact, where recovering the same answer at the sink means pattern-matching PlantUML — and `Row`/`Custom` markers are exported as `annotations: [{ index, kind, text }]` against the interaction they preceded. `Step` and `Assertion` markers are deliberately excluded: they are already structured in `steps`, and repeating them would be duplication rather than disclosure.
- **`stepPath` connects `steps` to `httpInteractions`.** Nothing joined the two; the connection existed positionally in the log stream and was consumed only by the diagram. Each interaction is now stamped with the step it happened under (`b0`, `0`, `1` — background steps first). Attribution is positional and sound because `RequestResponseLogger` is a FIFO queue, so one test's records keep their relative order however many tests run in parallel; it is *not* sound for a test doing work on a background thread, where a record can enqueue after the following step's marker — so the marker's text is checked against the step's, and a disagreement yields a null `stepPath` and a new `DiagnosticKind.StepAttributionMismatch` rather than a confident wrong answer.
- **`ReportConfigurationOptions.TestRunReportFullStepDetail`** (default `true`) — see below.

### Changed
- **The standard `TestRunReport.json` now carries full step detail.** `BuildFeaturesJsonModel` was called with `fullStepDetail: false` for the standard file and `true` only for the mergeable one, which `GenerateMergeableData` leaves off by default — so in the default configuration the richer mapper never ran and a parameterised test's inputs (its data-table rows, its example row values, its doc strings and comments) were absent from the file tooling reads, although the code to serialise them existed and was tested. A parameterised failure now shows what produced it. Turn it off with `TestRunReportFullStepDetail = false`; step detail is kilobytes against megabytes of payload, so the option exists for completeness rather than as a recommendation.
- **`stableId` folds in a scenario's example values — parameterised scenarios get new ids once.** `ScenarioStableId.Compute` hashed `featureName::outlineId::scenarioDisplayName`, so for a scenario outline whose rows share a display name every row hashed identically. Measured on the checked-in ReqNRoll example report: **6 scenarios, 4 distinct stableIds**, with three rows of "Different muffin recipes should produce the expected batch" all carrying `e9bf0e2c34b5a8fd` and differing only in `exampleValues`, which was not in the hash. The schema documents the field as the key for matching a test across runs — and per-row matching is precisely the case where it matters, so `diff` could not tell row 1 from row 3. The ordered example values are now part of the hash (order-independent, so a re-ordered example table does not churn ids). **This is behavioural**: anyone storing `stableId` historically for parameterised scenarios sees a one-off discontinuity. Non-parameterised scenarios are unaffected, and no code in `src/` consumes the field — the merge path matches on something else.

### Fixed
- **Changelog correction: the two diagram-toolbar entries above were listed under 3.0.45, which does not contain them.** `git show v3.0.45` has neither `diagram-toggle-layout-script.js` nor `renderWithPending`; both features were still uncommitted when that tag was cut, so the release notes described behaviour nobody could obtain by installing 3.0.45. They are moved here, to the release that actually ships them. This is the second instance of the same failure mode in two releases — 3.0.46 records that 3.0.45 also "shipped its call sites and its changelog prose without its one definition" — which makes it a release-process problem rather than two coincidences: the notes are being written from the working tree instead of from what is committed.
- **Report tests could not tell an element from a script that selects it.** Five tests searched the whole generated HTML for a marker — `data-toggle="databases"`, `data-toggle="headers"`, `data-toggle="assertions"`, `.truncate-lines-select`, the text `Sequence Diagrams` — and took a hit as proof the element was rendered. A report is one self-contained file whose behaviour ships as inline `<script>`, and those scripts address elements by exactly those attributes, so adding `renderWithPending(queue, document.querySelectorAll('.toggle-btn[data-toggle="databases"]'))` put the marker into the document ~48 KB ahead of the first `<button`: `LastIndexOf("<button", idx)` returned −1 and the test died with `ArgumentOutOfRangeException` instead of asserting anything. The markup was correct throughout — only the searches were wrong, and one was matching a CSS comment. New `ReportMarkup` test helper strips inline `<script>`/`<style>` and matches element open-tags, so these assertions are about what the report renders.
- **A failing step's and a failing assertion's message reached the sequence diagram and nothing else.** `Track.LogAssertion` receives the failure message and the caller's file and line, renders both into a PlantUML note, and then called `StepCollector.AddAssertionSubStep(testId, text, passed)` — text and a boolean. Separately, `CollectedStep.ErrorMessage` was set when a step failed and then dropped in `ToScenarioStep`, which had no field to put it in. The scenario-level `errorMessage` survived, so you could learn *that* a test failed but, with nested sub-steps, not which one carried which message — and the two most-wanted facts about a failure, why and where, were legible only by reading a 663 KB diagram. `ScenarioStep` now has `FailureMessage`, `SourceFile` and `SourceLine`; the assertion path threads all three through; both step mappers emit them, and XML and YAML too. They are written **regardless of `TestRunReportFullStepDetail`** — the smaller file exists to save payload bytes, not to withhold why a test failed. `kronikol query failures` answers "why" without a diagram.
- **The NDJSON ingest round trip was lossy: `durationMs` was accepted and then dropped.** `InteractionRecord` documents `durationMs` on the interaction contract, and `ToLog` mapped every field onto the record except that one. It was consumed for flow nesting and step durations, so a capturer that sends **one record for a whole call** — which the contract permits, and which has no second timestamp to derive from — watched Kronikol use the value and then found it absent from the report. It now lands on `RequestResponseLog.DurationMs` and wins over the derived value, closing the round trip.
- **Captured payloads lost characters to PlantUML's creole markup inside diagram notes.** Note bodies were escaped for backslashes only, so any creole marker a payload happened to carry was read as formatting and consumed. The case that surfaced it: a BigQuery job body whose `query` value is one PlantUML line — its four `-- ` SQL comments paired up into strikethrough, deleting the `--` markers and striking through the text between them. The same hole applied to two `https://` URLs on one line (italic, both `//` deleted), `**`, `__` and `""` pairs, a `[[…]]` that parses as a link, a line opening with `*`, `#` or `=`, and any tag PlantUML knows (`<b>`, `<color:red>`, `<font …>` in an HTML body) which was consumed wherever it appeared — payload text could therefore restyle the note it sat in. Note bodies and header values are now creole-escaped before Kronikol adds its own markup, and only where PlantUML would actually consume something: a marker needs a partner on the same line to style anything, so a lone `https://` is left exactly as captured and the escape adds no noise to the common case. Kronikol's own markup — the gray header tags, `<i>[binary content]</i>`, focus emphasis, the form-encoded `&` divider — is added after escaping and is never escaped; a `~` escape is never split from the character it protects by note chunking or long-run wrapping. Verified end to end against the real engine: the SQL comments, both URLs and a literal `<b>raw</b>` now reach the SVG as text, with no strikethrough.
- **`Kronikol.Templates` shipped at 2.37.2 no matter what the release was versioned.** The project set its own `<PackageVersion>2.37.2</PackageVersion>`, which overrides the `<Version>` every other package inherits from `Directory.Build.props`, so every release since 2026-05-24 packed 2.37.2, hit a 409 from nuget.org and was silently dropped by `dotnet nuget push --skip-duplicate` — 58 of the 59 packages in the 3.0.46 release published, and nothing in the job failed. Its content happened to be current, so no template shipped stale, but any edit to `templates/` would have gone unpublished until someone hand-bumped that line. The pin is removed: the package now takes the shared version like the rest, and `-p:Version` from the release tag flows through it.

## [3.0.46] - 2026-08-23

### Fixed
- **`Kronikol` did not compile at 3.0.45.** `ReportGenerator.GenerateTestRunReportData` filtered its tracked logs on `l.IsDiagramMarker`, but the property was never added to `RequestResponseLog` — the marker/interaction split shipped its call sites and its changelog prose without its one definition, so the tag builds from a clean checkout only as far as `src/Kronikol`. `RequestResponseLog.IsDiagramMarker` is now defined as `IsOverrideStart || IsOverrideEnd || IsActionStart`. It is a distinct property from the same-named `TestRunRecord.IsDiagramMarker` on the ingestion side, which keys off event names and timestamps; the 3.0.45 entries describing the export and diagnostics fixes are accurate as of this release.
- **`Kronikol.Extensions.ServiceBus` failed to restore (NU1605).** `Azure.Messaging.ServiceBus` floats on `7.*`, and 7.20.2 pulls `Azure.Core` 1.60.0 → `Microsoft.Extensions.Hosting.Abstractions` 10.0.9 → `Microsoft.Extensions.DependencyInjection.Abstractions` >= 10.0.9, while the net8.0/net9.0 pins sat at 10.0.7. NU1605 is in the SDK's default `WarningsAsErrors` list, so the downgrade failed the build outright with no `TreatWarningsAsErrors` anywhere in the repo. The pins are now 10.0.9 (net10.0 was unaffected — `10.*` floats above the floor). No commit caused this: the floating reference drifted into it on an upstream release.

### Changed
- **CI builds every packable project.** A new `Release Build (packable projects)` job mirrors `release.yml` (`dotnet restore release.slnf` → `dotnet build release.slnf --configuration Release`). The test matrix only builds what a test project references, so a packable project with no CI test coverage — `Kronikol.Extensions.ServiceBus` among them — could break and stay green until tag time. With 29 floating `X.*` package references across `src/`, any upstream release can reproduce that failure mode without a local change; this job turns it into a red PR instead of a failed release.

## [3.0.45] - 2026-08-22

### Added
- **Browser rendering runs off the main thread: the PlantUML engine now lives in Web Workers, with a per-page SVG cache and parallel prefetch for toggles.** `PlantUmlRendering.BrowserJs` reports used to load the 7 MB TeaVM engine with two `<script defer>` tags and render every diagram on the main thread, one at a time — the page was not interactive for seconds, a large report froze the browser for the whole render, and a note toggle on a big diagram blocked input for 3–7 s. The report page now fetches `viz-global.js` + `plantuml.js` as text and builds Blob workers from them together with a new embedded worker host (`plantuml-worker-host.js`: the minimal DOM the engine needs, an SVG serializer and the render protocol — a Blob worker created by a `file://` page cannot load anything over the network, which is why the page does the fetching). One worker starts at once; up to `BrowserRenderWorkers` (default 4, capped by `navigator.hardwareConcurrency`) start lazily after the first render. `window.plantuml.render(lines, targetId)` keeps its shape for every caller (the render queue, the note/assertion/step toggles, the internal-flow popup) — the worker's SVG lands via `innerHTML`, firing the same `MutationObserver`s as before, and worker output is identical to main-thread output (fidelity test). The render queue keeps one fragment per worker in flight; every successful render fills a bytes-bounded cache keyed by fragment source (`BrowserRenderCacheMegabytes`, default 64), and the toggle paths call the new `window.plantuml.prefetch(sources)` so the workers render a toggle's new fragments in parallel while the sequential swap serves cache hits. A worker failure takes exactly the path a synchronous engine throw took (the too-large re-split retry, the red block with the collapsed **Raw PlantUML**). Fallback to the previous main-thread path is automatic when Workers/`fetch`/`OffscreenCanvas` are unavailable or the engine cannot be fetched (offline with a cold cache, a CDN without CORS); `BrowserRenderWorkers = 0` selects it explicitly; `window.__kronikolRender` tells you which mode ran. New options: `ReportConfigurationOptions.BrowserRenderWorkers` (4), `BrowserRenderCacheMegabytes` (64), `BrowserFragmentMaxHeight` (12000 — exposes the existing split height; 4,000–6,000 renders ~20 % faster at the cost of more seams); `kronikol ingest --browser-render-workers <n>`. Measured on a real 20-diagram report (headless Chromium): page interactive 1.7–5.6 s → 0.08 s; full render 28–46 s with 22–39 s blocked → 8.5–9.2 s with 0.1–0.2 s blocked (worst task 4.8 s → 76 ms); note toggle 3.9–7.1 s → 0.9 s. On the E2E large-report fixture (6 note-dominated diagrams, `LargeReportFixture`): interactive 4.1 s → 0.44 s; full render 13.2 s with 11.4 s blocked → 4.4 s with 0 ms blocked (worst task 452 ms → 0); note toggle 2.6 s → 0.57 s; worker output verified identical to main-thread output (`BrowserRenderWorkerTests`). Design, measurements and the bench harness: `BROWSER_RENDER_WORKER_PLAN.md`, `tools/render-bench/`. Wiki: *PlantUML Browser Rendering → How Rendering Runs*.
- **Node renderer: one process per report instead of one per diagram, and a V8 code cache for the engine.** `NodeJsPlantUmlRenderer.RenderMany` streams every diagram of a report through a single `node` process as NDJSON (`{"id","source"}` in, `{"id","svg"}` / `{"id","error"}` out, errors isolated per diagram, unique target ids per render so TeaVM state cannot leak between diagrams); `DefaultDiagramsFetcher` uses it for `PlantUmlRendering.NodeJs`, so the per-diagram cost of node start + engine compile + warm-up (0.8–1.4 s each) is paid once. `plantuml-render.js` compiles the engine through `vm.Script` with `cachedData` persisted next to the downloaded engine (`plantuml.js.v8cache`, written on first run, regenerated when V8 rejects it after a node upgrade), cutting the engine compile from ~160 ms to ~1 ms. The single-diagram stdin mode is unchanged.

### Fixed
- **`TestRunReport.json` / `.xml` / `.yml`: Gherkin steps and assertions no longer appear as content-free calls to `http://override.com/`.** The override start/end pair that splices a step bar, an assertion note or a custom fragment into a sequence diagram — and the Setup/Action boundary marker — travel in the same tracked-log stream as real traffic, carrying nothing but their PlantUML. Every other consumer skips them (the sequence diagram, the component diagram, the flow-segment builder, the collapser); the data exports did not, and since `MapLogJson` does not emit `PlantUml`, each one reached the file as an empty `Request` with no method, service, headers, content or status — one pair per step and per assertion. In a 19-scenario Playwright run that was **424 of 1202** exported interactions, 35 % of the list, and nothing in the record said which step it came from. `RequestResponseLog.IsDiagramMarker` now names the concept and `GenerateTestRunReportData` filters on it for all three formats: a scenario whose only logs were markers gets an empty `httpInteractions` array in JSON and no `HttpInteractions` element/block in XML/YAML. The diagrams are unaffected — the markers still do their work there.
- **`Report diagnostics` no longer warns about one unpaired request per test on phase-separated runs.** `CountUnpairedRequests` excluded the override start/end pair and user actions but not the Setup/Action boundary marker `StartAction()` logs, which is a `Request` with a fresh `RequestResponseId` and no response by design; it now filters on `IsDiagramMarker` and so covers all three.
- **Note toggles on a fragmented diagram no longer hang on an engine failure.** `setNoteState`'s fragment loop (the Collapse/Truncate/Expand buttons on a diagram that is split into fragments) waited for an `<svg>` only, so a fragment the engine answered with text ("Diagram too large for browser rendering…") or a render error left `_noteRendering` stuck and every further toggle on that diagram ignored; it now treats any output as completion (as `processRenderQueue` already did), describes the failure, never caches it, and gives a silent fragment up after 60 s.
- **Notes can no longer be wider than the engine can draw — capture-capped bodies are re-indented and whitespace-free runs are wrapped.** A JSON body cut by a capture cap (`…truncated (N chars total)` from the HTTP taps and `RequestResponseLogger`, `…[bulk string truncated: …]` from the RESP decoder) is not parseable, so the JSON pretty-printer gave up and the note got the raw payload on **one line** — 65 KB of minified JSON that PlantUML's `wrapWidth` (which breaks at spaces only) could not wrap: a 400,000 px wide note, which plantuml.js refuses with `java.lang.RuntimeException: Diagram too large for browser rendering: 413164x3671 (max 4096)` written into the diagram's place (seen live: every scenario holding a capped Redis `SET` or BigQuery reply lost a fragment). Two generic fixes in `PlantUmlCreator`: a truncated body that is a valid JSON *prefix* (`Utf8JsonReader` with `isFinalBlock: false` — a non-JSON body that merely starts with a brace still takes the plain-text path) is re-indented by structure with the marker on its own line; and, whatever the formatter produced, no note line may carry a whitespace-free run longer than `MaxUnbrokenRunChars` (120) — longer runs (a minified payload, a base64 blob, a long URL) are broken, preferring a punctuation boundary and never inside a `<tag>`. Diagrams that rendered before are unchanged.
- **Browser rendering: every fragment of a split diagram shows the grey "Rendering diagram…" placeholder until its own SVG arrives.** A split diagram's container is marked rendered the moment its fragment divs are created (so its own placeholder does not sit above them), which left a blank box for the seconds the engine needs for the first fragment; the placeholder rule now also applies to `.puml-fragment:not([data-rendered])`.
- **Browser rendering: an engine failure written as text no longer wedges the render queue, and every failure keeps its source reachable.** plantuml.js reports "Diagram too large for browser rendering" as plain text (no `<svg>`) and "Syntax Error?" as an error image; the note-state re-render path (Expanded/Truncated/Collapsed, Assertions/Steps/Databases/Headers toggles) waited for an `<svg>` that never came, left `_plantumlRendering` stuck, and after its 15 s force-reset let renders overlap in the engine's shared state — every diagram after that point stayed blank, stale or wrong. Any output now completes a render (a 15 s per-fragment timeout is the backstop), a failure is never cached as a result, a "too large" text becomes a legible message, and both failure kinds get a collapsed **Raw PlantUML** `<details>` so the next one is diagnosable from the page. The re-render path also applies the "nothing to draw" guard the initial render already had, instead of sending an empty body to the engine.
- **Database taps: a Redis value larger than `MaxBufferedBytes` no longer disables decoding for the rest of the connection.** `RedisProtocolDecoder` used to need the whole value in its buffer; one cache `SET` over the 8 MiB cap threw, the decoder was switched off for that connection for the life of the stack and — because client libraries keep one interactive connection open for the process lifetime — every Redis arrow after that moment silently vanished. The decoder now streams values of any size past (see the `RespStreamParser` entry below), so no arrow is lost and memory per connection is O(cap), not O(largest value). The same class of failure — a decoder that stops producing records — is now never silent: every skip, reset, give-up, dropped segment and mid-message close is a counter, a `Log` line, an `OnCaptureDegraded` event and a `TcpTap.Diagnostics()` entry.

### Added
- **Capture health reaches the report — `IngestRequest.HostDiagnostics`, `kronikol ingest --diagnostic "<kind>:<message>"`, the "Report diagnostics" section** — a host can hand the ingest what it already knows about its capture components (a `TcpTap` whose decoder gave up on a connection, an `OtlpTap` that dropped export payloads, a `ProxyTap` that answered `502`) as `DiagnosticEntry` items; they are carried verbatim — **first** — into `IngestResult.Diagnostics`, into a new collapsed **"Report diagnostics"** block of `TestRunReport.html` (`Report diagnostics (3: CaptureDegraded ×2, MalformedLine)`, one line per entry with a kind badge, the message and the scenario id; it lists *every* diagnostic recorded before the outputs are written — host entries, skipped malformed lines, diagram render failures — and is absent when there are none) and into a new top-level `diagnostics` array of `TestRunReport.json` and the mergeable JSON (`{ kind, message, scenarioId }`, described by `TestRunReport.schema.json` as `$defs/diagnostic` with `kind` an enum of the `DiagnosticKind` names). The CLI flag is repeatable; the kind is a `DiagnosticKind` name (case-insensitive), anything else — or no colon — counts as `Other`, an empty message is a usage error. Nothing changes when the list is empty. The lesson: a capture give-up must be a report diagnostic, never only a log line.
- **`ProxyTap.Diagnostics()` and `OtlpTap.Diagnostics()`** — every capture component reports its health the same way: `IReadOnlyList<DiagnosticEntry>`, one `DiagnosticKind.CaptureDegraded` entry per non-zero problem counter, worded for a report reader (`web→graphql: 1 of 2 forwarded request(s) carried no test identity and were not captured …`, `otlp: 2 export payload(s) dropped because the mapping queue was full (QueueCapacity 256) …`), empty while healthy — ready to be passed as `HostDiagnostics`. New counters behind them: `ProxyTap.ForwardFailures` (requests answered `502` because the upstream was unreachable or the exchange threw), `OtlpTap.PayloadsRejected` (exports refused with `413` over `MaxRequestBytes`) and `OtlpTap.PayloadsFailed` (accepted payloads whose decode, mapping or sink write threw — their spans are lost); `SpansIgnored` is by design and not reported.
- **`RespStreamParser` — a resumable, streaming RESP2/RESP3 parser for `Kronikol.Extensions.TcpTap`** — replaces "whole value in the buffer" for the Redis decoder in both directions. A frame stack plus a *skipping* state: a bulk payload (a `SET` value, a `GET` reply, any element of an aggregate) longer than `RedisTapOptions.MaxBulkBytes` is consumed segment by segment, keeping the first bytes as a preview and the declared length, and surfaces as a `RespValue` with the new `Truncated` / `DeclaredLength` properties; `HasResult()` is unchanged, so a `GET` of a 10 MB value is still a `Get (Hit)`, and `AsText()` / `Render()` end the preview with ` …[bulk string truncated: 9,123,456 bytes on the wire, 65,408 kept]` (the preview leaves room for the marker under `BodyCapBytes`, so the record-time cap never cuts it off). FIFO pairing stays exact — 200 pipelined commands with an oversize value in the middle still produce 200 pairs in order. Bytes held for an unfinished value (a header line, sub-cap payloads, an open aggregate) are bounded by `MaxBufferedBytes`; crossing it now means "desynchronised stream", not "big value". Inline commands (`PING\r\n`) move into the stream parser; `RespParser` (stateless) stays for tests and tools. Counters `BytesSkipped`, `OversizePayloadsSkipped`, `LargestOversizePayload`, `ValuesCompleted`; `Reset()` drops any partial value.
- **`RedisTapOptions.MaxBulkBytes`** — the bulk-payload cap (default `null` = `BodyCapBytes`, else `MaxBufferedBytes`; never above `MaxBufferedBytes`; `EffectiveMaxBulkBytes` says which is in force). Nothing beyond the record-time cap is kept anyway, so buffering it buys nothing.
- **Capture health as data — counters, callback, `Diagnostics()`.** New `TcpTap` counters `OversizePayloadsSkipped`, `BytesSkipped`, `LargestOversizePayload`, `DecoderResets`, `DecodingDisabledConnections` (the number that must be zero), `ConnectionsClosedMidMessage`, `LastInteractionAt`, `BytesSinceLastInteraction`. `TcpTapOptions.OnCaptureDegraded : Action<CaptureDegradation>` fires as things happen — `CaptureDegradation(Tap, ConnectionId, Kind, Detail)` with `CaptureDegradationKind` `OversizePayloadSkipped | DecoderReset | DecodingDisabled | SegmentsDropped | ConnectionClosedMidMessage` (dropped segments reported at most once per connection per minute; an exception in the callback is caught and logged). `TcpTap.Diagnostics()` returns `IReadOnlyList<DiagnosticEntry>` — one `DiagnosticKind.CaptureDegraded` entry per non-zero counter, worded for the report (`tap-di-redis: decoding disabled on 1 connection(s) — redis arrows on them after 14:03:35Z are missing (…)`, `tap-di-redis: 3 oversize payload(s) streamed past (largest 9,123,456 B, …) — values recorded as previews`), plus a heuristic stall entry when at least `TcpTapOptions.DecodingStallBytes` (default 1 MiB, null = off) have flowed since the last recorded interaction — bytes moving but nothing recorded. Hand the list to the ingest's host diagnostics so a dead tap is a line in the report, not only in a log.
- **`TcpTapOptions.ResyncAfterOverflow` (default true) and `TapProtocolException.Recoverable`** — the `MaxBufferedBytes` cap, a pending-queue overflow (`MaxPendingCommands`) and a protocol error on a connection that had been decoding fine are now *recoverable*: the tap calls the new `IProtocolDecoder.TryReset()` (a default interface method; `RedisProtocolDecoder.Reset()` / `MongoProtocolDecoder.Reset()` implement it) and decoding continues — client bytes are discarded until a segment starts with a command (`*<n>\r\n$`, or an inline command letter on a connection that has sent inline commands), server bytes until a command is pending again — instead of being disabled for the rest of the connection. The first interaction recorded after a reset carries the note prefix `[resynchronised — pairing uncertain] ` on both arrows and an `x-kronikol-capture: resynced` pseudo-header (`TapInteraction.Headers`, new), because a reply still in flight may pair with the wrong command until the connection goes idle; eight resets without an interaction recorded in between give up on the connection. Bytes that are not the protocol at all on a fresh connection still disable decoding, now counted in `DecodingDisabledConnections`. Off = disable as before, but counted and reported.
- **`MongoProtocolDecoder` skips messages larger than `MaxBufferedBytes`** instead of letting the buffer cap throw: the header's `messageLength` is read first (`MongoWireParser.TryPeekHeader`, new), exactly that many bytes are stepped over, a skipped *reply* still answers its `responseTo` command with the note `[reply of N bytes skipped — larger than the capture cap]` (status OK) so the arrow is not lost, and a skipped *command* records nothing (its `$db`/collection are inside the skipped BSON) but is counted and reported like every other capture loss. `MongoWireParser.LooksLikeHeader` finds the next message boundary after a reset.
- **Run window — `IngestRequest.DropOutsideRunWindow` / `RunStartedAt` / `RunEndedAt`, `kronikol ingest --run-window` / `--run-start` / `--run-end`** — keep only *this* run's traffic. Capturers whose files outlive a run (taps that append for as long as the stack is up) read against a per-run tests file used to fold the **previous run's** traffic and the stack's start-up into "Traffic outside any test", dwarfing the run itself. Interaction pairs whose request lies before the run began or after it ended are dropped before attribution (judged on the request, so a late response to an in-run call stays) and counted as the new `DiagnosticKind.DroppedOutsideRunWindow`. The window is explicit or derived from the tests records: start = the earliest record — a host that writes a `{"event":"testrun","testId":"__run__","status":"started"}` marker before the runner starts (`TestRunRecord.Events.TestRun`) keeps the runner's own set-up, e.g. a global login, inside the run; end = the latest `testrun` verdict marker, else open. Traffic inside the window that belongs to no test still reaches `FoldUnknownTestsInto` / `DropUnattributed`.
- **Feature, rule and scenario titles read as sentences — `ReportConfigurationOptions.CapitaliseTitles`** (default **on**; `kronikol ingest --no-capitalise` turns it off together with the step rule) — the same `StepText` helper that capitalises keyword-less step labels now upper-cases the first letter of every feature, rule and scenario title (and an outline's template title, so its members still group), so a Gherkin `Scenario: the overview renders` is shown as `The overview renders` in the HTML, JSON, XML, YAML and living documentation alike; titles starting with a quote, bracket, digit or symbol are left alone and example display names are never touched. New `DiagnosticKind.TitlesNotStartingWithCapital` counts what is left. Both rules now also leave a **camelCase first word** alone (`graphqlErrorMessages reads…`, `iPhone…`) — an identifier is producer content like a quoted literal, and is not counted as a violation. Note: `stableId` is computed from the displayed title, so a title this rule changes gets a new one; titles that already start with a capital are unaffected.

### Fixed
- **`ReportDiagnostics` no longer reports step bars, assertion notes and user actions as "unpaired requests"** — every ingested run with markers used to print a spurious `N unpaired request(s)` warning (410 on a healthy 17-test run).
- **Unknown tests-NDJSON events never create a scenario** — a reporter's run-level event (`testrun`, `testId` `__run__`) became a phantom scenario that never ended, rendered as Failed and blanked `Specifications.html`; `TestRunRecord.IsKnownEvent` / `Events.All` gate the synthesiser.
- **Cucumber import: the reporter's attachments win over the messages' copies of the same artefact** (playwright-bdd inlines every attachment as BASE64), so a screenshot no longer lands twice in `attachments/`.
- **OTLP tap: Mongo handshake/auth spans (`isMaster`, `hello`, `sasl*`, `ping`, …) are connection plumbing, not calls** — mirrors the wire tap's exclusions so the two capture paths agree.

### Added
- **OTLP receiver-tee — `Kronikol.Extensions.Otlp`** — a third capture topology: the OpenTelemetry traces the services already export become diagram arrows. `OtlpTap` accepts `POST /v1/traces` exactly as a collector does (`application/x-protobuf` **and** `application/json`, gzip/deflate/br), optionally forwards each export byte-for-byte to a real collector (`ForwardBaseUri`, relaying its status and body) and otherwise answers `200` with an empty `ExportTraceServiceResponse`, then maps the client spans it recognises to Kronikol request/response pairs on an `IRequestResponseSink` — request at `startTimeUnixNano`, response at `endTimeUnixNano`, so call-tree nesting and durations work. **Attribution is exact:** `testId` = the span's W3C trace id (the "test mints the trace" model), with `ActivityTraceId`/`ActivitySpanId` always set; `AttributeByTraceId`/`KnownTestIds`/`FallbackTestId` send non-test traffic to a fold bucket instead. Authentication by shared-secret header (`ExpectedHeaders`; anything else → `401`, counted), a bounded drop-on-full mapping queue so a slow sink can never delay an exporter, a request-size cap, and counters for received/mapped/ignored/dropped/unauthenticated/forward-failed. DI via `services.AddOtlpTapTestTracking(o => …)` (one hosted service per tap). The listener is a plain socket, not `HttpListener`: on Windows http.sys refuses a non-loopback prefix without a URL ACL or elevation, and a containerised exporter reaching the host through `host.docker.internal` needs exactly that bind (`ListenHost = "+"`); `localhost` binds both loopbacks so an exporter resolving `::1` first pays no connect-refused round trip.
- **`SpanToInteractionMapper` (public, pure)** — maps one `OtlpSpan` to the call it stands for, accepting the deprecated *and* the stable semantic conventions (`db.system`↔`db.system.name`, `db.statement`↔`db.query.text`, `db.operation`↔`db.operation.name`, `db.mongodb.collection`↔`db.collection.name`, `db.name`/`db.redis.database_index`↔`db.namespace`, `net.peer.*`/`peer.service`↔`server.address`/`server.port`, `http.method`↔`http.request.method`, `http.url`↔`url.full`, `http.status_code`↔`http.response.status_code`). Redis → the command verb (`(Hit)`/`(Miss)` only when the producer reported a result), `redis://db{n}/{key}`, category `Redis`; MongoDB → the same directional labels the MongoDB extension draws (`Find ← Trial`, `Insert → Trial`, `FindAndModify ↔ Trial`), `mongodb:///{db}/{collection}`, category `MongoDB`; any other `db.system` → the operation or first SQL verb, `{system}://{server}/{namespace}` and a matching category (`BigQuery`, `PostgreSQL`, `MySQL`, `SqlServer`, … else `Database`); HTTP client spans → the method and `url.full`; messaging and RPC spans opt-in via `CaptureKinds`. Server spans are ignored unless `IncludeServerSpans`; a span whose status is ERROR becomes `500` with the status message as the response body; `ServiceNameMap` turns OTel names and peer addresses into diagram participants. `OtlpTraceReader` decodes both OTLP encodings (a hand-written protobuf reader — no protobuf dependency; hex or base64 ids, integer or enum-name `kind`, string int64 nanos) and never throws on malformed input.
- **Ingest-time merge of duplicate captures — `IngestRequest.MergeDuplicateInteractions` / `kronikol ingest --merge-duplicates`** — when a stack is captured from both sides (a wire tap that sees the protocol but guesses the test, and an OTLP tap that knows the test but not the payload) the same call arrives twice. `Kronikol.Ingestion.InteractionMerger` folds the two views into one arrow: same caller, service, method *verb* (first word, so `Get (Hit)` matches `GET` and `Find ← Trial` matches `Find`) and last URI segment, with intervals overlapping by at least `MergeOverlapThreshold` (default 0.8) of the shorter one; matching is greedy by best overlap and strictly one-to-one, so bursts pair off instead of collapsing. The span record's `testId`/`traceId`/`activityTraceId`/`activitySpanId` win, the wire record's `content`/`statusCode`/label win, and the merged request carries an `x-kronikol-captured-by: wire + span` pseudo-header. Unmatched records are never dropped.
- **`InteractionRecord.CapturedBy` / `RequestResponseLog.CapturedBy` (`capturedBy` on the wire)** — optional `wire` / `span` stamp identifying which capture path produced a record; the merge falls back to inferring it (a record with a span id and no content is span-like, one with content and no span id is wire-like) and leaves anything ambiguous alone.
- **Cucumber Messages importer — `Kronikol.Ingestion.Cucumber`** — Kronikol reads the Cucumber Messages protocol (schema 32.x) directly, so any runner that emits it becomes a first-class producer without an adapter in the test process: `playwright-bdd`'s `cucumberReporter('message')`, `cucumber-js --format message`, Cucumber-JVM `--plugin message:…`. `CucumberMessagesReader` turns the envelope NDJSON into typed envelopes (deliberately tolerant: an envelope type it does not consume is counted and ignored, unknown properties inside a known envelope are ignored, a line that is not valid JSON is counted and skipped — a messages file can never fail an ingest, the counts and reasons come back as warnings); `CucumberFeatureSynthesizer` maps them onto the report model — feature name/description/tags, `Rule` → `Scenario.Rule`, `Background` steps → `Scenario.BackgroundSteps` (**explicit**, not the heuristic detector), scenario description, the Gherkin keyword as authored (`Given`/`And`/`But`) with the pickle step's resolved `Context`/`Action`/`Outcome` phase behind it, `dataTable` → a tabular `table` step parameter, `docString` → `DocString`/`DocStringMediaType`, scenario outlines → `OutlineId` + `ExampleValues`/`ExampleRawValues` (so the parameterised-group pivot works for ingested runs) with the substituted placeholder highlighted via `TextSegments`, `@happy-path`/`@category:`/`@endpoint:` following the same conventions as the ReqNRoll adapter, step status/duration/exception → `Status`/`Duration`/step `Comments` + `Scenario.ErrorMessage`/`ErrorStackTrace`, `Attachment` envelopes → scenario/step attachments (inline base64 bodies written out and copied into `<reports>/attachments/`, the display name given the extension its media type implies so screenshots render inline), retries → last attempt wins with a `retry N` label, `TestRunStarted`/`TestRunFinished` → the run window. Hook steps are dropped by default (their attachments are kept, attributed to the scenario) or shown with `IncludeHooks`.
- **`kronikol ingest --cucumber-messages <file>` (repeatable) and `--include-hooks`** — `IngestRequest.CucumberMessagesFiles` / `IngestRequest.IncludeHooks`. A messages file alone is enough to produce a report; a messages file named this way is also removed from the interaction-capture set if it happens to sit inside an input directory. When `--tests` is given too, **the messages win for structure** (`CucumberFeatureMerger`): the Gherkin model replaces the reporter's for every scenario the messages own and the reporter's duplicate `step` events are dropped, while the tests file still contributes `assertion` events (nested under the Gherkin step whose time window contains them — the ✓/✗ rows and diagram notes), UI actions, attachments, the identity and a failure message when no Gherkin step failed. Scenarios the messages do not own — plain tests, the `--fold-unknown` bucket — are kept and rendered after the Gherkin features. The importer synthesises the same `start`/`step`/`end` records the tests format uses, so step delimiter bars, names and outcomes come out of the existing ingest machinery rather than a second implementation.
- **Joining a Gherkin scenario to its captured traffic** — a `kronikol-test-id` attachment (32-hex, `text/plain`) written by a `Before` hook becomes `Scenario.Id`, so interactions on the wire, the reporter's `ui`/`assertion` events and the Gherkin structure all converge on one scenario. Without it the importer mints `<pickleId>#<attempt>` and warns that interactions cannot be joined.
- **`Scenario.Description`** (Gherkin free text under the scenario line) and **`FileAttachment.MediaType`** (optional MIME type, when the capturer knew one).

### Fixed
- **Ingest: producer timestamp quirks no longer reorder the diagram** — a step a BDD runner never reached (`SKIPPED` after a failure) can carry the test case's own start time, and `testCaseFinished` can be stamped before the last step reports back (both observed in playwright-bdd 9.2). Step starts are now kept monotonic within a scenario and the scenario end is the later of `testCaseFinished` and the last `testStepFinished`, so a `Then` bar can no longer sort before the `When` that failed.
- **Database taps — `Kronikol.Extensions.TcpTap`** — the out-of-process capture component for database hops, the TCP counterpart of `Kronikol.Extensions.ProxyTap`. `TcpTap` is a transparent, byte-for-byte TCP tee: it listens on a port, opens one upstream connection per accepted connection and copies bytes both ways *unmodified and first*, handing a copy of each segment to a protocol decoder on a separate task. Point a service's Redis or MongoDB client at the tap instead of the database and its calls render in the sequence diagrams with **no change inside the service** — no code, no client library, any language. Forwarding never waits on capture: each pump writes downstream before it queues the copy, the queue is bounded and drop-on-full (counted in `SegmentsDropped`), half-close is propagated, decoded content is capped at `BodyCapBytes` (64 KB), and a decoder that throws is caught, counted in `DecodeErrors` and switched off for that one connection while it keeps forwarding.
  - **`RedisTap` (RESP2/RESP3)** — commands are arrays of bulk strings, replies matched FIFO per connection (Redis answers a pipelined connection in order); every RESP2 type plus the RESP3 additions (null, double, boolean, big number, verbatim string, blob error, map, set, push); hit/miss decided on the reply; `SELECT n` followed per connection so the database index in the URI stays right; unsolicited pub/sub deliveries and RESP3 pushes skipped without consuming a pending command; multi-key commands join every key into the URI exactly as the in-process extension does for a `RedisKey[]`; error replies become status 500 with the message.
  - **`MongoTap` (OP_MSG)** — the 16-byte header, OP_MSG flag bits (`checksumPresent`/`moreToCome`/`exhaustAllowed`), section kind 0 and kind-1 document sequences (folded into the body under their identifier, so `documents`/`updates`/`deletes` classify like inline arrays), replies matched by `responseTo`, `moreToCome` replies that answer nothing (a monitoring connection's streaming `hello`) skipped, `moreToCome` commands (`w:0` writes) recorded immediately; `{db}` taken from the command's `$db`; `ok: 0` / `errmsg` becomes status 500. The legacy OP_QUERY/OP_REPLY handshake — which MongoDB.Driver still opens every connection with — is recognised, stepped over and **recorded never**; OP_COMPRESSED is passed straight through undecoded and reported once.
  - **Byte-identical to the in-process extensions.** The tap compiles the same `RedisOperationClassifier` / `MongoDbOperationClassifier` / `MongoDbResponseSummary` source files that `Kronikol.Extensions.Redis` and `Kronikol.Extensions.MongoDB` use (linked into its own namespace, so a host may reference both packages), and golden tests assert the `Method`, `Uri` and reply notes match character for character at every verbosity.
  - **Security at the decoder, not at render.** Redis `AUTH`/`HELLO`/`RESET` and MongoDB `saslStart`/`saslContinue`/`authenticate`/`getnonce`/`createUser`/… are hard-excluded whatever `ExcludedCommands` contains, so a credential can never reach the store, `TestRunReport.json` or an NDJSON file. The tap never learns a connection string — it records `mongodb:///{db}/{coll}`, which cannot contain a password. `KeyRedaction`/`ValueRedaction`/`DocumentRedaction` hooks run before the sink. Handshake and keep-alive chatter is excluded by default, including the verbs and bookkeeping keys real clients actually send (`SENTINEL`, `SUBSCRIBE`, `QUIT`, `__Booksleeve_TieBreak`).
  - **Options and DI.** `TcpTapOptions` is the generic topology schema (listen/forward host and port — `ListenPort = 0` binds an ephemeral port readable as `BoundPort` —, caller/service names, dependency category, `Verbosity` `Summarised|Detailed|Raw`, `BodyCapBytes`, `CaptureReplies`, sink, phase, `FallbackTestName`/`FallbackTestId`, `IdentityResolver`, `EmitActivities` on the `Kronikol.TcpTap` `ActivitySource`, redaction hooks, `ChannelCapacity`/`MaxBufferedBytes`/`ReadBufferBytes`/`AcceptBacklog`, timeouts, and a `DecoderFactory` for teeing any other protocol); `RedisTapOptions` and `MongoTapOptions` add the protocol knobs. DI via `services.AddRedisTapTestTracking(o => …)`, `services.AddMongoTapTestTracking(o => …)` and `services.AddTcpTapTestTracking(o => …)` — one `IHostedService` per tap. Records carry the real command and reply timestamps, so call-tree ordering nests DB calls under the request that made them and `CollapseConsecutiveIdenticalCalls` folds cache bursts into `loop ×N · min–max ms`. Because a wire tap has no per-request identity, attribution is by test window at ingest (or via `IdentityResolver`).

### Changed
- **`MongoDbTrackingSubscriber` reply-note rendering moved to `MongoDbResponseSummary`** — the `n`/`nModified`/`nUpserted` metadata and the `cursor.firstBatch` document preview now come from a shared, driver-free helper (`MongoDbResponseSummary.ExtractMetadata` / `ExtractDetailed`) so the in-process subscriber and the new `MongoTap` produce the same note text. Behaviour and public API of `Kronikol.Extensions.MongoDB` are unchanged.
- **Attachments from external runs (`event: "attachment"`)** — the tests NDJSON gains an `attachment` event (`name`, `path`, optional `mediaType` and `step`) so a runner outside the .NET process can put screenshots, traces, videos and links into the report. `path` may be an absolute path, a path relative to `IngestRequest.AttachmentsBase` / `kronikol ingest --attachments-base <dir>`, or a `http`/`https` **URL** (rendered as a plain link and never copied — previously such a path was handed to the path APIs, which throws on Windows). `step` is the 0-based index of the top-level step the artefact belongs to; absent means scenario-level, and an index that no longer resolves falls back to the scenario rather than losing the artefact. `FileAttachment` gains an optional third positional `MediaType`, which the renderer now **prefers** over sniffing the file extension (`image/*` renders inline with a lightbox, anything else as a link) — and `.svg`, `.avif` and `.bmp` join the inline extension list. The media type is carried through `TestRunReport.json`/`.xml`/`.yml`, the JSON Schema, the XSD and the mergeable report. `IngestRequest.CleanAttachments` / `--clean-attachments` empties `<reports>/attachments/` first, so the folder holds exactly this run's files (nothing ever removed stale copies before).
- **The widened tests NDJSON — everything a Gherkin runner knows, without a Cucumber Messages file** — `start` gains `featureDescription`, `description`, `rule`, `tags[]`, `outlineId` and `exampleValues{}`; `step` gains `background`, `keywordType` (`Context|Action|Outcome|Conjunction|Unknown`), `docString`, `docStringMediaType`, `table` (`string[][]`, first row the header), `stackTrace` and `bypassReason`; `end` gains `stackTrace`. `FeatureSynthesizer` maps them onto `Scenario.Description` (**new model field**), `Rule`, `OutlineId`, `ExampleValues`/`ExampleFlatValues`/`ExampleRawValues`, `Labels`, `Categories`, `IsHappyPath`, `ErrorStackTrace`, `Feature.Description`/`Endpoint`/`Labels`, step doc-strings, tabular `table` parameters and step comments. Tags follow the ReqNRoll conventions (`@category:x`, `@endpoint:x`, `@happy-path`/`happy_path`/`happypath`; a tag carried by *every* scenario of a feature is also the feature's own label) via the new shared `Kronikol.Reports.ScenarioTags`. A `background: true` step goes straight into `Scenario.BackgroundSteps` and draws no delimiter bar; when any scenario supplies an explicit background the heuristic `BackgroundStepsDetector` is not run at all, and it also declines to run when no step carries a keyword (a common prefix of keyword-less UI actions is a coincidence, not a `Background:`).
- **Ingest-time attribution for capturers that cannot read a test header** — `IngestRequest.AttributeByTestWindow` / `kronikol ingest --attribute-by-window [fallbackId]` gives an interaction with no `testId` (or with the capturer's placeholder marker, `WindowAttributionFallbackId`) to the test whose `[start, end]` window contains its timestamp. Overlapping windows resolve to the test that **started latest** (the innermost in flight); a tie resolves to the first window in the file; a record in no window is left alone so `--fold-unknown` still collects it; and a **response follows its request** (matched by `requestResponseId`) rather than its own timestamp, so a slow query answered after the test's `end` record is neither orphaned nor handed to the next test. A test killed before its `end` record is bounded by the last timestamp seen for it. `IngestRequest.DropUnattributed` (a predicate, programmatic only) discards records that are *still* unattributed — and their paired responses — for a session-wide capturer that also sees the seeder or the health probes.
- **Phases from steps** — `IngestRequest.PhaseFromSteps` / `--phase-from-steps` gives an interaction the phase of the top-level step whose window contains it (`Given`/`Context` → `Setup`, `When`/`Then`/`Action`/`Outcome` → `Action`, `And`/`But`/`Conjunction` inherit), leaving alone any record whose capturer already said. `SeparateSetup` and `HighlightSetup` now partition an ingested diagram the way they partition an in-process one — until now they needed the in-process `TestPhaseContext`.
- **`InFlightIdentityRegistry` (`Kronikol.Extensions.ProxyTap`)** — the live alternative to window attribution for concurrent suites: a `ProxyTap` with `ProxyTapOptions.InFlightRegistry` set publishes the identity of every request it is forwarding, for as long as it is in flight, and a capturer that cannot read headers (a database tee on the same service's connections) asks `MostRecentFor(serviceName)` who that traffic belongs to. Thread-safe, opt-in, and nothing is published unless the option is set.
- **Structured report diagnostics** — `IngestResult.Diagnostics` is a read-only list of `DiagnosticEntry(Kind, Message, ScenarioId?)` (`RenderFailure`, `OutputFailure`, `MalformedLine`, `StepsNotStartingWithCapital`, `UnattributedInteractions`, `DroppedUnattributed`, `AttachmentFailure`, `Other`), printed by `kronikol ingest` and meant to be surfaced by a host. Any report generation can collect the same entries with `ReportDiagnosticsScope.Begin(new ReportDiagnosticsCollector())`; the scope is an `AsyncLocal`, so it flows into the parallel output workers and two generations in one process keep their diagnostics apart.

### Changed
- **Step and assertion labels are capitalised** (rendering change, default **on**) — `ReportConfigurationOptions.CapitaliseStepText` upper-cases the first *letter* of every step and assertion label that carries **no Gherkin keyword**, after skipping leading whitespace and the marker glyphs `✓ ✗ ⚠ • -`, so `✓ the envelope was empty` renders as `✓ The envelope was empty`. A label whose first non-marker character is an opening quote or bracket (`" ' ( [ {` and the typographic ones) is left exactly as it is — the quoted literal is the producer's content — and a step **with** a keyword is never touched, because the rendered line already starts with the capitalised keyword. Culture-invariant, Unicode-aware (`é`→`É`, `ł`→`Ł`), idempotent. The rule lives in one place (`Kronikol.Reports.StepText`) and is applied once over the finished model, so the HTML, JSON, XML and YAML views of a step cannot disagree; the diagram's `<<stepDelimiter>>` bars (keyword-less form) and `<<assertionNote>>` text go through the same helper via the process-wide `StepText.CapitaliseEnabled`, so the picture and the step list read alike. `ScenarioStep.TextSegments` are re-derived so inline-parameter highlighting still lines up. `kronikol ingest --no-capitalise` turns it off; what the rule deliberately leaves is reported as a `StepsNotStartingWithCapital` diagnostic with the first five examples.
- **A malformed capture line no longer costs you the report** — `NdjsonInteractionReader` and `NdjsonTestRunReader` now **skip and count** unparsable lines when handed a `MalformedLine` collector, which `IngestPipeline` and `kronikol ingest` do by default: a process killed mid-write leaves a truncated last line, and losing an entire run's report to it helped nobody. `kronikol ingest` prints `N malformed line(s) skipped` with file, line number and the first 80 characters of each, and every one appears in `IngestResult.Diagnostics`. `IngestRequest.StrictParsing` / `--strict` restores the throw for a pipeline where a garbage producer should be loud; the reader signatures without a collector behave exactly as before.
- **`kronikol merge` carries what it used to drop** — the mergeable report now round-trips attachment media types, `Scenario.Description` and `ExampleFlatValues` (which drives the pivot table's columns), so a merged report keeps the parameterised grouping and the inline images the original had.
- **`IngestRequest.ResultWhenUnknown` is documented for what it is** — the default stays `Passed` for compatibility, but the XML doc and the wiki now say plainly that **a test whose process died mid-run renders as passed**, and that a producer which always writes an `end` record should set `ExecutionResult.Failed`.

### Fixed
- **One broken diagram no longer costs you every diagram** — `DefaultDiagramsFetcher` had no per-scenario isolation: any exception while producing or rendering *one* diagram (a node render that timed out, a PlantUML server 5xx, a formatting processor that could not parse one body) propagated out of the whole set and **every** diagram in the report was gone. Diagram production now falls back to a per-scenario pass when the single-pass build fails, and every render call (server, inline SVG, local, node.js) is isolated: the scenario that failed shows a red `hnote across <<renderError>>` note naming the exception in place of its picture, every other diagram is untouched, and the failure is recorded as a `RenderFailure` diagnostic against that scenario. `AggregateException` wrappers are unwrapped so the message names the real cause. Configuration errors (no `LocalDiagramRenderer`, no image directory) still throw, because they are the caller's bug and the message tells them what to set.
- **One failing report output no longer kills the rest** — `ReportGenerator`'s `Parallel.Invoke` over the report outputs was unguarded, so an unwritable data file took the HTML, the component diagram, the CI summary and the artifact upload with it. Each output is now isolated, recorded as an `OutputFailure` diagnostic naming the file, and every other output is still written.
- **An attachment that cannot be copied no longer breaks report generation** — a source file another process still holds (or a path that is not usable at all) is left pointing at where it is and recorded as an `AttachmentFailure`, instead of throwing out of `CopyAttachmentsToReportsFolder`.
- **Tests that share the process-wide capture store no longer race** — the `PendingLogs` test collection was separate from the one used by every test that replays a run, so a `RequestResponseLogger.Clear()` in one could wipe the logs another was about to read; both now run in the single shared-capture-state collection.

## [3.0.44] - 2026-08-21

### Added
- **Proxy-tap topology — `Kronikol.Extensions.ProxyTap`** — Kronikol's first *out-of-process* capture component. `ProxyTap` is a transparent HTTP tee (`HttpListener`-based, no ASP.NET Core host needed) that listens on a port, forwards byte-for-byte to the real service, and records each exchange as a Kronikol request/response pair attributed to the running test — by the `test-tracking-*` headers when present, or by the W3C `traceparent` trace id (the "test mints the trace" model browser-driven E2E suites use). It re-injects the four correlation headers on the forwarded request so attribution survives hops that drop them, emits server/client spans on the `Kronikol.ProxyTap` `ActivitySource` (re-parenting the forwarded `traceparent`), decodes gzip/deflate/br for capture only, caps bodies, and applies a capture-time secret denylist (`authorization`, `cookie`, `set-cookie`, `x-api-key`, …) before anything reaches a sink. `ProxyTapOptions` is the generic topology schema (listen port, forward base URI, caller/service names, dependency category, header policy, identity fallbacks, sink); DI via `services.AddProxyTapTestTracking(o => …)` (one hosted service per tap). This is what makes Kronikol usable for polyglot, third-party or legacy backends that cannot be instrumented.
- **Language-neutral NDJSON capture format + `kronikol ingest`** — `Kronikol.Ingestion.InteractionRecord` defines one JSON object per tracked request or response, shaped exactly like the `httpInteraction` objects already published in `TestRunReport.json` (same camelCase property names) plus `testId`/`testName` attribution and optional `dependencyCategory`, `phase`, `metaType`, `activityTraceId`/`activitySpanId`, `trackingIgnore`. `NdjsonInteractionWriter` is an `IRequestResponseSink` that appends one line per entry (thread-safe, flushed per line, tail-able); `NdjsonInteractionReader` reads them back (opening with `FileShare.ReadWrite`, so a capture can be ingested while the writer still holds it open — live reporting). A companion tests NDJSON (`TestRunRecord`: `start`/`step`/`end` events) supplies scenario outcome, duration and steps; `FeatureSynthesizer` builds the `Feature[]` model from either source. `IngestPipeline.Run(...)` replays captures in call-tree order (`CallTreeOrdering`: each response directly after its request, calls a service made while handling a request nested between that request and its response, siblings by request time — so concurrent traffic stays readable and collapsible; `--chronological` for a strict timeline), optionally folds interactions of test ids absent from the tests file into one "outside any test" scenario (`FoldUnknownTestsInto` / `--fold-unknown`), normalises test names, resets the diagram cache and generates the standard reports; the new **`kronikol ingest <inputs…> [--tests f] [-o dir] [--render …] [-t title] [--collapse|--no-collapse] [--max-arrows n] [--no-redact] [--redact-header h]`** verb wraps it. Any capturer in any language can now feed Kronikol.
- **`IRequestResponseSink` abstraction** — `RequestResponseLoggerSink` (the in-process store), `NdjsonInteractionWriter` (file) and `CompositeRequestResponseSink` (fan-out) so a capturer can write to the process store *and* a replayable file at once.
- **Playwright adapter — `Kronikol.Playwright`** — `TestTrackingIdentity.Create(testName[, testId])` mints a per-test identity whose `TraceId` doubles as the W3C trace id (and, by default, as the test id); `ToHeaders()` yields the four `test-tracking-*` headers plus a `traceparent`; `browser.NewTrackedContextAsync(identity)`, `context.UseTestTrackingAsync(identity)` and `page.UseTestTrackingAsync(identity)` stamp them on every browser request (merging with existing extra headers); `identity.BeginScope()` opens the matching in-process `TestIdentityScope`. The sink is downstream — a Kronikol-instrumented backend (`TestTrackingContextMiddleware`) or a `ProxyTap`.
- **Capture-time redaction (`RequestResponseLogger.Redaction` / `CaptureRedaction`)** — a denylist/pattern/custom hook applied *before* an entry is stored, so redacted values never reach the in-memory store, `TestRunReport.json`, the mergeable JSON or an NDJSON file. `CaptureRedaction.Secrets()` is the secure preset (well-known credential headers → `[REDACTED]`); `RedactContent(regex)` scrubs tokens inside bodies; `DropHeaders` removes instead of replacing; `Custom` can rewrite or drop entries. `ExcludedHeaders` remains a render-only diagram setting — this is the security boundary.
- **Honoured `ReportConfigurationOptions.ReportsFolderPath`** — previously defined but ignored by the report pipeline (everything went to `<BaseDirectory>/Reports`). Relative paths resolve against `AppDomain.CurrentDomain.BaseDirectory`, absolute paths are used as-is, and *every* standard output (HTML, data, schema, component diagram, CI summary, copied attachments) lands there. `ReportGenerator.ResolveReportsDirectory(options)` exposes the resolution.
- **`DefaultDiagramsFetcher.Reset()` / `HasCachedDiagrams`** — the process-lifetime diagram cache is now resettable, so hosts that generate several reports in one process (incremental/live reporting, `kronikol ingest`, dashboards) no longer reuse the first run's diagrams.
- **Native collapsing of repeated calls + arrow cap** — `CollapseConsecutiveIdenticalCalls` (default off; on for `kronikol ingest`) folds maximal runs of consecutive identical request/response pairs (same caller, service, method, path+query, GraphQL operation, status) into one pair wrapped in a PlantUML `loop ×N · min–max ms` fragment; `CollapseThreshold` (default 2) sets the minimum run; `MaxArrowsPerDiagram` caps pairs per diagram and appends `… +N more calls omitted …`. Poll/retry-heavy traffic (BigQuery `getQueryResults`, health polls) stays legible.
- **Browser rendering: height-split fragments keep `loop`/`alt`/`partition` blocks balanced** — the client-side splitter (height splits and forced splits inside huge notes) now closes open blocks at a fragment boundary and re-opens them in the next fragment, and keeps a block opener with the pair it wraps; a `loop ×N` cut in two previously left a stranded `end` → `Syntax Error? (Assumed diagram type: class)` on large collapsed diagrams.
- **Browser rendering: empty-after-filter guard** — hiding assertion notes / step bars could leave a diagram with no body at all (a test that asserted but never touched a tracked dependency); plantuml.js answered `Syntax Error? (Assumed diagram type: class)`. The report now shows "Nothing to draw with the current filters…" for such fragments instead of sending them to the engine.
- **Markers-only diagrams are valid PlantUML** — a diagram made only of injected step bars / assertion notes declared no participant, so its `hnote across` lines were a syntax error in every real engine (`--render nodejs`/`local`/`server`, and in the browser as soon as the notes were shown). The generator now declares one `participant "(no interactions)"` lifeline for the notes to span (`PlantUmlCreator.MarkerOnlyParticipant`); the browser guard knows the line and still shows the "Nothing to draw…" affordance while the notes are hidden.
- **NodeJs rendering fixes** — with `PlantUmlRendering.NodeJs` the component diagram now renders as plain-syntax SVG whatever `PlantUmlImageFormat` says (the Node renderer is SVG-only and plantuml.js has no C4 stdlib; it used to throw or time out after the rest of the report was written), and diagram text is sent to node as UTF-8 (previously the console code page, so `×`/`·`/`–` in loop labels and any accented or non-Latin text rendered as `x`, `�` or `?` on Windows).
- **The user in the diagram (ingest)** — the interaction NDJSON accepts `kind: "ui"` records (a browser action such as `Click "Accept trial"`: one arrow from a `User` actor to the service, `durationMs` = the interval whose calls nest under it) and the tests NDJSON gains `assertion` events plus `level`/`status`/`durationMs`/`error` on `step` events: top-level steps draw the same black step delimiter bar as Step Tracking, assertions the same green ✓ / red ✗ `<<assertionNote>>` as Assertion Tracking (with the failure message), both via the same PlantUML injection — so the report's Show/Hide Steps and Show/Hide Assertions toggles apply — and the step list shows steps with nested steps and assertions as sub-steps. New `DependencyCategories.User` (actor shape, `#7D3C98`). `InteractionRecord.UserAction/StepMarker/AssertionMarker` factories; `RequestResponseLog.IsUserAction`.
- **"No interactions captured" marker** — a scenario whose id matched no tracked interaction (or whose diagram is only step bars / assertion notes) now renders an explicit `No interactions captured for this scenario.` line (`ShowNoInteractionsMarker`, default on) instead of a silently empty diagram section.
- **`DependencyCategories.AI` / `DependencyType.AI`** — LLM/AI-provider calls (Gemini, OpenAI, Ollama, Bedrock, …) get their own palette entry (`#16A085`, `control` participant, hexagon component) instead of falling to Unknown/grey.

### Fixed
- **`TestRunReport.json` now always carries `httpInteractions`** — the per-scenario interaction block was only emitted when `InternalFlowTracking` was on, so externally captured traffic (proxy taps, ingested NDJSON — which has no in-process spans and sets `InternalFlowTracking = false`) produced a data file with diagrams but no interactions. The data export no longer depends on internal-flow tracking.

### Changed
- `kronikol` top-level help lists both verbs; `Kronikol.Tool` package description updated.
- `RequestResponseLog` gained `CollapsedCount` / `CollapsedSummary` (set only by the diagram pipeline).
- `ReportGenerator.GenerateHtmlReport` gained an optional `showNoInteractionsMarker` parameter (default `false` to preserve direct callers' output; the standard pipeline passes the option).

## [3.0.43] - 2026-06-20

### Added
- **ClickHouse support (`Kronikol.Extensions.ClickHouse`)** — A new first-class extension that tracks ClickHouse operations in test diagrams via `DbConnection` wrapping. It works with **both** common .NET ClickHouse ADO.NET clients — `ClickHouse.Client` (`ClickHouse.Client.ADO.ClickHouseConnection`) and `Octonica.ClickHouseClient` (`Octonica.ClickHouseClient.ClickHouseConnection`) — and takes no hard dependency on either package (both derive from `DbConnection`).
  - **Manual wrapping:** `connection.WithClickHouseTestTracking(options)` returns a `TrackingClickHouseConnection` that intercepts all six command-execution methods (ExecuteReader/NonQuery/Scalar × sync/async) plus transactions, classifying and logging each operation.
  - **Dependency injection:** `services.AddClickHouseTestTracking(...)` decorates all registered `DbConnection`s, wrapping only ClickHouse connections (from either client) and leaving other connections untouched.
  - New `ClickHouse` dependency category, rendered as a database participant in sequence and component diagrams.
- **ClickHouse SQL dialect in `UnifiedSqlClassifier`** (shared by all SQL tracking extensions) — ClickHouse lightweight mutations `ALTER TABLE … UPDATE` / `ALTER TABLE … DELETE` (including `ON CLUSTER`) are now classified as `Update` / `Delete` rather than generic `ALTER TABLE`; and `OPTIMIZE TABLE`, `RENAME TABLE`, `ATTACH`, and `DETACH` are recognised as new operations (`Optimize`, `Rename`, `Attach`, `Detach`) with Detailed and Summarised diagram labels. Standard `ALTER TABLE … ADD/DROP COLUMN` continues to classify as `AlterTable`.

## [3.0.42] - 2026-06-20

### Added
- **Full step-detail fidelity in merged reports** — The mergeable report format (`GenerateMergeableData`) now carries everything needed to render steps exactly as a single combined run would: inline parameter highlighting (`TextSegments`), tabular/tree/inline step parameters, doc-strings, comments and bypass reasons (in addition to the step text, status, durations, substeps and attachments already carried). `kronikol merge` therefore reproduces step parameter tables, table-ref toggles and inline-highlighted values in the combined report. The standard `TestRunReport.json` format is unchanged — the extra detail is only emitted in the enriched mergeable file.

## [3.0.41] - 2026-06-20

### Added
- **Merge multiple test-run reports into one combined `TestRunReport.html`** — When a test suite is split across several parallel CI runners (e.g. 10 GitHub Actions runners each executing a subset), you can now combine their outputs into a single report identical to one produced had all tests run together. There are two new pieces:
  - **`ReportConfigurationOptions.GenerateMergeableData`** (default `false`): when enabled, each runner's `TestRunReport.json` is enriched into a complete, round-trippable artifact containing the features/scenarios/steps, per-scenario diagram source, the run's component-diagram relationships, precomputed internal-flow segment data and whole-test-flow fragments, and CI metadata.
  - **`kronikol merge`** (a new packable .NET tool, `Kronikol.Tool`): `kronikol merge ./artifacts -o TestRunReport.html` reads all the mergeable JSON files in the given files/directories/globs, combines them (features grouped by name with scenarios unioned, component relationships re-aggregated, internal-flow and whole-test-flow data unioned, CI metadata reconciled, earliest start / latest end times), and renders a single combined HTML report — including one merged Component Diagram across all runners' traffic, internal-flow popups, and flame charts.
  - Programmatic equivalents are available via `Kronikol.Reports.Merge.MergeableReportReader`, `MergeableReportMerger`, and `MergeableReportRenderer` (e.g. `MergeableReportRenderer.MergeFilesToHtml(paths, "TestRunReport.html")`).
  - Note: inline step-parameter highlighting and step doc-strings are not yet carried through the merge data format; step text, status, durations, substeps and attachments are.

## [3.0.40] - 2026-06-12

### Fixed
- **Three-fragment continuation notes no longer crash `makeNotesCollapsible`** — When a large note spanned 3+ client-side fragments, `noteIndexOffset` counted all note blocks from previous fragments including continuation blocks. Since continuation blocks don't represent new global notes, this inflated the offset, causing out-of-bounds access into `ownerNoteBlocks` and `noteBlocks`. The uncaught exception halted the render queue, preventing all subsequent diagrams from appearing. Fixed by subtracting continuation notes from previous fragments' counts, restructuring the index mapping to always pass local indices into the IIFE (with local→global conversion via `fragContinuationMap` inside), using safe fallback for `origContentLines`, and wrapping post-render hooks in try-catch to prevent any future error from stalling the queue.

## [3.0.39] - 2026-06-12

### Fixed
- **Continuation notes now always show expand (▼) button** — Continuation notes are chunks of a larger note, so they must always be treated as "long" regardless of chunk size. The expand button allows revealing the full original note content. Previously, `isLongNote` returned false for small chunks (e.g. 11 lines < 40 truncateLines), hiding the expand button. Fixed by adding `forceIsLong` flag for continuation notes that bypasses the `isLongNote` check throughout `createNoteButtons` and the expand/cycle callbacks. Verified by monkey-patching the user's original v3.0.37 HTML.

## [3.0.37] - 2026-06-07

### Fixed
- **Expand/collapse buttons on continuation notes now affect the correct note** — When `chunkLargeNotes` splits a note across diagram fragments, the continuation block in the second fragment shares the same original note index (0) as the first chunk. `makeNotesCollapsible` used a simple `noteIndexOffset` that counted all blocks in preceding fragments, causing the continuation note's buttons to accidentally control a different note (e.g., the response note at the wrong index). Fixed by building a `fragContinuationMap` that maps the continuation block to original index 0 and subsequent blocks to their correct offset indices.

## [3.0.36] - 2026-06-07

### Changed
- **Combined table in THEN section is now conditional by data source** — ReqNRoll Then-step tables now render inline with their step instead of being merged into a combined input/output table, since ReqNRoll inputs and outputs don't have a guaranteed 1:1 row relationship. Kronikol Tabular Attributes (always positional 1:1) continue to use the combined table. LightBDD tables use key-based row alignment when shared key columns exist, or a row-count fallback when both tables have more than one row. Single-row tables without keys or linked output render inline.

## [3.0.35] - 2026-06-07

### Fixed
- **`findNoteGroups` no longer merges adjacent notes with different fill colors** — When a transparent path (`fill=#00000000`) appeared between two notes with different fills (e.g. `#e2e2f0` and `#feffdd`), the path collection loop merged them into one giant group. The hover buttons were positioned on the merged bounding box instead of the individual note. Now stops collecting paths when the fill color changes to a different visible fill, keeping each note as its own group with correctly positioned buttons.

## [3.0.34] - 2026-06-07

### Fixed
- **Hover buttons on tall notes now reposition to stay visible in the viewport** — For notes taller than the viewport (e.g. large SQL queries that wrap to many lines), the minus/plus button at the top-right corner was scrolled off-screen when viewing the middle of the note. The button now repositions to the visible portion of the note on each hover, using the SVG screen transform to calculate the current scroll position.

## [3.0.33] - 2026-06-07

### Fixed
- **Hover buttons now appear on continuation notes with Creole separators (proper fix)** — PlantUML renders `..text..` Creole separators as text/line/polygon elements that appear BEFORE the note's path elements in SVG DOM order. `findNoteGroups` only scanned forward from paths to texts, so these pre-path text elements were orphaned — the continuation note was detected as a group but its "Continued From Previous Diagram" text wasn't included, and hover events on that text area didn't trigger the buttons. Fixed by adding a sweep after group detection that collects orphaned text elements within each note's bounding box regardless of DOM order. Verified by monkey-patching the user's original HTML (v3.0.31) to confirm the fix resolves the exact bug.

## [3.0.32] - 2026-06-07

### Fixed
- **Hover buttons now appear on continuation notes with `..text..` Creole separators** — `findNoteGroups` computed the note bounding box from only the first path element. For notes containing PlantUML Creole `..text..` separators (like `..Continued From Previous Diagram..`), the first path could be a tiny decorative element, causing all subsequent text and line elements to be rejected as "outside the note." The continuation note was invisible to hover button detection even though it rendered correctly. Fixed by computing the note bounding box as the union of ALL collected path elements.

## [3.0.31] - 2026-06-07

### Fixed
- **Hover buttons now work on all continuation notes including large ones** — Reverted continuation markers back to the original `..Continued From Previous Diagram..` Creole separator syntax (v3.0.29-v3.0.30 used `<color:gray>[...]</color>` and `[...]` which triggered PlantUML Creole link/color rendering that also broke SVG note detection). The underlying `hasNoteFoldTriangle` fix from v3.0.30 now correctly handles notes with 4+ SVG path elements (created by the Creole separator rendering) by using the largest path as the body reference instead of assuming `paths[0]`.

## [3.0.30] - 2026-06-06

### Fixed
- **`findNoteGroups` now detects large notes with extra SVG paths** — Notes with 4+ path elements (common for large anchored notes like `note left of X` with 100+ lines) were not detected as notes because `hasNoteFoldTriangle` assumed the first path was the body. Now uses the largest path as the body reference, correctly identifying the fold triangle regardless of path order.

## [3.0.29] - 2026-06-06

### Fixed
- **Hover buttons now appear on continuation notes in chunked diagram fragments** — The `..text..` PlantUML Creole separator syntax used for continuation markers ("..Continued From Previous Diagram..") created extra SVG path elements that broke `findNoteGroups` note detection, preventing hover buttons (minus, up/down arrows) from being created. Changed to `<color:gray>[text]</color>` which renders as plain gray text without disrupting the SVG note shape structure.

## [3.0.28] - 2026-06-06

### Fixed
- **Context menu "Copy box text" now works on continuation notes in client-side-chunked diagrams** — When a large note (>15,000 chars) was expanded and split into multiple diagram fragments by `chunkLargeNotes`, right-clicking on the continuation note in a later fragment showed the context menu without the "Copy box text" option. The handler was searching for notes only in the first fragment's SVG. Now resolves the correct SVG via `ownerSVGElement` and matches the clicked note group to its source block even when `findNoteGroups` returns extra participant-shape candidates.

## [3.0.27] - 2026-05-24

### Changed
- **"Databases" toggle now also hides/shows `collections` participants (Redis, distributed caches)** — The "Databases Shown/Hidden" toggle in HTML reports previously only stripped `database` PlantUML participants. It now also strips `collections` participants, which are used for Redis and other distributed caches. The toggle button still appears automatically when either `database` or `collections` participants are present in the diagram source.

## [3.0.26] - 2026-05-22

### Changed
- **Icon arrow color changed from white to `#101E3C`** — Updated the arrow color in the SVG icon, PNG icon, and embedded favicon to dark navy (`#101E3C`) for better contrast.

## [3.0.25] - 2026-05-22

### Fixed
- **`EnsureStarted` no longer blocks concurrent callers on constrained thread pools** — The `InternalFlowActivityListener.EnsureStarted()` method previously used a `lock` that caused thread-pool starvation on Linux CI runners with limited worker threads. When multiple handler instances raced on first use, all threads blocked waiting for the single lock, leaving no threads available for async continuations. Now uses lock-free `Interlocked.CompareExchange` so only one thread performs initialization and all others skip immediately without blocking. ([#70](https://github.com/lemonlion/Kronikol/issues/70))

## [3.0.24] - 2026-05-22

### Fixed
- **`TrackDependenciesForDiagrams` no longer hangs when `PortsToServiceNames` includes port 80** — The synchronous `Send()` override previously called `.GetAwaiter().GetResult()` on `SendAsync()`, creating a classic sync-over-async deadlock when infrastructure HTTP clients (TestContainers, Docker, configuration fetches) used the synchronous code path. The handler now forwards synchronous requests directly without tracking, since sync callers are infrastructure-level and not test-initiated requests. ([#69](https://github.com/lemonlion/Kronikol/issues/69))

## [3.0.23] - 2026-05-22

### Fixed
- **`AddTestTrackingContextPropagation` no longer silently fails when other `IStartupFilter` registrations exist** — Previously used `TryAddSingleton<IStartupFilter>` which is a no-op when any other `IStartupFilter` is already registered (ASP.NET Core always has several). Now uses `TryAddEnumerable` which correctly checks by both service type and implementation type. ([#66](https://github.com/lemonlion/Kronikol/issues/66))
- **`ClientNamesToServiceNames` now matches Refit v9 assembly-qualified client names** — Refit v9 registers clients with assembly-qualified names (e.g. `...IntelligenceAiIIntelligenceAiApiClient, Data.Insights.Api, Version=1.0.0.0, ...`) that end with `PublicKeyToken=null`, not the interface name. The resolver now strips assembly qualification before matching, and uses a `Contains` fallback for assembly-qualified names when `EndsWith` with boundary fails. ([#67](https://github.com/lemonlion/Kronikol/issues/67))

## [3.0.22] - 2026-05-22

### Fixed
- **`ClientNamesToServiceNames` ends-with matching now requires a non-alphanumeric boundary** — The suffix fallback no longer matches when the preceding character is a letter or digit, preventing false positives (e.g. key `"Client"` incorrectly matching `"MyBetterClient"`). Only true separator characters (`+`, `.`, `-`, `/`, etc.) qualify as a valid boundary. ([#65](https://github.com/lemonlion/Kronikol/issues/65))

## [3.0.21] - 2026-05-22

### Fixed
- **`TrackDependenciesForDiagrams` no longer shadows custom `IHttpMessageHandlerBuilderFilter` registrations** — Previously, calling `TrackDependenciesForDiagrams` replaced `IHttpClientFactory` entirely with `TestTrackingHttpClientFactory`, bypassing the `DefaultHttpClientFactory` and all registered `IHttpMessageHandlerBuilderFilter` implementations (Polly, logging, user custom filters). Now registers a `TrackingHttpMessageHandlerBuilderFilter` instead, which coexists with other filters in the standard pipeline. ([#65](https://github.com/lemonlion/Kronikol/issues/65))
- **`ClientNamesToServiceNames` now uses ends-with matching as a fallback** — When the exact dictionary key doesn't match the client name (e.g. Refit generates `Refit.Implementation.Generated+...+IIntelligenceAiApiClient`), the resolver now scans dictionary keys for a suffix match. Exact match still takes priority. This makes `ClientNamesToServiceNames` work with Refit, source-generated clients, and other frameworks that produce complex HttpClient names. ([#65](https://github.com/lemonlion/Kronikol/issues/65))
- **`TrackDependenciesForDiagrams` now passes `builder.Name` as `clientName` to the tracking handler** — Previously, `TestTrackingHttpClientFactory` discarded the client name from `CreateClient(string name)`, making `ClientNamesToServiceNames` silently unreachable through the standard factory path. The new filter-based approach always forwards the builder name. ([#65](https://github.com/lemonlion/Kronikol/issues/65))

## [3.0.20] - 2026-05-22

### Fixed
- **`TestTrackingMessageHandler` uses `_currentTestInfoFetcher` when only the test-name header is present (no ID header)** — Previously, if only the `kronikol-test-name` header was in the incoming request (but not `kronikol-test-id`), the handler built a partial lambda that called `StringValues.First()` on an empty sequence, throwing `InvalidOperationException`. The catch block then skipped tracking entirely, silently dropping logs even though a valid `CurrentTestInfoFetcher` was configured. Now both headers must be present for the context-header path to activate; with only a partial header the handler falls back to `_currentTestInfoFetcher` and tracking proceeds normally.
- **`AddTestTrackingContextPropagation()` is idempotent** — Calling the extension method more than once (e.g. from a base `WebApplicationFactory` and a derived one) previously registered `TestTrackingContextStartupFilter` twice, causing `TestIdentityScope.Begin` to be called twice per request. Changed to `TryAddSingleton` so the second call is a no-op.
- **`_listenerStarted` field uses `Volatile.Read`/`Volatile.Write`** — The bare `bool` read/write was a data race under concurrent first requests on a shared `TestTrackingMessageHandler` instance. `Volatile.Read` and `Volatile.Write` establish the required memory ordering without a lock.

## [3.0.19] - 2026-05-21

### Fixed
- **`TestTrackingMessageHandler` no longer pre-sets `InnerHandler` in the constructor** — The handler now lazily initialises `InnerHandler` to `HttpClientHandler` on first `SendAsync` only if nothing else has set it. This fixes `InvalidOperationException` when using the handler with `IHttpClientFactory`'s `CreateHandlerPipeline()`, `AddHttpMessageHandler<T>()`, or `IHttpMessageHandlerBuilderFilter`. The `SafeTrackingDelegatingHandler` wrapper is no longer needed. ([#62](https://github.com/lemonlion/Kronikol/issues/62))
- **`TestTrackingMessageHandler` and `MessageTracker` now fall back to `TestIdentityScope.Current` when `CurrentTestInfoFetcher` is null or throws** — Previously, if there was no `HttpContext` and no working fetcher delegate, tracking was silently skipped. Now the resolution chain continues through `TestIdentityScope.Current` → `TestIdentityScope.GlobalFallback` before giving up. This enables tracking inside `Task.Run` and other fire-and-forget scenarios when combined with `AddTestTrackingContextPropagation()`. ([#63](https://github.com/lemonlion/Kronikol/issues/63))

### Added
- **`AddTestTrackingContextPropagation()` extension method** — Registers `TestTrackingContextMiddleware` via `IStartupFilter`, which reads `kronikol-test-name` and `kronikol-test-id` headers from incoming requests and establishes a `TestIdentityScope` for the request duration. The `AsyncLocal`-based scope propagates into `Task.Run`, background threads, and other async dispatch, enabling tracking of fire-and-forget HTTP calls made within a request. ([#63](https://github.com/lemonlion/Kronikol/issues/63))
- **Documentation: Refit / HttpClientFactory integration guide** — Added Pattern 9 to the [[Tracking Dependencies]] wiki page covering `IHttpMessageHandlerBuilderFilter` with Refit named clients, `AddTestTrackingContextPropagation()` for fire-and-forget, and semaphore flush patterns. Updated `InnerHandler` documentation to reflect the new lazy initialization behaviour. ([#64](https://github.com/lemonlion/Kronikol/issues/64))

## [3.0.18] - 2026-05-21

### Added
- **Kronikol.Extensions.MongoDB.V2** — New package for projects using MongoDB.Driver v2.x. Identical API to `Kronikol.Extensions.MongoDB` but compatible with `MongoDB.Driver.Core` v2.x transitive dependencies. Fixes CS0433 build errors when platform libraries pin to MongoDB.Driver v2. ([#61](https://github.com/lemonlion/Kronikol/issues/61))
- **`AddRedisConnectionMultiplexerTracking()`** — New extension method that decorates `IConnectionMultiplexer` registrations so that `GetDatabase()` returns tracked `IDatabase` instances. Use this when your application uses a Redis wrapper library that doesn't expose `IDatabase` via DI. ([#60](https://github.com/lemonlion/Kronikol/issues/60))
- **`RedisTrackingConnectionMultiplexer`** — Public wrapper for manual `IConnectionMultiplexer` tracking outside DI.

### Fixed
- **ReqNRoll binding discovery** — Framework assemblies (`Kronikol.ReqNRoll.xUnit2`, `Kronikol.ReqNRoll.xUnit3`, `Kronikol.ReqNRoll.TUnit`) now contain discoverable `[Binding]` classes, eliminating the need to add `Kronikol.ReqNRoll.Core` to `reqnroll.json` `bindingAssemblies`. ([#59](https://github.com/lemonlion/Kronikol/issues/59))
- Added idempotency guards to ReqNRoll hooks to prevent double-execution if both Core and framework assemblies are scanned.

## [3.0.17] - 2026-05-21

### Fixed
- **Default favicon updated from old TTD monogram to new Kronikol scroll icon** — The generated HTML reports now display the Kronikol parchment/scroll icon (with white directional arrows) instead of the legacy blue TTD monogram.
- **Diagnostic report title changed from "TTD Diagnostic Report" to "Kronikol Diagnostic Report"** — Fixes remaining TTD branding reference in the diagnostic report's HTML `<title>` element.

## [3.0.16] - 2026-05-21

### Changed
- **Happy path tag detection now recognizes `happy-path`, `happy_path`, and `happypath` (case-insensitive)** in BDDfy and ReqNRoll adapters. Previously only `happy-path` was matched. Users tagging scenarios with `@happy_path` or `@happypath` will now have those scenarios correctly identified as happy paths, ordered first, badged, and filterable via the "Happy Paths Only" button.

## [3.0.15] - 2026-05-21

### Fixed
- **Given/When step tables suppressed when combined table exists** — When a scenario had tabular parameters in both Given/When and Then steps (triggering the combined results table), ALL inline step tables were suppressed — including the input tables from Given/When steps which should remain visible inline. Now only Then/And-after-Then step tables are suppressed (moved to the combined table), while Given/When/And-after-Given tables continue to render inline as expected.

## [3.0.14] - 2026-05-20

### Fixed
- **"Copy Highlighted Text" inserts newlines at word-wrap boundaries in SVG notes** — When selecting text within an SVG sequence diagram note and using the context menu's "Copy Highlighted Text" option, the clipboard text now preserves the original spacing from the PlantUML source. Previously, the browser's `getSelection().toString()` would insert `\n` at every SVG `<text>` element boundary (word-wrap positions). The fix normalizes the selected text by character-mapping it back to the original note source, preserving real line breaks while removing artificial ones.

## [3.0.13] - 2026-05-20

### Fixed
- **Expand arrow showing on fully-visible notes when headers are hidden** — When toggling "Headers: Hidden", the ▼ expand arrow (and truncated-state buttons) appeared on notes whose visible body content fit entirely within the truncation limit. The `isLongNote` function counted ALL lines including `<color:gray>` header lines, while the rendering logic excluded them. Added a `headersHidden` parameter to `isLongNote` that, when true, counts only non-gray effective lines (mirroring `buildSourceWithNoteStates` logic). Updated all 7 call sites to pass the appropriate headers-hidden state. Notes that become "short" after hiding headers now auto-transition from truncated (step 1) to expanded (step 2).

## [3.0.12] - 2026-05-20

### Fixed
- **Assertion formatting includes "to string" from ToString() calls** — When an assertion subject contains `.ToString()` (e.g., `response.StatusCode.ToString().Should().Be("200")`), the formatted output previously included "to string()" in the readable sentence. Now strips " to string() " and " to string " from the final formatted assertion text, producing cleaner output like "Response status code should be \"200\"".

## [3.0.11] - 2026-05-20

### Fixed
- **Syntax error in chunked note fragments when arrow uses colon-adjacent format** — Fixed a bug where expanding a large note (>15,000 characters) produced `Syntax Error? (Assumed diagram type: class)` in continuation fragments when the preceding arrow used colon-adjacent message format (e.g., `db -> svc: OK` with no space before the colon). The anchor participant regex `(\S+)` in `chunkLargeNotes` captured the trailing colon as part of the participant name, producing `note right of breakfastProvider:` which PlantUML interprets as single-line note syntax rather than a multi-line note block. Changed the regex to `([^\s:]+)` to exclude colons from the participant capture.

## [3.0.10] - 2026-05-19

### Fixed
- **Empty intermediate diagrams when expanding chunked notes** — Fixed a bug where continuation fragments of notes exceeding 15,000 characters rendered as empty diagrams (showing only participants with no visible content). When `chunkLargeNotes` splits a large note into multiple chunks, intermediate chunks contained bare `note right` without a preceding message interaction. PlantUML requires a prior message to anchor a directional note; without one, it renders nothing. Fixed by modifying the note header in continuation chunks to `note right of <participant>` (or `note left of <participant>`), explicitly anchoring the note to the relevant participant extracted from the preceding arrow.

## [3.0.9] - 2026-05-18

### Fixed
- **Unclosed note in chunked diagram splits** — Fixed a bug where diagrams with notes exceeding 15,000 characters produced `Syntax Error? (Assumed diagram type: class)` when the first chunked part was also height-split. `chunkLargeNotes` inserts `__SPLIT_BOUNDARY__` markers between note chunks, but the first part (containing `@startuml` but no `@enduml`) was passed to `splitDiagramSource` without appending `@enduml`. This caused `parseDiagramStructure` to treat the last line (`end note`) as the end boundary, excluding it from the body and leaving the note unclosed in the height-split fragment.

## [3.0.8] - 2026-05-18

### Fixed
- **Colored arrow detection in diagram splitting** — Fixed a bug where PlantUML colored arrows (`-[#color]>` and `-[#color]->`) were not recognized by the diagram splitting logic. The `indexOf('->')` check missed colored forward arrows entirely (since `-[#438DD5]>` doesn't contain the literal `->` substring), causing `parseTraceUnits`, `countArrows`, and `estimateUnitHeight` to treat them as plain text. This led to malformed fragment splits with only assertion notes and no arrows, producing `Syntax Error? (Assumed diagram type: class)` when rendering multi-fragment diagrams. Replaced all `indexOf`-based arrow detection with regex `/-(\\[[^\\]]*\\])?-?>/` which matches plain (`->`, `-->`), colored (`-[#color]>`), and colored return (`-[#color]->`) arrow syntax.

## [3.0.7] - 2026-05-17

### Fixed
- **Binary CosmosDB TransactionalBatch content in diagrams** — HTTP responses and requests containing binary-framed content (HybridRow/RecordIO) are now detected and have embedded JSON documents extracted. This fixes CosmosDB `TransactionalBatch` operations producing garbled binary output in diagram notes. The generic binary detection in `HttpContentReader` scans the first 256 bytes for control characters and uses brace-depth counting with string-literal awareness to extract valid JSON objects.
- **Note collapse after filter toggle** — Fixed a bug where collapsing or expanding a diagram note after hiding databases or steps would render the wrong note content. The root cause was twofold: (1) note indices in the filtered PlantUML source didn't map back to the original source indices, and (2) the PlantUML TeaVM renderer's global state leaked between sequential renders that reused the same temporary element ID. Fixed by computing a `filteredToOrigMap` for note index translation and using unique counter-based render target IDs.

## [3.0.6] - 2026-05-17

### Changed
- **Background section renamed to "Background Steps"** — The collapsible `<details>` section for background steps in reports now displays "Background Steps" instead of "Background".
- **Background extraction skipped when remaining steps start with Given or When** — `BackgroundStepsDetector` no longer extracts common step prefixes as background when the first remaining step in any scenario has keyword "Given" or "When". This prevents a separate "Background Steps" section from appearing in standard Given-When-Then BDD scenarios. Background extraction now only produces a separate section when remaining steps start with continuation keywords (Then, And, But) or when all steps are common.

## [3.0.5] - 2026-05-17

### Changed
- **MongoDB response format** — Removed the synthetic `N document(s)` prefix from MongoDB response content. Responses now show only the raw JSON array. When results are truncated, a `... (N more documents not shown)` footer is appended.
- **SQL response format** — Removed the `N rows` prefix from SQL FullRows response content (Dapper, EF Core, SqlClient, Npgsql, MySqlConnector, Oracle). Responses now show only the JSON array. When results are truncated, the footer now reads `... (N more rows not shown)`.
- **Spanner response format** — Same treatment as SQL: removed `N rows [columns]` prefix from FullRows mode. Truncation footer updated to `... (N more rows not shown)`.
- **Increased default MaxResponseDocuments** — MongoDB `MaxResponseDocuments` default increased from 5 to 10.
- **Increased default MaxResponseRows** — SQL and Spanner `MaxResponseRows` default increased from 5 to 10 across all extensions (SqlClient, Npgsql, MySqlConnector, Oracle, Dapper, EF Core, Spanner).
- **Removed "Step: " prefix from step delimiter hnotes** — Step delimiter notes in sequence diagrams no longer include the redundant `Step: ` prefix (e.g. `Step: Given A user exists` → `Given A user exists`).

## [3.0.4] - 2026-05-17

### Fixed
- **Removed diagram fullscreen lightbox on mobile** — The tap-to-fullscreen overlay introduced in v2.37.4 has been reverted. Diagrams remain horizontally scrollable in their containers without a lightbox modal, which was confusing on mobile.
- **Diagram toggle button sizing on mobile** — Removed the `max-width: 5.5em` constraint on "Sequence Diagrams", "Activity Diagrams", and "Flame Chart" buttons that crushed them to 71.5px and caused unreadable text wrapping.
- **Diagram Settings button now full width on mobile** — The "⚙ Diagram Settings" toggle button is now `display: block` with left/right margins matching the diagram content, instead of a small inline button.
- **CI metadata box full width on mobile** — The CI (e.g. GitHubActions) metadata box now stretches to full width of the header row on mobile viewports, instead of being constrained to its content width.

## [3.0.3] - 2026-05-17

### Fixed
- **Binary/garbled response content in diagrams** — HTTP responses with `Content-Encoding: gzip`, `deflate`, or `br` (Brotli) are now properly decompressed before logging. This fixes CosmosDB and other extensions producing unreadable binary content in diagram notes instead of the expected JSON. Affected extensions: CosmosDB, DynamoDB, EventBridge, SNS, SQS, AtlasDataApi, BlobStorage, CloudStorage, StorageQueues, S3, and the core `TestTrackingMessageHandler`.

### Changed
- **Shared `HttpContentReader` utility** — Extracted a shared `HttpContentReader.ReadContentAsStringAsync()` method in the core `Kronikol` package that handles gzip, deflate, and Brotli decompression. BigQuery's existing private decompression method was replaced with this shared utility to eliminate duplication.

## [3.0.2] - 2026-05-17

### Changed
- **Toolbar spacing refinements** — Adjusted margins for toolbar controls: `.truncate-lines-label` now uses `margin-left: 0` and `margin-right: 0.3em`, `.toggle-btn` uses `margin-left: 0.5em` (was `1.5em`), and `.truncate-lines-select` uses `margin-left: 0` (was `0.3em`).

## [3.0.1] - 2026-05-17

### Changed
- **MongoDB: response documents now formatted as indented JSON** — Document previews in response arrows (from `cursor.firstBatch`) are now pretty-printed with 2-space indentation instead of compact single-line JSON. Nested objects and arrays are also properly indented. The `"N document(s)"` count prefix and `MaxResponseDocuments` truncation continue to work as before.

## [3.0.0] - 2026-05-17

### Changed
- **Complete rebrand: TestTrackingDiagrams → Kronikol** — The project, all NuGet packages, namespaces, and assemblies have been renamed from `TestTrackingDiagrams` to `Kronikol`. This is a **breaking change** requiring updates to package references and `using` directives.
  - **NuGet packages**: All packages renamed (e.g. `TestTrackingDiagrams` → `Kronikol`, `TestTrackingDiagrams.xUnit3` → `Kronikol.xUnit3`, `TestTrackingDiagrams.Extensions.Redis` → `Kronikol.Extensions.Redis`, etc.)
  - **Namespaces**: All `TestTrackingDiagrams.*` namespaces → `Kronikol.*`
  - **dotnet new templates**: Template short names changed from `ttd-*` to `kronikol-*` (e.g. `dotnet new kronikol-xunit3`). Install via `dotnet new install Kronikol.Templates`.
  - **Environment variables**: Prefix changed from `TTD_` to `KRONIKOL_` (e.g. `KRONIKOL_PLANTUML_RENDERING`). The old `TTD_` prefix is still supported as a fallback for backward compatibility.
  - **HTTP headers**: Test identity headers changed from `ttd-test-name`/`ttd-test-id` to `kronikol-test-name`/`kronikol-test-id`.
  - **JSON report field**: `ttdVersion` → `kronikolVersion`.
  - **GitHub repository**: Renamed from `lemonlion/TestTrackingDiagrams` to `lemonlion/Kronikol`.
  - **New icon**: Updated package icon to new scroll/parchment design.

## [2.37.4] - 2026-05-17

### Added
- **Mobile UX improvements for HTML test reports** — Comprehensive responsive design enhancements for viewing reports on phones and tablets (375px–768px viewports):
  - **Collapsible filter section** — Filters are collapsed behind a "Filters" toggle on mobile, keeping the search bar and content immediately accessible instead of pushing content 4–5 screens down.
  - **Sticky search bar** — The search input stays pinned at the top of the filtering box when scrolling, always accessible on mobile.
  - **Back-to-top floating button** — A "↑" button appears after scrolling 2+ viewport heights, scrolls smoothly to top on tap.
  - **Diagram tap-to-fullscreen** — Diagrams that are shrunk to fit mobile width can be tapped to open in a full-screen scrollable overlay (pinch-zoom friendly). Press Escape or tap outside to close.
  - **Horizontally scrollable diagrams** — Diagram containers use `overflow-x: auto` on mobile instead of shrinking to illegibility.
  - **Per-scenario diagram controls toggle** — The Details/Headers/Steps/Databases toolbar is collapsed behind a "⚙ Diagram Settings" button on mobile, reducing visual clutter.
  - **Touch target size bump** — Filter/toggle buttons get `min-height: 36px` and larger padding at ≤480px for easier tapping.
  - **Filter button text truncation** — Long dependency/category button labels truncate with ellipsis instead of overflowing.
  - **"lines" label hidden** — The truncation `lines` label is hidden at ≤480px to save horizontal space.
  - **Step duration `nowrap`** — Step duration badges no longer wrap to a separate line at ≤768px.
  - **Failure `<pre>` word-break** — Stack traces in failure details use `word-break: break-word` to prevent horizontal overflow on narrow screens.
  - Violet theme support for sticky search bar background and back-to-top button colors.

## [2.37.3] - 2026-05-17

### Fixed
- **Database response payloads now appear at Summarised verbosity for MongoDB, Dapper, and Spanner** — Same bug as v2.37.2 (response content unconditionally suppressed at `Summarised` verbosity regardless of `LogResponseContent`) also affected three additional extensions that were missed in the initial fix.
  - **MongoDB** (`MongoDbTrackingSubscriber`): `OnCommandSucceeded` main response and phase variant now check `LogResponseContent` before suppressing at `Summarised`.
  - **Dapper** (`TrackingDbCommand`): `LogResponse` (rows-affected path) and `LogResponseWithContent` (scalar/reader path) plus their phase variants now check `LogResponseContent`.
  - **Spanner** (`SpannerTracker`): `LogResponse` main content and phase variant now check `LogResponseContent`. `TrackingSpannerCommand.LogResponse` rows-affected gate also fixed.
  - 13 new tests across 3 extensions covering Summarised + LogResponseContent scenarios.

## [2.37.2] - 2026-05-17

### Fixed
- **Database response payloads now appear at Summarised verbosity** — Response content (row counts, column names, row previews, scalar values, HTTP response bodies) was unconditionally suppressed at `Summarised` verbosity, regardless of the `LogResponseContent` flag. Since `LogResponseContent` defaults to `true`, users at `Summarised` verbosity saw empty response arrows despite the v2.37.0 response payload feature. Response content is now included at all verbosity levels when `LogResponseContent` is `true`. Set `LogResponseContent = false` to restore empty response arrows.
  - **EF Core** (`SqlTrackingInterceptor`): `LogCommandExecuted`, `LogCommandExecutedWithContent`, and all response phase variant builders now respect `LogResponseContent` independently of verbosity.
  - **CosmosDB** (`CosmosTrackingMessageHandler`): New `LogResponseContent` property on `CosmosTrackingMessageHandlerOptions` (default: `true`). `GetResponseContent` and response phase variants now include content at `Summarised` when the flag is `true`.
  - **SQL DiagnosticSource base** (`SqlDiagnosticTracker`): `LogCommandEnd` and both `LogResponse` overloads now respect `LogResponseContent` at `Summarised`. Affects SqlClient, Npgsql, MySqlConnector, and Oracle DiagnosticSource trackers.
  - Request content, headers, and URI detail level remain controlled by verbosity as before — only response arrows are affected.

## [2.37.1] - 2026-05-17

### Changed
- **Templates: removed assertion tracking (beta)** — The `[assembly: TrackAssertions]` attribute and `Kronikol.AssertionTracking` package reference have been removed from all dotnet templates. IL weaving can interfere with some build configurations, so assertion tracking is now an opt-in beta feature. See the templates README for instructions on enabling it manually.

## [2.37.0] - 2026-05-17

### Added
- **Database response payload capture across all SQL extensions** — Response arrows in PlantUML sequence diagrams now show actual data (row counts, column names, row previews, scalar values) instead of being empty. Extends the response payload capability introduced in v2.36.5 for Spanner to all database extensions.
  - New shared `SqlResponseDetail` enum (`RowCountOnly`, `RowCountAndColumns`, `FullRows`) and `TrackingDbDataReader` wrapper that captures row data as the reader is consumed.
  - New `SqlTrackingOptionsBase` properties: `LogResponseContent` (default: `true`), `MaxResponseRows` (default: `5`), `MaxValueDisplayLength` (default: `500`), `ResponseDetail` (default: `RowCountAndColumns`).
  - **Oracle** (`TrackingOracleCommand`): `ExecuteReader`/`ExecuteScalar` now capture and log response content.
  - **Dapper** (`TrackingDbCommand`): `ExecuteReader`/`ExecuteScalar` now capture response content. New properties on `DapperTrackingOptions`: `LogResponseContent`, `MaxResponseRows`, `MaxValueDisplayLength`, `ResponseDetail`.
  - **EF Core** (`SqlTrackingInterceptor`): `ReaderExecuted` wraps readers with `TrackingDbDataReader`; `ScalarExecuted` logs scalar values. New properties on `SqlTrackingInterceptorOptions`.
  - **SqlClient wrapping alternative**: New `TrackingSqlConnection`, `TrackingSqlCommand`, `TrackingSqlTransaction` classes and `SqlConnection.WithTestTracking()` extension method for connection-wrapping approach (alongside existing DiagnosticSource tracking).
  - **Npgsql wrapping alternative**: New `TrackingNpgsqlConnection`, `TrackingNpgsqlCommand`, `TrackingNpgsqlTransaction` classes and `NpgsqlConnection.WithTestTracking()` extension method.
  - **MySqlConnector wrapping alternative**: New `TrackingMySqlConnection`, `TrackingMySqlCommand`, `TrackingMySqlTransaction` classes and `MySqlConnection.WithTestTracking()` extension method.
  - **MongoDB**: `OnCommandSucceeded` now extracts `cursor.firstBatch` documents at Detailed verbosity, showing document previews. New `MongoDbTrackingOptions` properties: `LogResponseContent` (default: `true`), `MaxResponseDocuments` (default: `5`).
  - **Elasticsearch**: Response body is now included at Detailed verbosity (previously only at Raw). New `ElasticsearchTrackingOptions` property: `LogResponseContent` (default: `true`).
  - Set `LogResponseContent = false` on any extension to restore previous empty-arrow behaviour.

### Fixed
- **PlantUML render stuck state recovery** — Added timeout (15s) to the initial `processQueue` MutationObserver render path, which previously had no timeout and could leave `_plantumlRendering` stuck `true` indefinitely. Added 15s stuck-render recovery to `processRenderQueue`'s wait-for-idle loop. Added `_renderCompleteCount` global to allow tests to detect render queue completion without relying on SVG content comparison.
- **Report search/filter now hides empty `.rule` elements** — The `applyVisibility` JavaScript function previously only handled feature and scenario visibility. Rules containing no visible scenarios are now also hidden during search and filter operations.
- **DeferredLogFlushHandler flaky test** — Fixed test interference where leftover pending entries from other tests caused `Does_not_flush_when_no_pending_entries` to fail. The test now clears `PendingRequestResponseLogs` before running.
- **PlantUML render timeouts increased** — The `processRenderQueue` render poll timeout increased from 5s (`pollCount > 20`) to 30s (`pollCount > 120`) to accommodate slow renders under parallel test load.
- **NoteButtonsAfterHeaderHide race condition** — `ToggleHeadersHidden`/`ToggleHeadersShown` helpers now capture SVG outerHTML before clicking and wait for both `!_plantumlRendering` and SVG change with note element existence, preventing interaction with stale elements during re-render. Timeouts increased from 15s to 60s.
- **RenderAllDiagramsAndWait stability** — Now waits for `!window._plantumlRendering` in addition to SVG count, ensuring all initial renders complete before tests interact with the page.

## [2.36.5] - 2026-05-16

### Added
- **Spanner gRPC interceptor captures response payloads** (#58) — The `SpannerTrackingInterceptor` now extracts and logs response content from Spanner gRPC calls. Response arrows in PlantUML sequence diagrams show row counts, column names, commit timestamps, and batch DML results instead of being empty.
  - New `SpannerResponseDetail` enum (`RowCountOnly`, `RowCountAndColumns`, `FullRows`) controls response detail level.
  - New `SpannerTrackingOptions` properties: `LogResponseContent` (default: `true`), `MaxResponseRows` (default: `5`), `ResponseDetail` (default: `RowCountAndColumns`).
  - Streaming responses (`ExecuteStreamingSql`, `StreamingRead`) are accumulated via `TrackingAsyncStreamReader` and logged after stream completion or disposal.
  - `SpannerResponseFormatter` formats `ResultSet`, `CommitResponse`, `ExecuteBatchDmlResponse`, and `PartialResultSet` chunks with truncation for large values (500 chars) and wide tables (20 columns).
  - Set `LogResponseContent = false` to restore previous empty-arrow behaviour.

## [2.36.4] - 2026-05-15

### Fixed
- **Hover buttons not appearing on split/fragmented diagrams** — When a diagram splits into multiple fragments, `makeNotesCollapsible()` (which adds hover rects and toggle icons via SVG `getBBox()`) was called while fragment divs still had `display:none`, causing all dimensions to be zero. Hover rects and toggle icons were rendered but invisible. Post-render hooks are now deferred until after the swap when fragments become visible, ensuring `getBBox()` returns correct dimensions.

## [2.36.3] - 2026-05-15

### Fixed
- Fixed `DiagramContextMenuTests` unit tests (`SetNoteState_caches_rendered_svg`, `ProcessRenderQueue_caches_rendered_svg`) that expected old `container.innerHTML` caching pattern — updated to match new `renderTarget.innerHTML` approach from v2.36.2.

## [2.36.2] - 2026-05-15

### Fixed
- Fixed diagram content disappearing during re-render when note state changes on split/fragmented diagrams. Old SVG content now remains visible until new fragments are fully rendered, eliminating the blank flash.

## [2.36.1] - 2026-05-15

### Fixed
- Fixed test parallelism interference in CosmosDB and core correlation tests by adding `[Collection("TestCorrelationStore")]` to all classes that use shared `TestCorrelationStore` state.

## [2.36.0] - 2026-05-15

### Added
- **Parallel-safe background correlation** — New `TestCorrelationStore` system enables parallel-safe test attribution for background processing threads (Change Feed, Change Streams, Hangfire, hosted services) that cannot inherit `AsyncLocal` values. Replaces serial-only `GlobalFallback` for most use cases.
- **`TestCorrelationStore`** — Thread-safe static store that correlates work-item keys (document IDs, message keys) to test identities with configurable TTL and lazy eviction.
- **`CorrelatedProcessingScope`** — Helper that combines store lookup with `TestIdentityScope.Begin()` for one-call scope establishment.
- **`CorrelationKeys`** — Standardized key format helpers for all supported systems (Cosmos, Mongo, Kafka, ServiceBus, EventHubs, PubSub, SQS, SNS, StorageQueue, Custom).
- **`ProcessingCorrelation`** — Generic wrappers (`Wrap<T>`, `WrapSync<T>`, `WrapBatch<T>`) for custom background processors.
- **`ChangeFeedCorrelation`** (CosmosDB extension) — Wraps Change Feed Processor delegates to auto-resolve test identity from correlated document IDs.
- **`ChangeStreamCorrelation`** (MongoDB extension) — Wraps Change Stream processing delegates for test identity resolution.
- **`AutoCorrelateWrites`** option on CosmosDB and MongoDB extensions (default: `true`) — Auto-populates `TestCorrelationStore` on tracked write operations.
- **`AutoCorrelateOnConsume`** option on Kafka, ServiceBus, EventHubs, and PubSub extensions (default: `true`) — Auto-stores correlation on message consume for decoupled processing patterns.
- **`ChangeFeedKeyExtractor`** option on CosmosDB extension — Custom key extraction for composite partition key scenarios.
- **`ConsumeKeyExtractor`** option on Kafka extension — Custom correlation key extraction for consumed messages.
- **`TestCorrelationStore.OnResolveMiss`** — Diagnostic callback for debugging unresolved correlation lookups.
- **`TestCorrelationStore.Seed()`** — Pre-populate correlations for data that exists before the test runs.

## [2.35.8] - 2026-05-15

### Fixed
- **Diagram disappears momentarily when clicking note hover buttons** — On large diagrams that are split into multiple fragments, clicking a note expand/collapse button would cause the entire container to collapse to ~40px (from thousands of pixels) while re-rendering, causing a jarring page jump. The container now preserves its height via `minHeight` during re-render and clears it once the new SVG content is ready.
- **Pre-existing build error in DatabaseToggleTests** — Added missing `GenerateReportWithWideDatabaseParticipant` helper and `WideDatabaseParticipantPlantUmlSource` test data that `DatabaseToggleTests` referenced but did not exist.

## [2.35.7] - 2026-05-15

### Fixed
- **Race condition in parallel test execution** — Removed `RequestResponseLogger.Clear()` calls from `StepCollectorTests` that could wipe shared static state while `DefaultDiagramsFetcherTests` was running in parallel, causing intermittent "Sequence contains no matching element" failures on `Svg_format_produces_svg_url`.

## [2.35.6] - 2026-05-15

### Fixed
- **Note dblclick from collapsed goes to truncated for long notes** — When a long note was collapsed, double-clicking it would incorrectly expand fully instead of going to the truncated state. The `isLongNote` check now uses the original note content lines (from the owner's original source) rather than the current rendered content, which only contains a short preview when collapsed.

## [2.35.5] - 2026-05-14

### Fixed
- **Database toggle no longer clips diagram left edge** — The `stripDatabaseCalls()` function was rewritten from regex-based to line-by-line parsing. Previously, toggling databases off left orphaned positional notes (e.g. `note<<eventNote>> left`) that rendered at negative x coordinates in the SVG viewBox, causing the left side of the diagram to disappear off-screen. The new implementation tracks removed database arrows and removes any subsequent positional note blocks that would otherwise be orphaned.

## [2.35.4] - 2026-05-14

### Changed
- **Span count warning requires over 100 spans** — The "This might indicate a problem/recursive loop in your test" warning now only appears when a scenario has both >= 10x the median span count AND more than 100 spans. Previously any >= 10x outlier triggered the warning, which produced false positives for small test suites.

## [2.35.3] - 2026-05-14

### Fixed
- **Empty rule containers shown when search/filter hides all child scenarios** — The `applyVisibility()` function now hides `.rule` elements when all their child scenarios are filtered out, identical to how features are hidden. Rules are evaluated before features so that a feature correctly hides when all its rules are empty. Fixes [#57](https://github.com/lemonlion/Kronikol/issues/57).

## [2.35.0] - 2026-05-14

### Added
- **Client-side diagram splitting for BrowserJs rendering** — When using `PlantUmlRendering.BrowserJs`, diagrams are no longer split server-side. Instead, the full PlantUML source is sent to the client, where JavaScript splits diagrams at trace boundaries when estimated height exceeds 12,000px and chunks response notes exceeding 15,000 characters. This enables adaptive retry with smaller split thresholds if the PlantUML JS renderer reports a "too large" error, producing optimal rendering without arbitrary server-side limits.
- **`splitDiagramSource()` / `chunkLargeNotes()` client-side functions** — New JavaScript functions parse PlantUML structure and split at trace boundaries based on estimated height, or chunk large notes with continuation markers.
- **Fragment rendering pipeline** — Diagrams that split client-side render as `.puml-fragment` child elements with sequential rendering (respecting TeaVM global state). Post-render hooks (collapsible notes, toggles) are fragment-aware with global note indexing across fragments.

### Fixed
- **PlantUML `<style>` block parsing in `parseDiagramStructure`** — Avoided emitting literal `<style>` in JavaScript string comparisons to prevent naive HTML style-tag counters from false-positiving.

## [2.34.4] - 2026-05-14

### Added
- **Report: Database visibility toggle** — When any diagram contains a `database` participant (CosmosDB, EF Core, MongoDB, DynamoDB, Redis, Spanner, BigQuery, etc.), a "Databases Shown" / "Databases Hidden" toggle button appears in the report toolbar. Clicking it hides all database participants and their associated request/response arrows, simplifying diagrams to show only service-to-service communication. Works at both report level and per-scenario level. Shown by default.

## [2.34.3] - 2026-05-14

### Added
- **StepTracking: Conditional step bypass (`SkipIf` / `SkipReason`)** — All step attributes (`[GivenStep]`, `[WhenStep]`, `[ThenStep]`, `[ButStep]`, `[ButWhenStep]`, `[Step]`) now accept `SkipIf` and `SkipReason` properties. When `SkipIf` names a `bool` property or field on the test class (or base class) that evaluates to `true` at runtime, the step body is not executed and the step is recorded as `Bypassed` with the optional reason. The IL weaver resolves the member at compile time via Cecil (no runtime reflection). Async methods return `Task.CompletedTask` on bypass. If the named member doesn't exist or isn't `bool`, a build warning is emitted and the step executes normally.
- **`StepCollector.BypassStep()` method** — New method to programmatically mark the current active step as bypassed. Called by the IL weaver's SkipIf preamble, but also available for direct use.
- **`ScenarioStep.BypassReason` property** — The bypass reason is now available on the step model for report rendering.

## [2.34.2] - 2026-05-13

### Fixed
- **PlantUML JSON formatting** — Backtick characters (`` ` ``) in JSON content (e.g. BigQuery table references like `` `project.dataset.table` ``) are now rendered as literal backticks in PlantUML notes instead of being unicode-escaped as `\u0060`.

## [2.34.1] - 2026-05-13

### Fixed
- **CS1570/CS1573** — Fixed XML doc warnings in `Track.cs` (stray `</summary>` tag) and `RequestResponseLogger.cs` (missing `<param>` for `dependencyCategory`).
- **NU1608** — Resolved xunit version constraint conflict in LightBDD.xUnit2 by pinning `xunit` 2.9.3 to override transitive 2.4.2 from LightBDD.XUnit2 3.12.0.
- **NU5129** — Suppressed false-positive buildTransitive pack warning from .NET SDK 10.0.300 in StepTracking and AssertionTracking weaver packages.
- **CS8619** — Fixed nullability mismatch (`object?[]` vs `object[]`) in xUnit3 argument extraction for `TestMethodArguments`.
- **CS0108** — Added `new` modifier to `WikiGifTests.WaitForDiagramSvg` which intentionally hides the base class method.
- **CS1574** — Fixed unresolvable `BrowserContext` cref in PlaywrightFixture (changed to `IBrowserContext`).
- **xUnit1051** — Suppressed CancellationToken analyzer warning in E2E test project (Task.Delay in Playwright tests).
- **xUnit1031** — Suppressed blocking task warning in AssertionWeaverTests (intentional sync invocation of dynamically-loaded IL via reflection).
- **NU1902** — Suppressed SharpCompress vulnerability warning (no patched version exists; vulnerability is in `WriteToDirectory` which MongoDB doesn't expose).

## [2.34.0] - 2026-05-13

### Added
- **Automatic test identity propagation through messaging extensions** — All messaging extensions (Kafka, ServiceBus, EventHubs, PubSub, MassTransit) now automatically propagate test identity (`kronikol-test-name` and `kronikol-test-id`) through message headers/properties/attributes. When a test produces a message, the current test identity is injected into the message metadata. When a consumer/subscriber receives the message, the test identity is extracted and established as the ambient `TestIdentityScope`, enabling tracking to work correctly in event-driven architectures where ASP.NET applications listen to events rather than serving HTTP requests. Controlled via `PropagateTestIdentity` option (default: `true`) on each extension's options class.
- **`TestIdentityScope.SetFromMessage()`** — New method for establishing test identity from incoming message headers without creating a disposable scope. Identity persists on the async context until the next message is processed or explicitly cleared.
- **`TestTrackingMessageHeaders` constants** — New constants class defining `kronikol-test-name` and `kronikol-test-id` header names for cross-extension consistency.

## [2.33.79] - 2026-05-13

### Fixed
- **PlantUml: Strip null-valued JSON properties from diagram notes** — HTTP-captured response bodies (e.g. from BigQuery, CosmosDB, DynamoDB SDKs) often contain dozens of null-valued properties that add visual noise to sequence diagram notes. The JSON pretty-printer now recursively removes null properties before rendering, producing significantly cleaner and more readable diagrams.

## [2.33.78] - 2026-05-13

### Fixed
- **Extensions.BigQuery: Decompress gzip-compressed request/response bodies** — The BigQuery .NET client sends gzip-compressed HTTP bodies for large payloads. Previously, `ReadAsStringAsync()` produced garbled binary text in PlantUML diagram notes. The handler now detects `Content-Encoding: gzip` (and `deflate`) headers and decompresses before logging, producing readable JSON in the diagram notes.
- **PlantUml: Binary content fallback detection** — Added `IsBinaryContent` detection in `FormatNoteContent` as a safety net for any extension that encounters non-text content. Binary content is replaced with a `[binary content]` placeholder instead of rendering garbled characters in diagram notes.

## [2.33.77] - 2026-05-13

### Added
- **Extensions.MongoDB: Implement `IEventSubscriber` on `MongoDbTrackingSubscriber`** (#56) — `MongoDbTrackingSubscriber` now implements the MongoDB driver's `IEventSubscriber` interface, enabling direct use with `InMemoryEmulator.MongoDB` v1.1.0's `CommandEventSubscriptionBuilder.Subscribe(IEventSubscriber)` and any other event source that accepts `IEventSubscriber`. Usage: `options.ClusterConfigurator = builder => builder.Subscribe(new MongoDbTrackingSubscriber(opts));`. No new package dependencies required.

## [2.33.76] - 2026-05-13

### Fixed
- **Flat parameter table DOM ordering** — Flat parameter table now renders before grouped table in DOM so it is the first visible table (fixes flatten toggle button visibility).
- **Step-level image attachments render with lightbox and caption** — Step-level image attachments now have the same lightbox link wrapper and filename caption as scenario-level rendering.

## [2.33.75] - 2026-05-13

### Fixed
- **Namespace stripping corrupting unquoted dotted parameter values in LightBDD step text** — URLs (`http://idp.example.com`), OAuth scopes (`user.read`), and config keys were being truncated by `StripNamespacesFromText` which cannot distinguish them from namespace-qualified type names. Stripping now only applies to template literal segments in `BuildTextSegments` which never contain parameter values.

## [2.33.74] - 2026-05-13

### Fixed
- **AssertionTracking: BadImageFormatException with inherited generic base class fields** (#55) — Import field references via `Module.ImportReference()` to ensure proper metadata token generation for fields on generic instance types. Added `IsFieldAccessibleOnType()` check in lambda body and ldtoken detection paths to skip fields not in the outer class's inheritance chain.

## [2.33.73] - 2026-05-13

### Fixed
- **AssertionTracking: IL weaver crashing CLR with 3+ level member access chains** (#55) — The standalone `ldfld` detection in `DetectCapturedVariables()` incorrectly captured intermediate chain members (e.g. `Request` in `_postSteps.Request.MerchantName`) as independent variables. Fixed by checking whether the preceding instruction is `ldfld`, `callvirt`, or `call` — if so, the `ldfld` is a chain continuation and is skipped.

## [2.33.72] - 2026-05-12

### Added
- **Image attachments render as inline `<img>` previews with lightbox** — Image extensions (`.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`) now render as thumbnails (320×240px max) with click-to-expand fullscreen lightbox overlay. Non-image attachments continue as download links.

### Fixed
- **Skip background step detection when scenarios start with And/When** — The `BackgroundStepsDetector` now skips extraction for a rule group when any scenario's first step has keyword "And" or "When", preventing false positives where action/continuation steps were incorrectly identified as background steps.
- **Step-param-table columns getting blue col-highlight** — The `highlightColumns` JS was applying `col-highlight` to `.step-param-table` columns when their name matched an outline parameter name, causing DataTable step arguments to show an unexpected blue first column. Fixed by scoping the highlighting to `.step-param-combined-table` only.

### Changed
- **Rule border styling** — Changed `.rule` `border-left` colour to `rgb(100, 100, 100)`.

## [2.33.71] - 2026-05-12

### Changed
- **Background Steps renamed and restricted to ReqNRoll** — Background step detection now only applies to ReqNRoll scenarios. Added `InlineBackgroundSteps` option to control whether background steps are shown inline or extracted into a separate section.

## [2.33.70] - 2026-05-12

### Fixed
- **LightBDD dotted string parameter values truncated in reports** — Parameter values containing dots (e.g. URLs, config keys) were being incorrectly processed by namespace stripping logic, causing truncation in the rendered report.

## [2.33.69] - 2026-05-12

### Fixed
- **AssertionTracking throws BadImageFormatException when intercepting LINQ OrderByDescending + BeEquivalentTo chain** (#54) — When a test class inherited from a generic base class with a raw generic parameter field (e.g., `protected internal TResponse? Response;`), the assertion weaver emitted an invalid `box !0` instruction (unresolved `GenericParameter`) in the non-generic async state machine's MoveNext method. The CLR could not resolve the type parameter in this context, causing `BadImageFormatException (0x8007000B)`. Fixed by adding `ResolveFieldType()` which substitutes concrete type arguments from the declaring `GenericInstanceType` before emitting box instructions. Applied to all captured variable detection paths (chained field access, lambda body scanning, expression tree tokens, and standalone field references).

## [2.33.68] - 2026-05-12

### Fixed
- **ReqNRoll attachments duplicated N times** — The `AttachmentAddedEvent` handler was registered in `BeforeScenario`, which runs for every scenario, but the `ITestThreadExecutionEventPublisher` is test-thread-scoped. After N scenarios ran on the same thread, there were N handlers, each adding the same attachment to `StepCollector`. Fixed by tracking subscribed publishers in a `ConditionalWeakTable` and only subscribing once per publisher instance.

## [2.33.67] - 2026-05-12

### Added
- **Rule name included in report search data** — The `data-search` attribute on scenario and parameterized group elements now includes the Rule name (when present), allowing users to find scenarios by searching for their Gherkin Rule name in the report search bar.

## [2.33.66] - 2026-05-12

### Fixed
- **ReqNRoll attachments not captured in reports** — Attachments added via `IReqnrollOutputHelper.AddAttachment()` were silently lost because the `BeforeScenario` hook tried to wrap the output helper using BoDi's `RegisterInstanceAs`, which throws when the type has already been resolved (the preceding `Resolve` call marks it as resolved). The exception was swallowed by the catch block. Fixed by subscribing to ReqNRoll's `AttachmentAddedEvent` via `ITestThreadExecutionEventPublisher` instead, which reliably captures attachments using the correct ReqNRoll scenario ID and routes them to the appropriate step or scenario in `StepCollector`.

## [2.33.65] - 2026-05-11

### Added
- **Scenario-level attachments** — When `Track.Attachment()` is called with no active step (i.e., outside of any `Track.Step()` scope), the attachment is now rendered in the HTML report at the scenario level and included in JSON/XML/YAML data reports. Previously, these attachments were silently discarded because no adapter read the `StepCollector.GetScenarioAttachments()` data. All adapters (xUnit3, xUnit2, NUnit4, TUnit, MSTest, BDDfy, LightBDD, ReqNRoll) now wire scenario-level attachments into the `Scenario` model. The `CopyAttachmentsToReportsFolder` logic also processes scenario-level attachments. JSON/XSD schemas updated.

### Fixed
- **Bracket-appended TableRef segments missing formatted values and spacing** — When LightBDD steps had bracket-appended parameters like `[grantTypes: "{0}"]`, the `TableRef` segments were created without the `FormattedValue` from `INameParameterInfo`, and no space was inserted before each `TableRef`. Now the formatted value is passed through and spaces are added between consecutive segments.

## [2.33.64] - 2026-05-11

### Fixed
- **Data reports (JSON/XML/YAML) now include `rule`, `backgroundSteps`, `outlineId`, `exampleValues`, and step `attachments`** — These fields were present on the `Scenario` and `ScenarioStep` models and rendered correctly in the HTML report but were missing from the JSON, XML, and YAML data report serializers. All three formats now emit the full scenario metadata. JSON/XSD schemas updated accordingly.
- **Image attachments now render as inline `<img>` elements in HTML report** — Image file attachments (`.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`) were rendered as plain `<a>` download links. They now render as `<img class="attachment-image">` elements for immediate visual feedback.
- **ReqNRoll attachment capturing now works without plugin discovery** — The `AttachmentCapturingPlugin` was never loaded by ReqNRoll because it only discovers plugins from DLLs named `*.ReqnrollPlugin.dll`, but our assembly is `Kronikol.ReqNRoll.Core.dll`. The `IReqnrollOutputHelper` wrapping is now performed in the `[BeforeScenario]` hook (which IS discovered via `bindingAssemblies`), ensuring `Track.Attachment()` is called for all `outputHelper.AddAttachment()` calls.
- **TableRef segments with formatted values but no matching parameter now render the value** — When a `StepTextSegment.TableRef` had a `TableReferenceFormattedValue` (e.g., from bracket-appended params in CompositeStep methods) but no matching `StepParameter`, the renderer showed the parameter name as plain text instead of the formatted value. Now renders as `<span class="step-param-inline">` with the formatted value.

## [2.33.60] - 2026-05-11

### Fixed
- **step-table-ref buttons do nothing for simple inline parameter values** — When a step had parameters like `grantTypes: "authorisationcode"` or `allowRefreshToken: "True"` (simple scalar values, not tabular or complex objects), the report rendered dead `<button class="step-table-ref">` elements that did nothing on click. Now simple inline values render as `<span class="step-param-inline">` showing the actual value, and parameters with no match render as plain text. Buttons are only produced for parameters that have a backing table/tree or a large complex value to expand.

## [2.33.59] - 2026-05-11

### Fixed
- **Report page freeze (~60s) on large reports caused by unscoped DOM queries** — The `selectRow()` function used `document.querySelectorAll` with `[id^="prefix-"]` attribute selectors to find detail panels, diagrams, activity diagrams, and flame charts. On a 141MB report with 196 scenarios and ~8,600 diagrams, each of these selectors scanned the entire 2M+ node DOM. Additionally, the `DOMContentLoaded` handler called `selectRow()` for every `.param-test-table` at page load — triggering full-document scans before the page was even interactive. Fixed by: (1) scoping all `querySelectorAll` calls in `selectRow()` to `table.closest('.scenario')` instead of `document`, (2) extracting column highlighting into a lightweight `highlightColumns()` function, (3) calling `highlightColumns()` instead of `selectRow()` from the `DOMContentLoaded` handler, and (4) guarding the detail-states sync to skip when the outgoing panel is the same as the incoming panel (which is always true at initial load). Load time drops from ~60s to ~5s on a 196-scenario Platform.IdentityProvider report.

## [2.33.58] - 2026-05-11

### Fixed
- **Internal flow activity diagrams now use JSON script block instead of HTML attributes** — The v2.33.55 migration of diagram data from `data-plantuml-z` HTML attributes to the `<script id="puml-data">` JSON block only covered sequence diagrams. Internal flow activity diagrams (whole-test-flow and per-boundary) still emitted `data-plantuml-z` directly on each `<div>`, leaving 7,231 large attributes (~85MB) in the HTML for projects with many internal flow spans. These are now stored in the same `diagramDataMap` and emitted in the JSON block. The JS `getPumlZ()` helper already looks up by element ID in the JSON map, so no client-side changes were needed.

## [2.33.57] - 2026-05-11

### Fixed
- **Increased E2E test timeouts for CI stability** — Two `NoteButtonsAfterHeaderHideTests` tests (`Headers_hidden_full_3_state_cycle` and `Headers_hidden_up_arrow_visible_when_expanded`) were timing out on GitHub Actions due to slower headless Chromium execution under parallel test load. Increased wait timeouts from 10s to 30s and added an explicit `WaitForNoteElements()` guard after the collapsed-state transition to ensure rendering is fully complete before asserting button presence.

## [2.33.56] - 2026-05-11

### Fixed
- **Assertions lost when LightBDD steps have native sub-steps** — When a LightBDD step had composite sub-steps (e.g., a `Then` step with `And` sub-steps), tracked assertions made during those sub-steps were silently discarded. The merge logic in `FeatureResultExtensions.MapStep()` now passes collected assertion data recursively into sub-step mapping, and appends any parent-level assertions that were made outside the native sub-steps.

### Added
- **`IncludeTrackedAssertionsInStepList` option** — New `StepTrackingOptions.IncludeTrackedAssertionsInStepList` flag (default: `true`) allows disabling assertion sub-steps in the report step list. When set to `false`, `Track.That()` assertions still appear in sequence diagrams but are not added as sub-steps under their parent step.

## [2.33.55] - 2026-05-11

### Changed
- **Diagram data moved from HTML attributes to JSON script block** — PlantUML diagram source data (gzip+base64 compressed) is no longer stored in individual `data-plantuml-z` HTML attributes on each `<div>` element. Instead, all diagram data is consolidated into a single `<script id="puml-data" type="application/json">` block emitted before `</body>`. This eliminates the HTML parser bottleneck caused by tokenizing thousands of large quoted attribute values (e.g., 8,635 diagrams × ~12KB each ≈ 100MB of attribute data). The browser's HTML tokenizer processes script content as a single text node without quote/entity handling, and `JSON.parse()` is called lazily only when diagram data is needed. Backward compatibility is maintained: the JS `getPumlZ()` helper falls back to `getAttribute('data-plantuml-z')` for internal flow and component diagrams that still use the attribute approach.

## [2.33.54] - 2026-05-11

### Added
- **`content-visibility: auto` on `.scenario` elements** — Tells the browser to skip layout and paint for off-screen scenario sections, significantly reducing initial rendering cost on large reports. Combined with the existing feature-level `content-visibility`, this means only scenarios actually visible in the viewport are rendered. On a 102MB report with 196 scenarios (each containing ~44 diagrams), this avoids laying out ~190 off-screen scenarios on page load.

## [2.33.53] - 2026-05-11

### Added
- **Attachment files automatically copied to Reports folder** — When `Track.Attachment()` is used with absolute or non-relative file paths, the referenced files are now automatically copied into `Reports/attachments/` during report generation. The `<a href>` in the HTML is rewritten to `attachments/{filename}`, making attachment links work correctly when reports are uploaded to GitHub Pages, published as CI artifacts, or viewed from any location. Duplicate file names from different source paths are deduplicated (e.g. `report.txt`, `report_2.txt`). Paths already pointing to `attachments/` are left unchanged.

## [2.33.52] - 2026-05-11

### Changed
- **Search enrichment moved entirely server-side** — Previously, diagram participant names and request URLs were extracted client-side using a Web Worker that decompressed gzipped PlantUML source in the browser, causing a 30–60 second freeze on large reports (e.g., 102MB / 8,635 diagrams). Now the extraction happens at report generation time in C#, with terms baked directly into `data-search` and `data-row-search` attributes. The search bar is immediately usable on page load with no loading overlay, disabled state, or client-side decompression. Non-diagram search data (scenario names, step text, feature names, status, tags) remains unchanged.

### Removed
- **Client-side search enrichment infrastructure** — Removed the Web Worker, gzip decompression fallback, `enrichSearchData` script, loading overlay, disabled searchbar state, and `_diagramSearchTexts`/`_diagramRowSearchTexts` JS-side merge logic. CSS for `.search-loading-overlay`, `pulse-search-loading` animation, and `#searchbar:disabled::placeholder` also removed.

## [2.33.51] - 2026-05-11

### Fixed
- **Multi-line fluent assertion expressions not parsed correctly** — `AssertionExpressionFormatter` failed to parse expressions where `[CallerArgumentExpression]` captured multi-line code with whitespace before dots (e.g. `claim.Should()\n    .NotBeNull()\n    .And\n    .Be(value)`). The `.Should().` regex and `.And.` detection both required dots immediately adjacent with no whitespace. Now normalizes `\s+\.` → `.` before parsing, so multi-line fluent chains are formatted correctly.

## [2.33.50] - 2026-05-11

### Added
- **Step-table-ref buttons with no backing table now display their values** — When a step parameter is a complex object (record ToString like `TypeName { Name = Classic, Flour = Plain }`) that was rendered as a clickable table-ref button but had no corresponding tabular parameter, clicking the button previously did nothing. Now:
  - **Small values** (records with fewer than 5 simple fields): rendered inline as a styled span with grey background showing the parsed field values (e.g., `{ Name: Classic, Flour: Plain Flour }`), replacing the non-functional button entirely.
  - **Large values** (5+ fields or nested records): the button remains but clicking it toggles a formatted JSON expansion block below with proper indentation, unquoted numeric/boolean values, and null handling. A second click collapses it.

## [2.33.49] - 2026-05-11

### Changed
- **Search enrichment now indexes only participant names and request URLs from diagrams** — Previously, the entire decompressed PlantUML source text (~240MB across 8,635 diagrams) was indexed for search, causing a 30-60 second browser freeze even after bypassing DOM attributes (v2.33.47). The structured clone from Worker to main thread, GC pressure from holding ~240MB of strings, and the `join()` flush all contributed. Now the Worker extracts only deduped participant/actor/entity/database/boundary/control/collections/queue display names and HTTP request URLs (`GET: /path`, `POST: /api/endpoint`, etc.) from each diagram, reducing indexed data from ~240MB to ~1-2MB. Non-diagram search data (scenario names, step text, feature names, status, tags) remains unchanged.

## [2.33.48] - 2026-05-10

### Added
- **`Track.Attachment()` API for capturing file attachments** — New `Track.Attachment(filePath, name?)` static method attaches files to the current step (or scenario when no step is active). Attachments render as `<a class="step-attachment">` download links in the report. Name defaults to the file name when not provided. Works with all framework adapters.
- **`StepCollector.AddAttachment()` and `GetScenarioAttachments()`** — Backing store for `Track.Attachment()`. Supports step-level and scenario-level attachments, including nested steps.
- **Attachment merging in ReqNRoll and LightBDD adapters** — `StepCollector` attachments from `Track.Attachment()` are now merged into mapped steps alongside framework-native attachments (LightBDD `FileAttachments`).
- **`AttachmentCapturingOutputHelper` decorator** — Wraps Reqnroll's `IReqnrollOutputHelper` to automatically intercept `AddAttachment(filePath)` calls and route them through `Track.Attachment()`, so attachments added via Reqnroll's output helper appear in TTD reports.
- **`AttachmentCapturingPlugin` runtime plugin** — Reqnroll `IRuntimePlugin` that registers the `AttachmentCapturingOutputHelper` decorator at test thread initialization. Activated automatically via `[assembly: RuntimePlugin]`.
- **Attachment rendering E2E tests** — 4 Playwright tests verifying attachment link rendering, correct href, multiple attachments per step, and absence when no attachments.
- **11 unit tests** — 7 for `Track.Attachment` (step-level, scenario-level, name defaulting, no-op without test ID, nested steps, path separators) + 4 for `AttachmentCapturingOutputHelper` (delegation, Track integration, passthrough).

## [2.33.47] - 2026-05-10

### Fixed
- **Search enrichment bypasses DOM attributes entirely — eliminates remaining freeze on large reports** — v2.33.45 eliminated O(n²) writes but still called `setAttribute` to write ~240MB of decompressed PlantUML text into DOM attributes (`data-search`/`data-row-search`). On a 102MB report with 8,635 diagrams, `setAttribute` with multi-MB strings is fundamentally expensive: Blink allocates DOMString copies, runs CSS invalidation checks, and updates the attribute store for each call, taking 20-100ms per scenario. With 196 scenarios writing ~1-9MB each, this blocked the main thread for 30-60 seconds. Now stores decompressed diagram text in plain JS objects (`window._diagramSearchTexts`, `window._diagramRowSearchTexts`) instead of DOM attributes. The `fc()` filter cache builder and both row-level search paths merge the JS-side index at read time. V8 `join(' ')` of the same data takes ~2ms per scenario (vs ~20-100ms for `setAttribute`), reducing total flush time from 30-60 seconds to <500ms.

## [2.33.46] - 2026-06-09

### Added
- **Background steps detection and rendering** — Scenarios sharing a common step prefix (within the same Rule group) now have those steps automatically extracted into a collapsible "Background" section. The `BackgroundStepsDetector` uses a heuristic approach: it groups scenarios by Rule, identifies the longest common prefix of steps (matching by Keyword + Text), extracts them into `Scenario.BackgroundSteps`, and trims them from `Steps`. The Background section renders collapsed by default, before the Steps section, in both standard and parameterized scenario views. Wired into all three framework adapters (ReqNRoll, LightBDD, BDDfy).
- **Rule rendering E2E test coverage** — 8 Playwright tests covering rule section grouping, titles, open state, CSS classes, scenarios outside rules, multiple rules, scenario counts, and nested details.
- **Background rendering E2E test coverage** — 8 Playwright tests covering background section presence, summary text, collapsed default state, step count, step text, DOM ordering, absence when not applicable, and multiple scenarios with shared background.

## [2.33.45] - 2026-05-10

### Fixed
- **Search enrichment O(n²) DOM writes eliminated — no more 30-60s freeze on large reports** — v2.33.43's `flushResults()` wrote `setAttribute('data-search', existing + ' ' + text)` per diagram per Worker sub-batch, reading and rewriting the growing attribute string on every call. For 196 scenarios × 44 diagrams each, this caused ~3.9GB of cumulative DOM string reads+writes, blocking the main thread for 30-60 seconds. Now accumulates decompressed texts in JS arrays (`push` is O(1), no string copying) via `accumulateResults()`, then writes each element's `data-search` attribute exactly once at the end via `flushSearchData()`, batched 100 elements per `setTimeout` tick. Total main-thread blocking reduced from ~39 seconds to <200ms.

## [2.33.44] - 2026-05-10

### Fixed
- **Combined parameter table now renders inside the Steps section** — The `step-param-combined-table` (used when multiple steps each have tabular parameters) was rendered after the `</details>` closing tag of `scenario-steps`, making it invisible when the Steps section was collapsed. It now renders inside the Steps `<details>` element, after the final step.
- **Step table reference buttons now scroll to and highlight the target table** — Clicking a `step-table-ref` button (e.g. `recipe`) now smoothly scrolls to the associated parameter table and applies a brief yellow highlight flash (1.5s fade). Previously, the buttons attempted to toggle table visibility via a `step-param-table-collapsed` CSS class, which did not work. The up/down arrow indicators have been removed from the button text.
- **Combined table cells now have `data-param` attributes** — Each `<th>` and `<td>` in the combined parameter table now carries a `data-param` attribute matching the originating step parameter name, enabling the highlight JS to flash only the relevant columns when a step-table-ref button is clicked.

## [2.33.43] - 2026-05-10

### Fixed
- **Search data enrichment still freezing on 102MB reports due to memory pressure** — Three remaining bottlenecks fixed: (1) **176MB accumulator** — `accumulateResults` pushed all decompressed texts into arrays across all 44 Worker batches (~170MB held simultaneously), causing severe V8 GC pauses that froze the main thread for seconds at a time. Replaced with `flushResults` that writes each Worker sub-batch (50 results, ~1MB) directly to DOM attributes immediately and then discards. Peak memory reduced from ~170MB to ~1MB. (2) **4MB structured clone per onmessage** — the Worker accumulated ALL results internally and sent one giant `postMessage`. Now streams results per internal sub-batch of 50 with a `{ results, done }` envelope, reducing each structured clone from ~4MB to ~1MB. (3) **Deferred start** — `enrichSearchData()` fired immediately on DOMContentLoaded, competing with PlantUML diagram rendering setup. Now deferred by 50ms via `setTimeout(enrichSearchData, 50)` so diagrams begin rendering first. Also reduced `COLLECT_BATCH` from 200 to 100 for smaller postMessage payloads to the Worker.

## [2.33.42] - 2026-05-10

### Fixed
- **Step delimiters reappear when collapsing a note while steps are hidden** — `setNoteState()` rebuilt the diagram from `_noteOriginalSource` and applied `applyAssertionFilter()` but not `applyStepsFilter()`, so step delimiter hnotes were restored in the rendered source even though the toggle still said "Steps Hidden". Now wraps the source with `applyStepsFilter()` to match the `processRenderQueue()` pattern.

## [2.33.41] - 2026-05-10

### Fixed
- **Search data enrichment no longer freezes the browser on large reports** — Three blocking bottlenecks were identified and fixed in `enrichSearchData()`: (1) **Worker flooding** — all 44 collection batches were sent to the Worker simultaneously via rapid `setTimeout(0)` calls, causing 44 concurrent decompression chains and a flood of ~88 `onmessage` callbacks on the main thread. Now uses a serial pipeline: one batch is sent, the Worker processes it, returns results, then the next batch is collected and sent. The main thread does ~5ms of work per batch and is idle while the Worker runs. (2) **Expensive DOM queries in flush** — `flushSearchData` called `document.querySelector('[data-row-id="..."]')` for every unique row, which is a full document scan on a 102MB DOM (~2M+ nodes). With 100-500 rows at 50-200ms each, this alone caused 5-50 seconds of blocking JavaScript. Now caches element references during the collection phase and uses them directly during flush, eliminating all DOM queries. (3) **O(n²) string concatenation** — `accumulateResults` used `string += ' ' + text` which copies progressively larger strings for scenarios with many diagrams. Now uses array `push()` during accumulation and `join(' ')` at flush time.

## [2.33.40] - 2026-05-10

### Changed
- **Flat parameter table is now the default view for ReqNRoll parameterized tests** — When `ExampleFlatValues` are available (ReqNRoll scenarios with Gherkin Examples tables), the flat table showing original scalar columns is now displayed by default instead of the grouped/structured view. The grouped table starts hidden and can be revealed by clicking the toggle button. This provides a more intuitive default since the flat columns match the original Gherkin Examples table.

## [2.33.39] - 2026-05-10

### Fixed
- **LightBDD assertion sub-steps not nested under steps in the report step list** — `FeatureResultExtensions.MapScenario()` only mapped LightBDD's native `IStepResult.GetSubSteps()` and never consulted `StepCollector.GetSteps()`, so assertion sub-steps added via `Track.That()` / `StepCollector.AddAssertionSubStep()` during step execution were lost from the step list (though they still appeared as hnotes in diagrams). Now merges collected assertion sub-steps from `StepCollector` into the mapped steps, matching the existing ReqNRoll behaviour.

## [2.33.38] - 2026-05-10

### Fixed
- **Search data enrichment closure variable shadowing caused searchbar to stay disabled** — `enrichWithWorker()` and `enrichWithFallback()` received `collectIdx` as a parameter (passed by value from `enrichSearchData()`), which shadowed the outer closure variable that `collectBatch()` actually modifies. As a result, `sendNextBatch()` always checked the stale parameter value (0), never reaching the `else` branch to set `allCollected = true`, so `flushSearchData` and `onEnrichComplete` never fired — leaving the searchbar permanently disabled. Fixed by nesting all helper functions (`enrichWithWorker`, `enrichWithFallback`, `flushSearchData`, `collectBatch`, `decompress`, `accumulateResults`) inside `enrichSearchData()` so they share closure variables directly instead of receiving them as parameters.

## [2.33.37] - 2026-05-10

### Fixed
- **Search data enrichment fully chunked to prevent browser freezing** — The v2.33.33 Web Worker approach still froze the browser because DOM collection (reading 8,635 `data-plantuml-z` attributes in a tight loop), `postMessage` serialization (structured-cloning ~20-40MB in one call), and DOM flush (writing all `data-search` attributes synchronously) all blocked the main thread. Now every phase is chunked with `setTimeout` yields: DOM collection reads 200 elements per tick, items are sent to the Worker in 200-item batches instead of all at once, the Worker streams results back per-batch instead of accumulating all results, and `flushSearchData` writes 200 attributes per tick. The fallback path (no Worker) also uses smaller decompression batches (10 instead of 50).

## [2.33.36] - 2026-05-10

### Fixed
- **Parameterized test report: step layout state syncs across row switches** — When clicking through parameter table rows, the collapse/expand state of `<details>` elements (the Steps section, individual collapsible sub-steps, and failure result sections) is now synced from the outgoing panel to the incoming panel. Previously each row's panel maintained its own independent open/closed state, causing jarring layout shifts when switching between rows. Now the layout stays consistent and only the data values change.

## [2.33.35] - 2025-07-12

### Fixed
- **`TabularDeserializer` now supports C# records with primary constructors** — `Deserialize<T>()` previously called `Activator.CreateInstance<T>()` which requires a parameterless constructor, throwing `MissingMethodException` for record types like `record InvalidFieldFromRequest(string? Field, object? Value, string Reason)`. Now tries the parameterless constructor first (existing class behavior), then falls back to constructor-based instantiation matching parameters by sanitized name.

## [2.33.34] - 2025-07-12

### Added
- **`dependencyCategory` parameter on `ReplaceWithTracked` simplified overload** — The simplified `ReplaceWithTracked<TService>()` extension method in `Kronikol.Extensions.DispatchProxy` now accepts an optional `string? dependencyCategory` parameter, forwarding it to `TrackingProxyOptions.DependencyCategory`. Previously, only the full `TrackingProxyOptions` overload supported dependency categories for component diagram participant shapes and colours.

## [2.33.33] - 2025-07-12

### Fixed
- **Search data enrichment moved to Web Worker to prevent browser freezing** — The `enrichSearchData()` function decompressed `data-plantuml-z` attributes on the main thread, causing the browser to display "page not responding" on large reports (e.g. 102MB, 8,635 diagrams) even with the v2.33.30 Promise.all batching fix. All gzip decompression now runs in an inline Web Worker (background thread via Blob URL), keeping the main thread completely free for user interaction while the "Loading search data…" overlay is displayed. Falls back to the previous main-thread batching approach on browsers without Web Worker support.

## [2.33.32] - 2025-07-12

### Added
- **Flatten toggle for parameterized test tables** — ReqNRoll parameterized groups now render a `+` toggle button in the "Input Parameters" header. Clicking it switches from the default grouped/complex view (with R3 sub-tables and R4 expandable objects) to a flat view showing the original Gherkin Examples columns as scalar values. Clicking `−` returns to the grouped view. Active row selection syncs between tables on toggle, and search skips rows in the hidden table. The flat table wrapper supports horizontal scrolling when columns are wide.
- `Scenario.ExampleFlatValues` property preserving the original ReqNRoll ScenarioContext argument values before `BuildStructured()` transforms them into grouped columns.
- `ParameterizedGroup.FlatParameterNames` computed from `ExampleFlatValues` when all scenarios in a group have flat values.
- 10 new Playwright E2E tests (`FlattenToggleTests`) verifying toggle rendering, visibility, row sync, detail panel switching, search scoping, and horizontal scroll.

## [2.33.31] - 2025-07-12

### Fixed
- **BDDfy outline group header showing `]` for complex parameterized tests** — Complex `[Theory]` parameters with generic type notation (e.g. `List`1[TypeName]`) caused `ExtractBaseName` to fail when the parameter string was truncated at 200 characters, producing `]` as the outline group header instead of the humanized test method name. Fixed by using the raw test method name (`TestMethodName`) for OutlineId computation (matching the xUnit3 adapter approach), and replacing the truncation marker `...` with Unicode ellipsis `…` to prevent `FormatScenarioDisplayName` from treating truncation dots as namespace separators.

## [2.33.30] - 2025-07-12

### Fixed
- **Fixed enrichSearchData still crashing on large reports** — The v2.33.28 batched processing fix had a concurrency bug: `setTimeout(processBatch, 0)` fired the next batch immediately without waiting for the current batch's decompression Promises to resolve, so all 8,635 decompressions still ran concurrently. Rewrote to use `Promise.all()` to ensure each batch of 50 completes before starting the next. Also eliminated O(n²) string concatenation from repeated `setAttribute` calls by accumulating decompressed text in JS objects and flushing once per element at the end.

## [2.33.29] - 2026-05-10

### Fixed
- **Complex object parameters now truncated in step list and hnotes** — Step parameters with complex `ToString()` values (record types like `MuffinRecipeTestData { Name = Classic, Flour = Plain }`, generic collections like `List\`1[...]`) are now truncated to their type name (`[MuffinRecipeTestData]`, `[List<MyType>]`) instead of displaying the full `ToString()` representation. Affects LightBDD step text segments (now emit clickable ▴ toggle instead of inline value), LightBDD hnotes (step delimiters in sequence diagrams), BDDfy hnotes, and StepTracking (IL weaver) inline parameters.
- **Step table ▴ toggle buttons now work in combined-table layout** — When both setup and assertion tabular parameters existed (triggering the combined table layout), clicking the ▴ toggle button on a step did nothing. The `toggle_table_ref` JS function searched for `.step-param-table` inside `.step`, but in combined mode the table renders as `.step-param-combined-table` at the `.scenario` level. The function now falls back to searching the scenario container for the combined table.

### Added
- `ParameterParser.IsComplexObjectString()` — Detects whether a string value represents a complex object (record-style `ToString()` or generic collection type).
- `ParameterParser.ExtractTypeNameFromComplexString()` — Extracts a short type name from complex object strings (`MuffinRecipeTestData`, `List<String>`).
- 4 new Playwright E2E tests (`StepTableToggleTests`) verifying ▴ toggle button rendering, click-to-collapse, click-twice-to-restore, and table data content.

## [2.33.28] - 2025-07-10

### Fixed
- **Report freezing on large reports during search data loading** — The `enrichSearchData()` function decompressed all `data-plantuml-z` attributes in a tight synchronous loop, causing the browser to freeze on large reports (e.g. 102MB, 8,635 diagrams). Rewrote to use batched processing (50 elements per batch) with `setTimeout` yielding between batches to keep the UI responsive.

### Changed
- **Shortened assertion location PlantUML comment prefix** — Reduced the assertion source location comment prefix from `'__assertionLoc__:` to `'__^*__:` in PlantUML output, reducing report file size for projects with many tracked assertions.

## [2.33.27] - 2026-05-10

### Fixed
- **Parameterized group missing assertion/step toggle buttons** — Parameterized (grouped) test scenarios in reports were missing per-scenario "Assertions Shown/Hidden" and "Steps Shown/Hidden" toggle buttons in the diagram toolbar, even when the diagrams contained assertion notes or step delimiters. The `RenderParameterizedGroup` method was not receiving or rendering these toggles. Report-level toggles still worked.

## [2.33.26] - 2025-07-06

### Added
- **Expanded cross-SDK IL weaver tests** — Added 4 new high-risk codegen patterns (ternary expressions, try/catch/finally, switch expressions, await using) and .NET 8 SDK tests. Cross-SDK test count increased from 18 to 56 tests covering 7 patterns × 4 SDKs (8, 9, 10, 11) × 2 configurations (Debug, Release).
- **Multi-target assertion tracking tests** — Test project now targets net8.0, net9.0, and net10.0 to run IL weaver output through each CLR version's verifier. Uses TFM-conditional Microsoft.Build.Utilities.Core versions (17.11.48/17.14.28/18.4.0) for runtime compatibility.
- **.NET 8 SDK installed in CI** — CI workflow now installs .NET 8.0.x alongside 9.0.x and 10.0.x for cross-SDK test coverage.

## [2.33.25] - 2026-05-10

### Changed
- **Step parameter tables always visible** — Step parameter tables (tabular, tree, inline) now render inside the `<summary>` element rather than after `</summary>`, so they remain visible when a step is collapsed. Only sub-steps are now collapsible. Steps with parameters but no sub-steps render as plain `<div>` elements instead of `<details>/<summary>`.

## [2.33.24] - 2026-07-06

### Added
- **Cross-SDK IL weaver tests** — 18 new tests that compile fixtures with .NET 9, 10, and 11 preview SDKs (both Debug and Release), then weave and execute them to verify the IL weaver produces valid IL across different Roslyn codegen patterns (degenerate async state machines, real async with awaits, null-conditional branches). Uses isolated `AssemblyLoadContext` for each fixture and `TestAssemblyBuilder.BuildWithSdk()` which shells out to `dotnet build` with pinned SDK versions via temporary `global.json` files.
- **Assertion Tracking tests added to CI** — The `tests/Kronikol.Tests.AssertionTracking` project now has its own CI job. .NET 9 SDK also installed in CI for cross-SDK test coverage.

## [2.33.23] - 2026-05-10

### Added
- **Step delimiters in BDDfy sequence diagrams** — BDDfy tests now emit black `hnote` step delimiters (`Step: Given/When/Then ...`) into sequence diagrams at each step boundary, matching the behavior already available in Reqnroll and StepTracking (IL weaver) integrations. The `BDDfyStepTrackingExecutor` now brackets each step with `StepCollector.StartStep/CompleteStep`, enabling both step delimiters and assertion sub-step attachment during BDDfy step execution.
- **Step delimiters in LightBDD sequence diagrams** — LightBDD tests now emit step delimiters via a new `StepTrackingStepDecorator` (`IStepDecorator`) registered automatically by `CreateStandardReportsWithDiagrams()`. Each LightBDD step is bracketed with `StepCollector.StartStep/CompleteStep`, enabling step delimiters and assertion sub-step attachment.

## [2.33.22] - 2026-05-10

### Fixed
- **ReqNRoll assertion sub-steps not appearing in reports** — Assertions (`.Should()` calls) inside ReqNRoll step definitions were not surfacing as sub-steps in the test run report. The ReqNRoll hooks now bracket each step with `StepCollector.StartStep/CompleteStep` so that `Track.LogAssertion` can attach assertion sub-steps during execution, and `MapSteps` merges them into the report output.

## [2.33.21] - 2026-05-10

### Fixed
- **Report-level toolbar right-edge alignment** — The report-level toggle buttons (Assertions Shown/Hidden, Headers Shown/Hidden, Steps Shown/Hidden) had a `margin-right: 2em` that pushed them away from the right edge. Removed the margin so the toolbar right edge aligns with the filtering-box edge.

## [2.33.20] - 2026-05-10

### Fixed
- **Redundant "=" row indicator column in input-only step tables** — Tabular step parameters (ReqNRoll Gherkin tables, xUnit `TabularInputs`, etc.) rendered with a first column showing "=" on every row. This row-type indicator is only meaningful for verification tables that contain surplus (`+`) or missing (`-`) rows. The indicator column is now hidden when all rows are `Matching`, keeping it visible only for tables with mixed row types.

## [2.33.19] - 2026-05-10

### Fixed
- **Duplicate ✓/✗ symbols on assertion sub-steps in reports** — Assertion sub-steps displayed the pass/fail symbol twice: once from `StepCollector.AddAssertionSubStep` prepending it to the text, and again from the report renderer's own `step-status` icon span. Removed the symbol prefix from `AddAssertionSubStep` so the text is just the expression; the renderer's status icon is the single source of truth.
- **Null-conditional `?.` operator not stripped from assertion expressions** — `AssertionExpressionFormatter` already stripped `!` (null-forgiving) but not `?.` (null-conditional), causing formatted subjects like "Foo? bar" instead of "Foo bar". Added `?.` → `.` replacement alongside the existing `!` removal.

## [2.33.18] - 2026-05-10

### Fixed
- **Search bar placeholder text visible behind loading overlay** — When reports with interactive diagrams show the "Loading search data\u2026" overlay, the search input placeholder text ("Search... (@tag, $status, &&, ||, !!, parentheses)") was visible underneath. Added `#searchbar:disabled::placeholder { color: transparent; }` CSS rule to hide placeholder text while the input is disabled during loading.

## [2.33.17] - 2026-05-10

### Fixed
- **Null, empty, and whitespace-only values indistinguishable in input parameter tables** — In parameterized test scenario tables, null values rendered as blank cells instead of `<pre>null</pre>`, and whitespace-only values (e.g., `" "`) collapsed to invisible empty text. Two bugs fixed: (1) `ParameterParser.ExtractStructuredParameters` and `ExtractStructuredParametersWithRaw` coalesced null to `""` instead of `"null"`, losing null identity; (2) `RenderParameterizedGroup` used raw `HtmlEncode()` instead of `FormatDisplayValue()`, bypassing the null/whitespace styling. `FormatDisplayValue` now also wraps whitespace-only strings in `<pre>` elements for visibility.

## [2.33.16] - 2026-05-10

### Fixed
- **Scenario name truncated at nested brackets** — Theory scenario names containing parameters with nested brackets (e.g., `[effectiveRates: [AccountEffectiveRate { ... }]]`) were truncated at the first `[`, losing the parameter suffix. `ExtractBaseName` and `Parse` now find the matching `]` for the trailing bracket instead of the first `[`.
- **Record ToString with null properties rendered as empty** — Record types with null properties (e.g., `FeeCategory = , SubledgerType = }`) displayed empty values instead of `null`. `TryParseRecordToString` now emits `"null"` for empty record property values.
- **Single-item collections rendered as flat text instead of sub-table** — When a collection parameter contained exactly one complex item, it was rendered as a flat `ToString()` string instead of a horizontal property table. `SplitParams` now correctly tracks bracket nesting depth to avoid splitting on commas inside `{ }` braces.
- **Null values in sub-tables rendered as empty cells** — `FlattenToStringValues` and `RenderSubTable` now render null/empty values as `null` instead of blank cells.

## [2.33.15] - 2026-05-09

### Fixed
- **Context menu missing "Open current PlantUML" option after initial render** — When notes were collapsed or truncated on initial render (via `_preProcessSource`), the `data-plantuml` attribute was not updated to reflect the current rendered source. The context menu compared `_noteOriginalSource` with `data-plantuml` and found them equal, so it showed a single "Open PlantUML source in new tab" link instead of the submenu with both "Open full" and "Open current" options. Fixed by syncing `data-plantuml` after pre-processing in all three initial render paths (IntersectionObserver, `_renderDiagramsInContainer`, and first-scenario preload).

## [2.33.14] - 2026-05-09

### Fixed
- **Assertion tooltips not appearing on `hnote across` shapes** — `findAssertionNoteGroups()` delegated to `findNoteGroups()` which filters by fold triangle, excluding hexagonal `hnote` shapes. Rewrote to scan SVG independently for path/polygon+text groups matching ✓/✗ text. Also fixed polygon detection — PlantUML renders `hnote across` as `<polygon>` not `<path>`.
- **PlantUML source opens with garbled Unicode in new tab** — Changed Blob MIME type from `text/plain` to `text/plain;charset=utf-8` so ✓/✗ characters display correctly when opening PlantUML source or note text in a new browser tab.
- **Assertion toggle E2E tests checking wrong CSS state** — Fixed two pre-existing test failures where `Scenario_show_button_gets_active_class` and `Report_show_syncs_all_scenario_buttons` expected `details-active` on separate Show/Hide buttons, but the assertion toggle is a single button that changes `data-shown`.

### Changed
- **`var` assignment prefix stripped from assertion expressions** — Expressions like `var foo = bar.Should().BeTrue()` now produce the same report output as `bar.Should().BeTrue()` ("Bar should be true").
- **Assertion tooltip format** — Changed from `Filename.cs L42` to `Filename.cs L:42`.

## [2.33.13] - 2026-05-09

### Changed
- **Assertion variable value truncation increased from 50 to 100 characters** — The `FormatValue` method now truncates string and `ToString()` representations at 100 characters (was 50), allowing longer values to be displayed in assertion notes without ellipsis.

## [2.33.12] - 2026-05-09

### Fixed
- **AssertionTracking: InvalidProgramException when assertion follows a `lock` statement (issue #53)** — A `lock` block compiles to a try/finally with `Monitor.Enter`/`Exit`. The finally handler's `HandlerEnd` metadata points to the first instruction after the lock — which is the assertion's first instruction. When the weaver inserted its `tryStart` nop before that instruction, the nop fell inside the lock's finally handler region `[HandlerStart, HandlerEnd)`, causing our try/catch to start inside the finally — an illegal overlap rejected by the CLR verifier. The fix retargets `HandlerEnd` of any existing handler that references the assertion's first instruction, so the handler ends before our try block begins.

## [2.33.11] - 2026-06-05

### Fixed
- **AssertionTracking: InvalidProgramException with ternary/conditional in assertion arguments (issue #53)** — `ComputeExitStackDepth` incorrectly counted `dup` instructions from array initialization patterns (`newarr; dup; stelem.ref` for `params object[]` arguments) as Release-mode subject-sharing dups. This caused the weaver to emit spurious exit-spill `stloc` instructions that popped from an empty evaluation stack, producing `InvalidProgramException: Common Language Runtime detected an invalid program`. The fix restricts dup counting to instructions before the assertion entry point call (`.Should()` / `Assert.That()`), since subject-sharing dups always precede the entry call.

## [2.33.10] - 2026-05-09

### Fixed
- **AssertionTracking: NullReferenceException in generic methods with value type arguments (issue #53)** — Generic type parameters (`T`) have `IsValueType = false` at metadata level (unless constrained with `struct`), but at runtime can be instantiated with value types (`bool`, `int`, etc.). The IL weaver now emits `box T` for all generic parameter types when storing captured variable values in `object[]` arrays. `box` on a reference type is identity (no-op), so this is safe for both reference and value type instantiations.
- **AssertionTracking: InvalidProgramException with out/ref parameters (issue #53)** — Methods containing `out` or `ref` parameters could produce invalid IL when those parameters appeared in assertion argument expressions. `ldarg` on a by-reference parameter loads a managed pointer (`T&`), not a value — storing this in `object[]` via `stelem.ref` is invalid IL. The weaver now skips capturing `out`/`ref` parameters entirely (their values are unreliable for display anyway since the method may not have assigned them yet at the assertion point).

## [2.33.9] - 2026-05-09

### Fixed
- **Assertion tracking: instance field values now resolved in async methods** — When instance fields (e.g., `_secondConfirmationId`) were passed as direct arguments to assertion methods in async methods, the report displayed the field name instead of its runtime value. The AssertionWeaver now detects chained field access through `<>4__this` in both Debug (`ldarg.0 → ldfld <>4__this → ldfld _field`) and Release (`ldloc.N → ldfld _field` with cached outer this) IL patterns.

## [2.33.8] - 2026-05-09

### Changed
- **Sub-steps collapsed by default in reports** — Steps with sub-steps now render collapsed by default, reducing visual noise and letting users expand on demand. Steps with failed sub-steps auto-expand on page load to surface failures immediately. Inline tabular parameter tables (without sub-steps) remain expanded.

## [2.33.7] - 2026-05-09

### Changed
- **Toggle button labels changed from imperative to state-based** — Report toolbar toggle buttons now use state-based labels ("Headers Shown" / "Headers Hidden", "Assertions Shown" / "Assertions Hidden", "Steps Shown" / "Steps Hidden") instead of the previous imperative labels ("Hide Headers" / "Show Headers", etc.).

## [2.33.6] - 2026-05-09

### Changed
- **Assertion tracking: natural English for `.First()` / `.Last()` in subjects** — Assertion subjects containing `.First()` or `.Last()` LINQ calls are now formatted as "First value of my collection" and "Last value of my collection" instead of the raw "My collection first()" output.

## [2.33.5] - 2026-05-09

### Changed
- **Null parameter values rendered as `<pre>null</pre>` in reports** — Null input values in parameter tables, inline parameters, tree nodes, combined tables, and step text segments are now rendered as monospace-styled `null` (using `<pre>` with italic grey styling) to visually distinguish them from empty strings. Previously, null values appeared as plain text "null", indistinguishable from a legitimate string value.

## [2.33.4] - 2026-05-09

### Fixed
- **Assertion tracking: resolve method parameters** — Method parameters (e.g. `key` in `dict.Should().ContainKey(key)`) were not resolved to their runtime values in assertion output, showing the literal name instead of the value. Added `ldarg` detection to the IL weaver so parameters are now captured and displayed as `'myKeyValue'`.
- **Assertion tracking: strip `await` prefix** — Assertion expressions starting with `await` (e.g. `await response.StatusCode.Should().Be(...)`) rendered as "Await response status code should be..." instead of "Response status code should be...". The `await` keyword is now stripped before formatting.

## [2.33.3] - 2026-07-06

### Fixed
- **E2E test render-race condition with Playwright 1.59.0 / Chromium 147** — After toggling headers or changing note states, wait conditions in E2E tests detected stale SVG icons from the previous render while PlantUML re-rendering was still in progress. This caused `setNoteState` calls to silently no-op due to the `_plantumlRendering` guard. Fixed by adding `!window._plantumlRendering && !container._noteRendering` checks to `WaitForNoteElements`, `WaitForReRender`, `WaitForSvgReRender`, and `HideHeaders` wait conditions.
- **HeadersDetailsInterferenceTests selector bug** — After the single-button toggle migration, two tests checked for `[data-shown='true']` with `details-active` class after clicking a toggle that changes `data-shown` to `'false'`. Fixed assertions to check the actual post-toggle attribute value.

## [2.33.2] - 2026-05-10

### Fixed
- **NullReferenceException in `Track.ResolveVariableValues` with awaited assertions** — When an awaited assertion (e.g. TUnit `await value.Should().BeEqualTo(x)`) with captured variables failed, the IL weaver's `WrapAwaitedAssertion` method placed the `tryStart` nop *after* the captured-variable array construction. Branches retargeted from the merge point to `tryStart` (the sync-completion `brtrue` path) skipped array initialization, leaving `namesLocal`/`valuesLocal` as null. The catch handler then passed null arrays to `AssertionFailedWithValues` → `ResolveVariableValues`, which crashed on `varNames.Length`. Fixed by inserting `tryStart` *before* array construction so both sync and async paths initialize the arrays. Also added a defensive null guard in `ResolveVariableValues`. Fixes [#52](https://github.com/lemonlion/Kronikol/issues/52).

## [2.33.1] - 2026-05-09

### Fixed
- **AssertionTracking `InvalidProgramException` with record `with` expressions** — C# record init-only property setters return `System.Void modreq(IsExternalInit)`, but the weaver's `GetInstructionPushCount` compared `ReturnType.FullName` directly against `"System.Void"`, missing the `modreq` wrapper. This caused each init setter to be counted as pushing a value, inflating exit stack depth and generating spurious exit-spill `stloc` instructions that corrupted IL. The fix strips `modreq`/`modopt` wrappers before checking for void. Resolves the remaining 5 failures from [#47](https://github.com/lemonlion/Kronikol/issues/47).

## [2.33.0] - 2026-05-09

### Added
- **TUnit `[HeadIn]` attribute** — `Kronikol.TUnit` now includes a `[HeadIn]` attribute implementing `IDataSourceAttribute` for tabular data-driven tests in TUnit.
- **Auto-verify `TabularOutputs<T>` on disposal** — `TabularOutputs<T>` now implements `IDisposable`. Disposing auto-calls `Verify()` if actuals were recorded and `Verify()` was not already called. On xUnit v3 (`DisposalTracker`) and TUnit (`TestBuilderContext.Events.OnDispose`), this happens automatically — no explicit `Verify()` needed.

### Changed
- **Renamed `AddActual()` → `RecordActualResult()`** on `TabularOutputs<T>` for clarity.

## [2.32.4] - 2026-05-09

### Fixed
- **Parameter table preview shows array properties instead of items** — Arrays and collections used as step parameters displayed their .NET properties (`{ Length: 3, LongLength: 3, Rank: 1, ... }`) in the summary preview instead of their contents. The `GeneratePreview()` method now handles `IEnumerable` types: scalar arrays render inline as `["A", "B", "C"]` (truncated at 10 items), complex-item collections show `N items`, and empty collections show `[]`.
- **StepTracking package missing from NuGet releases** — The `Kronikol.StepTracking` package was not included in `release.slnf`, so it was never packed or pushed to NuGet during releases. Added it to the release solution filter.

## [2.32.3] - 2026-05-09

### Fixed
- **StepTracking `InvalidProgramException` on void methods with branches** — The StepWeaver used `ILProcessor.Replace()` to substitute `ret` instructions with `leave`, which created new instruction objects and left branch targets (e.g. `brfalse`) pointing to detached instructions. This caused `InvalidProgramException` at runtime for any void step method containing control flow (if/else, loops, etc.). The fix modifies `ret` instructions in-place (`OpCode`/`Operand` assignment) to preserve all existing branch references.

## [2.32.2] - 2026-05-09

### Fixed
- **AssertionTracking `InvalidProgramException` on assertions with awaited arguments (issue #47)** — Assertions like `value.Should().Be(expected, await someTask)` where `await` appears in assertion arguments (not on the assertion result) caused the weaver to misidentify them as awaited assertions and apply `WrapAwaitedAssertion`, producing invalid IL. The weaver now distinguishes between true assertion-awaits (`await x.Should().ThrowAsync<T>()`) and argument-awaits by checking whether `GetAwaiter()` is called on an assertion-library type. Assertions with argument-awaits are skipped (still execute, just not tracked) to avoid invalid IL.

## [2.32.1] - 2026-05-08

### Fixed
- **StepTracking `InvalidProgramException` on async methods with `[GivenStep]`/`[WhenStep]`/`[ThenStep]` (issue #50)** — The StepWeaver IL weaver had no async method handling. It wrapped the entire method body (including the async kick-off stub) in a try/catch and replaced `ret` with `leave`, producing invalid IL for async state machine methods. The weaver now detects async methods (returning `Task` or `Task<T>`) and uses a different strategy: `StartStep` is called at method entry, then the returned `Task` is passed through `StepCollector.CompleteStepAsync()` which awaits it and calls `CompleteStep` on success or failure, preserving the original exception.

### Added
- `StepCollector.CompleteStepAsync(Task)` and `StepCollector.CompleteStepAsync<T>(Task<T>)` — runtime helpers that wrap an async step's returned Task so that `CompleteStep` is called on completion or failure.

## [2.32.0] - 2026-08-07

### Added
- **Tabular Attributes** — Data-driven tests with typed input/output rows and built-in verification, ported from [LightBDD.TabularAttributes](https://github.com/AdaskoTheBeAsT/LightBDD.TabularAttributes).
  - **Core types** (`Kronikol` package):
    - `[Inputs(...)]`, `[Outputs(...)]`, `[HeadOut(...)]` — plain attributes for declaring data rows and output column names.
    - `TabularInputs<T>` — `IReadOnlyList<T>` with per-row diagram delimiters emitted during `foreach` iteration.
    - `TabularOutputs<T>` — `IReadOnlyList<T>` with `RecordActualResult()` + `Verify()` for position-based output comparison.
    - `TabularDeserializer` — column-to-property mapping with space removal, `&` → `And` substitution, and case-insensitive matching.
    - `TabularResolver` — reads tabular attributes from `MethodInfo` and constructs typed parameter values.
    - `ITabularParameterData` — interface enabling `StepCollector` to automatically render tabular step parameters in reports.
  - **Framework-specific `[HeadIn]` attributes** (acts as the data source trigger):
    - `Kronikol.xUnit3` — extends `Xunit.v3.DataAttribute`
    - `Kronikol.xUnit2` — extends `Xunit.Sdk.DataAttribute`
    - `Kronikol.NUnit4` — implements `NUnit.Framework.Interfaces.ITestBuilder`
    - `Kronikol.MSTest` — implements `Microsoft.VisualStudio.TestTools.UnitTesting.ITestDataSource`
  - **`StepCollector.BuildParameters()` enhancement** — automatically detects `ITabularParameterData` values and produces `Tabular` step parameters instead of `Inline`.

## [2.31.6] - 2026-05-08

### Fixed
- **`TrackingDiagramOverride` now falls back to `TestIdentityScope` when `TestContext` is unavailable (issue #49)** — Previously, `GetTestId()` in both `Kronikol.xUnit3` and `Kronikol.BDDfy.xUnit3` silently returned `null` when `TestContext.Current` threw or `.Test` was null (non-test threads), causing diagram overrides (`StartOverride`, `EndOverride`, `InsertPlantUml`, `InsertTestDelimiter`, `StartAction`, `StartSetup`) to be silently discarded. Now falls through to `TestIdentityScope.Current` (AsyncLocal) then `TestIdentityScope.GlobalFallback` — the same resolution chain used by `TestInfoResolver.Resolve()`. Diagram overrides called from hosted services, background threads, and change-feed processors now correctly associate with the running test.

## [2.31.5] - 2026-08-06

### Fixed
- **Null-conditional `?.Should()` assertions now fully tracked in Release mode (issue #47)** — The previous fix conservatively skipped instrumentation when the compiler's release-mode multi-dup pattern (`dup;dup;brtrue`) left values on the stack for subsequent assertions. The weaver now implements proper exit-spill logic: exit values are saved to locals before `leave`, a null-path exit block saves them on the short-circuit path, and they are reloaded after the catch handler for the next assertion to consume.

## [2.31.4] - 2026-08-06

### Fixed
- **`[SuppressAssertionTracking]` not working on async methods (issue #48)** — The attribute on an async method did not propagate to the compiler-generated state machine's `MoveNext()` method. The weaver now detects state machine types and checks the parent (kick-off) method for the suppress attribute.
- **InvalidProgramException with null-conditional `?.Should()` in async methods (issues #47, #48)** — The IL weaver produced invalid IL for null-conditional assertions in async state machines due to:
  - Internal `br` instructions targeting a trimmed trailing `leave` were not retargeted to the try-exit instruction, causing illegal cross-boundary branches.
  - Release-mode multi-dup patterns (multiple assertions sharing a subject via `dup;dup;brtrue`) left values on the stack that were cleared by `leave`. Assertions with exit stack depth > 0 are now skipped (not instrumented) to avoid invalid IL.
  - Branch-into-try retargeting now correctly points to the entry spill `stloc` instructions (before `tryStart`) so that branches arriving with values on the stack have them properly stored before try entry.
  - `SpillStackIfNeeded` now correctly forces `depth = 1` when `firstInstr` is `dup` and the linear stack walk returns ≤ 0.
- **"Test context not available on this thread" exceptions (issue #49)** — `TrackingDiagramOverride.GetTestId()` in both `Kronikol.xUnit3` and `Kronikol.BDDfy.xUnit3` now catches exceptions from `TestContext.Current` and gracefully returns when called on non-test threads.

## [2.31.3] - 2026-08-05

### Added
- **`[ButWhenStep]` attribute** — A new step attribute that displays "But" as its keyword but transitions to the Action phase (like `[WhenStep]`). Use for negative continuations in the action phase, e.g. "But the retry fails". Sequences with `[ButStep]` for And-sequencing.

### Fixed
- **Generated step attributes have missing semicolons (issue #46)** — `WriteLinesToFile` in the MSBuild .targets file treated literal `;` characters in `{ get; set; }` as item list separators, producing broken auto-property syntax (`{ get\nset\n}`). All semicolons in `Lines` attribute values are now MSBuild-escaped as `%3B`.
- **CI: Example.Api package downgrade errors** — Bumped NUnit from 4.5.1 to 4.6.0 and LightBDD.XUnit2 from 3.11.2 to 3.12.0 in Example.Api test projects to match transitive dependency requirements.

## [2.31.2] - 2026-08-05

### Fixed
- **CI: Example.Api package downgrade error** — Updated `Microsoft.AspNetCore.Mvc.Testing` in all Example.Api test projects from 10.0.5 to 10.0.7, matching the core library dependency upgraded in v2.31.0. This resolved NU1605 "detected package downgrade" errors that caused Example.Api, Integration, and BDDfy CI jobs to fail.

## [2.31.1] - 2026-08-05

### Fixed
- **Assertion tracking: instance field values now resolved in lambda predicates** — When using `.Contain(l => l.Field == _instanceField)` or similar predicate-based assertions, instance fields (e.g., `_orderId`) are now captured and resolved to their runtime values in assertion notes. Previously, the raw field name was shown instead of the actual value.
- **Expression-bodied async methods** — The `=> ` prefix from expression-bodied method syntax is now stripped from assertion expression text, producing cleaner subject names.

### Added
- New `ldtoken` handler in `DetectCapturedVariables` — captures instance fields referenced via `Expression.Field()` in expression tree construction (used by `Expression<Func<T, bool>>` predicate parameters).
- Extended `ldarg.0 + ldfld` handler to also capture regular instance fields (not just state machine fields with `<name>5__N` naming pattern).
- Extended `ldftn` lambda handler to capture instance fields accessed inside delegate lambda bodies.

## [2.31.0] - 2026-08-05

### Changed
- **Assertion tracking: `TrackAssertionsBeta` renamed to `TrackAssertions`** — The IL weaver attribute `[assembly: TrackAssertionsBeta]` has been renamed to `[assembly: TrackAssertions]`. The old attribute name is still recognized for backward compatibility.
- **MSBuild property renamed**: `TrackAssertionsBetaEnabled` → `TrackAssertionsEnabled`. The old property name is still supported for backward compatibility.
- **IL weaver is now the sole assertion tracking approach** — The `Kronikol.AssertionRewriter` (Roslyn source rewriter) package has been removed from the solution. The IL weaver (`Kronikol.AssertionTracking`) is now the only automated assertion tracking package.
- **All project templates** updated from `AssertionRewriter` to `AssertionTracking` package reference.

### Added
- **Pragma comment support in IL weaver** — The IL weaver now supports `// pragma:TrackAssertions:disable` and `// pragma:TrackAssertions:enable` comments for fine-grained control over which assertions are instrumented. Works as both inline (single-statement) and block (range) suppression.
- **Backward compatibility test** for old `TrackAssertionsBetaAttribute`.

### Removed
- **`Kronikol.AssertionRewriter` package** — The Roslyn source rewriter has been removed. Use `Kronikol.AssertionTracking` (IL weaver) instead. Migration: replace `[assembly: TrackAssertions]` package reference from `AssertionRewriter` to `AssertionTracking` — the attribute name remains the same.
- **`Kronikol.Tests.AssertionRewriter` test project** — Removed along with the source rewriter.

## [2.30.36] - 2026-08-05

### Changed
- **NuGet package upgrades** — Updated all dependencies to latest patch/minor versions within their current major:
  - Microsoft.AspNetCore.Mvc.Testing: 8.0.25→8.0.26, 9.0.14→9.0.15, 10.0.5→10.0.7
  - Microsoft.Build.Utilities.Core: 17.0.0→17.14.28
  - Microsoft.CodeAnalysis.CSharp: 4.12.0→4.14.0
  - LightBDD.Framework: 3.11.2→3.12.0
  - LightBDD.XUnit2: 3.11.2→3.12.0
  - Microsoft.Testing.Platform.MSBuild: 2.0.1→2.2.2
  - MSTest.TestFramework: 3.9.2→3.11.1
  - NUnit: 4.5.1→4.6.0
  - IKVM / IKVM.Image / IKVM.Image.JDK / IKVM.Image.JRE / IKVM.MSBuild: 8.9.1→8.15.0
  - Microsoft.NET.Test.Sdk: 18.3.0→18.5.1
  - FluentAssertions: 8.3.0→8.9.0
  - Microsoft.AspNetCore.Http: 2.2.2→2.3.9
  - Jint: 4.2.1→4.8.0
  - Microsoft.Playwright: 1.52.0→1.59.0

## [2.30.35] - 2026-05-08

### Added
- **Step delimiters in sequence diagrams** — When BDD steps execute, a black `hnote` delimiter (`Step: Given/When/Then ...`) is injected into sequence diagrams at each top-level step boundary. Controlled by `StepTrackingOptions.ShowStepDelimiters` (default: `true`). Uses `<<stepDelimiter>>` PlantUML stereotype for client-side filtering.
- **Step delimiter toggle** — Report-level and scenario-level "Hide Steps" / "Show Steps" toggle button appears when step delimiters are present in diagrams.

### Changed
- **Single-button toggles** — Headers and Assertions toggles converted from two-button radio groups (`Show`/`Hide`) to single toggle buttons that flip between "Hide Headers" / "Show Headers" and "Show Assertions" / "Hide Assertions". Reduces toolbar clutter.

## [2.30.34] - 2026-05-08

### Changed
- **Step parameter tables open by default** — Tabular parameter tables under steps now render expanded instead of collapsed. The toggle button (▴/▾) still allows collapsing them manually.

## [2.30.33] - 2026-05-08

### Added
- **StepTracking: Keyword deduplication** — When a step method name starts with the same keyword as its attribute (e.g. `[GivenStep] GivenTheyGo`), the keyword prefix is automatically stripped from the step text to avoid duplication in reports (`"They go"` not `"Given they go"`). Whole-word matching only — `WheneverTheyGo` is not affected.

## [2.30.32] - 2026-05-08

### Added
- **TUnit assertion tracking support** — The IL weaver now detects and instruments TUnit's `Assert.That()` and `.Should()` assertions (from `TUnit.Assertions` and `TUnit.Assertions.Should` namespaces) in addition to FluentAssertions and AwesomeAssertions.
- **Async/awaited assertion support** — The IL weaver now correctly handles awaited assertions (e.g. `await act.Should().ThrowAsync<T>()`, TUnit's `await Assert.That(x).IsEqualTo(5)`). Wraps `GetResult()` at the state machine merge point to catch assertion failures that manifest after await suspension/resume.
- **Roslyn rewriter TUnit detection** — The `AssertionWrappingRewriter` now also detects `Assert.That(...)` patterns for TUnit, in addition to the existing `.Should()` detection.

## [2.30.31] - 2026-05-08

### Fixed
- **Step tables now align with step text** — The left margin of inline step parameter tables (`step-param-table`) increased from `24px` to `2.3em` so they align with the start of the step text rather than the status indicator (tick/cross).

## [2.30.30] - 2026-05-08

### Changed
- **StepTracking: Removed `[AndStep]` attribute** — Redundant since keyword sequencing already auto-promotes consecutive same-keyword steps to "And". Use `[GivenStep]`, `[WhenStep]`, or `[ThenStep]` instead.
- **StepTracking: `[ButStep]` now behaves like `[GivenStep]`** — Sets `TestPhaseContext.Current = TestPhase.Setup` (same as Given), but displays "But" as the keyword. Consecutive `[ButStep]` methods sequence to "And".

### Added
- **Wiki: Step Tracking page** — Full documentation covering all step attributes, keyword sequencing, parameter capture, phase transitions, assertion sub-steps, and nested steps. Cross-referenced from Phase-Aware Tracking, Assertion Tracking, Internal Flow Tracking, and Generated Reports pages.

## [2.30.29] - 2026-07-05

### Added
- **New package: Kronikol.StepTracking** — IL weaver that instruments methods decorated with `[GivenStep]`, `[WhenStep]`, `[ThenStep]`, `[ButStep]`, or `[Step]` attributes with automatic BDD step tracking. Activated via `[assembly: TrackSteps]`.
  - Records step entry/exit timing and pass/fail status via `StepCollector`
  - Method names are humanized (PascalCase → "Pascal case", underscores → spaces)
  - Method parameters are captured as `StepParameter` values at runtime
  - Keyword sequencing: second `[GivenStep]` at same level becomes "And", etc.
  - Custom step text via `Description` property: `[GivenStep(Description = "A custom step")]`
  - Integrates with assertion tracking: assertions inside steps become sub-steps
  - `[WhenStep]` triggers `TestPhaseContext.Current = TestPhase.Action` (enables `SeparateSetup` diagrams)
  - `[GivenStep]` sets `TestPhaseContext.Current = TestPhase.Setup`
  - Nested step support: calling a step-attributed method from another creates sub-steps
  - Self-resolving overloads: weaved code resolves test ID from ambient `TestIdentityScope`/`TestIdResolver`
- **Framework adapter integration** — xUnit3, TUnit, NUnit4, and BDDfy adapters now populate `Scenario.Steps` from `StepCollector` when step attributes are used

## [2.30.28] - 2026-05-07

### Fixed
- **BDDfy: inline scenarios reported as Skipped instead of Passed** — When BDDfy tests use inline code (no `Given_`/`When_`/`Then_` step methods) and call `this.BDDfy()` at the end, BDDfy’s `Scenario.Result` returns `NotExecuted` because `Steps` is empty. This was mapped to `ExecutionResult.Skipped`, causing all inline scenarios to appear skipped in TTD reports despite passing. Now, when `Steps` is empty and `Result` is `NotExecuted`, the scenario is mapped to `Passed` — since reaching the Report processor means the test completed successfully. Fixes #45.

## [2.30.27] - 2026-05-07

### Added
- **AssertionTracking: lambda variable capture** — Variables referenced inside lambda arguments to assertion methods (e.g. `.Contain(l => l.EntityId == orderId)`) are now detected and resolved at runtime. The weaver scans `ldftn` targets to find state machine field accesses inside lambda method bodies. Previously, only variables loaded directly in the MoveNext method were captured.

### Fixed
- **AssertionTracking: multi-line expression truncation** — `ReadSourceText` now detects unbalanced parentheses in extracted source text and reads subsequent lines until balanced. This fixes assertions spanning multiple lines where the sequence point's EndLine doesn't cover the full statement.

### Changed
- **AssertionExpressionFormatter**: Lambda arguments are now wrapped with spaces inside brackets (`[ l => ... ]` instead of `[l => ...]`) for improved readability.

## [2.30.26] - 2026-05-07

### Fixed
- **AssertionTracking: InvalidProgramException with null-conditional (?.) in assertion arguments (Release mode)** — Three fixes:
  1. **Spill local type inference**: `GetPushedType` did not handle comparison operators (`cgt.un`, `ceq`, etc.) that push integers via `StackBehaviour.Pushi`. When the C# compiler in Release mode leaves a comparison result directly on the stack (no intermediate `stloc`/`ldloc`), the spill local was typed as `System.Object` (reference type) but received an `Int32` (value type), failing IL verification.
  2. **Exit stack depth with internal branches**: `ComputeExitStackDepth` returned `entryDepth` when it encountered an internal branch (ternary `?:` or null-conditional `?.`). For assertions with a spilled subject on the stack, `entryDepth` was 1, causing an incorrect exit spill `stloc` after the `pop` (which had already emptied the stack). Assertions are complete statements that consume their subject and discard results — exit depth is always 0 when branches are present.
  3. **Branch retargeting**: All branches originally targeting the assertion's first instruction are retargeted to `tryStart` to prevent illegal mid-try-block entry.

### Changed
- **AssertionExpressionFormatter**: Trailing underscores are now trimmed from subject names (matching existing leading-underscore trimming). Dotted member access paths in assertion arguments are simplified to show only the final member name (e.g. `_steps.Response.Items` → `'Items'`).

## [2.30.25] - 2026-05-07

### Fixed
- **AssertionTracking: InvalidProgramException in Release-compiled async methods with multiple assertions** — In Release mode, the C# compiler leaves values on the evaluation stack across sequence point boundaries (e.g. `GetResult()` return value feeds directly into `Should()` without an intermediate local, and `dup` provides copies for multiple assertions). The CLR requires the stack to be empty at try block entry points, and `leave` clears the stack. Added stack spill/restore logic: (1) computes stack depth at the assertion's first instruction and spills incoming values before the try block, reloading them inside; (2) computes exit stack depth after the assertion and spills outgoing values before the `leave`, reloading them after the catch handler. Also fixed generic type parameter resolution (e.g. `TaskAwaiter<int>.GetResult()` returning `!0`) when determining spill local types.

## [2.30.24] - 2026-05-07

### Fixed
- **AssertionTracking: InvalidProgramException in async methods with no await** — Expression-bodied `async Task` methods containing only a synchronous assertion and no `await` (e.g. `async Task Foo() => x.Should().Be(y)`) generated a degenerate state machine where the compiler's outer try/catch `TryStart` was the first instruction of the assertion. When the weaver inserted its inner try/catch `tryStart` nop before that instruction, it landed outside the outer handler's try region, creating an illegal overlapping exception handler that the CLR rejected with `InvalidProgramException`. Fixed by retargeting any existing handler whose `TryStart` references the assertion's first instruction to include the newly-inserted nop, maintaining proper nested handler structure. Added tests for both Debug and Release-compiled assemblies to verify correct IL generation under each optimization level.

## [2.30.23] - 2026-05-07

### Fixed
- **AssertionTracking: weaver infinite loop on async state machine fields** — `GetStateFieldOriginalName` could return an empty string for compiler-generated fields like `<>1__state`, `<>t__builder`, `<>u__1`, `<>s__1`, and `<>7__wrap1`. This empty name was then passed to `NameAppearsInExpression` which entered an infinite loop (`String.IndexOf("", idx)` always succeeds, and `idx += 0` never advances). Fixed by returning `null` for empty extracted names, and added a defensive early-return guard in `NameAppearsInExpression`. This was the root cause of the 12+ minute CI hang in BreakfastProvider.

## [2.30.22] - 2026-05-07

### Fixed
- **AssertionTracking: weaver hanging on CI due to `ReadingMode.Deferred`** — The v2.30.21 fix switched to `ReadingMode.Deferred` to avoid upfront resolution, but this caused thousands of lazy per-method-body reads and resolver calls, hanging on CI runners. Switched back to `ReadingMode.Immediate` (safe now that the assembly resolver is properly configured with `@(ReferencePath)` directories) — this performs one fast sequential read of all metadata. Added a fast-path exit when neither FluentAssertions nor AwesomeAssertions is referenced, skipping the entire weave process for assemblies that can't contain `.Should()` calls.

## [2.30.21] - 2026-05-07

### Fixed
- **AssertionTracking: weaver crashes with `Failed to resolve assembly` on CI** — Cecil's `DefaultAssemblyResolver` could not locate referenced assemblies (NuGet packages like TUnit.Core, nunit.framework, etc.) because no search directories were configured. The MSBuild target now passes `@(ReferencePath)` to the weaver task, which registers all reference assembly directories with Cecil's resolver. Also switched from `ReadingMode.Immediate` to `ReadingMode.Deferred` — the immediate mode eagerly parsed ALL IL bodies and resolved ALL custom attribute enum types during the initial assembly read (before any fast-path could skip methods), causing both crashes (unresolvable assemblies) and hangs (millions of instructions parsed upfront for assemblies with thousands of compiler-generated types).

## [2.30.20] - 2026-05-07

### Fixed
- **AssertionTracking: weaver hanging on Linux CI runners** — Three root causes addressed: (1) Replaced Cecil `ReadWrite=true` file mode with read-into-memory + write-to-new-file approach, eliminating exclusive file locks that could stall on overlay/virtual filesystems used by CI runners. (2) Added double-weave detection via a sentinel module attribute (`__AssertionTrackingWeaved__`) — if the assembly was already instrumented (e.g. MSBuild re-evaluation without recompilation), the weaver exits immediately instead of processing the already-weaved IL which would exponentially grow the instruction count. (3) Added `Inputs/Outputs` to the MSBuild target so it only runs when the intermediate assembly is newer than the weave marker file, preventing unnecessary re-execution on incremental builds. Also replaced the O(seqpoints × instructions) LINQ filtering with a single-pass pre-indexed boundary lookup, and added per-phase timing diagnostics (read/setup/weave/write milliseconds) logged at Low importance.

## [2.30.19] - 2026-05-07

### Fixed
- **AssertionTracking: catastrophic O(n²) weaver performance on large assemblies** — The IL weaver now performs a single O(n) scan of each method's instructions for `.Should()` calls before doing any detailed per-sequence-point analysis. Previously, every method (including thousands of compiler-generated async state machines, closures, and framework code) had its instructions materialized into a List and filtered per sequence point — causing O(methods × sequence_points × instructions) time complexity. On large Debug-mode test assemblies (LightBDD, TUnit, xUnit3, etc.) this caused build times to exceed 12+ minutes on CI runners. The fast-path exits in microseconds for methods without assertions (99%+ of all methods). Also added a source file cache to avoid redundant disk reads when multiple assertions reference the same file, and timing diagnostics that log elapsed milliseconds after weaving completes.

## [2.30.18] - 2026-05-07

### Changed
- **Parameterized scenario layout: steps moved below input parameters table** — In parameterized scenario detail panels (tests using `[MemberData]`, `[MethodDataSource]`, Scenario Outlines, etc.), the input parameters table now renders above the steps and failure diagnostics. Previously (since v2.29.15-beta), steps were rendered above the table. The new ordering prioritises the varying inputs — which are the primary differentiator between parameterized rows — with the shared step execution details below.

## [2.30.17] - 2026-05-07

### Changed
- **Assertion value resolution: smart collection formatting** — Small collections (≤10 items) containing only scalar values (primitives, enums, strings, Guid, DateTime, etc.) are now displayed inline with their actual values (e.g. `[ 1, 2, 3 ]`, `[ "Milk", "Sugar", "Brandy" ]`, `[ Monday, Friday ]`) instead of the generic `[N items]` count. Strings are quoted, nulls display as `null`. Collections with >10 items or containing complex objects still show the count format. Empty collections remain `[0 items]`.

## [2.30.16] - 2026-05-07

### Fixed
- **BDDfy.xUnit3: `CurrentTestInfo.Fetcher` was permanently null** — The static initializer `xUnit3.CurrentTestInfo.Fetcher` resolved to the same class (self-reference) because namespace `Kronikol.BDDfy` contains child namespace `xUnit3`, causing C# name resolution to bind `xUnit3.CurrentTestInfo` to `Kronikol.BDDfy.xUnit3.CurrentTestInfo` instead of the intended `Kronikol.xUnit3.CurrentTestInfo`. The self-referencing read returned null during static construction, permanently disabling HTTP tracking in all BDDfy + xUnit3 projects. Fixed by fully qualifying the reference. Fixes #44.

## [2.30.15] - 2026-05-07

### Added
- **ReqNRoll inline parameter highlighting**: Step text in HTML reports now renders ReqNRoll step parameters inline within the prose, highlighted as distinct values. Uses `BindingMatch.Arguments[].StartOffset` from the ReqNRoll runtime to precisely identify parameter positions — works with both regex and cucumber expression bindings.
- **Clickable tabular param references (LightBDD)**: When a LightBDD step has a bracket-appended tabular/tree parameter (e.g. `[items: "<$items>"]`), the report now renders a clickable toggle button instead of silently stripping the reference. Clicking the button expands/collapses the associated parameter table inline below the step.

## [2.30.14] - 2026-05-07

### Fixed
- **AssertionTracking: version mismatch detection** — The IL weaver now checks the version of the referenced `Kronikol` core library before weaving. If the core library is older than v2.30.7 (when `Track.AssertionPassed` was introduced), the weaver emits a clear build error instead of producing weaved IL that would crash at runtime with `MissingMethodException`. Fixes #43.
- **AssertionTracking: exclude `ret` from try blocks** — The weaver now excludes trailing `ret` instructions from assertion statement boundaries (alongside the existing `leave`/`leave.s` exclusion). A `ret` inside a try block is invalid IL that causes `InvalidProgramException` at runtime. This could occur when the assertion is the last statement in a sync method.

## [2.30.13] - 2026-05-07

### Added
- **AssertionTracking: lambda closure variable resolution** — The IL weaver now resolves captured variables inside lambda predicates (e.g. `list.Should().Contain(x => x.Id == expectedId)` shows `'abc'` instead of `expectedId`). Supports single and multiple captured variables, `||`/`&&` conditions, and async method closures. The display class field loading works for both regular methods (local display class) and async state machines (state field → display class field).

### Fixed
- **AssertionExpressionFormatter: lambda args now get value substitution** — Previously, `FormatArgs()` returned early for lambda expressions (wrapping in `[]` brackets) before calling `SubstituteResolvedValues`, so resolved variable values were never substituted into lambda argument text. The substitution now runs before the lambda bracket wrapping.

## [2.30.12] - 2026-05-07

### Added
- **AssertionTracking: runtime variable value resolution** — The IL weaver now captures local variable values at assertion time and resolves them into the diagram note text. Previously, assertion notes showed raw variable names (e.g. `expected`); now they show the actual runtime values (e.g. `'42'`). Supports regular local variables, async state machine fields, dotted property chain walking (up to 3 levels deep), null display, string truncation (50 chars), and collection count display. When no capturable variables are detected (all constant arguments), the zero-overhead path is used.

## [2.30.11] - 2026-05-06

### Fixed
- **Parameter preview: remove type name from nested object summaries** — The `GenerateNestedPreview` and `FormatPreviewValue` methods no longer prepend the type name (e.g. `IngredientSet { ... }`) to nested complex object previews in expandable parameter cells. Nested objects now display as `{ Prop = Val, ... }` matching the top-level preview format. Also strips type name prefix from C# record `ToString()` output used in nested previews.

## [2.30.10] - 2026-05-06

### Fixed
- **Assertion tooltips: theme-independent SVG detection** — The `findAssertionNoteGroups()` function now identifies assertion notes by reusing the existing `findNoteGroups()` shape detection and filtering to groups whose first text starts with ✓ or ✗, instead of matching hardcoded fill colors (`#d4edda`/`#f8d7da`). This makes assertion source-location tooltips resilient to PlantUML theme changes.

## [2.30.9] - 2026-05-06

### Added
- **AssertionTracking: async method support** — The IL weaver now instruments assertions inside `async` methods. Previously, async state machine `MoveNext` methods were skipped entirely. The weaver now correctly handles the compiler-generated try/catch structure by inserting exception handlers in proper nesting order (before containing handlers) and excluding trailing `leave` instructions from assertion statement boundaries.

### Fixed
- **AssertionTracking: outbound branch detection excludes `leave` instructions** — The null-propagation branch retargeting now only retargets conditional/unconditional branches (`brfalse`, `brtrue`, `br`, etc.), not `leave`/`leave.s` instructions which are structural control flow for exception handling in async state machines.

## [2.30.8] - 2026-05-07

### Fixed
- **AssertionTracking: null-propagation support via branch retargeting** — Null-conditional (`?.`) assertion chains are now fully instrumented instead of being skipped. The weaver retargets outbound branches from `?.` operators to the `leave` instruction inside the try block, so when `?.` short-circuits (value is null), execution exits the try cleanly without tracking (correct — no assertion ran). This replaces the previous approach of skipping these statements entirely.

## [2.30.7] - 2026-05-07

### Added
- **AssertionTracking package** (Kronikol.AssertionTracking): Cecil-based IL weaver that instruments FluentAssertions .Should() call chains post-compilation with assertion tracking. Operates on compiled IL preserving full C# semantics including null propagation. Activated via [assembly: TrackAssertionsBeta].
  - [SuppressAssertionTracking] attribute works on method and class level
  - Safely skips async state machine methods (MoveNext)
  - Detects and skips null-propagation (?.) statements that would produce invalid IL
  - Auto-generates TrackAssertionsBetaAttribute and SuppressAssertionTrackingAttribute via MSBuild targets
  - Runs AfterTargets=CoreCompile - no impact on IntelliSense or source generation

## [2.30.6] - 2026-05-06

### Fixed
- **Cross-platform caller file path extraction**: `Track.That()` source-location extraction now handles Windows-style backslash paths correctly on Linux. Previously, `Path.GetFileName()` on Linux would not strip directory components from paths containing `\`, causing assertion location tooltips to display the full path instead of just the filename.

## [2.30.5] - 2026-05-06

### Added
- **LightBDD inline parameter highlighting**: Step text in the HTML report now renders LightBDD parameters inline within the prose (e.g. "customer has `105` in account" with the value highlighted) instead of appending them as separate badges after the step text. This matches LightBDD's native HTML report behaviour and eliminates the previous duplicative display. Parameters are color-coded by verification status (success/failure/exception) and show a tooltip with the parameter name. When `TextSegments` are present on a step, separate inline parameter badges are suppressed.

## [2.30.4] - 2026-05-06

### Added
- **Assertion source-location tooltips**: `Track.That()`, `Track.That<T>()`, and `Track.ThatAsync()` now capture `[CallerFilePath]` and `[CallerLineNumber]`. The source location is embedded as a PlantUML comment (`'__assertionLoc__:Filename.cs:L42`) in the diagram source. When assertion notes are visible in the HTML report, hovering over an assertion note displays a native browser tooltip showing the source file and line number (e.g. "MyTests.cs L42").
- **AssertionRewriter: caller info pass-through**: When `OriginalFilePath` is set on the rewriter, the generated `Track.That()`/`Track.ThatAsync()` calls include explicit `callerFilePath` and `callerLineNumber` named arguments pointing to the original source file and line, so tooltips reference the correct file even though compilation runs from rewritten intermediate files.

## [2.30.3] - 2026-05-06

### Changed
- **Mobile: zoom controls hidden** — The diagram zoom slider is now hidden on mobile viewports (≤768px) since mobile devices use native pinch-to-zoom.
- **Mobile: context menu as bottom sheet** — The right-click diagram context menu now renders as a bottom-anchored sheet on mobile with larger tap targets (12px padding, 15px font), full-width layout, and slide-up animation. Submenus open on tap instead of hover.

## [2.30.2] - 2026-05-05

### Fixed
- **Report header: CI metadata alignment** — When CI metadata is present, the CI metadata + pie chart group now aligns to the top of the test-summary panel (previously it was vertically centered). When CI metadata is absent, the pie chart remains centered both vertically and horizontally.

## [2.30.1] - 2026-05-05

### Fixed
- **AssertionRewriter: `TrackAssertionsAttribute` now auto-generated at build time** (fixes #31, #33): The `.targets` file now emits `TrackAssertionsAttribute` and `SuppressAssertionTrackingAttribute` source files into the intermediate output path and includes them as `<Compile>` items before compilation. Previously, these types were supposed to be delivered via NuGet `contentFiles` but the packaging was broken — the nupkg contained no `contentFiles/` directory and the nuspec had no `<contentFiles>` metadata section. Users can now just add `[assembly: TrackAssertions]` without defining the attribute type manually.
- **AssertionRewriter: removed broken `contentFiles` packaging** from the `.csproj`. The `<None Pack="true" PackagePath="contentFiles\...">` items were never producing valid content files in the nupkg.

### Documentation
- **Wiki: AssertionRewriter correctly described as MSBuild task** (fixes #32): Replaced 4 incorrect references to "source generator" with accurate MSBuild task terminology. Updated the compatibility note to explain it runs `BeforeTargets="CoreCompile"` and coexists with all source generators. Updated Quick Start version reference and added note that attribute types are auto-generated.

## [2.30.0] - 2026-05-05

### Changed
- **Mouse wheel zoom now requires Ctrl key**: Previously, scrolling the mouse wheel over a selected diagram would zoom without requiring any modifier key. Now `Ctrl+wheel` (or `Cmd+wheel` on macOS) is always required to zoom diagrams, regardless of selection state. Plain mouse wheel scrolling passes through to the page as normal. This prevents accidental zoom when scrolling through reports.

## [2.29.20-beta] - 2026-05-05

### Fixed
- **Parameterized scenario group names not humanized**: When xUnit3 or TUnit tests use `[MemberData]`/`[MethodDataSource]` (tabular input), the scenario group heading in reports now correctly shows "My test scenario name" instead of raw method names like `My_test_scenario_name` or `MyTestScenarioName`. The `OutlineId` is now passed through `ScenarioTitleResolver.FormatScenarioDisplayName()` before being used as the group display name.

## [2.29.19-beta] - 2026-05-05

### Removed
- **`CurrentTestInfo.SafeFetcher`** (TUnit): Reverted. The non-throwing fetcher prevented `TestInfoResolver` from falling through to `TestIdentityScope.Current`/`GlobalFallback` and caused `TestTrackingMessageHandler` to track warmup traffic under garbage identities. The throwing behavior of `Fetcher` is intentional — the exception is caught by all consumers and triggers the correct fallback behaviour. Closes #28 (not planned).

## [2.29.18-beta] - 2026-05-05

### Fixed
- **Vertical drag shaking on zoomed diagrams**: Use `e.clientY` instead of `e.pageY` for drag-to-scroll. `pageY` includes `window.scrollY`, causing oscillation after `scrollBy()` shifts the page.

### Changed
- **Removed `[Obsolete]` from framework adapter `ServiceCollectionExtensions`**: The per-framework `TrackDependenciesForDiagrams` overloads (TUnit, xUnit3, xUnit2, NUnit4, MSTest, LightBDD, BDDfy, ReqNRoll) are no longer marked obsolete. They provide type-safe convenience overloads and are used by the project templates. Fixes #30.

### Removed
- **`CurrentTestInfo.SafeFetcher`** (TUnit): Reverted. The non-throwing fetcher prevented `TestInfoResolver` from falling through to `TestIdentityScope.Current`/`GlobalFallback` and caused `TestTrackingMessageHandler` to track warmup traffic under garbage identities. The throwing behavior of `Fetcher` is intentional — the exception is caught by all consumers and triggers the correct fallback behaviour.

## [2.29.17-beta] - 2026-05-06

### Added
- **Project templates** (`Kronikol.Templates`): 12 `dotnet new` templates for scaffolding component test projects pre-configured with dependency tracking, report generation, and automatic assertion rewriting. Templates: `kronikol-xunit3`, `kronikol-xunit2`, `kronikol-tunit`, `kronikol-nunit4`, `kronikol-mstest`, `kronikol-lightbdd-xunit3`, `kronikol-lightbdd-xunit2`, `kronikol-lightbdd-tunit`, `kronikol-bddfy-xunit3`, `kronikol-reqnroll-xunit3`, `kronikol-reqnroll-xunit2`, `kronikol-reqnroll-tunit`.

## [2.29.16-beta] - 2026-05-05

### Added
- **AssertionRewriter package** (`Kronikol.AssertionRewriter`): Roslyn-based MSBuild task that automatically wraps `.Should()` expression statements in `Track.That(() => ...)` at compile time. Opt in with `[assembly: TrackAssertions]`. Supports `[SuppressAssertionTracking]` attribute and `#pragma warning disable` regions for selective opt-out.

### Changed
- **E2E test parallelization**: Split 388 Playwright tests from a single sequential collection into 6 parallel collections (Zoom, Notes, Search, Diagrams, Reports, Scenarios) + existing FullPipeline. Increased `maxParallelThreads` from 4 to 8. Reduces local E2E execution time from ~20 min to ~9.5 min.

## [2.29.15-beta] - 2026-05-06

### Fixed
- **LightBDD namespace stripping**: Fully-qualified type names (e.g. `Example.Api.Tests.Component.LightBDD.xUnit3.MuffinBatchExpectation`) are now stripped to short names in both scenario display names (timeline labels) and step text. Previously only step text was stripped, leaving namespaces visible in timeline scenario labels and example values.

### Changed
- **Expandable parameter truncation**: Preview text is now truncated to 300 characters (was unlimited), and full JSON content is truncated to 10,000 characters to prevent massive DOM nodes from slow-rendering large payloads.
- **Steps rendered above parameter table**: In scenario detail panels, the step list and failure diagnostics now appear above the parameter table (previously below), improving readability for scenarios with large data tables.
- **Collapsible step tables**: Steps containing inline parameter tables (e.g. ReqNRoll `<table>` arguments) are now rendered inside collapsible `<details>` elements. Column headers are highlighted on hover for easier scanning of wide tables.

## [2.29.14-beta] - 2026-05-05

### Changed
- **CI metadata stacked above pie chart**: When CI metadata is present, it now appears vertically stacked above the summary pie chart (both centered horizontally and vertically within the header row) instead of side-by-side.
- **CI metadata box styling**: The CI metadata panel now has a gray background, rounded corners, and padding matching the test execution summary panel.
- **Violet theme**: CI metadata panel uses the violet background in the violet theme.

## [2.29.13-beta] - 2026-05-05

### Changed
- **Zoom UI simplified**: Removed the zoom toggle button and double-click-to-zoom. Only the zoom slider remains for controlling diagram zoom level.
- **Click-to-deselect**: Clicking a selected diagram now deselects it (previously clicking always selected).
- **Zoom slider repositioned**: Slider now floats at `2em` from top and left of the container (was `6px` from top with `6px` padding-left).
- **Vertical drag scrolls page**: When dragging a zoomed diagram vertically, the page scrolls instead of the container (horizontal drag still pans the container).

## [2.29.12-beta] - 2026-05-05

### Fixed
- **Humanize bug for PascalCase+underscore names**: `StringCasing.Titleize()` now correctly splits PascalCase words in underscore-separated names (e.g. `ParameterizedDiagnostic_Feature` → "Parameterized Diagnostic Feature" instead of "Parameterizeddiagnostic Feature").
- **Preview text no longer includes type name prefix**: `GeneratePreview()` now returns `{ Prop: val, ... }` instead of `TypeName { Prop: val, ... }`. Nested type names are still preserved (e.g. `Ingredients: IngredientSet { ... }`).
- **`GenerateDictionaryPreview` shows nested values**: For dictionaries with nested dict/list values, the preview now shows `{ Key: { nested }, ... }` instead of just `{ Key1, Key2, Key3 }`.
- **Combined table only renders for assertion scenarios**: The combined input→output table is now only shown when there are both setup (Given) and assertion (Then) phase tabular parameters. Pure-input scenarios render tables inline within their steps instead.

### Changed
- **ReqNRoll feature name aligned**: Changed from "Muffins Creation" to "Parameterized Diagnostic Feature" with endpoint `/diagnostic` to match xUnit3/LightBDD.

## [2.29.11-beta] - 2026-05-05

### Fixed
- **`FormatPreviewValue` now handles classes without custom ToString()**: When a nested property value is a class (not a record), and its `.ToString()` returns just the type name, the renderer now recursively generates a rich preview (e.g. `IngredientSet { Flour = Plain Flour, ... }`) instead of displaying the full qualified type name. This fixes LightBDD and any xUnit3 test using plain classes for test data.
- **`GenerateNestedPreview` matches record ToString() style**: Nested previews no longer wrap string values in quotes, matching the output style of C# record auto-generated ToString().

### Changed
- **`GroupScalarsByPrefix` preserves original key names**: Grouped scalar columns now keep their full original names (e.g. `ExpectedIngredientCount`) instead of stripping the prefix and adding spaces (`Ingredient Count`). This matches how xUnit3/LightBDD render typed object property names.
- **ReqNRoll feature file aligned**: Step text changed from "with the following baking profile:" to "the following baking:" so the derived group name is "Baking" (matching the xUnit3 property name). Table headers now use PascalCase (`DurationMinutes`, `PanType`) matching C# conventions. Added `ExpectedHasBakingInfo` column.

## [2.29.10-beta] - 2026-05-05

### Changed
- **ExampleValueGrouper parent nesting**: When multiple step tables are detected and a parent concept is derivable from the step text (e.g. "a muffin recipe ... with the following ingredients:"), all table groups are now nested under a single parent column ("Recipe") as an expandable (R4) cell. This aligns ReqNRoll rendered output with xUnit3/LightBDD wrapper type rendering.
- **ExampleValueGrouper scalar prefix grouping**: Unconsumed Example columns with a common prefix (e.g. `ExpectedIngredientCount`, `ExpectedToppingCount`) are automatically grouped into a sub-table (R3) column ("Expected") with simplified display names ("Ingredient Count", "Topping Count").
- **Unified column headers across all 3 frameworks**: xUnit3, LightBDD, and ReqNRoll now all render as "Recipe Name | Recipe | Expected" for the muffin recipe example tests.
- **Example test data unified**: All 3 framework example tests now use the same 3 recipes (Classic, Rustic Wholesome, Spiced Deluxe) with identical data values.

### Fixed
- **param-expand toggle prefix mismatch**: The `<details class="param-expand">` toggle event handler was constructing the wrong prefix (`'pg' + scenario.id`) instead of reading the correct prefix from the table's `data-prefix` attribute. This caused the detail panel to not become visible when clicking on an expandable cell within a parameterized row.

## [2.29.9-beta] - 2026-05-05

### Added
- **ReqNRoll structured data rendering via `ExampleValueGrouper`**: ReqNRoll Scenario Outline step tables are now automatically grouped into structured objects for rich rendering in the parameterized examples table. Single-row step tables render as sub-tables (R3), multi-row step tables render as expandable details (R4) — matching the same visual output produced by xUnit3 MemberData with complex object parameters.
- **`IDictionary<string, object?>` support in `ParameterValueRenderer`**: All rendering methods (`IsSmallComplexObject`, `IsComplexValue`, `RenderSubTable`, `GeneratePreview`, `GenerateHighlightedJson`, `TryGetFlattenableProperties`, `FlattenToStringValues`, `FlattenToRawValues`) now handle dictionaries as object-like values rather than collections, enabling dictionary-based structured parameter rendering.
- **`ExampleRawValues` property on `ReqNRollScenarioInfo`**: Carries structured raw values (dictionaries/lists) alongside the existing flat `ExampleValues` string dictionary.

### Changed
- **`ReqNRollTrackingHooks.AfterScenario`**: Now calls `ExampleValueGrouper.BuildStructured()` to produce grouped `ExampleValues` and `ExampleRawValues` from flat Example columns and step table data.
- **E2E tests updated**: `ReqNRoll_pipeline_renders_all_scalar_column_headers` and `ReqNRoll_pipeline_all_cells_are_plain_scalar` updated to verify the new structured column headers and rich cell rendering.

## [2.29.8-beta] - 2026-05-05

### Changed
- **Framework-specific argument capture decorators for LightBDD.xUnit3 and LightBDD.TUnit**: Each package now registers its own `IScenarioDecorator` that extracts raw test method arguments directly from the underlying framework's test context (`XUnit3ArgumentExtractor` / `TUnitArgumentExtractor`), falling back to LightBDD's generic `IScenario.Descriptor.Parameters` extraction if the framework context is unavailable. This ensures the same argument extraction logic used by the non-LightBDD adapters is also used when running LightBDD on those frameworks.
- **Added `TUnitArgumentExtractor` in `Kronikol.TUnit`**: Shared helper for extracting raw arguments from TUnit's `TestContext.Metadata.TestDetails.TestMethodArguments`, analogous to `XUnit3ArgumentExtractor` in the xUnit3 package.
- **Added project references from LightBDD framework packages to their base packages**: `LightBDD.TUnit` → `Kronikol.TUnit`, `LightBDD.xUnit2` → `Kronikol.xUnit2`. Enables sharing of argument extractors, `CurrentTestInfo`, and other infrastructure.
- **Core `ArgumentCaptureScenarioDecorator.TryCaptureFromDescriptor()` now `internal static`**: Framework-specific decorators can call this as a fallback when native extraction fails.
- **`CreateStandardReportsWithDiagramsInternal` accepts `registerDefaultDecorator` parameter**: Framework packages that register their own decorator pass `false` to avoid redundant registration.

## [2.29.7-beta] - 2026-05-05

### Changed
- **Framework-agnostic `ArgumentCaptureScenarioDecorator` moved to `LightBDD.Core`**: The decorator now uses `IScenario.Descriptor.Parameters` (LightBDD's own API) to capture raw arguments, making it fully framework-agnostic. Enables rich sub-table rendering for all LightBDD adapters (xUnit3, TUnit, xUnit2) with zero framework-specific code.
- **`LightBddConfiguration.CreateStandardReportsWithDiagrams()` overload added to all LightBDD packages** (xUnit3, TUnit, xUnit2): Consistent API across all frameworks — registers both the report pipeline and the argument capture decorator automatically.
- **`ReportWritersConfiguration.CreateStandardReportsWithDiagrams()` deprecated** across all packages: The old overload still works but produces a compiler warning directing users to the `LightBddConfiguration` overload.
- **`[CaptureLightBddArguments]` attribute deprecated** (xUnit3): The assembly-level attribute is no longer needed when using the new API.
- **BDDfy adapter now captures raw test method arguments**: `DiagramCapturingProcessor` captures `TestMethodArguments` from xUnit3's test context, passing them through `ParameterParser.ExtractStructuredParametersWithRaw()` — the same pipeline used by the non-BDDfy xUnit3 adapter. Enables rich rendering of complex objects in BDDfy parameterized tests.
- **Consolidated shared xUnit3 argument extraction code**: Created `XUnit3ArgumentExtractor` in `Kronikol.xUnit3` — a single shared helper for extracting raw arguments from `XunitTest`/`XunitTestCase`. Used by all three xUnit3-based packages (xUnit3, BDDfy.xUnit3, LightBDD.xUnit3) instead of duplicating the extraction pattern.
- **Added project references for code sharing**: `Kronikol.BDDfy.xUnit3` and `Kronikol.LightBDD.xUnit3` now reference `Kronikol.xUnit3` directly, enabling shared infrastructure (argument extraction, `CurrentTestInfo`) and eliminating code duplication that would be difficult to keep in sync.
- **BDDfy `CurrentTestInfo` delegates to xUnit3 implementation**: Eliminates near-identical copy in the BDDfy package.

## [2.29.6-beta] - 2026-05-05

### Fixed
- **LightBDD adapter: raw argument capture is now automatic — no `[assembly: CaptureLightBddArguments]` attribute required**: Previously, users had to add an explicit assembly-level attribute to enable rich rendering of complex objects (records, lists, nested types) in parameterized LightBDD reports. Without it, class-based types that don't override `ToString()` (e.g. `List<ToppingData>`) rendered as opaque type names. The new `ArgumentCaptureScenarioDecorator` (an `IScenarioDecorator`) is registered automatically when using the `LightBddConfiguration.CreateStandardReportsWithDiagrams()` overload. It captures raw test method arguments from xUnit3's `TestContext.Current` during scenario execution, before arguments are cleared. The legacy `[assembly: CaptureLightBddArguments]` attribute still works but is no longer necessary.

### Changed
- **New `LightBddConfiguration.CreateStandardReportsWithDiagrams(options)` overload**: Direct extension on `LightBddConfiguration` that registers both the report pipeline and the argument capture decorator in one call. Replaces the previous pattern of `configuration.ReportWritersConfiguration().CreateStandardReportsWithDiagrams(options)` — use `configuration.CreateStandardReportsWithDiagrams(options)` instead.
- **LightBDD example updated to class-based types**: Example now uses init-property classes (not records) with 3 recipe variations (Classic, Rustic Wholesome, Spiced Deluxe), matching real-world usage where `ToString()` returns just the type name.

## [2.29.5-beta] - 2026-05-05

### Fixed
- **`Track.That()` now resolves dotted property chains on captured variables**: Previously, `Track.That(() => x.Should().HaveCount(expected.ExpectedIngredientCount))` would resolve `expected` to its full `ToString()` representation (e.g. `'MuffinBatchExpectation { ExpectedIngredientCount =...'.ExpectedIngredientCount`) instead of navigating the property chain to the leaf value. `ClosureValueResolver` now detects dotted property access (e.g. `expected.ExpectedIngredientCount`, `config.Inner.Value`) in the assertion args, walks the chain via reflection, and resolves to the leaf value (e.g. `'5'`). Falls back gracefully when properties don't exist or intermediate values are null.
- **Assertion value substitution now processes longer keys first**: `SubstituteResolvedValues` now sorts resolved-value keys by length descending before substitution, preventing shorter keys (e.g. `expected`) from partially matching before their longer dotted variants (e.g. `expected.ExpectedIngredientCount`).

## [2.29.4-beta] - 2026-05-04

### Fixed
- **LightBDD adapter now captures raw parameter objects for rich rendering**: Previously, LightBDD's result API only exposes `FormattedValue` (ToString() representation), causing complex objects like `List<ToppingData>` to render as type names rather than their actual contents. Added `CaptureLightBddArgumentsAttribute` (assembly-level xUnit3 `BeforeAfterTestAttribute`) and `CapturedScenarioArguments` static store to capture raw test method arguments during execution. The LightBDD report mapper now looks up these captured values and populates `ExampleRawValues`, enabling the same rich R3/R4 rendering (sub-tables, expandable JSON) that xUnit3 and other adapters already support.
- **ReqNRoll scenario outline row ordering now deterministic**: Scenario outline examples sharing the same title were ordered non-deterministically because `.ThenBy(x => x.ScenarioTitle)` provided no differentiation. Added a secondary sort by `ExampleValues` content to ensure stable alphabetical ordering across runs.

## [2.29.3-beta] - 2026-05-04

### Fixed
- **Complex object parameters (nested records, collections) render poorly in string-based path**: When using LightBDD or other frameworks that only provide `ToString()` representations (no raw objects), nested record values like `IngredientSet { Flour = Plain, ... }` were displayed as raw text and `List<T>` properties showed as `System.Collections.Generic.List'1[Namespace.Type]`. Fixed by making `RenderSubTableFromParsed` recursively render nested record values as nested sub-tables and clean collection type names (e.g. `List<ToppingData>`). Also improved the expandable (R4) JSON view and preview text for the same cases.

## [2.29.2-beta] - 2026-05-04

### Changed
- **Zoom: no vertical scrollbar constraint**: Zoomed diagrams now expand to their full natural height instead of being capped at `80vh` with a vertical scrollbar. Only a horizontal scrollbar appears when the diagram is wider than the container.
- **Zoom controls float at top-left while scrolling**: The zoom button and slider now use `position: sticky` so they remain visible at the top-left of the diagram as the user scrolls down through a tall zoomed diagram.

## [2.29.1-beta] - 2026-05-04

### Fixed
- **Parameterized test parameters garbled when values contain square brackets**: `ParameterParser.Parse()` used `LastIndexOf('[')` to find the parameter bracket group, which incorrectly matched `[` characters inside parameter values (e.g. `Items[0].BatchId`). This caused rows 3-5 of parameterized tests with array-index field names to display blank parameter columns with the entire remainder dumped into a single "Arg 0" column. Fixed by requiring ` [` (space-bracket) to identify parameter bracket groups, consistent with `ExtractBaseName()` which already used this pattern.

## [2.29.0-beta] - 2026-05-04

### Added
- **Gradual zoom (beta)**: Diagrams now support smooth, incremental zooming instead of only toggling between fit-to-width and full size.
  - **Diagram selection**: Click a diagram to select it (blue glow indicator). Click elsewhere or press Escape to deselect.
  - **Zoom slider**: A horizontal range slider appears alongside the zoom toggle button on zoomable diagrams. Drag to smoothly zoom between fit-to-width and 100% natural size.
  - **Keyboard zoom**: `Ctrl`+`+` / `Ctrl`+`-` zoom the selected diagram in 5% increments.
  - **Mouse wheel zoom**: `Ctrl`+scroll wheel zooms the diagram under the cursor.
  - **Zoom-to-cursor**: All zoom methods (slider, keyboard, mouse wheel, double-click) scroll the diagram to keep the artifact under the cursor in the same viewport position, so you can zoom into a specific area without losing your place.

## [2.28.45] - 2026-05-04

### Added
- **`TestRunReportTitle` property** on `ReportConfigurationOptions`: allows full customization of the test run report page title. When set, the value is used verbatim — no " - Test Run Report" suffix is appended. When `null` (default), the existing auto-derived logic applies: `ComponentDiagramOptions.Title` → `FixedNameForReceivingService` → `"Test Run Report"`.
- **HTML `<title>` element**: the test run report HTML now includes a `<title>` tag in `<head>`, setting the browser tab title to match the report heading.

## [2.28.44] - 2026-05-04

### Fixed
- **Flaky unit tests under parallel execution**: Fixed shared static state pollution across xUnit parallel test classes. `TrackThatTests` now uses a unique `_testId` per instance (via `Guid.NewGuid()`) instead of a shared constant, preventing log cross-contamination between test methods. `TrackThatIntegrationTests` now filters `trackedLogs` by `testId` before passing to `GenerateHtmlReport`, preventing assertion logs from other parallel tests leaking into report assertions. `MessageTrackerTests` "does_nothing" tests now use unique `CallerName` markers and filter by marker instead of global log count. Added `[Collection("TestIdentityScope")]` to `TestIdentityScopeTests` and `TestInfoResolverTests` to serialize tests that mutate `TestIdentityScope.GlobalFallback`.

## [2.28.43] - 2026-05-04

### Reverted
- **Browser zoom fixed-pixel `max-width` on SVG diagrams** (introduced in v2.28.40): Reverted the SVG `max-width` from a fixed pixel snapshot (`container.clientWidth + 'px'`) back to `100%`. Removed the `snapshotMaxWidth()` function, the `window.resize` recalculation listener, and all pixel-snapshot calls in `addZoomButton`, `toggleDiagramZoom`, and `restoreZoomState`.

## [2.28.42] - 2026-05-04

### Fixed
- **Note hover buttons acting on wrong note**: In diagrams with `entity`, `queue`, or `database` participant types, clicking the minus/plus hover button on one note would collapse/expand a different note further down. The root cause was that `findNoteGroups` misidentified participant shapes (which also render as path+text SVG groups) as note groups, causing note index misalignment. Fixed by adding fold-triangle geometry detection — every PlantUML note has a small triangular fold path at one corner that participants never have. This works regardless of theme or note fill color.
- **`applyAssertionFilter` missing visibility parameter in `setNoteState`**: When toggling individual note state via hover buttons, `applyAssertionFilter` was called without the `showing` parameter, defaulting to assertion-hidden mode regardless of the actual setting.
- **Note state rollback on render failure**: If a PlantUML WASM re-render fails or times out after a note state change, the `_noteSteps` value is now rolled back to the previous state and buttons are resynchronized, preventing visual/state desynchronization.
- **Double-click on note buttons no longer triggers zoom toggle**: Added `dblclick` event suppression on all note hover button rects to prevent fast double-clicks from bubbling to the diagram zoom toggle handler.

## [2.28.41] - 2026-05-03

### Fixed
- **Track.TestIdResolver not set when DiagrammedTestRun is not instantiated**: The v2.28.40 fix only set `Track.TestIdResolver` in `DiagrammedTestRun` constructors, which are never called when users compose their own test setup (e.g. using `DiagrammedTestRun.TestContexts` statically). Moved the resolver initialization into `DiagrammedComponentTest` constructors/SetUp methods (xUnit3, NUnit4, TUnit, MSTest) and `TestTrackingAttribute.Before()` (xUnit2), which run before each test executes.

## [2.28.40] - 2026-05-03

### Fixed
- **Browser zoom now scales diagrams**: Large diagrams that start fit-to-width no longer stay the same physical size when using browser zoom (Ctrl+/-). The SVG `max-width` is now set as a fixed pixel value (snapshot of container width at render time) instead of `100%`, so browser zoom scales the diagram proportionally. A horizontal scrollbar appears when the zoomed diagram overflows. Genuine window resize recalculates the snapshot. The toggle to "natural size" is unaffected.
- **Track.That assertions silently dropped in non-BDDfy packages**: `Track.That()` assertions were not appearing in reports for projects using the standalone framework packages (xUnit3, xUnit2, NUnit4, TUnit, MSTest). The root cause was that `Track.TestIdResolver` was never configured, causing `ResolveTestId()` to return null and `LogAssertion()` to silently discard the assertion. Fixed by setting `Track.TestIdResolver` in each framework's `DiagrammedTestRun` constructor/Setup method. The BDDfy.xUnit3 package was already working correctly.

## [2.28.39] - 2026-05-03

### Fixed
- **Assertion Show/Hide scope bug**: Scenario-level Assertions Show/Hide radio buttons no longer affect other scenarios. Previously, clicking Show on one scenario would cause assertion notes to appear in other scenarios on subsequent re-renders. Assertion visibility is now stored per-container instead of globally.
- **Assertion radio button detection**: Report now correctly shows Assertions radio buttons when assertion notes exist in diagram source (previously only detected via tracked logs)

## [2.28.38] - 2026-05-03

### Changed
- **Radio button labels**: Changed from past-tense (Expanded/Collapsed/Truncated/Shown/Hidden) to imperative (Expand/Collapse/Truncate/Show/Hide) for clearer action-oriented UI

## [2.28.37] - 2026-05-03

### Added
- **Assertion value resolution**: `Track.That()` now resolves runtime values of captured variables via closure inspection and displays them in assertion notes (e.g. `expected` → `'hello-world'`). Falls back to source text for computed expressions, complex objects, or when resolution is not possible.
- **`Track.DiagnosticMode`**: When enabled, records reasons for value resolution fallbacks in `Track.DiagnosticLog` and the DiagnosticReport.html
- **`ClosureValueResolver`**: New internal component that inspects delegate closures to extract captured variable values safely

## [2.28.36] - 2026-05-03

### Fixed
- **Assertion expression formatting**: Removed null-forgiving operators (`!`) from rendered assertion text (e.g. `_auditLogResponse!.StatusCode` no longer shows the `!`)
- **Assertion expression formatting**: Strip leading `_` prefix from field names before humanising (e.g. `_auditLogResponse` renders as "Audit log response")

### Changed
- **Lambda assertion arguments**: Lambda expressions in assertion args are now wrapped in square brackets for readability (e.g. `OnlyContain(x => x.Foo == bar)` renders as "only contain [x => x.Foo == bar]")

## [2.28.35] - 2026-05-03

### Fixed
- **`Track.That()` assertions not appearing in diagrams**: `Track.That()` silently discarded assertions when called from LightBDD, BDDfy, or ReqNRoll test contexts because it could not resolve the test ID from framework-specific execution contexts

### Added
- **`Track.TestIdResolver`**: New static delegate on `Track` that framework integrations use to resolve the current test ID. Set automatically by LightBDD, BDDfy, and ReqNRoll adapters during configuration — no user action needed

## [2.28.34] - 2026-05-03

### Removed
- **Selenium test project**: Removed `Kronikol.Tests.Selenium` — fully replaced by the Playwright-based `Kronikol.Tests.EndToEnd` suite (identical 293-test coverage)
- Removed Selenium CI matrix jobs (4 jobs) — Playwright E2E jobs now provide full browser test coverage in CI

## [2.28.33] - 2026-05-03

### Changed
- **Setup partition background color**: Default changed from `#E2E2F0` to `#F6F6F6` for better visual distinction from participant fills
- **`SetupHighlightColor` configuration**: New property on `DiagramsFetcherOptions` and `ReportConfigurationOptions` allows users to customise the Setup partition background color

### Fixed
- **Collapsible note safety-net**: Removed all hardcoded color exclusions from `hasNoteFill()` — the fill-frequency filter now works universally regardless of PlantUML theme or user-configured colors
- **Safety-net robustness**: Added positional fallback with text-content validation when the fill-frequency filter cannot find a matching count, preventing incorrect note-group mapping in edge cases

## [2.28.32] - 2026-05-03

### Fixed
- `DependencyCategory` is now preserved through the deferred log path (`TrackingLogMode.Deferred`) — previously lost when `PendingLogEntry` was flushed

## [2.28.31] - 2026-05-03

### Added
- `DependencyCategory` property on `TrackingProxyOptions` — allows non-HTTP proxied dependencies (SMTP, gRPC, FTP, etc.) to render with the correct participant shape and colour in component diagrams instead of defaulting to HTTP API styling

## [2.28.30] - 2026-05-05

### Added
- **`Track.That()` inline assertion tracking**: New API (`Track.That(() => expr.Should()...)`) that captures assertion expressions and renders them as styled PlantUML `hnote` annotations in sequence diagrams. Green notes (✓) for passing assertions, red notes (✗) for failures (with failure message). Supports sync, async, and value-returning overloads via `[CallerArgumentExpression]`.
- **`AssertionExpressionFormatter`**: Converts raw `[CallerArgumentExpression]` strings (e.g. `result.Count.Should().Be(3)`) into readable English summaries (e.g. `result count should be 3`). Handles `.Should().` splitting, PascalCase word boundary detection, generic type arguments (`BeOfType<string>()`), `.And.` chaining, lambda arguments, and enum prefix stripping.
- **Assertions toggle UI**: Report-level and scenario-level "Assertions: Show/Hide" radio buttons (hidden by default) that toggle assertion note visibility without page reload. Uses the same queue-based re-render pattern as Details and Headers toggles.
- **Conditional `<<assertionNote>>` PlantUML style**: The custom style block is only emitted when assertion notes are present in the diagram, avoiding unnecessary styling overhead.

## [2.28.29] - 2026-05-04

### Added
- **Playwright E2E test suite**: Migrated all 27 Selenium browser test files to Playwright in `Kronikol.Tests.EndToEnd`. 309 tests pass (28 skipped wiki screenshot/gif tests). Tests run with full parallel execution support.
- **AGENTS.md**: Added agent customization file with Playwright test conventions.

### Fixed
- **Playwright `WaitForFunctionAsync` timeouts under parallel load**: Added `PollingInterval = 200` to all `WaitForFunctionAsync` calls. The default `requestAnimationFrame`-based polling fails when multiple browser contexts run simultaneously in headless Chromium; explicit interval-based polling resolves the issue.
- **SVG interaction helpers**: `DispatchContextMenu()`, JS-dispatched `mouseenter`/`dblclick` for note hover rects, and `FillSearchBar()` with manual `keyup` dispatch — all needed because Playwright's native mouse events don't reliably trigger handlers on SVG elements or `onkeyup`-bound inputs.

## [2.28.28] - 2026-05-01

### Fixed
- **Spanner and Dapper component diagram arrows no longer show full SQL text**: At Raw verbosity, the `Method` field in `RequestResponseLog` entries now contains just the SQL keyword (`Select`, `Insert`, `Update`, `Delete`) instead of the full SQL query text. This was causing component diagram arrows to be hundreds of characters long (e.g. `"Spanner: Insert, InsertOrUpdate, SELECT CustomerId, CustomerName, PreferredMilkType, LikesExtraToppings..."`) instead of the expected short summary (`"Spanner: Insert, InsertOrUpdate, Select"`). Affected extensions: `SpannerTracker` and `TrackingDbCommand` (Dapper). The sequence diagram content is unaffected — full SQL text still appears in the note/content body.

### Added
- **`SpannerOperationClassifier.GetRawKeyword()`**: New method that extracts just the SQL keyword from command text or returns the operation name for non-SQL operations (mutations). Used internally by `SpannerTracker` for the `Method` field.
- **`DapperOperationClassifier.GetRawKeyword()`**: Delegates to `UnifiedSqlClassifier.GetRawKeyword()` for consistent keyword extraction.

## [2.28.27] - 2026-05-01

### Fixed
- **CI: Unit test assertion updated after note button index refactor**: `MakeNotesCollapsible_passes_onTruncate_to_state_1` asserted the old variable name `idx` in the `setNoteState` call; updated to `srcIdx` to match the v2.28.26 refactor.

## [2.28.26] - 2026-05-01

### Fixed
- **Note hover buttons no longer affect the wrong note when header-only notes become empty**: When "Headers: Hidden" was active or a note was collapsed, notes whose entire content was gray HTTP headers (e.g. GET/DELETE request notes) would produce empty PlantUML notes with no SVG text elements. `findNoteGroups` skipped these empty notes, causing an index mismatch between SVG groups and source note blocks - so clicking a button on note N would actually collapse/expand note N-1. Fixed with two layers: (1) `buildSourceWithNoteStates` now inserts a non-breaking space placeholder when a note would otherwise be empty, ensuring PlantUML always renders text for every note; (2) `makeNotesCollapsible` now computes a `sourceIndexMap` when fewer SVG groups are detected than source blocks, mapping each SVG group to the correct source note by checking which notes are visible in the current rendered state.

### Added
- **8 new Selenium tests** (`NoteButtonIndexTests`) verifying note hover button index alignment: groups-vs-blocks equality with headers visible/hidden, minus-button targeting correct note after hide, multiple header-only notes maintaining alignment, collapse/expand with empty notes, and double-click cycling the correct note.

## [2.28.25] - 2026-05-01

### Added
- **4 additional Selenium tests** (`FailureClusterLinkTests`) targeting real-world cluster link navigation bugs found in a v2.22.12 report: parameterized group parent `<details>` opening, parameterized row viewport scroll, sequential click navigation without manual scroll-back, and triple sequential click navigation.

### Changed
- Updated `Selenium.WebDriver.ChromeDriver` from 136.0.7103.9200 to 147.0.7727.11700 to match current Chrome stable.

## [2.28.24] - 2026-05-01

### Fixed
- **Failure cluster links now navigate to the correct scenario when duplicate display names exist across features**: `GenerateScenarioAnchorId` previously produced identical `id` attributes for scenarios with the same display name in different features (e.g. "Health check fails" in both "Order API" and "Payment API"). `getElementById` always returned the first occurrence, so the second cluster link opened the wrong feature. Anchor IDs are now pre-computed with deduplication — the second occurrence receives a `-2` suffix (e.g. `scenario-health-check-fails-2`), the third gets `-3`, etc. The cluster links, scenario permalinks, and element IDs all use the same deduplicated map.

### Added
- **15 new Selenium tests** (`FailureClusterLinkTests`) verifying failure cluster link navigation: basic scroll-to-scenario, parent feature opening, multi-feature navigation, sequential link clicks, parameterized row activation and detail panel visibility, duplicate display name handling, URL hash updates, multi-cluster navigation, and already-expanded-feature edge case.

## [2.28.23] - 2026-05-01

### Fixed
- **`TrackDependenciesForDiagrams` no longer produces unpaired `override.com` entries in diagnostic reports** (fixes [#24](https://github.com/lemonlion/Kronikol/issues/24)): `TestTrackingMessageHandler` now auto-excludes requests to `override.com` (ASP.NET Core TestServer's internal base address) from tracking. These requests are still forwarded normally to the inner handler but produce no log entries. A new `ExcludedHosts` property on `TestTrackingMessageHandlerOptions` (default: `["override.com"]`) allows customising which hosts are excluded. Set to an empty collection to restore previous behavior.

### Added
- **`services.RemoveDbContext<TContext>()` extension method** (fixes [#25](https://github.com/lemonlion/Kronikol/issues/25)): New DI helper in `Kronikol.Extensions.EfCore.Relational` that removes all registrations related to a DbContext type — including `IDbContextOptionsConfiguration<TContext>` (an internal EF Core type that survives `RemoveAll<DbContextOptions<T>>()`). Call this in `ConfigureTestServices` before re-registering the DbContext with a tracking interceptor to ensure no production configuration callbacks survive.

### Documentation
- **EF Core Extension wiki**: Replaced `RemoveAll<DbContextOptions<T>>()` in Option A with `RemoveDbContext<T>()` and added migration warning about the insufficient removal pattern.
- **HTTP Tracking Setup wiki**: Added `ExcludedHosts` to the `TestTrackingMessageHandlerOptions` reference table.
- **Tracking Dependencies wiki**: Added note about automatic `override.com` exclusion in v2.28.23+.

## [2.28.22] - 2026-05-01

### Fixed
- **`CurrentTestInfo.Fetcher` no longer short-circuits the resolution chain when test context is unavailable**: All 8 framework `CurrentTestInfo.Fetcher` implementations (xUnit3, xUnit2, NUnit4, TUnit, MSTest, LightBDD, ReqNRoll, BDDfy) now throw `InvalidOperationException` when the test context is unavailable, instead of returning `TestIdentityScope.UnknownIdentity`. This allows the existing try/catch fallthrough pattern in `MessageTracker.GetTestInfo()`, `TestInfoResolver.Resolve()`, and `RequestResponseLogger.LogPair()` to correctly fall through to `TestIdentityScope.Current` and `GlobalFallback`. Previously, returning `("Unknown", "unknown")` satisfied the non-null check and caused resolution to stop immediately — silently misattributing events instead of reaching the correct fallback.
- **Defense-in-depth: `UnknownIdentity` treated as unresolved in all resolvers**: `TestInfoResolver.Resolve()` (both overloads), `MessageTracker.GetTestInfo()`, and `RequestResponseLogger.LogPair()` now explicitly check for `UnknownTestId` and fall through — regardless of whether the delegate throws or returns the sentinel value. This guards against custom fetcher implementations that return `UnknownIdentity` instead of throwing.

## [2.28.21] - 2026-05-01

### Added
- **`dependencyCategory` parameter on `RequestResponseLogger.LogPair`**: Both overloads now accept an optional `string? dependencyCategory` parameter that is passed through to `RequestResponseLog.DependencyCategory`. This allows manually logged interactions (blob uploads, custom service calls, etc.) to render with the correct participant shape and colour in sequence diagrams (e.g. `database` shape for blob storage) instead of the default generic `entity`.

## [2.28.20] - 2026-04-30

### Changed
- **Release workflow: ~60% faster builds via solution filter and parallelisation**: Replaced the sequential `for proj in src/*/` build and pack loops with a single `dotnet build release.slnf` / `dotnet pack release.slnf` invocation using a new solution filter file (`release.slnf`) that includes all 49 src projects. MSBuild now parallelises compilation across all available cores using its dependency-graph-aware scheduler (GitHub runners have 4 vCPUs). Added a dedicated `dotnet restore` step so NuGet restore happens once with a shared cache. Removed `apt-get clean` and `docker image prune` from the disk cleanup step (the fast `rm -rf` commands alone free 30+ GB and Docker isn't used). Expected total workflow time reduction: ~8m 46s → ~3-4m.

## [2.28.19] - 2026-04-30

### Fixed
- **Eliminated all CI build warnings**: Fixed 10,879 compiler warnings across the solution:
  - Suppressed CS1591 (missing XML doc comment) in `Directory.Build.props` for all src projects — these are informational warnings for undocumented members that don't affect functionality.
  - Fixed CS0419 (ambiguous cref) in `TestPhaseContext.cs`, `KafkaTrackingInterceptor.cs`, and `DiagrammedTestRun.cs` by qualifying overloaded method references with parameter types.
  - Fixed CS1573 (missing param tag) in `MessageTracker.TrackMessageRequest` and `TrackMessageResponse` by adding `<param>` tags for `noteOnRight` and `statusLabel`.
  - Fixed CS1574 (unresolved cref) in `BlobClientOptionsExtensions.cs` by replacing invalid `BlobClientOptions.Transport` cref with inline code reference.
  - Fixed CS1587 (XML comment not on valid element) across 23 files in 8 framework adapter packages by moving XML doc comments before attributes instead of after.
  - Suppressed NU1902/NU1904 (NuGet package vulnerability audit) in SqlClient extension — transitive deps from `Microsoft.Data.SqlClient 2.x` have known vulnerabilities but cannot be updated without breaking consumer compatibility.

## [2.28.18] - 2026-04-30

### Fixed
- **`TrackSendMessage` missing event note styling**: `TrackSendMessage` now sets `MetaType = RequestResponseMetaType.Event` on both request and response log entries, matching `TrackSendEvent`. Previously it used `Default`, causing message payload notes to render with plain white backgrounds instead of the light blue `<<eventNote>>` styling (`BackgroundColor #cfecf7`, `FontSize 11`, `RoundCorner 10`) that visually distinguishes async messaging from synchronous HTTP calls.

## [2.28.17] - 2026-04-30

### Fixed
- **Zoom state lost after note collapse/expand or truncation/headers change**: When a diagram was zoomed to natural size and then notes were collapsed/expanded (via radio buttons or hover buttons) or truncation/headers were toggled, the SVG re-render destroyed inline zoom styles. The zoom button icon also showed the wrong state after re-render. Added `restoreZoomState()` function that re-applies zoom inline styles (`maxWidth: none`, `overflow: auto`, `maxHeight: 80vh`, `cursor: grab`) whenever `addZoomButton` runs after a re-render, and ensures the button icon reflects the current zoom state.

## [2.28.16] - 2026-04-30

### Added
- **`TestIdentityScope.GlobalFallback` for pre-existing background threads**: New static, thread-safe fallback that provides test identity to threads started before `TestIdentityScope.Begin()` was called — such as Cosmos DB Change Feed Processor polling threads, Hangfire workers, and hosted service loops. These threads have their own execution context and never inherit `AsyncLocal` values. `SetGlobalFallback(testName, testId)` sets the fallback; `ClearGlobalFallback()` clears it in teardown. The resolution chain is now four levels: HTTP headers → `CurrentTestInfoFetcher` delegate → `TestIdentityScope.Current` (AsyncLocal) → `TestIdentityScope.GlobalFallback` (static). Both `TestInfoResolver.Resolve()` and `MessageTracker.GetTestInfo()` check `GlobalFallback` as the last resort before returning null. This eliminates the common boilerplate of maintaining a manual shared mutable field with lock in test fixtures.

### Documentation
- **Wiki: Background Thread Correlation** — added Solution 3 (GlobalFallback) with usage example, resolution order table, parallel execution warning, and comparison table vs ActiveTestTracker pattern.

## [2.28.15] - 2026-04-30

### Added
- **`DependencyCategories` constants class**: New static class with 24 public constants for all dependency category strings (CosmosDB, SQL, BigQuery, Redis, ServiceBus, BlobStorage, HTTP, MediatR, MessageQueue, MongoDB, DynamoDB, Elasticsearch, Spanner, Bigtable, Database, S3, CloudStorage, Grpc, PostgreSQL, SqlServer, MySQL, SQLite, Oracle, AtlasDataApi). Replaces magic strings across 40+ files in `DependencyPalette`, all extension trackers/handlers, and options classes.
- **`TrackingDefaults` constants class**: New static class with shared default values — `CallerName` ("Caller") used across 30 options files, and `PlantUmlJsCdnBase` (CDN URL) used in `NodeJsPlantUmlRenderer` and `DiagramContextMenu`.
- **`DependencyCategoriesTests`**: 4 new tests verifying all palette keys have matching constants and all constants are registered in the palette.

### Changed
- All dependency category string literals across 27+ extension tracker/handler files now reference `DependencyCategories.*` constants.
- All `CallerName` default values across 30 options files now reference `TrackingDefaults.CallerName`.
- `DependencyPalette.CategoryToType` dictionary keys now use `DependencyCategories.*` constants.
- `ComponentDiagramGenerator` HTTP string literals now use `DependencyCategories.HTTP`.
- CDN URL in `NodeJsPlantUmlRenderer` and `DiagramContextMenu` now references `TrackingDefaults.PlantUmlJsCdnBase`.

## [2.28.14] - 2026-04-30

### Added
- **`TestIdentityScope.UnknownTestName`, `UnknownTestId`, `UnknownIdentity`**: New public constants for the sentinel test identity values (`"Unknown"` / `"unknown"`) used when no test context is available. All 8 framework adapter `CurrentTestInfo.Fetcher` implementations, `DiagnosticReportGenerator`, and `TestTrackingAttribute` (xUnit v2) now use these constants instead of magic strings. Consumers can reference `TestIdentityScope.UnknownTestId` when filtering diagnostic logs or implementing custom fallback logic.

## [2.28.13] - 2026-04-30

### Added
- **Empty TestContexts warning**: `ReportDiagnostics.Analyse()` now emits a warning when log entries exist but no test contexts (features) were provided. `ReportGenerator` also outputs a console warning and still generates the diagnostic report (when `DiagnosticMode=true`) even when features are empty. This surfaces the most common cause of empty reports: forgetting `DiagrammedTestRun.TestContexts.Enqueue(TestContext.Current)` in `DisposeAsync()`.

### Documentation
- **New wiki page: Background Thread Correlation** — covers `TestIdentityScope`, instance-scoped `ActiveTestTracker`, understanding Unknown entries, `LazyHttpContextAccessor` pattern
- **New wiki page: Service Bus Tracking Patterns** — MessageTracker setup, BeforePublish/AfterPublish bridging, atomic tracking, dual-caller attribution, function trigger correlation
- **HTTP Tracking Setup: Handler Pipeline Ordering** — handler chain diagrams, `PrimaryHandler` vs `AdditionalHandlers`, `IHttpContextAccessor` timing, `CreateTestTrackingClient` behaviour, `_ =>` vs `sp =>` gotcha
- **Multi-Host Test Architectures** — added sections on consistent test identity, HttpContextAccessor wiring order, LazyHttpContextAccessor, initialization order summary
- **Diagnostics and Debugging: Troubleshooting** — 8 new troubleshooting entries for empty reports, Unknown entries, missing Service Bus, function trigger attribution, CosmosDB Unknown, wrong service names
- **Quick Start (xUnit)** — added callout warning about TestContexts.Enqueue for custom fixtures
- **Integration CosmosDB Extension** — added fault injection code example with fixture pattern, background thread correlation link
- **What's New in 2.0** — added "Upgrading from 2.27.x to 2.28.x" migration section (CallerName rename, package alignment)
- **Tracking Custom Dependencies** — added interface-based blob tracking example using auto-resolving `LogPair`
- **Home and Sidebar** — added links to new Service Bus Tracking Patterns and Background Thread Correlation pages

## [2.28.12] - 2026-04-30

### Fixed
- **AtlasDataApi handler: HttpContextAccessor options fallback**: `AtlasDataApiTrackingMessageHandler` now reads `HttpContextAccessor` from the options object when not passed via the constructor parameter, matching the pattern used by all other extension handlers. Previously, setting `options.HttpContextAccessor` had no effect — only the constructor parameter worked.
- **BigQuery handler: HttpContextAccessor options fallback**: Same fix as AtlasDataApi — `BigQueryTrackingMessageHandler` now reads from `options.HttpContextAccessor` as a fallback.
- **EventHubs tracker: double-assignment bug**: `EventHubsTracker` had a duplicate assignment that overwrote the `options.HttpContextAccessor` fallback with just the constructor parameter. Fixed to use the correct `?? options.HttpContextAccessor` pattern.

### Added
- **`ITrackingComponent.HasHttpContextAccessor`**: New default interface member (`bool HasHttpContextAccessor => false;`) indicates whether a tracking component has an `IHttpContextAccessor` configured. Implemented explicitly on all 25+ tracking components. Shown in the diagnostic report's Tracking Components table.
- **`AtlasDataApiTrackingMessageHandlerOptions.HttpContextAccessor`**: New property for setting `IHttpContextAccessor` on the options object (matching CosmosDB, CloudStorage, and other extensions).
- **`BigQueryTrackingMessageHandlerOptions.HttpContextAccessor`**: Same as above for BigQuery.
- **`UnmatchedClientNameRegistry`**: Static registry that records `clientName` values passed to `TestTrackingMessageHandler` that didn't match any `ClientNamesToServiceNames` key. The diagnostic report reads this to surface configuration mismatches.
- **Diagnostic report: Unmatched HTTP Client Names section**: When `DiagnosticMode=true`, shows all unmatched client names with request counts and a fix suggestion explaining exact-match semantics and typed HttpClient naming conventions.
- **Diagnostic report: Grouped tracking components**: Components with the same `ComponentName` are now aggregated into a single row showing total invocations, instance count, and active count. Multiple instances (common with `ICollectionFixture`) are shown with an expandable `<details>` element.
- **Diagnostic report: HttpContextAccessor column**: The Tracking Components table now includes an HttpContextAccessor column showing `✓ configured`, `⚠ null`, or `—` for each component type.
- **Diagnostic report: Smart "never invoked" warnings**: Distinguishes between fully-inactive component types (likely misconfiguration) and partially-inactive types (expected with collection fixtures).

### Documentation
- **Wiki: ClientNamesToServiceNames exact match semantics** (`HTTP-Tracking-Setup.md`): Added section documenting that matching uses exact dictionary lookup, typed HttpClient naming conventions, and the new diagnostic report section.
- **Wiki: AtlasDataApi HttpContextAccessor** (`Integration-AtlasDataApi-Extension.md`): Updated setup example to show the new `HttpContextAccessor` option.
- **Wiki: BigQuery HttpContextAccessor** (`Integration-BigQuery-Extension.md`): Same as above.
- **Wiki: Diagnostic report improvements** (`Diagnostics-and-Debugging.md`): Updated TrackingComponentRegistry section to document grouped table, accessor column, smart warnings, and `HasHttpContextAccessor` interface member.

## [2.28.11] - 2026-04-30

### Added
- **`DiagramMethod` and `DiagramStatusCode` wrapper types**: Named alternatives to `OneOf<HttpMethod, string>` and `OneOf<HttpStatusCode, string>` that avoid the `CS0104` ambiguous reference error when a project also references the popular `OneOf` NuGet package. Both types inherit from the existing `OneOf<T1,T2>` and are assignment-compatible everywhere the base type is used. Use `DiagramMethod method = "Blob Upload";` instead of `Kronikol.Tracking.OneOf<HttpMethod, string> method = "Blob Upload";`.
- **`RequestResponseLogger.LogPair()` auto-resolving overload**: New overload that doesn't require `testName`/`testId` parameters — resolves test identity from an optional `testInfoFetcher` delegate, then falls back to `TestIdentityScope.Current`. Simplifies custom dependency tracking in background processing scenarios.
- **`MessageTracker.TrackSendMessage()`**: Atomic request+response tracking with standard (non-event) arrow styling. Unlike `TrackSendEvent()` which produces event-styled blue arrows, `TrackSendMessage()` produces standard arrows matching HTTP call styling. Ideal for the common `AfterPublish` handler pattern where you want to log a successful send atomically.
- **Diagnostic report: Unknown entries breakdown**: The `DiagnosticReport.html` now includes a dedicated "Unknown Entries Breakdown" section when log entries with test ID `"unknown"` are present. Groups entries by service name and method with counts and timestamp ranges, making it easy to identify which background operations (change feed processor, hosted services, etc.) are producing unattributed tracking entries.

### Documentation
- **Wiki: OneOf type ambiguity avoidance** (`Tracking-Custom-Dependencies.md`): Added section covering `DiagramMethod`/`DiagramStatusCode`, `using` aliases, and fully-qualified name patterns.
- **Wiki: Auto-resolving LogPair** (`Tracking-Custom-Dependencies.md`): Added section showing the new overload with `TestIdentityScope` fallback.
- **Wiki: IDistributedCache tracking example** (`Tracking-Custom-Dependencies.md`): Complete manual decorator example for `IDistributedCache` with hit/miss tracking and dual test-identity resolution.
- **Wiki: CosmosDB InMemoryEmulator integration** (`Integration-CosmosDB-Extension.md`): Added section covering `WrapHandler()` pattern, two-phase HttpContextAccessor setup, recommended verbosity, and fault injection visibility.
- **Wiki: When ReplaceWithTracked won't work** (`Integration-DispatchProxy-Extension.md`): Added section documenting the DI bypass limitation with detection guidance via DiagnosticReport.
- **Wiki: Multi-Host Test Architectures** (new page): Covers dual-host pattern (WebApplicationFactory + FunctionTestServer), shared InMemoryMessaging, DI ordering gotcha, cross-container tracker bridging, and shared singleton patterns.
- **Wiki: JustEat.HttpClientInterception integration** (`HTTP-Tracking-Setup.md`): Added complete `IHttpMessageHandlerBuilderFilter` recipe for combining TTD tracking with JustEat HTTP mocking.

## [2.28.10] - 2026-04-30

### Changed
- **Mobile: Diagram toggle buttons render as compact squares**: The "Sequence Diagrams", "Activity Diagrams", and "Flame Chart" buttons now have `max-width: 5.5em; text-align: center` at ≤768px, causing the two-word labels to wrap vertically into square-shaped buttons (e.g. "Sequence\nDiagrams"). Desktop layout is unaffected — the rule is entirely inside a media query.

## [2.28.9] - 2026-04-30

### Fixed
- **Gray header color lost on wrap overflow**: Reduced the header chunk size from 100 to 80 characters in `BatchGray` and `FormatFormUrlEncodedContent`. At the previous 100-char chunk size, PlantUML's `wrapWidth 800` (set by the library) would wrap lines at the pixel boundary, and the continuation text lost its `<color:gray>` color tag — rendering overflow header text in black instead of gray.

## [2.28.8] - 2026-04-30

### Fixed
- **Mobile: Details/Headers options overflowing off-screen**: The report-level "Details:" and "Headers:" toggle sections in `.toolbar-right` now wrap within the viewport on mobile. Added `flex-wrap: wrap` and `width: 100%` to `.toolbar-right`, and removed the fixed `margin-left: 1.5em` from `.headers-radio` at ≤768px so toggles flow naturally onto the next line.
- **Mobile: Scenario-level diagram toggle overflow**: The `.diagram-toggle` row (Sequence Diagrams / Activity Diagrams / Flame Chart + Details/Headers) now wraps at ≤768px. The `.diagram-toggle-spacer` that pushed Details/Headers to the far right is hidden on mobile, allowing items to flow naturally within the container.
- **Mobile: Summary chart (green circle) not centred**: Changed `.summary-chart` from `align-self: flex-start` to `align-self: center` at ≤768px so the pass/fail donut chart is horizontally centred when the header row stacks vertically.

## [2.28.7] - 2026-04-30

### Fixed
- **Activity diagram loading text duplication**: Fixed bug where activity diagrams displayed both "Rendering Diagrams..." (from CSS `::before` pseudo-element) and "Loading..." (from inner div text) simultaneously. Removed redundant inner text from plantuml-browser divs in `InternalFlowHtmlGenerator` and `ComponentDiagramReportGenerator`.

## [2.28.6] - 2026-04-30

### Added
- **`TestIdentityScope` for background thread tracking**: New `AsyncLocal`-based ambient scope that propagates test identity into background threads, hosted services, change-feed subscribers, and other code paths where neither `HttpContext` nor the test framework's `TestContext` is available. Use `TestIdentityScope.Begin(testName, testId)` to wrap background processing that is logically part of a test. All tracking extensions now check `TestIdentityScope.Current` as a third-level fallback after HTTP headers and `CurrentTestInfoFetcher`. Resolution order: HTTP headers → delegate → `TestIdentityScope`.
- **`TestInfoResolver` triple-resolution**: Both `Resolve()` overloads and `MessageTracker.GetTestInfo()` now fall back to `TestIdentityScope.Current` when the delegate returns null or throws, before returning null (which causes tracking to be silently skipped).

### Documentation
- **CosmosDB Extension wiki**: Added deferred `HttpContextAccessor` assignment pattern for `WrapHandler` scenarios where DI doesn't exist at fixture construction time.
- **Tracking Custom Dependencies wiki**: Added "Tracking Background Processing with TestIdentityScope" section with resolution order table, usage examples, nesting behavior, and AsyncLocal propagation notes. Added "Suppressing Tracking During Fixture Setup" section showing `TestPhaseContext.Current = TestPhase.Setup` combined with `TrackDuringSetup = false`.

## [2.28.5] - 2026-04-30

### Added
- **Mobile-responsive HTML reports**: `TestRunReport.html` and `Specifications.html` now adapt to mobile and tablet viewports without any visible change to the existing desktop layout. Added `<!DOCTYPE html>`, `<meta charset="utf-8">`, and `<meta name="viewport">` to the HTML template. Two CSS `@media` breakpoints (768px and 480px) stack the header row, toolbar, and filter rows vertically, shrink the summary chart, make wide tables horizontally scrollable, and reduce button/badge font sizes on small screens. The jump-to-failure FAB remains accessible on mobile.
- **10 new Selenium tests** (`MobileResponsiveTests`) verifying viewport meta tag presence, vertical stacking of header/toolbar/filters at 375px width, no horizontal page overflow, table scroll behavior, filter box full-width, jump-to-failure visibility, and correct restoration of row layout at 1920px desktop width.

## [2.28.4] - 2026-04-30

### Changed
- **Selenium DiagramNoteTests split into 4 parallel classes**: The monolithic `DiagramNoteTests` class (59 tests) has been split into `DiagramNoteBasicTests` (16 tests), `DiagramNoteLongNoteTests` (15 tests), `DiagramNotePartitionTests` (12 tests), and `DiagramNoteSplitTests` (16 tests), all extending a shared `DiagramNoteTestBase` base class. Each class gets its own `IClassFixture<ChromeFixture>` instance, enabling xUnit to run them in parallel with 4 Chrome browsers instead of 1. Reduces Selenium wall-clock time from ~2m37s to ~37s locally.

## [2.28.3] - 2026-04-30

### Fixed
- **Selenium `StaleElementReferenceException` in DiagramNoteTests**: `GetSvgHtml()`, `DoubleClickFirstNoteAndWait()`, `ClickNoteButton()`, `ClickDownArrowAndWait()`, and `Long_note_up_arrow_click_goes_to_truncated` now use retry-based `WebDriverWait` loops that catch `StaleElementReferenceException` when the SVG DOM is replaced between element lookup and attribute/interaction. `ClickNoteButton()` and `ClickDownArrowAndWait()` also use `HoverNoteRect(0)` (existing retry helper) instead of raw `FindElement` + `MoveToElement`, and JS-dispatched clicks instead of native `.Click()` to avoid SVG path element interception.

## [2.28.2] - 2026-04-30

### Added
- **Comprehensive XML documentation**: Added `/// <summary>` XML doc comments to every public type across all 49 packages — core library, 25 extension packages, and 15 framework adapter packages. This enables full IntelliSense support for NuGet consumers.
- **XML documentation file generation**: Enabled `<GenerateDocumentationFile>` in `Directory.Build.props` for all src projects, so `.xml` doc files are included in NuGet packages automatically.

## [2.28.1] - 2026-04-29

### Deprecated
- **`CallingServiceName` → `CallerName`**: The `CallingServiceName` property on all 29 options classes (`TestTrackingMessageHandlerOptions`, `MessageTrackerOptions`, `TrackingProxyOptions`, `MediatorTrackingOptions`, `SqlTrackingOptionsBase`, and all 24 extension options) has been deprecated with an `[Obsolete]` attribute. Use `CallerName` instead — it is functionally identical. The deprecated property proxies to `CallerName` via `get => CallerName; set => CallerName = value;`, so existing code continues to work with a compile-time `CS0618` warning. `CallingServiceName` will be removed in a future major version.

### Changed
- All internal code, tests, examples, and documentation now use `CallerName` exclusively.
- All 49 wiki pages updated to reference `CallerName`.

## [2.28.0] - 2026-04-28

### Added
- **Direct database tracking extensions for 5 popular databases**: New NuGet packages providing first-class SQL operation tracking without depending on EF Core or Dapper:
  - **`Kronikol.Extensions.Npgsql`** — PostgreSQL tracking via Npgsql's built-in `DiagnosticSource` instrumentation. Subscribes to the `"Npgsql"` diagnostic listener and correlates `BeforeExecuteCommand`/`AfterExecuteCommand` events.
  - **`Kronikol.Extensions.SqlClient`** — SQL Server tracking via `Microsoft.Data.SqlClient`'s `DiagnosticSource`. Subscribes to `"SqlClientDiagnosticListener"` and handles both `WriteCommand*` and legacy `Execute*` event patterns.
  - **`Kronikol.Extensions.MySqlConnector`** — MySQL tracking via MySqlConnector's `DiagnosticSource`. Subscribes to the `"MySqlConnector"` diagnostic listener.
  - **`Kronikol.Extensions.Sqlite`** — SQLite tracking via `DbConnection` wrapping decorator pattern (no `DiagnosticSource` available). Intercepts all 6 execution paths (ExecuteReader/NonQuery/Scalar × sync/async), plus transaction begin/commit/rollback.
  - **`Kronikol.Extensions.Oracle`** — Oracle tracking via `DbConnection` wrapping decorator pattern (no `DiagnosticSource` available). Same 6-method interception + transaction tracking as SQLite.
- **`UnifiedSqlClassifier`** in core package: Shared SQL operation parser supporting all major dialects (SQL Server brackets, PostgreSQL/MySQL quotes, MySQL backticks, Spanner hints). Classifies 16 operation types including DDL, upserts (5 patterns), stored procedures, and transactions.
- **`SqlDiagnosticTracker` base class** in core package: Abstract tracker with command correlation (ConcurrentDictionary-based), phase-aware tracking, test info resolution, and variant attachment. Shared by all 5 database extensions.
- **`SqlTrackingOptionsBase`** in core package: Common configuration for all SQL trackers (service name, verbosity, parameter logging, phase-aware setup/action tracking, excluded operations).
- **DI integration**: Each DiagnosticSource extension provides `AddXxxTestTracking(options?)` for dependency injection. Each wrapping extension provides `DecorateAll<DbConnection>` with type-check guards. All extensions also support static `EnsureTracking()` or `connection.WithTestTracking()` for non-DI usage.
- **`DependencyPalette`**: Added category mappings for `"PostgreSQL"`, `"SqlServer"`, `"MySQL"`, `"SQLite"`, and `"Oracle"` — all resolve to the `Database` participant shape.

### Changed
- **`SqlOperationClassifier` (EfCore.Relational)**: Refactored to delegate to `UnifiedSqlClassifier` internally. Same public API, no breaking changes. Benefits from unified improvements (e.g., CALL proc name parenthesis stripping).
- **`DapperOperationClassifier`**: Refactored to delegate to `UnifiedSqlClassifier` internally. Same public API, no breaking changes. Now correctly classifies `COMMIT` and `ROLLBACK` operations (previously returned `Other`). Stored procedures invoked via `EXEC` now populate `TableName` with the proc name.
- **`UnifiedSqlClassifier.ExtractProcName`**: Now strips parenthesised arguments from CALL syntax (e.g., `CALL my_proc(1)` → `my_proc`).

## [2.27.20] - 2026-04-28

### Fixed
- **`TrackConsumeEvent()` payload note placement**: The request payload note was rendered on the left (`note left`), which placed it outside the diagram when the broker was the leftmost participant. Consume event notes now render on the right (`note right`), correctly placing the payload between the broker and consumer participants. A new `NoteOnRight` property on `RequestResponseLog` controls this; `TrackConsumeEvent` sets it automatically.
- **`IsCurrentRequestFromMyHost()` fails for keyed/manually constructed trackers**: The method resolved `MessageTracker` from `HttpContext.RequestServices` via `GetService(typeof(MessageTracker))`, which returned `null` for keyed singletons (`AddKeyedSingleton("Kafka", ...)`) and manually constructed trackers. Now compares `IHttpContextAccessor` references instead — each DI container has its own singleton accessor, so the comparison works for keyed, non-keyed, and manually constructed trackers alike.

## [2.27.19] - 2026-04-28

### Added
- **`TrackConsumeEvent()` method on `MessageTracker`**: Models message consumption (broker → consumer) as a delivery + acknowledgement pair. Arrow direction is `CallingServiceName` → `consumerName`, with the payload on the delivery arrow and a customisable ack label (default: `"Ack"`). This is the consumption counterpart to `TrackSendEvent()`.
- **`CallerDependencyCategory` property on `MessageTrackerOptions`**: Controls the PlantUML participant shape of the `CallingServiceName` participant independently of `DependencyCategory`. For consumption scenarios, set `CallerDependencyCategory = "MessageQueue"` so the broker renders as a `queue` without affecting the SUT's shape.
- **`CallerDependencyCategory` field on `RequestResponseLog`**: Propagated through the rendering pipeline to `PlantUmlCreator`, enabling correct shape and colour resolution for caller participants.
- **`IsCurrentRequestFromMyHost()` method on `MessageTracker`**: Returns `true` only when the current `HttpContext` belongs to the same DI container that created the tracker. Use in multi-`WebApplicationFactory` scenarios to prevent duplicate tracking from shared in-memory message stores.
- **PlantUmlCreator caller shape rendering**: Caller participants with `CallerDependencyCategory` set are now rendered using `DependencyPalette` shapes and colours, rather than always defaulting to `actor`/`entity`. Arrow colours also fall back to the caller's category when the service has no category.
- **Comprehensive wiki documentation**: DependencyCategory reference table with all 18+ recognised values, participant naming rules, arrow direction conventions, `TrackConsumeEvent` usage guide, and cross-host duplicate guard pattern.

## [2.27.18] - 2026-04-28

### Fixed
- **Empty report overwrite during xUnit v3 test discovery**: `dotnet test` launches the test host twice — once for discovery, once for execution. Both invoke `ITestPipelineStartup.StartAsync`/`StopAsync`, and the discovery pass collected zero features, causing `CreateStandardReportsWithDiagrams` to overwrite the existing `TestRunReport.html` with a structurally complete but empty report (filters and buttons visible, but no scenarios). The report now skips generation entirely when there are zero scenarios across all features, preserving the previous report until the execution pass writes the real one.

## [2.27.17] - 2026-04-28

### Added
- **Deterministic scenario stable IDs** (`ScenarioStableId.Compute()`): Each scenario in the JSON, XML, and YAML report output now includes a `stableId` field — a deterministic 16-character hex identifier derived from the feature name, scenario display name, and outline ID (for parameterized scenarios). Unlike the runtime `id` (which varies by test framework and can be random per run), the `stableId` is consistent across runs, making it suitable for cross-run matching (e.g., flaky test detection, trend tracking).
- **Updated report schemas**: JSON Schema and XSD both include the new `stableId` field as a required property on scenarios.

## [2.27.16] - 2026-04-28

### Added
- **New package: `Kronikol.Extensions.Kafka.BuildInterception`** — Automatically intercepts `ConsumerBuilder<TKey,TValue>.Build()` and `ProducerBuilder<TKey,TValue>.Build()` via [Harmony](https://github.com/pardeike/Harmony) runtime patching, wrapping the result with `TrackingKafkaConsumer` / `TrackingKafkaProducer` when tracking is enabled. This enables **zero production code changes** — no `.BuildTracked()`, no package reference in the API project, no DI changes. The Harmony dependency is isolated in the addon package.
- **`KafkaBuildInterceptor.EnableConsumerTracking<TKey,TValue>()`** — Enables consumer tracking and patches `ConsumerBuilder<TKey,TValue>.Build()` in a single call.
- **`KafkaBuildInterceptor.EnableProducerTracking<TKey,TValue>()`** — Enables producer tracking and patches `ProducerBuilder<TKey,TValue>.Build()` in a single call.
- **`KafkaBuildInterceptor.EnableTracking<TKey,TValue>()`** — Convenience method that enables both consumer and producer tracking and patches both `Build()` methods.
- **`KafkaBuildInterceptor.DisableConsumerTracking<TKey,TValue>()`** / **`DisableProducerTracking<TKey,TValue>()`** — Disables tracking for a specific type pair (Harmony patch remains but becomes a no-op).
- **`KafkaBuildInterceptor.Reset()`** — Clears all tracking state and removes all Harmony patches.

## [2.27.15] - 2026-04-28

### Fixed
- **Flaky MessageTracker test**: `TrackSendEvent_does_nothing_when_no_test_info` no longer races with parallel tests — replaced global `RequestAndResponseLogs.Length` assertion with ID-snapshot comparison.
- **Selenium StaleElementReferenceException**: `Short_note_no_up_arrow_when_expanded`, `Scenario_truncation_change_respected_by_note_buttons`, and `Reducing_truncation_makes_short_note_become_long` now use a retry-based `HoverNoteRect` helper that re-queries elements after SVG re-renders.
- **Selenium assertion failure**: `Long_note_dblclick_from_collapsed_goes_to_truncated_not_expanded` now waits for both plus buttons to appear before asserting count.

## [2.27.14] - 2026-04-28

### Added
- **Redis DI extension** (`AddRedisTestTracking()`): Decorates all `IDatabase` registrations with `RedisTrackingDatabase` via `DecorateAll<IDatabase>`, enabling zero-prod-change Redis tracking through `ConfigureTestServices`.
- **Dapper DI extension** (`AddDapperTestTracking()`): Decorates all `DbConnection` registrations with `TrackingDbConnection` via `DecorateAll<DbConnection>`, enabling zero-prod-change SQL tracking through `ConfigureTestServices`.
- **EventHubs DI extensions** (`AddEventHubsProducerTestTracking()`, `AddEventHubsConsumerTestTracking()`, `AddEventHubsTestTracking()`): Decorates `EventHubProducerClient` and `EventHubConsumerClient` registrations for zero-prod-change tracking.
- **ServiceBus `Action<>` overload** (`AddServiceBusTestTracking(Action<ServiceBusTrackingOptions>?)`): New configuration overload consistent with other extensions. The existing `ServiceBusTrackingOptions` parameter overload is preserved.

### Changed
- **ServiceBus tracking types now inherit from SDK classes**: `TrackingServiceBusClient : ServiceBusClient`, `TrackingServiceBusSender : ServiceBusSender`, `TrackingServiceBusReceiver : ServiceBusReceiver`. This enables `DecorateAll<ServiceBusClient>` to work transparently — production code typed as `ServiceBusClient` receives the tracking subclass without any code changes.
- **EventHubs tracking types now inherit from SDK classes**: `TrackingEventHubProducerClient : EventHubProducerClient`, `TrackingEventHubConsumerClient : EventHubConsumerClient`. Non-virtual properties (`EventHubName`, `FullyQualifiedNamespace`, `IsClosed`) are shadowed with `new`.
- **PubSub tracking types now inherit from SDK classes**: `TrackingPublisherClient : PublisherClient`, `TrackingSubscriberClient : SubscriberClient`. This enables `DecorateAll<PublisherClient>` / `DecorateAll<SubscriberClient>` in the updated `AddPubSubTestTracking()`.
- **ServiceBus DI extension refactored**: Uses `DecorateAll<ServiceBusClient>` instead of manual descriptor replacement. Preserves original service lifetime. The `ServiceBusClient` registration is now decorated in-place rather than replaced with a separate `TrackingServiceBusClient` registration.
- **PubSub DI extension enhanced**: `AddPubSubTestTracking()` now decorates `PublisherClient` and `SubscriberClient` registrations in addition to registering the `PubSubTracker` singleton.
- **PubSub options**: Added `IHttpContextAccessor` property to `PubSubTrackingOptions` for consistency with other extension options.

## [2.27.13] - 2026-04-28

### Changed
- **Selenium tests: Shared ChromeDriver via IClassFixture** — All 19 Selenium test classes now share a Chrome browser instance at the class level using `IClassFixture<ChromeFixture>` / `IClassFixture<ChromeFixture1280X900>`, reducing Chrome process launches from ~207 (one per test) to ~19 (one per class). This lowers memory pressure and eliminates redundant browser startup/shutdown overhead.

## [2.27.12] - 2026-04-28

### Fixed
- **CI: Release workflow "No space left on device"**: Freed additional disk space (Swift, GraalVM, PowerShell, hostedtoolcache, Docker images) and changed build/pack to target only `src/` projects instead of the full 77-project solution. Test projects are no longer built during release — only the single core test project is built implicitly by `dotnet test`.

## [2.27.11] - 2026-04-28

### Fixed
- **Eliminated all CI build warnings**: Fixed ~80 compiler and analyzer warnings across the solution, including nullability annotations (`CS8600`–`CS8625`), obsolete API usage (`CS0618`), xUnit analyzer rules (`xUnit2013`, `xUnit2017`, `xUnit2018`, `xUnit1051`), and unused field warnings (`CS0414`). No functional changes.

## [2.27.10] - 2026-04-28

### Fixed
- **`CurrentTestInfo.Fetcher` no longer throws `NullReferenceException` outside test context**: All 8 framework `CurrentTestInfo.Fetcher` implementations (xUnit3, xUnit2, NUnit4, TUnit, MSTest, LightBDD, ReqNRoll, BDDfy) now return `("Unknown", "unknown")` when the test context is unavailable (e.g. during hosted service processing, background threads, Service Bus message handlers). Previously, the delegates accessed the test context without null checks, causing `NullReferenceException` when invoked outside of test execution.
- **`MessageTracker.GetTestInfo()` now catches delegate exceptions**: `MessageTracker` was the only tracking component that invoked `CurrentTestInfoFetcher` without exception handling. All other extensions route through `TestInfoResolver.Resolve()`, which wraps the call in a try-catch. `MessageTracker.GetTestInfo()` now matches this behaviour — a throwing delegate returns `null` (tracking silently skipped) instead of propagating the exception to callers.
- **NUnit4: `TestContextEnumerableExtensions` build error on `IEnumerable<ParameterInfo>.Length`**: Fixed pre-existing compilation error caused by NUnit 4.5.1's `IMethodInfo.GetParameters()` returning `IEnumerable<ParameterInfo>` (which lacks a `Length` property). The result is now materialised to an array before pattern matching.

## [2.27.9] - 2026-04-28

### Added
- **Kafka: Static interceptor for internally-built consumers/producers** (`KafkaTrackingInterceptor`): Enables tracking for consumers and producers built via `new ConsumerBuilder<TKey,TValue>(...).Build()` inside `BackgroundService` or other non-DI code paths. Use `EnableConsumerTracking<TKey,TValue>()` / `EnableProducerTracking<TKey,TValue>()` in test setup, then replace `.Build()` with `.BuildTracked()` in production code (one-token change, no-op when not in test context). Also provides `.Tracked()` extension on existing `IConsumer` / `IProducer` instances.
- **Kafka: Consumer and producer factory interfaces**: `IKafkaConsumerFactory<TKey,TValue>` and `IKafkaProducerFactory<TKey,TValue>` with default implementations (`KafkaConsumerFactory`, `KafkaProducerFactory`) and tracking decorators (`TrackingKafkaConsumerFactory`, `TrackingKafkaProducerFactory`). Inject the factory in services that build consumers/producers internally for clean DI-based tracking.
- **`AddKafkaConsumerFactoryTestTracking<TKey,TValue>()`**: DI extension that registers a default consumer factory (if none exists) and decorates it with tracking.
- **`AddKafkaProducerFactoryTestTracking<TKey,TValue>()`**: DI extension that registers a default producer factory (if none exists) and decorates it with tracking.
- **`BuildTracked()` extension** on `ConsumerBuilder<TKey,TValue>` and `ProducerBuilder<TKey,TValue>` — builds and wraps with tracking if the static interceptor is active.
- **`Tracked()` extension** on `IConsumer<TKey,TValue>` and `IProducer<TKey,TValue>` — wraps an existing instance with tracking if the static interceptor is active. Prevents double-wrapping.

## [2.27.8] - 2026-04-28

### Added
- **Kafka: Transactional producer tracking**: All five transactional producer methods (`InitTransactions`, `BeginTransaction`, `CommitTransaction`, `AbortTransaction`, `SendOffsetsToTransaction`) are now tracked when `TrackTransactions = true`. Each has its own `KafkaOperation` enum value and classifier labels (full names in Detailed/Raw, shortened `Init Txn`/`Begin Txn`/`Commit Txn`/`Abort Txn`/`Send Offsets` in Summarised).
- **`TrackTransactions`** option on `KafkaTrackingOptions` (default `false`) — single flag to enable/disable tracking of all transactional producer operations.
- **`LogTransaction()`** method on `KafkaTracker`.
- **5 new `KafkaOperation` enum values**: `InitTransactions`, `BeginTransaction`, `CommitTransaction`, `AbortTransaction`, `SendOffsetsToTransaction`.

## [2.27.7] - 2026-04-28

### Fixed
- **Kafka: Consumer `Commit()` now tracked** (all 3 overloads): Previously, `KafkaOperation.Commit` was defined in the enum and the tracker had a `LogCommit()` method, but `TrackingKafkaConsumer` never called it. All three `Commit()` overloads now log when `TrackCommit = true`.
- **Kafka: Consumer `Unsubscribe()` now tracked**: Previously just delegated without logging. Now logs when `TrackUnsubscribe = true`.
- **Kafka: Producer `Flush()` now tracked** (both overloads): Previously just delegated without logging. Now logs when `TrackFlush = true`.
- **Kafka: Consumer now logs message Key**: Previously, `TrackingKafkaConsumer` only logged the message Value. Now uses the same `BuildContent` pattern as the producer, logging both Key and Value (controlled by `LogMessageKey` / `LogMessageValue`). Content is also correctly suppressed in Summarised mode.
- **CI: Split IKVM tests into own matrix job with disk cleanup**: The PlantUml.Ikvm test project copies hundreds of native runtime files per platform, exhausting disk space on GitHub Actions runners. IKVM tests now run in a dedicated job with pre-build cleanup of unused SDK/tooling directories.

### Added
- **`TrackFlush`** option on `KafkaTrackingOptions` (default `false`) — enables tracking of `IProducer.Flush()` calls.
- **`TrackUnsubscribe`** option on `KafkaTrackingOptions` (default `false`) — enables tracking of `IConsumer.Unsubscribe()` calls.
- **`LogFlush()`** and **`LogUnsubscribe()`** methods on `KafkaTracker`.

## [2.27.6] - 2026-04-28

### Fixed
- **Spanner: Raw and Detailed verbosity now produce different content**: Previously, both Raw and Detailed verbosity levels showed identical SQL text in the content body (and in phase variants). Raw now always includes parameter values when parameters exist, regardless of the `LogParameters` setting. The `LogParameters` option is now marked `[Obsolete]`.
- **Spanner: Phase variant content now correctly differentiates by verbosity level**: When using `SetupVerbosity`/`ActionVerbosity` overrides, the variant content now uses the appropriate content for each verbosity level (Raw includes parameters, Detailed shows plain SQL, Summarised omits content).

## [2.27.5] - 2026-04-27

### Fixed
- **Phase-aware verbosity overrides (`SetupVerbosity`/`ActionVerbosity`) now work for non-BDD test frameworks** (fixes #23): When the test phase is `Unknown` at capture time (i.e. no BDD framework sets the phase automatically) and verbosity overrides are configured, all extension trackers now pre-compute both Setup and Action rendering variants (`PhaseVariant`). The PlantUML renderer selects the correct variant based on `IsActionStart` marker position, so `SetupVerbosity = Summarised` / `ActionVerbosity = Detailed` (or any combination) works without requiring `StartSetup()`.

### Added
- **`PhaseVariant` record type** on `RequestResponseLog` — holds pre-computed `Method`, `Uri`, `Content`, `Headers`, and `Skip` for a specific verbosity level, allowing the renderer to pick the right variant per phase.
- **`PhaseVariantExtensions.AttachVariants<T>()` / `WithVariants<T>()`** — shared generic helper that all extension trackers use to attach variants when phase is `Unknown` and overrides are configured. Avoids duplicating variant logic across 24 extensions.
- **`StartSetup()` on all 8 framework `TrackingDiagramOverride` wrappers** (xUnit3, xUnit2, NUnit4, MSTest, TUnit, LightBDD, ReqNRoll, BDDfy) — delegates to `DefaultTrackingDiagramOverride.StartSetup()`. Users who prefer explicit phase boundaries can now call `StartSetup()` before setup code, though it is not required for verbosity overrides to work.

## [2.27.4] - 2026-04-27

### Fixed
- **Spanner and Bigtable services render as `participant` instead of `database` shape in diagrams**: Changed `DependencyCategory` from generic `"Database"` to `"Spanner"` and `"Bigtable"` respectively, and added both (plus generic `"Database"` fallback) to `DependencyPalette.CategoryToType`. These services now correctly render with the `database` shape and red color in sequence diagrams.

## [2.27.3] - 2026-04-27

### Fixed
- **Spanner gRPC interceptor: test identity not resolved in WebApplicationFactory scenarios**: Added `IHttpContextAccessor` overload to `SpannerConnectionExtensions.WithTestTracking()` so the interceptor can read test identity from HTTP request headers (propagated by `TestTrackingMessageHandler`) instead of relying solely on `AsyncLocal`, which does not propagate through the TestServer's request pipeline.

## [2.27.2] - 2026-04-27

### Fixed
- **Flaky `PendingRequestResponseLogsTests` under parallel execution**: Added missing `[CollectionDefinition("PendingLogs")]` so the three test classes sharing the `"PendingLogs"` collection are properly serialized by xUnit. Without this, xUnit silently ignored the `[Collection]` attribute.
- **`FlushAll_with_no_pending_entries_is_noop`**: Replaced assertion on total `RequestAndResponseLogs.Length` with testId-filtered assertion, eliminating race condition with concurrent test projects.
- **`AtlasDataApiTrackingMessageHandlerTests`**: Replaced `RequestResponseLogger.Clear()` with per-test `_testId` filtering. Tests no longer wipe the shared static log queue or assert on unfiltered total counts.
- **`TrackingDbCommandTests`**: Same fix — replaced `Clear()` and unfiltered `[0]`/`[1]` indexing with `GetLogsForTest()` filtered by unique `_testId`.
- **`MongoDbTrackingSubscriberTests`**: Replaced `Assert.Empty(RequestResponseLogger.RequestAndResponseLogs)` with testId-filtered assertion in `NoLogging_WhenCurrentTestInfoFetcherIsNull`.
- **Removed `RequestResponseLogger.Clear()` from all test constructors/teardown**: `ServiceBusTrackerTests`, `TrackingDbConnectionTests`, `TrackingDbTransactionTests`, `DbConnectionExtensionsTests`, `MongoDbTrackingSubscriberTests`. The `Clear()` call is destructive to concurrent tests and unnecessary when assertions filter by testId.

## [2.27.1] - 2026-04-27

### Fixed
- **`TestTrackingMessageHandler`** — exception from `CurrentTestInfoFetcher` (e.g. during app startup when no test is active) no longer crashes the HTTP call. The request is forwarded without tracking instead.
- **`DeferredLogFlushHandler`** — same fix: a throwing fetcher no longer prevents the HTTP response from being returned. Pending log entries remain queued for the next successful invocation.
- Partial context headers (test name present but no test ID) now gracefully skip tracking instead of throwing.

## [2.27.0] - 2025-07-25

### Added
- **`SpannerTrackingInterceptor`** — new gRPC interceptor that captures **all** Spanner operations at the transport layer, including Spanner-specific methods (`CreateInsertCommand`, `CreateSelectCommand`, `CreateInsertOrUpdateCommand`, etc.) that bypass ADO.NET wrapping. Extracts SQL text, table names, and mutation details from protobuf messages.
- **`SpannerConnectionStringBuilder.WithTestTracking()` extension** — configures gRPC interception via `SessionPoolManager.CreateWithSettings()` with `SpannerSettings.Interceptor`. Zero production code changes required.
- **`SpannerTracker.CreateServerObservers()`** — returns delegate tuple `(Action<string, IMessage, DateTimeOffset>, Action<string, IMessage, IMessage?, TimeSpan, StatusCode?, DateTimeOffset>)` for wiring to `Spanner.InMemoryEmulator`'s `FakeSpannerServer.OnRequestReceived` / `OnResponseSent` callbacks. Enables server-side observation as an alternative to client-side gRPC interception.

### Changed
- **Spanner extension now depends on `Google.Cloud.Spanner.V1` (5.\*) and `Grpc.Core.Api` (2.\*)** for protobuf type extraction in `SpannerTrackingInterceptor` and `CreateServerObservers()`.

### Documentation
- **Spanner wiki page**: Added Option D (gRPC Interception — recommended) and Option E (Server-Side Observation) setup guides with architecture diagrams, comparison tables, and "What Gets Captured" reference. Added warnings to Options A/B/C about limitations. Updated See Also with gRPC Extension and Phase-Aware Tracking links.

## [2.26.3] - 2025-07-24

### Added
- **`HttpContextAccessor` property on all extension options classes**: Every tracking extension options class now exposes `IHttpContextAccessor? HttpContextAccessor`. When set, the tracker reads it automatically via `?? options.HttpContextAccessor`, providing an alternative to the constructor parameter for dual-resolution test identity. This applies to: `TestTrackingMessageHandlerOptions`, `ServiceBusTrackingOptions`, `MassTransitTrackingOptions`, `ElasticsearchTrackingOptions`, `RedisTrackingDatabaseOptions`, `DapperTrackingOptions`, `EventHubsTrackingOptions`, `BlobTrackingMessageHandlerOptions`, `CloudStorageTrackingMessageHandlerOptions`, `CosmosTrackingMessageHandlerOptions`, `DynamoDbTrackingMessageHandlerOptions`, `EventBridgeTrackingMessageHandlerOptions`, `S3TrackingMessageHandlerOptions`, `SnsTrackingMessageHandlerOptions`, `SqsTrackingMessageHandlerOptions`, `StorageQueueTrackingMessageHandlerOptions`.
- **Auto-resolution of `IHttpContextAccessor` from DI**: DI extensions and convenience methods now auto-resolve `IHttpContextAccessor` from the service provider when available, eliminating the need for manual `httpContextAccessor: sp.GetRequiredService<IHttpContextAccessor>()` wiring:
  - `CreateTestTrackingClient()` (both overloads) — resolves from `factory.Services`
  - `AddServiceBusTestTracking()` — resolves from `IServiceProvider` in the factory lambda
  - `ReplaceWithTrackingProxy` simplified overload — accepts optional `IHttpContextAccessor?` parameter
  - All `DelegatingHandler`-based extensions (BlobStorage, CloudStorage, CosmosDB, DynamoDB, EventBridge, S3, SNS, SQS, StorageQueues) — convenience methods pass `options.HttpContextAccessor` to handler constructors
  - MassTransit, Elasticsearch, Redis, Dapper, EventHubs — convenience methods pass `options.HttpContextAccessor` to tracker constructors

### Documentation
- **All 16 extension wiki pages**: Added `HttpContextAccessor` row to options tables and updated dual-resolution notes with v2.26.3 auto-resolution info.
- **HTTP Tracking Setup wiki**: Rewrote "How to Use It" section with simplified examples showing auto-resolution. Replaced "MediatR Auto-Resolution" with broader "Auto-Resolution (v2.26.2+)" section covering all extensions. Updated extensions table with auto-resolution version info.
- **Diagnostics and Debugging wiki**: Added new "Other dependencies not appearing in per-test reports" section generalizing the gRPC troubleshooting pattern to all extensions.

## [2.26.2] - 2026-04-27

### Added
- **`CurrentTestInfo` static class in every framework package**: Each framework adapter package now provides a `static class CurrentTestInfo` with a get-only `Fetcher` property (`Func<(string Name, string Id)>`). This provides a uniform, discoverable API for setting `CurrentTestInfoFetcher` on any tracking options class — the syntax is identical regardless of framework:
  ```csharp
  CurrentTestInfoFetcher = CurrentTestInfo.Fetcher
  ```
  Available in: `Kronikol.xUnit3`, `Kronikol.xUnit2`, `Kronikol.NUnit4`, `Kronikol.MSTest`, `Kronikol.TUnit`, `Kronikol.LightBDD` (Core/xUnit2/xUnit3/TUnit), `Kronikol.ReqNRoll` (Core/xUnit2/xUnit3/TUnit), `Kronikol.BDDfy.xUnit3`.
- **`XUnit2TestTrackingMessageHandlerOptions.TestInfoFetcher`**: xUnit v2 options class now exposes a static `TestInfoFetcher` field (previously the delegate was only set inline in the constructor), aligning it with all other framework adapters.

### Documentation
- **All wiki pages**: Replaced verbose framework-specific `CurrentTestInfoFetcher` lambda examples with the new `CurrentTestInfo.Fetcher` syntax. Simplified "CurrentTestInfoFetcher by Framework" sections to a single code snippet plus a using-directive table.

## [2.26.1] - 2026-07-14

### Added
- **gRPC Extension — `AddTrackedGrpcClient<TClient>()` DI extension**: New `IServiceCollection` extension method that registers a singleton tracked gRPC client with `IHttpContextAccessor` auto-resolved from DI, matching the existing pattern used by BigQuery, Bigtable, MongoDB, Kafka, and other extensions. Eliminates the need for manual `HttpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>()` wiring.
- **gRPC Extension — auto-resolve `IHttpContextAccessor` in `CreateTestTrackingGrpcClient`**: Both `CreateTestTrackingGrpcClient` and `CreateTestTrackingGrpcClientWithChannel` now auto-resolve `IHttpContextAccessor` from `factory.Services` when not explicitly set on `GrpcTrackingOptions`. This ensures dual-resolution test identity works out of the box for the test → SUT direction.

## [2.26.0] - 2026-07-14

### Added
- **New `Kronikol.Extensions.AtlasDataApi` package**: MongoDB Atlas Data API extension with a `DelegatingHandler` (`AtlasDataApiTrackingMessageHandler`) that intercepts and classifies REST API operations. Supports 10 classified operations (FindOne, Find, InsertOne, InsertMany, UpdateOne, UpdateMany, DeleteOne, DeleteMany, ReplaceOne, Aggregate) with directional arrows (← read, → write, ↔ read-modify-write), three verbosity levels (Raw, Detailed, Summarised), JSON body metadata extraction (dataSource, database, collection, filter), ExcludedOperations filtering, and DI registration via `AddAtlasDataApiTestTracking()`.
- **MongoDB Extension — 11 new classified operations**: Added Change Streams (`Watch` via `$changeStream` pipeline detection), Transactions (`CommitTransaction`, `AbortTransaction`), Admin (`DropDatabase`, `ServerStatus`, `DbStats`, `CollStats`), DDL (`RenameCollection`, `ListIndexes`), and Legacy (`MapReduce`). Total classified operations: 28 (up from 17).
- **MongoDB Extension — directional arrows**: Detailed verbosity labels now include directional arrows: `←` for reads (Find, Aggregate, Count, Distinct, ListCollections, ListDatabases, ListIndexes), `→` for writes (Insert, Delete, DropIndex, DropCollection, DropDatabase, RenameCollection), `↔` for read-modify-write (FindAndModify, Update, BulkWrite). Schema and admin operations show no arrow.
- **MongoDB Extension — enriched metadata**: `MongoDbOperationInfo` now includes `DocumentCount` (from insert arrays), `DocumentId` (from filter `_id`), `PipelineStages` (from aggregate pipelines), and `IsGridFs` (from `fs.files`/`fs.chunks` collections). Detailed labels show enriched info like `Insert (×5) → users` and `Aggregate ($match, $group) ← orders`.
- **MongoDB Extension — `ExcludedOperations`**: New `HashSet<MongoDbOperation>` option to suppress specific operations from tracking (e.g. `ServerStatus`, `ListDatabases`).
- **MongoDB Extension — `LogFilterText`**: New `bool` option (default: `true`) to control whether filter document text is included in Detailed mode request content.
- **MongoDB Extension — `AddMongoDbTestTracking()` DI extension**: New service collection extension method that registers `MongoDbTrackingSubscriber` as a singleton with `IHttpContextAccessor` auto-resolved from DI.
- **MongoDB Extension — `endSessions` ignored by default**: Added `endSessions` to the default `IgnoredCommands` set to suppress session cleanup noise.
- **MongoDB Extension — response metadata extraction**: In Detailed mode, successful command replies now extract `n`, `nModified`, and `nUpserted` counts from the BSON reply and append them to response content.

## [2.25.2] - 2026-04-27

### Fixed
- **gRPC dependency tracking not resolving test identity from HTTP context**: `GrpcTrackingInterceptor` accepts an `IHttpContextAccessor` for dual-resolution test identity (HTTP headers first, delegate fallback), but none of the public API entry points (`GrpcTrackingChannel.Create`, `AsGrpcTrackingCallInvoker`, `CreateTestTrackingGrpcClient`, `WithTestTracking`) forwarded it. When a gRPC client ran inside the SUT's request pipeline (e.g. API calling a downstream gRPC service during a test HTTP request), test identity could not be resolved from the propagated HTTP context headers — causing gRPC dependency calls to be logged with "Unknown" test identity and not appearing in per-test reports. Added `IHttpContextAccessor? HttpContextAccessor` property to `GrpcTrackingOptions`; all entry points and the interceptor constructor now read from this property. Consumers set it once on the options object (typically via `sp.GetRequiredService<IHttpContextAccessor>()`) and the interceptor automatically resolves test identity from HTTP context headers, falling back to `CurrentTestInfoFetcher` when no HTTP context is available.

## [2.25.1] - 2026-04-27

### Fixed
- **Selenium tests**: Fixed `WaitForDiagramSvg` timeout in CI by explicitly calling `_renderDiagramsInContainer` before polling for SVG — `IntersectionObserver` doesn't fire reliably in headless Chrome. Applied to all 5 affected test files (`DiagramNoteTests`, `ContextMenuExtendedTests`, `ScenarioInteractionTests`, `DiagramZoomTests`, `DependencyColoringTests`).
- **`Collapsed_note_shows_plus_button_in_top_right`**: Fixed false assertion failure by scoping button selectors to the target scenario instead of the entire page — other scenarios' minus buttons (in truncated state) were being matched.

## [2.25.0] - 2026-04-27

### Added
- **`RequestResponseLogger.MaxContentLength`**: New global static property that truncates content at capture time when set. Content exceeding the limit is trimmed to the specified character count with a `…truncated (N chars total)` marker appended. Applies to all extensions (HTTP, BigQuery, Spanner, Bigtable, Cosmos, Redis, etc.) since truncation happens in the core `Log()` method. Default is `null` (no limit). Set during test setup, e.g. `RequestResponseLogger.MaxContentLength = 2000;`

## [2.24.1] - 2026-04-27

### Added
- **`BigtableTrackingOptions.ExcludedOperations`**: Added `HashSet<BigtableOperation>` property to suppress tracking of specific Bigtable operations, matching the pattern used by Spanner, Dapper, Elasticsearch, and EventBridge extensions.

## [2.24.0] - 2026-07-14

### Added
- **New `Kronikol.Extensions.Spanner` package**: Google Cloud Spanner extension with ADO.NET connection wrapping (`TrackingSpannerConnection`, `TrackingSpannerCommand`, `TrackingSpannerTransaction`) and a direct `SpannerTracker` for gRPC-style usage. Classifies 18 Spanner operations (Query, Read, Insert, Update, Delete, InsertOrUpdate, Replace, Commit, Rollback, BeginTransaction, BatchDml, PartitionQuery, PartitionRead, Ddl, CreateSession, DeleteSession, StreamingRead, Other) with three verbosity levels (Raw, Detailed, Summarised). Includes DI registration via `AddSpannerTestTracking()` and connection extension `WithTestTracking()`.
- **New `Kronikol.Extensions.Bigtable` package**: Google Cloud Bigtable extension with a direct `BigtableTracker` implementing `ITrackingComponent`. Classifies 7 Bigtable operations (ReadRows, MutateRow, MutateRows, CheckAndMutateRow, ReadModifyWriteRow, SampleRowKeys, Other) with directional diagram labels (← for reads, → for writes) and short table name extraction from full Bigtable resource paths. Includes DI registration via `AddBigtableTestTracking()`.
- **`BigQueryTracker`**: New direct tracker class for the BigQuery extension, providing `LogRequest`/`LogResponse` pair logging without HTTP interception. Useful for scenarios where BigQuery operations are tracked at a layer above or below the HTTP pipeline.
- **`BigQueryServiceCollectionExtensions.AddBigQueryTestTracking()`**: New DI extension in the BigQuery package that registers a singleton `BigQueryTracker` with `IHttpContextAccessor` auto-resolved from DI.

## [2.23.13] - 2026-04-27

### Fixed
- **SqlTrackingInterceptor**: Fixed `UriFormatException` when `Verbosity` is `Detailed` or `Raw` and the SQL Server connection uses comma-separated port notation (e.g. `127.0.0.1,33262` from Docker containers). The `DataSource` comma is now normalised to a colon for valid URI construction. ([#22](https://github.com/lemonlion/Kronikol/issues/22))

## [2.23.12] - 2026-04-27

### Added
- **`TestInfoResolver.CreateHttpFallbackFetcher()`**: New convenience method that creates a `Func<(string Name, string Id)>` encapsulating the dual-resolution pattern (httpContext headers first, fallback delegate second). Eliminates the ~10-line boilerplate previously needed when setting `CurrentTestInfoFetcher` on tracking options.
- **`ServiceCollectionDecoratorExtensions.DecorateAll<TService>()`**: New DI helper that wraps all existing registrations of a service type with a decorator. Removes the original registration and adds the decorated version, preserving the original service lifetime. Handles all descriptor types (factory, instance, type).
- **`ServiceCollectionDecoratorExtensions.DecorateAllOpen()`**: New DI helper that scans for all closed-generic registrations matching an open generic type and replaces each with a decorator type. Additional constructor parameters are resolved from DI via `ActivatorUtilities`.
- **`KafkaServiceCollectionExtensions.AddKafkaProducerTestTracking<TKey,TValue>()`**: New DI extension in the Kafka package that decorates all `IProducer<TKey,TValue>` registrations with `TrackingKafkaProducer` for test diagram tracking. Automatically resolves `IHttpContextAccessor` from DI.
- **`KafkaServiceCollectionExtensions.AddKafkaConsumerTestTracking<TKey,TValue>()`**: Same pattern for `IConsumer<TKey,TValue>` with `TrackingKafkaConsumer`.
- **`PubSubServiceCollectionExtensions.AddPubSubTestTracking()`**: New DI extension in the PubSub package that registers a singleton `PubSubTracker` with `IHttpContextAccessor` auto-resolved from DI.

## [2.23.11] - 2026-04-27

### Added
- **GraphQL query formatting in sequence diagram notes**: GraphQL request bodies are now automatically detected and formatted with proper indentation in diagram notes, replacing the previous single-line JSON string representation. The GraphQL query is parsed and pretty-printed with brace-depth indentation, while arguments inside parentheses (e.g. HotChocolate filtering/sorting) stay inline. Four configurable display modes via `GraphQlBodyFormat` on `ReportConfigurationOptions`:
  - `Json` — Previous behaviour: JSON pretty-print with query as a single-line string value.
  - `FormattedQueryOnly` — Formatted GraphQL query only; HTTP headers and metadata are suppressed.
  - `Formatted` — Formatted GraphQL query with HTTP headers shown above.
  - `FormattedWithMetadata` (default) — Formatted GraphQL query with HTTP headers, plus `variables` and `extensions` sections rendered below.
- When `FocusFields` are in use, GraphQL formatting automatically falls back to `Json` mode so JSON field highlighting works correctly.

## [2.23.10] - 2026-04-27

### Fixed
- **gRPC Activity spans not appearing in Activity Diagrams / Flamecharts**: Fixed three issues preventing gRPC calls from producing activity spans:
  1. `InternalFlowSpanCollector.FilterByAutoInstrumentation()` excluded `"Kronikol.Grpc"` spans because the source was not in `WellKnownAutoInstrumentationSources`. Added `"Kronikol.Grpc"` and `"Grpc.Net.Client"` to the well-known list.
  2. `AsyncUnaryCall` disposed its `Activity` immediately on method return (before the async response arrived), producing near-zero-duration spans. The Activity is now kept alive and stopped in `WrapUnaryResponse` after the response is logged, so spans cover the full call duration.
  3. No trace context was propagated to the server. A `traceparent` metadata header is now injected into all outgoing gRPC calls, allowing server-side ASP.NET Core spans to share the same TraceId.

## [2.23.9] - 2026-04-27

### Fixed
- **Hover buttons not appearing on notes with Creole separator markup**: Fixed a bug where `findNoteGroups()` failed to detect note groups in SVG when PlantUML's Creole `..text..` separator syntax was used inside notes (e.g. `..Continued From Previous Diagram..`). The Creole syntax causes PlantUML to insert `<line>` elements between the note's background `<path>` and `<text>` elements. The algorithm now uses bounding-box containment to skip `<line>`, `<rect>`, and `<circle>` elements that are visually inside the note, while correctly stopping at lifeline/arrow elements between different note groups.
- **Pre-existing Selenium test fix**: Fixed `Partition_long_note_expand_click_works` test that was failing due to SVG note fold path intercepting the button click — now uses JS-dispatched click.

### Added
- **Comprehensive Selenium regression tests for split-diagram hover buttons**: Added 6 new Selenium tests for 3-diagram split scenarios with Creole continuation notes, plus 6 tests for 2-diagram split initial render. Tests cover hover rects, toggle icons, hover visibility, double-click state cycling, and state change preservation across all diagram parts within a single scenario.

## [2.23.8] - 2026-04-27

### Added
- **Activity Diagram & Flamechart support for gRPC calls**: `GrpcTrackingInterceptor` now creates `System.Diagnostics.Activity` spans around each gRPC call, populates `ActivityTraceId`, `ActivitySpanId`, and `Timestamp` on all `RequestResponseLog` entries, and lazily starts the `InternalFlowActivityListener`. This enables gRPC calls to appear in Activity Diagrams and Flamecharts alongside HTTP calls — previously they were invisible because the interceptor never created activities or set trace context on log entries.

## [2.23.7] - 2026-04-27

### Added
- **`CreateTestTrackingGrpcClient` convenience extension on `WebApplicationFactory`**: New `factory.CreateTestTrackingGrpcClient<TEntryPoint, TClient>(options)` extension method mirrors the HTTP `CreateTestTrackingClient` API for gRPC. A single call handles the `GrpcResponseVersionHandler` (HTTP/2 fix for TestServer), `GrpcChannel` creation, `GrpcTrackingInterceptor` installation, and typed gRPC client construction — making it impossible to accidentally forget tracking. Also provides a `CreateTestTrackingGrpcClientWithChannel` variant that returns the underlying `GrpcChannel` for disposal.
- **`GrpcResponseVersionHandler`**: Built-in `DelegatingHandler` that fixes the HTTP response version mismatch when testing gRPC services in-process via `TestServer`. `TestServer` returns HTTP/1.1, but gRPC requires HTTP/2 — this handler copies the request version onto the response. Previously, every test project had to implement their own `ResponseVersionHandler`; now it's included in the `Kronikol.Extensions.Grpc` package.

## [2.23.6] - 2026-04-26

### Added
- **`GrpcTrackingChannel` factory for incoming gRPC tracking**: New `GrpcTrackingChannel.Create()` and `CreateWithChannel()` static methods that create a tracked `CallInvoker` from an `HttpMessageHandler` and base address. This enables rich gRPC-aware diagrams for test-to-SUT gRPC calls — with protobuf JSON deserialization, operation classification (`UnaryCall`, `ServerStreamingCall`, etc.), `grpc://` URIs, and gRPC status code mapping — instead of raw HTTP/2 `POST` requests with binary bodies. Also provides `HttpMessageHandler.AsGrpcTrackingCallInvoker()` extension method for terser syntax.

## [2.23.5] - 2026-04-26

### Added
- **Automatic GraphQL operation detection in diagram arrows**: HTTP POST requests containing GraphQL request bodies are now automatically detected and the diagram arrow label is enriched with the operation type and name (e.g. `POST: /graphql\n(query GetUser)`, `POST: /api/data\n(mutation CreateOrder)`). Detection is purely body-based (no URL assumption) using a regex that identifies the GraphQL `"query"` JSON key and parses the operation type (`query`/`mutation`/`subscription`) and optional operation name. Anonymous shorthand queries (`{ user { name } }`) are labelled as `(query)`. The `operationName` JSON field is respected when present. No configuration or extra packages required — works automatically for all HTTP-tracked GraphQL traffic.

## [2.23.4] - 2026-04-26

### Added
- **Static `TestInfoFetcher` property on all framework adapter options classes**: `XUnitTestTrackingMessageHandlerOptions`, `NUnitTestTrackingMessageHandlerOptions`, `TUnitTestTrackingMessageHandlerOptions`, `MSTestTestTrackingMessageHandlerOptions`, `BDDfyTestTrackingMessageHandlerOptions`, `LightBddTestTrackingMessageHandlerOptions`, and `ReqNRollTestTrackingMessageHandlerOptions` now expose a `public static readonly Func<(string Name, string Id)> TestInfoFetcher` property. Extension options (e.g. `SqlTrackingInterceptorOptions`, `CosmosTrackingMessageHandlerOptions`) can reference this directly instead of writing verbose inline lambdas with null guards.

## [2.23.3] - 2026-04-26

### Fixed
- **Failure cluster links not scrolling to second scenario**: Clicking a failure cluster link after previously clicking another would change the URL hash but not scroll to the target scenario. The onclick handler now explicitly calls `scrollIntoView` and `preventDefault` instead of relying on native anchor navigation, which fails when `<details>` elements are dynamically opened.

## [2.23.2] - 2026-04-26

### Fixed
- **Aligned scenario-steps and example-diagrams borders in parameterized groups**: The steps panel and diagrams panel now have matching left borders in parameterized/multi-parameter scenarios. Previously, the steps panel was indented 1em further right due to its `.param-detail-panels` wrapper having `margin-left: 1em`.

## [2.23.1] - 2026-04-26

### Fixed
- **Search now includes feature names, descriptions, labels, and tags**: The report search bar now indexes feature display names, feature descriptions, feature labels, scenario categories, and scenario labels as plain text in the `data-search` attribute. Previously, searching for a feature name like "Pancakes Creation" would return no results. This applies to both regular scenarios and parameterized groups.

## [2.23.0] - 2026-04-26

### Added
- **Dual-resolution test identity across all extensions**: All 22 tracking extensions now support resolving test identity from HTTP request headers (propagated by `TestTrackingMessageHandler`) in addition to the existing `CurrentTestInfoFetcher` delegate. This fixes a systemic issue where extensions running inside the SUT's request pipeline (e.g. via `WebApplicationFactory`) could not resolve the test context because the test framework's `AsyncLocal` does not flow from the test thread to the server thread.
  - New `TestInfoResolver` static helper in the core package centralises the dual-resolution logic: try HTTP headers first, fall back to delegate.
  - Every tracker/handler constructor now accepts an optional `IHttpContextAccessor? httpContextAccessor` parameter (backward-compatible; defaults to `null`).
  - `MediatorTrackingExtensions` auto-resolves `IHttpContextAccessor` from DI when creating the tracking proxy.
  - `TrackingProxyOptions` gains an `HttpContextAccessor` property for extensions using `TrackingProxy<T>` (MediatR, DispatchProxy).
  - `SqlTrackingInterceptor` refactored to use the shared `TestInfoResolver`.
  - 13 new unit tests for `TestInfoResolver` covering header precedence, delegate fallback, null/exception handling, and the nullable-tuple overload.
- **Affected extensions**: Kafka, CosmosDB, ServiceBus, EventHubs, EventBridge, SQS, SNS, MediatR, MassTransit, Redis, MongoDB, BlobStorage, S3, BigQuery, CloudStorage, StorageQueues, DynamoDB, Elasticsearch, Dapper, gRPC, DispatchProxy (via TrackingProxy), and EfCore.Relational (refactored).

## [2.22.32] - 2026-04-26

### Fixed
- **Dependency filter**: `ExtractDependencies` now matches all PlantUML participant types (`actor`, `boundary`, `control`, `entity`, `database`, `collections`, `queue`, `participant`). Previously only `entity` and `participant` were matched, so dependencies rendered as `database`, `collections`, or `queue` (e.g. Cosmos DB, Redis, ServiceBus) were silently excluded from the dependency filter buttons in the HTML report.

## [2.22.31] - 2026-04-26

### Fixed
- **DiagramNoteTests**: Fixed `StaleElementReferenceException` in `Short_note_no_up_arrow_when_expanded` by re-querying hover rects after `SetScenarioState` re-renders the SVG.

## [2.22.30] - 2026-04-26

### Fixed
- **Wiki Feature 8 (CI Summary)**: Removed `!pragma teoz true` from failure diagram source so the PlantUML server renders a compact, readable diagram inside the CI summary panel.
- **Wiki Feature 11 (DiagramFocus)**: Replaced hand-crafted PlantUML source with real TTD-generated diagram via `PlantUmlCreator.GetPlantUmlImageTagsPerTestId()` using `RequestResponseLog` entries and `FocusFields`. Response note now correctly appears on the right side (matching real TTD output).
- **Wiki Feature 12 (Failure Diagnostics)**: Fixed failure cluster expansion using `setAttribute('open', '')` instead of unreliable `click()` on `<details>` element in headless Chrome. Cluster is now visibly expanded from the start of the GIF.

## [2.22.29] - 2026-04-26

### Fixed
- **Wiki Feature 8 (CI Summary)**: Expand all `<details>` and scroll to show embedded PlantUML diagram image.
- **Wiki Feature 9 (JSON Report)**: Rewrote JSON viewer using inline style toggling; CSS sibling selectors were unreliable in headless Chrome.
- **Wiki Feature 11 (DiagramFocus)**: Changed highlighted fields from `<color:blue><b>` to `<back:#FFEB3B>` (yellow background) for much more visible highlighting.
- **Wiki Feature 12 (Failure Diagnostics)**: Removed initial overview hold so GIF starts directly at the failure cluster section.

## [2.22.28] - 2026-04-26

### Fixed
- **Wiki Feature 8 (CI Summary)**: Replaced fake header overlay with real `CiSummaryGenerator.GenerateMarkdown()` output rendered in GitHub Actions-styled page.
- **Wiki Feature 9 (JSON Report)**: Changed JSON viewer from dark theme (mostly black) to GitHub-styled light theme with visible syntax highlighting and toolbar.
- **Wiki Feature 11 (DiagramFocus)**: Set diagram Details to "Expanded" before screenshot so response notes with highlighted fields are visible.
- **Wiki Feature 12 (Failure Clustering)**: Changed test data so 4 failures share the same error message (`Connection refused (Stock Service:5001)`), enabling the `FailureClusterer` to produce a visible cluster. GIF now leads with the cluster section.

## [2.22.27] - 2026-04-25

### Fixed
- **Race condition in ParameterGrouper.Analyze**: R2 flattening mutated shared `Scenario` objects' `ExampleValues`/`ExampleRawValues` dictionaries. When `Parallel.Invoke` in `CreateStandardReportsWithDiagrams` generated multiple reports concurrently, thread B could encounter a `KeyNotFoundException` reading a key that thread A's flattening had already replaced. `Analyze` now deep-clones all scenario dictionaries at entry, so each parallel call works on independent copies.

### Added
- **TUnit end-to-end integration tests**: Added TUnit to the integration test matrix (`TestProjects.All`) with full C# attribute → TUnit adapter → report HTML pipeline verification. 8 new tests in `TUnitParameterizedRenderingTests.cs` covering R1 (scalar `[Arguments]`), R2 (flattened `[MethodDataSource]` records), R3 (sub-table for complex params), and R4 (expandable nested objects).
- **TUnit parameterized example tests**: Added `Parameterized_Feature.cs` to the TUnit example project with 10 test cases covering all 4 rendering rules using real `[Arguments]` and `[MethodDataSource]` attributes with `OrderScenario`, `ShippingAddress`, and `CustomerOrder` records.
- **TUnit `dotnet run` support**: `TestProjectRunner` now detects Microsoft.Testing.Platform projects (TUnit) and uses `dotnet run` instead of `dotnet test`, which is required on .NET 10+.
- **ReportParser `ExtractParameterizedGroupsAsync`**: New helper for asserting parameterized rendering in HTML reports, extracting scenario names, column headers, row counts, sub-table and expandable presence.

## [2.22.26] - 2026-04-25

### Added
- **Realistic PlantUML diagrams in integration tests**: `GenerateReport` helper now embeds per-scenario PlantUML sequence diagrams instead of empty strings, eliminating "Decompression error" in generated HTML reports and making them viewable in a browser.
- **TUnit R3/R4 integration tests**: Added `TUnit_scalar_plus_small_complex_object_renders_R3_subtable` and `TUnit_scalar_plus_deeply_nested_object_renders_R4_expandable` covering sub-table and expandable rendering for TUnit adapter patterns.

## [2.22.25] - 2026-04-24

### Fixed
- **Truncated record ToString() parsing**: `TryParseRecordToString` now handles records truncated by xUnit/MSTest display name limits (ending in `··...` or `...` instead of ` }`), extracting all fully-parsed properties. Previously, truncated records fell back to R0 (raw string in a single cell) instead of R2 (flattened columns).

### Added
- **Comprehensive parameterized rendering integration tests**: 29 new tests in `ParameterizedRenderingIntegrationTests.cs` covering all 8 framework adapters (xUnit2, xUnit3, TUnit, NUnit4, MSTest, LightBDD, ReqNRoll, BDDfy) with scalar, complex record, truncated record, nullable, nested, and edge case scenarios. Tests generate full HTML reports and verify correct R0/R1/R2/R3/R4 rendering.
- **5 new parser unit tests** for truncated record handling (mid-property-name, mid-value, mid-quoted-string, plain ellipsis).

## [2.22.24] - 2026-04-24

### Added
- **String-based record `ToString()` parsing for parameterized groups**: When test parameters are complex objects without raw .NET object references (e.g. display-name-only parsing), the report now parses C# record `ToString()` representations (e.g. `TypeName { Prop = Val, ... }`) and decomposes them into individual table columns (R2 flattening), sub-tables (R3), or expandable details (R4).
- **Defensive error handling**: All string-based record parsing is wrapped in try-catch with `Debug.WriteLine` warnings, ensuring malformed values gracefully fall back to plain text rendering with no cascading failures.

## [2.22.23] - 2026-04-24

### Fixed
- **DependencyCategory defaults for all trackers**: `MessageTracker` and all 15 extension trackers now set the correct `DependencyCategory` on `RequestResponseLog` entries, so PlantUML diagrams render the correct participant shapes (e.g. `queue` for message brokers, `database` for databases, `collections` for storage) instead of defaulting to `entity`.
- **MessageTrackerOptions.DependencyCategory**: New property (default `"MessageQueue"`) allowing callers to override the category used by `MessageTracker`.
- **DependencyPalette**: Added 7 new category mappings: `MessageQueue`, `MongoDB`, `DynamoDB`, `Elasticsearch`, `S3`, `CloudStorage`, `gRPC`.

## [2.22.22] - 2026-04-24

### Fixed
- **Wiki GIF tests**: Rewrote all `WikiGifTests` with correct CSS selectors, rich test data (50+ scenarios across 8 features), and proper PlantUML rendering (force-call `_renderDiagramsInContainer` for headless Chrome where `IntersectionObserver` doesn't fire).
- **GIF file sizes**: Added `-resize 960x -fuzz 2% -layers optimize` to ImageMagick stitching, reducing GIF sizes from ~280MB total to ~7.3MB total. Increased `WaitForExit` timeout from 120s to 600s to prevent truncated GIFs.
- **Feature01 (Interactive Report)**: Reduced Hold() durations to target ~34s (was ~47s). Shows 6+ filter combinations (Failed, Passed, Dependency, P95, Happy Paths, Categories).
- **Feature09 (CI Summary)**: Rewritten to use real report with JS-injected GitHub Actions header instead of broken custom HTML page.

### Changed
- **What's New In 2.0 wiki**: Removed former Feature 13 (Category Filtering) and renumbered remaining features.

## [2.22.21] - 2026-04-24

### Added
- **`MessageTrackerOptions.UseHttpContextCorrelation`**: When `true`, the `MessageTracker` reads test info from `IHttpContextAccessor` request headers first (the same dual-layer correlation used by the legacy constructor), falling back to `CurrentTestInfoFetcher` when `HttpContext` is null. This enables safe migration from the legacy constructor without losing interactions tracked via HTTP-propagated headers.
- **`TestTrackingMessageHandlerOptions.ClientNamesToServiceNames`**: Maps `IHttpClientFactory` client names to human-readable service names for diagrams. Useful when HTTP mocking makes port-based mapping via `PortsToServiceNames` unreliable. Pass the client name via `new TestTrackingMessageHandler(options, clientName: builder.Name)`. Resolution order: `FixedNameForReceivingService` > `ClientNamesToServiceNames` > `PortsToServiceNames` > `localhost:port`.

### Documentation
- **Event-Annotations wiki**: Added migration warning for `UseHttpContextCorrelation`, fixed all `CurrentTestInfoFetcher` examples to use null-safe patterns (replaced `TestContext.Current.Test!.TestDisplayName` with null-check), added `UseHttpContextCorrelation` to the options properties table.
- **HTTP-Tracking-Setup wiki**: Added `ClientNamesToServiceNames` to the `TestTrackingMessageHandlerOptions` reference table.
- **Tracking-Dependencies wiki**: Added simplified `ClientNamesToServiceNames` example alongside existing Pattern 8.
- **Generated-Reports wiki**: Added "Validating Tracking Configuration Changes" section with gold standard comparison workflow.

## [2.22.20] - 2026-04-24

### Changed
- **TTD Version row in Test Execution Summary is now hidden**: The TTD Version row in the HTML report summary table is now `display:none` so it doesn't clutter the visible report. The version is still present in the HTML (and in the `<meta name="generator">` tag) for diagnostic purposes.

## [2.22.19] - 2026-04-23

### Fixed
- **Failure cluster links broken for parameterized test scenarios**: Links in the failure clusters section did not navigate to parameterized test rows because individual `<tr>` rows only had `data-scenario-id` attributes, not `id` attributes. Added `id` to parameterized `<tr>` rows and updated the onclick handler to walk up ancestor `<details>` elements (opening them) and trigger row selection via `click()`.

## [2.22.18] - 2026-04-23

### Fixed
- **Flaky `No_unused_component_warning_when_no_components_registered` test**: `MessageTrackerTests` and `TestTrackingMessageHandlerTests` construct `MessageTracker`/`TestTrackingMessageHandler` instances whose constructors call `TrackingComponentRegistry.Register()`, but these test classes were not in the `"TrackingComponentRegistry"` xUnit collection. This allowed them to run in parallel with `ReportDiagnosticsTests`, polluting the static registry between `Clear()` and `Analyse()`. Added `[Collection("TrackingComponentRegistry")]` to both classes.

## [2.22.17] - 2026-04-23

### Fixed
- **Scenario-steps `<details>` not aligned with example-diagrams `<details>`**: `.scenario-steps` had `padding: 0.5em 1em` on the container, pushing its content inward compared to `.example-diagrams` which has no container padding. Removed container padding and moved it to `.scenario-steps > summary` (`padding: 1em`, matching `.example-diagrams > summary`) and child steps (`margin-left/right: 1em`), so both sections are now left-aligned.

## [2.22.16] - 2026-04-23

### Fixed
- **Component diagram not embedded in TestRunReport when ComponentDiagramOptions is null**: The embed decision in `CreateStandardReportsWithDiagrams` used `options.ComponentDiagramOptions?.EmbedInTestRunReport == true` which evaluates to `false` when `ComponentDiagramOptions` is `null` (the default). This contradicted the PlantUML generation path on the same method which already falls back to `new ComponentDiagramOptions()` (where `EmbedInTestRunReport` defaults to `true`). Extracted the decision into `ShouldEmbedComponentDiagram()` using the same `?? new ComponentDiagramOptions()` null-coalesce pattern — no mutation of the original options object, avoiding the WAF NRE that the v2.22.6 fix caused.

## [2.22.15] - 2026-04-23

### Added
- **Phase-aware tracking configuration (Setup vs Action)**: All tracking extensions now support configuring behavior differently for the Setup phase (Given/Arrange) vs the Action phase (When/Act). This allows reducing noise from setup operations while keeping full detail for the actions under test.
  - **`TestPhase` enum**: New `Unknown`, `Setup`, `Action` values representing the current test phase.
  - **`TestPhaseContext`**: AsyncLocal-based ambient context (similar to `TrackingTraceContext`) that holds the current `TestPhase`. BDD frameworks (BDDfy, LightBDD, ReqNRoll) set this automatically based on the step type keyword. Non-BDD tests use `TrackingDiagramOverride.StartSetup()` and `TrackingDiagramOverride.StartAction()`.
  - **`PhaseConfiguration`**: Utility class providing `ShouldTrack()` (phase-aware enable/disable), `GetEffectiveVerbosity<T>()` (phase-aware verbosity override), and `ResolvePhaseFromStepType()` (keyword-to-phase mapping).
  - **Phase on `RequestResponseLog`**: Each log entry now carries a `Phase` property indicating which test phase produced it.
  - **`TrackingDiagramOverride.StartSetup()`**: New method (with `Action` delegate overload) to explicitly mark the Setup phase.
- **Phase properties on all extension options**: Every tracking extension options class now has `SetupVerbosity?`, `ActionVerbosity?`, `TrackDuringSetup` (default `true`), and `TrackDuringAction` (default `true`) properties. When a phase-specific verbosity is set, it overrides the default `Verbosity` during that phase. Setting `TrackDuringSetup = false` suppresses all tracking during setup.
- **Phase-aware tracker implementations**: All 21 tracker implementations (EF Core, Redis, Kafka, gRPC, MassTransit, MongoDB, CosmosDB, Elasticsearch, Dapper, BigQuery, BlobStorage, CloudStorage, DynamoDB, EventBridge, S3, SNS, SQS, StorageQueues, EventHubs, PubSub, ServiceBus) now check `PhaseConfiguration.ShouldTrack()` before recording and use `PhaseConfiguration.GetEffectiveVerbosity()` to resolve the active verbosity level.
- **Phase-aware core handlers**: `TestTrackingMessageHandler`, `MessageTracker`, `TrackingProxy`, and `RequestResponseLogger` all support phase-based filtering and verbosity.
- **30 new unit tests** for `TestPhaseContext` (5 tests) and `PhaseConfiguration` (25 tests) covering all phase combinations, verbosity override precedence, and step-type keyword resolution.

## [2.22.14] - 2026-04-23

### Changed
- **Scenario-steps sections now have a rounded border**: `.scenario-steps` `<details>` elements are now surrounded by a slightly rounded `1px solid` border matching the `.example-diagrams` styling (`border-radius: 1em`, `border-color: rgb(224, 224, 224)`). The summary also received `background-color: white` and `border-radius: 1em` for visual consistency.
- **Parameterized detail-panel steps now use collapsible `<details>/<summary>`**: Steps within `param-detail-panel` were previously rendered as a plain `<div>`. They now use the same `<details class="scenario-steps" open><summary class="h4">Steps</summary>` pattern as normal scenario steps, making them collapsible and visually consistent.
- **Violet theme border override updated**: The violet theme's `.scenario-steps` override changed from `border-left-color` to `border-color` to match the new full-border design.

## [2.22.13] - 2026-04-23

### Fixed
- **Flaky TrackingComponentRegistry tests under parallel execution**: `TrackingComponentRegistry.Clear()` used a `while (TryTake)` drain loop on `ConcurrentBag` which is not atomic — items added from other threads could survive the clear due to thread-local storage stealing delays. Replaced with `Interlocked.Exchange` to atomically swap in a fresh empty bag. Also added `[CollectionDefinition]` for the `"TrackingComponentRegistry"` xUnit collection to ensure proper test serialisation.

## [2.22.12] - 2026-04-23

### Fixed
- **Note toggle buttons not working with SeparateSetup/partition diagrams**: `findNoteGroups()` incorrectly detected participant boxes and partition labels (fill `#E2E2F0`) as note groups, causing a mismatch between SVG note groups and source note blocks. Hover rects and button click handlers were attached to the wrong elements, making note collapse/expand/cycle actions appear broken. Fixed by excluding the standard PlantUML participant/partition fill (`#E2E2F0`) from `hasNoteFill()` and adding a safety-net fill-frequency filter in `makeNotesCollapsible()` that reconciles group counts with note block counts.
- **WebApplicationFactory NRE on teardown when ComponentDiagramOptions is null**: The v2.22.6 fix for `ComponentDiagramOptions` null handling changed the semantic behavior — `null` options now defaulted to embedding the component diagram (via `new ComponentDiagramOptions()` where `EmbedInTestRunReport = true`). This caused unexpected component diagram embedding for consumers who never configured `ComponentDiagramOptions`, adding work during report generation that could trigger a `NullReferenceException` in `WebApplicationFactory<T>.DisposeAsync()`. Reverted to the v2.22.5 null-conditional behavior: `null` `ComponentDiagramOptions` now means "don't embed".

### Added
- **TTD version embedded in all reports**: The Kronikol version is now included in HTML reports (as a `<meta name="generator">` tag and in the Test Execution Summary table), JSON data (`kronikolVersion` field), YAML data (`TtdVersion` field), XML data (`<TtdVersion>` element), and the JSON schema.

## [2.22.11] - 2026-04-23

### Added
- **R2 (FlattenedObject) parameter display rule**: When a parameterized test has a single complex object parameter with all scalar properties (≤ max columns), its properties are automatically flattened into individual columns in the parameter table. This provides a clear, readable view of complex test case objects instead of showing a single wall-of-text column. Only applies when structured extraction is available (xUnit3, TUnit, NUnit4).
- **R3 (SubTable) cell rendering**: Within R1/R2 parameter tables, individual cell values that are small complex objects (≤5 scalar properties, no nesting) are rendered as a mini sub-table inside the cell, with property names as row headers and values as row data.
- **R4 (ExpandableComplex) cell rendering**: Within R1/R2 parameter tables, individual cell values that are deeply complex (nested objects, arrays, or >5 properties) are rendered as a collapsible `<details>/<summary>` element with a type-name preview and syntax-highlighted JSON expansion body.
- **`ExampleRawValues` on Scenario**: New `Dictionary<string, object?>?` property that preserves the raw parameter objects (not just `ToString()` strings) for reflection-based R2/R3/R4 rendering.
- **`ParameterParser.ExtractStructuredParametersWithRaw()`**: New method that returns both string values and raw object references, used by xUnit3, TUnit, and NUnit4 adapters.
- **`ParameterValueRenderer` helper class**: Internal static class providing object introspection (`IsScalarType`, `IsSmallComplexObject`, `IsComplexValue`, `TryGetFlattenableProperties`), property flattening (`FlattenToStringValues`, `FlattenToRawValues`), and HTML rendering (`RenderSubTable`, `RenderExpandable`, `GenerateHighlightedJson`).
- **CSS for R3/R4**: `.cell-subtable` styles for sub-table cells and `details.param-expand` / `.expand-body` / `.prop-key` / `.prop-val` styles for expandable complex parameter cells.
- **JavaScript for R4 interaction**: Expanding a `<details class="param-expand">` element auto-selects the containing row (switching diagrams/detail panels); sub-table clicks don't bubble to row selection.
- **40 new unit tests** for `ParameterValueRenderer` covering type classification, property introspection, flattening, sub-table rendering, expandable rendering, JSON highlighting, and preview generation.
- **5 new R2 detection tests** in `ParameterGrouperTests` covering single complex param flattening, nested objects not flattening, no raw values fallback, multiple params not triggering R2, and max columns exceeded.
- **10+ new R2/R3/R4 rendering integration tests** in `ParameterizedGroupRenderTests` verifying flattened property columns, sub-table HTML, expandable details HTML, CSS/JS presence, and correct scalar fallback for single primitives.

### Changed
- **xUnit3, TUnit, NUnit4 adapters**: Updated to use `ExtractStructuredParametersWithRaw()` and populate `ExampleRawValues` alongside `ExampleValues`, enabling R2/R3/R4 cell rendering when structured extraction is available.
- **`ParameterGrouper.DetermineParamsAndRule()`**: Now detects R2 (FlattenedObject) when all scenarios have a single complex parameter with all-scalar properties ≤ max columns, flattening the property names into columns and updating `ExampleValues`/`ExampleRawValues` on each scenario.
- **`ReportGenerator.RenderParameterizedGroup()`**: Header and cell rendering now handles `FlattenedObject` rule identically to `ScalarColumns`, and applies per-cell R3/R4 rendering based on `ExampleRawValues` type inspection.

## [2.22.10] - 2026-04-23

### Added
- **Structured parameter extraction for TUnit, NUnit4, and MSTest adapters**: Parameterized test tables now use real parameter names instead of positional `arg0`, `arg1` keys when the framework provides access to method arguments and parameter metadata.
  - **TUnit**: Uses `TestDetails.TestMethodArguments` and `MethodMetadata.Parameters` to extract named parameter values directly, bypassing string parsing entirely.
  - **NUnit4**: Uses `TestContext.Test.Arguments` and `TestContext.Test.Method.GetParameters()` for the same structured extraction.
  - **MSTest**: Captures parameter names via `MethodInfo.GetParameters()` in `DiagrammedComponentTest.TestTrackingCleanup()` and rebinds positional keys from display-name parsing to real parameter names. Added `ParameterNames` property to `MSTestScenarioInfo`.
  - **xUnit3**: Refactored to use shared `ParameterParser.ExtractStructuredParameters()` method (no behavioral change — xUnit3 already had structured extraction).
- **`ParameterParser.ExtractStructuredParameters()` shared method**: New public method on `ParameterParser` that maps raw argument values to parameter names, used by all adapters that support structured extraction.

## [2.22.9] - 2026-04-23

### Changed
- **Steps section is now collapsible**: Test steps within each scenario are wrapped in a `<details>` element with a "Steps" summary heading, matching the pattern used by the Diagrams section. Steps are expanded by default but can be collapsed by clicking the heading to reduce visual noise, especially for scenarios with many or long parameterized steps.
- **Removed left border from top-level steps**: The 3px vertical border on the left of the top-level steps container has been removed for a cleaner look. Sub-step borders are preserved.

## [2.22.8] - 2026-04-23

### Fixed
- **Parameter parser now handles curly-brace nesting in C# record `ToString()` output**: `SplitParams()` and `FindColon()` previously only tracked parenthesis depth, so commas inside `TypeName { Prop = val, ... }` structures (produced by C# record auto-generated `ToString()`) were incorrectly treated as top-level parameter separators. This caused parameterized test tables to split a single complex parameter into multiple mangled columns. Both methods now track `braceDepth` alongside `parenDepth`.

## [2.22.7] - 2026-04-23

### Fixed
- **Removed redundant "Loading component diagram..." text**: The placeholder text was shown alongside the "Rendering Diagram..." indicator from the PlantUML renderer, creating duplicate loading messages.
- **Component Diagram and Scenario Timeline toggles now act as radio buttons**: Activating one automatically deactivates the other and removes its active styling, preventing both panels from being visible simultaneously.

## [2.22.6] - 2026-04-23

### Fixed
- **Component diagram not embedded in TestRunReport when ComponentDiagramOptions is null**: When users did not explicitly set `ComponentDiagramOptions` on `ReportConfigurationOptions` (the common case), the null-conditional `?.EmbedInTestRunReport == true` evaluated to `null == true` → `false`, silently suppressing the embedded component diagram despite `EmbedInTestRunReport` defaulting to `true`. Now falls back to `new ComponentDiagramOptions()` before checking the flag.

## [2.22.5] - 2026-04-22

### Fixed
- **Fixed flaky Selenium note toggle tests under concurrent load**: `WaitForReRender()` now waits for both the SVG re-render AND `makeNotesCollapsible()` to finish adding `.note-toggle-icon` elements. Previously it returned as soon as the SVG innerHTML changed, but under CPU contention from parallel Chrome instances, there was a timing gap before the JS callback created toggle icons — causing assertions on button counts/types to see stale or missing state. `SetScenarioState()` also now waits for toggle icons alongside hover rects.

## [2.22.4] - 2026-04-22

### Changed
- **Component diagram is now a toolbar toggle instead of a collapsible `<details>` section**: The embedded component diagram in the TestRunReport is hidden by default and revealed via a "Component Diagram" toggle button in the toolbar (matching the existing "Scenario Timeline" button style). This avoids the large diagram dominating the report on load. The PlantUML renderer is triggered on first show via `_renderDiagramsInContainer`, ensuring the diagram renders correctly despite being initially hidden from the IntersectionObserver.

## [2.22.3] - 2026-04-22

### Fixed
- **Restored activity diagram and flame chart span capture**: The v2.21.1 fix for Application Insights dependency tracking over-corrected by excluding *all* well-known auto-instrumentation `ActivitySource`s (`Microsoft.AspNetCore`, `Microsoft.EntityFrameworkCore`, `Npgsql`, `StackExchange.Redis`, etc.) from the `InternalFlowActivityListener`. Since `FilterByAutoInstrumentation` requires at least one well-known source span to anchor traces, this caused activity diagrams and flame charts to be completely empty for projects not using `AddTestTrackingExporter()`. The listener now only excludes `System.Net.Http` — the sole source where `ActivitySource.HasListeners()` triggers a mutually exclusive code path in `DiagnosticsHandler` that breaks Application Insights dependency telemetry.

## [2.22.2] - 2026-04-22

### Fixed
- **Note collapse/expand 3-state cycle for long notes**: Fixed three bugs in the note truncation/collapse/expand system:
  1. The ▼ (expand) button and + button from collapsed state now correctly go to **truncated** (step 1) for long notes, instead of skipping directly to fully expanded (step 2).
  2. The double-click cycle from collapsed state now correctly goes to **truncated** for long notes, instead of fully expanded.
  3. `isLongNote()` checks now use the container's per-scenario `_truncateLines` value instead of falling back to the global `window._truncateLines`. This fixes the ▲ button not appearing after scenario-level truncation changes.
- **Tooltip truncation for collapsed notes**: Collapsed note tooltips now respect the container's per-scenario truncation level instead of always using the global default.

### Added
- **Comprehensive Selenium tests for note state transitions**: Added 16 new Selenium tests covering all note collapse/expand/truncate state transitions including:
  - Long note 3-state double-click cycle (expanded → truncated → collapsed → truncated)
  - ▼ button from collapsed → truncated (not expanded) for long notes
  - ▼ button from truncated → expanded
  - ▲ button visibility and click behavior
  - Short note 2-state cycle (expanded ↔ collapsed)
  - Truncation level changes affecting note "long" classification
  - Minus button state transitions

## [2.22.1] - 2026-04-22

### Fixed
- **Deferred `InternalFlowActivityListener` startup**: The `TestTrackingMessageHandler` constructor no longer calls `InternalFlowActivityListener.EnsureStarted()`. The listener is now started lazily on the first `SendAsync()` call. Registering an `ActivityListener` during DI resolution could alter `ActivitySource.HasListeners()` state before Application Insights' `DependencyTrackingTelemetryModule` and the host had fully initialised, preventing `DiagnosticsHandler` from being added to the HTTP pipeline and silently breaking HTTP dependency telemetry.
- **Traceparent injection limited to TestServer scenarios**: `TestTrackingMessageHandler.SendAsync()` now only injects the `traceparent` header when `Activity.Current` is null (i.e. in-process TestServer calls). When an ambient Activity already exists, framework handlers (e.g. `DiagnosticsHandler` inside `SocketsHttpHandler`) create proper child Activities and inject `traceparent` themselves — pre-empting this by injecting the parent’s span ID broke Application Insights dependency correlation.
- **Fixed flaky `No_tracking_component_section_when_none_registered` test**: The diagnostic report test now clears `TrackingComponentRegistry` before asserting, preventing pollution from parallel test runs that create `TestTrackingMessageHandler` instances.

## [2.22.0] - 2026-04-22

### Added
- **Dependency-Type Colored Arrows & Typed Shapes**: Sequence and component diagrams now color-code arrows and use typed shapes based on the target service's dependency type (Issue #21).
  - New `DependencyCategory` parameter on `RequestResponseLog` — set automatically by all extension packages (CosmosDB, Redis, EF Core, ServiceBus, BigQuery, BlobStorage).
  - New `DependencyType` enum: `HttpApi`, `Database`, `Cache`, `MessageQueue`, `Storage`, `Unknown`.
  - New `DependencyPalette` static class with default vivid color palette and category-to-type resolution.
  - **Sequence diagrams**: Services render with typed PlantUML shapes (`database` for DB/Storage, `collections` for Cache, `queue` for MessageQueue, `entity` for HTTP APIs). Request/response arrows are colored by target service type.
  - **Component diagrams**: C4 mode uses `SystemDb()`/`SystemQueue()` for appropriate types. Plain PlantUML mode uses `database`/`collections`/`queue` shapes with matching skinparams. Arrows colored by dependency type (default) or P95 latency (opt-in via `ArrowColorMode.Performance`).
  - New `ArrowColorMode` enum to select between `DependencyType` (default) and `Performance` arrow coloring.
  - New config options on `ReportConfigurationOptions`: `SequenceDiagramArrowColors`, `SequenceDiagramParticipantColors`, `DependencyColors`, `ServiceTypeOverrides`.
  - New `DependencyColors` property on `ComponentDiagramOptions` for per-diagram color overrides.
- **Embedded Component Diagram in TestRunReport**: When `ComponentDiagramOptions.EmbedInTestRunReport` is `true` (default), the component diagram is rendered inline in the TestRunReport as a collapsible section before the scenario list, using the same BrowserJs PlantUML renderer. The standalone `ComponentDiagram.html` file continues to be generated as well.

### Changed
- Default arrow style in both sequence and component diagrams is now dependency-type colored. Previous behavior (plain arrows or P95-based coloring) is available via `SequenceDiagramArrowColors = false` or `ArrowColorMode.Performance`.
- `GetProtocol` in `ComponentDiagramGenerator` now prefers `DependencyCategory` over HTTP method when available, producing labels like "CosmosDB: Query" instead of "HTTP: POST".

## [2.21.2] - 2026-04-22

### Fixed
- **`TestTrackingMessageHandler.SendAsync` no longer sets `Activity.Current`**: The handler was creating a `new Activity("Kronikol.Request").Start()` when no ambient Activity existed, which set `Activity.Current` and interfered with Application Insights' telemetry correlation — `DependencyTelemetry` items received the wrong `Context.Operation.Name` (or none at all) instead of the server request's operation name (e.g. `"GET /health"`). The handler now generates trace/span IDs directly via `ActivityTraceId.CreateRandom()` / `ActivitySpanId.CreateRandom()` and injects the `traceparent` header without creating an Activity. This preserves W3C trace context propagation for InternalFlow span correlation while leaving `Activity.Current` untouched.

## [2.21.1] - 2026-04-22

### Fixed
- **`InternalFlowActivityListener` no longer breaks Application Insights HTTP dependency tracking**: The listener was subscribing to ALL `ActivitySource`s (`ShouldListenTo = _ => true`), which caused .NET's `System.Net.Http.DiagnosticsHandler` to take the `ActivitySource`-based code path instead of the `DiagnosticListener`-based path. Application Insights SDK 2.x only creates `DependencyTelemetry` from the latter, so HTTP dependency tracking was silently broken. The listener now excludes well-known auto-instrumentation sources (e.g. `System.Net.Http`, `Microsoft.AspNetCore`, `Microsoft.EntityFrameworkCore`) via `InternalFlowSpanCollector.WellKnownAutoInstrumentationSources`. Custom application `ActivitySource` spans are still captured for internal flow diagrams.

## [2.21.0] - 2026-04-22

### Added
- **`MessageTracker` upgraded to first-class tracking component**: The core `MessageTracker` class (used for tracking custom messaging abstractions) now implements `ITrackingComponent` with auto-registration in `TrackingComponentRegistry`, enabling unused-component diagnostic warnings.
  - New `MessageTrackerOptions` record with `ServiceName`, `CallingServiceName`, `Verbosity`, `CurrentTestInfoFetcher`, and `SerializerOptions` — aligns `MessageTracker` with the same options pattern used by all extension packages.
  - New `MessageTracker(MessageTrackerOptions)` constructor — recommended for new code. The legacy `IHttpContextAccessor`-based constructor is preserved for backward compatibility.
  - New `MessageTrackerVerbosity` enum (Raw, Detailed, Summarised) — `Summarised` omits message payloads from diagrams.
  - New `TrackSendEvent()` one-shot method — logs a complete fire-and-forget request/response pair in a single call, reducing boilerplate for event-driven patterns.
  - New `TrackMessagesForDiagrams(MessageTrackerOptions)` DI overload.

### Fixed
- Fixed `ReportDiagnosticsTests.Unused_component_warning_lists_count` flaky test — now clears `TrackingComponentRegistry` before asserting component counts, preventing pollution from parallel test runs.

## [2.20.0] - 2026-04-22

### Added
- **New `Kronikol.Extensions.EventBridge` package**: Track Amazon EventBridge operations in test diagrams via the AWS SDK HTTP pipeline. Intercepts `X-Amz-Target` JSON-RPC calls using the same `DelegatingHandler` pattern as the S3, DynamoDB, SQS, and SNS extensions. Includes:
  - `EventBridgeTrackingMessageHandler` — DelegatingHandler implementing `ITrackingComponent` with auto-registration. Classifies and logs PutEvents, rule management, target management, event bus lifecycle, archive, replay, and tagging operations.
  - `EventBridgeOperationClassifier` — Dictionary-based classifier mapping 28 `X-Amz-Target` headers to `EventBridgeOperation` enum values, with JSON body parsing for PutEvents (DetailType, Source, EntryCount, EventBusName) and rule operations (Name, EventBusName).
  - `AmazonEventBridgeConfigExtensions.WithTestTracking()` — Fluent extension on `AmazonEventBridgeConfig` that injects the tracking handler via `HttpClientFactory`.
  - URI scheme: `eventbridge://{busName}/` (Detailed/Summarised), original AWS URL (Raw).
  - Three verbosity levels: Raw (full HTTP details), Detailed (classified labels with context like `PutEvents [OrderCreated] x5`), Summarised (grouped labels like `ManageRule`, `ManageTargets`, `ManageBus`).
  - Default excluded operations: TagResource, UntagResource, ListTagsForResource, ListEventBuses.

## [2.19.0] - 2026-04-21

### Added
- **New `Kronikol.Extensions.Dapper` package**: Track Dapper and ADO.NET SQL operations in test diagrams. Wraps `DbConnection` to intercept all query execution with zero Dapper-specific dependencies — works with any ADO.NET provider. Includes:
  - `TrackingDbConnection` — Decorator wrapping `DbConnection` that implements `ITrackingComponent` with auto-registration. Creates `TrackingDbCommand` instances that intercept `ExecuteReader`, `ExecuteNonQuery`, `ExecuteScalar` (sync + async).
  - `TrackingDbCommand` — Intercepts all execution methods, classifies the SQL, and logs request/response pairs to `RequestResponseLogger`.
  - `TrackingDbTransaction` — Transparent wrapper that logs `BEGIN`, `COMMIT`, and `ROLLBACK` operations.
  - `DapperOperationClassifier` — Regex-based classifier recognising 15 SQL operation types (Query, Insert, Update, Delete, Merge, StoredProcedure, CreateTable, AlterTable, DropTable, CreateIndex, Truncate, BeginTransaction, Commit, Rollback, Other) with table name extraction.
  - `DbConnectionExtensions.WithTestTracking()` — Fluent extension on any `DbConnection` that wraps it in a `TrackingDbConnection`.
  - URI scheme: `sql://dataSource/database/table` (Detailed), `sql://dataSource/database` (Raw), `sql:///database/table` (Summarised).
  - Three verbosity levels with configurable SQL text logging, parameter logging, and operation exclusions.

## [2.18.0] - 2026-04-21

### Added
- **New `Kronikol.Extensions.Elasticsearch` package**: Track Elasticsearch operations in test diagrams via the Elastic .NET client's `OnRequestCompleted` callback. Intercepts and classifies REST API operations across indices. Includes:
  - `ElasticsearchTrackingCallbackHandler` — Callback handler implementing `ITrackingComponent`. Classifies and logs index, search, document, bulk, and cluster operations.
  - `ElasticsearchOperationClassifier` — Classifies 24 Elasticsearch REST API operations (IndexDocument, GetDocument, Search, Bulk, CreateIndex, DeleteIndex, etc.) from URL path patterns and HTTP methods.
  - `ElasticsearchClientSettingsExtensions.WithTestTracking()` — Fluent extension on `ElasticsearchClientSettings` that enables `DisableDirectStreaming` and registers the tracking callback.
  - URI scheme: `elasticsearch:///indexName` (Detailed), full request URI (Raw), `elasticsearch:///` (Summarised).
  - Configurable operation exclusions (ClusterHealth and CatApis excluded by default), request/response body capture, and three verbosity levels.

## [2.17.0] - 2026-04-21

### Added
- **New `Kronikol.Extensions.Kafka` package**: Track Apache Kafka produce and consume operations in test diagrams using wrapper classes around Confluent.Kafka's `IProducer<TKey,TValue>` and `IConsumer<TKey,TValue>`. Includes:
  - `KafkaTracker` — Central logging component implementing `ITrackingComponent`. Logs produce, consume, subscribe, and commit operations with Event MetaType. Consume operations swap caller/service names to reflect incoming message direction.
  - `TrackingKafkaProducer<TKey,TValue>` — Wrapper implementing `IProducer<TKey,TValue>` that intercepts `Produce` and `ProduceAsync` calls with topic, partition, and offset tracking.
  - `TrackingKafkaConsumer<TKey,TValue>` — Wrapper implementing `IConsumer<TKey,TValue>` that intercepts `Consume` and `Subscribe` calls, skipping EOF/null results.
  - `KafkaOperationClassifier` — Classifies operations (Produce, ProduceAsync, Consume, Subscribe, Unsubscribe, Commit, Flush) with topic, partition, and offset details.
  - URI scheme: `kafka:///topic` (Detailed), `kafka:///topic/partition@offset` (Raw), `kafka:///` (Summarised).
  - Supports configurable produce/consume/subscribe/commit tracking, message key/value logging, and three verbosity levels.

## [2.16.0] - 2026-04-21

### Added
- **New `Kronikol.Extensions.MassTransit` package**: Track MassTransit message operations (RabbitMQ, Azure Service Bus, Amazon SQS, and other transports) in test diagrams using MassTransit observer interfaces. Includes:
  - `MassTransitTracker` — Central logging component implementing `ITrackingComponent`. Logs send, publish, consume, and fault operations with Event MetaType.
  - `TrackingSendObserver`, `TrackingPublishObserver`, `TrackingConsumeObserver` — MassTransit observer implementations that delegate to the tracker.
  - `MassTransitOperationClassifier` — Classifies operations (Send, Publish, Consume, SendFault, PublishFault, ConsumeFault) with message type and URI extraction.
  - `BusConfigurationExtensions.WithTestTracking()` — Fluent extension on `IBusFactoryConfigurator`.
  - URI scheme: `masstransit:///queue-name` (Detailed) or transport URI (Raw).
  - Supports configurable send/publish/consume tracking, message body logging, and fault logging.

## [2.15.0] - 2026-04-21

### Added
- **New `Kronikol.Extensions.StorageQueues` package**: Track Azure Storage Queue operations in test diagrams using the Azure.Core Transport pattern (same as BlobStorage). Includes:
  - `StorageQueueOperationClassifier` — Classifies Storage Queue REST API operations (SendMessage, ReceiveMessages, PeekMessages, DeleteMessage, UpdateMessage, ClearMessages, CreateQueue, DeleteQueue, GetProperties, SetMetadata, ListQueues) from URL path patterns and query parameters.
  - `StorageQueueTrackingMessageHandler` — `DelegatingHandler` + `ITrackingComponent` that intercepts HTTP requests, classifies operations, and logs request/response pairs.
  - `QueueClientOptionsExtensions.WithTestTracking()` — Fluent extension on `QueueClientOptions` that sets `Transport` to `HttpClientTransport` with tracking handler.
  - URI scheme: `storagequeue:///queueName`.
  - Supports three verbosity levels: Raw, Detailed (with queue name in labels), and Summarised.

## [2.14.0] - 2026-04-21

### Added
- **New `Kronikol.Extensions.EventHubs` package**: Track Azure Event Hubs operations in test diagrams using the wrapper/decorator pattern around `EventHubProducerClient` and `EventHubConsumerClient`. Includes:
  - `EventHubsOperationClassifier` — Classifies Event Hubs operations (Send, SendBatch, CreateBatch, ReadEvents, ReadEventsFromPartition, GetPartitionIds, GetEventHubProperties, GetPartitionProperties, StartProcessing, StopProcessing, ProcessEvent) by method name with event count awareness.
  - `EventHubsTracker` — Central logging helper implementing `ITrackingComponent`. Logs request/response pairs with Event MetaType. Supports three verbosity levels with partition ID in URI.
  - `TrackingEventHubProducerClient` — Wrapper around `EventHubProducerClient` tracking single and batch send operations with event body serialization.
  - `TrackingEventHubConsumerClient` — Wrapper around `EventHubConsumerClient` tracking `ReadEventsAsync` and `ReadEventsFromPartitionAsync` via `IAsyncEnumerable`.
  - URI scheme: `eventhubs:///hub-name[/partition-id]`.

## [2.13.0] - 2026-04-21

### Added
- **New `Kronikol.Extensions.CloudStorage` package**: Track Google Cloud Storage operations in test diagrams using the `DelegatingHandler` pattern via Google APIs `HttpClientFactory`. Includes:
  - `CloudStorageOperationClassifier` — Classifies GCS REST API operations (Upload, Download, Delete, ListObjects, GetMetadata, UpdateMetadata, Copy, Compose, CreateBucket, DeleteBucket, GetBucket, ListBuckets) from URL path patterns. Distinguishes Download vs GetMetadata via `alt=media` query parameter.
  - `CloudStorageTrackingMessageHandler` — `DelegatingHandler` + `ITrackingComponent` that intercepts HTTP requests, classifies operations, and logs request/response pairs.
  - `TrackingCloudStorageHttpClientFactory` — Google APIs `HttpClientFactory` wrapper that injects the tracking handler.
  - `StorageClientBuilderExtensions.WithTestTracking()` — Fluent extension on `StorageClientBuilder`.
  - URI scheme: `gcs:///bucket/object` (Detailed) or original Google API URL (Raw).
  - Handles URL-encoded object names, Copy/Compose paths, and bucket-level operations.

## [2.12.0] - 2026-04-21

### Added
- **New `Kronikol.Extensions.SNS` package**: Track Amazon SNS operations in test diagrams using the `DelegatingHandler` pattern via `AmazonSimpleNotificationServiceConfig.HttpClientFactory`. Includes:
  - `SnsOperationClassifier` — Classifies SNS operations (Publish, PublishBatch, Subscribe, Unsubscribe, CreateTopic, DeleteTopic, ListTopics, ListSubscriptions, ListSubscriptionsByTopic, GetTopicAttributes, SetTopicAttributes, ConfirmSubscription) from `X-Amz-Target: AmazonSimpleNotificationService.{Op}` header or legacy `Action` query/form parameter. Extracts topic name from `TopicArn`/`TargetArn` ARN fields.
  - `SnsTrackingMessageHandler` — `DelegatingHandler` + `ITrackingComponent` that intercepts HTTP requests, classifies operations, and logs request/response pairs with configurable verbosity. Reconstructs request body after classification for downstream handlers.
  - `AmazonSNSConfigExtensions.WithTestTracking()` — Fluent extension on `AmazonSimpleNotificationServiceConfig` that installs the tracking handler via `HttpClientFactory`.
  - URI scheme: `sns:///topic-name` (Detailed/Summarised) or original AWS URL (Raw).
  - Supports FIFO topics (`.fifo` suffix preserved), direct publish via `TargetArn`, and full ARN extraction.

## [2.11.0] - 2026-04-21

### Added
- **New `Kronikol.Extensions.PubSub` package**: Track Google Cloud Pub/Sub operations in test diagrams using wrapper/decorator pattern around `PublisherClient` and `SubscriberClient`. Includes:
  - `PubSubOperationClassifier` — Classifies Pub/Sub operations (Publish, PublishBatch, Pull, Acknowledge, ModifyAckDeadline, Receive, StartSubscriber, StopSubscriber) with short name extraction from full GCP resource paths (`projects/p/topics/t` → `t`).
  - `PubSubTracker` — Central logging helper implementing `ITrackingComponent`. Logs request/response pairs with Event MetaType for publish/receive operations. Supports three verbosity levels.
  - `TrackingPublisherClient` — Wrapper around `PublisherClient` tracking single and batch publish operations with message content at Raw/Detailed verbosity.
  - `TrackingSubscriberClient` — Wrapper around `SubscriberClient` that wraps the message handler callback to track received messages and Ack/Nack replies.
  - URI scheme: `pubsub:///topic-name` (Detailed) or `pubsub:///projects/p/topics/t` (Raw).

## [2.10.0] - 2026-04-21

### Added
- **New `Kronikol.Extensions.Grpc` package**: Track gRPC client calls in test diagrams using the `Grpc.Core.Interceptors.Interceptor` API. Includes:
  - `GrpcOperationClassifier` — Classifies gRPC calls by method type (Unary, ServerStreaming, ClientStreaming, DuplexStreaming) with service name, method name, and full method path extraction from `ClientInterceptorContext`.
  - `GrpcTrackingInterceptor` — Client-side interceptor that intercepts all gRPC call types (AsyncUnaryCall, BlockingUnaryCall, AsyncServerStreamingCall, AsyncClientStreamingCall, AsyncDuplexStreamingCall). Logs request/response pairs with protobuf message serialization. Implements `ITrackingComponent` with auto-registration. Maps gRPC `StatusCode` to HTTP status codes for consistent error logging.
  - `GrpcChannelExtensions.WithTestTracking()` — Fluent extension on `GrpcChannel` that wraps the channel's `CallInvoker` with the tracking interceptor.
  - Three verbosity levels: Raw (full method path + call type annotation + headers), Detailed (method name with streaming annotations + grpc URI + message content), Summarised (method name only, no content).
  - URI scheme: `grpc:///ServiceName/MethodName` (path-based to preserve casing).
  - Optional `UseProtoServiceNameInDiagram` to use the proto service name instead of the configured `ServiceName`.
  - Streaming calls tracked at initiation level (not per-message) for clean diagrams.

## [2.9.0] - 2026-04-21

### Added
- **New `Kronikol.Extensions.SQS` package**: Track Amazon SQS operations in test diagrams. Includes:
  - `SqsOperationClassifier` — Classifies SQS operations from both the JSON protocol (`X-Amz-Target: AmazonSQS.{Op}`) and legacy query protocol (`Action=` parameter in query string or form body). Extracts queue names from URL path (`/account-id/queue-name`), body `QueueUrl` field, or body `QueueName` field. Supports 14 operations: messaging (SendMessage, SendMessageBatch, ReceiveMessage, DeleteMessage, DeleteMessageBatch), visibility (ChangeMessageVisibility, ChangeMessageVisibilityBatch), queue management (CreateQueue, DeleteQueue, GetQueueUrl, GetQueueAttributes, SetQueueAttributes, PurgeQueue, ListQueues).
  - `SqsTrackingMessageHandler` — `DelegatingHandler` that intercepts all SQS HTTP traffic, reads and reconstructs request bodies for classification, and logs request/response pairs for diagram generation. Implements `ITrackingComponent` with auto-registration.
  - `AmazonSQSConfigExtensions.WithTestTracking()` — Fluent extension on `AmazonSQSConfig` that injects a tracking `HttpClientFactory` into the AWS SDK pipeline. Zero production code changes required.
  - Three verbosity levels: Raw (HTTP method + full URI), Detailed (classified operation name + `sqs:///QueueName` URI + request/response bodies), Summarised (operation name only, `sqs:///QueueName` URI, no content/headers, skips unrecognised operations).
  - URI scheme: `sqs:///QueueName` (path-based to preserve casing, supports FIFO queue `.fifo` suffix).
  - Default excluded headers: `Authorization`, `x-amz-date`, `x-amz-security-token`, `x-amz-content-sha256`, `User-Agent`, `amz-sdk-invocation-id`, `amz-sdk-request`.

## [2.8.0] - 2026-04-21

### Added
- **New `Kronikol.Extensions.MongoDB` package**: Track MongoDB operations in test diagrams using the driver's built-in command monitoring events. Includes:
  - `MongoDbOperationClassifier` — Classifies MongoDB commands by name (find, insert, update, delete, aggregate, count, findAndModify, distinct, bulkWrite, createIndexes, dropIndexes, create, drop, listCollections, listDatabases, getMore), extracts collection name from the command BsonDocument, and optionally extracts filter text.
  - `MongoDbTrackingSubscriber` — Event-driven subscriber that hooks into `CommandStartedEvent`, `CommandSucceededEvent`, and `CommandFailedEvent` via `ClusterBuilder.Subscribe<T>()`. Uses `ConcurrentDictionary<int, PendingOperation>` to correlate request/response pairs by `RequestId`. Implements `ITrackingComponent` with auto-registration.
  - `MongoClientSettingsExtensions.WithTestTracking()` — Fluent extension on `MongoClientSettings` that chains a tracking subscriber into `ClusterConfigurator` without replacing existing configurators.
  - Three verbosity levels: Raw (full BSON command/reply + database.collection + filter), Detailed (operation → collection label + filter as request content), Summarised (operation name only, no content).
  - Default ignored commands: `isMaster`, `hello`, `saslStart`, `saslContinue`, `ping`, `buildInfo`, `getLastError`, `killCursors`. Optional `getMore` tracking (disabled by default).
  - URI scheme: `mongodb:///database/collection` (path-based to preserve casing).

## [2.7.0] - 2026-04-21

### Added
- **New `Kronikol.Extensions.DynamoDB` package**: Track Amazon DynamoDB operations in test diagrams. Includes:
  - `DynamoDbOperationClassifier` — Classifies DynamoDB operations from the `X-Amz-Target` header, extracting table names from JSON request bodies (including batch operations via `RequestItems` keys) and PartiQL statements. Supports 19 distinct operations: CRUD (PutItem, GetItem, UpdateItem, DeleteItem), queries (Query, Scan), batch (BatchWriteItem, BatchGetItem), transactions (TransactWriteItems, TransactGetItems), table management (CreateTable, DeleteTable, DescribeTable, ListTables, UpdateTable), and PartiQL (ExecuteStatement, BatchExecuteStatement, ExecuteTransaction).
  - `DynamoDbTrackingMessageHandler` — `DelegatingHandler` that intercepts all DynamoDB HTTP traffic, reads and reconstructs request bodies for classification, and logs request/response pairs for diagram generation. Implements `ITrackingComponent` with auto-registration.
  - `AmazonDynamoDBConfigExtensions.WithTestTracking()` — Fluent extension on `AmazonDynamoDBConfig` that injects a tracking `HttpClientFactory` into the AWS SDK pipeline. Zero production code changes required.
  - Three verbosity levels: Raw (HTTP method + full URI), Detailed (classified operation name + `dynamodb:///TableName` URI + request/response bodies), Summarised (operation name only, `dynamodb:///TableName` URI, no content/headers, skips unrecognised operations).
  - Default excluded headers: `Authorization`, `x-amz-date`, `x-amz-security-token`, `x-amz-content-sha256`, `User-Agent`, `amz-sdk-invocation-id`, `amz-sdk-request`.

### Fixed
- **Flaky `TrackingComponentRegistryTests` when run in parallel**: Tests now use `Assert.Contains` instead of exact count assertions, preventing false failures when handler auto-registration from parallel test projects pollutes the static registry.

## [2.6.0] - 2026-04-21

### Added
- **New `Kronikol.Extensions.S3` package**: Track Amazon S3 operations in test diagrams. Includes:
  - `S3OperationClassifier` — Regex-based classifier that identifies S3 operations from HTTP requests, supporting both **path-style** (`s3.region.amazonaws.com/bucket/key`) and **virtual-hosted-style** (`bucket.s3.region.amazonaws.com/key`) URL formats. Classifies 20 distinct operations including PutObject, GetObject, DeleteObject, CopyObject, multipart uploads, tagging, and bucket management.
  - `S3TrackingMessageHandler` — `DelegatingHandler` that intercepts all S3 HTTP traffic, classifies operations, and logs request/response pairs for diagram generation. Implements `ITrackingComponent` with auto-registration.
  - `AmazonS3ConfigExtensions.WithTestTracking()` — Fluent extension on `AmazonS3Config` that injects a tracking `HttpClientFactory` into the AWS SDK pipeline. Zero production code changes required.
  - Three verbosity levels: Raw (HTTP method + full URI), Detailed (classified operation name + `s3://bucket/key` URI), Summarised (operation name only, `s3://bucket/` URI, no content/headers, skips unrecognised operations).
  - Default excluded headers: `Authorization`, `x-amz-date`, `x-amz-security-token`, `x-amz-content-sha256`, `User-Agent`, `amz-sdk-invocation-id`, `amz-sdk-request`.

## [2.5.2] - 2026-04-21

### Changed
- **Increased PlantUML browser-rendering size limit by 50%**: The hosted `plantuml.js` CDN URL now points to a new patched build (`plantuml-js-plantuml_limit_size_98304`) that raises the maximum diagram pixel dimensions from 65,536px to 98,304px.

## [2.5.1] - 2026-04-21

### Fixed
- **EF Core extension now uses TFM-conditional package references**: `Microsoft.EntityFrameworkCore.Relational` is now referenced as `8.*` for net8.0, `9.*` for net9.0, and `10.*` for net10.0. Previously it unconditionally referenced `9.*`, which forced .NET 8 consumers to pull in EF Core 9 — a major version upgrade just to use the tracking package.

## [2.5.0] - 2026-04-21

### Added
- **HttpContext-based test identity for SQL tracking**: `SqlTrackingInterceptor` now accepts an optional `IHttpContextAccessor` and reads TTD's test identity headers (`test-tracking-current-test-name`, `test-tracking-current-test-id`) from the current `HttpContext` before falling back to `CurrentTestInfoFetcher`. This enables SQL tracking inside server-side HTTP request pipelines in `WebApplicationFactory`-based tests without custom fetcher wrappers.
- **Automatic `IHttpContextAccessor` resolution**: `AddSqlTestTracking(options)` now uses factory-based DI registration that auto-resolves `IHttpContextAccessor` when available. No code changes needed for existing consumers — SQL tracking in server-side pipelines works out of the box.

### Fixed
- **Exception safety in `SqlTrackingInterceptor`**: `CurrentTestInfoFetcher?.Invoke()` is now wrapped in a try-catch. An exception from a diagnostic fetcher delegate (e.g. `ScenarioExecutionContext.CurrentScenario` called outside a LightBDD scenario context) will never propagate into the EF Core command pipeline. If the fetcher throws and no `HttpContext` headers are available, the SQL command executes normally and is simply not logged.

## [2.4.1] - 2026-04-21

### Fixed
- **Corrected PostConfigure guidance for Duende IdentityServer**: The diagnostic report hint and wiki troubleshooting section previously recommended using `PostConfigure<ConfigurationStoreOptions>` to wire the SQL tracking interceptor into Duende's EF Core pipeline. This does not work because Duende registers its store options as a direct singleton (not via `IOptions<T>`) and captures the `ResolveDbContextOptions` delegate by value at service-registration time. The diagnostic hint now correctly advises adding `WithSqlTestTracking(sp)` inside the `ResolveDbContextOptions` implementation that runs at resolution time, and explicitly warns that `PostConfigure` does not work with Duende IdentityServer.
- **Fixed flaky ServiceBus test**: `Constructor_RegistersTrackerWithRegistry` failed intermittently due to xUnit parallel execution racing on the static `TrackingComponentRegistry`. Added `[Collection("TrackingComponentRegistry")]` to all test classes that share this static state.

## [2.4.0] - 2026-04-21

### Added
- **New `Kronikol.Extensions.ServiceBus` package**: Track Azure Service Bus messaging operations in test diagrams. Includes:
  - `ServiceBusTracker` — Central logging helper that logs send/receive/management operations as request/response pairs for diagram generation. Implements `ITrackingComponent` with auto-registration.
  - `TrackingServiceBusClient` — Wrapper around `ServiceBusClient` that creates tracked senders and receivers.
  - `TrackingServiceBusSender` — Wrapper around `ServiceBusSender` that intercepts `SendMessageAsync`, `SendMessagesAsync`, `ScheduleMessageAsync`, and `CancelScheduledMessageAsync`.
  - `TrackingServiceBusReceiver` — Wrapper around `ServiceBusReceiver` that intercepts `ReceiveMessageAsync`, `ReceiveMessagesAsync`, `PeekMessageAsync`, `CompleteMessageAsync`, `AbandonMessageAsync`, `DeadLetterMessageAsync`, `DeferMessageAsync`, and `RenewMessageLockAsync`.
  - `ServiceBusOperationClassifier` — Classifies Service Bus method calls into operations (Send, SendBatch, Receive, Complete, Abandon, DeadLetter, Defer, Schedule, etc.) with diagram labels.
  - `ServiceBusServiceCollectionExtensions.AddServiceBusTestTracking()` — DI extension that wraps existing `ServiceBusClient` registrations with tracking.
  - Three verbosity levels: Raw (enum names), Detailed (operation with queue/topic arrows), Summarised (simple operation names, no content).
  - Messaging operations (Send, Receive, Schedule, Peek) use `MetaType.Event` for blue async-messaging rendering in PlantUML diagrams.

## [2.3.2] - 2026-04-21

### Fixed
- **Failure cluster links now navigate correctly**: Clicking a scenario link in the failure clusters section of the HTML report now scrolls to the correct scenario. Previously, anchor IDs were generated from the test runtime ID instead of the scenario display name, causing a mismatch with the target element's ID.

## [2.3.1] - 2026-04-21

### Changed
- **TrackingComponentRegistry no longer throws**: Removed `ValidateAllComponentsWereInvoked()` and `ValidateComponentsWereInvoked<T>()` methods. Unused component detection is now fully passive — warnings appear in console output automatically and in the HTML diagnostic report when `DiagnosticMode=true`. TTD should never cause test failures in its default configuration.

## [2.3.0] - 2026-04-21

### Added
- **`ITrackingComponent` interface**: All tracking handlers and interceptors now implement `ITrackingComponent`, providing `ComponentName`, `WasInvoked`, and `InvocationCount` properties.
- **`TrackingComponentRegistry`**: Central static registry that auto-registers all tracking components on construction.
  - `GetUnusedComponents()` / `GetRegisteredComponents()` — Programmatic inspection of component state.
  - `Clear()` — Reset alongside `RequestResponseLogger.Clear()` in test setup.
- **Unused component warnings in diagnostic report**: `ReportDiagnostics.Analyse()` now warns when registered tracking components were never invoked — a strong indicator of misconfiguration (e.g. EF Core `DbContextOptions<T>` type mismatch). Warnings appear in console output automatically and in the HTML diagnostic report when `DiagnosticMode=true`. This never throws or fails tests.
- **Diagnostic report "Tracking Components" section**: When `DiagnosticMode=true`, the HTML diagnostic report now includes a table of all registered tracking components with their invocation counts and active/unused status, plus troubleshooting hints for common causes.
- **Invocation tracking on all extensions**: `SqlTrackingInterceptor`, `CosmosTrackingMessageHandler`, `BlobTrackingMessageHandler`, `BigQueryTrackingMessageHandler`, `RedisTracker`, and core `TestTrackingMessageHandler` all track invocation counts and auto-register with the registry.

### Documentation
- Added troubleshooting guide to EF Core Relational wiki page covering the `DbContextOptions<TBase>` vs `DbContextOptions<TDerived>` pitfall (Duende IdentityServer, ASP.NET Identity, ABP Framework).
- Added `TrackingComponentRegistry` documentation to Diagnostics and Debugging wiki page.
- Added Invocation Validation sections to all extension wiki pages (CosmosDB, BlobStorage, BigQuery, Redis, EF Core Relational).

## [2.2.1] - 2026-04-21

### Fixed
- **SqlTrackingInterceptor request/response pairing**: `LogCommandExecuting` and `LogCommandExecuted` now share the same `TraceId` and `RequestResponseId` via a `ConcurrentDictionary<DbCommand, (Guid, Guid)>` lookup. Previously each method generated its own IDs, making it impossible for the report generator to pair request and response entries — breaking internal flow popups, component diagram stats, and diagnostic report pairing warnings.

## [2.2.0] - 2026-04-21

### Added
- **New `Kronikol.Extensions.BigQuery` package**: Track Google BigQuery REST API operations in test diagrams. Includes:
  - `BigQueryTrackingMessageHandler` — A `DelegatingHandler` that intercepts all BigQuery REST calls and logs them as request/response pairs for diagram generation.
  - `BigQueryOperationClassifier` — Classifies BigQuery REST API URLs into operations (Query, Insert, Read, List, Create, Delete, Update, Cancel) with resource type extraction (table, dataset, job, model, routine, query, tabledata).
  - `BigQueryClientBuilderExtensions.WithTestTracking()` — Extension method on `BigQueryClientBuilder` for one-line integration.
  - Three verbosity levels: Raw (full HTTP), Detailed (classified labels with content), Summarised (operation names only, no content/headers).
  - Default header filtering for noisy Google API headers (Authorization, x-goog-api-client, etc.).

## [2.1.0] - 2026-04-21

### Added
- **`WithTestInfoFrom()` extension** on `SqlTrackingInterceptorOptions`: Copies `CurrentTestInfoFetcher`, `CurrentStepTypeFetcher`, and `CallingServiceName` from an existing `TestTrackingMessageHandlerOptions` instance. Works with all framework adapters (LightBDD, xUnit3, TUnit, MSTest, BDDfy, ReqNRoll) — no framework-specific subclass needed.
- **`services.AddSqlTestTracking(options)`**: DI extension that registers `SqlTrackingInterceptor` as a singleton in `IServiceCollection`.
- **`builder.WithSqlTestTracking(serviceProvider)` overload**: Resolves the interceptor from DI instead of requiring options to be passed directly. Use with `AddSqlTestTracking()` for cleaner `AddDbContext` callbacks.

## [2.0.0] - 2026-04-21

### Release
- **Official 2.0.0 release** — all beta features stabilized.

## [2.0.175-beta] - 2026-04-21

### Fixed
- **ExpectedTestCount guard was blocking all reports**: The partial-run guard introduced in v2.0.174-beta returned early from `CreateStandardReportsWithDiagrams`, preventing all report generation (TestRunReport, ComponentDiagram, etc.) during partial runs. Now it only disables Specifications (HTML + data) generation — TestRunReport and all other reports still generate normally during filtered/partial test runs.

## [2.0.174-beta] - 2026-04-20

### Changed
- **ExpectedTestCount guard moved to core pipeline**: The partial-run guard that prevents Specifications reports from being overwritten during filtered test runs is now a property on `ReportConfigurationOptions.ExpectedTestCount` and enforced in the core `ReportGenerator.CreateStandardReportsWithDiagrams()`. Previously this was LightBDD-specific (`StandardPipelineFormatter.ExpectedTestCount`). All frameworks (xUnit2/3, NUnit4, TUnit, MSTest, BDDfy, ReqNRoll) can now opt in by setting `options.ExpectedTestCount = () => count`. LightBDD adapters continue to wire this automatically via assembly reflection.

## [2.0.173-beta] - 2026-04-20

### Fixed
- **Per-call activity diagrams showing wrong spans**: `InternalFlowSegmentBuilder.BuildSegments()` now filters spans by the specific request log's `ActivityTraceId` in addition to timestamp windowing. Previously, all spans from all trace IDs within a test were pooled and separated only by timestamps — causing spans from one HTTP call to bleed into another call's popup when timing overlapped or was coarse (Windows ~15.6ms timer resolution). Each call's internal flow popup now correctly shows only spans belonging to that call's W3C trace.
- **Root span excluded from per-call diagrams**: Added a 50ms tolerance before the segment start timestamp to capture the `Kronikol.Request` root span, whose `Activity.Start()` fires before the log's `Timestamp = DateTimeOffset.UtcNow` is recorded. Combined with per-call TraceId filtering, this ensures the tolerance doesn't accidentally include unrelated spans.

## [2.0.172-beta] - 2026-04-20

### Fixed
- **LightBDD specs generation guard**: `StandardPipelineFormatter` now correctly prevents Specifications report generation when fewer tests ran than expected (partial run). The comparison operator was inverted (`>` instead of `<`), allowing partial runs to generate the report.
- **Loading message on sequence diagrams**: Unrendered diagrams inside collapsed features no longer show "Waiting for page load to complete..." after the page has loaded. A `body.plantuml-ready` CSS class now switches the message to "Rendering diagram..." once DOMContentLoaded fires, regardless of IntersectionObserver timing.
- **Note collapse breaking zoom toggle**: Collapsing or expanding notes via the radio buttons re-renders the diagram SVG (destroying the zoom button). The zoom button is now re-added via `requestAnimationFrame` after every note-state re-render.
- **Jump-to-failure scroll position**: The "Next Failure" button now scrolls to the scenario's `<summary>` (title) rather than the `<details>` element, and uses `block: 'start'` so the title is visible at the top of the viewport.
- **Showcase test not skipped**: `ShowcaseReportTests.Showcase_drives_through_report_features_capturing_frames` was running as a regular `[Fact]` on every test run (~60s). Now marked with `Skip` like all other GIF/screenshot generation tests.

## [2.0.171-beta] - 2026-04-20

### Fixed
- Activity diagram popups (internal flow) now correctly set `data-queued` and `data-rendered` attributes, preventing the "Waiting for page load to complete..." loading text from showing indefinitely after the diagram has rendered.
- Zoom toggle button reliability improved: the render callback now triggers `addZoomButton` via `requestAnimationFrame` after SVG insertion, and the IntersectionObserver path also defers to `requestAnimationFrame` to ensure layout is complete before checking diagram dimensions.

## [2.0.170-beta] - 2026-04-20

### Added
- Integration tests verifying specifications reports are blanked when any test fails, are not blanked for skipped/bypassed-only runs, and that the test run report is unaffected by failures.

## [2.0.169-beta] - 2026-04-20

### Changed
- **Breaking:** `ReportConfigurationOptions.FeaturesReportShowStepNumbers` renamed to `TestRunReportShowStepNumbers`.
- **Breaking:** `ComponentDiagramOptions.EmbedInFeaturesReport` renamed to `EmbedInTestRunReport`.
- Renamed all remaining "FeaturesReport" references (comments, test data, test class name) to "TestRunReport" for consistency with the actual report output filename.

## [2.0.168-beta] - 2026-04-20

### Added
- Diagram loading placeholders ("Waiting for page load to complete\u2026" and "Rendering diagram\u2026") now pulse with a gentle fade-in/fade-out animation so they feel alive rather than static.

## [2.0.167-beta] - 2026-04-20

### Changed
- First-state loading placeholder changed from "Waiting for page\u2026" to "Waiting for page load to complete\u2026" for clarity.

## [2.0.166-beta] - 2026-04-20

### Changed
- Diagram loading now shows two distinct states: "Waiting for page load to complete\u2026" while CDN scripts download, then "Rendering diagram\u2026" once the diagram enters the render queue. Previously a single "Loading diagram\u2026" message covered both phases. The `data-queued` attribute is now set when a diagram enters the render queue, while `data-rendered` is set only after the SVG has been fully rendered.

## [2.0.165-beta] - 2026-04-20

### Changed
- PlantUML CDN scripts (`viz-global.js`, `plantuml.js`) now load with the `defer` attribute, allowing the browser to parse and render the HTML report while the scripts download in parallel. Previously these blocking scripts stalled all page rendering. Unrendered diagram containers show a "Loading diagram\u2026" placeholder until the WASM engine is ready.
- Diagram zoom buttons are now added lazily via `IntersectionObserver` instead of eagerly scanning all diagram containers on page load. This eliminates hundreds of forced layout reflows (`getBoundingClientRect`) on large reports. A per-container `MutationObserver` waits for the SVG to render before checking whether the diagram needs a zoom toggle.

## [2.0.164-beta] - 2026-04-20

### Reverted
- Removed search data compression (data-search-z / data-row-search-z) introduced in v2.0.162-beta. The gzip+base64 approach only achieved ~22% reduction on real-world reports (not the projected 80%) and added a 30-second decompression delay on page load for large reports. Search attributes are back to plain text (data-search / data-row-search).

## [2.0.161-beta] - 2026-04-19

### Fixed
- Parameterized group row click no longer hides content when multiple features have parameterized groups — the `pgrp` prefix counter was resetting per feature, causing duplicate HTML element IDs across features. `selectRow()` uses global `document.querySelectorAll`, so clicking a row in one feature’s group would hide/show panels from a different feature’s identically-prefixed group.

## [2.0.160-beta] - 2026-04-19

### Added
- Collapsed diagram notes now show a plus (+) button in the top-right corner that expands the note — mirrors the existing bottom-center ▼ expand button. The minus (−) button remains when notes are expanded or truncated. Clicking either the plus button or the ▼ button returns the note to expanded state and restores the minus button.

## [2.0.159-beta] - 2026-04-19

### Fixed
- Parameterized test groups now correctly display the "Happy Path" badge and CSS class — previously the `happy-path` class was only applied to non-parameterized scenarios, so the "Happy Paths Only" filter button and happy-path styling were broken for parameterized groups

## [2.0.158-beta] - 2026-04-19

### Changed
- **BREAKING**: LightBDD adapter now delegates to the standard `ReportGenerator.CreateStandardReportsWithDiagrams()` pipeline — the same pipeline used by every other framework adapter (xUnit, NUnit, MSTest, TUnit, BDDfy, ReqNRoll). This eliminates a chronic source of feature-parity drift where new options and features had to be wired into both the main pipeline and a parallel LightBDD-specific pipeline.
- Removed `UnifiedReportFormatter`, `UnifiedSpecificationsDataFormatter`, `UnifiedTestRunDataFormatter`, and `PostReportActionsFormatter` — replaced by a single `StandardPipelineFormatter` that calls the shared pipeline once
- `ReportWritersConfigurationExtensions.CreateStandardReportsWithDiagramsInternal` reduced from ~180 lines to ~15 lines
- LightBDD now automatically gets component diagram generation, diagnostics, CI summary/artifacts — features that were previously unavailable in the LightBDD path

## [2.0.157-beta] - 2026-04-19

### Fixed
- LightBDD report generation now respects `GenerateSpecificationsReport`, `GenerateTestRunReport`, `GenerateSpecificationsData`, and `GenerateTestRunReportData` option flags — previously all reports were always generated regardless of these settings
- LightBDD adapter now passes `GroupParameterizedTests`, `MaxParameterColumns`, and `TitleizeParameterNames` options through to HTML report generation — previously these options were silently ignored
- LightBDD test run report title now incorporates `FixedNameForReceivingService` / `ComponentDiagramOptions.Title` via `GetTestRunReportTitle()` — previously always defaulted to "Test Run Report"
- LightBDD schema generation was unreachable dead code (placed after `return` statement in v2.0.156-beta) — moved before the return so it actually executes

### Changed
- `ReportGenerator.GetTestRunReportTitle()` visibility changed from `internal` to `public` so LightBDD adapter can use it

## [2.0.156-beta] - 2026-04-19

### Fixed
- LightBDD adapter now extracts all scenario parameters from `INameInfo.NameFormat`, including parameters substituted inline into the scenario name (e.g. `NewPasscode "{0}"`) — previously only bracket-appended parameters were detected
- LightBDD report formatters now generate reports when running a subset of tests (e.g. single test filtering) — previously the `ExpectedTestCount` guard prevented any output when the count didn't match the full assembly total
- LightBDD adapter now generates `TestRunReport.schema.json` (was missing because the schema writer was not registered in the LightBDD report configuration)

## [2.0.155-beta] - 2026-04-19

### Fixed
- ParameterParser now correctly extracts parameters from multiple separate bracket groups (e.g. `[version: "V1"] [claimName: "Sdes"]` as produced by LightBDD for unmatched inline data parameters)
- ExtractBaseName now strips all trailing bracket groups, not just the last one — fixes parameterized group titles retaining leftover brackets
- "All diagrams identical across test cases" badge no longer displays when there is only one test case in a parameterized group

## [2.0.154-beta] - 2026-04-18

### Fixed
- Three broken documentation links in README (CosmosDB, EF Core Relational, Redis) now point to wiki pages instead of non-existent docs/ files
- .NET targeting statement corrected from ".NET 10.0" to multi-target ".NET 8.0, .NET 9.0, and .NET 10.0"

### Added
- Four missing extension packages added to README Extensions table: Blob Storage, DispatchProxy, MediatR, OpenTelemetry
- Wiki links for the four new extensions added to README Documentation section

## [2.0.153-beta] - 2026-04-18

### Added
- GitHub issue templates (bug report and feature request) with structured forms
- Pull request template with checklist (TDD, tests, docs, changelog, version)
- CodeQL security scanning workflow (runs on push, PR, and weekly schedule)

### Changed
- Added explicit least-privilege `permissions: contents: read` to CI and CI Summary Preview workflows

## [2.0.152-beta] - 2026-04-18

### Changed
- Renamed README title from "Test Tracking Diagrams" to "Kronikol" (PascalCase, matching .NET package naming convention)
- Added TTD icon prefix to README title
- Updated nuget-readme.md title to match

## [2.0.151-beta] - 2026-04-18

### Added
- Default TTD favicon for all HTML reports — reports now show the TTD icon in the browser tab without any configuration
- `DefaultFavicon.DataUri` constant containing the base64-encoded SVG icon
- Favicon added to component diagram reports (previously had no favicon support)

### Changed
- `CustomFaviconBase64` now overrides the default TTD favicon instead of switching between a custom favicon and no favicon

## [2.0.150-beta] - 2026-04-18

### Changed
- Updated NuGet package descriptions across all 15 packages to reflect broader tracking capabilities (HTTP, database, cache, events, and more) instead of just "request-responses"
- Core package description now highlights all supported dependency types (Cosmos DB, SQL via EF Core, Redis, events/messages, arbitrary method calls)

## [2.0.149-beta] - 2026-04-18

### Removed
- Removed incorrect Mermaid references from nuget-readme.md (Mermaid is not currently supported)

## [2.0.148-beta] - 2026-04-18

### Changed
- Updated README.md to reflect all tracking capabilities beyond HTTP — now covers Cosmos DB, EF Core SQL, Redis, TrackingProxy, and events/messages
- Updated nuget-readme.md with the same broader language
- Revised ASCII architecture diagram to show CosmosDB, SQL DB, Redis, and proxy dependencies alongside HTTP and events
- Replaced HTTP-specific wording in How It Works, Use Cases, and Deterministic vs AI-Generated Diagrams sections with inclusive language covering all tracked interaction types
- Step 1 (Intercept) now uses a table summarising all six tracking mechanisms

## [2.0.139-beta] - 2026-04-18

### Added
- `.editorconfig` for consistent code style enforcement
- `.gitattributes` for cross-platform line ending normalisation
- `global.json` to pin .NET SDK version (replaces inline CI hack)
- `CHANGELOG.md` following Keep a Changelog format
- `CONTRIBUTING.md` with development workflow and PR guidelines
- `CODE_OF_CONDUCT.md` (Contributor Covenant v2.1)
- `SECURITY.md` with vulnerability reporting guidance
- NuGet package icon (sequence diagram motif with checkmark)
- XML doc comments on all core public API types: `ReportConfigurationOptions`, `TestTrackingMessageHandlerOptions`, `ComponentDiagramOptions`, `ServiceCollectionExtensions`, `WebApplicationFactoryExtensions`

### Fixed
- Removed copy-pasted `.gitignore` entry referencing unrelated project
- Removed 5 orphaned `Example.Api/` files with unresolved git merge conflict markers
- Removed untracked prototype/PoC files from repository root (`generate-poc.ps1`, `prototype-parameterized-grouping.html`)
- Moved `themes/` directory under `examples/` to match reorganised structure
- Removed internal design documents from tracked `docs/` directory
- Updated copyright year in LICENSE to 2023-2026

### Changed
- CI workflows now use committed `global.json` instead of inline SDK pinning

## [2.0.138-beta] - 2026-04-18

### Fixed
- LightBDD.xUnit3 example: added missing `using Kronikol.LightBDD` for `HappyPathAttribute`, `LightBddTestTrackingMessageHandlerOptions`, and `TrackingDiagramOverride`
- ReqNRoll xUnit2/xUnit3 examples: added `Kronikol.ReqNRoll.Core` to `reqnroll.json` binding assemblies so ReqNRoll discovers `[Binding]` hooks

## [2.0.137-beta] - 2026-04-18

### Changed
- Reorganised repository from flat structure into `src/`, `tests/`, `examples/` directories
- Updated all project references and solution file for new structure

## [2.0.0-beta] - 2026

### Added
- Complete rewrite with multi-framework support (xUnit v2/v3, NUnit 4, MSTest, TUnit)
- BDD framework integrations (BDDfy, LightBDD, ReqNRoll)
- Extension packages for CosmosDB, EF Core Relational, Redis, OpenTelemetry, MediatR, Blob Storage, DispatchProxy
- C4-style component diagram generation
- Interactive HTML reports with search, filtering, and zoom
- PlantUML IKVM package for offline rendering
- CI summary integration (GitHub Actions job summaries)
- Inline SVG rendering option
- Internal flow tracking
- Event annotations
- Custom theme support

## [1.x] - 2023–2025

### Notes
- Initial release series. See [GitHub Releases](https://github.com/lemonlion/Kronikol/releases) for detailed history.

[Unreleased]: https://github.com/lemonlion/Kronikol/compare/v2.0.139-beta...HEAD
[2.0.139-beta]: https://github.com/lemonlion/Kronikol/compare/v2.0.138-beta...v2.0.139-beta
[2.0.138-beta]: https://github.com/lemonlion/Kronikol/compare/v2.0.137-beta...v2.0.138-beta
[2.0.137-beta]: https://github.com/lemonlion/Kronikol/releases/tag/v2.0.137-beta
