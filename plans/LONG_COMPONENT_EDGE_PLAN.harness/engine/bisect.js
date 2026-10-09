'use strict';
// node bisect.js <engine.js> <tag> <shapes,comma,separated> [lo] [hi]
// env NODEFLAGS="--stack-size=400 --no-opt --no-maglev", CONC=8, REPS=1 (fresh process per probe, verdict must hold REPS times)
// For each shape, the longest statement length (in [lo, hi]) that draws, by bisection with one fresh node process per
// probe; then the verdicts at edge-2..edge+3 as a check. Appends JSON lines to results-<tag>.jsonl.
const { execFile } = require('child_process');
const fs = require('fs'), path = require('path');
const [engine, tag, shapesArg, loArg, hiArg] = process.argv.slice(2);
const flags = (process.env.NODEFLAGS || '').split(' ').filter(Boolean);
const CONC = Number(process.env.CONC || 8), REPS = Number(process.env.REPS || 1);
const out = path.join(__dirname, 'results-' + tag + '.jsonl');

let running = 0; const queue = [];
function slot() { return new Promise(r => { if (running < CONC) { running++; r(); } else queue.push(r); }); }
function release() { running--; const n = queue.shift(); if (n) { running++; n(); } }

async function probe(shape, len) {
  await slot();
  try {
    return await new Promise((resolve) => {
      execFile(process.execPath, [...flags, path.join(__dirname, 'probe.js'), engine, shape, String(len)], { env: process.env, timeout: 180000, maxBuffer: 1 << 24 }, (err, stdout) => {
        const line = String(stdout || '').trim().split('\n').pop();
        try { resolve(JSON.parse(line)); } catch (_) { resolve({ shape, len, verdict: 'X', msg: String(err && err.message || 'no output').slice(0, 200) }); }
      });
    });
  } finally { release(); }
}
async function draws(shape, len) {
  let last;
  for (let i = 0; i < REPS; i++) { last = await probe(shape, len); if (last.verdict !== 'draw') return { ok: false, r: last }; }
  return { ok: true, r: last };
}

async function bisectShape(shape) {
  let lo = Number(loArg || 60), hi = Number(hiArg || 4000);
  const a = await draws(shape, lo);
  if (!a.ok) return { shape, edge: null, note: 'fails at lo ' + lo, first: a.r };
  const b = await draws(shape, hi);
  if (b.ok) return { shape, edge: '>=' + hi, note: 'draws at hi' };
  let firstFail = b.r;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    const m = await draws(shape, mid);
    if (m.ok) lo = mid; else { hi = mid; firstFail = m.r; }
  }
  const around = [];
  for (const d of [-20, -2, -1, 1, 2, 20]) { const r = await probe(shape, lo + d); around.push((lo + d) + ':' + (r.verdict === 'draw' ? '.' : r.verdict)); }
  return { shape, edge: lo, failAt: hi, failVerdict: firstFail.verdict, failMsg: firstFail.msg, around: around.join(' ') };
}

(async () => {
  const shapes = shapesArg.split(',');
  const res = await Promise.all(shapes.map(s => bisectShape(s).then(r => {
    const rec = { tag, engine: path.basename(engine), flags: flags.join(' '), reps: REPS, ...r };
    fs.appendFileSync(out, JSON.stringify(rec) + '\n');
    console.log(JSON.stringify(rec));
    return rec;
  })));
})();
