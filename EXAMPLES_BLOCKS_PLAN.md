# Examples Blocks Plan — named `Examples:` groups in parameterized tables

## Status: IMPLEMENTED in full, shipped in 3.0.64

All milestones (M1–M6) landed as designed. Deviations from the plan as written:

- The stylesheet has no dark-mode section — the plan's "`stylesheets.css:1586+` region" is the
  responsive `@media` block, not a theme. The `.examples-block-row` styles were added to the
  single (light) theme only; nothing else in the report is theme-aware either.
- M2's fixture extension: the golden `playwright-bdd-9.2-messages.ndjson` is captured real-run
  data, so instead of hand-editing it, the multi-block coverage builds a synthetic
  `CucumberMessages` object in-test (`CucumberExamplesBlockTests`) plus a deserialization test
  for the new `description` field; the golden fixture pins the single-unnamed-block case.
- M3's spike: `PickleIdIndex` is a 0-based index into the feature's pickle list in feature-file
  order (the generated code passes "0" for the file's first scenario; outline rows continue
  counting). Verified against the real Reqnroll 3.3.4 pipeline — the split Muffins outline
  renders both named bands with correct counts and description. `PickleId` (runtime-populated)
  is preferred over the index when present; the value cross-check makes either route fail safe.
- Shipped as part of the combined 3.0.64 release (CI overhaul + ReqNRoll duplicate-steps #71
  were pending in the same working tree), not as its own patch version.

## Problem

A Gherkin scenario outline may have **multiple `Examples:` blocks**, each with an optional
name and description:

```gherkin
Scenario Outline: Market share movement is reported
    ...

    Examples: the merchant gained share
        | Period      | SharePercent | PriorSharePercent | Change |
        | OneWeek     | 42.50        | 40.00             | 2.50   |
        | OneYear     | 55.00        | 50.00             | 5.00   |

    Examples: the merchant lost share
        | Period      | SharePercent | PriorSharePercent | Change |
        | FourWeeks   | 42.50        | 45.00             | -2.50  |
        | OneMonth    | 38.20        | 41.70             | -3.50  |

    Examples: the merchant held its share
        | Period      | SharePercent | PriorSharePercent | Change |
        | RollingYear | 54.00        | 54.00             | 0.00   |
```

Kronikol's HTML report currently flattens all rows of an outline into **one** parameter
table: the division into blocks and the block names ("the merchant gained share") are lost.
Nothing in the pipeline even captures which block a row came from.

**Chosen design: separator band rows** — keep the single parameter table (one header, aligned
columns) and emit a full-width band row between the row groups carrying the block name,
description, and a per-block pass/fail count.

## Current architecture (as of 3.0.60)

Pipeline: framework adapters produce scenarios carrying `OutlineId` + `ExampleValues`;
`ParameterGrouper` groups scenarios sharing an `OutlineId` into a `ParameterizedGroup`;
`ReportGenerator.RenderParameterizedGroup` renders one collapsible section with a pivot table.

Key locations:

| What | Where |
|---|---|
| Scenario model | `src/Kronikol/Reports/Scenario.cs` (`OutlineId`, `ExampleValues`, `ExampleRawValues`, `ExampleFlatValues`, `ExampleDisplayName`) |
| Grouping | `src/Kronikol/Reports/ParameterGrouper.cs:38-56` (by `OutlineId`), `BuildGroup` at `:84`; model `src/Kronikol/Reports/ParameterizedGroup.cs` |
| Rendering | `src/Kronikol/Reports/ReportGenerator.cs`, `RenderParameterizedGroup` at `:1661`. Section `<details class="scenario scenario-parameterized">` at `:1751`; **flat** table `:1759-1833` (shown when `FlatParameterNames` present); **grouped** table `:1838-1960` (hidden behind the flatten toggle when a flat table exists); detail panels `:1964-2013` with ids `{prefix}-detail-{ri}` keyed by the member's index `ri` |
| Section search text | `ReportGenerator.cs:1701-1718` (`data-search`); per-row `data-row-search` at `:1892-1903` (grouped) and `:1805-1816` (flat) |
| Row-selection JS | `src/Kronikol/Reports/report-select-row-function.js` — reads `data-row-idx`, switches `{prefix}-detail-{idx}` / `-diagram-` / `-activity-` / `-flame-` panels |
| Flatten toggle JS | `src/Kronikol/Reports/report-toggle-flatten-params-js.js` — swaps `.param-table-flat` / `.param-table-grouped`, re-syncs `row-active` by `data-row-idx` |
| Search JS | `src/Kronikol/Reports/report-search-function.js` — hides whole scenario sections only; inside visible parameterized groups it **highlights** `tr[data-row-search]` rows (`row-search-match`), it never hides individual rows |
| Embedded report JSON | `ReportGenerator.BuildFeaturesJsonModel` at `:3164-3198` — writes `outlineId`, `exampleValues`, `exampleFlatValues`, `exampleDisplayName`; also `stableId` (`ScenarioStableId.Compute`, **must not change**) |
| Merge reader | `src/Kronikol/Reports/Merge/MergeableReportReader.cs:72-94` (`ReadScenario`) |
| Generic JSON ingest | `src/Kronikol/Ingestion/TestRunRecord.cs:125-129` (`outlineId`, `exampleValues` on the `start` event), accumulated in `src/Kronikol/Ingestion/FeatureSynthesizer.cs:73-77` and materialized at `:149-152` |
| Cucumber messages ingest | `src/Kronikol/Ingestion/Cucumber/GherkinIndex.cs:92-116` (`AddExamples` builds `ExampleRow(Values, ExamplesTags)` per row id — block name/description currently dropped); consumed in `src/Kronikol/Ingestion/Cucumber/CucumberFeatureSynthesizer.cs:484-505` (`OutlineId = node?.Node.Name`); message model `CucumberMessages.cs:152+` (`CucumberExamples` already deserializes `Name`; add `Description` if missing) |
| ReqNRoll live capture | `src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs:137-184` (builds `ReqNRollScenarioInfo` from `ScenarioContext.ScenarioInfo`); mapped to `Reports.Scenario` in `src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs:29-70` |
| Other adapters | xUnit2/3, NUnit4, MSTest, TUnit, LightBDD, BDDfy set `OutlineId`/`ExampleValues` from test parameters — **no block concept exists there; they stay untouched** (new fields remain null → rendering identical to today) |

### Known quirks to be aware of

- **Ordering bug**: `ScenarioInfoEnumerableExtensions.cs:33-35` orders outline members
  alphabetically by joined example values. With multiple blocks this interleaves rows from
  different blocks. Must be fixed as part of this work.
- **Weak legacy test assertions**: `tests/Kronikol.Tests/Reports/ExamplesTableReportTests.cs`
  asserts on `"examples-table"` / `"examples-detail-row"`, which pass only because those
  strings appear in the *inlined CSS/JS resources* (`stylesheets.css:827+`,
  `report-toggle-examples-detail-function.js`) — the generator emits `param-test-table`, not
  `examples-table`. New tests must assert on actual markup (e.g. `<tr class="examples-block-row"`),
  not on substrings that inlined resources can satisfy.
- **`ParameterGrouper.Analyze` deep-clones scenarios** (`ParameterGrouper.cs:27-32`) via a
  record `with` expression; new scalar fields are copied automatically, only dictionaries need
  explicit cloning — no change needed there beyond the fields existing on the record.

### ReqNRoll: how to get the block name at runtime (verified against Reqnroll 3.3.4)

`Reqnroll.ScenarioInfo` does **not** publicly expose the examples block. But (verified by
reflection over the 3.3.4 assembly and the generated `Muffins.feature.cs` in
`examples/Example.Api/tests/Example.Api.Tests.Component.ReqNRoll.xUnit3/Features/`):

- Generated code **always** constructs `FeatureInfo` with a
  `Reqnroll.Formatters.RuntimeSupport.FeatureLevelCucumberMessages` instance loaded from an
  **embedded ndjson resource** (e.g. `Features/Muffins.feature.ndjson`). It is reachable via
  the **internal** property `FeatureInfo.FeatureCucumberMessages`
  (type `Reqnroll.Formatters.RuntimeSupport.IFeatureLevelCucumberMessages`) exposing:
  `bool HasMessages`, `Io.Cucumber.Messages.Types.GherkinDocument GherkinDocument`,
  `IEnumerable<Io.Cucumber.Messages.Types.Pickle> Pickles`, `Source Source`.
- Generated row invocations pass a `pickleIndex` string into the `ScenarioInfo` ctor; it is
  stored in the **internal** property `ScenarioInfo.PickleIdIndex` (there are also internal
  `PickleId` / `PickleStepSequence` properties populated at runtime).
- The `Io.Cucumber.Messages.Types` API is public and available transitively via the
  `Reqnroll` package (`Gherkin` 35.x / `Cucumber.Messages` 30.x). Only the two internal
  properties need reflection.

Mapping algorithm (mirrors what `GherkinIndex` already does for the Cucumber ingest path):
pickle (via `PickleIdIndex` → index into `Pickles`, or via `PickleId` match) →
`pickle.AstNodeIds[1]` is the example-row AST id (present only for outline rows) → walk
`GherkinDocument.Feature.Children` (including children under `Rule` nodes) to the scenario
node whose `Examples[i].TableBody` contains that row id → block name/description/index.

Fallback chain (each guarded, never throws out of the hook):
1. Reflection succeeds + `HasMessages` → pickle → row id → block. Cross-check that the
   row's cell values match `ScenarioInfo.Arguments` values; on mismatch fall to 2.
2. Value-match: find the (single) block whose `TableBody` has a row whose cells equal the
   argument values; ambiguous (identical rows in two blocks) → give up (nulls).
3. Reflection fails / older or newer Reqnroll without these internals → nulls → report
   renders exactly as today.

## Design

### 1. Data model (Kronikol core)

Add to `Scenario` (`src/Kronikol/Reports/Scenario.cs`):

```csharp
/// <summary>Name of the Examples: block this outline row came from (e.g. "the merchant gained share").</summary>
public string? ExamplesBlockName { get; set; }
/// <summary>Free-text description under the Examples: header, when the author wrote one.</summary>
public string? ExamplesBlockDescription { get; set; }
/// <summary>0-based position of the Examples: block within the outline; orders and separates blocks (needed when blocks are unnamed).</summary>
public int? ExamplesBlockIndex { get; set; }
```

`ScenarioStableId.Compute` inputs are **unchanged** (adding block identity would re-key every
outline scenario across historical runs).

### 2. Rendering — band rows (ReportGenerator)

Activation rule — bands render only when the group has *block structure*:

> at least two distinct `ExamplesBlockIndex` values among members, **or** any member has a
> non-empty `ExamplesBlockName`.

A single unnamed block (today's overwhelmingly common case) and all non-Gherkin adapters
produce **byte-identical output to 3.0.60**. This protects existing goldens and keeps the
Kronikol4J byte-parity surface stable for reports that don't use named blocks.

When active, in **both** the flat table (`:1759`) and the grouped table (`:1838`), before the
first member row of each block emit:

```html
<tr class="examples-block-row">
  <td colspan="{1 + paramCols + 2}">   <!-- # + parameter columns + Status + Duration -->
    <span class="examples-block-name">Examples: the merchant gained share</span>
    <span class="examples-block-counts">2/2 passed</span>
    <span class="examples-block-desc">optional description…</span>  <!-- only when present -->
  </td>
</tr>
```

Rules for the band row (these keep every existing JS behavior working untouched):
- **No** `data-row-idx`, **no** `onclick`, **no** `data-row-search`, **no** `id`. `selectRow`
  and `toggleFlattenParams` iterate `tbody tr` only to clear/re-set `row-active` by
  `data-row-idx` — a band without those attributes is inert. The search highlighter only
  targets `tr[data-row-search]`. Search never hides individual rows, so no band-hiding logic
  is needed.
- Name falls back to `Examples` (keyword only) for an unnamed block sitting among named ones;
  `HtmlEncode` name and description.
- Counts reuse the existing badge vocabulary: `{failed} failed, {skipped} skipped, {passed}/{n} passed`
  (same construction as the section summary at `:1739-1743`), abbreviated to `n/n passed`
  when all pass.
- Member row numbering (`ri + 1`) stays **continuous across blocks** — `ri` doubles as the
  detail-panel/diagram key (`{prefix}-detail-{ri}`), so it must remain the index into
  `group.Scenarios`.
- Block name + description are appended to each member row's `data-row-search` (both tables)
  and to the section-level `searchParts` (`:1702-1717`), so searching "gained share" finds
  the section and highlights the block's rows.

CSS (`src/Kronikol/Reports/stylesheets.css`): `.examples-block-row td` — muted background
band, slightly bolder name, small caps or the report's existing `.label` styling for counts,
top border to separate from the previous block. Must be added to **both** the light theme and
the dark-mode section (`stylesheets.css:1586+` region). Keep it subtle; it is a divider, not
a header.

### 3. Ordering (the interleave fix)

- `ParameterGrouper.BuildGroup` (`ParameterGrouper.cs:84`): stable-sort members by
  `ExamplesBlockIndex ?? int.MaxValue` before building the group. Stable sort preserves the
  source order within a block and leaves blockless members (and whole blockless groups) in
  their existing order — output unchanged when no block info exists. This central fix covers
  every ingestion path.
- `ScenarioInfoEnumerableExtensions.cs:33-35` (ReqNRoll): insert
  `.ThenBy(x => x.ExamplesBlockIndex ?? int.MaxValue)` **before** the value-join `ThenBy` so
  scenario order (and thus scenario IDs/detail panels) is deterministic per block even before
  the central sort.
- Band boundaries are then computed by scanning the sorted members for changes of
  `(ExamplesBlockIndex, ExamplesBlockName)`.

### 4. Capture — per source

**a) Cucumber messages ingest** (`kronikol ingest` of ndjson):
- `CucumberMessages.cs`: confirm `CucumberExamples` has `Name`; add `Description` (`[JsonPropertyName("description")]`) if absent.
- `GherkinIndex.cs`: `ExampleRow` becomes
  `record ExampleRow(Dictionary<string,string> Values, string[] ExamplesTags, string? BlockName, string? BlockDescription, int BlockIndex)`;
  `AddExamples` enumerates blocks with an index and passes name/description through.
- `CucumberFeatureSynthesizer.cs:486-505`: set the three new `Scenario` fields from
  `exampleRow` (null when `exampleRow is null`).

**b) ReqNRoll live capture** (`Kronikol.ReqNRoll.Core`):
- New `ExamplesBlockResolver` (internal static): given `FeatureInfo` + `ScenarioInfo`,
  returns `(string? Name, string? Description, int? Index)` via the reflection route +
  fallback chain described above. Cache the reflected `PropertyInfo`s and the per-feature
  row-id→block lookup (keyed by `FeatureInfo` instance) — hooks run per scenario.
- `ReqNRollScenarioInfo.cs`: add the three fields; `ReqNRollTrackingHooks.AfterScenario`
  (`:164-181`) populates them only when `exampleValues is not null`.
- `ScenarioInfoEnumerableExtensions.ToFeatures` (`:49-69`): copy fields onto `Scenario`;
  ordering fix per §3.
- Compile against `Io.Cucumber.Messages.Types` via the transitive `Reqnroll` dependency; if
  the SDK demands a direct reference for public-surface usage, add
  `<PackageReference Include="Cucumber.Messages" ...>` pinned no higher than what Reqnroll
  3.3.4 carries.

**c) Generic JSON ingest** (external producers, `Ingesting-External-Captures`):
- `TestRunRecord.cs`: optional `start`-event fields `examplesBlockName`,
  `examplesBlockDescription`, `examplesBlockIndex` (document next to `outlineId` at `:125`).
- `FeatureSynthesizer.cs`: accumulate (`??=`, next to `:73-77`) and materialize (`:149-152`).

**d) Report merge** (parallel shards):
- `BuildFeaturesJsonModel` (`ReportGenerator.cs:3175-3198`): write `examplesBlockName`,
  `examplesBlockDescription`, `examplesBlockIndex` next to `outlineId`.
- `MergeableReportReader.ReadScenario` (`:72-94`): read them back.
- Merged groups reassemble blocks via the §3 sort. Edge case: the same block name carrying
  different indices across shards (feature edited mid-run) yields two bands — accepted,
  deterministic, not worth complexity.

**e) All other adapters** — no changes; fields stay null; activation rule keeps output identical.

### 5. Explicitly out of scope (record as follow-ons if wanted)

- Query tool (`Kronikol.Tool`) surfacing block names in `search`/`narrative` output
  (`ReportScanner.cs` reads `exampleValues` at `:499`; block fields would slot in the same way).
- XML/YAML report exports — unchanged.
- Block-level `Examples:` tags rendering (already partially handled as tags elsewhere).
- Non-Gherkin block support (e.g. mapping xUnit `MemberData` source names to blocks).
- **Kronikol4J**: this changes report HTML output for named-block reports — add it to the
  Kronikol4J parity backlog (rendering side only; capture side there is manual recorders).

## Milestones (TDD: red → green → refactor at every step)

### M1 — Data model + band-row rendering (core, no capture yet)
1. Add the three `Scenario` fields.
2. New `tests/Kronikol.Tests/Reports/ExamplesBlockRenderingTests.cs` (construct `Feature[]`
   directly, per the pattern in `ExamplesTableReportTests.cs`, but assert on real markup):
   - members with 2 named blocks + 1 unnamed → `<tr class="examples-block-row"` appears once
     per block, in **both** flat and grouped tables when `ExampleFlatValues` present;
   - block name and description HTML-encoded; description span absent when null;
   - per-block counts correct (mixed pass/fail);
   - activation rule: single unnamed block → output contains **no** `examples-block-row`
     and is byte-equal to a baseline generated from the same features with fields nulled;
   - all-null block fields → unchanged (regression);
   - band carries no `data-row-idx`/`onclick`/`data-row-search`;
   - member `data-row-search` and section `data-search` contain block name + description;
   - row numbering continuous across blocks; detail panel ids still `{prefix}-detail-{ri}`.
3. `ParameterGrouper` tests: stable sort by block index; interleaved input
   (block 1 row, block 0 row, block 1 row) comes out grouped; null-index members keep order
   and land after indexed ones.
4. CSS for `.examples-block-row` in light + dark sections.

### M2 — Cucumber messages ingest
1. Extend `CucumberFixtures.cs` with an outline carrying two named + one unnamed
   `Examples:` blocks (names, descriptions, distinct row ids).
2. `CucumberFeatureSynthesizerTests`: scenarios get correct
   name/description/index per row; non-outline scenarios get nulls.
3. Implement `ExampleRow` extension + `AddExamples` + synthesizer mapping.

### M3 — ReqNRoll live capture
1. Unit tests for `ExamplesBlockResolver` in `tests/Kronikol.Tests/ReqNRoll/`:
   `GherkinDocument`/`Pickle` types are plain constructible records — build a fake feature
   with two named blocks; test the pickle-index route, the value-match fallback, the
   ambiguous-rows give-up, and the reflection-failure null path (pass a `FeatureInfo`
   without messages).
2. Verify `PickleIdIndex` semantics empirically (spike inside this milestone): run the
   ReqNRoll xUnit3 example with a temporary multi-block feature and assert the mapping —
   this validates the index base (the generated code passes "1"-based-looking strings) and
   whether the index is feature-scoped. Encode the finding in the resolver + a comment.
3. Add a multi-block named-examples scenario outline to the ReqNRoll example projects
   (`examples/Example.Api/.../Features/` — e.g. extend `Muffins.feature` or add a
   `MarketShare.feature` mirroring the motivating example) so example reports exercise it.
4. Hook plumbing + ordering fix (`ScenarioInfoEnumerableExtensions`) with a test proving
   blocks no longer interleave.

### M4 — Generic ingest + merge round-trip
1. `FeatureSynthesizerTests`: `start` records with the new fields → scenarios carry them.
2. Merge test: generate mergeable JSON from features with blocks → `MergeableReportReader`
   round-trips the three fields; merged report renders bands.
3. Implement `TestRunRecord` fields, synthesizer accumulation, JSON model write, reader.

### M5 — Playwright E2E (`tests/Kronikol.Tests.EndToEnd/`, follow CLAUDE.md rules:
`PollingInterval = 200`, no force-clicks, no network mocking, `FillSearchBar()` helper)
1. Report with a multi-block outline (build features in-test like
   `FullPipelineParameterizedE2ETests.cs` does): band rows visible with names + counts.
2. Clicking a member row below a band still switches the detail panel; band row click is inert.
3. Flatten toggle: bands present and consistent in both table variants; active row survives
   the toggle.
4. Search for a block name: section stays visible, block's member rows get
   `row-search-match`, first match auto-selected.

### M6 — Docs + release
1. Wiki (`../Kronikol.wiki`):
   - `Generated-Reports.md` — update the Examples/Scenario-Outline bullet (`:226`) and the
     "Parameterized Test Grouping" section (`:304+`) with the band-row behavior and the
     activation rule;
   - `Ingesting-External-Captures.md` — add the three `start`-event fields to the table
     (`:104-105`) and to the sample ndjson line;
   - `Merging-Parallel-Reports.md` — one line: block identity survives merges.
2. `CHANGELOG.md` entry; bump the patch version in **all** packages (same number everywhere,
   per CLAUDE.md); full test suite; commit, tag `v{version}`, push commit + tag.
3. Note the rendering divergence in the Kronikol4J parity backlog.

## Risks / open questions

- **Reqnroll internal API drift**: `FeatureInfo.FeatureCucumberMessages` and
  `ScenarioInfo.PickleIdIndex` are internal; a Reqnroll upgrade may rename them. Mitigation:
  reflection guarded to return nulls (feature silently degrades to today's rendering), plus
  a unit test that reflects the same members off the referenced Reqnroll assembly so a
  version bump fails loudly in CI rather than silently degrading.
- **`PickleIdIndex` semantics** (0- vs 1-based, feature-scoped?) — resolved by the M3 spike;
  the value-match cross-check makes a wrong assumption fail safe (fallback, then nulls).
- **Older generated code**: projects generated by older ReqNRoll/SpecFlow won't carry
  feature-level messages → nulls → unchanged rendering. Acceptable.
- **Byte-parity**: the activation rule is the guard — add the explicit byte-equality
  regression test in M1 and keep it.
