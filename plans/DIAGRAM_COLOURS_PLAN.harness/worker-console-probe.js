'use strict';
const pw = require('C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package');
const fs = require('fs'), path = require('path'), url = require('url');
const html = `<!doctype html><html><body><script>
var src = "console.warn('WARN-FROM-WORKER'); console.error('ERROR-FROM-WORKER'); console.log('LOG-FROM-WORKER'); self.postMessage('done');";
var w = new Worker(URL.createObjectURL(new Blob([src], {type:'application/javascript'})));
w.onmessage = function(){ window.__done = 1; };
</script></body></html>`;
const p = path.join(process.env.TEMP, 'worker-console-probe.html'); fs.writeFileSync(p, html);
(async () => {
  const browser = await pw.chromium.launch({ headless: true });
  const page = await browser.newPage();
  const seen = [];
  page.on('console', m => seen.push('page.console[' + m.type() + '] ' + m.text()));
  page.context().on('console', m => seen.push('context.console[' + m.type() + '] ' + m.text()));
  page.on('worker', w => { seen.push('page.worker started'); });
  await page.goto(url.pathToFileURL(p).href);
  await page.waitForFunction('window.__done === 1', null, { timeout: 20000, polling: 200 });
  await new Promise(r => setTimeout(r, 500));
  console.log('playwright version ' + require('C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package/package.json').version);
  seen.forEach(l => console.log(l));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
