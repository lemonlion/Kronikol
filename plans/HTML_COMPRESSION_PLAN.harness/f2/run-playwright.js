// node run-playwright.js [engines csv] : the probe page in Playwright's own engines, over file:// and over http://127.0.0.1.
const path = require('path'), http = require('http'), fs = require('fs');
const pw = require('playwright-core');
const engines = (process.argv[2] || 'webkit').split(',');
const file = path.join(__dirname, 'probe.html');
(async () => {
  const server = http.createServer((q, s) => { s.writeHead(200, { 'content-type': 'text/html; charset=utf-8' }); s.end(fs.readFileSync(file)); });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const urls = [require('url').pathToFileURL(file).href, `http://127.0.0.1:${server.address().port}/probe.html`];
  for (const e of engines) {
    const browser = await pw[e].launch({ headless: true });
    for (const u of urls) {
      const page = await browser.newPage();
      await page.goto(u);
      await page.waitForFunction(() => document.title === 'done', null, { timeout: 20000, polling: 200 });
      console.log(e, u.startsWith('file') ? 'file://' : 'http://', await page.textContent('#out'));
      await page.close();
    }
    await browser.close();
  }
  server.close();
})().catch(e => { console.error(e); process.exit(1); });
