// webassembly-off-probe.js <shim page> [...]: renders a sequence diagram and three component diagrams (two elements,
// the Kronikol-shaped probe-component.puml, 40 services with 120 edges) through the shipped render script and records
// what each container holds: a drawing, the engine's error picture (an SVG too, "dot/GraphViz has crashed"), or text.
// The pages come from `emitter-corpus -- --shim <page> <workers>` (1 for the worker path, 0 for the main thread).
//   JSFLAGS=<flags>   Chromium --js-flags (--jitless turns WebAssembly off)
//   CSP=<policy>      a Content-Security-Policy meta written into the page (leave out 'wasm-unsafe-eval' to refuse
//                     compiling WebAssembly)
//   BROWSER=chromium|firefox|webkit, LAUNCH=<json launch options> (Firefox: {"firefoxUserPrefs":
//                     {"javascript.options.wasm":false}})
//   NOVIZ=1           a wrong viz-global.js hash in the page, so the shim drops Graphviz (Smetana without the fix)
//   ONLY=<names>      a comma list of the sources to render
// Prints one JSON report; results/webassembly-off-2026-09-26.txt is the run behind 3.31.3.
const fs = require('fs'), path = require('path'), url = require('url'), os = require('os');
const ROOT = path.resolve(__dirname, '../..');
const pw = require(path.join(ROOT, 'tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const VIZ_HASH = /VizGlobalJsIntegrity = "([^"]+)"/.exec(fs.readFileSync(path.join(ROOT, 'src/Kronikol/Constants/TrackingDefaults.cs'), 'utf8'))[1];

function bigComponent(n, edges) {
  const lines = ['@startuml', 'left to right direction', 'component "Caller" as caller'];
  for (let i = 0; i < n; i++) lines.push(`component "Service ${i}" as s${i}`);
  lines.push('caller --> s0 : "GET /start"');
  let e = 0;
  for (let i = 0; e < edges; i++) {
    const a = i % n, b = (i * 7 + 3) % n;
    if (a === b) continue;
    lines.push(`s${a} --> s${b} : "POST /op/${e}"`);
    e++;
  }
  lines.push('@enduml');
  return lines.join('\n');
}
const all = {
  sequence: '@startuml\nAlice -> Bob: hello\nBob --> Alice: ok\n@enduml',
  component: '@startuml\ncomponent Alpha\ncomponent Beta\nAlpha --> Beta\n@enduml',
  kronikolShape: fs.readFileSync(path.join(__dirname, 'probe-component.puml'), 'utf8').replace(/\r\n/g, '\n'),
  big40x120: bigComponent(40, 120),
};
const only = process.env.ONLY ? process.env.ONLY.split(',') : Object.keys(all);
const sources = Object.fromEntries(only.map(k => [k, all[k]]));

(async () => {
  const type = process.env.BROWSER || 'chromium';
  const launch = process.env.LAUNCH ? JSON.parse(process.env.LAUNCH) : {};
  if (process.env.JSFLAGS) launch.args = (launch.args || []).concat(['--js-flags=' + process.env.JSFLAGS]);
  const browser = await pw[type].launch(launch);
  const report = { browser: type, version: browser.version(), launch, noviz: !!process.env.NOVIZ, csp: process.env.CSP || null, pages: {} };
  for (const shim of process.argv.slice(2)) {
    let html = fs.readFileSync(shim, 'utf8');
    if (process.env.NOVIZ) {
      if (!html.includes(VIZ_HASH)) throw new Error('the page does not carry the viz hash');
      html = html.split(VIZ_HASH).join('sha256-' + 'A'.repeat(43) + '=');
    }
    if (process.env.CSP) html = html.replace('<meta charset="utf-8">', '<meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="' + process.env.CSP + '">');
    const pagePath = path.join(os.tmpdir(), 'webassembly-off-probe-' + path.basename(shim));
    fs.writeFileSync(pagePath, html);
    const page = await browser.newPage();
    const logs = [];
    page.on('console', m => { const t = m.text(); if (/Kronikol|PlantUML|WebAssembly|Viz|wasm|Smetana/i.test(t)) logs.push(m.type() + ': ' + t.slice(0, 300)); });
    page.on('pageerror', e => logs.push('pageerror: ' + String(e).slice(0, 300)));
    await page.goto(url.pathToFileURL(pagePath).href);
    const results = {};
    for (const [name, src] of Object.entries(sources)) {
      const t0 = Date.now();
      results[name] = await page.evaluate(([src, id]) => new Promise(resolve => {
        const el = document.createElement('div'); el.id = id; document.body.appendChild(el);
        const done = v => { clearInterval(t); clearTimeout(to); resolve(v); };
        const look = () => {
          const svg = el.querySelector('svg');
          const text = (el.textContent || '').trim();
          if (svg) done({ svg: true, crashed: /crashed|WebAssembly|Exception|Error/i.test(text), text: text.slice(0, 200) });
          else if (text) done({ svg: false, text: text.slice(0, 200) });
        };
        const t = setInterval(look, 50);
        const to = setTimeout(() => done({ timedOut: true }), 240000);
        window.plantuml.render(src.split('\n'), id);
      }), [src, 'probe-' + name]);
      results[name].ms = Date.now() - t0;
    }
    const telemetry = await page.evaluate(() => { const r = window.__kronikolRender; return { mode: r.mode, workers: r.workers, vizIntegrity: r.vizIntegrity, engineIntegrity: r.engineIntegrity, fallbackReason: r.fallbackReason, webAssembly: r.webAssembly }; });
    report.pages[path.basename(shim)] = { telemetry, results, logs };
    await page.close();
  }
  await browser.close();
  console.log(JSON.stringify(report, null, 1));
})().catch(e => { console.error(e); process.exit(1); });
