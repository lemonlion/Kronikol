# Adds the v1.2026.10beta1-f44b054-teavm0.17.0-dev-2 build to the engine fork's README: its row first in the versions
# table, a section saying what it holds and how it was built, and its usage. usage: python -I fork-readme.py <README.md>
import sys

p = sys.argv[1]
t = open(p, 'rb').read().decode('utf-8')
assert '\r' not in t
TAG = 'v1.2026.10beta1-f44b054-teavm0.17.0-dev-2'
CDN = 'https://cdn.jsdelivr.net/gh/lemonlion/plantuml-js-plantuml_limit_size_98304@' + TAG

old_row = '| tag | base | size limit | module format |\n|---|---|---|---|\n'
new_row = old_row + (
    f'| `{TAG}` | [plantuml/plantuml](https://github.com/plantuml/plantuml) master `f44b054d1` (1.2026.10beta1, MIT flavor, '
    'stock `npmPackage` build) compiled with TeaVM 0.17.0-dev-2 | default 8192px; pass `{maxSvgSize: N}` to `render` | '
    'ES module, as below; and the Wasm GC engine with the same API (`plantuml-wasm.js`), also packed into one script '
    '(`plantuml-wasm-bundle.js`) |\n')
assert t.count(old_row) == 1
t = t.replace(old_row, new_row)

old_section = '### What\'s in `v1.2026.8beta1-0e4f452`\n'
new_section = f'''### What's in `{TAG}`

A stock build of upstream master at
[`f44b054d1`](https://github.com/plantuml/plantuml/commit/f44b054d1) via `gradlew :plantuml-mit:npmPackage -Pci`
(JDK 25), with one change to the build: TeaVM 0.17.0-dev-2, from TeaVM's own repository
(`https://teavm.org/maven/repository`), in place of 0.16.0. That TeaVM build carries the fix for the regular
expression stack overflow reported in [teavm#1295](https://github.com/konsoletyper/teavm/issues/1295)
([teavm#1296](https://github.com/konsoletyper/teavm/pull/1296)), so a long arrow label or participant name no longer
runs the engine out of stack in a Web Worker. Nothing in PlantUML's sources is patched.

The files:

- `plantuml.js`: the JavaScript engine.
- `plantuml-wasm.js`, `plantuml.wasm` and `plantuml.wasm-runtime.js`: the package's Wasm GC engine, with the same API
  as `plantuml.js`. It needs Wasm GC (Chrome 119, Firefox 120, Safari 18.2 or later), and its runtime builds
  functions with `new Function`, so a content security policy has to allow `'unsafe-eval'` as well as
  `'wasm-unsafe-eval'`. Where either is missing, use `plantuml.js`.
- `plantuml-wasm-bundle.js`: the three files above packed into one script by
  [Kronikol](https://github.com/lemonlion/Kronikol), for a host that takes the engine as text (a classic Web Worker,
  a Node `vm` context): the runtime in function scope, `plantuml.wasm` embedded as base64 with its own decoder, and
  the package's `plantuml-wasm.js` unchanged but for its `import` line, its `import.meta` default and its `export`
  keywords. It ends in the same `export{{… as render,… as renderToString}}` as `plantuml.js`, starts loading the
  module at once, and sets `globalThis.__plantumlEngineReady`, a function whose promise settles when the module is
  ready or cannot load. The wrapper's crash recovery comes with it: a trap or a stack overflow fails the pending
  requests and the next call loads a fresh instance.
- `viz-global.js` (byte-identical to the one in `@plantuml/core` 1.2026.8), `themes.js`, `emoji.js`,
  `openiconic.js` and the package's `LICENSE` (MIT).

The text files are as the build wrote them on Windows, with the carriage returns upstream's own npm releases carry.

'''
assert t.count(old_section) == 1
t = t.replace(old_section, new_section + old_section)

old_usage = '## Usage\n\n'
new_usage = old_usage + f'''### Via jsDelivr CDN (`{TAG}`, Wasm GC)

```html
<script type="module">
  import {{ render }} from "{CDN}/plantuml-wasm.js";
  render(["@startuml", "a -> b: hello", "@enduml"], "targetElementId", {{ maxSvgSize: 98304 }});
</script>
```

`plantuml.js` at the same address is the JavaScript engine, for a runtime without Wasm GC.

'''
assert t.count(old_usage) == 1
t = t.replace(old_usage, new_usage)

old_license = 'The 1.2026.8beta1 and 1.2026.6 builds are MIT-licensed'
assert t.count(old_license) == 1
t = t.replace(old_license, 'The 1.2026.10beta1, 1.2026.8beta1 and 1.2026.6 builds are MIT-licensed')
open(p, 'wb').write(t.encode('utf-8'))
print('ok')
