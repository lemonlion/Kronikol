// Functional checks on the preview: the Happy paths control (All, Only, Exclude) <-> Happy Path tag chip, sort, tabs, jump to failure.
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const path = require('path');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1400, height: 1000 } });
  const errors = [];
  p.on('pageerror', e => errors.push(e.message));
  await p.goto('file:///' + path.resolve(process.argv[2]).split(path.sep).join('/'));
  await p.waitForTimeout(2000);
  await p.evaluate(() => { const m = document.querySelector('.more-filters'); if (m && m.getAttribute('aria-expanded') !== 'true') m.click(); }); await new Promise(r => setTimeout(r, 300)); // round 9: the filter rows start folded
  const visible = () => p.evaluate(() => Array.from(document.querySelectorAll('details.scenario')).filter(s => s.style.display !== 'none' && s.closest('details.feature').style.display !== 'none').length);
  const state = () => p.evaluate(() => ({
    happy: (document.querySelector('.happy-seg[aria-pressed="true"]') || {}).textContent,
    chip: document.querySelector('.tag-toggle[data-tag="Happy Path"]').getAttribute('data-tag-state'),
  }));
  const out = {};
  out.start = { visible: await visible(), ...(await state()) };
  await p.click('.happy-seg[data-hp="in"]');
  out.afterToggleOn = { visible: await visible(), ...(await state()) };
  await p.click('.tag-toggle[data-tag="Happy Path"]');
  out.afterChipToOut = { visible: await visible(), ...(await state()) };
  await p.click('.tag-toggle[data-tag="Happy Path"]');
  out.afterChipToOff = { visible: await visible(), ...(await state()) };
  await p.click('.tag-toggle[data-tag="Happy Path"]');
  out.afterChipToIn = { visible: await visible(), ...(await state()) };
  await p.click('.export-btn');   // Clear All
  out.afterClearAll = { visible: await visible(), ...(await state()) };
  await p.selectOption('#features-sort', 'failed');
  out.sortFailedFirst = await p.evaluate(() => Array.from(document.querySelectorAll('#report-content > details.feature')).slice(0, 3).map(f => f._kName));
  await p.selectOption('#features-sort', 'duration');
  out.sortSlowest = await p.evaluate(() => Array.from(document.querySelectorAll('#report-content > details.feature')).slice(0, 2).map(f => f._kName));
  await p.click('#tab-history');
  out.historyShown = await p.evaluate(() => !document.getElementById('panel-history').hidden && document.getElementById('panel-features').hidden);
  await p.click('.jump-to-failure');
  await p.waitForTimeout(600);
  out.afterJump = await p.evaluate(() => ({ featuresShown: !document.getElementById('panel-features').hidden, counter: document.getElementById('failure-counter').textContent }));
  out.errors = errors;
  console.log(JSON.stringify(out, null, 1));
  await b.close();
})();
