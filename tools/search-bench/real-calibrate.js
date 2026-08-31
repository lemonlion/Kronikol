// Real-corpus calibration for SEARCH_INDEX_PLAN §11: extracts real CodeBehind from generated test
// reports' puml-data blobs, measures trigram-bucket density vs the synthetic bench, exercises the
// §4.1 normalization on real formatter output, and measures DecompressionStream vs zlib overhead.
'use strict';
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const { performance } = require('perf_hooks');

const REPORTS_DIR = process.argv[2] || 'c:/Code/Kronikol/tests/Kronikol.Tests/bin/Debug/net10.0/Reports';
const B = 65536, FNV_OFF = 0x811c9dc5, FNV_PRIME = 0x01000193;

// ---- gather real CodeBehind docs from the biggest reports ----
const files = fs.readdirSync(REPORTS_DIR).map(n => path.join(REPORTS_DIR, n)).filter(p => p.endsWith('.html'))
  .map(p => ({ p, size: fs.statSync(p).size })).sort((a, b) => b.size - a.size).slice(0, 60);
const docs = [];
for (const { p } of files) {
  const html = fs.readFileSync(p, 'utf8');
  const m = html.match(/<script id="puml-data" type="application\/json">(.*?)<\/script>/s);
  if (!m) continue;
  for (const b64 of Object.values(JSON.parse(m[1]))) {
    try { docs.push(zlib.gunzipSync(Buffer.from(b64, 'base64')).toString()); } catch { }
  }
}
console.log(`real CodeBehind docs: ${docs.length}, total ${(docs.reduce((a, d) => a + d.length, 0) / 1048576).toFixed(1)} MB`);

// ---- §4.1 normalization (ASCII fold + strip tags/~ + rejoin flush-left continuations) ----
function normalize(s) {
  s = s.replace(/[A-Z]/g, c => c.toLowerCase());          // ASCII-only fold (İ/ß divergence)
  s = s.replace(/<\/?(?:color|font|i|b|size|back)[^>]*>/g, '');
  s = s.replace(/~(?=[/*_\-"\[<])/g, '');                 // creole escapes
  s = s.replace(/\n(?=\S)/g, '');                          // rejoin: newline followed by non-whitespace
  return s.replace(/[ \t]+/g, ' ');
}

// ---- bucket density on real vs shape corpora ----
function bucketCount(s) {
  const mark = new Uint8Array(B);
  for (let i = 0, n = s.length - 2; i < n; i++) {
    let h = FNV_OFF;
    h = Math.imul(h ^ s.charCodeAt(i), FNV_PRIME);
    h = Math.imul(h ^ s.charCodeAt(i + 1), FNV_PRIME);
    h = Math.imul(h ^ s.charCodeAt(i + 2), FNV_PRIME);
    mark[(h >>> 0) & (B - 1)] = 1;
  }
  let c = 0; for (let b = 0; b < B; b++) c += mark[b];
  return c;
}
let stats = [];
for (const d of docs) { const n = normalize(d); stats.push({ kb: n.length / 1024, buckets: bucketCount(n) }); }
stats.sort((a, b) => a.kb - b.kb);
const mid = stats[stats.length >> 1], big = stats[stats.length - 1];
const perKb = stats.map(s => s.buckets / Math.max(1, s.kb));
console.log(`bucket density: median doc ${mid.kb.toFixed(1)}KB -> ${mid.buckets} buckets; largest ${big.kb.toFixed(1)}KB -> ${big.buckets}; buckets/KB median ${perKb.sort((a, b) => a - b)[perKb.length >> 1].toFixed(0)}`);

// union across all docs (how full does the table get with real text)
{
  const mark = new Uint8Array(B);
  for (const d of docs) {
    const s = normalize(d);
    for (let i = 0, n = s.length - 2; i < n; i++) {
      let h = FNV_OFF;
      h = Math.imul(h ^ s.charCodeAt(i), FNV_PRIME);
      h = Math.imul(h ^ s.charCodeAt(i + 1), FNV_PRIME);
      h = Math.imul(h ^ s.charCodeAt(i + 2), FNV_PRIME);
      mark[(h >>> 0) & (B - 1)] = 1;
    }
  }
  let c = 0; for (let b = 0; b < B; b++) c += mark[b];
  console.log(`union buckets across all real docs: ${c}/${B}`);
}

// ---- rejoin-rule spot check: find a chunked/wrapped value and show it rejoined ----
const sample = docs.find(d => d.includes('<color:gray>')) || '';
const rawLines = sample.split('\n');
let flushLeftContinuations = 0;
for (let i = 1; i < rawLines.length; i++)
  if (rawLines[i].length && !/\s/.test(rawLines[i][0]) && rawLines[i - 1].length && !/^@|^note|^end|^participant|^database|^actor|^boundary|^control|^entity|^collections|^queue|^skinparam|^autonumber|^["A-Za-z].*(->|-->)/.test(rawLines[i])) flushLeftContinuations++;
console.log(`sample doc: ${rawLines.length} lines, flush-left non-directive lines (rejoin candidates): ${flushLeftContinuations}`);
const gray = sample.split('\n').filter(l => l.includes('<color:gray>')).slice(0, 3);
console.log('sample <color:gray> lines (pre-normalize):'); gray.forEach(l => console.log('  ' + l.slice(0, 140)));
const norm = normalize(sample);
const gi = norm.indexOf('[full path]');
if (gi >= 0) console.log('rejoined [full path] region: ' + norm.slice(gi, gi + 140));

// ---- DecompressionStream vs zlib overhead (Node's web-streams impl as browser proxy) ----
(async () => {
  const blobs = [];
  for (let i = 0; i < 2000; i++) blobs.push(zlib.gzipSync(Buffer.from(docs[i % docs.length] || 'x'.repeat(4096))));
  let t0 = performance.now();
  for (const b of blobs) zlib.gunzipSync(b);
  const sync = performance.now() - t0;
  t0 = performance.now();
  for (const b of blobs) {
    const ds = new DecompressionStream('gzip');
    const stream = new Blob([b]).stream().pipeThrough(ds);
    await new Response(stream).arrayBuffer();
  }
  const web = performance.now() - t0;
  console.log(`2000 blobs: zlib.gunzipSync ${sync.toFixed(0)}ms vs DecompressionStream ${web.toFixed(0)}ms (per-blob overhead ~${((web - sync) / 2000).toFixed(2)}ms)`);
})();
