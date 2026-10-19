// Item 25: a toggle's width through its loading state (report-level flag and radio), sampled every 25 ms.
// usage: node r25-pending.js <page> <chromium|firefox> <label regex, e.g. ^Headers$>
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require('../../../tools/playwright/installed-browser.js');
const path = require('path');
(async () => {
  const [page, browserName, labelRx] = process.argv.slice(2);
  const b = await launchInstalled(pw[browserName], browserName === 'chromium' ? { channel: 'chrome' } : { executablePath: 'C:/Users/cex/AppData/Local/ms-playwright/firefox-1511/firefox/firefox.exe' });
  const ctx = await b.newContext(browserName === 'firefox' ? { viewport: null } : { viewport: { width: 1400, height: 900 } });
  await ctx.addInitScript(() => { try { localStorage.setItem('kronikol.theme', 'light'); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} });
  const p = await ctx.newPage();
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/')); await p.waitForTimeout(2500);
  // open a few scenarios so a toggle has diagrams to redraw
  await p.evaluate(() => { const ss = Array.from(document.querySelectorAll('details.scenario')).slice(0, 6); ss.forEach(s => { s.closest('details.feature').open = true; s.open = true; }); });
  await p.waitForTimeout(3000);
  const box = await p.evaluate(rx => {
    const re = new RegExp(rx);
    const b = Array.from(document.querySelectorAll('.report-nav .details-radio-btn, .report-nav button')).find(x => re.test(x.textContent.trim()) && x.getBoundingClientRect().width > 0);
    b.setAttribute('data-probe-btn', '1');
    const r = b.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2, cls: b.className };
  }, labelRx);
  await p.evaluate(() => { window.__w = []; const b = document.querySelector('[data-probe-btn]'); const t0 = performance.now(); (function tick() { const r = b.getBoundingClientRect(); window.__w.push([Math.round(performance.now() - t0), +r.width.toFixed(2), b.classList.contains('details-pending') ? 'P' : '-', getComputedStyle(b).paddingRight]); if (performance.now() - t0 < 4000) setTimeout(tick, 25); })(); });
  await p.mouse.click(box.x, box.y);
  await p.waitForTimeout(4300);
  const w = await p.evaluate(() => window.__w);
  const widths = [...new Set(w.map(x => x[1]))];
  console.log(browserName, labelRx, 'class', box.cls, '| distinct widths', JSON.stringify(widths), '| pending samples', w.filter(x => x[2] === 'P').length);
  const changes = []; for (let i = 1; i < w.length; i++) if (w[i][1] !== w[i - 1][1] || w[i][2] !== w[i - 1][2]) changes.push(w[i].join(' '));
  console.log('  changes:', changes.slice(0, 12).join(' | '));
  await b.close();
})();
