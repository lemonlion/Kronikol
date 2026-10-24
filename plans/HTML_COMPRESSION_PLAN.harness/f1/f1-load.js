// node f1-load.js <page> [engine] [offline] : when a report page fires DOMContentLoaded, paints and fires load, the
// render script's route, and every request from navigation to load (plans/HTML_COMPRESSION_PLAN.md F1, steps 1-3).
// Playwright's bundled browsers only (never an installed Chrome or Edge).
const pw = require('playwright-core');
(async () => {
  const [file, engineName = 'webkit', offline] = process.argv.slice(2);
  const url = file.startsWith('/') ? 'file://' + file : 'file:///' + file.split('\\').join('/');
  const browser = await pw[engineName].launch({ headless: true });
  try {
    const context = await browser.newContext();
    const page = await context.newPage();
    const t0 = Date.now(), events = [];
    page.on('request', r => events.push([Date.now() - t0, 'request', r.url().slice(0, 120)]));
    page.on('requestfinished', r => events.push([Date.now() - t0, 'finished', r.url().slice(0, 120)]));
    page.on('requestfailed', r => events.push([Date.now() - t0, 'failed', r.url().slice(0, 120), (r.failure() || {}).errorText]));
    if (offline === 'nooffscreen') await page.addInitScript(() => { delete window.OffscreenCanvas; });
    await page.addInitScript(() => {
      window.__f1 = { dcl: [], load: [] };
      document.addEventListener('DOMContentLoaded', () => window.__f1.dcl.push(performance.now()));
      window.addEventListener('load', () => window.__f1.load.push(performance.now()));
      // The main thread's gaps (a 100 ms heartbeat that came late) and when diagrams' pictures appear.
      const gaps = window.__f1.gaps = []; let last = performance.now();
      setInterval(() => { const now = performance.now(); if (now - last > 600) gaps.push([Math.round(last), Math.round(now - last)]); last = now; }, 100);
      const svgs = window.__f1.svgs = [];
      new MutationObserver(ms => { for (const m of ms) for (const n of m.addedNodes) if (n.nodeName && n.nodeName.toLowerCase() === 'svg') svgs.push(Math.round(performance.now())); })
        .observe(document, { childList: true, subtree: true });
      // What the page itself asks for, with times: fetch, XHR, workers, and elements given a src or href.
      const asks = window.__f1.asks = [];
      const note = (kind, url) => asks.push([Math.round(performance.now()), kind, String(url).slice(0, 140)]);
      const f = window.fetch; window.fetch = function (u) { note('fetch', u && u.url || u); return f.apply(this, arguments); };
      const o = XMLHttpRequest.prototype.open; XMLHttpRequest.prototype.open = function (m, u) { note('xhr', u); return o.apply(this, arguments); };
      const W = window.Worker; window.Worker = function (u, opt) { note('worker', u); return new W(u, opt); };
      for (const [proto, attr] of [[HTMLScriptElement.prototype, 'src'], [HTMLImageElement.prototype, 'src'], [HTMLLinkElement.prototype, 'href'], [HTMLIFrameElement.prototype, 'src']]) {
        const d = Object.getOwnPropertyDescriptor(proto, attr);
        Object.defineProperty(proto, attr, { configurable: true, get: d.get, set(v) { note(proto.constructor.name + '.' + attr, v); d.set.call(this, v); } });
      }
      const sa = Element.prototype.setAttribute; Element.prototype.setAttribute = function (n, v) { if (n === 'src' || n === 'href') note(this.tagName + '@' + n, v); return sa.apply(this, arguments); };
    });
    await page.goto(url, { waitUntil: 'commit', timeout: 120000 });
    // WebKit refuses a file:// navigation offline, so the network goes once the page has committed.
    if (offline === 'offline') await context.setOffline(true);
    await page.waitForFunction(() => window.__kronikolReady === true || !document.querySelector('meta[name="kronikol-page"]'), null, { polling: 200, timeout: 120000 });
    const ready = Date.now() - t0;
    // The rebuilt document: its own load, polled (listeners added before document.open are erased).
    await page.waitForFunction(() => document.readyState === 'complete', null, { polling: 200, timeout: 120000 }).catch(() => {});
    const complete = Date.now() - t0;
    const facts = await page.evaluate(() => ({
      load: window.__kronikolLoad || null,
      f1: window.__f1 || null,
      render: window.__kronikolRender ? JSON.parse(JSON.stringify(window.__kronikolRender)) : null,
      resources: performance.getEntriesByType('resource').map(e => [Math.round(e.startTime), Math.round(e.responseEnd), e.name.slice(0, 100)]),
    }));
    console.log(JSON.stringify({ engine: engineName, offline: !!offline, readyMs: ready, completeMs: complete, ...facts, events }, null, 1));
  } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
