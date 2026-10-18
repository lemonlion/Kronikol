# Option C mock: CSS inventory for the v5 port

Read-only analysis, 2026-10-18. Mock: `scratchpad/cmock/` (round 17 state, `c-skin.css` 148,039 bytes, as embedded in
`option-c-r9.html`). Product: `C:\Code\Kronikol-v5\src\Kronikol\Reports\` and `Constants\Stylesheets.cs`.

Counting method: a rule is one selector block (a selector list counts once); `@keyframes` count as one rule each; line
numbers are each file's own. Counts come from a script over the 19 composed layers: **874 rules** in all.
`M2_CSS.css` lines are given in that file's own numbering (the composed part is lines 408-422, the suffix past
`CPAGE_CSS.css`).

---

## 0. How the mock's stylesheet is built and applied

**Composition (compose.py).** `c-tokens.css`, then the August layers with their one-line `:root` blocks dropped and
every literal colour and short property mapped to a `--kron-*` token (compose.py 31-63; it reports no literal left),
then the round files in this order:

| Order | Layer | Rules | Starts at `c-skin.css` line |
|---|---|---|---|
| 1 | c-tokens.css | 1 | 1 |
| 2 | aug/C_CSS.css (toolbar) | 54 | 65 |
| 3 | aug/CFULL_EXTRA.css (page) | 149 | 170 |
| 4 | aug/M2_CSS.css 408-422 (phone sheet) | 14 | 469 |
| 5 | aug/GEAR_CSS.css | 1 | 487 |
| 6 | c-extra.css | 151 | 491 |
| 7 | c-round2.css | 117 | 702 |
| 8 | c-round3.css | 45 | 840 |
| 9 | c-round4.css | 8 | 916 |
| 10 | c-round5.css | 105 | 957 |
| 11 | c-round6.css | 14 | 1142 |
| 12 | c-round7.css | 41 | 1179 |
| 13 | c-diagram-look.css | 14 | 1281 |
| 14 | c-round8.css | 31 | 1331 |
| 15 | c-round9.css (also rounds 10 and 11) | 30 | 1381 |
| 16 | c-dark.css | 5 | 1444 |
| 17 | c-contrast.css (round 13) | 24 | 1539 |
| 18 | c-round16.css | 12 | 1616 |
| 19 | c-round17.css | 58 | 1651 (ends 1781) |

**Injection (build.js).** The page is `title + theme boot script + engine + the report's head content + <style
id="kronikol-c"> + preview note + body + scripts` (build.js 267-280). The mock's sheet therefore comes **after every
product `<style>`**, including the theme sheet and the `CustomCss` block, so it wins every tie on specificity by order.
That is the root of most of the hacks in section 3.

**JS the CSS depends on.** `c-dom.js` = c-dom.r5.js + c-round6.js + c-diagram-look.js + c-round8.js + c-round9.js +
c-round9-tips.js + c-round13.js + c-round16.js + c-round17.js (build-r9.sh line 7; 2,289 lines, confirmed by line
count). `c-layout.js` runs after it. Three more files are spliced into the product's own scripts by build.js:
c-round16-notes.js and c-round16-strips.js (Events and Setup filters, build.js 91-181) and c-round17-buttons.js (the
whole `createNoteButtons`, build.js 247-265). build.js also patches wording (66-79), the format dropdown's optgroup
(82-89), the open-in-new-tab menu items (203-226) and the flame bar colour (228-245).

**The `kron-c` class** is put on `<html>` by the last line of c-dom.r5.js (674). 85 rules are scoped by it (3.1).

---

## 1. Token layer

### 1.1 How the theme is selected

- **Boot script** (build.js 277-279), first thing in the page, before any `<style>`: reads
  `localStorage['kronikol.theme']` (`"dark"` or `"light"`, inside try/catch); otherwise
  `matchMedia('(prefers-color-scheme: dark)')`; writes `data-theme` on `<html>`.
- **Switch** (c-dom.r5.js 565-610): a `.theme-toggle` button at the right of a new `.title-line` around the `h1`; a
  moon icon in light, a sun in dark (`.theme-sun` hidden by default, swapped by `:root[data-theme="dark"]`,
  c-round5.css 55-57). A click stores the choice; while nothing is stored the page follows the system's
  `prefers-color-scheme` change event.
- **CSS keys only on the attribute.** Light tokens are on `:root`; dark tokens on `:root[data-theme="dark"]`
  (specificity 0,2,0, so they beat the light block wherever they sit). `color-scheme: light` (c-tokens.css 62) and
  `color-scheme: dark` (c-dark.css 6) switch form controls and scrollbars.
- **There is no `@media (prefers-color-scheme: dark)` block anywhere in the CSS.** Without the boot script (script
  blocked, or a copy of the page without it) the page is always light.
- Other `localStorage` keys that change what the CSS shows: `kronikol.reportSettings` (c-dom.r5.js 612-672, report bar
  state replayed through the report's handlers) and `kronikol.scenarioSettings` (c-round17.js 103-118, toggles
  `body.k-scenario-settings`).

### 1.2 Colour tokens, `--kron-*`

Final values after every override (later file wins; dark wins over light by specificity). "Uses" counts `var()`
references in `c-skin.css`.

| Token | Light (`:root`) | Dark (`:root[data-theme="dark"]`) | Set at (L = light, D = dark) | Uses |
|---|---|---|---|---|
| `--kron-accent` | `#2559c7` | `#7ea6f2` | L c-tokens:5, D c-dark:7 | 53 |
| `--kron-selected` | `#dce8fb` | `#1f3254` | L c-tokens:6, D c-dark:8 | 17 |
| `--kron-selected-ink` | `#1d4fb0` | `#b0c9fb` | L c-tokens:7, D c-dark:9 | 19 |
| `--kron-selected-edge` | `#6a8fd9` | `#5b82cf` | L c-tokens:8, D c-dark:10 | 12 |
| `--kron-accent-rail` | `rgba(37, 89, 199, .18)` | `rgba(126, 166, 242, .30)` | L c-tokens:9, D c-dark:11 | 1 |
| `--kron-focus` | `rgba(37, 89, 199, .12)` | `rgba(126, 166, 242, .28)` | L c-tokens:10, D c-dark:12 | 1 (no effect, 1.5) |
| `--kron-hover` | `#edf1f8` | `#222b38` | L c-tokens:11, D c-dark:13 | 34 |
| `--kron-ground` | `#ebeef4` | `#0e131a` | L c-tokens:13, D c-dark:15 | 3 |
| `--kron-card` | `#ffffff` | `#171d26` | L c-tokens:14, D c-dark:16 | 39 |
| `--kron-strip` | `#f8fafd` | `#1b222c` | L c-tokens:15, D c-dark:17 | 15 |
| `--kron-strip-edge` | `#d3d9e3` | `#323c4b` | L c-tokens:16, D c-dark:18 | 6 |
| `--kron-field` | `#f2f5f9` | `#1f2732` | L c-tokens:17, D c-dark:19 | 8 |
| `--kron-quiet` | `#f7f9fc` | `#1b222c` | L c-tokens:18, D c-dark:20 | 3 |
| `--kron-quiet-edge` | `#d3d9e3` | `#323c4b` | L c-tokens:19, D c-dark:21 | 3 |
| `--kron-hairline` | `#d0d7e2` | `#343e4d` | L c-tokens:20, D c-dark:22 | 35 |
| `--kron-hairline-soft` | `#e2e7ee` | `#262f3b` | L c-tokens:21, D c-dark:23 | 24 |
| `--kron-divider` | `#cdd4df` | `#384354` | L c-tokens:22, D c-dark:24 | 7 |
| `--kron-divider-soft` | `#dde2ea` | `#2b3440` | L c-tokens:23, D c-dark:25 | 6 |
| `--kron-control-border` | `#aeb7c6` | `#4a5568` | L c-tokens:24, D c-dark:26 | 15 |
| `--kron-select-border` | `#7a8496` | `#748093` | L c-tokens:25 (`#858fa0`), D c-dark:27 (`#6f7b8e`), L c-contrast:10, D c-contrast:11 | 10 |
| `--kron-dot` | `#8a94a5` | `#7d899b` | L c-tokens:26, D c-dark:28 | 10 |
| `--kron-shadow` | `rgba(20, 30, 60, .06)` | `rgba(0, 0, 0, .45)` | L c-tokens:27, D c-dark:29 | 4 |
| `--kron-card-shadow` | `0 1px 2px rgba(20,30,60,.07), 0 2px 6px rgba(20,30,60,.05)` | `0 1px 2px rgba(0,0,0,.5), 0 2px 8px rgba(0,0,0,.3)` | L c-tokens:28, D c-dark:30 | 3 |
| `--kron-ink` | `#242b36` | `#e4e9f0` | L c-tokens:30, D c-dark:32 | 43 |
| `--kron-ink-soft` | `#4a5361` | `#c5cdd8` | L c-tokens:31, D c-dark:33 | 32 |
| `--kron-ink-quiet` | `#545c6a` | `#b3bcc8` | L c-tokens:32, D c-dark:34 | 11 |
| `--kron-muted` | `#5b6474` | `#a0aab7` | L c-tokens:33, D c-dark:35 | 31 |
| `--kron-label` | `#5f6878` | `#9ba5b3` | L c-tokens:34, D c-dark:36 | 21 |
| `--kron-label-soft` | `#616a7a` | `#98a2b0` | L c-tokens:35, D c-dark:37 | 4 |
| `--kron-faint` | `#626b7a` | `#949eac` | L c-tokens:36, D c-dark:38 | 9 |
| `--kron-on-solid` | `#ffffff` | `#ffffff` | L c-tokens:37, D c-dark:39 | 5 |
| `--kron-pass` | `#276c44` | `#62cd8f` | L c-tokens:39, D c-dark:41 | 9 |
| `--kron-pass-soft` | `#e6f3ec` | `#15301f` | L c-tokens:40, D c-dark:42 | 9 |
| `--kron-pass-ring` | `#3f9a62` | `#3fa66a` | L c-tokens:41, D c-dark:43 | 3 |
| `--kron-fail` | `#b3372e` | `#f28b81` | L c-tokens:42, D c-dark:44 | 27 |
| `--kron-fail-soft` | `#fbecea` | `#3b1d1b` | L c-tokens:43, D c-dark:45 | 12 |
| `--kron-fail-edge` | `#efc7c2` | `#6e302b` | L c-tokens:44, D c-dark:46 | 7 |
| `--kron-fail-ink` | `#7c2a24` | `#f6b3ab` | L c-tokens:45, D c-dark:47 | 2 |
| `--kron-fail-shadow` | `rgba(120, 30, 25, .25)` | `rgba(0, 0, 0, .45)` | L c-tokens:46, D c-dark:48 | 1 |
| `--kron-exclude-soft` | `#fbf1f0` | `#351f1d` | L c-tokens:47, D c-dark:49 | 2 |
| `--kron-exclude-edge` | `#e6c9c5` | `#6a3934` | L c-tokens:48, D c-dark:50 | 2 |
| `--kron-exclude-ink` | `#8a4a43` | `#eea69d` | L c-tokens:49, D c-dark:51 | 2 |
| `--kron-warn` | `#8a5a17` | `#e5b066` | L c-tokens:50, D c-dark:52 | 7 |
| `--kron-warn-soft` | `#fbf3e7` | `#2d2415` | L c-tokens:51, D c-dark:53 | 6 |
| `--kron-warn-edge` | `#ecd3ae` | `#66502a` | L c-tokens:52, D c-dark:54 | 4 |
| `--kron-skip` | `#5b6170` | `#a9b1be` | L c-tokens:53, D c-dark:55 | 3 |
| `--kron-skip-soft` | `#eef0f3` | `#262e39` | L c-tokens:54, D c-dark:56 | 3 |
| `--kron-bypass` | `#285fba` | `#8ab1f6` | L c-tokens:55, D c-dark:57 | 3 |
| `--kron-bypass-soft` | `#e8f0fc` | `#1c2a45` | L c-tokens:56, D c-dark:58 | 2 |
| `--kron-fail-solid` | `#b3372e` | `#b8433a` | L c-round5:118, D c-dark:87 | 1 |
| `--kron-code-bg` | `#e9edf3` | `#262f3c` | L c-round5:119, D c-dark:88 | 2 |
| `--kron-track` | `#edf0f5` | `#232b37` | L c-round5:120, D c-dark:89 | 1 |
| `--kron-exception-soft` | `#f8dcd9` | `#4a2220` | L c-round5:121, D c-dark:90 | 1 |
| `--kron-prop-key` | `#7d3c98` | `#c99be3` | L c-round5:148, D c-dark:92 | 1 |
| `--kron-prop-val` | `#1a5276` | `#8fc3ec` | L c-round5:148, D c-dark:92 | 1 |
| `--kron-panel` | `#f4f6fa` | `#12181f` | L c-round9:25, D c-round9:26 | 1 |

### 1.3 Diagram tokens, `--dg-*`

| Token | Light | Dark | Set at | Uses |
|---|---|---|---|---|
| `--dg-bg` | `#ffffff` | `#171d26` | L c-round5:65, D c-dark:60 | 1 |
| `--dg-ink` | `#242b36` | `#e4e9f0` | L c-round5:66, D c-dark:61 | 1 |
| `--dg-ink-soft` | `#5b6474` | `#aab3c0` | L c-round5:67, D c-dark:62 | 1 |
| `--dg-line` | `#4a5361` | `#8792a3` | L c-round5:68, D c-dark:63 | 2 |
| `--dg-participant` | `#e9eef6` | `#253041` | L c-round5:69, D c-dark:64 | 2 |
| `--dg-note` | `#fdf6e4` | `#2b2618` | L c-round5:70, D c-dark:65 | 2 |
| `--dg-note-edge` | `#b9a57a` | `#75653f` | L c-round5:71, D c-dark:66 | 1 |
| `--dg-event` | `#eee1f7` | `#2a2140` | L c-round5:72 (`#e2eff9`), D c-dark:67 (`#16283b`), L c-round17:79, D c-round17:80 | 2 |
| `--dg-event-edge` | `#b48cc8` | `#7d5c9c` | L c-round5:73, D c-dark:68, L c-round17:79, D c-round17:80 | 1 |
| `--dg-pass` | `#e3f1e8` | `#15301f` | L c-round5:74, D c-dark:69 | 1 |
| `--dg-pass-edge` | `#8cbf9f` | `#33724d` | L c-round5:75, D c-dark:70 | 1 |
| `--dg-fail` | `#fbe8e6` | `#3b1d1b` | L c-round5:76, D c-dark:71 | 1 |
| `--dg-fail-edge` | `#dc9f97` | `#7c3b34` | L c-round5:77, D c-dark:72 | 1 |
| `--dg-bar` | `#2f3b4f` | `#3d4d69` | L c-round5:78, D c-dark:73 | 1 |
| `--dg-activity` | `#eef3fc` | `#1d2638` | L c-round5:79, D c-dark:74 | 1 |
| `--dg-activity-edge` | `#5f6878` | `#7d899b` | L c-round5:80, D c-dark:75 | 1 |
| `--dg-lane` | `#c9d1de` | `#3a4656` | L c-round5:81, D c-dark:76 | 1 |
| `--dg-http` | `#438dd5` | `#6aa5e4` | L c-round5:82, D c-dark:77 | 2 |
| `--dg-db` | `#0e7c86` | `#45b3bf` | L c-round5:83 (`#e74c3c`), D c-dark:78 (`#f07a6c`), L c-round17:116, D c-round17:117 | 2 |
| `--dg-msg` | `#9b59b6` | `#c18fdc` | L c-round5:84, D c-dark:79 | 2 |
| `--dg-person` | `#08427b` | `#2f6db0` | L c-round5:85, D c-dark:80 | 1 |
| `--dg-icon` | `#ffffff` | `#222b38` | L c-round5:86, D c-dark:81 | 1 |
| `--dg-icon-edge` | `#8a94a5` | `#7d899b` | L c-round5:87, D c-dark:82 | 1 |
| `--dg-icon-ink` | `#5b6474` | `#c5cdd8` | L c-round5:88, D c-dark:83 | 3 |
| `--dg-http-fill` | `#2b6cb0` | (same, no dark value) | L c-round7:98 | 1 |
| `--dg-db-fill` | `#0b6c75` | `#0b6c75` | L c-round7:98 (`#c0392b`), L c-round17:116, D c-round17:117 | 1 |
| `--dg-msg-fill` | `#8e44ad` | (same, no dark value) | L c-round7:98 | 1 |
| `--dg-participant-edge` | `#b6c2d5` | `#4d5d76` | L c-diagram-look:7, D c-diagram-look:20 | 1 |
| `--dg-lifeline` | `#cdd5e0` | `#334052` | L c-diagram-look:8, D c-diagram-look:21 | 1 |
| `--dg-card-edge` | `#e0cf9f` | `#5e5236` | L c-diagram-look:9, D c-diagram-look:22 | 1 |
| `--dg-event-card-edge` | `#cfb6e6` | `#5d4780` | L c-diagram-look:10, D c-diagram-look:23, L c-round17:79, D c-round17:80 | 1 |
| `--dg-step` | `#e9eef6` | `#222c3a` | L c-diagram-look:11, D c-diagram-look:24 | 1 |
| `--dg-step-edge` | `#c4cfdf` | `#3b4a60` | L c-diagram-look:12, D c-diagram-look:25 | 1 |
| `--dg-step-ink` | `#2f3b4f` | `#dbe3ee` | L c-diagram-look:13, D c-diagram-look:26 | 1 |
| `--dg-step-rule` | `#a3adbe` | `#4f5d72` | L c-diagram-look:14, D c-diagram-look:27 | 1 (no effect, 1.5) |
| `--dg-step-table` | `#ffffff` | `#131a24` | L c-diagram-look:15, D c-diagram-look:28 | 1 |
| `--dg-badge` | `#2f3b4e` | `#c5cdd8` | L c-diagram-look:16, D c-diagram-look:29 | 1 |
| `--dg-badge-ink` | `#ffffff` | `#171d26` | L c-diagram-look:17, D c-diagram-look:30 | 1 |
| `--dg-db-edge` | `#09616a` | `#2f8f99` | L c-round17:119, D c-round17:120 | 1 |

### 1.4 Type, geometry and JS-set custom properties (one value for both themes)

| Token | Value | Set at | Uses |
|---|---|---|---|
| `--kron-font` | `-apple-system, BlinkMacSystemFont, "Segoe UI", "Noto Sans", Roboto, "Helvetica Neue", Arial, sans-serif` | c-tokens:58 (system-ui first), replaced c-round7:12 (no emoji fonts, round 11) | 36 |
| `--kron-mono` | `ui-monospace, SFMono-Regular, "SF Mono", Menlo, "Cascadia Mono", Consolas, "Liberation Mono", monospace` | c-tokens:59, replaced c-round7:13 | 24 |
| `--kron-fs-label` / `-meta` / `-control` / `-body` / `-feature` / `-title` | `11px` / `12px` / `13px` / `14px` / `16px` / `22px` | c-round7:14-19 | 8 / 4 / 9 / 4 / 1 / 1 |
| `--kron-label-track` | `.06em` | c-round7:20 | 5 |
| `--kron-count-slot` | `3ch` (JS overrides on the box, c-dom.r5.js 235) | c-tokens:60 | 1 (dead, 1.5) |
| `--gear-shift` | `-2.5px` | c-tokens:61 | 2 |
| `--kron-nav-h` | bar height in px, written on `<html>` by c-dom.r5.js 516 (0 when the bar is not sticky) | JS only | scroll-margin, 4 rules |
| `--fh` | flame bar hue, written per bar by the patched `buildBar` (build.js 228-245; hues 212, 262, 188, 338, 152, 32, 290, 8) | JS only | c-round17.css 102-106 |

### 1.5 Findings on the token layer

- **Effectively dead tokens:** `--dg-step-rule` (its only use, c-diagram-look.css 44, is overridden by the same
  selector at c-round17.css 50 with `--kron-hairline`); `--kron-focus` (CFULL_EXTRA.css 49's focus halo is cancelled by
  `box-shadow: none` at c-contrast.css 22); `--kron-count-slot` (only `.fcnt`, which no element carries).
- **Theme-independent fills:** `--dg-http-fill`, `--dg-msg-fill` and `--dg-db-fill` have no separate dark value by
  design (round 7: component box names are white in both themes).
- **Literal colours left after composition:** only the select chevron, `fill='%236a7280'` inside the data URIs at
  C_CSS.css 73, c-extra.css 192 and c-round2.css 70 (the `#` is URL-encoded, so compose.py's map does not see it; the
  chevron does not follow the theme), and the dark overlay `rgba(0,0,0,.6)` (c-dark.css 93).
- **Two tokens are overridden in place** across rounds rather than edited where defined: `--kron-select-border`
  (c-contrast.css 10-11) and the event, database and font tokens (c-round17.css 79-80, 116-120; c-round7.css 12-13). A
  native token file states each once.
- **No `prefers-color-scheme` fallback** (1.1). A native port should either keep a blocking boot script in `<head>`
  or repeat the dark block under `@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { ... } }`
  (best generated from one token table so the two cannot drift).

---

## 2. Rule groups by product surface

Each rule belongs to one group (the keyword classifier's misreads were corrected by hand: the More filters chevron
and fold icons are not diagram rules). Cross-cutting views (phone, dark, contrast, focus, type) follow the table and
overlap with it. "Mock-JS DOM" lists class names that exist only because the mock's scripts create or toggle them.

| # | Surface | Mock rules: file lines (count) | Rules | Product CSS it overrides or replaces | Mock-JS DOM it depends on |
|---|---|---|---|---|---|
| 0 | Page chrome and header | CFULL_EXTRA 2-4, 229; C_CSS 80; c-extra 4-17, 198-200; c-round5 48-57, 144; c-round7 24, 76; c-dark 85; c-contrast 40, 67-68 | 32 | stylesheets.css 1-7 (body), 21-28 (summary, details margins), 83-96 (.h2-.h4), 534-557 (.jump-to-failure), 1533-1551 (.back-to-top), phone 1564-1567, 1697-1710, 480 px 1752-1754, 1765-1771. The product has no `h1` rule. | `.title-line`, `.theme-toggle`, `.theme-icon`, `.theme-moon`, `.theme-sun` (c-dom.r5.js 593-610); `data-theme` (boot script); `.kron-preview-note` (build.js 269, preview only) |
| 1 | Tokens | 23 `:root` blocks (1.2-1.4) | 23 | none today: every product colour is a literal | `--kron-nav-h`, `--fh`, `--kron-count-slot` written by JS |
| 2 | Run summary card | **live:** c-round2 4-27 (incl. donut remaps 7-10, ring text 11), 123, 128-129; c-round7 34, 77-78. **dead:** CFULL_EXTRA 6-19, 103-104, 116, 230, 238-242; c-extra 34-35, 38 | 51 (28 live, 23 dead) | stylesheets.css 98-129 (.column-header, .test-execution-summary, .header-row, .summary-chart), 1557-1560, 1569-1581, 1756-1763; markup ReportGenerator.cs 1666-1679; donut `GeneratePieChartSvg` 2711-2745 | `.run-band`, `.run-ring` (a clone of `.summary-chart svg`), `.run-zone`, `.run-summary`, `.run-head`, `.run-title`, `.run-status`, `.run-status-{status}`, `.run-when`, `.run-stats`, `.run-stat`, `.run-stat-pass`, `.run-stat-fail`, `.ci-below` (measured) (c-layout.js 21-139). The product's `.header-row` is hidden (c-layout.js 114), so the dead rules style nothing. |
| 3 | CI card | **live:** c-round2 13, 16, 28-29. **dead:** CFULL_EXTRA 23-31, 105, 122, 127, 133, 230; c-extra 36 | 20 (4 live, 16 dead) | stylesheets.css 131-159 (.ci-chart-group, .ci-metadata, the 768.02 px cap), 1583-1593; markup ReportGenerator.cs 1682-1707 | `.run-zone.run-ci`, provider name mapped by `PROVIDERS` (c-layout.js 36), `.run-link` "View run" (85-108) |
| 4 | Filtering card | CFULL_EXTRA 33-95, 141-183, 244-272; c-extra 39-76, 91-100; c-round2 31-38; c-round3 3-58; c-round4 15, 25-30; c-round5 3-45, 166-172; c-round6 4-26; c-round7 56-63, 70-72; c-round9 1-20; c-contrast 17-22, 57-60; c-round16 26-28 | 182 | stylesheets.css 161-311 (box, header, export, filters, search, deep chip, help), 375-442 (status, happy, dependency, AND/OR, category mode), 469-518 (duration, percentile), 595-624 (export), 921-955 (category), 1497-1514 (mobile toggle), phone 1595-1617, 1716-1720, 480 px 1779-1797, 1812-1814; markup ReportGenerator.cs 1734-1825 | `.fltr-controls` (c-dom.r5.js 96-104); `.happy-filters`, `.happy-filters-label`, `.happy-seg` (150-163; the product's `.happy-path-filters` is hidden but kept for the URL hash, export and Clear All); `.tag-filters`, `.tag-filters-label`, `.tag-mode-toggle`, `.tag-toggle`, `.tagf-none`, `.tag-active`, `.tagf-out` (166-246); dependency `.tagf-out` and the "No dependencies" pseudo chip (209-230); `.wide-only` (122-125); `.filter-readout`, `.is-filtered` (380-446); `.mobile-filter-line` (392-404); `.pair-fits`, `.trio-fits` (506-513, measured); `.group-clear` (c-round6.js 26-51); `.more-filters`, `.more-filters-label`, `.more-filters-count`, `.has-on`, `.rows-folded`, `.help-open` (c-round9.js 1-67); `.kron-q-bad` (c-round16.js 164) |
| 5 | Report nav bar and tabs | c-round2 40-50, 59, 124; c-round7 47; c-round8 5-26, 37, 48; c-round9 22-30; c-contrast 71-76; c-round17 93-96 | 40 | stylesheets.css 626-648 (.toolbar-row, -left, -right), phone 1619-1629, 480 px 1773-1776; markup ReportGenerator.cs 1830-1862. The product has no tabs: Timeline and Components are `.timeline-toggle` panel buttons, History and Diagnostics are `<details>`. | `.report-nav` (added to `.toolbar-row`), `.report-tabs`, `.report-tab`, `.tab-n`, `.report-panels`, `.report-panel`, `.features-panel`, `.on-features` (c-layout.js 141-203, 557-608); `.nav-row`, `.nav-filters`, `.nav-readout`, `.nav-readout-long`, `.nav-readout-short`, `.nav-clear`, `.nav-filters-toggle`, `.is-active`, `.nav-compact`, `.nav-minimal`, `.nav-mark`, `.nav-float-sentinel`, `.k-floating` (c-round8.js); `.is-pinned` (c-round9.js 75-106) |
| 6 | Features tools line | C_CSS 78-79; CFULL_EXTRA 185-191, 280-281; c-round2 51-69, 125-136; c-round4 18; c-round6 28-36; c-round7 41; c-round9 36-50; c-contrast 64, 69; c-round17 82-91, 123-131 | 66 | stylesheets.css 520-532 (.collapse-expand-all), 636-641 (.toolbar-left), 1331-1350 (.timeline-toggle), 480 px 1773-1784 | `.features-tools`, `.features-tools-line` (it also carries `.toolbar-left`, so CFULL_EXTRA 280-281 apply to it on phones), `.view-switch`, `.view-btn`, `.sort-select#features-sort`, `.tools-spacer`, `.tools-gear`, `.tools-open`, `.features-table-mode`, `.toolbar-left-retired` (c-layout.js 204-336); `.sort-group`, `.sort-label` (c-round6.js 53-69); tab roles on Full and Summary (c-round9.js 108-151); `.k-fold-group`, `.k-fold-label`, `.k-fold-btn`, `.k-fold-expand`, `.k-fold-collapse` replacing `.collapse-expand-all` (c-round17.js 31-96); `.k-flag.k-scenario-settings-toggle` (98-123) |
| 7a | Payload and diagram controls, shared by both bars | C_CSS 18, 37-51, 57-76, 87-89, 103; M2_CSS 410-422; c-extra 52, 83, 191-194, 206; c-round9 52-58; c-round17 7-24 | 56 | collapsible-notes-styles.css 1-31 (radio, label, toggle, lines select and label, format and width selects), 35-41 (phone), 42-73 (pending spinner); stylesheets.css 1631-1636 (phone), 1812-1814; internal-flow-popup-styles.css 129 (.span-count-warning) | `.diagram-format`, `.diagram-flags` (c-dom.r5.js 54-67); `data-chip-label` and `aria-pressed` on `.toggle-btn` (22-36); the words "Payloads", "Expanded/Collapsed/Truncated", "N LINES" (38-51); Events and Setup buttons (build.js 163-179); the format optgroup (build.js 82-89) |
| 7b | Report-level options bar placement | C_CSS 52, 91; CFULL_EXTRA 186-188, 230, 278-298; c-extra 79, 87-88; c-round16 30-35 (and c-round2 54, counted in 6) | 26 | stylesheets.css 643-648, 1625-1629, 1773-1776; markup ReportGenerator.cs 1847-1861 | `.zone-adjacent` (c-dom.r5.js 517-523, measured); `.toolbar-right` moved into `.features-tools` (c-layout.js 305) |
| 8 | Filter box riding in the bar | c-round8 27-45; c-round9 31 | 11 | none (new behaviour) | the box moved into `.report-nav` (c-round8.js 169-211), `.has-filters`, `.riding`, `.filters-close`, `.filters-empty`, `.filters-empty-clear`, inline `max-height` (capBox 155-161) |
| 9 | Features and scenarios list | CFULL_EXTRA 193-197, 216; c-extra 108-118; c-round7 75; c-contrast 46 | 17 | stylesheets.css 30-48 (.feature, .scenario grey boxes), 332-338 (.failed, .skipped), 869-874 (.feature-description), 884-895 (.rule, .h2-5), phone 1648-1656 | none: product markup, moved into `.features-panel` and re-ordered by Sort (c-layout.js 252-286) |
| 9b | Features Summary table (the Summary view) | c-extra 19-31; c-round2 74-87; c-round5 165 | 27 | stylesheets.css 957-997, 1675-1678 | `.features-table-view` holding the moved `.features-summary-table-wrapper`; `tr.table-groups`, `.grp-start`, headers renamed "Total" (c-layout.js 175-243). The `.features-summary-details` card is hidden (181), so c-extra 20, 22-23 are dead. |
| 10 | Scenario header | CFULL_EXTRA 198-206; c-extra 119-126, 161; c-round7 69; c-contrast 15, 47 | 18 | stylesheets.css 340-357 (span.label, .endpoint), 444-467 (.duration-badge and speed tints), 559-593 (copy and link buttons), 650-653 (.scenario-focused), 1691-1695, 1799-1809; warm-up-styles.css 3-6; history-styles.css 2-50 (sparkline and verdict kinds, not restyled) | none |
| 11 | Failure result and error diff | CFULL_EXTRA 217-218; c-extra 149-163; c-round7 38; c-round17 30-36 | 18 | stylesheets.css 313-330, 1133-1171, phone 1686-1689. Not restyled: 1173-1226 (.failure-clusters), 1228-1259 (.background-calls) | diff marks re-drawn word by word (c-round16.js 209-262, which notes the change belongs in `ErrorDiffParser.GenerateDiffHtml`); the colon removed from `.diff-label` (c-dom.r5.js 468) |
| 12 | Steps, including the round 17 alignment | CFULL_EXTRA 207-215; c-extra 128-147; c-round3 43; c-round4 32-36; c-round5 124, 150, 155-156; c-round7 64-68, 90; c-round9 60-63; c-round16 9; c-round17 26-28, 37-40, 53-66 | 41 | stylesheets.css 655-867 (step containers, step, background, number, status, keyword, text, duration, comment, attachment, images, lightbox, doc string, sub-steps, collapsible) | none |
| 13 | Parameter tables | c-extra 165-184; c-round5 25-30, 125-139, 149, 153-164; c-round7 37, 46, 82-88; c-round16 11-24 | 55 | stylesheets.css 999-1131 (inline values, step tables, combined table, tree), 1353-1450 (parameter table, row tints, block rows, wrapper, flatten), 1453-1464 (status badges), 1482-1494 (cell sub-table, param-expand), phone 1668-1673 | none |
| 14 | Diagrams section and per-scenario options bar | C_CSS 4-11, 53, 82-85, 92-100, 104; CFULL_EXTRA 220-227; M2_CSS 409; GEAR_CSS 1-3; c-extra 84-86, 130, 186-190, 204-205; c-round3 45-47; c-round5 151; c-round7 61-62; c-contrast 65-66; c-round16 7-8; c-round17 58-63, 68-74, 123-125 | 51 | stylesheets.css 54-75, 1516-1530, 1639-1646, 1737-1743, 1823-1858 (.diagram-toggle, -btn, -active, -spacer, inline layout); inline-svg-styles.css 1-14, 20-37; collapsible-notes-styles.css 38-40 | `.diagram-toggle-tabs` split off `.diagram-toggle`, the rest marked `.diagram-options-bar`, spacers removed (c-dom.r5.js 69-90); `.not-seq`, `.diagrams-not-seq` (448-465); `.gear-in-summary`, `.gear-open` (538-563); `body.k-scenario-settings` (c-round17.js 98-123) |
| 16 | PlantUML SVG diagram look | c-round5 90-110; c-round7 99-101; c-diagram-look 33-49; c-round17 5, 47, 50-51, 121 (tokens in 1.3) | 41 | nothing in CSS today: diagram colours come from the emitters (3.5) | role classes from c-diagram-look.js 118-427: `k-lifeline`, `k-participant`, `k-participant-fill`, `k-step`, `k-step-text`, `k-step-rule`, `k-step-table`, `k-step-table-head`, `k-step-table-head-text`, `k-step-lift`, `k-card`, `k-card-event`, `k-card-assertion`, `k-fold`, `k-badge`, `k-number` (badges are off by default); `data-k-sequence` on the host, `data-k-roles` on the svg. Part 1 of the same file (32-116) also rewrites every PlantUML source (3.5). |
| 17 | Notes (buttons, hover) | c-round5 111-114; c-round17 42-43 | 4 | collapsible-notes-styles.css 32-34 | the product's own `createNoteButtons` (collapsible-notes-script.js from 318) writes `fill="#ffffff"`, `stroke="#999"`, `#666` (344-545) and the transparent `.note-hover-rect` (660-667); build.js replaces the whole function with c-round17-buttons.js (triangles as `<polygon fill="#666">`) |
| 18 | Internal-flow popup and flame chart | c-extra 195; c-round5 152, 178-183; c-dark 93; c-contrast 24-30; c-round17 98-110 | 21 | internal-flow-popup-styles.css 1-52, 66-116; flame-chart-render-script.js 11 (`hsl(hue, 60%, lightness)`). Not restyled: 53-65, 117-143 (call tree, whole-test flow, notes buttons, test band, relationship list and table, link hover `#0000EE`) | `--fh` per bar (build.js 228-245) |
| 19 | Timeline | c-extra 102-106; c-round3 60-74; c-round4 40; c-round5 141; c-round7 56 (the shared 13 px control rule), 85; c-contrast 32-36, 48-55 | 25 | stylesheets.css 1261-1330, 1658-1666; warm-up-styles.css 7-14 | `.timeline-controls`, `.tl-seg`, `.tl-seg-label`, `.tl-seg-btn`, `.filtered-note` (c-layout.js 338-450); bar widths and order set inline |
| 20 | Components tab | c-round3 75; c-round9 34 (plus c-extra 103, the tl-seg controls, c-round7 99-101 and c-round17 121) | 2 | none: `.component-diagram-section` has no product CSS | `.timeline-header.component-header`, `.timeline-controls`, three `.tl-seg` groups, `.filtered-note` (c-layout.js 452-555); the title text and `.timeline-info` mark (c-round9.js 94-102); the source is rewritten through `window._preProcessSource` |
| 21 | History | c-round2 93-116; c-round5 142; c-round7 28, 50-53, 86-91 | 31 | history-styles.css 51-136 (section, charts, lists); 2-50 kinds not restyled | `<details>` forced open, summary inert (c-layout.js 193-202); `pattern.k-fail-hatch` on failed bars (c-round13.js 1-56, inline fills, no CSS rule) |
| 22 | Diagnostics | c-round2 73, 89-92, 117-120; c-round4 9; c-round7 87, 92 | 11 | report-diagnostics-styles.css 2-53 | forced open (c-layout.js 193-202) |
| 23 | Context menu | c-round5 173-177 | 5 | context-menu-styles.css colours only (3-6, 17-19, 23, 36, 43-46, 63-65); inline-svg-styles.css 73 (slider) | none for CSS; c-round17.js 1-29 and build.js 203-226 change the open-in-new-tab items |

c-round4.css 9 (the card shadow on every card) and c-round4.css 18-23 (the selected outline on every segment and
chip) are single rules shared by several surfaces; they are counted where the classifier first met them (22 and 6).

### 2.1 Phone and narrow layout (cross-cut: 119 rules)

| Query | Rules | Where |
|---|---|---|
| `@media (max-width: 768px)` | 71 | C_CSS 86-101; M2_CSS 408-422; CFULL_EXTRA 230, 277-299; c-extra 203-207; c-round2 122-137; c-round6 32-36; c-round7 72; c-round8 47-49; c-round9 19 |
| `@container (max-width: 430px)` | 21 | CFULL_EXTRA 247-254, 261-272; c-extra 73-76, 98-100; c-round3 24-29, 53-58; c-round5 40; c-round6 12-15 |
| `@container (max-width: 520px)` | 9 | CFULL_EXTRA 141-150; c-round3 20-23; c-round5 6-11 |
| `@media (max-width: 600px)` | 6 | CFULL_EXTRA 237-243 (dead), c-round4 40 |
| `@media (max-width: 480px)` | 3 | C_CSS 102-105; c-extra 7 |
| other narrow queries | 9 | `@container (max-width: 875.98px)` CFULL_EXTRA 94 (its `min-width: 876px` twin at 90-91; both superseded by `.pair-fits`); `@container (max-width: 300px)` c-round5 13; `@container (width > 430px)` c-round6 19; `@media (max-width: 1350px)` CFULL_EXTRA 116, 117, 122, `(min-width: 900px) and (max-width: 1350px)` 127 and `(min-width: 769px) and (max-width: 899px)` 133 (header band, dead); `(min-width: 769px) and (max-width: 940px)` C_CSS 53 |

The filtering box is the query container (`container-type: inline-size`, CFULL_EXTRA 37), so its rows reflow on the
box's width, not the window's. Desktop-only rules sit under `@media (min-width: 769px)`: 22 rules (CFULL_EXTRA 227;
c-extra 85-89; c-round9 4-7, 27-32, 45-50, 56-58; c-round16 32-35; c-round17 11-18, 96), plus the two ranges that start
at 769 px (C_CSS 53, CFULL_EXTRA 133). The 1351 px header grid (CFULL_EXTRA 102-107, c-extra 38) is dead with the
header band.

### 2.2 Dark theme (cross-cut: 15 rules keyed on `:root[data-theme="dark"]`)

c-dark.css 5 (the token block), 85 (body), 86 and 92 (late tokens), 93 (popup overlay); c-diagram-look.css 19;
c-round9.css 26; c-contrast.css 11; c-round17.css 80, 105-106 (flame bars), 117, 120; c-round5.css 56-57 (theme icons).
Everything else follows the dark theme only through tokens.

### 2.3 Contrast fixes (cross-cut)

c-round4.css (8 rules: card shadow, 3:1 field border, outlined selected state, one label column, step indent);
c-contrast.css (24 rules, round 13: select border 3:1 on every surface, warm-up note at full strength, placeholder,
search focus ring, popup toggle as tabs, info mark 3:1, failure counter, the cross before failed feature, scenario and
timeline names, token bar colours, deep-search chip, hover on selects and headings, scroll margin under the pinned
bar). Contrast reasons also drive c-round5.css 126-129 (pass badge was 4.04:1), c-round7.css 94-101 (component box
fills for white names) and c-round17.css 112-120 (database colour clear of failure red).

### 2.4 Focus styles (cross-cut: 17 rules)

The global `button:focus-visible, select:focus-visible { outline: 2px solid var(--kron-accent); outline-offset: 1px }`
(C_CSS 80) already covers every button. Ten rules restate exactly that and nothing else: c-extra 72; c-round2 68;
c-round3 19, 70; c-round6 25; c-round8 27; c-round9 13; c-contrast 30; c-round17 89, 131. Real exceptions: c-round2 48
(`.report-tab`, offset -3px, inside the scrolling tab row); c-round5 53 (`.theme-toggle`, offset 2px); c-extra 123
(copy and link buttons revealed on focus); the search field, in three layers: CFULL_EXTRA 49 (August halo, cancelled),
c-round5 172 (accent border on `#searchbar`) and c-contrast 22 (the 2 px ring that replaces the halo). c-contrast.css 76
(scroll margin for focus not obscured, WCAG 2.4.11) belongs with them.

### 2.5 Type scale (cross-cut: c-round7.css, 41 rules)

Six sizes (11/12/13/14/16/22), weights 400 and 600 only, tabular digits for numbers, the UI font for numbers and mono
only for literals. 36 of the 41 rules are `.kron-c` scoped so they outrank both the product's sizes and the August
layers'. In a native sheet these values belong in each surface's own rule, not in a scale layer on top.

---

## 3. Hacks to drop in a native port

### 3.1 `.kron-c` scoping (85 rules)

c-extra.css 79, 83, 88, 174-180 (8); c-round2.css 77-83, 90-120 (38); c-round4.css 9 (1); c-round7.css 24-92 (36);
c-round16.css 33-34 (2). Each exists to reach or exceed the specificity of a product or August rule (for example
`.kron-c .feature-summary-table th` 0,2,1 over stylesheets.css 987 0,1,1; `.kron-c .param-test-table tbody tr` 0,2,2,
which ties stylesheets.css 1387-1401's `tr.row-passed` and wins by order). A native sheet has no layer to beat: drop the
prefix and the `kron-c` class (c-dom.r5.js 674). Before the script runs those 85 rules do not apply, so the mock also
paints once without them.

### 3.2 `!important` (33 rules)

| Mock | Why it is there | Native |
|---|---|---|
| c-extra 4 `[hidden]` | product rules set `display` on elements the mock hides with the attribute | keep as a base reset (common practice), fewer conflicts |
| c-round2 58 `.toolbar-left-retired` | the old action row, kept hidden for the report's scripts | drop with the element |
| c-round3 46-47 `.not-seq`, `.diagrams-not-seq ... .gear-in-summary` | the product's phone script writes inline `display` on the bar (report-init-script.js 33-48) | drop: the product's tab script hides the bar itself |
| c-round3 71 `.timeline-warm-up[hidden]` | repeats c-extra 4 | drop |
| c-round5 18-20 chip `::before`/`::after` | cancels the August dot chips (CFULL_EXTRA 162-166, 179-181; c-extra 60-67) | drop both sides: never draw the dots |
| c-round5 90 `svg { background }` | the engine writes `background:#FFFFFF` in the svg's inline style | emitter: transparent background, paint the container (3.5) |
| c-round5 93, 96-99, 102-103, 105, 107, 109 (strokes) | the engine writes `style="stroke:#..."` inline, which only `!important` beats | needed as long as colours are remapped in CSS (3.5) |
| c-diagram-look 33-35, 37-38, 41-42, 44, 46, 49; c-round17 47, 50-51 | role rules must beat c-round5's `!important` stroke remaps on the same elements | drop once the colour map and the role rules are written together (role rules last, or remaps scoped off role-tagged elements) |
| c-round8 48 `.nav-filters` on phones | same specificity and later than its own `display: flex`; defensive | drop |
| c-round17 5 link `fill` | beats the colour remap and the `fill` attribute the product's link binder writes | product JS sets a class or `style.fill`, no `!important` (3.5) |
| c-round17 121 component database edge | engine inline `style` stroke | as c-round5 |

### 3.3 Doubled classes (16 rules)

c-diagram-look.css 33-35, 37, 39, 41-42, 44, 46, 48-49 and c-round17.css 47, 50-51: `.plantuml-browser svg
.k-card.k-card` (0,3,1) to beat c-round5's `:is(.plantuml-browser, .iflow-diagram) svg :is(path, polygon)[fill="..." i]`
(0,2,2). c-round17.css 58 `details.failure-result.failure-result > summary` beats c-extra 151 (0,1,2). c-round17.css
64 `.scenario-steps.scenario-steps > .step-collapsible` ties stylesheets.css 707
`.scenario-steps > .step-collapsible.step-background` (0,3,0) and wins by order. All go with a single native sheet.

### 3.4 `:is()` grouping (29 rules)

c-round5.css 90-114 (24), c-round7.css 99-101 (3), c-round17.css 43, 121 (2). Used to share each rule between
`.plantuml-browser` and `.iflow-diagram`; `:is()` takes its highest argument's specificity, which is part of why the
role rules needed doubling. Native: one class on every diagram host (for example `k-diagram`, emitted by C# and by the
popup script) and plain selectors.

### 3.5 Rules keyed on attribute values the emitters or the engine write (28 + 1 rules)

| Mock rule | Value matched | Written by | Native port |
|---|---|---|---|
| c-round2 7-10 `.run-ring circle[stroke=...]` | `#1daf26`, `#cc0000`, `#949494`, `#2e7bff` | `GeneratePieChartSvg`, ReportGenerator.cs 2718-2721 (text `fill="#333"` at 2742) | **emitter**: classes on the circles (or the run band's own ring markup), colour by token |
| c-round5 90 `svg` background | inline `background:#FFFFFF` | the engine | **emitter**: transparent diagram background, `.plantuml-browser { background: var(--dg-bg) }`; check what the pinned engine writes first |
| c-round5 91 `text[fill="#000000"]` | default text | engine default | keep the remap, or emit an explicit Kronikol text colour so an engine upgrade cannot move it |
| c-round5 92 `text[fill="#666666"/"#686868"]` | header-line ink and component edge labels | `NotePalette.HeaderInk` (PlantUml/NotePalette.cs 22, computed `#686868`); ComponentDiagramGenerator.cs 146-147 (`Color`/`FontColor #666666`) | keep, generated from those constants |
| c-round5 93-94 `#181818` strokes, arrowheads | lines | engine default theme | **emitter**: explicit line skinparams with a Kronikol constant; longer term the upstream HColorVar idea (CSS variables in the engine's output) removes the remap |
| c-round5 95 `#e2e2f0` | participant fill | engine default | as above |
| c-round5 96 `#feffdd` | note fill | `NotePalette.DefaultNoteFill` (NotePalette.cs 7) | keep; sequence notes are already re-coloured by role (`k-card`), the remap still serves activity and popup diagrams |
| c-round5 97 `#cfecf7` | event note | `NotePalette.EventNoteFill`; `AddEventStyling` (PlantUml/PlantUmlCreator.cs 1267-1279, the fill at 1273) | **emitter** can write round 17's violet for the light theme; `HeaderInk` is computed from this fill (NotePalette.cs 22), so it moves with it |
| c-round5 98-99 `#d4edda`, `#f8d7da` | assertion notes | `Track.PassColor`, `Track.FailColor` (Tracking/Track.cs 16-17), used at Ingestion/InteractionRecord.cs 471 | keep, generated from the constants |
| c-round5 100 `polygon[fill="#000000"]` | step bar hexagon | `StepBarPlantUml.LegacyPrefix` (PlantUml/StepBarPlantUml.cs 55, `#black`) and `AddMarkerNoteStyling` (PlantUmlCreator.cs 1237-1265, `BackgroundColor black` at 1257) | **emitter**: write the band form (`rnote` and `<b>`, see below) so the role colours apply; c-diagram-look.js 227 also finds bands by this black fill |
| c-round5 101-103 `rect #f0f4ff`, strokes `#666666`, `#cccccc` | activity diagrams | InternalFlow/InternalFlowRenderer.cs 20-22, 77-79 | keep, generated from the constants |
| c-round5 104-110, c-round7 99-101 `#438dd5`, `#e74c3c`, `#9b59b6`, `#08427b` | dependency colours and component boxes | `DependencyPalette.DefaultColors` (DependencyPalette.cs 45-55, used for arrow colours through `GetArrowColor`, PlantUmlCreator.cs 312, 368, 1176); ComponentDiagramGenerator.cs 115-147; ComponentDiagramDiffer.cs 96-108 | **emitter** for the light values: database `#0e7c86` (round 17 item 22) and the deeper box fills `#2b6cb0`, `#0b6c75`, `#8e44ad` (round 7). The map covers only the three defaults: cache `#F39C12`, storage `#2ECC71`, AI `#16A085`, user `#7D3C98`, unknown `#95A5A6` and any `ReportConfigurationOptions.DependencyColors` override keep their light colours in the dark theme |
| c-round17 121 `[stroke="#c0392b"]` | component database border | ComponentDiagramGenerator.cs 133 (`BorderColor #C0392B`) | **emitter**: `#09616a` |
| c-round17 5 `text[data-iflow-fill][text-decoration="underline"]` | the link-hover state | `bindIflowLinks`, Reports/plantuml-browser-render-script.js 1325-1411 (repaints to the engine's `#0000ff`) | **product JS**: toggle a class, or set `style.fill = 'var(--kron-accent)'` |
| c-round5 112-114, c-round17 43 `.note-toggle-icon rect/line/text/polygon` | note button colours | collapsible-notes-script.js 344-545 (presentation attributes, which any CSS rule beats) | **product JS**: stop writing colours; CSS by class |

Source-side changes c-diagram-look.js makes to every render (32-116), which belong in the C# emitters:
`skinparam defaultFontName system-ui`, `shadowing false`, `SequenceMessagePadding 4`, `sequenceMessageAlignment
center`, and a `<style> sequenceDiagram { participant {...} actor, database, ... {...} lifeLine {...} note {...} }`
block after `!pragma teoz` (PlantUmlCreator.cs 1207-1218 writes that header); `entity` participants drawn as
`participant` (CreateEntitiesPlantUml); `hnote across <<stepDelimiter>> #black:<color:white>` and the `<<stepBody>>`
form drawn as `rnote across ... <b>` (StepBarPlantUml.cs 55-56); trailing `\n` dropped from step bodies; `skinparam
defaultFontName system-ui` on component diagrams. The role pass itself (lifelines, participants, bands, cards, step
tables, strips stretched to full width, note paths rounded) has no emitter equivalent for Teoz output: it stays in the
page's render script unless the engine learns to emit classes.

Coverage gap to decide: the mock themes only `.plantuml-browser svg` and `.iflow-diagram svg`. Inline SVG from the
Server, Local and NodeJs renderings (`.plantuml-inline-svg`) gets no remap and no roles, PNG diagrams cannot be themed,
and a user `PlantUmlTheme` (`!theme`, PlantUmlCreator.cs 1205) moves every colour off the map.

### 3.6 Resets that only neutralise product rules (gone once the base sheet is rewritten)

`details { margin: 0 }` (c-extra 17, against stylesheets.css 25-28); `.filter-search { background: none; padding: 0;
border-radius: 0 }` (c-extra 39, against 1600-1607); `.filters { gap: 0 }` (c-extra 42, against 188-192);
`.truncate-lines-label { display: none }` (c-extra 52); `.rule { margin: 0; padding: 0; border-left: none }` (c-extra
112, against 884-889); `.step:not(.step-collapsible)::before { content: none }` (c-extra 135, against 692-696);
`.scenario` and `.duration-badge` resets (CFULL_EXTRA 196-201, against 40-48, 444-467); `.kron-c .param-test-table
tbody tr...` (c-extra 174-180, against 1376-1402); `.param-table-wrapper > .param-test-table` margins (c-round5 27,
c-round16 15, against 1353-1358); `.scenario-steps > summary { background-color: transparent }` (c-round5 124, against
663-669); `.status-badge.badge-*` (c-round5 126-129, against 1461-1464); `.filter-search #searchbar` (c-round5 171-172,
against the ID rule 202-208); `.scenario-steps > .step-collapsible.step-background` (c-round17 28, against 706-709);
`.sub-steps > .step-background` (c-round17 40, against 700-704); and the mock's own reversals: c-round9 42 undoes
c-round2 62-67 (segment to tabs), c-round16 33-34 undo c-extra 88 (divider), c-round3 35-40 and c-round5 18-20 undo the
August dot chips.

### 3.7 Dead rules and selectors

- **No element anywhere:** `.fcnt` (CFULL_EXTRA 177-178; c-extra 64, 71; c-round3 40; part of c-round7 28-29),
  `.note-font-select` (c-extra 191, 194; the product no longer emits it), `.timeline-dur` (c-round7 28, 50),
  `.history-verdict-faster` (c-round2 114; `HistoryVerdictKind` has no Faster), `.tag-mode-hdr` (CFULL_EXTRA 183;
  c-extra 70; c-round5 23; nothing adds it).
- **Element hidden or replaced by the mock's JS:** the header band (2 and 3 above), `.features-summary-details`,
  `.collapse-expand-all` (replaced by `.k-fold-group`, c-round17.js 87), `.timeline-toggle` (the old action row is
  retired), `.happy-path-filters` and its toggle and label (hidden, c-dom.r5.js 149), the August `.toolbar-row` strip
  (CFULL_EXTRA 185, superseded by c-round2 41), `.kron-preview-note`, `.toolbar-left-retired`.
- Measured: **62 rules (about 5.7 KB)** name only such elements in every selector of their list, and **98
  declarations (about 2.4 KB)** are overridden later by the identical selector in the same context.

### 3.8 Cascade collisions: product rules still applying under the mock (read from the cascade, not measured in a browser)

1. **Pending indicator.** collapsible-notes-styles.css 53-70 vs C_CSS.css 62-63. The product's
   `.details-radio-btn.details-pending::after` sets `width: 0.65em`, `top: 50%`, `margin-top`, a 2px round border and
   `opacity: 0`; C's sweep (same selector, later) sets only `content`, `position`, `left`, `right`, `bottom`, `height`,
   `background`, `transform` and `animation`. The product's `details-pending-show` animation that lifts the opacity is
   replaced by `kc-sweep`, so the sweep stays at opacity 0 (and, if it showed, would be 0.65em wide with a round
   border). The product's `padding-right: 1.5em` (54) also still applies.
2. **Diagnostics separators.** report-diagnostics-styles.css 22-29 `border-bottom: 1px solid rgb(240, 232, 215)`
   survives c-round2.css 118's `border-top`: two lines between items, one a fixed light tan in the dark theme.
3. **Diagnostic kinds.** `.report-diagnostic-kind-*` (report-diagnostics-styles.css 41-50) lose to `.kron-c
   .report-diagnostic-kind` (c-round2.css 120): every kind, a render failure included, is the amber pill.
4. **History verdict kinds.** `always-failing`, `behaviour-changed`, `reordered`, `unstable-shape`, `alternating`,
   `quarantined` (history-styles.css 23-50) lose to `.kron-c .history-verdict` (c-round2.css 111) and draw as the
   quiet pill; `always-failing` loses its fail colour.
5. **Search field geometry.** `#searchbar` (stylesheets.css 202-208, ID specificity) keeps `padding: 0.5em;
   border-radius: 0.5em` over CFULL_EXTRA.css 48's `padding: 8px 12px; border-radius: 8px`.
6. **Phone rules the mock inherits.** `.filter-row { flex-direction: column }` (stylesheets.css 1609-1612) still
   stacks the filter groups on phones; `.filtering-box-header { flex-direction: column }` (1716-1720) stacks the
   FILTERING line when the box is wider than the 430 px container threshold; at 480 px the chips keep the product's
   `min-height: 36px` and padding (1779-1784); `.endpoint` is a block on phones in the product (1691-1695) but
   CFULL_EXTRA.css 206 floats it at every width (the mock's report has no endpoint, so this was never seen).
7. **Breakpoint seam.** The mock's desktop rules use `min-width: 769px` (22 rules, plus two ranges starting there)
   and its phone rules `max-width: 768px`; at a fractional viewport (768.8 CSS px at 125 % zoom, the case stylesheets.css 1553-1556 documents) neither
   applies. The product uses `min-width: 768.02px`.

### 3.9 Measured browser workarounds to keep (and re-measure on the native markup)

c-round17.css 11-18 (`.details-radio` as `inline-block` with `font-size: 0`, Firefox baseline rounding) and 23-24
(sub-pixel padding 5.375/4.625 px and the `@supports (-moz-appearance: none)` circle shift); c-round6.css 4-20 (the 92
px label as a width, for Firefox's max-content); c-round5.css 5 (status row `nowrap`, Firefox); the optical centring
offsets (C_CSS 43 `-4.12px`, CFULL_EXTRA 163 `-3.65px`); c-round7.css 72 (16 px search font on phones, iOS zoom);
`'\25B6\FE0E'` (c-round9.css 63, c-round17.css 61; the round 11 emoji trap); c-contrast.css 76 (scroll margin rather
than scroll padding, for the reason given there); c-round8.css 39 (`min-height: 100vh` under the bar while the box
rides).

---

## 4. Recommended structure for the product

### 4.1 Files and order in the page

Today (ReportGenerator.cs 1421-1430, 1511, 1526-1533): one `<style>` holding stylesheets.css, then history, diagnostics
and warm-up sheets when drawn, then the context menu, inline SVG, collapsible notes and internal-flow sheets, then the
theme (the Specifications violet sheet or none); `CustomCss` in a second `<style>`. Proposed:

```
<head>
  meta tags
  <script> theme boot (localStorage 'kronikol.theme', else prefers-color-scheme) </script>   new, before any style
  <style>
    report-tokens.css               new: light :root, dark :root[data-theme="dark"],
                                    optionally the same under prefers-color-scheme for no-JS copies
    stylesheets.css                 rewritten in C: page, run band, filtering, nav, tabs, panels, tools line,
                                    features, scenarios, steps, parameters, failure, diagrams section, phone
    history-styles.css              rewritten to tokens, verdict kinds kept distinct; still only when drawn
    report-diagnostics-styles.css   rewritten, kinds kept distinct; only when drawn
    warm-up-styles.css              tokens (opacity 1 per round 13); only when drawn
    context-menu-styles.css         tokens
    inline-svg-styles.css           tokens, centring (round 17 item 19)
    collapsible-notes-styles.css    BrowserJs only: the C options-bar controls, pending state, note icons
    diagram-look-styles.css         new, BrowserJs (and inline SVG if adopted): --dg-* remaps and role rules
    internal-flow-popup-styles.css  tokens, popup tabs, flame palette
    theme                           Specifications: violet as token overrides
  </style>
  CustomCss
```

The collapsible-notes sheet is the right home for the options-bar controls because the product only emits
`.details-radio` and the toggles when `isPlantUmlBrowser` (ReportGenerator.cs 1848-1860, BuildScenarioDiagramToolbar
1076-1093). The tokens can live in a CSS file (simplest for Kronikol4J byte parity) or in a C# table emitted as CSS
(lets the dark block be written twice, for the attribute and for the media query, from one source).

**Specifications theme.** `Stylesheets.VioletThemeStyleSheet` (Constants/Stylesheets.cs 50-134, the default of
`HtmlSpecificationsCustomStyleSheet`) is about 100 lines of selector overrides in the old palette (violet `.feature`
backgrounds, solid violet active chips). Under C it would fight the new rules; as token overrides
(`--kron-accent`, `--kron-selected`, `--kron-selected-ink`, `--kron-selected-edge`, `--kron-hover` and their dark
values) it is about a tenth of the size, and is what c-tokens.css 2-3 says a theme should be.

### 4.2 What becomes C# markup and what stays runtime JS

**Emit from C#** (today built in the browser by c-layout.js and c-dom.js): the run band and CI zone; the title line
and theme button; the nav with tabs, panels and tab counts; the Features tools line (Full and Summary tabs, fold
groups, SORT, Scenario settings); the nav-row filter readout, Clear All and Filters button; the empty state; the
filtering rows (`.fltr-controls`, the HAPPY PATHS trio with the URL hash, export and Clear All functions moved to it
instead of a hidden chip, the Tags row from `data-labels`, Untagged and No dependencies chips, group Clear, readout,
More filters, the mobile filter line, `.wide-only`); the scenario bar split into `.diagram-toggle-tabs` and
`.diagram-options-bar` with Format and Toggle zones, Payloads wording, Events and Setup toggles and the format
optgroup; the timeline and component controls and filtered notes; the component header; the summary table's group
row; History and Diagnostics as cards without a disclosure; the history hatch pattern (HistoryHtml.cs); the token diff
(ErrorDiffParser).

**Stays runtime state (JS toggles a class or a variable):** `pair-fits`, `trio-fits`, `zone-adjacent` (now only the
scenario bar), `ci-below`, `nav-compact`, `nav-minimal`, `is-pinned`, `k-floating`, `riding`, `has-filters`,
`rows-folded`, `help-open`, `is-filtered`, `is-active`, `has-on`, `on-features`, `features-table-mode`, `tools-open`,
`not-seq`, `diagrams-not-seq`, `gear-in-summary`, `gear-open`, `body.k-scenario-settings`, `--kron-nav-h`, `data-theme`;
and the SVG role pass (`k-*`), best moved into plantuml-browser-render-script.js after each render.

Product scripts that look up the restructured classes and must keep working: collapsible-notes-script.js
(`.toolbar-right`), diagram-toggle-layout-script.js, report-init-script.js and toggle-script.js (`.diagram-toggle`),
report-toggle-happy-paths-function.js, report-url-hash-function.js and report-export-function.js
(`.happy-path-toggle`).

### 4.3 Product rules that die and rules that survive

- **stylesheets.css, replaced by C:** 30-48, 54-68, 83-100, 102-172, 174-311 (except `.filter-search` position,
  `#searchbar:disabled`, the deep chip's position and animation), 313-357, 375-552, 559-593, 607-648, 650-769, 834-895,
  921-951, 957-1009, 1133-1171, 1262-1350 (timeline structure kept, colours replaced), 1353-1464, 1482-1494,
  1497-1551, 1557-1560, most of 1563-1815, 1823-1858.
- **stylesheets.css, survives (no C counterpart; tokenise):** 9-19 (.raw-plantuml), 52 (tables keep words), 60-62,
  70-81, 359-373 and 953-955 (the `*-hidden` filter mechanics), 771-823 (attachment images, lightbox), 825-832 (doc
  string), 897-919 (examples table), 1011-1131 (step tables, combined table, tree, expand, highlight, table ref),
  1173-1259 (failure clusters, background calls), 1415-1433 (example block name, counts, description), 1467-1479
  (detail panels, identical-diagram badge), 1817-1822 (no interactions). None of these was restyled or dark-checked by
  the mock; several (failure clusters, background calls, examples table, doc string, endpoint, category filters) are
  not present in the mock's source report at all.
- **collapsible-notes-styles.css:** 1-31 and 35-73 replaced by C_CSS's controls; 32 and 34 survive.
- **context-menu-styles.css:** colours to tokens; geometry and the phone sheet (66-113) survive.
- **internal-flow-popup-styles.css:** 1-52, 66-116 replaced; 53-65 and 117-143 survive, tokenised.
- **history-styles.css / report-diagnostics-styles.css:** sections and lists replaced; the per-kind pills survive in
  tokens (3.8 items 3-4).
- **inline-svg-styles.css, warm-up-styles.css:** survive with token colours (placeholder `#999`, selection ring, slider
  accent, the warm-up hatch's white gradient checked in dark).

### 4.4 Size estimate

| | Raw | Without comments | Rules |
|---|---|---|---|
| Mock `c-skin.css` today | 146 KB (148,039 bytes) | 103.6 KB | 874 |
| Product report sheets today (8 files, labs excluded) | 74.4 KB | 68.7 KB (47 KB with whitespace collapsed) | |
| Native C, estimate | | 70-85 KB | about 650-750 |

Basis: from the mock's 103.6 KB take out the 62 dead rules (5.7 KB), the 98 self-overridden declarations (2.4 KB), the
85 `.kron-c` prefixes, the product-neutralising resets (3.6), the August rules later rounds superseded (dot chips,
segmented Full/Summary, the August toolbar strip) and the duplicated focus rules, and fold the type scale into each
surface: about 60-75 KB for the C design itself. Add back the product rules that survive (4.3) once tokenised, about
8-10 KB. Net: about 10-25 % more CSS than today, while about 66 KB of layout JS (c-layout.js 31 KB, c-dom.r5.js 35 KB)
becomes C# markup. A report's diagram data dwarfs both.

### 4.5 Consequences to plan for

- **Kronikol4J byte parity.** stylesheets.css is shared byte for byte with the Java port (Constants/Stylesheets.cs
  27-30); the rewrite, every new sheet and every emitter colour or source change (fonts, `rnote` bands, `entity` to
  `participant`, database colour) go in the divergence ledger, and every diagram golden pin is re-captured.
- **Public surface.** `Stylesheets.HtmlReportStyleSheet` and `Stylesheets.VioletThemeStyleSheet` are public; the
  violet constant's value changes, and `HtmlSpecificationsCustomStyleSheet`, `InternalFlowPopupCustomStyleSheet` and
  `CustomCss` written against today's selectors or colours stop applying. v5 is the planned major; the wiki should
  document theming through `--kron-*` and `--dg-*` tokens.
- **Labs page.** LabsReportGenerator.cs 44-47 composes stylesheets.css with the history, diagnostics and labs sheets;
  it needs the token sheet and the boot script too.
- **Other renderings.** Decide the dark theme for inline SVG, PNG and user-themed diagrams (3.5, coverage gap).
- **No-JS copies.** Without the boot script there is no dark theme (1.1); check that Export Filtered HTML carries the
  boot script and the token sheet (not verified here).
