'use strict';
// Compare main-thread render vs worker render for the same sources (old engine).
// usage: node fidelity.js [engineFile] [lazyViz=0|1]
const path = require('path'), http = require('http'), fs = require('fs');
const { gen } = require('./gen.js');
const SP = __dirname;
const pw = require('C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package');
const engine = process.argv[2] || 'old-plantuml.js';
const esm = !/^old-/.test(engine);
const lazyViz = process.argv[3] === '1';

const sources = {
  gen50: gen(50).join('\n'),
  'puml-1': fs.readFileSync(path.join(SP, 'real', 'puml-1.puml'), 'utf8'),
  'puml-0': fs.readFileSync(path.join(SP, 'real', 'puml-0.puml'), 'utf8'),
  'puml-19': fs.readFileSync(path.join(SP, 'real', 'puml-19.puml'), 'utf8'),
};
const html = `<!doctype html><html><body><div id="out"></div>
<script src="/viz-global.js"></script>
${esm ? `<script type="module">import {render} from '/${engine}'; window.plantuml={render:(l,i)=>render(l,i,{})}; window.__ready=true;</script>`
      : `<script src="/${engine}"></script><script>plantumlLoad([], function(){ window.__ready = true; });</script>`}
</body></html>`;
const server = http.createServer((req, res) => {
  const u = req.url.split('?')[0];
  if (u === '/') { res.setHeader('content-type', 'text/html'); return res.end(html); }
  const p = path.join(SP, u); if (!fs.existsSync(p)) { res.statusCode = 404; return res.end(); }
  res.setHeader('content-type', 'application/javascript'); res.setHeader('cache-control', 'no-store'); fs.createReadStream(p).pipe(res);
});
function stats(svg) {
  if (!svg) return null;
  const count = (re) => (svg.match(re) || []).length;
  const w = (svg.match(/<svg[^>]*\swidth="([^"]+)"/) || [])[1], h = (svg.match(/<svg[^>]*\sheight="([^"]+)"/) || [])[1];
  const vb = (svg.match(/viewBox="([^"]+)"/) || [])[1];
  return { len: svg.length, w, h, vb, elements: count(/<[a-zA-Z]/g), texts: count(/<text/g), paths: count(/<path/g), rects: count(/<rect/g), lines: count(/<line/g), polygons: count(/<polygon/g) };
}
(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await pw.chromium.launch({ headless: true });
  const page = await browser.newPage();
  page.on('pageerror', e => console.error('PAGEERROR', e.message));
  page.on('console', m => { if (m.type() === 'error') console.error('CONSOLE', m.text().slice(0, 300)); });
  await page.goto(`http://127.0.0.1:${port}/`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__ready, null, { timeout: 60000 });
  const results = {};
  for (const [name, src] of Object.entries(sources)) {
    const main = await page.evaluate(async (src) => {
      const out = document.getElementById('out'); out.innerHTML = '';
      const t0 = performance.now();
      await new Promise(res => { const mo = new MutationObserver(() => { if (out.querySelector('svg')) { mo.disconnect(); setTimeout(res, 0); } }); mo.observe(out, { childList: true, subtree: true }); window.plantuml.render(src.split('\n'), 'out'); });
      return { ms: Math.round(performance.now() - t0), svg: out.innerHTML };
    }, src);
    const worker = await page.evaluate(async ({ src, engine, lazyViz, esm }) => {
      if (!window.__w) {
        window.__w = new Worker('/puml-worker.js');
        await new Promise((res, rej) => { window.__w.onmessage = ev => { if (ev.data.type === 'ready') res(); if (ev.data.type === 'fatal') rej(new Error(ev.data.message)); }; window.__w.postMessage({ type: 'init', viz: '/viz-global.js', engine: '/' + engine, esm, lazyViz }); });
      }
      const t0 = performance.now();
      return await new Promise(res => { window.__w.onmessage = ev => { const m = ev.data; if (m.type === 'done') res({ ms: Math.round(performance.now() - t0), workerMs: m.ms, svg: m.svg }); else if (m.type === 'error') res({ ms: Math.round(performance.now() - t0), error: m.message }); }; window.__w.postMessage({ type: 'render', seq: 1, id: 'x', lines: src.split('\n') }); });
    }, { src, engine, lazyViz, esm });
    const ms = stats(main.svg), ws = stats(worker.svg);
    // visual check: inject worker svg into page and see that the browser parses the same number of elements
    const parsed = worker.svg ? await page.evaluate((svg) => { const d = document.createElement('div'); d.innerHTML = svg; return { elements: d.querySelectorAll('*').length, texts: d.querySelectorAll('text').length }; }, worker.svg) : null;
    results[name] = { main: { ms: main.ms, ...ms }, worker: { ms: worker.ms, workerMs: worker.workerMs, error: worker.error, ...ws, parsedElements: parsed && parsed.elements, parsedTexts: parsed && parsed.texts } };
    if (main.svg && worker.svg) { fs.writeFileSync(path.join(SP, 'results', `fid-${name}-main.svg`), main.svg); fs.writeFileSync(path.join(SP, 'results', `fid-${name}-worker.svg`), worker.svg); }
  }
  await browser.close(); server.close();
  for (const [k, v] of Object.entries(results)) { console.log('=== ' + k); console.table([{ who: 'main', ...v.main }, { who: 'worker', ...v.worker }]); }
  process.exit(0);
})().catch(e => { console.error('FATAL', e && e.stack || e); process.exit(1); });
