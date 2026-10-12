// node yamlprobe.js <page path with forward slashes>: prints each drawn note line that holds the payload, its length
// and how many lone surrogate halves or U+FFFD it holds. Run on NoteFormatDefaultTests' emoji page built with the
// YAML view's guard removed, it showed the split halves the fact's first form could not see (plan §11).
const path = require('path');
// The Playwright driver the E2E project restores (run with the node beside it, in .playwright/node).
const pw = require(process.env.BENCH_PW || path.resolve(__dirname, '../../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
(async () => {
  const browser = await pw.chromium.launch();
  const page = await browser.newPage();
  await page.goto('file:///' + process.argv[2]);
  await page.evaluate(() => document.querySelectorAll('details').forEach(d => { d.open = true; }));
  await page.waitForFunction(() => {
    const s = document.querySelector('.plantuml-browser svg');
    return s && /even|odd/.test(s.textContent);
  }, null, { polling: 200, timeout: 60000 });
  const out = await page.evaluate(() => [...document.querySelectorAll('.plantuml-browser svg text')]
    .map(t => t.textContent)
    .filter(s => /even|odd|[\uD800-\uDFFF\uFFFD]/.test(s))
    .map(s => ({
      head: s.slice(0, 10),
      len: s.length,
      lone: (s.match(/[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]|\uFFFD/g) || []).length
    })));
  console.log(JSON.stringify(out));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
