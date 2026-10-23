const { chromium, firefox, webkit } = require('playwright-core');
const { pathToFileURL } = require('url');
(async () => {
  for (const b of ['chromium', 'firefox', 'webkit']) {
    const browser = await ({ chromium, firefox, webkit })[b].launch({ headless: true });
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(String(e.message).slice(0, 100)));
    await page.goto(pathToFileURL(process.argv[2]).href, { waitUntil: 'commit' });
    await page.waitForFunction(() => document.title === 'rebuilt' && document.readyState === 'complete', null, { polling: 100, timeout: 20000 });
    await page.waitForTimeout(300);
    const r = await page.evaluate(() => ({
      prop: window.__prop, report: window.__report, letRan: !!window.__letRan, varRan: !!window.__varRan,
      constRan: !!window.__constRan, errs: window.__errs, msgSeenByLoaderListener: window.__msgSeen || 0,
      krnTopNow: (function () { try { return krnTop; } catch (e) { return 'ERR ' + e.name; } })(),
      krnVarNow: (function () { try { return krnVar; } catch (e) { return 'ERR ' + e.name; } })(),
      url: location.href.split('/').pop()
    }));
    console.log(b, JSON.stringify(r), 'pageerrors=' + JSON.stringify(errors));
    await browser.close();
  }
})();
