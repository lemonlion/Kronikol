# TOGGLE_DEFAULTS_PLAN.md — Configurable start states for every report toggle/radio

**Status: ✅ EXECUTED IN FULL — shipped in 3.0.80 (2026-09-04).** M1–M8 implemented in order,
strictly against this plan, with the §8 recommendations adopted for every open question
(Q2 timeline wins, Q3 no new CLI flags, Q4 Tier 2 IN, Q5 filter selections stay out, Q6 group
wins over the flat `NotePayloadFormat`, Q7 `TestRunReportToggleDefaults`/`SpecificationsToggleDefaults`,
Q8 identical built-ins for both reports). Deviations from the letter of the plan, all recorded in
the 3.0.80 changelog/commit: the M2 five-string extraction had a sixth hidden shape (the
no-sequence-toggle parameterized branch emits filter buttons without the Details radio — preserved
byte-exactly via an else-branch in the builder call site); the internal-flow popup data script is
shared by both HTML files, so a Specifications `InternalFlowTab` override builds a second script
only when it differs; and the single-match search reveal opens rule/steps/background sections
unconditionally (not only when a seed closed them). Kept as a design record; see `PLANS_STATUS.md`.

**Execution notes (for a session picking this up cold):** answer §8 first, then run
M1→M8 in order (each strictly TDD per `CLAUDE.md`, which also holds the Playwright
rules and the release procedure). When line numbers have drifted, re-anchor by
grepping for: `// Global defaults` (the JS literals block), `scenarioNoteFormatSelect`
(the factored-string precedent and comment), `details-active` / `data-state="truncated"`
(toolbar markup), `toggle_expand_collapse` (expand-all buttons), `clear_all_filters`,
`update_url_hash`. The two reports and their shared renderer are mapped in §2.1;
the reference implementation for everything here is the existing `NotePayloadFormat`
chain in §2.3.

## 1. Goal

Every toggle, radio group, and stateful `<select>` in the generated HTML reports gets a
**configurable default start state**, set in code alongside all the other settings
(`ReportConfigurationOptions`). Two layers:

1. **TestRunReport defaults** — override the built-in start state for both reports.
2. **Specifications overrides** — `Specifications.html` **inherits the TestRunReport
   defaults automatically** unless a setting is specifically overridden for
   Specifications.

Resolution chain per setting, most specific wins:

```
built-in (today's hard-coded literal)
  ← TestRunReportToggleDefaults.X        (when non-null)
    ← SpecificationsToggleDefaults.X     (when non-null; Specifications.html only)
```

## 2. Current state (verified inventory)

### 2.1 One renderer, two call sites

Both HTML files come from the same `ReportGenerator.GenerateHtmlReport(...)`
([ReportGenerator.cs:408-445](src/Kronikol/Reports/ReportGenerator.cs#L408-L445)):
Specifications at `:254` (`includeTestRunData: false`, violet stylesheet,
`generateBlankOnFailedTests: true`, no ciMetadata/componentDiagram/diagnostics),
TestRunReport at `:259`. A third consumer is the merge renderer
([MergeableReportRenderer.cs:50](src/Kronikol/Reports/Merge/MergeableReportRenderer.cs#L50)),
which forwards only a **subset** of options (`:67-71`) — every new setting must be added
there too or merged reports silently lose it.

### 2.2 No persistence — every control resets to a hard-coded literal

localStorage persistence was removed (`report-persistent-filter-function.js` is two
no-op stubs; zero `localStorage` hits repo-wide). The only state carry-over is the URL
hash (`q`, `status`, `deps`, `depmode`, `catmode`, `hp`, `cats`, `dur`, `pctl`). So the
configured default IS the start state on every open — there is no stale-state hazard.

Each default is currently a **literal in two places that must stay in sync**:

- the C# markup string (e.g. `details-active` on the `data-state="truncated"` button,
  `<option value="40" selected>`, `data-shown="true"`, presence/absence of `open`);
- the JS `// Global defaults` block at
  [collapsible-notes-script.js:1632-1639](src/Kronikol/Reports/collapsible-notes-script.js#L1632-L1639)
  (`_headersHidden=false`, `_truncateLines=40`, `_detailsDefault='truncated'`,
  `_assertionsVisible=false`, `_stepsVisible=true`, `_databasesVisible=true`,
  `_noteFormatDefault='__NOTE_FORMAT_DEFAULT__'`) plus `var _depMode = 'AND'`
  (`report-dependency-filter-function.js:1`) and `var _catMode = 'OR'`
  (`report-category-filter-function.js:1`).

### 2.3 The one existing pattern: `NotePayloadFormat` (3.0.66)

`ReportConfigurationOptions.NotePayloadFormat`
([ReportConfigurationOptions.cs:321](src/Kronikol/ReportConfigurationOptions.cs#L321))
is today the **only** option that seeds a UI control's start state. Its chain is the
template this plan generalises:

1. enum in `src/Kronikol/Reports/` (first member = default, XML doc `Default: <c>…</c>`);
2. property on the options record;
3. named arg at both `GenerateHtmlReport` call sites (`:254`, `:259`) + merge renderer;
4. **HTML side**: `selected` computed in C# (`ReportGenerator.cs:594-595`);
5. **JS side**: `__NOTE_FORMAT_DEFAULT__` token substituted by
   `GetCollapsibleNotesScript(NotePayloadFormat)`
   ([DiagramContextMenu.cs:102-104](src/Kronikol/Reports/DiagramContextMenu.cs#L102-L104)),
   parameterless overload kept for compatibility (`:95`);
6. CLI flag (`kronikol ingest --note-format`), defaults test, script test
   (`NoteFormatToggleScriptTests`), Playwright suite (`NoteFormatDefaultTests`), wiki row.

Because it is threaded as one shared value, `NotePayloadFormat` **cannot differ between
the two reports today** — this plan fixes that for it and everything else.

### 2.4 Control inventory and scope

**Tier 1 — in scope** (stateful toggles/radios/selects with a meaningful start state):

| # | Control | Built-in default | C# emit sites | JS mirror |
|---|---|---|---|---|
| 1 | Details radio: Expand / Collapse / **Truncate** | `truncated` | `ReportGenerator.cs:936` + 5 scenario toolbar strings (`:1336`, `:1352`, `:1377`, `:2344`, `:2360`) | `_detailsDefault`, script `:1635` |
| 2 | Truncate-lines `<select>` | **40** | same 6 strings (`<option value="40" selected>`) | `_truncateLines`, `:1634` |
| 3 | Headers Shown/Hidden | **Shown** | `:937` + 5 | `_headersHidden`, `:1633` |
| 4 | Assertions Shown/Hidden | **Hidden** | `:939` + 4 (gated `hasAssertionNotes`, `:574-576`) | `_assertionsVisible`, `:1636` |
| 5 | Steps Shown/Hidden (delimiter bars) | **Shown** | `:941` + 4 (gated `hasStepDelimiters`) | `_stepsVisible`, `:1637` |
| 6 | Databases Shown/Hidden | **Shown** | `:943` + 4 (gated `hasDatabaseParticipants`) | `_databasesVisible`, `:1638` |
| 7 | Note format JSON⇄YAML `<select>` | **JSON** (via existing option) | `:594-601`, `:944` + 5 append sites (gated `hasJsonNotePayloads`, `:586-588`) | `_noteFormatDefault`, `:1639` |
| 8 | Features expanded/collapsed (+ "Expand All Features" button label) | **collapsed** | `open` absent at `:1090`; label at `:927` | — |
| 9 | Scenarios expanded/collapsed (+ button label) | **collapsed** | `open` absent at `:1250`, `:2002`; label at `:927` | — |
| 10 | Diagram-type tab: **Sequence** / Activity / Flame Chart | Sequence (Activity when no seq view) | active class at `:1329-1333`, `:1372-1373`, `:2338-2342`, `:2375-2376`; `display:none` on inactive views at `:1444-1450`, `:2437`, `:2471` | — |
| 11 | Scenario Timeline panel | **hidden** | button `:928-929` (gated `hasDurations`), panel `display:none` at `:1051` | inline JS `:509-517` |
| 12 | Component Diagram panel *(TestRunReport only; gated on the `componentDiagramPlantUml` arg / `GenerateComponentDiagram`)* | **hidden** | button `:930-931`, panel `:1080` | inline JS `:520-539`; mutual exclusion `:497-508` |
| 13 | Dependency filter mode | **AND** | button text `:893` | `_depMode`, `report-dependency-filter-function.js:1` |
| 14 | Category filter mode | **OR** | button text `:910` | `_catMode`, `report-category-filter-function.js:1` |

Controls 1–7 exist only when `PlantUmlRendering == BrowserJs` (report toolbar
`:933-946`) — under any other rendering mode their options resolve normally but are
inert (no control is emitted), and their wiki rows must say "BrowserJs only". A gated
control that is absent (no assertion notes, no durations, …) likewise makes its option
inert for that report. Their scenario-level twins are pasted **five times verbatim** in
`ReportGenerator.cs` — only the note-format `<select>` was factored out
(`scenarioNoteFormatSelect`, `:589-601`, with the "built once … cannot drift" comment).
§5 M2 mandates extracting the whole scenario toolbar string the same way before any new
defaults touch it.

**Tier 2 — in scope only if Q4 answers yes** (`<details>` disclosures and secondary
view toggles):

| Control | Built-in | Site |
|---|---|---|
| Features Summary `<details>` *(TestRunReport only)* | closed | `:698` |
| Failure clusters `<details>` *(TestRunReport-effective)* | open | `:1019` |
| Steps section `<details.scenario-steps>` | open | `:2618` |
| Diagrams section `<details.example-diagrams>` | open | `:1323`, `:2325` |
| Report diagnostics `<details>` *(TestRunReport only)* | closed | `:3253` |
| Rules `<details.rule>` | open | `:1148` |
| Background Steps `<details.scenario-background>` (gated on `SeparateBackgroundSteps`) | closed | `:2577` |
| Raw PlantUML `<details.example>` (non-BrowserJs modes only) | closed | `:1425`, `:2536` |
| Parameterized table view flat/grouped | flat | `:2015`, `:2098-2105` |
| Internal-flow popup tab Activity/Flame | Activity | `InternalFlowHtmlGenerator.cs:80-86`, `:252-254` |

**Out of scope** (see §4): filter *selections* (status chips, dependency/category
chips, Happy Paths, duration/percentile, search text), action buttons, per-note ±/▲/▼
glyphs (derived from note state), zoom, mobile responsive disclosures, search-help
panel, `details.failure-result` (open state is load-bearing: the 3.0.72 deep-search
stack-trace verify reads its rendered `<pre>` — see `SEARCH_INDEX_PLAN.md` and the
3.0.72 changelog entry — and the single-match reveal path does not force it open), `details.step-collapsible` (open iff the step failed — behaviour,
not a default), the parameterized **row-0 selection** (`:2037` — row identity is
data-dependent; scenario permalinks already select rows via the hash), sort state,
keyboard nav.

## 3. Design

### 3.1 Options surface

New file `src/Kronikol/Reports/ReportToggleDefaults.cs`:

```csharp
namespace Kronikol;

/// <summary>Default start states for the interactive controls in the generated HTML
/// reports. Every property is nullable; null means "inherit" (built-in default for
/// <see cref="ReportConfigurationOptions.TestRunReportToggleDefaults"/>, the effective
/// TestRunReport value for
/// <see cref="ReportConfigurationOptions.SpecificationsToggleDefaults"/>).</summary>
public record ReportToggleDefaults
{
    public ReportDetailsState? Details { get; set; }            // Default: Truncated
    public TruncateLineCount? TruncateLines { get; set; }       // Default: Lines40
    public bool? HeadersShown { get; set; }                     // Default: true
    public bool? AssertionsShown { get; set; }                  // Default: false
    public bool? StepsShown { get; set; }                       // Default: true
    public bool? DatabasesShown { get; set; }                   // Default: true
    public NotePayloadFormat? NotePayloadFormat { get; set; }   // Default: null → flat option
    public bool? FeaturesExpanded { get; set; }                 // Default: false
    public bool? ScenariosExpanded { get; set; }                // Default: false
    public DiagramTabKind? DiagramTab { get; set; }             // Default: Sequence (w/ fallback)
    public bool? ScenarioTimelineVisible { get; set; }          // Default: false
    public bool? ComponentDiagramVisible { get; set; }          // Default: false (TestRunReport only)
    public FilterCombinationMode? DependencyFilterMode { get; set; } // Default: And
    public FilterCombinationMode? CategoryFilterMode { get; set; }   // Default: Or
    // Tier 2 (Q4), matching the ten rows of the §2.4 second table:
    // FeaturesSummaryOpen, FailureClustersOpen, StepsSectionOpen, DiagramsSectionOpen,
    // DiagnosticsOpen, RulesOpen, BackgroundStepsOpen, RawPlantUmlOpen,
    // ParameterTableView (enum Flat/Grouped), InternalFlowTab (enum Activity/FlameChart)
}
```

Place the file beside `NotePayloadFormat.cs` and use the **same namespace** as that
enum (so existing user code needs no new `using`); verify at implementation time —
the options record itself is `namespace Kronikol`.

New enums in `src/Kronikol/Reports/` (first member = built-in default, per the
`NotePayloadFormat` convention): `ReportDetailsState { Truncated, Expanded, Collapsed }`,
`DiagramTabKind { Sequence, Activity, FlameChart }`,
`FilterCombinationMode { And, Or }` (note: the *category* built-in is `Or` — built-ins
live in the resolver, not in enum ordering, so one shared enum is fine; the doc comment
on each property states its own default), and `TruncateLineCount { Lines3 = 3,
Lines4 = 4, Lines5 = 5, Lines10 = 10, Lines15 = 15, Lines20 = 20, Lines25 = 25,
Lines30 = 30, Lines35 = 35, Lines40 = 40, Lines50 = 50, Lines60 = 60, Lines80 = 80,
Lines100 = 100 }` — one member per dropdown row, in numeric order with **explicit
underlying values** so `(int)` gives the number the markup and the
`__TRUNCATE_LINES_DEFAULT__` token need. `TruncateLineCount` deviates from
first-member-default like `FilterCombinationMode` does (its default `Lines40` lives in
the resolver), and the enum itself is the single source of truth for the dropdown: the
markup emitter builds the `<option>` list from `Enum.GetValues<TruncateLineCount>()`
instead of the six hand-written lists in the toolbar strings today. C# enums are not
closed types (`(TruncateLineCount)37` compiles), so the resolver still guards with
`Enum.IsDefined` and throws naming the valid members.

On `ReportConfigurationOptions` (follows the `ComponentDiagramOptions` nested-group
precedent rather than adding 28 flat properties for Tier 1 alone — 14 settings × 2
report kinds — to a ~90-property record):

```csharp
/// <summary>Default start states for report controls. Applies to the HTML test run
/// report AND, unless overridden via SpecificationsToggleDefaults, to
/// Specifications.html. Default: all unset (built-in defaults).</summary>
public ReportToggleDefaults TestRunReportToggleDefaults { get; set; } = new();

/// <summary>Specifications.html overrides. Any property left null inherits the
/// effective TestRunReport value. Default: all unset (full inheritance).</summary>
public ReportToggleDefaults SpecificationsToggleDefaults { get; set; } = new();
```

Both initialized non-null so user code composes without `??=` noise. Property naming
follows the existing flat-pair convention (`TestRunReport*` / `Specifications*`,
cf. `…ShowStepNumbers`, `…DataFormat`) — final names are Q7.

### 3.2 Resolution

New `src/Kronikol/Reports/ReportToggleDefaultsResolver.cs` mirroring
`SqlResponseDetailResolver` ([SqlResponseDetail.cs:26-39](src/Kronikol/Sql/SqlResponseDetail.cs#L26-L39)):

```csharp
public static ResolvedToggleDefaults Resolve(ReportConfigurationOptions options, bool specifications)
```

returns a fully non-nullable `ResolvedToggleDefaults` record — the same property list
as `ReportToggleDefaults` but with the `?` removed, plus a
`public static ResolvedToggleDefaults BuiltIn { get; }` instance holding today's
literals (Truncated / `Lines40` / headers+steps+databases shown / assertions hidden /
Json notes / everything collapsed / Sequence tab / panels hidden / dep `And` /
cat `Or`). This record is the **single source of truth** for the built-ins — the
literals currently duplicated in C# markup and JS move HERE. Resolution:
`BuiltIn`, overlaid by `TestRunReportToggleDefaults`, then (when `specifications`) by
`SpecificationsToggleDefaults`. `NotePayloadFormat` resolves as
`toggle-group value ?? options.NotePayloadFormat` (the flat property keeps working and
remains the simple knob; the group wins when set — Q6). The resolver is `public` so the
CLI/merge paths and tests use the same arithmetic; nothing downstream of
`GenerateHtmlReport` ever sees a null.

Settings that don't apply to a given output (`ComponentDiagramVisible` in
Specifications, Tier-2 TestRunReport-only sections) resolve normally and are simply
ignored by the renderer — same tolerance the codebase shows elsewhere (e.g. BrowserJs
options with a different renderer).

### 3.3 Renderer seam

`GenerateHtmlReport` gains **one** optional parameter,
`ResolvedToggleDefaults? toggleDefaults = null` (null → `ResolvedToggleDefaults.BuiltIn`),
NOT fourteen — the ~40-optional-parameter signature is already at its limit. Call
sites: `:254` passes `Resolve(options, specifications: true)`, `:259`
`Resolve(options, specifications: false)`,
`MergeableReportRenderer.cs:67-71` resolves from its options record, and
`IngestPipeline`/`kronikol ingest`/`merge` inherit via the options record they already
carry (`IngestRequest.Options`,
[IngestPipeline.cs:36](src/Kronikol/Ingestion/IngestPipeline.cs#L36); flags applied
onto the record in `src/Kronikol.Tool/IngestCommand.cs:255-270`). The existing `notePayloadFormat` parameter **stays** (compat; several tests and
`ReportTestHelper` call it directly) but the resolved record wins when provided —
internally `GenerateHtmlReport` folds the legacy param into the record when
`toggleDefaults` is null.

### 3.4 C# markup: compute instead of hard-code

- **Factor the scenario toolbar first** (M2): one `BuildScenarioDiagramToolbar(resolved,
  hasAssertions, hasSteps, hasDatabases, …)` helper replaces the five verbatim strings,
  with the same "built once — cannot drift" contract as `scenarioNoteFormatSelect`.
  The M2 two-part byte-identity pin (§5) proves the extraction is inert.
- Report toolbar `:936-944` and the factored scenario toolbar compute: which
  `details-radio-btn` carries `details-active`; which truncate-lines `<option>` carries
  `selected`, with the whole `<option>` list built from `TruncateLineCount` members
  (Q1 — decided); `data-shown` + label text + `details-active`
  for Headers/Assertions/Steps/Databases.
- **`disabled` coupling** (verified): `syncRadioButtons` disables every
  `.truncate-lines-select` whenever the details state is not `truncated`
  ([collapsible-notes-script.js:2193-2202](src/Kronikol/Reports/collapsible-notes-script.js#L2193-L2202)).
  A non-`Truncated` `Details` default must therefore also emit `disabled` on all six
  select sites, or the markup and JS disagree until the first click.
- `details.feature`/`details.scenario`/`details.scenario-parameterized` emit `open`
  when `FeaturesExpanded`/`ScenariosExpanded`; the `:927` button labels seed to the
  matching flip state ("Collapse All Features" when features start expanded) —
  `report-collapse-expand-all-function.js` toggles by label, so seed both sides.
- Diagram tab: active class + `display:none` computed per scenario as
  *requested tab if that view exists for this scenario, else today's fallback order*
  (seq → activity). Flame is only requestable where it exists
  (`InternalFlowShowFlameChart` + `BehindWithToggle`).
- Timeline/Component: seeding visible = drop the inline `display:none` on the panel
  (`:1051` / `:1080`) + seed the button's active class. Both-visible conflict: Q2.
- Dependency/category mode button text at `:893`/`:910` computed.
- Every seeded control keeps `autocomplete="off"` — the 3.0.68 rule, REQUIRED on any
  control ReportGenerator emits: Firefox silently restores `<select>`/`<input>` values
  across a plain reload (Chromium does not), and a restored stale value on top of a
  configured default is unclearable by the reader. Guard suite:
  `tests/Kronikol.Tests.EndToEnd/FormStateRestorationTests.cs`.

### 3.5 JS seeding: tokens, and the URL-hash trap

Extend the `GetCollapsibleNotesScript` token substitution
(`DiagramContextMenu.cs:102-104`; same mechanism as `__BROWSER_RENDER_WORKERS__`,
`:61-65`) to the whole globals block: `__DETAILS_DEFAULT__`,
`__TRUNCATE_LINES_DEFAULT__`, `__HEADERS_HIDDEN_DEFAULT__` (note the polarity flip:
the global is `_headersHidden`, the option is `HeadersShown`),
`__ASSERTIONS_VISIBLE_DEFAULT__`, `__STEPS_VISIBLE_DEFAULT__`,
`__DATABASES_VISIBLE_DEFAULT__`. Signature grows to take `ResolvedToggleDefaults`;
keep a compatibility overload preserving old behaviour (precedent
`DiagramContextMenu.cs:95`). Each HTML file substitutes its own resolved values —
per-report divergence falls out for free since each file embeds its own script copy.

`report-dependency-filter-function.js:1` / `report-category-filter-function.js:1` get
`__DEP_MODE_DEFAULT__` / `__CAT_MODE_DEFAULT__` tokens (these load through
`ReportGenerator.LoadResource` — add the substitution at that seam). Concrete shape:
each file opens with `var _depModeDefault = '__DEP_MODE_DEFAULT__';
var _depMode = _depModeDefault;` (resp. `_catModeDefault`/`_catMode`), and every other
consumer reads the `*Default` global instead of a literal. The filter modes
actually have **three** JS sites, not one: the `var` initialisers, the hash functions
below, and **`clear_all_filters`**, which resets `_depMode = 'AND'` / `_catMode = 'OR'`
with hard-coded literals
([report-export-function.js:23-33](src/Kronikol/Reports/report-export-function.js#L23-L33)) —
left as-is, "Clear All" would silently un-configure the report. All three read the
seeded `_depModeDefault`/`_catModeDefault` globals after this plan.

Load-order precedence (verified): `parse_url_hash` runs on DOMContentLoaded
(`report-init-script.js:3-5`), i.e. AFTER the seeded globals are set — so the effective
precedence is **URL hash > configured default > built-in**, which is the desired
deep-link behaviour and gets an explicit E2E pin.

**URL-hash functions currently assume the built-ins** (verified at baseline):
`update_url_hash` writes `depmode` only when `_depMode !== 'AND'` and `catmode` only
when `_catMode !== 'OR'` ([report-url-hash-function.js:11-12](src/Kronikol/Reports/report-url-hash-function.js#L11-L12));
`parse_url_hash` only accepts `depmode === 'OR'` / `catmode === 'AND'` (`:69-78`).
Required changes, or configured modes break deep links:

- write side compares against seeded `_depModeDefault`/`_catModeDefault` globals;
- parse side accepts **both** values symmetrically (a link from an AND-default report
  must force AND on an OR-default report) and updates the button text either way.

This is the one place a configured default changes observable behaviour of an
*existing* surface even for users who never set the option (hash grammar gains
`depmode=AND`/`catmode=OR` as valid, previously-ignored values) — changelog-worthy.

### 3.6 Zero-click rendering is part of the contract; defaults govern INITIAL state only

Seeding is not cosmetic: `_detailsDefault`/`_assertionsVisible`/etc. drive the lazy
BrowserJs render pipeline (`_preProcessSource`, note stripping, truncation), so a
configured default must produce the right rendering **without any clicks** — exactly
what `NoteFormatDefaultTests` pins for note format. Every Tier-1 control gets the same
three-fact E2E shape: zero-click render honours the seed; the toolbar control shows the
seeded state; toggling away and back still works.

**Defaults are initial-state only.** Verified: search actively mutates `open` states —
it collapses non-matching scenarios and force-opens a single match plus its
`details.example-diagrams`
([report-search-function.js:27, 64-66, 154-156](src/Kronikol/Reports/report-search-function.js#L64-L66)) —
and `clear_all_filters` does NOT restore `open` states. So after any interaction, the
page state is whatever the interaction leaves, exactly as today; configured defaults
are not a "restore point". E2E pins the initial state only. (A "return to configured
defaults" button is explicitly a non-goal; revisit only if asked.)

## 4. Explicitly out of scope (and why)

- **Filter selections** (status chips, dependency/category chips, Happy Paths,
  percentile/duration, search text): content-dependent sets, already shareable via URL
  hash; a "default filter" is a different feature (saved views) with its own UX
  questions. Revisit on request.
- **Category "All" chip**: its ON state is the emergent "no chips selected" state —
  covered by the above.
- **Per-note glyphs (±/▲/▼, Y/J)**: derived from note state / `_noteFormatDefault`;
  already follow the configured defaults.
- **Zoom slider**: value is computed fit-to-width per diagram, not a stored default.
- **Mobile disclosures, search-help panel, back-to-top**: responsive/transient UI.
- **Browser-side persistence (localStorage)**: deliberately removed once; re-adding is
  orthogonal to generation-time defaults and conflicts with the Firefox 3.0.68 lesson.
- **TOOLBAR_REDESIGN_PLAN interplay**: that plan reshapes the toolbar *markup*; this
  plan's options are semantic states, resolved into one record — the redesign consumes
  `ResolvedToggleDefaults` instead of literals and both plans stay independent (§7).

## 5. Milestones (each strictly TDD: red → green → refactor)

**M1 — Options surface + resolver.**
Enums, `ReportToggleDefaults`, the two properties (XML docs with `Default:` lines),
`ReportToggleDefaultsResolver` + `ResolvedToggleDefaults` (built-in constants live
here). Tests first: `ReportConfigurationOptionsDefaultsTests` additions (groups
non-null, all props null); new `ReportToggleDefaultsResolverTests` modelled on
`SqlResponseDetailResolverTests` — built-in passthrough, test-run override, spec
inherits test-run, spec overrides test-run, mixed per-property independence,
NotePayloadFormat flat-vs-group precedence.

**M2 — Seam + drift-killer refactor + byte-identity pin.**
Factor the five scenario toolbar strings into the single builder; add the
`toggleDefaults` parameter; wire both call sites + merge renderer. The pin comes in two
parts, because the HTML embeds `KronikolVersion` (meta generator `:627` + hidden table
row `:778`, sourced from `AssemblyInformationalVersion` at `:20-22`) so a **checked-in
golden would rot every release**:

- *Committed, permanent* — same-run A/B (precedent: the EXAMPLES_BLOCKS nulled-baseline
  byte-equality test, recorded in `PLANS_STATUS.md`): call `GenerateHtmlReport` three
  ways on identical inputs — (a) `toggleDefaults: null`, (b)
  `toggleDefaults: ResolvedToggleDefaults.BuiltIn`, (c)
  `toggleDefaults: ReportToggleDefaultsResolver.Resolve(new ReportConfigurationOptions(), …)`
  — and assert all three produce **byte-identical** HTML, for both the Specifications
  and TestRunReport argument shapes (§2.1). This pins "unset config = zero effect"
  forever and is the safety net for every later milestone.
- *Transient, dev-time only* — capture both HTML outputs from the pre-refactor build
  once, diff against the post-factoring build to prove the five-string extraction is
  inert, then discard the capture (not committed).

**M3 — Diagram-toolbar group (controls 1–7).**
JS tokens + `GetCollapsibleNotesScript` overload; C# computed markup (report + factored
scenario toolbar). Unit: token substitution + no `__…__` leak (mirror
`NoteFormatToggleScriptTests.Note_format_default_token_is_substituted`), per-control
markup assertions, gating respected (`hasAssertionNotes` etc.). E2E per control (§3.6
three-fact shape), plus scenario-level twins seeded consistently, plus one
inheritance fact: single options object, spec override on (say) `Details`, assert the
two generated files seed differently. Ride-along nit: the `parseInt(sel.value, 10) || 20`
fallbacks in `_setTruncateLines`/`_setScenarioTruncateLines`
(`collapsible-notes-script.js:2246`, `:2269`) hard-code 20 — route them through the
seeded default while in the file.

**M4 — Structure (controls 8–9).**
`open` emission + button-label seeding; expand-all buttons still flip correctly from a
seeded-expanded start. E2E includes keyboard nav/permalink still working with
everything expanded. Perf note: expanded-by-default fixtures stay small; do NOT raise
`BrowserRenderWorkerTests` budgets (they are contention-scaled since 3.0.69 — a
regression here is real).

**M5 — Panels + diagram tab (controls 10–12).**
Per-scenario tab fallback logic unit-tested against scenarios with/without seq, with
flame present/absent; timeline/component visibility seeding + mutual exclusion rule
(Q2) pinned; component setting inert in Specifications (asserted).

**M6 — Filter modes (controls 13–14) + hash grammar + Clear All.**
Tokens in the two filter scripts AND `clear_all_filters` (all three sites, §3.5);
`update_url_hash`/`parse_url_hash` per §3.5. E2E: deep-link from a default-modes
report opens correctly on a configured-modes report and vice versa; hash omits the
mode when it equals the *configured* default; hash beats configured default on load;
**Clear All resets the modes to the configured defaults**, not to AND/OR.

**M7 — Tier 2 (GATED on Q4).**
The ten §2.4 Tier-2 defaults, same pattern; `ParameterTableView` also seeds which
table gets `display:none` (`:2098-2100`) and the `−`/`+` header state. Rule: any
disclosure default that can HIDE content the deep-search index covers must extend the
single-match reveal path — today it force-opens only `details.example-diagrams`
(`report-search-function.js:64-66`); a reveal into a closed-by-default section must
open that section too, pinned per setting.

**M8 — Docs, parity, release.**
Wiki (at `../Kronikol.wiki`): `Report-Configuration.md` rows for both group properties
(BrowserJs-only markers per §2.4) + a new "Toggle default start states" section
documenting the inheritance chain, the initial-state-only semantics (§3.6), and a
Specifications-differs example; `Generated-Reports.md` cross-reference. CHANGELOG
(include the hash-grammar note from §3.5). README/nuget-readme only if the package
table blurbs mention configurability. Kronikol4J: divergence-ledger entry (options
surface + `collapsible-notes-script.js`/filter-scripts/markup all diverge further; the
port is pinned to 3.0.43 assets). Add the V4 unification note from §7 to `V4_PLAN.md`.
Then the standard release procedure per CLAUDE.md: full suite green (all project runs —
unit + SearchEngine + E2E), same version bump in ALL packages, template pins bumped to
the previous release (house convention), changelog, commit, tag `v{version}`, push
commit + tag.

## 6. Test matrix (summary)

| Layer | What | Where |
|---|---|---|
| Unit | option defaults; resolver chain (per property × 3 layers); flat-NotePayloadFormat precedence; `TruncateLineCount` `Enum.IsDefined` guard rejects undefined casts | `Kronikol.Tests/ReportConfigurationOptionsDefaultsTests.cs`, new `ReportToggleDefaultsResolverTests.cs` |
| Unit | byte-identical default output, same-run A/B across null/BuiltIn/empty-groups (both reports) — the M2 pin | new `ToggleDefaultsBaselineTests.cs` |
| Unit | per-control markup: active classes, `selected`, `open`, labels, `data-shown`; token substitution, no `__…__` leak; merge renderer forwards | `Kronikol.Tests/Reports/` (mirror `NoteFormatToggleScriptTests`, `ReportToggleTests` `MakeOptions` helper) |
| E2E | per Tier-1 control: zero-click render honours seed / control seeded / toggle round-trip (mirror `NoteFormatDefaultTests`, fixture via `ReportTestHelper` passing the options record) | `tests/Kronikol.Tests.EndToEnd/` |
| E2E | inheritance: one options object → both files via the full `CreateStandardReportsWithDiagrams` pipeline (precedent: `ReportToggleTests`), spec override diverges, unset props match | new `SpecificationsToggleInheritanceTests.cs` |
| E2E | hash round-trip under non-default filter modes; hash-beats-default on load; Clear All resets to configured defaults; Firefox restoration guard untouched (`FormStateRestorationTests`) | existing suites extended |
| E2E | Export Filtered HTML smoke under non-default seeds (export clones `head` scripts with substituted tokens + current-state DOM, `report-export-function.js:41-58`) | existing export tests extended |
| E2E | deep-search reveal on content stripped by a seeded-hidden state renders the documented behaviour (§7) | search suites extended |
| Perf | full E2E incl. `BrowserRenderWorkerTests` — budgets unchanged | CI |

All Playwright work follows CLAUDE.md rules (no `Force=true`, `PollingInterval = 200`,
`dispatchEvent` for SVG, `.First`/`.Nth`, `FillSearchBar`).

## 7. Risks & interactions

- **Five-fold toolbar duplication** is the biggest foot-gun; M2 removes it before any
  default becomes computable. The byte-identity pin proves the refactor is inert.
- **URL-hash grammar** changes even for non-adopters (§3.5) — small, but call it out.
- **Pinned signatures**: `buildSourceWithNoteStates`'s 5-param JS signature is
  regex-pinned — we only touch the globals block, not that signature. Do not thread new
  params through it; globals + tokens only.
- **`GenerateHtmlReport` compat**: the legacy `notePayloadFormat` param stays; direct
  callers (tests, `ReportTestHelper`, merge) keep compiling.
- **Firefox form restoration** (3.0.68): every seeded `<select>`/control keeps
  `autocomplete="off"`; a restored stale value on top of a *configured* default is the
  exact bug class 3.0.68 closed — the E2E guard already exists, keep it green.
- **Expanded-by-default perf**: opening all `<details>` on a huge report makes lazy
  diagram rendering start immediately for the viewport; document in the wiki that
  `ScenariosExpanded = true` trades first-paint work for click-free reading. No budget
  changes.
- **TOOLBAR_REDESIGN_PLAN / V4**: keep option names semantic (states, not markup);
  the redesign later consumes `ResolvedToggleDefaults` wherever it re-emits controls.
  V4 may fold the flat `NotePayloadFormat` into the group (breaking) — note in V4_PLAN.
- **Deep search vs seeded-hidden content**: the deep index is generation-time and
  state-independent, so a hit can land on content a seeded state strips from the
  rendered diagram (e.g. database notes with `DatabasesShown = false`). This behaviour
  class already exists today — assertions are hidden by default and their text behaves
  the same way — configurability just widens it. Documented + E2E-pinned as expected
  behaviour, not fixed; the wiki section says which toggle re-reveals the content.
- **Two Specifications conventions coexist** after this plan: the existing flat pairs
  (`SpecificationsShowStepNumbers`, `SpecificationsDataFormat`, …) are independent
  values that do NOT inherit, while the new group inherits-unless-overridden. Accepted
  for v3 (changing the flat pairs is breaking); note in `V4_PLAN.md` as a unification
  candidate alongside the flat-`NotePayloadFormat` fold-in (Q6).
- **Merged reports** resolve toggle defaults from the *merge-time* options record, not
  from the source reports — same as every other option on the merge path; wiki notes it.
- **Kronikol4J**: parity break widens (script assets + markup + options surface);
  ledger entry is a deliverable (M8), port work is not.

## 8. Open questions & recorded decisions (Q2–Q8 need answers before green-light)

1. **Truncate-lines: representation — DECIDED (user, 2026-09-04).** The setting picks
   one of the dropdown's EXISTING states; off-preset values are unrepresentable by
   design and dynamic injection of extra `<option>` rows is rejected. Form:
   **`TruncateLineCount?` enum** with one member per dropdown row and explicit
   underlying values (`Lines3 = 3` … `Lines100 = 100`), defined in §3.1. The enum is
   the single source of truth for the dropdown — the markup emitter enumerates its
   members instead of the six hand-written option lists in today's toolbar strings —
   and the resolver guards casts like `(TruncateLineCount)37` with `Enum.IsDefined`
   (C# enums are not closed types), failing report generation with an error naming
   the valid members. Trade-off accepted knowingly: adding/removing a preset is a
   breaking API change; the `int?`-validated alternative was considered and declined.
2. **`ScenarioTimelineVisible` and `ComponentDiagramVisible` both true**: they are
   mutually exclusive panels. Recommendation: Timeline wins (leftmost control, exists
   in both reports), Component stays one click away; pinned by test, no throw.
3. **CLI exposure**: `kronikol ingest`/`merge` reach the options record via
   `IngestRequest.Options` already. Recommendation: no new flags this pass
   (`--note-format` stays); a `--toggle-default name=value` repeatable flag is a
   follow-up if asked.
4. **Tier 2 in or out** (§2.4 second table — `<details>` sections, flat/grouped table,
   internal-flow tab)? Recommendation: in — marginal cost once M1–M3 seams exist, and
   "all the toggles" reads as including them.
5. **Filter start states stay out of scope** (§4) — confirm.
6. **Flat `NotePayloadFormat` precedence**: group value wins when set (recommended),
   flat property remains the simple both-reports knob. Alternative: obsolete the flat
   property now (noisier; better saved for V4).
7. **Naming**: `TestRunReportToggleDefaults` / `SpecificationsToggleDefaults` with type
   `ReportToggleDefaults` (recommended — matches the flat-pair prefix convention), or
   shorter `ToggleDefaults` / `SpecificationsToggleDefaults`?
8. **Should Specifications' violet stylesheet ever imply different built-ins** (e.g.
   Specifications historically shows step numbers where TestRunReport doesn't)?
   Recommendation: no — built-ins stay identical for both reports; anything
   Specifications-specific is an explicit override in user code, keeping the
   inheritance story simple.
