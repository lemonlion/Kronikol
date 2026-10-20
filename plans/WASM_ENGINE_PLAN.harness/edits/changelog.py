# The engine's entry in the changelog's [Unreleased] section, the first under Changed (breaking) (5.0.0,
# plans/WASM_ENGINE_PLAN.md). usage: python -I changelog.py <repo root>
import sys

root = sys.argv[1]
p = root + '/CHANGELOG.md'
t = open(p, 'rb').read().decode('utf-8')
assert '\r' not in t

anchor = '### Changed (breaking)\n\n'
assert t.count(anchor) == 1
entry = '''- **The report draws with PlantUML's Wasm engine, from a build of PlantUML's master on TeaVM 0.17.0-dev-2.**
  `TrackingDefaults.PlantUmlJsCdnBase` points at a build of PlantUML's master (`f44b054d1`, 1.2026.10beta1, MIT
  flavor) compiled with TeaVM 0.17.0-dev-2, published on the engine fork as `v1.2026.10beta1-f44b054-teavm0.17.0-dev-2`
  and served by jsDelivr (4.x: `@plantuml/core` 1.2026.8 from npm). It holds two engines that draw the same SVG:
  `plantuml.js`, and the Wasm GC engine packed into one script, `plantuml-wasm-bundle.js`. A report's render workers draw
  with the Wasm engine wherever the browser runs Wasm GC (Chrome 119, Firefox 120, Safari 18.2) and lets a script build
  functions from text, which a content security policy without `'unsafe-eval'` refuses; everywhere else, on the page's
  main thread and in the Node renderer, `plantuml.js` draws. Both are checked against their known hashes. A Wasm engine
  that passes the check and does not start gives way to `plantuml.js`, and a worker an error takes down while it draws
  is replaced, its diagram drawn once more and then failed alone, naming the error. `window.__kronikolRender` says which
  engine draws (`engine`: `wasm` or `js`), why the page is not on the Wasm one (`engineProblem`) and how many workers
  crashed (`workerCrashes`). The new TeaVM no longer runs the engine out of stack on a long label, name or block opener,
  so a diagram that failed with "ran out of stack" in 4.x draws; the statement caps stay where they were. The engine
  writes a little more into a diagram: each element's source line (`data-source-line`), a participant's qualified name,
  and an internal-flow link as an `<a>` round its text, as the Java engine draws it, which the report binds by its
  address. The Node renderer's stand-in DOM gained what the new engine asks of it, so a NodeJs report keeps every
  link's label, and its SVG declares the `xlink` prefix its links use (`plans/WASM_ENGINE_PLAN.md`).
'''
t = t.replace(anchor, anchor + entry)
open(p, 'wb').write(t.encode('utf-8'))
print('ok')
