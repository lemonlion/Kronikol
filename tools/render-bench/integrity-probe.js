// integrity-probe.js: does the platform verify subresource integrity where the engine pin plan
// (plans/ENGINE_PIN_PLAN.md, S2) wants to lean on it, per browser and page origin?
//   1. fetch(url, { integrity }) on the CDN engine: the right hash resolves, a wrong one rejects
//      (and how the rejection reads), in secure and non-secure contexts, page and Blob worker;
//   2. a classic <script integrity crossorigin> for viz-global.js: right loads, wrong errors;
//   3. a <script type=module integrity crossorigin> for plantuml.js, then import() of the same
//      URL: one request or two (a local server counts), and whether a wrong hash on the tag also
//      poisons the later import();
//   4. import() and a classic <script> against a server that sends no CORS header;
//   5. import() of a blob: URL holding the verified engine text.
// Run from tools/render-bench with the E2E project built (its Playwright package and browsers):
//   node integrity-probe.js <dir holding plantuml.js and viz-global.js> [chromium,firefox,webkit]
'use strict';
const http = require('http');
const fs = require('fs');
const path = require('path');
const os = require('os');
const url = require('url');
const pw = require('C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package');

const NL = '\n';
const CDN = 'https://cdn.jsdelivr.net/npm/@plantuml/core@1.2026.8';
const ENGINE_SRI = 'sha256-rejxXtfyoyJYFDMtOsbQW8fr277IYwUSzHz2eLwMTVI=';
const VIZ_SRI = 'sha256-/Gyi3oPdTj/Kln1SFBInd4kKXNGrEBEVofcW/ITm3F4=';
const WRONG = 'sha256-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=';
const dir = process.argv[2];
const browsers = (process.argv[3] || 'chromium,firefox').split(',');
if (!dir || !fs.existsSync(path.join(dir, 'plantuml.js'))) { console.error('usage: node integrity-probe.js <dir with plantuml.js + viz-global.js> [browsers]'); process.exit(2); }

const counts = {};
function fileServer(cors) {
  return http.createServer(function (req, res) {
    const u = new URL(req.url, 'http://x');
    const name = path.basename(u.pathname);
    const key = (cors ? 'cors' : 'nocors') + ' ' + u.pathname + u.search;
    counts[key] = (counts[key] || 0) + 1;
    const file = path.join(dir, name);
    if (!fs.existsSync(file)) { res.statusCode = 404; res.end('no such file'); return; }
    res.setHeader('content-type', 'application/javascript; charset=utf-8');
    res.setHeader('cache-control', 'public, max-age=31536000, immutable');
    if (cors) res.setHeader('access-control-allow-origin', '*');
    fs.createReadStream(file).pipe(res);
  });
}
const pageHtml = '<!doctype html><html><head><meta charset="utf-8"><title>integrity probe</title></head><body><div id="t"></div></body></html>';
const pageServer = http.createServer(function (req, res) { res.setHeader('content-type', 'text/html; charset=utf-8'); res.end(pageHtml); });

function lanIp() {
  const ifs = os.networkInterfaces();
  let fallback = null;
  for (const k of Object.keys(ifs)) for (const a of ifs[k]) {
    if (a.family !== 'IPv4' || a.internal) continue;
    if (a.address.indexOf('192.168.') === 0) return a.address;
    if (!fallback) fallback = a.address;
  }
  return fallback;
}
function listen(server, host) { return new Promise(function (resolve) { server.listen(0, host, function () { resolve(server.address().port); }); }); }

// The in-page helpers, evaluated as a string so that import() is parsed by the browser only.
const WORKER_SRC = 'self.onmessage = async function (ev) { var t0 = performance.now(); try { var r = await fetch(ev.data.u, ev.data.integrity ? { integrity: ev.data.integrity } : {}); var b = await r.arrayBuffer(); self.postMessage({ ok: true, bytes: b.byteLength, ms: Math.round(performance.now() - t0), subtle: !!(self.crypto && self.crypto.subtle) }); } catch (e) { self.postMessage({ ok: false, error: String(e && e.name) + ": " + String(e && e.message), subtle: !!(self.crypto && self.crypto.subtle) }); } };';
const LIB = [
  'window.__p = {',
  '  fetch: async function (u, integrity) {',
  '    var t0 = performance.now();',
  '    try { var r = await fetch(u, integrity ? { integrity: integrity } : {}); var b = await r.arrayBuffer(); return { ok: true, bytes: b.byteLength, ms: Math.round(performance.now() - t0) }; }',
  '    catch (e) { return { ok: false, error: String(e && e.name) + ": " + String(e && e.message), ms: Math.round(performance.now() - t0) }; }',
  '  },',
  '  script: function (u, integrity, type) {',
  '    return new Promise(function (resolve) {',
  '      var s = document.createElement("script");',
  '      if (type) s.type = type;',
  '      if (integrity) { s.integrity = integrity; s.crossOrigin = "anonymous"; }',
  '      s.src = u;',
  '      s.onload = function () { resolve({ ok: true }); };',
  '      s.onerror = function () { resolve({ ok: false }); };',
  '      document.head.appendChild(s);',
  '    });',
  '  },',
  '  imp: async function (u) {',
  '    try { var m = await import(u); return { ok: true, hasRender: typeof m.render === "function" }; }',
  '    catch (e) { return { ok: false, error: String(e && e.name) + ": " + String(e && e.message) }; }',
  '  },',
  '  render: async function (u) {',
  '    var m = await import(u);',
  '    var el = document.getElementById("t"); el.innerHTML = "";',
  '    return new Promise(function (resolve) {',
  '      var timer = setTimeout(function () { resolve({ rendered: false, timeout: true }); }, 60000);',
  '      new MutationObserver(function (x, mo) { var svg = el.querySelector("svg"); if (!svg) return; mo.disconnect(); clearTimeout(timer); resolve({ rendered: true, texts: svg.querySelectorAll("text").length }); }).observe(el, { childList: true, subtree: true });',
  '      try { m.render(["@startuml", "a -> b: hello", "@enduml"], "t", { maxSvgSize: 98304 }); } catch (e) { clearTimeout(timer); resolve({ rendered: false, error: String(e) }); }',
  '    });',
  '  },',
  '  workerFetch: function (u, integrity) {',
  '    return new Promise(function (resolve) {',
  '      var src = ' + JSON.stringify(WORKER_SRC) + ';',
  '      var w = new Worker(URL.createObjectURL(new Blob([src], { type: "application/javascript" })));',
  '      var timer = setTimeout(function () { resolve({ ok: false, error: "worker timeout" }); w.terminate(); }, 120000);',
  '      w.onmessage = function (ev) { clearTimeout(timer); resolve(ev.data); w.terminate(); };',
  '      w.onerror = function (ev) { clearTimeout(timer); resolve({ ok: false, error: "worker error: " + (ev && ev.message) }); w.terminate(); };',
  '      w.postMessage({ u: u, integrity: integrity });',
  '    });',
  '  },',
  '  blobImport: async function (u) {',
  '    var text = await (await fetch(u)).text();',
  '    var b = URL.createObjectURL(new Blob([text], { type: "text/javascript" }));',
  '    try { var m = await import(b); return { ok: true, hasRender: typeof m.render === "function" }; }',
  '    catch (e) { return { ok: false, error: String(e && e.name) + ": " + String(e && e.message) }; }',
  '  },',
  '  entries: function (u) { return performance.getEntriesByType("resource").filter(function (e) { return e.name === u; }).map(function (e) { return { transfer: e.transferSize, body: e.encodedBodySize }; }); }',
  '};'
].join(NL);

function call(fn, args) { return '__p.' + fn + '(' + args.map(function (a) { return JSON.stringify(a); }).join(', ') + ')'; }

async function main() {
  const cors = fileServer(true), nocors = fileServer(false);
  const corsPort = await listen(cors, '127.0.0.1');
  const nocorsPort = await listen(nocors, '127.0.0.1');
  const pagePort = await listen(pageServer, '0.0.0.0');
  const lan = lanIp();
  const fileHtml = path.join(os.tmpdir(), 'kronikol-integrity-probe.html');
  fs.writeFileSync(fileHtml, pageHtml);
  const origins = [
    { name: 'file://', url: url.pathToFileURL(fileHtml).href, local: true },
    { name: 'http://127.0.0.1', url: 'http://127.0.0.1:' + pagePort + '/', local: true },
    lan ? { name: 'http://' + lan + ' (LAN)', url: 'http://' + lan + ':' + pagePort + '/', local: false } : null
  ].filter(Boolean);
  const L = 'http://127.0.0.1:' + corsPort, N = 'http://127.0.0.1:' + nocorsPort;
  const results = [];
  for (const bname of browsers) {
    let browser;
    try { browser = await pw[bname].launch(); } catch (e) { console.log('# ' + bname + ': cannot launch: ' + String(e && e.message).split(NL)[0]); continue; }
    const version = browser.version();
    for (const o of origins) {
      const ctx = await browser.newContext();
      const page = await ctx.newPage();
      const consoleLines = [];
      page.on('console', function (m) { const t = m.text(); if (/integrity|Integrity|CORS|blocked|Failed/.test(t)) consoleLines.push(t.slice(0, 200)); });
      const r = { browser: bname + ' ' + version, origin: o.name };
      for (const k of Object.keys(counts)) delete counts[k]; // per context: the same URLs are used in every one
      try {
        await page.goto(o.url);
        await page.evaluate(LIB);
        r.secure = await page.evaluate('isSecureContext');
        r.subtle = await page.evaluate('!!(crypto && crypto.subtle)');
        // 1. fetch with integrity against the CDN: wrong first (so the plain fetch after it shows whether the wrong one was cached), then plain, then right.
        r.fetchWrong = await page.evaluate(call('fetch', [CDN + '/plantuml.js', WRONG]));
        r.fetchPlainAfterWrong = await page.evaluate(call('fetch', [CDN + '/plantuml.js', null]));
        r.entriesAfterWrongThenPlain = await page.evaluate(call('entries', [CDN + '/plantuml.js']));
        r.fetchRight = await page.evaluate(call('fetch', [CDN + '/plantuml.js', ENGINE_SRI]));
        r.fetchVizRight = await page.evaluate(call('fetch', [CDN + '/viz-global.js', VIZ_SRI]));
        r.workerFetchRight = await page.evaluate(call('workerFetch', [CDN + '/plantuml.js', ENGINE_SRI]));
        r.workerFetchWrong = await page.evaluate(call('workerFetch', [CDN + '/plantuml.js', WRONG]));
        // 2. classic viz tag: wrong (must not run), then right.
        r.vizTagWrong = await page.evaluate(call('script', [CDN + '/viz-global.js', WRONG, null]));
        r.vizDefinedAfterWrong = await page.evaluate('typeof Viz');
        r.vizTagRight = await page.evaluate(call('script', [CDN + '/viz-global.js', VIZ_SRI, null]));
        r.vizDefinedAfterRight = await page.evaluate('typeof Viz');
        // 3. module tag with the right hash, then import() of the same URL, then a real render through it.
        r.moduleTagRight = await page.evaluate(call('script', [CDN + '/plantuml.js', ENGINE_SRI, 'module']));
        r.importAfterModuleTag = await page.evaluate(call('imp', [CDN + '/plantuml.js']));
        r.renderAfterModuleTag = await page.evaluate(call('render', [CDN + '/plantuml.js']));
        if (o.local) {
          // Request counting needs the local server: a page on a LAN origin fetching loopback trips Chromium's private-network rules, so only file:// and loopback pages run these.
          const kr = L + '/plantuml.js?k=right', kw = L + '/plantuml.js?k=wrong', kc = L + '/plantuml.js?k=cache';
          r.localModuleTagRight = await page.evaluate(call('script', [kr, ENGINE_SRI, 'module']));
          r.localImportAfterRight = await page.evaluate(call('imp', [kr]));
          r.localRequestsRight = counts['cors /plantuml.js?k=right'] || 0;
          r.localModuleTagWrong = await page.evaluate(call('script', [kw, WRONG, 'module']));
          r.localImportAfterWrong = await page.evaluate(call('imp', [kw]));
          r.localRequestsWrong = counts['cors /plantuml.js?k=wrong'] || 0;
          r.localFetchWrong = await page.evaluate(call('fetch', [kc, WRONG]));
          r.localFetchPlainAfterWrong = await page.evaluate(call('fetch', [kc, null]));
          r.localRequestsWrongThenPlain = counts['cors /plantuml.js?k=cache'] || 0;
          r.localFetchRight = await page.evaluate(call('fetch', [L + '/plantuml.js?k=fr', ENGINE_SRI]));
          // 4. no CORS header: import(), fetch and a plain classic tag.
          r.importNoCors = await page.evaluate(call('imp', [N + '/plantuml.js?k=nocors']));
          r.fetchNoCors = await page.evaluate(call('fetch', [N + '/plantuml.js?k=nocors2', null]));
          r.classicTagNoCors = await page.evaluate(call('script', [N + '/viz-global.js?k=nocors', null, null]));
          // 5. blob: URL module import of the verified text.
          r.blobImport = await page.evaluate(call('blobImport', [L + '/plantuml.js?k=blob']));
        }
      } catch (e) { r.error = String(e && e.message).split(NL)[0]; }
      r.console = consoleLines.slice(0, 6);
      results.push(r);
      console.log(JSON.stringify(r));
      await ctx.close();
    }
    await browser.close();
  }
  cors.close(); nocors.close(); pageServer.close();
  console.log(NL + '# summary');
  const yes = function (x) { return x && x.ok ? 'ok' : 'no'; };
  console.log('| browser | origin | secure | subtle | fetch wrong | fetch right (ms) | worker fetch right/wrong | viz tag wrong/right (Viz) | module tag + import + render | local: tag right requests | tag wrong then import (requests) | wrong then plain requests | import no CORS | fetch no CORS | classic no CORS | blob import |');
  console.log('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|');
  for (const r of results) {
    console.log('| ' + r.browser + ' | ' + r.origin + ' | ' + r.secure + ' | ' + r.subtle
      + ' | ' + (r.fetchWrong ? (r.fetchWrong.ok ? 'RESOLVED (not enforced)' : 'rejected: ' + r.fetchWrong.error) : '-')
      + ' | ' + (r.fetchRight ? yes(r.fetchRight) + ' ' + r.fetchRight.ms : '-')
      + ' | ' + (r.workerFetchRight ? yes(r.workerFetchRight) : '-') + '/' + (r.workerFetchWrong ? (r.workerFetchWrong.ok ? 'RESOLVED' : 'rejected') : '-')
      + ' | ' + (r.vizTagWrong ? yes(r.vizTagWrong) : '-') + ' (' + r.vizDefinedAfterWrong + ') / ' + (r.vizTagRight ? yes(r.vizTagRight) : '-') + ' (' + r.vizDefinedAfterRight + ')'
      + ' | ' + (r.moduleTagRight ? yes(r.moduleTagRight) : '-') + ' + ' + (r.importAfterModuleTag ? (r.importAfterModuleTag.ok ? 'render=' + r.importAfterModuleTag.hasRender : 'no: ' + r.importAfterModuleTag.error) : '-') + ' + ' + (r.renderAfterModuleTag ? (r.renderAfterModuleTag.rendered ? 'svg ' + r.renderAfterModuleTag.texts + ' texts' : 'no render') : '-')
      + ' | ' + (r.localRequestsRight === undefined ? '-' : r.localRequestsRight)
      + ' | ' + (r.localModuleTagWrong ? yes(r.localModuleTagWrong) + ' then import ' + (r.localImportAfterWrong.ok ? 'OK (bypassed)' : 'rejected') + ' (' + r.localRequestsWrong + ')' : '-')
      + ' | ' + (r.localRequestsWrongThenPlain === undefined ? '-' : r.localRequestsWrongThenPlain)
      + ' | ' + (r.importNoCors ? (r.importNoCors.ok ? 'OK' : 'rejected') : '-')
      + ' | ' + (r.fetchNoCors ? (r.fetchNoCors.ok ? 'OK' : 'rejected') : '-')
      + ' | ' + (r.classicTagNoCors ? yes(r.classicTagNoCors) : '-')
      + ' | ' + (r.blobImport ? (r.blobImport.ok ? 'render=' + r.blobImport.hasRender : 'no: ' + r.blobImport.error) : '-')
      + ' |');
  }
}
main().catch(function (e) { console.error(e); process.exit(1); });
