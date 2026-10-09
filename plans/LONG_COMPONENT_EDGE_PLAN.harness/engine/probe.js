'use strict';
// node [--stack-size=N] [v8 flags] probe.js <engine.js> <shape> <len> [<len> ...]
// One line of JSON per length: { shape, len, stmt, verdict: draw|S|E|T, msg }. Fresh engine per process.
// REPEAT=k renders each length k times in this process (to see warm-JIT effects).
// PATCH_LABELS=1 / PATCH_TEAVM=1 apply the experimental fixes of patches.js (unobfuscated build only).
const h = require('./harness'), g = require('./gen'), P = require('./patches');
const [engine, shape, ...lens] = process.argv.slice(2);
const VIZ = process.env.VIZ || 'C:/Users/cex/AppData/Local/Kronikol/plantuml-js/1.2026.8/viz-global.js';
(async () => {
  await h.init(engine, VIZ, P.transform);
  const rep = Number(process.env.REPEAT || 1);
  for (const l of lens) {
    for (let k = 0; k < rep; k++) {
      const { statement, source } = g.shapeSource(shape, Number(l));
      const r = await h.render(source);
      const msg = r.verdict === 'draw' ? '' : ((r.svg.match(/RangeError[^<]*|java\.lang[^<]*|Syntax Error[^<]*/) || [r.thrown || r.text || ''])[0] || '').slice(0, 160);
      const heads = shape.startsWith('seq') ? (r.svg.match(/>a</g) || []).length : undefined;
      process.stdout.write(JSON.stringify({ shape, len: Number(l), stmt: statement.length, verdict: r.verdict, msg, heads }) + '\n');
    }
  }
  process.exit(0);
})().catch(e => { process.stdout.write(JSON.stringify({ fatal: String(e && e.stack || e) }) + '\n'); process.exit(1); });
