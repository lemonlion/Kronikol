# DIAGRAM_COLOURS_PLAN harness

The evidence behind `DIAGRAM_COLOURS_PLAN.md` §1 (R1 to R4 and R12 to R16, captured 2026-09-22; R19 to R28, 2026-09-25) on the
pinned engine (`core-1.2026.8beta1-0e4f452.js` in the bench, the CDN tag `v1.2026.8beta1-0e4f452` in
the worker and Node runs), headless Chromium through the E2E project's Playwright driver (1.59.1),
node 25.9.0. Nothing here ships.

## First pass: the bench page (a real `document`, the engine on the main thread)

- `render-probe.js <file.puml>`: renders one source through the engine in a page (the bench's shell
  from `tools/render-bench/render-svg.js`), answers **404 for `/themes.js`**, prints every non-log
  console message, and writes `<file>.blocked.svg` beside the input. Run it from `tools/render-bench/`
  (it needs the engine and `viz-global.js` there, and the E2E project built once for its driver).
- `probe.puml`: one internal-flow arrow label, a note with `<color:gray>`, `<color:#686868>`,
  `<color:blue>` and `<color:lightgray>` lines, and a `[[https://…]]` hyperlink.
- `probe-themed.puml`: the same with `!theme cerulean` after `@startuml`.
- `probe.svg`, `probe-themed-no-themes-js.svg`: their renders with `themes.js` blocked. Strip
  processing instructions and comments and they are byte-identical (R1, in a page). Both show links as
  `<text fill="#0000FF">` with no `<a>` (R3), note fill `#FEFFDD`, `gray` as `#808080`, `#686868`
  honoured (R2).
- `probe-themed-with-themes-js.svg`: the themed source rendered with `themes.js` served (it sits in
  `tools/render-bench/` from the upstream PR work): the theme applies.
- `contrast.py`: WCAG 2.1 ratios for today's grey, `lightgray`, and candidate inks on every Kronikol
  fill; the table in plan §3.2.

The console line the blocked themed render logs in a page, verbatim:

    PlantUML: themes.js could not be loaded, so '!theme cerulean' was ignored. Serve themes.js next to the page or register globalThis.PLANTUML_THEMES (importing themes.js as a module does this).

**This page is not the report's situation.** The first pass took it for one; the second pass below
is why plan §5 changed.

## Second pass: the shipped path (the report's own scripts, a Blob Web Worker, the Node renderer)

- `worker-probe.js`: builds a page the way `TestPageGenerator.GenerateBrowserJsSequenceDiagramPage`
  does (the shipped `plantuml-browser-render-script.js` with `DiagramContextMenu`'s substitutions,
  `plantuml-worker-host.js` inlined, the CDN base read from `TrackingDefaults.cs`), opens it from
  `file://` with a truthy `window.__iflowSegments['iflow-abc123']`, and reads what is painted. Three
  diagrams: `d-plain` (`probe.puml`), `d-themed` (`probe-themed.puml`), `d-after` (a trivial one queued
  behind the themed one). Writes `worker-<id>.svg`; the page it builds (`worker-probe.page.html`, now written to the OS temp directory) is
  regenerated each run and not kept. `FAILFAST=1` patches the inlined host with the one hook of plan
  §5.3 and writes `worker-<id>.failfast.svg` instead. Run from anywhere; needs the E2E project built
  once and network access to the CDN.
- Stock run: `d-plain` rendered at +978 ms, `d-themed` **not rendered after 200 s** (element empty,
  `data-rendered` unset, telemetry `errors: 1`), `d-after` rendered on a second worker; zero console
  messages. Fills after bind, after `mouseenter` on `GET`, after `mouseleave`: plan F14. A second stock
  run with `d-include` added: the include **not rendered after 70 s** either, element empty, telemetry
  `errors: 2` (one per hung diagram), `workers: 3`.
- `FAILFAST=1` run: all four rendered by +2.8 s on one worker (`d-include`, `include-probe.puml`,
  added after the stock run above: an SVG of `viewBox 0 0 413 117`, the engine's own picture),
  `errors: 0`, `worker-themed.failfast.svg` byte-identical (processing instructions stripped) to
  `worker-plain.failfast.svg`, which is identical to the stock run's `worker-plain.svg`; the page's
  console received the engine's warning from the worker.
- `worker-plain.svg`, `worker-after.svg`, `worker-plain.failfast.svg`, `worker-themed.failfast.svg`,
  `worker-after.failfast.svg`, `worker-include.failfast.svg`: the painted SVGs of the two runs.
- `worker-console-probe.js`: a Blob worker calling `console.warn`, `error` and `log`; all three arrive
  on `page.on('console')` and `context.on('console')` (R16).
- Node, the shipped `plantuml-render.js` from the library's cache directory
  (`%LOCALAPPDATA%\Kronikol\plantuml-js\v1.2026.8beta1-0e4f452\`, plan §11 has the command line):
  `probe.puml` 6,216 B in 452 ms; `probe-themed.puml` exit 1, no SVG,
  `ERROR: Timed out waiting for SVG render (20000ms)`, 25 s; `!theme plain` the same;
  `include-probe.puml` (`!include <C4/C4_Context>`) the same timeout. With the hook below on a copy:
  themed 6,216 B in 389 ms, identical to plain, the warning on stderr; the include a 1,331 B SVG in
  253 ms.
- `include-probe.puml`: the stdlib include source.

The Node hook, applied to a copy of `plantuml-render.js` (the `head:` line of `mockDocument`):

```js
head: (function () { var h = new MockElement('head'); var orig = h.appendChild.bind(h); h.appendChild = function (c) { var r = orig(c); if (c && c.tagName === 'SCRIPT' && typeof c.onerror === 'function') setTimeout(function () { c.onerror(new Error('no script loading in this host')); }, 0); return r; }; return h; })(),
```

The Node mock upper-cases `tagName`; the worker host keeps it as given, so its hook (in
`worker-probe.js`, `hostSource()`) tests `'script'`.

**Why the two passes disagree.** The engine never fetches `themes.js` itself (the build has no
`import()`, `import.meta`, `importScripts` or `currentScript`). With no `globalThis.PLANTUML_THEMES` it
appends `<script src="themes.js">` to `document.head` and waits for `onload` or `onerror`. The bench
page's real document answered the 404 with `onerror`; the worker host's and the Node script's mock
documents store the element and answer nothing. The CDN tag ships `themes.js` (326,345 B) beside the
engine; nothing in Kronikol loads it, by `THEME_PLAN` §2.1's design.

## Third pass (2026-09-25): everything that reaches the loader

Plan §1 has the evidence as R19 to R28 and the findings as F15 to F24. Everything was run on the same pin
and driver as above, with node 25.9.0 and the .NET 10 SDK's `dotnet fsi`.

- **`emit-payload-sources.fsx`** writes `payload-*.puml` through Kronikol's own code: `PlantUmlCreator`
  on the built `tests/Kronikol.Tests/bin/Debug/net10.0/Kronikol.dll`, and `StepBarPlantUml.Build` by
  reflection, as `StepCollector` calls it. Run it with `dotnet fsi` from the repository root.
  - `payload-control` is a plain call.
  - `payload-rust` is a 400 whose body quotes `Vec<&str>`.
  - `payload-emoji` is a request body carrying `<:rocket:>`.
  - `payload-stepbar-lightbdd` is the bar for `Given I have data [inputs: "<$inputs>"]` with a table.
  - `payload-stepbar-rust` is a bar quoting `Vec<&str>`.
  - `payload-amp` has `?page=1&size=10` in the path and `fish & chips` in the body.
  - `payload-xml` has XML bodies.

  The emitter writes `<&`, `<:` and `<$` as they are, and the step label raw.
- **`loader-probe.js`** is `worker-probe.js`'s page, the shipped render script and worker host from the
  CDN in a Blob worker, with a choice of set. The page is written to the OS temp directory.
  - **`SET=payload`** (the default). Stock, `d-rust` and `d-emoji` are never drawn in 200 s, with
    `errors: 2` and no console output. Under `FAILFAST=1` both hold the engine's text,
    `java.lang.RuntimeException: Failed to load openiconic.js` (`emoji.js`). In both runs the `~<`
    variants draw, painting `Vec<&str>,` and `<:rocket:>`. The SVGs are `loader-payload-*.svg`.
  - **`SET=poison`** puts `d-rust` first. Stock: nothing drawn in 400 s, `workers: 1`, `renders: 0`,
    `errors: 2`. Under `FAILFAST=1`: all settled by +1.2 s.
  - **`SET=allthemed`** is six themed diagrams. Stock: none drawn in 400 s, `workers: 1`, `renders: 0`,
    `errors: 2`.
  - **`SET=corpus`** is the 22 diagrams `corpus-hash.js` selects. It prints hashes, not files. Stock and
    `FAILFAST=1` are identical, 22 of 22.
- **`node-svg-probe.js`** renders `payload-control`, `payload-amp` and `payload-xml` with the Node
  renderer (`RENDERER=` picks a copy), then in Chromium parses each SVG as `image/svg+xml`, loads it as
  a data-URI `<img>`, and inlines it.
  - Stock: `payload-amp` gets a parsererror (`EntityRef: expecting ';'`) and the image fails with
    `naturalWidth` 0. `payload-xml`'s body text sits in `<order>` and `<result>` elements, painted width
    0.
  - With the escaping copy: all three are well-formed, all load, and all paint.
- **Node runs**, with outputs in the session scratchpad, not kept here:
  - `payload-rust` and `payload-emoji`: stock exits 1 after 25.2 s and 24.5 s; hooked they fail in
    0.4 s with the engine's text.
  - The step-bar sources: the LightBDD bar paints `Given I have data [inputs: "` then `"]`, and the
    `Vec<&str>` bar times out (23.6 s).
  - `~<` in a styled bar, a coloured bar, an assertion note and a note paints all three prefixes
    literally.
  - A `--batch` of six (payload first): stock, every line `Timed out`, 145.8 s. Hooked, 684 ms: the two
    payload lines fail, and the other four draw.
  - The corpus as a batch: stock, hooked and hooked-with-escaping are byte-identical, 22 of 22.
- **The two earlier pins** were fetched from jsDelivr and rendered by today's `plantuml-render.js`:
  - `v1.2026.3beta6-patched` (3.0.45) has script-tag loaders for `emoji.js` and stdlib only.
  - `v1.2026.6-patched` (3.0.50) adds `openiconic.js`. Neither has a theme loader.
  - On both, `probe-themed.puml` is byte-identical to `probe.puml`, and `include-probe.puml` times out
    (27.2 s and 28.0 s).
  - On `3beta6`, `payload-rust` draws with `<&str>` dropped, and `payload-emoji` times out.

S5's escaping prototype, applied to a copy of `plantuml-render.js` (on top of the hook), in
`serializeElement`:

```js
function escXml(v) { return String(v).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;"); }
function escXmlAttr(v) { return escXml(v).replace(/"/g, "&quot;"); }
//   text node:        return escXml(el.textContent || el.data || '');
//   attribute value:  attrs += ' ' + k + '="' + escXmlAttr(el._attributes[k]) + '"';
//   childless text:   var text = (...) ? escXml(el.textContent || '') : '';
```

It is XML escaping only. The worker host's `escText` also writes `&nbsp;`, which is not an XML entity.

## I3 (2026-09-25): old and new header forms in one merged report

Plan §1 R29 and R30, findings F25 and F26, and the I3 row of §10.3. Run on a candidate build of S1, in a
throwaway worktree at `96769c64` (3.29.3 plus a plan-log line). The worktree was removed afterwards;
nothing under `src/` in the main checkout was touched.

- **`s1-candidate.patch`** is S1 as §3.2 and §3.3 write it: `NotePalette` and `WcagContrast`, the two
  emitter sites, and the twelve script sites, each file's `NOTE_HEADER_TAG` declared at the top of its
  IIFE. Its computed `HeaderInk` came out `#686868`.
- **`emit-merge-inputs.fsx`** writes a mergeable `TestRunReport.json` with a given `Kronikol.dll` through
  the public pipeline (`RequestResponseLogger.Log`, then `ReportGenerator.CreateStandardReportsWithDiagrams`
  with `GenerateMergeableData`). It writes three scenarios:
  - a POST with headers and JSON bodies;
  - a GET whose request note is headers only;
  - a GET with a 2,300-character query, which gets the `[Full path]` block.

  A third argument turns an option off: `plain` for arrow colours, `noflow` for internal-flow tracking.
  Run it as `dotnet fsi -r:<build>/Kronikol.dll emit-merge-inputs.fsx <label> <out dir> [plain|noflow]`.
- **The inputs.** Today's build writes 11 `color:gray` tags per report and the candidate 11
  `color:#686868`, for each of the default, `plain` and `noflow` settings. The two 3.1.0 fixtures in
  `tests/Kronikol.Tests/TestData/Reports/` add 17 old-form diagrams.
- **The merges.** Each build's own `kronikol merge` (`dotnet <build>/Kronikol.Tool.dll merge … -o …`)
  produced a page. The candidate's page is the check; today's is the control, which shows the probe can
  fail.
- **`merge-probe.js <merged TestRunReport.html> [label]`** drives every scenario diagram in Chromium, with
  clipboard permissions and `dispatchEvent` for the SVG. It prints one row per diagram, with these columns:
  - `ink` and `keys`: the painted header fill and header keys;
  - `yaml`: per note, whether the YAML button is offered;
  - `copy`: Copy box text, whether a tag or header lines reach the clipboard;
  - `payload`: Copy all caller request payloads;
  - `yamlOn`: YAML switched on;
  - `hide` and `yamlHidden`: hide-headers, and the YAML buttons while the headers are hidden;
  - `collapse` and `tip`: the first note collapsed, and its tooltip.

  Then one `TWIN` line per shape, comparing the old-form and new-form scenario.
- **The results** are in `i3-candidate.txt`, `i3-control.txt`, `i3-candidate-noflow.txt` and
  `i3-control-noflow.txt`:
  - On the candidate's pages every twin agrees, nine pairs over the three settings, with no page errors.
    The new form paints `#686868` and the old `#808080`.
  - On the control pages the new form fails every header behaviour:
    - no YAML button on any note;
    - the tag in Copy box text and in the collapsed note's tooltip;
    - hide-headers leaves every header painted;
    - the collapsed preview carries the headers;
    - with plain arrows, the payload copy carries the tag, and a headers-only request is offered as a
      payload.
  - The 17 fixture diagrams' rows are identical on both pages.
- **Found on the way (F25).** The long-path GET is not drawn under the default settings in either form: the
  element holds `java.lang.RuntimeException: (JavaScript) RangeError: Maximum call stack size exceeded`.
  `[Full path]` was therefore observed only with `noflow`, where it paints in each form's ink and hides
  with the headers.
  - `payload-longpath.puml` is today's emitter output for that GET.
  - `loader-probe.js` gains `SET=statement`: the capped statement cut to each length, with and without the
    link, through the shipped worker.
  - `statement-node.js` writes the same twelve sources as a Node batch.
  - `statement-worker.txt` is the table: with the link, the worker fails at each tested length from 1,200 to
    1,700 characters and at 1,900 and 2,000, and draws all three without it. Node draws all twelve.
- **Found on the way (F26).** "Copy all caller request payloads" appeared only on the `plain` pages
  (`payload` is `none` on the 21 default-arrow diagrams that drew; the other two are F25's). `extractCallerPayloads` matches `caller -> `, and the
  default arrow is `caller -[#…]>`.

## I3 again on the release build (3.30.0, 2026-09-25)

Plan §8 step 4 and §12.2. The I3 run above, with the 3.30.0 build in place of the candidate. The old form
was written by the same 3.29.3 build (`b74c8ddc`) as before, and the new form and the merge by 3.30.0. This
time there is one page per setting (`default`, `plain`, `noflow`), each with the two 3.1.0 fixtures.

- `i3-release.txt`, `i3-release-plain.txt` and `i3-release-noflow.txt` are `merge-probe.js`'s output.
- Every twin agrees on all three pages, nine pairs, with no page errors. The new form paints `#686868` and the
  old `#808080`. With `noflow`, `[Full path]` paints in each form's ink and hides with the headers.
- Against the candidate's rows, every fixture row is identical except `payload`, which moved from `none` to
  `-/-`. The candidate predates 3.29.6's F26 fix, so the item was never offered with default arrows; it is
  offered now, and copies neither a tag nor a header line.
- F25 is unchanged: with internal-flow tracking on, the long-path GET is not drawn in either form.
