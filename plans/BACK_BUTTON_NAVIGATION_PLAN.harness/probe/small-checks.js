'use strict';
// Two small facts, http only, once per engine: node small-checks.js
//  1. CSS scroll anchoring support (CSS.supports('overflow-anchor', 'none')).
//  2. history.scrollRestoration of a new entry made, from a 'manual' entry, by pushState, by location.hash, by a link click.
const fs = require('fs');
const path = require('path');
const http = require('http');

const pw = require('C:/Users/cex/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package/index.js');
const { launchInstalled } = require('C:/Code/Kronikol/tools/playwright/installed-browser.js');
const PAGES = path.join(__dirname, 'pages');

function startServer() {
  return new Promise((resolve) => {
    const server = http.createServer((req, res) => {
      const file = path.join(PAGES, path.normalize(decodeURIComponent(new URL(req.url, 'http://x').pathname)).replace(/^[/\\]+/, ''));
      if (!file.startsWith(PAGES)) { res.writeHead(403); res.end(); return; }
      fs.readFile(file, (err, data) => { if (err) { res.writeHead(404); res.end(); return; }
        res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' }); res.end(data); });
    });
    server.listen(0, '127.0.0.1', () => resolve(server));
  });
}

async function main() {
  const server = await startServer();
  const base = `http://127.0.0.1:${server.address().port}/`;
  const out = {};
  try {
    for (const name of ['chromium', 'firefox', 'webkit']) {
      const browser = await launchInstalled(pw[name], { headless: true });
      try {
        const context = await browser.newContext();
        const page = await context.newPage();
        await page.goto(base + 'report.html');
        const r = { version: browser.version() };
        r.overflowAnchor = await page.evaluate("CSS.supports('overflow-anchor', 'none')");
        r.modes = {};
        for (const [k, act] of [['pushState', "history.pushState(null, '', '#p1')"], ['locationHash', "location.hash = 'c'"], ['linkClick', null]]) {
          await page.goto(base + 'report.html');
          await page.evaluate("history.scrollRestoration = 'manual'");
          if (act) await page.evaluate(act); else await page.click('#link');
          await page.waitForTimeout(200);
          r.modes[k] = await page.evaluate('({ hash: location.hash, mode: history.scrollRestoration })');
        }
        out[name] = r;
        console.log(name, JSON.stringify(r));
      } finally { await browser.close().catch(() => {}); }
    }
  } finally {
    await new Promise((r) => server.close(() => r()));
  }
  fs.writeFileSync(path.join(__dirname, 'results', 'small-checks.json'), JSON.stringify(out, null, 1));
}

main().catch((e) => { console.error(e); process.exitCode = 1; });
