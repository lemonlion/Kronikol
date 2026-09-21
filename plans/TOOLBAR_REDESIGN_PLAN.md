# TOOLBAR_REDESIGN_PLAN — Option B now, Option C at v4, tri-state filtering everywhere

**Status: DRAFT — nothing implemented.** Investigation and design are complete; this file is the implementation
spec. Work starts only on explicit go, TDD per CLAUDE.md (red → green → refactor, Playwright for every UI
behaviour). Design authority: the live comparison artifact
https://claude.ai/code/artifact/a47e08b8-8b3a-4690-883b-3d6093109c2c (six tabs, every decision demonstrated in
real BreakfastProvider content) and the session memory `toolbar-redesign-investigation.md` (the full locked-decision
list, measurement tooling, and instrument traps). Written 2026-08-31 against 3.0.69.

## 0. Decisions this plan encodes (and their provenance)

1. **B vs C is resolved by sequencing, not by choosing** (user, 2026-08-31): **Option B — the segmented,
   contained toolbar fix — ships in the v3.x line**, fully backwards compatible with the colour scheme carried by
   the standard stylesheet and by `HtmlSpecificationsCustomStyleSheet` (whose default, `Stylesheets.VioletThemeStyleSheet`,
   is what styles `Specifications.html` today — `ReportConfigurationOptions.cs:36`). **Option C — the quiet bar,
   which requires the whole-report redesign — ships at v4.0.0** as a breaking change, and introduces a
   **CSS-custom-property token layer as the new, documented, version-stable theming mechanism** (the repo's
   `stylesheets.css` contains zero custom properties today, so this is a clean introduction, not a migration).
2. **The tri-state filter component (tag variant A, with variant C as fallback) applies uniformly to Tags,
   Dependencies and Categories** (user, 2026-08-31). **A — cycling chips — is the default presentation for all
   three**; the C menu engages only when the vocabulary genuinely doesn't fit, decided by an estimated-rendered-size
   rule at generation time, **not** by which filter it is. (This supersedes the earlier "Dependencies default to C"
   suggestion and refines the "~12 tags" note: the real report's 17 dependency chips already wrap fine inline today,
   so a raw count is the wrong trigger — see §2.4.)
3. **The search DSL gains dependency expressions** (user: "Maybe we should create one for dependencies as well"):
   today both search paths — the legacy `@tag and/or/not` path and the advanced `&&/||/!!` path — evaluate tags
   against `data-categories` + `data-labels` only (`report-search-function.js:154`, `advanced-search.js:251`);
   `data-dependencies` is emitted on every scenario (`ReportGenerator.cs:1153`, `:1819`) but is unreachable from
   any expression. §2.5.
4. Everything the investigation locked stays locked: W3 split rows, word-free dot chips + `aria-pressed`,
   "Detail" (no colon), Expanded/Collapsed/Truncated, "40 LINES" uppercase options, Format↔Toggle dynamic divider,
   geometry-stable pending rail, ⚙-into-summary + resize re-check, M1/T1 with B and M2/T2 with C, baseline-aligned
   options bar (selects pinned to `line-height: 1.25` + 5px vertical padding; `align-items: baseline`), px units
   for control chrome. The artifact demonstrates each; the memory file lists them with rationale.
5. **Round 8 (user, 2026-08-31), three additions, all demonstrated live in the artifact:**
   - **DURATION pairs with STATUS whenever both runs fit whole**, and overflows **all-or-nothing** onto its own
     row. Markup: `.duration-filters` moves inside `.filter-row` (shared modern markup — B gets the atomic drop
     free from flex wrap in today's grammar). C adds a **measured container threshold — 871px needed, pinned at
     876px** (headroom, and chosen so the recomposed 1400px band, whose box measures 877px, pairs) — above which
     the run reads its label inline; below it a **complementary rule forces the full-width drop**, because
     without it there is a ~90px band where flex still fits both runs but with the column-style label — a
     half-paired state the design forbids (caught by probe, not by eye).
   - **≤520px container: the STATUS/DURATION pill fallback becomes equal-width 2-column grids** (text centred,
     `Custom` spanning the last row) — free-wrapped pills of five widths landed as a ragged 3+2 with a void.
   - **`Specifications.html` keeps its violet identity by default at v4**: the C token layer ships a violet
     default for the Specifications report (TestRunReport keeps the blue default). Demo palette: accent `#7c3aed`,
     hover `#f5f3ff`, selected fill `#ede9fe` / ink `#5b21b6`, ground `#f8f6fc`, hairlines `#e7e2f2`/`#f0ecf8`,
     active-chip border `#c4b5fd`, focus ring `rgba(124,58,237,.12)`. In the artifact this is one `:root` line
     plus six literal patches; in production every one of those literals is a §3.1 token, so the theme is the
     token line alone.
6. **Round 9 (user, 2026-08-31), two corrections to C:**
   - **The ≥1351px TestRunReport band re-composes as a two-column grid** (user: the three-across band "looks
     terrible" once the tags row made the box tall): summary stacks over the CI/donut group in one left column
     (CI table and donut side by side, hairline turned to a top edge), the filtering box takes the rest —
     877px at 1400 instead of the ~600 three-across left it, which both clears the pairing threshold and cuts
     the band from ~594px to ~404px tall with neither side floating against the other's height. CSS-only
     (`.header-row` becomes a grid at ≥1351px; no markup change). The ≤1350 full-width-row behaviour and its
     re-composition rules are unchanged.
   - **C drops the DETAIL label at desktop** (user): the segmented control reads without it, so C's toolbar is
     fully label-free above 768px. The span stays in the markup (shared with B, which keeps its grey label
     cell — now an explicit part of the B-vs-C character difference) and the mobile M1/M2 sheets keep
     DETAIL/FORMAT/TOGGLE as zone headings. The small-caps-pale label rule still governs the filtering box and
     the mobile headings.
   **Specifications is its own document, not a reskinned TestRunReport** (user caught the
     artifact briefly implying otherwise): verified against the local 3.0.69 output and the published
     BreakfastProvider spec, its body is `<h1>` → **filtering box full-width at the very top** (no Test
     Execution Summary, no CI table, no donut — those are TestRunReport's band) → toolbar row (Scenario
     Timeline but no Component Diagram) → features. Consequence: the spec box is nearly always wider than the
     880px pairing threshold on desktop, so STATUS+DURATION pair there by default. C's Phase 2 must style both
     compositions from the same box component — in the artifact that is one shared `filteringBoxHtml()` builder,
     and production should keep the equivalent single emit path.

## 1. Current state (what implementation touches)

| Area | Files | Notes |
|---|---|---|
| Markup emit | `src/Kronikol/Reports/ReportGenerator.cs` (~5 toolbar emit sites; filter rows at :876–899; `data-dependencies` :1153/:1819; `data-categories` :1186/:1815; happy-path badge :1197; search-help table :832) | Also the merge renderer path — find every emit site by grepping `diagram-toggle` and `-filters` |
| Filter JS | `report-dependency-filter-function.js`, `report-category-filter-function.js`, `report-toggle-happy-paths-function.js` (to be deleted), `report-status-filter-function.js`, `report-duration-filter-function.js` | Dep filter hard-codes `deps.size === 0 → no match` (line 30) — 194 of 341 items in the published report vanish the moment any chip is active, with no escape hatch; categories has `__uncategorized__`, deps has nothing |
| Search | `report-search-function.js` (legacy `@` path + tag-expression evaluator), `advanced-search.js` (`&&/||/!!`, `status:` tokens, quoted phrases) | Both match tags by **exact lowercased set membership**, and `@` names stop at whitespace in both tokenizers — multi-word names ("Happy Path", "Azure Event Hub") are unreachable via `@` today |
| URL hash | `report-url-hash-function.js` | Params today: `q, status, deps, depmode, catmode, hp, cats, dur, pctl`. `save_filter_state`/`restore_filter_state` are empty stubs — the hash is the only persistence |
| Styling | `stylesheets.css` (byte-shared with Kronikol4J — `Constants/Stylesheets.cs:21`), `Constants/Stylesheets.cs` (`VioletThemeStyleSheet` const), `internal-flow-popup-styles.css` (wrongly hosts `.diagram-toggle` base styles — finding F5), `collapsible-notes-styles.css` (pending spinner block) | `CustomCss` and `HtmlSpecificationsCustomStyleSheet` / `InternalFlowPopupCustomStyleSheet` are the injection points users already own |
| Toolbar JS | `report-init-script.js` (mobile-⚙ decided once at load), `diagram-toggle-layout-script.js`, `toggle-script.js`, `collapsible-notes-script.js` | |
| E2E | `tests/Kronikol.Tests.EndToEnd/` — `DiagramToggleLayoutTests`, `MobileResponsiveTests`, plus everything asserting toolbar text | Follow CLAUDE.md Playwright rules (no `force:true`, `PollingInterval = 200`, `FillSearchBar()`, dispatchEvent for SVG) |

**Two shipped bugs, measured on the published report** (documented in the artifact's Current tab): export button
labels wrap inside the buttons from a 1200px window down (`.export-btn` has no `white-space: nowrap`); between
~769 and ~1000px the whole report scrolls sideways (~1090px scroll width in a 900px window — search input's
intrinsic ~184px + `min-width:auto` flex items + 17 dependency chips in a squeezed column).

**Real vocabulary to design against** (published BreakfastProvider report): 9 distinct tags (Happy Path 42 …
four used once; 78 of 147 scenarios untagged); 17 dependencies (name lengths 6–31 chars, median 12, 254 label
chars total; 194 of 341 items have none); 0 categories. Shape: a few heavy names, a long tail.

## 2. Phase 1 — v3.x, Option B (backwards compatible)

### 2.0 The compatibility contract (write it down, test it)

What Phase 1 **must not change**:
- **Selector names and the active-class grammar** that existing custom stylesheets target:
  `.dependency-toggle`/`.dependency-active`, `.category-toggle`/`.category-active`, `.status-toggle`/`.status-active`,
  `.percentile-btn`/`.percentile-active`, `.dep-mode-toggle`/`.cat-mode-toggle`, `.filtering-box`, `.export-btn`.
  On the new tri-state chips, **the existing active class means the "require" state**, so a frozen copy of the
  violet theme (or any user sheet) keeps painting required chips exactly as it paints active chips today. The
  **exclude state is a new additive class** (`.dependency-exclude` etc.) with sensible default styling; old sheets
  simply don't theme it. `.happy-path-toggle` disappears with its control (a dead rule in old sheets is harmless).
- **URL-hash parsing**: every current param keeps its exact meaning forever. `hp=1` maps onto "require the
  happy-path tag"; `deps=a,b` stays the require list. New state rides new params (§2.6). Old links keep working.
- **`data-*` attributes** (`data-dependencies`, `data-categories`, `data-labels`, `data-status`) — other tooling
  reads them.
- The **options surface**: `CustomCss`, `HtmlSpecificationsCustomStyleSheet`, `CustomLogoHtml` etc. keep their
  semantics; `Stylesheets.VioletThemeStyleSheet` is **updated in the same release** to theme the new states, so
  default-configured Specifications reports stay coherent violet throughout.

What Phase 1 **may change** (all locked by the investigation): UI text ("Detail", Expanded/Collapsed/Truncated,
"40 LINES", chips losing Shown/Hidden), toolbar markup structure (the two-div W3 split), and internal CSS that no
published theme targets. The report is a document, not an API — but each rename lands in the changelog.

### 2.1 First slice: the two shipped bugs (own release, independent of everything else)

TDD: red Playwright tests asserting (a) no export-button label wraps at any window width 320–1400 (line-box count
inside the button), (b) `document.scrollWidth <= innerWidth` across the same sweep. Fixes: `white-space: nowrap`
on `.export-btn`; `min-width: 0` on the search input (and any flex item that needs it); a **minimal** header-row
wrap — below ~1000px the filtering box takes `flex-basis: 100%` under the summary row. This is a media-query on
today's markup, not the C band recomposition; it just stops the squeeze. Kronikol4J: mirror `stylesheets.css`.

### 2.2 W3 toolbar split + B skin

**Carried from `NOTE_APPEARANCE_CONTROLS_PLAN.md` (3.22.0), so B keeps them:** the note-width select reads
`Wrap` / `Wide` (values `default` / `full` unchanged) inside `<optgroup label="Note width">`, with a two-state
`title`; the note-font select (`Aa` / `Mono`, `<optgroup label="Note font">`) is emitted only under
`ShowNoteFontControls`, so the default options bar holds two selects, not three. Budget the optgroup when
pinning the selects to the buttons' box metrics: measured, Chromium draws a grouped select **15px wider**
closed (Firefox 0), which is why the JSON/YAML select was left ungrouped. All three note selects now share
one CSS rule and one `.details-pending` rule in `collapsible-notes-styles.css`; B replaces that rule, it
must not split it again.

The full cost map lives in the memory file; headline items: two-div toolbar markup (`.diagram-toggle-tabs` +
`.diagram-options-bar`, `.diagram-format`/`.diagram-flags` spans) at every emit site; word-free dot chips with
`aria-pressed` (script stops rewriting label text — fixes F9); segmented Detail control with B's grey label cell;
truncate-lines dropdown visible only in Truncated mode; dynamic Format↔Toggle divider **with its reset-selector
list updated in the same commit** (three separate regressions in the artifact came from forgetting a drawing
context); pending rail restyle (CSS-only — `setPending` JS untouched; delete the `padding-right: 1.5em` +
spinner `::after` from `collapsible-notes-styles.css`); focus styles (F8); move `.diagram-toggle` base styles out
of `internal-flow-popup-styles.css` into the always-shipped sheet (F5); collapse the duplicated 768px media blocks
(F10); give Violet the missing active-hover rule (F6); ⚙ into the summary with a resize re-check and collapsing
only the options bar; M1 settings panel; T1 top bar with the sticky toolbar-row variant. Baseline alignment per
the round-5c findings: selects pinned to the buttons' box metrics, `align-items: baseline` on the radio group.
Markup also moves `.duration-filters` inside `.filter-row` (§0.5): under B's inline-label grammar plain flex
wrap already gives the all-or-nothing pairing, so B needs no threshold — DURATION sits beside STATUS when the
box affords it and drops whole otherwise.
E2E: re-run `DiagramToggleLayoutTests` (float now targets the tabs bar) and `MobileResponsiveTests`; migrate
text-asserting tests to `data-state`/`aria-pressed`.

### 2.3 Retire "Happy Paths Only"

`HappyPathDetection.cs` already derives happy-path from a tag; the toggle is a one-tag special case. It becomes
the Happy Path chip **pinned first in the new Tags row**; delete `report-toggle-happy-paths-function.js` and the
`hp` filter channel (the hash keeps parsing `hp=1` per §2.0). The badge at `ReportGenerator.cs:1197` is untouched.

### 2.4 Tri-state filter component — Tags, Dependencies, Categories

One component, two presentations, three rows:
- **State model**: per name, off → require → exclude (a tag is binary, so {has, hasn't} subsets give exactly three
  useful states); set-level ANY/ALL over the required names (excludes always mean "none of"); a tri-state
  pseudo-chip per row for the no-value case — **Untagged**, **No dependencies** (this is the missing escape hatch
  for the 194 dependency-free items), **Uncategorized** (absorbing today's `__uncategorized__` button).
- **A (default)**: cycling chips; alt-click/right-click jumps straight to exclude; exclude is signalled three ways
  (minus in the dot, strikethrough, muted red) so it never relies on colour; **strikethrough covers the name only —
  the count is `display: inline-block`** (text-decoration cannot be cancelled on an inline child, only escaped by
  an atomic box); counts rendered at generation time but **hover-revealed** (user, 2026-08-31: always-on numbers
  are noise) — hidden via `opacity`, never `display:none`, so the space stays reserved and chips never change
  width on hover; on touch (no hover) the counts simply stay quiet, which is acceptable because they are
  supplementary. **The reservation is a fixed 3-digit slot** (`3ch` of the count font; user, 2026-08-31 — a
  variable-width blank right side made chips read lopsided once the numbers went quiet), with digits
  left-aligned in the slot so the name-to-number gap never varies (menu rows right-align instead so their
  count column lines up), and the chip balanced by **mirrored insets**: dot zone 8px outer + 7px inner on the
  left, count zone 7px inner + 8px outer on the right. A report with 4-digit counts bumps the slot globally at
  generation time (the generator knows the max), keeping every chip uniform; ANY/ALL switch always present,
  `.tag-mode-idle` (quiet) until two names are required.
- **C (fallback)**: the row collapses to removable pills + "Edit tags…" opening a panel with a find box, counts,
  and a named Any/Has/Hasn't segment per row. **Selected-segment colours** (decided 2026-08-31): Has takes the
  required-chip tint (`--ksel` ground, `--ksel-i` ink); **Hasn't takes the excluded-chip pairing — pink
  `#fbf1f0` ground, dark-red `#8a4a43` ink** — the same tint-plus-dark-ink grammar (border stays the shared
  segment grey; 6:1). Never a saturated solid with white text: it was the only such control in the design.
  **Row ordering** (user-requested 2026-08-31): an A–Z / Count segment beside the find box — A–Z default (the
  chips' and the published report's order), Count sorts by usage descending (ties A–Z), and the pseudo-row
  (Untagged / No dependencies) stays **pinned last under either sort** — it is the escape hatch, not a name.
  Sorting reorders the row nodes, never rebuilds them, so row state survives; the find filter composes with
  whichever order is active.
- **Fallback rule — estimated rendered size, not raw count**: the generator knows every label at build time;
  estimate wrapped rows at a reference box width from label lengths + chip chrome, and present A while the row
  estimate is small (propose ≤3 rows; tune the constant in the artifact before coding). Calibration points: the
  real 9 tags and 17 dependencies must both present as A; the artifact's 29-tag synthetic vocabulary must fall
  back to C. Generation-time decision — nothing switches at runtime.
- **Wiring**: `filter_dependencies`/`filter_categories` gain exclude sets and the pseudo-chip; a new
  `filter_tags` channel evaluates `data-labels`; exports (`report-export-function.js`) and the counts readout
  operate on the same visibility flags they do today — add tests proving filtered HTML/CSV honour tri-state.
- **Set-mode defaults stay per-row in v3** for compatibility: deps AND, cats OR, tags ANY (open question §5.2).

### 2.5 Dependency expressions in the search DSL

- **Advanced path** (`advanced-search.js`): a `dep:` token type alongside the existing `status:` precedent —
  `dep:kafka-broker && !!dep:spanner`, evaluated by exact membership against the item's `data-dependencies` set;
  `dep:none` for the no-dependency case.
- **Legacy path** (`report-search-function.js`): `@dep:name` tokens inside the existing `and/or/not/parens`
  grammar, same evaluator parameterised over which set a token consults.
- **Fix the multi-word gap for both namespaces while in there**: quoted names — `@"happy path"`,
  `dep:"azure event hub"` — in both tokenizers. Today no multi-word tag or dependency is expressible at all.
- Update the search-help panel (`ReportGenerator.cs:832`) and the chips' compiled-expression readout (if adopted,
  §5.3) to teach the syntax. `@tag` semantics are untouched — `dep:` is a separate namespace precisely so tag/dep
  name collisions can't change the meaning of existing saved searches.

### 2.6 URL hash v2

New params carry the new state: `tags=`/`tagx=` (require/exclude lists), `depx=`, `catx=`, `tagmode=`. Emission
keeps old params for old meanings (`deps=` stays the require list); parsing accepts every historical form forever
(`hp=1` → require happy-path tag). Round-trip tests: build state → hash → fresh load → identical state, for new
hashes and for hashes copied from a 3.0.69 report.

### 2.7 Test strategy (Phase 1)

Markup pins first (generator emits the two-div toolbar, chips with `data-tag-state`, counts, updated help table,
violet const covers new states), then Playwright behaviour: cycle/alt-click/ANY-ALL/pseudo-chips; exclude
semantics against a corpus where ALL vs ANY genuinely differ; `dep:`/`@dep:`/quoted-name searches in both paths;
hash round-trips incl. legacy; exports honour tri-state; Violet still paints required chips; F5 regression (toolbar
styled without internal-flow tracking); the §2.1 no-wrap/no-sideways-scroll sweeps become permanent guards; a
baseline-alignment guard asserting Δ = 0 between the options-bar controls' text baselines (port the `baseline.js`
technique — text-run box + `fontBoundingBoxAscent`, no ink thresholds) at DSF 1 and 2.

### 2.8 Kronikol4J, docs, release mechanics

`stylesheets.css` is byte-shared and the report JS is ported — every Phase 1 slice adds its Kronikol4J ledger
entry (the ledger already has 3.0.63–68 gaps; whether to mirror-in-release or batch the port is §5.4). Wiki pages
to update: report filtering, search syntax, theming/customisation (state the §2.0 contract explicitly), plus
changelog + versioning per CLAUDE.md (all packages, same number, tag `v{version}`). Ship Phase 1 as separately
revertible releases in this order: §2.1 bugs → §2.2 toolbar → §2.3+2.4 tri-state → §2.5+2.6 DSL & hash (the last
two can swap).

## 3. Phase 2 — v4.0.0, Option C (breaking)

1. **Token layer first, as its own PR**: `--kron-*` custom properties on `:root` (accent, accent-soft, surface,
   card, ink, label-ink, chip-active bg/fg, focus ring, status colours), every component consuming tokens.
   `VioletThemeStyleSheet` becomes ~a dozen token overrides. **The tokens are the documented public theming
   contract from v4**; internal selectors are explicitly non-contract (wiki statement + BREAKING changelog entry).
   This is what "less fragile to version changes" cashes out as: users override named tokens, Kronikol refactors
   selectors freely.
2. **The full-C page**, phased (header/filters → cards/steps → toolbar/diagrams), each phase green before the
   next: tinted ground + white hairline cards; quiet-bar toolbar (floating uppercase zone labels — small caps +
   pale `#7b8494` = label, sentence case + darker ink = clickable; ghost buttons align by ink, bordered by box);
   the header band recomposition with the filtering box reflowing on **its own** width (`container-type:
   inline-size`; measured thresholds: **≥1351px viewport the band is a two-column grid — summary over CI/donut
   left, box right (§0.6)**, box leaves the header row ≤1350px viewport, CI group unstacks and
   right-anchors only 900–1350px, its own row 769–899px, **STATUS+DURATION pair ≥876px container with the
   complementary forced drop below** (§0.5 — never ship the min-width rule without its max-width partner),
   **equal-width pill grids** ≤520px container (2-col `minmax(0,1fr)`, centred, `Custom` spanning; labels take
   `grid-column: 1/-1` when they go static at ≤430), action grid + static labels ≤430px container —
   `minmax(0,1fr)` columns, `display: contents` export cluster); phone summary-table grid;
   M2 sheet + T2 strip + sticky toolbar-row.
   **Filter-row anatomy (baseline alignment, user-caught 2026-08-31)**: every filter row is
   `<label><span class="fltr-controls">…controls…</span></label-row>` — the row is `display:flex;
   align-items:baseline`, the label a `flex: 0 0 <column>` item, the controls a nested wrapping flex
   (`flex: 1 1 0; min-width: 0; align-items: baseline`). Never absolutely position a row label with a
   hand-tuned `top` (the mock's `top:7px` painted DEPENDENCIES 2.5px below the chip text) and never
   center-align mixed font sizes (mode pills, hints, menu rows, readouts, the FILTERING header — all
   baseline). The nested wrapper is what keeps wrapped chip lines in the control column while the label
   still participates in baseline alignment; a flex container exports its first item's text baseline.
   Two traps with the wrapper: at the stacked-label breakpoint the controls need their own
   `flex-basis: 100%` (with basis 0 they line-break onto the label's 100% line and collapse to zero
   width — every chip spills out); and summary/CI **table cells need `vertical-align: baseline`**
   (the UA sheet middle-aligns tbody, which paints mono value cells 1px below their labels). Floated
   trailers (`.endpoint`) cannot baseline-align — their `margin-top` is measured against the title's
   baseline and guarded by the audit, not styled by eye.
   **Uniform control height (user-caught 2026-08-31)**: every boxed control in a filter row is
   **28px** — 1px border + padding + 16px content, the segmented STATUS/DURATION radios being the
   reference. 12.5px-text controls (chips, the menu trigger) reach it with 5px vertical padding;
   smaller-font controls (11px ANY/AND mode pills, 12px readout pills) pin `line-height: 16px` and
   split their padding **asymmetrically** (mode pills 6 top / 4 bottom, pills 5/5), because on the
   shared baseline a smaller font has less ascent and equal padding hangs the box low. Pin
   line-height on any control whose content includes fallback-font glyphs (the trigger's ▾ caret
   inflated its line box to 17px). The splits are measured, not styled: the height gate asserts
   every control at 28px AND flush (top/bottom ≤0.35px) with its row-mates. Exempt: ghost text
   actions (Clear All, exports, Reset — no box at rest), the ? icon circle, menu-internal segment
   buttons (compact list controls, a different surface).
3. **Breaking-changes list for the migration guide**: report markup/selector changes throughout; custom
   stylesheets written against v3 selectors need the token migration (provide an old-selector → token table);
   the default Specifications theme becomes the **token-based violet** (decision §0.5 — palette listed there),
   so `Specifications.html` keeps the colour identity it has today while `TestRunReport.html` keeps the blue
   default. Not broken: URL hashes, `data-*` attributes, options semantics, data outputs.
4. v4 scope beyond this plan (other accumulated breaking wishes) is tracked elsewhere — this plan claims only the
   redesign + tokens.

## 4. Verification protocol

The investigation's instruments become the regression net, not one-off probes: the width sweep (no horizontal
scroll, no in-button label wrap, 320–1400), the band budget (header-row composition per width), and the baseline
guard all run as E2E against the **generated** report — never against mocks, which is how the original strawman
band survived four rounds. Visual claims get measured at real device scales (1×/1.25×/2×): Chrome snaps painted
text baselines to whole device pixels, so computed-style-only checks pass while the screen is wrong.

**Baseline guard, concretely** (prototyped as the mock's `baselineaudit.js`, 2026-08-31 — port to a Playwright
E2E): for every visible text node, measure the layout baseline exactly by inserting a `0×0 inline-block` probe
before it (its bottom margin edge sits ON the line's baseline — no canvas font metrics, so mono-vs-UI rows
measure true); cluster runs that share a visual row (first-line rects overlap ≥50% AND the nearest common
ancestor is a row-flex/grid/table-row, or both live in one inline formatting context, walking through
inline-level atomics and floats); fail on any cluster whose baseline spread exceeds ~0.35px. Exemptions are
named with reasons, not silenced: icon-in-box glyphs (`?` help circle, `×`/`−` removers, the ⚙ box), text runs
wrapped to multiple lines (a block, not a row member — its one-line neighbours centre against the stack, which
is why the mobile tab bar keeps `align-items: center`), and zone boundaries (the header band's columns). One
sweep of the mock found 147 misaligned rows across 11 defect classes; after the §3 anatomy landed it reads 0.

**Height gate** (prototyped as the mock's `heightprobe.js`): asserts the §3 uniform-control-height contract —
every boxed filter-row control measures the reference 28px (±0.35) AND sits flush with its row-mates (top and
bottom deltas ≤0.35px). Both checks are needed on top of the baseline guard: baseline alignment says nothing
about box extents, and equal heights alone can still sit offset (the first padding split left equal-height
pills 2px low with baselines perfect). The three gates ship together: overflow sweep, baseline guard, height
gate — each catches a class the other two are structurally blind to.

## 5. Open questions (user decisions, none blocking §2.1)

1. **Chip counts**: static generation-time totals (proposed — cheap, stable) vs live re-count under the other
   active filters.
2. **Set-mode defaults**: keep per-row (deps AND, cats OR, tags ANY) in v3; unify at v4?
3. **Compiled-expression readout** under the filter rows (paste-able into search, teaches the DSL): production
   feature or artifact-only teaching device?
4. **Kronikol4J**: mirror each release or extend the existing ledger pin and port in a batch?
5. **Fallback threshold**: confirm "≤3 estimated rows at reference width = A" after tuning in the artifact.
6. **§2.1's minimal band wrap** changes today's header below ~1000px ahead of C — confirmed as acceptable
   bug-fix territory?
7. ~~Selected-Hasn't colour~~ **DECIDED 2026-08-31 from the two-way comparison frame: the excluded-chip
   pairing** — pink `#fbf1f0` ground, dark-red `#8a4a43` ink, border the shared segment grey (6:1). The exact
   grammar of the selected Has (tint ground + dark ink of the hue); the white-on-red it replaces was the only
   saturated-solid control in C. Recorded in §2.4's component spec below.

## 6. Out of scope

Implementation before go; the parked page-shift-on-toggle bug (own memory, own repro tooling); the Kronikol4J
port work itself; any v4 breaking change not listed in §3.
