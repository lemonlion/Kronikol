# Browser rendering off the main thread — implementation plan

Status: **implemented in 3.0.45 (2026-08-22) — Phases 0–6**; Phase 7 (engine upgrade) remains an optional
follow-up. Production code: `src/Kronikol/Reports/plantuml-worker-host.js`, the shim in
`src/Kronikol/Reports/plantuml-browser-render-script.js`, prefetch hooks in `collapsible-notes-script.js`,
`ReportConfigurationOptions.BrowserRenderWorkers/BrowserRenderCacheMegabytes/BrowserFragmentMaxHeight`,
`kronikol ingest --browser-render-workers`, the Node renderer's batch mode + V8 code cache. Tests:
`tests/Kronikol.Tests/Reports/PlantUmlWorkerHostTests.cs`, `DiagramContextMenuTests`,
`tests/Kronikol.Tests.EndToEnd/BrowserRenderWorkerTests.cs` (+ `LargeReportFixture`), `NodeJsPlantUmlRendererTests`.
Originally written 2026-08-22 from a measured investigation; everything quantitative below was
measured in headless Chromium on a real report (see "Key results"). This document is meant to be picked up cold: it
explains how browser rendering works today, what is slow and why, what the target design is, and a phased,
test-first work breakdown with acceptance criteria. Prototype code that produced the numbers lives in
`tools/render-bench/` (harness + worker host); it is reference material, not production code.

---

## 1. Background

### 1.1 How `PlantUmlRendering.BrowserJs` works today

`ReportGenerator` (`src/Kronikol/Reports/ReportGenerator.cs` ~line 561) embeds three scripts into `TestRunReport.html`
when `PlantUmlRendering == BrowserJs`, all loaded from embedded resources through
`DiagramContextMenu` (`src/Kronikol/Reports/DiagramContextMenu.cs`):

| resource (`src/Kronikol/Reports/`) | role |
|---|---|
| `plantuml-browser-render-script.js` | loads the engine, decodes each diagram's source, splits it into height-bounded fragments, renders fragments **one at a time on the main thread** through a FIFO queue (`processQueue`), runs post-render hooks. `__PLANTUML_CDN_BASE__` is replaced with `TrackingDefaults.PlantUmlJsCdnBase`. |
| `collapsible-notes-script.js` | note Collapse/Truncate/Expand, Assertions/Steps/Databases/Headers toggles. Each toggle rebuilds the diagram source, re-splits it and **re-renders the changed fragments sequentially** (`setNoteState` → `renderNextFrag`, and `processRenderQueue` → `renderNextFragment`). Has its own `_svgCache` keyed by fragment source, filled only by this path. |
| `internal-flow-popup-script.js` | renders activity diagrams in a popup via `window.plantuml.render(lines, el.id)`. |

The engine is the TeaVM-compiled PlantUML (`plantuml.js`, 7.1 MB, plus `viz-global.js`, 1.4 MB Graphviz/WASM) served
from the user's own jsDelivr fork `lemonlion/plantuml-js-plantuml_limit_size_98304@v1.2026.3beta6-patched`
(`TrackingDefaults.PlantUmlJsCdnBase`, `src/Kronikol/Constants/TrackingDefaults.cs`). The fork is upstream
1.2026.3beta6 with the two `4096.0` size-limit compares patched to `98304.0`. Both files are classic scripts:
`<script defer src=…/viz-global.js>` + `<script defer src=…/plantuml.js>`; the page then calls `plantumlLoad()` and
`window.plantuml.render(lines, targetElementId)`. Rendering is asynchronous: the engine later inserts an `<svg>` into
the target element; every caller detects completion with a `MutationObserver` on the target (childList/subtree).

Diagram sources are in the `<script id="puml-data" type="application/json">` block (gzip+base64 per diagram id),
decoded with `DecompressionStream`. Elements: `div.plantuml-browser#puml-N`; fragments are child
`div.puml-fragment#puml-N-frag-K`; completion is marked with `data-rendered="1"`. Rendering starts for the first
scenario immediately and for other diagrams when they scroll into view (`IntersectionObserver`, 200 px margin);
`window._renderDiagramsInContainer(container)` forces a container.

The splitter (`splitWithChunkedNotes`) cuts a diagram at an estimated `_maxDiagramHeight = 12000` px (estimates:
45 px per arrow, 18 px per note line), closes/re-opens open `loop/alt/…` blocks at the cut, and chunks single notes
over `_maxNoteChars = 15000` chars. Each fragment is a complete PlantUML diagram with its own participant header.

Reports are opened from **`file://`** (the E2E tests also use local HTML files). This matters for Workers (see §4.5).

### 1.2 The Node renderer (`PlantUmlRendering.NodeJs`)

`src/Kronikol/PlantUml/NodeJsPlantUmlRenderer.cs` downloads `viz-global.js` + `plantuml.js` from the same CDN into
`%LOCALAPPDATA%/Kronikol/plantuml-js/`, extracts the embedded `src/Kronikol/PlantUml/plantuml-render.js` (a DOM
polyfill + driver) and **spawns one `node` process per diagram** (`DefaultDiagramsFetcher.GetNodeJsRenderedDiagrams`
renders sequentially). Only relevant to Phase 6.

### 1.3 The problem, as reported and as measured

"Large amounts of large diagrams must render as fast as possible on load; very big diagrams freeze the page; the
diagrams are interactive but their reload time makes them feel unresponsive."

Measured on `C:/Code/work/sidekick-intelligence-e2e/.logs/kronikol/TestRunReport.html` (1.0 MB, 83 scenarios,
20 diagrams — 14 of them 200–540 KB of PlantUML, 6.5k–13k lines, almost all note text (JSON bodies), 40–95 arrows
each — 36 fragments at 12,000 px):

- The engine alone (7 MB) keeps the page non-interactive for 1.7–5.6 s after load.
- Force-rendering all 20 diagrams takes 28–46 s, during which the main thread is blocked 22–39 s in long tasks
  (worst single task 1.7–4.8 s) — the "freeze".
- One note toggle on the biggest diagram takes 3.9–7.1 s, ~3–4 s of it blocking — the "unresponsive" feel.

---

## 2. Key results (what the fix is worth)

Headless Chromium (Playwright driver from `tests/Kronikol.Tests.EndToEnd/bin/…/.playwright`), report served over a
local http server with the CDN URLs rewritten to local copies of the exact engine builds; variants applied as string
patches to the inline scripts; long tasks via `PerformanceObserver('longtask')`; completion via `data-rendered`.
Machine was under other load, so the baseline varies by ±30% between runs; the optimised variants stay in a narrow band.

"ready" = `body.plantuml-ready`; "all" = time to force-render every diagram; "blocked" = sum of long-task time over
50 ms in that phase; "toggle" = click on a `.note-toggle-icon` of the largest rendered diagram until its new SVG shows.
Engine *old* = current fork build; *esm6* = npm `@plantuml/core` 1.2026.6 (ESM, MIT) with the same 4096→98304 patch.

| variant | ready | first diagram | all 20 | blocked | worst task | toggle | toggle blocked |
|---|---|---|---|---|---|---|---|
| **baseline** (4 runs) | 1.7–5.6 s | ≈ready | **27.7–46.0 s** | **22–39 s** | 0.9–4.8 s | **3.9–7.1 s** | 2.3–4.4 s |
| 1 worker · old | 0.15 s | 2.8 s | 21.7 s | 0.2 s | 96 ms | 3.4 s | 0.3 s |
| 4 workers · old | 0.14 s | 2.7 s | 17.5 s | 1.9 s | 192 ms | 3.2 s | 0.5 s |
| 8 workers · old | 0.09 s | 2.5 s | 18.7 s | 3.2 s | 278 ms | 3.8 s | 0.4 s |
| **4 workers · old · cache/prefetch · 12,000 px** (2 runs) | **0.08 s** | 1.5–1.7 s | **8.5–9.2 s** | **0.1–0.2 s** | 71–76 ms | **0.88–0.93 s** | 0.3 s |
| 4 workers · esm6 · cache/prefetch · 12,000 px (2 runs) | 0.08–0.11 s | 1.2–1.7 s | 6.0–9.0 s | 0.4–1.2 s | 100–160 ms | 0.80–0.83 s | 0.3 s |
| 4 workers · esm6 · cache/prefetch · 4,000 px (3 runs) | 0.09–0.10 s | 1.3–1.6 s | 5.9–10.1 s | 0.0–0.2 s | 63–97 ms | 0.50–0.54 s | 0.3 s |
| file:// page · 4 workers · old · cache · 4,000 px · engine fetched from the real jsDelivr fork | 0.09 s | 1.8 s | 10.2 s | 0.0 s | 53 ms | 0.62 s | 0.3 s |
| **fixture** (`LargeReportFixture`, 6 diagrams, file://, xUnit E2E run) · baseline main thread | 4.1 s | — | 13.2 s | 11.4 s | 452 ms | 2.6 s | 2.7 s |
| **fixture** · shipped implementation (4 workers · cache · prefetch · 12,000 px) | 0.44 s | 1.0 s (first worker ready) | 4.4 s | 0.0 s | 0 ms | 0.57 s | 0.27 s (worst 176 ms) |

Other measured facts used in the design:

- Worker output is identical to main-thread output (same width/height/viewBox, element and text counts on four real
  diagrams and a 50-arrow synthetic one); a worker render is ~15% faster than the same render on a small page and far
  faster than on the real page (every `getBBox` reflow and live-DOM build on a 1 MB document is expensive).
- A note toggle re-rendered 13 fragments of which 12 were byte-identical to what was on screen — the existing
  `_svgCache` is never filled by the initial render. Caching by source string and prefetching in parallel cut the
  toggle from 2.4 s to 0.5 s and total render from 8.8 s to 5.9 s in the same configuration (identical fragments
  across diagrams also dedupe).
- No gain from: 8 workers vs 4, lazy Graphviz loading, `Error.stackTraceLimit = 0`, patching the engine's DOM glue
  (text measurement is ~10% of a render and cached per font+text).
- Engine builds: `@plantuml/core` 1.2026.6 is 15–25% faster and emits ~35% smaller SVG but costs 1–5 s of module
  compile in the worker before the first diagram; the gh-pages js-plantuml 1.2026.7beta12 is **4–5× slower** (newer
  TeaVM captures a JS stack for every `new Throwable()`, and PlantUML's `real/RealMax` layout code allocates one per
  object and per loop iteration) — do not adopt it.
- Node renderer: spawn-per-diagram costs 0.8–1.4 s per diagram (node start 0.1 s + engine compile 0.16 s + engine
  top-level 0.25 s + WASM 0.03 s + cold JIT ~0.25 s); one warm process renders the same five diagrams in 1.9 s vs
  5.1 s; `vm.Script({cachedData})` cuts the compile from 161 ms to 1 ms (96 KB cache file).

Decision taken with the user: **keep 12,000 px fragments and the current engine build**; do workers + cache first.
Fragment height and the engine upgrade become optional follow-ups.

---

## 3. Goals and non-goals

Goals (acceptance, measured on the fixture in §6.1 and spot-checked on the real report above):
1. Page interactive (`body.plantuml-ready`) within 300 ms of load regardless of engine size.
2. No main-thread long task over 200 ms caused by diagram rendering; typical worst task under 100 ms.
3. Full render of a 20-large-diagram report at least 3× faster than today (target ≤ 10 s on the real report).
4. Note/assertion/step toggles on a large diagram complete in under 1 s and never block input.
5. Output identical to today's main-thread rendering (same SVG geometry; post-render hooks unchanged).
6. Works from `file://` and from `http(s)://`; works offline-with-cached-engine no worse than today; degrades to the
   current main-thread path when Workers/fetch are unavailable.

Non-goals: changing PlantUML semantics, the splitter's output, the engine fork, or the Server/Local/IKVM paths.

---

## 4. Target design

### 4.1 Worker host (`plantuml-worker-host.js`, new embedded resource)

A self-contained classic-worker script that hosts the engine and implements just enough DOM for it. Reference
implementation: `tools/render-bench/puml-worker.js` (~230 lines, working). The engine's whole DOM surface is:
`document.getElementById(target)`, `createElement`/`createElementNS`, `setAttribute(NS)`, `appendChild`/`removeChild`,
`textContent`, `document.body.appendChild/removeChild` + `getBBox()` (text measurement), `createElement('canvas')
.getContext('2d').measureText` (text measurement), `DOMParser` + `document.importNode` (embedded SVG), `XMLSerializer`,
`document.head.appendChild(script)` (stdlib loading — never used by Kronikol diagrams), `document.baseURI` (viz).

Host responsibilities:
- Mock `document`, `window = self`, `HTMLElement`/`SVGElement`/`Element`/`Node`, `DOMParser`, `XMLSerializer`.
  `MockElement` keeps attributes in insertion order, child nodes (elements, text nodes, processing instructions),
  `textContent`/`innerHTML` get/set, `cloneNode`, `getBBox() → {0,0,0,0}` (the engine's `StringBounder` then takes
  its own canvas fallback for heights — this is what makes output match), `getContext('2d') → one shared
  `OffscreenCanvas` context` (main-thread-identical `measureText`).
- `document.baseURI` must be an `http(s)` URL (viz-global does `new URL("viz-global.js", document.baseURI)`; a
  `blob:` base throws). Use `self.location.href` when it is http(s), else a constant like `https://kronikol.invalid/`.
- Serializer: XML-escape text/attributes (`& < > "` and NBSP), non-self-closing tags (what `innerHTML` produces),
  emit PIs as `<?target data?>`, pass `_raw` through for embedded SVG.
- Protocol: in `{type:'init', …}` then `{type:'render', seq, id, lines}`; out `{type:'ready'}`,
  `{type:'done', seq, id, svg, ms}`, `{type:'error', seq, id, message}`, `{type:'fatal', message}`. One render at a
  time per worker (TeaVM global state); queue inside the worker. Completion: the engine appends the finished `<svg>`
  to the target element — serialize on that append (deferred by `setTimeout 0`), with a 25 ms poll as a backstop
  and a 150 s timeout → `error`. Silence `console.log` while the engine runs (it is chatty) but keep `console.error`.
- Engine sources are **inlined into the worker Blob** by the page (see 4.5), so the host does no network: in `init`
  mode `inline`, it just calls `self.plantumlLoad([], cb)` (classic build) or uses `self.__plantumlExports`
  (ESM build transformed by the page, only relevant if the engine is upgraded later).
- Wrap viz-global in its own `try/catch` when concatenating: a Graphviz failure must not take sequence diagrams down
  (Graphviz is only used for non-sequence diagrams).

### 4.2 Main-thread shim (in `plantuml-browser-render-script.js`)

Keeps the public surface every caller uses — `window.plantumlLoad()` (becomes a no-op) and
`window.plantuml.render(lines, targetId)` — and adds `window.plantuml.prefetch(sources)`.

- Engine acquisition: `fetch(CDN/viz-global.js)` + `fetch(CDN/plantuml.js)` as text (CORS `*` is sent by jsDelivr;
  verified from `file://`). Build one Blob: `workerHostSource + "\n;try{" + viz + "}catch(e){console.error(...)}\n;(function(){" + engine + "\n})();"`,
  `URL.createObjectURL`, `new Worker(url)`. Start **one** worker immediately; start the remaining
  `workerCount - 1` after the first `done` message (avoids four parallel 0.5–1 s engine compiles delaying the first
  diagram). Default `workerCount = Math.min(4, navigator.hardwareConcurrency || 2)`.
- Dispatch: least-busy worker; `pending[seq] = {id, key, worker}`; on `done`, `document.getElementById(id).innerHTML
  = svg` (this fires the callers' `MutationObserver`s exactly as the engine's own DOM insertion did); on `error`,
  write the same failure markup the synchronous `catch` in `processQueue` writes today (the "Diagram too large /
  Render error" block with the collapsed **Raw PlantUML** `<details>`), and keep the existing re-split-smaller retry
  for the "too large" text. Renders requested before the first worker exists are queued in the shim.
- Cache: `Map<sourceString, svgString>` filled on every successful render (initial render included), bounded by
  total bytes (default 64 MB, LRU by insertion) — SVG strings are 0.1–5 MB each. `render()` serves a cache hit
  synchronously (set `innerHTML` immediately; the observer already attached by the caller sees the mutation).
  In-flight dedupe: a second `render` with the same source while one is running just registers another target id.
- `prefetch(sources)`: dispatch every uncached source with no target; results go to the cache. Called by the two
  toggle loops in `collapsible-notes-script.js` (§4.4) right after they build `fragQueue` / `fragList`, so the
  sequential loops become a series of cache hits while the workers render in parallel.
- Fallback: if `Worker`, `fetch`, `Blob`/`URL.createObjectURL` or `OffscreenCanvas` are unavailable, or the engine
  fetch fails (offline), fall back to today's path: inject the two `<script>` tags and use the real `plantumlLoad` /
  `window.plantuml` (keep the old code behind `function useMainThreadEngine()`). The `plantuml-ready` class and
  the "Rendering diagram…" `::before` message keep their current semantics.
- Telemetry for tests/diagnostics: `window.__kronikolRender = { workers, renders, cacheHits, workerMs, injectMs,
  mode: 'worker' | 'main-thread' }`.

### 4.3 Queue changes (`processQueue`)

`rendering` becomes an in-flight counter: `if (inFlight >= maxParallel || renderQueue.length === 0) return;`,
`inFlight++` on dispatch, `inFlight--` on completion, and after each dispatch `setTimeout(processQueue, 0)` so all
slots fill. `maxParallel = workerCount` in worker mode, `1` in the fallback. `window._plantumlRendering` stays a
boolean ("anything in flight") because `collapsible-notes-script.js` reads it to avoid overlapping its own
re-renders with the initial queue — with workers that guard can simply remain (it only serialises toggles against
the initial render, which is fine).

### 4.4 Note-toggle paths (`collapsible-notes-script.js`)

Two places build a list of new fragments and then render them one by one: `setNoteState` (`fragQueue`, then
`renderNextFrag`) and `processRenderQueue` (`fragList`, then `renderNextFragment`). Insert, immediately after the
list is built: `if (window.plantuml.prefetch) window.plantuml.prefetch(list.map(f => f.source));`. Keep the
sequential loops as they are — they already check `_svgCache` first; additionally let them consult the shim cache
through `render()` (a hit is synchronous). Optional cleanup: drop `_svgCache` in favour of the shim cache.
The "too large / no svg" handling added in the last release stays valid (an `error` message produces text, not svg,
which those paths already treat as completion).

`internal-flow-popup-script.js` needs no change (it calls `window.plantuml.render` and waits with a MutationObserver).

### 4.5 `file://` and the Worker rules (measured, not theoretical)

- Chrome refuses `new Worker('file://…')`, so the worker must be created from a Blob URL.
- A Blob worker created by a `file://` document **cannot load anything over the network**: `importScripts` of the
  local server *and* of jsDelivr both fail (`NetworkError`), even with CORS headers. Hence: fetch the engine text on
  the main thread and inline it into the Blob. This was validated end to end against the real jsDelivr fork.
- The main-thread `fetch()` from `file://` to jsDelivr works because jsDelivr sends `Access-Control-Allow-Origin: *`.
  If `PlantUmlJsCdnBase` is ever pointed at a host without CORS, the shim's fallback path (script tags) still works;
  document this next to the option.
- Browser support: Blob workers + OffscreenCanvas 2D + `DecompressionStream` — Chrome/Edge 69+, Firefox 105+,
  Safari 16.4+; older browsers take the fallback path automatically.

### 4.6 Options (`ReportConfigurationOptions`, mirrored in `DiagramsFetcherOptions` where relevant)

- `BrowserRenderWorkers` (int, default 4; 0 = main-thread path, i.e. today's behaviour).
- `BrowserRenderCacheMegabytes` (int, default 64).
- `BrowserFragmentMaxHeight` (int px, default 12000 — exposes the existing constant; 4000–6000 measured faster but
  adds fragment seams).
These are written into the render script as constants (like `__PLANTUML_CDN_BASE__` is today). CLI: `kronikol ingest
--browser-render-workers N` if the tool exposes other report options; otherwise skip.

### 4.7 Node renderer (`NodeJsPlantUmlRenderer`, Phase 6)

- Batch mode: `plantuml-render.js` accepts NDJSON on stdin (`{"id":…, "source":…}` per line) and writes one
  `{"id":…, "svg":…}` or `{"id":…, "error":…}` per line; `DefaultDiagramsFetcher.GetNodeJsRenderedDiagrams` sends all
  diagrams of a report in one process. Per-diagram target elements get unique ids (TeaVM state leaks between targets
  with the same id — the browser code already does this).
- Code cache: `new vm.Script(code, {cachedData})` with the cache written next to the downloaded engine
  (`plantuml.js.v8cache`) on first run; discard on `cachedDataRejected` (node/V8 version change).
- Keep the single-diagram stdin mode for compatibility (the tests for it exist).

---

## 5. Work breakdown (TDD — red, green, refactor; every bug found along the way gets a test and a fix)

Each phase ends with the full test suite green. Phases 1–4 are the deliverable; 5 finishes it; 6–7 are follow-ups.

### Phase 0 — fixtures and measurement (½ day)
- Add an E2E fixture generator for a "large report": N diagrams × (M arrows + big JSON notes), reusing the existing
  `GenerateReport(...)` helper pattern in `tests/Kronikol.Tests.EndToEnd` (see `LoadingMessageTests`). Target: 6
  diagrams of ~150 KB source so a single E2E run stays under a minute.
- Add `tools/render-bench/README.md` pointing at the harness (`bench-report.js`, `variants/worker.js`,
  `puml-worker.js`, `fidelity.js`) and how to run it against a real report (`node tools/render-bench/bench-report.js
  baseline <path-to-TestRunReport.html>`). Harness is not part of the build.
- Record baseline numbers from the fixture in this document (§2 table: add a "fixture" row).

### Phase 1 — worker host + fidelity (1 day)
- New `src/Kronikol/Reports/plantuml-worker-host.js` (from `tools/render-bench/puml-worker.js`), registered in
  `Kronikol.csproj` `<EmbeddedResource>`, exposed by `DiagramContextMenu.GetPlantUmlWorkerHostScript()`.
- Unit tests (`tests/Kronikol.Tests/Reports/…`): resource present; serializer escaping (`&`, `<`, quotes, NBSP),
  attribute order, PI output, `textContent` set/get, `cloneNode(true)`, `baseURI` is http(s) when location is
  `blob:`. (Run the host's pure functions under Node via the existing JS-test approach if one exists; otherwise a
  Playwright page that imports the script into a worker and round-trips a small DOM.)
- Playwright fidelity test: render three fixture diagrams on the main thread and in a worker, assert identical
  `width`/`height`/`viewBox`, equal `<text>` count and equal element count (the prototype shows exactly these match).

### Phase 2 — shim and engine acquisition (1–1½ days)
- `plantuml-browser-render-script.js`: replace the two `<script defer>` tags with: inline worker host source (as a
  JS string constant produced by the C# side — `DiagramContextMenu` must JSON-escape it), engine fetch, Blob worker
  creation, the `window.plantuml` shim (`render`, `prefetch`), `plantumlLoad` no-op, fallback to script tags.
- Tests: `PlantUmlBrowserReportGeneratorTests` — update the two assertions that require `plantumlLoad()` /
  `window.plantuml.render` to assert the new shim surface; `DiagramContextMenuTests` line ~889 ordering assertion
  becomes "worker bootstrap registered before DOMContentLoaded handler"; E2E: `plantuml-ready` within 300 ms after
  load on the large fixture (`PerformanceObserver` not needed — measure `performance.now()` at class add via
  `MutationObserver` in a test init script); a diagram renders from `file://` (the E2E base already uses local files);
  a render error surfaces the same "Raw PlantUML" block; fallback path renders when `Worker` is stubbed out
  (`Page.AddInitScriptAsync("delete window.Worker")`).
- Playwright rules in `CLAUDE.md` apply (no `Force=true`, `PollingInterval = 200`, `.First` on multi-match).

### Phase 3 — parallel queue + staggered worker start (½ day)
- `processQueue` counter, `setTimeout(processQueue,0)` refill, `maxParallel`; start worker 1 immediately, 2..N after
  first `done`.
- Tests: unit-ish (Playwright `evaluate`) that with 4 workers four fragments are in flight; E2E: forcing all diagrams
  of the fixture produces no long task > 200 ms (`PerformanceObserver('longtask')` registered via init script) and
  finishes under a budget derived from the Phase 0 baseline (≥3× faster); `__kronikolRender.workers === 4`.

### Phase 4 — cache + prefetch in the toggle paths (½–1 day)
- Shim cache (bytes-bounded LRU) filled on every success; `prefetch`; hooks in `setNoteState` and
  `processRenderQueue`; cache hit served synchronously.
- Tests: unit (Playwright evaluate): second `render` of an identical source does not reach a worker
  (`cacheHits` increments) and still fires the caller's MutationObserver; cache eviction respects the byte bound;
  E2E: on the large fixture, expand a note on the biggest diagram — completes < 1 s, `workerRenders` during the toggle
  ≤ 2, no long task > 200 ms; existing collapsible-note E2E tests (`DiagramNote*Tests`) still pass.

### Phase 5 — options, docs, release (½ day)
- Options from §4.6 with tests (values reach the script; 0 workers = legacy path).
- Wiki (`../Kronikol.wiki`): `PlantUML-Browser-Rendering.md` (new "How rendering runs" section: workers, cache,
  `file://` note, browser support, fallback), `Report-Configuration.md` (new options), `Inline-SVG-Rendering.md`
  if it describes the loading message. README/nuget-readme if they mention the engine load.
- `CHANGELOG.md` "Unreleased → Added/Changed" entry with the measured before/after; bump the patch version in
  `Directory.Build.props` (single `<Version>` for all packages — bumped 3.0.44 → 3.0.45 for this work) per
  `CLAUDE.md`; commit, tag `v{version}`, push commit and tag.
- Kronikol4J parity: the Java port ships a copy of `plantuml-browser-render-script.js`
  (`kronikol4j-report/src/main/resources/io/kronikol/report/assets/`) and its report output is byte-matched to .NET;
  port the same script changes there (separate repo, separate release) or note the divergence in its README.

### Phase 6 — Node renderer (follow-up, ~1 day)
- Batch NDJSON mode in `plantuml-render.js` + `NodeJsPlantUmlRenderer.RenderMany(IEnumerable<…>)`, used by
  `DefaultDiagramsFetcher.GetNodeJsRenderedDiagrams`; V8 code cache; keep single mode.
- Tests: `NodeJsPlantUmlRendererTests` — batch returns one SVG per input in order, errors isolated per diagram, cache
  file created and reused, rejected cache regenerated; integration timing asserts batch of 5 is ≥2× faster than
  5 single spawns (skip on CI without node).

### Phase 7 — optional engine upgrade (follow-up)
- Re-base the jsDelivr fork on npm `@plantuml/core` ≥1.2026.6 (MIT): apply the two `4096.0 → 98304.0` compares and
  the `(max 4096)` message; it is an ES module (`export { render, renderToString }`), so the shim transforms the
  trailing `export{C as render,D as renderToString}` into `self.__plantumlExports = {render:C, renderToString:D}` when
  inlining, and `plantuml-render.js` gains `document.createProcessingInstruction` in its mock. Expected: ~15–25%
  faster renders, 35% smaller SVG, 1–5 s extra before the first diagram. **Not** the gh-pages 1.2026.7beta12 build.

---

## 6. Test plan details

### 6.1 Large fixture
Generator in the E2E project: `participant` × 4; per step: request arrow, DB arrow, reply, a `note right` with a
2–8 KB pretty-printed JSON body; 40 steps per diagram; 6 diagrams in 3 scenarios. This is the shape of the real
report (note-dominated), not the arrow-dominated synthetic used for engine profiling.

### 6.2 Metrics captured in tests (via `Page.AddInitScriptAsync`)
`window.__bench = { longTasks: [], readyAt }` with a `PerformanceObserver({type:'longtask', buffered:true})`, and
a `MutationObserver` on `body.class` for `plantuml-ready`. Assertions use generous but meaningful budgets
(ready < 300 ms; worst long task < 200 ms; toggle < 1000 ms; full fixture render < 3× baseline/… as measured in
Phase 0). Budgets are relative where the machine matters, absolute where the design guarantees it.

### 6.3 Regression
All existing E2E suites (diagram notes, context menu, zoom, filters, loading message, background rendering,
internal flow popup) must pass unchanged — the render surface is unchanged by design.

---

## 7. Risks and mitigations

| risk | mitigation |
|---|---|
| Engine fetch blocked (offline, corporate proxy, CSP on a hosted report) | fallback to script tags (today's path); telemetry `mode` shows which ran; docs. |
| Memory: 4 engine instances (~60–100 MB heap each) + SVG cache | default 4 workers capped by `hardwareConcurrency`; cache byte bound; workers are per page and die with it. |
| Output drift between worker and main thread (fonts) | `OffscreenCanvas.measureText` uses the same fonts as the page; fidelity test in Phase 1; keep the fallback path identical. |
| TeaVM state leaks between renders in one worker | one render at a time per worker, unique target ids (already the rule), fresh mock target per render. |
| Toggle re-splits move fragment boundaries (more fragments change than the one toggled) | prefetch makes it parallel; optional later: split on the *original* source and apply note states per fragment so boundaries are stable. |
| `innerHTML` of a 5 MB SVG on the main thread | measured 20–100 ms; acceptable; could be chunked with `requestIdleCallback` if a report has dozens of such fragments. |
| Kronikol4J byte-parity of report HTML | port the same script change; both repos version independently. |

---

## 8. Reference: prototype and harness

`tools/render-bench/` (not built, not shipped):
- `puml-worker.js` — worker host (mock DOM + protocol), the basis for Phase 1.
- `variants/worker.js` — the shim + queue/cache/prefetch patches as applied to a generated report, the basis for
  Phases 2–4 (`WORKERS`, `MAXH`, `PREFETCH`, `FILEMODE=2`, `CDNENGINE` env switches).
- `bench-report.js` — loads a real `TestRunReport.html` in Chromium, applies a variant, measures ready/first/all/
  blocked/toggle; writes `results/report-<tag>.json`.
- `fidelity.js`, `bench-browser.js`, `bench-node.js`, `gen.js` — output comparison, engine-only browser bench,
  Node spawn-vs-batch bench, synthetic diagram generator.
Run from the repo root with the E2E project built (it provides the Playwright driver at
`tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package`).
