// Synthetic bench for SEARCH_INDEX_PLAN §11 — trigram index build/load/query across three tiers.
// Node approximates Chrome (same V8); zlib.gunzipSync approximates DecompressionStream throughput.
'use strict';
const zlib = require('zlib');
const { performance } = require('perf_hooks');

// ---------- deterministic corpus ----------
let seed = 0xC0FFEE;
function rand() { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed / 4294967296; }
function pick(a) { return a[(rand() * a.length) | 0]; }
function guid() {
  let s = '';
  for (let i = 0; i < 32; i++) { s += '0123456789abcdef'[(rand() * 16) | 0]; if (i === 7 || i === 11 || i === 15 || i === 19) s += '-'; }
  return s;
}
const KEYS = ['orderId', 'customerId', 'status', 'amount', 'currency', 'createdUtc', 'lineItems', 'sku', 'quantity', 'unitPrice', 'discountCode', 'shippingAddress', 'city', 'postcode', 'correlationId', 'traceparent', 'retryCount', 'isPriority'];
const WORDS = ['order', 'payment', 'refund', 'customer', 'submits', 'receives', 'confirmation', 'gateway', 'inventory', 'reserved', 'dispatched', 'validation', 'declined', 'timeout', 'retried'];
const STATUSES = ['Pending', 'Confirmed', 'Dispatched', 'Declined', 'Refunded'];

function jsonPayload(targetBytes, extra) {
  let out = '{\n';
  while (out.length < targetBytes) {
    const k = pick(KEYS);
    const v = rand() < 0.25 ? `"${guid()}"` : rand() < 0.5 ? `"${pick(STATUSES)}"` : ((rand() * 100000) | 0) / 100;
    out += `  "${k}": ${v},\n`;
  }
  if (extra) out += `  "note": "${extra}",\n`;
  return out + '  "final": true\n}';
}

function genScenarioText(docBytes, extra) {
  // ~15% step/arrow lines, ~85% payload — mirrors heavy-payload reports.
  let t = '';
  for (let i = 0; i < 12; i++) t += `${pick(WORDS)} the ${pick(WORDS)} ${pick(WORDS)} for ${guid()}\n`;
  for (let i = 0; i < 6; i++) t += `"Api" -> "Gateway" : POST: /api/${pick(WORDS)}/${guid()}\n`;
  const payloadBytes = Math.max(0, docBytes - t.length);
  const per = Math.max(200, (payloadBytes / 4) | 0);
  for (let i = 0; i < 4; i++) t += `note right\n${jsonPayload(per, i === 0 ? extra : null)}\nend note\n`;
  return t.toLowerCase();
}

// ---------- index build (JS proxy for the C# side) ----------
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

function varintLen(v) { return v < 128 ? 1 : v < 16384 ? 2 : 3; }
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
    if (listBytes < bitsetBytes) {
      sparse++;
      const buf = Buffer.alloc(2 + listBytes); buf[0] = 2; buf[1] = r.length & 0xff; // (real format: varint count)
      let o = 2; prev = 0;
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
  const t0 = performance.now();
  const gz = zlib.gzipSync(raw, { level: 9 });
  const gzipMs = performance.now() - t0;
  return { raw, gz, b64Len: Math.ceil(gz.length / 3) * 4, gzipMs, dense, sparse, empty };
}

// ---------- client-side decode + query ----------
function decode(gzB64) {
  const t0 = performance.now();
  const raw = zlib.gunzipSync(Buffer.from(gzB64, 'base64'));
  const B = raw.readUInt32LE(5), D = raw.readUInt32LE(9), bitsetBytes = (D + 7) >> 3;
  const offsets = new Int32Array(B); // offset of each row's tag byte
  let o = 13;
  for (let b = 0; b < B; b++) {
    offsets[b] = o;
    const tag = raw[o++];
    if (tag === 1) o += bitsetBytes;
    else if (tag === 2) { const n = raw[o++]; for (let i = 0; i < n; i++) { while (raw[o] & 128) o++; o++; } }
  }
  return { raw, B, D, bitsetBytes, offsets, decodeMs: performance.now() - t0 };
}
function rowInto(ix, b, out) { // AND row b into candidate bitset `out`; returns false if row empty
  const { raw, offsets, bitsetBytes } = ix;
  let o = offsets[b]; const tag = raw[o++];
  if (tag === 0) { out.fill(0); return false; }
  if (tag === 1) { for (let i = 0; i < bitsetBytes; i++) out[i] &= raw[o + i]; return true; }
  const n = raw[o++]; const tmp = new Uint8Array(bitsetBytes);
  let d = 0;
  for (let i = 0; i < n; i++) { let v = 0, sh = 0; while (raw[o] & 128) { v |= (raw[o++] & 127) << sh; sh += 7; } v |= raw[o++] << sh; d += v; tmp[d >> 3] |= 1 << (d & 7); }
  for (let i = 0; i < bitsetBytes; i++) out[i] &= tmp[i];
  return true;
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

// ---------- tiers ----------
function runTier(name, D, docKB) {
  seed = 0xC0FFEE;
  console.log(`\n=== ${name}: ${D} scenarios x ~${docKB}KB ===`);
  const docs = [], blobs = [];
  const planted = 'planted-needle-' + guid();
  let corpusBytes = 0;
  for (let d = 0; d < D; d++) {
    const extra = d === (D >> 1) ? planted : (d % 10 === 0 ? 'discount applied to order' : null);
    const t = genScenarioText(docKB * 1024, extra);
    docs.push(t); corpusBytes += t.length;
    blobs.push(zlib.gzipSync(Buffer.from(t), { level: 9 })); // per-scenario puml-data blob
  }
  console.log(`corpus: ${(corpusBytes / 1048576).toFixed(1)} MB text, per-scenario gz blobs total ${(blobs.reduce((a, b) => a + b.length, 0) / 1048576).toFixed(1)} MB`);

  for (const B of [32768, 65536]) {
    const idx = buildIndex(docs, B);
    const ser = serialize(idx);
    console.log(`B=${B}: build ${idx.buildMs.toFixed(0)}ms | raw ${(ser.raw.length / 1048576).toFixed(2)}MB gz ${(ser.gz.length / 1048576).toFixed(2)}MB b64 ${(ser.b64Len / 1048576).toFixed(2)}MB (+${ser.gzipMs.toFixed(0)}ms gzip) | rows dense/sparse/empty ${ser.dense}/${ser.sparse}/${ser.empty}`);
    if (B !== 65536) continue;

    const ix = decode(ser.gz.toString('base64'));
    console.log(`decode (gunzip+offsets): ${ix.decodeMs.toFixed(0)}ms`);

    const cache = new Map();
    function verify(cands, term) {
      const t0 = performance.now();
      let hits = 0, decompressed = 0;
      for (const d of cands) {
        let txt = cache.get(d);
        if (txt === undefined) { txt = zlib.gunzipSync(blobs[d]).toString(); cache.set(d, txt); decompressed++; }
        if (txt.includes(term)) hits++;
      }
      return { hits, decompressed, verifyMs: performance.now() - t0 };
    }
    for (const [label, term] of [['selective (planted GUID needle)', planted], ['moderate ("discount applied")', 'discount applied'], ['worst ("id", ubiquitous)', 'id']]) {
      cache.clear();
      const q = queryCandidates(ix, term);
      const v = verify(q.cands, term);
      console.log(`${label}: candidates ${q.cands.length}/${D} (intersect ${q.intersectMs.toFixed(1)}ms), verify ${v.verifyMs.toFixed(0)}ms (${v.decompressed} decompressed), hits ${v.hits}`);
      // warm repeat (verify-text cache already populated)
      const q2 = queryCandidates(ix, term); const v2 = verify(q2.cands, term);
      console.log(`   warm repeat: intersect ${q2.intersectMs.toFixed(1)}ms + verify ${v2.verifyMs.toFixed(0)}ms`);
    }
    // baseline: today's approach if everything were in data-search (linear includes over all text)
    const t0 = performance.now(); let n = 0;
    for (const s of docs) if (s.includes('discount applied')) n++;
    console.log(`baseline linear includes() over full corpus (no index, text already in RAM): ${(performance.now() - t0).toFixed(0)}ms (${n} hits)`);
  }
}

runTier('MEDIAN (typical report)', 300, 3);
runTier('MEDIUM (large report)', 1400, 40);
runTier('WORST (target-scale ceiling)', 2000, 50);
