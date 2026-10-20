# S13b: the colours the tokens never reached (found by S13's preparation, design section 8)

Scope found on 2026-10-20 on S13's tree (s11a, S13 applied over 4e75ce61). CLAUDE.md: fix the bugs found along the way.
Each is a literal colour that reads in the light theme only (or not even there), on a page that has the dark theme
since S1 (the run report, the Specifications report) or since S13 (the labs page).

## Found (declarations with a literal colour, outside var() fallbacks)

stylesheets.css (24 declarations; 3 are meant for both themes and stay):
- KEEP: `.lightbox-overlay` background `rgba(0,0,0,0.85)` (a scrim), `.lightbox-overlay img` shadow,
  `.jump-to-failure:hover` `color-mix(in srgb, var(--kron-fail-solid) 86%, #000)` (a darkened solid).
- `body > details > details > details.example` background-color: white (the raw PlantUML disclosure; RG 2559 and 4058
  write `details.example`; check in the browser whether the child-of-body chain still matches in 5.0.0's tab panels:
  if not, the rule is dead and goes).
- `#searchbar:disabled` background rgb(245,245,245): nothing disables the field (no `disabled` in RG or the scripts):
  dead today; tokenise (`--kron-strip`) or drop.
- `#duration-threshold` border rgb(180,180,180) -> `--kron-control-border`; check its ground and ink in dark (native
  input: does the token sheet set `color-scheme: dark`?).
- `.duration-filters-unit` color rgb(100,100,100) -> `--kron-muted`.
- `.attachment-missing` color #a33 -> `--kron-fail`.
- `.examples-table th, td` border #ddd -> `--kron-hairline`; `thead` #f0f0f0 -> `--kron-strip`; rows: passed #f0fff0,
  failed #fff0f0, skipped #fff8e1, bypassed #f0f0ff, expandable:hover and expanded #ffe0e0, detail row td #fff5f5 ->
  the `-soft` tokens (`--kron-pass-soft`, `--kron-fail-soft`, `--kron-warn-soft`, `--kron-bypass-soft`; hover/expanded
  and the detail row: a stronger fail tint, check what the token sheet has).
- `.row-surplus td:first-child` green, `.row-missing td:first-child` red: NO WRITER in src (cs/js): dead, drop.
- `.flatten-toggle` border #bbb, color #555; hover background #ddd, color #333 -> `--kron-control-border`,
  `--kron-muted`, `--kron-hover`, `--kron-ink`.
- `.no-interactions` color #8a8f98 -> `--kron-faint`.

context-menu-styles.css (the diagram's right-click menu): background #fff, border #ccc, item hover #e8f0fe, hr
#e0e0e0, submenu arrow #666, submenu the same; NO `color` of its own, so in the dark theme its items inherit the light
ink on white (about 1.2:1): the worst of these. Keep the shadows. -> `--kron-card`, `--kron-hairline`, `--kron-hover`,
`--kron-hairline-soft`, `--kron-muted`, and set `color: var(--kron-ink)`.

inline-svg-styles.css (a page without the token sheet carries it too, so tokens need fallbacks here):
- `.puml-fragment:not([data-rendered])` (and its pair) color #999: 2.85:1 on white, under AA in the light theme too ->
  `var(--kron-faint, #6b7280)`-style with an AA fallback.
- `.diagram-selected` ring rgba(59,130,246,0.5) and `.diagram-zoom-slider` accent-color rgb(59,130,246): blue in the
  violet theme -> the accent token with today's value as fallback.

history-styles.css + HistoryHtml.Colour (HistoryHtml.cs 369-378): the sparkline writes its run colours inline as hex
(`#228b22`, `#bf0000`, `#949494`, absent `#e6e6e6`, unknown `#c8c8c8`, not a test `#dcdcdc`, else `#b8a000`) into
`style="background:linear-gradient(90deg,...)"`; its sheet has background rgb(230,230,230) and border
rgba(0,0,0,0.15). On the dark card (labs page since S13; the report with ShowScenarioHistory) an absent run is a bright
bar. Inline var() works in a style attribute; BYTES: `var(--kron-pass)` is 16 characters against 7, twice per run
(20 stops for 10 runs: about +180 bytes per scenario with history). Option: short aliases on `.history-sparkline`
(`--hs-p: var(--kron-pass)` ...) so the inline text is `var(--hs-p)` (11). Decide in the design.

Not fixed here, for the owner list: collapsible-notes-script.js draws the note buttons white (`#ffffff`), edged
`#999`, symbols `#666`, in every theme (they read, 5.7:1, but do not follow the theme; an SVG copied out of the page has
no page CSS, so a var() needs its fallback). context-menu-script.js's new-tab pages are standalone (white grounds,
fine). warm-up-styles.css's hatch is white at 55% over a bar (check in dark).

## Tests to write first (red on S13's tree)
- Core: one fact over the report's own sheets (stylesheets.css, context-menu-styles.css, history-styles.css,
  warm-up-styles.css; inline-svg-styles.css with fallbacks allowed): every colour is a var() but black-with-alpha
  shadows and scrims and the darkened solid (an explicit allowlist by selector and property). Red: each literal listed.
- Core: HistoryHtml.Sparkline writes no hex colour (tokens or aliases), the gradient's stops unchanged otherwise.
- Core: the dead rules (row-surplus, row-missing; searchbar:disabled if dropped; details.example chain if dead) are
  gone.
- Playwright (dark theme by EmulateMedia, light as a guard): the context menu's items read at 4.5:1 on its ground;
  a parameterized scenario's examples table (each row kind) reads at 4.5:1 and paints no light ground; the flatten
  toggle, the missing attachment, the no-interactions line and the duration field read at 4.5:1 (3:1 for edges);
  the labs page's sparkline paints no bar lighter than the card in dark.
- Mutations: one per token put back to its literal, the menu's ink dropped, the sparkline back to hex.

## Dead code found while scoping (2026-10-20, S13's tree)

- `.examples-table`, its cells and `thead`, the `examples-row-*` tints and `.examples-detail-row td` (stylesheets.css,
  plus the phone block's `.examples-table`): no C# or JS writes `examples-table`, `examples-row-passed/failed/...` or
  `examples-detail-row` (searched src and tools, .cs .js .ts .java). The only reader is
  `report-toggle-examples-detail-function.js` (`toggle_examples_detail(row)`), which RG loads (1504) and embeds in
  every report's script (1711), and which nothing calls (no `toggle_examples_detail` anywhere else). Dead since before
  3.0.0 at least (git -S finds only the JS externalisation de7b45a8 and the rebrand). Drop the rules, the function and
  its resource; check tests that pin it (the full suites will say) and the Kronikol4J ledger (its copy of the sheet).
- `.row-surplus td:first-child` (green) and `.row-missing td:first-child` (red): no writer. Drop.
- `#searchbar:disabled`: nothing disables the field. Tokenise or drop (a disabled field is a fair state to style:
  tokenise, `--kron-strip`, cheap).
- `body > details > details > details.example`: `details.example` is written (RG 2559, 4058); whether the chain from
  body still matches in 5.0.0's tab panels needs the browser (if not, drop the rule; if so, tokenise the white).

## Decisions (2026-10-20)

stylesheets.css: `#searchbar:disabled` background `var(--kron-strip)`; `#duration-threshold` border
`1px solid var(--kron-control-border)`; `.duration-filters-unit` `var(--kron-muted)`; `.attachment-missing`
`var(--kron-fail)`; `.flatten-toggle` border `1px solid var(--kron-control-border)`, ink `var(--kron-muted)`, hover
`var(--kron-hover)` under `var(--kron-ink)`; `.no-interactions` `var(--kron-faint)`. Dropped as dead: the
`.examples-table` family (with its phone-block rule), `.row-surplus`/`.row-missing`, and `body > details > details >
details.example` if the browser shows it matches nothing (5.0.0's features sit in a tab panel, not under body).
The script `report-toggle-examples-detail-function.js` and its load and embed (RG 1504, 1711) go.

context-menu-styles.css (ComponentDiagramReportGenerator.cs 211 carries it too, so each var() keeps today's value as
its fallback): menu and submenu `var(--kron-card, #fff)`, edge `1px solid var(--kron-hairline, #ccc)`, and a new
`color: var(--kron-ink, #242b36)`; item hover `var(--kron-hover, #e8f0fe)`; `hr` `var(--kron-hairline-soft, #e0e0e0)`;
the submenu arrow `var(--kron-muted, #666)`. Shadows stay.

inline-svg-styles.css: the placeholder `var(--kron-faint, #767676)` (the fallback AA on white, 4.54:1; #999 was
2.85:1); `.diagram-selected` `0 0 0 3px color-mix(in srgb, var(--kron-accent, #3b82f6) 50%, transparent)`;
`.diagram-zoom-slider` `accent-color: var(--kron-accent, rgb(59, 130, 246))`.

history-styles.css + HistoryHtml.Colour: aliases on `.history-sparkline`: `--sp: var(--kron-pass-ring)`,
`--sf: var(--kron-fail-solid)`, `--ss: var(--kron-skip)`, `--sa: var(--kron-track)`, `--su: var(--kron-hairline)`,
`--sn: var(--kron-hairline-soft)`, `--so: var(--kron-warn)`; `Colour` returns `var(--sp)` and so on (9 characters
where hex took 7: 2 more per stop, 4 more per run). The sheet's own grey ground goes: the page writes the gradient
as an inline `background:` shorthand, which resets the background colour, so the sheet's ground never painted (4.x's
grey included); its edge is `1px solid var(--kron-hairline)` (no fallbacks: S13's fact holds history-styles.css to
none). Light values move: pass rgb(34,139,34) -> rgb(63,154,98), fail rgb(191,0,0) -> rgb(179,55,46), not a test
rgb(220,220,220) -> rgb(226,231,238), other rgb(184,160,0) -> rgb(138,90,23) (HistorySparklineTests and
HistoryHtmlTests' two pins move with them). Dark: absent #232b37, not a test #262f3b, unknown #343e4d on the #171d26
card. GradualZoomTests' glow pin (a regex on 4.x's blue) moves to the accent at half strength, read through a probe.

Owner items: the note buttons (white in every theme, they read); the sparkline's 4.x greens and reds become the
report's pass ring and fail solid (a look change in light, in a view off by default and on the labs page).
