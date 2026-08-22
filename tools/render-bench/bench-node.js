'use strict';
// Node renderer experiments: per-diagram process spawn (current NodeJsPlantUmlRenderer behaviour) vs one warm process (batch),
// with and without V8 code cache for the engine compile.
const { spawnSync } = require('child_process');
const fs = require('fs'), path = require('path'), vm = require('vm');
const SP = __dirname;
const LOCAL = path.join(process.env.LOCALAPPDATA, 'Kronikol', 'plantuml-js');
const files = ['puml-0', 'puml-2', 'puml-5', 'puml-19', 'puml-1'].map(n => path.join(SP, 'real', n + '.puml'));
const mode = process.argv[2] || 'spawn';

if (mode === 'spawn') {
  const rows = [];
  for (const f of files) {
    const src = fs.readFileSync(f, 'utf8');
    const t0 = performance.now();
    const r = spawnSync('node', [path.join(LOCAL, 'plantuml-render.js'), path.join(LOCAL, 'viz-global.js'), path.join(LOCAL, 'plantuml.js')], { input: src, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
    rows.push({ file: path.basename(f), ms: Math.round(performance.now() - t0), ok: r.status === 0 && r.stdout.includes('<svg'), svgKB: Math.round(r.stdout.length / 1024), err: (r.stderr || '').slice(0, 80) });
  }
  console.log('spawn-per-diagram'); console.table(rows); console.log('total ms', rows.reduce((a, r) => a + r.ms, 0));
} else {
  // batch: one process, engine loaded once. mode 'batch' or 'batch-cache'
  const { setup, loadViz, makeTarget, t } = require('./bench-common.js');
  setup();
  (async () => {
    const T0 = t();
    const R = {};
    await loadViz(R);
    const code = fs.readFileSync(path.join(SP, 'old-plantuml.js'), 'utf8');
    const cacheFile = path.join(SP, 'results', 'engine.v8cache');
    let a = t();
    let script;
    if (mode === 'batch-cache' && fs.existsSync(cacheFile)) {
      script = new vm.Script(code, { filename: 'plantuml.js', cachedData: fs.readFileSync(cacheFile) });
      R.cacheRejected = script.cachedDataRejected;
    } else {
      script = new vm.Script(code, { filename: 'plantuml.js' });
      if (mode === 'batch-cache') fs.writeFileSync(cacheFile, script.createCachedData());
    }
    R.compileMs = Math.round(t() - a);
    const origLog = console.log; console.log = () => {};
    a = t(); script.runInThisContext(); R.runMs = Math.round(t() - a);
    a = t(); await new Promise(res => globalThis.plantumlLoad([], res)); R.loadMs = Math.round(t() - a);
    const rows = [];
    for (const f of files) {
      const src = fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n');
      const id = 'd' + rows.length; const { box } = makeTarget(id);
      const s0 = t();
      globalThis.plantuml.render(src.split('\n'), id);
      while (!(box.svg && box.svg.includes('<svg'))) { await new Promise(r => setTimeout(r, 1)); if (t() - s0 > 60000) break; }
      rows.push({ file: path.basename(f), ms: Math.round(t() - s0), svgKB: Math.round(box.svg.length / 1024) });
    }
    console.log = origLog;
    R.totalMs = Math.round(t() - T0);
    console.log(mode, JSON.stringify(R)); console.table(rows);
    process.exit(0);
  })().catch(e => { console.error('FATAL', e && e.stack || e); process.exit(1); });
}
