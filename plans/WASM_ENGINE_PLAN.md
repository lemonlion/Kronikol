# Wasm engine plan (5.0.0)

**Status:** executed for 5.0.0 on 2026-10-20, at the owner's ask of that day. Harness: `WASM_ENGINE_PLAN.harness/`
(`build/` holds the build's one patch, the bundle packer and the fork's README script; `edits/` every scripted edit of
the release; `probes/` the measurements below).

## 1. The ask

The owner, 2026-10-20: push and release v5 "including all the TestRunReport.json stuff - compression/.krkl etc, and
Kronikol labels merging with categories, and also using the plantuml wasm Teavm 1.17.0 self hosted build - or if
there's a later version", then "sorry I meant 1.17.0 (for Teavm)".

TeaVM has no 1.17.0. Its newest build is **0.17.0-dev-2** (TeaVM's own repository, `https://teavm.org/maven/repository`,
2026-10-12), the first with the maintainer's fix for the regular expression stack overflow Kronikol reported
([teavm#1295](https://github.com/konsoletyper/teavm/issues/1295), [#1296](https://github.com/konsoletyper/teavm/pull/1296),
both closed 2026-10-10 in favour of the maintainer's own, more general fix). The last stable release is 0.16.0. The
release uses 0.17.0-dev-2, and section 7 says when to move.

## 2. The build

- **Source:** PlantUML's master at `f44b054d1` (1.2026.10beta1), MIT flavor, built with
  `gradlew :plantuml-mit:npmPackage -Pci` on JDK 25. The one change is `build/teavm-dev2.patch`: TeaVM 0.17.0-dev-2 in
  `gradle/libs.versions.toml`, and TeaVM's repository in the four places Gradle resolves plugins and libraries from.
  Nothing in PlantUML's sources is patched.
- **Files:** `plantuml.js` (the JavaScript engine, 3.38 MB), `plantuml.wasm` (4.54 MB), `plantuml.wasm-runtime.js` and
  `plantuml-wasm.js` (the Wasm GC engine, with the same API as `plantuml.js`), `viz-global.js` (byte-identical to
  `@plantuml/core` 1.2026.8's, so its hash is unchanged), `themes.js`, `emoji.js`, `openiconic.js` and the `LICENSE`.
- **`plantuml-wasm-bundle.js`** (6.09 MB), packed by `build/pack-wasm-bundle.js`: the Wasm engine as one script, so that it
  inlines into the render worker's Blob as `plantuml.js` does (a worker a `file://` page builds can fetch nothing). The
  runtime sits in a function scope, the module is embedded as base64 with a decoder of its own, and the package's
  `plantuml-wasm.js` is unchanged but for its `import` line, its `import.meta` default and its `export` keywords. It ends
  in the same `export{… as render,… as renderToString}` every consumer rewrites, starts loading the module at once, and
  sets `globalThis.__plantumlEngineReady`, whose promise settles when the module is ready or cannot load.
- **Published** on the engine fork `lemonlion/plantuml-js-plantuml_limit_size_98304`, tag
  `v1.2026.10beta1-f44b054-teavm0.17.0-dev-2` (commit `fa437f8`, README by `build/fork-readme.py`), served by jsDelivr's
  `/gh/` route, which keeps a tag's files once it has served them. Every file is checked against its hash all the same:
  `plantuml.js` `sha256-/vtFyFB1p3iF7f2rj0da7UNXYle2np+qILBnXWqAEgg=`, `plantuml-wasm-bundle.js`
  `sha256-Cnk+sDwumDPD/bLkAL7f5dDvyXrY8CowxReQJ8AgxzE=`, `viz-global.js` `sha256-/Gyi3oPdTj/Kln1SFBInd4kKXNGrEBEVofcW/ITm3F4=`.
- **Checked before the pin moved:** the two engines draw the same SVG for each of PlantUML's 479 test sources and for the
  12 sources of Kronikol's corpus, and BreakfastProvider's full report (425 diagrams) draws in Chromium, Firefox and
  WebKit on both.

## 3. Decisions

- **D1. Self-hosted on the fork.** The build is not on npm, so the CDN base moves from jsDelivr's `/npm/` route to its
  `/gh/` route on the fork, as the pins before 3.31.1 were. `TrackingDefaults.PlantUmlJsCdnBase` carries it, and
  `PlantUmlJsIntegrity` and the new `PlantUmlWasmEngineIntegrity` the hashes.
- **D2. The page draws with the Wasm engine in its render workers** wherever the browser compiles WebAssembly, validates a
  Wasm GC type (Chrome 119, Firefox 120, Safari 18.2) and lets a script build a function from text, which the Wasm
  runtime's glue does and a content security policy without `'unsafe-eval'` refuses. The page asks all three before it
  fetches an engine (`wasmEngineUnavailable`), and fetches `plantuml.js` where one is missing.
- **D3. The main thread draws with `plantuml.js`.** The Wasm wrapper takes any error that reaches the global scope while a
  diagram waits for the engine's own crash, and starts the engine again. In a worker every such error is the engine's;
  on the page it can be any script's. The main-thread path is the fallback (no Workers, `BrowserRenderWorkers = 0`, an
  engine that could not be fetched), so it stays 4.x's, on the new build.
- **D4. The Node renderer runs `plantuml.js`** of the same build. Moving it to the Wasm engine is a change of its own.
- **D5. A Wasm engine that fails its hash check is refused**, as `plantuml.js` is: no fallback to the other engine, since
  the same mirror serves both.
- **D6. A Wasm engine that passes the check and does not start gives way to `plantuml.js`**: the workers and their jobs
  start again on it, and the console and `engineProblem` say why.
- **D7. A worker an error reaches while it draws is replaced.** Its engine may never answer again (in Firefox a Wasm stack
  overflow reaches no handler inside the worker, only the page's `worker.onerror`), so the page terminates it, starts
  another, draws what it held once more, and fails a diagram that takes a second worker down, alone, naming the error.
  A crash that names a stack overflow fails its diagram at once: the overflow is the source's, and drawn again it would
  overflow again. A worker the page let go of may still have answers on their way, and they are ignored.
- **D8. A link stays the `<a>` the engine draws.** From 1.2026.10 the TeaVM engine draws an internal-flow link as the Java
  engine does, an `<a href="#iflow-…" target="_top">` round its underlined text, where the 4.x engine drew the text
  alone. The report already binds the Java engine's `<a>` by its address (Server and Local rendering; the popup
  script's capture-phase click handler stops the navigation), so a browser-drawn link now takes that path. The Node renderer keeps the link too
  (section 4).
- **D9. The statement caps stay.** The new TeaVM draws far past every stack cap (section 5), but raising them changes
  what every report writes, so it is a change of its own (section 7).
- **D10. Telemetry.** `window.__kronikolRender` gains `engine` (`wasm` or `js`), `engineProblem` (why the page is not on
  the Wasm engine, null while it is) and `workerCrashes`.

## 4. What the new engine draws differently

Measured against `@plantuml/core` 1.2026.8 on the same sources:

- every element group carries the source line it came from, `data-source-line`, and an empty `<g class="message">`
  heads each message's drawing;
- a participant's groups carry `data-qualified-name` and `data-entity-uid`;
- numbers lose their trailing zeros (`63.6`, not `63.60`);
- a link is an `<a>` with `href`, `target`, `title` and their `xlink:` twins, round its text.

The Node renderer's stand-in DOM needed three things for the last: `setAttributeNS` (the engine writes `xlink:href` and
the rest with it), `firstChild` and `lastChild` (it reads a link's `firstChild` when it closes the link, and keeps only a
link that holds something: without them every link read as empty and was dropped with its label), and a declaration of
the `xlink` prefix, which the engine never writes and an XML reader, or a browser drawing the SVG as an image, needs.

These are report-output changes, recorded in the Kronikol4J divergence ledger (`OPTION_C_PLAN.harness/kronikol4j-ledger.md`).

## 5. Measurements (2026-10-20, `probes/`)

In node 25.9, through Kronikol's Node renderer:

| statement | 1.2026.8 | 5.0.0's engine |
|---|---|---|
| `loop` label | 2,000: the stack picture; 2,100 and past: `Syntax Error?` | 16,000 draws; 32,000: wider than the 98,304-pixel limit |
| coloured step bar | 6,000: RangeError text, no SVG | 6,000 draws; 20,000: wider than the limit |
| participant name | 2,000: `Syntax Error?` | 8,000 draws; 32,000: wider than the limit |

The new engine still runs out of stack on deep nesting: 2,000 groups (500 draw), 1,500 nested `if`s (300 draw), 5,000
nested `<b>` in a message or a component edge's label. It fails each as text (`RangeError: Maximum call stack size
exceeded`), never as its error picture. The facts that drove the page's and the Node renderer's handling of a stack
failure with long statements drive it with these sources now, or with a stub engine drawing the 4.x engine's picture.

In a Chromium worker the Wasm engine crashes on 1,000 nested groups (the page's `worker.onerror` sees `Uncaught
RangeError: Maximum call stack size exceeded`), which D7 fails at once. A failure that reaches the page through a
render's `onError`, as this one does, used to be written as `Render error: …` and never described, since a stack
failure always came back drawn before: the render queue now describes it as it describes a drawn one. Both
descriptions said the engine reads some statements one character at a time on its stack, which 5.0.0's does not; they
say a diagram is nested too deeply now. 5,000 nested `<b>` and 1,400 nested activity `if`s answered nothing within
120 s in the worker and failed at the worker host's 150 s timeout; no source Kronikol writes nests like that, and every
diagram of the parity and smoke runs drew (section 7).

## 6. Tests

Core (`Kronikol.Tests`): the shim carries both hashes; the engine the page draws with brings its own file, address and
hash; the probe answers null, `WebAssembly is not defined` (`--jitless`), `Wasm GC is not supported` and
`functions cannot be built from text: …`; the main thread draws with `plantuml.js`; the one Function constructor is
the probe's, in a `try`; the Wasm bundle on the CDN hashes to its constant (Integration); in node, a link keeps its
text, an SVG whose links use the `xlink` prefix declares it once, and one without links does not; the Node facts on long
statements pin the new engine's drawing, and its stack failures are driven by deep nesting or a stub engine.

End to end (`Kronikol.Tests.EndToEnd`, `WasmEngineTests`): Chromium draws with the Wasm engine in its workers and never
fetches `plantuml.js`; the main thread draws with `plantuml.js`; a policy with `'wasm-unsafe-eval'` and without
`'unsafe-eval'` draws with `plantuml.js` and still loads Graphviz; a Wasm engine that does not start gives way to
`plantuml.js`; a worker an error takes down is replaced, its diagram fails alone after a second try, and the page goes
on drawing. The integrity and mirror facts name the engine each path fetches.

## 7. Follow-ups

- **TeaVM 0.17.0:** rebuild on the stable release when it ships, and move the pin.
- **`@plantuml/core`:** when PlantUML publishes a build on TeaVM 0.17 with the Wasm engine in the package (Arnaud said the
  Wasm files ship in the next release, #2834), move the pin back to npm.
- **The statement caps:** re-measure in the three browsers' workers on the new engine and raise them.
- **The Node renderer on the Wasm engine.**
- **A link's tooltip:** the engine's `title` and `xlink:title` name the segment (`#iflow-…`), as the Java engine's always
  did.
- **Nested markup in the Wasm worker:** 5,000 nested `<b>` and 1,400 nested activity `if`s take the worker to its 150 s
  timeout instead of failing fast; find where the time goes and report it upstream.

## 8. Log

- 2026-10-20: built, published on the fork, measured; Kronikol's side written test first in the worktree
  `Kronikol-v5-wasm` from `v5` (`475410f0`).
