// Second-pass width sweep: the conditions the first sweep held fixed. Usage:
//   node sweep2.js [step] [dir] [pages]
// Env:
//   ENGINE=chromium|firefox|webkit   the browser (default chromium; the suite runs chromium only)
//   SCROLLBARS=1                     chromium: drop Playwright's --hide-scrollbars, so a tall page gets the
//                                    classic scrollbar a desktop Chrome on Windows or Linux draws
//   MODE=reload|shrink|grow          reload at every width (the page a user who opens it at that width
//                                    gets); shrink = load at the widest width and narrow it step by step
//                                    without a reload (a window snapped to half the screen); grow = load at
//                                    320 and widen without a reload (a phone or small tablet rotated)
//   INJECT_CSS='...'                 a style tag added after load (font or text-spacing stress)
//   TAG=x                            names the results file sweep2-results-<TAG>.json
// Every mode opens every <details>, shows .filters and every phone-hidden scenario toolbar (as a tap on
// "Diagram Settings" would). Overflow is scrollWidth past clientWidth, so a classic scrollbar is not
// counted as overflow. The engine fetch is blocked: diagrams never render, and are not a width source.
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const fs = require('fs');

const STEP = parseInt(process.argv[2] || '20', 10);
const DIR = process.argv[3] || 'out-head';
const OUT = path.isAbsolute(DIR) ? DIR : path.join(__dirname, DIR);
const PAGES = process.argv[4] ? process.argv[4].split(',') : fs.readdirSync(OUT).filter(f => f.endsWith('.html')).sort();
const ENGINE = process.env.ENGINE || 'chromium';
const MODE = process.env.MODE || 'reload';
const MIN = parseInt(process.env.MIN || '320', 10), MAX = parseInt(process.env.MAX || '1400', 10);

const widths = [];
for (let w = MIN; w <= MAX; w += STEP) widths.push(w);
if (MODE === 'shrink') widths.reverse();

const MEASURE = `() => {
  const lines = el => { const r = document.createRange(); r.selectNodeContents(el); return r.getClientRects().length; };
  const de = document.documentElement;
  const client = de.clientWidth;
  const rightMost = () => {
    let best = null, bestR = -1;
    document.querySelectorAll('body *').forEach(el => {
      if (el.closest('svg')) return;
      const r = el.getBoundingClientRect(); if (r.width === 0 || r.right <= client + 1) return;
      const cs = getComputedStyle(el); if (cs.display === 'none' || cs.position === 'fixed') return;
      if (r.right > bestR) { bestR = r.right; best = el; }
    });
    return best ? (best.tagName.toLowerCase() + (best.id ? '#' + best.id : '') + (best.className && typeof best.className === 'string' ? '.' + best.className.trim().split(/\\s+/).join('.') : '')) + '@' + Math.round(bestR) : null;
  };
  const vis = el => el && el.getBoundingClientRect().width > 0;
  const box = document.querySelector('.filtering-box');
  const cluster = document.querySelector('.filtering-box-export');
  const hr = document.querySelector('.header-row');
  const search = document.querySelector('#searchbar');
  const ci = document.querySelector('.ci-metadata');
  const toggles = [...document.querySelectorAll('.diagram-toggle')].filter(vis);
  const wrappedIn = sel => [...document.querySelectorAll(sel)].filter(vis).filter(b => lines(b) > 1).length;
  const boxR = vis(box) ? box.getBoundingClientRect() : null;
  const clR = vis(cluster) ? cluster.getBoundingClientRect() : null;
  // The widest visible scenario toolbar's row count: distinct button tops.
  let toolbarRowsMax = 0, toolbarButtonsMax = 0;
  for (const t of toggles) {
    const tops = new Set([...t.querySelectorAll('button, select')].filter(vis).map(b => Math.round(b.getBoundingClientRect().top / 4)));
    toolbarRowsMax = Math.max(toolbarRowsMax, tops.size);
    toolbarButtonsMax = Math.max(toolbarButtonsMax, t.querySelectorAll('button').length);
  }
  // .feature and .scenario carry content-visibility: auto, whose paint containment CLIPS whatever
  // overflows them: a toolbar control pushed past the scenario's edge is invisible and unreachable,
  // and the page's scrollWidth never shows it. So: controls past their own toolbar's right edge, and
  // containers whose content is wider than they are.
  let clippedControls = 0, clippedPx = 0;
  for (const t of toggles) {
    const tr = t.getBoundingClientRect();
    for (const k of t.querySelectorAll('button, select')) {
      if (!vis(k)) continue;
      const over = k.getBoundingClientRect().right - tr.right;
      if (over > 1) { clippedControls++; clippedPx = Math.max(clippedPx, Math.round(over)); }
    }
  }
  let containOverflow = 0, containOverflowPx = 0, containSample = null;
  document.querySelectorAll('.feature, .scenario').forEach(c => {
    if (!vis(c)) return;
    const over = c.scrollWidth - c.clientWidth;
    if (over > 1) {
      containOverflow++;
      if (over > containOverflowPx) {
        containOverflowPx = over;
        const cr = c.getBoundingClientRect();
        let best = null, bestR = cr.right;
        c.querySelectorAll('*').forEach(el => { if (el.closest('svg')) return; const r = el.getBoundingClientRect(); if (r.width > 0 && r.right > bestR) { bestR = r.right; best = el; } });
        containSample = best ? best.tagName.toLowerCase() + (best.className && typeof best.className === 'string' ? '.' + best.className.trim().split(/\\s+/).join('.') : '') : null;
      }
    }
  });
  return {
    clippedControls, clippedPx, containOverflow, containOverflowPx, containSample,
    inner: window.innerWidth, client, scroll: de.scrollWidth,
    overflow: de.scrollWidth - client,
    culprit: de.scrollWidth > client + 1 ? rightMost() : null,
    exportWrapped: wrappedIn('.export-btn'),
    topBarWrapped: wrappedIn('.toolbar-row button'),
    scenarioWrapped: wrappedIn('.diagram-toggle button'),
    toolbarsVisible: toggles.length, toolbarRowsMax, toolbarButtonsMax,
    clusterEscapes: boxR && clR ? Math.round(clR.right - boxR.right) : null,
    boxWidth: boxR ? Math.round(boxR.width) : null,
    searchWidth: vis(search) ? Math.round(search.getBoundingClientRect().width) : null,
    ciWidth: vis(ci) ? Math.round(ci.getBoundingClientRect().width) : null,
    headerDir: hr ? getComputedStyle(hr).flexDirection + '/' + getComputedStyle(hr).flexWrap : null,
    boxBelowSummary: (() => { const s = document.querySelector('.header-row > .test-execution-summary'); return s && boxR ? boxR.top >= s.getBoundingClientRect().bottom - 1 : null; })(),
    mobileJs: !!document.querySelector('.scenario-diagram-controls-toggle'),
  };
}`;

const OPEN_ALL = `() => {
  document.querySelectorAll('details.feature, details.scenario, details.example-diagrams').forEach(d => d.open = true);
  const f = document.querySelector('.filters'); if (f && getComputedStyle(f).display === 'none') f.style.display = 'flex';
  document.querySelectorAll('.scenario-diagram-controls').forEach(c => { c.style.display = 'flex'; });
}`;

async function settle(page) {
  // two animation frames for the layout script's rAF-scheduled pass, then a short pause
  await page.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))));
  await page.waitForTimeout(40);
}

(async () => {
  const launch = { headless: true };
  if (ENGINE === 'chromium' && process.env.SCROLLBARS) launch.ignoreDefaultArgs = ['--hide-scrollbars'];
  const browser = await pw[ENGINE].launch(launch);
  const results = { engine: ENGINE, version: browser.version(), mode: MODE, scrollbars: !!process.env.SCROLLBARS, step: STEP, dir: DIR, pages: {} };
  for (const name of PAGES) {
    const file = path.join(OUT, name);
    if (!fs.existsSync(file)) { console.log('missing', file); continue; }
    const ctx = await browser.newContext({ viewport: { width: widths[0], height: 900 } });
    const page = await ctx.newPage();
    await page.route('**/*', r => r.request().url().startsWith('file:') ? r.continue() : r.abort());
    const url = 'file:///' + file.split(path.sep).join('/');
    const load = async () => {
      await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 120000 });
      await page.waitForSelector('details.feature', { state: 'attached', timeout: 120000 });
      await page.evaluate(eval('(' + OPEN_ALL + ')'));
      if (process.env.INJECT_CSS) await page.addStyleTag({ content: process.env.INJECT_CSS });
      await settle(page);
    };
    const rows = [];
    const t0 = Date.now();
    if (MODE !== 'reload') await load();
    for (const w of widths) {
      await page.setViewportSize({ width: w, height: 900 });
      if (MODE === 'reload') await load(); else await settle(page);
      const m = await page.evaluate(eval('(' + MEASURE + ')'));
      rows.push({ w, ...m });
    }
    const secs = ((Date.now() - t0) / 1000).toFixed(1);
    results.pages[name] = rows;
    await ctx.close();

    const asc = [...rows].sort((a, b) => a.w - b.w);
    const bandsOf = pred => {
      const out = [];
      for (const r of asc.filter(pred)) { const b = out[out.length - 1]; if (b && r.w === b.to + STEP) { b.to = r.w; b.max = Math.max(b.max, r.overflow); } else out.push({ from: r.w, to: r.w, max: r.overflow, culprit: r.culprit }); }
      return out;
    };
    const fmt = bs => bs.length ? bs.map(b => b.from === b.to ? `${b.from}` : `${b.from}-${b.to}`).join(', ') : 'never';
    const ob = bandsOf(r => r.overflow > 1);
    const sw = asc.filter(r => r.searchWidth !== null);
    const minSearch = sw.length ? sw.reduce((m, r) => r.searchWidth < m.searchWidth ? r : m) : null;
    console.log(`\n== ${name}  [${ENGINE} ${browser.version()}, ${MODE}${process.env.SCROLLBARS ? ', scrollbars' : ''}${process.env.INJECT_CSS ? ', css' : ''}, ${secs}s]`);
    console.log('  scrollbar width (inner - client) seen:', [...new Set(asc.map(r => r.inner - r.client))].join('/'));
    console.log('  sideways scroll:', ob.length ? ob.map(b => `${b.from}-${b.to} (max +${b.max}, ${b.culprit})`).join('; ') : 'none');
    console.log('  export label wrap:', fmt(bandsOf(r => r.exportWrapped > 0)), '| top-bar wrap:', fmt(bandsOf(r => r.topBarWrapped > 0)), '| scenario-toolbar wrap:', fmt(bandsOf(r => r.scenarioWrapped > 0)));
    console.log('  cluster outside box:', fmt(bandsOf(r => r.clusterEscapes > 1)));
    const cc = asc.filter(r => r.clippedControls > 0), co = asc.filter(r => r.containOverflow > 0);
    console.log('  toolbar controls past the toolbar edge (clipped):', fmt(bandsOf(r => r.clippedControls > 0)), cc.length ? `(max +${Math.max(...cc.map(r => r.clippedPx))} px)` : '',
      '| content clipped inside a feature or scenario:', fmt(bandsOf(r => r.containOverflow > 0)), co.length ? `(max +${Math.max(...co.map(r => r.containOverflowPx))} px, e.g. ${co.reduce((m, r) => r.containOverflowPx > m.containOverflowPx ? r : m).containSample})` : '');
    console.log('  narrowest search input:', minSearch ? `${minSearch.searchWidth} px at ${minSearch.w}` : 'n/a', '| box at 770/900/1000/1100/1200:', [770, 900, 1000, 1100, 1200].map(w => asc.find(r => r.w === w)?.boxWidth ?? '-').join('/'),
      '| CI box at 1400:', asc.find(r => r.w === 1400)?.ciWidth ?? '-');
    console.log('  visible toolbars at 320/1400:', asc[0].toolbarsVisible + '/' + asc[asc.length - 1].toolbarsVisible, '| most buttons in one toolbar:', Math.max(...asc.map(r => r.toolbarButtonsMax)), '| most toolbar rows:', Math.max(...asc.map(r => r.toolbarRowsMax)), '| mobile JS state:', asc[0].mobileJs + '/' + asc[asc.length - 1].mobileJs);
  }
  const tag = process.env.TAG || `${DIR}-${ENGINE}-${MODE}${process.env.SCROLLBARS ? '-sb' : ''}`;
  fs.writeFileSync(path.join(__dirname, 'sweep2-results-' + tag.replace(/[^\w.-]+/g, '_') + '.json'), JSON.stringify(results, null, 1));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
