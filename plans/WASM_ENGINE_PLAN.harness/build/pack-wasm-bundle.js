// Packs PlantUML's Wasm GC engine (the npm package's plantuml-wasm.js wrapper, its plantuml.wasm-runtime.js and
// plantuml.wasm) into ONE script that Kronikol's loaders take as they take plantuml.js: the report page's worker
// (a classic Blob worker, the text inlined), its main-thread fallback (a module tag, import()) and the Node renderer
// (vm.Script). So the file has no import statement and no import.meta, carries the module's bytes itself (base64, with
// its own decoder: a vm context has no atob), and ends in the npm tail `export{X as render,Y as renderToString}`
// that every loader rewrites. The wrapper is the package's own, unchanged but for its two module-only lines and its
// export keywords, so its crash recovery (a trap or a stack overflow resets the instance) comes with it.
// Also sets globalThis.__plantumlEngineReady (the wrapper's ready(), already started), so a host can wait for the
// module to instantiate, and hear when it cannot, before it sends work.
// usage: node pack-wasm-bundle.js <npm package dir> <out.js> <label>
'use strict';
const fs = require('fs');
const path = require('path');
const [dir, outPath, label] = process.argv.slice(2);
if (!dir || !outPath || !label) throw new Error('usage: node pack-wasm-bundle.js <npm package dir> <out.js> <label>');

let runtime = fs.readFileSync(path.join(dir, 'plantuml.wasm-runtime.js'), 'utf8');
const runtimeExport = /export\s*\{([^}]*)\}\s*;?\s*$/;
const re = runtimeExport.exec(runtime);
if (!re) throw new Error('the runtime does not end in its export list');
const names = {};
re[1].split(',').forEach(part => {
    const m = /^\s*([A-Za-z_$][\w$]*)\s+as\s+([A-Za-z_$][\w$]*)\s*$/.exec(part);
    if (!m) throw new Error('unexpected runtime export: ' + part);
    names[m[2]] = m[1];
});
if (!names.load) throw new Error('the runtime exports no load');
runtime = runtime.slice(0, re.index);
if (/import\.meta|^\s*import\s|^\s*export\s/m.test(runtime)) throw new Error('the runtime uses module-only syntax');

let wrapper = fs.readFileSync(path.join(dir, 'plantuml-wasm.js'), 'utf8');
const edits = [
    ['import { load } from "./plantuml.wasm-runtime.js";', 'const { load } = __kronikolWasmRuntime;'],
    ['const DEFAULT_WASM_URL = new URL("./plantuml.wasm", import.meta.url).href;', 'const DEFAULT_WASM_URL = __kronikolWasmBytes;'],
    ['export function isWasmGCSupported()', 'function isWasmGCSupported()'],
    ['export async function ready(options)', 'async function ready(options)'],
    ['export async function render(lines, elementId, options)', 'async function render(lines, elementId, options)'],
    ['export function renderToString(lines, onSuccess, onError, options)', 'function renderToString(lines, onSuccess, onError, options)'],
];
for (const [from, to] of edits) {
    const count = wrapper.split(from).length - 1;
    if (count !== 1) throw new Error('the wrapper has ' + count + ' of: ' + from);
    wrapper = wrapper.replace(from, to);
}
if (/import\.meta|^\s*import\s|^\s*export\s/m.test(wrapper)) throw new Error('the wrapper still uses module-only syntax');

const b64 = fs.readFileSync(path.join(dir, 'plantuml.wasm')).toString('base64');
const out = `// PlantUML's Wasm GC engine (${label}), packed into one script for Kronikol: the npm package's plantuml-wasm.js
// wrapper, its plantuml.wasm-runtime.js and plantuml.wasm, unchanged but for the wrapper's module-only lines.
var __kronikolWasmRuntime = (function () {
${runtime}
return { load: ${names.load} };
})();
var __kronikolWasmBytes = (function (s) {
    var map = new Uint8Array(128), chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/', i;
    for (i = 0; i < 64; i++) map[chars.charCodeAt(i)] = i;
    var pad = s.charAt(s.length - 1) === '=' ? (s.charAt(s.length - 2) === '=' ? 2 : 1) : 0;
    var out = new Uint8Array(s.length / 4 * 3 - pad), o = 0;
    for (i = 0; i < s.length; i += 4) {
        var n = (map[s.charCodeAt(i)] << 18) | (map[s.charCodeAt(i + 1)] << 12) | (map[s.charCodeAt(i + 2)] << 6) | map[s.charCodeAt(i + 3)];
        out[o++] = n >> 16;
        if (o < out.length) out[o++] = (n >> 8) & 255;
        if (o < out.length) out[o++] = n & 255;
    }
    return out;
})("${b64}");
${wrapper}
// Kronikol: start loading now, and let a host wait for it (a rejected promise names why the module would not load).
globalThis.__plantumlEngineReady = (function () { var p = ready(); p.catch(function () {}); return function () { return ready(); }; })();
export{render as render,renderToString as renderToString};
`;
fs.writeFileSync(outPath, out);
console.log(outPath, (out.length / 1048576).toFixed(2) + ' MB');
