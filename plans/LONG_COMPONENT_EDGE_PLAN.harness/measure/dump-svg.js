// node dump-svg.js <page.html> [browser] [jsflags]: the texts of #comp-diagram's SVG, for reading an error picture.
const path = require('path'), url = require('url');
const pw = require(process.env.BENCH_PW || 'C:/Code/Kronikol/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package');
(async () => {
  const [file, browser = 'chromium', jsflags = ''] = process.argv.slice(2);
  const b = await pw[browser === 'chrome' ? 'chromium' : browser].launch(Object.assign({ headless: true }, browser === 'chrome' ? { channel: 'chrome' } : {}, jsflags ? { args: ['--js-flags=' + jsflags] } : {}));
  const page = await b.newPage();
  await page.goto(url.pathToFileURL(path.resolve(file)).href);
  await page.waitForFunction(() => { const el = document.getElementById('comp-diagram'); return el && (el.querySelector('svg') || el.textContent.trim()); }, null, { polling: 200, timeout: 180000 });
  const r = await page.evaluate(() => { const el = document.getElementById('comp-diagram'); const svg = el.querySelector('svg');
    return svg ? Array.from(svg.querySelectorAll('text')).map(t => t.textContent) : ['TEXT: ' + el.textContent]; });
  console.log(r.slice(0, 40).join('\n'));
  console.log('... total texts', r.length);
  await b.close();
})();
