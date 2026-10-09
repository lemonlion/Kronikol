'use strict';
// node svgdump.js <engine.js> <out.json>  — renders a fixed set of sources (Labels cases) and writes {name: sha256(svg)}
// PATCH_LABELS=1 applies the Labels.init guard (see probe.js) to the unobfuscated build.
const h = require('./harness'), g = require('./gen'), fs = require('fs'), crypto = require('crypto');
const [engine, outFile] = process.argv.slice(2);
const VIZ = 'C:/Users/cex/AppData/Local/Kronikol/plantuml-js/1.2026.8/viz-global.js';
const isQ = (c) => c === '"' || c === '\u201c' || c === '\u201d' || c === '\uE121';
globalThis.__labelsGuard = (s) => {
  let q = 0; for (let i = 0; i < s.length; i++) if (isQ(s[i])) q++;
  const f = s.length > 0 && isQ(s[0]), l = s.length > 0 && isQ(s[s.length - 1]);
  return (q === 4 && f && l) || (q === 2 && f && !l) || (q === 2 && !f && l);
};
function transform(code) {
  if (!process.env.PATCH_LABELS) return code;
  return code.replace('if ($this.$firstLabel === null && $this.$secondLabel === null) {', 'if ($this.$firstLabel === null && $this.$secondLabel === null && globalThis.__labelsGuard(String($labelLink.$nativeString))) {');
}
const edges = {
  quoted: 'caller --> warehouse : "ClickHouse: INSERT INTO a, INSERT INTO b - 2 calls across 1 tests"',
  unquoted: 'caller --> warehouse : ClickHouse: INSERT INTO a - 1 calls across 1 tests',
  both: 'caller --> warehouse : "1" uses "n"',
  first: 'caller --> warehouse : "1" uses',
  second: 'caller --> warehouse : uses "n"',
  quotedInner: 'caller --> warehouse : "say "hi" now"',
  curly: 'caller --> warehouse : \u201cleft\u201d mid \u201dright\u201d',
  arrowLabel: 'caller --> warehouse : "reads >"',
};
(async () => {
  await h.init(engine, VIZ, transform);
  const out = {};
  for (const [k, e] of Object.entries(edges)) {
    const r = await h.render(g.component(e));
    out[k] = r.verdict + ':' + crypto.createHash('sha256').update(r.svg).digest('hex').slice(0, 16) + ':' + ((r.svg.match(/<text[^>]*>[^<]*<\/text>/g) || []).map(t => t.replace(/<[^>]+>/g, '')).filter(t => /uses|1|n|reads|say|hi|now|left|mid|right|ClickHouse/.test(t)).join('|')).slice(0, 120);
  }
  // also a class-diagram link with roles
  for (const [k, e] of Object.entries({ cls: 'a "1" --> "n" b : owns', clsq: 'a --> b : "1" owns "n"' })) {
    const r = await h.render(g.klass(e));
    out[k] = r.verdict + ':' + crypto.createHash('sha256').update(r.svg).digest('hex').slice(0, 16);
  }
  fs.writeFileSync(outFile, JSON.stringify(out, null, 1));
  process.exit(0);
})();
