# render-bench — browser-rendering benchmark harness (reference, not shipped)

Prototype and measurement scripts behind `BROWSER_RENDER_WORKER_PLAN.md`. Plain Node scripts; not part of the build.

The production implementation lives in `src/Kronikol/Reports/plantuml-browser-render-script.js` (shim, queue,
cache, prefetch) and `src/Kronikol/Reports/plantuml-worker-host.js` (worker host, grown from `puml-worker.js`
here). The repeatable measurement is the E2E test `tests/Kronikol.Tests.EndToEnd/BrowserRenderWorkerTests.cs`
on the `LargeReportFixture` (6 note-dominated diagrams); it appends its numbers to
`tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/PlaywrightOutput/render-bench-results.txt`. Use the scripts
below when you need to measure a *real* report or compare engine builds.

Run against a real report: `node tools/render-bench/bench-report.js baseline <path-to-TestRunReport.html>`.

Prerequisites
- Build `tests/Kronikol.Tests.EndToEnd` once (the scripts use its Playwright driver at
  `tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package` and the installed Chromium).
- Engine builds next to these scripts (not committed — large):
  `old-plantuml.js` + `viz-global.js` = copy of `%LOCALAPPDATA%/Kronikol/plantuml-js/{plantuml.js,viz-global.js}`
  (or download from `TrackingDefaults.PlantUmlJsCdnBase`, the npm package `@plantuml/core@1.2026.8` from 3.30.5); optional `core-1.2026.6-patched.js` =
  `https://cdn.jsdelivr.net/npm/@plantuml/core@1.2026.6/plantuml.js` with every `4096.0` → `98304.0`.
- `polyfill.js` is the DOM-polyfill half of `src/Kronikol/PlantUml/plantuml-render.js` (regenerate if that changes).

Scripts
- `bench-report.js <baseline|worker> [TestRunReport.html]` — loads a real report in headless Chromium, applies a
  variant, measures ready / first diagram / force-render-all / main-thread long tasks / note-toggle time; writes
  `results/report-<TAG>.json`. Env for the `worker` variant (`variants/worker.js`): `WORKERS=4`, `MAXH=12000`,
  `PREFETCH=1`, `ENGINE=<file>`, `FILEMODE=2` (open via file://, engine inlined into a Blob worker), `CDNENGINE=1`
  (fetch the engine from `TrackingDefaults.PlantUmlJsCdnBase`, read from the source), `LAZYVIZ=1`, `TAG=<result name>`.
- `puml-worker.js` — the Web Worker engine host (mock DOM + protocol). Basis for the production worker host.
- `fidelity.js [engine]` — renders the same sources on the main thread and in the worker and compares geometry/counts.
- `bench-browser.js <old|new> <engine> [sizes]` — engine-only render timings on synthetic sequence diagrams.
- `bench-node.js <spawn|batch|batch-cache>` — Node renderer: process-per-diagram vs one warm process vs V8 code cache.
- `gen.js` — synthetic sequence-diagram generator; `bench-common.js` — shared Node polyfill harness.
- `bench-ladder.js`: two engine builds interleaved per rep on the corpus, SVG SHA-256 identity per file (the fork-versus-npm ladder behind
  `ENGINE_PIN_PLAN.md`; `results/ladder-fork-vs-npm-2026-09-22.txt`). `corpus-hash.js` hashes the corpus SVGs on their own.
- `integrity-probe.js <engine dir> [chromium,firefox,webkit]`: does the browser enforce `fetch(url, { integrity })`, `<script integrity>` on a
  classic and a module tag, and `import()` after a module tag (one request or two), per page origin (file://, loopback, LAN http)? Needs the
  browsers installed (`node <playwright package>/cli.js install webkit` for the third). `results/integrity-probe-2026-09-22.txt`.
- `secure-context-probe.js`: `isSecureContext` and `crypto.subtle` per origin, page and Blob worker, and the cost of hashing the engine
  natively versus in script. Superseded for the pin plan by `integrity-probe.js` (the browser hashes inside `fetch`), kept for the numbers.
- `statement-limits-probe.js <label>=<engine dir> [...]`: the statement-length pins of `NodeJsPlantUmlRendererTests` against any engine
  build, one node process per probe, plus a bisection for each edge; `results/statement-limits-<date>.{txt,json}`.
- An `<engine dir>` for the two probes above and the probes below is a folder holding `plantuml.js` and `viz-global.js`: the Node
  renderer's cache `%LOCALAPPDATA%/Kronikol/plantuml-js/<tag>/` for the pinned build, or an npm release extracted
  with `npm pack @plantuml/core@<version>` and `tar --force-local -xzf plantuml-core-<version>.tgz` (Git Bash tar
  reads `C:/...` as a remote host without `--force-local`); the files land in `package/`.
- `v8-code-cache-probe.js [length | damage <engine dir> | race <engine dir> [rounds] [concurrent]]`: V8 accepts a code cache against
  a changed source of the same length (why the Node renderer deletes `plantuml.js.v8cache` when it replaces the engine); a release
  node checks no checksum on the payload, so a damaged cache crashes node on every later render (why the pin plan's S3 checks it);
  and whether cold processes writing at once can tear the cache. `results/v8-code-cache-2026-09-25.txt`.
- `payload-syntax-probe.js [--loader-hook] <label>=<engine dir> [...]`: which note text makes the engine ask its script loader for
  `openiconic.js` or `emoji.js`, which the Node renderer's mock DOM never loads, and what that does to a `--batch` run.
  `--loader-hook` runs a temp copy of the script with `globalThis.PLANTUML_STDLIB_LOADER` failing every request at once (the npm
  build honours it; the fork build has no such hook). `results/payload-syntax-2026-09-25.txt`.
- `worker-wedge-probe.js <page with 1 worker> <page with 4 workers> [seconds]`: the same payloads through the shipped BrowserJs shim;
  the pages come from `emitter-corpus -- --shim`.
- `emitter-corpus/`: a console app named `formatter-probe`, the assembly name Kronikol grants its internals to.
  `dotnet run --project emitter-corpus -- <dir>` writes 14 sources from Kronikol's own emitter (header blocks, both step-bar forms,
  assertion notes, internal-flow links, focus colours, a 400-line note, 200 calls, creole-looking payloads);
  `-- --shim <page> <workers>` writes a bare page carrying the shipped render script; `-- --linked-labels <dir>` writes the linked
  statements `statement-limits-worker-probe.js --scan` cuts. `emitter-corpus-compare.js <dir> <label>=<engine
  dir> <label>=<engine dir>` renders the sources through the shipped Node script on two builds and compares the SVG bytes
  (`results/emitter-corpus-2026-09-25.txt`; re-run on the 3.31.1 serializer, all 14 sources, `results/emitter-corpus-2026-09-26.txt`).
  Before 3.29.6 the creole-payload source stalled every later source of the batch; the mock DOM now answers the engine's script loads at once.
- `statement-limits-worker-probe.js <shim page> <label>=<cdn base> [...]`: the statement limits in the shipped BrowserJs worker, per
  engine route (the page from `emitter-corpus -- --shim <page> 1`); `results/statement-limits-worker-2026-09-25.txt`.
  `--scan <dir> <shim page> <label>=<cdn base> [...]` cuts the text inside each `[[#iflow-…]]` link the emitter wrote (the sources
  from `emitter-corpus -- --linked-labels <dir>`: five request-label shapes, a request inside a collapsed-run `loop`, and a component
  edge) to every length from `FROM` to `TO` by `STEP`, the way `TruncateLabel` cuts it. The edge moves with V8's tier-up, so
  `COLD=1` gives each case a fresh page (the first render in a new worker; `CONCURRENCY=<n>` pages at once), `JSFLAGS` goes to
  Chromium as `--js-flags` (`--no-opt --no-maglev` for interpreter-sized frames with WebAssembly on, `--jitless` for both off),
  `BROWSER=firefox|webkit` and `LAUNCH=<json launch options>` (Firefox prefs, a WebKit environment) measure the other engines, a
  shim page written with 0 workers measures the main thread, `UNLINKED=1` drops the link and `ASIS=1` renders each source as
  written (the check after a fix). `results/statement-limits-worker-2026-09-26.txt` is the S0 measurement behind
  `PlantUmlStatementLimits.MaxLinkedLabelChars`.
- `probe-component.puml`: a Kronikol-shaped plain-shape component diagram for the ladder's non-sequence row.
- `results/`: the JSON results quoted in the plans. `engine-speed-2026-09-26.txt` holds the samples behind the two engine-speed budgets of
  `Large_report_renders_off_the_main_thread_within_budget`, and `fragment-height-2026-09-26.txt` the fragment-height table from
  `BrowserRenderWorkerTests.Bench_fragment_height_against_render_and_toggle_time` (explicit: run it by name with `-- xUnit.Explicit=on`),
  both `plans/ENGINE_PIN_PLAN.md` §10.2.
