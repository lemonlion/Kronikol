// The diagram look in the real page flow: opens two scenarios (first render), switches the note detail to Expanded
// (a re-render through the note controls), opens an internal-flow popup, and the Components tab; on each diagram,
// the fonts the SVG asks for, the face the page uses, text painted past its box, box-to-lifeline joins, and the
// paints that carry the look's markers. Fails loudly on render errors.
// usage: node diagram-check.js <page.local.html>
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require('../../../tools/playwright/installed-browser.js');
const path = require('path');
const MEASURE = () => {
  const res = [];
  document.querySelectorAll('.plantuml-browser svg, #component-diagram svg, .iflow-diagram svg').forEach(svg => {
    const box = svg.getBoundingClientRect(); if (!box.width) return;
    const kind = svg.closest('#component-diagram') ? 'component' : svg.closest('.iflow-diagram, .iflow-popup') ? 'iflow' : 'sequence';
    const own = e => !e.closest('.note-toggle-icon') && !e.classList.contains('note-hover-rect');
    const fams = {}; svg.querySelectorAll('text').forEach(t => { if (!own(t)) return; const f = t.getAttribute('font-family'); fams[f] = (fams[f] || 0) + 1; });
    const shapes = Array.from(svg.querySelectorAll('rect, path, polygon, ellipse')).filter(own).filter(s => { const f = s.getAttribute('fill'); return f && f !== 'none' && !/^#0{6}(0{2})?$/i.test(f) && s.getBBox().width > 8 && s.getBBox().height > 8; }).map(s => ({ s, b: s.getBBox() }));
    let spill = 0, spillN = 0, spillText = '';
    svg.querySelectorAll('text').forEach(t => {
      if (!own(t)) return;
      const tb = t.getBBox(); if (!tb.width) return;
      const cx = tb.x + tb.width / 2, cy = tb.y + tb.height / 2;
      const h = shapes.filter(o => o.b.x <= cx && cx <= o.b.x + o.b.width && o.b.y <= cy && cy <= o.b.y + o.b.height).sort((a, b) => a.b.width * a.b.height - b.b.width * b.b.height)[0];
      if (!h) return;
      const over = Math.max(0, tb.x + tb.width - (h.b.x + h.b.width), h.b.x - tb.x);
      if (over > 0.5) { spillN++; if (over > spill) { spill = over; spillText = t.textContent.slice(0, 30); } }
    });
    const lls = Array.from(svg.querySelectorAll('line')).map(e => ({ e, b: e.getBBox() })).filter(o => o.b.width < 2 && o.b.height > 80);
    const joins = [];
    lls.forEach(o => { const x = o.b.x; const above = shapes.filter(s => s.b.x <= x && x <= s.b.x + s.b.width && s.b.y + s.b.height <= o.b.y + 3 && s.b.y + s.b.height > o.b.y - 30); if (above.length) { const s = above.sort((a, b) => (b.b.y + b.b.height) - (a.b.y + a.b.height))[0]; joins.push(+(o.b.y - (s.b.y + s.b.height)).toFixed(1)); } });
    const markers = svg.getAttribute('data-k-roles');
    const lifeline = lls[0] ? getComputedStyle(lls[0].e).stroke : null;
    const badges = svg.querySelectorAll('.k-badge').length;
    res.push({ kind, badges, w: Math.round(+svg.getAttribute('width') || box.width), fonts: fams, spill: spillN ? `${spillN} worst ${spill.toFixed(1)} "${spillText}"` : 0, joins: joins.slice(0, 8), markers, lifeline });
  });
  return res;
};
(async () => {
  const [page] = process.argv.slice(2);
  const b = await launchInstalled(chromium, { channel: 'chrome' });
  const ctx = await b.newContext({ viewport: { width: 1400, height: 1000 } });
  await ctx.addInitScript(() => { try { localStorage.setItem('kronikol.theme', 'light'); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} });
  const p = await ctx.newPage();
  const errors = []; p.on('pageerror', e => errors.push(e.message)); p.on('console', m => { if (m.type() === 'error') errors.push('console: ' + m.text().slice(0, 200)); });
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/'));
  await p.waitForTimeout(1500);
  await p.evaluate(() => { const feats = document.querySelectorAll('#report-content > details.feature'); [1, 3].forEach(i => { const f = feats[i]; f.open = true; const s = f.querySelector('details.scenario'); s.open = true; s.querySelectorAll('details').forEach(d => d.open = true); }); });
  for (let i = 0; i < 60; i++) { await p.waitForTimeout(300); if (await p.evaluate(() => document.querySelectorAll('#report-content .plantuml-browser svg').length >= 2)) break; }
  await p.waitForTimeout(800);
  console.log('first render', JSON.stringify(await p.evaluate(MEASURE)));
  const failedText = await p.evaluate(() => Array.from(document.querySelectorAll('#report-content .plantuml-browser')).filter(e => !e.querySelector('svg')).map(e => e.textContent.slice(0, 120)));
  if (failedText.length) console.log('NOT DRAWN', JSON.stringify(failedText));
  // a re-render through the report's own note controls
  await p.click('.toolbar-right .details-radio-btn[data-state="expanded"]');
  await p.waitForTimeout(2500);
  console.log('after Expanded', JSON.stringify(await p.evaluate(MEASURE)));
  // an internal-flow popup from the first diagram's first link
  const opened = await p.evaluate(() => { const a = document.querySelector('#report-content .plantuml-browser svg a, #report-content .plantuml-browser svg [data-iflow], #report-content .plantuml-browser svg .iflow-link'); if (!a) return false; a.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true })); return true; });
  await p.waitForTimeout(3000);
  console.log('popup opened', opened, JSON.stringify(await p.evaluate(() => { const pop = document.querySelector('.iflow-popup, .iflow-overlay'); return pop ? { shown: getComputedStyle(pop).display !== 'none', svgs: pop.querySelectorAll('svg').length, text: pop.textContent.trim().slice(0, 80) } : null; })));
  await p.keyboard.press('Escape');
  await p.waitForTimeout(500);
  const comp = await p.$('#tab-components');
  if (comp) { await comp.click(); await p.waitForTimeout(3500); console.log('components', JSON.stringify((await p.evaluate(MEASURE)).filter(r => r.kind === 'component'))); }
  console.log('errors', JSON.stringify(errors.slice(0, 6)));
  await b.close();
})();
