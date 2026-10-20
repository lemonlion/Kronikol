'use strict';
// Draws one source on several engine builds in a real browser page (the engine imported as a module, rendering into a
// div) and prints what the browser's DOM holds around the label. usage: node browser-link.js <browser> <source file>
// <name=engineDir>...
const fs = require('fs'), path = require('path'), http = require('http');
const { launch } = require('../mw/browsers');

const [browserName, sourceFile, ...engines] = process.argv.slice(2);
const source = fs.readFileSync(sourceFile, 'utf8').replace(/\r/g, '').split('\n').filter((l, i, a) => i < a.length - 1 || l);
const dirs = Object.fromEntries(engines.map(e => e.split('=')));

const server = http.createServer((req, res) => {
  const u = decodeURIComponent(req.url.split('?')[0]);
  if (u === '/') { res.writeHead(200, { 'content-type': 'text/html' }); res.end('<!doctype html><div id="t"></div>'); return; }
  const m = /^\/([^/]+)\/(.+)$/.exec(u);
  const file = m && dirs[m[1]] ? path.join(dirs[m[1]], m[2]) : null;
  if (!file || !fs.existsSync(file)) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': file.endsWith('.wasm') ? 'application/wasm' : 'text/javascript', 'access-control-allow-origin': '*' });
  fs.createReadStream(file).pipe(res);
});
server.listen(0, '127.0.0.1', async () => {
  const base = 'http://127.0.0.1:' + server.address().port;
  const b = await launch(browserName);
  try {
    for (const name of Object.keys(dirs)) {
      const p = await b.newPage();
      await p.goto(base + '/');
      const out = await p.evaluate(async ({ url, lines }) => {
        const errors = [];
        window.addEventListener('error', e => errors.push(String(e.message)));
        const mod = await import(url);
        const el = document.getElementById('t');
        el.innerHTML = '';
        const answer = mod.render(lines, 't', { maxSvgSize: 98304 });
        if (answer && answer.then) await answer.catch(e => errors.push('rejected: ' + e));
        for (let i = 0; i < 400 && !el.querySelector('svg'); i++) await new Promise(r => setTimeout(r, 25));
        await new Promise(r => setTimeout(r, 100));
        return { html: el.innerHTML, errors };
      }, { url: base + '/' + name + '/' + (fs.existsSync(path.join(dirs[name], 'plantuml-wasm.js')) && process.env.WASM ? 'plantuml-wasm.js' : 'plantuml.js'), lines: source });
      const s = out.html;
      const i = s.indexOf(process.env.NEEDLE || 'GET: /orders');
      console.log('== ' + name + ' (' + s.length + ' chars, errors: ' + JSON.stringify(out.errors) + ')');
      console.log(i < 0 ? 'NEEDLE NOT FOUND; message group: ' + (s.match(/<g class="message"[\s\S]*?<\/g>/g) || []).join(' | ').slice(0, 900)
        : s.slice(Math.max(0, i - 500), i + 80));
      await p.close();
    }
  } finally {
    await b.close();
    server.close();
  }
});
