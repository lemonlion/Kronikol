# PlantUML Theme Support Plan

**Status:** ❌ Not started (plan only) · **Written:** 2026-08-30, last revised 2026-08-31 (upstream PRs merged) · **Repo version at writing:** 3.0.69

> **Upstream [plantuml#2848](https://github.com/plantuml/plantuml/pull/2848) and
> [plantuml#2849](https://github.com/plantuml/plantuml/pull/2849) MERGED 2026-08-31**
> (`3ec0b614`, `0b312d52`). They move work that earlier revisions of this plan carried in Kronikol into
> the engine: `!theme` is applied by the browser build (#2848), and a declared document background is
> painted (#2849). **The remaining gate is the npm publish** — `@plantuml/core` latest is still 1.2026.7,
> which predates both. See §0.

## Purpose and deliverable

Make `ReportConfigurationOptions.PlantUmlTheme` a real feature covering **all 43 themes** bundled with
PlantUML, with no rendering bugs, no loss of diagram interactivity, and no illegible output — where a theme
genuinely themes the whole diagram, **notes included**.

Today the option is worse than broken: in the default rendering mode it does nothing at all, and in the
modes where it does work it breaks note interactions on 4 themes, fails to render on 1, and produces an
invisible diagram on 20.

**Deliverable:** the option ships enabled for the full theme list, with each theme's note styling **derived
from the theme itself**, backed by an automated acceptance harness that renders every theme through the
production engine and fails the build if any theme regresses on render, detection, contrast, or the
distinguishability of the semantic note colours.

## Scope

**In scope: `PlantUmlRendering.BrowserJs` only.** This is the default and the only mode this plan supports
for theming.

**Out of scope: `Server`, `Local` (IKVM), `NodeJs`.** Those modes keep today's `!theme` emission unchanged
and are documented as "theme applied by the engine, unsupported and unvalidated". Two pre-existing defects
found in those paths are recorded in [Appendix A](#appendix-a--defects-found-outside-this-plans-scope) and
deliberately not fixed here.

---

## 0. Upstream prerequisite

Both PRs are **merged on `master`** (2026-08-31: #2848 at `3ec0b614`, #2849 at `0b312d52`; the same-day
snapshot release carries them). What has not happened yet is an npm publish: `@plantuml/core` latest is
**1.2026.7**, cut before the merges. Kronikol currently pins
`lemonlion/plantuml-js-plantuml_limit_size_98304@v1.2026.6-patched` (the `maxSvgSize` fork).

The remaining gate is therefore exactly the one already recorded for the perf work: **upgrade at npm
1.2026.8** (expected to retire the fork — `maxSvgSize` and the LiveBoxes/cache patches are on master too).
Nothing in this plan ships before that pin moves, but the Appendix B re-verification no longer needs to
wait: the harness can run today against an engine built from `master` (`gradlew :plantuml-mit:npmPackage`,
as #2848's own `browser-test/` does), so the palette table can be validated before the release lands.

### What the merged PRs change for us

| Earlier plan revision | After #2848 / #2849 |
|---|---|
| Splice the theme's `.puml` text into every diagram source, because `!theme` is a silent no-op | Emit `!theme <name>` — the engine applies it |
| Prepend inside `shimRender` and re-key the render cache, because 8 KB of theme text per fragment would blow the cache budget | **Dropped.** The source carries one short `!theme` line, so the existing source-keyed cache is already correct |
| Supply a background for every theme from container CSS, because the engine never paints one | Needed only for the 12 themes whose labels vanish on a white page — and done in-source via `!$BGCOLOR = "<value>"` before `!theme`, the themes' own override hook (§2.5). The engine then paints it, and PNG export gets it free from the `<svg>` `style` attribute |
| — | Unknown theme names now raise an engine error instead of rendering silently unthemed |

### What they do not change

- **The note derivation (§2.3) is untouched and still the heart of this plan.** Neither PR touches note
  colours; making a theme's notes Kronikol's notes is still ours to do.
- **Kronikol renders in Web Workers, where the engine cannot fetch `themes.js`.** *(Corrected 2026-09-25,
  `DIAGRAM_COLOURS_PLAN.md` F9, F11, F15, F21.)* The worker host gives the engine a mock `document` with a
  `head`, so `TeaVmScriptLoader`'s script append succeeds, but nothing loads or answers it. From 3.0.76,
  the pin that added `themes.js`, to 3.29.5 that was a hang: the themed render never finished, and nor did
  any later render on the same worker. From 3.29.6 the mock head answers the append with `onerror`, and
  the engine renders unthemed with a console warning. The engine loads OpenIconic, emoji and stdlib
  modules by the same append, so the registration pattern below is the one any later bundle would
  follow. #2848 anticipates exactly this: a host may register
  `globalThis.PLANTUML_THEMES` itself, and both the engine and the generated script resolve it through
  `globalThis`. Kronikol must do that (§2.1).
- **22 of 44 themes declare no document background, in the Java build as much as the browser build.** Of
  those, **11 draw their arrow labels in white** and so vanish on Kronikol's light report page; the other 11
  use dark labels and are perfectly legible on white. Add `spacelab-white`, which declares a white
  background and then draws white text on it, and **12 themes need a background from Kronikol**, supplied through the themes' own `$BGCOLOR` override hook (§2.5).
  That is a theme-authoring gap in PlantUML, not something #2849 left unfinished — its contract is parity
  with the Java build, and it meets it. The host page is Kronikol's to know, not PlantUML's.

---

## 1. What was measured

All numbers come from rendering a Kronikol-shaped probe (actor + entity + database + queue participants,
2 plain notes, 1 stereotyped event note, 1 assertion `hnote across`, a step-delimiter bar, inline-coloured
dependency arrows, a hyperlink) through the **production browser engine** (`core-1.2026.6-patched`, matching
`TrackingDefaults.PlantUmlJsCdnBase`), then running the report's own `findNoteGroups()`, `hasNoteFill()`,
`hasNoteFoldTriangle()` and `findAssertionNoteGroups()` — extracted verbatim from
`collapsible-notes-script.js` — against the resulting SVG in Chromium.

Harness: `tools/render-bench/theme-{native,derive,matrix,detect,contrast}.js`, corpora under
`tools/render-bench/themeprobe/`.

### 1.1 The five facts the design rests on

| # | Finding | Evidence |
|---|---|---|
| 1 | ~~The browser engine ignores `!theme` entirely.~~ **Fixed upstream by #2848.** Measured on the currently pinned engine: no theme resources compiled in. | `none.svg`, `cerulean.svg`, `hacker.svg` all md5 `57badbc4…`; `grep -c "puml-theme" core-1.2026.6-patched.js` → 0 |
| 2 | **Splicing the theme file's own text works.** Still the mechanism Kronikol uses, now via `globalThis.PLANTUML_THEMES` rather than the diagram source (§2.1). | 43/43 themes rendered with correct, distinct palettes |
| 3 | ~~The browser engine emits no diagram background under any mechanism.~~ **Fixed upstream by #2849** for the 21 themes that declare one; the other 22 declare none and are unpainted in the Java build too. | `v-b.svg`, `v-c.svg`: no `background:` on `<svg>`, no background rect |
| 4 | **Inline colours beat `<style>` class colours.** A colour baked onto a directive at capture time cannot be re-themed at report time. | `b-inline.svg` assert contrast **1.05**; `a-style.svg` (same palette, stereotype only) **11.49** |
| 5 | **Every theme's own note styling is readable off a rendered probe**, so a Kronikol palette can be derived from it rather than overriding it. | `theme-native.json` — note fill, border, ink, participant pair, link colour for all 43 |

### 1.2 Acceptance results

43 themes + unthemed baseline, production engine, four configurations:

| Configuration | Renders | Banner | Notes found | Asserts | Contrast fails | Notes look themed |
|---|---|---|---|---|---|---|
| Theme inlined, nothing else | 44/44 | **30** | 4 themes at 0/3 | ok | **30** | yes |
| + padding strip + pins | 44/44 | 0 | 44/44 | 44/44 | 0 | no — fixed colours |
| + two fixed palettes (light/dark) | 44/44 | 0 | 44/44 | 44/44 | 0 | no — fixed colours |
| **+ derived from the theme** ← target | **44/44** | **0** | **44/44** | **44/44** | **0** | **yes** |

The final configuration keeps each theme's own note fill, border and ink, and derives Kronikol's semantic
variants from them. Sample of the measured result (contrast ratios):

| Theme | Note fill / ink | Header on note | Event / assertion text | Step bar text |
|---|---|---|---|---|
| *(unthemed baseline)* | `#FEFFDD` / `#000000` | 3.98 | 20.56 | 16.37 |
| `amiga` | `#0B58A8` / `#FFFFFF` | 3.27 | 7.06 | 7.06 |
| `carbon-gray` | `#FFFFFF` / `#262626` | 3.54 | 15.13 | 13.76 |
| `cerulean-outline` | `#FFFFFF` / `#033C73` | 3.61 | 11.07 | 13.52 |
| `crt-amber` | `#282828` / `#FFB000` | 3.45 | 8.05 | 8.05 |
| `vibrant` | `#7FFFD4` / `#454645` | 4.21 | 7.75 | 9.48 |

**Zero issues across all 44.** That is the target state this plan implements.

---

## 2. The mechanism

### 2.1 Emit `!theme`, and register the theme text ourselves

The source carries a plain `!theme <name>` line, which #2848 makes the engine honour.

Getting the theme text to the engine is the one place Kronikol cannot use the default path. #2848 serves
bundled themes from a generated `themes.js` (326 KB, 27 KB gzipped) fetched on demand by
`TeaVmScriptLoader` — which appends a script tag and therefore **cannot work inside a Web Worker**, which is
where Kronikol renders every diagram. (The worker host's mock `document` accepts the append and nothing
answers it: a hang until 3.29.6, an `onerror` and an unthemed render from 3.29.6, §0.) The PR provides for this: the engine consults
`globalThis.PLANTUML_THEMES` before fetching.

So Kronikol embeds **only the selected theme's text** in the report HTML through the existing placeholder
mechanism (`DiagramContextMenu.GetBrowserRenderScript` already substitutes `__PLANTUML_CDN_BASE__`,
`__BROWSER_RENDER_WORKERS__` and friends — add `__PLANTUML_THEME_SOURCE__`), and the worker host registers
it as a one-entry `globalThis.PLANTUML_THEMES` map before the first render.

Four things fall out of that, all of them wanted:

- works in workers, which the fetch path cannot;
- no 326 KB fetch — a themed report pays ~8 KB (median theme size), and stays self-contained and offline;
- Kronikol still controls the theme text, so the padding strip (§2.2) remains possible;
- the main-thread fallback path registers the same global, so both render paths behave identically.

### 2.2 Strip the deprecated padding skinparams

30 of 43 theme files carry `skinparam ParticipantPadding` / `BoxPadding` / `Padding`, which the 1.2026
engine renders as a yellow *"Please use CSS style instead of skinparam …"* banner **inside the diagram**.
Filtering lines matching `^\s*skinparam\s+\w*padding\b` removes it with no other change to the output
(verified: identical fill set before and after, minus the banner box's own colours). Done once, at embed
time — which is only possible because Kronikol supplies the theme text itself (§2.1).

**Confirmed in the merged artefact.** The `themes.js` now on `master` (326 KB) carries **31** deprecated
padding skinparams across the bundled themes, so a `!theme` through the published engine will surface the
banner exactly as the spliced-text measurements did. The strip stays — but note the delivery consequence:
because Kronikol registers its own `PLANTUML_THEMES` entry (§2.1), the stripped text is what the engine
sees; if Kronikol ever fell back to the engine's own `themes.js`, the banners would return. If upstream
later cleans the theme files the strip becomes a harmless no-op; the harness's zero-banner assertion tells
us either way.

### 2.3 Derive the note palette from the theme

This is the heart of the plan. Nothing is pinned to a Kronikol constant; every value comes from the theme's
own rendered output, measured offline (§2.4) and baked into the registry.

| Value | Derivation |
|---|---|
| **Note fill** | The theme's own note fill. Transparent (`#00000000`, the 4 `-outline` themes) → the theme's page background: the outline look is preserved exactly, and `hasNoteFill()` finally has something to find. Never pure black — `hasNoteFill()` rejects `#000000`, so a black fill is nudged to `#0A0A0A`. |
| **Note border** | The theme's own note border. |
| **Note ink** | The theme's own note font colour. Auto-corrected **only** when the theme's own pair fails 4.5: try near-black and near-white, and if neither clears it, walk the note fill's *lightness* (keeping the theme's hue and saturation) until it does. |
| **Event / assertion-pass / assertion-fail** | The note fill hue-rotated to 196° / 132° / 4° at constant lightness — so on a pale note they come out as pale tints (reproducing today's `#CFECF7` / `#D4EDDA` / `#F8D7DA` almost exactly from the default theme), and on a dark note as dark tints. Because lightness is preserved, they inherit the note's text contrast for free. |
| **Separation guard** | A hue rotation does nothing when the theme's note is *already* that hue — `sandstone`, `superhero` and `minty` have cyan notes, so the event tint came out at ΔE 3–7 from the note itself, i.e. invisible as a category. The tint therefore also walks lightness until it clears **ΔE 14** — the minimum pairwise distance of Kronikol's own shipped palette — while never dropping text contrast below 4.5. |
| **Header line** (`<color:gray>`) | The note ink blended toward the note fill, stopping at 3.9 — matching today's `#808080`-on-`#FEFFDD` (3.87), so the default theme does not regress. |
| **Step-delimiter bar** | The theme's own participant fill/ink pair when it is opaque and clears 4.5, else inverse video from the theme's dominant text and background. |
| **Setup partition** | The page background nudged 7% toward the note ink. |
| **Link** | The theme's own hyperlink colour, handed to the JS as data rather than pinned. |
| **Background** | The theme's declared background where it has one (10 of 43); otherwise a neutral by family; 6 hand-picked overrides (§3.1). |

Emitted after the theme text, so the derived values win:

```
skinparam noteBackgroundColor  <palette.NoteBackground>
skinparam noteBorderColor      <palette.NoteBorder>
skinparam noteFontColor        <palette.NoteText>
skinparam hyperlinkColor       <palette.Link>
<style>
 .eventNote     { BackgroundColor <palette.EventNote>       FontColor <palette.NoteText> FontSize 11 RoundCorner 10 }
 .assertionNote { BackgroundColor <palette.AssertPass|Fail>  FontColor <palette.NoteText> FontSize 11 RoundCorner 5 }
</style>
```

`noteBackgroundColor` is also what takes the four `-outline` themes from **0 of 3 notes detected to 3 of 3**.

### 2.4 Generate the registry offline, don't compute at runtime

The derivation needs a *rendered* diagram to read the theme's real note styling from, so it cannot run at
report time. It runs as an offline generator (`theme-native.js` → `theme-derive.js`) that renders each
theme once and emits a checked-in palette table. Regenerating is a deliberate act, reviewed in a diff,
gated by the acceptance harness — not something that happens on a user's machine.

### 2.5 Supply a background only where the theme declares none — via `$BGCOLOR`

#2849 paints the document background for the themes that declare one. For those Kronikol does nothing:
the diagram is self-contained, and `getBackgroundColor()` in `context-menu-script.js` finds the value on
its first check, so PNG export is fixed upstream too.

**11 themes deliberately default to transparent** (`black-knight`, `cyborg`, `cyborg-outline`, `hacker`,
`minty`, `sandstone`, `sketchy`, `spacelab`, `superhero`, `superhero-outline`, `united`) and draw white
arrow labels, which vanish on Kronikol's white report page. This is not an omission — every one carries the
standard theme parameterisation, an explicit contract for exactly our situation:

```
!if %not(%variable_exists("$BGCOLOR"))
!$BGCOLOR = "transparent"
!endif
skinparam BackgroundColor $BGCOLOR
```

So the fix is to use that contract: for these 11 (plus `spacelab-white`, whose default is its own bug),
Kronikol emits **`!$BGCOLOR = "<value>"` before the `!theme` line**. Verified on the jar
(`!$BGCOLOR = "#1E1E1E"` + `!theme hacker` → `background:#1E1E1E` painted; without it, nothing), and #2849's
contract is parity with the jar, so the post-merge browser engine paints it identically.

This is strictly better than the container-CSS approach an earlier revision carried, and better than a
trailing `skinparam backgroundColor`:

- the value flows through the theme's **internal** `$BGCOLOR` uses (`FontColor $BGCOLOR` inverse-video
  text, box and sequence backgrounds), which a post-hoc skinparam would leave at `transparent`;
- the SVG is self-contained — PNG export, the lightbox, and any future export path get the background with
  no `data-` attribute plumbing;
- it is the theme author's own extension point, so an upstream theme redesign keeps working.

The 11 background-less themes with dark labels (`aws-orange`, `bluegray`, `cerulean`, `cerulean-outline`,
`cloudscape-design`, `lightgray`, `materia`, `materia-outline`, `metal`, `silver`, `sketchy-outline`) are
legible on white and need nothing. The container CSS keeps only one small job: painting the report page's
diagram panel the same colour as a painted SVG so the padding around it matches, read from the palette.

### 2.6 Stop baking colours at capture time

The one structural change. `Track.cs:16-17` writes `#d4edda`/`#f8d7da` into the captured record,
`StepCollector.cs:81`, `Ingestion/InteractionRecord.cs:283` and `TrackingDiagramOverride.cs:78` write
`#black:<color:white>`, and `PlantUmlCreator.cs:388,1137` write `<color:gray>`. Fact 4 shows those literals
beat any report-time `<style>`, so a theme can never re-colour them.

Capture emits the **stereotype only**; the report-time `<style>` supplies the colour. Verified safe:
`stripAssertionNotes` and `stripStepDelimiters` match on `<<assertionNote>>` / `<<stepDelimiter>>` with
`[^\n]*` swallowing the rest of the line, so removing the inline colour does not affect the visibility
toggles.

`TrackingDiagramOverride.cs:78` emits `hnote across #black:<color:white>Test …` with **no** stereotype.
Give it one (`<<testBoundary>>`); `stripStepDelimiters` matches `<<stepDelimiter>>` specifically, so a new
stereotype does not change what the step toggle removes.

**Backward compatibility is mandatory.** Already-captured trace files and externally-ingested captures carry
the old literals. A report-time rewrite maps the known legacy literals (`#d4edda`, `#f8d7da`, `#cfecf7`,
`#black:<color:white>`, `<color:gray>`) onto palette values, so old data themes correctly and unthemed
reports keep byte-identical output.

### 2.7 Repair the remaining colour-coupled JavaScript

| Site | Change |
|---|---|
| `plantuml-browser-render-script.js:1099` | Capture each link text's original fill before recolouring and restore *that*, not `#000000` |
| `plantuml-browser-render-script.js:1062` | Match the theme's link colour from a data attribute rather than the literal `#0000ff` |
| `collapsible-notes-script.js:248-407` | Injected hover chip colours (`#ffffff` chip, `#999` border, `#666` glyph) derive from the note fill and ink |
| `internal-flow-popup-styles.css:151-152` | Hover link colour from a CSS variable |

---

## 3. Per-theme data

Everything derives except the **background** and, for six themes, a hand-picked override.

Measured from the rendered SVG, not from a regex over the theme files:

- **13 themes declare a non-white background** — painted by the engine after #2849, self-contained, nothing
  needed from Kronikol. (The PR counts 12; the difference is one near-white theme, `mars` at `#F9F9F9`.)
- **8 declare white**, which the paint rule skips as the skin default. Harmless on a white page — except
  `spacelab-white`, which then draws its arrow labels in white too: contrast 1.00 against its *own* declared
  background, in every engine. That is a bug in the theme file, and the only one of its kind.
- **22 declare nothing**, of which 11 need a background from Kronikol (§2.5).

### 3.1 The six themes needing a hand-picked background

| Theme | Body text | Derived neutral gives | Chosen | Why |
|---|---|---|---|---|
| `spacelab-white` | `#FFFFFF` | **1.00** | `#F2F5F9` | Declares a white background and draws white text on it — broken against its own ground, in every engine |
| `mimeograph` | `#9275B6` | **2.61** | `#F2EFEC` | Low-contrast by its author's intent |
| `cerulean-outline` | `#2FA4E7` | **2.77** | `#FFFFFF` | Outline style uses a mid-tone accent as body text |
| `cyborg-outline` | `#2A9FD6` | **2.99** | `#1B1B1B` | as above |
| `materia-outline` | `#2196F3` | **3.12** | `#FFFFFF` | as above |
| `superhero-outline` | `#DF691A` | **3.40** | `#2B3E50` | as above |

### 3.2 Known residue

- **`materia-outline` lands at ΔE 12** between its note fill and one semantic tint, against the ΔE 14 floor.
  Its note fill is the page white, which leaves little lightness headroom while holding 4.5 text contrast.
  Either hand-tune that one tint or accept 12 as documented. Every other theme clears 14.
- **Dependency arrow colours need no change.** `DependencyPalette`'s eight inline colours were checked
  against every candidate background: worst case 2.10 on white (`#F39C12` amber), which is today's shipped
  behaviour, improving to 2.36 on the dark neutral. Arrow strokes are decorative 1px lines, not text, so no
  threshold applies. The harness records the value so a regression is visible.

---

## 4. Phases

Each phase has a gate that must pass before the next begins. TDD throughout, per `CLAUDE.md`.

### Phase 0 — Acceptance harness (test infrastructure first)

1. Promote the probe scripts into `tools/theme-audit/` with the corpus checked in.
2. Add `ThemeAcceptanceTests` in `Kronikol.Tests.EndToEnd` (it already has Playwright and can reach the
   engine). For every theme, render the probe and assert:
   - renders without engine error and produces an `<svg>`;
   - zero deprecation-banner text nodes;
   - `findNoteGroups()` count equals the source note count, with the right text per group;
   - `findAssertionNoteGroups()` finds the ✓ and ✗ bars;
   - note ink vs note fill ≥ 4.5; header vs note fill ≥ 3.0; event, assertion and step-bar text ≥ 4.5;
   - theme body text vs palette background ≥ 4.5;
   - **ΔE ≥ 14 between the note fill and each semantic tint, and between pass and fail** (the check that
     keeps a derived palette meaningful, not merely legible).
3. Run it in the E2E Remainder job — 43 renders is the slowest test in the repo by construction. Keep the
   skip/exclude lists in sync (see `ci-invisible-projects`).

**Gate:** the harness reproduces the measured matrix — 44/44 clean on the derived palettes, and the
*unpinned* configuration reproduces the 30 banners and 4 zero-detection themes. A harness that cannot show
the bug cannot prove the fix.

### Phase 1 — Palette generator and registry (data only)

1. Port `theme-native.js` + `theme-derive.js` into the offline generator, with the derivation rules of §2.3
   as the specification and unit tests over the colour maths (hue rotation at constant lightness, the ΔE
   separation walk, the header blend, the black-fill nudge).
2. `KronikolDiagramTheme` enum (43 members + `Default`) and `DiagramThemePalette` record: `Background`,
   `NoteBackground`, `NoteBorder`, `NoteText`, `HeaderText`, `EventNote`, `AssertPass`, `AssertFail`,
   `StepBar`, `StepBarText`, `SetupPartition`, `Link`.
3. Generated palette table checked in, plus the 43 stripped theme texts as embedded resources (287 KB raw)
   so themed reports work offline and deterministically.
4. Keep `PlantUmlTheme` as `string?` for compatibility; add the typed option; an unknown string warns and
   falls back to no theme rather than silently emitting a directive that does nothing.

**Gate:** unit tests — every enum member resolves to a palette; every palette passes the same arithmetic the
harness applies; every embedded theme text is non-empty and free of `skinparam *padding`; regenerating the
table from the checked-in probes is byte-stable.

### Phase 2 — Source generation

1. `CreatePlantUmlPrefix` emits `!theme <name>` followed by the derived block (§2.3), in that order, so the
   derived values override the theme's own note styling rather than the reverse. (`ComponentDiagramGenerator`
   has this ordering backwards today — see Phase 5.)
2. Event note, assertion notes, step bar, setup partition and header lines become palette-driven (§2.6).
3. Legacy-literal rewrite for previously captured and ingested data.

**Gate:** unit tests pin the emitted prefix for the default and two themes; **the unthemed prefix must be
byte-identical to today's** so no existing report output changes.

### Phase 3 — Report delivery

1. `__PLANTUML_THEME_SOURCE__` placeholder; the selected theme's stripped text embedded once.
2. The worker host registers it as `globalThis.PLANTUML_THEMES = { "<name>": "<text>" }` before the first
   render, and the main-thread fallback path does the same. This is the step that makes theming work at all
   inside a worker, where `TeaVmScriptLoader` cannot fetch `themes.js`.
3. `!$BGCOLOR` emission for the 12 themes that need it (§2.5); container panel colour matched to the palette.
4. Assert the engine never fetches `themes.js` — a fetch means the registration did not take, and the report
   would silently depend on the network.

*No cache change is needed.* An earlier revision of this plan had to re-key `shimRender` because it spliced
8 KB of theme text into every fragment's source, and the cache is keyed on that source
(`plantuml-browser-render-script.js:337`). With #2848 the source carries a single `!theme` line, so the
existing key is both small and correct.

**Gate:** E2E — a themed report renders, note collapse/expand and hover chips work, PNG export carries the
right background on both a background-declaring and a background-less theme, render telemetry still shows
cache hits across note toggles, and no `themes.js` request is made.

### Phase 4 — JavaScript colour-coupling repairs

The five sites in §2.7, each with an E2E test on one light-host and one dark-host theme.

**Gate:** the full existing E2E suite passes on the default theme (no regression), plus the new tests.

### Phase 5 — Component diagrams

1. Move `!theme` above the `skinparam` block in `ComponentDiagramGenerator` (line 116 currently lands after
   lines 74–110, so the theme silently overrides the dependency-type palette).
2. Emit the theme in `ComponentDiagramDiffer`, which ignores it entirely today.
3. Apply the same delivery mechanism — component diagrams render through the same browser path.
4. Decide whether `InternalFlowRenderer`'s activity diagrams (own `skinparam ActivityBackgroundColor
   #f0f4ff`) follow the theme or stay fixed — they render in a popup over the report page.

**Gate:** component-diagram golden tests updated; a themed component diagram and its diff match.

### Phase 6 — Documentation and release

Wiki (`Diagram-Customisation.md`, `Report-Configuration.md`, `Component-Diagrams.md`, and
`PlantUML-Browser-Rendering.md`, which currently claims themes "are applied by the browser engine" — false),
README if relevant, CHANGELOG, patch bump across **all** packages, tag, push.

---

## 5. Tests that will need updating

| Test | Why |
|---|---|
| `PlantUmlCreatorTests` — `BackgroundColor #cfecf7`, `partition #F6F6F6 Setup` | Prefix and colour emission change |
| `LargeNoteSplitTests:214` | Hard-codes the full expected prefix string |
| `ReportTestHelper` fixtures (`#d4edda`, `#cfecf7`, `partition #F6F6F6`) | Sample sources embed the literals |
| `TrackThatTests`, `TrackThatIntegrationTests`, `TrackValueResolutionTests` | Assert `#d4edda`/`#f8d7da` in captured records — these move to stereotypes |
| `DiagramContextMenuTests:30` | Asserts the literal `rect.getAttribute('fill')` appears in the script |
| `DiagramNoteMixedParticipantTests:182`, `DiagramNotePartitionTests:169` | Match on `#cfecf7` / `#F6F6F6` |
| Kronikol4J `GoldenHtmlParityTest`, `ComponentDiagramReportGoldenTest` | Pin .NET↔Java report parity; any prefix or script change diverges until Java mirrors it |

The Phase 2 gate (unthemed output byte-identical) is what keeps this list from growing.

---

## 6. Open decisions

1. ~~Embed all 43 theme texts, or fetch from the CDN?~~ **Resolved by the worker constraint** (§2.1):
   Kronikol registers the selected theme itself, so the package embeds all 43 stripped texts (287 KB raw)
   and each report carries exactly one (~8 KB median). The engine's own `themes.js` is never fetched.
2. ~~Do notes follow the theme or stay Kronikol's?~~ **Resolved: notes follow the theme** (§2.3), validated
   across all 43.
3. **`materia-outline`'s ΔE 12 tint** — hand-tune or document (§3.2).
4. **Dark diagram on a light report page.** A dark-host theme puts a dark rectangle in the middle of a light
   report. Does the report page follow (a report-level dark treatment), or does the diagram sit as a
   distinct panel? Affects `Stylesheets.cs` and the violet theme.
5. **Whether to keep the raw-string escape hatch** once the enum exists.

## 7. Risks

- **The derivation is only as good as its inputs.** It reads a rendered probe, so a probe missing a
  construct produces a palette that has never been checked for it. The probe corpus is part of the
  specification and must cover every Kronikol construct.
- **The npm publish is the schedule.** Both PRs are merged; nothing ships until `@plantuml/core` 1.2026.8
  (or equivalent) is published and the `maxSvgSize` fork is retired or rebased onto it. Phase 0 and the
  Appendix B re-verification can run now against a master-built engine; hold Phase 2 onward until the pin
  can actually move.
- **Worker registration is load-bearing and silent when it fails.** If `globalThis.PLANTUML_THEMES` is not
  registered, the engine falls back to fetching `themes.js`, which cannot succeed in a worker — and #2848's
  behaviour there is to render the diagram *unthemed* rather than fail. That is a silent regression to
  exactly today's bug, hence the explicit no-fetch assertion in Phase 3. *(Corrected 2026-09-25.)* In the
  worker the unthemed render was not what happened from 3.0.76 to 3.29.5: the append was never answered,
  so the diagram never rendered, and nothing after it on the same worker did either. It holds from 3.29.6
  only because the worker host's mock head answers the append with `onerror` (`DIAGRAM_COLOURS_PLAN.md`
  S3a). The Phase 3 no-fetch assertion stays load-bearing.
- **Kronikol4J divergence.** Parity tests are pinned on 3.0.43 fixtures already; this widens the gap unless
  the Java port mirrors the prefix and script changes.
- **Legacy-literal rewrite is a data-compatibility surface.** Ingested captures may carry variants; the
  rewrite must be conservative and leave anything unrecognised alone.
- **Theme files are older than the engine.** The padding strip handles today's warnings; a future engine
  bump could surface new ones. The harness's zero-banner assertion is what catches that.

---

## Appendix A — Defects found outside this plan's scope

Both pre-existing, unrelated to theming, affecting only `Local` (IKVM) rendering. Recorded because the
investigation surfaced them; **not fixed by this plan.**

1. **Sequence-diagram `partition` is unsupported by the pinned jar.** `SeparateSetup`/`HighlightSetup` emit
   `partition #F6F6F6 Setup` … `end`, which PlantUML 1.2024.6 rejects outright, replacing the whole diagram
   with an error image. Works in the browser engine (1.2026.6) and on the PlantUML server. The suite misses
   it because unit tests assert source text and E2E renders with `BrowserJs`. Fix would be a jar bump or
   emitting `group` for older engines.
2. **`carbon-gray` cannot parse Kronikol's stereotyped notes on the jar.** A stereotyped note with no inline
   background colour is a syntax error under that theme in 1.2024.6. Not reproducible in the browser engine,
   where `carbon-gray` renders correctly. Note that §2.6 removes the inline colour, which would make this
   worse on the jar — another reason theming stays BrowserJs-only.

## Appendix B — Re-verification needed against the post-merge build

Everything in §1 was measured on the pre-merge engine (1.2026.6-patched) with the theme text spliced into
the source. The rendering is the same either way — the theme text reaches the preprocessor by a different
route — but the harness must be re-run against a post-merge engine before Phase 1 freezes the palette
table. This is now actionable: build one from `master` with `gradlew :plantuml-mit:npmPackage` (the same
artefact #2848's `browser-test/` checks run against), without waiting for the npm release. Specifically:

1. All 43 themes still render, still detect 3/3 notes and 1/1 assertions.
2. The zero-banner assertion — does `themes.js` carry the deprecated padding skinparams (§2.2)?
3. `theme-native.json` regenerated through `!theme <name>` rather than spliced text, and the derived palettes
   diffed against the checked-in table. Any difference is a real behavioural change and needs explaining,
   not overwriting.
4. Which themes #2849 paints for — this plan assumes 21 of 44, taken from the PR and corroborated by the
   jar renders in `themeprobe/all`. Confirm against the real build and set §3.1 from that, not from the
   theme-file regex.

## Appendix C — Harness commands

```
node tools/render-bench/theme-native.js                                     # read each theme's own styling
node tools/render-bench/theme-derive.js                                     # derive palettes + probes
node tools/render-bench/render-svg.js core-1.2026.6-patched.js themeprobe/derived/*.puml
node tools/render-bench/theme-matrix.js derived                             # acceptance matrix
```

Corpora: `themeprobe/native` (no pins — the derivation's input) · `themeprobe/derived` (the target state) ·
`themeprobe/js43`, `js43b`, `js43c` (the pinned configurations, kept as the counter-examples) ·
`themeprobe/all` (jar renders) · `themeprobe/adj` (same-fill adjacency stress) · `themeprobe/arch`
(inline-vs-style precedence proof).

Generated data: `theme-native.json` (measured input) · `theme-palettes.json` (the 44 derived palettes) ·
`theme-matrix.json` (last acceptance run).
