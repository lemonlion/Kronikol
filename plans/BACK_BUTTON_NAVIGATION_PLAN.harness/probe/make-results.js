'use strict';
// Builds RESULTS.md from results/<engine>.json. Run from this folder: node make-results.js
// Each cell is run 1's value; when run 2 differs the cell shows both and says so.
const fs = require('fs');
const path = require('path');

const R = path.join(__dirname, 'results');
const load = (e) => { const f = path.join(R, e + '.json'); return fs.existsSync(f) ? JSON.parse(fs.readFileSync(f, 'utf8')) : null; };
const DATA = { chromium: load('chromium'), 'chromium-real': load('chromium-real'), firefox: load('firefox'), webkit: load('webkit') };
const NOTES = fs.existsSync(path.join(__dirname, 'notes.js')) ? require('./notes.js') : {};

const NAMES = { chromium: 'Chromium', 'chromium-real': 'Chromium (bfcache + rate limiter on)', firefox: 'Firefox', webkit: 'WebKit',
                'firefox-strict': 'Firefox (stock file:// origin pref)', cft: 'Chrome for Testing' };
const COLS = [['chromium', 'file'], ['chromium', 'http'], ['firefox', 'file'], ['firefox', 'http'], ['webkit', 'file'], ['webkit', 'http']];
const REAL = [['chromium-real', 'file'], ['chromium-real', 'http']];
const FILE_COLS = [['chromium', 'file'], ['firefox', 'file'], ['firefox-strict', 'file'], ['webkit', 'file']];
// The binary each configuration actually ran (a headless Chromium launch with no channel runs the headless shell;
// browserType().executablePath() names the full build instead).
const BINARY = {
  chromium: 'ms-playwright\\chromium_headless_shell-1217\\chrome-headless-shell-win64\\chrome-headless-shell.exe',
  'chromium-real': 'ms-playwright\\chromium_headless_shell-1217\\chrome-headless-shell-win64\\chrome-headless-shell.exe',
  firefox: 'ms-playwright\\firefox-1511\\firefox\\firefox.exe',
  webkit: 'ms-playwright\\webkit-2272\\Playwright.exe',
};

// Extras live in results/extras-<engine>-<case>.json; a case name 'x:<case>' reads them.
const XCACHE = {};
function getX(engine, kase) {
  const k = `${engine}-${kase}`;
  if (!(k in XCACHE)) {
    const f = engine === 'cft' ? path.join(R, 'cft-bfcache.json') : path.join(R, `extras-${k}.json`);
    XCACHE[k] = fs.existsSync(f) ? JSON.parse(fs.readFileSync(f, 'utf8')) : null;
  }
  return XCACHE[k];
}
function get(engine, proto, kase, run) {
  if (kase.startsWith('x:')) {
    const d = getX(engine, kase.slice(2));
    if (!d) return undefined;
    const hit = d.results.find((x) => x.run === run && x.proto === proto);
    return hit ? hit.res : undefined;
  }
  const d = DATA[engine];
  if (!d) return undefined;
  const hit = d.results.find((x) => x.run === run && x.proto === proto && x.case === kase);
  return hit ? hit.res : undefined;
}

// ---------- formatting ----------
const js = (v) => (v === undefined ? 'undefined' : JSON.stringify(v).replace(/"([A-Za-z_$][\w$]*)":/g, '$1:'));
const frag = (u) => { if (typeof u !== 'string') return String(u); const i = u.indexOf('#'); return i < 0 ? '(no hash)' : u.slice(i); };
const hashOf = (h) => (h ? h : '(no hash)');
const shortUrl = (s) => String(s).replace(/file:\/\/\/[^'"\s]*?\/pages\//g, 'file:///…/').replace(/http:\/\/127\.0\.0\.1:\d+/g, 'http://127.0.0.1:port');
const esc = (s) => String(s).replace(/\|/g, '\\|').replace(/\n/g, ' ');
function evName(e) {
  switch (e.type) {
    case 'popstate': return `popstate(state=${js(e.evState)})`;
    case 'hashchange': return `hashchange(${frag(e.oldURL)}>${frag(e.newURL)})`;
    case 'navigate': return `navigate(${e.navigationType}${e.hashChange ? ', hashChange' : ''}${e.userInitiated ? ', userInitiated' : ''}`
      + `${e.canIntercept === false ? ', canIntercept=false' : ''}${e.intercepted ? ', intercepted' : ''}`
      + `${e.interceptError ? ', intercept threw ' + e.interceptError : ''})`;
    case 'currententrychange': return 'currententrychange';
    case 'navigatesuccess': return 'navigatesuccess';
    case 'navigateerror': return `navigateerror(${e.message})`;
    case 'pageshow': return `pageshow(persisted=${e.persisted})`;
    case 'pagehide': return `pagehide(persisted=${e.persisted})`;
    default: return e.type;
  }
}
const seq = (list) => (!Array.isArray(list) ? js(list) : list.length ? list.map(evName).join(' then ') : 'none');
const legacy = (list) => seq(Array.isArray(list) ? list.filter((e) => e.type === 'popstate' || e.type === 'hashchange') : list);
const full = (list) => seq(Array.isArray(list) ? list.filter((e) => e.type !== 'pageshow' && e.type !== 'pagehide') : list);
// Drops the events of the replaceState that set up the case: keeps everything from the first event the assignment caused.
const fromFirst = (list) => {
  if (!Array.isArray(list)) return list;
  const i = list.findIndex((e) => (e.type === 'navigate' && e.navigationType !== 'replace') || e.type === 'popstate' || e.type === 'hashchange');
  return i < 0 ? [] : list.slice(i);
};
const navRes = (n) => (!n ? 'n/a' : n.ok ? 'resolved' : n.err.replace(/^page\.\w+: /, ''));
const tryRes = (t) => (!t ? 'n/a' : t.ok ? `ok, hash ${hashOf(t.hash)}` : t.err);
const yesno = (b) => (b ? 'yes' : 'no');

function cell(engine, proto, kase, fmt, opts) {
  const val = (run) => {
    const res = get(engine, proto, kase, run);
    if (res === undefined) return 'not run';
    if (res && res.harnessError) return 'harness error: ' + res.harnessError;
    try { return shortUrl(fmt(res)); } catch (e) { return 'n/a (' + e.message + ')'; }
  };
  const a = val(1);
  const b = val(2);
  if (opts && opts.both) return b === 'not run' ? a : `${a} / ${b}`;
  return b === a || b === 'not run' ? a : `${a} **[run 2: ${b}]**`;
}

const out = [];
const flaky = [];
const realDiffs = {};
// Cells that measure a Playwright artifact rather than the browser: Firefox's page.reload() over http adds an entry (Q11c).
const ARTIFACTS = [
  { qid: 'Q7', engine: 'firefox', proto: 'http', label: (l) => l.startsWith('[reload') && !l.includes(': saved ('),
    text: "not a reload: Firefox's page.reload() over http adds an entry instead (Q11c); see Q7b" },
  { qid: 'Q11', engine: 'firefox', proto: 'http', label: () => true,
    text: "not a reload: Firefox's page.reload() over http adds an entry instead (Q11c); see Q11b" },
];
function table(qid, rows, cols = COLS) {
  out.push(`| Measure | ${cols.map(([e, p]) => `${NAMES[e]} ${p}`).join(' | ')} |`);
  out.push(`|---|${cols.map(() => '---').join('|')}|`);
  let realSeen = false;
  for (const [label, kase, fmt, opts] of rows) {
    const cells = cols.map(([e, p]) => {
      const art = ARTIFACTS.find((a) => a.qid === qid && a.engine === e && a.proto === p && a.label(label));
      return art ? art.text : cell(e, p, kase, fmt, opts);
    });
    cells.forEach((c, i) => { if (c.includes('**[run 2:')) flaky.push(`${qid} / ${label} / ${NAMES[cols[i][0]]} ${cols[i][1]}`); });
    out.push(`| ${esc(label)} | ${cells.map(esc).join(' | ')} |`);
    if (cols === COLS && DATA['chromium-real']) {
      for (const p of ['file', 'http']) {
        if (opts && opts.both) continue;
        const base = cell('chromium', p, kase, fmt);
        const real = cell('chromium-real', p, kase, fmt);
        if (real !== 'not run') realSeen = true;
        if (real !== base && real !== 'not run') (realDiffs[qid] = realDiffs[qid] || []).push(`${label} (${p}): ${real}`);
      }
    }
  }
  out.push('');
  if (cols === COLS && realSeen) {
    if (realDiffs[qid]) {
      out.push(`With bfcache and the rate limiter on, Chromium differs here: ${realDiffs[qid].map(esc).join('; ')}.`);
    } else {
      out.push('With bfcache and the rate limiter on (chromium-real), Chromium gives the same values in every row.');
    }
    out.push('');
  }
}
function section(qid, title, rows, cols) {
  out.push(`## ${qid}. ${title}`, '');
  if (NOTES[qid + ':before']) out.push(NOTES[qid + ':before'], '');
  table(qid, rows, cols);
  if (NOTES[qid]) out.push(NOTES[qid], '');
}

// ---------- header ----------
const v = (e) => (DATA[e] ? DATA[e].browserVersion : 'not run');
out.push('# History API and Navigation API probe: results', '');
out.push(`Measured ${(DATA.chromium || DATA.firefox || DATA.webkit).startedAt.slice(0, 10)} on Windows 11 (10.0.26200), headless, viewport 1280x720,`
  + ` Node ${(DATA.chromium || {}).node}, playwright-core ${(DATA.chromium || {}).playwright} (the copy the .NET driver ships, whose browsers.json pins`
  + ' the three installed revisions). Only Playwright\'s bundled browsers were launched.', '');
out.push('| Configuration | browser.version() | Binary | Launch options |', '|---|---|---|---|');
const LAUNCH = {
  chromium: 'headless, Playwright defaults', 'chromium-real': "headless, ignoreDefaultArgs ['--disable-back-forward-cache', '--disable-ipc-flooding-protection']",
  firefox: 'headless, Playwright defaults', webkit: 'headless, Playwright defaults',
};
for (const e of ['chromium', 'chromium-real', 'firefox', 'webkit']) out.push(`| ${NAMES[e]} | ${v(e)} | ${BINARY[e]} | ${LAUNCH[e]} |`);
const fs1 = getX('firefox-strict', 'file-origin');
if (fs1) out.push(`| ${NAMES['firefox-strict']} (Q2b, Q10b, Q11c only) | ${fs1.browserVersion} | ${BINARY.firefox} | headless, firefoxUserPrefs {'security.fileuri.strict_origin_policy': true} |`);
const cft = getX('cft', 'bfcache');
if (cft) out.push(`| Chrome for Testing (Q12c only) | ${cft.browserVersion} | ms-playwright\\chromium-1217\\chrome-win64\\chrome.exe | channel 'chromium' (new headless), launchPersistentContext on a seeded profile, ignoreDefaultArgs ['--disable-back-forward-cache'] |`);
out.push('');
if (NOTES.summary) out.push(NOTES.summary, '');
if (NOTES.header) out.push(NOTES.header, '');

const q = (fmt) => fmt;
// ---------- Q1 ----------
section('Q1', "pushState('#a'), pushState('#b'), then Back and Forward", [
  ['pushState({k:1}, "", "#a")', 'q1', (r) => `${tryRes(r.push1)}, history.length ${r.initial.len} > ${r.push1.len}`],
  ['pushState({k:2}, "", "#b")', 'q1', (r) => `${tryRes(r.push2)}, history.length ${r.push2.len}`],
  ['Events fired by the two pushes (popstate/hashchange)', 'q1', (r) => legacy(r.pushEvents)],
  ['page.goBack()', 'q1', (r) => navRes(r.back)],
  ['Back: popstate/hashchange, in order', 'q1', (r) => legacy(r.backEvents)],
  ['Back: every event, in order', 'q1', (r) => full(r.backEvents)],
  ['After Back: history.state, location.hash', 'q1', (r) => `${js(r.afterBack.state)}, ${hashOf(r.afterBack.hash)}`],
  ['Forward: popstate/hashchange, in order', 'q1', (r) => legacy(r.fwdEvents)],
  ['Forward: every event, in order', 'q1', (r) => full(r.fwdEvents)],
  ['After Forward: history.state, location.hash', 'q1', (r) => `${js(r.afterFwd.state)}, ${hashOf(r.afterFwd.hash)}`],
]);

// ---------- Q2 ----------
section('Q2', 'pushState that changes the query or the path', [
  ["'#a' (control)", 'q2', (r) => tryRes(r.fragment)],
  ["'?x=1#a' (query)", 'q2', (r) => (r.query.ok ? `allowed: ${shortUrl(r.query.href)}` : r.query.err)],
  ["'report.html?y=2' (same path, query)", 'q2', (r) => (r.samePathQuery.ok ? `allowed: ${shortUrl(r.samePathQuery.href)}` : r.samePathQuery.err)],
  ["'other.html' (path, same directory)", 'q2', (r) => (r.path.ok ? `allowed: ${shortUrl(r.path.href)}` : r.path.err)],
  ["'sub/other.html' (path, subdirectory)", 'q2', (r) => (r.subPath.ok ? `allowed: ${shortUrl(r.subPath.href)}` : r.subPath.err)],
]);

const allowedOr = (t) => (!t ? 'n/a' : t.ok ? `allowed: ${shortUrl(t.href)}` : t.err);
section('Q2b', 'Q2 on file:// again: stock Firefox origin pref, and the same push from a page-owned timer (extras.js file-origin)', [
  ["'?x=1#a' by page.evaluate", 'x:file-origin', (r) => allowedOr(r.query)],
  ["'?x=1#a' by a page timer", 'x:file-origin', (r) => allowedOr(r.queryFromTimer)],
  ["'other.html' by page.evaluate", 'x:file-origin', (r) => allowedOr(r.path)],
  ["'other.html' by a page timer", 'x:file-origin', (r) => allowedOr(r.pathFromTimer)],
  ["'sub/other.html' by page.evaluate", 'x:file-origin', (r) => allowedOr(r.subPath)],
  ["'sub/other.html' by a page timer", 'x:file-origin', (r) => allowedOr(r.subPathFromTimer)],
], FILE_COLS);

// ---------- Q3 ----------
section('Q3', "location.hash = 'c' (from an entry whose state is {k:'base'}; #c is at y=3000)", [
  ['New entry? (history.length before > after)', 'q3', (r) => `${r.before.len > 0 ? '' : ''}${r.before.len} > ${r.afterSet.len} (${r.afterSet.len > r.before.len ? 'yes' : 'no'})`],
  ['history.state right after the assignment', 'q3', (r) => js(r.set.state)],
  ['scrollY right after / after settle', 'q3', (r) => `${r.set.y} / ${r.afterSet.y}`],
  ['Events of the assignment, in order', 'q3', (r) => full(fromFirst(r.setEvents))],
  ['Back: popstate/hashchange, in order', 'q3', (r) => legacy(r.backEvents)],
  ['Back: every event, in order', 'q3', (r) => full(r.backEvents)],
  ['After Back: history.state, hash, scrollY', 'q3', (r) => `${js(r.afterBack.state)}, ${hashOf(r.afterBack.hash)}, ${r.afterBack.y}`],
  ['Forward: popstate/hashchange, in order', 'q3', (r) => legacy(r.fwdEvents)],
  ['After Forward: history.state, hash, scrollY', 'q3', (r) => `${js(r.afterFwd.state)}, ${hashOf(r.afterFwd.hash)}, ${r.afterFwd.y}`],
]);

// ---------- Q4 ----------
const q4row = (k) => (r) => {
  const x = r[k];
  const entry = x.after.len > x.before.len ? `new entry (${x.before.len} > ${x.after.len})` : `no new entry (${x.after.len})`;
  const scroll = x.after.y === x.before.y ? `no re-scroll (y ${x.before.y})` : `re-scrolled (y ${x.before.y} > ${x.after.y})`;
  return `${entry}; ${scroll}; events: ${full(x.events)}`;
};
section('Q4', 'Assigning the same hash again (at #c, scrolled away to y=500 first)', [
  ['location.hash = location.hash', 'q4', q4row('sameHash')],
  ["location.hash = '#c'", 'q4', q4row('sameHashLiteral')],
  ['location.href = location.href (for comparison)', 'q4', q4row('hrefSelf')],
]);

// ---------- Q5 ----------
const q5rows = [];
for (const mode of ['auto', 'manual']) {
  q5rows.push(
    [`[${mode}] click: new entry, scrollY 800 >`, 'q5', (r) => { const m = r[mode]; return `${m.afterClick.len > m.before.len ? 'new entry' : 'no new entry'}, y ${m.afterClick.y}, hash ${hashOf(m.afterClick.hash)}`; }],
    [`[${mode}] click: events`, 'q5', (r) => full(r[mode].clickEvents)],
    [`[${mode}] Back: events`, 'q5', (r) => full(r[mode].backEvents)],
    [`[${mode}] Back: scrollY (800 = restored), state`, 'q5', (r) => `${r[mode].afterBack.y}, state ${js(r[mode].afterBack.state)}, hash ${hashOf(r[mode].afterBack.hash)}`],
    [`[${mode}] scroll to 300, Forward: scrollY (6000 = restored)`, 'q5', (r) => `${r[mode].afterFwd.y}, hash ${hashOf(r[mode].afterFwd.hash)}; events: ${legacy(r[mode].fwdEvents)}`],
  );
}
section('Q5', 'Clicking a real <a href="#target"> (link at y=1000, #target at y=6000, page at y=800 before the click)', q5rows);

section('Q5b', 'Q5 disambiguated: the #target entry is left at y=6500 (its own scroll), #target sits at 6000; Back; scroll to 300; Forward', [
  ['[auto] y after Back (800 = restored)', 'x:q5b', (r) => String(r.auto.afterBack)],
  ['[auto] y after Forward', 'x:q5b', (r) => `${r.auto.afterFwd} (${r.auto.forwardMeans})`],
  ['[manual] y after Back (800 = restored)', 'x:q5b', (r) => String(r.manual.afterBack)],
  ['[manual] y after Forward', 'x:q5b', (r) => `${r.manual.afterFwd} (${r.manual.forwardMeans})`],
]);

// ---------- Q6 ----------
const q6rows = [];
for (const mode of ['auto', 'manual']) {
  q6rows.push(
    [`[${mode}] Back: scrollY at popstate, at hashchange, after`, 'q6', (r) => {
      const ev = r[mode].backEvents || [];
      const at = (t) => { const e = ev.find((x) => x.type === t); return e ? e.y : '-'; };
      return `${at('popstate')}, ${at('hashchange')}, ${r[mode].afterBack.y}`;
    }],
    [`[${mode}] scroll to 1500, Forward: scrollY after (0 = restored)`, 'q6', (r) => String(r[mode].afterFwd.y)],
  );
}
section('Q6', 'Scroll restoration for a pushState entry (scroll to 3000, pushState #p, scroll to 0, Back)', q6rows);

// ---------- Q7 ----------
const secAt = (o) => (o && o.top ? `${o.top.id}${o.top.offset ? '+' + o.top.offset + 'px' : ''}` : 'n/a');
const q7v = (k, label) => [
  [`${label}: saved (y, top section)`, 'q7', (r) => `${r[k].saved2.y}, ${secAt(r[k].saved2)}${r[k].saved2.y !== r[k].saved.y ? ` (drifted from ${r[k].saved.y})` : ''}`],
  [`${label}: browser's own restore (y, section)`, 'q7', (r) => `y ${r[k].browser.y} (at event: ${js(r[k].yAtEvent)}), ${secAt(r[k].browser)}, scrollRestoration ${r[k].browser.sr}`],
];
const restoreRow = (k, label) => [`${label}`, 'q7', (r) => {
  const x = r[k];
  const ok = (o) => (o && o.top && o.top.id === x.saved2.top.id && Math.abs(o.top.offset - x.saved2.top.offset) <= 2);
  return `now ${secAt(x.restoredNow)}; after 2 frames ${secAt(x.restored)}; +600 ms ${secAt(x.restoredLate)} => ${ok(x.restoredLate) ? 'SAME section' : 'WRONG section'}`;
}];
section('Q7', 'content-visibility:auto (300 sections, 60 px rendered, 500 px placeholder): save at s250, leave, come back, restore', [
  ...q7v('backTopAuto', '[Back, top, auto]'),
  ...q7v('backTopManualA', '[Back, top, manual]').slice(0, 1),
  restoreRow('backTopManualA', '[Back, top, manual] (a) scrollTo(savedY)'),
  restoreRow('backTopManualB', '[Back, top, manual] (b) scrollIntoView(section) + offset'),
  ...q7v('backDetourAuto', '[Back, via s120 then top, auto]'),
  restoreRow('backDetourManualA', '[Back, via s120 then top, manual] (a) scrollTo(savedY)'),
  restoreRow('backDetourManualB', '[Back, via s120 then top, manual] (b) scrollIntoView + offset'),
  ...q7v('reloadAuto', '[reload, auto]'),
  ['[reload, manual] browser scrollY after reload, scrollRestoration', 'q7', (r) => `${r.reloadManualA.browser.y}, ${r.reloadManualA.browser.sr}, state kept: ${yesno(r.reloadManualA.browser.state && r.reloadManualA.browser.state.sec)}`],
  restoreRow('reloadManualA', '[reload, manual] (a) scrollTo(history.state.savedY)'),
  restoreRow('reloadManualB', '[reload, manual] (b) scrollIntoView + offset from history.state'),
]);

const restoreRowX = (k, label) => [label, 'x:q7reload', (r) => {
  const x = r[k];
  const ok = (o) => (o && o.top && o.top.id === x.saved.top.id && Math.abs(o.top.offset - x.saved.top.offset) <= 2);
  return `now ${secAt(x.restoredNow)}; after 2 frames ${secAt(x.restored)}; +600 ms ${secAt(x.restoredLate)} => ${ok(x.restoredLate) ? 'SAME section' : 'WRONG section'}`;
}];
section('Q7b', "Q7's reload variants again, reloading by location.reload() (extras.js q7reload)", [
  ['[reload, auto] saved (y, top section)', 'x:q7reload', (r) => `${r.reloadAuto.saved.y}, ${secAt(r.reloadAuto.saved)}`],
  ["[reload, auto] browser's own restore", 'x:q7reload', (r) => `y ${r.reloadAuto.browser.y} (at pageshow: ${js(r.reloadAuto.yAtPageshow)}), ${secAt(r.reloadAuto.browser)}, navigation type ${r.reloadAuto.browser.navType} => ${r.reloadAuto.browser.top.id === r.reloadAuto.saved.top.id ? 'SAME section' : 'WRONG section'}`],
  ['[reload, manual] saved; browser y after reload; state kept', 'x:q7reload', (r) => `${r.reloadManualA.saved.y}, ${secAt(r.reloadManualA.saved)}; ${r.reloadManualA.browser.y}, scrollRestoration ${r.reloadManualA.browser.sr}; ${yesno(r.reloadManualA.browser.state && r.reloadManualA.browser.state.sec)}`],
  restoreRowX('reloadManualA', '[reload, manual] (a) scrollTo(history.state.savedY)'),
  restoreRowX('reloadManualB', '[reload, manual] (b) scrollIntoView + offset from history.state'),
]);

// ---------- Q8 ----------
const q8row = (k) => (r) => {
  const x = r[k];
  const err = x.firstErrorAt === null ? 'no exception' : `throws from call ${x.firstErrorAt + 1} (${x.errors} of 500 threw): ${x.firstError}`;
  const ign = x.ignored ? `; ${x.ignored} calls silently ignored from call ${x.firstIgnoredAt + 1}` : '';
  const con = x.consoleCount ? `; console: ${x.console.map((m) => m.type + ': ' + m.text).join(' / ')}` : '; no console message';
  return `${err}${ign}; final hash ${x.hash}, history.length ${x.len}${con}; one more call 300 ms later: ${tryRes(x.oneMoreAfter300ms)}`;
};
section('Q8', '500 calls in a tight loop, each kind on a fresh page', [
  ["replaceState(null, '', '#n'+i) x500", 'q8', q8row('replaceState')],
  ["pushState(null, '', '#n'+i) x500", 'q8', q8row('pushState')],
  ['Time for the 500 replaceState calls, ms (run 1 / run 2)', 'q8', (r) => String(r.replaceState.ms), { both: true }],
  ['Time for the 500 pushState calls, ms (run 1 / run 2)', 'q8', (r) => String(r.pushState.ms), { both: true }],
], [...COLS, ...REAL]);

const RL_COLS = [['chromium-real', 'file'], ['chromium-real', 'http'], ['firefox', 'file'], ['firefox', 'http'], ['webkit', 'file'], ['webkit', 'http']];
const rlPage = (k) => (r) => {
  const x = r[k];
  const err = x.firstErrorAt === null ? 'no exception' : `throws from call ${x.firstErrorAt + 1} (${x.errors} of 500): ${x.firstError}`;
  const ign = x.ignored ? `; calls ${x.firstIgnoredAt + 1} to 500 silently ignored (${x.ignored})` : '';
  return `${err}${ign}; hash ${x.hashNow}; history.length ${x.lenNow}, 1 s later ${x.lenLater}${x.console.length ? '; console: ' + x.console.join(' / ') : ''}`;
};
section('Q8b', 'Q8 again from page-owned timer callbacks (extras.js ratelimit-page, ratelimit), and how long the limit lasts', [
  ['replaceState x500 from a page timer', 'x:ratelimit-page', rlPage('replaceState')],
  ['pushState x500 from a page timer', 'x:ratelimit-page', rlPage('pushState')],
  ['After a 500-call replaceState burst, the first once-a-second retry that takes effect', 'x:ratelimit', (r) => (r.reopenedAfterMs === null ? 'none within 12 s' : `${r.reopenedAfterMs} ms after the burst`), { both: true }],
], RL_COLS);
const HL_COLS = [['chromium', 'file'], ['chromium', 'http'], ['firefox', 'file'], ['firefox', 'http'], ['webkit', 'file'], ['webkit', 'http']];
section('Q8c', 'history.length after 500 pushState calls from a page timer, over time (extras.js histlen; Chromium without its rate limiter)', [
  ['history.length at load', 'x:histlen', (r) => String(r.atLoad)],
  ['right after / 1 s / 3 s / 6 s', 'x:histlen', (r) => r.samples.map((s) => s.len).join(' / ')],
  ['calls that threw', 'x:histlen', (r) => String(r.errors)],
], HL_COLS);

// ---------- Q9 ----------
const cur = (c) => (!c ? 'n/a' : `hash ${hashOf(c.hash)}, getState() ${c.getState === 'undefined' ? 'undefined' : js(c.getState)}, history.state ${js(c.historyState)}, entries ${c.entries}`);
section('Q9', 'Navigation API', [
  ["'navigation' in window; self.origin", 'q9', (r) => `${r.has.inWindow ? 'yes' : 'no'}; origin ${r.has.origin}`],
  ['At load: currentEntry, entries().length', 'q9', (r) => (r.initial ? `${r.initial.currentEntry ? 'currentEntry index ' + r.initial.currentEntry.index : 'currentEntry null'}, entries ${r.initial.entries}` : 'n/a')],
  ["navigation.navigate('#x'): committed / finished / hash", 'q9', (r) => (r.navX ? (r.navX.threw ? 'threw ' + r.navX.threw : `${r.navX.committed} / ${r.navX.finished} / ${hashOf(r.navX.hash)}`) : 'n/a')],
  ["navigate('#x'): events", 'q9', (r) => (r.navXEvents ? full(r.navXEvents) : 'n/a')],
  ["navigate('#y', {state:{a:1}}): getState() after", 'q9', (r) => (r.afterY ? cur(r.afterY) : 'n/a')],
  ['Back (page.goBack): events', 'q9', (r) => (r.backEvents ? full(r.backEvents) : 'n/a')],
  ['After Back: getState()', 'q9', (r) => (r.afterBack ? cur(r.afterBack) : 'n/a')],
  ['After Forward: getState()', 'q9', (r) => (r.afterFwd ? cur(r.afterFwd) : 'n/a')],
  ["intercept() on location.hash = 'z': events", 'q9', (r) => (r.interceptHashEvents ? `${full(r.interceptHashEvents)}; hash after ${hashOf(r.afterInterceptHash.hash)}` : 'n/a')],
  ['intercept() on a Back (traverse): events', 'q9', (r) => (r.interceptBackEvents ? `${full(r.interceptBackEvents)}; hash after ${hashOf(r.afterInterceptBack.hash)}` : 'n/a')],
]);

// ---------- Q10 ----------
const q10rows = [];
for (const [k, label] of [['plain', '<iframe>'], ['sandboxScripts', 'sandbox="allow-scripts"'], ['sandboxScriptsSameOrigin', 'sandbox="allow-scripts allow-same-origin"']]) {
  q10rows.push(
    [`${label}: origins (top / child); child Navigation API`, 'q10', (r) => `${r[k].origins.top} / ${r[k].origins.child}; ${r[k].childNavApi.has ? (r[k].childNavApi.currentEntry ? 'currentEntry set' : `currentEntry null, entries ${r[k].childNavApi.entries}`) : 'absent'}`],
    [`${label}: child pushState #f1, #f2`, 'q10', (r) => `${tryRes(r[k].push1)}; ${tryRes(r[k].push2)}`],
    [`${label}: top history.length before > after the pushes`, 'q10', (r) => `${r[k].len0.top} > ${r[k].len2.top} (child reads ${r[k].len2.child})`],
    [`${label}: page.goBack() at top`, 'q10', (r) => `${navRes(r[k].goBack)}; child now ${hashOf(r[k].afterGoBack.child.hash)}, top ${shortUrl(r[k].afterGoBack.topUrl)}; child events: ${legacy(r[k].afterGoBack.childEvents)}`],
    [`${label}: then top history.back()`, 'q10', (r) => `child now ${hashOf(r[k].afterHistBack.child.hash)}, top ${shortUrl(r[k].afterHistBack.topUrl)}; top events: ${legacy(r[k].afterHistBack.topEvents)}`],
    [`${label}: then page.goForward()`, 'q10', (r) => `${navRes(r[k].goForward)}; child now ${hashOf(r[k].afterGoForward.child.hash)}`],
  );
}
section('Q10', 'The report in an iframe (parent.html embeds report.html)', q10rows);

const readOr = (t) => (!t ? 'n/a' : t.ok ? 'readable' : t.err.replace(/^TypeError: .*/, 'contentDocument is null'));
section('Q10b', 'file:// iframes with the stock Firefox origin pref (extras.js file-origin)', [
  ['<iframe>: parent reads iframe.contentDocument', 'x:file-origin', (r) => readOr(r.iframeParentReadsChild)],
  ['<iframe>: child origin; child pushState #f1', 'x:file-origin', (r) => `${r.iframeChildOrigin}; ${tryRes(r.iframeChildPush)}`],
  ['sandbox="allow-scripts": parent reads iframe.contentDocument', 'x:file-origin', (r) => readOr(r.sandboxIframeParentReadsChild)],
  ['sandbox="allow-scripts": child origin; child pushState #f1', 'x:file-origin', (r) => `${r.sandboxIframeChildOrigin}; ${tryRes(r.sandboxIframeChildPush)}`],
], FILE_COLS);

// ---------- Q11 ----------
const q11rows = [];
for (const mode of ['auto', 'manual']) {
  for (const [fk, flabel] of [['noTarget', "#r (no element)"], ['targetC', '#c (element at y=3000)']]) {
    const k = `${mode}_${fk}`;
    q11rows.push([`[${mode}] pushState ${flabel}, scroll 2500, reload`, 'q11', (r) => {
      const m = r[k];
      return `state ${js(m.after.state)}, hash ${hashOf(m.after.hash)}, scrollRestoration ${m.after.sr}; y at pageshow ${js(m.yAtPageshow)}, after load ${m.after.y}, +700 ms ${m.afterLate.y}`;
    }]);
  }
}
section('Q11', 'Reload after pushState (pushState at y=2000, then scroll to 2500, reload)', q11rows);

const q11bRows = [];
for (const mode of ['auto', 'manual']) {
  for (const [fk, flabel] of [['noTarget', '#r (no element)'], ['targetC', '#c (element at y=3000)']]) {
    const k = `${mode}_${fk}`;
    q11bRows.push([`[${mode}] pushState ${flabel}, scroll 2500, location.reload()`, 'x:q11b', (r) => {
      const m = r[k];
      return `state ${js(m.after.state)}, hash ${hashOf(m.after.hash)}, scrollRestoration ${m.after.sr}, type ${m.after.navType}; y at pageshow ${js(m.yAtPageshow)}, after load ${m.after.y}, +700 ms ${m.yLate}`;
    }]);
  }
}
section('Q11b', 'Q11 again, reloading by location.reload() (extras.js q11b)', q11bRows);
const rs = (k) => (r) => `state ${js(r[k].after.state)}, history.length ${r[k].before.len} > ${r[k].after.len}, scrollRestoration ${r[k].after.sr}, type ${r[k].after.navType}`;
section('Q11c', 'Which reload keeps history.state (extras.js reload-state; scrollRestoration set to manual first)', [
  ['replaceState({k}), then page.reload()', 'x:reload-state', rs('replaceThenPageReload')],
  ['pushState({k}, "#r"), then page.reload()', 'x:reload-state', rs('pushThenPageReload')],
  ['pushState({k}, "#r"), then location.reload()', 'x:reload-state', rs('pushThenLocationReload')],
], [...COLS, ['firefox-strict', 'file'], ['firefox-strict', 'http']]);

// ---------- Q12 ----------
section('Q12', 'bfcache: pushState #bf, follow a link to other.html, Back', [
  ['pageshow after Back', 'q12', (r) => (r.log || []).filter((e) => e.type === 'pageshow' || e.type === 'pagehide').map(evName).join(' then ') || 'none'],
  ['JS state intact (counter 42 survives)', 'q12', (r) => `${r.jsState && r.jsState.counter === 42 ? 'yes' : 'no'} (counter ${r.jsState && r.jsState.counter})`],
  ['Navigation timing type; notRestoredReasons', 'q12', (r) => `${r.navEntry && r.navEntry.type}; ${js(r.navEntry && r.navEntry.notRestoredReasons)}`],
  ['After Back: hash, state, history.length', 'q12', (r) => `${hashOf(r.after.hash)}, ${js(r.after.state)}, ${r.after.len}`],
], [...COLS, ...REAL]);

section('Q12b', 'Why Chromium did not restore: CDP Page.backForwardCacheNotUsed, headless shell vs Chrome for Testing', [
  ['Headless shell, bfcache switch removed: notRestoredExplanations', 'x:bfcache-cdp', (r) => r.notUsed.map((n) => n.explanations.join(', ')).join('; ') || 'none (restored)'],
  ['Headless shell: pageshow persisted, counter', 'x:bfcache-cdp', (r) => `${js(r.pageshow)}, ${r.counter}`],
], [['chromium-real', 'file'], ['chromium-real', 'http']]);
if (cft) {
  section('Q12c', 'Q12 in Chrome for Testing 147 (full Chromium, new headless, bfcache switch removed; cft-bfcache.js)', [
    ['pagehide/pageshow around the Back', 'x:bfcache', (r) => r.pageshow.join(' then ')],
    ['JS state intact (counter 42 survives)', 'x:bfcache', (r) => `${r.counter === 42 ? 'yes' : 'no'} (counter ${r.counter})`],
    ['Navigation timing type; notRestoredReasons', 'x:bfcache', (r) => `${r.navType}; ${js(r.notRestoredReasons)}`],
    ['CDP notRestoredExplanations', 'x:bfcache', (r) => (r.cdpNotUsed.length ? r.cdpNotUsed.join(', ') : 'none (restored)')],
    ['After Back: hash, history.state', 'x:bfcache', (r) => `${hashOf(r.hash)}, ${js(r.state)}`],
  ], [['cft', 'file'], ['cft', 'http']]);
}

// ---------- Q13 ----------
section('Q13', 'history.length: load, three pushStates, two replaceStates', [
  ['At load', 'q13', (r) => String(r.atLoad)],
  ['After 3 pushState', 'q13', (r) => `${r.after3Push} (+${r.after3Push - r.atLoad})`],
  ['After 2 replaceState', 'q13', (r) => `${r.after2Replace} (+${r.after2Replace - r.atLoad} since load)`],
]);

// ---------- extra ----------
section('X1', 'Entries added from timers with no user activation (noact.html: pushState #na1, #na2 after load)', [
  ['page.goBack() lands on', 'noact', (r) => `${hashOf(r['noact.html'].after.hash)} (${navRes(r['noact.html'].goBack)})`],
  ["the page's own history.back() from a timer lands on", 'noact', (r) => hashOf(r['noact-selfback.html'].after.hash)],
]);

section('X1b', 'X1 again with a 1 s pause between the second push and page.goBack() (extras.js x1b)', [
  ['page.goBack() lands on', 'x:x1b', (r) => `${r.landedOn} (${r.goBack}), history.length ${r.len}`],
]);
section('X2', 'One real click on the first entry, then #na1 and #na2 pushed from timers 6 s later (noact-click.html; extras.js x2)', [
  ['page.goBack() lands on', 'x:x2', (r) => `${r.landedOn} (${r.goBack}); events: ${r.events.join(', ')}`],
]);

out.push('## Flaky cells', '');
if (NOTES.flaky) out.push(NOTES.flaky, '');
out.push(flaky.length ? flaky.map((f) => `- ${f}`).join('\n') : 'None: every cell gave the same value in run 1 and run 2.', '');
if (NOTES.footer) out.push(NOTES.footer, '');

fs.writeFileSync(path.join(__dirname, 'RESULTS.md'), out.join('\n'));
console.log('wrote RESULTS.md', out.length, 'lines;', flaky.length, 'flaky cells');
