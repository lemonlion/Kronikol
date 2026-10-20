// S21: a report shown in <iframe srcdoc> (how the Azure DevOps viewer extensions show an HTML artifact). Its URL is
// about:srcdoc but its base URL is the embedding page's, so a RELATIVE url passed to replaceState/pushState resolves
// against the parent. Measures, for each way of building the url: does the call throw, and what is location after.
// Bundled browsers only (launchInstalled falls through to launch() for them), headless; parent over http and file.
const fs = require('fs');
const http = require('http');
const path = require('path');
const { pathToFileURL } = require('url');
const pw = require('C:/Users/cex/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package/index.js');
const { launchInstalled } = require('C:/Code/Kronikol/tools/playwright/installed-browser.js');

const dir = __dirname;
// the report's own way today (report-url-hash-function.js:101), the bare fragment, and the absolute url with the fragment replaced
const child = `<!doctype html><meta charset="utf-8"><body><script>
  var out = {};
  function tryIt(name, build, fn) {
    try { history[fn](null, '', build()); out[name + '.' + fn] = 'ok ' + location.href; }
    catch (e) { out[name + '.' + fn] = 'THROWS ' + e.name + ': ' + String(e.message).slice(0, 160); }
  }
  tryIt('today', function () { return window.location.pathname + window.location.search + '#status=Failed'; }, 'replaceState');
  tryIt('fragment', function () { return '#status=Passed'; }, 'replaceState');
  tryIt('absolute', function () { return location.href.replace(/#.*$/, '') + '#sid-0123456789abcdef'; }, 'replaceState');
  tryIt('today', function () { return window.location.pathname + window.location.search + '#a'; }, 'pushState');
  tryIt('absolute', function () { return location.href.replace(/#.*$/, '') + '#b'; }, 'pushState');
  out.pathname = location.pathname; out.baseURI = document.baseURI; out.historyLength = history.length;
  parent.postMessage(JSON.stringify(out), '*');
<\/script></body>`;
const esc = child.replace(/&/g, '&amp;').replace(/"/g, '&quot;');
const parentHtml = sandbox => `<!doctype html><meta charset="utf-8"><title>parent</title><body>
<script>window.addEventListener('message', function (e) { document.title = 'DONE'; window.__result = e.data; });<\/script>
<iframe ${sandbox} srcdoc="${esc}"></iframe></body>`;
const variants = { plain: '', 'sandbox-scripts': 'sandbox="allow-scripts"', 'sandbox-scripts-same-origin': 'sandbox="allow-scripts allow-same-origin"' };
for (const [k, v] of Object.entries(variants)) fs.writeFileSync(path.join(dir, `parent-${k}.html`), parentHtml(v));

(async () => {
  const server = http.createServer((req, res) => {
    const f = path.join(dir, decodeURIComponent(req.url.split('?')[0]).replace(/^\//, ''));
    if (!fs.existsSync(f)) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' }); res.end(fs.readFileSync(f));
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const rows = [];
  try {
    for (const name of ['chromium', 'firefox', 'webkit']) {
      const browser = await launchInstalled(pw[name], { headless: true });
      const version = browser.version();
      for (const k of Object.keys(variants)) {
        for (const proto of ['http', 'file']) {
          const url = proto === 'http' ? `http://127.0.0.1:${port}/parent-${k}.html?build=1` : pathToFileURL(path.join(dir, `parent-${k}.html`)).href;
          const context = await browser.newContext();
          const page = await context.newPage();
          await page.goto(url);
          let result = null;
          try { await page.waitForFunction(() => document.title === 'DONE', null, { timeout: 5000, polling: 200 }); result = JSON.parse(await page.evaluate(() => window.__result)); }
          catch (e) { result = { error: String(e).split('\n')[0] }; }
          rows.push({ engine: name, version, frame: k, parent: proto, ...result });
          await context.close();
        }
      }
      await browser.close();
    }
  } finally { server.close(); }
  fs.writeFileSync(path.join(dir, 's21-results.json'), JSON.stringify(rows, null, 2));
  for (const r of rows) console.log(JSON.stringify(r));
})().catch(e => { console.error(e); process.exit(1); });
