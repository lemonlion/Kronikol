// Does headless Chromium on Linux draw a classic scrollbar once --hide-scrollbars is dropped?
// Run inside mcr.microsoft.com/playwright:v1.59.1-noble with the E2E project's driver package mounted at /pw.
const pw = require('/pw');
(async () => {
  for (const [name, opts] of [['default launch', {}], ['without --hide-scrollbars', { ignoreDefaultArgs: ['--hide-scrollbars'] }]]) {
    const browser = await pw.chromium.launch(opts);
    const page = await browser.newPage({ viewport: { width: 1000, height: 600 } });
    await page.setContent('<!doctype html><html><body style="margin:0"><div style="height:5000px">tall</div></body></html>');
    const r = await page.evaluate(() => ({ inner: innerWidth, client: document.documentElement.clientWidth }));
    console.log(`${name}: chromium ${browser.version()}, innerWidth ${r.inner}, clientWidth ${r.client}, scrollbar ${r.inner - r.client} px`);
    await browser.close();
  }
})().catch(e => { console.error(e); process.exit(1); });
