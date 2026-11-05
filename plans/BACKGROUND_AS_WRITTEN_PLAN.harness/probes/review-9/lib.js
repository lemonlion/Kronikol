const path = require('path');
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright');
const { launchInstalled } = require('<tree>/tools/playwright/installed-browser.js');
const R = '<review-9>';
let browser;
async function start() { browser = await launchInstalled(pw.chromium, { headless: true }); return browser; }
async function stop() { if (browser) await browser.close(); }
function url(build, report, file = 'TestRunReport.html') { return 'file:///' + path.join(R, 'out', build, report, file).replace(/\\/g, '/'); }
async function open(build, report, ctxOptions = {}) {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 }, ...ctxOptions });
  const page = await ctx.newPage();
  page.on('pageerror', e => console.log('  PAGEERROR', build, e.message));
  await page.goto(url(build, report));
  await page.locator('details.feature').first().waitFor();
  return page;
}
async function openFilters(page) {
  if (await page.locator('#filtering-box').isVisible()) return;
  await page.locator('.nav-filters-toggle').click();
  await page.waitForFunction(() => !document.getElementById('filtering-box').hidden, null, { polling: 100 });
}
// Types as a reader does: real keys, then the debounce or Enter
async function type(page, text, enter = true) {
  await openFilters(page);
  const f = page.locator('#filtering-box #searchbar');
  await f.click();
  await page.keyboard.press('Control+A');
  await page.keyboard.press('Delete');
  await f.pressSequentially(text, { delay: 10 });
  if (enter) await f.press('Enter'); else await page.waitForTimeout(400);
  await page.waitForTimeout(150);
}
// Replaces the query without passing through an empty field (select all, then the first key replaces it)
async function typeOver(page, text) {
  await openFilters(page);
  const f = page.locator('#filtering-box #searchbar');
  await f.click();
  await page.keyboard.press('Control+A');
  await f.pressSequentially(text, { delay: 10 });
  await f.press('Enter');
  await page.waitForTimeout(150);
}
async function append(page, text) {
  await openFilters(page);
  const f = page.locator('#filtering-box #searchbar');
  await f.click();
  await page.keyboard.press('End');
  await f.pressSequentially(text, { delay: 10 });
  await f.press('Enter');
  await page.waitForTimeout(150);
}
async function empty(page) {
  await openFilters(page);
  const f = page.locator('#filtering-box #searchbar');
  await f.click();
  await page.keyboard.press('Control+A');
  await page.keyboard.press('Delete');
  await page.waitForTimeout(100);
}
// Opens the feature and the group by hand, as a reader does
async function openOutline(page, name = 'Pay') {
  await page.evaluate(() => { const f = document.querySelector('details.feature'); if (!f.open) f.querySelector(':scope > summary').click(); });
  await page.waitForTimeout(50);
  const g = page.locator('details.scenario-parameterized', { hasText: name }).first();
  if (await g.getAttribute('open') === null) await g.locator('> summary').first().click();
  await page.waitForTimeout(50);
  return g;
}
// clicks a row in the visible table of outline `name`
async function row(page, idx, name = 'Pay') {
  const g = page.locator('details.scenario-parameterized', { hasText: name }).first();
  const rows = g.locator('table.param-test-table:visible tbody tr[data-row-idx="' + idx + '"]');
  await rows.first().locator('td').first().click();
  await page.waitForTimeout(50);
}
async function st(page, name = 'Pay') {
  return page.evaluate((name) => {
    const g = [...document.querySelectorAll('details.scenario-parameterized')].find(x => x.querySelector('summary').textContent.includes(name));
    const a = g.querySelector('table.param-test-table:not([style*="display"]) tbody tr.row-active') || g.querySelector('tbody tr.row-active');
    const panels = [...g.querySelectorAll('.param-detail-panel')];
    const out = { open: g.open, row: a && a.getAttribute('data-row-idx') };
    out.panels = panels.map(p => {
      const bg = p.querySelector('details.scenario-background'), s = p.querySelector('details.scenario-steps'), f = p.querySelector('details.failure-result');
      const comps = [...p.querySelectorAll('details.step-collapsible')].map(d => d.open ? 'O' : 'c').join('');
      return (p.style.display === 'none' ? ' ' : '*') + 'b' + (bg ? (bg.open ? 'O' : 'c') : '-') + ' s' + (s ? (s.open ? 'O' : 'c') : '-') + ' f' + (f ? (f.open ? 'O' : 'c') : '-') + ' [' + comps + ']';
    });
    return out;
  }, name);
}
function fmt(s) { return `row=${s.row} open=${s.open} | ` + s.panels.join(' | '); }
// click the summary of a section in the panel shown
async function toggleSection(page, sel, name = 'Pay') {
  const g = page.locator('details.scenario-parameterized', { hasText: name }).first();
  await g.locator('.param-detail-panel:not([style*="display"]) ' + sel + ' > summary').first().click();
  await page.waitForTimeout(40);
}
async function seq(title, report, fn, builds = ['old', 'new'], ctxOptions = {}) {
  console.log('\n== ' + title);
  for (const b of builds) {
    const page = await open(b, report, ctxOptions);
    const log = [];
    const snap = async (label, name) => log.push('  ' + b + ' ' + label.padEnd(30) + fmt(await st(page, name)));
    const say = (t) => log.push('  ' + b + ' ' + t);
    await fn(page, snap, say, b);
    console.log(log.join('\n'));
    await page.context().close();
  }
}
module.exports = { start, stop, open, url, type, typeOver, append, empty, openOutline, row, st, fmt, toggleSection, openFilters, seq, R, pw };
