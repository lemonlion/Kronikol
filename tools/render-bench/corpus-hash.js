'use strict';
// SHA-256 of the rendered <svg> for every corpus diagram, for one engine build.
// usage: node corpus-hash.js <engine-file>
const path = require('path'), http = require('http'), fs = require('fs'), crypto = require('crypto');
const SP = 'C:/Code/Kronikol/tools/render-bench';
const pw = require('C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package');
const engine = process.argv[2];
if (!engine) { console.error('usage: node corpus-hash.js <engine>'); process.exit(2); }

const files = [];
const realDir = path.join(SP, 'real');
for (const f of fs.readdirSync(realDir))
  if (f.endsWith('.puml')) files.push(path.join(realDir, f));
const smallDir = path.join(realDir, 'many-small');
if (fs.existsSync(smallDir))
  for (const f of fs.readdirSync(smallDir).slice(0, 10))
    if (f.endsWith('.puml')) files.push(path.join(smallDir, f));

const html = `<!doctype html><html><head></head><body><div id="out"></div>
<script src="/viz-global.js"></script>
<script type="module">import {render} from '/${path.basename(engine)}';window.__render=(l,i)=>render(l,i,{maxSvgSize:98304});window.__ready=1;</script>
</body></html>`;

const server = http.createServer((req, res) => {
  const u = req.url.split('?')[0];
  if (u === '/index.html') { res.setHeader('content-type', 'text/html'); return res.end(html); }
  const p = path.join(SP, u);
  if (!fs.existsSync(p)) { res.statusCode = 404; return res.end(); }
  res.setHeader('content-type', 'application/javascript');
  res.setHeader('cache-control', 'no-store');
  fs.createReadStream(p).pipe(res);
});

(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await pw.chromium.launch({ headless: true });
  const page = await browser.newPage();
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction('window.__ready && window.__render', null, { timeout: 120000 });

  for (const f of files) {
    const lines = fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n').split('\n');
    const r = await page.evaluate(async ({ lines }) => {
      const out = document.getElementById('out'); out.innerHTML = '';
      const done = new Promise(res => {
        const mo = new MutationObserver(() => {
          if (out.querySelector('svg') || out.textContent) { mo.disconnect(); res(); }
        });
        mo.observe(out, { childList: true, subtree: true });
      });
      let err = null;
      try { window.__render(lines, 'out'); } catch (e) { err = String((e && e.message) || e); }
      if (!err) await Promise.race([done, new Promise(r => setTimeout(r, 180000))]);
      const svg = out.querySelector('svg');
      return { err, svg: svg ? svg.outerHTML : null };
    }, { lines });
    const h = r.svg ? crypto.createHash('sha256').update(r.svg).digest('hex') : 'NO-SVG ' + r.err;
    console.log(`${path.basename(f).padEnd(26)} ${h}`);
  }
  await browser.close();
  server.close();
})();
