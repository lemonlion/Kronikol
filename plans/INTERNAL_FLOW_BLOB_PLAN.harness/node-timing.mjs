// Parse and decode cost of the segment block in Node (V8), the numbers behind the plan's §1.2.
//
//   node node-timing.mjs <TestRunReport.html>
//
// "Today" is V8 parsing and evaluating the 5.7 MB object literal as JavaScript. Timing
// `new vm.Script(src)` on the SAME source repeatedly hits V8's compilation cache and reads about
// 6 ms instead of 36: every iteration here appends a unique comment to defeat it. "Proposed" is
// the popup script's path: atob and the byte loop of report-decompress-helper.js,
// DecompressionStream('gzip'), Response.text(), JSON.parse. Median of five.
import fs from 'node:fs';
import zlib from 'node:zlib';
import vm from 'node:vm';

const p = process.argv[2];
if (!p) { console.error('usage: node node-timing.mjs <TestRunReport.html>'); process.exit(2); }
const d = fs.readFileSync(p, 'latin1');
const open = d.indexOf('window.__iflowSegments = ');
if (open < 0) throw new Error('no window.__iflowSegments block in ' + p);
const start = open + 'window.__iflowSegments = '.length;
let blob = d.slice(start, d.indexOf('</script>', start)).trimEnd();
if (blob.endsWith(';')) blob = blob.slice(0, -1);
const raw = Buffer.from(blob, 'latin1');
const gz = zlib.gzipSync(raw, { level: 6 });
const b64 = gz.toString('base64');
console.log(`block ${raw.length.toLocaleString()} bytes; gzip level 6 ${gz.length.toLocaleString()}; base64 ${b64.length.toLocaleString()} (${(100 * b64.length / raw.length).toFixed(1)}%)`);

function time(label, fn, n = 5) {
    const r = [];
    for (let i = 0; i < n; i++) { const t0 = performance.now(); fn(i); r.push(performance.now() - t0); }
    r.sort((a, b) => a - b);
    console.log(`${label.padEnd(58)} median ${r[Math.floor(n / 2)].toFixed(1).padStart(6)} ms  min ${r[0].toFixed(1).padStart(6)}  max ${r[n - 1].toFixed(1).padStart(6)}`);
}

time('today: V8 parse+eval of the object literal (cache defeated)', i => {
    const src = 'window.__iflowSegments = ' + blob + ';//' + i + Math.random();
    new vm.Script(src, { produceCachedData: false }).runInContext(vm.createContext({ window: {} }));
});
time('today, same source every time (V8 compilation cache; the trap)', () => {
    new vm.Script('window.__iflowSegments = ' + blob + ';').runInContext(vm.createContext({ window: {} }));
});
time('JSON.parse of the same text', () => JSON.parse(blob));
time('atob + byte loop (the shared helper) on the base64', () => {
    const s = atob(b64); const bytes = new Uint8Array(s.length);
    for (let i = 0; i < s.length; i++) bytes[i] = s.charCodeAt(i);
});
time('Uint8Array.fromBase64, where the engine has it', () => { if (Uint8Array.fromBase64) Uint8Array.fromBase64(b64); });
const bytes = Buffer.from(b64, 'base64');
time('zlib.gunzipSync', () => zlib.gunzipSync(bytes));

const r = [];
for (let i = 0; i < 5; i++) {
    const t0 = performance.now();
    const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip'));
    JSON.parse(await new Response(stream).text());
    r.push(performance.now() - t0);
}
r.sort((a, b) => a - b);
console.log(`${'proposed: DecompressionStream + text + JSON.parse'.padEnd(58)} median ${r[2].toFixed(1).padStart(6)} ms  min ${r[0].toFixed(1).padStart(6)}  max ${r[4].toFixed(1).padStart(6)}`);
