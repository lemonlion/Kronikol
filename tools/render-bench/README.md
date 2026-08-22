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
  (or download from `TrackingDefaults.PlantUmlJsCdnBase`); optional `core-1.2026.6-patched.js` =
  `https://cdn.jsdelivr.net/npm/@plantuml/core@1.2026.6/plantuml.js` with every `4096.0` → `98304.0`.
- `polyfill.js` is the DOM-polyfill half of `src/Kronikol/PlantUml/plantuml-render.js` (regenerate if that changes).

Scripts
- `bench-report.js <baseline|worker> [TestRunReport.html]` — loads a real report in headless Chromium, applies a
  variant, measures ready / first diagram / force-render-all / main-thread long tasks / note-toggle time; writes
  `results/report-<TAG>.json`. Env for the `worker` variant (`variants/worker.js`): `WORKERS=4`, `MAXH=12000`,
  `PREFETCH=1`, `ENGINE=<file>`, `FILEMODE=2` (open via file://, engine inlined into a Blob worker), `CDNENGINE=1`
  (fetch the engine from the real jsDelivr fork), `LAZYVIZ=1`, `TAG=<result name>`.
- `puml-worker.js` — the Web Worker engine host (mock DOM + protocol). Basis for the production worker host.
- `fidelity.js [engine]` — renders the same sources on the main thread and in the worker and compares geometry/counts.
- `bench-browser.js <old|new> <engine> [sizes]` — engine-only render timings on synthetic sequence diagrams.
- `bench-node.js <spawn|batch|batch-cache>` — Node renderer: process-per-diagram vs one warm process vs V8 code cache.
- `gen.js` — synthetic sequence-diagram generator; `bench-common.js` — shared Node polyfill harness.
- `results/` — the JSON results quoted in the plan.
