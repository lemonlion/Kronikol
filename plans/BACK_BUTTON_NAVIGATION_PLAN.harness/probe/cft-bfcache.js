'use strict';
// Q12 (bfcache) in Playwright's bundled full Chromium (Chrome for Testing, channel 'chromium', new headless mode), because
// the headless shell's embedder disables bfcache (CDP reason BackForwardCacheDisabledForDelegate).
//
// Chrome for Testing runs Chrome's browser-process code, which on Windows may sign in with an empty password to learn
// whether the account's password is blank (about 10 s after launch) unless the profile's Local State already holds the
// answer. So this launches on a profile seeded by the repository's seedProfile() (tools/playwright/installed-browser.js),
// stays open past that 10 s, and the caller reads the account's BadPasswordAttempts counter before and after.
// Usage, from this folder: node cft-bfcache.js
const fs = require('fs');
const path = require('path');
const http = require('http');
const { pathToFileURL } = require('url');

const pw = require('C:/Users/cex/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package/index.js');
const { seedProfile } = require('C:/Code/Kronikol/tools/playwright/installed-browser.js');

const PAGES = path.join(__dirname, 'pages');
const RESULTS = path.join(__dirname, 'results');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function startServer() {
  return new Promise((resolve) => {
    const server = http.createServer((req, res) => {
      let p = decodeURIComponent(new URL(req.url, 'http://127.0.0.1').pathname);
      if (p === '/') p = '/report.html';
      const file = path.join(PAGES, path.normalize(p).replace(/^[/\\]+/, ''));
      if (!file.startsWith(PAGES)) { res.writeHead(403); res.end(); return; }
      fs.readFile(file, (err, data) => {
        if (err) { res.writeHead(404); res.end(); return; }
        res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'content-length': data.length });
        res.end(data);
      });
    });
    server.listen(0, '127.0.0.1', () => resolve(server));
  });
}

async function q12(context, base) {
  const page = await context.newPage();
  try {
    const cdp = await context.newCDPSession(page);
    await cdp.send('Page.enable');
    const notUsed = [];
    cdp.on('Page.backForwardCacheNotUsed', (e) => notUsed.push(...(e.notRestoredExplanations || []).map((x) => `${x.type}: ${x.reason}`)));
    await page.goto(base + 'report.html');
    await page.evaluate(`window.__jsState.counter = 42; history.pushState({k:'bf'}, '', '#bf')`);
    await page.evaluate('window.__log.length = 0');
    await page.click('#to-other');
    await page.waitForURL(/other\.html/);
    await sleep(500);
    await page.goBack({ waitUntil: 'commit' });
    await page.waitForFunction(() => window.__log && window.__log.some((e) => e.type === 'pageshow'), null, { timeout: 5000, polling: 200 }).catch(() => {});
    await sleep(300);
    return await page.evaluate(`(() => { const e = performance.getEntriesByType('navigation')[0];
      return { pageshow: window.__log.filter((x) => x.type === 'pageshow' || x.type === 'pagehide').map((x) => x.type + '(persisted=' + x.persisted + ')'),
               counter: window.__jsState.counter, hash: location.hash, state: history.state, navType: e && e.type,
               notRestoredReasons: e && e.notRestoredReasons ? JSON.parse(JSON.stringify(e.notRestoredReasons)) : null,
               userAgent: navigator.userAgent }; })()`).then((r) => ({ ...r, cdpNotUsed: notUsed }));
  } finally { await page.close().catch(() => {}); }
}

async function main() {
  const server = await startServer();
  const bases = { file: pathToFileURL(PAGES).href + '/', http: `http://127.0.0.1:${server.address().port}/` };
  const dir = path.join(__dirname, 'cft-profile');
  fs.rmSync(dir, { recursive: true, force: true });
  fs.mkdirSync(dir);
  seedProfile(dir);
  const out = { startedAt: new Date().toISOString(), seededLocalState: fs.readFileSync(path.join(dir, 'Local State'), 'utf8'), results: [] };
  const t0 = Date.now();
  let context;
  try {
    context = await pw.chromium.launchPersistentContext(dir, {
      channel: 'chromium', headless: true, viewport: { width: 1280, height: 720 },
      ignoreDefaultArgs: ['--disable-back-forward-cache'],
    });
    out.browserVersion = context.browser() ? context.browser().version() : null;
    for (let run = 1; run <= 2; run++) {
      for (const proto of ['file', 'http']) {
        let res;
        try { res = await q12(context, bases[proto]); } catch (e) { res = { harnessError: String(e.message).split('\n')[0] }; }
        out.results.push({ run, proto, res });
        console.log('run' + run, proto, JSON.stringify(res).replace(/file:\/\/\/[^"]*?\/pages\//g, 'file:///…/').slice(0, 900));
      }
    }
    // Stay open past the browser's delayed (about 10 s) blank-password check, so the counter read after is meaningful.
    while (Date.now() - t0 < 16000) await sleep(500);
    out.openForMs = Date.now() - t0;
  } finally {
    if (context) await context.close().catch(() => {});
    if (server.closeAllConnections) server.closeAllConnections();
    await new Promise((r) => server.close(() => r()));
    for (let i = 0; i < 10; i++) { try { fs.rmSync(dir, { recursive: true, force: true }); break; } catch { await sleep(300); } }
    out.profileRemoved = !fs.existsSync(dir);
  }
  fs.writeFileSync(path.join(RESULTS, 'cft-bfcache.json'), JSON.stringify(out, null, 1));
  console.log('open for', out.openForMs, 'ms; profile removed', out.profileRemoved, '; version', out.browserVersion);
}

main().catch((e) => { console.error(e); process.exitCode = 1; });
