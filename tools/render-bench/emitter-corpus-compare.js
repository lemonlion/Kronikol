// emitter-corpus-compare.js: renders every .puml in a folder through the shipped plantuml-render.js --batch on
// two engine dirs and compares the SVG bytes per source; a second run on the first engine shows whether a
// source's SVG is stable run to run. Sources come from emitter-corpus/ (Kronikol's own emitter).
//   dotnet run --project emitter-corpus -- <corpus dir>
//   node emitter-corpus-compare.js <corpus dir> <label0>=<engine dir 0> <label1>=<engine dir 1>
// A source that stalls the engine takes every later source of its batch with it (plans/ENGINE_PIN_PLAN.md
// §1.15), so keep the creole-payload source out of the folder when comparing builds.
// Measured 2026-09-25: 13 of 13 identical, fork build and npm 1.2026.8 (results/emitter-corpus-2026-09-25.txt).
const fs = require('fs'), path = require('path'), cp = require('child_process'), crypto = require('crypto');
const RENDER = path.resolve(__dirname, '../../src/Kronikol/PlantUml/plantuml-render.js');
const [corpus, ...engineArgs] = process.argv.slice(2);
const engines = engineArgs.map(a => { const i = a.indexOf('='); return { label: a.slice(0, i), dir: a.slice(i + 1) }; });
const files = fs.readdirSync(corpus).filter(f => f.endsWith('.puml')).sort();
const input = files.map(f => JSON.stringify({ id: f, source: fs.readFileSync(path.join(corpus, f), 'utf8') })).join('\n') + '\n';
function batch(dir) {
  const t = Date.now();
  const r = cp.spawnSync(process.execPath, [RENDER, path.join(dir, 'viz-global.js'), path.join(dir, 'plantuml.js'), '--batch'],
    { input, encoding: 'utf8', maxBuffer: 1 << 30, timeout: 1800000 });
  const out = {};
  for (const line of (r.stdout || '').split('\n')) { if (!line.trim()) continue; const o = JSON.parse(line); out[o.id] = o; }
  return { out, ms: Date.now() - t, exit: r.status, cache: (/code cache: (\w+)/.exec(r.stderr || '') || [])[1] };
}
const h = s => crypto.createHash('sha256').update(s).digest('hex').slice(0, 12);
const runs = engines.map(e => ({ ...e, a: batch(e.dir) }));
const again = batch(engines[0].dir);
const rows = files.map(f => {
  const svgs = runs.map(e => (e.a.out[f] || {}).svg || null);
  const row = { file: f };
  runs.forEach((e, i) => { const o = e.a.out[f] || {}; row[e.label] = o.svg ? `${h(o.svg)} (${o.svg.length} B)` : `ERROR ${String(o.error || 'missing').slice(0, 70)}`; });
  row.identical = svgs[0] != null && svgs.every(s => s === svgs[0]);
  row.stableRunToRun = svgs[0] != null && (again.out[f] || {}).svg === svgs[0];
  row.viewBox = svgs[0] ? ((/viewBox="([^"]*)"/.exec(svgs[0]) || [])[1] || null) : null;
  return row;
});
console.log(JSON.stringify({ node: process.version, engines: runs.map(e => ({ label: e.label, dir: e.dir, ms: e.a.ms, exit: e.a.exit, cache: e.a.cache })) }, null, 1));
console.table(rows);
console.log('identical', rows.filter(r => r.identical).length, 'of', rows.length, '; stable run to run', rows.filter(r => r.stableRunToRun).length, 'of', rows.length);
