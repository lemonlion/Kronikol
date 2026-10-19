// Item 11: the page top (bar at rest), the bar floating, and the bar floating with Filters open.
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require('../../../tools/playwright/installed-browser.js');
const path = require('path');
(async () => {
  const [page, browserName, out] = process.argv.slice(2);
  const b = await launchInstalled(pw[browserName], browserName === 'chromium' ? { channel: 'chrome' } : { executablePath: 'C:/Users/cex/AppData/Local/ms-playwright/firefox-1511/firefox/firefox.exe' });
  const p = await (await b.newContext(browserName === 'firefox' ? { viewport: null } : { viewport: { width: 1400, height: 900 } })).newPage();
  await p.addInitScript(() => { try { localStorage.setItem('kronikol.theme', 'light'); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} });
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/')); await p.waitForTimeout(2500);
  const info = () => p.evaluate(() => { const nav = document.querySelector('.toolbar-row.report-nav'), box = document.querySelector('.filtering-box'), t = document.querySelector('.nav-filters-toggle'); const r = e => e ? Math.round(e.getBoundingClientRect().top) + '..' + Math.round(e.getBoundingClientRect().bottom) : '-'; return 'scrollY ' + Math.round(scrollY) + ' nav ' + r(nav) + ' box ' + r(box) + ' in nav ' + (nav && box ? nav.contains(box) : '-') + ' toggle visible ' + (t ? t.getBoundingClientRect().width > 0 : '-'); });
  console.log('rest   ', await info()); await p.screenshot({ path: out + '-rest.png', clip: { x: 0, y: 0, width: 1400, height: 520 } });
  await p.evaluate(() => window.scrollTo(0, 2500)); await p.waitForTimeout(600);
  console.log('float  ', await info()); await p.screenshot({ path: out + '-float.png', clip: { x: 0, y: 0, width: 1400, height: 300 } });
  const tg = await p.evaluate(() => { const g = document.querySelector('.nav-filters'); const bs = g ? Array.from(g.querySelectorAll('button')) : []; const t = bs.find(x => /filters/i.test(x.textContent)); if (t) { t.setAttribute('data-probe-tg', '1'); return t.textContent.trim(); } return 'none: ' + bs.map(x => x.textContent.trim()).join('|'); });
  console.log('toggle', tg);
  await p.click('[data-probe-tg]'); await p.waitForTimeout(800);
  console.log('open   ', await info()); await p.screenshot({ path: out + '-float-open.png', clip: { x: 0, y: 0, width: 1400, height: 520 } });
  await p.evaluate(() => window.scrollTo(0, 0)); await p.waitForTimeout(700);
  console.log('top    ', await info()); await p.screenshot({ path: out + '-back.png', clip: { x: 0, y: 0, width: 1400, height: 520 } });
  await p.evaluate(() => window.scrollTo(0, 1500)); await p.waitForTimeout(700);
  console.log('again  ', await info());
  await b.close();
})();
