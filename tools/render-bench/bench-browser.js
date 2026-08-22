'use strict';
// Real-Chromium benchmark for plantuml.js builds, using the Playwright driver bundled with the .NET E2E tests.
// usage: node bench-browser.js <old|new> <plantuml-file> [sizes]
const path = require('path'), http = require('http'), fs = require('fs');
const { gen } = require('./gen.js');
const SP = __dirname;
const PW = 'C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package';
const pw = require(PW);
const kind = process.argv[2] || 'old';
const file = process.argv[3] || (kind === 'old' ? 'old-plantuml.js' : 'upstream-patched-plantuml.js');
const sizes = (process.argv[4] || '5,50,200,500').split(',').map(Number);

const instrument = `<script>${process.env.STL0 ? "Error.stackTraceLimit=0;" : ""}
window.__m={getBBox:0,getBBoxMs:0,measureText:0,measureTextMs:0,appendBody:0};
(function(){const ogb=SVGGraphicsElement.prototype.getBBox;SVGGraphicsElement.prototype.getBBox=function(){const t=performance.now();const r=ogb.call(this);window.__m.getBBoxMs+=performance.now()-t;window.__m.getBBox++;return r;};
const omt=CanvasRenderingContext2D.prototype.measureText;CanvasRenderingContext2D.prototype.measureText=function(s){const t=performance.now();const r=omt.call(this,s);window.__m.measureTextMs+=performance.now()-t;window.__m.measureText++;return r;};})();
</script>`;
const htmlOld = `<!doctype html><html><head>${instrument}</head><body><div id="out"></div>
<script src="/viz-global.js"></script><script src="/${file}"></script>
<script>window.__ready=new Promise(r=>plantumlLoad([],r)).then(()=>{window.__render=(lines,id)=>window.plantuml.render(lines,id);});</script>
</body></html>`;
const htmlNew = `<!doctype html><html><head>${instrument}</head><body><div id="out"></div>
<script src="/viz-global.js"></script>
<script type="module">import {render} from '/${file}';window.__render=(lines,id)=>render(lines,id,{});window.__ready=Promise.resolve();</script>
</body></html>`;

const server = http.createServer((req, res) => {
  const u = req.url.split('?')[0];
  if (u === '/index.html') { res.setHeader('content-type', 'text/html'); return res.end(kind === 'old' ? htmlOld : htmlNew); }
  const p = path.join(SP, u); if (!fs.existsSync(p)) { res.statusCode = 404; return res.end(); }
  res.setHeader('content-type', 'application/javascript'); res.setHeader('cache-control', 'no-store'); fs.createReadStream(p).pipe(res);
});

(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await pw.chromium.launch({ headless: true });
  const page = await browser.newPage();
  page.on('pageerror', e => console.error('PAGEERROR', e.message));
  const R = { kind, file, renders: [] };
  let a = performance.now();
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  R.gotoLoadMs = Math.round(performance.now() - a);
  a = performance.now();
  await page.waitForFunction('window.__ready && window.__render', null, { timeout: 120000 });
  await page.evaluate(() => window.__ready);
  R.readyAfterLoadMs = Math.round(performance.now() - a);
  R.resourceTimings = await page.evaluate(() => performance.getEntriesByType('resource').map(e => ({ name: e.name.split('/').pop(), ms: Math.round(e.duration), kb: Math.round((e.encodedBodySize || 0) / 1024) })));
  for (const n of sizes) for (let rep = 0; rep < 2; rep++) {
    const lines = gen(n);
    const r = await page.evaluate(async ({ lines, id }) => {
      const out = document.getElementById('out'); out.innerHTML = '';
      const m0 = { ...window.__m };
      const t0 = performance.now();
      let err = null;
      const done = new Promise(res => { const mo = new MutationObserver(() => { if (out.querySelector('svg') || out.textContent) { mo.disconnect(); res(); } }); mo.observe(out, { childList: true, subtree: true }); });
      try { window.__render(lines, id); } catch (e) { err = String(e && e.message || e); }
      if (!err) await Promise.race([done, new Promise(r => setTimeout(r, 180000))]);
      const ms = performance.now() - t0;
      const svg = out.querySelector('svg');
      return { ms: Math.round(ms), err, svgKB: svg ? Math.round(svg.outerHTML.length / 1024) : 0, h: svg ? svg.getAttribute('height') : null, getBBox: window.__m.getBBox - m0.getBBox, getBBoxMs: Math.round(window.__m.getBBoxMs - m0.getBBoxMs), measureText: window.__m.measureText - m0.measureText, measureTextMs: Math.round(window.__m.measureTextMs - m0.measureTextMs), text: err ? null : (svg ? null : out.textContent.slice(0, 120)) };
    }, { lines, id: 'out' });
    R.renders.push({ arrows: n, rep, ...r });
  }
  R.jsHeapMB = await page.evaluate(() => performance.memory ? Math.round(performance.memory.usedJSHeapSize / 1048576) : null);
  await browser.close(); server.close();
  const { renders, resourceTimings, ...rest } = R;
  console.log(JSON.stringify(rest)); console.log(JSON.stringify(resourceTimings)); console.table(renders);
  process.exit(0);
})().catch(e => { console.error('FATAL', e && e.stack || e); process.exit(1); });
