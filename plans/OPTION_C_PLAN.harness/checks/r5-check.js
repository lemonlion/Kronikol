// Round-5 checks: WRAP/WIDE, Happy paths wording, the Tags OR/AND switch, the theme switch and its memory, the report
// settings' memory across a reload, collapsed notes without braces, the filter line's alignment, and screenshots.
// usage: node r5-check.js <page.local.html> <out dir> [width=1400]
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require('../../../tools/playwright/installed-browser.js');
const path = require('path'), fs = require('fs');
(async () => {
  const [page, outDir, w] = process.argv.slice(2);
  const width = Number(w || 1400);
  fs.mkdirSync(outDir, { recursive: true });
  const url = 'file:///' + path.resolve(page).split(path.sep).join('/');
  const b = await launchInstalled(chromium, { channel: 'chrome' });
  const ctx = await b.newContext({ viewport: { width, height: 1000 }, colorScheme: 'light' });
  const p = await ctx.newPage();
  const errors = [];
  p.on('pageerror', e => errors.push(e.message));
  await p.goto(url); await p.waitForTimeout(1500);
  await p.evaluate(() => { const m = document.querySelector('.more-filters'); if (m && m.getAttribute('aria-expanded') !== 'true') m.click(); }); await new Promise(r => setTimeout(r, 300)); // round 9: the filter rows start folded
  const out = {};
  out.widthOptions = await p.evaluate(() => Array.from(document.querySelector('.toolbar-right .note-width-select').options).map(o => o.textContent));
  out.happy = await p.evaluate(() => Array.from(document.querySelectorAll('.happy-seg')).map(b => b.textContent));
  out.tagMode = await p.evaluate(() => { const t = document.querySelector('.tag-mode-toggle'), d = document.querySelector('.dep-mode-toggle'); const s = e => { const c = getComputedStyle(e); return [c.color, c.backgroundColor, c.borderTopColor, c.fontSize, c.paddingTop].join(' '); }; return { tags: t.textContent, deps: d.textContent, same: s(t) === s(d) }; });
  await p.click('.tag-mode-toggle'); out.tagModeAfterClick = await p.evaluate(() => document.querySelector('.tag-mode-toggle').textContent); await p.click('.tag-mode-toggle');
  // the filter line: the label, the readout and the actions share one centre line
  out.filterLine = await p.evaluate(() => {
    const mid = e => { const r = e.getBoundingClientRect(); return +(r.top + r.height / 2).toFixed(1); };
    const head = document.querySelector('.filtering-box-header');
    const textMid = e => { const rg = document.createRange(); rg.selectNodeContents(e); const r = rg.getBoundingClientRect(); return +(r.top + r.height / 2).toFixed(1); };
    return { h2: textMid(head.querySelector('h2')), readout: textMid(document.querySelector('.filter-readout')), clear: textMid(head.querySelector('.export-btn')), boxTop: +document.querySelector('.filtering-box').getBoundingClientRect().top.toFixed(1), headTop: +head.getBoundingClientRect().top.toFixed(1) };
  });
  await p.screenshot({ path: path.join(outDir, `filters-light-${width}.png`), clip: await p.evaluate(() => { const r = document.querySelector('.filtering-box').getBoundingClientRect(); return { x: r.left - 4, y: r.top + scrollY - 4, width: r.width + 8, height: Math.min(r.height + 8, 420) }; }) });
  // theme: system light, so light; the switch makes it dark and remembers it
  out.themeStart = await p.evaluate(() => document.documentElement.getAttribute('data-theme'));
  await p.click('.theme-toggle'); await p.waitForTimeout(200);
  out.themeAfterClick = await p.evaluate(() => ({ theme: document.documentElement.getAttribute('data-theme'), stored: localStorage.getItem('kronikol.theme'), pressed: document.querySelector('.theme-toggle').getAttribute('aria-pressed'), bg: getComputedStyle(document.body).backgroundColor }));
  // report settings: Collapsed, 20 lines, Headers shown, JSON, WIDE; then reload
  await p.selectOption('.toolbar-right .truncate-lines-select', '20'); await p.waitForTimeout(300);
  await p.click('.toolbar-right .toggle-btn[data-toggle="headers"]'); await p.waitForTimeout(300);
  await p.selectOption('.toolbar-right .note-format-select', 'json'); await p.waitForTimeout(300);
  await p.selectOption('.toolbar-right .note-width-select', 'full'); await p.waitForTimeout(300);
  await p.click('.toolbar-right .details-radio-btn[data-state="collapsed"]'); await p.waitForTimeout(300);
  out.stored = await p.evaluate(() => localStorage.getItem('kronikol.reportSettings'));
  await p.reload(); await p.waitForTimeout(2500);
  out.afterReload = await p.evaluate(() => {
    const bar = document.querySelector('.toolbar-right');
    return { theme: document.documentElement.getAttribute('data-theme'), details: (bar.querySelector('.details-radio-btn[data-state].details-active') || {}).getAttribute && bar.querySelector('.details-radio-btn[data-state].details-active').getAttribute('data-state'),
      lines: bar.querySelector('.truncate-lines-select').value, headers: bar.querySelector('.toggle-btn[data-toggle="headers"]').getAttribute('data-shown'),
      format: bar.querySelector('.note-format-select').value, width: bar.querySelector('.note-width-select').value, widthText: bar.querySelector('.note-width-select').selectedOptions[0].textContent };
  });
  // a scenario's notes in Collapsed + JSON: no leading "{" and, when nothing is cut, no trailing "}"
  await p.evaluate(() => { const f = document.querySelector('#report-content > details.feature'); f.open = true; const s = f.querySelector('details.scenario'); s.open = true; s.scrollIntoView(); });
  for (let i = 0; i < 30; i++) { await p.waitForTimeout(300); if (await p.evaluate(() => !!document.querySelector('#report-content details.scenario[open] .plantuml-browser svg'))) break; }
  await p.waitForTimeout(800);
  out.collapsedNotes = await p.evaluate(() => {
    const svg = document.querySelector('#report-content details.scenario[open] .plantuml-browser svg');
    if (!svg) return null;
    return Array.from(svg.querySelectorAll('text')).map(t => t.textContent).filter(t => /:/.test(t) && /"|\{|\}/.test(t)).slice(0, 10);
  });
  await p.evaluate(() => { const d = document.querySelector('#report-content details.scenario[open] .plantuml-browser'); d.scrollIntoView({ block: 'start' }); scrollBy(0, -140); });
  await p.waitForTimeout(400);
  await p.screenshot({ path: path.join(outDir, `collapsed-dark-${width}.png`) });
  // back to light, defaults, and the light scenario screenshot
  await p.evaluate(() => { localStorage.removeItem('kronikol.reportSettings'); localStorage.setItem('kronikol.theme', 'light'); });
  await p.reload(); await p.waitForTimeout(2000);
  await p.evaluate(() => { const f = document.querySelector('#report-content > details.feature'); f.open = true; const s = f.querySelector('details.scenario'); s.open = true; f.querySelectorAll('details.scenario-steps').forEach(d => d.open = true); });
  for (let i = 0; i < 30; i++) { await p.waitForTimeout(300); if (await p.evaluate(() => !!document.querySelector('#report-content details.scenario[open] .plantuml-browser svg'))) break; }
  await p.waitForTimeout(800);
  await p.evaluate(() => { const d = document.querySelector('#report-content details.scenario[open] .plantuml-browser'); d.scrollIntoView({ block: 'start' }); scrollBy(0, -140); });
  await p.waitForTimeout(400);
  await p.screenshot({ path: path.join(outDir, `diagram-light-${width}.png`) });
  await p.evaluate(() => scrollTo(0, 0));
  await p.screenshot({ path: path.join(outDir, `top-light-${width}.png`) });
  out.errors = errors;
  console.log(JSON.stringify(out, null, 1));
  await b.close();
})();
