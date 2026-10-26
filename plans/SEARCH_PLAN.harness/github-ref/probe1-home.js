// Probe 1: the logged-out github.com home page. One page load.
const { launch, newContext, save, installMeasure, sleep, OUT } = require('./lib');
const path = require('path');

(async () => {
  const browser = await launch();
  const ctx = await newContext(browser, { width: 1280, height: 900 });
  const page = await ctx.newPage();
  const resp = await page.goto('https://github.com/', { waitUntil: 'domcontentloaded', timeout: 60000 });
  const raw = await resp.text();
  save('raw-home.html', raw);
  await page.waitForLoadState('load').catch(() => {});
  await sleep(2500);
  await installMeasure(page);
  const header = await page.evaluate(() => {
    const h = document.querySelector('header');
    return h ? h.outerHTML : null;
  });
  save('rendered-home-header.html', header || '');
  const qb = await page.evaluate(() => {
    const q = document.querySelector('qbsearch-input');
    return q ? q.outerHTML : null;
  });
  save('rendered-home-qbsearch.html', qb || '');
  await page.screenshot({ path: path.join(OUT, 'home-loggedout-1280-light-viewport.png') });
  const info = await page.evaluate(() => ({
    title: document.title,
    htmlAttrs: Array.from(document.documentElement.attributes).map(a => a.name + '=' + a.value),
    qbCount: document.querySelectorAll('qbsearch-input').length,
    headerCount: document.querySelectorAll('header').length,
  }));
  console.log(JSON.stringify(info, null, 2));
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
