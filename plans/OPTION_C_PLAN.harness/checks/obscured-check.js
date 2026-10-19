// Round 13: WCAG 2.2 2.4.11 (Focus Not Obscured) inside an opened scenario, where buttons, links, toggles and step
// headings take focus as well as the scenario headings. Opens the failed scenario and a passed one with every
// section, starts at a control in the middle of the window with the bar pinned, then Tab x50 and Shift+Tab x80,
// counting where each focused control lands: clear of the bar, partly under it, or wholly under it.
// usage: node obscured-check.js <page.local.html> [chrome|firefox]
const PW = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require('../../../tools/playwright/installed-browser.js');
const path = require('path');
(async () => {
  const [page, which = 'chrome'] = process.argv.slice(2);
  const url = 'file:///' + path.resolve(page).split(path.sep).join('/');
  let p, close;
  if (which === 'firefox') {
    const fb = await require('../mw/browsers').launch('firefox');
    p = (await fb.newPage()).raw; await p.setViewport({ width: 1400, height: 900 }); close = () => fb.close();
  } else {
    const b = await launchInstalled(PW.chromium, { channel: 'chrome' });
    p = await (await b.newContext({ viewport: { width: 1400, height: 900 } })).newPage(); close = () => b.close();
  }
  const wait = ms => new Promise(r => setTimeout(r, ms));
  await p.goto(url); await wait(2200);
  await p.evaluate(() => {
    [document.querySelector('details.scenario[data-status="Failed"]'), document.querySelector('details.scenario[data-status="Passed"]')].forEach(s => { s.closest('details.feature').open = true; const r = s.closest('details.rule'); if (r) r.open = true; s.open = true; s.querySelectorAll('details').forEach(d => d.open = true); });
    document.querySelector('details.scenario[data-status="Failed"]').scrollIntoView({ block: 'start' });
    window.scrollBy(0, 300);
  });
  await wait(1500);
  // the same start on every page: the failed scenario's Steps heading, brought into view by focusing it
  const start = await p.evaluate(() => {
    const e = document.querySelector('details.scenario[data-status="Failed"] details.scenario-steps > summary');
    if (!e) return null;
    e.focus();
    return document.activeElement === e ? 'SUMMARY.h4 "Steps" of the failed scenario at y ' + Math.round(e.getBoundingClientRect().top) : null;
  });
  const check = () => p.evaluate(() => {
    const nav = document.querySelector('.toolbar-row.report-nav'), a = document.activeElement;
    if (!a || a === document.body) return 'none';
    if (nav.contains(a)) return 'bar';
    const nr = nav.getBoundingClientRect(), r = a.getBoundingClientRect();
    if (r.width === 0 && r.height === 0) return 'unseen';
    if (r.bottom <= 0 || r.top >= innerHeight) return 'offscreen';
    if (r.top >= nr.bottom - 0.5 || r.bottom <= nr.top + 0.5) return 'clear';
    return r.top >= nr.top - 0.5 && r.bottom <= nr.bottom + 0.5 ? 'hidden' : 'partly';
  });
  const tally = { start, forward: {}, back: {}, examples: [] };
  // Puppeteer has no chord shorthand: Shift held round the Tab
  const press = async key => { if (key === 'Shift+Tab') { await p.keyboard.down('Shift'); await p.keyboard.press('Tab'); await p.keyboard.up('Shift'); } else await p.keyboard.press(key); };
  const step = async (key, dir) => {
    await press(key); await wait(+(process.env.STEP_MS || 70));
    const s = await check(); tally[dir][s] = (tally[dir][s] || 0) + 1;
    if ((s === 'hidden' || s === 'partly' || s === 'offscreen') && tally.examples.length < 6) tally.examples.push(dir + ' ' + s + ' ' + await p.evaluate(() => { const a = document.activeElement; const r = a.getBoundingClientRect(); return a.tagName + '.' + String(a.className.baseVal !== undefined ? a.className.baseVal : a.className).split(' ')[0] + ' "' + (a.textContent || '').trim().slice(0, 24) + '" at ' + Math.round(r.left) + ',' + Math.round(r.top) + ' in ' + (a.closest('svg') ? 'svg' : a.parentElement.className); }));
  };
  for (let i = 0; i < 50; i++) await step('Tab', 'forward');
  for (let i = 0; i < 80; i++) await step('Shift+Tab', 'back');
  console.log(which + ' ' + path.basename(page) + ' ' + JSON.stringify(tally));
  await close();
})();
