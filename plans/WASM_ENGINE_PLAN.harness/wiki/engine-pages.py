# The wiki's 5.0.0 engine pages (plans/WASM_ENGINE_PLAN.md): PlantUML Browser Rendering says which engine draws, from
# where, how the page chooses, falls back and recovers, and what the telemetry carries; Large Response and Diagram
# Handling, Diagnostics and Debugging and Report Configuration say the same where they name the engine, and Generated
# Reports says what the keyboard reaches in an internal-flow link the engine draws as anchors.
# usage: python -I engine-pages.py <wiki folder>
import sys

wiki = sys.argv[1]
TAG = 'v1.2026.10beta1-f44b054-teavm0.17.0-dev-2'
BASE = 'https://cdn.jsdelivr.net/gh/lemonlion/plantuml-js-plantuml_limit_size_98304@' + TAG
WASM_LINK = '[[The Wasm engine|PlantUML-Browser-Rendering#the-wasm-engine-500]]'


def edit(page, pairs):
    p = wiki + '/' + page
    raw = open(p, 'rb').read()
    crlf = b'\r\n' in raw
    t = raw.decode('utf-8').replace('\r\n', '\n')
    for old, new in pairs:
        assert t.count(old) == 1, (page, t.count(old), old[:100])
        t = t.replace(old, new)
    if crlf:
        t = t.replace('\n', '\r\n')
    open(p, 'wb').write(t.encode('utf-8'))


edit('PlantUML-Browser-Rendering.md', [
    # where the engine comes from
    ("""4. Two JavaScript files are fetched from jsDelivr (`TrackingDefaults.PlantUmlJsCdnBase`). Since 3.31.1 that is the npm route, `https://cdn.jsdelivr.net/npm/@plantuml/core@1.2026.8`: the release PlantUML itself published to npm. The registry never lets a published name@version hold other bytes, so nobody can change what the URL serves. Before 3.31.1 it was a fork repository holding tagged engine builds; those tags stay published for the reports that point at them.
   - `viz-global.js` (~1.4 MB) — the Graphviz layout engine
""",
     """4. Two JavaScript files are fetched from jsDelivr (`TrackingDefaults.PlantUmlJsCdnBase`). From 5.0.0 that is a tag of the engine fork on jsDelivr's GitHub route, `""" + BASE + """`: a stock build of PlantUML's master at `f44b054d1` (1.2026.10beta1, MIT flavor) compiled with TeaVM 0.17.0-dev-2, whose one change from PlantUML's own build is that TeaVM version. jsDelivr keeps a tag's files once it has served them, and every file is checked against its known hash (see [[Engine integrity|PlantUML-Browser-Rendering#engine-integrity-3311]]), so a file that changed on the fork is refused, never drawn with. From 3.31.1 through 4.x it was the npm route, `@plantuml/core@1.2026.8`, and before that other tags of the same fork; every one stays published for the reports that point at it.
   - `viz-global.js` (~1.4 MB) — the Graphviz layout engine, byte-identical to the one in `@plantuml/core` 1.2026.8
   - the engine, in one of two builds that draw the same SVG: `plantuml-wasm-bundle.js` (~6.1 MB, PlantUML's Wasm GC engine with its module embedded) in the render workers of a browser that runs Wasm GC, and `plantuml.js` (~3.4 MB) everywhere else (see """ + WASM_LINK + """).
"""),
    ("""   - `plantuml.js` (~3.9 MB) — the PlantUML engine compiled to JavaScript, a **stock** build: PlantUML's release commit `149874a1` (MIT flavor). It is nine commits past the `0e4f452e` build that 3.0.76 to 3.31.0 used, none of them reachable from a Kronikol diagram, and every diagram Kronikol emits draws byte-identical on the two; on the render ladder it is 4 % faster. Both carry the merged""",
     """   - Through 4.x the engine was `plantuml.js` (~3.9 MB) alone, `@plantuml/core` 1.2026.8's: a **stock** build of PlantUML's release commit `149874a1` (MIT flavor), nine commits past the `0e4f452e` build that 3.0.76 to 3.31.0 used, none of them reachable from a Kronikol diagram, on which every diagram Kronikol emits drew byte-identical; on the render ladder it was 4 % faster. Every build since carries the merged"""),
    ("""It is an ES module, which every consumer (worker host, main-thread fallback, the Node renderer) loads by rewriting or `import()`ing its trailing `export` statement.""",
     """Both engines are ES modules, which every consumer (worker host, main-thread fallback, the Node renderer) loads by rewriting or `import()`ing the trailing `export` statement."""),
    ("""- **Bootstrap.** A small shim in the report fetches `viz-global.js` + `plantuml.js` as text, inlines them with a worker host""",
     """- **Bootstrap.** A small shim in the report fetches `viz-global.js` + the engine (`plantuml-wasm-bundle.js` or `plantuml.js`, see """ + WASM_LINK + """) as text, inlines them with a worker host"""),
    ("""engineFetchMs, engineReadyAt, fallbackReason, engineIntegrity, vizIntegrity, webAssembly }` — useful""",
     """engineFetchMs, engineReadyAt, fallbackReason, engineIntegrity, vizIntegrity, webAssembly, engine, engineProblem, workerCrashes }` — useful"""),
    ("""`window.plantuml.cacheStats()` returns the render cache's `{ entries, bytes, limit, hits }` on demand.""",
     """`engine` (5.0.0) is `'wasm'` or `'js'`, `engineProblem` says why the page is not on the Wasm engine (`null` while it is), and `workerCrashes` counts the workers an error took down while they drew (see """ + WASM_LINK + """). `window.plantuml.cacheStats()` returns the render cache's `{ entries, bytes, limit, hits }` on demand."""),
    ("""WebAssembly, which Graphviz needs, is older than all of these, but a browser can run with it off: see [[Without WebAssembly|PlantUML-Browser-Rendering#without-webassembly-3313]].
""",
     """WebAssembly, which Graphviz needs, is older than all of these, but a browser can run with it off: see [[Without WebAssembly|PlantUML-Browser-Rendering#without-webassembly-3313]]. The Wasm engine needs more, Wasm GC (Chrome 119, Firefox 120, Safari 18.2): an older browser draws with `plantuml.js`, as fast as 4.x drew.
"""),
    ("""`window.__kronikolRender.mode` is `'main-thread'` and `fallbackReason` says why. Offline-with-cached-engine is no worse than before: the browser serves the cached CDN responses to the fetch.
""",
     """`window.__kronikolRender.mode` is `'main-thread'` and `fallbackReason` says why. Offline-with-cached-engine is no worse than before: the browser serves the cached CDN responses to the fetch. The main thread always draws with `plantuml.js`, even where the browser runs Wasm GC (5.0.0, see """ + WASM_LINK + """).
"""),
    # integrity
    ("""The page knows the SHA-256 of both engine files (internal constants beside `PlantUmlJsCdnBase`, in the Subresource Integrity form `sha256-…`) and hands them to the browser's own integrity check: the worker path's `fetch` carries `integrity`, and so do the fallback's two tags.""",
     """The page knows the SHA-256 of every engine file (internal constants beside `PlantUmlJsCdnBase`, in the Subresource Integrity form `sha256-…`: `viz-global.js` and, from 5.0.0, both engines) and hands them to the browser's own integrity check: the worker path's `fetch` carries `integrity`, and so do the fallback's two tags."""),
    ("""On an npm URL that nobody can change, the one real cause of a mismatch is something between the viewer and jsDelivr rewriting the response:""",
     """On a pinned URL, the likely cause of a mismatch is something between the viewer and jsDelivr rewriting the response:"""),
    ("""- **A refused `plantuml.js` refuses every diagram.** Each one says `Render error: PlantUML engine unavailable: engine integrity check failed: plantuml.js from <url> does not match sha256-<expected>; the browser console names the hash it computed` (Chromium and Firefox print the hash they computed, WebKit the byte counts). No worker starts, and there is no fallback, since the tags would fetch the same bytes.
""",
     """- **A refused engine refuses every diagram.** Each one says `Render error: PlantUML engine unavailable: engine integrity check failed: <file> from <url> does not match sha256-<expected>; the browser console names the hash it computed` (Chromium and Firefox print the hash they computed, WebKit the byte counts), where `<file>` is the engine the page fetched: `plantuml-wasm-bundle.js` in the render workers of a browser that runs Wasm GC, `plantuml.js` elsewhere. No worker starts, and there is no fallback, to the other engine either, since the same mirror serves both.
"""),
    ("""A mirror or proxy that serves the engine URLs must serve the npm package's exact bytes.""",
     """A mirror or proxy that serves the engine URLs must serve the tag's exact bytes, all three files."""),
    # without WebAssembly: the JavaScript engine
    ("""- **The telemetry:** `window.__kronikolRender.webAssembly` is `false`, and `vizIntegrity` stays `null`, since the file was never fetched.
""",
     """- **The telemetry:** `window.__kronikolRender.webAssembly` is `false`, and `vizIntegrity` stays `null`, since the file was never fetched.
- **The engine is `plantuml.js`** (5.0.0): the Wasm engine cannot run where no module compiles, so `engine` is `'js'` and `engineProblem` carries the same reason.
"""),
    # the new section, after Without WebAssembly and before the next heading after it
    ("""A component diagram of 40 services and 120 edges took 1.2 s through Smetana under `--jitless`. The Node renderer makes the same check (see [[Node.js rendering|PlantUML-Browser-Rendering#nodejs-rendering-plantumlrenderingnodejs]]).
""",
     """A component diagram of 40 services and 120 edges took 1.2 s through Smetana under `--jitless`. The Node renderer makes the same check (see [[Node.js rendering|PlantUML-Browser-Rendering#nodejs-rendering-plantumlrenderingnodejs]]).

### The Wasm engine (5.0.0)

From 5.0.0 the engine tag holds two builds of one engine that draw the same SVG: `plantuml.js`, and PlantUML's Wasm GC engine packed into one script, `plantuml-wasm-bundle.js`, which inlines into the worker Blob as `plantuml.js` does. On BreakfastProvider's report of 425 diagrams, an earlier build of the same engine (2026-10-17) took 49 % less of the workers' time than `plantuml.js` in Chrome, and 43 % less in Firefox.

- **Which engine draws.** Before it fetches an engine, the page asks three things: does a WebAssembly module compile, does a Wasm GC type validate (Chrome 119, Firefox 120, Safari 18.2), and may a script build a function from text, which the Wasm engine's runtime does and a content security policy without `'unsafe-eval'` refuses. Where all three hold, the render workers draw with the Wasm engine; elsewhere they draw with `plantuml.js`, and the console says why (`Kronikol: the PlantUML Wasm engine cannot run here (<reason>), so the page draws with its JavaScript build.`). The question about functions built from text is the only place the page builds one, inside a `try`; under such a policy the browser reports the refusal as a violation, once per page.
- **The main thread draws with `plantuml.js`.** The Wasm engine takes any error that reaches the page's global scope while a diagram waits for its own crash, and starts again, which in a worker is right (every such error is the engine's) and on a page is not. So the fallback that draws on the main thread loads `plantuml.js`, and `engineProblem` reads `the page draws on its main thread`.
- **A Wasm engine that does not start gives way.** One that passes its hash check and fails to start in a worker is replaced by `plantuml.js`: the workers start again on it and draw what they held. The console says `Kronikol: the PlantUML Wasm engine did not start (<reason>), so the page draws with its JavaScript build.`, and `engineProblem` carries the reason. A Wasm engine that fails its hash check is refused like any engine file, with no fallback.
- **A worker that crashes is replaced.** An error that reaches the page from a worker while it draws means the engine in it may never answer again: in Firefox a stack overflow inside the Wasm engine reaches no handler in the worker, only the page. The page lets that worker go, starts another, and draws the diagram it held once more; a diagram that takes a second worker down fails alone, as `Render error: the PlantUML engine crashed drawing this diagram: <the error>`, and every other diagram draws. A crash that names a stack overflow fails its diagram at once, since drawn again it would overflow again, and the report describes it as the engine's stack failure. `workerCrashes` counts the crashes.
- **The Node renderer runs `plantuml.js`** of the same tag (see [[Node.js rendering|PlantUML-Browser-Rendering#nodejs-rendering-plantumlrenderingnodejs]]).
- **What still runs it out of stack is nesting**, blocks or markup inside one another: from about 1,000 groups the Wasm engine crashes its worker, and the diagram fails at once with the overflow named; the JavaScript engine writes its `RangeError` as text. Either way the report describes it as the engine's stack failure, now saying that something is nested too deeply.
- **What the engine draws differently from 4.x's:** each element group carries its source line (`data-source-line`), a participant's groups its qualified name, and an internal-flow link is an `<a>` round each line of its label, as the Java engine draws it, which the report binds by its address: it takes off the titles that name the link's segment (a browser shows them as the tooltip and reads them as the link's name), names the first anchor from the whole label and leaves it the link's one Tab stop. Its TeaVM no longer runs the engine out of stack on a long label, name or block opener (see [[Large Response and Diagram Handling › Statement-Length Limits (3.0.48+)|Large-Response-and-Diagram-Handling#statement-length-limits-3048]]).
"""),
    # the statements that ran the 4.x engine out of stack
    ("Since 4.14.6 a stack failure is reported as one:",
     "From 5.0.0 the engine draws those statements at any length Kronikol writes, and what still runs it out of stack is deep nesting (see " + WASM_LINK + "). Since 4.14.6 a stack failure is reported as one:"),
    # Node rendering
    ("""1. On first use the renderer downloads `plantuml.js` and `viz-global.js` from the engine CDN""",
     """1. On first use the renderer downloads `plantuml.js` (the JavaScript build, from 5.0.0 also where the page draws with the Wasm engine) and `viz-global.js` from the engine CDN"""),
])

edit('Large-Response-and-Diagram-Handling.md', [
    ("""**The message limit is PlantUML's own; every other limit here belongs to the JavaScript build.**""",
     """**5.0.0's engine draws far past every stack limit here.** Its TeaVM, 0.17.0-dev-2, no longer walks a pattern once per character on the stack: in node it draws a `loop` label of 16,000 characters, a coloured bar of 6,000 and a participant's name of 8,000, and fails past those only where the diagram is wider than its 98,304-pixel limit; in a Chrome worker an earlier master on the same TeaVM drew an edge label of 102,400 characters and a participant's name of 12,800, in both its engines. It still runs out of stack on deep nesting (from about 2,000 groups inside one another in node), and fails that as text. The constants below stay where the 4.x engine's worker put them in 5.0.0 (see [[The Wasm engine|PlantUML-Browser-Rendering#the-wasm-engine-500]]).

**The message limit is PlantUML's own; every other limit here belongs to the JavaScript build.**"""),
])

edit('Diagnostics-and-Debugging.md', [
    ("""See [[PlantUML Browser Rendering › Without WebAssembly|PlantUML-Browser-Rendering#without-webassembly-3313]].
""",
     """See [[PlantUML Browser Rendering › Without WebAssembly|PlantUML-Browser-Rendering#without-webassembly-3313]]. `engine` (5.0.0) says which engine draws, `'wasm'` or `'js'`; `engineProblem` why the page is not on the Wasm engine (no Wasm GC, a policy without `'unsafe-eval'`, the main thread, or a Wasm engine that did not start); and `workerCrashes` how many workers an error took down while they drew. See [[PlantUML Browser Rendering › The Wasm engine|PlantUML-Browser-Rendering#the-wasm-engine-500]].
"""),
])

edit('Generated-Reports.md', [
    ("""- **Internal-flow popup**: with the browser engine (BrowserJs, the default) an arrow's link label is one Tab stop,
  named from its label, and Enter or Space opens its popup; where links show only on hover, focus shows it (5.0.0;
  before, only a pointer could open it). A diagram the Java engine drew keeps its links as they were. The popup takes
  the theme, its Activity and Flame Chart views drawn as tabs (5.0.0). See [[Internal Flow Tracking]].
""",
     """- **Internal-flow popup**: an arrow's link label is one Tab stop, named from its whole label, and Enter or Space opens
  its popup; where links show only on hover, focus shows it (5.0.0; before, the browser engine drew the link as text
  only a pointer could open, and the Java engine as one link for each line of the label, each named by the segment's
  id). The popup takes the theme, its Activity and Flame Chart views drawn as tabs (5.0.0). See
  [[Internal Flow Tracking]].
"""),
])
edit('Report-Configuration.md', [
    ("""`BrowserJs` (default) renders client-side in the browser via the PlantUML TeaVM JS engine.""",
     """`BrowserJs` (default) renders client-side in the browser via PlantUML's TeaVM engine, its Wasm GC build where the browser runs it (5.0.0) and its JavaScript build elsewhere."""),
])
print('ok')
