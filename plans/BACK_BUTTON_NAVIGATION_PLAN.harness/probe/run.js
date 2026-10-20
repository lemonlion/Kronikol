'use strict';
// History API / Navigation API probe for a single-file HTML report opened from file:// and http://127.0.0.1.
//
// Usage, from this folder:  node run.js <engine> [case,case,...] [--runs N]
//   chromium       Playwright's bundled Chromium headless shell, Playwright's default switches
//   chromium-real  the same binary without --disable-back-forward-cache and --disable-ipc-flooding-protection
//                  (the two Playwright defaults that turn off bfcache and Chromium's navigation rate limiter)
//   firefox        Playwright's bundled Firefox
//   webkit         Playwright's bundled WebKit (WinCairo port)
//
// Only Playwright's bundled browsers are launched: no channel, no executablePath. launchInstalled() is the
// repository's guard for installed Chrome/Edge; for bundled browsers it is plain browserType.launch().
// Writes results/<engine>.json.
const fs = require('fs');
const path = require('path');
const http = require('http');
const { pathToFileURL } = require('url');

// playwright-core 1.59.1 (the .NET driver's copy): its browsers.json pins chromium-1217, firefox-1511 and
// webkit-2272, the three revisions installed under %LOCALAPPDATA%\ms-playwright.
const pw = require('C:/Users/cex/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package/index.js');
const { launchInstalled } = require('C:/Code/Kronikol/tools/playwright/installed-browser.js');

const PROBE = __dirname;
const PAGES = path.join(PROBE, 'pages');
const RESULTS = path.join(PROBE, 'results');
fs.mkdirSync(RESULTS, { recursive: true });

const firstLine = (s) => String(s || '').split('\n')[0].slice(0, 400);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const ENGINES = {
  chromium: () => launchInstalled(pw.chromium, { headless: true }),
  'chromium-real': () => launchInstalled(pw.chromium, {
    headless: true,
    ignoreDefaultArgs: ['--disable-back-forward-cache', '--disable-ipc-flooding-protection'],
  }),
  firefox: () => launchInstalled(pw.firefox, { headless: true }),
  webkit: () => launchInstalled(pw.webkit, { headless: true }),
};

// ---------- static server ----------
function startServer() {
  return new Promise((resolve) => {
    const server = http.createServer((req, res) => {
      const u = new URL(req.url, 'http://127.0.0.1');
      let p = decodeURIComponent(u.pathname);
      if (p === '/') p = '/report.html';
      const file = path.join(PAGES, path.normalize(p).replace(/^[/\\]+/, ''));
      if (!file.startsWith(PAGES)) { res.writeHead(403); res.end(); return; }
      fs.readFile(file, (err, data) => {
        if (err) { res.writeHead(404, { 'content-type': 'text/plain' }); res.end('not found'); return; }
        res.writeHead(200, { 'content-type': file.endsWith('.html') ? 'text/html; charset=utf-8' : 'application/octet-stream',
                             'content-length': data.length });
        res.end(data);
      });
    });
    server.listen(0, '127.0.0.1', () => resolve(server));
  });
}

// ---------- helpers ----------
async function open(c, file, { waitUntil = 'load' } = {}) {
  const context = await c.browser.newContext({ viewport: { width: 1280, height: 720 } });
  const page = await context.newPage();
  const consoleMsgs = [];
  const events = [];
  page.on('console', (m) => {
    const text = m.text();
    if (text.startsWith('EVT ')) { try { events.push(JSON.parse(text.slice(4))); } catch { /* ignore */ } return; }
    consoleMsgs.push({ type: m.type(), text: text.slice(0, 400) });
  });
  page.on('pageerror', (e) => consoleMsgs.push({ type: 'pageerror', text: firstLine(e.message) }));
  await page.goto(c.base + file, { waitUntil });
  await settle(page);
  return { context, page, consoleMsgs, events, close: () => context.close().catch(() => {}) };
}

// Two animation frames (or 500 ms if frames do not come), then ms of wall time.
async function settle(target, ms = 200) {
  try {
    await target.evaluate(() => new Promise((resolve) => {
      let done = false;
      const fin = () => { if (!done) { done = true; resolve(); } };
      requestAnimationFrame(() => requestAnimationFrame(fin));
      setTimeout(fin, 500);
    }));
  } catch { /* a navigation was in flight */ }
  await sleep(ms);
}

const SNAP = `(() => {
  let state; try { state = history.state; } catch (e) { state = 'ERR:' + e.name; }
  return { hash: location.hash, href: location.href, state, len: history.length, y: Math.round(scrollY),
           sr: history.scrollRestoration };
})()`;
async function snap(t) { try { return await t.evaluate(SNAP); } catch (e) { return { err: firstLine(e.message) }; } }
async function drain(t) {
  try { return await t.evaluate('window.__log ? window.__log.splice(0) : null'); }
  catch (e) { return { err: firstLine(e.message) }; }
}

// Runs a statement that may throw; reports the outcome and the URL afterwards.
const TRY = (code) => `(() => {
  try { ${code}; return { ok: true, href: location.href, hash: location.hash, state: history.state, len: history.length }; }
  catch (e) { return { ok: false, err: e.name + ': ' + e.message, href: location.href, len: history.length }; }
})()`;

async function nav(page, dir, { timeout = 5000, waitUntil = 'load' } = {}) {
  const t0 = Date.now();
  try {
    const resp = dir === 'back' ? await page.goBack({ timeout, waitUntil }) : await page.goForward({ timeout, waitUntil });
    return { ok: true, resp: resp ? resp.status() : null, ms: Date.now() - t0 };
  } catch (e) { return { ok: false, err: firstLine(e.message), ms: Date.now() - t0 }; }
}
async function reload(page, { timeout = 10000, waitUntil = 'load' } = {}) {
  const t0 = Date.now();
  try { await page.reload({ timeout, waitUntil }); return { ok: true, ms: Date.now() - t0 }; }
  catch (e) { return { ok: false, err: firstLine(e.message), ms: Date.now() - t0 }; }
}

// ---------- cases ----------
const cases = {};

// Q1: pushState #a, #b, then Back and Forward.
cases.q1 = async (c) => {
  const s = await open(c, 'report.html'); const { page } = s; const r = {};
  try {
    r.initial = await snap(page);
    r.push1 = await page.evaluate(TRY(`history.pushState({k:1}, '', '#a')`));
    r.push2 = await page.evaluate(TRY(`history.pushState({k:2}, '', '#b')`));
    await settle(page);
    r.pushEvents = await drain(page);
    r.back = await nav(page, 'back'); await settle(page, 300);
    r.backEvents = await drain(page); r.afterBack = await snap(page);
    r.fwd = await nav(page, 'forward'); await settle(page, 300);
    r.fwdEvents = await drain(page); r.afterFwd = await snap(page);
  } finally { await s.close(); }
  return r;
};

// Q2: pushState that changes the query or the path.
cases.q2 = async (c) => {
  const r = {};
  for (const [k, url] of [['fragment', '#a'], ['query', '?x=1#a'], ['samePathQuery', 'report.html?y=2'],
                          ['path', 'other.html'], ['subPath', 'sub/other.html']]) {
    const s = await open(c, 'report.html');
    try { r[k] = await s.page.evaluate(TRY(`history.pushState(null, '', ${JSON.stringify(url)})`)); }
    finally { await s.close(); }
  }
  return r;
};

// Q3: location.hash = 'c' from an entry whose state is {k:'base'}.
cases.q3 = async (c) => {
  const s = await open(c, 'report.html'); const { page } = s; const r = {};
  try {
    await page.evaluate(`history.replaceState({k:'base'}, '')`);
    r.before = await snap(page);
    r.set = await page.evaluate(`(() => { location.hash = 'c';
      return { hash: location.hash, state: history.state, len: history.length, y: Math.round(scrollY) }; })()`);
    await settle(page, 300);
    r.setEvents = await drain(page); r.afterSet = await snap(page);
    r.back = await nav(page, 'back'); await settle(page, 300);
    r.backEvents = await drain(page); r.afterBack = await snap(page);
    r.fwd = await nav(page, 'forward'); await settle(page, 300);
    r.fwdEvents = await drain(page); r.afterFwd = await snap(page);
  } finally { await s.close(); }
  return r;
};

// Q4: assigning the current hash again (page scrolled away from #c first).
cases.q4 = async (c) => {
  const s = await open(c, 'report.html'); const { page } = s; const r = {};
  try {
    await page.evaluate(`location.hash = 'c'`); await settle(page, 300); await drain(page);
    r.atC = await snap(page);
    for (const [k, code] of [['sameHash', 'location.hash = location.hash'], ['sameHashLiteral', "location.hash = '#c'"],
                             ['hrefSelf', 'location.href = location.href']]) {
      await page.evaluate('scrollTo(0, 500)'); await settle(page);
      const before = await snap(page);
      r[k] = { code, before };
      r[k].result = await page.evaluate(TRY(code));
      await settle(page, 300);
      r[k].events = await drain(page); r[k].after = await snap(page);
    }
  } finally { await s.close(); }
  return r;
};

// Q5: a real click on <a href="#target">, Back, then Forward from a different scroll position.
cases.q5 = async (c) => {
  const r = {};
  for (const mode of ['auto', 'manual']) {
    const s = await open(c, 'report.html'); const { page } = s; const m = {};
    try {
      await page.evaluate(`history.scrollRestoration = '${mode}'`);
      await page.evaluate('scrollTo(0, 800)'); await settle(page);
      m.before = await snap(page);
      await page.click('#link');
      await settle(page, 300);
      m.clickEvents = await drain(page); m.afterClick = await snap(page);
      m.back = await nav(page, 'back'); await settle(page, 400);
      m.backEvents = await drain(page); m.afterBack = await snap(page);
      await page.evaluate('scrollTo(0, 300)'); await settle(page);
      m.fwd = await nav(page, 'forward'); await settle(page, 400);
      m.fwdEvents = await drain(page); m.afterFwd = await snap(page);
    } finally { await s.close(); }
    r[mode] = m;
  }
  return r;
};

// Q6: scroll restoration for a pushState entry.
cases.q6 = async (c) => {
  const r = {};
  for (const mode of ['auto', 'manual']) {
    const s = await open(c, 'report.html'); const { page } = s; const m = {};
    try {
      await page.evaluate(`history.scrollRestoration = '${mode}'`);
      await page.evaluate('scrollTo(0, 3000)'); await settle(page);
      m.before = await snap(page);
      await page.evaluate(`history.pushState({k:'p'}, '', '#p'); scrollTo(0, 0)`); await settle(page); await drain(page);
      m.back = await nav(page, 'back'); await settle(page, 400);
      m.backEvents = await drain(page); m.afterBack = await snap(page);
      await page.evaluate('scrollTo(0, 1500)'); await settle(page);
      m.fwd = await nav(page, 'forward'); await settle(page, 400);
      m.fwdEvents = await drain(page); m.afterFwd = await snap(page);
    } finally { await s.close(); }
    r[mode] = m;
  }
  return r;
};

// Q7: content-visibility:auto sections; save at s250, leave, come back (Back or reload), restore.
const TOP = `({ y: Math.round(scrollY), top: __topSection(), layout: __layout() })`;
async function q7variant(c, { leave, mode, restore, detour }) {
  const s = await open(c, 'cv.html'); const { page } = s; const r = { leave, mode, restore, detour };
  try {
    await page.evaluate(`history.scrollRestoration = '${mode}'`);
    await page.evaluate(`document.getElementById('s250').scrollIntoView({ block: 'start' })`);
    await settle(page, 300);
    r.saved = await page.evaluate(TOP);
    await sleep(300);
    r.saved2 = await page.evaluate(TOP);          // drift check: is the saved position stable?
    await page.evaluate(`history.replaceState({ savedY: ${r.saved2.y}, sec: '${r.saved2.top.id}', offset: ${r.saved2.top.offset} }, '')`);
    if (leave === 'back') {
      await page.evaluate(`history.pushState({ k: 'away' }, '', '#away')`);
      if (detour) { await page.evaluate(`document.getElementById('${detour}').scrollIntoView({ block: 'start' })`); await settle(page, 300); }
      await page.evaluate('scrollTo(0, 0)'); await settle(page, 300);
      r.away = await page.evaluate(TOP);
      await drain(page);
      r.nav = await nav(page, 'back');
    } else {
      s.events.length = 0;
      r.nav = await reload(page);
    }
    await settle(page, 400);
    // y at the moment popstate (Back) or pageshow (reload) ran, before any page restore.
    r.yAtEvent = leave === 'back'
      ? ((await drain(page)) || []).filter((e) => e.type === 'popstate').map((e) => e.y)
      : s.events.filter((e) => e.type === 'pageshow').map((e) => e.y);
    r.browser = await page.evaluate(`(() => { const o = ${TOP}; o.state = history.state; o.sr = history.scrollRestoration; return o; })()`);
    if (restore === 'a') await page.evaluate('scrollTo(0, history.state.savedY)');
    if (restore === 'b') await page.evaluate(`(() => { const st = history.state;
      document.getElementById(st.sec).scrollIntoView({ block: 'start' }); scrollBy(0, st.offset); })()`);
    if (restore) {
      r.restoredNow = await page.evaluate(TOP);
      await settle(page, 400);
      r.restored = await page.evaluate(TOP);
      await sleep(600);
      r.restoredLate = await page.evaluate(TOP);
    }
  } finally { await s.close(); }
  return r;
}
cases.q7 = async (c) => {
  const variants = {
    backTopAuto: { leave: 'back', mode: 'auto', restore: null },
    backTopManualA: { leave: 'back', mode: 'manual', restore: 'a' },
    backTopManualB: { leave: 'back', mode: 'manual', restore: 'b' },
    backDetourAuto: { leave: 'back', mode: 'auto', restore: null, detour: 's120' },
    backDetourManualA: { leave: 'back', mode: 'manual', restore: 'a', detour: 's120' },
    backDetourManualB: { leave: 'back', mode: 'manual', restore: 'b', detour: 's120' },
    reloadAuto: { leave: 'reload', mode: 'auto', restore: null },
    reloadManualA: { leave: 'reload', mode: 'manual', restore: 'a' },
    reloadManualB: { leave: 'reload', mode: 'manual', restore: 'b' },
  };
  const r = {};
  for (const [k, v] of Object.entries(variants)) {
    try { r[k] = await q7variant(c, v); } catch (e) { r[k] = { harnessError: firstLine(e.message) }; }
  }
  return r;
};

// Q8: 500 replaceState / 500 pushState calls in a tight loop, each kind on a fresh page.
cases.q8 = async (c) => {
  const r = {};
  for (const kind of ['replaceState', 'pushState']) {
    const s = await open(c, 'report.html');
    try {
      r[kind] = await s.page.evaluate(`(() => {
        const out = { firstErrorAt: null, firstError: null, errors: 0, firstIgnoredAt: null, ignored: 0 };
        const t = performance.now();
        for (let i = 0; i < 500; i++) {
          try {
            history.${kind}(null, '', '#n' + i);
            if (location.hash !== '#n' + i) { out.ignored++; if (out.firstIgnoredAt === null) out.firstIgnoredAt = i; }
          } catch (e) {
            out.errors++;
            if (out.firstErrorAt === null) { out.firstErrorAt = i; out.firstError = e.name + ': ' + e.message; }
          }
        }
        out.ms = Math.round(performance.now() - t);
        out.hash = location.hash; out.len = history.length;
        return out;
      })()`);
      await settle(s.page, 300);
      r[kind].oneMoreAfter300ms = await s.page.evaluate(TRY(`history.${kind}(null, '', '#more')`));
      r[kind].console = s.consoleMsgs.slice(0, 6);
      r[kind].consoleCount = s.consoleMsgs.length;
    } finally { await s.close(); }
  }
  return r;
};

// Q9: Navigation API.
cases.q9 = async (c) => {
  const s = await open(c, 'report.html'); const { page } = s; const r = {};
  const CUR = `(() => { const n = window.navigation; if (!n) return null; const e = n.currentEntry;
    let st; try { st = e ? e.getState() : 'no currentEntry'; } catch (x) { st = 'ERR ' + x.name; }
    return { hash: location.hash, currentEntry: e ? { url: e.url, index: e.index, sameDocument: e.sameDocument } : null,
             getState: st === undefined ? 'undefined' : st, historyState: history.state, entries: n.entries().length,
             canGoBack: n.canGoBack, len: history.length }; })()`;
  try {
    r.has = await page.evaluate(`({ inWindow: 'navigation' in window, type: typeof window.navigation,
      NavigateEvent: typeof window.NavigateEvent, origin: self.origin })`);
    if (!r.has.inWindow || r.has.type !== 'object') return r;
    r.initial = await page.evaluate(CUR);
    r.navX = await page.evaluate(`(async () => {
      try {
        const res = navigation.navigate('#x');
        const out = {};
        out.committed = await res.committed.then(() => 'resolved', (e) => 'rejected ' + e.name + ': ' + e.message);
        out.finished = await res.finished.then(() => 'resolved', (e) => 'rejected ' + e.name + ': ' + e.message);
        out.hash = location.hash; out.len = history.length;
        return out;
      } catch (e) { return { threw: e.name + ': ' + e.message, hash: location.hash }; }
    })()`);
    await settle(page);
    r.navXEvents = await drain(page); r.afterX = await page.evaluate(CUR);
    r.navY = await page.evaluate(`(async () => {
      try { const res = navigation.navigate('#y', { state: { a: 1 } });
            const f = await res.finished.then(() => 'resolved', (e) => 'rejected ' + e.name + ': ' + e.message);
            return { finished: f }; }
      catch (e) { return { threw: e.name + ': ' + e.message }; }
    })()`);
    await settle(page);
    r.navYEvents = await drain(page); r.afterY = await page.evaluate(CUR);
    r.back = await nav(page, 'back'); await settle(page, 300);
    r.backEvents = await drain(page); r.afterBack = await page.evaluate(CUR);
    r.fwd = await nav(page, 'forward'); await settle(page, 300);
    r.fwdEvents = await drain(page); r.afterFwd = await page.evaluate(CUR);
    // intercept(): a same-document hash navigation, then a Back traversal, both intercepted.
    await page.evaluate('window.__interceptMode = true');
    r.interceptHash = await page.evaluate(TRY(`location.hash = 'z'`));
    await settle(page, 300);
    r.interceptHashEvents = await drain(page); r.afterInterceptHash = await page.evaluate(CUR);
    r.interceptBack = await nav(page, 'back'); await settle(page, 300);
    r.interceptBackEvents = await drain(page); r.afterInterceptBack = await page.evaluate(CUR);
  } finally { await s.close(); }
  return r;
};

// Q10: the report inside an iframe; pushes in the child, Back at the top level.
async function q10variant(c, file) {
  const s = await open(c, file); const { page } = s; const r = { file };
  try {
    const frame = page.frames().find((f) => f !== page.mainFrame());
    if (!frame) return { file, err: 'no child frame' };
    r.origins = { top: await page.evaluate('self.origin'), child: await frame.evaluate('self.origin') };
    r.len0 = { top: await page.evaluate('history.length'), child: await frame.evaluate('history.length') };
    r.childNavApi = await frame.evaluate(`(() => { const n = window.navigation;
      return n ? { has: true, currentEntry: !!n.currentEntry, entries: n.entries().length } : { has: false }; })()`);
    r.push1 = await frame.evaluate(TRY(`history.pushState({f:1}, '', '#f1')`));
    r.push2 = await frame.evaluate(TRY(`history.pushState({f:2}, '', '#f2')`));
    r.len2 = { top: await page.evaluate('history.length'), child: await frame.evaluate('history.length') };
    await settle(page); await drain(frame); await drain(page);
    r.goBack = await nav(page, 'back', { timeout: 3000 });
    await settle(page, 400);
    r.afterGoBack = { topUrl: page.url(), top: await snap(page), child: await snap(frame),
                      childEvents: await drain(frame), topEvents: await drain(page) };
    r.histBack = await page.evaluate(TRY('history.back()'));
    await settle(page, 500);
    r.afterHistBack = { topUrl: page.url(), top: await snap(page), child: await snap(frame),
                        childEvents: await drain(frame), topEvents: await drain(page) };
    r.goForward = await nav(page, 'forward', { timeout: 3000 });
    await settle(page, 400);
    r.afterGoForward = { topUrl: page.url(), top: await snap(page), child: await snap(frame),
                         childEvents: await drain(frame), topEvents: await drain(page) };
  } finally { await s.close(); }
  return r;
}
cases.q10 = async (c) => {
  const r = {};
  for (const [k, f] of [['plain', 'parent.html'], ['sandboxScripts', 'parent-sb.html'], ['sandboxScriptsSameOrigin', 'parent-sbso.html']]) {
    try { r[k] = await q10variant(c, f); } catch (e) { r[k] = { harnessError: firstLine(e.message) }; }
  }
  return r;
};

// Q11: reload after pushState.
cases.q11 = async (c) => {
  const r = {};
  for (const mode of ['auto', 'manual']) {
    for (const [fk, frag] of [['noTarget', '#r'], ['targetC', '#c']]) {
      const s = await open(c, 'report.html'); const { page } = s; const m = {};
      try {
        await page.evaluate(`history.scrollRestoration = '${mode}'`);
        await page.evaluate('scrollTo(0, 2000)'); await settle(page);
        await page.evaluate(`history.pushState({k:'reload'}, '', '${frag}')`);
        await page.evaluate('scrollTo(0, 2500)'); await settle(page, 300);
        m.before = await snap(page);
        s.events.length = 0;
        m.reload = await reload(page); await settle(page, 400);
        m.yAtPageshow = s.events.filter((e) => e.type === 'pageshow').map((e) => e.y);
        m.after = await snap(page);
        await sleep(700);
        m.afterLate = await snap(page);
      } finally { await s.close(); }
      r[`${mode}_${fk}`] = m;
    }
  }
  return r;
};

// Q12: bfcache: pushState, navigate to other.html by a link, Back.
cases.q12 = async (c) => {
  const s = await open(c, 'report.html'); const { page } = s; const r = {};
  try {
    await page.evaluate(`window.__jsState.counter = 42; history.pushState({k:'bf'}, '', '#bf')`);
    r.before = await snap(page);
    await drain(page);
    await page.click('#to-other');
    await page.waitForURL(/other\.html/, { timeout: 10000 });
    await settle(page, 300);
    r.otherUrl = page.url();
    s.events.length = 0;
    r.back = await nav(page, 'back', { waitUntil: 'commit', timeout: 10000 });
    await page.waitForFunction(() => window.__log && window.__log.some((e) => e.type === 'pageshow'), null,
      { timeout: 5000, polling: 200 }).catch(() => {});
    await settle(page, 300);
    r.after = await snap(page);
    r.jsState = await page.evaluate('window.__jsState');
    r.log = await drain(page);
    r.navEntry = await page.evaluate(`(() => { const e = performance.getEntriesByType('navigation')[0]; if (!e) return null;
      let nrr = 'unsupported';
      if ('notRestoredReasons' in e) nrr = e.notRestoredReasons ? JSON.parse(JSON.stringify(e.notRestoredReasons)) : null;
      return { type: e.type, notRestoredReasons: nrr }; })()`);
    r.consoleEvents = s.events.filter((e) => /^page(show|hide)/.test(e.type)).map((e) => `${e.type}(persisted=${e.persisted})`);
  } finally { await s.close(); }
  return r;
};

// Q13: history.length after load, three pushStates and two replaceStates.
cases.q13 = async (c) => {
  const s = await open(c, 'report.html'); const { page } = s; const r = {};
  try {
    r.atLoad = await page.evaluate('history.length');
    r.after3Push = await page.evaluate(`history.pushState(null,'','#p1'); history.pushState(null,'','#p2');
      history.pushState(null,'','#p3'); history.length`);
    r.after2Replace = await page.evaluate(`history.replaceState(null,'','#r1'); history.replaceState(null,'','#r2'); history.length`);
  } finally { await s.close(); }
  return r;
};

// Extra: entries pushed from timers with no user activation; Back by page.goBack() and by the page's own
// history.back(). No page.evaluate runs before the Back (Chromium and WebKit evaluate with a user gesture).
cases.noact = async (c) => {
  const r = {};
  for (const file of ['noact.html', 'noact-selfback.html']) {
    const context = await c.browser.newContext({ viewport: { width: 1280, height: 720 } });
    try {
      const page = await context.newPage();
      const events = [];
      page.on('console', (m) => { const t = m.text(); if (t.startsWith('EVT ')) { try { events.push(JSON.parse(t.slice(4))); } catch { /* */ } } });
      await page.goto(c.base + file, { waitUntil: 'load' });
      const t0 = Date.now();
      while (Date.now() - t0 < 5000 && !events.some((e) => e.type === 'pushed' && e.n === 2)) await sleep(50);
      const m = {};
      if (file === 'noact.html') { m.goBack = await nav(page, 'back'); }
      await sleep(800);
      m.after = await snap(page);
      m.events = events.filter((e) => e.type !== 'pageshow').map((e) => `${e.type}${e.n ? e.n : ''}(${e.hash || 'no hash'},len=${e.len})`);
      r[file] = m;
    } finally { await context.close().catch(() => {}); }
  }
  return r;
};

// ---------- main ----------
async function main() {
  const args = process.argv.slice(2);
  const engine = args[0];
  if (!ENGINES[engine]) throw new Error('engine must be one of ' + Object.keys(ENGINES).join(', '));
  const only = args[1] && !args[1].startsWith('--') ? args[1].split(',') : null;
  const runsIdx = args.indexOf('--runs');
  const runs = runsIdx >= 0 ? Number(args[runsIdx + 1]) : 2;
  const server = await startServer();
  const httpBase = `http://127.0.0.1:${server.address().port}/`;
  const fileBase = pathToFileURL(PAGES).href + '/';
  const out = { engine, playwright: require('C:/Users/cex/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package/package.json').version,
                startedAt: new Date().toISOString(), node: process.version, fileBase, httpBase, results: [] };
  let browser;
  try {
    browser = await ENGINES[engine]();
    out.browserVersion = browser.version();
    out.browserType = browser.browserType().name();
    out.executable = browser.browserType().executablePath();
    for (let run = 1; run <= runs; run++) {
      for (const [proto, base] of [['file', fileBase], ['http', httpBase]]) {
        for (const [name, fn] of Object.entries(cases)) {
          if (only && !only.includes(name)) continue;
          const t0 = Date.now();
          let res;
          try { res = await fn({ browser, base, proto, engine }); }
          catch (e) { res = { harnessError: firstLine(e.message) }; }
          out.results.push({ run, proto, case: name, ms: Date.now() - t0, res });
          console.log(`${engine} run${run} ${proto} ${name} ${Date.now() - t0} ms${res && res.harnessError ? ' HARNESS ERROR ' + res.harnessError : ''}`);
        }
      }
    }
  } finally {
    if (browser) await browser.close().catch(() => {});
    if (server.closeAllConnections) server.closeAllConnections();
    await new Promise((r) => server.close(() => r()));
  }
  out.finishedAt = new Date().toISOString();
  const suffix = only ? '.' + only.join('-') : '';
  fs.writeFileSync(path.join(RESULTS, `${engine}${suffix}.json`), JSON.stringify(out, null, 1));
  console.log('wrote', path.join(RESULTS, `${engine}${suffix}.json`), 'browser', out.browserVersion);
}

main().catch((e) => { console.error(e); process.exitCode = 1; });
