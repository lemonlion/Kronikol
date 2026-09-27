// Third P3 audit: what window._countArrows counts as a message, line by line (the pattern in effect since 3.0.48).
// Usage: node split-arrow-count.js
const path = require('path'), url = require('url');
const pw = require(path.join(path.resolve(__dirname, '..', '..'), 'tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
(async () => {
    const browser = await pw.chromium.launch({ headless: true });
    const page = await (await browser.newContext()).newPage();
    await page.goto(url.pathToFileURL(path.resolve(__dirname, 'split-page.html')).href);
    await page.waitForFunction(() => typeof window._countArrows === 'function');
    const cases = ['x => y', 'a <- b', 'a ..> b', 'a .> b', 'Caller -[#438DD5]> OrdersAPI: GET: /x', 'OrdersAPI -[#438DD5]-> Caller: 200', 'plain text', '"url": "http://x/a-b"', '{"a": 1}', '<!-- c -->'];
    for (const c of cases) console.log(JSON.stringify(c).padEnd(44) + ' counted as ' + await page.evaluate(l => window._countArrows([l]), c) + ' arrow(s)');
    await browser.close();
})();
