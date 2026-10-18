// The Timeline and Components tabs follow the filters: with Happy paths set to Only, each shows the filtered note and
// fewer rows or components; Full brings the whole run back. The component diagram's Direction and Call summaries
// redraw it. Screenshots of each state.
// usage: node tabs-check.js <page.local.html> <out dir> [width=1400]
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const path = require('path'), fs = require('fs');
(async () => {
  const [page, outDir, w] = process.argv.slice(2);
  const width = Number(w || 1400);
  fs.mkdirSync(outDir, { recursive: true });
  const b = await chromium.launch({ channel: 'chrome' });
  const p = await b.newPage({ viewport: { width, height: 1000 } });
  const errors = [];
  p.on('pageerror', e => errors.push(e.message));
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/'));
  await p.waitForTimeout(1500);
  await p.evaluate(() => { const m = document.querySelector('.more-filters'); if (m && m.getAttribute('aria-expanded') !== 'true') m.click(); }); await new Promise(r => setTimeout(r, 300)); // round 9: the filter rows start folded
  // on a phone the filter rows fold under the report's Filters toggle
  if (await p.isVisible('.mobile-filter-toggle')) { await p.click('.mobile-filter-toggle'); await p.waitForTimeout(300); }
  const out = {};
  await p.click('.happy-seg[data-hp="in"]'); await p.waitForTimeout(400);
  out.readout = await p.evaluate(() => document.querySelector('.filter-readout').textContent);
  await p.click('#tab-timeline'); await p.waitForTimeout(400);
  const tl = () => p.evaluate(() => {
    const rows = Array.from(document.querySelectorAll('#scenario-timeline > .timeline-row'));
    const note = document.querySelector('.scenario-timeline .filtered-note');
    return { shown: rows.filter(r => r.offsetParent !== null).length, total: rows.length, note: note && !note.hidden ? note.textContent : null };
  });
  out.timelineFiltered = await tl();
  await p.screenshot({ path: path.join(outDir, `timeline-filtered-${width}.png`) });
  await p.click('.scenario-timeline .tl-seg-btn[data-view="full"]'); await p.waitForTimeout(300);
  out.timelineFull = await tl();
  await p.click('#tab-components');
  const svgState = () => p.evaluate(() => {
    const host = document.querySelector('.component-diagram-section');
    const svg = host && host.querySelector('svg');
    const note = host && host.querySelector('.filtered-note');
    if (!svg) return { svg: false, note: note && !note.hidden ? note.textContent : null };
    const vb = (svg.getAttribute('viewBox') || '').split(/\s+/).map(Number);
    const text = svg.textContent;
    return { svg: true, w: vb[2], h: vb[3], textLen: text.length, callsShown: /calls across|call across/.test(text), title: /Component Diagram/.test(text), note: note && !note.hidden ? note.textContent : null };
  });
  const waitNew = async prev => {
    for (let i = 0; i < 100; i++) { await p.waitForTimeout(200); const s = await svgState(); if (s.svg && JSON.stringify(s) !== JSON.stringify(prev)) return s; }
    return { timeout: true, ...(await svgState()) };
  };
  out.componentsFiltered = await waitNew(null);
  await p.screenshot({ path: path.join(outDir, `components-filtered-${width}.png`) });
  await p.click('.component-diagram-section .tl-seg-btn[data-dir="tb"]');
  out.componentsTopToBottom = await waitNew(out.componentsFiltered);
  await p.screenshot({ path: path.join(outDir, `components-tb-${width}.png`) });
  await p.click('.component-diagram-section .tl-seg-btn[data-calls="hidden"]');
  out.componentsNoCalls = await waitNew(out.componentsTopToBottom);
  await p.click('.component-diagram-section .tl-seg-btn[data-view="full"]');
  out.componentsFull = await waitNew(out.componentsNoCalls);
  await p.click('.component-diagram-section .tl-seg-btn[data-dir="lr"]');
  await p.click('.component-diagram-section .tl-seg-btn[data-calls="shown"]');
  out.componentsFullLrCalls = await waitNew(out.componentsFull);
  await p.screenshot({ path: path.join(outDir, `components-full-${width}.png`) });
  // a filter change while the Components tab is open redraws it; Clear All brings the whole run back
  await p.click('.component-diagram-section .tl-seg-btn[data-view="filtered"]');
  out.componentsFilteredAgain = await waitNew(out.componentsFullLrCalls);
  await p.click('#tab-features'); await p.waitForTimeout(200);
  await p.click('.export-btn'); await p.waitForTimeout(400);
  await p.click('#tab-components'); out.componentsAfterClearAll = await waitNew(out.componentsFilteredAgain);
  // a narrow filter (the search box) leaves out components no matching scenario uses
  await p.click('#tab-features'); await p.waitForTimeout(200);
  await p.fill('#searchbar', 'muffin'); await p.dispatchEvent('#searchbar', 'keyup'); await p.waitForTimeout(2000); // the deep pass answers after the quick one
  out.narrowReadout = await p.evaluate(() => document.querySelector('.filter-readout').textContent);
  await p.click('#tab-components'); out.componentsNarrow = await waitNew(out.componentsAfterClearAll);
  await p.screenshot({ path: path.join(outDir, `components-narrow-${width}.png`) });
  out.errors = errors;
  console.log(JSON.stringify(out, null, 1));
  await b.close();
})();
