'use strict';
// node --stack-size=N [flags] stack.js <engine.js> <shape> <len> [outPrefix]
// Renders one source (gen.js SHAPES) and, if the engine overflowed, saves the RangeError's stack (Error.stackTraceLimit
// from STL=inf) and prints the frame histogram, the repeating cycle at the deep end, and the frames below the cycle.
// PATCH_LABELS / PATCH_TEAVM apply patches.js (unobfuscated build only).
const fs = require('fs');
const h = require('./harness'), g = require('./gen'), P = require('./patches');
const [engine, shape, lenArg, outPrefix] = process.argv.slice(2);
const VIZ = process.env.VIZ || 'C:/Users/cex/AppData/Local/Kronikol/plantuml-js/1.2026.8/viz-global.js';
const out = (s) => process.stdout.write(s + String.fromCharCode(10));

(async () => {
  await h.init(engine, VIZ, P.transform);
  const { statement, source } = g.shapeSource(shape, Number(lenArg));
  const r = await h.render(source);
  out(`verdict=${r.verdict} stmt=${statement.length} stacks=${r.rangeErrorStacks.length}`);
  if (r.verdict !== 'draw') out('text: ' + (r.svg.match(/RangeError[^<]*|java\.lang[^<]*|Syntax Error[^<]*/g) || []).slice(0, 2).join(' | ') + (r.thrown ? ' THROWN ' + r.thrown.slice(0, 300) : ''));
  if (!r.rangeErrorStacks.length) return process.exit(0);
  const st = r.rangeErrorStacks[0];
  if (outPrefix) fs.writeFileSync(outPrefix + '.stack.txt', st);
  const frames = st.split('\n').slice(1).map(l => l.trim().replace(/^at /, ''));
  const names = frames.map(f => f.replace(/ \(.*\)$/, ''));
  out('frames=' + frames.length);
  const hist = new Map(); for (const n of names) hist.set(n, (hist.get(n) || 0) + 1);
  for (const [n, c] of [...hist.entries()].sort((a, b) => b[1] - a[1]).slice(0, 12)) out(String(c).padStart(7) + '  ' + n);
  const seg = names.slice(50, Math.min(4050, names.length));
  let period = 0;
  for (let p = 1; p <= 300 && !period; p++) {
    let ok = true; for (let i = 0; i + p < seg.length && i < 1500; i++) if (seg[i] !== seg[i + p]) { ok = false; break; }
    if (ok) period = p;
  }
  if (period) { out('period=' + period + ' cycle (deepest first):'); for (let i = 0; i < period; i++) out('   ' + frames[i + 50]); }
  // frames below the cycle: the first index (from the bottom) where the cycle stops
  if (period) {
    const cyc = new Set(names.slice(50, 50 + period));
    let k = names.length - 1; while (k >= 0 && !cyc.has(names[k])) k--;
    out('below the cycle (' + (names.length - 1 - k) + ' frames), first 40:');
    for (const f of frames.slice(k - 3, k + 40)) out('   ' + f);
  }
  process.exit(0);
})();
