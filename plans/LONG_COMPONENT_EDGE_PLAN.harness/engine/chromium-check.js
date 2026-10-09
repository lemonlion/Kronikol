'use strict';
// node chromium-check.js <engine.js> <shape> <len> [<len> ...]
// The same renders in headless Chromium (Playwright's build), on the page's main thread or in a module Web Worker
// (WORKER=1), with V8 flags from JSFLAGS (e.g. "--stack-size=400 --no-opt --no-maglev"). The engine is served as a
// copy whose JS-error wrapper records the RangeError's stack, so the cycle can be read in the browser too.
// Each length gets a fresh page (cold engine). Prints one JSON line per length.
const path = require('path'), http = require('http'), fs = require('fs');
const g = require('./gen');
const PW = 'C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package';
const pw = require(PW);
const [engine, shape, ...lens] = process.argv.slice(2);
const VIZ = 'C:/Users/cex/AppData/Local/Kronikol/plantuml-js/1.2026.8/viz-global.js';

let code = fs.readFileSync(engine, 'utf8');
let n = 0;
code = code.replace(/(let\s+\$rt_wrapException\s*=\s*err\s*=>\s*\{)/, (m) => { n++; return m + 'if(err instanceof RangeError&&!globalThis.__rs)globalThis.__rs=String(err.stack);'; });
if (!n) code = code.replace(/(let ([A-Za-z_$][\w$]*)=err=>\{let ex=err\[([A-Za-z_$][\w$]*)\];if\(!ex\)\{ex=)/, (m) => { n++; return m.replace('=>{', '=>{if(err instanceof RangeError&&!globalThis.__rs)globalThis.__rs=String(err.stack);'); });
if (!n) throw new Error('no wrapper');

const page1 = `<!doctype html><html><head></head><body><div id="out"></div>
<script src="/viz-global.js"></script>
<script type="module">import {render} from '/engine.js';window.__render=(lines,id)=>render(lines,id,{maxSvgSize:98304});window.__ready=1;</script></body></html>`;

const server = http.createServer((req, res) => {
  const u = req.url.split('?')[0];
  if (u === '/index.html') { res.setHeader('content-type', 'text/html'); return res.end(page1); }
  if (u === '/engine.js') { res.setHeader('content-type', 'application/javascript'); return res.end(code); }
  if (u === '/viz-global.js') { res.setHeader('content-type', 'application/javascript'); return fs.createReadStream(VIZ).pipe(res); }
  res.statusCode = 404; res.end();
});

(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const args = process.env.JSFLAGS ? ['--js-flags=' + process.env.JSFLAGS] : [];
  const browser = await pw.chromium.launch({ headless: true, args });
  for (const l of lens) {
    const { statement, source } = g.shapeSource(shape, Number(l));
    const page = await browser.newPage();
    await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
    await page.waitForFunction('window.__ready && window.__render', null, { timeout: 120000, polling: 200 });
    const r = await page.evaluate(async ({ lines }) => {
      const out = document.getElementById('out'); out.innerHTML = '';
      const done = new Promise(res => { const mo = new MutationObserver(() => { if (out.querySelector('svg') || out.textContent) { mo.disconnect(); res(); } }); mo.observe(out, { childList: true, subtree: true }); });
      let err = null;
      try { window.__render(lines, 'out'); } catch (e) { err = String(e && e.message || e); }
      if (!err) await Promise.race([done, new Promise(r => setTimeout(r, 60000))]);
      const svg = out.querySelector('svg');
      const text = (svg ? svg.textContent : out.textContent) || '';
      const st = globalThis.__rs || '';
      const frames = st.split('\n').slice(1).map(s => s.trim().replace(/^at /, '').replace(/ \(.*$/, ''));
      return { err, overflow: /Maximum call stack/.test(text + (err || '')), syntax: /Syntax Error/.test(text), frames: frames.length, top: frames.slice(0, 12) };
    }, { lines: source.split('\n') });
    const verdict = r.overflow ? 'S' : r.syntax ? 'E' : 'draw';
    process.stdout.write(JSON.stringify({ shape, len: Number(l), stmt: statement.length, verdict, frames: r.frames, top: process.env.TOP ? r.top : undefined, chromium: browser.version(), jsflags: process.env.JSFLAGS || '' }) + '\n');
    await page.close();
  }
  await browser.close(); server.close();
})().catch(e => { console.error(e); process.exit(1); });
