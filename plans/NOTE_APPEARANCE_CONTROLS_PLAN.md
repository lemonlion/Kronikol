# Plan: the monospace note control goes behind a flag, and the note-width dropdown says what it does

**Opened 2026-09-15**, from the user's review of 3.0.85 on a real report. **Status: EXECUTED 2026-09-18 as
3.22.0** (green-lit by the user that day; written against 3.17.0, so every "3.18.0" below reads 3.22.0).
One design claim did not survive measurement; see *Execution record* at the end.

Two requests, one release:

1. **The monospace note feature is turned off.** It is hidden behind a configuration flag whose default
   is off, and when off nothing of it appears in the report: no `M`/`A` glyph in a note's hover cluster,
   no `Aa / Mono` dropdown at report or scenario level.
2. **The `Fit / Full` note-width dropdown is fixed.** (a) Its labels do not say what it does, and using it
   often shows nothing. (b) It is not styled like the other dropdowns.

## 0. How the monospace control got here, and why this plan withdraws it

The user asked for one thing on 2026-09-11: a way to widen a note so a SQL query could be read
(`NOTE_WRAP_AND_WIDTH_PLAN.md`, Part B). While planning that, §2.9 of the plan measured that PlantUML
draws note text proportionally and scatters an aligned `AS` column across 65.8px, and *recommended*
shipping a monospace toggle as a sibling of the width control. The recommendation was adopted inside
the same release without a separate go. The measurement stands; the decision to ship it as a
default-visible control was the plan's, not the user's, and the user's verdict on seeing it is "not sure
how useful it is". This plan takes it out of the default UI and leaves it available to anyone who
opts in. Nothing measured in §2.9 or §2.10 is disputed here; only the default.

A lesson recorded for the next plan: a recommendation in a plan is not a green light for a feature the
request did not name. Ship the ask; put the sibling in the open questions.

## 1. What exists today, by surface

Everything below was read from the code on 2026-09-15.

### 1.1 Monospace

| Surface | Where |
|---|---|
| Per-note glyph `M` / `A` (drawn only for notes with more than one line) | `src/Kronikol/Reports/collapsible-notes-script.js:418-436` (`appearanceInfo.monoUseful`), state and handlers at `:904-921` |
| Bulk dropdowns `Aa / Mono`, report level and scenario level | `src/Kronikol/Reports/ReportGenerator.cs:957-966` (built), `:720` (scenario toolbar), `:1334` (report toolbar) |
| Script handlers `window._setNoteFont` / `_setScenarioNoteFont` | `collapsible-notes-script.js:2950-2970` |
| Per-note state and re-render (`setNoteFont`, `setAllNoteFonts`) | `collapsible-notes-script.js:1691-1698`, `:1729-1743` |
| Stereotype `<<kronNoteMono>>` and the `.kronNoteMono { FontName "Courier New" }` class in the injected style block | `collapsible-notes-script.js:1514-1524`, `:1553-1559` (`noteAppearanceStyle` always declares both classes) |
| Configured start state `ReportToggleDefaults.NoteFont` (`NoteFontFamily.Default` / `Monospace`) | `src/Kronikol/Reports/ReportToggleDefaults.cs:42-47`, `src/Kronikol/Reports/NoteAppearance.cs`, resolver `ReportToggleDefaultsResolver.cs:21`, `:70` |
| Script seed `window._noteFontDefault` and the zero-click first paint | `src/Kronikol/Reports/DiagramContextMenu.cs:113`, `collapsible-notes-script.js:2081`, `:2337-2338` |
| Tests | `tests/Kronikol.Tests.EndToEnd/NoteAppearanceTests.cs:193-227` (paint + alignment), `:288-300` (bulk select); `tests/Kronikol.Tests/Reports/ToggleDefaultsMarkupTests.cs:259-283`; `tests/Kronikol.Tests/Reports/NoteAppearanceScriptTests.cs:145-186`; `tests/Kronikol.Tests/Reports/ReportToggleDefaultsResolverTests.cs:36` |
| Docs | `../Kronikol.wiki/PlantUML-Browser-Rendering.md:153-200`, `../Kronikol.wiki/Report-Configuration.md:233`, `CHANGELOG.md` 3.0.85 and 3.0.86 |

The engine-contract E2E facts (`A_width_class_on_one_note_widens_only_that_note` and the compose fact,
`NoteAppearanceTests.cs:106-146`) stamp `<<kronNoteMono>>` at source level through
`ReportTestHelper.GenerateReportWithNoteWidthClassDiagram`. They test the engine, not the UI, and are
untouched by this plan.

### 1.2 The width dropdown

Emitted by `BuildNoteAppearanceSelect("note-width-select", "Note width", …)` at `ReportGenerator.cs:731-736`
with options `("default", "Fit")` and `("full", "Full")` (`:958`). No visible label; `aria-label` and `title`
both read "Note width".

**Finding 1, the labels.** "Fit" and "Full" are near-synonyms to a reader. "Fit" suggests *fit the note to
the diagram*, which is what "Full" actually does; nothing says that "Fit" is the default wrap width. The
JSON/YAML select gets away with no label because its options are the names of formats; these are not.

**Finding 2, the behaviour is conditional and often invisible.** "Full" sets `MaximumWidth` on every note
(`setAllNoteWidths`, `collapsible-notes-script.js:1706-1727`). `MaximumWidth` is a ceiling: a note whose
text already fits does not change size. So in a scenario where no note wraps, choosing "Full" re-renders
and nothing moves; and "Fit" is the state the report starts in, so choosing it first is a no-op. The
per-note `↔` glyph is only *offered* where widening would change something (`canWiden`, `:912-917`);
the dropdown has no such gate and cannot, because containers render lazily (a scenario's eligibility is
unknowable until every diagram in it has drawn). This is the "not clear from behaviour" half.

**Finding 3, the styling.** `src/Kronikol/Reports/collapsible-notes-styles.css` styles
`.truncate-lines-select` (`:14-20`) and `.note-format-select` (`:23-29`): `padding: 0.2em 0.3em`, a
1px `rgb(180,180,180)` border, `border-radius: 0.4em`, `font-size: 0.85em`, `margin-left: 0.5em`. **There
is no rule for `.note-font-select` or `.note-width-select` anywhere in the shipped CSS.** Both render as
the browser's default `<select>`: larger text, square corners, a different height, and no left margin,
so they sit against the JSON/YAML select. The same omission applies to the pending state: the
`.details-pending` selector list (`:43-45`) names the radio buttons, the truncate select and the format
select only, so a bulk `Full` over a large report (measured at ~40ms per container, ~20s on the 488-diagram
production report) throbs on nothing. `renderWithPending` adds the class; no CSS answers it. That is a
bug, and it compounds Finding 2: the one moment the control could show that it is doing something, it
shows nothing.

### 1.3 The Java port

`../Kronikol4J/src` contains neither `note-font-select` nor `note-width-select`, and
`../Kronikol4J/docs/REMAINING_PARITY.md` has no 3.0.85 entry: Part B of `NOTE_WRAP_AND_WIDTH_PLAN` never
reached the port. Hiding the font control by default therefore *shrinks* the divergence between a
default .NET report and the Java one; the width select remains a known gap. §6 records it.

## 2. Design

### 2.1 The flag

```csharp
/// <summary>
/// <c>BrowserJs</c> only. When <c>true</c>, the report offers the monospace note controls: the
/// <c>M</c>/<c>A</c> glyph in a note's hover cluster and the note-font dropdowns at report and
/// scenario level. Default: <c>false</c>, so no monospace control appears anywhere in the report.
/// A configured <see cref="ReportToggleDefaults.NoteFont"/> of <see cref="NoteFontFamily.Monospace"/>
/// is honoured either way; with the controls hidden it makes every note monospace with no way to
/// switch in the report, which is a legitimate configuration.
/// </summary>
public bool ShowNoteFontControls { get; set; }
```

On `ReportConfigurationOptions`, beside `ShowNoInteractionsMarker` (`ReportConfigurationOptions.cs:406-410`,
the existing presence-flag precedent) and the note options (`NotePayloadFormat` `:427`,
`DiagramNoteWrapWidth` `:446`). One flat option for both reports.

**Transport: the flag rides `ResolvedToggleDefaults`, not a new `GenerateHtmlReport` parameter.** The
three consumers that must agree (the toolbar markup at both levels, and the script seed that gates the
glyph) all already read the resolved record; `ReportToggleDefaultsResolver.Resolve` sets it from the flat
option the way it sets `NotePayloadFormat` (`ReportToggleDefaultsResolver.cs:52-58`), and the overlays
leave it alone. That is one plumbing chain rather than two, which is the 3.0.67 lesson
(`_computeGlobalNoteIndex`) applied before it bites. The record property is documented as *not a start
state*: it says whether a control exists. `ResolvedToggleDefaults.BuiltIn` keeps `false`, so
`ToggleDefaultsBaselineTests.Unset_config_produces_byte_identical_output_across_null_builtin_and_resolved`
holds without modification.

Not chosen, and why:

- A nullable on `ReportToggleDefaults` (per-report, inheritable). Those are start states, every one is
  `bool?`/enum?, and `ReportConfigurationOptionsDefaultsTests` reflects over the group expecting nulls. A
  presence flag there is a category error, and nobody has asked for the two reports to differ.
- A new `GenerateHtmlReport` parameter, like `showNoInteractionsMarker`. Works, but the script seed would
  need the same value through a second path (`GetCollapsibleNotesScript`), which is the fork this design
  avoids.
- Removing the feature. Full removal deletes public surface (`NoteFontFamily`, `ReportToggleDefaults.NoteFont`),
  which is a major bump and reserved for v4. The flag leaves that door open: `V4_PLAN.md` may list "drop
  the monospace note control, or keep it opt-in" as a candidate.

### 2.2 What "off" means, exactly

With `ShowNoteFontControls = false` (the default):

| Surface | Behaviour |
|---|---|
| `M`/`A` glyph | Not created. `monoUseful` becomes `window._noteFontControls && contentLines.length > 1`. The width glyph takes the slot the mono glyph would have had; `nextSlot` already handles an absent mono glyph (`:424-431`), so no arithmetic changes. |
| `Aa / Mono` selects | Not emitted at either level: the gate becomes `hasDiagramNotes && toggles.ShowNoteFontControls`. `BuildScenarioDiagramToolbar` receives an empty string, as it does today for a report without notes. |
| `window._setNoteFont` / `_setScenarioNoteFont` | Still defined (nothing calls them; `NoteAppearanceScriptTests` pins their presence and there is no reason to fork the script by flag). |
| `ReportToggleDefaults.NoteFont = Monospace` | Honoured: `window._noteFontDefault = 'mono'` seeds, the zero-click paint applies `setAllNoteFonts`, every note draws in Courier New, and nothing in the UI switches it back. Documented in the option's summary and the wiki row. |
| `.kronNoteMono { FontName "Courier New" }` in the injected style block | Still declared whenever a note carries *any* non-default appearance (i.e. when the width control is used). An unused class declaration is invisible in the drawn SVG; splitting `noteAppearanceStyle` by flag would fork the one source-rebuild chain for no visible gain. Left alone, deliberately. |
| Script seed | New token `__NOTE_FONT_CONTROLS__` → `window._noteFontControls = true|false`, substituted beside `__NOTE_FONT_DEFAULT__` in `DiagramContextMenu.GetCollapsibleNotesScript` (`DiagramContextMenu.cs:110-114`). |

With `ShowNoteFontControls = true`: exactly today's 3.0.85 behaviour, plus the styling and grouping fixes
of §2.3 applied to the font select as well.

### 2.3 The width dropdown

**Labels.** Options become `Wrap` (value `default`, unchanged) and `Wide` (value `full`, unchanged).
"Wrap" names the default state by what it does (notes wrap at the report's `DiagramNoteWrapWidth`); "Wide"
names the other by what it does. Both are one syllable, so the closed control stays as narrow as today's
and the toolbar's existing width problems (export labels wrap under 1200px, sideways scroll at 769-1000px,
both parked in `TOOLBAR_REDESIGN_PLAN.md`) get no worse. The `option` values and the `NoteWidthMode`
enum members stay: values are what the E2E facts and the handlers key on, and the enum is public API.

**Context inside the dropdown.** The options are wrapped in `<optgroup label="Note width">`. An optgroup
label renders as a heading when the dropdown is open and costs nothing in the closed state, so a reader
who opens it sees *Note width: Wrap / Wide* without the toolbar growing. Screen readers announce it too.
The font select, when shown, gets `<optgroup label="Note font">` for the same reason. (Optional, same
cost: `<optgroup label="Note format">` on the JSON/YAML select, built inline at `ReportGenerator.cs:946-951`;
the existing markup pin asserts the `<option>` run as a substring and survives the wrapper.)

**The title says both states.** `title` becomes `Note width. Wrap: notes wrap at the report's note width.
Wide: notes widen to fill the diagram.` `aria-label` stays `Note width` (the optgroup carries the detail
for assistive tech).

**Finding 2 is documented, not engineered away.** Disabling the select when nothing in scope can widen was
considered and rejected: containers render lazily, so a scenario's "nothing wraps here" is unknown until
every diagram in it has drawn, and a control that enables itself as you scroll is worse than one that
sometimes does nothing. The wiki's *What "full width" means* section gains one sentence: *Wide changes only
notes that wrap; a note that already fits stays as it is, so on a scenario of short payloads the control
appears to do nothing.* With the pending state fixed (next), a reader at least sees the re-render happen.

**Styling (the bug).** `collapsible-notes-styles.css` extends the two selector lists:

```css
.note-format-select,
.note-font-select,
.note-width-select { …the existing rule… }

.details-radio-btn.details-pending,
.truncate-lines-select.details-pending,
.note-format-select.details-pending,
.note-font-select.details-pending,
.note-width-select.details-pending { …the existing rule… }
```

Three lines of CSS, no markup change, no script change. A shared `toolbar-select` class was considered
and deferred: `TOOLBAR_REDESIGN_PLAN.md` §2.2 restyles the whole options bar (selects pinned to the
buttons' box metrics, the pending rail redrawn), and introducing a class grammar now that B will replace
is churn. The rename `Fit/Full → Wrap/Wide` is added to that plan's markup list so B carries it.

## 3. Tests (red first, per CLAUDE.md)

### 3.1 Unit

- `ReportConfigurationOptionsDefaultsTests.ShowNoteFontControls_defaults_to_false`.
- `ReportToggleDefaultsResolverTests`: `BuiltIn` has `ShowNoteFontControls == false`; `Resolve` copies the
  flat option; neither overlay group can change it (reflection fact: `ReportToggleDefaults` has no such
  property).
- `ToggleDefaultsMarkupTests`, anchored on emitted attributes rather than bare substrings
  (`report-html-substring-assertions`):
  - `Note_font_select_is_absent_by_default`: a BrowserJs report *with* notes contains no
    `class="note-font-select"` and no `onchange="window._setNoteFont(this)"`, and still contains
    `class="note-width-select"`.
  - `Note_font_select_is_emitted_when_the_controls_are_shown`: the existing
    `Note_appearance_selects_are_emitted_at_report_and_scenario_level` and
    `Note_font_default_seeds_selects_and_script` move under `ShowNoteFontControls = true`.
  - `A_configured_monospace_default_is_honoured_with_the_controls_hidden`: `NoteFont = Monospace`, flag
    off → `window._noteFontDefault = 'mono'` present, no font select.
  - `Note_width_select_names_its_states`: `<optgroup label="Note width"><option value="default" selected>Wrap</option><option value="full">Wide</option></optgroup>`,
    and the `Note_width_default_seeds_selects_and_script` pin updated to the new text.
  - `ToggleDefaultsBaselineTests.Unset_config_produces_byte_identical_output_across_null_builtin_and_resolved`
    must pass untouched.
- `NoteAppearanceScriptTests`:
  - `The_monospace_glyph_is_gated_on_the_seeded_controls_flag`: no `__NOTE_FONT_CONTROLS__` token
    survives; `window._noteFontControls = false` under `BuiltIn`, `true` under
    `BuiltIn with { ShowNoteFontControls = true }`; the `monoUseful` expression reads
    `window._noteFontControls`.
  - `Every_toolbar_select_shares_one_style_and_one_pending_rule`: the CSS resource
    (`DiagramContextMenu.GetCollapsibleNotesStyles()`) contains the `.note-font-select` and
    `.note-width-select` selectors in the same rule as `.note-format-select`, and both
    `.details-pending` compounds. This is a stylesheet asserted as a stylesheet, not report HTML
    scanned for a substring, so the bare-substring hazard does not apply.

### 3.2 Playwright (`NoteAppearanceTests`, rules per CLAUDE.md: `PollingInterval = 200`, `.First`/`.Nth`,
`dispatchEvent` for SVG hover)

- `The_monospace_glyph_is_absent_by_default`: on the wide-SQL fixture generated with defaults,
  `ClickNoteButton("mono")` returns `NOT_VISIBLE` and `ClickNoteButton("width")` returns `CLICKED`.
- `The_font_select_is_absent_by_default`: `Page.Locator(".note-font-select").CountAsync() == 0` and
  `.note-width-select` count `> 0`.
- The three existing monospace facts (`Monospace_paints_the_note_text_in_courier_new`,
  `Monospace_lines_up_text_that_the_proportional_font_scatters`,
  `The_report_level_font_select_switches_every_note`) navigate to a fixture generated with the flag on.
  `ReportTestHelper.GenerateReportWithWideSqlNote` gains a `showNoteFontControls` argument that passes
  `toggleDefaults: ResolvedToggleDefaults.BuiltIn with { ShowNoteFontControls = true }` to
  `GenerateHtmlReport` (`ReportTestHelper.cs:3550-3555`).
- `The_width_select_is_drawn_like_the_format_select`: in one toolbar that has both, the computed
  `height`, `borderRadius`, `fontSize`, `borderTopColor` and `marginLeft` of `.note-width-select` equal
  those of `.note-format-select`. Needs a fixture whose payloads trip both gates (`hasJsonNotePayloads`
  wants a `\n{` or `\n[`; the wide-SQL fixture's one-line JSON response may or may not: check at
  implementation, and extend the fixture rather than the assertion if it does not).
- Not written: a computed-style fact for the pending throb. On a one-diagram fixture the re-render
  finishes inside the 0.2s animation delay, so the assertion would race the thing it measures. The
  structural CSS pin above is the guard.

## 4. Documentation

- `../Kronikol.wiki/Report-Configuration.md`: a `ShowNoteFontControls` row in the flat options table
  (beside `DiagramNoteWrapWidth`, `:132`); the `NoteFont` row (`:233`) gains "the controls are hidden
  unless `ShowNoteFontControls` is set; a configured `Monospace` still applies"; the `NoteWidth` row
  (`:234`) names the new labels.
- `../Kronikol.wiki/PlantUML-Browser-Rendering.md` §*Note Appearance* (`:153-200`): the glyph table and
  the dropdown sentence split into the width control (default) and the monospace control (opt-in, with
  the flag named); the measured §2.9 rationale stays as the reason to opt in; *What "full width" means*
  gains the "Wide changes only notes that wrap" sentence.
- `CHANGELOG.md`: see §5. No em-dashes or list-of-three padding in the changelog or wiki text
  (`no-llm-tells-in-public-text`).
- `plans/NOTE_WRAP_AND_WIDTH_PLAN.md`: a two-line postscript under the status block pointing here
  ("the monospace control was withdrawn from the default UI in 3.18.0; see NOTE_APPEARANCE_CONTROLS_PLAN.md").
- `plans/TOOLBAR_REDESIGN_PLAN.md` §2.2: add the `Wrap/Wide` labels and the optgroup headings to the
  markup list so Option B carries them.
- `plans/PLANS_STATUS.md`: row added at creation; updated at execution.

## 5. Versioning

**Minor: 3.18.0.** `ShowNoteFontControls` is new public surface, and a new option is a minor bump
whatever its default. The label change and the CSS fix are patch-class on their own; the highest change
decides.

One judgement to make explicit. The new option's default *hides* a control that 3.0.85 showed, so a
consumer who generates a report today without touching their configuration gets different output. Under
CLAUDE.md's strict reading ("changing a default so that existing code behaves differently without being
touched") that is major-shaped. It is recommended as **minor** because: the API types and options that
consumers can *name* (`NoteFontFamily`, `ReportToggleDefaults.NoteFont`) all still exist and still work;
what disappears is a control nobody configured; the same CLAUDE.md rule says a change to generated report
output is not on its own a major; and the control is four days old. The changelog states plainly that the
default-visible control was withdrawn and how to get it back. A major is never bumped without asking, and
none is proposed; if the user reads the rule the other way, the same work ships as 4.0.0-candidate content
under `V4_PLAN.md` instead.

Changelog shape (draft):

> **Minor: the monospace note control is opt-in, and the note-width dropdown says what it does.**
> `ShowNoteFontControls` (default `false`) hides the `M`/`A` glyph and the note-font dropdowns; a
> configured `NoteFont = Monospace` still applies. The note-width dropdown reads `Wrap` / `Wide` under a
> `Note width` heading, and is now styled like the JSON/YAML dropdown; it had shipped without any CSS
> rule, so it drew as a bare browser select and gave no pending feedback during a bulk re-render.

## 6. Kronikol4J

Add to `../Kronikol4J/docs/REMAINING_PARITY.md`: the port has neither note-appearance select nor the
hover glyphs (3.0.85 Part B never ported). After 3.18.0 a default .NET report emits only the width select
(`Wrap`/`Wide`, `<optgroup label="Note width">`) and the width glyph; the font control is behind
`ShowNoteFontControls`. Byte parity for a report that draws notes stays broken until the width control is
ported; the font control need not be ported for default parity.

## 7. Milestones

| | Scope | Bump |
|---|---|---|
| M1 | The flag: option, resolved-record property, resolver, markup gate, script token and `monoUseful` gate, unit and E2E facts, wiki rows | part of 3.18.0 |
| M2 | The width dropdown: `Wrap`/`Wide`, optgroups, title, the CSS rule and pending rule, markup and computed-style facts, wiki sentence, TOOLBAR_REDESIGN note | part of 3.18.0 |
| M3 | Release: changelog, version in every package, tag `v3.18.0`, PLANS_STATUS, NOTE_WRAP_AND_WIDTH postscript, Kronikol4J ledger line | 3.18.0 |

M1 and M2 are independent and can land as separate commits; M3 needs both.

## 8. Open questions (decide before go)

| # | Question | Recommendation |
|---|---|---|
| Q1 | Option name | `ShowNoteFontControls`. Follows `ShowNoInteractionsMarker` / `InternalFlowShowFlameChart`. Alternatives: `NoteFontControls`, `EnableNoteMonospace`. |
| Q2 | With the flag off and `NoteFont = Monospace` configured: honour or ignore? | **Honour.** A consumer who names `Monospace` asked for it; silently ignoring a configured value is the surprising branch. "Every note monospace, no toggle" is a coherent configuration. |
| Q3 | Width labels | **`Wrap` / `Wide`.** Alternatives measured for toolbar cost: `Wrapped` / `Full width` (widest option ~+45px in the closed state), `Narrow` / `Wide` (`Narrow` misdescribes an 800px default). |
| Q4 | Optgroup heading on the JSON/YAML select too? | Yes, for consistency; zero closed-state cost; the existing pin survives. Skip if the toolbar redesign is imminent. |
| Q5 | Bump | 3.18.0 minor, per §5. The user decides whether the strict reading applies. |
| Q6 | Should v4 remove the monospace control outright? | Add a candidate line to `V4_PLAN.md`; decide there, not here. |

## Appendix: the exact edit sites

- `src/Kronikol/ReportConfigurationOptions.cs` after `:446` (`DiagramNoteWrapWidth`): the option.
- `src/Kronikol/Reports/ReportToggleDefaultsResolver.cs:21-22`: `public bool ShowNoteFontControls { get; init; }`
  beside `NoteFont`/`NoteWidth`; `:54` (`Resolve`): `with { NotePayloadFormat = …, ShowNoteFontControls = options.ShowNoteFontControls }`;
  `Overlay` copies `baseline.ShowNoteFontControls`.
- `src/Kronikol/Reports/ReportGenerator.cs:957-966`: gate the two font selects on `toggles.ShowNoteFontControls`;
  `:958` the width options `("default", "Wrap"), ("full", "Wide")`; `:731-736` `BuildNoteAppearanceSelect`
  gains an optgroup label parameter and the longer `title`.
- `src/Kronikol/Reports/DiagramContextMenu.cs:113`: `.Replace("__NOTE_FONT_CONTROLS__", toggleDefaults.ShowNoteFontControls ? "true" : "false")`.
- `src/Kronikol/Reports/collapsible-notes-script.js:2081`: `window._noteFontControls = __NOTE_FONT_CONTROLS__;`;
  `:909` `monoUseful: window._noteFontControls && (…) > 1`.
- `src/Kronikol/Reports/collapsible-notes-styles.css:23` and `:43-45`: the selector lists.
- `tests/Kronikol.Tests.EndToEnd/ReportTestHelper.cs:3530`: `GenerateReportWithWideSqlNote(…, bool showNoteFontControls = false)`.

## Execution record (2026-09-18, 3.22.0)

M1, M2 and M3 shipped as written, with one correction.

**The optgroup is not free in the closed state.** §2.3 said an `<optgroup>` label "costs nothing in the
closed state". Measured on the wide-SQL fixture: Chromium indents grouped options and sizes the closed
select from them, so each grouped select drew **15px wider** (67.2 to 82.2, 65.2 to 80.2, 63.2 to 78.2).
Firefox: 0. Per toolbar in Chromium, select widths including margins:

| Markup | Width |
|---|---|
| 3.0.85 (format styled, font and width bare) | 174.0px |
| 3.22.0, heading on the width select only | 158.9px |
| 3.22.0, heading on width and JSON/YAML | 173.9px |

The width select keeps its heading: it is the fix for Finding 1, and the default toolbar still ends
15px narrower than 3.0.85 because the font select is gone. **Q4 was dropped.** The optional JSON/YAML
heading was recommended on "zero closed-state cost", which is false, its options already name
themselves (Finding 1 says so), and it would have spent the whole saving on a toolbar with two parked
overflow bugs. It is a one-line change if wanted.

Open questions as resolved: Q1 `ShowNoteFontControls`. Q2 honour. Q3 `Wrap` / `Wide`. Q4 dropped, above.
Q5 minor, 3.22.0; 3.21.0 had since set the precedent (`ShowHistorySection`, a default-off option hiding
what a report used to show, shipped as a minor). Q6 candidate line added to `V4_PLAN.md`.

Also changed, not in the plan: the font select's `aria-label` went from `Note payload font` to `Note font`
so it matches its optgroup heading, and it gained a two-state `title` like the width select's. The
`ScriptTokens` leak guard in `ToggleDefaultsMarkupTests` had never listed `__NOTE_FONT_DEFAULT__` or
`__NOTE_WIDTH_DEFAULT__`; both are in it now with `__NOTE_FONT_CONTROLS__`.

The computed-style E2E fact compares all three selects inside ONE toolbar and was run red against the
3.0.85 stylesheet before the CSS change was trusted.

## Audit (2026-09-19, 3.22.1)

The user asked for the execution to be checked against this plan. Every item in §2 to §7 and the appendix
was present and correct. What the plan itself had not listed, and the execution therefore missed:

| Missed | Why it mattered | Fixed in 3.22.1 |
|---|---|---|
| The `NoteFontFamily` summary said "readers can switch any note, or all of them, in the report" whatever the default | False by default since 3.22.0, and it ships in the package's XML docs | Summary rewritten; `ReportToggleDefaults.NoteFont` says a configured value applies with the controls hidden; `NoteWidthMode` names the `Wrap` / `Wide` labels |
| No Playwright fact for any *configured* note start state | Q2 ("honour") was pinned on markup and the script seed only, never on what is painted | Three facts: configured `Monospace` with the controls hidden (first paint, no glyph, survives a width change), configured `Monospace` with them shown (the select reads it, the glyph takes it back), configured `Full` (starts wide, the select reads it, `Wrap` returns to the starting width). All passed first time: no product bug |
| `Note_appearance_selects_are_absent_outside_browser_rendering` ran with the flag off | It no longer proved the font select is gated on the rendering mode | It now asks for the controls; a new fact does the same for a report without notes |
| Wiki `Generated-Reports.md` feature list and `_Sidebar.md` never mentioned the controls (a 3.0.85 gap) | The §4 list named two wiki pages and these were not among them | Bullet and sidebar entry added |
| Changelog 3.22.0 said the option restores "exactly what 3.0.85 showed" | It restores the controls, restyled and relabelled | Reworded |

**One consequence the plan did not state.** §0 says the control stays "available to anyone who opts in".
That is true in code only. `kronikol merge` renders with a fixed options object and `kronikol ingest` has a
curated flag set (`--note-format`, `--diagnostics-section`), so a CLI-written report lost the monospace
control in 3.22.0 with no way to ask for it back. That is where every toggle default already stood with
the CLI. No flag was added: it would be new public surface for a feature the user doubts, which is the
lesson of §0. It is stated in the 3.22.1 changelog and the wiki option row, and left as a question for the
user (`--note-font-controls` on `ingest` would be a minor bump).

Checked and found sound, so not changed: nothing else in the shipped CSS or scripts treats the JSON/YAML
select specially, so there was no further sibling rule to extend; `DiagramContextMenu` is the only loader
of the script, so the new bare token cannot break another consumer; no persisted state (hash, storage)
carries the note font, so a hidden control cannot be driven from a link; the parameterised-group renderer
takes the pre-built toolbar string, so there is one emission site, not two; CI for the release SHA ran the
Playwright project from a clean checkout, which stands in for the clean-worktree check.

Found on the way and fixed: two `cref`s to `LightBDD.Core.Extensibility.Execution.IScenarioDecorator`
never resolved, because inside `namespace Kronikol.LightBDD` the name binds to `Kronikol.LightBDD.…`. Both
now use `global::`.
