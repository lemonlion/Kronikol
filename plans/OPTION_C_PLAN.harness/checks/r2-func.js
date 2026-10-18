// Item 2: open two scenarios by hand, then collapse all scenarios with one click; expand all features; states.
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const path = require('path');
(async () => {
  const [page, browserName] = process.argv.slice(2);
  const b = await pw[browserName].launch(browserName === 'chromium' ? { channel: 'chrome' } : { executablePath: 'C:/Users/cex/AppData/Local/ms-playwright/firefox-1511/firefox/firefox.exe' });
  const p = await (await b.newContext(browserName === 'firefox' ? { viewport: null } : { viewport: { width: 1400, height: 900 } })).newPage();
  const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/')); await p.waitForTimeout(2500);
  const st = () => p.evaluate(() => {
    const q = s => document.querySelector(s).getAttribute('aria-disabled');
    const n = s => Array.from(document.querySelectorAll(s)).filter(d => d.open).length;
    return 'features open ' + n('details.feature') + '/' + document.querySelectorAll('details.feature').length + ', scenarios open ' + n('details.scenario') + ' | feature expand ' + q('.k-fold-group:nth-of-type(1) .k-fold-expand') + ' collapse ' + q('.k-fold-group:nth-of-type(1) .k-fold-collapse') + ' | scenario expand ' + Array.from(document.querySelectorAll('.k-fold-expand'))[1].getAttribute('aria-disabled') + ' collapse ' + Array.from(document.querySelectorAll('.k-fold-collapse'))[1].getAttribute('aria-disabled');
  });
  await p.waitForTimeout(300); console.log('start   ', await st());
  await p.evaluate(() => { const s = document.querySelectorAll('details.scenario'); [s[0], s[5]].forEach(x => { x.closest('details.feature').open = true; x.open = true; }); });
  await p.waitForTimeout(300); console.log('by hand ', await st());
  await p.locator('.k-fold-collapse').nth(1).click(); await p.waitForTimeout(300); console.log('collapse scenarios', await st());
  await p.locator('.k-fold-collapse').nth(0).click(); await p.waitForTimeout(300); console.log('collapse features ', await st());
  await p.locator('.k-fold-expand').nth(0).click(); await p.waitForTimeout(300); console.log('expand features   ', await st());
  console.log('errors', errs.join(' | ') || 'none');
  await b.close();
})();
