# Plan — Combine Background Steps with regular steps by default

## Goal

Flip the default: background steps render **inline with the scenario's regular steps** in one
`Steps` list. Separating them into the collapsible `Background Steps` section becomes opt-in
configuration. When combined, repeated primary keywords collapse to `And` — so
`Given / Given / When` reads `Given / And / When`, not `Given / Given / When`.

---

## 1. Current state

### 1.1 Where background steps come from

| Source | Path | Keyword provenance |
|---|---|---|
| Gherkin `Background:` via ReqNRoll | [ScenarioInfoEnumerableExtensions.cs:72](src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs#L72) | `StepInstance.StepDefinitionKeyword` — a ReqNRoll **enum**, so always English ([ReqNRollTrackingHooks.cs:74,128](src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs#L74)) |
| Cucumber messages ingest | [CucumberFeatureSynthesizer.cs:397,497](src/Kronikol/Ingestion/Cucumber/CucumberFeatureSynthesizer.cs#L397) | The **literal** Gherkin keyword, trimmed — localised for non-English dialects |
| Generic ingest (`background: true` records) | [FeatureSynthesizer.cs:81,147](src/Kronikol/Ingestion/FeatureSynthesizer.cs#L81) | Producer-supplied |
| Heuristic common-prefix detection | [FeatureSynthesizer.cs:233-241](src/Kronikol/Ingestion/FeatureSynthesizer.cs#L233) → [BackgroundStepsDetector.cs](src/Kronikol/Reports/BackgroundStepsDetector.cs) | Whatever the framework recorded |

All four land in `Scenario.BackgroundSteps` ([Scenario.cs:24](src/Kronikol/Reports/Scenario.cs#L24)).

### 1.2 Two hard constraints on the implementation

**Shared instances.** `BackgroundStepsDetector` assigns the *same array instance*
(`members[0].Steps.Take(n).ToArray()`) to every scenario in a Rule group
([BackgroundStepsDetector.cs:45-50](src/Kronikol/Reports/BackgroundStepsDetector.cs#L45)) — the
`ScenarioStep` records are shared.

**Concurrent readers.** `RunOutputs` is `Parallel.Invoke`
([ReportGenerator.cs:340-354](src/Kronikol/Reports/ReportGenerator.cs#L340)): the Specifications
HTML, the TestRunReport HTML, and the JSON/XML/YAML data writers all run **at the same time** over
the same `Scenario` and `ScenarioStep` instances.

Together these make keyword rewriting-by-mutation a data race that would leak `And` into the data
files. **The keyword change must be a render-time projection.** (`StepText.ApplyToFeatures`
([StepText.cs:137-148](src/Kronikol/Reports/StepText.cs#L137)) does mutate shared background steps,
but it runs *before* `RunOutputs` and capitalisation is idempotent — it is not a precedent for this.)

### 1.3 Where background steps are rendered

Two duplicated blocks, both emitting `<details class="scenario-background"><summary class="h4">Background Steps</summary>`:

- [ReportGenerator.cs:1213-1226](src/Kronikol/Reports/ReportGenerator.cs#L1213) — normal scenario
- [ReportGenerator.cs:1993-2006](src/Kronikol/Reports/ReportGenerator.cs#L1993) — parameterized-group detail panels

Each is followed by a near-identical `scenario-steps` block
([1228-1247](src/Kronikol/Reports/ReportGenerator.cs#L1228),
[2008-2026](src/Kronikol/Reports/ReportGenerator.cs#L2008)) doing three things the background block
does not: `ShouldRenderCombinedTable`, the `afterThen` tracker (drives `skipTabularInline`), and
`RenderCombinedTabularParameters`.

CSS: [stylesheets.css:598-627](src/Kronikol/Reports/stylesheets.css#L598).

Both HTML reports (Specifications and TestRunReport) go through the same `GenerateHtmlReport`, so
they change together.

### 1.4 The existing option is dead code

`ReportConfigurationOptions.InlineBackgroundSteps`
([ReportConfigurationOptions.cs:242-243](src/Kronikol/ReportConfigurationOptions.cs#L242)) is
**declared, documented in the wiki and CHANGELOG, and never read anywhere**. `GenerateHtmlReport` has
no corresponding parameter. Its "test"
([StepRenderingReportTests.cs:836-849](tests/Kronikol.Tests/Reports/StepRenderingReportTests.cs#L836))
calls `GenerateReportWithInlineBackground`, a helper byte-identical to `GenerateReport`, and asserts
only that both step texts appear — it passes vacuously. Line 839 admits it:
`// inlineBackgroundSteps option not yet implemented`.

No user can currently be depending on it doing anything.

### 1.5 Background-first ordering is already the model's convention

`OrderedStepPaths` ([ReportGenerator.cs:3008-3015](src/Kronikol/Reports/ReportGenerator.cs#L3008))
enumerates background steps first with path `b0, b1, …` then regular steps `0, 1, …`, and
`AttributeInteractionsToSteps`' doc comment states attribution is positional "in document order
(**background steps first**)" ([2928-2938](src/Kronikol/Reports/ReportGenerator.cs#L2928)).
`REPORT_QUERY_PLAN.md` §276 depends on that `backgroundSteps`/`steps` index path.

Two consequences: combined rendering matches the interaction-attribution model exactly, and the
data outputs **must** keep the two collections separate.

### 1.6 A latent benefit worth noting

`toggle_expand_collapse` is only wired to `details.feature` and `details.scenario`
([ReportGenerator.cs:889](src/Kronikol/Reports/ReportGenerator.cs#L889)). The background `<details>`
is in neither selector, so **"Expand All Scenarios" never opens the Background section** — every
E2E test has to click its summary explicitly. Combining removes that dead end. It also means a
misfiring common-prefix heuristic no longer hides real steps behind a collapsed disclosure.

---

## 2. Bugs found during investigation

All are pre-existing, all get fixed here under TDD (per CLAUDE.md), all are made more visible by the
default change.

### Bug 1 — background step text is not searchable

`CollectStepText` is called on `scenario.Steps` at four sites
([1155](src/Kronikol/Reports/ReportGenerator.cs#L1155),
[1732](src/Kronikol/Reports/ReportGenerator.cs#L1732),
[1830](src/Kronikol/Reports/ReportGenerator.cs#L1830),
[1916](src/Kronikol/Reports/ReportGenerator.cs#L1916)) and **never** on `BackgroundSteps`. The
search box filters purely on the resulting `data-search` / `data-row-search` attributes
([report-search-function.js](src/Kronikol/Reports/report-search-function.js)), so text that lives
only in a background step is unfindable.

### Bug 2 — step numbering restarts

With `showStepNumbers`, the background list numbers `1..n` and the steps list restarts at `1`
([1219](src/Kronikol/Reports/ReportGenerator.cs#L1219) vs
[1240](src/Kronikol/Reports/ReportGenerator.cs#L1240)). Two steps in one scenario are both "1.".

### Bug 3 — background-only scenarios lose their steps in parameterized groups

[ReportGenerator.cs:1980](src/Kronikol/Reports/ReportGenerator.cs#L1980):

```csharp
var hasAnyDetail = scenarios.Any(s => s.Steps is { Length: > 0 } || s.Result == ExecutionResult.Failed);
```

`BackgroundSteps` is not consulted, so a passing scenario whose entire step list was extracted as
background renders **no detail panel at all** — the steps vanish from the report.

This is reachable, not theoretical: `BackgroundStepsDetector` deliberately permits the whole list to
become background ("or when all steps are common (no remaining steps)",
[BackgroundStepsDetector.cs:36-42](src/Kronikol/Reports/BackgroundStepsDetector.cs#L36)), leaving
`Steps` as an empty array. A scenario outline whose example rows share identical step text — no
`<param>` interpolation in the step prose — hits exactly this.

### Bug 4 — Features Summary step counts exclude background steps

[ReportGenerator.cs:656](src/Kronikol/Reports/ReportGenerator.cs#L656) and
[697-709](src/Kronikol/Reports/ReportGenerator.cs#L697) build `hasAnySteps` and `allSteps` from
`s.Steps` only. The summary table therefore under-counts, and a feature whose steps all became
background loses the four step columns entirely. In combined mode the mismatch becomes plainly
visible: the table says 2, the scenario below lists 4.

### Bug 5 — the Specifications data files drop background steps entirely

Four writers emit `Steps` and no background:

- `GenerateYamlSpecs` — [1511-1516](src/Kronikol/Reports/ReportGenerator.cs#L1511) (public API)
- `GenerateSpecificationsYaml` — [3772-3777](src/Kronikol/Reports/ReportGenerator.cs#L3772)
- `GenerateSpecificationsJson` — [3804](src/Kronikol/Reports/ReportGenerator.cs#L3804)
- `GenerateSpecificationsXml` — [3838](src/Kronikol/Reports/ReportGenerator.cs#L3838)

The Specifications **HTML** shows them; `Specifications.yml`/`.json`/`.xml` — the living-documentation
artefact — silently omits them. The TestRunReport writers get this right
([JSON 2999](src/Kronikol/Reports/ReportGenerator.cs#L2999),
[XML 3299](src/Kronikol/Reports/ReportGenerator.cs#L3299),
[YAML 3426](src/Kronikol/Reports/ReportGenerator.cs#L3426)), so there is an established shape to copy.
`SpecificationsDataTests` has no background case at all, which is why this survived.

Fix by emitting a sibling `BackgroundSteps` node, matching the TestRunReport writers — **not** by
merging into `Steps`, which would make the data lossy and break the `b{i}` path convention of §1.5.
There is no Specifications schema (only `GenerateTestRunReportSchema`,
[280](src/Kronikol/Reports/ReportGenerator.cs#L280)), so no schema change is needed.

---

## 3. Design decisions

### 3.1 Option surface

**Recommendation:** add `SeparateBackgroundSteps` (bool, default `false`), mirroring `SeparateSetup`
([ReportConfigurationOptions.cs:53](src/Kronikol/ReportConfigurationOptions.cs#L53)), and mark
`InlineBackgroundSteps` `[Obsolete]`.

Why a new name rather than flipping `InlineBackgroundSteps` to default `true`:

- `InlineBackgroundSteps = false` would then mean "separate", which reads backwards.
- A `bool` cannot distinguish "left at default" from "explicitly `false`", so honouring the old
  property would silently give anyone who copied the wiki's `InlineBackgroundSteps = false` example
  ([Step-Tracking.md:488](../Kronikol.wiki/Step-Tracking.md)) the *old* behaviour — from a property
  that has never had an effect.
- `Separate*` matches the naming already in the record.

`[Obsolete("Background steps are inlined by default. Set SeparateBackgroundSteps = true for the old separate section.")]`,
kept one minor version. Ignoring it changes nothing for anyone, since it was already a no-op.

*Alternative if you'd rather not grow the surface:* delete `InlineBackgroundSteps` outright. Binary-breaking
on a property that has never done anything. Say the word.

**One option or two?** `showStepNumbers` is split per report
(`SpecificationsShowStepNumbers` / `TestRunReportShowStepNumbers`), but `GroupParameterizedTests`,
`TitleizeParameterNames` and `MaxParameterColumns` are shared. Background-step layout is a
presentation-consistency concern across both reports, so: **one shared option**. Easy to split later
if asked.

### 3.2 Keyword collapsing (`Given / Given` → `Given / And`)

New helper `src/Kronikol/Reports/StepKeywordCollapser.cs`:

```csharp
/// Returns the keyword to *display* for each step, collapsing a repeat of the
/// current primary keyword to "And". Never mutates the steps.
public static string?[] DisplayKeywords(IReadOnlyList<ScenarioStep> steps)
```

Walking the list, tracking `current` = the last primary keyword seen:

| Step keyword | Displayed as | Effect on `current` |
|---|---|---|
| `Given` / `When` / `Then` (case-insensitive, trimmed), **≠** `current` | unchanged | becomes `current` |
| `Given` / `When` / `Then`, **==** `current` | `And` | unchanged |
| `And` / `But` / `*` | unchanged | unchanged (conjunctions inherit) |
| `null` / empty | unchanged | unchanged |
| anything else (localised or free-form) | unchanged | `current` reset to `null` |

Reuse the exact vocabulary already established by
`IngestAttribution.PhaseForStep` ([IngestAttribution.cs:172-182](src/Kronikol/Ingestion/IngestAttribution.cs#L172)) —
`given/context`, `when/then/action/outcome/butwhen`, `and/but/conjunction/*` — rather than inventing
a second keyword table. Note `butwhen` is in that list and must be treated as a primary, not a
conjunction.

Casing of the emitted `And` follows the keyword it replaces (`GIVEN` → `AND`).

**Localisation is a deliberate non-goal.** We cannot synthesise a German `Und` from `Angenommen`, so
unrecognised keywords pass through untouched and disable collapsing until the next recognised
primary. This costs nothing on the main path: ReqNRoll supplies an English enum (§1.1), so only the
Cucumber-messages ingest of a non-English feature file is affected, and it degrades to today's
rendering. See §6 for the optional fix.

`RenderStep` ([2290](src/Kronikol/Reports/ReportGenerator.cs#L2290)) gains an optional
`string? displayKeyword = null`, used in place of `step.Keyword` at
[2352](src/Kronikol/Reports/ReportGenerator.cs#L2352). No model mutation — this is what keeps §1.2 safe.

**Scope of collapsing — its own option.** Collapsing only at the background/steps seam would be
inconsistent (`Given / Given` inside a plain scenario still renders doubled). Recommendation: apply
to the whole rendered list, gated by `CollapseRepeatedStepKeywords` (bool, default `true`), so
literal keywords are one flag away.

With `SeparateBackgroundSteps = true` the two lists collapse **independently**, so the separated
`Steps` section still opens with its own `Given`.

### 3.3 Visual affordance in combined mode

Background steps keep a marker: `RenderStep` emits `class="step step-background"` (and
`step step-collapsible step-background`) when the step came from the background. CSS gives it a muted
left accent consistent with the existing `.scenario-background` border. This also gives the E2E tests
a stable hook.

### 3.4 What deliberately does not change

- `BackgroundStepsDetector` and every ingestion path — detection is unchanged; only presentation moves.
- The `backgroundSteps`/`steps` split in all data outputs, the merge reader
  ([MergeableReportReader.cs:92](src/Kronikol/Reports/Merge/MergeableReportReader.cs#L92)),
  attachment copying ([3717](src/Kronikol/Reports/ReportGenerator.cs#L3717)) and the
  `b{i}` step-path convention.
- Diagram generation. `hasStepDelimiters` is computed from `<<stepDelimiter>>` markers in the PlantUML
  ([559-562](src/Kronikol/Reports/ReportGenerator.cs#L559)), not from the HTML step list, and
  `BuildStepWindows` skips `record.Background == true`
  ([IngestAttribution.cs:196-205](src/Kronikol/Ingestion/IngestAttribution.cs#L196)). Explicit
  background steps therefore have no delimiter bar while heuristic ones do — unchanged by this work,
  but slightly more noticeable once the two kinds of step sit in one list. Worth a wiki sentence, not
  a code change.
- `CiSummaryGenerator` — renders no steps at all. Unaffected.
- `ScenarioStableId` — hashes names and example values, not steps. Unaffected.

---

## 4. Implementation steps (TDD, red → green → refactor)

### Step 1 — `StepKeywordCollapser`

New `tests/Kronikol.Tests/Reports/StepKeywordCollapserTests.cs`. Red first:

- `Given / Given / When` → `Given / And / When`
- `Given / When / When / Then / Then` → `Given / When / And / Then / And`
- `Given / And / Given` → `Given / And / And` (a conjunction does not reset `current`)
- `Given / Then / Given` → unchanged (not a repeat of the *current* keyword)
- `But`, `*`, `null`, empty pass through and do not reset `current`
- `ButWhen` treated as a primary
- unrecognised keyword (`Angenommen`) passes through **and** resets `current`, so a following `Given`
  is not collapsed
- `GIVEN / GIVEN` → `GIVEN / AND` (casing follows source)
- keywords with surrounding whitespace (`"Given "`) are matched trimmed
- input steps are not mutated

Then implement.

### Step 2 — extract the shared step-section renderer

```csharp
private static void RenderScenarioStepSections(
    StringBuilder body, Scenario scenario, bool showStepNumbers,
    bool separateBackgroundSteps, bool collapseRepeatedStepKeywords)
```

- **Combined (default):** concatenate `BackgroundSteps ?? []` + `Steps ?? []`; render nothing if
  empty. One `<details class="scenario-steps" open><summary class="h4">Steps</summary>`.
  `ShouldRenderCombinedTable`, the `afterThen` tracker and `RenderCombinedTabularParameters` all take
  the **combined** array. Numbering `1..N` across the whole list. Indices
  `< BackgroundSteps.Length` get `step-background`.
- **Separated:** current markup, plus the Bug 2 numbering fix (steps continue at
  `BackgroundSteps.Length + 1`) and independent per-section collapsing.

Replace both call sites ([1213-1247](src/Kronikol/Reports/ReportGenerator.cs#L1213),
[1993-2026](src/Kronikol/Reports/ReportGenerator.cs#L1993)). This removes ~35 lines of existing
duplication and guarantees the two surfaces stay in step.

### Step 3 — plumb the options

- `ReportConfigurationOptions`: `SeparateBackgroundSteps`, `CollapseRepeatedStepKeywords`,
  `[Obsolete]` on `InlineBackgroundSteps`.
- `GenerateHtmlReport` ([404-436](src/Kronikol/Reports/ReportGenerator.cs#L404)): two new optional
  params at the end (source-compatible — every existing call uses named arguments).
- `RenderParameterizedGroup` ([1682-1708](src/Kronikol/Reports/ReportGenerator.cs#L1682)): same two,
  threaded from [1110-1127](src/Kronikol/Reports/ReportGenerator.cs#L1110).
- Both `Add(...)` sites — [250](src/Kronikol/Reports/ReportGenerator.cs#L250) (Specifications),
  [255](src/Kronikol/Reports/ReportGenerator.cs#L255) (TestRunReport).

### Step 4 — Bug 1 (search index)

Add `CollectStepText(s.BackgroundSteps, …)` beside each of the four existing calls. Red test: a
report whose only occurrence of a phrase is in a background step, asserting the phrase lands in
`data-search`; a parameterized-group variant asserting `data-row-search`. Applies in both modes.

### Step 5 — Bug 3 (`hasAnyDetail`)

Include `s.BackgroundSteps is { Length: > 0 }` in the predicate at
[1980](src/Kronikol/Reports/ReportGenerator.cs#L1980). Red test: a passing parameterized group whose
scenarios have only background steps currently renders no `.param-detail-panel`.

### Step 6 — Bug 4 (Features Summary counts)

`hasAnySteps` and `allSteps` ([656](src/Kronikol/Reports/ReportGenerator.cs#L656),
[699-704](src/Kronikol/Reports/ReportGenerator.cs#L699)) concatenate background steps. Red test:
feature with 2 background + 2 regular steps reports 4, not 2.

### Step 7 — Bug 5 (Specifications data)

Add a `BackgroundSteps` node to all four writers, shaped like the TestRunReport equivalents. Red
tests in `SpecificationsDataTests` for YAML, JSON and XML, plus one for the public `GenerateYamlSpecs`.

### Step 8 — CSS

`.step-background` in `stylesheets.css`. Keep the `.scenario-background` rules — separated mode still
uses them.

### Step 9 — unit tests (`tests/Kronikol.Tests/Reports/`)

Fix the broken helper first: replace `GenerateReportWithInlineBackground`
([StepRenderingReportTests.cs:26-34](tests/Kronikol.Tests/Reports/StepRenderingReportTests.cs#L26))
with `GenerateReportSeparateBackground` that actually passes the flag. Rewrite
[822-877](tests/Kronikol.Tests/Reports/StepRenderingReportTests.cs#L822):

- `Report_combines_background_steps_with_regular_steps_by_default`
- `Report_separates_background_steps_when_option_enabled`
- `Report_renders_and_keyword_for_background_given_followed_by_scenario_given`
- `Report_separated_mode_does_not_collapse_across_sections`
- `Report_numbers_combined_steps_continuously` / `…_separated_steps_continuously`
- `Report_renders_combined_list_when_only_background_steps_exist` (`Steps = null`)
- `Report_renders_nothing_when_both_lists_empty`
- `Report_combined_tabular_parameters_span_background_and_regular_steps` — background step carrying a
  tabular parameter, confirming `ShouldRenderCombinedTable` sees the combined array
- Parameterized-group equivalents for both modes — that second surface has **no** background coverage today
- A regression test for §1.2: generate the HTML and then assert the JSON data still says `Given`,
  proving no keyword mutation leaked into the model

### Step 10 — E2E tests (`tests/Kronikol.Tests.EndToEnd/`)

`ReportTestHelper.GenerateReportWithBackground`
([ReportTestHelper.cs:1551](tests/Kronikol.Tests.EndToEnd/ReportTestHelper.cs#L1551)) uses
`Given / And` background and `When / Then` steps — it never exercises the `Given / Given` seam. Add a
fourth scenario with background `[Given X]` and steps starting `Given Y`, plus a
`GenerateReportWithSeparatedBackground(...)` and its `PlaywrightTestBase` wrapper
([PlaywrightTestBase.cs:65](tests/Kronikol.Tests.EndToEnd/PlaywrightTestBase.cs#L65)).

`BackgroundRenderingTests.cs` — all eight tests assert `details.scenario-background`:

| Existing | New form |
|---|---|
| `…render_background_section` | 0 `.scenario-background`, 2 `.step-background` inside `.scenario-steps` |
| `Background_section_has_correct_summary_text` | the single summary reads `Steps` |
| `Background_section_is_collapsed_by_default` | replaced: the combined list is open by default (and needs no extra click — §1.6) |
| `…contains_correct_number_of_steps` | `.scenario-steps .step` count is 4 |
| `Background_steps_display_correct_text` | same texts, no second `<details>` to open |
| `…renders_before_steps_section` | background steps are the first `.step` children |
| `Scenario_without_background_has_no_background_section` | third scenario has zero `.step-background` |
| `Multiple_scenarios_with_same_background…` | both scenarios show their background steps inline |

New E2E:

- `Repeated_given_keyword_renders_as_and` against the new fourth scenario
- `SeparatedBackgroundRenderingTests.cs` re-asserting the original eight against the separated fixture
- `Search_matches_text_that_only_appears_in_a_background_step` — `FillSearchBar()`, assert the
  scenario stays visible

Per the repo's Playwright rules: `PollingInterval = 200`, no `Force = true`, no network mocking,
`.First`/`.Nth(n)` on multi-match selectors.

### Step 11 — docs

- `../Kronikol.wiki/Report-Configuration.md:153` — replace the `InlineBackgroundSteps` row with the
  two new options; note the deprecation.
- `../Kronikol.wiki/Step-Tracking.md:253-286, 486-498` — rewrite "Background Steps": combined by
  default, `And` collapsing, the localisation caveat, the delimiter note from §3.4, how to opt into
  the separate section.
- `../Kronikol.wiki/Generated-Reports.md:178` — still describes the collapsible section as the behaviour.
- `../Kronikol.wiki/API-Reference.md:55` — `BackgroundStepsDetector` entry implies a separate rendered section.
- `CHANGELOG.md` — behaviour change, two new options, one deprecation, five bug fixes.

### Step 12 — release

Full suite green, then patch-bump **all** packages to one version, changelog, commit, tag `v{version}`,
push commit and tag.

---

## 5. Risks and call-outs

1. **Visible default change.** Every report gains a longer `Steps` list and loses the `Background Steps`
   disclosure. `SeparateBackgroundSteps = true` restores it in one line.
2. **`CollapseRepeatedStepKeywords` defaults on and touches scenarios with no background** — any
   `Given / Given` anywhere becomes `Given / And`. Wider blast radius than the background change
   itself. Defaulting it `false` and collapsing only across the seam is a one-flag adjustment; I'd
   still recommend on, since the alternative renders the same duplication it exists to fix.
3. **Localised Gherkin passes through uncollapsed** (§3.2). Affects only the Cucumber-messages ingest
   of non-English features; degrades to today's output.
4. **Kronikol4J output parity.** The Java port's report output byte-matches .NET today; this diverges
   it for any feature with background steps. There is no `java/` tree in this repo, so it is a
   follow-up on that side, out of scope here.
5. **Step numbers shift** relative to external notes citing "step 3". Diagram delimiters key off
   keyword+text rather than index (§3.4), so diagrams are unaffected.
6. **The shared-instance + `Parallel.Invoke` constraint** (§1.2) is the one way this change could
   silently corrupt data. Step 9's data-output regression test exists specifically to pin it.
7. Bugs 3–5 are behaviour changes to existing output in their own right. If you want them split into
   a separate release from the default flip, Steps 4–7 are independent of Steps 1–3 and can ship first.

---

## 6. Optional follow-up: localised conjunctions (Phase B)

`ScenarioStep` has no `KeywordType`, though the Cucumber pipeline knows it
([CucumberMessages.cs:201](src/Kronikol/Ingestion/Cucumber/CucumberMessages.cs#L201),
[TestRunRecord.cs:65-72](src/Kronikol/Ingestion/TestRunRecord.cs#L65)) and
`CucumberStepWindow` already carries it
([CucumberFeatureSynthesizer.cs:47](src/Kronikol/Ingestion/Cucumber/CucumberFeatureSynthesizer.cs#L47)).

Carrying it onto `ScenarioStep` would let the collapser (a) recognise a localised primary keyword by
its *meaning* and (b) recover the localised word for "And" — the literal keyword of any step in the
same feature whose `KeywordType` is `Conjunction`. That closes the §3.2 gap properly.

Cost: `ScenarioStep` + the three TestRunReport data writers + the JSON schema + the merge reader +
round-trip tests. Genuinely useful but clearly a separate piece of work; excluded from this plan
unless you want it folded in.

---

## 7. Files touched

**Source**
- `src/Kronikol/ReportConfigurationOptions.cs` — 2 new options, 1 obsoleted
- `src/Kronikol/Reports/StepKeywordCollapser.cs` — new
- `src/Kronikol/Reports/ReportGenerator.cs` — signature, 2 call sites, extracted renderer, `RenderStep`, 4 search sites, `hasAnyDetail`, Features-Summary counts, 4 specs data writers
- `src/Kronikol/Reports/stylesheets.css` — `.step-background`

**Tests**
- `tests/Kronikol.Tests/Reports/StepKeywordCollapserTests.cs` — new
- `tests/Kronikol.Tests/Reports/StepRenderingReportTests.cs` — helper fix + rewritten background tests
- `tests/Kronikol.Tests/Reports/SpecificationsDataTests.cs` — background coverage for YAML/JSON/XML
- `tests/Kronikol.Tests.EndToEnd/ReportTestHelper.cs`, `PlaywrightTestBase.cs` — fixtures
- `tests/Kronikol.Tests.EndToEnd/BackgroundRenderingTests.cs` — rewritten
- `tests/Kronikol.Tests.EndToEnd/SeparatedBackgroundRenderingTests.cs` — new

**Docs**
- `CHANGELOG.md`, `../Kronikol.wiki/{Report-Configuration,Step-Tracking,Generated-Reports,API-Reference}.md`

**Untouched by design:** `BackgroundStepsDetector`, all ingestion paths, TestRunReport JSON/XML/YAML,
the JSON schema, the merge reader, `CiSummaryGenerator`, `ScenarioStableId`, diagram generation.
