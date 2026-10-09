'use strict';
// #162 follow-up: Kronikol's other statement caps in the BrowserJs worker. Sources come from Kronikol 4.14.4's own
// emitters (gen/gen.cs -> src/); each case puts one source into the ComponentDiagram.html page written with
// BrowserRenderWorkers = 4 (../pages/w4/n10, the shipped render script and settings, data-plantuml-z replaced) and
// opens it in a fresh browser, so the render is the page's first, in a fresh worker.
//   node follow.js bisect '<json>'  {label, browser, jsflags, shape, lo, hi, reps}
//   node follow.js once   '<json>'  {label, browser, jsflags, shape, values:[...], reps}
//   node follow.js legacy '<json>'  {label, browser, jsflags}: the 09-25 legacy probe's own cases, one page, one after another
// Verdict: an <svg> with no RangeError / "Maximum call stack", no "Syntax Error?" / "An error has occurred", whose first
// drawn line does not start with "PlantUML ", and that draws every expected name (a sequence participant twice).
const fs = require('fs'), path = require('path'), url = require('url'), zlib = require('zlib');
const pw = require(process.env.BENCH_PW || 'C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package');
const ROOT = __dirname;
const SRC = path.join(ROOT, 'src');
const GEN = path.join(ROOT, 'pages'), LOGS = path.join(ROOT, 'logs'), PROFILES = path.join(ROOT, 'profiles');
for (const d of [GEN, LOGS, PROFILES]) fs.mkdirSync(d, { recursive: true });
const TEMPLATE = path.join(ROOT, '..', 'pages', 'w4', 'n10', 'ComponentDiagram.html');

// ---- sources -----------------------------------------------------------------------------------------------------
const read = f => fs.readFileSync(path.join(SRC, f), 'utf8');
const lineOf = (src, rx) => src.split('\n').find(l => rx.test(l));
function replaceLine(src, rx, line) {
  const lines = src.split('\n');
  const i = lines.findIndex(l => rx.test(l));
  if (i < 0) throw new Error('no line ' + rx);
  lines[i] = line + (lines[i].endsWith('\r') ? '\r' : '');
  return lines.join('\n');
}
// The 09-25 legacy probe's filler: the linked label of plans/DIAGRAM_COLOURS_PLAN.harness/payload-longpath.puml plus
// sixty &extraN=valueN pairs, cut with a trailing ellipsis.
const payload = fs.readFileSync('C:/Code/Kronikol/plans/DIAGRAM_COLOURS_PLAN.harness/payload-longpath.puml', 'utf8').replace(/\r\n/g, '\n');
const linkedSt = payload.split('\n').find(l => l.includes('[[#iflow-'));
const lm = /^(.*?: )\[\[#iflow-[0-9a-f-]+ (.*)\]\]$/.exec(linkedSt);
const LEGACY_LABEL = lm[2].replace(/…$/, '') + Array.from({ length: 60 }, (_, i) => '&extra' + i + '=value' + i).join('');
const legacyCut = (s, n) => s.slice(0, n - 1) + '…';
// Cut a statement to exactly n characters without stranding the backslash of a two-character escape.
function cutAt(text, n) {
  let s = text.slice(0, n);
  let slashes = 0;
  while (slashes < s.length && s[s.length - 1 - slashes] === '\\') slashes++;
  if (slashes % 2 === 1) s = s.slice(0, -1) + 'x';
  return s;
}
// PlantUmlStatementLimits.TruncateLabel, as statement-limits-worker-probe.js ports it.
function truncateLabel(label, budget) {
  const marker = '…';
  if (budget <= 0) return '';
  if (label.length <= budget) return label;
  let c = Math.max(0, budget - marker.length);
  for (let p = c - 1; p >= 0 && p >= c - 10; p--) { if (label[p] === '>') break; if (label[p] !== '<') continue; if (label.startsWith('<U+', p)) c = p; break; }
  let slashes = 0;
  while (c - slashes > 0 && label[c - slashes - 1] === '\\') slashes++;
  if (slashes % 2 === 1) c--;
  return label.slice(0, c) + marker;
}
// DiagramWidth.Wrap(label, 100) for the method-list label (checked against the emitter in ../measure.js check-wrap).
function wrap100(text) {
  const parts = text.split(', ');
  const items = parts.map((p, i) => i < parts.length - 1 ? p + ',' : p);
  const lines = []; let cur = null;
  for (const it of items) { if (cur !== null && cur.length + 1 + it.length > 100) { lines.push(cur); cur = null; } cur = cur === null ? it : cur + ' ' + it; }
  if (cur !== null) lines.push(cur);
  return lines.join('\\n');
}
const methods = n => 'ClickHouse: ' + Array.from({ length: n }, (_, k) => 'INSERT INTO orders_archive_' + String(k + 1).padStart(3, '0')).join(', ');
const STATS_TAIL = ']]\\nP50: 45ms | P95: 120ms | P99: 999ms | 12% errors\\n48 calls across 5 tests"';
const STATS_HEAD = 'caller -[#E74C3C]-> warehouse : "[[#iflow-rel-Caller-Warehouse ';
// The emitter wraps the whole first display line, link opener included, then cuts the text inside the link.
const LINK_OPEN = '[[#iflow-rel-Caller-Warehouse ';
const LINK_FULL = wrap100(LINK_OPEN + methods(90) + ']]');
const LINK_INNER = LINK_FULL.slice(LINK_OPEN.length, LINK_FULL.length - 2);
const statsEdge = n => STATS_HEAD + truncateLabel(LINK_INNER, n) + STATS_TAIL;

const BAR_PREFIX = 'hnote across <<stepDelimiter>> #black:<color:white>';
const LEGACY_UNBROKEN = LEGACY_LABEL.split('\\n        ').join('');
const TOKEN_BAR = lineOf(read('seq-bar-token.puml'), /^hnote across/).replace(/\r$/, '');
const NAME0 = 'Svcorders-archive-replica.eu-west-1.inte';   // the first 40 characters of every long participant name
const SQL0 = 'SELECT o.Id, o.CustomerId, o.Total';
// shape -> value -> { src, expect: [[text, minCount]], sequence }
const SHAPES = {
  // 1. Block openers: the emitter's collapsed-run diagram with its `loop ×6 · 37 ms` opener, and its setup diagram with
  // `partition #F6F6F6 Setup`, each opener lengthened to exactly v characters with the legacy probe's filler.
  loop: v => ({ src: replaceLine(read('seq-loop.puml'), /^loop /, 'loop ' + legacyCut(LEGACY_LABEL, v - 5)), expect: [['Api', 2], ['StockService', 2], ['OrderService', 2]] }),
  partition: v => ({ src: replaceLine(read('seq-partition.puml'), /^partition /, 'partition #F6F6F6 ' + legacyCut(LEGACY_LABEL, v - 18)), expect: [['Api', 2], ['CustomerService', 2], ['OrderService', 2]] }),
  // 2. The coloured step bar as the emitter writes a step that is one unbreakable token (StepBarPlantUml.Build: the only
  // way the legacy form gets long; at 1,400 it is the emitter's own capped statement), cut to v; and the styled form a
  // long step with spaces takes (wrapped every 110, capped at 16,000), cut to v.
  'bar-token': v => ({ src: replaceLine(read('seq-bar-token.puml'), /^hnote across/, cutAt(lineOf(read('seq-bar-token.puml'), /^hnote across/).replace(/\r$/, ''), v)), expect: [['Api', 2], ['OrderService', 2]] }),
  'bar-rich': v => ({ src: replaceLine(read('seq-bar-rich.puml'), /^hnote across/, cutAt(lineOf(read('seq-bar-rich.puml'), /^hnote across/).replace(/\r$/, ''), v)), expect: [['Api', 2], ['OrderService', 2]] }),
  // Controls for the legacy probe's bars: Kronikol's bar prefix with the legacy filler (which carries `\n        `
  // display breaks), the same filler with those breaks taken out, and the emitter's token bar without <<stepDelimiter>>.
  'bar-legacy-label': v => ({ src: replaceLine(read('seq-bar-token.puml'), /^hnote across/, BAR_PREFIX + legacyCut(LEGACY_LABEL, v - BAR_PREFIX.length)), expect: [['Api', 2], ['OrderService', 2]] }),
  'bar-legacy-label-unbroken': v => ({ src: replaceLine(read('seq-bar-token.puml'), /^hnote across/, BAR_PREFIX + legacyCut(LEGACY_UNBROKEN, v - BAR_PREFIX.length)), expect: [['Api', 2], ['OrderService', 2]] }),
  'bar-token-nostereo': v => ({ src: replaceLine(read('seq-bar-token.puml'), /^hnote across/, cutAt(TOKEN_BAR.replace(' <<stepDelimiter>>', ''), v)), expect: [['Api', 2], ['OrderService', 2]] }),
  // 3, 4, 6. A long name (v characters) through the emitter, as written (wrapped every 80, the alias derived from it).
  'seq-entity': v => ({ src: read(`seq-entity/${v}.puml`), expect: [['Api', 2], [NAME0.slice(0, Math.min(40, v)), 2]] }),
  'seq-database': v => ({ src: read(`seq-database/${v}.puml`), expect: [['Api', 2], [NAME0.slice(0, Math.min(40, v)), 2]] }),
  'comp-database': v => ({ src: read(`comp-database/${v}.puml`), expect: [['Caller', 1], [NAME0.slice(0, Math.min(40, v)), 1]] }),
  'comp-system': v => ({ src: read(`comp-system/${v}.puml`), expect: [['Caller', 1], ['StockService', 1], [NAME0.slice(0, Math.min(40, v)), 1]] }),
  activity: v => ({ src: read(`activity/${v}.puml`), expect: [['POST /orders', 1], [SQL0.slice(0, Math.min(30, v)), 1]] }),
  // 5. The stats-linked edge: link text cut to v (the emitter's cap is 350), then its two stats lines.
  'stats-edge': v => ({ src: replaceLine(read('comp-stats.puml'), /^caller -/, statsEdge(v)), expect: [['Caller', 1], ['Warehouse', 1]] }),
  'stats-edge-asis': () => ({ src: read('comp-stats.puml'), expect: [['Caller', 1], ['Warehouse', 1]] }),
};
const SEQUENCE = new Set(['loop', 'partition', 'bar-token', 'bar-rich', 'seq-entity', 'seq-database']);
function describe(src) {
  // The longest statement and the kind of line it is, for the record.
  const lines = src.split('\n').map(l => l.replace(/\r$/, '').trim());
  let best = '';
  for (const l of lines) if (l.length > best.length) best = l;
  const kind = /^(loop|partition) /.test(best) ? 'opener' : /^hnote/.test(best) ? 'bar' : /^(entity|database|actor|participant|rectangle) /.test(best) ? 'declaration'
    : /^:/.test(best) ? 'action' : /-\[|->|-->/.test(best) ? 'arrow' : 'other';
  const decl = lines.find(l => /^(entity|database|rectangle) "(Svc|\*\*Svc)/.test(l));
  const arrow = lines.filter(l => /-\[#|->/.test(l)).reduce((a, l) => l.length > a.length ? l : a, '');
  return { maxLine: best.length, maxKind: kind, declLine: decl ? decl.length : undefined, arrowLine: arrow.length || undefined };
}

// ---- pages and browsers ------------------------------------------------------------------------------------------
const tplHtml = fs.readFileSync(TEMPLATE, 'utf8');
const tplZ = /data-plantuml-z="([^"]+)"/.exec(tplHtml)[1];
function writePage(name, src) {
  const nz = zlib.gzipSync(Buffer.from(src, 'utf8')).toString('base64');
  const file = path.join(GEN, name.replace(/[^a-z0-9._-]+/gi, '_') + '.html');
  fs.writeFileSync(file, tplHtml.replace(`data-plantuml-z="${tplZ}"`, `data-plantuml-z="${nz}"`));
  return file;
}
function profileDir(cfg) {
  const d = path.join(PROFILES, cfg.browser + '-' + (cfg.jsflags ? cfg.jsflags.replace(/[^a-z0-9]+/gi, '_') : 'jit'));
  fs.mkdirSync(d, { recursive: true });
  return d;
}
function launch(cfg) {
  const opts = { headless: true, args: cfg.jsflags ? ['--js-flags=' + cfg.jsflags] : [] };
  if (cfg.browser === 'chrome') opts.channel = 'chrome';
  return pw.chromium.launchPersistentContext(profileDir(cfg), opts);
}
const CLASSIFY = `(function (el, expect) {
  if (!el) return null;
  var svg = el.querySelector('svg');
  if (svg) {
    var texts = Array.prototype.map.call(svg.querySelectorAll('text'), function (t) { return t.textContent; });
    var all = (svg.textContent || '').replace(/\\u00a0/g, ' ');
    var first = (texts[0] || '').trim();
    var at = (texts.filter(function (t) { return /From textarea/.test(t); })[0] || '').trim();
    var err = (texts.filter(function (t) { return /RangeError|Maximum call stack|too much recursion|Syntax Error|An error has occurred|Exception/i.test(t); }).pop() || '').trim();
    var head = (first.slice(0, 30) + ' | ' + at + ' ' + err.slice(-160)).slice(0, 260);
    var svgs = el.querySelectorAll('svg').length;
    if (/RangeError|Maximum call stack|too much recursion|StackOverflow/i.test(all)) return { v: 'STACK', detail: head, svg: true, svgs: svgs };
    if (/Syntax Error\\?|An error has occurred/i.test(all) || first.indexOf('PlantUML ') === 0) return { v: 'ERROR', detail: head, svg: true, svgs: svgs };
    var missing = [];
    for (var i = 0; i < expect.length; i++) {
      var n = all.split(expect[i][0]).length - 1;
      if (n < expect[i][1]) missing.push(expect[i][0].slice(0, 16) + ' ' + n + '/' + expect[i][1]);
    }
    if (missing.length) return { v: 'MISSING', detail: missing.join(', ') + ' | ' + texts.slice(0, 3).join(' | ').slice(0, 120), svg: true, svgs: svgs };
    return { v: 'drawn', svg: true, svgs: svgs };
  }
  var text = (el.textContent || '').trim();
  if (text) return { v: /Maximum call stack|too much recursion|RangeError/i.test(text) ? 'STACK-TEXT' : 'TEXT', detail: text.slice(0, 200), svg: false };
  return null;
})`;
async function coldCase(cfg, file, expect, src) {
  const ctx = await launch(cfg);
  try {
    const page = ctx.pages()[0] || await ctx.newPage();
    const t0 = Date.now();
    await page.goto(url.pathToFileURL(file).href);
    const h = await page.waitForFunction(([c, e]) => (0, eval)(c)(document.getElementById('comp-diagram'), e), [CLASSIFY, expect], { polling: 200, timeout: 180000 });
    const r = await h.jsonValue();
    r.ms = Date.now() - t0;
    const info = await page.evaluate(() => { const t = window.__kronikolRender || {}; const el = document.getElementById('comp-diagram');
      return { mode: t.mode, workers: t.workers, engine: t.engineIntegrity, ua: navigator.userAgent, rendered: el.getAttribute('data-plantuml'), frags: el.querySelectorAll('.puml-fragment').length }; });
    r.tel = { mode: info.mode, workers: info.workers, engine: info.engine, ua: (/(HeadlessChrome|Chrome)\/[\d.]+/.exec(info.ua) || [''])[0] };
    // The page renders the source it was given, unsplit: anything else is flagged.
    const norm = s => (s || '').replace(/\r/g, '').trim();
    r.srcSame = norm(info.rendered) === norm(src);
    r.frags = info.frags;
    return r;
  } catch (e) {
    return { v: 'HARNESS', detail: String(e && e.message || e).slice(0, 200) };
  } finally {
    await ctx.close().catch(() => {});
  }
}
const logTo = (label, o) => fs.appendFileSync(path.join(LOGS, label + '.jsonl'), JSON.stringify(Object.assign({ t: new Date().toISOString() }, o)) + '\n');
async function verdict(cfg, shape, v, tag) {
  const { src, expect } = SHAPES[shape](v);
  if (src.includes('@@FRAGMENT@@')) throw new Error(shape + ' ' + v + ': the emitter split the diagram');
  const file = writePage(`${shape}-${v}`, src);
  const r = await coldCase(cfg, file, expect, src);
  const d = describe(src);
  logTo(cfg.label, Object.assign({ kind: 'case', tag, browser: cfg.browser, jsflags: cfg.jsflags || '', shape, value: v }, d, r));
  console.log(`${cfg.label} ${shape} v=${v} (max line ${d.maxLine} ${d.maxKind}${d.declLine ? ', decl ' + d.declLine : ''}${d.arrowLine ? ', arrow ' + d.arrowLine : ''}) ${tag}: ${r.v}${r.detail ? ' [' + r.detail.slice(0, 150) + ']' : ''} ${r.ms || ''}ms ${r.tel ? r.tel.mode + '/' + r.tel.workers : ''}${r.srcSame === false ? ' SOURCE-CHANGED' : ''}${r.frags ? ' FRAGS=' + r.frags : ''}`);
  return r;
}
const drew = r => r.v === 'drawn';

async function bisect(cfg) {
  const step = cfg.step || 10, reps = cfg.reps || 2, shape = cfg.shape;
  const snap = x => Math.round(x / step) * step;
  let lo = cfg.lo, hi = cfg.hi;
  const results = {};
  const rec = (v, r) => (results[v] = results[v] || []).push(r.v);
  let r = await verdict(cfg, shape, hi, 'hi'); rec(hi, r);
  if (drew(r)) {
    for (let i = 1; i < reps; i++) { r = await verdict(cfg, shape, hi, 'hi-rep'); rec(hi, r); }
    const sum = { kind: 'summary', label: cfg.label, shape, everyDrewTo: hi, results };
    logTo(cfg.label, sum); console.log(JSON.stringify(sum));
    return;
  }
  r = await verdict(cfg, shape, lo, 'lo'); rec(lo, r);
  while (!drew(r)) {
    if (lo <= (cfg.min || 20)) { const sum = { kind: 'summary', label: cfg.label, shape, nothingDrewFrom: lo, results }; logTo(cfg.label, sum); console.log(JSON.stringify(sum)); return; }
    hi = lo; lo = Math.max(cfg.min || 20, snap(lo / 2));
    r = await verdict(cfg, shape, lo, 'lo-down'); rec(lo, r);
  }
  while (hi - lo > step) {
    const mid = snap((lo + hi) / 2);
    if (mid === lo || mid === hi) break;
    r = await verdict(cfg, shape, mid, 'mid'); rec(mid, r);
    if (drew(r)) lo = mid; else hi = mid;
  }
  for (let i = 1; i < reps; i++) {
    r = await verdict(cfg, shape, lo, 'edge-draw-rep'); rec(lo, r);
    r = await verdict(cfg, shape, hi, 'edge-fail-rep'); rec(hi, r);
  }
  r = await verdict(cfg, shape, lo - step, 'around'); rec(lo - step, r);
  r = await verdict(cfg, shape, hi + step, 'around'); rec(hi + step, r);
  const sum = { kind: 'summary', label: cfg.label, browser: cfg.browser, jsflags: cfg.jsflags || '', shape, lastDraw: lo, firstFail: hi, results };
  logTo(cfg.label, sum); console.log(JSON.stringify(sum));
}
async function once(cfg) {
  for (let i = 0; i < (cfg.reps || 1); i++)
    for (const v of cfg.values) await verdict(cfg, cfg.shape, v, 'once');
}

// The 09-25 legacy mode's own cases (statement-limits-worker-probe.js legacy()), built the same way, rendered one after
// another in one page as it did, each verdict read both ways: the legacy rule (any <svg> is "drawn") and the strict one.
async function legacy(cfg) {
  const base = payload;
  const st = linkedSt, m = lm;
  const label = LEGACY_LABEL, cut = legacyCut;
  const head = st.slice(0, st.indexOf(m[2]));
  const cases = [];
  for (const n of [1000, 1500, 1800, 2000]) cases.push({ kind: 'message', n, src: base.replace(st, m[1] + cut(label, n - m[1].length)), expect: [['Caller', 2], ['Orders API', 2]] });
  const mini = body => `@startuml\n!pragma teoz true\nparticipant A\nparticipant B\nA -> B : one\n${body}\nB --> A : two\n@enduml`;
  for (const n of [800, 1000, 1200, 1300, 1400, 1471, 1600, 2000]) cases.push({ kind: 'loop label', n, src: mini(`loop ${cut(label, n)}\nA -> B : in\nend`), expect: [['A', 2], ['B', 2]] });
  const bar = 'hnote across #black:<color:white>';
  for (const n of [800, 1000, 1200, 1300, 1400, 1600, 2000]) cases.push({ kind: 'coloured bar', n, src: mini(bar + cut(label, n - bar.length)), expect: [['A', 2], ['B', 2]] });
  const ctx = await launch(cfg);
  try {
    const page = ctx.pages()[0] || await ctx.newPage();
    await page.goto(url.pathToFileURL(writePage('legacy-host', read('seq-bar-short.puml'))).href);
    await page.waitForFunction(() => document.querySelector('#comp-diagram svg'), null, { polling: 200, timeout: 180000 });
    for (const c of cases) {
      const r = await page.evaluate(([src, id, classify, expect]) => new Promise(resolve => {
        const el = document.createElement('div'); el.id = id; document.body.appendChild(el);
        const cls = (0, eval)(classify);
        const to = setTimeout(() => { clearInterval(t); resolve({ v: 'TIMEOUT' }); }, 90000);
        const t = setInterval(() => { const r = cls(el, expect); if (r) { clearInterval(t); clearTimeout(to); el.remove(); resolve(r); } }, 50);
        window.plantuml.render(src.split('\n'), id);
      }), [c.src, 'lg' + cases.indexOf(c), CLASSIFY, c.expect]);
      const legacyVerdict = r.svg ? 'drawn' : (r.detail || r.v);
      logTo(cfg.label, { kind: 'legacy', browser: cfg.browser, jsflags: cfg.jsflags || '', case: c.kind, n: c.n, legacyVerdict, strict: r.v, detail: r.detail });
      console.log(`${cfg.label} ${c.kind} ${c.n}: legacy rule says ${legacyVerdict}; strict ${r.v}${r.detail ? ' [' + r.detail.slice(0, 140) + ']' : ''}`);
    }
  } finally {
    await ctx.close().catch(() => {});
  }
}

(async () => {
  const [cmd, json] = process.argv.slice(2);
  if (cmd === 'check') {
    // The stats edge built here against the emitter's own, and each shape's line at a few values.
    const emitted = lineOf(read('comp-stats.puml'), /^caller -/).replace(/\r$/, '');
    console.log('stats edge at 350 identical to the emitter:', statsEdge(350) === emitted, emitted.length);
    for (const [s, v] of [['loop', 600], ['partition', 600], ['bar-token', 600], ['bar-rich', 600], ['bar-token', 1400], ['seq-entity', 300], ['seq-database', 300], ['comp-database', 300], ['comp-system', 300], ['activity', 300], ['stats-edge', 350]]) {
      const { src } = SHAPES[s](v);
      console.log(s, v, JSON.stringify(describe(src)));
    }
    const tokenLine = lineOf(read('seq-bar-token.puml'), /^hnote across/).replace(/\r$/, '');
    console.log('emitter token bar length', tokenLine.length, 'equals bar-token 1400:', SHAPES['bar-token'](1400).src === read('seq-bar-token.puml'));
    return;
  }
  const cfg = JSON.parse(json);
  if (cmd === 'bisect') await bisect(cfg);
  else if (cmd === 'once') await once(cfg);
  else if (cmd === 'legacy') await legacy(cfg);
  else throw new Error('unknown ' + cmd);
})().catch(e => { console.error(e); process.exit(1); });
