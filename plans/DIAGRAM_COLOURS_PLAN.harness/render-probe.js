'use strict';
const path = require('path'), http = require('http'), fs = require('fs');
const SP = 'C:/Code/Kronikol/tools/render-bench';
const pw = require('C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package');
const engine = 'core-1.2026.8beta1-0e4f452.js';
const file = process.argv[2];
const html = `<!doctype html><html><head></head><body><div id="out"></div>
<script src="/viz-global.js"></script>
<script type="module">import {render} from '/${engine}';window.__render=(lines,id)=>render(lines,id,{});window.__ready=1;</script>
</body></html>`;
const server = http.createServer((req, res) => {
  const u = req.url.split('?')[0];
  if (u === '/index.html') { res.setHeader('content-type', 'text/html'); return res.end(html); }
  const p = path.join(SP, u);
  if (u === '/themes.js' || !fs.existsSync(p)) { console.log('HTTP 404 ' + u); res.statusCode = 404; return res.end(); }
  res.setHeader('content-type', 'application/javascript'); fs.createReadStream(p).pipe(res);
});
(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await pw.chromium.launch({ headless: true });
  const page = await browser.newPage();
  page.on('console', m => { if (m.type() !== 'log' && m.type() !== 'debug') console.log('CONSOLE[' + m.type() + '] ' + m.text().slice(0, 400)); });
  page.on('pageerror', e => console.log('PAGEERROR ' + e.message));
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction('window.__ready && window.__render', null, { timeout: 120000 });
  const lines = fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n').split('\n');
  const r = await page.evaluate(async ({ lines }) => {
    const out = document.getElementById('out');
    const done = new Promise(res => { const mo = new MutationObserver(() => { if (out.querySelector('svg') || out.textContent) { mo.disconnect(); res(); } }); mo.observe(out, { childList: true, subtree: true }); });
    window.__render(lines, 'out');
    await Promise.race([done, new Promise(r => setTimeout(r, 60000))]);
    await new Promise(r => setTimeout(r, 1500));
    const svg = out.querySelector('svg');
    return svg ? svg.outerHTML : ('NOSVG ' + out.textContent.slice(0, 200));
  }, { lines });
  if (r.startsWith('NOSVG')) console.log('RESULT ' + r); else { fs.writeFileSync(file.replace(/\.puml$/, '') + '.blocked.svg', r); console.log('RESULT svg ' + r.length + ' chars'); }
  await browser.close(); server.close();
})();
