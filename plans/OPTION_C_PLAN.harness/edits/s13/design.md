# S13: the Specifications and labs pages on the tokens (Q3), prepared and not applied

> Applied on 2026-10-20 over TAGS_PLAN T2 and the rest of S12 (`4e75ce61`), after this was written at `db663d88` and
> `d385c715`. T2 made a category a tag (its switch is `.tag-mode-toggle`, its More chip `.tag-more`) and added a fact
> that the violet grounds its two mode switches alike, and D49 titled the history's links into the report
> (`HistoryHtml`, on the labs page too). So the scripts here are the ported ones, `scripts-s13-t2.py` and
> `tests-s13-t2.py`: the violet's selector lists name T2's controls, `.tag-more:hover` joins the hover edges, T2's
> switches fact holds the two switches' grounds and hover edges equal, and the labs fact pins D49's title. The harness's
> `checks/css-facts-check.py` is the ported check. Line numbers below are at the older bases. The plan's log has the
> measured red, green and mutations.

Prepared on 2026-10-19 at the brief's base, `db663d88` (S12 phone layout, part 1), and dry-run again on `d385c715`, where
branch v5 stands now (`c010bcb6`, the merge of 4.32.0, then `c7abf98f` D43 and `d385c715` S12a). Every file S13 edits
has the same bytes at `c010bcb6` and `d385c715`; three of them moved between `db663d88` and `c010bcb6` (`ReportGenerator.cs`,
`ReportConfigurationOptions.cs`, `ToolbarStylingTests.cs`), and S13's edit to each is the same on both bases, hunk for
hunk (section 10). Line numbers are at `db663d88`, with `d385c715`'s in brackets where they differ. Nothing in any
worktree was changed, and nothing was built or run with .NET or Playwright.

The owner's answer to Q3 (plans/OPTION_C_PLAN.md section 7): "the token sheet, the theme and the violet theme as token
overrides, no layout redesign".

| File | md5 at `db663d88` | md5 at `d385c715` |
|---|---|---|
| src/Kronikol/Reports/LabsReportGenerator.cs | 7a2b84871c2ee590927f17a51678d0d4 | same |
| src/Kronikol/Reports/ReportGenerator.cs (UTF-8 BOM, kept) | d32cfefa40f50f61afe675d379ca36ed | 61b130ec82099f3d80dd403a78365c01 |
| src/Kronikol/Constants/Stylesheets.cs | 14fd5c549901ef12904266f441ca5a5b | same |
| src/Kronikol/ReportConfigurationOptions.cs (UTF-8 BOM, kept) | 9d47bd04f97561262171c4ccf832a392 | b4acb5e46e836312c3d5d2b91bd82de4 |
| src/Kronikol/Reports/labs-styles.css | 72985de8ce4f96b6340886ed34beec68 | same |
| src/Kronikol/Reports/history-styles.css | 0a9321cfb07645972349a21b26784194 | same |
| src/Kronikol/Reports/report-diagnostics-styles.css | ef7e57d247750d2e826b093333d8fee2 | same |
| src/Kronikol/Reports/stylesheets.css | 6f462536dda3c28ed3376a5b9ac5e4a9 | same |
| tests/Kronikol.Tests/Reports/LabsReportTests.cs | e41381ec80693aa00a3e5f560c3f664c | same |
| tests/Kronikol.Tests/History/HistoryRingsMarkupTests.cs | ead4058b3efa682f7a23ff4a70b853b0 | same |
| tests/Kronikol.Tests/Reports/StylesheetRulesTests.cs | 5b579ff5b9c85affbe6db95715ef71fa | same |
| tests/Kronikol.Tests.EndToEnd/LabsPageTests.cs | 57c8ce54c1b97ecaa5d9919020540721 | same |
| tests/Kronikol.Tests.EndToEnd/HistoryRingsTests.cs | 96ce0200b8d74899d0b8e5539d84f407 | same |
| tests/Kronikol.Tests.EndToEnd/DiagnosticsCardTests.cs | 6ba73c2ba378b34f68b7eb56fd24e46a | same |
| tests/Kronikol.Tests.EndToEnd/ToolbarStylingTests.cs | df80c92b9f19060a25d14bbb5085cfb4 | 749467234de716e01e16401395261d74 |
| src/Kronikol/Reports/report-tokens.css, report-theme-boot-script.js, report-theme-switch-script.js (read only: S1 must be in) | b566ea7c..., 46149bd9..., 37515e7e... | same |

Apply with `python scripts-s13.py <root>` and then `python tests-s13.py <root>` (or the tests first, for the red step:
they compile against today's code, so no `--stage` split is needed). Each checks every anchor (exactly once) in memory
and writes nothing unless all hold, keeps BOMs and line endings, and refuses a second run; `scripts-s13.py` also refuses
a tree without S1's token sheet and theme scripts. Copy `checks/mutate-s13.py`, `checks/css-facts-check.py` and
`checks/violet-palette.py` into `plans/OPTION_C_PLAN.harness/checks/`.

Files here: `design.md`, `scripts-s13.py`, `tests-s13.py`, `ThemedPagesHtmlTests.cs` (core, 9 facts, 10 cases),
`ThemedPagesTests.cs` (Playwright, 6 facts, 12 cases), `checks/mutate-s13.py` (35 mutations, 23 core and 12 Playwright,
`--check` mode), `checks/css-facts-check.py` (the stylesheet facts in Python, run before and after without a build),
`checks/violet-palette.py` (the dark values and every contrast quoted here), `dry-run.sh` (its output in
`dry-<base>.log`), `mutate-css-sim.log` (section 6), and per base
`dry-<base>-base/`, `dry-<base>-ref/`, `dry-<base>/`, `dry-<base>-crlf/` with their `.md5` and `.diff` files.

## 1. What the preview does

Nothing for these pages. The preview (`plans/OPTION_C_PLAN.harness/mock/`) themes the run report only: it has no labs
page and no Specifications report, and no violet. What it says about them is a contract: `c-tokens.css` 1-3, "The
--kron-* custom properties are the theming contract Option C introduces: a theme (the violet Specifications default
included) overrides tokens, never selectors." The stylesheet inventory says the same of the violet (inventory/stylesheet.md
481-485: "as token overrides ... it is about a tenth of the size") and of the labs page (556-557: "it needs the token
sheet and the boot script too"). The preview's search help colours, which S13 uses (section 3.4), are `c-extra.css`
44-47 and `c-round5.css` 166-169.

## 2. What the product does today

**The Specifications report** is written by `GenerateHtmlReportCore` with `UserStylesheets(HtmlSpecificationsCustomStyleSheet, ...)`
(ReportGenerator.cs 530 [531]), so it shares the run report's head since S1: the boot script before any style (1619
[1633]), the token sheet opening the style block (1495 [1509]), the theme after the component sheets in the same block
(1501, 1625 [1515, 1639]), the switch script (1667 [1681]) and the title line with the switch (1678 [1692]). The token
sheet, the boot script and the theme switch are there; what is not is the violet. `Stylesheets.VioletThemeStyleSheet`
(Constants/Stylesheets.cs 53-141 [same]) is 4.x's literal colours, the same in both themes:

- in the dark theme a Specifications report draws its features and filter box on 4.x's light violet card (`#DDD6FE`)
  under the dark theme's light ink, 1.14:1, and its chosen controls in the light theme's solid violet;
- in the light theme, over the C base S1 to S11 built: the passed step's tick is the pass green on solid violet (1.50:1),
  the back-to-top arrow the soft ink on solid violet (1.84:1), a hovered chosen tab takes the accent on solid violet
  (the base's `.diagram-toggle-active:hover`, (0,2,0), beats the theme's `.diagram-toggle-active { color }`, (0,1,0):
  1.11:1), a label's ink is `--kron-muted` on `#C4B5FD` (3.23:1), an attachment's name `#A78BFA` on the violet card
  (1.96:1), the search field's edge `#C4B5FD` on the violet card (1.33:1), and the sub-steps rail is the card's own
  colour (1.00:1: since 5.0.0 a scenario has no background and sits on its feature's card, where 4.x drew it white);
- its search help is painted dark (`#1E1534`) under the light theme's dark ink, 1.22:1 (the dark theme's light ink
  happens to read on it). This came in with the search help itself (2d85b713, 2026-04-17) and is live on main too
  (section 8);
- five of its rules and one selector of a sixth style markup 5.0.0 no longer writes (`.test-execution-summary`,
  `.ci-metadata`, `.timeline-toggle-active` and its hover, `.rule`'s left border, which S1 took away, and
  `.timeline-toggle:hover`), and one, the `.duration-fast` tint, would draw a box with no padding round 5.0.0's text
  durations.

**The labs page** (LabsReportGenerator.cs, `Build` 33-73) carries no script (remarks 15-19). Its styles (43-48) are
stylesheets.css, the history, diagnostics and labs sheets, without the token sheet, so every `var(--kron-*)` of the base
sheet resolves to nothing: the page is white, its text black, and its type the browser's default serif outside the
History card, which sets its own font for this page (history-styles.css 62-64, 73). The history and diagnostics sheets
give every token a light fallback for this page (72 in history-styles.css, 12 in report-diagnostics-styles.css; their
comments at 12-14 and 1-4 say so). The labs sheet (labs-styles.css, 47 lines) is 4.x's literal greys and yellows. The
header (56-60) is an `h1` holding the title and the Labs badge (0.45em of the browser's 28 px heading), the about line
and the meta line.

**The search help** (stylesheets.css 334-397) is 4.x's literal greys in every report. In the dark theme its panel,
`rgb(250, 250, 250)`, sits under the dark theme's light ink: 1.17:1, unreadable in every dark report, the run report
included.

## 3. The change

### 3.1 The labs page (LabsReportGenerator.cs)

- The boot script (`report-theme-boot-script.js`) is the head's first script, before the style block, as in the report;
  the token sheet opens the style block, then the four sheets as before; the switch script
  (`report-theme-switch-script.js`) ends the head, after the reader's `CustomCss` and the favicon, as in the report. Both
  are the report's own files, read with `ReportGenerator.LoadResource` (made `internal`; it caches).
- The header's first line is the report's title line: `<div class="title-line"><h1>{title} <span class="labs-badge">Labs</span></h1>{ThemeSwitchButton}</div>`,
  the switch the run report draws (`ReportGenerator.ThemeSwitchButton`, made `internal`). The about and meta lines
  follow as before. The base sheet's `.title-line` and `.theme-toggle` rules draw it; the title is 22 px
  (`--kron-fs-title`), the run report's, where it was the browser's 28 px.
- The remarks say it runs only the theme's scripts.
- labs-styles.css on the tokens:

| Rule | 4.x value | S13 |
|---|---|---|
| `.labs-badge` background, colour, border | `rgb(255, 243, 205)`, `rgb(122, 92, 0)`, `rgb(232, 200, 110)` | `--kron-warn-soft`, `--kron-warn`, `--kron-warn-edge` (5.37:1 light, 7.81:1 dark; 4.x 5.64:1) |
| `.labs-badge` font-size | `0.45em` (12.6 px of the old 28 px heading, 9.9 px of the 22 px one) | `var(--kron-fs-label)`, 11 px |
| `.labs-about`, `.labs-meta` colour | `rgb(90, 90, 90)` | `--kron-muted` (5.13:1 on the light ground, 7.92:1 on the dark) |
| `.labs-scenario-table` cell rules | `rgb(225, 225, 225)` | `--kron-hairline-soft` |
| `.labs-feature th` background | `rgb(245, 245, 245)` | `--kron-strip` |
| `.labs-stable` colour | `rgb(120, 120, 120)` | `--kron-faint` |
| `.labs-scenarios` | a margin | a card as the History section and the diagnostics are: `--kron-card`, a `--kron-hairline` edge, 12 px corners, the card shadow, its summary padded 12 px 16 px over a `--kron-hairline-soft` rule, the table's wrap padded 0 8 px 8 px |

  The card is the one change of shape (Q5): on the page's ground (`#ebeef4`) the table's row rules would be 1.07:1, where
  4.x drew them on white; on a card they are 1.24:1, as the report's own tables are.

### 3.2 The sheets the labs page shares with the report

Every page that carries history-styles.css and report-diagnostics-styles.css now carries the token sheet (the run
report, RG 1495-1499 [1509-1513], and the labs page), so their fallbacks go: 72 in history-styles.css (71 after the
History card's own `font-family` line, which goes with its comment), 12 in report-diagnostics-styles.css, each read to
its own closing parenthesis (a fallback holding `rgba()` or a whole shadow list goes whole), and the comments at
history-styles.css 12-14, 62-64 and 115-116 and report-diagnostics-styles.css 1-4 say so. Kept, because a page without
the token sheet carries them: the fallbacks in inline-svg-styles.css and internal-flow-popup-styles.css (the diagram
pages), and the `--kron-nav-h` fallbacks (a value the scripts set, not a token). The sparkline's own two literals
(history-styles.css 8 and 10) stay under the colours `HistoryHtml` writes inline (section 8).

### 3.3 The violet theme (Constants/Stylesheets.cs)

The theme sets the report's tokens again, light on `:root` and dark on `:root[data-theme="dark"]` and again under
`@media (prefers-color-scheme: dark) { :root:not([data-theme]) }`, the two blocks the token sheet uses. It comes after
the token sheet in the same style block, so its `:root` wins over the token sheet's by order, and its dark blocks
(specificity (0,2,0), as the token sheet's) win in the dark theme. Each dark value is the token sheet's own dark value
turned in OKLCH from its hue to the theme's (292.7, the hue of `#8b5cf6`) at the same lightness and chroma, so the
contrasts the token sheet was audited at hold (`checks/violet-palette.py`).

| Token | Light | Dark | What reads it |
|---|---|---|---|
| `--kron-accent` | `#6d28d9` | `#a998eb` | links, focus rings, the picked tab's underline, a search match's edge, accent text (7.10:1 on white, 5.12:1 on the violet card; 4.x's `#8b5cf6` text 3.05:1 there) |
| `--kron-accent-rail` | `rgba(109, 40, 217, .18)` | `rgba(169, 152, 235, .30)` | the notes' rail |
| `--kron-hover` | `#ede9fe` | `#2b2837` | every hover tint, 4.x's idle hovers among them |
| `--kron-selected`, `-ink`, `-edge` | `#c4b5fd`, `#5b21b6`, `#8b5cf6` | `#342b51`, `#cac0f6`, `#8774c8` | a selected row, More filters open, the open gear, labels (4.87:1; 7.69:1 dark) |
| `--kron-field`, `--kron-strip` | `#f5f3ff`, `#f5f3ff` | `#272431`, `#22202b` | the mode toggles and fields, table headers, the tools strip, the search help |
| `--kron-pass`, `--kron-pass-soft` | `#5b21b6`, `#ede9fe` | `#b7a3ff`, `#2a253e` | a passed parameter cell, the fast duration, pass badges (Q4) |
| `--kron-bypass`, `--kron-bypass-soft` | `#5b21b6`, `#ddd6fe` | `#b3a4f0`, `#2c2543` | a bypassed step, the 4.x look |

| Theme variable | Light | Dark | For |
|---|---|---|---|
| `--violet-card` | `#ddd6fe` | `#1d1b25` | the feature, summary-table and filter cards, the phone search line (ink on it 10.26:1 and 13.93:1) |
| `--violet-rail` | `#a78bfa` | `#3e3b4c` | the sub-steps rail and an attachment image's edge (1.96:1 and 1.57:1, the default dark hairline's) |
| `--violet-edge` | `#a78bfa` | `#a78bfa` | a hovered control's edge (5.28:1 on the dark hover) |
| `--violet-solid`, `--violet-solid-hover` | `#8b5cf6`, `#7c3aed` | `#7c3aed`, `#6d28d9` | a chosen control and its hover, the passed step, back to top (white on them 4.23:1 and 5.70:1 light, 5.70:1 and 7.10:1 dark) |

The rules after the token blocks, every colour a variable:

- kept from 4.x, on variables: the cards (`.feature`, `.features-table-view`, `.filtering-box`, the phone
  `.filter-search`); the hover edges, stated before the chosen states (13 selectors, `.tag-mode-toggle:hover` now with
  its siblings); `.iflow-rel-list li:hover`'s edge; the seven chosen states, the popup's and the diagram's chosen tabs
  and their hovers, `.step-status.passed`, the search match's 4 px edge, back to top and its hover. Each solid violet
  now sets `color: var(--kron-on-solid)` (the tick, the arrow, the hovered tab), and the chosen radio's dot is white
  (the base's accent dot would be 1.68:1 on it);
- changed: `.attachment-image-name` (`#A78BFA`, 1.96:1) and `span.label`'s ink take `--kron-selected-ink`, the label
  its edge in `--kron-selected`; `#searchbar`'s edge `--kron-selected-edge` (3.05:1; 4.x 1.33:1); `.sub-steps` and
  `.attachment-image` `--violet-rail`;
- left to the tokens, the same light colour as before: every idle hover's background (`--kron-hover`), the mode
  toggles' and the summary table's headers (`--kron-field`, `--kron-strip`), the bypassed steps and the passed cell
  (`--kron-bypass*`, `--kron-pass-soft`), the fast duration's colour (`--kron-pass`), the focus outline and an
  attachment link (`--kron-accent`, one shade darker than 4.x's `#8b5cf6`, Q3);
- dropped: the rules and the selector for markup 5.0.0 no longer writes; the `.duration-fast` tint; `.example-diagrams`' edge (the
  card's own colour; the base hairline is as invisible on the violet card in light, 1.04:1, and gives the diagram card
  its edge in dark, 1.57:1); and the eight search help rules (section 3.4).

Checked by `checks/css-facts-check.py` and a cascade read of every rule against the base sheets (each competing base
rule is of the same or lower specificity and earlier): in the light theme every colour ToolbarStylingTests holds is
unchanged, and every other 4.x colour is unchanged but those listed as changed or dropped. The theme goes from 49 rules
to 17 after its token blocks; with the three blocks its text grows from 84 lines to 106.

### 3.4 The search help (stylesheets.css 334-397)

| Part | 4.x | S13 |
|---|---|---|
| toggle edge, ink | `rgb(180, 180, 180)`, `rgb(100, 100, 100)` | `--kron-control-border`, `--kron-muted` (5.97:1 light, 7.20:1 dark) |
| toggle hovered | `rgb(240, 240, 240)`, `rgb(50, 50, 50)` | `--kron-hover`, `--kron-ink` |
| panel edge, ground | `rgb(200, 200, 200)`, `rgb(250, 250, 250)` | `--kron-strip-edge`, `--kron-strip` (ink on it 13.63:1, 13.12:1 dark; violet 12.99:1, 13.14:1) |
| table rules | `rgb(200, 200, 200)`, `rgb(230, 230, 230)` | `--kron-hairline`, `--kron-hairline-soft` |
| code chips | `rgb(230, 230, 230)` | `--kron-code-bg` (12.13:1, 11.07:1 dark) |
| note, keys | `rgb(100, 100, 100)`; `rgb(230, 230, 230)` on `rgb(200, 200, 200)` | `--kron-muted`; `--kron-code-bg` on `--kron-control-border`, ink `--kron-ink` |

The preview's colours, but for the code, which keeps its chip (the preview drew it on the card with a hairline), and
the panel's text, which keeps the page's ink (the preview: `--kron-ink-soft`). The violet's eight help rules go, so the
violet page's help is the base's in the violet's strip.

### 3.5 ReportGenerator.cs, for stacking with S12

S13 changes three lines of ReportGenerator.cs and nothing else in it: 75 [76], `private static string LoadResource` to
`internal`, and 93-94 [94-95], one comment line and `private const string ThemeSwitchButton` to `internal`. It does
not touch the head template (1611-1673 [1625-1687]) or any markup there; the labs page's markup is in
LabsReportGenerator.cs. The S12 packages: D49 edits RG's markup (the search line, 1816 [1830]), D48 the head template,
phone part 2 stylesheets.css 192-193 and 1980-2041; S13's stylesheets.css edit is 334-397 and its anchors are text, so
the stages stack in any order. Re-run `dry-run.sh` on the stacked tree before applying.

## 4. Deviations

1. The violet keeps rules on selectors, where the preview's contract says tokens only: ToolbarStylingTests holds 4.x's
   solid violet for the chosen controls, which no token carries (C's chosen state is a tint), and the violet cards have
   no token of their own (`--kron-card` would recolour every card, the diagrams' among them). Q2.
2. The accent is violet-700 (`#6d28d9`) where 4.x's accent-coloured text was `#8b5cf6` (4.23:1 on white, under AA). Q3.
3. Pass and bypass are violet wherever the theme draws them, where 4.x painted three rules violet. Q4.
4. The labs page's table of every scenario is a card. Q5.
5. The search help: the code chip and the panel's ink as above.

## 5. Tests

**Core, `ThemedPagesHtmlTests` (new, `tests/Kronikol.Tests/Reports/`, no collection: no pipeline).** A labs page built
by `LabsReportGenerator.Build` (no history, one diagnostic), parsed with AngleSharp, and the sheets read with `CssRules`:

| Fact | Holds |
|---|---|
| `The_labs_page_chooses_its_theme_before_its_first_style` | the head's first script or style is `script#kronikol-theme-boot`, its text the embedded boot script |
| `The_labs_page_opens_its_styles_with_the_token_sheet` | two style blocks; the first opens with the token sheet, then the base, history, diagnostics and labs sheets in that order; the reader's sheet is the second |
| `The_labs_page_has_the_reports_theme_switch_on_its_title_line` | `header.labs-header` holds `div.title-line`, then the two lines; the title line holds the `h1` (title and badge) and a button equal to the run report's own `div.title-line > button.theme-toggle`; the head has `script#kronikol-theme-switch`, its text the embedded switch script |
| `The_labs_pages_own_parts_are_drawn_in_the_tokens` | the badge's three colours and size, the about and meta ink, the table's card and edge, its rules, the feature rows, the stable ink, by name; no literal colour anywhere in the sheet |
| `A_sheet_carried_only_beside_the_token_sheet_has_no_colour_of_its_own_to_fall_back_on` (history, diagnostics) | no `var(--x, ...)`; the sheet reads `var(--kron-card)` |
| `The_violet_theme_sets_the_reports_tokens_for_the_light_and_the_dark_theme` | one block each: `:root`, `:root[data-theme="dark"]`, and `:root:not([data-theme])` in `(prefers-color-scheme: dark)`; five light and five dark values by name; the same names in light and dark; the two dark blocks equal; every `--kron-` name a token of the token sheet, the rest `--violet-` |
| `Every_colour_the_violet_theme_draws_outside_its_token_blocks_is_a_variable` | at least ten rules; every value a `var()` and no literal |
| `Every_solid_violet_the_theme_paints_carries_the_ink_made_for_it` | every rule that paints `--violet-solid*` (six) sets `color: var(--kron-on-solid)` (back to top's hover keeps its rule's); the chosen radio's dot is `--kron-on-solid` |
| `The_search_help_is_drawn_in_the_tokens_and_the_violet_leaves_it_to_them` | the eight help rules hold no literal; the panel, its edge, the code chip and the toggle's hover by name; the violet has no `.search-help` rule |

**Playwright, `ThemedPagesTests` (new, `tests/Kronikol.Tests.EndToEnd/`, collection Reports, as DiagnosticsCardTests:
`GenerateReportWithEverySection` writes the labs page with `LabsReportGenerator.Build`, no pipeline).** Dark is set with
`EmulateMediaAsync`, the stored pick by the switch itself. Every colour is read with `getComputedStyle`, and a token
through a probe element, so a page without the token sheet fails on the literal checks too (a probe and a rule with no
token both read nothing):

| Fact | Holds |
|---|---|
| `The_labs_page_follows_the_systems_setting_and_paints_its_parts_in_the_tokens` (light, dark) | `data-theme` is the system's; the ground is the literal (`rgb(235, 238, 244)`, `rgb(14, 19, 26)`) and `--kron-ground`; the History card is the literal card (`rgb(255, 255, 255)`, `rgb(23, 29, 38)`), and it, the diagnostics and the scenario table are `--kron-card`; the title's, the meta line's, the badge's and a feature row's colours are their tokens; the switch's `aria-pressed` |
| `The_switch_on_the_labs_page_flips_its_theme_and_the_pick_holds_over_a_reload` | light; a click: dark, pressed, retitled, the dark ground; after a reload still dark (a fresh context per test, so the pick does not leak) |
| `The_specifications_report_is_violet_in_the_light_and_the_dark_theme` (light, dark) | the ground, the feature and filter cards (`rgb(221, 214, 254)`; `rgb(29, 27, 37)`), the chosen Show (`rgb(139, 92, 246)`; `rgb(124, 58, 237)`) under white |
| `A_hovered_chosen_diagram_tab_keeps_its_white_ink_on_a_darker_violet` (light, dark) | at rest white; hovered `rgb(124, 58, 237)` (`rgb(109, 40, 217)` dark) under white |
| `The_violet_reaches_the_controls_its_4x_rules_never_named` | More filters open `rgb(196, 181, 253)` under `rgb(91, 33, 182)` (a report tab hovered is ToolbarStylingTests' new case) |
| `The_search_help_reads_in_every_theme` (default and violet, light and dark) | the panel on `--kron-strip` under `--kron-ink`, a code chip on `--kron-code-bg`, each 4.5:1 or more |

The repo's E2E rules hold: no forced click, no `WaitForFunctionAsync` (so no polling to set), no SVG interaction, every
locator that can match more than one element taken `.First`, no network mocking, no search bar.

**Pins moved (`tests-s13.py`):**

| Test | Old | New |
|---|---|---|
| `LabsReportTests` class summary | "It has no script;" | "Of the report's scripts it runs only the theme's two (5.0.0, plans/OPTION_C_PLAN.md S13);" |
| `LabsReportTests.The_names_link_into_the_report_by_stable_id_and_the_page_has_no_script` | `Assert.DoesNotContain("<script", page)` | renamed `..._and_the_page_runs_only_the_theme_scripts`: the page's script tags are exactly `<script id="kronikol-theme-boot">` and `<script id="kronikol-theme-switch">` |
| `HistoryRingsMarkupTests` class summary | "each with its light value as a fallback for the labs page" | "with no fallback: since S13 the labs page carries the token sheet too" |
| `HistoryRingsMarkupTests.Every_verdict_kind_keeps_its_own_look_and_every_colour_is_a_token_with_a_fallback` | `Assert.DoesNotMatch(@"var\(--[\w-]+\s*\)", css)` (every token has a fallback) | renamed `..._every_colour_is_a_token`, `Assert.DoesNotMatch(@"var\(--[\w-]+\s*,", css)` (none has); its summary says why |
| `StylesheetRulesTests` T4 header | "the violet theme's hovers come before its active states (S4)" | "... hover edges come before its active states (S4); since 5.0.0 it sets the report's tokens (S13)" |
| `StylesheetRulesTests.IdleHovers` and `.Every_violet_idle_hover_precedes_every_violet_active_state` | 16 idle hovers, among them `.timeline-toggle:hover` and four that only set a tint; `ActiveStates` with the two timeline selectors | `HoverEdges`, the 13 hovers whose edge the theme sets; `ActiveStates` without the timeline's; renamed `Every_violet_hover_edge_precedes_every_violet_active_state` |
| `StylesheetRulesTests.Violet_idle_hovers_use_the_themes_own_tint` | each idle hover's background `#ede9fe` in the theme | replaced by `Violet_hovers_take_the_themes_tint_from_its_hover_token`: `:root`'s `--kron-hover` is `#ede9fe`; each of 17 hovers takes its background from the base sheets' `var(--kron-hover...)` and none from the theme; the edge is `var(--violet-edge)` |
| `StylesheetRulesTests.Violet_active_states_are_violet_and_their_hovers_darker` | `#8b5cf6` and `#7c3aed` on the percentile, timeline and diagram rules | `--violet-solid` `#8b5cf6` and `--violet-solid-hover` `#7c3aed` on `:root`, read by the percentile's edge, the chosen tab and both chosen hovers |
| `StylesheetRulesTests.The_violet_theme_recolours_the_blue_the_base_sheet_paints_outside_the_toolbars` | the theme's `#ede9fe` on the link and copy hovers; `inset 4px 0 0 #8b5cf6`; a summary saying rows keep status tints | the base's `var(--kron-hover)` on both and the theme's `--kron-hover`; `inset 4px 0 0 var(--violet-solid)`; the summary's stale sentence (5.0.0 rows have no status tint) rewritten |
| `LabsPageTests.The_page_runs_no_script` | `script` count 0 | renamed `The_page_runs_only_the_theme_scripts`: the ids are `kronikol-theme-boot` and `kronikol-theme-switch` |
| `HistoryRingsTests` class summary and `The_labs_page_draws_the_rings_and_the_timed_bars_with_no_script` | "runs no script"; `script` count 0; probes `var(--kron-pass-ring, #3f9a62)`, `var(--kron-track, #edf0f5)`, `var(--kron-ink, #242b36)` and a comment "no token sheet until S13" | "runs none of the report's scripts but the theme's"; renamed `..._with_none_of_the_reports_scripts`, the two ids; probes with no fallback, the pass ring also `rgb(63, 154, 98)`, the comment rewritten |
| `DiagnosticsCardTests.On_the_labs_page_the_diagnostics_stay_a_disclosure_in_their_4x_colours` | the card `rgb(255, 251, 243)`; "no token sheet" | renamed `..._in_the_themes_tokens`: the card equals a `--kron-card` probe and `rgb(255, 255, 255)` |
| `ToolbarStylingTests.HeaderHovers` | "The violet theme's tabs are S13's."; no violet report tab | "The violet theme's tint is the hover token it sets (S13)."; a new case `{ true, Tint, null, ".report-tab[aria-selected=\"false\"]" }` |
| `ToolbarStylingTests.TabColours` comment | "(S8; the theme is S13's)" | "(S8), with the tokens it sets (S13)" |

Every other ToolbarStylingTests fact is unchanged and passes on the reasoning in section 3.3; `A_violet_filter_toggle_keeps_its_active_colour_while_hovered`
is unchanged and passes.

**Checked and not affected:** LabsReportTests' other facts (the `h1` substring at 272 is inside the title line; nothing
in the token sheet, the two scripts or the switch matches `<a `, `onclick`, `.html`, `<b>`, `<v2>`, `<now>` or
`id<1>`), `LabsReportRunTests`, `MergedLabsPageTests`, `HistoryOutputsTests` and `DefaultReportCleanTests` (their
`DoesNotContain("history-")` reads the run report, which S13 does not change), `IngestHostDiagnosticsTests`,
`ReportThemeTests` (the token sheet itself), `ReportGeneratorStyleOrderTests` (its `/*THEME*/` marker),
`ToggleDefaultsBaselineTests` (three variants of one run, no pinned bytes), every `StylesheetRulesTests` fact that walks
the violet (its selectors parse, its one width bound is `max-width: 768px`), `ViewportSweepTests`' labs sweeps (the title
line's `h1` has `min-width: 0` and breaks anywhere; the switch is 34 px), and LabsPageTests' other facts.

**Expected red.** With the tests applied to today's code: the core class fails 9 of 9 facts (10 of 10 cases); of the
moved pins, `LabsReportTests`, `HistoryRingsMarkupTests`, three of the four T4 facts, `LabsPageTests`, `HistoryRingsTests`,
`DiagnosticsCardTests` and the new ToolbarStylingTests case fail; `Every_violet_hover_edge_precedes_every_violet_active_state`
passes there (today's theme already orders its hovers so), its red being its old selectors. The Playwright class fails
11 of 12 cases: the Specifications report's light case passes there, holding 4.x's light violet over the change.
`checks/css-facts-check.py` shows the stylesheet half of this without a build: 4 of 15 pass before, 15 of 15 after.

## 6. Mutations (`checks/mutate-s13.py`)

35, each naming the fact that should fail, `--check [root]` to verify the anchors only, and a test-name filter.
Core (23): the boot script left out; the token sheet left out; the token sheet after the base sheet; the switch left
off the title line; the switch script left out (twice: the title line fact and the moved LabsReportTests pin); the title
line not the report's class; the badge on 4.x's yellow; the table no card; a history colour falling back again (twice:
the new theory and the moved HistoryRingsMarkupTests pin); a diagnostics colour falling back again; the violet without
its dark block; without the system's dark block; a dark card the light one; the accent left blue; the hover token not
set; a colour written as a literal again; the passed tick under the pass ink; a hovered chosen tab under the accent; the
chosen radio's dot in the accent; the search help back on its light grey; the violet's dark search help back.
Playwright (12): the token sheet left out (three: the new labs fact, the moved HistoryRingsTests and DiagnosticsCardTests
pins); the table no card; the switch left off; the switch script left out (twice: the switch fact and the moved
LabsPageTests pin); the violet without its dark block; a hovered chosen tab under the accent; the selected token not set
(More filters); the hover token not set (ToolbarStylingTests' header hovers, the violet report tab among them); the
search help back on its light grey.

Not mutated in the browser: the boot script (without it the switch script sets the theme itself, a frame later) and a
fallback left in a shared sheet (the token wins wherever the token sheet is); core facts hold both. The 16 core
mutations of the sheets were run against `checks/css-facts-check.py` on the dry run's result: each fails the fact it
names (`mutate-css-sim.log`). The other 19 need a build.

## 7. Risks and answers

| Risk | Answer |
|---|---|
| A user's `HtmlSpecificationsCustomStyleSheet` or `CustomCss` built on the 4.x violet's rules | The constant's value changes, as every 5.0.0 sheet's did; the changelog says to set the tokens. A copy of 4.x's constant still applies over the C base, in light colours in both themes |
| The theme's `:root` must follow the token sheet's | It does in the one style block (RG 1495-1501 [1509-1515]); a user's `CustomCss` comes after both, so it can set the tokens again |
| A token the violet sets reaches a control nobody looked at | Wanted (Q2's contract); the cascade read and the Playwright facts cover the controls ToolbarStylingTests and S13 name. Reached and not measured: the run band's pass colours (not on the Specifications page), the diff's expected side (failures are blanked there), status badges |
| `ReportGenerator.LoadResource` and `ThemeSwitchButton` become internal | Internal only, no public surface; the labs page reads the same cached text |
| Kronikol4J byte parity | The labs page's head and header, the four sheets and the violet constant change; ledger line in section 11 |
| A labs page opened without its scripts | The token sheet's `prefers-color-scheme` block gives it the system's theme, as the report |

## 8. Not done, and why

- The Specifications page's steps with numbers: the step number comes before the status circle, so a heading's first
  letter and the circles do not share a column there (S12a's log, plans/OPTION_C_PLAN.md 1004, left it to S13). Q3 says
  no layout redesign, so S13 leaves it. Q6.
- Found and not fixed, outside S13's sheets (each a dark-theme defect of the default theme too):
  - `.more-filters-count`: white on `--kron-accent`, 2.44:1 in the dark theme (2.51:1 in the violet's); a token for ink
    on the accent would fix both;
  - literal colours left in stylesheets.css (lines at both bases; S13 moves every line past 397 down 4):
    `#searchbar:disabled` (265), `#duration-threshold` and its unit (628, 635), `.attachment-missing` (1098),
    `.examples-table` and its row tints (1254-1269), `.flatten-toggle` (1869-1882), `.no-interactions` (2204);
    `.mobile-filter-toggle` (1988) and the phone `.filter-search` (2041) are S12 phone part 2's;
  - the history sparkline's run colours, written inline by `HistoryHtml.Colour` (HistoryHtml.cs 360-369: an absent run
    `#e6e6e6`, bright on the dark card), and its two sheet literals under them.
- On main (4.32.0, `17914701`), the violet theme's eight search help rules (Constants/Stylesheets.cs 116-123) draw the
  Specifications report's help panel `#1E1534` under 4.x's black ink, 1.21:1, since the help came in (2d85b713,
  2026-04-17). A 4.x patch that drops the eight rules gives the help 4.x's light panel; it is not part of this package.

## 9. Owner questions

1. (D1/D2) The violet in the dark theme: as here, dark and violet (D2, the recommendation), or the light theme's violet
   only, the dark theme as the run report's (D1: the dark blocks go, and the cards' rule reads a variable the dark
   theme leaves out)?
2. The chosen controls keep 4.x's solid violet (ToolbarStylingTests holds it), or take C's tint manner, which makes the
   theme token overrides alone, about a tenth of its size (inventory 481-485), and turns ToolbarStylingTests' violet
   cases round? The solid's white text is 4.23:1, under AA; violet-600 would be 5.70:1.
3. The accent: violet-700 `#6d28d9` (AA on white and on the violet card), or 4.x's `#8b5cf6`?
4. Pass and bypass violet wherever the theme draws them (as here), or only where 4.x painted them (the passed step,
   the passed cell, the fast duration)?
5. The labs page: its title line with the report's switch, its badge at the label size, its table of every scenario as
   a card. Fine?
6. The Specifications page's step numbers and the steps' column (section 8): a later stage, or as it is?
7. The search help's fix is in S13 because the violet's help rules go with it; the default theme's dark help is fixed
   by the same edit. Keep it here, or a stage of its own?

## 10. Dry run (2026-10-19)

`sh dry-run.sh <commit> <label>` reads the 15 files the scripts edit and the 3 S1 files `scripts-s13.py` checks for
with `git -C C:/Code/Kronikol-v5 cat-file -p <commit>:<path>` into `dry-<label>-base/` (the 2 sheets the Python
stylesheet check reads into `dry-<label>-ref/`), copies it to `dry-<label>/` and applies both scripts. Run for
`db663d88` and for `d385c715`; every result below holds on both.

- `scripts-s13.py`: ok, 8 files. `tests-s13.py`: ok, 7 edited, 2 copied. Every anchor was found exactly once (each
  script asserts it before writing).
- A second run of each is refused: "S13 is in already: the labs page loads the boot script" and "already there: the S13
  tests are in already"; nothing changes.
- BOMs: ReportGenerator.cs and ReportConfigurationOptions.cs keep theirs; no other file has one, the two new files
  none. Line endings: every file LF only, before and after; the three files that end without a newline (RG, the
  options, Stylesheets.cs) still do.
- On a CRLF copy of each base (`dry-<label>-crlf/`): both scripts ok; all 15 edited files CRLF only and, read as LF,
  the same text as the LF run.
- `checks/mutate-s13.py --check dry-<label>`: 35 of 35 anchors found once.
- `checks/css-facts-check.py`: 4 of 15 facts pass on the base, 15 of 15 on the result.
- `ReportGenerator.cs` changes at 75 and 93-94 (`db663d88`) and 76 and 94-95 (`d385c715`), the same edit; so are
  ReportConfigurationOptions.cs's and ToolbarStylingTests.cs's, and the other 12 edited files and the 2 new ones end
  byte for byte the same on both bases. md5s after: `dry-db663d88.md5`, `dry-d385c715.md5`; the diffs:
  `dry-<label>.diff` (1,147 lines each); the runs' output: `dry-<label>.log`.

## 11. Draft text for later steps

- Changelog, `[Unreleased]`, Added (after "Colour and type tokens."): "**The labs page takes the theme.**
  `TestRunReport.labs.html` opens light or dark as the report does, with the report's switch at the right of its title,
  and draws its parts in the report's tokens, its table of every scenario a card as the History section and the
  diagnostics are. Of the report's scripts it runs only the theme's two."
- Changed (breaking): "**The violet theme is written on the tokens.** `Stylesheets.VioletThemeStyleSheet`, the default
  `HtmlSpecificationsCustomStyleSheet`, sets the report's colour tokens for the light and the dark theme, so a
  Specifications report opened dark is dark and violet, and it keeps 4.x's solid violet for a chosen control, the
  passed step and the back-to-top button, under white. Its accent is a darker violet (`#6d28d9`), and a control 4.x's
  rules never named, a report tab or More filters, takes its colours. A sheet built on the 4.x constant's rules should
  set the tokens instead. The history and diagnostics sheets no longer give their colours a fallback."
- Fixed: "**The search help reads in the dark theme.** Its panel, table and keys were 4.x's light greys, a light panel
  under the dark theme's light ink; they take the tokens. On the violet page the help was drawn dark under the light
  theme's dark ink, and it is the base's now." and "**The violet theme's ticks, arrows and edges.** On solid violet the
  passed step's tick and the back-to-top arrow were 1.50:1 and 1.84:1, and a hovered chosen tab took the accent; they
  are white. An attachment's name, a label and the search field's edge read 1.96:1, 3.23:1 and 1.33:1, and take the
  theme's selected colours; the sub-steps rail, drawn in the card's own colour since a scenario sits on its feature's
  card, takes the theme's rail."
- Kronikol4J ledger (`ledger-s10.md`'s S-lines): "S13 (Q3, when applied): `LabsReportGenerator.Build` writes
  `<script id="kronikol-theme-boot">` (report-theme-boot-script.js) before its style block, opens the block with
  report-tokens.css, ends the head with `<script id="kronikol-theme-switch">` (report-theme-switch-script.js), and draws
  the header's first line as `div.title-line` (the `h1` with its badge, then the run report's theme switch button);
  page resources: labs-styles.css (tokens, the scenario table a card), history-styles.css (no fallbacks, no
  `font-family` on `.history-section`) and report-diagnostics-styles.css (no fallbacks), stylesheets.css (the search
  help on tokens), and `VioletThemeStyleSheet` (rewritten: token blocks for light and both dark forms, `--violet-*`
  variables, then 17 rules where 4.x had 49). Kronikol4J's labs page and violet constant are 4.x's."
- Wiki (for S14's list): Generated-Reports.md 110 and Cross-Run-History.md 589 ("It has no script."); Report-Configuration.md
  112 (`HtmlSpecificationsCustomStyleSheet`: "an overlay on it" becomes the tokens it sets, light and dark) and 184
  (`GenerateLabsReport`: "It has no script"); API-Reference.md 70 (`VioletThemeStyleSheet`, "the violet overlay");
  the theming page S14 writes for the tokens (wiki-s10.md 25-27): the violet as the example of a theme on the tokens,
  with its `--violet-*` variables.

## 12. Line references I am less sure of

- ReportGenerator.cs 1816 [1830] for the search line D49 edits: counted on both bases, not read from D49's package.
- The head template's span, 1611-1673 [1625-1687]: from the boot (1619 [1633]) and switch (1667 [1681]) lines and the
  `$$"""` around them.
- plans/OPTION_C_PLAN.md 1004 for S12a's note on the Specifications page: at `d385c715`.
