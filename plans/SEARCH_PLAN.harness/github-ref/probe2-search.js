// Probe 2: the logged-out search results page for repositories. One page load.
const { launch, newContext, save, installMeasure, sleep, OUT } = require('./lib');
const path = require('path');

(async () => {
  const browser = await launch();
  const ctx = await newContext(browser, { width: 1280, height: 900 });
  const page = await ctx.newPage();
  const resp = await page.goto('https://github.com/search?q=playwright&type=repositories', { waitUntil: 'domcontentloaded', timeout: 60000 });
  console.log('status', resp.status());
  save('raw-search-repos.html', await resp.text());
  await page.waitForLoadState('load').catch(() => {});
  await sleep(3000);
  await installMeasure(page);
  save('rendered-search-repos-header.html', await page.evaluate(() => (document.querySelector('header') || {}).outerHTML || ''));
  save('rendered-search-repos-qbsearch.html', await page.evaluate(() => (document.querySelector('qbsearch-input') || {}).outerHTML || ''));
  save('rendered-search-repos-main.html', await page.evaluate(() => (document.querySelector('main') || document.querySelector('[data-target="react-app.reactRoot"]') || {}).outerHTML || ''));
  await page.screenshot({ path: path.join(OUT, 'results-repos-1280-light-viewport.png') });
  await page.screenshot({ path: path.join(OUT, 'results-repos-1280-light-full.png'), fullPage: true });
  const info = await page.evaluate(() => ({
    title: document.title,
    htmlAttrs: Array.from(document.documentElement.attributes).map(a => a.name + '=' + a.value),
    qbCount: document.querySelectorAll('qbsearch-input').length,
    headers: Array.from(document.querySelectorAll('header')).map(h => h.className),
    reactApps: Array.from(document.querySelectorAll('react-app')).map(r => r.getAttribute('app-name')),
    bodyW: document.body.scrollWidth,
  }));
  console.log(JSON.stringify(info, null, 2));
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
