'use strict';
// Follow-ups to run.js. Usage, from this folder: node extras.js <engine> <case>
//   bfcache-cdp   (chromium, chromium-real) why Chromium did not restore from bfcache: CDP Page.backForwardCacheNotUsed
//   ratelimit     (any) after 500 replaceState calls, poll once a second for up to 12 s until a call takes effect again
// Writes results/extras-<engine>-<case>.json. Bundled browsers only, as in run.js.
const fs = require('fs');
const path = require('path');
const http = require('http');
const { pathToFileURL } = require('url');

const pw = require('C:/Users/cex/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package/index.js');
const { launchInstalled } = require('C:/Code/Kronikol/tools/playwright/installed-browser.js');

const PAGES = path.join(__dirname, 'pages');
const RESULTS = path.join(__dirname, 'results');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const firstLine = (s) => String(s || '').split('\n')[0].slice(0, 400);

const ENGINES = {
  chromium: () => launchInstalled(pw.chromium, { headless: true }),
  'chromium-real': () => launchInstalled(pw.chromium, { headless: true,
    ignoreDefaultArgs: ['--disable-back-forward-cache', '--disable-ipc-flooding-protection'] }),
  firefox: () => launchInstalled(pw.firefox, { headless: true }),
  // Firefox with the stock value of the one Playwright pref that changes file:// origins (playwright.cfg sets it false).
  'firefox-strict': () => launchInstalled(pw.firefox, { headless: true,
    firefoxUserPrefs: { 'security.fileuri.strict_origin_policy': true } }),
  webkit: () => launchInstalled(pw.webkit, { headless: true }),
};

const TRY = (code) => `(() => {
  try { ${code}; return { ok: true, href: location.href.replace(/^.*\\/pages\\//, '…/'), hash: location.hash }; }
  catch (e) { return { ok: false, err: e.name + ': ' + e.message.replace(/file:\\/\\/\\/[^'"\\s]*\\/pages\\//g, 'file:///…/') }; }
})()`;
const SNAP = `(() => ({ hash: location.hash, state: history.state, len: history.length, sr: history.scrollRestoration,
  navType: (performance.getEntriesByType('navigation')[0] || {}).type }))()`;

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

const cases = {};

cases['bfcache-cdp'] = async (browser, base) => {
  const context = await browser.newContext();
  try {
    const page = await context.newPage();
    const cdp = await context.newCDPSession(page);
    await cdp.send('Page.enable');
    const notUsed = [];
    cdp.on('Page.backForwardCacheNotUsed', (e) => notUsed.push({
      explanations: (e.notRestoredExplanations || []).map((x) => `${x.type}: ${x.reason}${x.context ? ' (' + x.context + ')' : ''}`),
    }));
    await page.goto(base + 'report.html');
    await page.evaluate(`window.__jsState.counter = 42; history.pushState({k:'bf'}, '', '#bf')`);
    await page.click('#to-other');
    await page.waitForURL(/other\.html/);
    await sleep(300);
    await page.goBack({ waitUntil: 'commit' });
    await sleep(800);
    return { notUsed, counter: await page.evaluate('window.__jsState.counter'),
             pageshow: await page.evaluate("window.__log.filter((e) => e.type === 'pageshow').map((e) => e.persisted)") };
  } finally { await context.close().catch(() => {}); }
};

cases.ratelimit = async (browser, base) => {
  const context = await browser.newContext();
  try {
    const page = await context.newPage();
    await page.goto(base + 'report.html');
    // Burst and polls both run in page-owned timer callbacks (never directly in page.evaluate).
    await page.evaluate(`window.__rw = null; setTimeout(function () {
      var o = { firstError: null, firstErrorAt: null, firstIgnoredAt: null, polls: [] };
      for (var i = 0; i < 500; i++) {
        try { history.replaceState(null, '', '#n' + i); if (location.hash !== '#n' + i && o.firstIgnoredAt === null) o.firstIgnoredAt = i; }
        catch (e) { if (o.firstErrorAt === null) { o.firstErrorAt = i; o.firstError = e.name + ': ' + e.message; } }
      }
      var t0 = performance.now(), s = 0;
      window.__rw = o;
      var iv = setInterval(function () {
        s++; var h = '#poll' + s, p = { sinceBurstMs: Math.round(performance.now() - t0) };
        try { history.replaceState(null, '', h); p.works = location.hash === h; } catch (e) { p.works = false; p.err = e.name; }
        o.polls.push(p);
        if (p.works || s >= 12) { clearInterval(iv); o.done = true; o.reopenedAfterMs = p.works ? p.sinceBurstMs : null; }
      }, 1000);
    }, 0)`);
    await page.waitForFunction('window.__rw && window.__rw.done', null, { polling: 250, timeout: 20000 });
    return await page.evaluate('window.__rw');
  } finally { await context.close().catch(() => {}); }
};

// The 500-call loops again, but run by a page-owned timer callback instead of directly by page.evaluate (in Firefox,
// juggler runs evaluated code through the Debugger API, and Firefox exempts system callers from its rate limit).
cases['ratelimit-page'] = async (browser, base) => {
  const out = {};
  for (const kind of ['replaceState', 'pushState']) {
    const context = await browser.newContext();
    try {
      const page = await context.newPage();
      const warnings = [];
      page.on('console', (m) => { if (!m.text().startsWith('EVT ')) warnings.push(m.type() + ': ' + m.text().slice(0, 300)); });
      await page.goto(base + 'report.html');
      await page.evaluate(`window.__rl = null; setTimeout(function () {
        var o = { firstErrorAt: null, firstError: null, errors: 0, firstIgnoredAt: null, ignored: 0 };
        for (var i = 0; i < 500; i++) {
          try { history.${kind}(null, '', '#n' + i);
                if (location.hash !== '#n' + i) { o.ignored++; if (o.firstIgnoredAt === null) o.firstIgnoredAt = i; } }
          catch (e) { o.errors++; if (o.firstErrorAt === null) { o.firstErrorAt = i; o.firstError = e.name + ': ' + e.message; } }
        }
        o.lenNow = history.length; o.hashNow = location.hash; window.__rl = o;
        setTimeout(function () { o.lenLater = history.length; o.hashLater = location.hash; o.done = true; }, 1000);
      }, 0)`);
      await page.waitForFunction('window.__rl && window.__rl.done', null, { polling: 200, timeout: 15000 });
      out[kind] = await page.evaluate('window.__rl');
      out[kind].console = warnings.slice(0, 4);
    } finally { await context.close().catch(() => {}); }
  }
  return out;
};

// Does history.state survive a reload? By page.reload() (Firefox: the browser's Reload command), by the page's own
// location.reload(), after replaceState and after pushState.
cases['reload-state'] = async (browser, base) => {
  const out = {};
  const variants = {
    replaceThenPageReload: [`history.replaceState({k:'replaced'}, '')`, 'page'],
    pushThenPageReload: [`history.pushState({k:'pushed'}, '', '#r')`, 'page'],
    pushThenLocationReload: [`history.pushState({k:'pushed'}, '', '#r')`, 'location'],
  };
  for (const [k, [setup, how]] of Object.entries(variants)) {
    const context = await browser.newContext();
    try {
      const page = await context.newPage();
      await page.goto(base + 'report.html');
      await page.evaluate(`history.scrollRestoration = 'manual'; ${setup}`);
      const before = await page.evaluate(SNAP);
      if (how === 'page') await page.reload();
      else await Promise.all([page.waitForEvent('load'), page.evaluate('setTimeout(() => location.reload(), 0)')]);
      await sleep(300);
      out[k] = { before, after: await page.evaluate(SNAP) };
    } finally { await context.close().catch(() => {}); }
  }
  return out;
};

// One real click on the first entry; two entries pushed 6 s later with no activation; then page.goBack().
cases.x2 = async (browser, base) => {
  const context = await browser.newContext();
  try {
    const page = await context.newPage();
    const events = [];
    page.on('console', (m) => { const t = m.text(); if (t.startsWith('EVT ')) { try { events.push(JSON.parse(t.slice(4))); } catch { /* */ } } });
    await page.goto(base + 'noact-click.html');
    await page.click('#go');
    const t0 = Date.now();
    while (Date.now() - t0 < 12000 && !events.some((e) => e.type === 'pushed' && e.n === 2)) await sleep(100);
    await sleep(300);
    let goBack;
    try { await page.goBack({ timeout: 5000 }); goBack = 'resolved'; } catch (e) { goBack = firstLine(e.message); }
    await sleep(600);
    const after = await page.evaluate(SNAP);
    return { goBack, landedOn: after.hash || '(no hash)', len: after.len,
             events: events.filter((e) => /pushed|clicked|popstate|hashchange/.test(e.type)).map((e) => `${e.type}${e.n || ''}(${e.hash || 'no hash'})`) };
  } finally { await context.close().catch(() => {}); }
};

// file:// origin checks that playwright.cfg's security.fileuri.strict_origin_policy=false can change (file only).
cases['file-origin'] = async (browser, base) => {
  if (!base.startsWith('file:')) return { skipped: 'file only' };
  const out = {};
  for (const [k, url] of [['query', '?x=1#a'], ['path', 'other.html'], ['subPath', 'sub/other.html']]) {
    const context = await browser.newContext();
    try {
      const page = await context.newPage();
      await page.goto(base + 'report.html');
      out[k] = await page.evaluate(TRY(`history.pushState(null, '', ${JSON.stringify(url)})`));
      // The same push from a page-owned timer callback rather than from the evaluate call itself.
      await page.goto(base + 'report.html');
      await page.evaluate(`window.__p = null; setTimeout(function () { window.__p = ${TRY(`history.pushState(null, '', ${JSON.stringify(url)})`)}; }, 0)`);
      await page.waitForFunction('window.__p', null, { polling: 100, timeout: 5000 });
      out[k + 'FromTimer'] = await page.evaluate('window.__p');
    } finally { await context.close().catch(() => {}); }
  }
  for (const [k, file] of [['iframe', 'parent.html'], ['sandboxIframe', 'parent-sb.html']]) {
    const context = await browser.newContext();
    try {
      const page = await context.newPage();
      await page.goto(base + file);
      out[k + 'ParentReadsChild'] = await page.evaluate(TRY(`document.getElementById('f').contentDocument.title`));
      const frame = page.frames().find((f) => f !== page.mainFrame());
      out[k + 'ChildOrigin'] = await frame.evaluate('self.origin');
      out[k + 'ChildPush'] = await frame.evaluate(TRY(`history.pushState(null, '', '#f1')`));
    } finally { await context.close().catch(() => {}); }
  }
  return out;
};

// Q5 disambiguated: the #target entry's own scroll position (6500) differs from #target's position (6000), so a
// Forward that lands on 6500 restored the entry's scroll, on 6000 scrolled to the fragment, on 300 did neither.
cases.q5b = async (browser, base) => {
  const out = {};
  for (const mode of ['auto', 'manual']) {
    const context = await browser.newContext({ viewport: { width: 1280, height: 720 } });
    try {
      const page = await context.newPage();
      await page.goto(base + 'report.html');
      await page.evaluate(`history.scrollRestoration = '${mode}'; scrollTo(0, 800)`);
      await sleep(250);
      await page.click('#link');
      await sleep(300);
      await page.evaluate('scrollTo(0, 6500)');
      await sleep(250);
      const atTarget = await page.evaluate('Math.round(scrollY)');
      await page.goBack();
      await sleep(400);
      const afterBack = await page.evaluate('Math.round(scrollY)');
      await page.evaluate('scrollTo(0, 300)');
      await sleep(250);
      await page.goForward();
      await sleep(400);
      const afterFwd = await page.evaluate('({ y: Math.round(scrollY), hash: location.hash })');
      out[mode] = { atTarget, afterBack, afterFwd: afterFwd.y, hash: afterFwd.hash,
        forwardMeans: afterFwd.y === 6500 ? 'restored the entry scroll' : afterFwd.y === 6000 ? 'scrolled to the fragment' : afterFwd.y === 300 ? 'did not scroll' : 'other' };
    } finally { await context.close().catch(() => {}); }
  }
  return out;
};

// ---- final batch: reloads by location.reload() (Firefox's page.reload() over http adds an entry instead of reloading) ----
async function frames(page) {
  try {
    await page.evaluate(() => new Promise((resolve) => {
      let done = false; const fin = () => { if (!done) { done = true; resolve(); } };
      requestAnimationFrame(() => requestAnimationFrame(fin)); setTimeout(fin, 500);
    }));
  } catch { /* navigation in flight */ }
}
function eventLog(page) {
  const events = [];
  page.on('console', (m) => { const t = m.text(); if (t.startsWith('EVT ')) { try { events.push(JSON.parse(t.slice(4))); } catch { /* */ } } });
  return events;
}
const locationReload = (page) => Promise.all([page.waitForEvent('load'), page.evaluate('setTimeout(() => location.reload(), 0)')]);
const TOP = `({ y: Math.round(scrollY), top: __topSection(), layout: __layout() })`;

// Q7's reload variants with location.reload().
cases.q7reload = async (browser, base) => {
  const out = {};
  for (const [k, mode, restore] of [['reloadAuto', 'auto', null], ['reloadManualA', 'manual', 'a'], ['reloadManualB', 'manual', 'b']]) {
    const context = await browser.newContext({ viewport: { width: 1280, height: 720 } });
    try {
      const page = await context.newPage();
      const events = eventLog(page);
      await page.goto(base + 'cv.html'); await frames(page); await sleep(200);
      await page.evaluate(`history.scrollRestoration = '${mode}'`);
      await page.evaluate(`document.getElementById('s250').scrollIntoView({ block: 'start' })`);
      await frames(page); await sleep(600);
      const saved = await page.evaluate(TOP);
      await page.evaluate(`history.replaceState({ savedY: ${saved.y}, sec: '${saved.top.id}', offset: ${saved.top.offset} }, '')`);
      events.length = 0;
      await locationReload(page);
      await frames(page); await sleep(400);
      const browserRes = await page.evaluate(`(() => { const o = ${TOP}; o.state = history.state; o.sr = history.scrollRestoration;
        o.navType = performance.getEntriesByType('navigation')[0].type; return o; })()`);
      const r = { saved, browser: browserRes, yAtPageshow: events.filter((e) => e.type === 'pageshow').map((e) => e.y) };
      if (restore) {
        if (restore === 'a') await page.evaluate('scrollTo(0, history.state.savedY)');
        else await page.evaluate(`(() => { const st = history.state; document.getElementById(st.sec).scrollIntoView({ block: 'start' }); scrollBy(0, st.offset); })()`);
        r.restoredNow = await page.evaluate(TOP);
        await frames(page); await sleep(400);
        r.restored = await page.evaluate(TOP);
        await sleep(600);
        r.restoredLate = await page.evaluate(TOP);
      }
      out[k] = r;
    } finally { await context.close().catch(() => {}); }
  }
  return out;
};

// Q11 with location.reload().
cases.q11b = async (browser, base) => {
  const out = {};
  for (const mode of ['auto', 'manual']) {
    for (const [fk, frag] of [['noTarget', '#r'], ['targetC', '#c']]) {
      const context = await browser.newContext({ viewport: { width: 1280, height: 720 } });
      try {
        const page = await context.newPage();
        const events = eventLog(page);
        await page.goto(base + 'report.html'); await frames(page);
        await page.evaluate(`history.scrollRestoration = '${mode}'`);
        await page.evaluate('scrollTo(0, 2000)'); await frames(page); await sleep(200);
        await page.evaluate(`history.pushState({k:'reload'}, '', '${frag}')`);
        await page.evaluate('scrollTo(0, 2500)'); await frames(page); await sleep(300);
        events.length = 0;
        await locationReload(page);
        await frames(page); await sleep(400);
        const after = await page.evaluate(SNAP);
        after.y = await page.evaluate('Math.round(scrollY)');
        await sleep(700);
        out[`${mode}_${fk}`] = { after, yAtPageshow: events.filter((e) => e.type === 'pageshow').map((e) => e.y),
                                 yLate: await page.evaluate('Math.round(scrollY)') };
      } finally { await context.close().catch(() => {}); }
    }
  }
  return out;
};

// X1 again with a 1 s pause between the second push and page.goBack().
cases.x1b = async (browser, base) => {
  const context = await browser.newContext();
  try {
    const page = await context.newPage();
    const events = eventLog(page);
    await page.goto(base + 'noact.html');
    const t0 = Date.now();
    while (Date.now() - t0 < 5000 && !events.some((e) => e.type === 'pushed' && e.n === 2)) await sleep(50);
    await sleep(1000);
    let goBack;
    try { await page.goBack({ timeout: 5000 }); goBack = 'resolved'; } catch (e) { goBack = firstLine(e.message); }
    await sleep(500);
    const after = await page.evaluate(SNAP);
    return { goBack, landedOn: after.hash || '(no hash)', len: after.len };
  } finally { await context.close().catch(() => {}); }
};

// history.length over time after 500 pushState calls made by a page-owned timer.
cases.histlen = async (browser, base) => {
  const context = await browser.newContext();
  try {
    const page = await context.newPage();
    await page.goto(base + 'report.html');
    await page.evaluate(`window.__hl = null; setTimeout(function () {
      var o = { atLoad: history.length, samples: [] }; var errs = 0;
      for (var i = 0; i < 500; i++) { try { history.pushState(null, '', '#n' + i); } catch (e) { errs++; } }
      o.errors = errs; o.samples.push({ ms: 0, len: history.length }); window.__hl = o;
      [1000, 3000, 6000].forEach(function (ms) { setTimeout(function () { o.samples.push({ ms: ms, len: history.length }); if (ms === 6000) o.done = true; }, ms); });
    }, 0)`);
    await page.waitForFunction('window.__hl && window.__hl.done', null, { polling: 250, timeout: 15000 });
    return await page.evaluate('window.__hl');
  } finally { await context.close().catch(() => {}); }
};

async function main() {
  const [engine, list] = process.argv.slice(2);
  const kases = (list || '').split(',').filter(Boolean);
  if (!ENGINES[engine] || !kases.length || kases.some((k) => !cases[k])) throw new Error('usage: node extras.js <engine> <case>[,<case>...]');
  const server = await startServer();
  const bases = { file: pathToFileURL(PAGES).href + '/', http: `http://127.0.0.1:${server.address().port}/` };
  const outs = {};
  let browser;
  try {
    browser = await ENGINES[engine]();
    for (const kase of kases) {
      const out = outs[kase] = { engine, case: kase, browserVersion: browser.version(), startedAt: new Date().toISOString(), results: [] };
      for (let run = 1; run <= 2; run++) {
        for (const proto of ['file', 'http']) {
          let res;
          try { res = await cases[kase](browser, bases[proto]); } catch (e) { res = { harnessError: firstLine(e.message) }; }
          out.results.push({ run, proto, res });
          console.log(engine, kase, 'run' + run, proto, JSON.stringify(res).slice(0, 700));
        }
      }
      fs.writeFileSync(path.join(RESULTS, `extras-${engine}-${kase}.json`), JSON.stringify(out, null, 1));
    }
  } finally {
    if (browser) await browser.close().catch(() => {});
    if (server.closeAllConnections) server.closeAllConnections();
    await new Promise((r) => server.close(() => r()));
  }
}

main().catch((e) => { console.error(e); process.exitCode = 1; });
