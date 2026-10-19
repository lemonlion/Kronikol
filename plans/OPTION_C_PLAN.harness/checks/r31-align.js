// Item 31: vertical alignment of the report bar's controls (label, radios, dropdowns, toggles, chips): each control's
// box middle and its text's ink middle, measured from a screenshot. usage: node r31-align.js <page> <chromium|firefox> <out prefix>
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require('../../../tools/playwright/installed-browser.js');
const path = require('path');
(async () => {
  const [page, browserName, out] = process.argv.slice(2);
  const b = await launchInstalled(pw[browserName], browserName === 'chromium' ? { channel: 'chrome' } : { executablePath: 'C:/Users/cex/AppData/Local/ms-playwright/firefox-1511/firefox/firefox.exe' });
  const ctx = await b.newContext(browserName === 'firefox' ? { viewport: null } : { viewport: { width: 1400, height: 900 }, deviceScaleFactor: 1 });
  await ctx.addInitScript(() => { try { localStorage.setItem('kronikol.theme', 'light'); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} });
  const p = await ctx.newPage();
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/')); await p.waitForTimeout(2500);
  const bar = p.locator('.toolbar-row .toolbar-right').first();
  const items = await p.evaluate(() => {
    const bar = document.querySelector('.toolbar-row .toolbar-right');
    const br = bar.getBoundingClientRect();
    const els = Array.from(bar.querySelectorAll('.diff-label, .details-radio-label, label, button, select, .truncate-lines-label')).filter(e => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0 && !e.closest('button') || e.tagName === 'BUTTON'; });
    return els.filter(e => e.getBoundingClientRect().width > 0).map(e => { const r = e.getBoundingClientRect(); return { tag: e.tagName, cls: (e.className || '').toString().split(' ')[0], text: (e.textContent || '').trim().slice(0, 14), x0: Math.round(r.left - br.left), x1: Math.round(r.right - br.left), top: +(r.top - br.top).toFixed(2), mid: +((r.top + r.bottom) / 2 - br.top).toFixed(2), h: +r.height.toFixed(2), fs: getComputedStyle(e).fontSize, lh: getComputedStyle(e).lineHeight, va: getComputedStyle(e).verticalAlign }; });
  });
  await bar.screenshot({ path: out + '-' + browserName + '.png' });
  require('fs').writeFileSync(out + '-' + browserName + '.json', JSON.stringify(items));
  items.forEach(i => console.log(browserName, i.tag, i.cls, JSON.stringify(i.text), 'x', i.x0 + '-' + i.x1, 'top', i.top, 'mid', i.mid, 'h', i.h, i.fs, i.lh, i.va));
  await b.close();
})();
