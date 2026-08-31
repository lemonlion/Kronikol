// M1 (SEARCH_INDEX_PLAN §11): blob size for B ∈ {32768, 65536} + candidate precision over a
// payload-heavy formatter-driven corpus (formatter-probe --corpus N output). Locks Q-A.
// Usage: node m1-bench.js [corpusDir]
'use strict';
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const { performance } = require('perf_hooks');
const { normalizeForSearch } = require('./normalize');

const corpusDir = process.argv[2] || path.join(__dirname, 'formatter-output', 'corpus');
const files = fs.readdirSync(corpusDir).filter(f => /^doc-\d+\.puml$/.test(f))
  .sort((a, b) => parseInt(a.match(/\d+/)[0]) - parseInt(b.match(/\d+/)[0]));
if (!files.length) { console.error('no corpus docs in ' + corpusDir); process.exit(1); }

console.log(`loading + normalizing ${files.length} docs...`);
const t0 = performance.now();
let rawBytes = 0;
const docs = files.map(f => {
  const raw = fs.readFileSync(path.join(corpusDir, f), 'utf8');
  rawBytes += raw.length;
  return normalizeForSearch(raw);
});
const normMs = performance.now() - t0;
const blobs = docs.map(d => zlib.gzipSync(Buffer.from(d), { level: 9 }));
console.log(`corpus: ${(rawBytes / 1048576).toFixed(1)} MB raw CodeBehind, ${(docs.reduce((a, d) => a + d.length, 0) / 1048576).toFixed(1)} MB normalized (load+normalize ${normMs.toFixed(0)}ms, single-threaded JS)`);

const FNV_OFF = 0x811c9dc5, FNV_PRIME = 0x01000193;
function buildIndex(docs, B) {
  const D = docs.length, rows = new Array(B).fill(null);
  const mark = new Uint8Array(B);
  const t0 = performance.now();
  for (let d = 0; d < D; d++) {
    const s = docs[d]; mark.fill(0);
    for (let i = 0, n = s.length - 2; i < n; i++) {
      let h = FNV_OFF;
      h = Math.imul(h ^ s.charCodeAt(i), FNV_PRIME);
      h = Math.imul(h ^ s.charCodeAt(i + 1), FNV_PRIME);
      h = Math.imul(h ^ s.charCodeAt(i + 2), FNV_PRIME);
      mark[(h >>> 0) & (B - 1)] = 1;
    }
    for (let b = 0; b < B; b++) if (mark[b]) (rows[b] ??= []).push(d);
  }
  return { rows, D, B, buildMs: performance.now() - t0 };
}

function varintLen(v) { return v < 128 ? 1 : v < 16384 ? 2 : v < 2097152 ? 3 : 4; }
function serialize(idx) {
  const { rows, D, B } = idx, bitsetBytes = (D + 7) >> 3;
  const parts = [];
  const head = Buffer.alloc(13); head.write('KSI1'); head[4] = 1; head.writeUInt32LE(B, 5); head.writeUInt32LE(D, 9);
  parts.push(head);
  let dense = 0, sparse = 0, empty = 0;
  for (let b = 0; b < B; b++) {
    const r = rows[b];
    if (!r) { parts.push(Buffer.from([0])); empty++; continue; }
    let listBytes = 0, prev = 0;
    for (const d of r) { listBytes += varintLen(d - prev); prev = d; }
    const countBytes = varintLen(r.length);
    if (countBytes + listBytes < bitsetBytes) {
      sparse++;
      const buf = Buffer.alloc(1 + countBytes + listBytes); buf[0] = 2;
      let o = 1, c = r.length;
      while (c >= 128) { buf[o++] = (c & 127) | 128; c >>= 7; } buf[o++] = c;
      prev = 0;
      for (const d of r) { let v = d - prev; prev = d; while (v >= 128) { buf[o++] = (v & 127) | 128; v >>= 7; } buf[o++] = v; }
      parts.push(buf);
    } else {
      dense++;
      const buf = Buffer.alloc(1 + bitsetBytes); buf[0] = 1;
      for (const d of r) buf[1 + (d >> 3)] |= 1 << (d & 7);
      parts.push(buf);
    }
  }
  const raw = Buffer.concat(parts);
  const gz = zlib.gzipSync(raw, { level: 9 });
  return { raw, gz, b64Len: Math.ceil(gz.length / 3) * 4, dense, sparse, empty };
}

function decode(gzBuf) {
  const t0 = performance.now();
  const raw = zlib.gunzipSync(gzBuf);
  const B = raw.readUInt32LE(5), D = raw.readUInt32LE(9), bitsetBytes = (D + 7) >> 3;
  const offsets = new Int32Array(B);
  let o = 13;
  for (let b = 0; b < B; b++) {
    offsets[b] = o;
    const tag = raw[o++];
    if (tag === 1) o += bitsetBytes;
    else if (tag === 2) {
      let n = 0, sh = 0;
      while (raw[o] & 128) { n |= (raw[o++] & 127) << sh; sh += 7; } n |= raw[o++] << sh;
      for (let i = 0; i < n; i++) { while (raw[o] & 128) o++; o++; }
    }
  }
  return { raw, B, D, bitsetBytes, offsets, decodeMs: performance.now() - t0 };
}
function rowInto(ix, b, out) {
  const { raw, offsets, bitsetBytes } = ix;
  let o = offsets[b]; const tag = raw[o++];
  if (tag === 0) { out.fill(0); return; }
  if (tag === 1) { for (let i = 0; i < bitsetBytes; i++) out[i] &= raw[o + i]; return; }
  let n = 0, sh = 0;
  while (raw[o] & 128) { n |= (raw[o++] & 127) << sh; sh += 7; } n |= raw[o++] << sh;
  const tmp = new Uint8Array(bitsetBytes);
  let d = 0;
  for (let i = 0; i < n; i++) { let v = 0, s2 = 0; while (raw[o] & 128) { v |= (raw[o++] & 127) << s2; s2 += 7; } v |= raw[o++] << s2; d += v; tmp[d >> 3] |= 1 << (d & 7); }
  for (let i = 0; i < bitsetBytes; i++) out[i] &= tmp[i];
}
function queryCandidates(ix, term) {
  const t0 = performance.now();
  const out = new Uint8Array(ix.bitsetBytes).fill(0xff);
  for (let i = 0, n = term.length - 2; i < n; i++) {
    let h = FNV_OFF;
    h = Math.imul(h ^ term.charCodeAt(i), FNV_PRIME);
    h = Math.imul(h ^ term.charCodeAt(i + 1), FNV_PRIME);
    h = Math.imul(h ^ term.charCodeAt(i + 2), FNV_PRIME);
    rowInto(ix, (h >>> 0) & (ix.B - 1), out);
  }
  const cands = [];
  for (let d = 0; d < ix.D; d++) if (out[d >> 3] & (1 << (d & 7))) cands.push(d);
  return { cands, intersectMs: performance.now() - t0 };
}

// Query classes. Needles are planted per-doc by the corpus generator ("needle-doc-<d>-").
const D = docs.length;
const midNeedleMatch = docs[D >> 1].match(/needle-doc-\d+-[0-9a-f]{6}/);
const queries = [
  ['selective (planted per-doc needle)', midNeedleMatch ? midNeedleMatch[0] : 'needle-doc-'],
  ['moderate (vocab word "warehouse")', 'warehouse'],
  ['moderate-phrase ("inventoryservice")', 'inventoryservice'],
  ['broad ("order", vocab-ubiquitous)', 'order'],
  ['worst ("id", json-ubiquitous)', 'id'],
];

for (const B of [32768, 65536]) {
  const idx = buildIndex(docs, B);
  const ser = serialize(idx);
  console.log(`\nB=${B}: build ${idx.buildMs.toFixed(0)}ms | raw ${(ser.raw.length / 1048576).toFixed(2)}MB gz ${(ser.gz.length / 1048576).toFixed(2)}MB b64 ${(ser.b64Len / 1048576).toFixed(2)}MB | rows dense/sparse/empty ${ser.dense}/${ser.sparse}/${ser.empty}`);

  const ix = decode(ser.gz);
  console.log(`decode: ${ix.decodeMs.toFixed(0)}ms`);
  for (const [label, term] of queries) {
    const q = queryCandidates(ix, term);
    // ground truth + precision
    let truth = 0;
    const t1 = performance.now();
    for (const dtext of docs) if (dtext.includes(term)) truth++;
    const truthMs = performance.now() - t1;
    // false positives the verify pass must absorb (decompress + includes)
    let fp = 0;
    const t2 = performance.now();
    for (const c of q.cands) {
      const txt = zlib.gunzipSync(blobs[c]).toString();
      if (!txt.includes(term)) fp++;
    }
    const verifyMs = performance.now() - t2;
    const precision = q.cands.length ? ((q.cands.length - fp) / q.cands.length * 100).toFixed(1) : '100.0';
    console.log(`  ${label}: candidates ${q.cands.length}/${D}, true ${truth}, precision ${precision}% (intersect ${q.intersectMs.toFixed(1)}ms, cold verify ${verifyMs.toFixed(0)}ms; linear-truth scan ${truthMs.toFixed(0)}ms)`);
  }
}
