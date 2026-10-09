'use strict';
// Issue #162 re-measurement harness. Pages come from probe/probe.cs (Kronikol 4.14.4, ComponentDiagramReportGenerator):
//   pages/w4/n<N>/ComponentDiagram.html (BrowserRenderWorkers = 4) and pages/w0/n<N>/... (BrowserRenderWorkers = 0).
// A case replaces the `caller ...` edge line inside the page's data-plantuml-z (gzip+base64, same page, same scripts,
// same settings) with a line of an exact length, then opens it and reads the verdict from #comp-diagram.
//
//   node measure.js bisect '<json>'   {label, browser, page, mode, jsflags, prefs, env, shape, lo, hi, step, reps}
//   node measure.js cases  '<json>'   {label, browser, page, mode, jsflags, prefs, env, cases:[{shape,L}|{n}], reps}
//   node measure.js stack  '<json>'   {label, browser, jsflags, prefs, env}
//
// browser: chromium (Playwright's bundled build, headless shell), chromium-new (bundled full Chromium, new headless),
// chrome (installed Google Chrome stable, channel 'chrome'), firefox, webkit.
// mode: cold (a fresh browser per case: the page's own first render, in a fresh worker or a fresh main thread)
//       warm (fresh browser per repeat; the N=10 page draws, then WARMUPS shorter edges of the same shape render one
//             after another in the same page and worker, then the case renders there).
// Every case is run sequentially: one page at a time, one browser at a time.
const fs = require('fs'), path = require('path'), url = require('url'), zlib = require('zlib'), os = require('os');
const PW = process.env.BENCH_PW || 'C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package';
const pw = require(PW);
const ROOT = __dirname;
const GEN = path.join(ROOT, 'gen');
const LOGS = path.join(ROOT, 'logs');
const PROFILES = path.join(ROOT, 'profiles');
for (const d of [GEN, LOGS, PROFILES]) fs.mkdirSync(d, { recursive: true });

// ---------------------------------------------------------------------------------------------------------------
// Sources
const PREFIX = { coloured: 'caller -[#E74C3C]-> warehouse : "', plain: 'caller --> warehouse : "' };
function methodsText(n) {
  const items = [];
  for (let k = 1; k <= n; k++) items.push('INSERT INTO orders_archive_' + String(k).padStart(3, '0'));
  return 'ClickHouse: ' + items.join(', ') + ` - ${n} calls across 1 tests`;
}
// DiagramWidth.Wrap(label, 100) for this label shape: comma-separated items packed greedily into lines of at most 100
// characters, the space after the comma consumed by the break. Checked against the emitter's output (check-wrap).
function wrap100(text) {
  const parts = text.split(', ');
  const items = parts.map((p, i) => i < parts.length - 1 ? p + ',' : p);
  const lines = [];
  let cur = null;
  for (const it of items) {
    if (cur !== null && cur.length + 1 + it.length > 100) { lines.push(cur); cur = null; }
    cur = cur === null ? it : cur + ' ' + it;
  }
  if (cur !== null) lines.push(cur);
  return lines.join('\\n');
}
const LONG = methodsText(90);
const LONG_WRAPPED = wrap100(LONG);
// Cut text to exactly `len` characters without stranding the backslash of a `\n` escape.
function cut(text, len) {
  if (len > text.length) throw new Error('text too short for ' + len);
  let s = text.slice(0, len);
  let slashes = 0;
  while (slashes < s.length && s[s.length - 1 - slashes] === '\\') slashes++;
  if (slashes % 2 === 1) s = s.slice(0, -1) + 'x';
  return s;
}
// The edge line of shape `shape` at exactly L characters (the trimmed statement, quotes included).
function edgeLine(shape, L) {
  switch (shape) {
    case 'real': return PREFIX.coloured + cut(LONG_WRAPPED, L - PREFIX.coloured.length - 1) + '"';
    case 'unbroken': return PREFIX.coloured + cut(LONG, L - PREFIX.coloured.length - 1) + '"';
    case 'plain-wrapped': return PREFIX.plain + cut(LONG_WRAPPED, L - PREFIX.plain.length - 1) + '"';
    case 'plain-unbroken': return PREFIX.plain + cut(LONG, L - PREFIX.plain.length - 1) + '"';
    case 'short-lines': {
      // `abc\nabc\n...`: 3-character display lines; L - 34 = 5k - 2 when L is 832 (160 lines).
      const room = L - PREFIX.coloured.length - 1;
      const k = Math.floor((room + 2) / 5);
      let label = Array(k).fill('abc').join('\\n');
      label += 'x'.repeat(room - label.length);
      return PREFIX.coloured + label + '"';
    }
    case 'xfill': return PREFIX.coloured + 'x'.repeat(L - PREFIX.coloured.length - 1) + '"';
    default: throw new Error('unknown shape ' + shape);
  }
}
const pageCache = {};
function readPage(file) {
  if (pageCache[file]) return pageCache[file];
  const html = fs.readFileSync(file, 'utf8');
  const m = /data-plantuml-z="([^"]+)"/.exec(html);
  const source = zlib.gunzipSync(Buffer.from(m[1], 'base64')).toString('utf8');
  return (pageCache[file] = { html, z: m[1], source });
}
function replaceEdge(source, line) {
  const lines = source.split('\n');
  const i = lines.findIndex(l => l.startsWith('caller '));
  if (i < 0) throw new Error('no edge line');
  const cr = lines[i].endsWith('\r') ? '\r' : '';
  lines[i] = line + cr;
  return lines.join('\n');
}
function templateFor(pageKind) { return path.join(ROOT, 'pages', pageKind, 'n10', 'ComponentDiagram.html'); }
function realPage(pageKind, n) { return path.join(ROOT, 'pages', pageKind, 'n' + n, 'ComponentDiagram.html'); }
function writeCasePage(pageKind, shape, L) {
  const tpl = readPage(templateFor(pageKind));
  const line = edgeLine(shape, L);
  if (line.length !== L) throw new Error(`built ${line.length} for ${L}`);
  const src = replaceEdge(tpl.source, line);
  const nz = zlib.gzipSync(Buffer.from(src, 'utf8')).toString('base64');
  const out = tpl.html.replace(`data-plantuml-z="${tpl.z}"`, `data-plantuml-z="${nz}"`);
  const file = path.join(GEN, `${pageKind}-${shape}-${L}.html`);
  fs.writeFileSync(file, out);
  return file;
}

// ---------------------------------------------------------------------------------------------------------------
// Browsers
function profileDir(cfg) {
  const key = [cfg.browser, (cfg.jsflags || '').replace(/[^a-z0-9]+/gi, '_'), JSON.stringify(cfg.prefs || {}).replace(/[^a-z0-9]+/gi, '_'), JSON.stringify(cfg.env || {}).replace(/[^a-z0-9]+/gi, '_')].join('-').slice(0, 120);
  const d = path.join(PROFILES, key);
  fs.mkdirSync(d, { recursive: true });
  return d;
}
async function launch(cfg) {
  const opts = { headless: true };
  if (cfg.browser === 'chromium' || cfg.browser === 'chromium-new' || cfg.browser === 'chrome') {
    opts.args = cfg.jsflags ? ['--js-flags=' + cfg.jsflags] : [];
    if (cfg.browser === 'chrome') opts.channel = 'chrome';
    if (cfg.browser === 'chromium-new') opts.channel = 'chromium';
    return pw.chromium.launchPersistentContext(profileDir(cfg), opts);
  }
  if (cfg.browser === 'firefox') {
    if (cfg.prefs) opts.firefoxUserPrefs = cfg.prefs;
    return pw.firefox.launchPersistentContext(profileDir(cfg), opts);
  }
  if (cfg.browser === 'webkit') {
    if (cfg.env) opts.env = Object.assign({}, process.env, cfg.env);
    return pw.webkit.launchPersistentContext(profileDir(cfg), opts);
  }
  throw new Error('unknown browser ' + cfg.browser);
}

// In-page verdict for an element: null while nothing is there yet.
const CLASSIFY = `(function (el) {
  if (!el) return null;
  var svg = el.querySelector('svg');
  if (svg) {
    var texts = Array.prototype.map.call(svg.querySelectorAll('text'), function (t) { return t.textContent; });
    var all = svg.textContent || '';
    var first = (texts[0] || '').trim();
    var line = texts.filter(function (t) { return /From textarea/.test(t); })[0] || ''; var err = texts.filter(function (t) { return /RangeError|Maximum call stack|too much recursion|StackOverflow|Syntax Error|An error has occurred|Exception|Error/i.test(t); }).pop() || texts[texts.length - 1] || ''; var head = (line.trim() + ' ' + err.trim()).slice(0, 220);
    if (/RangeError|Maximum call stack|too much recursion|StackOverflow/i.test(all)) return { v: 'STACK', detail: head };
    if (/Syntax Error|An error has occurred|Cannot find|has crashed/i.test(all) || first.indexOf('PlantUML ') === 0) return { v: 'ERROR', detail: head };
    if (all.indexOf('Caller') >= 0 && all.indexOf('Warehouse') >= 0) return { v: 'drawn', w: svg.getAttribute('width'), h: svg.getAttribute('height') };
    return { v: 'NONAMES', detail: head };
  }
  var text = (el.textContent || '').trim();
  if (text) return { v: /Maximum call stack|too much recursion|RangeError/i.test(text) ? 'STACK-TEXT' : 'TEXT', detail: text.slice(0, 160) };
  return null;
})`;
const TEL = `(function () { var t = window.__kronikolRender || {}; return { mode: t.mode, workers: t.workers, fallbackReason: t.fallbackReason,
  engineIntegrity: t.engineIntegrity, vizIntegrity: t.vizIntegrity, renders: t.renders, errors: t.errors, webAssembly: t.webAssembly, ua: navigator.userAgent }; })()`;

async function coldCase(cfg, file) {
  const ctx = await launch(cfg);
  try {
    const page = ctx.pages()[0] || await ctx.newPage();
    const t0 = Date.now();
    await page.goto(url.pathToFileURL(file).href);
    const h = await page.waitForFunction(`${CLASSIFY}(document.getElementById('comp-diagram'))`, null, { polling: 200, timeout: 180000 });
    const r = await h.jsonValue();
    r.ms = Date.now() - t0;
    r.tel = await page.evaluate(TEL);
    return r;
  } catch (e) {
    return { v: 'HARNESS', detail: String(e && e.message || e).slice(0, 200) };
  } finally {
    await ctx.close().catch(() => {});
  }
}

const WARMUPS = +(process.env.WARMUPS || 50);
async function warmCase(cfg, shape, L) {
  const ctx = await launch(cfg);
  try {
    const page = ctx.pages()[0] || await ctx.newPage();
    const t0 = Date.now();
    await page.goto(url.pathToFileURL(realPage(cfg.page, 10)).href);
    const first = await (await page.waitForFunction(`${CLASSIFY}(document.getElementById('comp-diagram'))`, null, { polling: 200, timeout: 180000 })).jsonValue();
    if (first.v !== 'drawn') return { v: 'HARNESS', detail: 'the N=10 page did not draw: ' + JSON.stringify(first) };
    const tpl = readPage(templateFor(cfg.page));
    const lo = cfg.warmLo || 300, hi = cfg.warmHi || 1035;
    const warm = [];
    for (let i = 0; i < WARMUPS; i++) {
      const len = Math.round(lo + (hi - lo) * i / Math.max(1, WARMUPS - 1));
      warm.push(replaceEdge(tpl.source, edgeLine(cfg.warmShape || shape, len)));
    }
    const caseSrc = replaceEdge(tpl.source, edgeLine(shape, L));
    const res = await page.evaluate(async ([warm, caseSrc, classifySrc]) => {
      const classify = eval(classifySrc);
      const renderAndWait = (src, id) => new Promise(resolve => {
        const el = document.createElement('div'); el.id = id; document.body.appendChild(el);
        const to = setTimeout(() => { clearInterval(t); el.remove(); resolve({ v: 'TIMEOUT' }); }, 150000);
        const t = setInterval(() => { const r = classify(el); if (r) { clearInterval(t); clearTimeout(to); el.remove(); resolve(r); } }, 25);
        window.plantuml.render(src.split('\n'), id);
      });
      const warmVerdicts = {};
      for (let i = 0; i < warm.length; i++) {
        const r = await renderAndWait(warm[i], 'warm' + i);
        warmVerdicts[r.v] = (warmVerdicts[r.v] || 0) + 1;
      }
      const r = await renderAndWait(caseSrc, 'case');
      r.warm = warmVerdicts;
      return r;
    }, [warm, caseSrc, CLASSIFY]);
    res.ms = Date.now() - t0;
    res.tel = await page.evaluate(TEL);
    return res;
  } catch (e) {
    return { v: 'HARNESS', detail: String(e && e.message || e).slice(0, 200) };
  } finally {
    await ctx.close().catch(() => {});
  }
}

// ---------------------------------------------------------------------------------------------------------------
// Logging
function logFile(label) { return path.join(LOGS, label.replace(/[^a-z0-9._-]+/gi, '_') + '.jsonl'); }
function log(label, obj) {
  const line = JSON.stringify(Object.assign({ t: new Date().toISOString() }, obj));
  fs.appendFileSync(logFile(label), line + '\n');
  return line;
}
function brief(r) {
  return `${r.v}${r.detail ? ' [' + r.detail.slice(0, 90) + ']' : ''} ${r.ms || ''}ms ${r.tel ? r.tel.mode + '/' + r.tel.workers + 'w' : ''}${r.warm ? ' warm=' + JSON.stringify(r.warm) : ''}`;
}

async function verdict(cfg, shape, L, tag) {
  const r = cfg.mode === 'warm' ? await warmCase(cfg, shape, L) : await coldCase(cfg, writeCasePage(cfg.page, shape, L));
  log(cfg.label, { kind: 'case', tag, browser: cfg.browser, jsflags: cfg.jsflags || '', prefs: cfg.prefs || null, env: cfg.env || null, page: cfg.page, mode: cfg.mode, shape, L, v: r.v, detail: r.detail, ms: r.ms, tel: r.tel, warm: r.warm });
  console.log(`${cfg.label} ${shape} L=${L} ${tag}: ${brief(r)}`);
  return r;
}
const drew = r => r.v === 'drawn';

async function bisect(cfg) {
  const step = cfg.step || 10, reps = cfg.reps || 3, shape = cfg.shape;
  const snap = x => Math.round(x / step) * step;
  let lo = cfg.lo, hi = cfg.hi;
  const results = {};
  const record = (L, r) => { (results[L] = results[L] || []).push(r.v); };
  let r = await verdict(cfg, shape, hi, 'hi'); record(hi, r);
  if (drew(r)) {
    // Every length to hi draws: confirm hi reps times.
    for (let i = 1; i < reps; i++) { r = await verdict(cfg, shape, hi, 'hi-rep'); record(hi, r); }
    const sum = { kind: 'summary', label: cfg.label, shape, everyDrewTo: hi, results };
    log(cfg.label, sum); console.log(JSON.stringify(sum));
    return sum;
  }
  r = await verdict(cfg, shape, lo, 'lo'); record(lo, r);
  while (!drew(r)) {
    if (lo <= 100) { const sum = { kind: 'summary', label: cfg.label, shape, nothingDrewFrom: lo, results }; log(cfg.label, sum); console.log(JSON.stringify(sum)); return sum; }
    hi = lo; lo = Math.max(100, snap(lo / 2));
    r = await verdict(cfg, shape, lo, 'lo-down'); record(lo, r);
  }
  while (hi - lo > step) {
    const mid = snap((lo + hi) / 2);
    if (mid === lo || mid === hi) break;
    r = await verdict(cfg, shape, mid, 'mid'); record(mid, r);
    if (drew(r)) lo = mid; else hi = mid;
  }
  for (let i = 1; i < reps; i++) {
    r = await verdict(cfg, shape, lo, 'edge-draw-rep'); record(lo, r);
    r = await verdict(cfg, shape, hi, 'edge-fail-rep'); record(hi, r);
  }
  // A look either side: one step further on each side.
  if (cfg.around !== false) {
    r = await verdict(cfg, shape, lo - step, 'around'); record(lo - step, r);
    r = await verdict(cfg, shape, hi + step, 'around'); record(hi + step, r);
  }
  const sum = { kind: 'summary', label: cfg.label, browser: cfg.browser, jsflags: cfg.jsflags || '', page: cfg.page, mode: cfg.mode, shape, lastDraw: lo, firstFail: hi, results };
  log(cfg.label, sum); console.log(JSON.stringify(sum));
  return sum;
}

// Every length from `from` to `to` by `step` (and `to` itself), `reps` times each: for configurations expected to draw
// every length, where a bisection would only look at its two ends.
async function scan(cfg) {
  const reps = cfg.reps || 1, step = cfg.step || 100;
  const lengths = [];
  for (let L = cfg.from; L < cfg.to; L += step) lengths.push(L);
  lengths.push(cfg.to);
  const results = {};
  for (let i = 0; i < reps; i++)
    for (const L of lengths) {
      const r = await verdict(cfg, cfg.shape, L, 'scan');
      (results[L] = results[L] || []).push(r.v);
    }
  const failed = Object.entries(results).filter(([, vs]) => vs.some(v => v !== 'drawn'));
  const sum = { kind: 'summary', label: cfg.label, browser: cfg.browser, jsflags: cfg.jsflags || '', page: cfg.page, mode: cfg.mode, shape: cfg.shape, scan: `${cfg.from}..${cfg.to} by ${step} x${reps}`, failed, everyDrew: failed.length === 0 };
  log(cfg.label, sum); console.log(JSON.stringify(sum));
  return sum;
}

async function cases(cfg) {
  const reps = cfg.reps || 1;
  const out = [];
  // Repeats outside, cases inside: each pass runs every case once, so a drift over time shows as a pass, not a case.
  for (let i = 0; i < reps; i++) {
    for (const c of cfg.cases) {
      let r;
      if (c.n !== undefined) {
        // The emitter's own page for N statements, unmodified.
        const file = realPage(cfg.page, c.n);
        r = await coldCase(cfg, file);
        const edge = fs.readFileSync(path.join(path.dirname(file), 'edge.txt'), 'utf8');
        log(cfg.label, { kind: 'case', tag: 'real-n', browser: cfg.browser, jsflags: cfg.jsflags || '', page: cfg.page, mode: 'cold', n: c.n, L: edge.length, v: r.v, detail: r.detail, ms: r.ms, tel: r.tel });
        console.log(`${cfg.label} N=${c.n} L=${edge.length}: ${brief(r)}`);
        out.push({ n: c.n, L: edge.length, v: r.v });
      } else {
        r = await verdict(cfg, c.shape, c.L, 'case');
        out.push({ shape: c.shape, L: c.L, v: r.v });
      }
    }
  }
  const sum = { kind: 'summary', label: cfg.label, out };
  log(cfg.label, sum);
  return sum;
}

// ---------------------------------------------------------------------------------------------------------------
// Stack depth: a trivial recursive function on the main thread and in a Blob worker (as the shim creates them).
const STACK_SRC = `
function probeDepths() {
  function simple() { var d = 0; function f() { d++; f(); } try { f(); } catch (e) { return { d: d, e: String(e).slice(0, 60) }; } }
  function fat() { var d = 0; function g(a, b, c, e1, e2) { d++; var x = a + b, y = b + c, z = x * y, w = z - a; g(x, y, z, w, d); return x + y + z + w; } try { g(1, 2, 3, 4, 5); } catch (e) { return { d: d, e: String(e).slice(0, 60) }; } }
  // hot: the same function made hot first (bounded recursion, 4000 times), so a JIT has compiled it, then unbounded.
  var hd = 0, hlim = 0; function h(x) { hd++; if (hd < hlim) return h(x) + x; return x; }
  function hot() { hd = 0; hlim = 1e12; try { h(1); } catch (e) { return hd; } return -1; }
  var out = { simple: [], fat: [], hot: [] };
  for (var i = 0; i < 6; i++) { out.simple.push(simple().d); out.fat.push(fat().d); }
  out.hot.push(hot());
  for (var j = 0; j < 4000; j++) { hd = 0; hlim = 300; h(1); }
  for (var k = 0; k < 4; k++) out.hot.push(hot());
  out.err = simple().e;
  return out;
}`;
async function stack(cfg) {
  const ctx = await launch(cfg);
  try {
    const file = path.join(GEN, 'stack.html');
    fs.writeFileSync(file, `<!DOCTYPE html><html><head><meta charset="utf-8"><script>${STACK_SRC}
window.__stack = new Promise(function (resolve) {
  setTimeout(function () {
    var main = probeDepths();
    var blob = new Blob([${JSON.stringify(STACK_SRC)} + '\\nself.onmessage = function () { self.postMessage(probeDepths()); };'], { type: 'application/javascript' });
    var w = new Worker(URL.createObjectURL(blob));
    w.onmessage = function (ev) { resolve({ main: main, worker: ev.data, ua: navigator.userAgent }); };
    w.onerror = function (ev) { resolve({ main: main, worker: 'error ' + ev.message, ua: navigator.userAgent }); };
    w.postMessage('go');
  }, 50);
});
</script></head><body>stack</body></html>`);
    const page = ctx.pages()[0] || await ctx.newPage();
    await page.goto(url.pathToFileURL(file).href);
    const r = await page.evaluate('window.__stack');
    const sum = { kind: 'stack', label: cfg.label, browser: cfg.browser, jsflags: cfg.jsflags || '', prefs: cfg.prefs || null, env: cfg.env || null, r,
      ratioSimple: (Math.max(...r.main.simple) / Math.max(...r.worker.simple)).toFixed(2),
      ratioFat: (Math.max(...r.main.fat) / Math.max(...r.worker.fat)).toFixed(2), ratioHot: (Math.max(...r.main.hot) / Math.max(...r.worker.hot)).toFixed(2) };
    log(cfg.label, sum);
    console.log(JSON.stringify(sum));
    return sum;
  } finally {
    await ctx.close().catch(() => {});
  }
}

// ---------------------------------------------------------------------------------------------------------------
async function main() {
  const [cmd, json] = process.argv.slice(2);
  if (cmd === 'check-wrap') {
    // The JS wrap against the emitter's own edge lines.
    for (const n of [10, 15, 20, 30, 40, 44, 48, 56]) {
      const edge = fs.readFileSync(path.join(ROOT, 'pages', 'w4', 'n' + n, 'edge.txt'), 'utf8');
      const mine = PREFIX.coloured + wrap100(methodsText(n)) + '"';
      console.log(n, edge.length, mine.length, edge === mine ? 'IDENTICAL' : 'DIFFERENT');
    }
    const edge66 = fs.readFileSync(path.join(ROOT, 'pages', 'w4', 'n66', 'edge.txt'), 'utf8');
    const mine66 = PREFIX.coloured + LONG_WRAPPED.slice(0, 1944) + '…"';
    console.log(66, edge66.length, mine66.length, edge66 === mine66 ? 'IDENTICAL (capped)' : 'DIFFERENT');
    for (const s of ['real', 'unbroken', 'plain-wrapped', 'plain-unbroken', 'short-lines', 'xfill']) {
      const l = edgeLine(s, s === 'short-lines' ? 832 : 600);
      console.log(s, l.length, (l.match(/\\n/g) || []).length + 1, 'display lines', JSON.stringify(l.slice(0, 70)), '...', JSON.stringify(l.slice(-30)));
    }
    return;
  }
  const cfg = JSON.parse(json);
  if (cmd === 'bisect') await bisect(cfg);
  else if (cmd === 'cases') await cases(cfg);
  else if (cmd === 'stack') await stack(cfg);
  else if (cmd === 'scan') await scan(cfg);
  else throw new Error('unknown command ' + cmd);
}
main().catch(e => { console.error(e); process.exit(1); });
